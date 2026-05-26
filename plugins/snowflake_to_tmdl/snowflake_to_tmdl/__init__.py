"""Snowflake to Power BI TMDL conversion plugin for pbt.

MVP: convert a single fully-qualified Snowflake table reference
(DATABASE.SCHEMA.TABLE) into a Power BI table .tmdl file using
INFORMATION_SCHEMA.COLUMNS only. Primary/foreign keys and relationships
are intentionally out of scope.
"""

from .ref import SnowflakeRef
from .types import ColumnSchema, TmdlType, map_snowflake_type
from .render import render_table_tmdl

__all__ = [
    "SnowflakeRef",
    "ColumnSchema",
    "TmdlType",
    "map_snowflake_type",
    "render_table_tmdl",
]
