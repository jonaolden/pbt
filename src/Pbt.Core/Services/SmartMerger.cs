using Pbt.Core.Infrastructure;
using Pbt.Core.Models;

namespace Pbt.Core.Services;

/// <summary>
/// Smart merge service that preserves manual edits when re-scaffolding
/// </summary>
public class SmartMerger
{
    private readonly YamlSerializer _serializer;
    private readonly MergeOptions _options;

    public SmartMerger(MergeOptions options)
    {
        _serializer = new YamlSerializer();
        _options = options;
    }

    /// <summary>
    /// Merge generated table with existing table (if exists), preserving manual edits
    /// </summary>
    public TableDefinition MergeTable(TableDefinition generated, string filePath)
    {
        // If file doesn't exist, return generated as-is (first run)
        if (!File.Exists(filePath))
        {
            return generated;
        }

        // Load existing table
        TableDefinition existing;
        try
        {
            existing = _serializer.LoadFromFile<TableDefinition>(filePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not load existing file '{filePath}': {ex.Message}");
            Console.WriteLine("Using generated table definition.");
            return generated;
        }

        // Create merged table
        var merged = new TableDefinition
        {
            // ALWAYS preserve existing name (user may have renamed)
            Name = existing.Name,

            // Preserve manual description, fallback to generated
            Description = !string.IsNullOrEmpty(existing.Description) && !_options.OverwriteDescriptions
                ? existing.Description
                : generated.Description ?? existing.Description,

            // ALWAYS PRESERVE hierarchies (CRITICAL - never touch manual hierarchies)
            Hierarchies = existing.Hierarchies,

            // ALWAYS PRESERVE measures (CRITICAL - never touch manual measures)
            Measures = existing.Measures,

            // Update source from generated (CSV is source of truth for schema/table)
            Source = generated.Source,

            // Preserve MExpression if exists (manual override wins)
            MExpression = existing.MExpression,

            // Preserve manual settings
            IsHidden = existing.IsHidden,
            LineageTag = existing.LineageTag,

            Columns = new List<ColumnDefinition>()
        };

        // Build column lookup for existing columns
        // Match by SourceColumn first (actual data source column), fall back to Name for calculated columns
        var existingColumnsBySource = existing.Columns
            .Where(c => !string.IsNullOrEmpty(c.SourceColumn))
            .ToDictionary(c => c.SourceColumn!, StringComparer.OrdinalIgnoreCase);

        // For calculated columns (those with Expression), match by Name
        var existingColumnsByName = existing.Columns
            .Where(c => string.IsNullOrEmpty(c.SourceColumn))
            .ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        // Track which existing columns have been matched to avoid duplicates
        var matchedExistingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Merge columns
        foreach (var genColumn in generated.Columns)
        {
            ColumnDefinition? existingColumn = null;

            // Match by SourceColumn (preferred - handles renamed columns)
            if (!string.IsNullOrEmpty(genColumn.SourceColumn) &&
                existingColumnsBySource.TryGetValue(genColumn.SourceColumn, out existingColumn))
            {
                matchedExistingColumns.Add(existingColumn.Name);
            }
            // Fall back to Name match for calculated columns without SourceColumn
            else if (string.IsNullOrEmpty(genColumn.SourceColumn) &&
                     existingColumnsByName.TryGetValue(genColumn.Name, out existingColumn))
            {
                matchedExistingColumns.Add(existingColumn.Name);
            }

            if (existingColumn != null)
            {
                // Column exists - merge properties
                var mergedColumn = new ColumnDefinition
                {
                    Name = genColumn.Name,

                    // CSV is source of truth for type (always update if configured)
                    Type = _options.UpdateTypes ? genColumn.Type : existingColumn.Type,

                    // M type: update from source if available, otherwise preserve existing
                    MType = _options.UpdateTypes && !string.IsNullOrEmpty(genColumn.MType)
                        ? genColumn.MType
                        : existingColumn.MType ?? genColumn.MType,

                    // CSV is source of truth for source column (only for data columns)
                    SourceColumn = genColumn.SourceColumn,

                    // Preserve Expression for calculated columns
                    // If generated has expression, use it; otherwise preserve existing
                    Expression = !string.IsNullOrEmpty(genColumn.Expression)
                        ? genColumn.Expression
                        : existingColumn.Expression,

                    // Preserve manual description, fallback to CSV comment
                    Description = !string.IsNullOrEmpty(existingColumn.Description) && !_options.OverwriteDescriptions
                        ? existingColumn.Description
                        : genColumn.Description ?? existingColumn.Description,

                    // Preserve manual format string, fallback to generated
                    FormatString = existingColumn.FormatString ?? genColumn.FormatString,

                    // Preserve manual display folder, fallback to generated
                    DisplayFolder = existingColumn.DisplayFolder ?? genColumn.DisplayFolder,

                    // ALWAYS preserve manual settings
                    IsHidden = existingColumn.IsHidden,
                    LineageTag = existingColumn.LineageTag,

                    // Preserve SortByColumn (manual setting)
                    SortByColumn = existingColumn.SortByColumn ?? genColumn.SortByColumn
                };

                merged.Columns.Add(mergedColumn);

                // Warn if type changed
                if (_options.UpdateTypes && existingColumn.Type != genColumn.Type && !_options.DryRun)
                {
                    Console.WriteLine($"  ! Column '{genColumn.Name}' type changed: {existingColumn.Type} -> {genColumn.Type}");
                }
            }
            else
            {
                // New column - add it
                merged.Columns.Add(genColumn);

                if (!_options.DryRun)
                {
                    Console.WriteLine($"  + Adding new column: {genColumn.Name} ({genColumn.Type})");
                }
            }
        }

        // Handle deleted columns (columns in existing but not matched to any generated column)
        var deletedColumns = existing.Columns.Where(c => !matchedExistingColumns.Contains(c.Name)).ToList();

        if (!_options.PruneDeleted)
        {
            // Keep deleted columns by default (safer)
            foreach (var deletedColumn in deletedColumns)
            {
                merged.Columns.Add(deletedColumn);

                if (!_options.DryRun)
                {
                    Console.WriteLine($"  ~ Preserving removed column: {deletedColumn.Name} (not in CSV)");
                }
            }
        }
        else
        {
            // Prune deleted columns
            if (deletedColumns.Count > 0 && !_options.DryRun)
            {
                Console.WriteLine($"  - Removing {deletedColumns.Count} column(s) not in CSV");
                foreach (var col in deletedColumns)
                {
                    Console.WriteLine($"    - {col.Name}");
                }
            }
        }

        return merged;
    }

    /// <summary>
    /// Print merge preview (dry run mode)
    /// </summary>
    public void PrintMergePreview(TableDefinition generated, string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"  [NEW] Will create {Path.GetFileName(filePath)}");
            Console.WriteLine($"        {generated.Columns.Count} columns");
            return;
        }

