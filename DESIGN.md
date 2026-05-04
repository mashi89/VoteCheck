# VoteCheck — Software Design Documentation

## 1. Purpose

VoteCheck is a desktop application for browsing and exploring Finnish Parliament (Eduskunta) voting records via the open data API at `avoindata.eduskunta.fi`. Users can search votes by date or by MP surname, drill down from a vote into party vote distributions and individual MP votes, and view the current seating of parliament.

---

## 2. Solution Structure

```
VoteCheck.sln
├── VoteCollector/          — class library (net8.0, namespace MaSHi)
├── VoteCheckGUI/           — Avalonia desktop UI (net8.0, namespace VoteCheckGUI)
└── VoteCollectorTests/     — MSTest unit tests (net8.0, namespace VoteCollectorTests)
```

The GUI depends on VoteCollector. The test project depends on both (it tests VoteCollector directly, and uses `InternalsVisibleTo` to access `ColumnSizingHelper` from VoteCheckGUI).

---

## 3. VoteCollector — Class Library

### 3.1 `OpenDataRetriever` (static class)

The single public entry point for all data access. All methods are synchronous from the caller's perspective — they block internally with `.GetAwaiter().GetResult()` on the underlying async HTTP call.

#### Public fields

| Field | Type | Purpose |
|---|---|---|
| `hasMore` | `bool` | Set by `ReadData`/`GetCurrentMPs` after each fetch. `true` if the API indicated more pages exist beyond what was fetched. The GUI exposes this via an "Scroll down to find more" label. |
| `PartyNameToAbbreviation` | `Dictionary<string,string>` | Maps full Finnish parliamentary group names (as returned by `SaliDBAanestysJakauma.Ryhma`) to the short abbreviations used in `SaliDBAanestysEdustaja.EdustajaRyhmaLyhenne`. Case-insensitive. Source of truth is `Parties.txt`. |

#### Public methods

**`GetVotingData(year, skipEven, count, type)`**
Fetches voting session records from `SaliDBAanestys`. The `type` parameter is the API filter column (typically `IstuntoVPVuosi`). Returns a `DataTable` with session/metadata columns removed, key columns reordered. Returns `null` on API error (exception swallowed internally). Sets `hasMore`.

**`GetVotingDataByDate(date, skipEven, count)`**
Wraps `GetVotingData` with client-side date filtering. Accepts `yyyy`, `yyyy-MM`, or `yyyy-MM-dd`. Queries by integer year (`IstuntoVPVuosi`, which the API supports as a filter), then filters rows client-side on `IstuntoPvm.StartsWith(date)`. This workaround exists because `IstuntoPvm` is reported as SQL type `OTHER` by the API and cannot be used as a filter column directly.

**`GetPartyDistData(votingId, skipEven, type)`**
Fetches party vote distribution for a given `AanestysId` from `SaliDBAanestysJakauma`. Removes `Imported` and `Tyyppi` columns. Returns `null` (without throwing) when the API reports zero rows — callers should treat null as "no data available" and leave the current view unchanged. Other errors are re-thrown.

**`GetEdustajaData(votingId, skipEven, partyFilter)`**
Fetches individual MP votes from `SaliDBAanestysEdustaja` for a given `AanestysId`. When `partyFilter` is provided (a party abbreviation such as `"sd"`), only rows matching `EdustajaRyhmaLyhenne` (case-insensitive, whitespace-trimmed) are returned. Removes `Imported` column.

**`GetCurrentMPs()`**
Fetches all current MPs from `SeatingOfParliament`, paginating 100 rows per page until `hasMore = false`. Accumulates all pages into a single `DataTable`. Always uses `voting: true` (no language filter). Always sets `hasMore = false` on completion.

**`GetCombinedData(inputName, skipEven, count, type)`**
Single-page surname search (no date filter). Fetches one page of `SaliDBAanestysEdustaja` results, then enriches each row with an individual `SaliDBAanestys` lookup (N+1 pattern, one request per row) to add `AanestysOtsikko`, `KohtaOtsikko`, `PaaKohtaOtsikko`, `KohtaKasittelyOtsikko`, and `AanestysAlkuaika`. Fast for a quick name search but only returns one page.

