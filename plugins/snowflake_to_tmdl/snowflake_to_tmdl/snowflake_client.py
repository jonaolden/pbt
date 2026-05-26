"""Snowflake INFORMATION_SCHEMA reader.

Only INFORMATION_SCHEMA.COLUMNS (plus TABLES for the comment) is queried —
no primary key, foreign key, or constraint metadata is read.
"""

from __future__ import annotations

import os

from .ref import SnowflakeRef
from .types import ColumnSchema

_REQUIRED_ENV = ("SNOWFLAKE_ACCOUNT", "SNOWFLAKE_USER", "SNOWFLAKE_PASSWORD")


def _require_env() -> dict[str, str | None]:
    missing = [k for k in _REQUIRED_ENV if not os.environ.get(k)]
    if missing:
        raise EnvironmentError(
            "Missing required Snowflake environment variables: "
            + ", ".join(missing)
            + ". See .env.example."
        )
    return {
        "account": os.environ["SNOWFLAKE_ACCOUNT"],
        "user": os.environ["SNOWFLAKE_USER"],
        "password": os.environ["SNOWFLAKE_PASSWORD"],
        "warehouse": os.environ.get("SNOWFLAKE_WAREHOUSE"),
        "role": os.environ.get("SNOWFLAKE_ROLE"),
    }


_COLUMNS_QUERY = """
SELECT
    c.COLUMN_NAME,
    c.ORDINAL_POSITION,
    c.DATA_TYPE,
    c.IS_NULLABLE,
    c.COMMENT AS COLUMN_COMMENT
FROM {database}.INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_SCHEMA = %(schema)s
  AND c.TABLE_NAME = %(table)s
ORDER BY c.ORDINAL_POSITION
""".strip()

_TABLE_COMMENT_QUERY = """
SELECT t.COMMENT
FROM {database}.INFORMATION_SCHEMA.TABLES t
WHERE t.TABLE_SCHEMA = %(schema)s
  AND t.TABLE_NAME = %(table)s
""".strip()


def fetch_columns(ref: SnowflakeRef) -> tuple[list[ColumnSchema], str | None]:
    """Connect to Snowflake and return columns + the table-level comment.

    Snowflake credentials are read from environment variables:

    * SNOWFLAKE_ACCOUNT (required)
    * SNOWFLAKE_USER (required)
    * SNOWFLAKE_PASSWORD (required)
    * SNOWFLAKE_WAREHOUSE (optional)
    * SNOWFLAKE_ROLE (optional)
    """
    creds = _require_env()

    try:
        import snowflake.connector  # type: ignore
    except ImportError as e:
        raise ImportError(
            "snowflake-connector-python is required to query Snowflake. "
            "Install it with: pip install snowflake-connector-python"
        ) from e

    conn = snowflake.connector.connect(
        account=creds["account"],
        user=creds["user"],
        password=creds["password"],
        warehouse=creds["warehouse"],
        role=creds["role"],
        database=ref.database,
        schema=ref.schema,
    )
    try:
        cur = conn.cursor()
        try:
            params = {"schema": ref.schema, "table": ref.table}

            cur.execute(_TABLE_COMMENT_QUERY.format(database=ref.database), params)
            row = cur.fetchone()
            table_comment = row[0] if row and row[0] else None

            cur.execute(_COLUMNS_QUERY.format(database=ref.database), params)
            rows = cur.fetchall()
        finally:
            cur.close()
    finally:
        conn.close()

    if not rows:
        raise LookupError(
            f"No columns found for {ref.fqn}. Check the ref and that the role has "
            "INFORMATION_SCHEMA access."
        )

    columns = [
        ColumnSchema(
            name=r[0],
            ordinal_position=int(r[1]),
            data_type=r[2],
            is_nullable=str(r[3]).upper() == "YES",
            comment=r[4] if r[4] else None,
        )
        for r in rows
    ]
    return columns, table_comment