        TableDefinition existing;
        try
        {
            existing = _serializer.LoadFromFile<TableDefinition>(filePath);
        }
        catch
        {
            Console.WriteLine($"  [ERROR] Cannot read existing file: {Path.GetFileName(filePath)}");
            return;
        }

        // Build lookups - match by SourceColumn like in MergeTable
        var existingColumnsBySource = existing.Columns
            .Where(c => !string.IsNullOrEmpty(c.SourceColumn))
            .ToDictionary(c => c.SourceColumn!, StringComparer.OrdinalIgnoreCase);

        var existingColumnsByName = existing.Columns
            .Where(c => string.IsNullOrEmpty(c.SourceColumn))
            .ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        var matchedExistingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var newColumns = new List<ColumnDefinition>();
        var typeChanges = new List<ColumnDefinition>();

        foreach (var genColumn in generated.Columns)
        {
            ColumnDefinition? existingColumn = null;

            // Match by SourceColumn first
            if (!string.IsNullOrEmpty(genColumn.SourceColumn) &&
                existingColumnsBySource.TryGetValue(genColumn.SourceColumn, out existingColumn))
            {
                matchedExistingColumns.Add(existingColumn.Name);

                // Check for type changes
                if (existingColumn.Type != genColumn.Type)
                {
                    typeChanges.Add(genColumn);
                }
            }
            // Fall back to Name match
            else if (string.IsNullOrEmpty(genColumn.SourceColumn) &&
                     existingColumnsByName.TryGetValue(genColumn.Name, out existingColumn))
            {
                matchedExistingColumns.Add(existingColumn.Name);

                if (existingColumn.Type != genColumn.Type)
                {
                    typeChanges.Add(genColumn);
                }
            }
            else
            {
                // New column
                newColumns.Add(genColumn);
            }
        }

        var deletedColumns = existing.Columns.Where(c => !matchedExistingColumns.Contains(c.Name)).ToList();

        Console.WriteLine($"\n  {Path.GetFileName(filePath)}:");

        if (newColumns.Count > 0)
        {
            Console.WriteLine($"    + Add: {string.Join(", ", newColumns.Select(c => $"{c.Name} ({c.Type})"))}");
        }

        if (typeChanges.Count > 0)
        {
            Console.WriteLine($"    ~ Update types:");
            foreach (var genCol in typeChanges)
            {
                // Find the existing column to show old type
                ColumnDefinition? existingCol = null;
                if (!string.IsNullOrEmpty(genCol.SourceColumn))
                {
                    existingColumnsBySource.TryGetValue(genCol.SourceColumn, out existingCol);
                }
                else
                {
                    existingColumnsByName.TryGetValue(genCol.Name, out existingCol);
                }

                if (existingCol != null)
                {
                    Console.WriteLine($"      - {genCol.Name}: {existingCol.Type} -> {genCol.Type}");
                }
            }
        }

        if (deletedColumns.Count > 0)
        {
            if (_options.PruneDeleted)
            {
                Console.WriteLine($"    - Remove: {string.Join(", ", deletedColumns.Select(c => c.Name))}");
            }
            else
            {
                Console.WriteLine($"    ~ Keep deleted: {string.Join(", ", deletedColumns.Select(c => c.Name))}");
            }
        }

        if (existing.Hierarchies.Count > 0)
        {
            Console.WriteLine($"    ✓ Preserve: {existing.Hierarchies.Count} hierarchy/hierarchies");
        }

        var manualDescriptions = existing.Columns.Count(c => !string.IsNullOrEmpty(c.Description));
        if (manualDescriptions > 0)
        {
            Console.WriteLine($"    ✓ Preserve: {manualDescriptions} manual description(s)");
        }

        if (newColumns.Count == 0 && typeChanges.Count == 0 && deletedColumns.Count == 0)
        {
            Console.WriteLine($"    ✓ No changes");
        }
    }
}
