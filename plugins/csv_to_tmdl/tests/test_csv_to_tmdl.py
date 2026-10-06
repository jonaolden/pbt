import pytest

from csv_to_tmdl import cli, read_schema, render_table_tmdl

CSV = """TABLE_NAME,COLUMN_NAME,DATA_TYPE,ORDINAL_POSITION,COLUMN_COMMENT,TABLE_COMMENT
ORDERS,AMOUNT,decimal,2,,Order header
ORDERS,ID,int,1,Primary id,Order header
GUESTS,NAME,nvarchar,1,,
"""


@pytest.fixture
def csv_file(tmp_path):
    f = tmp_path / "schema.csv"
    f.write_text(CSV)
    return f


def test_read_groups_and_orders(csv_file):
    tables = read_schema(str(csv_file))
    assert [c["column_name"] for c in tables["ORDERS"]] == ["ID", "AMOUNT"]
    assert list(tables) == ["ORDERS", "GUESTS"]


def test_render_sqlserver_types(csv_file):
    tmdl = render_table_tmdl("ORDERS", read_schema(str(csv_file))["ORDERS"], "sqlserver")
    assert tmdl.startswith("/// Order header\ntable ORDERS\n")
    assert "\t/// Primary id\n\tcolumn ID\n\t\tdataType: int64" in tmdl
    assert "column AMOUNT\n\t\tdataType: decimal" in tmdl


def test_render_is_deterministic(csv_file):
    cols = read_schema(str(csv_file))["ORDERS"]
    assert render_table_tmdl("ORDERS", cols) == render_table_tmdl("ORDERS", cols)


def test_missing_required_column(tmp_path):
    f = tmp_path / "bad.csv"
    f.write_text("table_name,column_name\nT,C\n")
    with pytest.raises(ValueError, match="data_type"):
        read_schema(str(f))


def test_cli_writes_one_file_per_table(csv_file, tmp_path):
    out = tmp_path / "out"
    assert cli.main([str(csv_file), "--source-type", "sqlserver", "-o", str(out)]) == 0
    assert sorted(p.name for p in out.iterdir()) == ["GUESTS.tmdl", "ORDERS.tmdl"]
