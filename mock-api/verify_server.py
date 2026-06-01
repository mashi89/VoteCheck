#!/usr/bin/env python3
"""
Quick smoke-test: verify that mock_server.py is responding correctly.
Run this while mock_server.py is running in another terminal.

Usage:
    python verify_server.py              # assumes port 8080
    python verify_server.py --port 9000
"""

import argparse
import json
import sys
import urllib.request

TABLES = [
    "SeatingOfParliament",
    "SaliDBAanestys",
    "SaliDBAanestysEdustaja",
    "SaliDBAanestysJakauma",
]


def get(base, table, **params):
    import urllib.parse
    qs = urllib.parse.urlencode({k: v for k, v in params.items() if v is not None})
    url = f"{base}/api/v1/tables/{table}/rows?{qs}"
    with urllib.request.urlopen(url, timeout=10) as resp:
        return json.loads(resp.read().decode("utf-8"))


def check(label, condition, detail=""):
    status = "OK " if condition else "FAIL"
    mark = "✓" if condition else "✗"
    print(f"  [{status}] {mark} {label}" + (f": {detail}" if detail else ""))
    return condition


def detect_year(base):
    """Pick the first IstuntoVPVuosi value found in the SaliDBAanestys fixture."""
    try:
        r = get(base, "SaliDBAanestys", perPage=1, page=0)
        if r.get("rowCount", 0) > 0:
            idx = r["columnNames"].index("IstuntoVPVuosi")
            return r["rowData"][0][idx]
    except Exception:
        pass
    return "2024"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=8080)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--year", default=None,
                        help="Parliamentary year to filter by (auto-detected from fixture if omitted)")
    args = parser.parse_args()
    base = f"http://{args.host}:{args.port}"

    failures = 0

    year = args.year or detect_year(base)

    # ── 1. Envelope fields present ───────────────────────────────────────────
    print(f"\nTesting {base} (year={year})")
    print("\n1. Response envelope (SeatingOfParliament):")
    try:
        r = get(base, "SeatingOfParliament", perPage=2, page=0)
    except Exception as e:
        print(f"  [FAIL] Connection error: {e}")
        print("  Is mock_server.py running?")
        sys.exit(1)

    for field in ("page", "perPage", "hasMore", "tableName", "columnNames",
                  "rowData", "columnCount", "rowCount", "pkName"):
        failures += 0 if check(f"field '{field}' present", field in r) else 1

    failures += 0 if check("page=0",    r.get("page") == 0)    else 1
    failures += 0 if check("perPage=2", r.get("perPage") == 2) else 1
    failures += 0 if check("rowCount=2", r.get("rowCount") == 2) else 1

    # ── 2. Pagination ────────────────────────────────────────────────────────
    print("\n2. Pagination (SeatingOfParliament, perPage=1):")
    r0 = get(base, "SeatingOfParliament", perPage=1, page=0)
    r1 = get(base, "SeatingOfParliament", perPage=1, page=1)
    failures += 0 if check("page 0 has 1 row",     len(r0["rowData"]) == 1)    else 1
    failures += 0 if check("page 0 hasMore=true",  r0["hasMore"] is True)      else 1
    failures += 0 if check("page 1 has 1 row",     len(r1["rowData"]) == 1)    else 1
    failures += 0 if check("page 0 and 1 differ",  r0["rowData"] != r1["rowData"]) else 1

    # ── 3. Column filter ─────────────────────────────────────────────────────
    print(f"\n3. Column filter (SaliDBAanestys by IstuntoVPVuosi={year}):")
    r = get(base, "SaliDBAanestys", perPage=5, page=0,
            columnName="IstuntoVPVuosi", columnValue=year)
    if check("returned rows", r.get("rowCount", 0) > 0):
        ki_idx = r["columnNames"].index("IstuntoVPVuosi")
        years_found = {row[ki_idx] for row in r["rowData"]}
        failures += 0 if check(f"all rows have IstuntoVPVuosi={year}", years_found == {year}) else 1
    else:
        failures += 1

    # ── 4. AanestysId filter for MP votes ────────────────────────────────────
    print("\n4. SaliDBAanestysEdustaja filter (by AanestysId):")
    r_votes = get(base, "SaliDBAanestys", perPage=1, page=0,
                  columnName="IstuntoVPVuosi", columnValue=year)
    if r_votes.get("rowCount", 0) > 0:
        aid_idx = r_votes["columnNames"].index("AanestysId")
        sample_id = r_votes["rowData"][0][aid_idx]
        r_edust = get(base, "SaliDBAanestysEdustaja", perPage=5, page=0,
                      columnName="AanestysId", columnValue=sample_id)
        col_idx = r_edust["columnNames"].index("AanestysId")
        ids = {row[col_idx] for row in r_edust["rowData"]}
        failures += 0 if check(
            f"all MP rows have AanestysId={sample_id}", ids <= {sample_id}
        ) else 1
    else:
        print("  [SKIP] No SaliDBAanestys rows available")

    # ── 5. Unknown table returns error ───────────────────────────────────────
    print("\n5. Unknown table returns error:")
    import urllib.error
    try:
        get(base, "NonExistentTable", perPage=1, page=0)
        failures += 1
        check("unknown table returns 404", False)
    except urllib.error.HTTPError as e:
        failures += 0 if check("unknown table returns 404", e.code == 404) else 1

    # ── Summary ──────────────────────────────────────────────────────────────
    print(f"\n{'All checks passed.' if failures == 0 else f'{failures} check(s) failed.'}")
    sys.exit(0 if failures == 0 else 1)


if __name__ == "__main__":
    main()
