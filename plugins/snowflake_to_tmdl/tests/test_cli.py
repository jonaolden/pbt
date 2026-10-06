from pathlib import Path

from snowflake_to_tmdl import cli
from snowflake_to_tmdl.types import ColumnSchema


def _fake_fetch(ref):
    return (
        [
            ColumnSchema("ID", "NUMBER", 1, False, None),
            ColumnSchema("NAME", "VARCHAR", 2, True, None),
        ],
        "A small reference table",
    )


def test_cli_writes_file(tmp_path: Path, monkeypatch, capsys):
    monkeypatch.setattr("snowflake_to_tmdl.snowflake_client.fetch_columns", _fake_fetch)
    out = tmp_path / "out.tmdl"

    rc = cli.main(["DB.SCH.TBL", "-o", str(out)])

    assert rc == 0
    assert out.exists()
    content = out.read_text()
    assert content.startswith("/// A small reference table")
    assert "table TBL" in content
    assert "column ID" in content
    assert "column NAME" in content


def test_cli_writes_stdout(monkeypatch, capsys):
    monkeypatch.setattr("snowflake_to_tmdl.snowflake_client.fetch_columns", _fake_fetch)
    rc = cli.main(["DB.SCH.TBL", "-o", "-"])
    assert rc == 0
    captured = capsys.readouterr()
    assert "table TBL" in captured.out


def test_cli_rejects_bad_ref(capsys):
    rc = cli.main(["not-a-real-ref"])
    assert rc == 2
    err = capsys.readouterr().err
    assert "error:" in err


def test_cli_whole_schema_writes_dir(tmp_path: Path, monkeypatch):
    monkeypatch.setattr("snowflake_to_tmdl.snowflake_client.fetch_columns", _fake_fetch)
    monkeypatch.setattr("snowflake_to_tmdl.snowflake_client.list_tables", lambda d, s: ["A", "B"])

    rc = cli.main(["DB.SCH", "-o", str(tmp_path)])

    assert rc == 0
    assert sorted(p.name for p in tmp_path.glob("*.tmdl")) == ["A.tmdl", "B.tmdl"]
