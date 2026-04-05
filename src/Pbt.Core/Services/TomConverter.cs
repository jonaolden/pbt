using Microsoft.AnalysisServices.Tabular;
using Pbt.Core.Models;

namespace Pbt.Core.Services;

/// <summary>
/// Converts TOM (Tabular Object Model) objects to PBT YAML definitions and vice versa.
/// Extracted from CLI ImportCommand to enable testing and reuse.
/// </summary>
public static class TomConverter
{
    /// <summary>
    /// Convert a TOM Table to a TableDefinition YAML model.
    /// </summary>
    public static TableDefinition ToTableDefinition(Table table, bool includeLineageTags = false)
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
            tableDef.MExpression = mSource.Expression;
        }

        // Extract data columns
        foreach (var column in table.Columns.OfType<DataColumn>())
        {
            tableDef.Columns.Add(new ColumnDefinition
            {
                Name = column.Name,
                Type = column.DataType.ToString(),
                Description = column.Description,
                SourceColumn = column.SourceColumn,
                FormatString = column.FormatString,
                IsHidden = column.IsHidden,
                DisplayFolder = column.DisplayFolder,
                LineageTag = includeLineageTags ? column.LineageTag : null
            });
        }

        // Extract calculated columns
        foreach (var column in table.Columns.OfType<CalculatedColumn>())
        {
            tableDef.Columns.Add(new ColumnDefinition
            {
                Name = column.Name,
                Type = column.DataType.ToString(),
                Description = column.Description,
                Expression = column.Expression,
                FormatString = column.FormatString,
                IsHidden = column.IsHidden,
                DisplayFolder = column.DisplayFolder,
                LineageTag = includeLineageTags ? column.LineageTag : null
            });
        }

        // Extract hierarchies
        foreach (var hierarchy in table.Hierarchies)
        {
            var hierarchyDef = new HierarchyDefinition
            {
                Name = hierarchy.Name,
                Description = hierarchy.Description,
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
            tableDef.Measures.Add(new MeasureDefinition
            {
                Name = measure.Name,
                Table = table.Name,
                Expression = measure.Expression,
                Description = measure.Description,
                FormatString = measure.FormatString,
                DisplayFolder = measure.DisplayFolder,
                IsHidden = measure.IsHidden,
                LineageTag = includeLineageTags ? measure.LineageTag : null
            });
        }

        return tableDef;
    }

    /// <summary>
    /// Convert a TOM Database to a ModelDefinition YAML model.
    /// </summary>
    public static ModelDefinition ToModelDefinition(Database database, bool includeLineageTags = false)
    {
        var modelDef = new ModelDefinition
        {
            Name = database.Name,
            Description = database.Model.Description,
            CompatibilityLevel = database.CompatibilityLevel,
            Tables = new List<TableReference>(),
            Relationships = new List<RelationshipDefinition>(),
            Measures = new List<MeasureDefinition>()
        };

        // Add table references
        foreach (var table in database.Model.Tables)
        {
            modelDef.Tables.Add(new TableReference { Ref = table.Name });
        }

        // Extract relationships
        foreach (var relationship in database.Model.Relationships.OfType<SingleColumnRelationship>())
        {
            modelDef.Relationships.Add(new RelationshipDefinition
            {
                FromTable = relationship.FromTable.Name,
                FromColumn = relationship.FromColumn.Name,
                ToTable = relationship.ToTable.Name,
                ToColumn = relationship.ToColumn.Name,
                Cardinality = MapCardinality(relationship.FromCardinality, relationship.ToCardinality),
                CrossFilterDirection = MapCrossFilterDirection(relationship.CrossFilteringBehavior),
                Active = relationship.IsActive
            });
        }

        // Extract measures from all tables
        foreach (var table in database.Model.Tables)
        {
            foreach (var measure in table.Measures)
            {
                modelDef.Measures.Add(new MeasureDefinition
                {
                    Name = measure.Name,
                    Table = table.Name,
                    Expression = measure.Expression,
                    Description = measure.Description,
                    FormatString = measure.FormatString,
                    DisplayFolder = measure.DisplayFolder,
                    LineageTag = includeLineageTags ? measure.LineageTag : null
                });
            }
        }

        return modelDef;
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
