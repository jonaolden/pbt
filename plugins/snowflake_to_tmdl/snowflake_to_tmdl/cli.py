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
        help="Fully-qualified Snowflake table ref, e.g. DB.SCHEMA.TABLE",
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

    try:
        ref = SnowflakeRef.parse(args.ref)
    except ValueError as e:
        print(f"error: {e}", file=sys.stderr)
        return 2

    from .snowflake_client import fetch_columns  # lazy import; needs driver

    try:
        columns, table_comment = fetch_columns(ref)
    except (EnvironmentError, ImportError, LookupError) as e:
        print(f"error: {e}", file=sys.stderr)
        return 1

    tmdl = render_table_tmdl(
        ref,
        columns,
        table_name=args.table_name,
        table_comment=table_comment,
        include_partition=not args.no_partition,
    )

    if args.output is None:
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
