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
    private Model? _currentModel;
    private readonly List<(EntityPartitionSource Source, string Expression, string Context)> _pendingEntityLinks = new();
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
        _currentModel = model;
        _pendingEntityLinks.Clear();
        model.Description = modelDef.Description;
        model.Culture = modelDef.Culture;
        model.DiscourageImplicitMeasures = modelDef.DiscourageImplicitMeasures;
        model.DefaultPowerBIDataSourceVersion = Microsoft.AnalysisServices.Tabular.PowerBIDataSourceVersion.PowerBI_V3;
        model.SourceQueryCulture = modelDef.SourceQueryCulture;

        // Data access options
        model.DataAccessOptions.LegacyRedirects = true;
        model.DataAccessOptions.ReturnErrorValuesAsNull = true;

        ApplyMetadata(modelDef, model.Annotations.Add, model.ExtendedProperties.Add);

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

        // 0. Add data sources (query partitions reference them)
        foreach (var dsDef in modelDef.DataSources ?? new())
            model.DataSources.Add(BuildDataSource(dsDef));

        // 1. Add tables
        foreach (var tableRef in modelDef.Tables)
        {
            var tableDef = _tableRegistry.GetTable(tableRef.Ref);
            var table = BuildTable(tableDef);
            model.Tables.Add(table);
        }

        ResolveColumnReferences(model, modelDef.Tables.Select(t => _tableRegistry.GetTable(t.Ref)));

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

        // 5a. Link Direct Lake entity partitions to their expression sources
        foreach (var (source, expression, context) in _pendingEntityLinks)
        {
            source.ExpressionSource = model.Expressions.Find(expression)
                ?? throw new InvalidOperationException($"{context} expression_source '{expression}' not found in model expressions");
        }

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

        // 7b. Add user-defined functions
        foreach (var fnDef in modelDef.Functions ?? new())
        {
            var fn = new Function { Name = fnDef.Name, Expression = fnDef.Expression, Description = fnDef.Description, IsHidden = fnDef.IsHidden ?? false };
            ApplyMetadata(fnDef, fn.Annotations.Add, fn.ExtendedProperties.Add);
            model.Functions.Add(fn);
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

        // 8b. Add cultures with translations
        foreach (var cultureDef in modelDef.Cultures ?? new())
            model.Cultures.Add(BuildCulture(cultureDef, model));

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
    /// Build a partition; the source kind follows which fields are set (M, DAX, native query, Direct Lake entity)
    /// </summary>
    private Partition BuildPartition(PartitionDefinition partDef, TableDefinition tableDef)
    {
        var mExpr = ResolveMExpression(partDef.MExpression, partDef.MExpressionFile, tableDef.SourceFilePath);
        var context = $"Partition '{partDef.Name}' in table '{tableDef.Name}'";

        PartitionSource source;
        if (!string.IsNullOrWhiteSpace(mExpr))
            source = new MPartitionSource { Expression = mExpr };
        else if (!string.IsNullOrWhiteSpace(partDef.CalculatedExpression))
            source = new CalculatedPartitionSource { Expression = partDef.CalculatedExpression };
        else if (!string.IsNullOrWhiteSpace(partDef.Query))
            source = new QueryPartitionSource { Query = partDef.Query, DataSource = ResolveDataSource(partDef.DataSource, context) };
        else if (!string.IsNullOrWhiteSpace(partDef.EntityName))
        {
            if (string.IsNullOrWhiteSpace(partDef.ExpressionSource))
                throw new InvalidOperationException($"{context} uses entity_name and needs expression_source");
            source = new EntityPartitionSource
            {
                EntityName = partDef.EntityName,
                SchemaName = partDef.SchemaName
            };
            _pendingEntityLinks.Add(((EntityPartitionSource)source, partDef.ExpressionSource, context));
        }
        else
            throw new InvalidOperationException($"{context} needs one of m_expression, m_expression_file, calculated_expression, query or entity_name");

        var partition = new Partition { Name = partDef.Name, Description = partDef.Description, Source = source };
        if (!string.IsNullOrWhiteSpace(partDef.Mode))
            partition.Mode = ParsePartitionMode(partDef.Mode);
        ApplyMetadata(partDef, partition.Annotations.Add, partition.ExtendedProperties.Add);
        return partition;
    }

    private DataSource ResolveDataSource(string? name, string context) =>
        string.IsNullOrWhiteSpace(name)
            ? throw new InvalidOperationException($"{context} uses query and needs data_source")
            : _currentModel?.DataSources.Find(name)
                ?? throw new InvalidOperationException($"{context} data_source '{name}' not found");

    private static DataSource BuildDataSource(DataSourceDefinition def)
    {
        DataSource ds;
        if (!string.IsNullOrWhiteSpace(def.ConnectionString))
        {
            ds = new ProviderDataSource { Name = def.Name, ConnectionString = def.ConnectionString };
            if (!string.IsNullOrWhiteSpace(def.Provider)) ((ProviderDataSource)ds).Provider = def.Provider;
        }
        else if (!string.IsNullOrWhiteSpace(def.Protocol))
        {
            var sds = new StructuredDataSource
            {
                Name = def.Name,
                ConnectionDetails = new ConnectionDetails { Protocol = def.Protocol }
            };
            foreach (var (k, v) in def.Address ?? new()) sds.ConnectionDetails.Address[k] = v;
            foreach (var (k, v) in def.Credential ?? new()) sds.Credential[k] = v;
            ds = sds;
        }
        else
            throw new InvalidOperationException($"Data source '{def.Name}' needs connection_string or protocol");

        ds.Description = def.Description;
        ApplyMetadata(def, ds.Annotations.Add, ds.ExtendedProperties.Add);
        return ds;
    }

    private static void ApplyMetadata(MetadataDefinition def, Action<Annotation> addAnnotation, Action<ExtendedProperty> addExtended)
    {
        if (def.Annotations != null)
            foreach (var (key, value) in def.Annotations)
                addAnnotation(new Annotation { Name = key, Value = value });
        if (def.ExtendedProperties != null)
            foreach (var (key, value) in def.ExtendedProperties)
                addExtended(new StringExtendedProperty { Name = key, Value = value });
    }

    private static T ParseEnum<T>(string value, string what) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var result)
            ? result
            : throw new ArgumentException($"Unknown {what}: {value}. Valid values: {string.Join(", ", Enum.GetNames<T>())}");

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

        // Add partitions - explicit list takes priority over single-partition shorthand
        if (tableDef.Partitions != null && tableDef.Partitions.Count > 0)
        {
            foreach (var partDef in tableDef.Partitions)
            {
                table.Partitions.Add(BuildPartition(partDef, tableDef));
            }
        }
        else
        {
            // Single partition from MExpression, MExpressionFile, CalculatedExpression, or Source
            var mExpression = ResolveMExpression(tableDef.MExpression, tableDef.MExpressionFile, tableDef.SourceFilePath);

            if (string.IsNullOrWhiteSpace(mExpression) && string.IsNullOrWhiteSpace(tableDef.CalculatedExpression) && tableDef.Source != null)
            {
                mExpression = GenerateMExpressionFromSource(tableDef.Source, tableDef);
            }

            if (!string.IsNullOrWhiteSpace(mExpression) || !string.IsNullOrWhiteSpace(tableDef.CalculatedExpression))
            {
                table.Partitions.Add(BuildPartition(
                    new PartitionDefinition { Name = tableDef.Name, MExpression = mExpression, CalculatedExpression = tableDef.CalculatedExpression },
                    tableDef));
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
                if (sortByColumn == null)
                {
                    throw new InvalidOperationException(
                        $"Column '{colDef.Name}' in table '{tableDef.Name}' sorts by '{colDef.SortByColumn}' which does not exist");
                }
                if (column != null)
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
        ApplyMetadata(tableDef, table.Annotations.Add, table.ExtendedProperties.Add);
        if (!string.IsNullOrWhiteSpace(tableDef.DataCategory)) table.DataCategory = tableDef.DataCategory;
        if (tableDef.IsPrivate.HasValue) table.IsPrivate = tableDef.IsPrivate.Value;
        if (tableDef.ExcludeFromModelRefresh.HasValue) table.ExcludeFromModelRefresh = tableDef.ExcludeFromModelRefresh.Value;
        if (tableDef.AlternateSourcePrecedence.HasValue) table.AlternateSourcePrecedence = tableDef.AlternateSourcePrecedence.Value;
        if (!string.IsNullOrWhiteSpace(tableDef.DetailRowsExpression))
            table.DefaultDetailRowsDefinition = new DetailRowsDefinition { Expression = tableDef.DetailRowsExpression };

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
    private ModeType ParsePartitionMode(string mode) =>
        Enum.TryParse<ModeType>(mode, ignoreCase: true, out var m)
            ? m
            : throw new ArgumentException($"Unknown partition mode: {mode}. Valid values: {string.Join(", ", Enum.GetNames<ModeType>())}");

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

        ApplyCommonColumnProperties(column, colDef);

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

        ApplyCommonColumnProperties(column, colDef);

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

    private static void ApplyCommonColumnProperties(Column column, ColumnDefinition colDef)
    {
        ApplyMetadata(colDef, column.Annotations.Add, column.ExtendedProperties.Add);
        if (colDef.IsNullable.HasValue) column.IsNullable = colDef.IsNullable.Value;
        if (colDef.IsUnique.HasValue) column.IsUnique = colDef.IsUnique.Value;
        if (!string.IsNullOrWhiteSpace(colDef.EncodingHint))
            column.EncodingHint = ParseEnum<EncodingHintType>(colDef.EncodingHint, "encoding hint");
    }

    /// <summary>
    /// Resolve alternate_of once every table and column exists
    /// </summary>
    private static void ResolveColumnReferences(Model model, IEnumerable<TableDefinition> tableDefs)
    {
        Column FindColumn(string reference, string ownerTable, string context)
        {
            var parts = reference.Split('.', 2);
            var (tableName, colName) = parts.Length == 2 ? (parts[0], parts[1]) : (ownerTable, parts[0]);
            return model.Tables.Find(tableName)?.Columns.Find(colName)
                ?? throw new InvalidOperationException($"{context} references column '{reference}' which does not exist");
        }

        foreach (var tableDef in tableDefs)
        {
            foreach (var colDef in tableDef.Columns)
            {
                var column = model.Tables[tableDef.Name].Columns[colDef.Name];
                var context = $"Column '{tableDef.Name}.{colDef.Name}'";

                if (colDef.AlternateOf != null)
                {
                    var alt = new AlternateOf { Summarization = ParseEnum<SummarizationType>(colDef.AlternateOf.Summarization, "summarization") };
                    if (!string.IsNullOrWhiteSpace(colDef.AlternateOf.BaseColumn))
                        alt.BaseColumn = FindColumn(colDef.AlternateOf.BaseColumn, tableDef.Name, context);
                    else if (!string.IsNullOrWhiteSpace(colDef.AlternateOf.BaseTable))
                        alt.BaseTable = model.Tables.Find(colDef.AlternateOf.BaseTable)
                            ?? throw new InvalidOperationException($"{context} alternate_of references table '{colDef.AlternateOf.BaseTable}' which does not exist");
                    else
                        throw new InvalidOperationException($"{context} alternate_of needs base_column or base_table");
                    column.AlternateOf = alt;
                }
            }
        }
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

        ApplyMetadata(hierarchyDef, hierarchy.Annotations.Add, hierarchy.ExtendedProperties.Add);

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
        (relationship.FromCardinality, relationship.ToCardinality) = ParseCardinality(relDef.Cardinality);

        // Parse cross filter direction
        if (!string.IsNullOrWhiteSpace(relDef.CrossFilterDirection))
        {
            relationship.CrossFilteringBehavior = ParseCrossFilterDirection(relDef.CrossFilterDirection);
        }

        if (!string.IsNullOrWhiteSpace(relDef.SecurityFilteringBehavior))
            relationship.SecurityFilteringBehavior = ParseEnum<SecurityFilteringBehavior>(relDef.SecurityFilteringBehavior, "security filtering behavior");
        if (!string.IsNullOrWhiteSpace(relDef.JoinOnDateBehavior))
            relationship.JoinOnDateBehavior = ParseEnum<DateTimeRelationshipBehavior>(relDef.JoinOnDateBehavior, "join on date behavior");
        ApplyMetadata(relDef, relationship.Annotations.Add, relationship.ExtendedProperties.Add);

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

        return StableGuid($"Relationship|{relationshipKey}");
    }

    /// <summary>
    /// No manifest: derive a stable GUID from the object path so rebuilds stay deterministic
    /// </summary>
    private static string StableGuid(string key) =>
        new Guid(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(key))).ToString();

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

        ApplyMetadata(measureDef, measure.Annotations.Add, measure.ExtendedProperties.Add);
        if (!string.IsNullOrWhiteSpace(measureDef.FormatStringExpression))
            measure.FormatStringDefinition = new FormatStringDefinition { Expression = measureDef.FormatStringExpression };
        if (!string.IsNullOrWhiteSpace(measureDef.DetailRowsExpression))
            measure.DetailRowsDefinition = new DetailRowsDefinition { Expression = measureDef.DetailRowsExpression };
        if (measureDef.Kpi is { } k)
        {
            var kpi = new KPI
            {
                Description = k.Description,
                TargetExpression = k.TargetExpression,
                TargetFormatString = k.TargetFormatString,
                StatusExpression = k.StatusExpression,
                StatusGraphic = k.StatusGraphic,
                TrendExpression = k.TrendExpression,
                TrendGraphic = k.TrendGraphic
            };
            measure.KPI = kpi;
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
    private DataType ParseDataType(string type) =>
        Enum.TryParse<DataType>(type, ignoreCase: true, out var dt) && dt != DataType.Automatic && dt != DataType.Unknown
            ? dt
            : throw new ArgumentException($"Unknown data type: {type}");

    /// <summary>
    /// Parse cardinality string to TOM enums
    /// </summary>
    private static (RelationshipEndCardinality From, RelationshipEndCardinality To) ParseCardinality(string cardinality)
    {
        const RelationshipEndCardinality one = RelationshipEndCardinality.One, many = RelationshipEndCardinality.Many;
        return cardinality switch
        {
            "ManyToOne" => (many, one),
            "OneToMany" => (one, many),
            "OneToOne" => (one, one),
            "ManyToMany" => (many, many),
            _ => throw new ArgumentException($"Unknown cardinality: {cardinality}")
        };
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
                _ => throw new ArgumentException($"Unknown lineage object type: {objectType}")
            };
        }

        return StableGuid($"{objectType}|{tableName}|{objectName}");
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
            _ => throw new ArgumentException(
                $"Unknown incremental refresh granularity: {config.Granularity}. Valid values: Day, Month, Quarter, Year")
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
            Value = System.Text.Json.JsonSerializer.Serialize(new { dateColumn = config.DateColumn, granularity = config.Granularity })
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
            : StableGuid($"Connector:{connector.Name}");

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
                : StableGuid($"Expression:{exprDef.Name}");

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

        if (!string.IsNullOrWhiteSpace(calcGroupDef.NoSelectionExpression))
            table.CalculationGroup.NoSelectionExpression = new CalculationGroupExpression { Expression = calcGroupDef.NoSelectionExpression };
        if (!string.IsNullOrWhiteSpace(calcGroupDef.MultipleOrEmptySelectionExpression))
            table.CalculationGroup.MultipleOrEmptySelectionExpression = new CalculationGroupExpression { Expression = calcGroupDef.MultipleOrEmptySelectionExpression };

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
            if (table == null)
            {
                throw new InvalidOperationException(
                    $"Perspective '{perspectiveDef.Name}' references table '{tableName}' which is not in the model");
            }

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

        ApplyMetadata(perspectiveDef, perspective.Annotations.Add, perspective.ExtendedProperties.Add);
        return perspective;
    }

    private static Culture BuildCulture(CultureDefinition def, Model model)
    {
        var culture = new Culture { Name = def.Name };
        ApplyMetadata(def, culture.Annotations.Add, culture.ExtendedProperties.Add);

        foreach (var t in def.Translations)
        {
            var table = model.Tables.Find(t.Table)
                ?? throw new InvalidOperationException($"Culture '{def.Name}' translation references table '{t.Table}' which is not in the model");
            var context = $"Culture '{def.Name}' translation '{t.Table}.{t.Object}'";

            MetadataObject target = table;
            if (!string.IsNullOrWhiteSpace(t.Object))
            {
                target = (MetadataObject?)table.Columns.Find(t.Object)
                    ?? (MetadataObject?)table.Measures.Find(t.Object)
                    ?? (MetadataObject?)table.Hierarchies.Find(t.Object)
                    ?? throw new InvalidOperationException($"{context} does not exist");
            }

            culture.ObjectTranslations.Add(new ObjectTranslation
            {
                Object = target,
                Property = ParseEnum<TranslatedProperty>(t.Property, "translated property"),
                Value = t.Value
            });
        }
        return culture;
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

            var tablePermission = new TablePermission { Table = table };
            if (!string.IsNullOrWhiteSpace(tablePerm.FilterExpression))
                tablePermission.FilterExpression = tablePerm.FilterExpression;

            if (tablePerm.ColumnPermissions != null)
            {
                foreach (var (colName, permission) in tablePerm.ColumnPermissions)
                {
                    var col = table.Columns.Find(colName)
                        ?? throw new InvalidOperationException(
                            $"Role '{roleDef.Name}' column permission references column '{tablePerm.Table}.{colName}' which does not exist");
                    tablePermission.ColumnPermissions.Add(new ColumnPermission
                    {
                        Column = col,
                        MetadataPermission = ParseEnum<MetadataPermission>(permission, "column permission")
                    });
                }
            }

            role.TablePermissions.Add(tablePermission);
        }

        ApplyMetadata(roleDef, role.Annotations.Add, role.ExtendedProperties.Add);
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