**`GetCombinedDataWithDateFilter(inputName, dateFilter, skipEven, perPage)`**
All-pages surname search with date filtering. Uses a two-query join to avoid N+1:
1. Paginates through **all pages** of `SaliDBAanestysEdustaja?EdustajaSukunimi=name` via `FetchAllPages`
2. Paginates through **all pages** of `SaliDBAanestys?IstuntoVPVuosi=year` via `FetchAllPages` (raw rows, all enrichment columns present)
3. Builds an `AanestysId → session row` dictionary, applying the date prefix filter
4. Joins MP rows to the dictionary client-side, copies enrichment columns, returns the result
Total HTTP requests: (MP pages + session pages), not O(rows). Throws `"No rows found matching the date filter."` when the join yields zero rows.

#### Private methods

**`ReadData(dataUrl, skipEven, voting)`**
Core JSON-to-DataTable pipeline. Makes one HTTP GET, parses the JSON envelope, calls `InitTable` then `AppendTable`. Throws on empty body, malformed JSON, API-level error (`message` field present), or `rowCount=0`. Sets `hasMore`.

**`InitTable(input)`**
Creates an empty `DataTable` whose columns match the `columnNames` array in the API JSON response.

**`AppendTable(input, tempTable, skipEven, voting)`**
Iterates `rowData` and adds rows to `tempTable`. Applies a language filter based on `token[1]` (which is `KieliId` in bilingual tables):
- `voting=true` → filter bypassed, all rows added (used for `SaliDBAanestysJakauma` and `SeatingOfParliament`)
- `skipEven=true, voting=false` → keeps rows where `token[1]` is odd (Finnish, `KieliId=1`)
- `skipEven=false, voting=false` → keeps rows where `token[1]` is even (Swedish, `KieliId=2`)

**`GetVotingDistData(votingId, skipEven)`**
Builds the URL for `SaliDBAanestysJakauma` and calls `ReadData` with `voting=true` (party distribution data has no Finnish/Swedish duplication).

**`GetNameData(inputName, skipEven, count, type)`**
Builds the URL for `SaliDBAanestysEdustaja` and calls `ReadData` with `voting=false` (this table is bilingual).

**`GetVotingDataOfOne(votingNbr)`**
Fetches a single row from `SaliDBAanestys` by `AanestysId`. Used inside `GetCombinedData` for enrichment. Uses `voting=true`. Returns `null` on error or no rows.

**`FetchAllPages(dbName, columnName, columnValue, perPage, skipEven, voting)`**
Generic paginator used by `GetCombinedDataWithDateFilter` and `GetCurrentMPs`-style loops. Iterates page 0, 1, 2 … until `hasMore=false` or `rowCount=0`. Calls `InitTable`/`AppendTable` directly (bypasses `ReadData`). Throws `"No rows found."` when page 0 returns `rowCount=0`.

**`GetDataAsync(url)`**
Thin wrapper around `_httpClient.GetAsync`. Logs HTTP status. Throws on network failure.

### 3.2 Language filtering in detail (`skipEven` / `voting`)

`SaliDBAanestys` stores every voting session record twice — once in Finnish (`KieliId=1`) and once in Swedish (`KieliId=2`). `token[1]` in that table is `KieliId`, so the parity filter correctly selects the desired language copy.

`SaliDBAanestysEdustaja` does **not** have this duplication. Its `token[1]` is `AanestysId`, not `KieliId`. This applies to all three callers that read this table (`GetEdustajaData`, `GetNameData` inside `GetCombinedData`). All four non-`SaliDBAanestys` cases (`SaliDBAanestysEdustaja` ×2, `SaliDBAanestysJakauma`, `SeatingOfParliament`) use `voting=true` to bypass the filter entirely.

In the GUI, `skipEven` is passed as `!isSwedish`, where `isSwedish` is the state of the Swedish checkbox. Default (unchecked) gives `skipEven=true` (Finnish).

### 3.3 `Logger` (static class)

Writes structured log lines to:
1. `System.Diagnostics.Trace` (always)
2. A daily rotating file at `%LOCALAPPDATA%/VoteCheck/logs/votecheck-yyyy-MM-dd.log` (unless `FileSinkEnabled = false`)

Format: `[yyyy-MM-dd HH:mm:ss.fff] [LEVEL] message`. Exceptions append type, message, and stack trace. Logging failures are silently swallowed. The test setup sets `FileSinkEnabled = false` to keep the log directory clean.

---

## 4. API Tables Reference

