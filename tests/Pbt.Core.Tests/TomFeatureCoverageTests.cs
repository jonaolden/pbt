using Microsoft.AnalysisServices.Tabular;
using Pbt.Core.Infrastructure;
using Pbt.Core.Models;
using Pbt.Core.Services;

namespace Pbt.Core.Tests;

public class TomFeatureCoverageTests
{
    private const string FactYaml = @"
name: Fact
m_expression: 'let Source = 1 in Source'
alternate_source_precedence: 1
extended_properties: { owner: finance }
columns:
  - { name: Amount, type: Decimal, summarize_by: Sum }
  - { name: Key, type: Int64 }
measures:
  - name: Total
    table: Fact
    expression: SUM(Fact[Amount])
    format_string_expression: '""0.00""'
    detail_rows_expression: 'Fact'
    annotations: { a: b }
    kpi: { target_expression: '100', status_expression: '1', status_graphic: Traffic Light - Single }
";

    private const string AggYaml = @"
name: Agg
m_expression: 'let Source = 1 in Source'
columns:
  - name: Amount
    type: Decimal
    alternate_of: { summarization: Sum, base_column: Fact.Amount }
  - { name: Key, type: Int64, alternate_of: { summarization: GroupBy, base_column: Fact.Key } }
";

    [Fact]
    public void Compose_MapsAnnotationsKpiAggregationsAndOls()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(dir, "fact.yaml"), FactYaml);
        File.WriteAllText(Path.Combine(dir, "agg.yaml"), AggYaml);

        var serializer = new YamlSerializer();
        var registry = new TableRegistry(serializer);
        registry.LoadTables(dir);

        var modelDef = new ModelDefinition
        {
            Name = "M",
            CompatibilityLevel = 1702,
            Description = "desc",
            Annotations = new() { ["x"] = "y" },
            Tables = { new TableReference { Ref = "Fact" }, new TableReference { Ref = "Agg" } },
            Roles = new()
            {
                new RoleDefinition
                {
                    Name = "R",
                    TablePermissions =
                    {
                        new TablePermissionDefinition
                        {
                            Table = "Fact",
                            ColumnPermissions = new() { ["Amount"] = "None" }
                        }
                    }
                }
            }
        };

        var db = new ModelComposer(registry).ComposeModel(modelDef, projectRootPath: dir);
        var model = db.Model;

        Assert.Equal("desc", model.Description);
        Assert.Equal("y", model.Annotations["x"].Value);

        var fact = model.Tables["Fact"];
        Assert.Equal(1, fact.AlternateSourcePrecedence);
        Assert.Equal("finance", ((StringExtendedProperty)fact.ExtendedProperties["owner"]).Value);

        var total = fact.Measures["Total"];
        Assert.Equal("\"0.00\"", total.FormatStringDefinition.Expression);
        Assert.Equal("Fact", total.DetailRowsDefinition.Expression);
        Assert.Equal("b", total.Annotations["a"].Value);
        Assert.Equal("100", total.KPI.TargetExpression);

        var alt = model.Tables["Agg"].Columns["Amount"].AlternateOf;
        Assert.Equal(SummarizationType.Sum, alt.Summarization);
        Assert.Same(fact.Columns["Amount"], alt.BaseColumn);

        var ols = model.Roles["R"].TablePermissions["Fact"].ColumnPermissions[0];
        Assert.Equal(MetadataPermission.None, ols.MetadataPermission);

        // Deterministic tags without a manifest
        var db2 = new ModelComposer(registry).ComposeModel(modelDef, projectRootPath: dir);
        Assert.Equal(fact.LineageTag, db2.Model.Tables["Fact"].LineageTag);
    }

    [Fact]
    public void Compose_BuildsCalculatedQueryAndEntityPartitions()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(dir, "calc.yaml"), "name: Calc\ncalculated_expression: 'ROW(\"a\", 1)'\n");
        File.WriteAllText(Path.Combine(dir, "dq.yaml"),
            "name: Dq\npartitions:\n  - { name: Dq, mode: DirectQuery, data_source: Sql, query: 'select 1 as a' }\ncolumns:\n  - { name: a, type: Int64 }\n");
        File.WriteAllText(Path.Combine(dir, "dl.yaml"),
            "name: Dl\npartitions:\n  - { name: Dl, mode: DirectLake, entity_name: t, schema_name: dbo, expression_source: DatabaseQuery }\ncolumns:\n  - { name: a, type: Int64 }\n");

        var registry = new TableRegistry(new YamlSerializer());
        registry.LoadTables(dir);

        var modelDef = new ModelDefinition
        {
            Name = "M",
            CompatibilityLevel = 1702,
            Tables = { new TableReference { Ref = "Calc" }, new TableReference { Ref = "Dq" }, new TableReference { Ref = "Dl" } },
            DataSources = new() { new DataSourceDefinition { Name = "Sql", Protocol = "tds", Address = new() { ["server"] = "s", ["database"] = "d" } } },
            Expressions = new() { new ExpressionDefinition { Name = "DatabaseQuery", Expression = "let x = 1 in x" } }
        };

        var model = new ModelComposer(registry).ComposeModel(modelDef, projectRootPath: dir).Model;

        Assert.IsType<CalculatedPartitionSource>(model.Tables["Calc"].Partitions[0].Source);
        var dq = (QueryPartitionSource)model.Tables["Dq"].Partitions[0].Source;
        Assert.Same(model.DataSources["Sql"], dq.DataSource);
        var dl = (EntityPartitionSource)model.Tables["Dl"].Partitions[0].Source;
        Assert.Same(model.Expressions["DatabaseQuery"], dl.ExpressionSource);
        Assert.Equal(ModeType.DirectLake, model.Tables["Dl"].Partitions[0].Mode);
    }

    [Fact]
    public void Compose_BuildsFunctionsCulturesAndSerializesToTmdl()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(dir, "fact.yaml"), FactYaml);
        var registry = new TableRegistry(new YamlSerializer());
        registry.LoadTables(dir);

        var modelDef = new ModelDefinition
        {
            Name = "M",
            CompatibilityLevel = 1702,
            Tables = { new TableReference { Ref = "Fact" } },
            Functions = new() { new FunctionDefinition { Name = "AddOne", Expression = "(x) => x + 1" } },
            Cultures = new()
            {
                new CultureDefinition
                {
                    Name = "sv-SE",
                    Translations = { new TranslationDefinition { Table = "Fact", Object = "Amount", Property = "Caption", Value = "Belopp" } }
                }
            }
        };

        var db = new ModelComposer(registry).ComposeModel(modelDef, projectRootPath: dir);

        Assert.NotNull(db.Model.Functions.Find("AddOne"));
        Assert.Single(db.Model.Cultures["sv-SE"].ObjectTranslations);

        var outDir = Path.Combine(dir, "out");
        TmdlSerializer.SerializeDatabaseToFolder(db, outDir);
        Assert.True(File.Exists(Path.Combine(outDir, "tables", "Fact.tmdl")));
    }
}
