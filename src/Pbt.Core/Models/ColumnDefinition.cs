namespace Pbt.Core.Models;

/// <summary>
/// Represents a column in a table
/// </summary>
public class ColumnDefinition : MetadataDefinition
{
    /// <summary>
    /// Column name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Data type (String, Int64, DateTime, Decimal, Double, Boolean)
    /// This represents the TMDL type used in Tabular Object Model
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// M type for Power Query (e.g., "Int32.Type", "Text.Type", "DateTime.Type")
    /// Used in Table.TransformColumnTypes for query folding optimization
    /// If not specified, defaults to Type conversion
    /// </summary>
    public string? MType { get; set; }

    /// <summary>
    /// Source column name in the data source (defaults to Name if not specified)
    /// Mutually exclusive with Expression - if Expression is set, this is ignored
    /// </summary>
    public string? SourceColumn { get; set; }

    /// <summary>
    /// DAX expression for calculated columns
    /// When set, creates a calculated column instead of a data column
    /// Mutually exclusive with SourceColumn
    /// </summary>
    public string? Expression { get; set; }

    /// <summary>
    /// Column description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Format string for display
    /// </summary>
    public string? FormatString { get; set; }

    /// <summary>
    /// Whether the column is hidden
    /// </summary>
    public bool? IsHidden { get; set; }

    /// <summary>
    /// Display folder for organizing columns in the field list
    /// </summary>
    public string? DisplayFolder { get; set; }

    /// <summary>
    /// Lineage tag (optional override, usually auto-generated)
    /// </summary>
    public string? LineageTag { get; set; }

    /// <summary>
    /// Name of the column to sort by (for text columns that should be sorted by a numeric column)
    /// For example, "Month Name" might be sorted by "Month" column
    /// </summary>
    public string? SortByColumn { get; set; }

    /// <summary>
    /// Data category for special column behaviors (e.g., City, Country, Latitude, Longitude,
    /// WebUrl, ImageUrl, Barcode, etc.)
    /// </summary>
    public string? DataCategory { get; set; }

    /// <summary>
    /// Default aggregation: None, Sum, Count, Min, Max, Average, DistinctCount
    /// </summary>
    public string? SummarizeBy { get; set; }

    /// <summary>
    /// Marks the column as the table's primary key
    /// </summary>
    public bool? IsKey { get; set; }

    public bool? IsNullable { get; set; }

    public bool? IsUnique { get; set; }

    /// <summary>
    /// Encoding hint: Default, Hash, Value
    /// </summary>
    public string? EncodingHint { get; set; }

    /// <summary>
    /// Marks the column as an aggregation of a detail-table column
    /// </summary>
    public AlternateOfDefinition? AlternateOf { get; set; }
}

/// <summary>
/// Aggregation mapping: this column summarizes a column (or table, for Count) in a detail table
/// </summary>
public class AlternateOfDefinition
{
    /// <summary>
    /// Sum, Min, Max, Count, GroupBy
    /// </summary>
    public string Summarization { get; set; } = "Sum";

    /// <summary>
    /// Detail column as "Table.Column" (Sum, Min, Max, GroupBy)
    /// </summary>
    public string? BaseColumn { get; set; }

    /// <summary>
    /// Detail table (Count)
    /// </summary>
    public string? BaseTable { get; set; }
}
