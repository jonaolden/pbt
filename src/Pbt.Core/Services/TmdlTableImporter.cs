using Microsoft.AnalysisServices.Tabular;
using Pbt.Core.Infrastructure;
using Pbt.Core.Models;

namespace Pbt.Core.Services;

public class TmdlTableImporter
{
    private readonly YamlSerializer _serializer;

    public TmdlTableImporter(YamlSerializer serializer)
    {
        _serializer = serializer;
    }

    /// <summary>
    /// Extract all tables from a TMDL directory or file
    /// Supports both complete TMDL models and individual table TMDL files
    /// </summary>
    public List<TableDefinition> ExtractTables(string tmdlPath, bool includeLineageTags = false)
    {
        // Validate path
        if (!Directory.Exists(tmdlPath) && !File.Exists(tmdlPath))
        {
            throw new FileNotFoundException($"TMDL path not found: {tmdlPath}");
        }

        // If a specific file is provided, only import that file
        if (File.Exists(tmdlPath))
        {
            var tableDef = ExtractTableFromFile(tmdlPath, includeLineageTags);
            return tableDef != null ? new List<TableDefinition> { tableDef } : new List<TableDefinition>();
        }

        // Directory processing
        var directoryPath = tmdlPath;

        // Check if it's a complete TMDL model or individual table files
        var isCompleteModel = IsCompleteTmdlModel(directoryPath);

        if (isCompleteModel)
        {
            return ExtractTablesFromCompleteModel(directoryPath, includeLineageTags);
        }
        else
        {
            return ExtractTablesFromIndividualFiles(directoryPath, includeLineageTags);
        }
    }

    /// <summary>
    /// Extract a single table by name from a TMDL directory or file
    /// </summary>
    public TableDefinition? ExtractTable(string tmdlPath, string tableName, bool includeLineageTags = false)
    {
        // For single table extraction, use the same logic as ExtractTables but filter
        var tables = ExtractTables(tmdlPath, includeLineageTags);
        return tables.FirstOrDefault(t => t.Name == tableName);
    }

    /// <summary>
    /// Check if directory contains a complete TMDL model structure
    /// </summary>
    private bool IsCompleteTmdlModel(string directoryPath)
    {
        var databaseTmdlPath = Path.Combine(directoryPath, "database.tmdl");
        var definitionPath = Path.Combine(directoryPath, "definition");

        return File.Exists(databaseTmdlPath) || Directory.Exists(definitionPath);
    }

    /// <summary>
    /// Extract tables from a complete TMDL model (with database.tmdl)
    /// </summary>
    private List<TableDefinition> ExtractTablesFromCompleteModel(string directoryPath, bool includeLineageTags)
    {
        Database database;
        try
        {
            database = TmdlSerializer.DeserializeDatabaseFromFolder(directoryPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to deserialize TMDL model from: {directoryPath}\n\n" +
                $"Error: {ex.Message}", ex);
        }

        if (database.Model == null)
        {
            throw new InvalidOperationException("TMDL does not contain a valid model");
        }

        // Extract all tables
        var tables = new List<TableDefinition>();
        foreach (var table in database.Model.Tables)
        {
            var tableDef = ConvertToTableDefinition(table, includeLineageTags);
            tables.Add(tableDef);
        }

        return tables;
    }

