using Microsoft.AnalysisServices.Tabular;
using Pbt.Core.Models;

namespace Pbt.Core.Services;

/// <summary>
/// Converts TOM (Tabular Object Model) objects to PBT YAML definitions and vice versa.
/// Extracted from CLI ImportCommand to enable testing and reuse.
/// </summary>
public static class TomConverter
{
    // Annotations the composer adds itself; importing them would duplicate on rebuild
    private static readonly HashSet<string> ComposerAnnotations = new() { "__PBI_TimeIntelligenceEnabled", "PBI_ProTooling" };

    private static Dictionary<string, string>? ToDictionary<T>(IEnumerable<T> items, Func<T, string> name, Func<T, string?> value, Func<T, bool>? skip = null)
    {
        var d = items.Where(i => skip == null || !skip(i)).Where(i => value(i) != null).ToDictionary(name, i => value(i)!);
        return d.Count == 0 ? null : d;
    }

    private static T WithMetadata<T>(T def, IEnumerable<Annotation> annotations, IEnumerable<ExtendedProperty> extended, Func<string, bool>? skipAnnotation = null)
        where T : MetadataDefinition
    {
        def.Annotations = ToDictionary(annotations, a => a.Name, a => a.Value, a => skipAnnotation?.Invoke(a.Name) == true);
        def.ExtendedProperties = ToDictionary(extended.OfType<StringExtendedProperty>(), e => e.Name, e => e.Value);
        return def;
    }

    /// <summary>
    /// Convert a TOM Table to a TableDefinition YAML model.
    /// </summary>
    public static TableDefinition ToTableDefinition(Table table, bool includeLineageTags = false)
    {
        var tableDef = WithMetadata(new TableDefinition
        {
            Name = table.Name,
            Description = table.Description,
            IsHidden = table.IsHidden,
            LineageTag = includeLineageTags ? table.LineageTag : null,
            DataCategory = string.IsNullOrEmpty(table.DataCategory) ? null : table.DataCategory,
            IsPrivate = table.IsPrivate ? true : null,
            ExcludeFromModelRefresh = table.ExcludeFromModelRefresh ? true : null,
            AlternateSourcePrecedence = table.AlternateSourcePrecedence != 0 ? table.AlternateSourcePrecedence : null,
            DetailRowsExpression = table.DefaultDetailRowsDefinition?.Expression,
            Columns = new List<ColumnDefinition>(),
            Hierarchies = new List<HierarchyDefinition>(),
            Measures = new List<MeasureDefinition>()
        }, table.Annotations, table.ExtendedProperties);

        // Partitions: a lone plain partition becomes the table shorthand, anything else a partitions list
        var partitions = table.Partitions.Select(ToPartitionDefinition).ToList();
        if (partitions.Count == 1 && partitions[0] is { Mode: null, Description: null, Annotations: null, ExtendedProperties: null, Query: null, EntityName: null } only)
        {
            tableDef.MExpression = only.MExpression;
            tableDef.CalculatedExpression = only.CalculatedExpression;
        }
        else if (partitions.Count > 0)
        {
            tableDef.Partitions = partitions;
        }

        foreach (var column in table.Columns.Where(c => c is DataColumn or CalculatedColumn or CalculatedTableColumn))
            tableDef.Columns.Add(ToColumnDefinition(column, includeLineageTags));

        foreach (var hierarchy in table.Hierarchies)
        {
            var hierarchyDef = WithMetadata(new HierarchyDefinition
            {
                Name = hierarchy.Name,
                Description = hierarchy.Description,
                DisplayFolder = hierarchy.DisplayFolder,
                LineageTag = includeLineageTags ? hierarchy.LineageTag : null,
                Levels = hierarchy.Levels.Select(l => new LevelDefinition { Name = l.Name, Column = l.Column.Name }).ToList()
            }, hierarchy.Annotations, hierarchy.ExtendedProperties);
            tableDef.Hierarchies.Add(hierarchyDef);
        }

        foreach (var measure in table.Measures)
            tableDef.Measures.Add(ToMeasureDefinition(measure, table.Name, includeLineageTags));

        return tableDef;
    }

