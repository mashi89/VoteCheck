# Finnish Parliament Open Data API — Usage Reference

This document describes how VoteCheck uses the Finnish Parliament open data API at `avoindata.eduskunta.fi`.

---

## Base URL

```
https://avoindata.eduskunta.fi
```

Overridable at runtime with the `VOTECHECK_API_BASE_URL` environment variable (used by the mock server and integration tests).

---

## Endpoint Pattern

All requests use a single endpoint form:

```
GET /api/v1/tables/{tableName}/rows?perPage={n}&page={p}[&columnName={col}&columnValue={val}]
```

| Parameter | Description |
|---|---|
| `tableName` | Name of the API table (see below) |
| `perPage` | Rows per page. **Maximum 100** — the API rejects higher values with an error response. |
| `page` | Zero-based page index |
| `columnName` | Optional filter column name |
| `columnValue` | Value to match for `columnName` |

`columnName`/`columnValue` are URL-encoded with `Uri.EscapeDataString`.

---

## Response Envelope

Every response is a JSON object with this structure:

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
  ],
  "pkName": "AanestysId",
  "pkStartValue": null,
  "pkLastValue": null
}
```

| Field | Type | Meaning |
|---|---|---|
| `page` | int | Zero-based page index of this response |
| `perPage` | int | Requested rows per page |
| `hasMore` | bool | `true` if additional pages exist beyond this one |
| `tableName` | string | Name of the queried table |
| `columnCount` | int | Number of columns |
| `rowCount` | int | Number of rows in this page |
| `columnNames` | string[] | Column names in positional order |
| `rowData` | array of arrays | One inner array per row; values are positional, matching `columnNames` |

Rows in `rowData` are positional arrays — `rowData[i][j]` corresponds to `columnNames[j]`. VoteCheck's `InitTable` creates columns in `columnNames` order; `AppendTable` maps values by column ordinal.

**Error response**: when the API rejects a request (e.g., `perPage > 100` or an unsupported filter column), the response body contains a `"message"` field. VoteCheck treats any response with a `"message"` field as an error and throws.

---

## Tables Used

### `SaliDBAanestys` — Voting Sessions

**Purpose:** One record per voting event. Every vote is stored **twice** — once in Finnish (`KieliId=1`) and once in Swedish (`KieliId=2`). Column index 1 is always `KieliId`.

**Filters used by VoteCheck:**

| Filter column | Used in | Notes |
|---|---|---|
| `IstuntoVPVuosi` | `GetVotingData`, `GetVotingDataByDate`, `GetCombinedDataWithDateFilter` | Integer parliamentary year; filter-safe |
| `AanestysId` | `GetVotingDataOfOne` | Single-row lookup for enrichment |

**`IstuntoPvm` cannot be used as a filter.** The API reports its SQL type as `OTHER` and rejects it as a `columnName`. Date filtering is done by querying `IstuntoVPVuosi` and then filtering rows client-side where `IstuntoPvm.StartsWith(datePrefix)`.

**Columns (35 total):**

| Column | Index | Notes |
|---|---|---|
| `AanestysId` | 0 | Primary key |
| `KieliId` | 1 | `1` = Finnish (odd), `2` = Swedish (even). Language filter pivot. |
| `IstuntoVPVuosi` | 2 | Parliamentary year (integer) |
| `IstuntoNumero` | 3 | Session number |
| `IstuntoPvm` | 4 | Session date (`yyyy-MM-dd HH:mm:ss`). Cannot be an API filter. |
| `IstuntoIlmoitettuAlkuaika` | 5 | Announced session start time |
| `IstuntoAlkuaika` | 6 | Actual session start time |
| `PJOtsikko` | 7 | Presiding officer title |
| `AanestysNumero` | 8 | Vote number within session |
| `AanestysAlkuaika` | 9 | Vote start time |
| `AanestysLoppuaika` | 10 | Vote end time |
| `AanestysMitatoity` | 11 | `1` if vote was annulled |
| `AanestysOtsikko` | 12 | Vote title (language-specific) |
| `AanestysLisaOtsikko` | 13 | Additional vote title |
| `PaaKohtaTunniste` | 14 | Main agenda item identifier |
| `PaaKohtaOtsikko` | 15 | Main agenda item title |
| `PaaKohtaHuomautus` | 16 | Main agenda item note |
| `KohtaKasittelyOtsikko` | 17 | Agenda item processing stage title |
| `KohtaKasittelyVaihe` | 18 | Agenda item processing stage |
| `KohtaJarjestys` | 19 | Agenda item order |
| `KohtaTunniste` | 20 | Agenda item identifier |
| `KohtaOtsikko` | 21 | Agenda item title |
| `KohtaHuomautus` | 22 | Agenda item note |
| `AanestysTulosJaa` | 23 | Votes for (Jaa) |
| `AanestysTulosEi` | 24 | Votes against (Ei) |
| `AanestysTulosTyhjia` | 25 | Abstentions (Tyhjiä) |
| `AanestysTulosPoissa` | 26 | Absent |
| `AanestysTulosYhteensa` | 27 | Total voters |
| `Url` | 28 | Result URL path |
| `AanestysPoytakirja` | 29 | Minutes reference |
| `AanestysPoytakirjaUrl` | 30 | Minutes URL path |
| `AanestysValtiopaivaasia` | 31 | Parliamentary matter reference |
| `AanestysValtiopaivaasiaUrl` | 32 | Parliamentary matter URL path |
| `AliKohtaTunniste` | 33 | Sub-item identifier |
| `Imported` | 34 | Import timestamp |

**Columns removed by `GetVotingData` before returning to the GUI:**
`KieliId`, `KohtaTunniste`, `KohtaJarjestys`, `IstuntoVPVuosi`, `IstuntoIlmoitettuAlkuaika`, `IstuntoAlkuaika`, `AanestysLoppuaika`, `IstuntoNumero`, `AanestysNumero`, `PaaKohtaTunniste`, `PaaKohtaOtsikko`, `PaaKohtaHuomautus`, `KohtaKasittelyOtsikko`, `KohtaKasittelyVaihe`, `KohtaHuomautus`, `Url`, `AanestysPoytakirja`, `AanestysPoytakirjaUrl`, `AanestysValtiopaivaasiaUrl`, `AanestysTulosYhteensa`, `AliKohtaTunniste`, `Imported`, `AanestysLisaOtsikko`.

---

### `SaliDBAanestysEdustaja` — Individual MP Votes

**Purpose:** One row per MP per voting event. Not bilingual — no `KieliId` column. Column index 1 is `AanestysId`.

**Filters used by VoteCheck:**

| Filter column | Used in | Notes |
|---|---|---|
| `AanestysId` | `GetEdustajaData`, `GetNameData` (inside `GetCombinedData`) | Fetch all MP votes for one vote |
| `EdustajaSukunimi` | `GetCombinedDataWithDateFilter` | Surname search |

**Columns (8 total):**

| Column | Index | Notes |
|---|---|---|
| `EdustajaId` | 0 | Primary key |
| `AanestysId` | 1 | Foreign key to `SaliDBAanestys`. Used as filter column. |
| `EdustajaEtunimi` | 2 | MP first name |
| `EdustajaSukunimi` | 3 | MP surname |
| `EdustajaHenkiloNumero` | 4 | MP person number |
| `EdustajaRyhmaLyhenne` | 5 | Party abbreviation (e.g. `"sd"`, `"kesk"`) |
| `EdustajaAanestys` | 6 | Vote cast: `"Jaa"`, `"Ei"`, `"Tyhja"`, or `"Poissa"` |
| `Imported` | 7 | Import timestamp |

**Important:** because `token[1]` is `AanestysId` (not `KieliId`), the language parity filter must be bypassed (`voting=true`) for all queries on this table.

**Columns removed before returning to the GUI:** `EdustajaId`, `Imported`.

---

### `SaliDBAanestysJakauma` — Party Vote Distribution

**Purpose:** One row per party per voting event showing how each party voted. Not bilingual.

**Filter used by VoteCheck:**

| Filter column | Used in |
|---|---|
| `AanestysId` | `GetPartyDistData` / `GetVotingDistData` |

**Columns (10 total):**

| Column | Index | Notes |
|---|---|---|
| `JakaumaId` | 0 | Primary key |
| `AanestysId` | 1 | Foreign key to `SaliDBAanestys` |
| `Tyyppi` | 2 | Distribution type code |
| `Ryhma` | 3 | Full party group name in Finnish (e.g. `"Keskustan eduskuntaryhmä"`) |
| `Jaa` | 4 | Votes for |
| `Ei` | 5 | Votes against |
| `Tyhja` | 6 | Abstentions |
| `Poissa` | 7 | Absent |
| `YhteensaAanestaneet` | 8 | Total voting |
| `Imported` | 9 | Import timestamp |

The `Ryhma` value is the full Finnish parliamentary group name. `OpenDataRetriever.PartyNameToAbbreviation` maps it to the short abbreviation used in `SaliDBAanestysEdustaja.EdustajaRyhmaLyhenne` for the party drill-down.

**Columns removed before returning to the GUI:** `Imported`, `Tyyppi`.

---

### `SeatingOfParliament` — Current MPs

**Purpose:** Current seating/membership of parliament. Not bilingual. Paginated 100 rows per page; VoteCheck fetches all pages.

**Filter used:** none — full table scan.

**Columns (6 total):**

| Column | Index | Notes |
|---|---|---|
| `hetekaId` | 0 | MP identifier |
| `seatNumber` | 1 | Parliament seat number |
| `lastname` | 2 | Surname |
| `firstname` | 3 | First name |
| `party` | 4 | Party abbreviation |
| `minister` | 5 | Boolean-like flag for ministerial status |

---

## Pagination

All tables support pagination via `page` (0-based) and `perPage` (max 100).

- `hasMore: true` means at least one more page exists.
- `rowCount: 0` on page 0 means no results; VoteCheck throws `"No rows found."` in this case.
- `rowCount: 0` on a later page means the previous page was the last; iteration stops.

VoteCheck's `FetchAllPages` and `GetCurrentMPs` implement full pagination loops. Most other methods (`ReadData`-based) fetch a single page and expose `hasMore` to the GUI via `OpenDataRetriever.hasMore`.

---

## Language Filtering (`skipEven` / `voting`)

`SaliDBAanestys` stores every vote twice. The language is at column index 1 (`KieliId`):

| `KieliId` | Language |
|---|---|
| `1` (odd) | Finnish |
| `2` (even) | Swedish |

`AppendTable` filters by parity of `token[1]`:

| `voting` | `skipEven` | Behaviour |
|---|---|---|
| `true` | any | Filter bypassed — all rows included |
| `false` | `true` | Keep rows where `token[1]` is odd (Finnish) |
| `false` | `false` | Keep rows where `token[1]` is even (Swedish) |

`voting=true` must be used for `SaliDBAanestysEdustaja`, `SaliDBAanestysJakauma`, and `SeatingOfParliament` because their `token[1]` is not `KieliId`.

In the GUI, `skipEven` is passed as `!isSwedish` (default: Swedish checkbox unchecked → `skipEven=true` → Finnish rows).

---

## Known API Constraints

| Constraint | Detail |
|---|---|
| `perPage` maximum | 100. Higher values are rejected with an error `message` in the response. |
| `IstuntoPvm` not filterable | The API SQL engine reports it as type `OTHER`. Use `IstuntoVPVuosi` (integer) as the filter and post-filter `IstuntoPvm` client-side. |
