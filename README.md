# VoteCheck / Edustajavahti

Check what Finnish MPs have been voting on. The product is a server-rendered website —
live at **[edustajavahti.fi](https://edustajavahti.fi)** — serving a local mirror of
[api.eduskunta.fi](https://api.eduskunta.fi/), with shareable permalinks for every division
and a JSON API beside them. A cross-platform Avalonia desktop app also exists; it predates
the site and is on the [legacy path](#the-legacy-desktop-app).

Everything the site publishes is public open data. There are no accounts, no tracking and
nothing to sign up for.

## What it answers

- *What has my representative been voting on lately?*
- *How did parliament — and each party — vote on this?*
- *How active is a representative?* Attendance, absences, blank votes.
- *Who inside a party broke ranks?*
- *Which divisions were about a topic I care about?*

## The site

| Route | Page |
|---|---|
| `/` | Latest divisions, newest first, with a topic search box |
| `/vote/{id}` | One division: what it was about, the result, the chamber as it voted, the party split |
| `/vote/{id}/{party}` | The same division narrowed to one parliamentary group's ballots |
| `/mps` | Every member in the mirror, filterable by name |
| `/mp/{personNumber}` | A member: group, attendance, and their recent ballots |
| `/search?query=` | Full-text search over division titles, subjects and subject keywords |

A division page leads with the *subject* and its topic keywords rather than the procedural
title, because "Hallituksen esitys laiksi tuloverolain muuttamisesta" says which statute is
being amended and not what the vote decided. Below that, the division is drawn as the chamber
it happened in — one seat per member, grouped as they sit — and then as a party-by-party
table. Screenshots of each in `docs/screenshots/`, e.g.
[the chamber](docs/screenshots/vote-chamber.png) and
[the matter panel](docs/screenshots/vote-matter.png).

Pages are plain server-rendered HTML: no JavaScript is required to read anything, every page
is crawlable, and a permalink unfurls with its tally in a chat client or a feed.

## The JSON API

`/api/v1`, read-only, documented at `/swagger`. It reads the same mirror the pages do, never
upstream, so it is fast and cannot be knocked over by api.eduskunta.fi having a bad day.

| Endpoint | Returns |
|---|---|
| `GET /api/v1/sessions?count=50` | Latest divisions |
| `GET /api/v1/sessions/{id}` | One division with its party distribution |
| `GET /api/v1/sessions/{id}/votes?party=kok` | Individual ballots, optionally one group |
| `GET /api/v1/mps?name=` | Members, filterable by name |
| `GET /api/v1/mps/{personNumber}?count=50` | A member with their recent ballots |
| `GET /api/v1/mps/{personNumber}/activity` | Attendance and the Jaa/Ei/Tyhjä/Poissa split |
| `GET /api/v1/search?query=` | Full-text search over divisions |

Descriptive fields resolve to one language via `?lang=fi|sv` (default `fi`); upstream carries
no English on vote data, so `lang=en` falls back to Finnish rather than returning blanks.
Party abbreviations and vote values stay canonical Finnish — they are identifiers, not prose.
Ballots are a separate call from the party distribution on purpose, so a caller showing only
the split never pays for 199 rows.

Cross-origin access is off unless a deployment names its origins (`VoteCheck:AllowedOrigins`).

## Architecture

```
api.eduskunta.fi ──▶ VoteCheck.Core ──▶ VoteSyncService ──▶ votecheck.db ──▶ VoteCheckWeb
 (JSON, no auth)     the single         (BackgroundService    (SQLite +       Razor pages
                     upstream boundary   in VoteCheckWeb)      FTS5 mirror,    + /api/v1
                                                              disposable)
```

The mirror is not an optimisation, it is the design: one division is ~76 KB upstream and the
search endpoint is capped at 450 POSTs per 3000 s per IP, so proxying would be both slow and
rude. The mirror is disposable — gitignored, rebuildable from the API at any time.

The solution (`VoteCheck.sln`) has seven projects:

| Project | Type | What it is |
|---|---|---|
| `VoteCheckWeb` | ASP.NET Core | **The product.** Razor Pages, `/api/v1`, the sync service and the SQLite mirror |
| `VoteCheck.Core` | Class library | The single boundary to api.eduskunta.fi: typed models, `EduskuntaClient`, an `IMemoryCache` decorator, archive enumeration |
| `VoteCheck.Core.Tests` | MSTest | `VoteCheck.Core` against real captured responses, committed as fixtures |
| `VoteCheckWeb.Tests` | MSTest | Queries, presentation and the chamber diagram against a temp database built by the real schema |
| `VoteCollector` | Class library | Legacy data layer over the retiring table API; returns `DataTable` |
| `WPFGUI` | Avalonia desktop app | The legacy GUI (named WPFGUI historically; it is Avalonia, not WPF) |
| `VoteCollectorTests` | MSTest | Tests for `VoteCollector` |

## Running the web app

```
dotnet run --project VoteCheckWeb
```

It syncs on startup, so a fresh database takes a few minutes to fill (the 2023+ window is
~2,800 divisions). To browse immediately against real data instead, use the committed sample
and give the sync an empty window so it cannot overwrite it:

```
cp tools/votecheck-sample.db /tmp/votecheck.db
VoteCheck__DbPath=/tmp/votecheck.db VoteCheck__SyncMinYear=9999 dotnet run --project VoteCheckWeb
```

See [`tools/README.md`](tools/README.md) for what that sample covers.

### Configuration

| Key | Default | Meaning |
|-----|---------|---------|
| `VoteCheck:DbPath` | `votecheck.db` | SQLite mirror. Needs a writable *directory* — WAL creates `-wal`/`-shm` beside it |
| `VoteCheck:SyncMinYear` | `2023` | Backfill floor, as a *parliamentary* year (`istuntovpvuosi`), which is not the calendar year |
| `VoteCheck:SyncPollMinutes` | `15` | How often to look for new divisions once the backfill is done |
| `VoteCheck:SyncPageSize` | `50` | Divisions per upstream request; each carries ~199 ballots (~76 KB) |
| `VoteCheck:SyncRequestDelayMs` | `500` | Politeness delay; upstream caps search at 450 requests / 3000 s / IP |
| `VoteCheck:BehindProxy` | `false` | Trust `X-Forwarded-Proto`/`-Host`. **Required behind a TLS-terminating proxy**, or canonical URLs, `og:url` and the sitemap advertise `http` |
| `VoteCheck:AllowedOrigins` | *(none)* | CORS origins for `/api/v1`. Empty means same-origin only |

## Tests

```
dotnet test VoteCheck.sln
```

Every test runs against committed fixtures or a temporary SQLite database, so the suite never
calls api.eduskunta.fi and cannot be broken by upstream being slow or down. CI runs the same
command on every pull request, then builds the container image and checks the SBOM still
describes the source tree.

That isolation has a cost worth knowing about: the two worst upstream surprises this project
has hit — a mandatory `User-Agent` header and a `"-"` where a person number belongs — both
passed straight through mocked tests and were only found against live traffic. Anything that
touches the upstream shape deserves one real request before it ships.

## Deployment

For a real deployment — UpCloud Helsinki, Caddy for automatic TLS, provisioning script and a
runbook — see **[`deploy/README.md`](deploy/README.md)**. In short:

```
# on a fresh Ubuntu server
curl -fsSL https://raw.githubusercontent.com/mashi89/VoteCheck/master/deploy/setup.sh | bash
# then, from a clone of this repo
DOMAIN=your.domain [email protected] \
  docker compose -f docker-compose.prod.yml up -d --build
```

To run the container alone, without TLS or a proxy:

```
docker compose up --build
```

The image is the whole product; `/data` is a volume holding the mirror.

Three things that will bite otherwise:

- **Persist `/data`.** The mirror is rebuildable from the API, but re-backfilling on every
  restart is slow and rude to upstream.
- **Set `VoteCheck__BehindProxy=true`** when something else terminates TLS. Permalinks are the
  product's distribution mechanism, and they will advertise the wrong scheme without it.
- **`ufw` does not protect published Docker ports.** Docker writes its own iptables rules and
  they are evaluated first, so a published port answers the whole internet however ufw is
  configured. `deploy/cloudflare-firewall.sh` filters in `DOCKER-USER` for that reason, and
  the only check that means anything is a request from another machine.

The runtime image ships no curl, so there is no `HEALTHCHECK` in the Dockerfile — point your
orchestrator's HTTP probe at `/health`. `docker-compose.yml` shows one way.

## Software bill of materials

`sbom/edustajavahti.cdx.json` lists every component the deployed image carries — NuGet closure,
the .NET and ASP.NET Core shared frameworks, the runtime base image — in CycloneDX 1.6. It is
what the Cyber Resilience Act (Regulation (EU) 2024/2847, Annex I Part II point 1) requires of
a manufacturer, and CI fails if a dependency moves without it. Regenerate with
`python3 sbom/generate.py`; see **[`sbom/README.md`](sbom/README.md)** for the two-SBOM split
and what is still missing.

## Documentation

| File | What it holds |
|---|---|
| [`design.md`](design.md) | Why the product is shaped this way, what has shipped, and what is next |
| [`docs/eduskunta-api.md`](docs/eduskunta-api.md) | How api.eduskunta.fi actually behaves — every claim checked against the live service |
| [`CONTRIBUTING.md`](CONTRIBUTING.md) | Issues first, one topic per branch, and the branch naming convention |
| [`deploy/README.md`](deploy/README.md) | The runbook for edustajavahti.fi |
| [`sbom/README.md`](sbom/README.md) | The CRA bill of materials and how it is kept true |
| [`tools/README.md`](tools/README.md) | The committed sample database and how to rebuild it |
| [`docs/screenshots/`](docs/screenshots/) | Illustrations for pull requests, as old as the commit that added them |

## Data source

Everything comes from the Finnish Parliament's open data API at `https://api.eduskunta.fi/api/v1/`.
It is unauthenticated: no key, no token, no registration. `docs/eduskunta-api.md` is the field
guide; the four things most likely to cost an afternoon:

- **A `User-Agent` header is mandatory.** Every endpoint answers `403` without one, and
  `HttpClient` sends none by default.
- **`GET /kansanedustajat` is not the sitting parliament.** It returns 1000 records from the
  all-time roster, most of them long dead. The mirror builds its member list from ballots instead.
- **Fields that look scalar are bilingual objects.** `kayttaytyminen` — the vote itself — is
  `{"fi": "Jaa", "sv": "Ja"}`, and so are group abbreviations, districts and every breakdown name.
- **`POST /search` is the only way to page the archive**, it is capped at
  `startFromIndex + maxResults <= 10000`, and the archive is larger than that. The full archive
  needs the async dataset export.

## The legacy desktop app

`WPFGUI` and `VoteCollector` are the original program: an Avalonia desktop client over
`avoindata.eduskunta.fi/api/v1/tables/...`, the parliament's **legacy** table API.

That API is scheduled for discontinuation at the **end of 2026**. Checked on 2026-09-21 it had
not gone anywhere — the table endpoints still answer `200` with live rows and there is no HTTP
redirect — so this path still works today, and the deadline is real regardless. Nothing new
should be built on it; see `design.md` for the decision about what happens to these two projects
before the shutdown.

```
dotnet run --project WPFGUI/VoteCheckGUI.csproj
dotnet publish WPFGUI/VoteCheckGUI.csproj -c Release -r win-x64 --self-contained
```

It reads four tables — `SaliDBAanestys` (divisions), `SaliDBAanestysEdustaja` (individual
ballots), `SaliDBAanestysJakauma` (party distribution) and `SeatingOfParliament` (currently
seated members) — through `OpenDataRetriever`, which returns `System.Data.DataTable`:

| Method | Description |
|--------|-------------|
| `GetVotingData(year, skipEven, count, type)` | Divisions, optionally filtered by year |
| `GetVotingDataByDate(date, skipEven, count)` | Divisions matching a date prefix |
| `GetCurrentMPs()` | Currently seated members (auto-paginated) |
| `GetEdustajaData(votingId, skipEven, partyFilter)` | Individual ballots for a division |
| `GetPartyDistData(votingId, skipEven, type)` | Party distribution for a division |
| `GetCombinedData(inputName, skipEven, count, type)` | Ballots enriched with division details |

The GUI searches by surname or date, lists currently seated members, toggles Swedish party
names, and drills down from a division to its party distribution to individual ballots.
Vote values are `Jaa`, `Ei`, `Tyhjä` and `Poissa`; the party names it maps live in
`Parties.txt`.

## Technology

| Category | Technology |
|----------|-----------|
| Language | C# |
| Runtime | .NET 8.0 |
| Web | ASP.NET Core Razor Pages, minimal APIs, Swashbuckle 6.5 |
| Storage | SQLite (`Microsoft.Data.Sqlite` 8.0) with FTS5 |
| Desktop (legacy) | [Avalonia](https://avaloniaui.net/) 11.3.12, DataGrid, Fluent theme, Inter fonts |
| JSON | Newtonsoft.Json 13.0.3 |
| Testing | MSTest |
| Deployment | Docker, Caddy, Cloudflare, UpCloud |

Prerequisites: the [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), and
internet access for whichever upstream you are pointing at — unless you are running against
the committed sample database, which needs neither.

```bash
git clone https://github.com/mashi89/VoteCheck.git
cd VoteCheck
dotnet build VoteCheck.sln
```

## Contributing

Every change has an issue, and creating it is part of the work. One topic per branch, branched
from `master`, named `category/issue-number-short-description`. The full convention, including
why, is in [`CONTRIBUTING.md`](CONTRIBUTING.md).
