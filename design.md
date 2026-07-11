# VoteCheck — Design Document

## 1. Purpose & Vision

VoteCheck gives the general public a fast, on-the-go tool to fact-check Finnish
politicians: did a representative actually vote the way they claim, and do they
keep their word? It does this by surfacing the official voting records of the
Finnish Parliament (Eduskunta) from the [Open Data API](https://avoindata.eduskunta.fi/)
in a form an ordinary citizen can navigate in seconds — no SQL, no CSV dumps,
no reading plenary minutes.

### Target users

- **Citizens / voters** — "How did my MP vote on X?" during a news cycle or
  before an election.
- **Journalists & fact-checkers** — quick verification of claims made in
  interviews or on social media.
- **Researchers / activists** — drill-down from a topic to party positions to
  individual votes.

### Core use cases

1. Check the result of a vote on a given issue.
2. See what a specific representative has been voting recently.
3. See a vote's distribution by party.
4. Drill-down: topic → party distribution → individual MP votes within a party.

## 2. Current Architecture

```
┌───────────────────────────────────────────────┐
│ VoteCheckGUI (Avalonia 11, .NET 8)            │
│  MainWindow: search UI, DataGrid, drill-down  │
│  navigation with back-stack                   │
└───────────────┬───────────────────────────────┘
                │ static method calls, DataTable results
┌───────────────▼───────────────────────────────┐
│ VoteCollector (class library)                 │
│  OpenDataRetriever (static): URL building,    │
│  HTTP GET, JSON → DataTable, column cleanup,  │
│  client-side filtering                        │
└───────────────┬───────────────────────────────┘
                │ HTTPS (System.Net.Http.HttpClient)
┌───────────────▼───────────────────────────────┐
│ avoindata.eduskunta.fi  REST API              │
│  /api/v1/tables/{table}/rows                  │
└───────────────────────────────────────────────┘
```

### Projects

| Project | Role |
|---|---|
| `VoteCollector` | Data-access layer. Queries the Open Data REST API, parses JSON with Newtonsoft.Json, returns `System.Data.DataTable`. |
| `WPFGUI` (assembly `VoteCheckGUI`) | Avalonia desktop UI. Code-behind pattern; all logic in `MainWindow.xaml.cs`. |
| `VoteCollectorTests` | MSTest unit tests for `VoteCollector` (HttpClient substituted via reflection). |

### Data source tables

| Table | Contents | Used by |
|---|---|---|
| `SaliDBAanestys` | Voting sessions (subject, date, result) | `GetVotingData`, `GetVotingDataByDate`, `GetVotingDataOfOne` |
| `SaliDBAanestysEdustaja` | Individual MP votes per session | `GetEdustajaData`, `GetCombinedData` |
| `SaliDBAanestysJakauma` | Party-level distribution per session | `GetPartyDistData` |
| `SeatingOfParliament` | Currently seated MPs | `GetCurrentMPs` |

Vote values: `Jaa` (Yes), `Ei` (No), `Tyhjä` (Abstain), `Poissa` (Absent).

### Key design decisions (as-built)

- **`DataTable` as the universal data contract.** Chosen for direct DataGrid
  binding and schema flexibility (the API defines columns dynamically). Trade-off:
  no compile-time typing; column indices are hard-coded in `GetCombinedData`.
- **Static data-access class.** `OpenDataRetriever` is static with shared
  mutable state (`hasMore`, `finalTable`, `baseUrl`). Simple, but not
  thread-safe and awkward to test (reflection needed to inject `HttpClient`).
- **Sync-over-async.** Public methods block on `GetDataAsync(...).GetAwaiter().GetResult()`;
  the GUI wraps calls in `Task.Run` to stay responsive.
- **Client-side filtering** where the API can't filter server-side
  (e.g. `IstuntoPvm` is typed `OTHER`, so date search filters a year query
  locally; party filter on MP votes is also local).
- **No persistence or caching.** Every view is a live API round-trip.
- **Column curation in the data layer.** `GetVotingData` removes ~20 raw
  columns and reorders the rest so the GUI can bind the table as-is.

## 3. UI Flow

```
Search (surname | date | current MPs)
   └─ vote/session list  ── double-tap row ──▶ party distribution
                                                 └─ double-tap party ──▶ MP votes in party
   ◀───────────────────────── Back button (navigation history stack)
```

Supporting features: query-count limit (default 50), "Today" shortcut,
Swedish party-name toggle, winning-vote bolding, "scroll down for more" status
when `hasMore` is set.

## 4. Known Limitations

These shape the roadmap below.

1. **No promise-vs-vote linkage.** The app shows *votes*, but fact-checking
   "do they keep their word" requires connecting votes to statements/promises.
   Today the user must do that mentally.
2. **Search is exact-prefix / exact-match.** Surname search requires the exact
   surname; there is no topic/keyword search over vote subjects
   (`GetSubjectData` is a stub).
3. **Not "on-the-go."** A desktop app doesn't serve the stated mobile,
   spur-of-the-moment audience. No web or mobile presence.
4. **Performance / API load.** `GetCombinedData` issues one HTTP request per
   result row (N+1); no caching layer; date search over-fetches a whole year.
5. **Fragile data contracts.** Hard-coded column ordinals (`row[7] = votingDataRow[12]`)
   break silently if the API adds/moves columns.
6. **Static shared state** in `OpenDataRetriever` prevents concurrent queries
   and complicates testing.
7. **Finnish-only UX** aside from the Swedish party-name toggle; no English.

## 5. Roadmap

### Near-term (foundation)

- Introduce a typed model layer (records for Vote, VotingSession, MP,
  PartyDistribution) mapped by column *name*, replacing ordinal indexing.
- Make `OpenDataRetriever` an instance class with injected `HttpClient`
  (`IHttpClientFactory`-ready), truly async public API (`Task<T>`), no static
  mutable state.
- Add a local cache (SQLite or file-based) for immutable historical votes —
  past votes never change, so they cache forever. See §5.1 for measured sizing.

### Mid-term (product)

- Topic/keyword search over vote subjects (`KohtaOtsikko` etc.), fuzzy surname
  matching, and an MP profile view (photo, party, attendance rate, recent votes).
- Batch/parallelize the N+1 queries in `GetCombinedData`.

### 5.1 Vote-cache sizing (measured 2026-07)

Probed directly against the live API: the dataset contains **~43,500 voting
sessions** (`SaliDBAanestys`) and **~8.66 million individual MP votes**
(`SaliDBAanestysEdustaja`, ≈ 43.5k sessions × ~199 voters). Since a vote is
one of four values (Jaa/Ei/Tyhjä/Poissa), it packs into 2 bits:

| Layer | Naive SQLite | Packed |
|---|---|---|
| 8.66M individual votes | ~150–200 MB | ~2–5 MB (2-bit codes in a per-session blob + MP-roster mapping per parliamentary term) |
| 43.5k session records (Finnish titles dominate) | ~40–50 MB | same; compresses ~4–5× for transfer |
| FTS5 full-text index on titles (enables topic search) | — | ~40–60 MB on disk |
| MP roster | negligible | negligible |

Result: **~15–25 MB compressed download, ~60–100 MB on disk** including a
full-text search index. Optionally bundle only the current + previous
parliamentary term and lazy-load older history to shrink further.

Cache properties:

- **Immutable + append-only.** Past votes never change; the snapshot never
  invalidates. Delta sync fetches only sessions newer than the local maximum —
  a plenary day adds a few dozen votes (kilobytes).
- **Eliminates the N+1 problem** in `GetCombinedData` and enables instant
  topic search and offline browsing.
- **Dual-purpose:** the same packed SQLite snapshot is the server-side cache
  for the website and the bundled database for the mobile app.

### 5.2 Client strategy: web first, then mobile

Fact-checking is a *sharing* activity — the moment of value is posting a
permalink ("here's the receipt") that opens instantly for people who will
never install an app. An app-store install is exactly the friction that kills
spur-of-the-moment use. Therefore:

1. **Web first** — reach, shareable permalinks, SEO (MP profile pages should
   rank for "how did [MP] vote"). See §7.
2. **Mobile app second** — for the engaged users the website attracts. Wins
   what the web can't: bundled offline cache (§5.1), push notifications
   ("your MP just voted on X"), home-screen retention. Avalonia 11 targets
   iOS and Android, so the existing UI stack and `VoteCollector` core carry
   over without a rewrite.
3. The desktop app remains a third client of the same core.

### Long-term (vision)

- **Web spinoff** with politician profile pages — latest votes, attendance,
  party-loyalty score, shareable permalinks to individual votes. Detailed in §7.
- Promise tracking: curated or crowd-sourced database of public statements
  linked to relevant votes ("said X on date Y — voted Z on date W").

## 6. Testing Strategy

- Unit tests (MSTest) for the data layer with mocked HTTP responses
  (`JSONSamples/` holds real API response samples).
- Gap: no GUI tests; no integration tests against the live API (worth one
  smoke test since the API schema can drift — see Limitation 5).

## 7. Web Version — Planned Architecture

### Goals

- Shareable, fast-loading permalinks: `/mp/{id}`, `/vote/{id}`,
  `/vote/{id}/party/{abbr}` — the atomic units of fact-checking.
- MP profile pages: photo, party, latest votes, attendance rate,
  party-loyalty score.
- Topic search over vote subjects.
- Near-zero operating cost; one-person maintainable.

### Recommended stack

| Concern | Choice | Rationale |
|---|---|---|
| Backend | ASP.NET Core Minimal API (.NET 8) | Reuses `VoteCollector` core and existing C# skills; one runtime across all clients |
| Pages | Server-side rendered (Razor Pages or Blazor SSR, no WebAssembly) | Permalinks must render instantly and be crawlable/SEO-visible; no SPA needed for read-mostly content |
| Storage | Single SQLite file + FTS5 (§5.1) | Read-heavy with exactly one writer (the sync job) — SQLite's ideal case; no DB server to operate |
| Ingest | `BackgroundService` polling avoindata.eduskunta.fi for new sessions (delta sync) | Data changes only on plenary days; append-only |
| Caching | ASP.NET output caching; historical pages effectively immutable | Past votes never change → cache aggressively, CDN-friendly |
| Deployment | One container on a small VPS or Azure App Service | Whole product is one process + one file; scale is bounded by Finland's population of politics-followers |

### Shape

```
avoindata.eduskunta.fi ──▶ Sync BackgroundService ──▶ votecheck.db (SQLite + FTS5)
                                                          │
                        Razor/Blazor SSR pages ◀── Minimal API (ASP.NET Core)
                        /mp/{id}, /vote/{id},             ▲
                        /search?q=...                     │
                        + /api/v1/... (JSON)  ◀── mobile & desktop clients later
```

The JSON API is the same surface the future mobile app consumes, so the web
version *is* the backend extraction step — no throwaway work.

### Non-goals (v1)

- User accounts, comments, or crowd-sourcing (moderation cost; add only with
  promise-tracking later).
- Real-time updates during a plenary session (poll interval of minutes is fine).

### Status (2026-07-12)

Scaffolded and smoke-tested against live data: sync imports Finnish-only
sessions (`KieliId = 1` — the API stores a Swedish duplicate of every vote
under the adjacent `AanestysId`), vote values are trimmed and normalized to
`Jaa | Ei | Tyhjä | Poissa` (source data says `Tyhjää`), and search uses
per-word FTS5 prefix matching to handle Finnish compound words.

## 8. Next Steps

In priority order; each step is independently shippable.

1. **Commit the scaffold and update the README.** Branch + PR with
   `VoteCheckWeb/`, `design.md`, and the `.gitignore` entry. Document the
   web project in the README (run instructions, config keys `VoteCheck:DbPath`,
   `SyncMinYear`, `SyncPollMinutes`) and correct the vote-value note
   (`Tyhjää`, not `Tyhjä`, in the raw API).

2. **Complete and verify the full backfill.** Let the sync run to the end of
   the 2023+ range and cross-check session/vote counts against the API.
   Harden the sync while watching it: retry with backoff on transient HTTP
   failures, small delay between requests (be a polite API citizen), and log
   a clear "backfill complete" marker. Acceptance: fresh database reaches
   steady state unattended; restart resumes from the page cursor without
   duplicates or gaps.

3. **Tests for VoteCheckWeb.** Unit tests for the pieces that guard
   correctness: `NormalizeVote`, the `KieliId` filter, FTS query building,
   and `Queries` against an in-memory SQLite database seeded with known rows
   (party sums, attendance %, search). These encode the two data bugs found
   in smoke testing so they can't regress.

4. **Make permalinks shareable-grade.** OpenGraph/Twitter-card meta tags on
   `/vote/{id}` and `/mp/{id}` (title = vote subject + result, e.g.
   "Jaa 107 – Ei 81") so links unfurl correctly in social feeds; canonical
   URLs; `<title>`/meta description per page. This is the product's core
   distribution mechanism — do it before driving any traffic.

5. **Deploy.** Dockerfile (multi-stage build, volume for `votecheck.db`),
   a small VPS or Azure App Service behind HTTPS (Caddy or App Service TLS),
   and a domain. Database needs no backup discipline — it can be rebuilt
   from the API — but persist it across restarts to avoid re-backfilling.
   Acceptance: a shared `/vote/{id}` link opens publicly in under a second.
