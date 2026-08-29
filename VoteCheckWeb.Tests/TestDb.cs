using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using VoteCheckWeb.Data;

namespace VoteCheckWeb.Tests;

// A throwaway mirror on disk, built by the real EnsureSchema so the tests exercise the
// shipped schema — triggers, FTS index and all — rather than a hand-written copy of it.
//
// A file rather than :memory: because Db opens a fresh connection per query, and an
// in-memory database would vanish between them unless a connection were held open.
internal sealed class TestDb : IDisposable {

    private readonly string _path;

    public Db Db { get; }
    public Queries Queries { get; }

    public TestDb() {
        _path = Path.Combine( Path.GetTempPath(), $"votecheck-test-{Guid.NewGuid():N}.db" );
        Db = new Db( new ConfigurationBuilder()
            .AddInMemoryCollection( new Dictionary<string, string?> { ["VoteCheck:DbPath"] = _path } )
            .Build() );
        Db.EnsureSchema();
        Queries = new Queries( Db );
    }

    public void AddSession(
        string id, string date, string title, string subject,
        int year, int sessionNumber, int voteNumber,
        int yes = 0, int no = 0, int blank = 0, int absent = 0, bool cancelled = false,
        string titleSv = "", string subjectSv = "" ) {
        Exec( """
            INSERT INTO session ( id, date, title, subject, title_sv, subject_sv,
                                  vp_year, session_number, vote_number,
                                  result_yes, result_no, result_blank, result_absent, cancelled )
            VALUES ( $id, $date, $title, $subject, $tsv, $ssv,
                     $y, $s, $n, $yes, $no, $blank, $absent, $c )
            """,
            ( "$id", id ), ( "$date", date ), ( "$title", title ), ( "$subject", subject ),
            ( "$tsv", titleSv ), ( "$ssv", subjectSv ),
            ( "$y", year ), ( "$s", sessionNumber ), ( "$n", voteNumber ),
            ( "$yes", yes ), ( "$no", no ), ( "$blank", blank ), ( "$absent", absent ),
            ( "$c", cancelled ? 1 : 0 ) );
    }

    public void AddMp( int personNumber, string first, string last, string party ) =>
        Exec( "INSERT INTO mp ( person_number, first_name, last_name, party ) VALUES ( $pn, $f, $l, $p )",
            ( "$pn", personNumber ), ( "$f", first ), ( "$l", last ), ( "$p", party ) );

    public void AddVote( string sessionId, int personNumber, string party, string vote ) =>
        Exec( "INSERT INTO vote ( session_id, person_number, party, vote ) VALUES ( $s, $pn, $p, $v )",
            ( "$s", sessionId ), ( "$pn", personNumber ), ( "$p", party ), ( "$v", vote ) );

    private void Exec( string sql, params (string Name, object Value)[] args ) {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach ( var (name, value) in args )
            cmd.Parameters.AddWithValue( name, value );
        cmd.ExecuteNonQuery();
    }

    public void Dispose() {
        SqliteConnection.ClearAllPools();
        foreach ( var suffix in new[] { "", "-wal", "-shm" } ) {
            try { File.Delete( _path + suffix ); } catch ( IOException ) { /* best effort */ }
        }
    }
}
