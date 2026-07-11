using Microsoft.Data.Sqlite;

namespace VoteCheckWeb.Data;

// Owns the SQLite connection string and schema. One writer (the sync service),
// many readers (page handlers) — SQLite in WAL mode handles this without a server.
public sealed class Db {

    private readonly string _connectionString;

    public Db( IConfiguration config ) {
        var path = config["VoteCheck:DbPath"] ?? "votecheck.db";
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
    }

    public SqliteConnection Open() {
        var conn = new SqliteConnection( _connectionString );
        conn.Open();
        return conn;
    }

    public void EnsureSchema() {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode = WAL;

            CREATE TABLE IF NOT EXISTS session (
                id            INTEGER PRIMARY KEY,   -- AanestysId
                date          TEXT NOT NULL,         -- IstuntoPvm (ISO)
                title         TEXT NOT NULL,         -- AanestysOtsikko
                subject       TEXT NOT NULL,         -- KohtaOtsikko
                result_yes    INTEGER NOT NULL,
                result_no     INTEGER NOT NULL,
                result_blank  INTEGER NOT NULL,
                result_absent INTEGER NOT NULL,
                cancelled     INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS mp (
                person_number INTEGER PRIMARY KEY,   -- EdustajaHenkiloNumero
                first_name    TEXT NOT NULL,
                last_name     TEXT NOT NULL,
                party         TEXT NOT NULL          -- latest known EdustajaRyhmaLyhenne
            );

            CREATE TABLE IF NOT EXISTS vote (
                session_id    INTEGER NOT NULL REFERENCES session(id),
                person_number INTEGER NOT NULL,
                party         TEXT NOT NULL,          -- party at the time of the vote
                vote          TEXT NOT NULL,           -- Jaa | Ei | Tyhjä | Poissa
                PRIMARY KEY ( session_id, person_number )
            ) WITHOUT ROWID;

            CREATE INDEX IF NOT EXISTS ix_vote_person ON vote ( person_number, session_id );
            CREATE INDEX IF NOT EXISTS ix_session_date ON session ( date );

            CREATE VIRTUAL TABLE IF NOT EXISTS session_fts USING fts5 (
                title, subject, content='session', content_rowid='id'
            );

            CREATE TRIGGER IF NOT EXISTS session_ai AFTER INSERT ON session BEGIN
                INSERT INTO session_fts ( rowid, title, subject )
                VALUES ( new.id, new.title, new.subject );
            END;

            CREATE TABLE IF NOT EXISTS sync_state (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }
}
