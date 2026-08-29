# tools

Development helpers. Nothing here is referenced by the build or the apps.

## Sample database

`votecheck-sample.db` is a small SQLite mirror seeded from **real**
`api.eduskunta.fi` responses — 26 divisions spanning 2008–2026, 522 MPs,
5,174 individual ballots. It exists so the web app and its queries can be
exercised against realistic data without a full backfill (the complete
archive is 15,562 votes / ~1.2 GB).

| File | What it is |
|---|---|
| `sample-votes.ndjson.gz` | The source: real vote records, one JSON object per line (137 KB) |
| `seed-sample-db.py` | Builds the `.db` from that NDJSON |
| `votecheck-sample.db` | Generated output, committed for convenience |

Rebuild at any time — output is deterministic:

```
python3 tools/seed-sample-db.py tools/sample-votes.ndjson.gz tools/votecheck-sample.db
```

Point the web app at it with `VoteCheck:DbPath`.

### Schema differs from `VoteCheckWeb/Data/Db.cs` — deliberately

This uses the **migrated** schema from design.md §7 step 3, which the app has
not adopted yet:

- `session.id` is `TEXT` (`"2008-103-1"`), not `INTEGER` — the new API's vote
  identifiers are strings, so the legacy `AanestysId` integer key cannot hold them.
- Because FTS5 external-content tables require an integer `content_rowid`, the
  search index hangs off a surrogate `session.seq INTEGER PRIMARY KEY`, with
  `id TEXT UNIQUE` beside it.
- Vote values are normalised to `Jaa | Ei | Tyhjä | Poissa`; the raw API emits
  `Tyhjää` (34 occurrences in this sample alone).

Treat this file as the reference implementation of that migration.

### Refreshing the sample

See the docstring in `seed-sample-db.py`. In short: `/search` cannot enumerate
the archive (hard cap of `startFromIndex + maxResults <= 10000`), so the sample
comes from the async `/search/dataset` export job.

Two upstream quirks worth remembering, both found the hard way:
`sort` entries are `{property, ascending}`, and the `fields` projection is
accepted but silently ignored — expect full ~76 KB records regardless.

### Known gaps

- The sample is evenly spread (`every 620th vote`), so **no two divisions share
  a plenary session** — it will not exercise "all votes in one session" queries.
- The database is in WAL mode, so opening it creates `-wal`/`-shm` side files.
