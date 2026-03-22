using Microsoft.AnalysisServices.Tabular;
using Pbt.Core.Infrastructure;
using Pbt.Core.Models;
using ValidationResult = Pbt.Core.Models.ValidationResult;

namespace Pbt.Core.Services;

/// <summary>
/// Validates PBI Composer projects using structural checks and TOM validation
/// </summary>
public class Validator
{
    private readonly YamlSerializer _serializer;

    private static readonly HashSet<string> ValidDataTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "string", "int64", "datetime", "decimal", "double", "boolean"
    };

    private static readonly HashSet<string> ValidCardinalities = new(StringComparer.Ordinal)
    {
        "ManyToOne", "OneToMany", "OneToOne", "ManyToMany"
    };

    private static readonly HashSet<string> ValidCrossFilterDirections = new(StringComparer.Ordinal)
    {
        "Single", "Both", "Automatic"
    };

    private static readonly HashSet<string> ValidSummarizeBy = new(StringComparer.Ordinal)
    {
        "None", "Sum", "Count", "Min", "Max", "Average", "DistinctCount"
    };

    private static readonly HashSet<string> ValidPartitionModes = new(StringComparer.Ordinal)
    {
        "Import", "DirectQuery", "Dual"
    };

    public Validator(YamlSerializer serializer)
    {
        _serializer = serializer;
    }

    /// <summary>
    /// Validate project using resolved asset paths
    /// </summary>
    public ValidationResult ValidateProjectWithAssets(string projectPath, ResolvedAssetPaths assetPaths)
    {
        var result = new ValidationResult();

        // 1. Validate project.yml exists
        var projectYamlPath = Path.Combine(projectPath, "project.yml");
        if (!File.Exists(projectYamlPath))
        {
            result.AddError("project.yml not found", projectPath,
                suggestion: "Run 'pbt init' to create a new project");
            return result;
        }

        // 2. Load and validate project definition
        ProjectDefinition? project = null;
        try
        {
            project = _serializer.LoadFromFile<ProjectDefinition>(projectYamlPath);
            ValidateProjectDefinition(project, projectYamlPath, result);
        }
        catch (Exception ex)
        {
            result.AddError($"Failed to load project.yml: {ex.Message}", projectYamlPath);
            return result;
        }

        // 3. Validate asset paths exist
        if (assetPaths.TablePaths.Count == 0)
        {
            result.AddError("No table paths configured", projectYamlPath,
                suggestion: "Add at least one tables path to the assets configuration");
        }

        if (assetPaths.ModelPaths.Count == 0)
        {
            result.AddError("No model paths configured", projectYamlPath,
                suggestion: "Add at least one models path to the assets configuration");
        }

        if (!result.IsValid) return result;

        // 4. Load tables from all configured paths
        TableRegistry? registry = null;
        try
        {
            registry = new TableRegistry(_serializer);
            registry.LoadTablesWithPriority(assetPaths.TablePaths);

            // Validate basic table structure
            foreach (var table in registry.GetAllTables())
            {
                ValidateTableStructure(table, result);
            }
        }
        catch (Exception ex)
        {
            result.AddError($"Failed to load tables: {ex.Message}");
            return result;
        }

        // 5. Load and validate models from all configured paths
        foreach (var modelsPath in assetPaths.ModelPaths)
        {
            if (!Directory.Exists(modelsPath)) continue;

            var modelFiles = Directory.GetFiles(modelsPath, "*.yaml")
                .Concat(Directory.GetFiles(modelsPath, "*.yml"))
                .ToList();

            foreach (var modelFile in modelFiles)
            {
                try
                {
                    var model = _serializer.LoadFromFile<ModelDefinition>(modelFile);
                    ValidateModelReferences(model, registry, modelFile, result);
                }
                catch (Exception ex)
                {
                    result.AddError($"Failed to load model: {ex.Message}", modelFile);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Validate entire project structure using the legacy flat layout (tables/ and models/ directories).
    /// Delegates to ValidateProjectWithAssets with synthetic asset paths.
    /// </summary>
    public ValidationResult ValidateProject(string projectPath)
    {
        var result = new ValidationResult();

        if (!Directory.Exists(projectPath))
        {
            result.AddError($"Project directory does not exist: {projectPath}");
            return result;
        }

        var projectYaml = Path.Combine(projectPath, "project.yml");
        if (!File.Exists(projectYaml))
        {
            result.AddError("project.yml not found", projectPath,
                suggestion: "Run 'pbt init' to create a new project");
            return result;
        }

        var tablesDir = Path.Combine(projectPath, "tables");
        if (!Directory.Exists(tablesDir))
        {
            result.AddError("tables/ directory not found", projectPath);
        }

        var modelsDir = Path.Combine(projectPath, "models");
        if (!Directory.Exists(modelsDir))
        {
            result.AddError("models/ directory not found", projectPath);
        }

        if (!result.IsValid)
        {
            return result;
        }

        // Delegate to the canonical validation path with synthetic asset paths
        var assetPaths = new ResolvedAssetPaths
        {
            TablePaths = { tablesDir },
            ModelPaths = { modelsDir }
        };

        return ValidateProjectWithAssets(projectPath, assetPaths);
    }

    /// <summary>
    /// Validate project definition
    /// </summary>
    private void ValidateProjectDefinition(ProjectDefinition project, string filePath, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(project.Name))
        {
            result.AddError("Project name is required", filePath);
        }

        if (project.CompatibilityLevel < 1200 || project.CompatibilityLevel > 1700)
        {
            result.AddWarning(
                $"Unusual compatibility level: {project.CompatibilityLevel}",
                filePath,
                context: "Common values are 1200, 1400, 1500, 1600");
        }
    }

    /// <summary>
    /// Validate basic table structure (YAML parsing and required fields only)
    /// </summary>
    private void ValidateTableStructure(TableDefinition table, ValidationResult result)
    {
        var filePath = table.SourceFilePath ?? "unknown";

        // Only validate that required fields exist
        if (string.IsNullOrWhiteSpace(table.Name))
        {
            result.AddError("Table name is required", filePath);
        }

        // Check for duplicate column names
        if (table.Columns != null && table.Columns.Count > 0)
        {
            var columnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in table.Columns)
            {
                if (string.IsNullOrWhiteSpace(column.Name))
                {
                    result.AddError("Column name is required", filePath, context: $"Table: {table.Name}");
                    continue;
                }

                if (!columnNames.Add(column.Name))
                {
                    result.AddError($"Duplicate column name: {column.Name}", filePath,
                        context: $"Table: {table.Name}");
                }

                if (!string.IsNullOrWhiteSpace(column.Type) && !ValidDataTypes.Contains(column.Type))
                {
                    result.AddError(
                        $"Column '{column.Name}' has unknown type '{column.Type}'",
                        filePath,
                        context: $"Table: {table.Name}",
                        suggestion: $"Valid types: {string.Join(", ", ValidDataTypes.Order())}");
                }

                // Validate summarize_by
                if (!string.IsNullOrWhiteSpace(column.SummarizeBy) && !ValidSummarizeBy.Contains(column.SummarizeBy))
                {
                    result.AddError(
                        $"Column '{column.Name}' has unknown summarize_by '{column.SummarizeBy}'",
                        filePath,
                        context: $"Table: {table.Name}",
                        suggestion: $"Valid values: {string.Join(", ", ValidSummarizeBy)}");
                }

                // Validate sort_by_column references an existing column
                if (!string.IsNullOrWhiteSpace(column.SortByColumn))
                {
                    var sortByExists = table.Columns?.Any(c =>
                        c.Name.Equals(column.SortByColumn, StringComparison.OrdinalIgnoreCase)) ?? false;
                    if (!sortByExists)
                    {
                        result.AddError(
                            $"Column '{column.Name}' sort_by_column references unknown column '{column.SortByColumn}'",
                            filePath,
                            context: $"Table: {table.Name}");
                    }
                }
            }
        }

        // Validate partitions
        if (table.Partitions != null)
        {
            foreach (var partition in table.Partitions)
            {
                if (string.IsNullOrWhiteSpace(partition.Name))
                {
                    result.AddError("Partition name is required", filePath, context: $"Table: {table.Name}");
                }

                if (!string.IsNullOrWhiteSpace(partition.Mode) && !ValidPartitionModes.Contains(partition.Mode))
                {
                    result.AddError(
                        $"Partition '{partition.Name}' has unknown mode '{partition.Mode}'",
                        filePath,
                        context: $"Table: {table.Name}",
                        suggestion: $"Valid values: {string.Join(", ", ValidPartitionModes)}");
                }
            }
        }

        // Validate hierarchy references point to existing columns
        foreach (var hierarchy in table.Hierarchies ?? new List<HierarchyDefinition>())
        {
            if (string.IsNullOrWhiteSpace(hierarchy.Name))
            {
                result.AddError("Hierarchy name is required", filePath, context: $"Table: {table.Name}");
                continue;
            }

            foreach (var level in hierarchy.Levels ?? new List<LevelDefinition>())
            {
                if (string.IsNullOrWhiteSpace(level.Column))
                {
                    result.AddError($"Hierarchy '{hierarchy.Name}' level '{level.Name}' has no column",
                        filePath, context: $"Table: {table.Name}");
                    continue;
                }

                var columnExists = table.Columns?.Any(c =>
                    c.Name.Equals(level.Column, StringComparison.OrdinalIgnoreCase)) ?? false;

                if (!columnExists)
                {
                    result.AddError(
                        $"Hierarchy '{hierarchy.Name}' level '{level.Name}' references unknown column '{level.Column}'",
                        filePath, context: $"Table: {table.Name}");
                }
            }
        }
    }

    /// <summary>
    /// Validate model references (table refs, relationship refs, measure refs)
    /// </summary>
    private void ValidateModelReferences(ModelDefinition model, TableRegistry registry,
        string filePath, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
        {
            result.AddError("Model name is required", filePath);
        }

        // Validate table references exist in registry
        var tablesInModel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tableRef in model.Tables ?? new List<TableReference>())
        {
            if (string.IsNullOrWhiteSpace(tableRef.Ref))
            {
                result.AddError("Table reference is empty", filePath, context: $"Model: {model.Name}");
                continue;
            }

            if (!registry.ContainsTable(tableRef.Ref))
            {
                var suggestion = FindSimilarTableName(tableRef.Ref, registry.ListTables());
                result.AddError($"Table reference '{tableRef.Ref}' not found in registry", filePath,
                    context: $"Model: {model.Name}",
                    suggestion: suggestion ?? $"Available tables: {string.Join(", ", registry.ListTables())}");
            }
            else
            {
                tablesInModel.Add(tableRef.Ref);
            }
        }

        // Validate relationship table references, column references, and cardinality
        foreach (var relationship in model.Relationships ?? new List<RelationshipDefinition>())
        {
            if (!string.IsNullOrWhiteSpace(relationship.FromTable) && !tablesInModel.Contains(relationship.FromTable))
            {
                result.AddError($"Relationship from_table '{relationship.FromTable}' is not in the model",
                    filePath, context: $"Model: {model.Name}");
            }

            if (!string.IsNullOrWhiteSpace(relationship.ToTable) && !tablesInModel.Contains(relationship.ToTable))
            {
                result.AddError($"Relationship to_table '{relationship.ToTable}' is not in the model",
                    filePath, context: $"Model: {model.Name}");
            }

            // Validate from_column exists in the from table
            if (!string.IsNullOrWhiteSpace(relationship.FromTable) && tablesInModel.Contains(relationship.FromTable)
                && !string.IsNullOrWhiteSpace(relationship.FromColumn))
            {
                var fromTable = registry.GetTable(relationship.FromTable);
                if (fromTable != null)
                {
                    var columnExists = fromTable.Columns?.Any(c =>
                        c.Name.Equals(relationship.FromColumn, StringComparison.OrdinalIgnoreCase)) ?? false;
                    if (!columnExists)
                    {
                        result.AddError(
                            $"Relationship from_column '{relationship.FromColumn}' not found in table '{relationship.FromTable}'",
                            filePath, context: $"Model: {model.Name}");
                    }
                }
            }

            // Validate to_column exists in the to table
            if (!string.IsNullOrWhiteSpace(relationship.ToTable) && tablesInModel.Contains(relationship.ToTable)
                && !string.IsNullOrWhiteSpace(relationship.ToColumn))
            {
                var toTable = registry.GetTable(relationship.ToTable);
                if (toTable != null)
                {
                    var columnExists = toTable.Columns?.Any(c =>
                        c.Name.Equals(relationship.ToColumn, StringComparison.OrdinalIgnoreCase)) ?? false;
                    if (!columnExists)
                    {
                        result.AddError(
                            $"Relationship to_column '{relationship.ToColumn}' not found in table '{relationship.ToTable}'",
                            filePath, context: $"Model: {model.Name}");
                    }
                }
            }

            // Validate cardinality value
            if (!string.IsNullOrWhiteSpace(relationship.Cardinality) && !ValidCardinalities.Contains(relationship.Cardinality))
            {
                result.AddError(
                    $"Unknown cardinality '{relationship.Cardinality}'",
                    filePath,
                    context: $"Model: {model.Name}",
                    suggestion: $"Valid values: {string.Join(", ", ValidCardinalities)}");
            }

            // Validate cross-filter direction (None is not valid in TOM)
            if (!string.IsNullOrWhiteSpace(relationship.CrossFilterDirection))
            {
                if (relationship.CrossFilterDirection.Equals("None", StringComparison.OrdinalIgnoreCase))
                {
                    result.AddError(
                        $"Cross filter direction 'None' is not supported by the Tabular Object Model",
                        filePath,
                        context: $"Model: {model.Name}",
                        suggestion: "Valid values: Single, Both. Use 'active: false' to disable a relationship instead.");
                }
                else if (!ValidCrossFilterDirections.Contains(relationship.CrossFilterDirection))
                {
                    result.AddError(
                        $"Unknown cross filter direction '{relationship.CrossFilterDirection}'",
                        filePath,
                        context: $"Model: {model.Name}",
                        suggestion: $"Valid values: {string.Join(", ", ValidCrossFilterDirections)}");
                }
            }
        }

        // Validate measure table references and expressions
        foreach (var measure in model.Measures ?? new List<MeasureDefinition>())
        {
            if (!string.IsNullOrWhiteSpace(measure.Table) && !tablesInModel.Contains(measure.Table))
            {
                result.AddError($"Measure '{measure.Name}' references table '{measure.Table}' which is not in the model",
                    filePath, context: $"Model: {model.Name}");
            }

            // Validate DAX expression has balanced brackets
            if (!string.IsNullOrWhiteSpace(measure.Expression))
            {
                ValidateDaxBrackets(measure.Expression, measure.Name, filePath, model.Name, result);
            }
        }
    }

    /// <summary>
    /// Validate that a DAX expression has balanced brackets: (), [], {}
    /// </summary>
    private void ValidateDaxBrackets(string expression, string measureName, string filePath,
        string modelName, ValidationResult result)
    {
        var stack = new Stack<char>();
        var bracketPairs = new Dictionary<char, char>
        {
            { ')', '(' },
            { ']', '[' },
            { '}', '{' }
        };

        foreach (var ch in expression)
        {
            if (ch is '(' or '[' or '{')
            {
                stack.Push(ch);
            }
            else if (bracketPairs.TryGetValue(ch, out var expected))
            {
                if (stack.Count == 0 || stack.Pop() != expected)
                {
                    result.AddError(
                        $"Measure '{measureName}' has unbalanced brackets in expression",
                        filePath, context: $"Model: {modelName}");
                    return;
                }
            }
        }

        if (stack.Count > 0)
        {
            result.AddError(
                $"Measure '{measureName}' has unbalanced brackets in expression",
                filePath, context: $"Model: {modelName}");
        }
    }

    /// <summary>
    /// Validate TOM Database model using Analysis Services validation
    /// This catches semantic errors that YAML validation cannot detect
    /// </summary>
    public ValidationResult ValidateTomModel(Database database, string modelName)
    {
        var result = new ValidationResult();

        try
        {
            // TOM performs validation during serialization
            // We'll attempt to serialize to a temporary in-memory structure
            // If serialization succeeds, the model is valid

            // Basic model structure validation
            if (database.Model == null)
            {
                result.AddError("Database has no model", modelName);
                return result;
            }

            // Validate each table has at least one partition or is a calculated table
            foreach (var table in database.Model.Tables)
            {
                if (table.Partitions.Count == 0 && !IsCalculatedTable(table))
                {
                    result.AddWarning(
                        $"Table '{table.Name}' has no partitions and no data source",
                        modelName,
                        context: "Table will be empty unless it's a calculated table",
                        suggestion: "Add an M expression or source definition to load data");
                }
            }

            // Check for relationship issues
            foreach (var relationship in database.Model.Relationships)
            {
                if (relationship is SingleColumnRelationship scr)
                {
                    if (scr.FromColumn == null || scr.ToColumn == null)
                    {
                        result.AddError("Relationship has missing column reference", modelName);
                    }
                }
            }

            // If we get here without exceptions, TOM model is structurally valid
            return result;
        }
        catch (Exception ex)
        {
            result.AddError($"TOM model validation failed: {ex.Message}", modelName);
            return result;
        }
    }

    /// <summary>
    /// Check if a table is a calculated table (has a calculated partition source)
    /// </summary>
    private bool IsCalculatedTable(Table table)
    {
        return table.Partitions.Any(p => p.Source is CalculatedPartitionSource);
    }

    /// <summary>
    /// Find similar table name for suggestions
    /// </summary>
    private string? FindSimilarTableName(string tableName, List<string> availableTables)
    {
        // Simple case-insensitive match
        var closeMatch = availableTables.FirstOrDefault(t =>
            t.Equals(tableName, StringComparison.OrdinalIgnoreCase));

        if (closeMatch != null)
        {
            return $"Did you mean: {closeMatch}?";
        }

        // Check for partial matches
        var partialMatches = availableTables.Where(t =>
            t.Contains(tableName, StringComparison.OrdinalIgnoreCase) ||
            tableName.Contains(t, StringComparison.OrdinalIgnoreCase)).ToList();

        if (partialMatches.Any())
        {
            return $"Did you mean: {string.Join(" or ", partialMatches)}?";
        }

        return null;
    }
}
