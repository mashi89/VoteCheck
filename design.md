# VoteCheck — Design & Technical Roadmap

*Last updated: 2026-07-19*

## 1. Purpose

VoteCheck lets anyone check what Finnish MPs (kansanedustajat) have been voting on, using the
[Finnish Parliament Open Data API](https://avoindata.eduskunta.fi/). The long-term goal is an
**easy-to-use activity checker for Finnish political representatives, usable from a browser and
as a mobile-installable app** — not just a desktop program.

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

## 3. Target Architecture (to-be)

```
┌──────────────────────┐     ┌──────────────────────────┐     ┌─────────────────────────┐
│  Browser / PWA       │     │  VoteCheck.Api           │     │ avoindata.eduskunta.fi  │
│  (installable on     │ ──▶ │  ASP.NET Core minimal    │ ──▶ │ upstream open data      │
│   mobile & desktop)  │     │  API + response cache    │     │                         │
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
- **API in the middle.** The browser talks to *our* API, never to avoindata.eduskunta.fi directly.
  This lets us cache aggressively (immutable historical votes), shape friendly JSON, add computed
  endpoints (activity summaries), and stay within upstream rate limits.
- **PWA instead of separate native apps.** A Progressive Web App gives browser + "install to home
  screen" on Android/iOS from a single codebase — the fastest route to "usable from browser/app".
- **Public data, no accounts.** No authentication needed for v1; everything is open data.

### Candidate API surface (v1)

| Endpoint | Purpose |
|----------|---------|
| `GET /api/mps` | Current MPs (name, party, constituency) |
| `GET /api/mps/{id}/votes?count=50` | An MP's recent votes with issue titles |
| `GET /api/mps/{id}/activity` | Computed summary: attendance %, Jaa/Ei/Tyhjä/Poissa breakdown |
| `GET /api/votes?date=yyyy-MM-dd` | Voting sessions by date/year prefix |
| `GET /api/votes/{id}/distribution` | Party-level result for a session |
| `GET /api/votes/{id}/ballots?party=ps` | Individual MP votes for a session |

## 4. Roadmap — Next 3 Steps

### Step 1 — Refactor the data layer into `VoteCheck.Core` (foundation)

*Goal: a thread-safe, async, typed core library that can be hosted anywhere.*

- Convert `OpenDataRetriever` from a static class with shared state into an instance-based
  `EduskuntaClient` taking `HttpClient` via constructor injection (enables `IHttpClientFactory`
  and clean test mocks — no more reflection hacks).
- Replace `DataTable` returns with typed records (`Mp`, `VotingSession`, `Vote`,
  `PartyDistribution`); keep thin `DataTable` adapters temporarily so the Avalonia GUI keeps
  working during the transition.
- Make all I/O `async`/`await` end-to-end; remove `ManualResetEvent`/blocking patterns.
- Add an in-memory cache (`IMemoryCache`) with long TTLs for historical (immutable) sessions and
  short TTLs for "current MPs" / recent votes.
- Port existing MSTest tests to the new API; keep coverage of pagination (`hasMore`) and party
  mapping.

*Done when:* the Avalonia app runs unchanged on top of the new core, and tests pass without
reflection-based mocking.

### Step 2 — Stand up `VoteCheck.Api` (ASP.NET Core minimal API)

*Goal: the data is reachable from any browser via clean JSON endpoints.*

- New project `VoteCheck.Api` referencing `VoteCheck.Core`; implement the v1 endpoints above.
- Add the first *computed* endpoint, `GET /api/mps/{id}/activity`, aggregating attendance and
  vote-type distribution — this is the "activity checker" differentiator over raw open data.
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

- **Topic search:** free-text search over vote titles (`KohtaOtsikko`) with a small indexed store.
- **Notifications:** "follow an MP" with web push when they vote (requires a scheduled fetcher
  and a persistence layer — first real database need).
- **Charts:** party-line cohesion, MP attendance trends over an electoral term.
- **Historical MP data:** extend beyond `SeatingOfParliament` (current term) to past terms.
- **Retire or slim the desktop app** once the PWA reaches feature parity; Avalonia project can
  remain as a thin shell over the same core.

## 6. Risks & Open Questions

| Risk | Mitigation |
|------|------------|
| Upstream API rate limits / availability | Cache immutable history aggressively; consider nightly snapshot into SQLite if limits bite |
| Upstream schema changes (undocumented tables) | Keep JSON samples in `JSONSamples/`, contract tests against live API in CI (allowed to warn, not fail builds) |
| MP identity across terms (`EdustajaHenkiloNumero` vs `EdustajaId`) | Decide canonical ID in Step 1 model design |
| Hosting cost for a hobby project | Free tiers + output caching keep compute minimal; static PWA assets are nearly free to serve |
