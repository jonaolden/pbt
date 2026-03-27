using System.Text.RegularExpressions;
using Microsoft.AnalysisServices.Tabular;
using Pbt.Core.Models;

namespace Pbt.Core.Services;

/// <summary>
/// Composes TOM Database objects from model definitions
/// </summary>
public sealed class ModelComposer
{
    private static readonly Regex EnvVarPattern = new(@"\$\{(\w+)\}", RegexOptions.Compiled);
    private readonly TableRegistry _tableRegistry;
    private LineageManifestService? _lineageService;
    private ModelDefinition? _modelDef;
    private Dictionary<string, ConnectorConfig> _connectors = new();

    public ModelComposer(TableRegistry tableRegistry)
    {
        _tableRegistry = tableRegistry;
    }

    /// <summary>
    /// Register a connector configuration for shared expression generation
    /// </summary>
    public void RegisterConnector(ConnectorConfig connector)
    {
        if (!_connectors.ContainsKey(connector.Name))
        {
            _connectors[connector.Name] = connector;
        }
    }

    /// <summary>
    /// Compose a TOM Database from a model definition
    /// </summary>
    /// <param name="modelDef">Model definition</param>
    /// <param name="compatibilityLevel">Power BI compatibility level</param>
    /// <param name="lineageService">Optional lineage manifest service for tag management</param>
    /// <param name="project">Optional project definition for format strings</param>
    /// <returns>TOM Database object</returns>
    /// <summary>
    /// Optional project root path for resolving external M expression files
    /// </summary>
    private string? _projectRootPath;

    /// <summary>
    /// Environment overrides for shared expressions
    /// </summary>
    private EnvironmentDefinition? _environment;

    public Database ComposeModel(ModelDefinition modelDef, LineageManifestService? lineageService = null, string? projectRootPath = null, EnvironmentDefinition? environment = null)
    {
        _lineageService = lineageService;
        _modelDef = modelDef;
        _projectRootPath = projectRootPath;
        _environment = environment;

        var database = new Database
        {
            Name = modelDef.Name,
            CompatibilityLevel = modelDef.CompatibilityLevel,
            Model = new Model
            {
                Name = modelDef.Name
            }
        };

        var model = database.Model;
        model.Culture = modelDef.Culture;
        model.DiscourageImplicitMeasures = modelDef.DiscourageImplicitMeasures;
        model.DefaultPowerBIDataSourceVersion = Microsoft.AnalysisServices.Tabular.PowerBIDataSourceVersion.PowerBI_V3;
        model.SourceQueryCulture = modelDef.SourceQueryCulture;

        // Data access options
        model.DataAccessOptions.LegacyRedirects = true;
        model.DataAccessOptions.ReturnErrorValuesAsNull = true;

        // Disable auto time intelligence by default
        model.Annotations.Add(new Annotation
        {
            Name = "__PBI_TimeIntelligenceEnabled",
            Value = modelDef.AutoTimeIntelligence ? "1" : "0"
        });

        // Enable dev mode tooling
        model.Annotations.Add(new Annotation
        {
            Name = "PBI_ProTooling",
            Value = "[\"DevMode\"]"
        });

        // 1. Add tables
        foreach (var tableRef in modelDef.Tables)
        {
            var tableDef = _tableRegistry.GetTable(tableRef.Ref);
            var table = BuildTable(tableDef);
            model.Tables.Add(table);
        }

        // 2. Add relationships
        var relationshipCounter = new Dictionary<string, int>();
        foreach (var relDef in modelDef.Relationships)
        {
            ValidateRelationship(relDef, model);
            var relationship = BuildRelationship(relDef, model, relationshipCounter);
            model.Relationships.Add(relationship);
        }

        // 3. Add model-level measures (override table-level measures with same name)
        foreach (var measureDef in modelDef.Measures)
        {
            var table = model.Tables.Find(measureDef.Table);
            if (table == null)
            {
                throw new InvalidOperationException(
                    $"Measure '{measureDef.Name}' references table '{measureDef.Table}' which is not in the model");
            }

            // Remove existing table-level measure with same name (model-level wins)
            var existing = table.Measures.Find(measureDef.Name);
            if (existing != null)
            {
                table.Measures.Remove(existing);
            }

            var measure = BuildMeasure(measureDef, measureDef.Table);
            table.Measures.Add(measure);
        }

        // 4. Add shared connector expressions
        foreach (var connector in _connectors.Values)
        {
            var expression = BuildConnectorExpression(connector);
            model.Expressions.Add(expression);
        }

        // 5. Add shared expressions / Power Query parameters (from model and project)
        AddSharedExpressions(modelDef, model);

        // 5b. Add RangeStart/RangeEnd expressions for tables with incremental refresh
        AddIncrementalRefreshExpressions(model);

        // 6. Add calculation groups
        if (modelDef.CalculationGroups != null)
        {
            foreach (var calcGroupDef in modelDef.CalculationGroups)
            {
                var calcGroupTable = BuildCalculationGroupTable(calcGroupDef);
                model.Tables.Add(calcGroupTable);
            }
        }

        // 7. Add field parameters
        if (modelDef.FieldParameters != null)
        {
            foreach (var fieldParamDef in modelDef.FieldParameters)
            {
                var fieldParamTable = BuildFieldParameterTable(fieldParamDef);
                model.Tables.Add(fieldParamTable);
            }
        }

        // 8. Add perspectives
        if (modelDef.Perspectives != null)
        {
            foreach (var perspectiveDef in modelDef.Perspectives)
            {
                var perspective = BuildPerspective(perspectiveDef, model);
                model.Perspectives.Add(perspective);
            }
        }

        // 9. Add roles with RLS
        if (modelDef.Roles != null)
        {
            foreach (var roleDef in modelDef.Roles)
            {
                var role = BuildRole(roleDef, model);
                model.Roles.Add(role);
            }
        }

        return database;
    }

