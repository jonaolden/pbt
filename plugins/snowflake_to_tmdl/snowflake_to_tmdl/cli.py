"""Command-line entrypoint: ``snowflake-to-tmdl DATABASE.SCHEMA.TABLE``."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from .ref import SnowflakeRef
from .render import render_table_tmdl


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="snowflake-to-tmdl",
        description=(
            "Convert a Snowflake fully-qualified table reference into a "
            "Power BI table TMDL file. Reads only INFORMATION_SCHEMA.COLUMNS "
            "(no PK/FK/relationship extraction)."
        ),
    )
    parser.add_argument(
        "ref",
        help="Snowflake table ref DB.SCHEMA.TABLE, or DB.SCHEMA for every base "
        "table in the schema (then -o is a directory)",
    )
    parser.add_argument(
        "-o",
        "--output",
        type=Path,
        help="Output path. Default: <TABLE>.tmdl in cwd. Use '-' for stdout.",
    )
    parser.add_argument(
        "--table-name",
        help="Override the TMDL table name. Defaults to the source table name.",
    )
    parser.add_argument(
        "--no-partition",
        action="store_true",
        help="Skip the M partition stub (columns only).",
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    args = _build_parser().parse_args(argv)
    whole_schema = args.ref.strip().count(".") == 1

    from .snowflake_client import fetch_columns, list_tables  # lazy; needs driver

    try:
        if whole_schema:
            if args.table_name or str(args.output) == "-":
                print("error: --table-name and '-o -' need a single table ref.", file=sys.stderr)
                return 2
            database, schema = (p.strip().strip('"') for p in args.ref.split("."))
            refs = [SnowflakeRef(database, schema, t) for t in list_tables(database, schema)]
        else:
            refs = [SnowflakeRef.parse(args.ref)]
    except ValueError as e:
        print(f"error: {e}", file=sys.stderr)
        return 2
    except (EnvironmentError, ImportError, LookupError, RuntimeError) as e:
        print(f"error: {e}", file=sys.stderr)
        return 1

    for ref in refs:
        try:
            columns, table_comment = fetch_columns(ref)  # ponytail: one connection per table
        except (EnvironmentError, ImportError, LookupError, RuntimeError) as e:
            print(f"error: {e}", file=sys.stderr)
            return 1

        tmdl = render_table_tmdl(
            ref,
            columns,
            table_name=args.table_name,
            table_comment=table_comment,
            include_partition=not args.no_partition,
        )

        if whole_schema:
            out_path = (args.output or Path(".")) / f"{ref.table}.tmdl"
        elif args.output is None:
            out_path = Path(f"{args.table_name or ref.table}.tmdl")
        elif str(args.output) == "-":
            sys.stdout.write(tmdl)
            return 0
        else:
            out_path = args.output

        out_path.parent.mkdir(parents=True, exist_ok=True)
        out_path.write_text(tmdl, encoding="utf-8")
        print(f"Wrote {out_path}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
