# VoteCheck — Design & Technical Roadmap

*Last updated: 2026-08-29*

> ⚠️ **Upstream API migration is already underway — target the new API now, not later.**
> The legacy table API this project uses (`avoindata.eduskunta.fi/api/v1/tables/...`) is being
> retired: `avoindata.eduskunta.fi` has been redirecting to the new service's open data since
> **30 March 2026**, and the legacy service is scheduled for full **discontinuation at the end of
> 2026** — about five months out as of this writing. Its replacement is live today at
> **`api.eduskunta.fi`**: a modern, documented, unauthenticated JSON API with a published
> [OpenAPI 3.0 spec](https://api.eduskunta.fi/openapi.json). Full endpoint map in §3.1.
>
> **This changes the roadmap:** there is no longer a reason to build `VoteCheck.Core` against the
> legacy table/`DataTable` shape and swap later — build directly against `api.eduskunta.fi` from
> Step 1. Sections below have been updated accordingly.

## 1. Purpose

VoteCheck lets anyone check what Finnish MPs (kansanedustajat) have been voting on, using the
Finnish Parliament Open Data API — currently the legacy
[avoindata.eduskunta.fi](https://avoindata.eduskunta.fi/) table API, migrating to the new
[api.eduskunta.fi](https://api.eduskunta.fi/) (see banner above and §3.1). The long-term goal is
an **easy-to-use activity checker for Finnish political representatives, usable from a browser
and as a mobile-installable app** — not just a desktop program.

Typical user questions the product should answer in a few taps:

- *What has my MP been voting on lately?*
- *How did the parliament / each party vote on issue X?*
- *How active is an MP?* (attendance, absences, blank votes)
- *Who inside a party broke ranks on a vote?*

## 2. Current State (as-is)

```
┌────────────────────┐      ┌─────────────────────┐      ┌─────────────────────────┐
│  WPFGUI (Avalonia) │ ───▶ │  VoteCollector      │ ───▶ │ avoindata.eduskunta.fi  │
│  desktop XAML app  │      │  static class,      │      │ REST API (JSON tables)  │
│  code-behind UI    │      │  DataTable results  │      │                         │
└────────────────────┘      └─────────────────────┘      └─────────────────────────┘
```

| Component | Notes |
|-----------|-------|
| `VoteCollector` | Single static class `OpenDataRetriever` (~660 lines). Synchronous wrappers over `HttpClient`, returns `System.Data.DataTable`, shared mutable static state (`hasMore`, `baseUrl`, `finalTable`), party mapping hard-coded from `Parties.txt`. |
| `WPFGUI` (`VoteCheckGUI`) | Avalonia 11 desktop app, logic in code-behind (`MainWindow.xaml.cs`), drill-down navigation vote → party distribution → individual MPs. |
| `VoteCollectorTests` | MSTest tests; mock `HttpClient` injected via reflection. |

### Constraints of the current design

1. **Desktop-only reach.** Users must install .NET and run a desktop binary; there is no URL to share.
2. **Data layer is not reusable as-is.** Static state and `DataTable` returns make it hard to host
   behind a web server (no thread safety, no async, no typed contracts for JSON serialization).
3. **Every query hits the upstream API live.** No caching layer; historical voting data is
   immutable and ideal for caching, but nothing exploits that.
4. **Finnish-only column names and raw table semantics leak to the UI** (e.g. `SaliDBAanestys`
   column names shown directly in grids).
5. **Built against the legacy API**, which is already being redirected away from and will be shut
   down by end of 2026 (see banner above) — reason enough to target the replacement directly
   rather than invest further in the current shape.

## 3. Target Architecture (to-be)

```
api.eduskunta.fi ────▶ VoteCheck.Core ────▶ sync ────▶ votecheck.db
/api/v1/... (new,      EduskuntaClient +    (Background    (SQLite + FTS5
unauthenticated JSON)  typed models +        Service)       mirror; disposable,
                       caching decorator                    rebuildable)
                              │                                   │
                              ▼                                   ▼
                                                          VoteCheckWeb
                                                          Razor SSR pages +
                                                          /api/v1 JSON (Swagger,
                                                          ?lang, CORS, /health)
                                                          shareable permalinks
                                                                  │
                                                                  ▼
                                                          Browser (crawlable,
                                                          no JS required)
```

Principles:

- **One core library, many frontends.** `VoteCheck.Core` (evolved from `VoteCollector`) holds all
  Eduskunta API access, typed domain models (`Mp`, `VotingSession`, `Vote`, `PartyDistribution`,
  `MpActivitySummary`) and business logic. The web API, the browser UI, and the existing desktop
  app all consume it.
- **API in the middle.** The browser talks to *our* API, never to api.eduskunta.fi directly. This
  lets us cache aggressively (immutable historical votes), shape friendly JSON, add computed
  endpoints (activity summaries), and stay comfortably within upstream's rate limit (see §3.1).
- **SSR first, installable app later** *(decision 2026-08-29, revised from "PWA instead of
  separate native apps")*. Server-rendered Razor pages give crawlable, instantly-rendering
  permalinks — the atomic unit of fact-checking that gets shared, and the product's distribution
  mechanism. A PWA/mobile client can still come later over `VoteCheckWeb`'s `/api/v1`
  JSON without re-architecting.
- **Mirror, don't proxy, for the web frontend.** Upstream payloads are heavy (~75 KB per vote,
  ~750 KB for ten recent votes) and `/search*` is rate-capped (450 POST/3000s/IP), so
  `VoteCheckWeb` serves from a local SQLite + FTS5 mirror fed by a sync service, projecting each
  page down to what it needs. The mirror is disposable — gitignored and rebuildable from the API.
- **Public data, no accounts.** No authentication needed for v1 — matches upstream, which is
  itself fully open (no API key or token).

### 3.1 New upstream API — `api.eduskunta.fi`

Confirmed live and documented (via local research against the published spec):

| | |
|---|---|
| Base URL | `https://api.eduskunta.fi/api/v1/` |
| OpenAPI spec | [`https://api.eduskunta.fi/openapi.json`](https://api.eduskunta.fi/openapi.json) (OpenAPI 3.0.0) |
| Interactive docs | [`https://api.eduskunta.fi/`](https://api.eduskunta.fi/) (JS-rendered explorer) |
| Auth | None — no API key or token in the spec |
| Rate limit | 450 POST requests / 3000 seconds / IP (per spec description; applies to `/search*`) |
| Formats | JSON (primary); `.../xml` variants return raw XML; `/search/dataset` bulk export returns NDJSON; document/attachment endpoints `302`-redirect to the file |

Endpoint map (all relative to the base URL):

| Area | Endpoints |
|------|-----------|
| MPs | `GET /kansanedustajat`, `GET /kansanedustajat/{id}` (by `henkilonumero`) |
| Votes | `GET /taysistunnot/aanestykset/{aanestystunnus}` (single vote), `GET /taysistunnot/istunnon-aanestykset/{istuntotunnus}` (all votes in a plenary session), `GET /taysistunnot/asian-aanestykset/{eduskuntatunnus}` (all votes on a matter), `GET /taysistunnot/uusimmat-aanestykset` (recent votes) |
| Matters / documents | `GET /valtiopaivaasiat/{eduskuntatunnus}` (+ `/xml`), `GET /asiakirjat/edktunnus/{edktunnus}` (+ `/html`, `/pdf`, `/xml`) |
| Plenary sessions | `GET /taysistunnot/poytakirja-asiakohdat/{eduskuntatunnus}/html` |
| Search | `POST /search`, `GET /search?q=`, `POST /search/count`, `POST /search/dataset` (async bulk export job) |
| Reference data | `/reference-data/eduskuntaryhmat`, `/vaalipiirit`, `/sukupuolet`, `/valiokunnat`, `/asiatyypit`, `/valtiopaivat`, `/vaalikaudet`, `/kansanedustajat`, etc. |

Notable shape details that affect our design — **confirmed against real captured responses**
(kept as fixtures in `VoteCheck.Core.Tests/Fixtures/` and asserted by the tests there):

- A vote (`Aanestys`) comes back with `aanestystulos` (jaa/ei/tyhjia/poissa/yhteensä tally),
  plus **pre-computed breakdowns** — `eduskuntaryhmaJakaumat` (by party),
  `hallitusoppositioJakaumat` (government/opposition, as "Hallitusryhmät"/"Oppositioryhmät"),
  `vaalipiiriJakaumat` (by electoral district) — so our "party distribution" and
  "government vs. opposition" views are a thin reshaping of upstream data, not custom aggregation.
- **Every vote payload embeds the complete ballot list** (`aanestystapahtumat`, one entry per
  seat — 199 in the captured sample), and `uusimmat-aanestykset` returns those full objects too.
  **This resolves the earlier open question:** per-MP vote history and activity summaries can be
  derived by filtering ballots by `henkilonumero`, with no dedicated per-MP endpoint and no
  indexing needed for the recent-votes window. Only *deep historical* per-MP queries would need
  our own index, since walking every past session would be expensive.
- **Many fields are bilingual objects, not strings** — including ones that look scalar:
  `kayttaytyminen` (the vote itself) is `{fi:"Jaa", sv:"Ja"}`, and so are `edkryhmalyhenne`
  (`{fi:"kok", sv:"saml"}`), `eduskuntaryhma`, `vaalipiiri`, `sukupuoli`, and every jakauma
  `nimi`. MP payloads add an `en` key on some fields. The desktop app's Swedish toggle therefore
  becomes "pick the language key" rather than a name-mapping table.
- `aanestysotsikko` describes the *ballot options* ("proposal X JAA / proposal Y EI"), not the
  subject. The human-readable subject of the vote is **`kohta.otsikko`**, with the originating
  document id in `kohta.asiakirjat.paaasiakirjaEduskuntatunnus` (e.g. "HE 32/2026 vp") — that id
  is the key for `/valtiopaivaasiat` and `/taysistunnot/asian-aanestykset`.
- **`uusimmat-aanestykset` returns a nested array** (`[[vote], [vote], …]`), unlike the other
  vote endpoints; `EduskuntaClient` flattens it. It is also **not chronologically ordered** —
  the captured sample runs sessions 60, 65, 69, 71, 71, 71, 71, 58, 69, 71 — so anything
  presenting "latest votes" must sort explicitly rather than trusting upstream order.
- Numeric ids (`henkilonro`, `henkilonumero`) and `istuntovpvuosi`/`istuntonumero`/
  `aanestysnumero` arrive as JSON **strings**. Timestamps are ISO 8601 with offset, except
  `istuntopvm`, which is a date with offset (`"2026-06-03+03:00"`) and does not parse as a
  `DateTimeOffset` — kept as a string in the models.
- The Speaker (`puhemies`) does not vote and is absent from `aanestystapahtumat` — presiding
  must not be counted as an absence when computing attendance.

### Candidate API surface (v1) — our own API, backed by the above

| Endpoint | Purpose | Backed by upstream |
|----------|---------|---------------------|
| `GET /api/mps` | Current MPs (name, party, constituency) | `GET /kansanedustajat` |
| `GET /api/mps/{id}/votes?count=50` | An MP's recent votes with issue titles | `uusimmat-aanestykset` / session vote endpoints, filtering the embedded `aanestystapahtumat` by `henkilonumero`; titles from `kohta.otsikko` |
| `GET /api/mps/{id}/activity` | Computed summary: attendance %, Jaa/Ei/Tyhjä/Poissa breakdown | derived from the above |
| `GET /api/votes?date=yyyy-MM-dd` | Voting sessions by date prefix | `GET /taysistunnot/uusimmat-aanestykset` / session lookups |
| `GET /api/votes/{id}` | One division with its tally | `GET /taysistunnot/aanestykset/{aanestystunnus}` |
| `GET /api/votes/{id}/distribution` | Party, government/opposition and district breakdowns | the three `*Jakaumat` arrays on the same endpoint |
| `GET /api/votes/{id}/ballots?party=kok` | Individual MP votes for a division | `aanestystapahtumat` on the same endpoint, filtered |
| `GET /api/sessions/{id}/votes` | All divisions in one plenary session | `GET /taysistunnot/istunnon-aanestykset/{istuntotunnus}` |

All of the above are implemented and take `?lang=fi|sv|en`. Ballots are deliberately a separate
call from `distribution`, so a client showing only the party split never pays for 199 rows.

## 4. Roadmap — Next 3 Steps

### Step 1 — Build `VoteCheck.Core` directly against `api.eduskunta.fi` (foundation)

*Goal: a thread-safe, async, typed core library targeting the **new** API from day one — no
detour through the legacy table shape, since it has only months of runway left (§banner, §3.1).*

> **Status: essentially complete.** `VoteCheck.Core` now contains:
> `IEduskuntaClient`/`EduskuntaClient` over `kansanedustajat` and the
> `taysistunnot/*aanestykset*` endpoints; typed models replacing `DataTable`;
> `CachingEduskuntaClient`, an `IMemoryCache` decorator with split TTLs and single-flight
> de-duplication; and `MpActivityService`, which derives per-MP vote history and activity
> summaries from the embedded ballots. Real captured responses are committed as fixtures under
> `VoteCheck.Core.Tests/Fixtures/` and asserted against, which caught two things the documented
> research missed: most "scalar" ballot fields are bilingual objects, and `uusimmat-aanestykset`
> is a nested array. Whole solution builds; all tests pass (55 new + 71 legacy).
>
> **Deferred, deliberately:** the endpoints still unwrapped (matters, documents, `/search`,
> reference data). Having already shipped two wrong shapes from documentation alone, these
> should not be modeled until a live response for each has been captured — see §6.

- New `EduskuntaClient` (instance-based, `HttpClient` via constructor injection — enables
  `IHttpClientFactory` and clean test mocks, no reflection hacks) wrapping the `api.eduskunta.fi`
  endpoints in §3.1: `kansanedustajat`, the `taysistunnot/*aanestykset*` family, and the
  `reference-data` lookups needed for party/electoral-district names.
- Model the JSON responses directly as typed records (`Mp`, `VotingSession`/`Aanestys`, `Vote`,
  `PartyDistribution`) — no `DataTable` at all; retire that concept rather than adapting it.
  `OpenDataRetriever`/`VoteCollector` (legacy table API) can stay as-is behind a feature flag only
  as a fallback until the new client is verified, then be deleted — not maintained long-term.
- ~~Resolve the open question from §3.1: how to get "an MP's recent votes".~~ **Done** — every
  vote payload embeds the full ballot list, so this is a filter over `aanestystapahtumat`, not a
  separate lookup or index.
- All I/O `async`/`await` end-to-end.
- In-memory cache (`IMemoryCache`) — **done**, as `CachingEduskuntaClient`, a decorator rather
  than logic baked into the client. Completed votes and session votes get a long TTL (12 h) since
  they're immutable; MPs, matter votes and `uusimmat-aanestykset` get a short one (10 min).
  Concurrent callers for the same uncached key collapse onto one upstream fetch, so a traffic
  burst can't fan out into duplicate requests. Null results are not cached — a 404 today may be
  a real record tomorrow.
- Per-MP derivation — **done**, as `MpActivityService`: `ExtractVotesFor` pulls an MP's ballot
  out of each division (subject from `kohta.otsikko`, not `aanestysotsikko`), and `Summarize`
  produces the Jaa/Ei/Tyhjä/Poissa counts and attendance rate behind the Step 2 activity
  endpoint. Annulled divisions are excluded; an empty window reports a null attendance rate
  rather than 0%, so "no data" stays distinguishable from "never showed up".
- New MSTest coverage against the new client (mocked `HttpClient`, no reflection); port over the
  useful existing test cases (pagination-style behavior, party-code mapping) adapted to the new
  shapes.

*Done when:* `EduskuntaClient` can fetch an MP, a vote with its party/government-opposition
breakdown, and recent votes, entirely from `api.eduskunta.fi`, with tests passing on mocked HTTP.

### Step 2 — Stand up a JSON API (ASP.NET Core minimal API)

*Goal: the data is reachable from any browser via clean JSON endpoints.*

> **Status: delivered, then folded into `VoteCheckWeb` (§7 step 6, 2026-08-29).** The
> standalone `VoteCheck.Api` project no longer exists; its JSON surface, Swagger UI,
> `?lang` resolution, CORS configuration and `/health` probe now live in `VoteCheckWeb`
> and read the local mirror instead of upstream. History below kept for context.
>
> **Original status: built and running locally.** `VoteCheck.Api` served nine v1 routes, with
> Swagger UI at `/swagger` and a `/health` probe. `EduskuntaClient` is registered via
> `IHttpClientFactory` and wrapped in `CachingEduskuntaClient`; output caching sits in front
> with matching immutable/volatile policies. Verified by booting the app, not only by tests.
>
> Two things worth knowing:
> - **Language is a query parameter** (`?lang=fi|sv|en`, default `fi`). Since upstream returns
>   bilingual objects everywhere, resolving them server-side keeps payloads small and replaces
>   the desktop app's Swedish toggle. An unsupported value is a 400, not a silent fallback.
> - **Upstream failures map to 502/504**, not 500. Everything here comes from
>   api.eduskunta.fi, so that service being unreachable is a normal condition and shouldn't
>   read as a fault in VoteCheck.
>
> Not done: actually deploying it. The Dockerfile and CI workflow are in place, but choosing a
> host and pushing an image needs credentials, so a public URL is still outstanding.

- New project `VoteCheck.Api` referencing `VoteCheck.Core`; implement the v1 endpoints above.
- Add the first *computed* endpoint, `GET /api/mps/{id}/activity`, aggregating attendance and
  vote-type distribution across an MP's votes — this is the "activity checker" differentiator
  over raw open data (note: per-vote party/government-opposition breakdowns are already provided
  upstream, so this endpoint's real work is the *cross-vote, per-MP* rollup, not per-vote tallying).
- Response caching + output caching middleware; CORS enabled for the future frontend origin;
  OpenAPI/Swagger UI for discoverability. **Done** — output caching uses the same
  immutable/volatile split as the client cache. CORS origins come from configuration
  (`VoteCheck:AllowedOrigins`) and default to *none* rather than `*`, so a deployment has to
  name the PWA's origin deliberately.
- Containerize (Dockerfile) and deploy a public instance (Azure App Service free tier, Fly.io, or
  similar); add a GitHub Actions workflow for build + test + deploy. **Partly done** —
  Dockerfile (multi-stage, non-root) and a CI workflow that builds, tests and verifies the image
  both exist. CI runs entirely against committed fixtures, so it never calls upstream and can't
  be broken by it. Choosing a host and pushing an image needs credentials and is left open.

*Done when:* `curl https://<host>/api/mps` returns live data and Swagger UI documents the API.
*Currently:* both work locally; the public `<host>` is the remaining piece.

### Step 3 — Ship the web frontend (revised: SSR over the mirror, not WASM)

*Goal: a shareable URL that works on phone and desktop.*

> **Status: built on `feature/web`, pending convergence (§7).** A Razor Pages SSR app,
> `VoteCheckWeb`, exists with the v1 screens working against live data: latest votes,
> vote drill-down to party distribution and individual ballots, MP search, MP profile,
> and FTS5 topic search — plus its own `/api/v1` JSON surface. It ingests from the
> **legacy** API, which is what §7 fixes.

**Decision (2026-08-29): SSR + SQLite mirror, not the Blazor-WASM PWA sketched earlier.**
Reasons, in order of weight:

1. Permalinks (`/vote/{id}`, `/mp/{id}`) must render instantly and be crawlable/SEO-visible —
   a WASM app can do neither without a prerendering layer that is itself SSR.
2. Payload economics: fetching ~75 KB per vote into the browser to show a tally is waste;
   the mirror projects server-side (see §3 principles).
3. Upstream `/search*` rate caps make a local FTS5 index the safer search backend.

The WASM/PWA route is *deferred, not rejected* — `VoteCheckWeb`'s `/api/v1` remains the
JSON surface a future installable client would consume (§5), documented at `/swagger` and
CORS-capable for a separate origin.

Still to do on the frontend itself (§7 steps 5–7): tests, OpenGraph/canonical tags for link
unfurling, a `wwwroot` for a favicon and robots.txt, localization
scaffold (Finnish first; the bilingual `LocalizedText` fields make Swedish nearly free).

*Done when:* a public URL serves the SSR pages from a database synced via `VoteCheck.Core`,
and an MP's recent votes can be found on a phone in under three taps.

## 5. Later (beyond the next 3 steps)

- **Topic search:** the new API's `/search` (fuzzy, cross-entity: MPs, matters, docs, speeches,
  votes) may cover this natively — evaluate before building a custom indexed store.
- **Notifications:** "follow an MP" with web push when they vote (requires a scheduled fetcher
  and a persistence layer — first real database need).
- **Charts:** party-line cohesion, MP attendance trends over an electoral term.
- **Historical MP data:** extend beyond `SeatingOfParliament` (current term) to past terms.
- **Retire or slim the desktop app** once the PWA reaches feature parity; Avalonia project can
  remain as a thin shell over the same core.

## 6. Risks & Open Questions

| Risk | Mitigation |
|------|------------|
| **Legacy API shutdown (end of 2026, already redirecting since 30 Mar 2026)** — resolved by targeting `api.eduskunta.fi` directly in Step 1 (see banner, §3.1) instead of the legacy table API | No further mitigation needed beyond following the updated Step 1 plan; keep `OpenDataRetriever` only as a short-lived fallback, not a long-term dependency; **note `VoteCheckWeb`'s sync still ingests from the legacy API** until §7 step 4 lands — that is the single most time-sensitive item in the repo |
| ~~No confirmed endpoint for "all votes by one MP"~~ — **resolved**: ballots are embedded in every vote payload (§3.1) | Recent-window per-MP history is a client-side filter. Still open at larger scale: *deep historical* per-MP queries would mean walking every past session, so a per-MP index becomes worthwhile if we go beyond recent votes |
| **Payload size** — each vote carries ~199 ballots plus three breakdown sets (~75 KB per vote); `uusimmat-aanestykset` returned ~750 KB for 10 votes | Our API should project down to what each view needs rather than proxying upstream objects; cache parsed results, and avoid fetching full vote objects when only tallies are shown |
| Upstream API rate limits / availability | `/search*` is capped at 450 POST/3000s/IP per the spec. `CachingEduskuntaClient` now covers this: immutable data cached 12 h, volatile 10 min, and concurrent callers for one key share a single fetch |
| **Remaining endpoints modeled from documentation alone would likely be wrong again** — two of the shapes derived that way (bilingual ballot fields, nested recent-votes array) turned out incorrect when checked | Capture a live response for each of `/valtiopaivaasiat`, `/asiakirjat`, `/search` and `/reference-data/*` before modeling them, and commit it as a fixture like the existing three |
| Upstream schema changes | Real captured responses are committed as fixtures in `VoteCheck.Core.Tests/Fixtures/` and asserted by shape tests, so a breaking change surfaces as a test failure; refresh fixtures periodically since they're a point-in-time snapshot |
| MP identity across terms (`henkilonumero` continuity, legacy `EdustajaId`/`EdustajaHenkiloNumero`) | Decide canonical ID (`henkilonumero`, per the new API) in Step 1 model design |
| Hosting cost for a hobby project | Free tiers + output caching keep compute minimal; static PWA assets are nearly free to serve |
## 7. Convergence Plan — `VoteCheckWeb` onto `VoteCheck.Core`

*Written 2026-08-28, revised 2026-08-29 after Step 1 landed.* `VoteCheckWeb` (§4 Step 3) and
`VoteCheck.Core`/`VoteCheck.Api` grew on parallel branches. They are complementary, not
redundant: `VoteCheckWeb` has the UI, permalinks and SQLite mirror but ingests from the
retiring legacy API; `VoteCheck.Core` has the new-API client, typed models, caching and tests,
but no pages and no persistence. This section converges them: `VoteCheckWeb` keeps its Razor UI
and SQLite mirror, `VoteCheck.Core` becomes the single upstream boundary, and the duplicate
legacy client in `VoteCheckWeb/Sync/` is deleted.

### Blockers to resolve on the way

- **String vote identifiers break the FTS5 index.** The mirror schema keys on
  `session.id INTEGER PRIMARY KEY` (legacy `AanestysId`, e.g. `51221`); the new API's
  identifier is a string (`Aanestys.Id`, e.g. `"2026-60-1"`). Moving `session.id` and
  `vote.session_id` to `TEXT` invalidates `content_rowid='id'`, because FTS5 external-content
  tables require an INTEGER rowid. The search index must keep a surrogate integer rowid beside
  the text tunnus, or abandon external-content mode.

- **`IEduskuntaClient` cannot enumerate history.** It exposes recent votes, a vote by tunnus,
  votes in a session, votes for a matter, and the MP endpoints — nothing that walks the
  archive. The current sync pages through legacy `SaliDBAanestys` from 2023 onward and has no
  equivalent. Either the interface gains an enumeration method or backfill synthesises session
  identifiers (`{year}-{number}`) and walks `GetVotesInSessionAsync` until exhaustion. Check
  the OpenAPI spec (§3.1) first — this may already be answered there.

- **The `KieliId` filter becomes obsolete.** The legacy API stored a Swedish duplicate of every
  vote under the adjacent `AanestysId`; the new one returns a single record with `fi`/`sv`
  inline as `LocalizedText`. Delete that filter and the test that was to pin it, rather than
  porting either. The `Tyhjää` → `Tyhjä` normalisation still applies but moves to
  `EdustajanAanestys.Kayttaytyminen`.

### Steps

In priority order; each step is independently landable.

1. ~~**Land `VoteCheck.Core` on master.**~~ **Done 2026-08-29** (PR #25): `VoteCheck.Core`,
   `VoteCheck.Core.Tests`, `VoteCheck.Api`, `VoteCheck.Api.Tests` merged; solution builds,
   153 tests green (71 legacy + 55 Core + 27 Api).

2. **Close the backfill gap.** Verify against the OpenAPI spec / live API whether sessions can
   be enumerated, then add the method to `IEduskuntaClient` and both implementations. This is
   the only step with genuine unknowns, so it runs early: if enumeration proves impossible the
   mirror design needs rethinking, and that must surface now. Timebox it.

3. **Migrate the mirror schema to string identifiers.** `session.id` and `vote.session_id` to
   `TEXT`; rebuild the FTS5 index per the blocker above. The database is disposable, so this is
   a drop-and-rebackfill, not a migration script.

4. **Repoint the sync.** Rewrite `VoteSyncService` against `IEduskuntaClient` and delete
   `VoteCheckWeb/Sync/EduskuntaApiClient.cs`. Move vote normalisation to `Kayttaytyminen`,
   drop the `KieliId` filter, flatten `LocalizedText` party abbreviations at the boundary.
   Acceptance: a fresh database backfills 2023+ unattended and resumes after restart without
   duplicates or gaps. **This is the deadline-critical step** — the legacy API has been
   redirecting since 30 Mar 2026 and shuts down at year end.

5. ~~**Tests for `VoteCheckWeb`.**~~ **Done 2026-08-29.** `VoteCheckWeb.Tests` covers
   `Queries` against a temp database built by the real `EnsureSchema` (party sums,
   attendance, name and FTS search, annulled-division handling) and `VoteValue`
   normalisation, which moved out of the sync so it could be tested and shared. 24 tests.
   The chronological-ordering assertions were mutation-checked: reinstating
   `ORDER BY id DESC` fails two of them.

6. ~~**Decide `VoteCheck.Api`'s fate.**~~ **Done 2026-08-29: folded into `VoteCheckWeb`.**
   One deployable, one upstream path, one JSON surface. What moved across: the
   `/mps/{id}/activity` rollup (now a SQL aggregate over the mirror rather than a
   derivation from live payloads), `?lang` resolution, Swagger/OpenAPI at `/swagger`,
   configurable CORS via `VoteCheck:AllowedOrigins`, and `/health`. `VoteCheck.Api` and
   `VoteCheck.Api.Tests` are deleted; the Dockerfile moved to `VoteCheckWeb/` and CI builds
   that image.

   Carrying `?lang` required a schema change, since the mirror stored Finnish only: `session`
   now holds `title_sv`/`subject_sv`, falling back to Finnish per row when a translation is
   absent. English is deliberately not stored — the vote endpoints upstream carry none, so
   `lang=en` resolves to Finnish rather than returning blanks. Party abbreviations and vote
   values stay canonical Finnish: they are identifiers, not prose. Search still matches the
   Finnish FTS index whatever language the results render in.

7. **Ship it.** *Metadata and crawlability done 2026-08-29; deployment outstanding.*

   Done: OpenGraph and Twitter-card tags on every page, with `/vote/{id}` leading its card
   with the tally (`Jaa 101 – Ei 90 · …`) because feeds truncate the tail and the numbers
   are the fact being checked; per-page `<title>`, meta description and absolute
   `<link rel=canonical>`; a `wwwroot` with a favicon; and `/robots.txt` and `/sitemap.xml`
   served as endpoints rather than files, since both need the deployment's own origin.
   Search result pages are `Disallow`ed — they are generated per query and add nothing to
   an index. Card text is truncated on a word boundary: a subject can be a full sentence,
   and a card cut mid-word reads as broken.

   **Still to do — deployment.** `VoteCheckWeb/Dockerfile` builds the whole product and CI
   builds that image, but choosing a host and pushing needs credentials. Requirements when
   it happens: HTTPS, `votecheck.db` on a persistent volume (the mirror is rebuildable but
   re-backfilling on every restart is slow and rude to upstream), and one unattended full
   backfill of the 2023+ window (~2,771 divisions, ~56 requests) before traffic arrives.
   Acceptance: a shared `/vote/{id}` link opens publicly in under a second and unfurls with
   its tally.