    private static PartitionDefinition ToPartitionDefinition(Partition partition)
    {
        var def = WithMetadata(new PartitionDefinition
        {
            Name = partition.Name,
            Description = string.IsNullOrEmpty(partition.Description) ? null : partition.Description,
            Mode = partition.Mode == ModeType.Default ? null : partition.Mode.ToString()
        }, partition.Annotations, partition.ExtendedProperties);

        switch (partition.Source)
        {
            case MPartitionSource m:
                // Normalize tabs to spaces for YAML compatibility
                def.MExpression = m.Expression?.Replace("\t", "  ");
                break;
            case CalculatedPartitionSource c:
                def.CalculatedExpression = c.Expression;
                break;
            case QueryPartitionSource q:
                def.Query = q.Query;
                def.DataSource = q.DataSource?.Name;
                break;
            case EntityPartitionSource e:
                def.EntityName = e.EntityName;
                def.SchemaName = e.SchemaName;
                def.ExpressionSource = e.ExpressionSource?.Name;
                break;
        }
        return def;
    }

    private static ColumnDefinition ToColumnDefinition(Column column, bool includeLineageTags)
    {
        var colDef = WithMetadata(new ColumnDefinition
        {
            Name = column.Name,
            Type = column.DataType.ToString(),
            Description = column.Description,
            FormatString = column.FormatString,
            IsHidden = column.IsHidden,
            DisplayFolder = column.DisplayFolder,
            LineageTag = includeLineageTags ? column.LineageTag : null,
            SortByColumn = column.SortByColumn?.Name,
            DataCategory = string.IsNullOrEmpty(column.DataCategory) ? null : column.DataCategory,
            SummarizeBy = column.SummarizeBy == AggregateFunction.Default ? null : column.SummarizeBy.ToString(),
            IsKey = column.IsKey ? true : null,
            IsNullable = column.IsNullable ? null : false,
            IsUnique = column.IsUnique ? true : null,
            EncodingHint = column.EncodingHint == EncodingHintType.Default ? null : column.EncodingHint.ToString()
        }, column.Annotations, column.ExtendedProperties);

        switch (column)
        {
            case DataColumn dc: colDef.SourceColumn = dc.SourceColumn; break;
            case CalculatedTableColumn ctc: colDef.SourceColumn = ctc.SourceColumn; break;
            case CalculatedColumn cc: colDef.Expression = cc.Expression; break;
        }

        if (column.AlternateOf is { } alt)
        {
            colDef.AlternateOf = new AlternateOfDefinition
            {
                Summarization = alt.Summarization.ToString(),
                BaseColumn = alt.BaseColumn == null ? null : $"{alt.BaseColumn.Table.Name}.{alt.BaseColumn.Name}",
                BaseTable = alt.BaseTable?.Name
            };
        }
        return colDef;
    }

    private static MeasureDefinition ToMeasureDefinition(Measure measure, string tableName, bool includeLineageTags)
    {
        var def = WithMetadata(new MeasureDefinition
        {
            Name = measure.Name,
            Table = tableName,
            Expression = measure.Expression,
            Description = measure.Description,
            FormatString = measure.FormatString,
            DisplayFolder = measure.DisplayFolder,
            IsHidden = measure.IsHidden,
            LineageTag = includeLineageTags ? measure.LineageTag : null,
            FormatStringExpression = measure.FormatStringDefinition?.Expression,
            DetailRowsExpression = measure.DetailRowsDefinition?.Expression
        }, measure.Annotations, measure.ExtendedProperties);

        if (measure.KPI is { } k)
        {
            def.Kpi = new KpiDefinition
            {
                Description = k.Description,
                TargetExpression = k.TargetExpression,
                TargetFormatString = k.TargetFormatString,
                StatusExpression = k.StatusExpression,
                StatusGraphic = k.StatusGraphic,
                TrendExpression = k.TrendExpression,
                TrendGraphic = k.TrendGraphic
            };
        }
        return def;
    }