| Table | Filter used | `voting` flag | Notes |
|---|---|---|---|
| `SaliDBAanestys` | `IstuntoVPVuosi` or `AanestysId` | `false` | Bilingual; `token[1]` = `KieliId`; filtered via `skipEven` |
| `SaliDBAanestysEdustaja` | `AanestysId` or `EdustajaSukunimi` | `true` | Not bilingual; `token[1]` = `AanestysId`, no `KieliId` column; filter bypassed |
| `SaliDBAanestysJakauma` | `AanestysId` | `true` | Not bilingual; no language filter |
| `SeatingOfParliament` | none (full table) | `true` | Paginated 100/page; not bilingual |

### API Constraints

- **`perPage` is capped at 100.** Requests with a higher value are rejected with an API error. All paginating code must use `perPage ≤ 100`.
- **`IstuntoPvm` cannot be used as a filter column** — the API reports its SQL type as `OTHER`. Date filtering is done by querying on the integer `IstuntoVPVuosi` and filtering client-side on `IstuntoPvm`.

### API Response Envelope (JSON)

```json
{
  "page": 0,
  "perPage": 10,
  "hasMore": false,
  "tableName": "SaliDBAanestys",
  "columnCount": 35,
  "rowCount": 2,
  "columnNames": ["AanestysId", "KieliId", ...],
  "rowData": [
    ["13259", "1", "1996", ...],
    ["13260", "2", "1996", ...]
  ]
}
```

Rows in `rowData` are positional arrays; column names come from `columnNames`. `InitTable` maps names; `AppendTable` maps values by column ordinal.

---

## 5. VoteCheckGUI — Avalonia UI

### 5.1 Layout

A single `MainWindow` (1450 × 650, resizable). The root `Grid` has three rows:
1. **Breadcrumb bar** — a `TextBlock` showing the current navigation path (e.g., `Päivähaku: 2024-03 › Lakialoite X › PS`)
2. **Progress bar** — 4 px indeterminate strip, visible only during async operations
3. **Left panel** (265 px) + **DataGrid** (remainder)

The left panel contains (top to bottom via `DockPanel`):
- Back button
- Date search section (`tbDate` + Find + Today)
- Surname search section (`tbSurname` + Find)
- Current MPs section (Fetch)
- Amount of queries field (`tbQueryCount`, default 50) — docked to bottom
- "Scroll down to find more" label — docked to bottom
- Swedish checkbox (`cbSwedish`) — docked to bottom

### 5.2 Navigation State Machine

View state is tracked by `dgStatus`, `newDataTable`, and a history stack:

| Field | Meaning |
|---|---|
| `dgStatus` | Label for the current view (`"Päivähaku"`, `"Sukunimihaku"`, `"Puoluejakaumahaku"`, `"Edustajahaku"`, `"Kansanedustajat"`) |
| `newDataTable` | The DataTable backing the currently displayed grid |
| `_navHistory` | `Stack<(DataTable, string Status, List<string> Breadcrumb)>` — full back history |

**`ShowData`** is the central navigation method. It accepts a `resetHistory` flag:
- `resetHistory: true` (top-level searches) — clears the stack, starts a fresh context
- `resetHistory: false` (drill-down, default) — pushes the current `(newDataTable, dgStatus, breadcrumb)` snapshot onto `_navHistory` before switching to the new view

**Back button** pops one entry from `_navHistory` and fully restores `DataTable`, `dgStatus`, and breadcrumb. Does nothing when the stack is empty. Multiple presses navigate arbitrarily deep back to the original search.

**Breadcrumb** is a `List<string>`. `SetBreadcrumb` replaces it entirely (top-level searches). `PushBreadcrumb` appends one label (drill-down, called *after* `ShowData` so the history snapshot captures the pre-navigation breadcrumb). Breadcrumb is restored by `btnBack_Click` directly from the history entry — there is no separate `PopBreadcrumb`.

### 5.3 Drill-Down Logic

Double-clicking a row in the DataGrid triggers `dataGrid_DoubleTapped` → `DrillDownAsync`.

The drill-down behavior depends on `dgStatus`:

| Current `dgStatus` | Action |
|---|---|
| `"Puoluejakaumahaku"` | Fetch individual MP votes (`GetEdustajaData`) filtered to the clicked party (via `PartyNameToAbbreviation` lookup). Navigates to `"Edustajahaku"`. |
| anything else | Fetch party vote distribution (`GetPartyDistData`) for the clicked `AanestysId`. Navigates to `"Puoluejakaumahaku"`. If `GetPartyDistData` returns null (no data), does nothing — the current table stays. |

