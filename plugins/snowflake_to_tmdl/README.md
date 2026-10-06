# snowflake-to-tmdl

A Python MVP plugin for [pbt](https://github.com/jonaolden/pbt). Converts a
fully-qualified Snowflake table reference into a Power BI table `.tmdl` file
by querying only `INFORMATION_SCHEMA.COLUMNS`.

## Scope

In scope:

- Parse a `DATABASE.SCHEMA.TABLE` ref (or `DATABASE.SCHEMA` for all base tables).
- Query column metadata from Snowflake `INFORMATION_SCHEMA`.
- Map Snowflake data types to TMDL data types (mirrors `examples/snowflake.yaml`).
- Emit a deterministic, valid Power BI table TMDL document.

Out of scope (per design):

- Primary keys, foreign keys, constraints.
- Relationship generation. Relationships are authored manually or
  produced separately by an LLM from user instructions.
- dbt parsing. Views (schema import covers base tables only).

## Install

```bash
pip install -e plugins/snowflake_to_tmdl
# For live Snowflake connectivity:
pip install -e 'plugins/snowflake_to_tmdl[snowflake]'
```

## Credentials

Credentials are read from environment variables. ``SNOWFLAKE_ACCOUNT`` is the
only hard requirement; everything else depends on the authentication method.

```bash
export SNOWFLAKE_ACCOUNT=myorg-myaccount   # required (".snowflakecomputing.com" is stripped if present)
export SNOWFLAKE_USER=my_username
export SNOWFLAKE_WAREHOUSE=COMPUTE_WH      # optional
export SNOWFLAKE_ROLE=ANALYST_ROLE         # optional
```

### Authentication method

Set `SNOWFLAKE_AUTHENTICATOR` explicitly, or let the plugin infer it:

| `SNOWFLAKE_AUTHENTICATOR` | Required vars | Notes |
|---|---|---|
| `snowflake` | `SNOWFLAKE_USER`, `SNOWFLAKE_PASSWORD` | Username + password |
| `snowflake_jwt` | `SNOWFLAKE_USER`, `SNOWFLAKE_PRIVATE_KEY_FILE` | Key-pair auth |
| `externalbrowser` | `SNOWFLAKE_USER` | SSO via browser |

If `SNOWFLAKE_AUTHENTICATOR` is **not** set, the method is inferred:

1. `snowflake` when `SNOWFLAKE_PASSWORD` is set
2. `snowflake_jwt` when `SNOWFLAKE_PRIVATE_KEY_FILE` is set
3. `externalbrowser` otherwise

## CLI

```bash
# Write ANALYTICS_DB.PUBLIC.RESERVATIONS into ./RESERVATIONS.tmdl
snowflake-to-tmdl ANALYTICS_DB.PUBLIC.RESERVATIONS

# Custom output path
snowflake-to-tmdl ANALYTICS_DB.PUBLIC.RESERVATIONS -o tables/Reservations.tmdl

# Print to stdout (useful for piping or diffing)
snowflake-to-tmdl ANALYTICS_DB.PUBLIC.RESERVATIONS -o -

# Every base table in a schema -> ./tmdl/<TABLE>.tmdl
snowflake-to-tmdl ANALYTICS_DB.PUBLIC -o tmdl/

# Skip the M partition stub (columns only)
snowflake-to-tmdl ANALYTICS_DB.PUBLIC.RESERVATIONS --no-partition

# Rename the TMDL table (sourceColumn references keep the original names)
snowflake-to-tmdl ANALYTICS_DB.PUBLIC.RESERVATIONS --table-name Reservations
```

Then convert to pbt YAML: `pbt import table tmdl/ ./tables`.

## Library use

```python
from snowflake_to_tmdl import SnowflakeRef, render_table_tmdl
from snowflake_to_tmdl.snowflake_client import fetch_columns

ref = SnowflakeRef.parse("ANALYTICS_DB.PUBLIC.RESERVATIONS")
columns, table_comment = fetch_columns(ref)
tmdl = render_table_tmdl(ref, columns, table_comment=table_comment)
print(tmdl)
```

## Tests

```bash
cd plugins/snowflake_to_tmdl
python -m pytest tests/
```

The test suite covers ref parsing, Snowflake → TMDL type mapping, and TMDL
rendering. None of the tests require a live Snowflake connection — the CLI
test patches the schema fetch.

## Generated M partition

The default partition uses the `Snowflake.Databases` connector and navigates
`Database → Schema → Table`, then calls `Table.SelectColumns` with the exact
column list returned from `INFORMATION_SCHEMA.COLUMNS` plus
`MissingField.UseNull`:

```m
let
    Source = Snowflake.Databases("<server>", "<warehouse>"),
    Database = Source{[Name="DB", Kind="Database"]}[Data],
    Schema = Database{[Name="SCH", Kind="Schema"]}[Data],
    Table = Schema{[Name="TBL", Kind="Table"]}[Data],
    SelectedColumns = Table.SelectColumns(Table, {"COL1", "COL2", ...}, MissingField.UseNull)
in
    SelectedColumns
```

This pins the model's input schema: new columns added in Snowflake won't appear
in Power BI until the TMDL is regenerated, and dropped columns become nulls
rather than refresh errors. Replace `<server>` / `<warehouse>` (or wire to a
shared expression) before publishing. Pass `--no-partition` to skip the M block
entirely.

## Type mapping

| Snowflake | TMDL |
|---|---|
| `NUMBER`, `INTEGER`, `BIGINT`, `SMALLINT`, `TINYINT`, `INT` | `int64` |
| `DECIMAL`, `NUMERIC` | `decimal` |
| `FLOAT`, `DOUBLE`, `REAL` | `double` |
| `VARCHAR`, `STRING`, `TEXT`, `CHAR`, `VARIANT`, `OBJECT`, `ARRAY` | `string` |
| `DATE`, `DATETIME`, `TIMESTAMP*`, `TIME` | `dateTime` |
| `BOOLEAN` | `boolean` |
| `BINARY`, `VARBINARY` | `binary` |
| Anything else | `string` (safe fallback) |
