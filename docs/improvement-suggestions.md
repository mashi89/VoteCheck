# Improvement Suggestions

Derived from log analysis and code review. Items are ordered by impact.

---

## 1. Fix sorting double-fire (Bug)

**Severity:** High — sorting is currently broken for the user.

**Root cause:** `dataGrid_Sorting` is registered in two places:
- `Sorting="dataGrid_Sorting"` in `MainWindow.xaml` (line 93)
- `dataGrid.Sorting += dataGrid_Sorting` in the constructor (`MainWindow.xaml.cs`)

Every column header click fires the handler twice. The sort toggle flips ascending → descending → ascending, producing a net no-op. The log confirms: 12 sort events for 6 clicks, each pair 9ms apart (machine speed, not user input).

**Fix:** Remove the constructor registration `dataGrid.Sorting += dataGrid_Sorting` — the XAML binding is sufficient.

---

## 2. Reduce surname+date search from 46s to ~2s (Efficiency)

**Severity:** High — the current implementation fetches the entire career voting history of an MP surname before applying any date filter.

**Root cause:** `GetCombinedDataWithDateFilter` fetches *all pages* of `SaliDBAanestysEdustaja` for the given surname first (12,400+ rows / 124 pages for "Honkasalo / 2026"), then joins with session data. The year filter is only applied after the full fetch.

**Proposed fix — flip the query order:**
1. Fetch all `SaliDBAanestys` rows for `IstuntoVPVuosi={year}` first (this is a small set — typically 1–3 pages).
2. Filter those rows client-side by the date prefix to get a set of matching `AanestysId` values.
3. Fetch `SaliDBAanestysEdustaja` filtered by surname only, then join against the known `AanestysId` set — stopping pagination early once all matched IDs are accounted for, or limiting to a reasonable page cap.

This reduces the dominant cost (124 pages × 300ms = 37s) to roughly 2–3 pages of session data plus a handful of MP vote lookups.

---

## 3. Validate year input before running the slow search (UX)

**Severity:** Medium — the 2016/2026 typo caused an error dialog after a full round-trip to the API.

The year in the date field could be checked client-side against a reasonable range (e.g. 2000–current year) before issuing any API call. This gives the user instant feedback instead of waiting for the server to return zero rows.

---

## 4. Add structured logging sink (Observability)

**Severity:** Low — current text logs are sufficient for manual audits but not for aggregate analysis.

The current `Logger` writes free-text lines. To support cross-session analysis (average latency per operation, p95 response times, action funnels, zero-result rate), each event should also be written as a structured record — one JSON object per line is the simplest approach and requires no external dependencies.

Example line:
```json
{"ts":"2026-05-29T21:26:21.536","level":"INFO","op":"GetCombinedDataWithDateFilter","inputName":"Honkasalo","dateFilter":"2026"}
```

---

## 5. Add session-end event (Observability)

**Severity:** Low — currently there is no way to measure session length or detect whether a slow search caused the user to abandon.

Log a `[UI] Session ended` line on `Application.Exit` or `Window.Closing`. Combined with the session-start log, this gives session duration and lets you correlate long API calls with early exits.
