using System.Text;
using System.Text.RegularExpressions;
using Pbt.Core.Models;

namespace Pbt.Core.Services;

public static partial class SnowflakeTarget
{
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_$]*$")]
    private static partial Regex Plain();

    /// <summary>
    /// Parse <c>database.schema.table</c>. Unquoted parts fold to upper case (Snowflake rules);
    /// <c>"quoted"</c> parts keep case, with <c>""</c> as an escaped quote.
    /// </summary>
    public static (string Database, string Schema, string Table) Parse(string target)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false, quoted = false;

        void EndPart()
        {
            var raw = sb.ToString();
            if (raw.Length == 0) throw new ArgumentException($"Empty identifier in '{target}'.");
            if (!quoted && !Plain().IsMatch(raw))
                throw new ArgumentException($"Identifier '{raw}' in '{target}' needs double quotes (e.g. \"{raw}\").");
            if (quoted && raw.Contains('.'))
                throw new ArgumentException($"Identifier '{raw}' contains '.', which is not supported.");
            parts.Add(quoted ? raw : raw.ToUpperInvariant());
            sb.Clear();
            quoted = false;
        }

        for (var i = 0; i < target.Length; i++)
        {
            var c = target[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < target.Length && target[i + 1] == '"') { sb.Append('"'); i++; }
                else { inQuotes = !inQuotes; quoted = true; }
            }
            else if (c == '.' && !inQuotes) EndPart();
            else sb.Append(c);
        }
        if (inQuotes) throw new ArgumentException($"Unterminated quote in '{target}'.");
        EndPart();

        if (parts.Count != 3)
            throw new ArgumentException($"Snowflake target '{target}' has {parts.Count} component(s); expected database.schema.table.");
        return (parts[0], parts[1], parts[2]);
    }
}

/// <summary>
/// Snowflake adapter: reads INFORMATION_SCHEMA in-process via Snowflake.Data. Credentials come from
/// <c>SNOWFLAKE_*</c> env vars, never from project files and never logged.
/// </summary>
public sealed class SnowflakeAdapter : ISourceAdapter
{
    private readonly Func<string, string, string, TableDefinition> _fetch;

    /// <param name="fetch">(database, schema, table) -> metadata; overridable for tests</param>
    public SnowflakeAdapter(Func<string, string, string, TableDefinition>? fetch = null)
    {
        _fetch = fetch ?? SnowflakeMetadata.Fetch;
    }

    public string? ValidateTarget(string target)
    {
        try { SnowflakeTarget.Parse(target); return null; }
        catch (ArgumentException ex) { return ex.Message; }
    }

    public TableDefinition GetTableMetadata(string target)
    {
        var (db, schema, table) = SnowflakeTarget.Parse(target);
        return _fetch(db, schema, table);
    }

    public SourceDefinition ToSource(string target, SourceConnectionConfig connection)
    {
        var (db, schema, table) = SnowflakeTarget.Parse(target);
        return new SourceDefinition
        {
            Type = "snowflake",
            Connector = connection.Connector,
            Connection = connection.Connection,
            Database = db,
            Schema = schema,
            Table = table
        };
    }
}

/// <summary>INFORMATION_SCHEMA reader. Only COLUMNS (plus TABLES for the comment); no PK/FK metadata.</summary>
public static class SnowflakeMetadata
{
    private static readonly string[] Authenticators = { "externalbrowser", "snowflake", "snowflake_jwt" };

