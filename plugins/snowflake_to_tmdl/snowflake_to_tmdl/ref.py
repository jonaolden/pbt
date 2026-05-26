"""Snowflake fully-qualified reference parsing."""

from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class SnowflakeRef:
    """A fully-qualified Snowflake object reference: DATABASE.SCHEMA.TABLE."""

    database: str
    schema: str
    table: str

    @classmethod
    def parse(cls, ref: str) -> "SnowflakeRef":
        if not ref or not ref.strip():
            raise ValueError("Snowflake ref must not be empty.")

        parts = [p.strip().strip('"') for p in ref.strip().split(".")]
        if len(parts) != 3 or not all(parts):
            raise ValueError(
                f"Invalid Snowflake ref '{ref}'. Expected DATABASE.SCHEMA.TABLE."
            )

        database, schema, table = parts
        return cls(database=database, schema=schema, table=table)

    @property
    def fqn(self) -> str:
        return f"{self.database}.{self.schema}.{self.table}"