    /// <summary>
    /// Convert a TOM Database to a ModelDefinition YAML model.
    /// Calculation group tables become calculation_groups; every other table is a table ref.
    /// </summary>
    public static ModelDefinition ToModelDefinition(Database database, bool includeLineageTags = false)
    {
        var model = database.Model;
        var modelDef = WithMetadata(new ModelDefinition
        {
            Name = database.Name,
            Description = model.Description,
            CompatibilityLevel = database.CompatibilityLevel,
            Culture = model.Culture,
            SourceQueryCulture = model.SourceQueryCulture,
            DiscourageImplicitMeasures = model.DiscourageImplicitMeasures,
            AutoTimeIntelligence = model.Annotations.Find("__PBI_TimeIntelligenceEnabled")?.Value == "1",
            Tables = new List<TableReference>(),
            Relationships = new List<RelationshipDefinition>(),
            Measures = new List<MeasureDefinition>()
        }, model.Annotations, model.ExtendedProperties, ComposerAnnotations.Contains);

        foreach (var table in model.Tables)
        {
            if (table.CalculationGroup != null)
                (modelDef.CalculationGroups ??= new()).Add(ToCalculationGroupDefinition(table));
            else
                modelDef.Tables.Add(new TableReference { Ref = table.Name });
        }

        foreach (var relationship in model.Relationships.OfType<SingleColumnRelationship>())
        {
            modelDef.Relationships.Add(WithMetadata(new RelationshipDefinition
            {
                FromTable = relationship.FromTable.Name,
                FromColumn = relationship.FromColumn.Name,
                ToTable = relationship.ToTable.Name,
                ToColumn = relationship.ToColumn.Name,
                Cardinality = MapCardinality(relationship.FromCardinality, relationship.ToCardinality),
                CrossFilterDirection = MapCrossFilterDirection(relationship.CrossFilteringBehavior),
                Active = relationship.IsActive,
                RelyOnReferentialIntegrity = relationship.RelyOnReferentialIntegrity,
                SecurityFilteringBehavior = relationship.SecurityFilteringBehavior == SecurityFilteringBehavior.OneDirection ? null : relationship.SecurityFilteringBehavior.ToString(),
                JoinOnDateBehavior = relationship.JoinOnDateBehavior == DateTimeRelationshipBehavior.DateAndTime ? null : relationship.JoinOnDateBehavior.ToString()
            }, relationship.Annotations, relationship.ExtendedProperties));
        }

        foreach (var table in model.Tables.Where(t => t.CalculationGroup == null))
            foreach (var measure in table.Measures)
                modelDef.Measures.Add(ToMeasureDefinition(measure, table.Name, includeLineageTags));

        if (model.Expressions.Count > 0)
        {
            modelDef.Expressions = model.Expressions.Select(e => new ExpressionDefinition
            {
                Name = e.Name,
                Kind = e.Kind.ToString(),
                Expression = e.Expression,
                Description = string.IsNullOrEmpty(e.Description) ? null : e.Description
            }).ToList();
        }

        if (model.DataSources.Count > 0)
            modelDef.DataSources = model.DataSources.Select(ToDataSourceDefinition).ToList();

        if (model.Functions.Count > 0)
        {
            modelDef.Functions = model.Functions.Select(f => WithMetadata(new FunctionDefinition
            {
                Name = f.Name,
                Expression = f.Expression,
                Description = string.IsNullOrEmpty(f.Description) ? null : f.Description,
                IsHidden = f.IsHidden ? true : null
            }, f.Annotations, f.ExtendedProperties)).ToList();
        }

        if (model.Cultures.Count > 0)
            modelDef.Cultures = model.Cultures.Select(ToCultureDefinition).ToList();

        if (model.Roles.Count > 0)
            modelDef.Roles = model.Roles.Select(ToRoleDefinition).ToList();

        if (model.Perspectives.Count > 0)
            modelDef.Perspectives = model.Perspectives.Select(ToPerspectiveDefinition).ToList();

        return modelDef;
    }

    private static CalculationGroupDefinition ToCalculationGroupDefinition(Table table)
    {
        var group = table.CalculationGroup;
        return new CalculationGroupDefinition
        {
            Name = table.Name,
            Description = table.Description,
            Precedence = group.Precedence,
            NoSelectionExpression = group.NoSelectionExpression?.Expression,
            MultipleOrEmptySelectionExpression = group.MultipleOrEmptySelectionExpression?.Expression,
            Columns = table.Columns.Where(c => c is DataColumn).Select(c => ToColumnDefinition(c, false)).ToList(),
            CalculationItems = group.CalculationItems.Select(i => new CalculationItemDefinition
            {
                Name = i.Name,
                Expression = i.Expression,
                FormatStringExpression = i.FormatStringDefinition?.Expression,
                Ordinal = i.Ordinal,
                Description = string.IsNullOrEmpty(i.Description) ? null : i.Description
            }).ToList()
        };
    }

    private static DataSourceDefinition ToDataSourceDefinition(DataSource ds)
    {
        var def = WithMetadata(new DataSourceDefinition
        {
            Name = ds.Name,
            Description = string.IsNullOrEmpty(ds.Description) ? null : ds.Description
        }, ds.Annotations, ds.ExtendedProperties);

        switch (ds)
        {
            case ProviderDataSource p:
                def.ConnectionString = p.ConnectionString;
                def.Provider = string.IsNullOrEmpty(p.Provider) ? null : p.Provider;
                break;
            case StructuredDataSource sds:
                def.Protocol = sds.ConnectionDetails?.Protocol;
                var address = sds.ConnectionDetails?.Address;
                if (address != null)
                {
                    var pairs = new Dictionary<string, string?>
                    {
                        ["server"] = address.Server, ["database"] = address.Database, ["schema"] = address.Schema,
                        ["object"] = address.Object, ["url"] = address.Url, ["path"] = address.Path,
                        ["account"] = address.Account, ["domain"] = address.Domain, ["resource"] = address.Resource
                    };
                    def.Address = ToDictionary(pairs, p => p.Key, p => string.IsNullOrEmpty(p.Value) ? null : p.Value);
                }
                // Passwords are never imported
                var cred = sds.Credential;
                if (cred != null)
                {
                    var pairs = new Dictionary<string, string?>
                    {
                        ["AuthenticationKind"] = cred.AuthenticationKind, ["PrivacySetting"] = cred.PrivacySetting, ["Username"] = cred.Username
                    };
                    def.Credential = ToDictionary(pairs, p => p.Key, p => string.IsNullOrEmpty(p.Value) ? null : p.Value);
                }
                break;
        }
        return def;
    }

