using Microsoft.Data.Sqlite;
using VoteCheck.Core;
using VoteCheck.Core.Models;
using VoteCheckWeb.Data;

namespace VoteCheckWeb.Sync;

// Fills the local SQLite mirror from api.eduskunta.fi, then tails it.
//
// The archive is walked oldest-first through IEduskuntaClient.GetVotePageAsync, storing the
// index reached in sync_state. Because that enumeration sorts ascending, an index keeps
// pointing at the same division between cycles: new votes append past the cursor, so
// resuming is just "carry on from where we stopped" with no risk of skipping or repeating.
//
// Everything a division needs is in one payload — tallies, party breakdowns and all ~199
// ballots — so there is no per-vote follow-up call. One page is one transaction.
public sealed class VoteSyncService : BackgroundService {

    private readonly Db _db;
    private readonly IEduskuntaClient _api;
    private readonly ILogger<VoteSyncService> _log;
    private readonly int _minYear;
    private readonly int _pageSize;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _requestDelay;

    public VoteSyncService( Db db, IEduskuntaClient api, IConfiguration config, ILogger<VoteSyncService> log ) {
        _db = db;
        _api = api;
        _log = log;
        // Backfill floor, as a parliamentary year (istuntovpvuosi) — which is not the calendar
        // year: filtering 2023+ by year yields ~2,771 divisions, by sitting date only ~1,875.
        _minYear = config.GetValue( "VoteCheck:SyncMinYear", 2023 );
        // Each division carries its full ballot list (~76 KB), so a page of 50 is already ~4 MB.
        _pageSize = Math.Clamp( config.GetValue( "VoteCheck:SyncPageSize", 50 ), 1, 200 );
        _pollInterval = TimeSpan.FromMinutes( config.GetValue( "VoteCheck:SyncPollMinutes", 15 ) );
        _requestDelay = TimeSpan.FromMilliseconds( config.GetValue( "VoteCheck:SyncRequestDelayMs", 500 ) );
    }

    protected override async Task ExecuteAsync( CancellationToken ct ) {
        while ( !ct.IsCancellationRequested ) {
            try {
                await SyncAsync( ct );
            } catch ( Exception ex ) when ( ex is not OperationCanceledException ) {
                _log.LogError( ex, "Sync cycle failed; retrying after poll interval" );
            }
            try {
                await Task.Delay( _pollInterval, ct );
            } catch ( OperationCanceledException ) {
                return;
            }
        }
    }

    private async Task SyncAsync( CancellationToken ct ) {
        using var conn = _db.Open();

        var cursor = int.TryParse( GetState( conn, "vote_cursor" ), out var stored ) ? stored : 0;
        var imported = 0;
        var total = 0;

        while ( !ct.IsCancellationRequested ) {
            if ( cursor + _pageSize > EduskuntaClient.MaxSearchWindow ) {
                // Upstream refuses to page deeper than this. Only reachable with a low
                // SyncMinYear; the full archive needs the async dataset export instead.
                _log.LogError(
                    "Backfill window exhausted at {Cursor}: upstream caps paging at {Cap}. " +
                    "Raise VoteCheck:SyncMinYear (currently {MinYear}) or switch to a dataset export.",
                    cursor, EduskuntaClient.MaxSearchWindow, _minYear );
                return;
            }

            var page = await _api.GetVotePageAsync( _minYear, cursor, _pageSize, ct );
            total = page.TotalCount;

            if ( page.Votes.Count == 0 )
                break;

            imported += ImportPage( conn, page );
            cursor += page.Votes.Count;
            SetState( conn, "vote_cursor", cursor.ToString() );

            if ( !page.HasMore )
                break;

            // Be a polite client: upstream caps search at 450 requests per 3000s per IP.
            await Task.Delay( _requestDelay, ct );
        }

        if ( imported > 0 )
            _log.LogInformation(
                "Imported {Count} divisions; cursor at {Cursor} of {Total}", imported, cursor, total );

        if ( total > 0 && cursor >= total && GetState( conn, "backfill_complete" ) == null ) {
            SetState( conn, "backfill_complete", DateTimeOffset.UtcNow.ToString( "o" ) );
            _log.LogInformation( "Backfill complete: {Total} divisions from {MinYear} onward", total, _minYear );
        }
    }

