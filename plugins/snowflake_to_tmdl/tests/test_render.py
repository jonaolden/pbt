import pytest

from snowflake_to_tmdl.ref import SnowflakeRef
from snowflake_to_tmdl.render import render_table_tmdl
from snowflake_to_tmdl.types import ColumnSchema


@pytest.fixture
def ref() -> SnowflakeRef:
    return SnowflakeRef("ANALYTICS_DB", "PUBLIC", "RESERVATIONS")


@pytest.fixture
def columns() -> list[ColumnSchema]:
    return [
        ColumnSchema("RESERVATION_ID", "NUMBER", 1, False, "Primary identifier"),
        ColumnSchema("GUEST_NAME", "VARCHAR", 2, True, None),
        ColumnSchema("CHECK_IN", "TIMESTAMP_NTZ", 3, True, "When the guest arrived"),
        ColumnSchema("ROOM_RATE", "DECIMAL", 4, True, None),
        ColumnSchema("IS_CANCELLED", "BOOLEAN", 5, True, None),
    ]


def test_render_table_header_and_columns(ref, columns):
    tmdl = render_table_tmdl(ref, columns, include_partition=False)

    assert tmdl.startswith("table RESERVATIONS\n")
    assert "\tlineageTag: " in tmdl

    # All columns present, in ordinal order, with mapped types
    assert "column RESERVATION_ID" in tmdl
    assert "dataType: int64" in tmdl
    assert "column GUEST_NAME" in tmdl
    assert "dataType: string" in tmdl
    assert "column CHECK_IN" in tmdl
    assert "dataType: dateTime" in tmdl
    assert "column ROOM_RATE" in tmdl
    assert "dataType: decimal" in tmdl
    assert "column IS_CANCELLED" in tmdl
    assert "dataType: boolean" in tmdl

    # Ordering is by ordinal_position
    idx_id = tmdl.index("column RESERVATION_ID")
    idx_guest = tmdl.index("column GUEST_NAME")
    idx_cancel = tmdl.index("column IS_CANCELLED")
    assert idx_id < idx_guest < idx_cancel


def test_render_emits_descriptions(ref, columns):
    tmdl = render_table_tmdl(ref, columns, table_comment="Hotel reservations.", include_partition=False)
    assert "/// Hotel reservations." in tmdl
    assert "/// Primary identifier" in tmdl
    assert "/// When the guest arrived" in tmdl


def test_render_source_column_matches_original_name(ref, columns):
    tmdl = render_table_tmdl(ref, columns, include_partition=False)
    for col in columns:
        assert f"sourceColumn: {col.name}" in tmdl


def test_summarize_by_only_on_numeric(ref, columns):
    tmdl = render_table_tmdl(ref, columns, include_partition=False)
    numeric_cols = [c for c in columns if c.data_type in ("NUMBER", "DECIMAL")]
    # summarizeBy: none should appear once per numeric column
    assert tmdl.count("summarizeBy: none") == len(numeric_cols)


def test_render_with_partition(ref, columns):
    tmdl = render_table_tmdl(ref, columns, include_partition=True)
    assert "partition RESERVATIONS = m" in tmdl
    assert "mode: import" in tmdl
    assert "Snowflake.Databases" in tmdl


def test_partition_uses_navigation_steps(ref, columns):
    tmdl = render_table_tmdl(ref, columns, include_partition=True)
    # DB -> Schema -> Table navigation, not a raw SELECT
    assert f'[Name="{ref.database}", Kind="Database"]' in tmdl
    assert f'[Name="{ref.schema}", Kind="Schema"]' in tmdl
    assert f'[Name="{ref.table}", Kind="Table"]' in tmdl
    assert "Value.NativeQuery" not in tmdl
    assert "SELECT *" not in tmdl


def test_partition_selects_columns_explicitly(ref, columns):
    tmdl = render_table_tmdl(ref, columns, include_partition=True)
    assert "Table.SelectColumns(" in tmdl
    assert "MissingField.UseNull" in tmdl
    for col in columns:
        assert f'"{col.name}"' in tmdl


def test_no_partition_omits_m_block(ref, columns):
    tmdl = render_table_tmdl(ref, columns, include_partition=False)
    assert "partition" not in tmdl
    assert "Snowflake.Databases" not in tmdl
    assert "Table.SelectColumns" not in tmdl


def test_render_table_name_override(ref, columns):
    tmdl = render_table_tmdl(ref, columns, table_name="Reservations", include_partition=False)
    assert tmdl.startswith("table Reservations\n")
    # source column references must remain the original Snowflake names
    assert "sourceColumn: RESERVATION_ID" in tmdl


def test_render_requires_columns(ref):
    with pytest.raises(ValueError):
        render_table_tmdl(ref, [])


def test_render_is_deterministic(ref, columns):
    first = render_table_tmdl(ref, columns)
    second = render_table_tmdl(ref, columns)
    assert first == second


def test_unknown_type_renders_as_string(ref):
    cols = [ColumnSchema("GEO", "GEOGRAPHY", 1)]
    tmdl = render_table_tmdl(ref, cols, include_partition=False)
    assert "column GEO" in tmdl
    assert "dataType: string" in tmdl


def test_columns_sorted_by_ordinal(ref):
    cols = [
        ColumnSchema("THIRD", "VARCHAR", 3),
        ColumnSchema("FIRST", "VARCHAR", 1),
        ColumnSchema("SECOND", "VARCHAR", 2),
    ]
    tmdl = render_table_tmdl(ref, cols, include_partition=False)
    i1 = tmdl.index("column FIRST")
    i2 = tmdl.index("column SECOND")
    i3 = tmdl.index("column THIRD")
    assert i1 < i2 < i3


def test_identifier_quoting_when_needed(ref):
    cols = [ColumnSchema("has space", "VARCHAR", 1)]
    tmdl = render_table_tmdl(ref, cols, table_name="My Table", include_partition=False)
    assert "table 'My Table'" in tmdl
    assert "column 'has space'" in tmdl
    # sourceColumn keeps the raw name; no quoting needed
    assert "sourceColumn: has space" in tmdl
