"""Source data type -> TMDL data type maps. Unknown types fall back to string."""

from __future__ import annotations

_I, _DEC, _DBL, _S, _DT, _B, _BIN = (
    "int64", "decimal", "double", "string", "dateTime", "boolean", "binary",
)

TYPE_MAPS: dict[str, dict[str, str]] = {
    "snowflake": {
        "NUMBER": _I, "INT": _I, "INTEGER": _I, "BIGINT": _I, "SMALLINT": _I,
        "TINYINT": _I, "BYTEINT": _I,
        "DECIMAL": _DEC, "NUMERIC": _DEC,
        "FLOAT": _DBL, "FLOAT4": _DBL, "FLOAT8": _DBL, "DOUBLE": _DBL,
        "DOUBLE PRECISION": _DBL, "REAL": _DBL,
        "VARCHAR": _S, "STRING": _S, "TEXT": _S, "CHAR": _S, "CHARACTER": _S,
        "VARIANT": _S, "OBJECT": _S, "ARRAY": _S,
        "DATE": _DT, "DATETIME": _DT, "TIMESTAMP": _DT, "TIMESTAMP_NTZ": _DT,
        "TIMESTAMP_LTZ": _DT, "TIMESTAMP_TZ": _DT, "TIME": _DT,
        "BOOLEAN": _B, "BOOL": _B,
        "BINARY": _BIN, "VARBINARY": _BIN,
    },
    "sqlserver": {
        "INT": _I, "BIGINT": _I, "SMALLINT": _I, "TINYINT": _I,
        "DECIMAL": _DEC, "NUMERIC": _DEC, "MONEY": _DEC, "SMALLMONEY": _DEC,
        "FLOAT": _DBL, "REAL": _DBL,
        "CHAR": _S, "VARCHAR": _S, "NCHAR": _S, "NVARCHAR": _S, "TEXT": _S,
        "NTEXT": _S, "UNIQUEIDENTIFIER": _S, "XML": _S,
        "DATE": _DT, "DATETIME": _DT, "DATETIME2": _DT, "SMALLDATETIME": _DT,
        "DATETIMEOFFSET": _DT, "TIME": _DT,
        "BIT": _B,
        "BINARY": _BIN, "VARBINARY": _BIN, "IMAGE": _BIN,
    },
}


def map_type(source_type: str, data_type: str) -> str:
    """Map a source DATA_TYPE (precision/scale ignored, e.g. ``NUMBER(10,2)``)."""
    key = (data_type or "").split("(", 1)[0].strip().upper()
    return TYPE_MAPS[source_type].get(key, _S)
