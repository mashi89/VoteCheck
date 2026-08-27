# VoteCheck — Design & Technical Roadmap

*Last updated: 2026-07-20*

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
┌──────────────────────┐     ┌──────────────────────────┐     ┌─────────────────────────┐
│  Browser / PWA       │     │  VoteCheck.Api           │     │ api.eduskunta.fi        │
│  (installable on     │ ──▶ │  ASP.NET Core minimal    │ ──▶ │ /api/v1/... (new,       │
│   mobile & desktop)  │     │  API + response cache    │     │ unauthenticated JSON)   │
└──────────────────────┘     └───────────┬──────────────┘     └─────────────────────────┘
┌──────────────────────┐                 │
│  Avalonia desktop    │ ──▶ ┌───────────▼──────────────┐
│  (kept, optional)    │     │  VoteCheck.Core          │
└──────────────────────┘     │  async services + typed  │
                             │  models (no DataTable)   │
                             └──────────────────────────┘
```

Principles:

- **One core library, many frontends.** `VoteCheck.Core` (evolved from `VoteCollector`) holds all
  Eduskunta API access, typed domain models (`Mp`, `VotingSession`, `Vote`, `PartyDistribution`,
  `MpActivitySummary`) and business logic. The web API, the browser UI, and the existing desktop
  app all consume it.
- **API in the middle.** The browser talks to *our* API, never to api.eduskunta.fi directly. This
  lets us cache aggressively (immutable historical votes), shape friendly JSON, add computed
  endpoints (activity summaries), and stay comfortably within upstream's rate limit (see §3.1).
- **PWA instead of separate native apps.** A Progressive Web App gives browser + "install to home
  screen" on Android/iOS from a single codebase — the fastest route to "usable from browser/app".
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
  vote endpoints; `EduskuntaClient` flattens it.
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
| `GET /api/votes?date=yyyy-MM-dd` | Voting sessions by date | `GET /taysistunnot/uusimmat-aanestykset` / session lookups |
| `GET /api/votes/{id}/distribution` | Party-level result for a session | `aanestystulos` + `eduskuntaryhmaJakaumat` on `GET /taysistunnot/aanestykset/{aanestystunnus}` |
| `GET /api/votes/{id}/ballots?party=ps` | Individual MP votes for a session | `aanestystapahtumat` on the same endpoint, filtered |

## 4. Roadmap — Next 3 Steps

### Step 1 — Build `VoteCheck.Core` directly against `api.eduskunta.fi` (foundation)

*Goal: a thread-safe, async, typed core library targeting the **new** API from day one — no
detour through the legacy table shape, since it has only months of runway left (§banner, §3.1).*

> **Status: client + models done and validated against live payloads.**
> `VoteCheck.Core`/`EduskuntaClient` covers `kansanedustajat` and the
> `taysistunnot/*aanestykset*` endpoints, with typed models replacing `DataTable`. Real
> captured responses are committed as fixtures under `VoteCheck.Core.Tests/Fixtures/` and the
> shape tests assert against them, which caught two things the documented research missed: most
> "scalar" ballot fields are actually bilingual objects, and `uusimmat-aanestykset` is a nested
> array. The per-MP-votes open question is **resolved** (ballots are embedded in every vote —
> see §3.1). Whole solution builds; all tests pass (28 new + 71 legacy).
>
> Remaining for Step 1: `IMemoryCache` layer, and endpoints not yet wrapped or verified
> (matters, documents, `/search`, reference data).

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
- In-memory cache (`IMemoryCache`): long TTLs for historical (immutable) votes/matters, short TTLs
  for "current MPs" and `uusimmat-aanestykset`; keep well under the 450 req/3000s rate limit
  documented for `/search*`.
- New MSTest coverage against the new client (mocked `HttpClient`, no reflection); port over the
  useful existing test cases (pagination-style behavior, party-code mapping) adapted to the new
  shapes.

*Done when:* `EduskuntaClient` can fetch an MP, a vote with its party/government-opposition
breakdown, and recent votes, entirely from `api.eduskunta.fi`, with tests passing on mocked HTTP.

### Step 2 — Stand up `VoteCheck.Api` (ASP.NET Core minimal API)

*Goal: the data is reachable from any browser via clean JSON endpoints.*

- New project `VoteCheck.Api` referencing `VoteCheck.Core`; implement the v1 endpoints above.
- Add the first *computed* endpoint, `GET /api/mps/{id}/activity`, aggregating attendance and
  vote-type distribution across an MP's votes — this is the "activity checker" differentiator
  over raw open data (note: per-vote party/government-opposition breakdowns are already provided
  upstream, so this endpoint's real work is the *cross-vote, per-MP* rollup, not per-vote tallying).
- Response caching + output caching middleware; CORS enabled for the future frontend origin;
  OpenAPI/Swagger UI for discoverability.
- Containerize (Dockerfile) and deploy a public instance (Azure App Service free tier, Fly.io, or
  similar); add a GitHub Actions workflow for build + test + deploy.

*Done when:* `curl https://<host>/api/mps` returns live data and Swagger UI documents the API.

### Step 3 — Ship the browser/PWA frontend

*Goal: a shareable URL that works on phone and desktop, installable as an app.*

- Frontend project `VoteCheck.Web` — **Blazor WebAssembly (PWA template)** is the natural fit for
  this C# codebase (shares models from `VoteCheck.Core`); a lightweight TypeScript/React SPA is
  the alternative if broader contributor familiarity matters more.
- v1 screens, mobile-first:
  1. **MP search** (type-ahead by surname) → MP page with recent votes + activity summary.
  2. **Vote browser** (by date) → drill-down to party distribution → individual ballots
     (parity with today's desktop drill-down).
- PWA manifest + service worker → installable on Android/iOS home screen, basic offline shell.
- Localization scaffold: Finnish first, Swedish/English strings behind a resource file
  (the Swedish party-name toggle already exists in the desktop app — carry the concept over).

*Done when:* a public URL serves the app, Lighthouse PWA checks pass, and an MP's recent votes
can be found on a phone in under three taps.

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
| **Legacy API shutdown (end of 2026, already redirecting since 30 Mar 2026)** — resolved by targeting `api.eduskunta.fi` directly in Step 1 (see banner, §3.1) instead of the legacy table API | No further mitigation needed beyond following the updated Step 1 plan; keep `OpenDataRetriever` only as a short-lived fallback, not a long-term dependency |
| ~~No confirmed endpoint for "all votes by one MP"~~ — **resolved**: ballots are embedded in every vote payload (§3.1) | Recent-window per-MP history is a client-side filter. Still open at larger scale: *deep historical* per-MP queries would mean walking every past session, so a per-MP index becomes worthwhile if we go beyond recent votes |
| **Payload size** — each vote carries ~199 ballots plus three breakdown sets (~75 KB per vote); `uusimmat-aanestykset` returned ~750 KB for 10 votes | Our API should project down to what each view needs rather than proxying upstream objects; cache parsed results, and avoid fetching full vote objects when only tallies are shown |
| Upstream API rate limits / availability | `/search*` is capped at 450 POST/3000s/IP per the spec; cache immutable history aggressively; consider a nightly snapshot into SQLite if limits bite elsewhere |
| Upstream schema changes | Real captured responses are committed as fixtures in `VoteCheck.Core.Tests/Fixtures/` and asserted by shape tests, so a breaking change surfaces as a test failure; refresh fixtures periodically since they're a point-in-time snapshot |
| MP identity across terms (`henkilonumero` continuity, legacy `EdustajaId`/`EdustajaHenkiloNumero`) | Decide canonical ID (`henkilonumero`, per the new API) in Step 1 model design |
| Hosting cost for a hobby project | Free tiers + output caching keep compute minimal; static PWA assets are nearly free to serve |
