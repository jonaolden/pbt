"""Read a schema CSV and render one Power BI table .tmdl document per table."""

from __future__ import annotations

import csv
import uuid
from collections import defaultdict

from .types import map_type

INDENT = "\t"
_REQUIRED = ("table_name", "column_name", "data_type")


def read_schema(path: str) -> dict[str, list[dict[str, str]]]:
    """Return ``{table_name: [column rows ordered by ordinal_position]}``.

    Header names are matched case-insensitively. ``table_name``,
    ``column_name`` and ``data_type`` are required; ``ordinal_position``,
    ``table_comment`` and ``column_comment`` are optional.
    """
    with open(path, newline="", encoding="utf-8-sig") as f:
        reader = csv.DictReader(f)
        reader.fieldnames = [h.strip().lower() for h in reader.fieldnames or []]
        missing = [c for c in _REQUIRED if c not in reader.fieldnames]
        if missing:
            raise ValueError(f"CSV is missing required column(s): {', '.join(missing)}")
        rows = [{k: (v or "").strip() for k, v in r.items() if k} for r in reader]

    if not rows:
        raise ValueError(f"No records found in CSV file: {path}")
    for r in rows:
        if not all(r[c] for c in _REQUIRED):
            raise ValueError(f"Row is missing table_name/column_name/data_type: {r}")

    tables: dict[str, list[dict[str, str]]] = defaultdict(list)
    for r in rows:
        tables[r["table_name"]].append(r)
    for cols in tables.values():
        cols.sort(key=lambda r: int(r.get("ordinal_position") or 10**9))  # stable
    return dict(tables)


def _lineage_tag(*seed: str) -> str:
    return str(uuid.uuid5(uuid.NAMESPACE_OID, "|".join(seed)))


def _quote(name: str) -> str:
    return name if name.isidentifier() else f"'{name}'"


def _doc(text: str, indent: str) -> list[str]:
    return [f"{indent}/// {line}" for line in text.splitlines() if line]


def render_table_tmdl(
    table: str, columns: list[dict[str, str]], source_type: str = "snowflake"
) -> str:
    """Render a .tmdl table (columns only; add a partition/source via pbt YAML)."""
    lines = _doc(columns[0].get("table_comment", ""), "")
    lines += [f"table {_quote(table)}", f"{INDENT}lineageTag: {_lineage_tag(table)}", ""]
    for c in columns:
        name, tmdl_type = c["column_name"], map_type(source_type, c["data_type"])
        lines += _doc(c.get("column_comment", ""), INDENT)
        lines += [
            f"{INDENT}column {_quote(name)}",
            f"{INDENT * 2}dataType: {tmdl_type}",
            f"{INDENT * 2}lineageTag: {_lineage_tag(table, name)}",
        ]
        if tmdl_type in ("int64", "decimal", "double"):
            lines.append(f"{INDENT * 2}summarizeBy: none")
        lines += [f"{INDENT * 2}sourceColumn: {name}", ""]
    return "\n".join(lines).rstrip() + "\n"