    /// <summary>
    /// Build a TOM Table from a table definition
    /// </summary>
    private Table BuildTable(TableDefinition tableDef)
    {
        var table = new Table
        {
            Name = tableDef.Name,
            Description = tableDef.Description,
            IsHidden = tableDef.IsHidden
        };

        // Add partitions - multiple partition support for incremental refresh
        if (tableDef.Partitions != null && tableDef.Partitions.Count > 0)
        {
            // Explicit partitions list takes priority
            foreach (var partDef in tableDef.Partitions)
            {
                var mExpr = ResolveMExpression(partDef.MExpression, partDef.MExpressionFile, tableDef.SourceFilePath);
                if (!string.IsNullOrWhiteSpace(mExpr))
                {
                    var partition = new Partition
                    {
                        Name = partDef.Name,
                        Source = new MPartitionSource { Expression = mExpr }
                    };

                    if (!string.IsNullOrWhiteSpace(partDef.Mode))
                    {
                        partition.Mode = ParsePartitionMode(partDef.Mode);
                    }

                    table.Partitions.Add(partition);
                }
            }
        }
        else
        {
            // Single partition from MExpression, MExpressionFile, or Source (backward compat)
            var mExpression = ResolveMExpression(tableDef.MExpression, tableDef.MExpressionFile, tableDef.SourceFilePath);

            if (string.IsNullOrWhiteSpace(mExpression) && tableDef.Source != null)
            {
                mExpression = GenerateMExpressionFromSource(tableDef.Source, tableDef);
            }

            if (!string.IsNullOrWhiteSpace(mExpression))
            {
                var partition = new Partition
                {
                    Name = tableDef.Name,
                    Source = new MPartitionSource { Expression = mExpression }
                };
                table.Partitions.Add(partition);
            }
        }

        // Add columns
        foreach (var colDef in tableDef.Columns)
        {
            var column = BuildColumn(colDef, tableDef.Name);
            table.Columns.Add(column);
        }

        // Set SortByColumn references after all columns are added
        foreach (var colDef in tableDef.Columns)
        {
            if (!string.IsNullOrWhiteSpace(colDef.SortByColumn))
            {
                var column = table.Columns.Find(colDef.Name);
                var sortByColumn = table.Columns.Find(colDef.SortByColumn);
                if (column != null && sortByColumn != null)
                {
                    column.SortByColumn = sortByColumn;
                }
            }
        }

        // Add hierarchies
        foreach (var hierarchyDef in tableDef.Hierarchies)
        {
            var hierarchy = BuildHierarchy(hierarchyDef, table);
            table.Hierarchies.Add(hierarchy);
        }

        // Add table-level measures
        foreach (var measureDef in tableDef.Measures)
        {
            var measure = BuildMeasure(measureDef, tableDef.Name);
            table.Measures.Add(measure);
        }

        // Add table-level annotations
        if (tableDef.Annotations != null)
        {
            foreach (var (key, value) in tableDef.Annotations)
            {
                table.Annotations.Add(new Annotation { Name = key, Value = value });
            }
        }

        // Configure incremental refresh policy
        if (tableDef.IncrementalRefresh != null)
        {
            ApplyIncrementalRefreshPolicy(table, tableDef.IncrementalRefresh);
        }

        // Generate lineage tag
        if (string.IsNullOrWhiteSpace(tableDef.LineageTag))
        {
            table.LineageTag = GenerateLineageTag(tableDef.Name, tableDef.Name, "Table");
        }
        else
        {
            table.LineageTag = tableDef.LineageTag;
        }

        return table;
    }

    /// <summary>
    /// Resolve M expression from inline string or external file
    /// </summary>
    private string? ResolveMExpression(string? inlineExpression, string? expressionFile, string? sourceFilePath)
    {
        if (!string.IsNullOrWhiteSpace(inlineExpression))
        {
            return inlineExpression;
        }

        if (!string.IsNullOrWhiteSpace(expressionFile))
        {
            var filePath = expressionFile;
            // Resolve relative paths against the table definition file or project root
            if (!Path.IsPathRooted(filePath))
            {
                if (!string.IsNullOrWhiteSpace(sourceFilePath))
                {
                    var dir = Path.GetDirectoryName(sourceFilePath);
                    if (dir != null)
                    {
                        filePath = Path.Combine(dir, filePath);
                    }
                }
                else if (!string.IsNullOrWhiteSpace(_projectRootPath))
                {
                    filePath = Path.Combine(_projectRootPath, filePath);
                }
            }

            if (File.Exists(filePath))
            {
                return File.ReadAllText(filePath);
            }

            throw new FileNotFoundException($"M expression file not found: {filePath}");
        }

        return null;
    }

    /// <summary>
    /// Parse partition mode string to TOM ModeType
    /// </summary>
    private ModeType ParsePartitionMode(string mode)
    {
        return mode switch
        {
            "Import" => ModeType.Import,
            "DirectQuery" => ModeType.DirectQuery,
            "Dual" => ModeType.Dual,
            _ => throw new ArgumentException($"Unknown partition mode: {mode}. Valid values: Import, DirectQuery, Dual")
        };
    }

    /// <summary>
    /// Build a TOM Column from a column definition
    /// Returns either a DataColumn or CalculatedColumn based on whether Expression is set
    /// </summary>
    private Column BuildColumn(ColumnDefinition colDef, string tableName)
    {
        // If Expression is set, create a calculated column
        if (!string.IsNullOrWhiteSpace(colDef.Expression))
        {
            return BuildCalculatedColumn(colDef, tableName);
        }

        // Otherwise create a data column
        return BuildDataColumn(colDef, tableName);
    }

