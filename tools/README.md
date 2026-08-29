# tools

Development helpers. Nothing here is referenced by the build or the apps.

## Sample database

`votecheck-sample.db` is a small SQLite mirror seeded from **real**
`api.eduskunta.fi` responses — 44 divisions spanning 2008–2026, 522 MPs,
8,756 individual ballots. It exists so the web app and its queries can be
exercised against realistic data without a full backfill (the complete
archive is 15,562 divisions / ~1.2 GB).

The divisions are **chosen for coverage, not sampled evenly**: every page the
app serves has at least one worked example, and the awkward records upstream
actually contains are present rather than filtered out.

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

### What it covers

| Page / route | What in the sample exercises it |
|---|---|
| `/` (front page) | 41 non-annulled divisions, newest-first across 2008–2026 |
| `/vote/{id}` | every division carries its full ~199-ballot list and party breakdown |
| `/vote/{id}/{party}` | ten distinct party abbreviations (`kok`, `ps`, `sd`, `kesk`, `r`, …) |
| `/mp/{personNumber}` | 326 MPs with 10+ ballots; several with absences, so attendance % is non-trivial |
| `/mps?name=` | 522 MPs to filter by surname |
| `/search?query=` | Finnish subject text — `mietintö` returns 21, `pääluokka` 25 |

Edge cases deliberately included, each found in the real archive:

- **3 annulled divisions** (`cancelled = 1`). The front page and search must
  exclude them while the permalink still resolves — verified: absent from `/`,
  still `200` at `/vote/2009-55-1`.
- **8 divisions from one plenary sitting** (`2023-76-*`), so anything grouping
  by sitting has more than one row to work with.
- **2 divisions whose Speaker `henkilonumero` is `"-"`** (`2019-12-1`,
  `2023-14-1`) — the whole archive contains exactly these two, and they throw
  on a non-nullable int.
- **1 ballot with no party abbreviation** (in `2020-169-10`), the only one in
  15,562 divisions, exercising the unknown-party fallback.
- **267 blank (`Tyhjä`) ballots**, including divisions where blanks are
  numerous, covering the `Tyhjää` → `Tyhjä` normalisation.

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

- MP rows carry each member's **latest** party across the whole 2008–2026 span,
  so a 2008 ballot may show a party the member joined later. Ballot rows keep
  the party held at the time; the `mp` table is a convenience.
- No division from before 2008 — the upstream archive itself starts there.
- The database is in WAL mode, so opening it creates `-wal`/`-shm` side files.

### Running the app against it

Sync is live, so point it somewhere it cannot overwrite the fixture, and give it
an empty upstream window unless you want it topped up from the real API:

```
cp tools/votecheck-sample.db /tmp/votecheck.db
VoteCheck__DbPath=/tmp/votecheck.db VoteCheck__SyncMinYear=9999 \
  dotnet run --project VoteCheckWeb
```
