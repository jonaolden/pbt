"""Snowflake INFORMATION_SCHEMA reader.

Only INFORMATION_SCHEMA.COLUMNS (plus TABLES for the comment) is queried —
no primary key, foreign key, or constraint metadata is read.
"""

from __future__ import annotations

import os
from typing import Any, Optional

from .ref import SnowflakeRef
from .types import ColumnSchema


_VALID_AUTHENTICATORS = ("externalbrowser", "snowflake", "snowflake_jwt")


def _normalize_account(account: str) -> str:
    """Strip the ``.snowflakecomputing.com`` suffix from an account locator.

    Users frequently copy the full host (``myorg-myaccount.snowflakecomputing.com``)
    from the Snowflake UI. The connector wants the bare locator.
    """
    if not account:
        return account
    account = account.strip()
    suffix = ".snowflakecomputing.com"
    lower = account.lower()
    if lower.endswith(suffix):
        account = account[: -len(suffix)]
    return account


def build_connection_params(
    database: Optional[str] = None,
    schema: Optional[str] = None,
) -> dict[str, Any]:
    """Build Snowflake connection parameters from environment variables.

    Auth method selection (``SNOWFLAKE_AUTHENTICATOR`` env var):

    * ``externalbrowser`` — SSO via browser (default when no password/key is set)
    * ``snowflake`` — username + password
    * ``snowflake_jwt`` — key-pair (requires ``SNOWFLAKE_PRIVATE_KEY_FILE``)

    If ``SNOWFLAKE_AUTHENTICATOR`` is not set, the method is inferred:

    * ``snowflake`` when ``SNOWFLAKE_PASSWORD`` is set
    * ``snowflake_jwt`` when ``SNOWFLAKE_PRIVATE_KEY_FILE`` is set
    * ``externalbrowser`` otherwise
    """
    account = os.environ.get("SNOWFLAKE_ACCOUNT")
    if not account:
        raise RuntimeError(
            "SNOWFLAKE_ACCOUNT environment variable is required. "
            "Set it to your account locator (e.g. 'myorg-myaccount'). "
            "See .env.example."
        )

    user = os.environ.get("SNOWFLAKE_USER")
    password = os.environ.get("SNOWFLAKE_PASSWORD")
    private_key_file = os.environ.get("SNOWFLAKE_PRIVATE_KEY_FILE")
    warehouse = os.environ.get("SNOWFLAKE_WAREHOUSE")
    role = os.environ.get("SNOWFLAKE_ROLE")

    authenticator = os.environ.get("SNOWFLAKE_AUTHENTICATOR")
    if authenticator:
        authenticator = authenticator.strip().lower()
        if authenticator not in _VALID_AUTHENTICATORS:
            raise RuntimeError(
                f"Invalid SNOWFLAKE_AUTHENTICATOR '{authenticator}'. "
                f"Must be one of: {', '.join(_VALID_AUTHENTICATORS)}."
            )
    else:
        if password:
            authenticator = "snowflake"
        elif private_key_file:
            authenticator = "snowflake_jwt"
        else:
            authenticator = "externalbrowser"

    if authenticator == "snowflake_jwt" and not private_key_file:
        raise RuntimeError(
            "SNOWFLAKE_AUTHENTICATOR=snowflake_jwt requires "
            "SNOWFLAKE_PRIVATE_KEY_FILE to be set."
        )

    params: dict[str, Any] = {
        "account": _normalize_account(account),
        "authenticator": authenticator,
    }
    if user:
        params["user"] = user
    if authenticator == "snowflake" and password:
        params["password"] = password
    if authenticator == "snowflake_jwt" and private_key_file:
        params["private_key_file"] = private_key_file
    if warehouse:
        params["warehouse"] = warehouse
    if role:
        params["role"] = role
    if database:
        params["database"] = database
    if schema:
        params["schema"] = schema

    return params


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


_TABLES_QUERY = """
SELECT t.TABLE_NAME
FROM {database}.INFORMATION_SCHEMA.TABLES t
WHERE t.TABLE_SCHEMA = %(schema)s
  AND t.TABLE_TYPE = 'BASE TABLE'
ORDER BY t.TABLE_NAME
""".strip()


def list_tables(database: str, schema: str) -> list[str]:
    """Return the base-table names in ``database.schema`` (views excluded)."""
    conn = _connect(build_connection_params(database=database, schema=schema))
    try:
        cur = conn.cursor()
        try:
            cur.execute(_TABLES_QUERY.format(database=database), {"schema": schema})
            names = [r[0] for r in cur.fetchall()]
        finally:
            cur.close()
    finally:
        conn.close()
    if not names:
        raise LookupError(f"No tables found in {database}.{schema}.")
    return names


def _connect(params: dict[str, Any]):
    try:
        import snowflake.connector  # type: ignore
    except ImportError as e:
        raise ImportError(
            "snowflake-connector-python is required to query Snowflake. "
            "Install it with: pip install snowflake-connector-python"
        ) from e
    return snowflake.connector.connect(**params)


def fetch_columns(ref: SnowflakeRef) -> tuple[list[ColumnSchema], str | None]:
    """Connect to Snowflake and return columns + the table-level comment.

    Credentials and auth method come from environment variables — see
    :func:`build_connection_params` for the full selection rules.
    """
    conn = _connect(build_connection_params(database=ref.database, schema=ref.schema))
    try:
        cur = conn.cursor()
        try:
            query_params = {"schema": ref.schema, "table": ref.table}

            cur.execute(_TABLE_COMMENT_QUERY.format(database=ref.database), query_params)
            row = cur.fetchone()
            table_comment = row[0] if row and row[0] else None

            cur.execute(_COLUMNS_QUERY.format(database=ref.database), query_params)
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
