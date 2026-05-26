"""Snowflake to TMDL type mapping.

Mappings mirror the canonical pbt mapping in examples/snowflake.yaml so
output is consistent with the .NET importer.
"""

from __future__ import annotations

from dataclasses import dataclass
from enum import Enum


class TmdlType(str, Enum):
    INT64 = "int64"
    DECIMAL = "decimal"
    DOUBLE = "double"
    STRING = "string"
    DATETIME = "dateTime"
    BOOLEAN = "boolean"
    BINARY = "binary"


_TYPE_MAP: dict[str, TmdlType] = {
    # Numeric
    "NUMBER": TmdlType.INT64,
    "DECIMAL": TmdlType.DECIMAL,
    "NUMERIC": TmdlType.DECIMAL,
    "INT": TmdlType.INT64,
    "INTEGER": TmdlType.INT64,
    "BIGINT": TmdlType.INT64,
    "SMALLINT": TmdlType.INT64,
    "TINYINT": TmdlType.INT64,
    "BYTEINT": TmdlType.INT64,
    "FLOAT": TmdlType.DOUBLE,
    "FLOAT4": TmdlType.DOUBLE,
    "FLOAT8": TmdlType.DOUBLE,
    "DOUBLE": TmdlType.DOUBLE,
    "DOUBLE PRECISION": TmdlType.DOUBLE,
    "REAL": TmdlType.DOUBLE,
    # String
    "VARCHAR": TmdlType.STRING,
    "STRING": TmdlType.STRING,
    "TEXT": TmdlType.STRING,
    "CHAR": TmdlType.STRING,
    "CHARACTER": TmdlType.STRING,
    # Date / time
    "DATE": TmdlType.DATETIME,
    "DATETIME": TmdlType.DATETIME,
    "TIMESTAMP": TmdlType.DATETIME,
    "TIMESTAMP_NTZ": TmdlType.DATETIME,
    "TIMESTAMP_LTZ": TmdlType.DATETIME,
    "TIMESTAMP_TZ": TmdlType.DATETIME,
    "TIME": TmdlType.DATETIME,
    # Boolean
    "BOOLEAN": TmdlType.BOOLEAN,
    "BOOL": TmdlType.BOOLEAN,
    # Semi-structured (flattened to string in PBI)
    "VARIANT": TmdlType.STRING,
    "OBJECT": TmdlType.STRING,
    "ARRAY": TmdlType.STRING,
    # Binary
    "BINARY": TmdlType.BINARY,
    "VARBINARY": TmdlType.BINARY,
}


def map_snowflake_type(snowflake_type: str) -> TmdlType:
    """Map a Snowflake DATA_TYPE string to a TMDL data type.

    Snowflake INFORMATION_SCHEMA.COLUMNS.DATA_TYPE values arrive uppercase
    without precision/scale, e.g. ``NUMBER``, ``TIMESTAMP_NTZ``.
    Unknown types fall back to string, matching the existing pbt behavior
    for opaque/semi-structured types.
    """
    if not snowflake_type:
        return TmdlType.STRING

    key = snowflake_type.strip().upper()
    # Strip precision/scale if a caller passed e.g. "NUMBER(10,2)"
    if "(" in key:
        key = key.split("(", 1)[0].strip()

    return _TYPE_MAP.get(key, TmdlType.STRING)


@dataclass(frozen=True)
class ColumnSchema:
    """Column metadata extracted from INFORMATION_SCHEMA.COLUMNS."""

    name: str
    data_type: str
    ordinal_position: int
    is_nullable: bool = True
    comment: str | None = None
