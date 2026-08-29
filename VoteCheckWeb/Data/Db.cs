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
                -- Surrogate integer key. Exists only because FTS5 external-content
                -- tables require an INTEGER content_rowid and cannot key off `id`.
                seq            INTEGER PRIMARY KEY,
                -- Vote identifier as the API states it, e.g. "2026-60-1"
                -- ({vpYear}-{sessionNumber}-{voteNumber}).
                id             TEXT NOT NULL UNIQUE,
                date           TEXT NOT NULL,         -- ISO date; upstream istuntopvm carries a UTC offset we trim
                title          TEXT NOT NULL,         -- aanestysotsikko
                subject        TEXT NOT NULL,         -- kohta.otsikko
                -- Components of `id`, stored separately because ordering by the id
                -- string is wrong: "2009-114-4" sorts before "2009-24-1" but happened
                -- five months later. Chronology needs the numbers, not the text.
                vp_year        INTEGER NOT NULL,
                session_number INTEGER NOT NULL,
                vote_number    INTEGER NOT NULL,
                result_yes     INTEGER NOT NULL,
                result_no      INTEGER NOT NULL,
                result_blank   INTEGER NOT NULL,
                result_absent  INTEGER NOT NULL,
                cancelled      INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS mp (
                person_number INTEGER PRIMARY KEY,   -- henkilonumero
                first_name    TEXT NOT NULL,
                last_name     TEXT NOT NULL,
                party         TEXT NOT NULL          -- latest known edkryhmalyhenne
            );

            CREATE TABLE IF NOT EXISTS vote (
                session_id    TEXT NOT NULL REFERENCES session(id),
                person_number INTEGER NOT NULL,
                party         TEXT NOT NULL,          -- party at the time of the vote
                vote          TEXT NOT NULL,          -- Jaa | Ei | Tyhjä | Poissa
                PRIMARY KEY ( session_id, person_number )
            ) WITHOUT ROWID;

            CREATE INDEX IF NOT EXISTS ix_vote_person ON vote ( person_number, session_id );
            CREATE INDEX IF NOT EXISTS ix_session_date ON session ( date );
            CREATE INDEX IF NOT EXISTS ix_session_chrono
                ON session ( vp_year DESC, session_number DESC, vote_number DESC );

            CREATE VIRTUAL TABLE IF NOT EXISTS session_fts USING fts5 (
                title, subject, content='session', content_rowid='seq'
            );

            -- Voting history is append-only, so an insert trigger is sufficient.
            CREATE TRIGGER IF NOT EXISTS session_ai AFTER INSERT ON session BEGIN
                INSERT INTO session_fts ( rowid, title, subject )
                VALUES ( new.seq, new.title, new.subject );
            END;

            CREATE TABLE IF NOT EXISTS sync_state (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }
}
