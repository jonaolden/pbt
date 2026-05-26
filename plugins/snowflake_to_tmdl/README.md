# snowflake-to-tmdl

A Python MVP plugin for [pbt](https://github.com/jonaolden/pbt). Converts a
fully-qualified Snowflake table reference into a Power BI table `.tmdl` file
by querying only `INFORMATION_SCHEMA.COLUMNS`.

## Scope

In scope:

- Parse a `DATABASE.SCHEMA.TABLE` ref.
- Query column metadata from Snowflake `INFORMATION_SCHEMA`.
- Map Snowflake data types to TMDL data types (mirrors `examples/snowflake.yaml`).
- Emit a deterministic, valid Power BI table TMDL document.

Out of scope (per design):

- Primary keys, foreign keys, constraints.
- Relationship generation. Relationships are authored manually or
  produced separately by an LLM from user instructions.
- Multi-table import, dbt parsing.

## Install

```bash
pip install -e plugins/snowflake_to_tmdl
# For live Snowflake connectivity:
pip install -e 'plugins/snowflake_to_tmdl[snowflake]'
```

## Credentials

Set the same environment variables used by the .NET importer
(see `.env.example` at the repo root):

```bash
export SNOWFLAKE_ACCOUNT=myorg-myaccount
export SNOWFLAKE_USER=my_username
export SNOWFLAKE_PASSWORD=my_secure_password
export SNOWFLAKE_WAREHOUSE=COMPUTE_WH   # optional
export SNOWFLAKE_ROLE=ANALYST_ROLE      # optional
```

## CLI

```bash
# Write ANALYTICS_DB.PUBLIC.RESERVATIONS into ./RESERVATIONS.tmdl
snowflake-to-tmdl ANALYTICS_DB.PUBLIC.RESERVATIONS

# Custom output path
snowflake-to-tmdl ANALYTICS_DB.PUBLIC.RESERVATIONS -o tables/Reservations.tmdl

# Print to stdout (useful for piping or diffing)
snowflake-to-tmdl ANALYTICS_DB.PUBLIC.RESERVATIONS -o -

# Skip the M partition stub (columns only)
snowflake-to-tmdl ANALYTICS_DB.PUBLIC.RESERVATIONS --no-partition

# Rename the TMDL table (sourceColumn references keep the original names)
snowflake-to-tmdl ANALYTICS_DB.PUBLIC.RESERVATIONS --table-name Reservations
```

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