    // One page, one transaction: a crash mid-page leaves the cursor where it was, and the
    // page is re-imported harmlessly because every write is an upsert.
    private static int ImportPage( SqliteConnection conn, VotePage page ) {
        using var tx = conn.BeginTransaction();
        var count = 0;

        foreach ( var vote in page.Votes ) {
            if ( string.IsNullOrWhiteSpace( vote.Id ) )
                continue;

            var tulos = vote.Aanestystulos;
            Exec( conn, tx, """
                INSERT INTO session ( id, date, title, subject, vp_year, session_number, vote_number,
                                      result_yes, result_no, result_blank, result_absent, cancelled )
                VALUES ( $id, $date, $title, $subject, $year, $session, $number,
                         $yes, $no, $blank, $absent, $cancelled )
                ON CONFLICT ( id ) DO NOTHING
                """,
                ( "$id", vote.Id ),
                // istuntopvm carries a UTC offset ("2026-06-03+03:00") and does not parse as
                // a plain date, so keep the leading ISO day and drop the rest.
                ( "$date", Left( vote.Istuntopvm, 10 ) ),
                ( "$title", vote.Aanestysotsikko?.Fi ?? "" ),
                ( "$subject", vote.Kohta?.Otsikko?.Fi ?? "" ),
                ( "$year", ParseInt( vote.Istuntovpvuosi ) ),
                ( "$session", ParseInt( vote.Istuntonumero ) ),
                ( "$number", ParseInt( vote.Aanestysnumero ) ),
                ( "$yes", tulos?.Jaa ?? 0 ),
                ( "$no", tulos?.Ei ?? 0 ),
                ( "$blank", tulos?.Tyhjia ?? 0 ),
                ( "$absent", tulos?.Poissa ?? 0 ),
                ( "$cancelled", vote.Aanestysmitatoity ? 1 : 0 ) );

            foreach ( var ballot in vote.Aanestystapahtumat ) {
                var party = ballot.Edkryhmalyhenne?.Fi?.Trim() ?? "";
                var choice = VoteValue.Normalize( ballot.Kayttaytyminen?.Fi );

                Exec( conn, tx, """
                    INSERT INTO mp ( person_number, first_name, last_name, party )
                    VALUES ( $pn, $first, $last, $party )
                    ON CONFLICT ( person_number ) DO UPDATE SET party = $party
                    """,
                    ( "$pn", ballot.Henkilonumero ),
                    ( "$first", ballot.Etunimi?.Trim() ?? "" ),
                    ( "$last", ballot.Sukunimi?.Trim() ?? "" ),
                    ( "$party", party ) );

                Exec( conn, tx, """
                    INSERT OR IGNORE INTO vote ( session_id, person_number, party, vote )
                    VALUES ( $sid, $pn, $party, $vote )
                    """,
                    ( "$sid", vote.Id ),
                    ( "$pn", ballot.Henkilonumero ),
                    ( "$party", party ),
                    ( "$vote", choice ) );
            }

            count++;
        }

        tx.Commit();
        return count;
    }

    private static string Left( string? value, int length ) =>
        string.IsNullOrEmpty( value ) ? "" : value[..Math.Min( length, value.Length )];

    private static int ParseInt( string? value ) => int.TryParse( value, out var n ) ? n : 0;

    private static void Exec( SqliteConnection conn, SqliteTransaction tx, string sql, params (string Name, object Value)[] args ) {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach ( var (name, value) in args )
            cmd.Parameters.AddWithValue( name, value );
        cmd.ExecuteNonQuery();
    }

    private static string? GetState( SqliteConnection conn, string key ) {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM sync_state WHERE key = $key";
        cmd.Parameters.AddWithValue( "$key", key );
        return cmd.ExecuteScalar() as string;
    }

    private static void SetState( SqliteConnection conn, string key, string value ) {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO sync_state ( key, value ) VALUES ( $key, $value ) ON CONFLICT ( key ) DO UPDATE SET value = $value";
        cmd.Parameters.AddWithValue( "$key", key );
        cmd.Parameters.AddWithValue( "$value", value );
        cmd.ExecuteNonQuery();
    }
}