    /// <summary>
    /// Extract tables from individual TMDL files in a directory
    /// </summary>
    private List<TableDefinition> ExtractTablesFromIndividualFiles(string directoryPath, bool includeLineageTags)
    {
        var tmdlFiles = Directory.GetFiles(directoryPath, "*.tmdl");

        if (tmdlFiles.Length == 0)
        {
            throw new InvalidOperationException(
                $"No TMDL files found in directory: {directoryPath}\n\n" +
                "Expected either:\n" +
                "  - Individual table TMDL files (*.tmdl)\n" +
                "  - Complete TMDL model (database.tmdl + definition/ folder)");
        }

        var tables = new List<TableDefinition>();
        var failedTables = new List<(string fileName, string error)>();

        foreach (var tmdlFile in tmdlFiles)
        {
            try
            {
                var tableDef = ExtractTableFromFile(tmdlFile, includeLineageTags);
                if (tableDef != null)
                {
                    tables.Add(tableDef);
                }
            }
            catch (Exception ex)
            {
                failedTables.Add((Path.GetFileName(tmdlFile), ex.Message));
            }
        }

        // If we successfully imported at least one table, continue
        // Show warnings for failed tables but don't fail the entire import
        if (failedTables.Count > 0 && tables.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\nWarning: {failedTables.Count} table(s) could not be imported:");
            foreach (var (fileName, error) in failedTables)
            {
                var shortError = error.Split('\n').FirstOrDefault() ?? error;
                Console.WriteLine($"  ! {fileName}: {shortError}");
            }
            Console.ResetColor();
            Console.WriteLine();
        }

        // If all tables failed, throw an error
        if (tables.Count == 0)
        {
            var errorDetails = string.Join("\n", failedTables.Select(f => $"  - {f.fileName}: {f.error}"));
            throw new InvalidOperationException(
                $"Failed to import any tables from {tmdlFiles.Length} TMDL file(s)\n\n" +
                $"Errors:\n{errorDetails}\n\n" +
                "Note: Some tables may reference shared expressions or other model-level objects.\n" +
                "If these tables are part of a complete model, try importing the entire model directory instead.");
        }

        return tables;
    }

