# VoteCheck — Design & Technical Roadmap

*Last updated: 2026-09-21*

> **Upstream migration: done for the product, open only for the desktop remnant.**
> `VoteCheckWeb` and `VoteCheck.Core` have read `api.eduskunta.fi` and nothing else since
> 2026-08-29 (§7 step 4). What still reads the legacy table API
> (`avoindata.eduskunta.fi/api/v1/tables/...`) is `VoteCollector` and the Avalonia GUI above it.
> That API is scheduled for discontinuation at the **end of 2026**, which makes those two
> projects a decision with a date on it rather than an open-ended tidy-up — see §9.
>
> Checked again on 2026-09-21: the legacy API has **not** begun redirecting, whatever earlier
> drafts of this file said. The table endpoints still answer `200` with live rows and there is
> no HTTP redirect anywhere. The published shutdown stands and the urgency was real; the
> mechanism described here was simply invented.
>
> **How the new API actually behaves now lives in its own file**,
> [`docs/eduskunta-api.md`](docs/eduskunta-api.md) — every claim there was checked against the
> live service. Where it and §3.1 disagree, that file is right: it is maintained against
> `curl`, and this one against intent.

## 1. Purpose

VoteCheck lets anyone check what Finnish MPs (kansanedustajat) have been voting on, using the
Finnish Parliament Open Data API — [api.eduskunta.fi](https://api.eduskunta.fi/) (§3.1). The
goal is an **easy-to-use activity checker for Finnish political representatives, usable from a
browser and as a mobile-installable app** — not just a desktop program. The browser half of
that has been live at [edustajavahti.fi](https://edustajavahti.fi) since 2026-09-04; the
installable half has not been started (§9).

Typical user questions the product should answer in a few taps:

- *What has my MP been voting on lately?*
- *How did the parliament / each party vote on issue X?*
- *How active is an MP?* (attendance, absences, blank votes)
- *Who inside a party broke ranks on a vote?*

## 2. Current State (as-is)

*Rewritten 2026-09-21. What this section described — a desktop program and nothing else — has
been the minority of the repository since the web product shipped.*

```
api.eduskunta.fi ──▶ VoteCheck.Core ──▶ VoteSyncService ──▶ votecheck.db ──▶ VoteCheckWeb ──▶ edustajavahti.fi
 (JSON, no auth)      the single         (hosted inside      (SQLite +       Razor pages       (live since
                      upstream boundary   VoteCheckWeb)       FTS5 mirror)    + /api/v1         2026-09-04)

avoindata.eduskunta.fi ──▶ VoteCollector ──▶ WPFGUI (Avalonia)     ← legacy, on a dead clock
 (legacy table API,         DataTable,       desktop XAML app
  shuts down end of 2026)   static state
```

| Component | State |
|-----------|-------|
| `VoteCheckWeb` | The product. Razor Pages (`/`, `/vote/{id}`, `/vote/{id}/{party}`, `/mps`, `/mp/{id}`, `/search`), a `/api/v1` JSON surface with Swagger, the sync service, `/health`, `/robots.txt` and `/sitemap.xml`. Deployed as a single container on one UpCloud server in Helsinki, behind Cloudflare. |
| `VoteCheck.Core` | The single boundary to api.eduskunta.fi: typed models, `EduskuntaClient`, `CachingEduskuntaClient`, archive enumeration through `/search`, and the matter lookup. |
| `votecheck.db` | SQLite + FTS5 mirror, 2023 onward (~2,800 divisions and their ~199 ballots each, plus the matters behind them). Disposable: gitignored, rebuildable, one writer. |
| `VoteCheck.Core.Tests` / `VoteCheckWeb.Tests` | MSTest against committed fixtures and a temp database built by the real schema. |
| `VoteCollector` + `WPFGUI` | Untouched since the migration. Static class, `DataTable`, synchronous `HttpClient`, reading an API that shuts down at the end of 2026. |

### What is still weak

1. **The mirror starts at 2023.** `SyncMinYear` is a parliamentary-year floor and `/search`
   cannot page past 10,000 results, so the archive before 2023 — back to 2008, 15,500-odd
   divisions in total — is simply not there. Any question about a member's career rather than
   their current term is unanswerable today.
2. **A division still does not say what voting *Jaa* meant.** The subject and its topic
   keywords lead the page now, but the step from "the motion was carried 101–90" to "and that
   means X happens" is the reader's to make. §5's selkokieli entry reaches the same defect from
   the accessibility side; they are one piece of work.
3. **The member page shows almost nothing about the member** ([#47](https://github.com/mashi89/VoteCheck/issues/47)).
   Name, group, attendance, ballots. Not the constituency — which is how a visitor works out
   whether this is *their* representative at all — nor committees, ministries, or the group
   history our single `party` column flattens away.
4. **Two breakdowns are downloaded on every division and never shown.**
   `hallitusoppositioJakaumat` and `vaalipiiriJakaumat` arrive inside a payload the sync
   already pays for. Government against opposition is, for many divisions, the single most
   explanatory fact about them.
5. **One instance, one writer.** SQLite on local block storage with the sync as sole writer is
   deliberate (§7 step 7), but it means the deployment cannot scale horizontally and a restart
   is visible.
6. **The legacy projects have a deadline and no decision** (§9).

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

> **This section is a summary; [`docs/eduskunta-api.md`](docs/eduskunta-api.md) is the
> reference.** Written 2026-09-21 against the live service, it covers the behaviour that costs
> afternoons: the mandatory `User-Agent`, what `/kansanedustajat` actually returns, the
> `/search` window, and the three corrections below. Four things recorded here or in this
> project's history turned out to be wrong, so prefer the file that was checked with `curl`.

Corrections since this section was first written, all confirmed live on 2026-09-21:

- **The matter lookup is a direct `GET`, not a search.** `GET /valtiopaivaasiat/{eduskuntatunnus}`
  is exact and outside the POST rate cap; `GetMatterAsync` used `/search` free text until
  [#59](https://github.com/mashi89/VoteCheck/issues/59), because the obvious URL 404s until the
  slash inside `113/2026` is escaped. It refuses two ways — 404 for an unknown identifier, 400
  for one that is not an identifier at all, such as the combined `LA 1, 18/2023 vp` — and both
  mean "there is no one matter here".
- **`GET /kansanedustajat` is not the sitting parliament**, and it returns an envelope rather
  than an array. It serves 1000 records from the all-time roster, most of them long dead, with
  no paging. The mirror builds its member list from ballots instead, which is complete by
  construction.
- **A `fields` projection is honoured**, contrary to a note once recorded here. It blanks the
  excluded keys rather than removing them, so a check that counts keys concludes it was
  ignored. Excluding `aanestystapahtumat` takes a division from ~76 KB to ~5 KB — which is what
  makes a metadata-only pass over the archive affordable (§9 step 6).

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

### Our own API surface — as delivered

Served by `VoteCheckWeb` from the mirror, not by proxying upstream. The 2026-08-29 table of
*candidate* endpoints has been replaced by what exists; the shapes moved as the mirror, rather
than the upstream payload, became the thing being projected.

| Endpoint | Purpose |
|----------|---------|
| `GET /api/v1/sessions?count=50` | Latest divisions, newest first |
| `GET /api/v1/sessions/{id}` | One division with its party distribution |
| `GET /api/v1/sessions/{id}/votes?party=kok` | Individual ballots, optionally one group |
| `GET /api/v1/mps?name=` | Members, filterable by name |
| `GET /api/v1/mps/{personNumber}?count=50` | A member with their recent ballots |
| `GET /api/v1/mps/{personNumber}/activity` | Attendance and the Jaa/Ei/Tyhjä/Poissa rollup |
| `GET /api/v1/search?query=` | Full-text search over titles, subjects and topic keywords |

Two deliberate differences from the original sketch. `?lang` resolves `fi|sv` only: the vote
endpoints upstream carry no English, so `lang=en` falls back to Finnish rather than serving
blanks. And there is no `/votes?date=` — the front page answers "what happened lately" by
chronological order over the mirror, which is both cheaper and what people actually ask for.

Ballots remain a separate call from the distribution, so a client showing only the party split
never pays for 199 rows.

## 4. Roadmap — the first three steps (all delivered)

*Kept as the record of how the product got here. All three closed out between 2026-08-29 and
2026-09-04; §8 records what has shipped since, and **the live roadmap is §9**.*

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
> Done 2026-09-04: <https://edustajavahti.fi> is live on UpCloud Helsinki behind Cloudflare,
> built from the kit completed 2026-08-29 — Dockerfile, CI image build, compose overlays and
> the `deploy/` runbook. See §7 step 7.

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

§7 steps 5–7 have since closed this out: `VoteCheckWeb.Tests`, OpenGraph and canonical tags
for link unfurling, a `wwwroot` favicon with `/robots.txt` and `/sitemap.xml` served as
endpoints (both need the deployment's own origin), and Swedish carried through the queries
with a per-row Finnish fallback — nearly free, as predicted, because upstream sends both
languages on every division.

*Done when:* a public URL serves the SSR pages from a database synced via `VoteCheck.Core`,
and an MP's recent votes can be found on a phone in under three taps.

## 5. The long list

*Everything wanted but not scheduled. Items promoted into the live roadmap are marked; the
rest stay here until something makes them next.*

- ~~**Topic search**~~ — **done 2026-09-18** ([#53](https://github.com/mashi89/VoteCheck/issues/53)),
  and not the way this line expected. Upstream `/search` was not used: it is rate-capped, fuzzy
  and ranks rather than filters. The mirror's own FTS5 index searches division titles and
  subjects, and the matters' YSO subject keywords are written into that index as they arrive —
  which is what makes searching *alkoholipolitiikka* find the divisions about it rather than
  only those whose legal title happens to contain the word.
- **Notifications:** "follow an MP" with web push when they vote (requires a scheduled fetcher
  and a persistence layer — first real database need).
- **Charts:** party-line cohesion, MP attendance trends over an electoral term. The chamber
  diagram (§8) is the first of these and sets the house style: draw the thing that happened,
  in the shape it happened in, server-side and without JavaScript.
- **Historical MP data:** extend beyond the current term to past ones — *promoted to §9 step 6*,
  since the mirror's 2023 floor is what blocks it and the way past that floor is now known.
- **Selkokieli (Finnish plain language) as an optional reading mode:** an accessibility
  feature that serves the product's purpose directly — a voting record nobody can read is not
  a check on power. The audience is people with reading or comprehension difficulties, language
  learners, and readers who simply bounce off officialese. Written to the Selkokeskus guidance:
  <https://selkokeskus.fi/selkokieli/nain-kirjoitat-selkokielta/>.

  The genre section, *Ohjeita informoivien tekstien tekijöille*, is the governing one: it
  covers texts whose job is to inform or help the reader do something, and names
  *säädöstekstit* — legislative texts — among them. That is what this site publishes, so the
  informative-text rules apply to it directly rather than by analogy.

  What the guidance demands of a site like this one — drawn from that section and from
  *Vuorovaikutus lukijan kanssa ja tekstin kokonaisuus*, *Helpot kielen rakenteet* and
  *Selkokielen sanasto*. Layout and typography, and the criteria Selkokeskus assesses for the
  *selkotunnus*, are not yet read:

  - **No content gaps (*sisällöllinen aukko*).** A gap is anything the writer expects the
    reader to infer that the text never states outright. A table cell reading `Jaa` is exactly
    that: it assumes the reader knows what the motion was and what voting for it did to it.
    The standard arrives independently at the same defect as the "what did Jaa/Ei mean" work —
    strong evidence those are one piece of work rather than two.
  - **Write from the reader's perspective, not the organisation's.** This site speaks in the
    parliament's voice by construction: `aanestysotsikko` *is* institutional voice, inherited
    wholesale from upstream. Undoing that is editorial work, not a setting.
  - **Headings must match their content.** Division titles routinely name the procedural step
    rather than what was decided, so using one as a heading breaks this rule even when the
    title is perfectly accurate.
  - **Do not condescend, and do not underestimate the reader.** The guidance warns against a
    patronising (*holhoava*) tone and against explaining words the reader can be assumed to
    know. A selko mode that reads as a simplified ghetto has failed on the standard's own
    terms.
  - **One idea per paragraph, subheadings for rhythm, recap in long texts.** MP pages are long
    and will get longer; this is a layout constraint as much as a wording one.
  - **Know the subject more broadly than the source text**, so you can judge what is essential.
    This is a data requirement, not just an editorial one: a division's `title` and `subject`
    are not enough to say what was actually decided. It needs the matter behind the vote —
    half of which has since arrived: `/valtiopaivaasiat` is modelled and the mirror holds each
    division's matter, its type in plain Finnish, its subject terms and its outcome (§8).
    `/asiakirjat`, the documents themselves, is still unmodelled, and the matter shape still
    has no captured fixture behind it (§6).
  - **Make the reader an active agent.** The guidance warns against casting the reader as
    permanently passive or as an object of help, and prefers the imperative and the sinä-form
    for instructions. That suits this product exactly: the citizen checking a representative is
    the actor, and the copy should read that way — *katso, miten edustajasi äänesti*, not a
    passive report about what is available.
  - **Support the text with images and infographics.** Named explicitly in the genre section.
    The party distribution is the obvious candidate: a graphic carries a split that a table of
    four numbers does not. Overlaps with the **Charts** item above; do them together.
  - **Have it checked, by a subject expert and ideally by another selkokieli expert.** Review
    is part of the standard, not a nicety — and the guidance notes the subject expert's pass
    often makes the text *harder*, so someone has to hold the selko purpose afterwards. This
    is a recurring editorial cost with a named skill attached, which is the main thing to weigh
    before committing to any scope beyond chrome.

  The structure and vocabulary rules settle the scope question, because **parliamentary Finnish
  is their systematic inverse.** The guidance asks for the active voice, common case forms,
  short clauses carrying one idea, no participle or infinitive constructions, no
  *lauseenvastikkeet*, everyday concrete words, short words, and no abbreviations.
  `aanestysotsikko` is passive, nominalised, participle-heavy, abstract and long by
  construction — that is what the register is *for*. So a selko view cannot be the same string
  with easier words substituted. It has to be written from knowledge of the matter, which is
  the same conclusion the "know the subject more broadly than the source text" rule reaches.

  Four consequences worth deciding early:

  - **A table is structurally anti-cohesive.** The guidance requires that relations between
    things be visible, warns against loose disconnected main clauses, and allows only short
    lists whose items form a whole. A three-column table of divisions is the table equivalent
    of the thing it warns about: rows with no stated relation to one another. Selko mode is
    therefore probably not the current tables with simpler wording — it is a different
    presentation, closer to grouped prose.
  - **One term per thing, everywhere.** *Viittaa samaan asiaan samalla sanalla.* Today the
    schema says `session`, the UI says *äänestys*, and the roadmap says *division*. Pick one
    Finnish term for the concept and never vary it for style. Cheap now, expensive later.
  - **Party abbreviations survive, but on the exception.** The rule is to avoid abbreviations
    *unless the abbreviation is more familiar than the expansion*, and `kok` or `sd` plainly is.
    Keep `edkryhmalyhenne` in the interface, and expand it once where it first appears rather
    than banishing it.
  - **Explanations go inline, not in a glossary.** The guidance asks for the explanation that
    is sufficient and most useful *in this context*, not a dictionary entry. That argues
    against a separate terms page and for a short gloss beside the term that needs it.

  Finally, the two halves of the guidance pull against each other, and holding both is the
  actual craft: cut every piece of information the reader does not need, while never creating a
  *sisällöllinen aukko*. Shorter, and yet nothing left to infer.

  Worth noting who the guidance says this is for: people who need selkokieli *"voidakseen
  osallistua yhteiskunnan toimintaan"* — in order to take part in society. For a product whose
  entire purpose is holding representatives accountable to the people they represent, that is
  the audience, not a secondary one.

  The hard part is not our chrome, it is the source data. The densest Finnish on the site is
  upstream: `aanestysotsikko` and `kohta.otsikko` are parliamentary officialese, and there are
  thousands of them. Three honest scopes, in increasing cost:

  - **Chrome only** — navigation, labels, explanations of what a division and a Jaa/Ei are.
    Fully achievable, invents nothing, and already most of the comprehension barrier for a
    first-time visitor.
  - **Chrome plus curated summaries** for divisions that matter. Editorial work per division,
    so it does not scale to the full mirror; pick a threshold (contested votes, party-line
    breaks) rather than pretending to cover everything.
  - **Generated summaries** — must be labelled as such and never presented as the record.
    An unverified restatement of how somebody voted is precisely the failure mode this project
    cannot afford, and it fails the standard on its own terms: the guidance requires checking
    by a subject expert and use of reliable sources only. Unreviewed generated text is not
    selkokieli, whatever it reads like.

  Two constraints to design around. **`selkokieli` is a standard, not a style:** Selkokeskus
  assesses material and grants the *selkotunnus*, so the UI must not claim the label without
  it — "selkokieli" as a self-applied badge is a claim about someone else's certification.
  And **it is a third variant of Finnish, not a fourth language:** the existing `LocalizedText`
  and `IsSwedish( lang )` plumbing keys off fi/sv, so this needs a deliberate decision (a
  `fi-selko` variant, or an orthogonal toggle) rather than being bolted onto the language
  parameter.

  Design it together with the "what did Jaa/Ei actually mean" work rather than after it. That
  restatement is the same problem at a lower standard, and the no-content-gaps rule above is
  the same finding reached from the other direction. Writing it once, to the guidance, costs
  little more than writing it twice without.
- **Retire or slim the desktop app** — *promoted to §9's dated decisions.* It is no longer a
  "once the PWA reaches parity" question: the API it reads shuts down at the end of 2026
  whatever else happens.

## 6. Risks & Open Questions

*Revised 2026-09-21. Rows that were retired by the migration are gone rather than struck
through; §7 and §8 hold the history of how each was closed.*

| Risk | Where it stands |
|------|-----------------|
| **Legacy API shutdown, end of 2026** | Closed for the product: the sync has read `api.eduskunta.fi` since 2026-08-29 (§7 step 4). Still open for `VoteCollector` and `WPFGUI`, which have no replacement and a deadline — §9's dated decisions |
| **Modelling an endpoint from documentation alone gets it wrong** — four such claims have now been wrong: bilingual ballot fields, the nested recent-votes array, the `/kansanedustajat` envelope, and `fields` being "silently ignored" | Partly mitigated. `docs/eduskunta-api.md` now records the checked behaviour, and fixtures cover the vote endpoints, the MP endpoints and a `/search` page. **Still missing a captured fixture for `/valtiopaivaasiat`**, which is modelled and in production — its tests assert against hand-built objects. Capture it before touching that model again (§9 step 4) |
| **Mocked tests pass what live traffic rejects** — the mandatory `User-Agent` and `puhemies.henkilonumero: "-"` both got through a green suite | Nothing structural; the working rule is that anything touching an upstream shape gets one real request before it ships, and the response becomes a fixture |
| **Payload size** — a division is ~76 KB, almost all of it ballots | The mirror pays it once per division rather than per view, and pages project down. A `fields` projection can cut a metadata pass to ~5 KB per division and is not used yet (§9 step 6) |
| Upstream rate limits / availability | `/search*` is capped at 450 POST/3000 s/IP. The sync sleeps `SyncRequestDelayMs` between requests, the matter pass is bounded per cycle, and the pages never touch upstream at all — they read the mirror, so upstream being down is invisible to a visitor |
| Upstream schema changes | Fixtures are asserted by shape tests, so a breaking change surfaces as a failure. They are a point-in-time snapshot and nothing refreshes them on a schedule; that is a known gap, not a solved problem |
| **The mirror is the single point of failure** — one server, one SQLite file, one writer | Deliberate (§7 step 7) and cheap to rebuild: the database is a pure function of upstream plus `SyncMinYear`. A rebuild costs hours of backfill, not data |
| **Presenting a vote wrongly is the failure this project cannot afford** | Nothing shown is inferred. The matter behind a division is fetched by exact identifier, which matches or 404s rather than returning the nearest plausible record; annulled divisions are excluded from tallies and attendance; a result is reported as "the majority voted Jaa" rather than as the bill passing, because a qualified majority is sometimes required. Generated summaries stay out of the record entirely (§5, selkokieli) |
| Hosting cost for a hobby project | One Starter server in Helsinki, plus Cloudflare's free tier. Output caching keeps compute negligible |

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

2. ~~**Close the backfill gap.**~~ **Done 2026-08-29.** Enumeration is possible, with a
   caveat worth recording. No `taysistunnot/*` endpoint walks history; `/search` does, but
   caps at `startFromIndex + maxResults <= 10000`, so it cannot reach all 15,562 divisions.
   `/search/dataset` (an async export job) can. The cap does not bind here: the 2023+ window
   is 2,771 divisions, so `GetVotePageAsync` on plain `/search` is sufficient for the app and
   the dataset job stays unbuilt until something needs the full archive.

   Two claims once recorded here were wrong, and both are corrected in
   `docs/eduskunta-api.md`. `Sort` is `{property, ascending}` — which is exactly what the spec
   shows, so there was no discrepancy. And a `fields` projection is **not** ignored: it is
   honoured, but it blanks excluded keys rather than removing them, so a check that counts keys
   sees no difference and draws the wrong conclusion. Counting bytes instead, excluding
   `aanestystapahtumat` takes one division from 76,819 bytes to 5,457. Also note `istuntovpvuosi` (2,771 for
   2023+) and an equivalent-looking date range (1,875) return materially different sets —
   the year filter is the one that matches what `SyncMinYear` has always meant.

3. ~~**Migrate the mirror schema to string identifiers.**~~ **Done 2026-08-29.**
   `session.id` and `vote.session_id` are `TEXT`, with a surrogate `session.seq` INTEGER
   primary key existing purely because an FTS5 external-content table requires an INTEGER
   `content_rowid` and cannot key off `id`.

   One trap this uncovered: ordering by the identifier string reverses history. `"2009-114-4"`
   sorts before `"2009-24-1"` lexicographically but happened five months later, so `id` is
   stored alongside its parsed `vp_year` / `session_number` / `vote_number` components and
   every chronological query orders on those. Two tests fail if the string ordering is
   reinstated, which is the point of them.

4. ~~**Repoint the sync.**~~ `VoteSyncService` rewritten against `IEduskuntaClient` and
   `VoteCheckWeb/Sync/EduskuntaApiClient.cs` deleted. Vote normalisation moved out to
   `VoteCheckWeb/Data/VoteValue.cs` — beside the schema rather than inside the sync, because
   the queries, the pages and the seeding script all need the same four strings. The
   `KieliId` filter is gone and `LocalizedText` party abbreviations are flattened at the
   boundary.
   Acceptance: a fresh database backfills 2023+ unattended and resumes after restart without
   duplicates or gaps. **This was the deadline-critical step** — the legacy API shuts down at
   the end of 2026. (An earlier note here said it had already begun redirecting; it had not.
   See the banner at the top of this file.)

   **Done 2026-08-29**, verified against the live API rather than stubs: an empty database
   reached 205 divisions for vp-year 2026, 202 MPs and 40,795 ballots with zero orphaned
   rows and a complete FTS index, and a restart re-imported nothing. The cursor lives in
   `sync_state["vote_cursor"]`, one page per transaction.

   Two upstream realities that only live traffic exposed, both of which stubbed tests
   passed straight through:
   - **`api.eduskunta.fi` returns 403 without a `User-Agent`,** on every endpoint, and
     `HttpClient` sends none by default — `VoteCheck.Core` as merged in step 1 could not
     reach production at all. Hence `EduskuntaClient.DefaultUserAgent`.
   - **`puhemies.henkilonumero` is sometimes `"-"`** (2 divisions of 15,562: no Speaker
     recorded). A plain `int` threw and would have failed the whole page, so
     `LenientInt32Converter` maps anything unparseable to null.

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

7. ~~**Ship it.**~~ **Done 2026-09-04.** *Metadata and crawlability 2026-08-29, the
   deployment kit the same day, and the deployment itself the following week.*

   Done: OpenGraph and Twitter-card tags on every page, with `/vote/{id}` leading its card
   with the tally (`Jaa 101 – Ei 90 · …`) because feeds truncate the tail and the numbers
   are the fact being checked; per-page `<title>`, meta description and absolute
   `<link rel=canonical>`; a `wwwroot` with a favicon; and `/robots.txt` and `/sitemap.xml`
   served as endpoints rather than files, since both need the deployment's own origin.
   Search result pages are `Disallow`ed — they are generated per query and add nothing to
   an index. Card text is truncated on a word boundary: a subject can be a full sentence,
   and a card cut mid-word reads as broken.

   **Deployed 2026-09-04**, from the kit landed 2026-08-29 (PR #27). `edustajavahti.fi`,
   registered at Domainkeskus and delegated to Cloudflare, runs on a single UpCloud Starter
   server in Helsinki, with `deploy/README.md` as the runbook. Helsinki
   and a single instance are not arbitrary: the audience is Finnish, SQLite needs local
   block storage rather than a network filesystem, and the sync is the single writer — a
   second replica would mean two processes writing one file.

   The runbook's ordering carries the anti-DDoS design, so it is worth stating here rather
   than only there. Because Caddy takes its certificate via **DNS-01**, which needs no
   inbound request, the server can be provisioned, certified and firewalled *before any DNS
   record points at it*. The origin IP therefore never appears unproxied even briefly —
   historical-DNS archives never capture it, and there is no window in which an attacker
   learns the address and bypasses the proxy afterwards. DNS-01 also sidesteps the failure
   HTTP-01 has behind a proxy, where a setting like *Always Use HTTPS* breaks renewal months
   later, silently. That much held exactly as designed: the certificate was issued while the
   domain still resolved to nothing at all.

   The origin lockdown did not, and the correction is the most valuable thing this deployment
   produced. It was written here as two layers — UpCloud's firewall, which sits before the
   network interface and so drops floods without consuming the server's bandwidth, and `ufw`
   as an inner layer. **`ufw` is not an inner layer.** Docker publishes 80/443 by writing its
   own iptables rules, and those are evaluated before ufw's INPUT chain, so a published
   container port answers the entire internet however ufw is configured. This was not
   theoretical: `ufw status` listed 44 Cloudflare-scoped rules while `curl` from another
   machine got a 308 off the raw IP. `deploy/cloudflare-firewall.sh` now filters in
   `DOCKER-USER`, the hook Docker provides ahead of its own rules, scoped to the
   default-route interface so the sync's own tcp/443 calls to api.eduskunta.fi are not caught
   by the drop, and installs a systemd unit because iptables does not survive a reboot.

   The UpCloud layer is not in place: it requires a paid account and this one is on trial.
   What that costs is volumetric-flood absorption on the raw IP, an attack that needs the
   address first — which is what the DNS-01 ordering above denies it. When it is enabled,
   note that UpCloud's firewall is stateless, so outbound must stay open or the sync and cert
   renewal fail quietly.

   The only check that means anything is a request from another machine: from the host, traffic
   to its own public IP goes over loopback, which every layer here allows, so it answers no
   matter how exposed the origin is.

   Acceptance met: the 2023+ backfill imported 2,771 divisions unattended before any DNS record
   existed, and a shared `/vote/{id}` link opens publicly and unfurls with its tally. Still
   outstanding: confirmation that `edustajavahti.fi` is clear at PRH/EUIPO/ytj.fi.

## 8. What shipped since the deployment

*2026-09-04 to 2026-09-21. §7 ends with the site going live; this is the fortnight after it,
recorded here because the roadmap in §9 starts from what these left standing.*

**The division page answers the question first.** It used to open with upstream's procedural
title — *"1. lakiehdotus 8 d §: mietintö JAA / Saku Nikkasen ehdotus (vl 1) EI"* — and print
`Jaa 89 · Ei 77`, leaving the reader to know which side Jaa was and compare two numbers. The
subject leads now, the result is stated in words behind a proportional bar, and the procedural
title stays as the labelled *äänestysasettelu*. It says *"enemmistö äänesti Jaa"* rather than
*"the bill passed"*: some questions need a qualified majority, so inferring passage from these
counts would be wrong in exactly the cases that matter most.

Jaa and Ei are no longer green and red. Through a protanopia simulation that pair sits at a
perceptual distance of dE 20 — the two readings of every division looking alike — against
dE 84 for blue and warm orange. The pair also differs in luminance, so it survives greyscale
and print, and it drops the implication that Jaa is the good answer.

**The division is drawn as the chamber it happened in.** One seat per member, coloured by the
ballot cast, in the fan of concentric arcs the Eduskunta actually sits in — geometry measured
from the published seating plan (eight rows of 16, 22, 26, 32, 30, 29, 26 and 18 seats, aisles
at roughly 62°, 90° and 117°) rather than guessed from stylised graphics, which is what an
earlier chevron version had done while its caption claimed otherwise. Seat *positions* are
derived, not published: upstream says how members voted, not where they sit, and the caption
says so on the page. Absent members are drawn hollow rather than given a fifth colour — a
difference in shape as well as hue. The geometry is generated, so its truthfulness is asserted
rather than eyeballed: every member seated exactly once, each group contiguous, nothing outside
the viewBox, and 199 ballots treated as a full chamber because the Speaker does not vote.
Its label placement is still wrong in two ways — [#50](https://github.com/mashi89/VoteCheck/issues/50).

**The design system reaches the whole site.** The front page and search share one division row
as a partial; the member profile shows the breakdown behind its attendance figure using the
same bar a division uses; the member list names groups in full rather than as codes; navigation
marks its section with `aria-current`; and the footer says where the data comes from and that
the mirror can lag — a site asking people to check what their representative did has to be
checkable itself.

**A division now says what it was about, and links to the thing it decided**
([#51](https://github.com/mashi89/VoteCheck/issues/51),
[#53](https://github.com/mashi89/VoteCheck/issues/53),
[#55](https://github.com/mashi89/VoteCheck/issues/55),
[#59](https://github.com/mashi89/VoteCheck/issues/59)). Every division carries the identifier of
the document it decides, and `VoteCheck.Core` had parsed it all along without anyone storing it.
The mirror now has a `matter` table: the document type in plain Finnish, the identifier as a
link to eduskunta.fi, the YSO subject terms as topics — *tulovero, kotitalousvähennys,
matkakustannukset* rather than only the legal title — and the matter's own outcome, worded as
the matter's, because a bill can lose an amendment vote and pass anyway. Those subject terms
are in the FTS index too, which is what makes searching *alkoholipolitiikka* find the divisions
about it. Four details that cost time are in `docs/eduskunta-api.md`: upstream returns the terms
alphabetically while numbering them by importance, the index needed its own copy of the text
because matters arrive after divisions do, an FTS5 table's shape is fixed at creation, and the
matter lookup is a direct `GET` once the slash in the identifier is escaped.

**The upstream API is written down** ([#57](https://github.com/mashi89/VoteCheck/issues/57),
[#61](https://github.com/mashi89/VoteCheck/issues/61),
[#60](https://github.com/mashi89/VoteCheck/issues/60)). `docs/eduskunta-api.md` records every
behaviour that has cost an afternoon, each claim checked against the live service. Writing it
found three wrong claims in this repository's own history — `sort`, the `fields` projection and
the `valtiopaivaasia` expression key — and one wrong line of code: `GetMpsAsync` deserialized a
bare array where the endpoint sends an envelope, so it had never once worked against the live
service. It survived because nothing depends on it and its only test stubbed a shape the service
does not send.

**Compliance and convention**, both 2026-09-04: the CRA software bill of materials with a CI
check that fails when a dependency moves without it (`sbom/`), and the branching convention in
`CONTRIBUTING.md` — issues first, one topic per branch, and the issue number in the branch name.

Two of the repository's original issues were answered by all of this rather than by any single
change: [#13](https://github.com/mashi89/VoteCheck/issues/13) *"I want to see recent parliament
votes"* is the front page, and [#12](https://github.com/mashi89/VoteCheck/issues/12) *"I want to
see what my MP is doing recently"* is `/mp/{personNumber}`.

## 9. Roadmap — the next three steps

*Written 2026-09-21, from §2's "what is still weak" and the open issue list. The ordering is by
how much each closes the gap between what the site shows and what a visitor came to find out —
not by cost, though it happens that the first is also the cheapest.*

### Step 4 — Say what the division decided

*Goal: close the last step between "101–90" and "and so this is what happens now".*

The page now says what the vote was about and how it went. What it still does not say is what
voting Jaa *did*. A cell reading `Jaa` assumes the reader knows the motion and what supporting
it meant — which §5's selkokieli guidance names as a *sisällöllinen aukko*, a content gap, and
which is the defect the whole selkokieli entry converges on. This step is that work at its
cheapest and most factual end, and nothing here invents a word of editorial text.

- **Government against opposition.** `hallitusoppositioJakaumat` is in every division payload
  the sync already downloads, pre-computed by upstream, and has never been shown. For a great
  many divisions it is the single most explanatory fact about them — and, unlike the party
  split, it is not something we could derive ourselves: it depends on which groups were in
  government **on that date**, which would otherwise mean maintaining a history of Finnish
  governments. Upstream states it per division, for free.
- **By electoral district.** `vaalipiiriJakaumat`, likewise already in the payload. Secondary,
  probably below the fold, but it is the one breakdown that answers "how did my region vote".
- Both are distributions rather than scalars, so they do not fit as columns on `session`: this
  is a small table beside it (`session_id`, the group name, and the four counts), filled at
  import from the payload the sync already holds. The party split is computed from the stored
  ballots instead, and stays that way — these two cannot be, which is the whole point.
- Divisions already in the mirror were imported before that table existed and the cursor sits
  past them, so nothing would ever go back for them. This needs the same one-time cursor rewind
  the matter columns needed: cheap, since a re-walk updates only what it adds, but it has to be
  remembered or the backfill silently covers new divisions only.
- **What the ballot options were, in words.** `aanestysotsikko` states them
  (*"mietintö JAA / ehdotus EI"*) in a register nobody reads. The honest version is a gloss
  beside the tally saying which proposal Jaa supported, built from the structure of that string
  and the matter's own record — never from a summary we generate.
- **Capture a `/valtiopaivaasiat` fixture while in here.** It is modelled and in production, and
  its tests assert against hand-built objects; it is the last endpoint on the §6 list without a
  captured response behind it.

*Done when:* someone who knows nothing of parliamentary procedure can read a division page and
say what was decided, which side was which, and where their representative stood — without
leaving the first screen.

### Step 5 — Make the member page worth visiting ([#47](https://github.com/mashi89/VoteCheck/issues/47))

*Goal: the page the product is named for should answer "is this my representative, and what do
they do".*

`/mp/{personNumber}` shows a name, a group, an attendance figure and a list of ballots. Missing
is almost everything upstream publishes about a member, starting with the **constituency** —
which is how a visitor works out whether this is their representative at all. Then committee
memberships, current ministry (ministers vote differently and are absent more, so saying so
explains a low attendance figure rather than leaving it to read as absenteeism), group and term
history, interrupted terms and substitutions, and declared interests. Deliberately **not**
email or telephone: they are in the feed, but republishing direct contact details on a page
inviting judgement of a politician's record invites a pile-on. Link to eduskunta.fi instead.

This is a sync and schema change before it is a page change:

1. Model `GET /kansanedustajat/{henkilonumero}` in `VoteCheck.Core` — the single-member
   endpoint, which works properly, unlike the roster endpoint it is easily confused with.
2. Widen the `mp` table, and add a table beside it for the list-valued fields.
3. Then the page.

**Settle one thing during (1):** whether member detail can be fetched in bulk or needs one
request per member. Roughly 200 GETs per refresh is affordable — GETs are not the capped verb —
but it decides whether this is a cheap per-cycle pass or its own slow job. The roster endpoint
is not an answer to it: 1000 all-time records, no paging, and only a few dozen sitting members.

While in here, decide what to do about the single `party` column. A member who changed group
mid-term is exactly the case it flattens away, and the group history is the field that fixes it.

*Done when:* a visitor can tell from `/mp/{personNumber}` whether this is their representative,
what they work on between divisions, and why their attendance reads as it does.

### Step 6 — Open the archive before 2023

*Goal: stop `SyncMinYear` being the thing that decides what the site knows.*

The mirror holds 2023 onward. The archive goes back to 2008 and runs to some 15,500 divisions,
so every question about a member's career rather than their current term is unanswerable today,
and §5's "historical MP data" has been blocked on precisely this.

`/search` cannot page past `startFromIndex + maxResults <= 10000`, which is why the floor exists.
Two ways past it, and the cheap one should be tried first:

- **Walk it a parliamentary year at a time.** The range expression already takes `from`/`to`, so
  each year is its own query and each year's set is far under the cap. This needs no new
  endpoint and no new machinery — a loop around the existing `GetVotePageAsync` and a cursor per
  year rather than one global cursor.
- **`POST /search/dataset`**, the async export job, if the per-year walk turns out to be blocked
  by something. It returns a `jobId` to poll, requires both `category` and `sort`, and 429s when
  too many jobs run at once — real complexity, worth avoiding if the loop works.

Either way, **use the `fields` projection**. Excluding `aanestystapahtumat` takes a division from
~76 KB to ~5 KB, so a pass that only needs tallies moves a fifteenth of the data. Nothing uses
it today because it was recorded as "silently ignored", which was wrong (§3.1).

One thing to decide before starting rather than after: the full archive is about 1.2 GB of
SQLite, against a Starter server's disk and a mirror that is currently rebuilt from scratch when
anything goes wrong. A full-archive backfill is hours of upstream traffic; check the disk and
the rebuild story first.

*Done when:* a member's whole career is answerable, and a backfill from 2008 completes unattended.

### Decisions with a date on them

Not steps, but they expire, which is why they are written down rather than left to be noticed.

| By | Decision |
|---|---|
| **End of 2026** | **`VoteCollector` and `WPFGUI`.** They read `avoindata.eduskunta.fi`, which shuts down then, and nothing has touched them since the migration. Port or delete — and the case for deleting is strong: the web product supersedes both, and a desktop client for a dead API costs a test suite, an SBOM entry and a place in every architecture diagram. Whichever it is, it needs an issue and it should not be decided in December |
| When the account leaves trial | **The UpCloud firewall layer** (§7 step 7). It absorbs volumetric floods before the network interface; `DOCKER-USER` filtering covers everything else. Note it is stateless, so outbound must stay open or the sync and certificate renewal fail quietly |
| Open since 2026-09-04 | **Name clearance for `edustajavahti.fi`** at PRH, EUIPO and ytj.fi. The domain is registered and live; nobody has checked whether the name is clear |
| No date, and that is the problem | **Fixture refresh.** Fixtures are a point-in-time snapshot and nothing refreshes them on a schedule, so a silent upstream change stays silent until something else breaks. Either pick an interval or accept the drift deliberately |

### Known defects

- [#50](https://github.com/mashi89/VoteCheck/issues/50) — the chamber drops a 23-member group's
  label while keeping a one-member group's, because collisions are resolved in sweep order
  rather than by group size; and a one-member group's label floats off on its own, stretching
  the viewBox and shrinking the chamber.

### Further out

Unchanged from §5, and none of it is scheduled: the installable mobile client
([#9](https://github.com/mashi89/VoteCheck/issues/9)) over the `/api/v1` the site already
serves; "follow a representative" notifications, which need a scheduler and the first real
write path; charts for party cohesion and attendance over a term; and selkokieli, which is the
largest editorial commitment on the list and the one with a named external cost — Selkokeskus
assesses the material, so the label cannot be self-applied.
