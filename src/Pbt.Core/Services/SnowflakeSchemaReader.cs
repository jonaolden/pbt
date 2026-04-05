using System.Data;
using Pbt.Core.Models;
using Snowflake.Data.Client;

namespace Pbt.Core.Services;

/// <summary>
/// Reads table and column metadata directly from Snowflake INFORMATION_SCHEMA.
/// Returns CsvSchemaRow list to reuse the existing TableGenerator pipeline.
/// </summary>
public sealed class SnowflakeSchemaReader : ISchemaReader
{
    private readonly SourceTypeConfig _config;

    public SnowflakeSchemaReader(SourceTypeConfig config)
    {
        _config = config;

        if (config.Import == null)
            throw new InvalidOperationException(
                "Source config is missing 'import' section. Add database, schema, and tables.");

        if (config.Connector == null)
            throw new InvalidOperationException(
                "Source config is missing 'connector' section. Add connection and warehouse.");

        if (string.IsNullOrWhiteSpace(config.Import.Database))
            throw new InvalidOperationException("'import.database' is required.");

        if (string.IsNullOrWhiteSpace(config.Import.Schema))
            throw new InvalidOperationException("'import.schema' is required.");
    }

    public List<CsvSchemaRow> ReadSchema()
    {
        var import = _config.Import!;
        var connectionString = BuildConnectionString(_config.Connector!, import);
        var query = BuildQuery(import);

        var rows = new List<CsvSchemaRow>();

        using var conn = new SnowflakeDbConnection(connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = query;

        // Bind schema parameter
        var schemaParam = cmd.CreateParameter();
        schemaParam.ParameterName = "schema";
        schemaParam.DbType = DbType.String;
        schemaParam.Value = import.Schema;
        cmd.Parameters.Add(schemaParam);

        // Bind table name parameters if filtering
        if (!import.ImportAllTables)
        {
            for (int i = 0; i < import.Tables.Count; i++)
            {
                var tableParam = cmd.CreateParameter();
                tableParam.ParameterName = $"table{i}";
                tableParam.DbType = DbType.String;
                tableParam.Value = import.Tables[i].ToUpperInvariant();
                cmd.Parameters.Add(tableParam);
            }
        }

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new CsvSchemaRow
            {
                TableCatalog = GetStringOrNull(reader, "TABLE_CATALOG"),
                TableSchema = GetStringOrNull(reader, "TABLE_SCHEMA"),
                TableName = reader.GetString(reader.GetOrdinal("TABLE_NAME")),
                TableComment = GetStringOrNull(reader, "TABLE_COMMENT"),
                ColumnName = reader.GetString(reader.GetOrdinal("COLUMN_NAME")),
                OrdinalPosition = reader.GetInt32(reader.GetOrdinal("ORDINAL_POSITION")),
                DataType = reader.GetString(reader.GetOrdinal("DATA_TYPE")),
                IsNullable = GetStringOrNull(reader, "IS_NULLABLE"),
                ColumnDefault = GetStringOrNull(reader, "COLUMN_DEFAULT"),
                ColumnComment = GetStringOrNull(reader, "COLUMN_COMMENT")
            });
        }

        if (rows.Count == 0)
        {
            var tableInfo = import.ImportAllTables
                ? "all tables"
                : $"tables: {string.Join(", ", import.Tables)}";

            throw new InvalidOperationException(
                $"No columns found in {import.Database}.{import.Schema} for {tableInfo}. " +
                "Check that the names are correct and your Snowflake role has INFORMATION_SCHEMA access.");
        }

        return rows;
    }

    /// <summary>
    /// Test the connection without running the full import.
    /// </summary>
    public string TestConnection()
    {
        var connectionString = BuildConnectionString(_config.Connector!, _config.Import!);

        using var conn = new SnowflakeDbConnection(connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CURRENT_ACCOUNT(), CURRENT_USER(), CURRENT_ROLE(), CURRENT_WAREHOUSE()";

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return $"Account: {reader.GetString(0)}, User: {reader.GetString(1)}, " +
                   $"Role: {reader.GetString(2)}, Warehouse: {reader.GetString(3)}";
        }

        return "Connected (no account info returned)";
    }

    private static string BuildConnectionString(ConnectorConfig connector, SourceImportConfig import)
    {
        var account = ResolveEnvVar(connector.Connection, "SNOWFLAKE_ACCOUNT");
        var warehouse = connector.Warehouse != null ? ResolveEnvVar(connector.Warehouse, null) : null;
        var user = Environment.GetEnvironmentVariable("SNOWFLAKE_USER")
            ?? throw new InvalidOperationException("SNOWFLAKE_USER environment variable is required");
        var password = Environment.GetEnvironmentVariable("SNOWFLAKE_PASSWORD")
            ?? throw new InvalidOperationException("SNOWFLAKE_PASSWORD environment variable is required");
        var role = Environment.GetEnvironmentVariable("SNOWFLAKE_ROLE");

        var parts = new List<string>
        {
            $"account={account}",
            $"user={user}",
            $"password={password}",
            $"db={import.Database}",
            $"schema={import.Schema}"
        };

        if (!string.IsNullOrWhiteSpace(warehouse))
            parts.Add($"warehouse={warehouse}");

        if (!string.IsNullOrWhiteSpace(role))
            parts.Add($"role={role}");

        return string.Join(";", parts);
    }

    private static string BuildQuery(SourceImportConfig config)
    {
        var tableTypes = config.IncludeViews
            ? "('BASE TABLE', 'VIEW')"
            : "('BASE TABLE')";

        var query = $@"
SELECT
    t.TABLE_CATALOG,
    t.TABLE_SCHEMA,
    t.TABLE_NAME,
    t.COMMENT AS TABLE_COMMENT,
    c.COLUMN_NAME,
    c.ORDINAL_POSITION,
    c.DATA_TYPE,
    c.IS_NULLABLE,
    c.COLUMN_DEFAULT,
    c.COMMENT AS COLUMN_COMMENT
FROM {config.Database}.INFORMATION_SCHEMA.TABLES t
JOIN {config.Database}.INFORMATION_SCHEMA.COLUMNS c
    ON t.TABLE_CATALOG = c.TABLE_CATALOG
    AND t.TABLE_SCHEMA = c.TABLE_SCHEMA
    AND t.TABLE_NAME = c.TABLE_NAME
WHERE t.TABLE_SCHEMA = :schema
    AND t.TABLE_TYPE IN {tableTypes}";

        if (!config.ImportAllTables)
        {
            var paramNames = config.Tables
                .Select((_, i) => $":table{i}")
                .ToList();
            query += $"\n    AND t.TABLE_NAME IN ({string.Join(", ", paramNames)})";
        }

        query += "\nORDER BY t.TABLE_NAME, c.ORDINAL_POSITION";

        return query;
    }

    /// <summary>
    /// Resolve a value that may contain ${ENV_VAR} placeholders or be a literal.
    /// </summary>
    private static string ResolveEnvVar(string value, string? fallbackEnvVar)
    {
        if (value.StartsWith("${") && value.EndsWith("}"))
        {
            var envName = value[2..^1];
            return Environment.GetEnvironmentVariable(envName)
                ?? throw new InvalidOperationException($"Environment variable '{envName}' is not set");
        }

        return value;
    }

    private static string? GetStringOrNull(IDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
}
