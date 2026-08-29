#!/usr/bin/env python3
"""Seed a VoteCheck SQLite mirror from real api.eduskunta.fi vote records.

Usage:
    python3 tools/seed-sample-db.py tools/sample-votes.ndjson.gz tools/votecheck-sample.db

Input is NDJSON (one search result per line, gzipped or plain) as returned by the
dataset-export endpoint. To refresh or widen the sample:

    # 1. start an export job (sort is {property, ascending}; `fields` is accepted but IGNORED)
    curl -sL -H 'Content-Type: application/json' \
         -d '{"category":"aanestys","sort":[{"property":"id","ascending":true}]}' \
         https://api.eduskunta.fi/api/v1/search/dataset
    # 2. poll until COMPLETED (~80s), then download .resultUrl
    curl -sL https://api.eduskunta.fi/api/v1/search/dataset/status/<jobId>
    # 3. thin it out - the full export is ~1.2 GB / 15,562 votes, each ~76 KB
    awk 'NR % 620 == 1' full-export.ndjson | gzip -9 > tools/sample-votes.ndjson.gz

Why the dataset endpoint and not /search: /search is hard-capped at
startFromIndex + maxResults <= 10000, and the archive holds more than that.

Schema note (design.md 7, step 3): vote identifiers are TEXT ("2008-103-1"), so the
FTS5 external-content index hangs off an INTEGER surrogate `seq` - FTS5 requires an
integer content_rowid and will not accept the text key.
"""
import gzip, json, sqlite3, sys

SCHEMA = """
PRAGMA journal_mode = WAL;

CREATE TABLE session (
    seq            INTEGER PRIMARY KEY,     -- surrogate rowid, for FTS5 external content
    id             TEXT NOT NULL UNIQUE,    -- aanestystunnus, e.g. "2008-103-1"
    date           TEXT NOT NULL,           -- ISO date; istuntopvm has a UTC offset we trim
    title          TEXT NOT NULL,
    subject        TEXT NOT NULL,
    -- Components of `id`. Ordering by the id string is wrong: "2009-114-4" sorts
    -- before "2009-24-1" but happened five months later.
    vp_year        INTEGER NOT NULL,
    session_number INTEGER NOT NULL,
    vote_number    INTEGER NOT NULL,
    result_yes     INTEGER NOT NULL,
    result_no      INTEGER NOT NULL,
    result_blank   INTEGER NOT NULL,
    result_absent  INTEGER NOT NULL,
    cancelled      INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE mp (
    person_number INTEGER PRIMARY KEY,      -- henkilonumero
    first_name    TEXT NOT NULL,
    last_name     TEXT NOT NULL,
    party         TEXT NOT NULL             -- latest known group abbreviation
);

CREATE TABLE vote (
    session_id    TEXT NOT NULL REFERENCES session(id),
    person_number INTEGER NOT NULL,
    party         TEXT NOT NULL,            -- party at the time of the vote
    vote          TEXT NOT NULL,            -- Jaa | Ei | Tyhjä | Poissa
    PRIMARY KEY ( session_id, person_number )
) WITHOUT ROWID;

CREATE INDEX ix_vote_person ON vote ( person_number, session_id );
CREATE INDEX ix_session_date ON session ( date );
CREATE INDEX ix_session_chrono
    ON session ( vp_year DESC, session_number DESC, vote_number DESC );

CREATE VIRTUAL TABLE session_fts USING fts5 (
    title, subject, content='session', content_rowid='seq'
);

-- History is append-only, so an insert trigger is sufficient.
CREATE TRIGGER session_ai AFTER INSERT ON session BEGIN
    INSERT INTO session_fts ( rowid, title, subject )
    VALUES ( new.seq, new.title, new.subject );
END;

CREATE TABLE sync_state (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);
"""

# The raw API says "Tyhjää"; the domain value is "Tyhjä" (design.md §7 blockers).
NORMALIZE = {"Jaa": "Jaa", "Ei": "Ei", "Tyhjää": "Tyhjä", "Tyhjä": "Tyhjä", "Poissa": "Poissa"}

def as_int(v):
    try:
        return int(v)
    except (TypeError, ValueError):
        return 0

def fi(node):
    return (node or {}).get("fi")

def main(src, dst):
    con = sqlite3.connect(dst)
    con.executescript(SCHEMA)

    mp_latest, sessions, votes, unknown = {}, [], [], set()
    opener = gzip.open if src.endswith(".gz") else open
    for line in opener(src, "rt", encoding="utf-8"):
        a = json.loads(line).get("aanestys")
        if not a:
            continue
        tulos = a.get("aanestystulos") or {}
        date = (a.get("istuntopvm") or "")[:10]      # "2026-06-03+03:00" -> "2026-06-03"
        sessions.append((
            a["id"], date,
            fi(a.get("aanestysotsikko")) or "",
            fi((a.get("kohta") or {}).get("otsikko")) or "",
            as_int(a.get("istuntovpvuosi")), as_int(a.get("istuntonumero")),
            as_int(a.get("aanestysnumero")),
            tulos.get("jaa", 0), tulos.get("ei", 0),
            tulos.get("tyhjia", 0), tulos.get("poissa", 0),
            1 if a.get("aanestysmitatoity") else 0,
        ))
        for b in a.get("aanestystapahtumat") or []:
            raw = fi(b.get("kayttaytyminen"))
            val = NORMALIZE.get((raw or "").strip())
            if val is None:
                unknown.add(raw)
                continue
            pn, party = b["henkilonumero"], fi(b.get("edkryhmalyhenne")) or "?"
            votes.append((a["id"], pn, party, val))
            if pn not in mp_latest or date >= mp_latest[pn][0]:
                mp_latest[pn] = (date, b.get("etunimi") or "", b.get("sukunimi") or "", party)

    con.executemany(
        "INSERT INTO session (id,date,title,subject,vp_year,session_number,vote_number,"
        "result_yes,result_no,result_blank,result_absent,cancelled) "
        "VALUES (?,?,?,?,?,?,?,?,?,?,?,?)", sessions)
    con.executemany(
        "INSERT INTO mp (person_number,first_name,last_name,party) VALUES (?,?,?,?)",
        [(pn, v[1], v[2], v[3]) for pn, v in mp_latest.items()])
    con.executemany(
        "INSERT OR IGNORE INTO vote (session_id,person_number,party,vote) VALUES (?,?,?,?)", votes)
    con.commit()

    print(f"sessions={len(sessions)} mps={len(mp_latest)} ballots={len(votes)}")
    if unknown:
        print("UNKNOWN vote values (skipped):", unknown, file=sys.stderr)
    con.close()

if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
