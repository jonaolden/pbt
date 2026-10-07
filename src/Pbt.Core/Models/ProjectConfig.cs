namespace Pbt.Core.Models;

/// <summary>
/// Project-level generation config (pbt.yml): concise table declarations expanded by <c>generate-tables</c>
/// </summary>
public class ProjectConfig
{
    /// <summary>
    /// Named source connections, keyed by the <c>source</c> name used in <see cref="Tables"/> (e.g. "snowflake")
    /// </summary>
    public Dictionary<string, SourceConnectionConfig> Sources { get; set; } = new();

    /// <summary>
    /// Optional model YAML (relative to pbt.yml). Generated tables are added to its <c>tables</c> refs.
    /// </summary>
    public string? Model { get; set; }

    public List<TableGenSpec> Tables { get; set; } = new();
}

public class SourceConnectionConfig
{
    /// <summary>
    /// Name of a shared connector expression (preferred; keeps account/warehouse out of table files)
    /// </summary>
    public string? Connector { get; set; }

    /// <summary>
    /// Literal connection string (no secrets; ${VAR} is not resolved here)
    /// </summary>
    public string? Connection { get; set; }
}

public class TableGenSpec
{
    public string TableName { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
}
