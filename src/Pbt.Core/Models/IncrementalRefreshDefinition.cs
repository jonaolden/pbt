namespace Pbt.Core.Models;

/// <summary>
/// Incremental refresh configuration for a table.
/// Generates RangeStart/RangeEnd expressions and sets the refresh policy on the TOM table.
/// </summary>
public class IncrementalRefreshDefinition
{
    /// <summary>
    /// The date/datetime column used for filtering (source column name).
    /// Example: "OrderDate", "DateKey"
    /// </summary>
    public string DateColumn { get; set; } = string.Empty;

    /// <summary>
    /// Granularity of refresh ranges: Day, Month, Quarter, Year.
    /// </summary>
    public string Granularity { get; set; } = "Day";

    /// <summary>
    /// Number of periods to incrementally refresh (rolling window).
    /// Example: 30 (days), 12 (months)
    /// </summary>
    public int IncrementalPeriods { get; set; } = 30;

    /// <summary>
    /// Number of periods for full refresh (historical window).
    /// Example: 365 (days), 24 (months)
    /// </summary>
    public int IncrementalPeriodOffset { get; set; } = 365;

    /// <summary>
    /// Polling expression to detect data changes (optional).
    /// When set, Power BI only refreshes partitions where data has changed.
    /// Example: "MAX(Sales[ModifiedDate])"
    /// </summary>
    public string? PollingExpression { get; set; }
}
