using Microsoft.Data.Sqlite;

namespace VoteCheckWeb.Data;

public sealed record SessionSummary( int Id, string Date, string Title, string Subject,
    int Yes, int No, int Blank, int Absent );

public sealed record MpSummary( int PersonNumber, string FirstName, string LastName, string Party );

public sealed record MpVote( int SessionId, string Date, string Title, string Vote );

public sealed record PartyDistribution( string Party, int Yes, int No, int Blank, int Absent );

public sealed record IndividualVote( int PersonNumber, string FirstName, string LastName, string Party, string Vote );

public sealed record MpProfile( MpSummary Mp, int TotalVotes, int Present, IReadOnlyList<MpVote> LatestVotes );

// Read-side queries against the SQLite cache. Every method opens its own
// short-lived connection; SQLite + WAL makes that cheap.
public sealed class Queries {

    private readonly Db _db;

    public Queries( Db db ) => _db = db;

    public IReadOnlyList<SessionSummary> LatestSessions( int count ) {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, date, title, subject, result_yes, result_no, result_blank, result_absent
            FROM session WHERE cancelled = 0 ORDER BY id DESC LIMIT $count
            """;
        cmd.Parameters.AddWithValue( "$count", count );
        return ReadSessions( cmd );
    }

    public IReadOnlyList<SessionSummary> SearchSessions( string query, int count ) {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.id, s.date, s.title, s.subject, s.result_yes, s.result_no, s.result_blank, s.result_absent
            FROM session_fts f JOIN session s ON s.id = f.rowid
            WHERE session_fts MATCH $query AND s.cancelled = 0
            ORDER BY rank LIMIT $count
            """;
        // Finnish compounds ("lakiehdotus") mean exact-token match misses most hits;
        // turn each word into a quoted prefix term: laki muutos -> "laki"* "muutos"*
        var terms = query.Split( ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries )
            .Select( t => "\"" + t.Replace( "\"", "\"\"" ) + "\"*" );
        cmd.Parameters.AddWithValue( "$query", string.Join( " ", terms ) );
        cmd.Parameters.AddWithValue( "$count", count );
        return ReadSessions( cmd );
    }

    public SessionSummary? GetSession( int id ) {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, date, title, subject, result_yes, result_no, result_blank, result_absent
            FROM session WHERE id = $id
            """;
        cmd.Parameters.AddWithValue( "$id", id );
        return ReadSessions( cmd ).FirstOrDefault();
    }

    public IReadOnlyList<PartyDistribution> GetPartyDistribution( int sessionId ) {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT party,
                   SUM( vote = 'Jaa' ), SUM( vote = 'Ei' ),
                   SUM( vote = 'Tyhjä' ), SUM( vote = 'Poissa' )
            FROM vote WHERE session_id = $id
            GROUP BY party ORDER BY COUNT(*) DESC
            """;
        cmd.Parameters.AddWithValue( "$id", sessionId );
        var result = new List<PartyDistribution>();
        using var r = cmd.ExecuteReader();
        while ( r.Read() )
            result.Add( new PartyDistribution( r.GetString( 0 ), r.GetInt32( 1 ), r.GetInt32( 2 ), r.GetInt32( 3 ), r.GetInt32( 4 ) ) );
        return result;
    }

    public IReadOnlyList<IndividualVote> GetIndividualVotes( int sessionId, string? party = null ) {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT v.person_number, m.first_name, m.last_name, v.party, v.vote
            FROM vote v JOIN mp m ON m.person_number = v.person_number
            WHERE v.session_id = $id AND ( $party IS NULL OR v.party = $party COLLATE NOCASE )
            ORDER BY m.last_name, m.first_name
            """;
        cmd.Parameters.AddWithValue( "$id", sessionId );
        cmd.Parameters.AddWithValue( "$party", (object?)party ?? DBNull.Value );
        var result = new List<IndividualVote>();
        using var r = cmd.ExecuteReader();
        while ( r.Read() )
            result.Add( new IndividualVote( r.GetInt32( 0 ), r.GetString( 1 ), r.GetString( 2 ), r.GetString( 3 ), r.GetString( 4 ) ) );
        return result;
    }

    public IReadOnlyList<MpSummary> FindMps( string? nameFilter = null ) {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT person_number, first_name, last_name, party FROM mp
            WHERE $filter IS NULL OR last_name LIKE $filter || '%' COLLATE NOCASE
            ORDER BY last_name, first_name
            """;
        cmd.Parameters.AddWithValue( "$filter", (object?)nameFilter ?? DBNull.Value );
        var result = new List<MpSummary>();
        using var r = cmd.ExecuteReader();
        while ( r.Read() )
            result.Add( new MpSummary( r.GetInt32( 0 ), r.GetString( 1 ), r.GetString( 2 ), r.GetString( 3 ) ) );
        return result;
    }

    public MpProfile? GetMpProfile( int personNumber, int latestCount ) {
        using var conn = _db.Open();

        MpSummary? mp;
        using ( var cmd = conn.CreateCommand() ) {
            cmd.CommandText = "SELECT person_number, first_name, last_name, party FROM mp WHERE person_number = $pn";
            cmd.Parameters.AddWithValue( "$pn", personNumber );
            using var r = cmd.ExecuteReader();
            mp = r.Read() ? new MpSummary( r.GetInt32( 0 ), r.GetString( 1 ), r.GetString( 2 ), r.GetString( 3 ) ) : null;
        }
        if ( mp == null ) return null;

        int total, present;
        using ( var cmd = conn.CreateCommand() ) {
            cmd.CommandText = "SELECT COUNT(*), SUM( vote != 'Poissa' ) FROM vote WHERE person_number = $pn";
            cmd.Parameters.AddWithValue( "$pn", personNumber );
            using var r = cmd.ExecuteReader();
            r.Read();
            total = r.GetInt32( 0 );
            present = total > 0 ? r.GetInt32( 1 ) : 0;
        }

        var latest = new List<MpVote>();
        using ( var cmd = conn.CreateCommand() ) {
            cmd.CommandText = """
                SELECT s.id, s.date, s.title, v.vote
                FROM vote v JOIN session s ON s.id = v.session_id
                WHERE v.person_number = $pn ORDER BY s.id DESC LIMIT $count
                """;
            cmd.Parameters.AddWithValue( "$pn", personNumber );
            cmd.Parameters.AddWithValue( "$count", latestCount );
            using var r = cmd.ExecuteReader();
            while ( r.Read() )
                latest.Add( new MpVote( r.GetInt32( 0 ), r.GetString( 1 ), r.GetString( 2 ), r.GetString( 3 ) ) );
        }

        return new MpProfile( mp, total, present, latest );
    }

    private static IReadOnlyList<SessionSummary> ReadSessions( SqliteCommand cmd ) {
        var result = new List<SessionSummary>();
        using var r = cmd.ExecuteReader();
        while ( r.Read() )
            result.Add( new SessionSummary( r.GetInt32( 0 ), r.GetString( 1 ), r.GetString( 2 ), r.GetString( 3 ),
                r.GetInt32( 4 ), r.GetInt32( 5 ), r.GetInt32( 6 ), r.GetInt32( 7 ) ) );
        return result;
    }
}
