#!/usr/bin/env python3
"""normalize_columns.py - Pre-build hook for dimension table cleanup

Transforms DIM_* tables to follow Power BI display conventions:

  Table names:
    DIM_BUSINESS_SEGMENT  ->  Business Segment

  Column names (excludes *_ID and *_ID_* patterns):
    BUSINESS_SEGMENT_NAME ->  Business Segment Name

  ID columns are left unchanged and marked hidden:
    BUSINESS_SEGMENT_ID   ->  BUSINESS_SEGMENT_ID (is_hidden: true)

  source_column values are NEVER modified — they must match the data source.

Usage:
    pbt build . --pre-hook "python3 ./scripts/normalize_columns.py"
"""

import re
import sys
import glob

import yaml


def to_title_case(name: str) -> str:
    """Convert UPPER_SNAKE_CASE to Title Case.

    DIM_BUSINESS_SEGMENT -> Business Segment
    BUSINESS_SEGMENT_NAME -> Business Segment Name
    """
    return " ".join(word.capitalize() for word in name.split("_"))


def is_id_column(name: str) -> bool:
    """Return True if the column name ends with _ID or contains _ID_ anywhere."""
    upper = name.upper()
    return upper.endswith("_ID") or "_ID_" in upper


def process_table(table: dict) -> tuple[bool, list[str]]:
    """Apply naming transforms to a single DIM_* table. Returns (changed, log)."""
    name = table.get("name", "")
    if not name.upper().startswith("DIM_"):
        return False, []

    changed = False
    log = []

    # --- Table name: strip DIM_ prefix and title-case ---
    new_name = to_title_case(name[4:])  # skip "DIM_"
    if new_name != name:
        log.append(f"  table: {name} -> {new_name}")
        table["name"] = new_name
        changed = True

    # --- Columns ---
    for col in table.get("columns", []):
        col_name = col.get("name", "")

        if is_id_column(col_name):
            # ID columns: keep original name, mark hidden
            if not col.get("is_hidden"):
                col["is_hidden"] = True
                log.append(f"  hide:  {col_name}")
                changed = True
        else:
            # Non-ID columns: title-case the name
            new_col_name = to_title_case(col_name)
            if new_col_name != col_name:
                log.append(f"  col:   {col_name} -> {new_col_name}")
                col["name"] = new_col_name
                changed = True

        # source_column is NEVER modified

    return changed, log


def main() -> None:
    files = sorted(glob.glob("tables/*.yaml") + glob.glob("tables/*.yml"))
    if not files:
        print("No table files found in tables/")
        return

    total_changed = 0

    for path in files:
        with open(path) as f:
            table = yaml.safe_load(f)
        if not table:
            continue

        changed, log = process_table(table)
        if changed:
            with open(path, "w") as f:
                yaml.dump(table, f, default_flow_style=False, sort_keys=False, allow_unicode=True)
            total_changed += 1
            print(f"{path}:")
            for line in log:
                print(line)

    print(f"\nNormalized {total_changed} table(s).")


if __name__ == "__main__":
    main()
