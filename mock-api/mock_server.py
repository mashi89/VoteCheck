#!/usr/bin/env python3
"""
Local HTTP mock server for the Finnish Parliament open data API.

Reads fixture files from fixtures/ (produced by fetch_fixtures.py) and
serves them at http://127.0.0.1:8080, replicating the real API's response
envelope exactly — including pagination and columnName/columnValue filtering.

What this server intentionally does NOT do:
  - Language filtering (KieliId odd/even) — that is VoteCheck's job
  - Column removal — that is VoteCheck's job
  - The API's SQL type restrictions on IstuntoPvm — all columns are filterable

Usage:
    python mock_server.py                  # port 8080
    python mock_server.py --port 9000

Run VoteCheck tests against it:
    VOTECHECK_API_BASE_URL=http://127.0.0.1:8080 dotnet test VoteCollectorTests/

Run the WPF GUI against it (Windows, with env var set):
    set VOTECHECK_API_BASE_URL=http://127.0.0.1:8080
    dotnet run --project WPFGUI/WPFGUI.csproj
"""

import argparse
import json
import os
import re
import sys
from http.server import BaseHTTPRequestHandler, HTTPServer
from urllib.parse import parse_qs, urlparse

_DEFAULT_FIXTURES_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "fixtures")
FIXTURES_DIR = _DEFAULT_FIXTURES_DIR  # overwritten by --fixtures-dir at startup

# In-memory cache: table_name → {columnNames, pkName, rows}
_cache = {}


def load_fixture(table_name):
    if table_name in _cache:
        return _cache[table_name]
    path = os.path.join(FIXTURES_DIR, f"{table_name}.json")
    if not os.path.exists(path):
        return None
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    _cache[table_name] = data
    print(f"[mock] Loaded fixture {table_name}: {len(data['rows'])} rows")
    return data


def build_response(fixture, table_name, per_page, page, column_name, column_value):
    rows = fixture["rows"]
    column_names = fixture["columnNames"]

    # Apply column filter
    if column_name and column_value is not None:
        if column_name not in column_names:
            return {"message": f"Unknown column '{column_name}' in table '{table_name}'"}
        col_idx = column_names.index(column_name)
        rows = [r for r in rows if str(r[col_idx]) == str(column_value)]

    # Paginate
    start = page * per_page
    end = start + per_page
    page_rows = rows[start:end]

    return {
        "page": page,
        "perPage": per_page,
        "hasMore": end < len(rows),
        "tableName": table_name,
        "columnNames": column_names,
        "rowData": page_rows,
        "columnCount": len(column_names),
        "rowCount": len(page_rows),
        "pkName": fixture.get("pkName"),
        "pkStartValue": None,
        "pkLastValue": None,
    }


_ROUTE = re.compile(r"^/api/v1/tables/([^/]+)/rows$")


class MockHandler(BaseHTTPRequestHandler):
    def do_GET(self):
        parsed = urlparse(self.path)
        m = _ROUTE.match(parsed.path)
        if not m:
            self._send(404, {"message": f"Path not found: {parsed.path}"})
            return

        table_name = m.group(1)
        qs = parse_qs(parsed.query)

        try:
            raw_pp = qs.get("perPage", ["100"])[0]
            raw_pg = qs.get("page", ["0"])[0]
            per_page = max(1, min(int(raw_pp), 100))
            page = max(0, int(raw_pg))
        except (ValueError, IndexError):
            self._send(400, {"message": "perPage and page must be integers"})
            return

        column_name  = qs.get("columnName",  [None])[0]
        column_value = qs.get("columnValue", [None])[0]

        fixture = load_fixture(table_name)
        if fixture is None:
            self._send(404, {
                "message": (
                    f"No fixture data for table '{table_name}'. "
                    f"Run fetch_fixtures.py to download it."
                )
            })
            return

        body = build_response(fixture, table_name, per_page, page, column_name, column_value)
        self._send(200, body)

    def _send(self, status, body):
        data = json.dumps(body, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def log_message(self, fmt, *args):
        cn = self.headers.get("columnName", "-") if hasattr(self, "headers") else "-"
        print(f"[mock] {fmt % args}")


def main():
    global FIXTURES_DIR
    parser = argparse.ArgumentParser(description="VoteCheck offline mock API server")
    parser.add_argument("--port", type=int, default=8080, help="TCP port (default: 8080)")
    parser.add_argument("--host", default="127.0.0.1", help="Bind address (default: 127.0.0.1)")
    parser.add_argument(
        "--fixtures-dir",
        default=None,
        help="Path to fixture JSON files (default: fixtures/). "
             "Use sample_fixtures/ for the checked-in samples.",
    )
    args = parser.parse_args()

    if args.fixtures_dir:
        FIXTURES_DIR = os.path.abspath(args.fixtures_dir)
    else:
        # Fall back to sample_fixtures/ if fixtures/ is empty
        sample_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "sample_fixtures")
        has_real = os.path.isdir(FIXTURES_DIR) and any(
            f.endswith(".json") for f in os.listdir(FIXTURES_DIR)
        )
        if not has_real and os.path.isdir(sample_dir):
            FIXTURES_DIR = sample_dir
            print(f"Note: fixtures/ is empty; using sample_fixtures/ instead.")

    if not os.path.isdir(FIXTURES_DIR):
        print(
            f"Error: fixtures directory not found at {FIXTURES_DIR}\n"
            "Run fetch_fixtures.py first, or use --fixtures-dir sample_fixtures/",
            file=sys.stderr,
        )
        sys.exit(1)

    available = sorted(f[:-5] for f in os.listdir(FIXTURES_DIR) if f.endswith(".json"))
    if not available:
        print(
            f"Error: no fixture JSON files in {FIXTURES_DIR}. "
            "Run fetch_fixtures.py first, or use --fixtures-dir sample_fixtures/",
            file=sys.stderr,
        )
        sys.exit(1)

    print(f"Available fixtures: {', '.join(available)}")
    print(f"Listening on http://{args.host}:{args.port}")
    print(f"Set VOTECHECK_API_BASE_URL=http://{args.host}:{args.port} to use this server")
    print("Press Ctrl+C to stop.\n")

    server = HTTPServer((args.host, args.port), MockHandler)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nStopped.")


if __name__ == "__main__":
    main()
