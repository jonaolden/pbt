using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Pbt.Core.Models;

namespace Pbt.Core.Services;

/// <summary>
/// Reads CSV schema files (e.g., information_schema exports from Snowflake/SQL Server)
/// </summary>
public sealed class CsvSchemaReader
{
    /// <summary>
    /// Read schema rows from CSV file
    /// </summary>
    public List<CsvSchemaRow> ReadSchema(string csvPath)
    {
        if (!File.Exists(csvPath))
        {
            throw new FileNotFoundException($"CSV file not found: {csvPath}");
        }

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HeaderValidated = null,  // Don't throw on missing headers
            MissingFieldFound = null,  // Don't throw on missing fields
            TrimOptions = TrimOptions.Trim,  // Trim whitespace
            PrepareHeaderForMatch = args => args.Header.ToLower()  // Case-insensitive matching
        };

        using var reader = new StreamReader(csvPath);
        using var csv = new CsvReader(reader, config);

        var records = csv.GetRecords<CsvSchemaRow>().ToList();

        if (records.Count == 0)
        {
            throw new InvalidOperationException($"No records found in CSV file: {csvPath}");
        }

        // Validate required columns
        if (records.Any(r => string.IsNullOrWhiteSpace(r.TableName)))
        {
            throw new InvalidOperationException("CSV must have 'table_name' column");
        }

        if (records.Any(r => string.IsNullOrWhiteSpace(r.ColumnName)))
        {
            throw new InvalidOperationException("CSV must have 'column_name' column");
        }

        if (records.Any(r => string.IsNullOrWhiteSpace(r.DataType)))
        {
            throw new InvalidOperationException("CSV must have 'data_type' column");
        }

        return records;
    }

    /// <summary>
    /// Group schema rows by table name
    /// </summary>
    public Dictionary<string, List<CsvSchemaRow>> GroupByTable(List<CsvSchemaRow> rows)
    {
        return rows
            .GroupBy(r => r.TableName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(r => r.OrdinalPosition ?? int.MaxValue).ToList(),
                StringComparer.OrdinalIgnoreCase
            );
    }
}