    /// <summary>
    /// Extract a single table from a TMDL file
    /// </summary>
    private TableDefinition? ExtractTableFromFile(string tmdlFilePath, bool includeLineageTags)
    {
        // Read the table TMDL content
        var tableTmdlContent = File.ReadAllText(tmdlFilePath);

        // Extract table name from TMDL content
        var tableName = ExtractTableNameFromTmdl(tableTmdlContent);

        // Create a sanitized database name (remove quotes and spaces)
        var sanitizedDbName = tableName.Trim('\'', '"').Replace(" ", "_");

        // Create a temporary directory with minimal TMDL model structure
        var tempDir = Path.Combine(Path.GetTempPath(), $"pbt_import_{Guid.NewGuid()}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var definitionDir = Path.Combine(tempDir, "definition");
            var tablesDir = Path.Combine(definitionDir, "tables");
            Directory.CreateDirectory(tablesDir);

            // Create minimal database.tmdl with sanitized name
            var databaseTmdl = $@"database {sanitizedDbName}_temp
	compatibilityLevel: 1600

";
            File.WriteAllText(Path.Combine(tempDir, "database.tmdl"), databaseTmdl);

            // Extract referenced queryGroup names from table TMDL
            var queryGroups = ExtractQueryGroupsFromTmdl(tableTmdlContent);

            // Create minimal model.tmdl with referenced query groups
            var modelTmdl = "model Model\n\tculture: en-US\n\n";

            // Add query groups if any are referenced
            foreach (var qg in queryGroups)
            {
                modelTmdl += $"\tqueryGroup '{qg}'\n\n";
            }

            File.WriteAllText(Path.Combine(definitionDir, "model.tmdl"), modelTmdl);

            // Copy the table TMDL file
            File.WriteAllText(Path.Combine(tablesDir, Path.GetFileName(tmdlFilePath)), tableTmdlContent);

            // Deserialize the minimal model
            Database database;
            try
            {
                database = TmdlSerializer.DeserializeDatabaseFromFolder(tempDir);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to deserialize table from {Path.GetFileName(tmdlFilePath)}: {ex.Message}", ex);
            }

            if (database.Model == null || database.Model.Tables.Count == 0)
            {
                throw new InvalidOperationException($"No tables found in {Path.GetFileName(tmdlFilePath)}");
            }

            // Extract the table
            var table = database.Model.Tables[0];
            return ConvertToTableDefinition(table, includeLineageTags);
        }
        finally
        {
            // Clean up temp directory
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    /// <summary>
    /// Extract table name from TMDL content
    /// </summary>
    private string ExtractTableNameFromTmdl(string tmdlContent)
    {
        // Table TMDL starts with "table TableName"
        var lines = tmdlContent.Split('\n');
        var firstLine = lines.FirstOrDefault()?.Trim();

        if (firstLine != null && firstLine.StartsWith("table "))
        {
            return firstLine.Substring(6).Trim();
        }

        return "UnknownTable";
    }

    /// <summary>
    /// Extract queryGroup references from TMDL content
    /// </summary>
    private List<string> ExtractQueryGroupsFromTmdl(string tmdlContent)
    {
        var queryGroups = new HashSet<string>();
        var lines = tmdlContent.Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            // Look for "queryGroup: GroupName" pattern
            if (trimmed.StartsWith("queryGroup:"))
            {
                var groupName = trimmed.Substring("queryGroup:".Length).Trim();
                if (!string.IsNullOrEmpty(groupName))
                {
                    queryGroups.Add(groupName);
                }
            }
        }

        return queryGroups.ToList();
    }

    /// <summary>
    /// Convert a TOM Table to a TableDefinition YAML model
    /// </summary>
    private TableDefinition ConvertToTableDefinition(Table table, bool includeLineageTags)
    {
        var tableDef = new TableDefinition
        {
            Name = table.Name,
            Description = table.Description,
            IsHidden = table.IsHidden,
            LineageTag = includeLineageTags ? table.LineageTag : null,
            Columns = new List<ColumnDefinition>(),
            Hierarchies = new List<HierarchyDefinition>(),
            Measures = new List<MeasureDefinition>()
        };

        // Extract M expression from partition
        if (table.Partitions.Count > 0 && table.Partitions[0].Source is MPartitionSource mSource)
        {
            // Normalize tabs to spaces for YAML compatibility
            tableDef.MExpression = mSource.Expression?.Replace("\t", "  ");
        }

        // Extract data columns
        foreach (var column in table.Columns.OfType<DataColumn>())
        {
            var colDef = new ColumnDefinition
            {
                Name = column.Name,
                Type = column.DataType.ToString(),
                Description = column.Description,
                SourceColumn = column.SourceColumn,
                FormatString = column.FormatString,
                IsHidden = column.IsHidden,
                DisplayFolder = column.DisplayFolder,
                LineageTag = includeLineageTags ? column.LineageTag : null,
                SortByColumn = column.SortByColumn?.Name
            };

            tableDef.Columns.Add(colDef);
        }

        // Extract calculated columns
        foreach (var column in table.Columns.OfType<CalculatedColumn>())
        {
            var colDef = new ColumnDefinition
            {
                Name = column.Name,
                Type = column.DataType.ToString(),
                Description = column.Description,
                Expression = column.Expression,
                FormatString = column.FormatString,
                IsHidden = column.IsHidden,
                DisplayFolder = column.DisplayFolder,
                LineageTag = includeLineageTags ? column.LineageTag : null,
                SortByColumn = column.SortByColumn?.Name
            };

            tableDef.Columns.Add(colDef);
        }

        // Extract hierarchies
        foreach (var hierarchy in table.Hierarchies)
        {
            var hierarchyDef = new HierarchyDefinition
            {
                Name = hierarchy.Name,
                Description = hierarchy.Description,
                DisplayFolder = hierarchy.DisplayFolder,
                LineageTag = includeLineageTags ? hierarchy.LineageTag : null,
                Levels = new List<LevelDefinition>()
            };

            foreach (var level in hierarchy.Levels)
            {
                hierarchyDef.Levels.Add(new LevelDefinition
                {
                    Name = level.Name,
                    Column = level.Column.Name
                });
            }

            tableDef.Hierarchies.Add(hierarchyDef);
        }

        // Extract measures
        foreach (var measure in table.Measures)
        {
            var measureDef = new MeasureDefinition
            {
                Name = measure.Name,
                Table = table.Name,
                Expression = measure.Expression,
                Description = measure.Description,
                FormatString = measure.FormatString,
                DisplayFolder = measure.DisplayFolder,
                IsHidden = measure.IsHidden,
                LineageTag = includeLineageTags ? measure.LineageTag : null
            };

            tableDef.Measures.Add(measureDef);
        }

        return tableDef;
    }
}