    /// <summary>
    /// Auth comes from SNOWFLAKE_AUTHENTICATOR, else inferred: password -> snowflake,
    /// SNOWFLAKE_PRIVATE_KEY_FILE -> snowflake_jwt, otherwise externalbrowser (SSO).
    /// </summary>
    public static string BuildConnectionString(string database, string schema, Func<string, string?> env)
    {
        var account = env("SNOWFLAKE_ACCOUNT")?.Trim();
        if (string.IsNullOrEmpty(account))
            throw new InvalidOperationException(
                "SNOWFLAKE_ACCOUNT environment variable is required (e.g. 'myorg-myaccount'). See .env.example.");
        const string suffix = ".snowflakecomputing.com";
        if (account.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) account = account[..^suffix.Length];

        var password = env("SNOWFLAKE_PASSWORD");
        var keyFile = env("SNOWFLAKE_PRIVATE_KEY_FILE");
        var auth = env("SNOWFLAKE_AUTHENTICATOR")?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(auth))
            auth = !string.IsNullOrEmpty(password) ? "snowflake" : !string.IsNullOrEmpty(keyFile) ? "snowflake_jwt" : "externalbrowser";
        else if (!Authenticators.Contains(auth))
            throw new InvalidOperationException(
                $"Invalid SNOWFLAKE_AUTHENTICATOR '{auth}'. Must be one of: {string.Join(", ", Authenticators)}.");
        if (auth == "snowflake_jwt" && string.IsNullOrEmpty(keyFile))
            throw new InvalidOperationException("SNOWFLAKE_AUTHENTICATOR=snowflake_jwt requires SNOWFLAKE_PRIVATE_KEY_FILE.");

        var b = new Snowflake.Data.Client.SnowflakeDbConnectionStringBuilder
        {
            ["account"] = account, ["authenticator"] = auth, ["db"] = database, ["schema"] = schema
        };
        void Set(string key, string? value) { if (!string.IsNullOrEmpty(value)) b[key] = value; }
        Set("user", env("SNOWFLAKE_USER"));
        if (auth == "snowflake") Set("password", password);
        if (auth == "snowflake_jwt") Set("private_key_file", keyFile);
        Set("warehouse", env("SNOWFLAKE_WAREHOUSE"));
        Set("role", env("SNOWFLAKE_ROLE"));
        return b.ConnectionString;
    }

    public static TableDefinition Fetch(string database, string schema, string table)
    {
        using var conn = new Snowflake.Data.Client.SnowflakeDbConnection(
            BuildConnectionString(database, schema, Environment.GetEnvironmentVariable));
        conn.Open();

        var db = "\"" + database.Replace("\"", "\"\"") + "\"";
        string? comment;
        using (var cmd = Query(conn, $"SELECT COMMENT FROM {db}.INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = ? AND TABLE_NAME = ?", schema, table))
            comment = cmd.ExecuteScalar() as string;

        var columns = new List<ColumnDefinition>();
        using (var cmd = Query(conn,
            $"SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, COMMENT FROM {db}.INFORMATION_SCHEMA.COLUMNS " +
            "WHERE TABLE_SCHEMA = ? AND TABLE_NAME = ? ORDER BY ORDINAL_POSITION", schema, table))
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                var type = SourceTypes.ToTmdl(r.GetString(1));
                columns.Add(new ColumnDefinition
                {
                    Name = r.GetString(0),
                    Type = type,
                    SourceColumn = r.GetString(0),
                    IsNullable = string.Equals(r.GetString(2), "YES", StringComparison.OrdinalIgnoreCase),
                    Description = r.IsDBNull(3) || r.GetString(3).Length == 0 ? null : r.GetString(3),
                    SummarizeBy = SourceTypes.SummarizeBy(type)
                });
            }

        if (columns.Count == 0)
            throw new InvalidOperationException(
                $"No columns found for {database}.{schema}.{table}. Check the target and that the role has INFORMATION_SCHEMA access.");
        return new TableDefinition { Name = table, Description = string.IsNullOrEmpty(comment) ? null : comment, Columns = columns };
    }

    private static System.Data.IDbCommand Query(System.Data.IDbConnection conn, string sql, params string[] args)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        for (var i = 0; i < args.Length; i++)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = (i + 1).ToString();
            p.DbType = System.Data.DbType.String;
            p.Value = args[i];
            cmd.Parameters.Add(p);
        }
        return cmd;
    }
}
