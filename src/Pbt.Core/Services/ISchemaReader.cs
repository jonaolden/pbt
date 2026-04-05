using Pbt.Core.Models;

namespace Pbt.Core.Services;

/// <summary>
/// Reads table/column metadata from a data source.
/// Returns CsvSchemaRow list to reuse the existing TableGenerator pipeline.
/// Implementations: CsvSchemaReader (file), SnowflakeSchemaReader (live query).
/// </summary>
public interface ISchemaReader
{
    List<CsvSchemaRow> ReadSchema();
}
