"""``csv-to-tmdl schema.csv --source-type snowflake -o tables/``"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from .render import read_schema, render_table_tmdl
from .types import TYPE_MAPS


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(
        prog="csv-to-tmdl",
        description="Write one Power BI table .tmdl per table found in an "
        "INFORMATION_SCHEMA.COLUMNS-style CSV (table_name, column_name, data_type, ...).",
    )
    p.add_argument("csv", type=Path, help="Schema CSV export")
    p.add_argument("--source-type", choices=sorted(TYPE_MAPS), default="snowflake",
                   help="Database the CSV came from; selects the type map (default: snowflake)")
    p.add_argument("-o", "--output", type=Path, default=Path("."),
                   help="Output directory (default: cwd)")
    args = p.parse_args(argv)

    try:
        tables = read_schema(str(args.csv))
    except (OSError, ValueError) as e:
        print(f"error: {e}", file=sys.stderr)
        return 1

    args.output.mkdir(parents=True, exist_ok=True)
    for name, cols in tables.items():
        out = args.output / f"{name}.tmdl"
        out.write_text(render_table_tmdl(name, cols, args.source_type), encoding="utf-8")
        print(f"Wrote {out}", file=sys.stderr)
    return 0
