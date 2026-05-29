# Session Audit — 2026-05-29

Analysis of the log file `votecheck-2026-05-29.log` covering the session started at 21:25:23.

## Breadcrumb Trail

| Time | Action | Detail |
|------|--------|--------|
| 21:25:23 | Session started | — |
| 21:25:30 | FindBySurname | Honkasalo / 2016 |
| 21:26:18 | **Error** | "No rows found matching the date filter." |
| 21:26:21 | FindBySurname (retry) | Honkasalo / 2026 |
| 21:27:07 | Result loaded | 69 rows — **46 seconds elapsed** |
| 21:28:07 | DrillDown → Party distribution | votingId=56217 — "Luottamuslause valtioneuvostolle JAA / Tuomas Kettusen eh…" (10 parties) |
| 21:28:09 | DrillDown → MP votes | KESK — 11 MPs |
| 21:28:10 | DrillDown → Party distribution | votingId=56217 (revisited) |
| 21:28:13 | DrillDown → MP votes | PS — 18 MPs |
| 21:28:15 | Back | ← from PS MPs |
| 21:28:15 | Back | ← from party distribution |
| 21:28:16 | Reset | — |

## Key Observations

### 1. User mistyped the year (2016 instead of 2026)
The first search immediately failed. The user corrected the year and re-ran. No data loss, but the error dialog interrupted the flow.

### 2. The corrected search took 46 seconds
`GetCombinedDataWithDateFilter` for "Honkasalo / 2026" issued **125 HTTP requests** across **124 pages**, fetching **12,400+ rows** from `SaliDBAanestysEdustaja` before filtering down to 69 results. Each page request took ~300ms. The bottleneck is entirely network/pagination — not client-side processing.

### 3. The user was researching the government confidence vote
After loading results, the user drilled into `votingId=56217` ("Luottamuslause valtioneuvostolle"), specifically examining how KESK (11 MPs) and PS (18 MPs) voted.

### 4. Sorting appears broken — 12 sort events from 6 clicks
During an earlier session (20:57), the user toggled AanestysAlkuaika sorting 6 times but the log records 12 sort events. Each click fires `dataGrid_Sorting` twice because the handler is registered in both XAML and the constructor. The column toggles ascending→descending→ascending per click — net no-op.
