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

    // Matters fetched per cycle. One request each, so this is the only part of the sync that
    // scales with the archive rather than with what is new: about 875 for the 2023+ window,
    // filled in over a few cycles rather than in one burst against upstream's rate cap.
    private const int MatterBatch = 60;

    // Sessions whose minutes are read per cycle. Each costs one search — the capped verb,
    // shared with the vote paging — and one document fetch, so the ~150 sessions of the 2023+
    // window fill in over a couple of hours rather than in one burst.
    private const int MinutesBatch = 20;

    // How long to wait before asking again for minutes that were not published yet. They
    // usually appear within hours of the session; a few never do.
    private static readonly TimeSpan MinutesRetry = TimeSpan.FromHours( 6 );

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

        // Rows imported before a column or table existed carry nothing in it, and the cursor
        // sits past them, so nothing would ever go back for them. Rewinding once is cheap —
        // the archive re-walks 50 divisions to a request, so a few dozen calls — and the
        // import writes only what it adds on a row it already has. Each rewind is recorded
        // under its own key, so adding the next one does not repeat the ones before it.
        RewindOnce( conn, "doc_backfill", "record which matter each division decides" );
        RewindOnce( conn, "split_backfill", "record the government and district breakdowns" );
        RereadMinutesOnce( conn, "minutes_reports", "read the committee reports they refer to" );

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

        await SyncMattersAsync( conn, ct );
        await SyncMinutesAsync( conn, ct );
    }

    // Read each session's minutes for what the sides of its votes stood for. Context, like the
    // matters: nothing here may abort the cycle, and a session that fails is asked again next
    // time. Newest sessions first, because those are the divisions people are reading.
    private async Task SyncMinutesAsync( SqliteConnection conn, CancellationToken ct ) {
        var pending = new List<(int Year, int Session)>();
        using ( var cmd = conn.CreateCommand() ) {
            cmd.CommandText = """
                SELECT s.vp_year, s.session_number
                FROM ( SELECT DISTINCT vp_year, session_number FROM session ) s
                LEFT JOIN sitting_minutes m
                    ON m.vp_year = s.vp_year AND m.session_number = s.session_number
                WHERE m.vp_year IS NULL OR ( m.document = '' AND m.checked_at < $retryBefore )
                ORDER BY s.vp_year DESC, s.session_number DESC
                LIMIT $limit
                """;
            cmd.Parameters.AddWithValue( "$retryBefore", ( DateTimeOffset.UtcNow - MinutesRetry ).ToString( "o" ) );
            cmd.Parameters.AddWithValue( "$limit", MinutesBatch );
            using var r = cmd.ExecuteReader();
            while ( r.Read() ) pending.Add( ( r.GetInt32( 0 ), r.GetInt32( 1 ) ) );
        }

        if ( pending.Count == 0 ) return;

        var read = 0;
        // Reports by matter and identifier, for this cycle. A session's votes on one bill all
        // refer to the same report, and so do the sessions of its first and second reading.
        var reports = new Dictionary<string, Mietinto?>();
        foreach ( var (year, session) in pending ) {
            if ( ct.IsCancellationRequested ) break;

            try {
                var minutes = await _api.GetSessionMinutesAsync( year, session, ct );
                if ( minutes != null )
                    await ResolveFromReportsAsync( conn, minutes, reports, ct );
                if ( StoreMinutes( conn, year, session, minutes ) > 0 ) read++;
            } catch ( Exception ex ) when ( ex is not OperationCanceledException ) {
                _log.LogWarning( ex, "Could not read minutes of session {Session}/{Year}; will retry next cycle",
                    session, year );
            }

            await Task.Delay( _requestDelay, ct );
        }

        if ( read > 0 )
            _log.LogInformation( "Read the minutes of {Count} sessions", read );
    }

    // Fills in the texts the minutes only refer to — the committee's statements and sections,
    // and the dissenting opinions' — from the report each vote's item was decided on.
    //
    // A report that cannot be fetched throws, so the session is not recorded as read and is
    // asked for again next cycle; storing it without the texts would keep them out for good.
    // A matter that does not list the report is an answer, and the votes keep what the minutes
    // themselves said.
    private async Task ResolveFromReportsAsync(
        SqliteConnection conn, Poytakirja minutes, Dictionary<string, Mietinto?> reports, CancellationToken ct ) {

        foreach ( var vote in minutes.Aanestykset ) {
            if ( vote.Mietinto.Length == 0 || ( vote.Jaa?.Viite == null && vote.Ei?.Viite == null ) )
                continue;

            var matter = DocumentOf( conn, vote.AanestysTunnus );
            if ( matter == null ) continue;

            var key = $"{matter}\n{vote.Mietinto}";
            if ( !reports.TryGetValue( key, out var report ) ) {
                report = await _api.GetCommitteeReportAsync( matter, vote.Mietinto, ct );
                reports[ key ] = report;
                await Task.Delay( _requestDelay, ct );
            }
            if ( report == null ) continue;

            if ( vote.Jaa != null ) report.Resolve( vote.Jaa );
            if ( vote.Ei != null ) report.Resolve( vote.Ei );
        }
    }

    // The matter a division decides a step of, which is where its committee report is listed.
    private static string? DocumentOf( SqliteConnection conn, string sessionId ) {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT doc_id FROM session WHERE id = $id AND doc_id <> ''";
        cmd.Parameters.AddWithValue( "$id", sessionId );
        return cmd.ExecuteScalar() as string;
    }

    // Stores what one session's minutes say about its votes, and records that the session has
    // been asked about. Returns the number of divisions it wrote.
    internal static int StoreMinutes( SqliteConnection conn, int year, int session, Poytakirja? minutes ) {
        using var tx = conn.BeginTransaction();
        var written = 0;

        foreach ( var vote in minutes?.Aanestykset ?? [] ) {
            // Only divisions the mirror holds. The minutes record every vote of the session,
            // and a vote the mirror lacks has nowhere to be shown.
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO session_minutes ( session_id, setup, outcome,
                                              yes_label, yes_context, yes_text,
                                              no_label, no_context, no_text )
                SELECT $sid, $setup, $outcome, $yesLabel, $yesContext, $yesText,
                       $noLabel, $noContext, $noText
                WHERE EXISTS ( SELECT 1 FROM session WHERE id = $sid )
                ON CONFLICT ( session_id ) DO UPDATE SET
                    setup       = excluded.setup,
                    outcome     = excluded.outcome,
                    yes_label   = excluded.yes_label,
                    yes_context = excluded.yes_context,
                    yes_text    = excluded.yes_text,
                    no_label    = excluded.no_label,
                    no_context  = excluded.no_context,
                    no_text     = excluded.no_text
                """;
            cmd.Parameters.AddWithValue( "$sid", vote.AanestysTunnus );
            cmd.Parameters.AddWithValue( "$setup", vote.Asettelu );
            cmd.Parameters.AddWithValue( "$outcome", vote.Tulos );
            cmd.Parameters.AddWithValue( "$yesLabel", vote.Jaa?.Nimi ?? "" );
            cmd.Parameters.AddWithValue( "$yesContext", vote.Jaa?.Kohta ?? "" );
            cmd.Parameters.AddWithValue( "$yesText", vote.Jaa?.Teksti ?? "" );
            cmd.Parameters.AddWithValue( "$noLabel", vote.Ei?.Nimi ?? "" );
            cmd.Parameters.AddWithValue( "$noContext", vote.Ei?.Kohta ?? "" );
            cmd.Parameters.AddWithValue( "$noText", vote.Ei?.Teksti ?? "" );
            written += cmd.ExecuteNonQuery();
        }

        // A document that yielded no votes is recorded as not found, so it is asked for again
        // rather than marked as read for ever: an unreadable or half-published document is
        // something upstream may still fix.
        Exec( conn, tx, """
            INSERT INTO sitting_minutes ( vp_year, session_number, document, checked_at )
            VALUES ( $year, $session, $document, $at )
            ON CONFLICT ( vp_year, session_number ) DO UPDATE SET
                document   = excluded.document,
                checked_at = excluded.checked_at
            """,
            ( "$year", year ),
            ( "$session", session ),
            ( "$document", written > 0 ? minutes!.Edktunnus : "" ),
            ( "$at", DateTimeOffset.UtcNow.ToString( "o" ) ) );

        tx.Commit();
        return written;
    }

    // Fetch the matters the divisions point at — what a bill is about, in words a reader
    // recognises, rather than only its legal title.
    //
    // Deliberately last, and in its own bounded batch. A division is the thing this site
    // exists to show; its matter is context. So nothing here may abort the cycle: if upstream
    // refuses a matter, divisions still arrive, and the matter is simply retried next time.
    private async Task SyncMattersAsync( SqliteConnection conn, CancellationToken ct ) {
        var pending = new List<string>();
        using ( var cmd = conn.CreateCommand() ) {
            cmd.CommandText = """
                SELECT DISTINCT s.doc_id
                FROM session s
                LEFT JOIN matter m ON m.id = s.doc_id
                WHERE s.doc_id <> '' AND m.id IS NULL
                LIMIT $limit
                """;
            cmd.Parameters.AddWithValue( "$limit", MatterBatch );
            using var r = cmd.ExecuteReader();
            while ( r.Read() ) pending.Add( r.GetString( 0 ) );
        }

        if ( pending.Count == 0 ) return;

        var stored = 0;
        foreach ( var id in pending ) {
            if ( ct.IsCancellationRequested ) break;

            try {
                var matter = await _api.GetMatterAsync( id, ct );

                // A null is recorded too. Some identifiers name several documents at once
                // ("LA 1, 18/2023 vp") and have no single matter; writing the miss stops them
                // being retried every cycle for ever.
                StoreMatter( conn, id, matter );
                if ( matter != null ) stored++;
            } catch ( Exception ex ) when ( ex is not OperationCanceledException ) {
                _log.LogWarning( ex, "Could not fetch matter {Matter}; will retry next cycle", id );
            }

            await Task.Delay( _requestDelay, ct );
        }

        if ( stored > 0 )
            _log.LogInformation( "Recorded {Count} matters", stored );
    }

    private static void StoreMatter( SqliteConnection conn, string id, Valtiopaivaasia? matter ) {
        using var tx = conn.BeginTransaction();
        Exec( conn, tx, """
            INSERT INTO matter ( id, type_name, title, outcome, keywords, url, fetched_at )
            VALUES ( $id, $type, $title, $outcome, $keywords, $url, $at )
            ON CONFLICT ( id ) DO UPDATE SET
                type_name  = excluded.type_name,
                title      = excluded.title,
                outcome    = excluded.outcome,
                keywords   = excluded.keywords,
                url        = excluded.url,
                fetched_at = excluded.fetched_at
            """,
            ( "$id", id ),
            ( "$type", matter?.Asiakirjatyyppinimi?.Fi ?? "" ),
            ( "$title", matter?.Nimeke?.Fi ?? "" ),
            ( "$outcome", matter?.Kokonaispaatosnimi?.Fi ?? "" ),
            ( "$keywords", matter == null ? "" : string.Join( "\n", matter.Keywords() ) ),
            ( "$url", matter?.PublicUrl()?.ToString() ?? "" ),
            ( "$at", DateTimeOffset.UtcNow.ToString( "o" ) ) );

        // Put the subject terms in the search index, for every division of this matter.
        //
        // The division was indexed when it arrived, before its matter was known, so the
        // keywords have to be written in afterwards — this is the step that makes searching
        // "alkoholipolitiikka" find the divisions about it rather than only the ones whose
        // legal title happens to contain the word.
        Exec( conn, tx, """
            UPDATE session_fts
            SET keywords = $keywords
            WHERE rowid IN ( SELECT seq FROM session WHERE doc_id = $id )
            """,
            ( "$keywords", matter == null ? "" : string.Join( "\n", matter.Keywords() ) ),
            ( "$id", id ) );

        tx.Commit();
    }

    // One page, one transaction: a crash mid-page leaves the cursor where it was, and the
    // page is re-imported harmlessly because every write is an upsert.
    internal static int ImportPage( SqliteConnection conn, VotePage page ) {
        using var tx = conn.BeginTransaction();
        var count = 0;

        foreach ( var vote in page.Votes ) {
            if ( string.IsNullOrWhiteSpace( vote.Id ) )
                continue;

            var tulos = vote.Aanestystulos;
            Exec( conn, tx, """
                INSERT INTO session ( id, date, title, subject, title_sv, subject_sv,
                                      vp_year, session_number, vote_number,
                                      result_yes, result_no, result_blank, result_absent, cancelled,
                                      doc_id, doc_type )
                VALUES ( $id, $date, $title, $subject, $titleSv, $subjectSv,
                         $year, $session, $number,
                         $yes, $no, $blank, $absent, $cancelled,
                         $docId, $docType )
                -- Only the document is updated on a re-import. A division's result never
                -- changes, but rows imported before these columns existed carry none, and
                -- re-walking the archive is how they get one.
                ON CONFLICT ( id ) DO UPDATE SET
                    doc_id = excluded.doc_id,
                    doc_type = excluded.doc_type
                """,
                ( "$id", vote.Id ),
                // istuntopvm carries a UTC offset ("2026-06-03+03:00") and does not parse as
                // a plain date, so keep the leading ISO day and drop the rest.
                ( "$date", Left( vote.Istuntopvm, 10 ) ),
                ( "$title", vote.Aanestysotsikko?.Fi ?? "" ),
                ( "$subject", vote.Kohta?.Otsikko?.Fi ?? "" ),
                ( "$titleSv", vote.Aanestysotsikko?.Sv ?? "" ),
                ( "$subjectSv", vote.Kohta?.Otsikko?.Sv ?? "" ),
                ( "$year", ParseInt( vote.Istuntovpvuosi ) ),
                ( "$session", ParseInt( vote.Istuntonumero ) ),
                ( "$number", ParseInt( vote.Aanestysnumero ) ),
                ( "$yes", tulos?.Jaa ?? 0 ),
                ( "$no", tulos?.Ei ?? 0 ),
                ( "$blank", tulos?.Tyhjia ?? 0 ),
                ( "$absent", tulos?.Poissa ?? 0 ),
                ( "$cancelled", vote.Aanestysmitatoity ? 1 : 0 ),
                // "HE 113/2026 vp" — the matter this division decides a step of.
                ( "$docId", vote.Kohta?.Asiakirjat?.PaaasiakirjaEduskuntatunnus?.Fi?.Trim() ?? "" ),
                ( "$docType", vote.Kohta?.Asiakirjat?.PaaasiakirjaAsiatyyppi?.Trim() ?? "" ) );

            ImportSplits( conn, tx, vote.Id, SplitKind.Government, vote.HallitusoppositioJakaumat );
            ImportSplits( conn, tx, vote.Id, SplitKind.District, vote.VaalipiiriJakaumat );

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

    private void RewindOnce( SqliteConnection conn, string key, string reason ) {
        if ( GetState( conn, key ) != null ) return;
        SetState( conn, "vote_cursor", "0" );
        SetState( conn, key, DateTimeOffset.UtcNow.ToString( "o" ) );
        _log.LogInformation( "Rewinding once to {Reason}", reason );
    }

    // The minutes counterpart of RewindOnce: forget which sessions' minutes have been read, so
    // the next cycles read them all again with whatever the reading has learnt since. The rows
    // already stored stay and are replaced as each session is read; nothing goes blank.
    private void RereadMinutesOnce( SqliteConnection conn, string key, string reason ) {
        if ( GetState( conn, key ) != null ) return;
        using ( var cmd = conn.CreateCommand() ) {
            cmd.CommandText = "DELETE FROM sitting_minutes";
            cmd.ExecuteNonQuery();
        }
        SetState( conn, key, DateTimeOffset.UtcNow.ToString( "o" ) );
        _log.LogInformation( "Reading the minutes again to {Reason}", reason );
    }

    private static void ImportSplits(
        SqliteConnection conn, SqliteTransaction tx, string sessionId, string kind,
        IReadOnlyList<Aanestysjakauma> splits ) {

        for ( var i = 0; i < splits.Count; i++ ) {
            var split = splits[ i ];
            var name = split.Nimi?.Fi?.Trim();
            if ( string.IsNullOrEmpty( name ) ) continue;

            // An upsert rather than INSERT OR IGNORE, unlike the ballots: the rewind that
            // backfills this table walks rows that are already here, and it is the counts it
            // has come for.
            Exec( conn, tx, """
                INSERT INTO session_split ( session_id, kind, name, name_sv, ordinal,
                                            yes, no, blank, absent )
                VALUES ( $sid, $kind, $name, $nameSv, $ordinal, $yes, $no, $blank, $absent )
                ON CONFLICT ( session_id, kind, name ) DO UPDATE SET
                    name_sv = excluded.name_sv,
                    ordinal = excluded.ordinal,
                    yes     = excluded.yes,
                    no      = excluded.no,
                    blank   = excluded.blank,
                    absent  = excluded.absent
                """,
                ( "$sid", sessionId ),
                ( "$kind", kind ),
                ( "$name", name ),
                ( "$nameSv", split.Nimi?.Sv?.Trim() ?? "" ),
                ( "$ordinal", i ),
                ( "$yes", split.Jaa ),
                ( "$no", split.Ei ),
                ( "$blank", split.Tyhjia ),
                ( "$absent", split.Poissa ) );
        }
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
