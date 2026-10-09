using Pbt.Core.Models;

namespace Pbt.Core.Services;

/// <summary>
/// Source-specific part of table generation. M rendering is not here: the returned
/// <see cref="SourceDefinition"/> is expanded to M by <c>ModelComposer</c> at build time,
/// and source-to-TMDL type mapping is owned by the metadata provider (see <see cref="SourceTypes"/>).
/// </summary>
public interface ISourceAdapter
{
    /// <summary>Returns an actionable error message, or null if the target is valid.</summary>
    string? ValidateTarget(string target);

    /// <summary>Columns (source order) and description from the live source. Throws on lookup failure.</summary>
    TableDefinition GetTableMetadata(string target);

    SourceDefinition ToSource(string target, SourceConnectionConfig connection);
}

/// <summary>Source DATA_TYPE -> TMDL type, shared by all adapters. Precision/scale ignored; unknown and semi-structured types become String.</summary>
public static class SourceTypes
{
    public static string ToTmdl(string dataType)
    {
        var key = dataType.Split('(')[0].Trim().ToUpperInvariant();
        return key switch
        {
            // Snowflake + SQL Server (names that exist in both agree)
            "NUMBER" or "INT" or "INTEGER" or "BIGINT" or "SMALLINT" or "TINYINT" or "BYTEINT" => "Int64",
            "DECIMAL" or "NUMERIC" or "MONEY" or "SMALLMONEY" => "Decimal",
            "FLOAT" or "FLOAT4" or "FLOAT8" or "DOUBLE" or "DOUBLE PRECISION" or "REAL" => "Double",
            "DATE" or "DATETIME" or "DATETIME2" or "SMALLDATETIME" or "DATETIMEOFFSET" or "TIME"
                or "TIMESTAMP" or "TIMESTAMP_NTZ" or "TIMESTAMP_LTZ" or "TIMESTAMP_TZ" => "DateTime",
            "BOOLEAN" or "BOOL" or "BIT" => "Boolean",
            "BINARY" or "VARBINARY" or "IMAGE" => "Binary",
            _ => "String"
        };
    }

    /// <summary>Numeric columns get no default aggregation, matching the old plugins.</summary>
    public static string? SummarizeBy(string tmdlType) => tmdlType is "Int64" or "Decimal" or "Double" ? "None" : null;
}
