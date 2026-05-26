import pytest

from snowflake_to_tmdl.snowflake_client import (
    _normalize_account,
    build_connection_params,
)


_SNOWFLAKE_ENV_VARS = (
    "SNOWFLAKE_ACCOUNT",
    "SNOWFLAKE_USER",
    "SNOWFLAKE_PASSWORD",
    "SNOWFLAKE_PRIVATE_KEY_FILE",
    "SNOWFLAKE_AUTHENTICATOR",
    "SNOWFLAKE_WAREHOUSE",
    "SNOWFLAKE_ROLE",
)


@pytest.fixture(autouse=True)
def _clean_env(monkeypatch):
    for var in _SNOWFLAKE_ENV_VARS:
        monkeypatch.delenv(var, raising=False)
    yield


# ─── _normalize_account ────────────────────────────────────────────────


def test_normalize_account_strips_suffix():
    assert _normalize_account("myorg-acct.snowflakecomputing.com") == "myorg-acct"


def test_normalize_account_case_insensitive_suffix():
    assert _normalize_account("MYORG-ACCT.SnowflakeComputing.com") == "MYORG-ACCT"


def test_normalize_account_passes_through_bare_locator():
    assert _normalize_account("myorg-acct") == "myorg-acct"


def test_normalize_account_strips_whitespace():
    assert _normalize_account("  myorg-acct  ") == "myorg-acct"


# ─── build_connection_params: account requirement ─────────────────────


def test_requires_account():
    with pytest.raises(RuntimeError, match="SNOWFLAKE_ACCOUNT"):
        build_connection_params()


# ─── inferred auth method ─────────────────────────────────────────────


def test_infers_password_auth(monkeypatch):
    monkeypatch.setenv("SNOWFLAKE_ACCOUNT", "acct")
    monkeypatch.setenv("SNOWFLAKE_USER", "alice")
    monkeypatch.setenv("SNOWFLAKE_PASSWORD", "secret")
    params = build_connection_params()
    assert params["authenticator"] == "snowflake"
    assert params["password"] == "secret"
    assert params["user"] == "alice"
    assert "private_key_file" not in params


def test_infers_jwt_auth(monkeypatch):
    monkeypatch.setenv("SNOWFLAKE_ACCOUNT", "acct")
    monkeypatch.setenv("SNOWFLAKE_USER", "alice")
    monkeypatch.setenv("SNOWFLAKE_PRIVATE_KEY_FILE", "/keys/rsa.p8")
    params = build_connection_params()
    assert params["authenticator"] == "snowflake_jwt"
    assert params["private_key_file"] == "/keys/rsa.p8"
    assert "password" not in params


def test_infers_externalbrowser_default(monkeypatch):
    monkeypatch.setenv("SNOWFLAKE_ACCOUNT", "acct")
    monkeypatch.setenv("SNOWFLAKE_USER", "alice")
    params = build_connection_params()
    assert params["authenticator"] == "externalbrowser"
    assert "password" not in params
    assert "private_key_file" not in params


def test_password_wins_over_key_when_both_set(monkeypatch):
    monkeypatch.setenv("SNOWFLAKE_ACCOUNT", "acct")
    monkeypatch.setenv("SNOWFLAKE_PASSWORD", "secret")
    monkeypatch.setenv("SNOWFLAKE_PRIVATE_KEY_FILE", "/keys/rsa.p8")
    params = build_connection_params()
    assert params["authenticator"] == "snowflake"
    assert "private_key_file" not in params


# ─── explicit SNOWFLAKE_AUTHENTICATOR ─────────────────────────────────


def test_explicit_externalbrowser_ignores_password(monkeypatch):
    monkeypatch.setenv("SNOWFLAKE_ACCOUNT", "acct")
    monkeypatch.setenv("SNOWFLAKE_USER", "alice")
    monkeypatch.setenv("SNOWFLAKE_PASSWORD", "secret")
    monkeypatch.setenv("SNOWFLAKE_AUTHENTICATOR", "externalbrowser")
    params = build_connection_params()
    assert params["authenticator"] == "externalbrowser"
    assert "password" not in params


def test_explicit_jwt_requires_key_file(monkeypatch):
    monkeypatch.setenv("SNOWFLAKE_ACCOUNT", "acct")
    monkeypatch.setenv("SNOWFLAKE_AUTHENTICATOR", "snowflake_jwt")
    with pytest.raises(RuntimeError, match="SNOWFLAKE_PRIVATE_KEY_FILE"):
        build_connection_params()


def test_explicit_authenticator_is_case_insensitive(monkeypatch):
    monkeypatch.setenv("SNOWFLAKE_ACCOUNT", "acct")
    monkeypatch.setenv("SNOWFLAKE_PASSWORD", "secret")
    monkeypatch.setenv("SNOWFLAKE_AUTHENTICATOR", "SNOWFLAKE")
    params = build_connection_params()
    assert params["authenticator"] == "snowflake"


def test_invalid_authenticator_rejected(monkeypatch):
    monkeypatch.setenv("SNOWFLAKE_ACCOUNT", "acct")
    monkeypatch.setenv("SNOWFLAKE_AUTHENTICATOR", "oauth2")
    with pytest.raises(RuntimeError, match="Invalid SNOWFLAKE_AUTHENTICATOR"):
        build_connection_params()


# ─── optional fields and database/schema args ─────────────────────────


def test_warehouse_and_role_included_when_set(monkeypatch):
    monkeypatch.setenv("SNOWFLAKE_ACCOUNT", "acct")
    monkeypatch.setenv("SNOWFLAKE_WAREHOUSE", "WH")
    monkeypatch.setenv("SNOWFLAKE_ROLE", "ANALYST")
    params = build_connection_params()
    assert params["warehouse"] == "WH"
    assert params["role"] == "ANALYST"


def test_optional_fields_omitted_when_unset(monkeypatch):
    monkeypatch.setenv("SNOWFLAKE_ACCOUNT", "acct")
    params = build_connection_params()
    for key in ("warehouse", "role", "user", "database", "schema"):
        assert key not in params


def test_database_and_schema_args_passed_through(monkeypatch):
    monkeypatch.setenv("SNOWFLAKE_ACCOUNT", "acct")
    params = build_connection_params(database="DB", schema="SCH")
    assert params["database"] == "DB"
    assert params["schema"] == "SCH"


def test_account_is_normalized_in_output(monkeypatch):
    monkeypatch.setenv(
        "SNOWFLAKE_ACCOUNT", "myorg-acct.snowflakecomputing.com"
    )
    params = build_connection_params()
    assert params["account"] == "myorg-acct"