    /// <summary>
    /// Build a TOM DataColumn from a column definition
    /// </summary>
    private DataColumn BuildDataColumn(ColumnDefinition colDef, string tableName)
    {
        var column = new DataColumn
        {
            Name = colDef.Name,
            DataType = ParseDataType(colDef.Type),
            SourceColumn = colDef.SourceColumn ?? colDef.Name,
            Description = colDef.Description,
            IsHidden = colDef.IsHidden ?? false
        };

        // Set display folder if specified
        if (!string.IsNullOrWhiteSpace(colDef.DisplayFolder))
        {
            column.DisplayFolder = colDef.DisplayFolder;
        }

        // Apply format string from column definition or model-level format_strings config
        var formatString = colDef.FormatString;
        if (string.IsNullOrWhiteSpace(formatString) && _modelDef != null)
        {
            _modelDef.FormatStrings.TryGetValue(colDef.Type, out formatString);
        }

        if (!string.IsNullOrWhiteSpace(formatString))
        {
            column.FormatString = formatString;
        }

        // Set data category
        if (!string.IsNullOrWhiteSpace(colDef.DataCategory))
        {
            column.DataCategory = colDef.DataCategory;
        }

        // Set summarize by
        if (!string.IsNullOrWhiteSpace(colDef.SummarizeBy))
        {
            column.SummarizeBy = ParseAggregateFunction(colDef.SummarizeBy);
        }

        // Set is key
        if (colDef.IsKey == true)
        {
            column.IsKey = true;
        }

        // Add column annotations
        if (colDef.Annotations != null)
        {
            foreach (var (key, value) in colDef.Annotations)
            {
                column.Annotations.Add(new Annotation { Name = key, Value = value });
            }
        }

        // Generate lineage tag
        if (string.IsNullOrWhiteSpace(colDef.LineageTag))
        {
            column.LineageTag = GenerateLineageTag(tableName, colDef.Name, "Column");
        }
        else
        {
            column.LineageTag = colDef.LineageTag;
        }

        return column;
    }

    /// <summary>
    /// Build a TOM CalculatedColumn from a column definition with an expression
    /// </summary>
    private CalculatedColumn BuildCalculatedColumn(ColumnDefinition colDef, string tableName)
    {
        var column = new CalculatedColumn
        {
            Name = colDef.Name,
            DataType = ParseDataType(colDef.Type),
            Expression = colDef.Expression!,
            Description = colDef.Description,
            IsHidden = colDef.IsHidden ?? false
        };

        if (!string.IsNullOrWhiteSpace(colDef.DisplayFolder))
        {
            column.DisplayFolder = colDef.DisplayFolder;
        }

        var formatString = colDef.FormatString;
        if (string.IsNullOrWhiteSpace(formatString) && _modelDef != null)
        {
            _modelDef.FormatStrings.TryGetValue(colDef.Type, out formatString);
        }

        if (!string.IsNullOrWhiteSpace(formatString))
        {
            column.FormatString = formatString;
        }

        if (!string.IsNullOrWhiteSpace(colDef.DataCategory))
        {
            column.DataCategory = colDef.DataCategory;
        }

        if (!string.IsNullOrWhiteSpace(colDef.SummarizeBy))
        {
            column.SummarizeBy = ParseAggregateFunction(colDef.SummarizeBy);
        }

        if (colDef.IsKey == true)
        {
            column.IsKey = true;
        }

        if (colDef.Annotations != null)
        {
            foreach (var (key, value) in colDef.Annotations)
            {
                column.Annotations.Add(new Annotation { Name = key, Value = value });
            }
        }

        if (string.IsNullOrWhiteSpace(colDef.LineageTag))
        {
            column.LineageTag = GenerateLineageTag(tableName, colDef.Name, "Column");
        }
        else
        {
            column.LineageTag = colDef.LineageTag;
        }

        return column;
    }

    /// <summary>
    /// Build a TOM Hierarchy from a hierarchy definition
    /// </summary>
    private Hierarchy BuildHierarchy(HierarchyDefinition hierarchyDef, Table table)
    {
        var hierarchy = new Hierarchy
        {
            Name = hierarchyDef.Name,
            Description = hierarchyDef.Description
        };

        // Set display folder if specified
        if (!string.IsNullOrWhiteSpace(hierarchyDef.DisplayFolder))
        {
            hierarchy.DisplayFolder = hierarchyDef.DisplayFolder;
        }

        foreach (var levelDef in hierarchyDef.Levels)
        {
            var column = table.Columns.Find(levelDef.Column);
            if (column == null)
            {
                throw new InvalidOperationException(
                    $"Hierarchy '{hierarchyDef.Name}' level '{levelDef.Name}' references column '{levelDef.Column}' which does not exist in table '{table.Name}'");
            }

            var level = new Level
            {
                Name = levelDef.Name,
                Column = column,
                Ordinal = hierarchy.Levels.Count
            };
            hierarchy.Levels.Add(level);
        }

        // Generate lineage tag
        if (string.IsNullOrWhiteSpace(hierarchyDef.LineageTag))
        {
            hierarchy.LineageTag = GenerateLineageTag(table.Name, hierarchyDef.Name, "Hierarchy");
        }
        else
        {
            hierarchy.LineageTag = hierarchyDef.LineageTag;
        }

        return hierarchy;
    }

