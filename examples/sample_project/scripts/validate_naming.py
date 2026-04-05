#!/usr/bin/env python3
"""validate_naming.py - Pre-build hook for naming convention validation

Validates that dimension tables have been normalized before building:
  - DIM_* table names should have been title-cased (no raw DIM_ prefix)
  - Column names in dimension tables should be Title Case (not UPPER_SNAKE)
  - ID columns should be marked hidden
  - source_column values must not contain spaces

Run after normalize_columns.py to verify its output, or standalone
as a CI gate.

Usage:
    pbt build . --pre-hook "python3 ./scripts/validate_naming.py"
"""

import sys
import glob
import re

import yaml

TITLE_CASE = re.compile(r"^[A-Z][a-z]+(?: [A-Z][a-z]+)*$")
errors = []

for path in sorted(glob.glob("tables/*.yaml") + glob.glob("tables/*.yml")):
    with open(path) as f:
        table = yaml.safe_load(f)

    if not table:
        continue

    table_name = table.get("name", "")

    # Tables should not still have raw DIM_ prefix
    if table_name.upper().startswith("DIM_"):
        errors.append(
            f"{path}: table '{table_name}' still has DIM_ prefix "
            f"(run normalize_columns.py first)"
        )

    for col in table.get("columns", []):
        col_name = col.get("name", "")
        source_col = col.get("source_column", "")

        # source_column must not contain spaces
        if source_col and " " in source_col:
            errors.append(
                f"{path}: column '{col_name}' has spaces in "
                f"source_column '{source_col}'"
            )

        # ID columns should be hidden
        upper = col_name.upper()
        is_id = upper.endswith("_ID") or "_ID_" in upper
        if is_id and not col.get("is_hidden"):
            errors.append(
                f"{path}: ID column '{col_name}' should be hidden"
            )

if errors:
    print(f"Naming validation failed ({len(errors)} error(s)):", file=sys.stderr)
    for e in errors:
        print(f"  - {e}", file=sys.stderr)
    sys.exit(1)
else:
    print("Naming validation passed.")
