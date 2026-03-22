namespace Pbt.Core.Models;

/// <summary>
/// Tracks what was generated during scaffolding to enable smart merge
/// </summary>
public class ScaffoldManifest
{
    /// <summary>
    /// Timestamp of last scaffolding run
    /// </summary>
    public DateTime LastScaffolded { get; set; }

    /// <summary>
    /// List of generated tables
    /// </summary>
    public List<GeneratedTable> GeneratedTables { get; set; } = new();
}

/// <summary>
/// Metadata about a generated table
/// </summary>
public class GeneratedTable
{
    /// <summary>
    /// Table name
    /// </summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Path to the generated YAML file
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Column names that were generated from CSV
    /// </summary>
    public List<string> ColumnsGenerated { get; set; } = new();

    /// <summary>
    /// Column types that were generated (for detecting type changes)
    /// </summary>
    public Dictionary<string, string> ColumnTypes { get; set; } = new();

    /// <summary>
    /// Source schema
    /// </summary>
    public string? SourceSchema { get; set; }

    /// <summary>
    /// Source table name
    /// </summary>
    public string? SourceTable { get; set; }
}
