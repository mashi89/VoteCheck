# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build the solution
dotnet build VoteCheck.sln

# Run all tests
dotnet test VoteCollectorTests/VoteCollectorTests.csproj

# Run a single test class
dotnet test VoteCollectorTests/VoteCollectorTests.csproj --filter "ClassName=GetVotingDataTests"

# Run a single test method
dotnet test VoteCollectorTests/VoteCollectorTests.csproj --filter "FullyQualifiedName~GetVotingData_ReturnsNull_WhenApiReportsZeroRows"

# Run the WPF GUI (Windows only)
dotnet run --project WPFGUI/WPFGUI.csproj
```

## Architecture

Three .NET 8 projects in one solution (`VoteCheck.sln`):

- **VoteCollector** — class library (namespace `MaSHi`) with a single static class `OpenDataRetriever` that fetches and transforms data from the Finnish Parliament open data API (`avoindata.eduskunta.fi`). Target: `net8.0`.
- **WPFGUI** — WPF desktop front-end (namespace `WPFGUI`, target `net8.0-windows`). References VoteCollector. All UI logic is in `MainWindow.xaml.cs`.
- **VoteCollectorTests** — MSTest project testing `OpenDataRetriever`. Uses reflection to inject a mock `HttpClient` into the private `_httpClient` field, and to call private methods (`InitTable`, `AppendTable`, `ReadData`). See `TestHelpers` in `OpenDataRetrieverTests.cs`.

## Key design facts

### API tables used
| API table | Purpose |
|---|---|
| `SaliDBAanestys` | Voting sessions (one Finnish + one Swedish row per vote) |
| `SaliDBAanestysEdustaja` | Per-MP votes for a given `AanestysId` |
| `SaliDBAanestysJakauma` | Party vote distribution for a given `AanestysId` |
| `SeatingOfParliament` | Current MPs (paginated, 100 per page) |

### Language filtering (`skipEven` / `voting`)
`SaliDBAanestys` stores every vote twice — once in Finnish (`KieliId=1`, odd) and once in Swedish (`KieliId=2`, even). `token[1]` in that table is `KieliId`. `AppendTable` uses `token[1]` as the filter:
- `skipEven=true` → keep odd rows (Finnish)
- `skipEven=false` → keep even rows (Swedish)
- `voting=true` bypasses this filter entirely (used for `SaliDBAanestysEdustaja`, `SaliDBAanestysJakauma`, and `SeatingOfParliament` — none of these have a `KieliId` column at `token[1]`)

In `WPFGUI`, `skipEven` is passed as `!isSwedish` (checkbox value).

### API constraints
- **`perPage` is capped at 100.** The API rejects higher values with an error. All code that sets `perPage` must use ≤ 100.
- **`IstuntoPvm` cannot be used as a filter column** (SQL type `OTHER`). See date filtering below.

### Date filtering
`IstuntoPvm` cannot be used as an API filter column (reported as `OTHER` SQL type). `GetVotingDataByDate` works around this by querying by `IstuntoVPVuosi` (integer year) and then filtering rows client-side using `IstuntoPvm.StartsWith(date)`. Accepts `yyyy`, `yyyy-MM`, or `yyyy-MM-dd`.

### Drill-down navigation in the GUI
The WPF grid supports double-click drill-down:
- From a vote list → party distribution (`GetPartyDistData`)
- From a party distribution row → individual MP votes for that party (`GetEdustajaData` with `partyFilter`)

The current view state is tracked via `dgStatus` string (`"Puoluejakaumahaku"` triggers the MP sub-drill). A Back button swaps `oldDataTable`/`newDataTable`.

### Party name mapping
`OpenDataRetriever.PartyNameToAbbreviation` maps full Finnish parliamentary group names (as returned by `SaliDBAanestysJakauma.Ryhma`) to short abbreviations used in `SaliDBAanestysEdustaja.EdustajaRyhmaLyhenne`. Source of truth: `Parties.txt` at the repository root.

### Mock HTTP pattern in tests
Tests inject a mock `HttpClient` via reflection on the private static `_httpClient` field. Use `TestHelpers.SetMockHttpClient(json)` for a single response or `TestHelpers.SetSequentialMockHttpClient(json1, json2, ...)` for paginated calls. Sample JSON payloads are in `SampleJson` static class in `OpenDataRetrieverTests.cs`.