The drill-down requires the current table to contain `AanestysId`. The party drill-down additionally requires `Ryhmä` (renamed from `Ryhma` by the GUI on receive).

### 5.4 DataGrid Rendering

`AutoGenerateColumns="False"`. Columns are built manually in `ApplyDataSource` using `DataGridTemplateColumn` with `FuncDataTemplate<DataRowView?>` because:
- Avalonia's `AutoGenerateColumns` reflects on `DataRowView`'s own CLR properties, not the underlying DataTable columns.
- `DataGridTextColumn` bindings do not resolve `DataRowView` indexers in Avalonia.

Each column gets a `DataRowViewComparer` as `CustomSortComparer`, enabling header-click sorting on all columns. Sorting is handled in `dataGrid_Sorting` by setting `DataView.Sort` and re-binding `ItemsSource`.

**Bold winning vote columns** (`Jaa`/`Ei`): `MarkWinningVotes` adds hidden boolean helper columns `_JaaBold`/`_EiBold`. The template column for `Jaa`/`Ei` reads the corresponding helper column to decide `FontWeight.Bold` vs `FontWeight.Normal`. Ties (equal counts) bold neither.

### 5.5 Column Sizing

`ColumnSizingHelper.GetSizing(columnName)` classifies columns as `Star` (flexible) or `Fixed` (pixel width). Star columns (`Kohta`, `Äänestysaihe`, `Ryhmä`, `Käsittely`, `Pääkohta`) stretch to fill remaining space. Fixed columns have named pixel widths in `ApplyColumnWidth`.

### 5.6 Column Renaming

After each API call the GUI renames API field names to Finnish display names using `RenameColumn`. For example: `EdustajaEtunimi` → `Etunimi`, `AanestysTulosJaa` → `Jaa`, `Ryhma` → `Ryhmä`.

### 5.7 Surname Search with Date Filter

When `tbDate` is non-empty at the time of a surname search, `FindBySurnameInternalAsync` routes to `GetCombinedDataWithDateFilter` instead of `GetCombinedData`. This fetches all pages of surname results and all pages of session data for the year, joining them client-side. The breadcrumb shows `Sukunimihaku: Name / dateFilter`. When `tbDate` is empty the fast single-page path is used.

### 5.8 Busy State

`SetBusy(true)` disables all four action buttons and shows the progress bar. `SetBusy(false)` reverses this. Called with `try/finally` in every async entry point so the UI always unlocks.

### 5.9 Date Validation

`FindByDateInternalAsync` validates the input against `DateTime.TryParseExact` with formats `yyyy-MM-dd`, `yyyy-MM`, and `yyyy` before making any API call. Invalid input shows a modal alert.

---

## 6. VoteCollectorTests — Test Project

### 6.1 Test Infrastructure

**`MockHttpMessageHandler`** — `HttpMessageHandler` subclass that returns a fixed response body and HTTP status code for every request.

**`SequentialMockHttpMessageHandler`** — returns responses from a `Queue<string>`, one per request. Used for pagination tests.

**`TestHelpers`** — static reflection helpers:
- `SetMockHttpClient(json)` — injects a single-response mock into the private static `_httpClient` field of `OpenDataRetriever` via reflection.
- `SetSequentialMockHttpClient(json1, json2, ...)` — injects a sequential mock.
- `InvokeInitTable(input)` — calls the private `InitTable` method via reflection.
- `InvokeAppendTable(input, table, skipEven, voting)` — calls the private `AppendTable` method via reflection.
- `InvokeReadData(url, skipEven, voting)` — calls the private `ReadData` method via reflection; unwraps `TargetInvocationException` to re-throw the original exception type.

### 6.2 Sample JSON (`SampleJson` static class)

All test fixtures are inline JSON string constants representing real API response shapes:

