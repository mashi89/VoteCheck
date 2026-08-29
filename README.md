# VoteCheck

Explore voting records from the Finnish Parliament (Eduskunta). `VoteCheckWeb` is the
main product — a server-rendered site with shareable permalinks and a JSON API, serving
from a local mirror of [api.eduskunta.fi](https://api.eduskunta.fi/). A cross-platform
Avalonia desktop app also exists, predating it.

> **Note:** sections below describing `avoindata.eduskunta.fi` tables cover the *legacy*
> desktop path. That API has been redirecting since 30 March 2026 and shuts down at the
> end of 2026; `VoteCheckWeb` and `VoteCheck.Core` target its replacement. See `design.md`.

## Use Cases

- Check the voting result of an issue in the Finnish Parliament
- Check what a representative has lately been voting
- Check voting distribution by parties in a certain election
- Drill-down search: look for topic → voting distribution by political party → who voted what inside a party

## Architecture

The solution (`VoteCheck.sln`) contains seven projects:

| Project | Type | Description |
|---------|------|-------------|
| `VoteCheckWeb` | ASP.NET Core web app | **The product.** Razor Pages + `/api/v1` JSON, served from a SQLite mirror it syncs from api.eduskunta.fi |
| `VoteCheck.Core` | Class library | The single boundary to api.eduskunta.fi: typed models, caching decorator, archive enumeration |
| `VoteCheck.Core.Tests` / `VoteCheckWeb.Tests` | MSTest | Tests for the above, against committed fixtures and a temp SQLite database |
| `VoteCollector` | Class library | Legacy data layer over the retiring table API; returns `DataTable` |
| `WPFGUI` | Desktop application (Avalonia) | Cross-platform XAML GUI (named WPFGUI historically, but uses Avalonia — not WPF) |
| `VoteCollectorTests` | Unit test project (MSTest) | Tests for `VoteCollector` |

## Running the web app

```
dotnet run --project VoteCheckWeb
```

It syncs on startup, so a fresh database takes a few minutes to fill (the 2023+ window is
~2,800 divisions). To browse immediately against real data instead, use the committed
sample and give the sync an empty window so it cannot overwrite it:

```
cp tools/votecheck-sample.db /tmp/votecheck.db
VoteCheck__DbPath=/tmp/votecheck.db VoteCheck__SyncMinYear=9999 dotnet run --project VoteCheckWeb
```

See `tools/README.md` for what that sample covers.

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

## Deployment

For a real deployment — UpCloud Helsinki, Caddy for automatic TLS, provisioning script and
a runbook — see **[`deploy/README.md`](deploy/README.md)**. In short:

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

Two things that will bite otherwise:

- **Persist `/data`.** The mirror is rebuildable from the API, but re-backfilling on every
  restart is slow and rude to upstream.
- **Set `VoteCheck__BehindProxy=true`** when something else terminates TLS. Permalinks are
  the product's distribution mechanism, and they will advertise the wrong scheme without it.

The runtime image ships no curl, so there is no `HEALTHCHECK` in the Dockerfile — point
your orchestrator's HTTP probe at `/health`. `docker-compose.yml` shows one way.

## Technology Stack

| Category | Technology |
|----------|-----------|
| Language | C# |
| Runtime | .NET 8.0 |
| UI framework | [Avalonia](https://avaloniaui.net/) 11.3.12 (cross-platform XAML) |
| UI components | Avalonia DataGrid, Fluent theme, Inter fonts |
| JSON parsing | [Newtonsoft.Json](https://www.newtonsoft.com/json) 13.0.3 |
| HTTP client | `System.Net.Http.HttpClient` |
| Data containers | `System.Data.DataTable` |
| Testing | MSTest |

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Internet access (the app fetches live data from `avoindata.eduskunta.fi`)

## Getting Started

### Clone and build

```bash
git clone https://github.com/mashi89/VoteCheck.git
cd VoteCheck
dotnet build VoteCheck.sln
```

### Run the GUI

```bash
dotnet run --project WPFGUI/VoteCheckGUI.csproj
```

### Run in release mode

```bash
dotnet run --project WPFGUI/VoteCheckGUI.csproj -c Release
```

### Publish a self-contained executable

```bash
dotnet publish WPFGUI/VoteCheckGUI.csproj -c Release -r win-x64 --self-contained
```

## Running Tests

```bash
dotnet test VoteCollectorTests/VoteCollectorTests.csproj
```

## GUI Features

| Feature | Description |
|---------|-------------|
| **Find by Surname** | Search for an MP by surname and view their recent votes |
| **Find by Date** | Search votes by date — accepts `yyyy`, `yyyy-MM`, or `yyyy-MM-dd` |
| **Today** shortcut | Prefills the date field with today's date |
| **Current MPs** | Displays all currently seated parliament members |
| **Query count** | Controls the maximum number of results returned (default: 50) |
| **Swedish filter** | Toggles Swedish-language party names |
| **Drill-down navigation** | Double-click a vote row → party distribution; double-click a party row → individual MP votes |
| **Back button** | Returns to the previous view in the navigation history |
| **Status indicator** | Shows "Scroll down to find more" when additional pages are available |

## Data Source — Finnish Parliament Open Data API

All data is fetched from:

```
https://avoindata.eduskunta.fi/api/v1/tables/{tableName}/rows
  ?perPage={count}&page={page}&columnName={column}&columnValue={value}
```

### Tables used

| Table | Contents |
|-------|----------|
| `SaliDBAanestys` | Voting sessions |
| `SaliDBAanestysEdustaja` | Individual MP votes per session |
| `SaliDBAanestysJakauma` | Party-level vote distribution per session |
| `SeatingOfParliament` | Currently seated MPs |

### Response format

```json
{
  "page": 0,
  "perPage": 10,
  "hasMore": true,
  "rowCount": 42,
  "tableName": "SaliDBAanestysEdustaja",
  "columnNames": ["EdustajaId", "AanestysId", "EdustajaEtunimi", ...],
  "rowData": [["2745050", "13301", "Markus", ...]]
}
```

Vote values: `Jaa` (Yes), `Ei` (No), `Tyhjä` (Blank/Abstain), `Poissa` (Absent)

## `VoteCollector` Public API

| Method | Description |
|--------|-------------|
| `GetVotingData(year, skipEven, count, type)` | Fetch voting sessions, optionally filtered by year |
| `GetVotingDataByDate(date, skipEven, count)` | Fetch voting sessions matching a date prefix |
| `GetCurrentMPs()` | Fetch all currently seated MPs (auto-paginated) |
| `GetEdustajaData(votingId, skipEven, partyFilter)` | Fetch individual MP votes for a session, with optional party filter |
| `GetPartyDistData(votingId, skipEven, type)` | Fetch party-level vote distribution for a session |
| `GetCombinedData(inputName, skipEven, count, type)` | Fetch MP votes enriched with vote subject details |

## Supported Political Parties

Defined in `Parties.txt`:

| Full Name | Abbreviation |
|-----------|-------------|
| Keskustan eduskuntaryhmä | kesk |
| Kansallisen kokoomuksen eduskuntaryhmä | kok |
| Perussuomalaisten eduskuntaryhmä | ps |
| Sosialidemokraattinen eduskuntaryhmä | sd |
| Vihreä eduskuntaryhmä | vihr |
| Vasemmistoliiton eduskuntaryhmä | vas |
| Ruotsalainen eduskuntaryhmä | r |
| Kristillisdemokraattinen eduskuntaryhmä | kd |