    /// <summary>
    /// Build a TOM Relationship from a relationship definition
    /// </summary>
    private SingleColumnRelationship BuildRelationship(RelationshipDefinition relDef, Model model, Dictionary<string, int> relationshipCounter)
    {
        var fromTable = model.Tables.Find(relDef.FromTable);
        var toTable = model.Tables.Find(relDef.ToTable);
        var fromColumn = fromTable!.Columns.Find(relDef.FromColumn);
        var toColumn = toTable!.Columns.Find(relDef.ToColumn);

        // Generate UUID for relationship name (deterministic if lineage service is available)
        var relationshipId = GenerateRelationshipId(relDef, relationshipCounter);

        var relationship = new SingleColumnRelationship
        {
            Name = relationshipId,
            FromColumn = fromColumn,
            ToColumn = toColumn,
            IsActive = relDef.Active
        };

        // Parse cardinality
        ParseCardinality(relDef.Cardinality, out var fromCardinality, out var toCardinality);
        relationship.FromCardinality = fromCardinality;
        relationship.ToCardinality = toCardinality;

        // Parse cross filter direction
        if (!string.IsNullOrWhiteSpace(relDef.CrossFilterDirection))
        {
            relationship.CrossFilteringBehavior = ParseCrossFilterDirection(relDef.CrossFilterDirection);
        }

        // Set referential integrity for DirectQuery performance
        if (relDef.RelyOnReferentialIntegrity)
        {
            relationship.RelyOnReferentialIntegrity = true;
        }

        return relationship;
    }

    /// <summary>
    /// Generate a deterministic UUID for a relationship
    /// </summary>
    private string GenerateRelationshipId(RelationshipDefinition relDef, Dictionary<string, int> relationshipCounter)
    {
        // Create base key for the relationship
        var baseKey = $"{relDef.FromTable}.{relDef.FromColumn}->{relDef.ToTable}.{relDef.ToColumn}";

        // Track occurrences of this relationship pattern
        if (!relationshipCounter.TryGetValue(baseKey, out var count))
        {
            relationshipCounter[baseKey] = 0;
        }
        else
        {
            relationshipCounter[baseKey] = ++count;
        }

        // Include count in key for uniqueness
        var relationshipKey = count > 0 ? $"{baseKey}#{count}" : baseKey;

        if (_lineageService != null)
        {
            return _lineageService.GetOrGenerateRelationshipTag(relationshipKey);
        }

        // Fallback to random GUID if no lineage service
        return Guid.NewGuid().ToString();
    }

    /// <summary>
    /// Build a TOM Measure from a measure definition
    /// </summary>
    private Measure BuildMeasure(MeasureDefinition measureDef, string tableName)
    {
        var measure = new Measure
        {
            Name = measureDef.Name,
            Expression = measureDef.Expression,
            Description = measureDef.Description,
            IsHidden = measureDef.IsHidden ?? false
        };

        if (!string.IsNullOrWhiteSpace(measureDef.FormatString))
        {
            measure.FormatString = measureDef.FormatString;
        }

        if (!string.IsNullOrWhiteSpace(measureDef.DisplayFolder))
        {
            measure.DisplayFolder = measureDef.DisplayFolder;
        }

        // Generate lineage tag
        if (string.IsNullOrWhiteSpace(measureDef.LineageTag))
        {
            measure.LineageTag = GenerateLineageTag(tableName, measureDef.Name, "Measure");
        }
        else
        {
            measure.LineageTag = measureDef.LineageTag;
        }

        return measure;
    }

    /// <summary>
    /// Validate that a relationship is valid
    /// </summary>
    private void ValidateRelationship(RelationshipDefinition relDef, Model model)
    {
        // Ensure both tables are in the model
        var fromTable = model.Tables.Find(relDef.FromTable);
        if (fromTable == null)
        {
            throw new InvalidOperationException(
                $"Relationship from table '{relDef.FromTable}' not found in model");
        }

        var toTable = model.Tables.Find(relDef.ToTable);
        if (toTable == null)
        {
            throw new InvalidOperationException(
                $"Relationship to table '{relDef.ToTable}' not found in model");
        }

        // Ensure columns exist
        var fromColumn = fromTable.Columns.Find(relDef.FromColumn);
        if (fromColumn == null)
        {
            throw new InvalidOperationException(
                $"Column '{relDef.FromColumn}' not found in table '{relDef.FromTable}'");
        }

        var toColumn = toTable.Columns.Find(relDef.ToColumn);
        if (toColumn == null)
        {
            throw new InvalidOperationException(
                $"Column '{relDef.ToColumn}' not found in table '{relDef.ToTable}'");
        }
    }

