# VoteCheck

A cross-platform desktop application for exploring voting records from the Finnish Parliament (Eduskunta) using the [Finnish Parliament Open Data API](https://avoindata.eduskunta.fi/).

## Use Cases

- Check the voting result of an issue in the Finnish Parliament
- Check what a representative has lately been voting
- Check voting distribution by parties in a certain election
- Drill-down search: look for topic → voting distribution by political party → who voted what inside a party

## Architecture

The solution (`VoteCheck.sln`) contains three projects:

| Project | Type | Description |
|---------|------|-------------|
| `VoteCollector` | Class library | Core data-retrieval layer; queries the Open Data REST API and returns `DataTable` results |
| `WPFGUI` | Desktop application (Avalonia) | Cross-platform XAML GUI; handles user interaction and drill-down navigation (named WPFGUI historically, but uses Avalonia — not WPF) |
| `VoteCollectorTests` | Unit test project (MSTest) | Tests for `VoteCollector` |

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
