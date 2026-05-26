import pytest

from snowflake_to_tmdl.ref import SnowflakeRef


def test_parse_basic():
    ref = SnowflakeRef.parse("ANALYTICS_DB.PUBLIC.RESERVATIONS")
    assert ref.database == "ANALYTICS_DB"
    assert ref.schema == "PUBLIC"
    assert ref.table == "RESERVATIONS"
    assert ref.fqn == "ANALYTICS_DB.PUBLIC.RESERVATIONS"


def test_parse_strips_quotes_and_whitespace():
    ref = SnowflakeRef.parse('  "DB" . "SCH" . "TBL"  ')
    assert (ref.database, ref.schema, ref.table) == ("DB", "SCH", "TBL")


@pytest.mark.parametrize("bad", ["", "  ", "ONLY_ONE", "DB.SCHEMA", "DB..TABLE", "A.B.C.D"])
def test_parse_rejects_invalid(bad):
    with pytest.raises(ValueError):
        SnowflakeRef.parse(bad)
