# `api.eduskunta.fi` — what it actually does

*Every claim below was checked against the live service on 2026-09-21. Where a number is a
count of something in the archive it will have drifted since; where a claim is about behaviour
it should still hold, and ["Checking this yourself"](#checking-this-yourself) has the `curl` to
prove it either way.*

This is the Finnish Parliament's open data API, and the only upstream VoteCheck has. It is
unauthenticated, reasonably documented and mostly pleasant. The parts that are not pleasant are
the reason this file exists: each one below cost somebody an afternoon, and several of them fail
in the quiet way — a 200 with the wrong data in it — rather than the loud way.

Read it alongside `design.md` §3.1, which covers what we *do* with the API. This covers how the
API behaves.

---

## Access

| | |
|---|---|
| Base URL | `https://api.eduskunta.fi/api/v1/` |
| Spec | `https://api.eduskunta.fi/openapi.json` (OpenAPI 3.0) |
| Explorer | `https://api.eduskunta.fi/` (JS-rendered) |
| Auth | None. No key, no token, no registration. |

### A `User-Agent` is mandatory, and its absence looks like a permissions problem

Every endpoint answers **403** to a request without a `User-Agent` header. Not 400, not a
message saying what is missing — a bare 403, which reads as "you are not allowed to do this"
rather than "you left out a header".

This matters more than it sounds, because `HttpClient` sends no `User-Agent` by default. A
.NET client that works perfectly from `curl` will 403 on every call until somebody notices.
`EduskuntaClient` sets `DefaultUserAgent` in its constructor for exactly this reason, and that
line is load-bearing: remove it and nothing in the project can reach upstream.

Any non-empty string works. There is no allowlist and no registration.

### The legacy `avoindata.eduskunta.fi` table API

The old table API (`avoindata.eduskunta.fi/api/v1/tables/...`) is scheduled for discontinuation
at the end of 2026, and `api.eduskunta.fi` is its replacement. Nothing new should be built on it.

**But note it has not gone anywhere yet.** As of 2026-09-21 the table endpoints still answer
`200` with live rows, and `avoindata.eduskunta.fi` does not redirect at the HTTP level —
contrary to what the banner in `design.md` says. If a migration deadline ever needs to be
planned around, check it rather than trusting either document; the announcement lives on the
site's own JS-rendered pages, not in a header.

### Rate limit: 450 POSTs per 3000 seconds per IP

The spec states this as a flat cap on POST requests — which in practice means `/search`,
`/search/count`, `/search/dataset` and `/aggregations/*`, since those are the only POSTs.
Roughly seven and a half requests a minute, sustained.

That number is why `VoteSyncService` sleeps `SyncRequestDelayMs` (default 500 ms) between
requests, and the reason is worth stating rather than just the number: a full backfill is
thousands of requests, and the penalty for exceeding the cap falls on the whole origin IP, not
on one job. A sync that trips the limit takes the live site down with it. The delay is cheap
insurance on a job that has no deadline.

GETs appear not to be capped, but nothing advertises that, and no rate-limit headers come back
on any response — there is no budget to read, only the documented number to stay under.

---

## The endpoints we use

### `GET /kansanedustajat` — **not** the current MPs

The name and the spec summary ("Fetch all MPs") both suggest this returns the sitting
parliament. It does not. It returns **1000 records from the all-time roster**, most of them
long dead — 929 former members, 67 sitting, 4 interrupted, with birth years going back to 1839.
There are no paging parameters, so 1000 is simply where it stops.

Two consequences:

- Only about a third of the 200 sitting MPs appear in it. Anything that needs the current
  parliament must filter on `edustajantoimenTila` — one of the few fields here that is a plain
  string rather than a `{fi, sv}` object, with values `Nykyinen`, `Entinen`, `Keskeytynyt` — and
  should not expect to find everyone.
- **The response is an envelope, not an array**: `{"kansanedustajat": [...], "searchMetadata": null}`.
  `EduskuntaClient.GetMpsAsync` deserializes it as a bare `List<Mp>`, which cannot succeed
  against live output. There is no captured fixture for this endpoint, which is how it went
  unnoticed — the single-MP endpoint has one and is fine.

`GET /kansanedustajat/{henkilonumero}` returns one MP directly, and its field names differ from
the ones in a ballot: `henkilonro`, `kutsumanimi`, `etunimet` here, against `henkilonumero`,
`etunimi` in `aanestystapahtumat`. They are different records about the same person, not the
same record twice.

### `GET /taysistunnot/aanestykset/{aanestystunnus}` — one division, and it carries everything

The identifier is `{vpvuosi}-{istuntonumero}-{aanestysnumero}`, e.g. `2026-80-3`. An unknown one
gives a clean `404 {"message":"No matches for given id"}`.

One response is about **76 KB**, and that is because it holds the whole division:

- `aanestystulos` — the tally (`jaa`, `ei`, `tyhjia`, `poissa`, `yhteensa`).
- `aanestystapahtumat` — **every ballot**, one per seat. 199, not 200: the Speaker presides and
  does not vote. Presiding must never be counted as an absence.
- `eduskuntaryhmaJakaumat` — the split by parliamentary group, pre-computed.
- `hallitusoppositioJakaumat` — government against opposition, as `Hallitusryhmät` /
  `Oppositioryhmät`.
- `vaalipiiriJakaumat` — the split by electoral district, 13 of them.

**The first and last of those three are available and unused.** The product shows the group
split; a government-versus-opposition view and a by-district view need no extra request and no
aggregation of our own — the numbers are already in a payload the mirror is downloading anyway.
Worth knowing before anyone builds either by counting ballots.

The subject of the vote is `kohta.otsikko`. It is *not* `aanestysotsikko`, which describes the
ballot options ("proposal X JAA / proposal Y EI") and tells a reader nothing.

### `GET /taysistunnot/uusimmat-aanestykset` — a window, not a feed

The spec is precise about what this is, and the precision matters: *all voting results on
matters from during the last 30 days, or at least from matters having had votes in the 100 most
recent votes, **grouped by parliamentary matters***.

So three things are true of it that "uusimmat" does not suggest:

- **It is grouped, so the JSON is nested** — a list of lists, unlike every other vote endpoint.
  `EduskuntaClient.GetRecentVotesAsync` flattens it.
- **It is not in chronological order.** A recent sample came back
  `12 Jun, 17 Jun, 23 Jun, 23 Jun, 23 Jun, 18 Sep, 23 Jun, 29 May, 17 Jun, 23 Jun`. Anything
  presenting "the latest votes" must sort explicitly.
- **It goes quiet during recess.** The 30-day window is empty over the summer, so the fallback
  clause takes over and the endpoint serves months-old divisions. A check during the recess
  found nothing newer than June here while `/search` was already returning September divisions —
  which looked like the endpoint being stale, but is the documented behaviour working as
  written. Re-checked in session on 2026-09-21 it does include the newest sitting day.

The honest summary is that it answers "what has parliament been voting on recently", not "what
are the most recent divisions". For the second question, sort `/search` by `istuntopvm`
descending. That is what the sync does.

### `POST /search` — the only way to page the archive

Everything else is a lookup by identifier. `/search` is the only surface that can walk backwards
through 15,000-odd divisions, which makes it the backbone of the mirror and the place where all
the sharp edges are. It has its own section below.

`POST /search/count` takes the same body, returns `{"count": n}`, and is the cheap way to ask
"how many" without transferring results. It is not subject to the 10000 window.

`GET /search?q=` exists but is not a free-text endpoint — `q` takes **the entire search request
JSON, url-encoded**, as a string. `?q=alkoholilaki` does not return a bad request; it returns
**500 Internal server error**. Use the POST.

### `GET /valtiopaivaasiat/{eduskuntatunnus}` — the direct matter lookup

`GET /valtiopaivaasiat/HE%20113%2F2026%20vp` returns the full matter record, and a nonexistent
identifier gives `404 {"message":"No matches for given id HE 99999/2026 vp"}`.

**This is worth knowing because `EduskuntaClient.GetMatterAsync` does not use it.** It fetches
matters through `POST /search` with a free-text query instead, which is a rate-limited POST,
returns a ranked guess rather than an exact match, and therefore needs the identifier comparison
described below to be safe. The direct GET is exact, cheap, and outside the POST cap. If the
matter-fetching pass is ever revisited, start here.

---

## `/search`: the quirks that cost time

### The request shape

```json
{
  "category":       "aanestys",
  "maxResults":     50,
  "startFromIndex": 0,
  "sort":           [{ "property": "istuntopvm", "ascending": true }],
  "expression":     { "property": "istuntovpvuosi", "from": 2023, "to": 9999 }
}
```

`sort` is an array of `{property, ascending}`, and **the spec does document it that way** — both
in the `Sort` schema and in every example. A note in this project's history claims it differs
from the spec; it does not, and did not need rediscovering.

### `startFromIndex + maxResults` may not exceed 10000

Past that the request is refused outright:

```
400 {"message":"Invalid max results + start from index: Requested: 10010, Limit: 10000","status":400}
```

The boundary is inclusive — `startFromIndex: 9990, maxResults: 10` succeeds, `maxResults: 20`
does not. `EduskuntaClient.MaxSearchWindow` encodes this and throws before the request rather
than after, so the failure names the cause.

The archive is larger than the window: an unrestricted `aanestys` search reports 15,574 total
results, so **the whole vote archive cannot be paged from `/search` at all**. The ways past it
are to narrow the filter until the matching set fits under 10000 — which is what
`SyncMinYear` does — or to use `POST /search/dataset`, an async job whose result size is bounded
only by the query. The dataset job returns a `jobId` to poll, requires both `category` and
`sort`, and 429s when too many jobs run at once.

### The results are envelopes, not objects

Every hit is a fixed-shape wrapper with one slot per category — `kansanedustaja`,
`valtiopaivaasia`, `aanestys`, `asiakirja`, `puheenvuoro` and the rest — of which exactly one is
populated and the others are `null`. Read the slot matching the category you asked for.

### `fields` works — but it looks like it doesn't

This one is worth reading twice, because the obvious test gives the wrong answer.

A `fields` projection (`{"operation": "include" | "exclude", "list": [...]}`) **is honoured**.
What it does *not* do is remove keys from the result object: the envelope keeps all twenty
`aanestys` keys, and the ones outside an `include` list come back as `null`. So a check that
counts keys — which is the natural check — sees twenty either way and concludes the projection
was ignored. It wasn't. Count bytes instead:

| Request | Response |
|---|---|
| no `fields` | 76,622 bytes, 199 ballots |
| `include: ["id"]` | **922 bytes**, everything else `null` |
| `exclude: ["aanestystapahtumat"]` | **5,425 bytes**, tallies and all three jakaumat intact |

A note in this project's history records `fields` as "accepted and silently ignored". That is
wrong, and expensively so: excluding `aanestystapahtumat` alone turns a 76 KB division into a
5 KB one. Any pass that wants tallies without ballots — a backfill of metadata, a count, a
re-walk to fill a new column — is currently moving fifteen times more data than it needs.

### Category `aanestys`: filter by `istuntovpvuosi`, and know what that year is

```json
"expression": { "property": "istuntovpvuosi", "from": 2023, "to": 9999 }
```

`from` is inclusive and `to` is exclusive, per the `RangeExpression` schema. The property arrives
in payloads as a *string* but the range expression wants integers.

**`istuntovpvuosi` is the parliamentary year, not the calendar year**, and the two disagree by
enough to matter. Filtering divisions from 2023 onward:

- by `istuntovpvuosi` — **2,783** divisions
- by `istuntopvm` (the actual sitting date) — **1,887** divisions

Nearly nine hundred divisions sat on a calendar date before 2023 but belong to a parliamentary
year of 2023 or later. Neither number is wrong; they answer different questions. The mirror's
`SyncMinYear` has always meant the parliamentary year, so the filter matches the name.

Dates use a *different* expression shape: `{"property": "istuntopvm", "fromDate": "2023-01-01",
"toDate": "2099-12-31"}`. Passing `from`/`to` to a date property fails with the generic
`ANY_OF: no subschema out of 15 matched`, which names neither the property nor the problem.

### Category `valtiopaivaasia`: an expression does work, with the right key

The recorded finding here is that this category accepts only a free-text `query` and rejects an
`expression` on `eduskuntatunnus` with a 400. **That is not right, and the distinction is
worth having.** What actually happens:

| Body | Result |
|---|---|
| `{"property": "eduskuntatunnus", "eq": ...}` | 400 |
| `{"property": "eduskuntatunnus", "value": ...}` | 400 |
| `{"property": "eduskuntatunnus", "stringValue": "HE 113/2026 vp"}` | **200, exactly 1 result** |
| `{"property": "eduskuntatunnus", "match": "HE 113/2026 vp"}` | 200, **202,828 results** |

`eq` and `value` are not expression keys anywhere in this API. The exact-match key is
`stringValue` (`StringEqualsExpression`, valid where a property is indexed as `keyword`);
`match` is the fuzzy one (`TextMatchExpression`, for `text`-indexed properties) and on an
identifier it matches essentially the entire corpus, ranked. The 400 that led to the original
conclusion was the schema rejecting an invented key, not the category rejecting filters.

Both wrong keys fail with the same unhelpful message — `ANY_OF: no subschema out of 15 matched`,
naming neither the offending key nor the fifteen alternatives. When a search 400s with that,
the answer is always in `components.schemas.ExpressionType` in the spec.

### Always compare the identifier you got back against the one you asked for

`GetMatterAsync` uses a free-text `query` for the matter lookup, and free text is ranked, not
filtered: a search for one identifier can return a *different* matter that merely cites it.
Nothing about the response says so — it is a 200 with a plausible record in it.

So the returned `eduskuntatunnus` is compared against the requested one and a mismatch is
discarded. This is the failure mode that justifies the check existing: attaching the wrong
subject keywords to a division is worse than attaching none, because it is wrong in a way
nobody will notice and everybody will believe.

(The direct `GET /valtiopaivaasiat/{eduskuntatunnus}` has no such problem — it matches or 404s.)

### Which categories exist

Checked directly. `aanestys`, `valtiopaivaasia`, `asiakirja`, `kansanedustaja`, `puheenvuoro`
and `tapahtuma` all return 200. `asia` and `he` do not exist —
`400 {"message":"Invalid category: he"}`, which is at least a message that says what is wrong.

`langCode` is documented as supported by `kansanedustaja`, `valtiopaivaasia` and `aanestys`, but
sending `"sv"` to an `aanestys` search does not strip the Finnish side: bilingual fields still
come back as `{fi, sv}`. Pick the language key yourself.

---

## Payload shapes worth knowing before you model them

**Bilingual fields are objects, not strings — including ones that look scalar.** `kayttaytyminen`
(the ballot itself) is `{"fi": "Jaa", "sv": "Ja"}`. So are `edkryhmalyhenne` (`{"fi": "kok",
"sv": "saml"}`), `eduskuntaryhma`, `vaalipiiri`, `sukupuoli`, `eduskuntatunnus`, and every
jakauma `nimi`. MP payloads add an `en` key on some fields and not others. This is
`LocalizedText` in the models, and modelling any of these as `string` produces a client that
compiles, passes its mocked tests, and fails on live data.

**`kohta.asiakirjat.paaasiakirjaEduskuntatunnus` is the matter identifier**, e.g.
`{"fi": "HE 113/2026 vp", "sv": "RP 113/2026 rd"}`. It is the key for
`/valtiopaivaasiat/{...}` and `/taysistunnot/asian-aanestykset/{...}`. Present on every
division checked — 60 of 60 in the original survey, and 60 of 60 again in a fresh sample
taken for this document. Alongside it, `paaasiakirjaAsiatyyppi` carries the bare type code
(`"HE"`) as a plain string.

**`puhemies.henkilonumero` is sometimes the string `"-"`.** Two divisions in the archive
carry it — the earliest is `2023-14-1` — meaning no Speaker was recorded. Every numeric id in
this API arrives as a JSON *string* to begin with (`henkilonumero`, `henkilonro`,
`istuntovpvuosi`, `istuntonumero`, `aanestysnumero`), so a plain `int` property is already a
parse; `"-"` makes it a throwing one. `LenientInt32Converter` turns anything unparseable into
`null`, because the alternative is two rows failing an entire sync page.

**`istuntopvm` is a date with an offset** — `"2026-09-18+03:00"` — which is not a valid
`DateTimeOffset` and does not parse as one. The models keep it as a string. Other timestamps
are ordinary ISO 8601.

**A `valtiopaivaasia` carries `asiasanat`, and they arrive alphabetically.** The subject
keywords are YSO ontology terms, each with an `aiheteksti`, a `muutunnus` (the YSO URI) and an
`asiasanaJarjestys`. The ordering number is the meaningful one — it says which terms are the
headline subjects — and upstream returns them **sorted by text instead**. For HE 113/2026 vp:

```
2 kotitalousvähennys      1 tulovero
6 kulttuurisetelit        2 kotitalousvähennys
5 luontoisedut     →      3 matkakustannukset
3 matkakustannukset       4 verovähennykset
1 tulovero                5 luontoisedut
4 verovähennykset         6 kulttuurisetelit
7 verovapaus              7 verovapaus
```

Rendering them as received puts `kulttuurisetelit` second on a page whose subject is income tax.
`Valtiopaivaasia.Keywords()` sorts by `asiasanaJarjestys` for this reason.

The same record carries `asiakirjatyyppinimi` (`"Hallituksen esitys"`, the readable form of the
bare `HE`) and `kokonaispaatosnimi` (`"Hyväksytty"`) — the fate of the **whole matter**, not the
result of any one division. A bill can lose an amendment vote and pass anyway, so anything
showing it has to say which of the two it is showing.

---

## Related: eduskunta.fi page URLs

Not the API, but needed next to it, since a matter identifier is only useful if a reader can
follow it somewhere.

```
https://www.eduskunta.fi/valtiopaivaasiat/{TYPE}+{NUMBER}/{YEAR}
```

`HE 113/2026 vp` becomes `.../valtiopaivaasiat/HE+113/2026`. That URL answers **307** and
redirects to the site's real address for the matter
(`/asiat-ja-aanestykset/valtiopaivaasiat/HE%20113%2F2026%20vp`), so any check has to follow
redirects — the 307 is returned for real and imaginary documents alike, and only the final
response distinguishes them. Followed through: verified 200 for `HE`, `VNS`, `VNT` and `VK`;
a nonexistent document ends at a **404** rather than resolving to something misleading, so a
wrong guess fails visibly.

**A combined identifier has no single page.** `LA 1, 18/2023 vp` names two documents at once,
and there is nothing to link to. `Valtiopaivaasia.PublicUrl()` returns null for anything whose
number is not a plain integer rather than guessing at one half of it.

---

## Checking this yourself

This document will go stale. Everything in it is a single cheap request; none of the GETs count
against the POST cap, and the POSTs below are a handful out of 450.

```bash
UA='VoteCheck/1.0'
B=https://api.eduskunta.fi/api/v1

# The User-Agent rule: 403 without, 200 with.
curl -s -o /dev/null -w '%{http_code}\n' -H 'User-Agent:' "$B/kansanedustajat"
curl -s -o /dev/null -w '%{http_code}\n' -A "$UA"        "$B/kansanedustajat"

# One division, whole. Tally, ballot count, jakaumat, matter identifier.
curl -s -A "$UA" "$B/taysistunnot/aanestykset/2026-80-3" \
  | python3 -c 'import sys,json; d=json.load(sys.stdin); \
print(d["aanestystulos"]); \
print("ballots", len(d["aanestystapahtumat"])); \
print({k: len(d[k]) for k in ("eduskuntaryhmaJakaumat","hallitusoppositioJakaumat","vaalipiiriJakaumat")}); \
print(d["kohta"]["asiakirjat"]["paaasiakirjaEduskuntatunnus"])'

# The parliamentary year is not the calendar year. Expect two different numbers.
curl -s -A "$UA" -H 'Content-Type: application/json' -d \
  '{"category":"aanestys","expression":{"property":"istuntovpvuosi","from":2023,"to":9999}}' \
  "$B/search/count"
curl -s -A "$UA" -H 'Content-Type: application/json' -d \
  '{"category":"aanestys","expression":{"property":"istuntopvm","fromDate":"2023-01-01","toDate":"2099-12-31"}}' \
  "$B/search/count"

# The 10000 window. First succeeds, second is a 400 naming the limit.
curl -s -A "$UA" -H 'Content-Type: application/json' -d \
  '{"category":"aanestys","maxResults":10,"startFromIndex":9990,"expression":{"property":"istuntovpvuosi","from":2000,"to":9999}}' \
  "$B/search" | head -c 120; echo
curl -s -A "$UA" -H 'Content-Type: application/json' -d \
  '{"category":"aanestys","maxResults":20,"startFromIndex":9990,"expression":{"property":"istuntovpvuosi","from":2000,"to":9999}}' \
  "$B/search"; echo

# fields really does project — compare the byte counts, not the key counts.
for F in '' ',"fields":{"operation":"include","list":["id"]}' ',"fields":{"operation":"exclude","list":["aanestystapahtumat"]}'; do
  curl -s -o /dev/null -w "%{size_download} bytes\n" -A "$UA" -H 'Content-Type: application/json' -d \
    "{\"category\":\"aanestys\",\"maxResults\":1,\"startFromIndex\":0,\"sort\":[{\"property\":\"istuntopvm\",\"ascending\":false}]$F,\"expression\":{\"property\":\"istuntovpvuosi\",\"from\":2026,\"to\":9999}}" \
    "$B/search"
done

# Exact matter lookup by expression (1 result) versus fuzzy (the whole corpus).
curl -s -A "$UA" -H 'Content-Type: application/json' -d \
  '{"category":"valtiopaivaasia","maxResults":1,"expression":{"property":"eduskuntatunnus","stringValue":"HE 113/2026 vp"}}' \
  "$B/search" | head -c 120; echo
curl -s -A "$UA" -H 'Content-Type: application/json' -d \
  '{"category":"valtiopaivaasia","maxResults":1,"expression":{"property":"eduskuntatunnus","match":"HE 113/2026 vp"}}' \
  "$B/search" | head -c 120; echo

# ...and the direct GET, which needs no search at all.
curl -s -o /dev/null -w '%{http_code}\n' -A "$UA" "$B/valtiopaivaasiat/HE%20113%2F2026%20vp"
curl -s -o /dev/null -w '%{http_code}\n' -A "$UA" "$B/valtiopaivaasiat/HE%2099999%2F2026%20vp"

# The two divisions with no Speaker recorded.
curl -s -A "$UA" -H 'Content-Type: application/json' -d \
  '{"category":"aanestys","maxResults":2,"expression":{"property":"puhemies.henkilonumero","stringValue":"-"}}' \
  "$B/search" | head -c 120; echo

# /kansanedustajat is an envelope around the all-time roster, not a list of the sitting MPs.
curl -s -A "$UA" "$B/kansanedustajat" \
  | python3 -c 'import sys,json,collections; d=json.load(sys.stdin); \
print("top-level keys:", list(d)); \
print(len(d["kansanedustajat"]), "records"); \
print(collections.Counter(m["edustajantoimenTila"] for m in d["kansanedustajat"]))'

# eduskunta.fi matter pages: 307 either way, so follow the redirect for the real answer.
curl -sL -o /dev/null -w '%{http_code}\n' "https://www.eduskunta.fi/valtiopaivaasiat/HE+113/2026"
curl -sL -o /dev/null -w '%{http_code}\n' "https://www.eduskunta.fi/valtiopaivaasiat/ZZ+999/2026"
```

When a search 400s with `ANY_OF: no subschema out of 15 matched`, the fifteen subschemas are in
the spec and the message will never name them:

```bash
curl -s https://api.eduskunta.fi/openapi.json \
  | python3 -c 'import sys,json; s=json.load(sys.stdin)["components"]["schemas"]; \
print([r["$ref"].split("/")[-1] for r in s["ExpressionType"]["oneOf"]])'
```
