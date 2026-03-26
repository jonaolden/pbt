using System.Data;
using Pbt.Core.Infrastructure;
using Pbt.Core.Models;
using Snowflake.Data.Client;

namespace Pbt.Core.Services;

/// <summary>
/// Reads table and column metadata directly from Snowflake INFORMATION_SCHEMA.
/// Returns List&lt;CsvSchemaRow&gt; to reuse the existing CSV import pipeline
/// (TableGenerator, SourceTypeMapper, SmartMerger).
///
/// Queries INFORMATION_SCHEMA.TABLES (for table comments) joined with
/// INFORMATION_SCHEMA.COLUMNS (for column names, types, ordinal positions, and comments).
/// </summary>
public sealed class SnowflakeSchemaReader
{
    /// <summary>
    /// Build a Snowflake connection string from resolved connector config.
    /// Expects environment variables to already be loaded via EnvResolver.
    /// </summary>
    private static string BuildConnectionString(ConnectorConfig connector, SnowflakeImportConfig import)
    {
        // Resolve ${VAR} placeholders in all connector fields
        var account = EnvResolver.Resolve(connector.Connection);
        var warehouse = EnvResolver.Resolve(connector.Warehouse);
        var user = EnvResolver.Resolve(
            Environment.GetEnvironmentVariable("SNOWFLAKE_USER") ?? "${SNOWFLAKE_USER}");
        var password = EnvResolver.Resolve(
            Environment.GetEnvironmentVariable("SNOWFLAKE_PASSWORD") ?? "${SNOWFLAKE_PASSWORD}");
        var role = Environment.GetEnvironmentVariable("SNOWFLAKE_ROLE");

        // Build connection string
        var parts = new List<string>
        {
            $"account={account}",
            $"user={user}",
            $"password={password}",
            $"db={import.Database}",
            $"schema={import.Schema}"
        };

        if (!string.IsNullOrWhiteSpace(warehouse))
        {
            parts.Add($"warehouse={warehouse}");
        }

        if (!string.IsNullOrWhiteSpace(role))
        {
            parts.Add($"role={role}");
        }

        return string.Join(";", parts);
    }

    /// <summary>
    /// Build the INFORMATION_SCHEMA query.
    /// Joins TABLES (for table comment) with COLUMNS (for column metadata).
    /// Always filters on TABLE_SCHEMA; optionally filters on TABLE_NAME list.
    /// </summary>
    private static string BuildQuery(SnowflakeImportConfig config)
    {
        // Table type filter
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

        // Add table name filter if not importing all
        if (!config.ImportAllTables)
        {
            // Build parameterized IN clause
            var paramNames = config.Tables
                .Select((_, i) => $":table{i}")
                .ToList();
            query += $"\n    AND t.TABLE_NAME IN ({string.Join(", ", paramNames)})";
        }

        query += "\nORDER BY t.TABLE_NAME, c.ORDINAL_POSITION";

        return query;
    }

    /// <summary>
    /// Query Snowflake INFORMATION_SCHEMA and return results as CsvSchemaRow list.
    /// This plugs directly into the existing CsvSchemaReader.GroupByTable()
    /// -> TableGenerator pipeline.
    /// </summary>
    public List<CsvSchemaRow> ReadSchema(SourceTypeConfig sourceConfig)
    {
        if (sourceConfig.Import == null)
        {
            throw new InvalidOperationException(
                "Source config is missing 'import' section. " +
                "Add database, schema, and tables to your snowflake.yaml.");
        }

        if (sourceConfig.Connector == null)
        {
            throw new InvalidOperationException(
                "Source config is missing 'connector' section. " +
                "Add connection, warehouse, and name to your snowflake.yaml.");
        }

        var import = sourceConfig.Import;

        // Validate import config
        if (string.IsNullOrWhiteSpace(import.Database))
        {
            throw new InvalidOperationException(
                "'import.database' is required in snowflake.yaml");
        }

        if (string.IsNullOrWhiteSpace(import.Schema))
        {
            throw new InvalidOperationException(
                "'import.schema' is required in snowflake.yaml");
        }

        // Build connection and query
        var connectionString = BuildConnectionString(
            sourceConfig.Connector, import);
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
                TableCatalog = reader.GetString(
                    reader.GetOrdinal("TABLE_CATALOG")),
                TableSchema = reader.GetString(
                    reader.GetOrdinal("TABLE_SCHEMA")),
                TableName = reader.GetString(
                    reader.GetOrdinal("TABLE_NAME")),
                TableComment = reader.IsDBNull(
                    reader.GetOrdinal("TABLE_COMMENT"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("TABLE_COMMENT")),
                ColumnName = reader.GetString(
                    reader.GetOrdinal("COLUMN_NAME")),
                OrdinalPosition = reader.GetInt32(
                    reader.GetOrdinal("ORDINAL_POSITION")),
                DataType = reader.GetString(
                    reader.GetOrdinal("DATA_TYPE")),
                IsNullable = reader.GetString(
                    reader.GetOrdinal("IS_NULLABLE")),
                ColumnDefault = reader.IsDBNull(
                    reader.GetOrdinal("COLUMN_DEFAULT"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("COLUMN_DEFAULT")),
                ColumnComment = reader.IsDBNull(
                    reader.GetOrdinal("COLUMN_COMMENT"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("COLUMN_COMMENT"))
            });
        }

        if (rows.Count == 0)
        {
            var tableInfo = import.ImportAllTables
                ? "all tables"
                : $"tables: {string.Join(", ", import.Tables)}";

            throw new InvalidOperationException(
                $"No columns found in {import.Database}.{import.Schema} " +
                $"for {tableInfo}. " +
                "Check that the database, schema, and table names are correct, " +
                "and that your Snowflake role has SELECT access to " +
                "INFORMATION_SCHEMA.");
        }

        return rows;
    }

    /// <summary>
    /// Test the Snowflake connection without running the full import.
    /// Returns account info on success.
    /// </summary>
    public string TestConnection(SourceTypeConfig sourceConfig)
    {
        if (sourceConfig.Connector == null || sourceConfig.Import == null)
        {
            throw new InvalidOperationException(
                "Connector and import sections are required.");
        }

        var connectionString = BuildConnectionString(
            sourceConfig.Connector, sourceConfig.Import);

        using var conn = new SnowflakeDbConnection(connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CURRENT_ACCOUNT(), CURRENT_USER(), " +
                          "CURRENT_ROLE(), CURRENT_WAREHOUSE()";

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return $"Account: {reader.GetString(0)}, " +
                   $"User: {reader.GetString(1)}, " +
                   $"Role: {reader.GetString(2)}, " +
                   $"Warehouse: {reader.GetString(3)}";
        }

        return "Connected (no account info returned)";
    }
}
