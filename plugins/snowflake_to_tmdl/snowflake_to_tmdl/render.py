"""Render a Power BI table .tmdl document from column metadata."""

from __future__ import annotations

import uuid
from collections.abc import Iterable

from .ref import SnowflakeRef
from .types import ColumnSchema, TmdlType, map_snowflake_type

INDENT = "\t"


def _lineage_tag(*seed_parts: str) -> str:
    """Deterministic lineage tag derived from a stable seed.

    TMDL lineage tags are GUIDs; pbt emits them deterministically so
    rebuilds keep existing Power BI report bindings intact.
    """
    seed = "|".join(seed_parts)
    return str(uuid.uuid5(uuid.NAMESPACE_OID, seed))


def _needs_quoting(name: str) -> bool:
    return not name.isidentifier()


def _quote(name: str) -> str:
    return f"'{name}'" if _needs_quoting(name) else name


def _emit_description(description: str | None, indent: str) -> list[str]:
    if not description:
        return []
    # TMDL triple-slash docstrings; one line per source comment line.
    return [f"{indent}/// {line}" for line in description.splitlines() if line]


def _render_column(col: ColumnSchema, table_name: str) -> list[str]:
    tmdl_type = map_snowflake_type(col.data_type)
    lines: list[str] = []
    lines.extend(_emit_description(col.comment, INDENT))
    lines.append(f"{INDENT}column {_quote(col.name)}")
    lines.append(f"{INDENT * 2}dataType: {tmdl_type.value}")
    lines.append(f"{INDENT * 2}lineageTag: {_lineage_tag(table_name, col.name)}")
    if tmdl_type in (TmdlType.INT64, TmdlType.DECIMAL, TmdlType.DOUBLE):
        lines.append(f"{INDENT * 2}summarizeBy: none")
    lines.append(f"{INDENT * 2}sourceColumn: {col.name}")
    lines.append("")
    return lines


def _render_partition(ref: SnowflakeRef, table_name: str) -> list[str]:
    """Emit an M partition that reads the source table from Snowflake.

    The expression uses the standard Snowflake.Databases connector signature.
    It assumes a Power BI ``SnowflakeConnection`` shared expression is
    available, or that the user will adjust the connection details in the
    generated TMDL. This keeps the MVP self-contained without requiring an
    expressions.tmdl to be generated alongside.
    """
    m_expr = (
        "let\n"
        f'    Source = Value.NativeQuery(Snowflake.Databases("<server>", "<warehouse>"){{[Name="{ref.database}"]}}[Data], '
        f'"SELECT * FROM {ref.database}.{ref.schema}.{ref.table}", null, [EnableFolding=true])\n'
        "in\n"
        "    Source"
    )
    lines = [f"{INDENT}partition {_quote(table_name)} = m"]
    lines.append(f"{INDENT * 2}mode: import")
    lines.append(f"{INDENT * 2}source = ```")
    for m_line in m_expr.splitlines():
        lines.append(f"{INDENT * 3}{m_line}")
    lines.append(f"{INDENT * 3}```")
    lines.append("")
    return lines


def render_table_tmdl(
    ref: SnowflakeRef,
    columns: Iterable[ColumnSchema],
    *,
    table_name: str | None = None,
    table_comment: str | None = None,
    include_partition: bool = True,
) -> str:
    """Render a Power BI table .tmdl document.

    Args:
        ref: The source Snowflake reference (DATABASE.SCHEMA.TABLE).
        columns: Column metadata, ordered by ORDINAL_POSITION.
        table_name: Override the emitted table name. Defaults to ``ref.table``.
        table_comment: Optional table-level description.
        include_partition: When True, emit an M partition stub bound to
            the Snowflake source. Disable for tests or when the caller will
            attach its own partition.
    """
    name = table_name or ref.table
    cols = sorted(columns, key=lambda c: c.ordinal_position)
    if not cols:
        raise ValueError(
            f"No columns provided for {ref.fqn}; cannot render a TMDL table."
        )

    lines: list[str] = []
    lines.extend(_emit_description(table_comment, ""))
    lines.append(f"table {_quote(name)}")
    lines.append(f"{INDENT}lineageTag: {_lineage_tag(name)}")
    lines.append("")

    for col in cols:
        lines.extend(_render_column(col, name))

    if include_partition:
        lines.extend(_render_partition(ref, name))

    # Ensure file ends with a single trailing newline.
    return "\n".join(lines).rstrip() + "\n"
