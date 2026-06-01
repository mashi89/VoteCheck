#!/usr/bin/env python3
"""
Export the four VoteCheck tables from a local MariaDB/MySQL database into
the same fixture JSON format that mock_server.py serves.

All values are stored as strings (or null), matching the real API's behaviour.

Setup:
    pip install PyMySQL

Usage:
    python fetch_from_db.py                         # defaults: localhost, root, no password
    python fetch_from_db.py --database parldatadb
    python fetch_from_db.py --host 127.0.0.1 --port 3306 --user root --password secret --database mydb
    python fetch_from_db.py --tables SaliDBAanestys SeatingOfParliament

Connection can also be configured via environment variables:
    MYSQL_HOST, MYSQL_PORT, MYSQL_USER, MYSQL_PASSWORD, MYSQL_DATABASE
"""

import argparse
import json
import os
import sys
from datetime import date, datetime

try:
    import pymysql
    import pymysql.cursors
except ImportError:
    print(
        "PyMySQL is not installed. Run:\n  pip install PyMySQL",
        file=sys.stderr,
    )
    sys.exit(1)

FIXTURES_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "fixtures")

ALL_TABLES = [
    "SaliDBAanestys",
    "SaliDBAanestysEdustaja",
    "SaliDBAanestysJakauma",
    "SeatingOfParliament",
]


def to_str(value):
    """Convert a SQL value to string (or None), matching the real API's JSON shape."""
    if value is None:
        return None
    if isinstance(value, (datetime, date)):
        return str(value)
    return str(value)


def get_primary_key(cursor, table):
    """Return the primary key column name for the given table."""
    cursor.execute(
        "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS "
        "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = %s "
        "AND COLUMN_KEY = 'PRI' LIMIT 1",
        (table,),
    )
    row = cursor.fetchone()
    return row["COLUMN_NAME"] if row else None


def export_table(cursor, table):
    """Query all rows from a table and return fixture-format dict."""
    cursor.execute(f"SELECT * FROM `{table}`")
    rows = cursor.fetchall()

    if not rows:
        print(f"  Warning: no rows found in {table}")
        column_names = [d[0] for d in cursor.description]
        return {"tableName": table, "columnNames": column_names, "pkName": None, "rows": []}

    column_names = [d[0] for d in cursor.description]
    pk_name = get_primary_key(cursor, table)

    serialized_rows = [
        [to_str(row[col]) for col in column_names]
        for row in rows
    ]

    return {
        "tableName": table,
        "columnNames": column_names,
        "pkName": pk_name,
        "rows": serialized_rows,
    }


def save_fixture(table, data):
    os.makedirs(FIXTURES_DIR, exist_ok=True)
    path = os.path.join(FIXTURES_DIR, f"{table}.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    print(f"  Saved {len(data['rows'])} rows → {os.path.relpath(path)}")


def main():
    parser = argparse.ArgumentParser(
        description="Export VoteCheck tables from MariaDB to fixture JSON files"
    )
    parser.add_argument("--host",     default=os.getenv("MYSQL_HOST",     "127.0.0.1"))
    parser.add_argument("--port",     default=int(os.getenv("MYSQL_PORT", "3306")), type=int)
    parser.add_argument("--user",     default=os.getenv("MYSQL_USER",     "root"))
    parser.add_argument("--password", default=os.getenv("MYSQL_PASSWORD", ""),
                        help="DB password (or set MYSQL_PASSWORD env var)")
    parser.add_argument("--database", default=os.getenv("MYSQL_DATABASE", ""),
                        required=not os.getenv("MYSQL_DATABASE"),
                        help="Database name (required)")
    parser.add_argument("--tables", nargs="*", default=ALL_TABLES,
                        metavar="TABLE",
                        help=f"Tables to export (default: all four). Available: {', '.join(ALL_TABLES)}")
    args = parser.parse_args()

    unknown = set(args.tables) - set(ALL_TABLES)
    if unknown:
        print(f"Unknown tables: {', '.join(unknown)}. Valid: {', '.join(ALL_TABLES)}", file=sys.stderr)
        sys.exit(1)

    print(f"Connecting to {args.user}@{args.host}:{args.port}/{args.database} ...")
    try:
        conn = pymysql.connect(
            host=args.host,
            port=args.port,
            user=args.user,
            password=args.password,
            database=args.database,
            charset="utf8mb4",
            cursorclass=pymysql.cursors.DictCursor,
        )
    except pymysql.err.OperationalError as e:
        print(f"Connection failed: {e}", file=sys.stderr)
        sys.exit(1)

    with conn:
        with conn.cursor() as cursor:
            for table in args.tables:
                print(f"\nExporting {table} ...")
                try:
                    data = export_table(cursor, table)
                    save_fixture(table, data)
                except pymysql.err.ProgrammingError as e:
                    print(f"  Error: {e} — skipping", file=sys.stderr)

    print("\nDone.")
    print("Start the mock server with:")
    print("  python mock_server.py")


if __name__ == "__main__":
    main()
