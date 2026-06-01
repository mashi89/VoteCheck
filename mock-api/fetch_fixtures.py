#!/usr/bin/env python3
"""
Download sample data from the Finnish Parliament open data API and save
as fixture JSON files that mock_server.py can serve offline.

Each table is stored as one JSON file in fixtures/ containing all fetched
rows in the raw API format (columnNames + rows list).  The mock server then
handles filtering and pagination dynamically, mirroring exactly what the
real API does — without any language filtering or column removal (those
transformations are VoteCheck's job).

Usage:
    python fetch_fixtures.py                     # 2024, 10 sample votes
    python fetch_fixtures.py --year 2023
    python fetch_fixtures.py --max-votes 20      # more MP/party data
    python fetch_fixtures.py --surnames Aaltonen Aho
"""

import argparse
import json
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

API_BASE = "https://avoindata.eduskunta.fi"
FIXTURES_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "fixtures")
PER_PAGE = 100  # API maximum


def fetch_page(table, per_page=PER_PAGE, page=0, column_name=None, column_value=None):
    params = {"perPage": per_page, "page": page}
    if column_name:
        params["columnName"] = column_name
        params["columnValue"] = column_value
    url = f"{API_BASE}/api/v1/tables/{table}/rows?" + urllib.parse.urlencode(params)
    req = urllib.request.Request(url, headers={"User-Agent": "VoteCheck-MockFetcher/1.0"})
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"HTTP {e.code} for {url}: {body[:200]}") from e


def fetch_all_pages(table, column_name=None, column_value=None, max_rows=None):
    """Paginate through all pages and return merged {columnNames, pkName, rows}."""
    all_rows = []
    column_names = None
    pk_name = None
    page = 0

    while True:
        print(f"    page {page}...", end=" ", flush=True)
        data = fetch_page(table, page=page, column_name=column_name, column_value=column_value)

        if column_names is None:
            column_names = data.get("columnNames", [])
            pk_name = data.get("pkName")

        row_count = data.get("rowCount", 0)
        if row_count == 0:
            print("0 rows")
            break

        batch = data.get("rowData", [])
        all_rows.extend(batch)
        print(f"{len(all_rows)} rows so far")

        if max_rows is not None and len(all_rows) >= max_rows:
            all_rows = all_rows[:max_rows]
            break

        if not data.get("hasMore", False):
            break

        page += 1
        time.sleep(0.25)

    return {"tableName": table, "columnNames": column_names, "pkName": pk_name, "rows": all_rows}


def merge_into(target, source):
    """Append source rows into target, assuming identical schema."""
    if target["columnNames"] is None:
        target["columnNames"] = source["columnNames"]
        target["pkName"] = source["pkName"]
    target["rows"].extend(source["rows"])


def save_fixture(table, data):
    os.makedirs(FIXTURES_DIR, exist_ok=True)
    path = os.path.join(FIXTURES_DIR, f"{table}.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    print(f"  Saved {len(data['rows'])} rows → {os.path.relpath(path)}")


def main():
    parser = argparse.ArgumentParser(
        description="Download VoteCheck API fixtures for offline testing"
    )
    parser.add_argument(
        "--year", default="2024",
        help="Parliamentary year for SaliDBAanestys (default: 2024)"
    )
    parser.add_argument(
        "--max-votes", type=int, default=10,
        help="Number of voting sessions to fetch full MP/party breakdown for (default: 10)"
    )
    parser.add_argument(
        "--surnames", nargs="*", metavar="NAME",
        help="Also fetch SaliDBAanestysEdustaja rows for these MP surnames"
    )
    args = parser.parse_args()

    # ── 1. SeatingOfParliament (all current MPs, ~200 rows) ──────────────────
    print("Fetching SeatingOfParliament (all current MPs)...")
    mps = fetch_all_pages("SeatingOfParliament")
    save_fixture("SeatingOfParliament", mps)

    # ── 2. SaliDBAanestys for the chosen year ────────────────────────────────
    print(f"\nFetching SaliDBAanestys for year={args.year}...")
    votes = fetch_all_pages("SaliDBAanestys", column_name="IstuntoVPVuosi", column_value=args.year)
    save_fixture("SaliDBAanestys", votes)

    if not votes["rows"]:
        print("No voting sessions found. Skipping MP and party data.")
        return

    # ── 3. Pick Finnish-row AanestysIds (KieliId=1) for the sample ──────────
    col = votes["columnNames"]
    id_idx = col.index("AanestysId")
    ki_idx = col.index("KieliId")
    finnish_ids = [r[id_idx] for r in votes["rows"] if str(r[ki_idx]) == "1"]
    sample_ids = finnish_ids[: args.max_votes]
    print(f"\n{len(sample_ids)} voting IDs selected: {sample_ids}")

    # ── 4. SaliDBAanestysEdustaja and SaliDBAanestysJakauma per vote ─────────
    all_edust = {"tableName": "SaliDBAanestysEdustaja", "columnNames": None, "pkName": None, "rows": []}
    all_jakauma = {"tableName": "SaliDBAanestysJakauma",  "columnNames": None, "pkName": None, "rows": []}

    for i, vid in enumerate(sample_ids):
        print(f"\n  [{i + 1}/{len(sample_ids)}] AanestysId={vid}")

        print(f"  SaliDBAanestysEdustaja:")
        edust = fetch_all_pages("SaliDBAanestysEdustaja", column_name="AanestysId", column_value=vid)
        if edust["rows"]:
            merge_into(all_edust, edust)

        print(f"  SaliDBAanestysJakauma:")
        jakauma = fetch_all_pages("SaliDBAanestysJakauma", column_name="AanestysId", column_value=vid)
        if jakauma["rows"]:
            merge_into(all_jakauma, jakauma)

        time.sleep(0.3)

    save_fixture("SaliDBAanestysEdustaja", all_edust)
    save_fixture("SaliDBAanestysJakauma", all_jakauma)

    # ── 5. Optional: extra surname rows ─────────────────────────────────────
    if args.surnames:
        print(f"\nFetching extra SaliDBAanestysEdustaja rows by surname...")
        for surname in args.surnames:
            print(f"  EdustajaSukunimi={surname}:")
            extra = fetch_all_pages(
                "SaliDBAanestysEdustaja", column_name="EdustajaSukunimi", column_value=surname
            )
            if extra["rows"]:
                merge_into(all_edust, extra)
        # Re-save with the extra rows merged in
        save_fixture("SaliDBAanestysEdustaja", all_edust)

    print("\nDone.")
    print("Start the mock server with:")
    print("  python mock_server.py")
    print("\nThen run tests against it with:")
    print("  VOTECHECK_API_BASE_URL=http://127.0.0.1:8080 dotnet test VoteCollectorTests/")


if __name__ == "__main__":
    main()