    private static CultureDefinition ToCultureDefinition(Culture culture) =>
        WithMetadata(new CultureDefinition
        {
            Name = culture.Name,
            Translations = culture.ObjectTranslations.Select(t =>
            {
                var (table, name) = t.Object switch
                {
                    Table tb => (tb.Name, null),
                    Column c => (c.Table.Name, c.Name),
                    Measure m => (m.Table.Name, m.Name),
                    Hierarchy h => (h.Table.Name, h.Name),
                    _ => (null as string, null as string)
                };
                return table == null ? null : new TranslationDefinition { Table = table, Object = name, Property = t.Property.ToString(), Value = t.Value };
            }).OfType<TranslationDefinition>().ToList()
        }, culture.Annotations, culture.ExtendedProperties);

    private static RoleDefinition ToRoleDefinition(ModelRole role) =>
        WithMetadata(new RoleDefinition
        {
            Name = role.Name,
            Description = string.IsNullOrEmpty(role.Description) ? null : role.Description,
            ModelPermission = role.ModelPermission.ToString(),
            TablePermissions = role.TablePermissions.Select(tp => new TablePermissionDefinition
            {
                Table = tp.Table.Name,
                FilterExpression = tp.FilterExpression ?? string.Empty,
                ColumnPermissions = tp.ColumnPermissions.Count == 0
                    ? null
                    : tp.ColumnPermissions.ToDictionary(cp => cp.Column.Name, cp => cp.MetadataPermission.ToString())
            }).ToList()
        }, role.Annotations, role.ExtendedProperties);

    private static PerspectiveDefinition ToPerspectiveDefinition(Perspective perspective)
    {
        var def = WithMetadata(new PerspectiveDefinition
        {
            Name = perspective.Name,
            Description = string.IsNullOrEmpty(perspective.Description) ? null : perspective.Description,
            Tables = perspective.PerspectiveTables.Select(pt => pt.Table.Name).ToList()
        }, perspective.Annotations, perspective.ExtendedProperties);

        var excluded = perspective.PerspectiveTables
            .SelectMany(pt => pt.Table.Columns.Where(c => c is DataColumn or CalculatedColumn && pt.PerspectiveColumns.Find(c.Name) == null)
                .Select(c => $"{pt.Table.Name}.{c.Name}"))
            .ToList();
        if (excluded.Count > 0) def.ExcludeColumns = excluded;

        var measures = perspective.PerspectiveTables.SelectMany(pt => pt.PerspectiveMeasures.Select(m => m.Measure.Name)).ToList();
        if (measures.Count > 0) def.Measures = measures;
        return def;
    }

    /// <summary>
    /// Map TOM cardinality enum pair to YAML string representation.
    /// </summary>
    public static string MapCardinality(RelationshipEndCardinality from, RelationshipEndCardinality to)
    {
        return (from, to) switch
        {
            (RelationshipEndCardinality.Many, RelationshipEndCardinality.One) => "ManyToOne",
            (RelationshipEndCardinality.One, RelationshipEndCardinality.Many) => "OneToMany",
            (RelationshipEndCardinality.One, RelationshipEndCardinality.One) => "OneToOne",
            (RelationshipEndCardinality.Many, RelationshipEndCardinality.Many) => "ManyToMany",
            _ => throw new InvalidOperationException($"Unknown cardinality combination: {from} to {to}")
        };
    }

    /// <summary>
    /// Map TOM cross-filter behavior to YAML string representation.
    /// </summary>
    public static string MapCrossFilterDirection(CrossFilteringBehavior behavior)
    {
        return behavior switch
        {
            CrossFilteringBehavior.OneDirection => "Single",
            CrossFilteringBehavior.BothDirections => "Both",
            CrossFilteringBehavior.Automatic => "Automatic",
            _ => "Single"
        };
    }
}
