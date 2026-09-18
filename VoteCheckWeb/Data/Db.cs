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
                title          TEXT NOT NULL,         -- aanestysotsikko.fi
                subject        TEXT NOT NULL,         -- kohta.otsikko.fi
                -- Swedish is carried alongside because Finland is officially bilingual and
                -- upstream already sends both on every division; English is not stored,
                -- because the vote endpoints do not carry it at all.
                title_sv       TEXT NOT NULL DEFAULT '',
                subject_sv     TEXT NOT NULL DEFAULT '',
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

            -- Holds its own copy of the text rather than pointing at `session`, which an
            -- external-content index would. Two reasons, both about the keywords: they live in
            -- `matter`, which an index over `session` cannot reach, and they arrive after the
            -- division does — the matter is fetched in a later pass — so no insert trigger
            -- could ever fill them. The copy costs about a megabyte over the whole window.
            CREATE VIRTUAL TABLE IF NOT EXISTS session_fts USING fts5 ( title, subject, keywords );

            -- Voting history is append-only, so an insert trigger is enough. It looks the
            -- keywords up rather than writing a blank: a division can arrive after the matter
            -- it belongs to is already held, and then they are known at insert time. A matter
            -- arriving later fills them in itself.
            CREATE TRIGGER IF NOT EXISTS session_ai AFTER INSERT ON session BEGIN
                INSERT INTO session_fts ( rowid, title, subject, keywords )
                VALUES ( new.seq, new.title, new.subject,
                         COALESCE( ( SELECT keywords FROM matter WHERE id = new.doc_id ), '' ) );
            END;

            -- A matter before parliament: the bill, report or interpellation a division
            -- decides a step of. Several divisions share one — about three to each — so this
            -- is keyed on the parliamentary identifier rather than repeated per division.
            CREATE TABLE IF NOT EXISTS matter (
                id         TEXT PRIMARY KEY,             -- "HE 113/2026 vp"
                type_name  TEXT NOT NULL DEFAULT '',     -- "Hallituksen esitys", not the code
                title      TEXT NOT NULL DEFAULT '',
                -- How the matter ended overall. NOT the result of any one division: a bill can
                -- lose an amendment vote and pass anyway, so whatever renders this has to say
                -- which of the two it is showing.
                outcome    TEXT NOT NULL DEFAULT '',
                keywords   TEXT NOT NULL DEFAULT '',     -- newline separated, in upstream's order
                url        TEXT NOT NULL DEFAULT '',
                fetched_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS sync_state (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();

        Migrate( conn );
    }

    // Columns added to a table that already exists.
    //
    // CREATE TABLE IF NOT EXISTS does nothing to a table that is already there, so a column
    // added later never reaches a database that predates it. The mirror is rebuildable, but a
    // rebuild costs thousands of upstream requests and leaves the site half-empty while it
    // runs — a poor trade for adding a column. SQLite's ADD COLUMN is cheap and does not
    // rewrite the table.
    private static void Migrate( SqliteConnection conn ) {
        AddColumn( conn, "session", "doc_id", "TEXT NOT NULL DEFAULT ''" );
        AddColumn( conn, "session", "doc_type", "TEXT NOT NULL DEFAULT ''" );
        RebuildSearchIndexForKeywords( conn );
    }

    // The search index predates the keywords and cannot gain a column: an FTS5 table's shape
    // is fixed at creation, and CREATE ... IF NOT EXISTS leaves an existing one alone. So an
    // index without a keywords column is dropped and built again.
    //
    // Nothing is fetched to do it. Every value is already in the mirror, so the index is
    // repopulated from session joined to matter in a single statement — unlike the columns
    // above, this costs no upstream traffic at all.
    private static void RebuildSearchIndexForKeywords( SqliteConnection conn ) {
        using ( var check = conn.CreateCommand() ) {
            check.CommandText =
                "SELECT COUNT(*) FROM pragma_table_info( 'session_fts' ) WHERE name = 'keywords'";
            if ( Convert.ToInt64( check.ExecuteScalar() ) > 0 ) return;
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DROP TRIGGER IF EXISTS session_ai;
            DROP TABLE IF EXISTS session_fts;

            CREATE VIRTUAL TABLE session_fts USING fts5 ( title, subject, keywords );

            CREATE TRIGGER session_ai AFTER INSERT ON session BEGIN
                INSERT INTO session_fts ( rowid, title, subject, keywords )
                VALUES ( new.seq, new.title, new.subject,
                         COALESCE( ( SELECT keywords FROM matter WHERE id = new.doc_id ), '' ) );
            END;

            INSERT INTO session_fts ( rowid, title, subject, keywords )
            SELECT s.seq, s.title, s.subject, COALESCE( m.keywords, '' )
            FROM session s
            LEFT JOIN matter m ON m.id = s.doc_id;
            """;
        cmd.ExecuteNonQuery();
    }

    private static void AddColumn(
        SqliteConnection conn, string table, string column, string definition ) {

        using ( var check = conn.CreateCommand() ) {
            check.CommandText =
                "SELECT COUNT(*) FROM pragma_table_info( $table ) WHERE name = $column";
            check.Parameters.AddWithValue( "$table", table );
            check.Parameters.AddWithValue( "$column", column );
            if ( Convert.ToInt64( check.ExecuteScalar() ) > 0 ) return;
        }

        // The table and column names here are literals from Migrate, never input — ALTER TABLE
        // takes no parameters for them.
        using var add = conn.CreateCommand();
        add.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        add.ExecuteNonQuery();
    }
}
