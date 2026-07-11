using Microsoft.Data.Sqlite;
using VoteCheckWeb.Data;

namespace VoteCheckWeb.Sync;

// Delta-syncs voting data from the Eduskunta open data API into the local SQLite cache.
// SaliDBAanestys is append-only, so a page cursor stored in sync_state is enough:
// on each cycle we resume from the last (possibly partial) page, insert any sessions
// we don't have yet, and fetch the individual MP votes for each new session.
public sealed class VoteSyncService : BackgroundService {

    private const int PerPage = 100;

    private readonly Db _db;
    private readonly EduskuntaApiClient _api;
    private readonly ILogger<VoteSyncService> _log;
    private readonly int _minYear;
    private readonly TimeSpan _pollInterval;

    public VoteSyncService( Db db, EduskuntaApiClient api, IConfiguration config, ILogger<VoteSyncService> log ) {
        _db = db;
        _api = api;
        _log = log;
        // Backfill floor: sessions from earlier parliamentary years are skipped.
        _minYear = config.GetValue( "VoteCheck:SyncMinYear", 2023 );
        _pollInterval = TimeSpan.FromMinutes( config.GetValue( "VoteCheck:SyncPollMinutes", 15 ) );
    }

    protected override async Task ExecuteAsync( CancellationToken ct ) {
        while ( !ct.IsCancellationRequested ) {
            try {
                await SyncAsync( ct );
            } catch ( Exception ex ) when ( ex is not OperationCanceledException ) {
                _log.LogError( ex, "Sync cycle failed; retrying after poll interval" );
            }
            await Task.Delay( _pollInterval, ct );
        }
    }

    private async Task SyncAsync( CancellationToken ct ) {
        using var conn = _db.Open();

        var page = int.Parse( GetState( conn, "aanestys_page" ) ?? "0" );
        var hasMore = true;
        var imported = 0;

        while ( hasMore && !ct.IsCancellationRequested ) {
            var result = await _api.GetPageAsync( "SaliDBAanestys", page, PerPage, ct: ct );
            hasMore = result.HasMore;

            foreach ( var row in result.Rows ) {
                if ( !int.TryParse( row.GetValueOrDefault( "AanestysId" ), out var sessionId ) )
                    continue;
                // Every vote is stored twice: KieliId 1 = Finnish, 2 = Swedish duplicate
                // under the adjacent AanestysId. Import only the Finnish rows.
                if ( row.GetValueOrDefault( "KieliId" )?.Trim() != "1" )
                    continue;
                if ( int.TryParse( row.GetValueOrDefault( "IstuntoVPVuosi" ), out var year ) && year < _minYear )
                    continue;
                if ( SessionExists( conn, sessionId ) )
                    continue;

                await ImportSessionAsync( conn, sessionId, row, ct );
                imported++;
            }

            if ( hasMore ) {
                page++;
                SetState( conn, "aanestys_page", page.ToString() );
            }
        }

        // The last page was partial; resume from it next cycle to pick up appended rows.
        SetState( conn, "aanestys_page", page.ToString() );
        if ( imported > 0 )
            _log.LogInformation( "Sync complete: {Count} new voting sessions imported", imported );
    }

    private async Task ImportSessionAsync( SqliteConnection conn, int sessionId, Dictionary<string, string> s, CancellationToken ct ) {
        using var tx = conn.BeginTransaction();

        Exec( conn, tx, """
            INSERT OR IGNORE INTO session ( id, date, title, subject, result_yes, result_no, result_blank, result_absent, cancelled )
            VALUES ( $id, $date, $title, $subject, $yes, $no, $blank, $absent, $cancelled )
            """,
            ( "$id", sessionId ),
            ( "$date", s.GetValueOrDefault( "IstuntoPvm", "" ) ),
            ( "$title", s.GetValueOrDefault( "AanestysOtsikko", "" ) ),
            ( "$subject", s.GetValueOrDefault( "KohtaOtsikko", "" ) ),
            ( "$yes", ParseInt( s, "AanestysTulosJaa" ) ),
            ( "$no", ParseInt( s, "AanestysTulosEi" ) ),
            ( "$blank", ParseInt( s, "AanestysTulosTyhjia" ) ),
            ( "$absent", ParseInt( s, "AanestysTulosPoissa" ) ),
            ( "$cancelled", s.GetValueOrDefault( "AanestysMitatoity" ) == "1" ? 1 : 0 ) );

        // Individual MP votes for this session; also refreshes the MP roster.
        var page = 0;
        var hasMore = true;
        while ( hasMore && !ct.IsCancellationRequested ) {
            var result = await _api.GetPageAsync( "SaliDBAanestysEdustaja", page, PerPage,
                "AanestysId", sessionId.ToString(), ct );
            hasMore = result.HasMore;
            page++;

            foreach ( var v in result.Rows ) {
                if ( !int.TryParse( v.GetValueOrDefault( "EdustajaHenkiloNumero" ), out var personNumber ) )
                    continue;
                var party = v.GetValueOrDefault( "EdustajaRyhmaLyhenne", "" ).Trim();
                var vote = NormalizeVote( v.GetValueOrDefault( "EdustajaAanestys", "" ) );

                Exec( conn, tx, """
                    INSERT INTO mp ( person_number, first_name, last_name, party )
                    VALUES ( $pn, $first, $last, $party )
                    ON CONFLICT ( person_number ) DO UPDATE SET party = $party
                    """,
                    ( "$pn", personNumber ),
                    ( "$first", v.GetValueOrDefault( "EdustajaEtunimi", "" ).Trim() ),
                    ( "$last", v.GetValueOrDefault( "EdustajaSukunimi", "" ).Trim() ),
                    ( "$party", party ) );

                Exec( conn, tx, """
                    INSERT OR IGNORE INTO vote ( session_id, person_number, party, vote )
                    VALUES ( $sid, $pn, $party, $vote )
                    """,
                    ( "$sid", sessionId ),
                    ( "$pn", personNumber ),
                    ( "$party", party ),
                    ( "$vote", vote ) );
            }
        }

        tx.Commit();
    }

    // API vote values are space-padded, and the source data uses "Tyhjää";
    // canonical form in the cache is Jaa | Ei | Tyhjä | Poissa.
    private static string NormalizeVote( string raw ) => raw.Trim() switch {
        "Jaa" => "Jaa",
        "Ei" => "Ei",
        "Tyhjää" or "Tyhjä" => "Tyhjä",
        "Poissa" => "Poissa",
        var other => other,
    };

    private static bool SessionExists( SqliteConnection conn, int sessionId ) {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM session WHERE id = $id";
        cmd.Parameters.AddWithValue( "$id", sessionId );
        return cmd.ExecuteScalar() != null;
    }

    private static int ParseInt( Dictionary<string, string> row, string key ) =>
        int.TryParse( row.GetValueOrDefault( key ), out var value ) ? value : 0;

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