| Constant | Table | Description |
|---|---|---|
| `SaliDBAanestys_TwoRows_HasMoreFalse` | `SaliDBAanestys` | One Finnish + one Swedish row |
| `SaliDBAanestys_OneRow_HasMoreTrue` | `SaliDBAanestys` | One Finnish row, pagination not exhausted |
| `SaliDBAanestys_TwoFinnishRows_DifferentDates` | `SaliDBAanestys` | Two Finnish rows on Oct-01 and Nov-01 1996 |
| `SaliDBAanestysEdustaja_TwoRows` | `SaliDBAanestysEdustaja` | Two MP rows with odd `AanestysId=13301` |
| `SaliDBAanestysJakauma_TwoRows` | `SaliDBAanestysJakauma` | Two party rows with even `AanestysId=13260` |
| `SeatingOfParliament_ThreeRows_HasMoreFalse` | `SeatingOfParliament` | 3 MPs, single page |
| `SeatingOfParliament_TwoRows_HasMoreTrue` | `SeatingOfParliament` | 2 MPs, page 0 of 2 |
| `SeatingOfParliament_OneRow_HasMoreFalse` | `SeatingOfParliament` | 1 MP, page 1 of 2 |
| `AnyTable_ZeroRows` | any | `rowCount=0` — triggers exception path |

### 6.3 Test Classes

| Class | Coverage |
|---|---|
| `InitTableTests` | Column names, count, empty row set |
| `AppendTableTests` | Language filter (`skipEven`/`voting`), row value mapping, missing `rowData` |
| `ReadDataTests` | `hasMore` flag, zero rows, empty/malformed JSON, filter pass-through |
| `GetVotingDataTests` | Column removal, reordering, `hasMore`, language filtering |
| `GetVotingDataByDateTests` | Date prefix filtering (full date, year+month, year-only, no match) |
| `GetPartyDistDataTests` | Column removal, row count, error re-throw |
| `GetEdustajaDataTests` | Column removal, party filter (case, whitespace, null, empty, no match) |
| `PartyNameToAbbreviationTests` | All 8 parties, case-insensitive lookup, unknown key |
| `GetCurrentMPsTests` | Single-page, multi-page accumulation, `hasMore`, error cases |

The test project has no dedicated setup/teardown beyond `TestSetup.cs` which sets `Logger.FileSinkEnabled = false`.

---

## 7. Key Design Decisions and Constraints

**Static class with mutable static fields in `OpenDataRetriever`** — `finalTable`, `baseUrl`, `json`, `o`, `counter`, `saveCounter`, and `hasMore` are all static. This means `OpenDataRetriever` is not thread-safe and only one operation can be in flight at a time. The GUI serializes calls with `await Task.Run(...)` from the UI thread, which is safe in practice but would break under concurrent use. The static `_httpClient` is the seam used by tests to inject a mock.

**`ReadData` only fetches one page** — despite pagination support in the API, `ReadData` makes exactly one HTTP request. Multi-page fetching is only implemented in `GetCurrentMPs` (which loops manually). All other methods fetch a single page. The `hasMore` flag signals to the UI that more results exist.

**N+1 in `GetCombinedData`** — surname search makes one HTTP request per voting record to enrich with vote topic data. For 50 results this is 51 requests. This is the current approach; no batching or caching is implemented.

**Column ordinal assumptions** — `AppendTable` maps API row data to `DataTable` columns by `column.Ordinal` (positional), not by name. This is correct because `InitTable` creates columns in the same order as `columnNames`. The language filter reads `token[1]` by index, relying on `KieliId` always being at position 1 in bilingual tables.

**`perPage` hard limit of 100** — the API rejects any request with `perPage > 100`. All methods that accept a `perPage` or `count` parameter must stay within this limit. `FetchAllPages` and `GetCurrentMPs` handle this by looping across pages.

**`IstuntoPvm` date filtering is client-side** — the API SQL engine reports `IstuntoPvm` as type `OTHER` and rejects it as a filter column. The workaround queries by `IstuntoVPVuosi` (integer year) and post-filters rows whose `IstuntoPvm` starts with the requested date string.

**Avalonia cross-platform target** — the GUI targets `net8.0` (not `net8.0-windows`) and uses Avalonia rather than WPF, making it runnable on Linux and macOS as well as Windows. The project file sets `OutputType=WinExe` (suppresses the console window on Windows) but Avalonia's runtime handles cross-platform windowing.

**Party name mapping** — `PartyNameToAbbreviation` is hardcoded from `Parties.txt`. There are 8 parties. The GUI uses a fallback: if `Ryhma` is not found in the dictionary, it is used as-is (assumed to already be an abbreviation). This handles parties not in the map.