    /// <summary>
    /// Parse data type string to TOM DataType
    /// </summary>
    private DataType ParseDataType(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "string" => DataType.String,
            "int64" => DataType.Int64,
            "datetime" => DataType.DateTime,
            "decimal" => DataType.Decimal,
            "double" => DataType.Double,
            "boolean" => DataType.Boolean,
            _ => throw new ArgumentException($"Unknown data type: {type}")
        };
    }

    /// <summary>
    /// Parse cardinality string to TOM enums
    /// </summary>
    private void ParseCardinality(string cardinality, out RelationshipEndCardinality from, out RelationshipEndCardinality to)
    {
        switch (cardinality)
        {
            case "ManyToOne":
                from = RelationshipEndCardinality.Many;
                to = RelationshipEndCardinality.One;
                break;
            case "OneToMany":
                from = RelationshipEndCardinality.One;
                to = RelationshipEndCardinality.Many;
                break;
            case "OneToOne":
                from = RelationshipEndCardinality.One;
                to = RelationshipEndCardinality.One;
                break;
            case "ManyToMany":
                from = RelationshipEndCardinality.Many;
                to = RelationshipEndCardinality.Many;
                break;
            default:
                throw new ArgumentException($"Unknown cardinality: {cardinality}");
        }
    }

    /// <summary>
    /// Parse cross filter direction string to TOM enum
    /// </summary>
    private CrossFilteringBehavior ParseCrossFilterDirection(string direction)
    {
        return direction switch
        {
            "Single" => CrossFilteringBehavior.OneDirection,
            "Both" => CrossFilteringBehavior.BothDirections,
            "Automatic" => CrossFilteringBehavior.Automatic,
            _ => throw new ArgumentException($"Unknown cross filter direction: {direction}. Valid values: Single, Both, Automatic")
        };
    }

    /// <summary>
    /// Generate a lineage tag using the manifest service if available,
    /// otherwise generate a random GUID
    /// </summary>
    private string GenerateLineageTag(string tableName, string objectName, string objectType)
    {
        if (_lineageService != null)
        {
            return objectType switch
            {
                "Table" => _lineageService.GetOrGenerateTableTag(tableName),
                "Column" => _lineageService.GetOrGenerateColumnTag(tableName, objectName),
                "Measure" => _lineageService.GetOrGenerateMeasureTag(tableName, objectName),
                "Hierarchy" => _lineageService.GetOrGenerateHierarchyTag(tableName, objectName),
                _ => Guid.NewGuid().ToString()
            };
        }

        // Fallback to random GUID if no lineage service
        return Guid.NewGuid().ToString();
    }

    /// <summary>
    /// Add RangeStart/RangeEnd named expressions if any table uses incremental refresh.
    /// These are Power Query parameters that Power BI uses for partition boundaries.
    /// </summary>
    private void AddIncrementalRefreshExpressions(Model model)
    {
        var hasIncrementalRefresh = _tableRegistry.GetAllTables()
            .Any(t => t.IncrementalRefresh != null);

        if (!hasIncrementalRefresh) return;

        // Only add if not already present (model-level expressions take precedence)
        if (model.Expressions.Find("RangeStart") == null)
        {
            model.Expressions.Add(new NamedExpression
            {
                Name = "RangeStart",
                Kind = ExpressionKind.M,
                Expression = "#datetime(2020, 1, 1, 0, 0, 0) meta [IsParameterQuery=true, Type=\"DateTime\", IsParameterQueryRequired=true]",
                Description = "Incremental refresh range start (managed by Power BI Service)"
            });
        }

        if (model.Expressions.Find("RangeEnd") == null)
        {
            model.Expressions.Add(new NamedExpression
            {
                Name = "RangeEnd",
                Kind = ExpressionKind.M,
                Expression = "#datetime(2024, 12, 31, 0, 0, 0) meta [IsParameterQuery=true, Type=\"DateTime\", IsParameterQueryRequired=true]",
                Description = "Incremental refresh range end (managed by Power BI Service)"
            });
        }
    }

    /// <summary>
    /// Apply incremental refresh policy to a TOM table.
    /// Sets the RefreshPolicy with rolling window configuration.
    /// </summary>
    private static void ApplyIncrementalRefreshPolicy(Table table, IncrementalRefreshDefinition config)
    {
        var granularity = config.Granularity.ToLowerInvariant() switch
        {
            "day" => RefreshGranularityType.Day,
            "month" => RefreshGranularityType.Month,
            "quarter" => RefreshGranularityType.Quarter,
            "year" => RefreshGranularityType.Year,
            _ => RefreshGranularityType.Day
        };

        var policy = new BasicRefreshPolicy
        {
            IncrementalPeriodsOffset = -config.IncrementalPeriodOffset,
            IncrementalPeriods = config.IncrementalPeriods,
            IncrementalGranularity = granularity,
            RollingWindowPeriods = config.IncrementalPeriodOffset,
            RollingWindowGranularity = granularity,
            SourceExpression = table.Partitions.Count > 0 && table.Partitions[0].Source is MPartitionSource src
                ? src.Expression
                : null
        };

        if (!string.IsNullOrWhiteSpace(config.PollingExpression))
        {
            policy.PollingExpression = config.PollingExpression;
        }

        table.RefreshPolicy = policy;

        // Add annotation to mark as incremental refresh enabled
        table.Annotations.Add(new Annotation
        {
            Name = "PBI_IncrementalRefresh",
            Value = $"{{\"dateColumn\":\"{config.DateColumn}\",\"granularity\":\"{config.Granularity}\"}}"
        });
    }

    /// <summary>
    /// Generate M expression from source metadata
    /// </summary>
    private string GenerateMExpressionFromSource(SourceDefinition source, TableDefinition tableDef)
    {
        // If custom query is provided, use it
        if (!string.IsNullOrWhiteSpace(source.Query))
        {
            return GenerateCustomQueryExpression(source, tableDef);
        }

        // Generate connector-specific M expression
        return source.Type.ToLowerInvariant() switch
        {
            "snowflake" => GenerateSnowflakeExpression(source, tableDef),
            "sqlserver" => GenerateSqlServerExpression(source, tableDef),
            _ => throw new NotSupportedException(
                $"Source type '{source.Type}' is not supported. Supported types: snowflake, sqlserver. " +
                $"Alternatively, provide a custom M expression in the 'MExpression' property.")
        };
    }

    /// <summary>
    /// Generate M expression for custom SQL query
    /// </summary>
    private string GenerateCustomQueryExpression(SourceDefinition source, TableDefinition tableDef)
    {
        // This is a generic query template - actual connector depends on source type
        var baseExpression = source.Type.ToLowerInvariant() switch
        {
            "snowflake" => $@"let
    Source = Snowflake.Databases(""{source.Connection}""),
    Database = Source{{[Name=""{source.Database}""]}},
    Query = Value.NativeQuery(Database, ""{source.Query}"")",
            "sqlserver" => $@"let
    Source = Sql.Database(""{source.Connection}"", ""{source.Database}""),
    Query = Value.NativeQuery(Source, ""{source.Query}"")",
            _ => throw new NotSupportedException($"Custom queries not supported for source type: {source.Type}")
        };

        // Add type transformation if M types are specified
        var typeTransform = GenerateTypeTransformation(tableDef, "Query");
        if (!string.IsNullOrEmpty(typeTransform))
        {
            return baseExpression + ",\n" + typeTransform + "\nin\n    TypedTable";
        }

        return baseExpression + "\nin\n    Query";
    }

    /// <summary>
    /// Generate M expression for Snowflake source
    /// </summary>
    private string GenerateSnowflakeExpression(SourceDefinition source, TableDefinition tableDef)
    {
        if (string.IsNullOrWhiteSpace(source.Database))
        {
            throw new InvalidOperationException("Snowflake source requires 'Database' property");
        }

        if (string.IsNullOrWhiteSpace(source.Table))
        {
            throw new InvalidOperationException("Snowflake source requires 'Table' property");
        }

        var schemaName = !string.IsNullOrWhiteSpace(source.Schema) ? source.Schema : "PUBLIC";
        string baseExpression;

        // Use shared connector if specified
        if (!string.IsNullOrWhiteSpace(source.Connector))
        {
            baseExpression = $@"let
    Source = {source.Connector},
    Database = Source{{[Name=""{source.Database}""]}},
    Schema = Database{{[Name=""{schemaName}""]}},
    Table = Schema{{[Name=""{source.Table}""]}}";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(source.Connection))
            {
                throw new InvalidOperationException("Snowflake source requires 'Connection' or 'Connector' property");
            }

            baseExpression = $@"let
    Source = Snowflake.Databases(""{source.Connection}""),
    Database = Source{{[Name=""{source.Database}""]}},
    Schema = Database{{[Name=""{schemaName}""]}},
    Table = Schema{{[Name=""{source.Table}""]}}";
        }

        // Add type transformation if M types are specified
        var typeTransform = GenerateTypeTransformation(tableDef, "Table");
        if (!string.IsNullOrEmpty(typeTransform))
        {
            return baseExpression + ",\n" + typeTransform + "\nin\n    TypedTable";
        }

        return baseExpression + "\nin\n    Table";
    }

    /// <summary>
    /// Generate M expression for SQL Server source
    /// </summary>
    private string GenerateSqlServerExpression(SourceDefinition source, TableDefinition tableDef)
    {
        if (string.IsNullOrWhiteSpace(source.Connection))
        {
            throw new InvalidOperationException("SQL Server source requires 'Connection' property");
        }

        if (string.IsNullOrWhiteSpace(source.Database))
        {
            throw new InvalidOperationException("SQL Server source requires 'Database' property");
        }

        if (string.IsNullOrWhiteSpace(source.Table))
        {
            throw new InvalidOperationException("SQL Server source requires 'Table' property");
        }

        var schemaName = !string.IsNullOrWhiteSpace(source.Schema) ? source.Schema : "dbo";

        var baseExpression = $@"let
    Source = Sql.Database(""{source.Connection}"", ""{source.Database}""),
    Table = Source{{[Schema=""{schemaName}"",Item=""{source.Table}""]}}";

        // Add type transformation if M types are specified
        var typeTransform = GenerateTypeTransformation(tableDef, "Table");
        if (!string.IsNullOrEmpty(typeTransform))
        {
            return baseExpression + ",\n" + typeTransform + "\nin\n    TypedTable";
        }

        return baseExpression + "\nin\n    Table";
    }

    /// <summary>
    /// Generate type transformation M code using Table.TransformColumnTypes
    /// </summary>
    private string GenerateTypeTransformation(TableDefinition tableDef, string sourceStepName)
    {
        // Check if any columns have M types specified
        var columnsWithMTypes = tableDef.Columns
            .Where(c => !string.IsNullOrEmpty(c.MType) && !string.IsNullOrEmpty(c.SourceColumn))
            .ToList();

        if (columnsWithMTypes.Count == 0)
        {
            return string.Empty;
        }

        // Generate type list for Table.TransformColumnTypes
        var typeList = string.Join(",\n        ", columnsWithMTypes.Select(c =>
            $"{{\"{c.SourceColumn}\", {c.MType}}}"
        ));

        return $@"    TypedTable = Table.TransformColumnTypes({sourceStepName}, {{
        {typeList}
    }})";
    }

    /// <summary>
    /// Build a shared connector expression for reuse across tables
    /// </summary>
    private NamedExpression BuildConnectorExpression(ConnectorConfig connector)
    {
        var mExpression = connector.Name.ToLower() switch
        {
            var name when name.Contains("snowflake") => GenerateSnowflakeConnectorExpression(connector),
            var name when name.Contains("sqlserver") || name.Contains("sql") => GenerateSqlServerConnectorExpression(connector),
            _ => GenerateSnowflakeConnectorExpression(connector) // Default to Snowflake
        };

        var lineageTag = _lineageService != null
            ? _lineageService.GetOrGenerateRelationshipTag($"Connector:{connector.Name}")
            : Guid.NewGuid().ToString();

        var expression = new NamedExpression
        {
            Name = connector.Name,
            Expression = mExpression,
            LineageTag = lineageTag
        };

        return expression;
    }

    /// <summary>
    /// Generate M expression for Snowflake shared connector
    /// </summary>
    private string GenerateSnowflakeConnectorExpression(ConnectorConfig connector)
    {
        var options = new List<string>();

        if (!string.IsNullOrEmpty(connector.Implementation))
        {
            options.Add($"[Implementation=\"{connector.Implementation}\"]");
        }

        var optionsStr = options.Count > 0 ? ", " + string.Join(", ", options) : string.Empty;

        if (!string.IsNullOrEmpty(connector.Warehouse))
        {
            return $@"Snowflake.Databases(""{connector.Connection}"", ""{connector.Warehouse}""{optionsStr})";
        }

        return $@"Snowflake.Databases(""{connector.Connection}""{optionsStr})";
    }

    /// <summary>
    /// Generate M expression for SQL Server shared connector
    /// </summary>
    private string GenerateSqlServerConnectorExpression(ConnectorConfig connector)
    {
        // For SQL Server, we'd need database name, which should be in Connection
        return $@"Sql.Database(""{connector.Connection}"")";
    }

    /// <summary>
    /// Parse summarize_by string to TOM AggregateFunction
    /// </summary>
    private AggregateFunction ParseAggregateFunction(string summarizeBy)
    {
        return summarizeBy switch
        {
            "None" => AggregateFunction.None,
            "Sum" => AggregateFunction.Sum,
            "Count" => AggregateFunction.Count,
            "Min" => AggregateFunction.Min,
            "Max" => AggregateFunction.Max,
            "Average" => AggregateFunction.Average,
            "DistinctCount" => AggregateFunction.DistinctCount,
            _ => throw new ArgumentException($"Unknown summarize_by value: {summarizeBy}. Valid values: None, Sum, Count, Min, Max, Average, DistinctCount")
        };
    }

    /// <summary>
    /// Add shared expressions from model and project definitions.
    /// Environment overrides are applied if an environment is active.
    /// </summary>
    private void AddSharedExpressions(ModelDefinition modelDef, Model model)
    {
        // Collect all expressions from the model definition
        var expressionMap = new Dictionary<string, ExpressionDefinition>(StringComparer.OrdinalIgnoreCase);

        if (modelDef.Expressions != null)
        {
            foreach (var expr in modelDef.Expressions)
            {
                expressionMap[expr.Name] = expr;
            }
        }

        // Apply environment overrides
        if (_environment != null)
        {
            foreach (var (name, value) in _environment.Expressions)
            {
                if (expressionMap.TryGetValue(name, out var existing))
                {
                    existing = new ExpressionDefinition
                    {
                        Name = existing.Name,
                        Kind = existing.Kind,
                        Expression = SubstituteEnvironmentVariables(value),
                        Description = existing.Description
                    };
                    expressionMap[name] = existing;
                }
                else
                {
                    expressionMap[name] = new ExpressionDefinition
                    {
                        Name = name,
                        Expression = SubstituteEnvironmentVariables(value)
                    };
                }
            }
        }

        foreach (var exprDef in expressionMap.Values)
        {
            // Don't add duplicates (connector expressions may already be present)
            if (model.Expressions.Find(exprDef.Name) != null)
                continue;

            var expressionValue = SubstituteEnvironmentVariables(exprDef.Expression);

            var lineageTag = _lineageService != null
                ? _lineageService.GetOrGenerateRelationshipTag($"Expression:{exprDef.Name}")
                : Guid.NewGuid().ToString();

            var namedExpression = new NamedExpression
            {
                Name = exprDef.Name,
                Expression = expressionValue,
                Description = exprDef.Description,
                LineageTag = lineageTag
            };

            if (!string.IsNullOrWhiteSpace(exprDef.Kind))
            {
                namedExpression.Kind = ParseExpressionKind(exprDef.Kind);
            }

            model.Expressions.Add(namedExpression);
        }
    }

    /// <summary>
    /// Substitute ${ENV_VAR} references with environment variable values
    /// </summary>
    private static string SubstituteEnvironmentVariables(string value)
    {
        return EnvVarPattern.Replace(value, match =>
        {
            var envVarName = match.Groups[1].Value;
            var envValue = Environment.GetEnvironmentVariable(envVarName);
            return envValue ?? match.Value; // Keep original if env var not set
        });
    }

    /// <summary>
    /// Parse expression kind string
    /// </summary>
    private ExpressionKind ParseExpressionKind(string kind)
    {
        return kind switch
        {
            "M" => ExpressionKind.M,
            _ => ExpressionKind.M // Default to M
        };
    }

    /// <summary>
    /// Build a calculation group as a TOM Table
    /// </summary>
    private Table BuildCalculationGroupTable(CalculationGroupDefinition calcGroupDef)
    {
        var table = new Table
        {
            Name = calcGroupDef.Name,
            Description = calcGroupDef.Description
        };

        // Set calculation group property
        table.CalculationGroup = new CalculationGroup();

        if (calcGroupDef.Precedence.HasValue)
        {
            table.CalculationGroup.Precedence = calcGroupDef.Precedence.Value;
        }

        // Add columns
        foreach (var colDef in calcGroupDef.Columns)
        {
            var column = BuildColumn(colDef, calcGroupDef.Name);
            table.Columns.Add(column);
        }

        // Add calculation items
        foreach (var itemDef in calcGroupDef.CalculationItems)
        {
            var item = new CalculationItem
            {
                Name = itemDef.Name,
                Expression = itemDef.Expression,
                Description = itemDef.Description
            };

            if (itemDef.Ordinal.HasValue)
            {
                item.Ordinal = itemDef.Ordinal.Value;
            }

            if (!string.IsNullOrWhiteSpace(itemDef.FormatStringExpression))
            {
                item.FormatStringDefinition = new FormatStringDefinition
                {
                    Expression = itemDef.FormatStringExpression
                };
            }

            table.CalculationGroup.CalculationItems.Add(item);
        }

        // Generate lineage tag
        table.LineageTag = GenerateLineageTag(calcGroupDef.Name, calcGroupDef.Name, "Table");

        // Add a default partition for calculation group
        var partition = new Partition
        {
            Name = calcGroupDef.Name,
            Source = new CalculationGroupSource()
        };
        table.Partitions.Add(partition);

        return table;
    }

    /// <summary>
    /// Build a field parameter as a TOM Table
    /// </summary>
    private Table BuildFieldParameterTable(FieldParameterDefinition fieldParamDef)
    {
        var table = new Table
        {
            Name = fieldParamDef.Name,
            Description = fieldParamDef.Description
        };

        // Build the DAX expression for the field parameter table
        var valueLines = fieldParamDef.Values.Select((v, i) =>
        {
            var ordinal = v.Ordinal ?? i;
            return $"    ({v.Expression}, \"{v.Name}\", {ordinal})";
        });
        var daxExpression = "{\n" + string.Join(",\n", valueLines) + "\n}";

        // Add calculated partition
        var partition = new Partition
        {
            Name = fieldParamDef.Name,
            Source = new CalculatedPartitionSource
            {
                Expression = daxExpression
            }
        };
        table.Partitions.Add(partition);

        // Add standard field parameter columns
        var valueColumn = new CalculatedTableColumn
        {
            Name = fieldParamDef.Name,
            DataType = DataType.String,
            IsHidden = true,
            SourceColumn = $"[Value1]"
        };
        valueColumn.LineageTag = GenerateLineageTag(fieldParamDef.Name, fieldParamDef.Name, "Column");
        table.Columns.Add(valueColumn);

        var nameColumn = new CalculatedTableColumn
        {
            Name = $"{fieldParamDef.Name} Fields",
            DataType = DataType.String,
            SourceColumn = "[Value2]"
        };
        nameColumn.LineageTag = GenerateLineageTag(fieldParamDef.Name, $"{fieldParamDef.Name} Fields", "Column");
        table.Columns.Add(nameColumn);

        var ordinalColumn = new CalculatedTableColumn
        {
            Name = $"{fieldParamDef.Name} Order",
            DataType = DataType.Int64,
            IsHidden = true,
            SourceColumn = "[Value3]"
        };
        ordinalColumn.LineageTag = GenerateLineageTag(fieldParamDef.Name, $"{fieldParamDef.Name} Order", "Column");
        table.Columns.Add(ordinalColumn);

        // Mark as field parameter via annotation
        table.Annotations.Add(new Annotation
        {
            Name = "ParameterMetadata",
            Value = "{\"version\":3,\"kind\":2}"
        });

        table.LineageTag = GenerateLineageTag(fieldParamDef.Name, fieldParamDef.Name, "Table");

        return table;
    }

    /// <summary>
    /// Build a TOM Perspective
    /// </summary>
    private Perspective BuildPerspective(PerspectiveDefinition perspectiveDef, Model model)
    {
        var perspective = new Perspective
        {
            Name = perspectiveDef.Name,
            Description = perspectiveDef.Description
        };

        foreach (var tableName in perspectiveDef.Tables)
        {
            var table = model.Tables.Find(tableName);
            if (table == null) continue;

            var perspectiveTable = new PerspectiveTable { Table = table };

            // Add all columns unless specifically excluded
            foreach (var column in table.Columns)
            {
                var qualifiedName = $"{tableName}.{column.Name}";
                if (perspectiveDef.ExcludeColumns == null || !perspectiveDef.ExcludeColumns.Contains(qualifiedName))
                {
                    perspectiveTable.PerspectiveColumns.Add(new PerspectiveColumn { Column = column });
                }
            }

            // Add measures (filter if specific list provided)
            foreach (var measure in table.Measures)
            {
                if (perspectiveDef.Measures == null || perspectiveDef.Measures.Count == 0 ||
                    perspectiveDef.Measures.Contains(measure.Name))
                {
                    perspectiveTable.PerspectiveMeasures.Add(new PerspectiveMeasure { Measure = measure });
                }
            }

            // Add hierarchies
            foreach (var hierarchy in table.Hierarchies)
            {
                perspectiveTable.PerspectiveHierarchies.Add(new PerspectiveHierarchy { Hierarchy = hierarchy });
            }

            perspective.PerspectiveTables.Add(perspectiveTable);
        }

        return perspective;
    }

    /// <summary>
    /// Build a TOM ModelRole with RLS table permissions
    /// </summary>
    private ModelRole BuildRole(RoleDefinition roleDef, Model model)
    {
        var role = new ModelRole
        {
            Name = roleDef.Name,
            Description = roleDef.Description,
            ModelPermission = ParseModelPermission(roleDef.ModelPermission)
        };

        foreach (var tablePerm in roleDef.TablePermissions)
        {
            var table = model.Tables.Find(tablePerm.Table);
            if (table == null)
            {
                throw new InvalidOperationException(
                    $"Role '{roleDef.Name}' references table '{tablePerm.Table}' which is not in the model");
            }

            var tablePermission = new TablePermission
            {
                Table = table,
                FilterExpression = tablePerm.FilterExpression
            };

            role.TablePermissions.Add(tablePermission);
        }

        return role;
    }

    /// <summary>
    /// Parse model permission string
    /// </summary>
    private ModelPermission ParseModelPermission(string permission)
    {
        return permission switch
        {
            "Read" => ModelPermission.Read,
            "ReadRefresh" => ModelPermission.ReadRefresh,
            "None" => ModelPermission.None,
            _ => throw new ArgumentException($"Unknown model permission: {permission}. Valid values: Read, ReadRefresh, None")
        };
    }
}

