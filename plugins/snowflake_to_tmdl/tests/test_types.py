import pytest

from snowflake_to_tmdl.types import TmdlType, map_snowflake_type


@pytest.mark.parametrize(
    "sf_type, expected",
    [
        ("NUMBER", TmdlType.INT64),
        ("INTEGER", TmdlType.INT64),
        ("BIGINT", TmdlType.INT64),
        ("DECIMAL", TmdlType.DECIMAL),
        ("NUMERIC", TmdlType.DECIMAL),
        ("FLOAT", TmdlType.DOUBLE),
        ("DOUBLE", TmdlType.DOUBLE),
        ("VARCHAR", TmdlType.STRING),
        ("STRING", TmdlType.STRING),
        ("TEXT", TmdlType.STRING),
        ("CHAR", TmdlType.STRING),
        ("DATE", TmdlType.DATETIME),
        ("TIMESTAMP_NTZ", TmdlType.DATETIME),
        ("TIMESTAMP_TZ", TmdlType.DATETIME),
        ("TIME", TmdlType.DATETIME),
        ("BOOLEAN", TmdlType.BOOLEAN),
        ("VARIANT", TmdlType.STRING),
        ("OBJECT", TmdlType.STRING),
        ("ARRAY", TmdlType.STRING),
        ("BINARY", TmdlType.BINARY),
    ],
)
def test_known_mappings(sf_type, expected):
    assert map_snowflake_type(sf_type) is expected


def test_case_insensitive():
    assert map_snowflake_type("varchar") is TmdlType.STRING


def test_strips_precision_scale():
    assert map_snowflake_type("NUMBER(10,2)") is TmdlType.INT64
    assert map_snowflake_type("VARCHAR(255)") is TmdlType.STRING


def test_unknown_falls_back_to_string():
    assert map_snowflake_type("GEOGRAPHY") is TmdlType.STRING
    assert map_snowflake_type("") is TmdlType.STRING
