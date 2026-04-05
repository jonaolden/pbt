using Pbt.Core.Infrastructure;
using Pbt.Core.Models;
using Pbt.Core.Services;

namespace Pbt.Core.Tests;

public class ValidatorTests
{
    private readonly YamlSerializer _serializer = new();

    [Fact]
    public void ValidateProject_ValidExampleProject_ShouldPass()
    {
        // Arrange
        var projectRoot = FindProjectRoot();
        if (projectRoot == null) return;

        var exampleProjectPath = Path.Combine(projectRoot, "examples", "sample_project");
        if (!Directory.Exists(exampleProjectPath)) return;

        var validator = new Validator(_serializer);

        // Act
        var result = validator.ValidateProject(exampleProjectPath);

        // Assert
        Assert.True(result.IsValid, result.FormatMessages());
    }

    [Fact]
    public void ValidateProject_MissingModelsDirectory_ShouldFail()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), $"validator_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempPath);
        Directory.CreateDirectory(Path.Combine(tempPath, "tables"));
        // Intentionally no models/ directory

        try
        {
            var validator = new Validator(_serializer);

            // Act
            var result = validator.ValidateProject(tempPath);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Message.Contains("models/"));
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    [Fact]
    public void ValidateTable_InvalidDataType_ShouldFail()
    {
        // Arrange
        var tempPath = CreateTestProject();

        try
        {
            var tableDef = new TableDefinition
            {
                Name = "TestTable",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "BadColumn", Type = "InvalidType" }
                }
            };

            var tablesPath = Path.Combine(tempPath, "tables");
            _serializer.SaveToFile(tableDef, Path.Combine(tablesPath, "test.yaml"));

            var validator = new Validator(_serializer);

            // Act
            var result = validator.ValidateProject(tempPath);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Message.Contains("unknown type") &&
                                                 e.Message.Contains("InvalidType"));
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    [Fact]
    public void ValidateTable_DuplicateColumnNames_ShouldFail()
    {
        // Arrange
        var tempPath = CreateTestProject();

        try
        {
            var tableDef = new TableDefinition
            {
                Name = "TestTable",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "ID", Type = "Int64" },
                    new() { Name = "ID", Type = "Int64" } // Duplicate
                }
            };

            var tablesPath = Path.Combine(tempPath, "tables");
            _serializer.SaveToFile(tableDef, Path.Combine(tablesPath, "test.yaml"));

            var validator = new Validator(_serializer);

            // Act
            var result = validator.ValidateProject(tempPath);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Message.Contains("Duplicate column"));
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    [Fact]
    public void ValidateHierarchy_InvalidColumnReference_ShouldFail()
    {
        // Arrange
        var tempPath = CreateTestProject();

        try
        {
            var tableDef = new TableDefinition
            {
                Name = "TestTable",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "Year", Type = "Int64" }
                },
                Hierarchies = new List<HierarchyDefinition>
                {
                    new()
                    {
                        Name = "Date Hierarchy",
                        Levels = new List<LevelDefinition>
                        {
                            new() { Name = "Year", Column = "NonExistentColumn" }
                        }
                    }
                }
            };

            var tablesPath = Path.Combine(tempPath, "tables");
            _serializer.SaveToFile(tableDef, Path.Combine(tablesPath, "test.yaml"));

            var validator = new Validator(_serializer);

            // Act
            var result = validator.ValidateProject(tempPath);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Message.Contains("references unknown column"));
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    [Fact]
    public void ValidateModel_InvalidTableReference_ShouldFail()
    {
        // Arrange
        var tempPath = CreateTestProject();

        try
        {
            // Create a valid table
            var tableDef = new TableDefinition
            {
                Name = "ValidTable",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "ID", Type = "Int64" }
                }
            };

            var tablesPath = Path.Combine(tempPath, "tables");
            _serializer.SaveToFile(tableDef, Path.Combine(tablesPath, "valid.yaml"));

            // Create model with invalid reference
            var modelDef = new ModelDefinition
            {
                Name = "TestModel",
                Tables = new List<TableReference>
                {
                    new() { Ref = "NonExistentTable" }
                }
            };

            var modelsPath = Path.Combine(tempPath, "models");
            _serializer.SaveToFile(modelDef, Path.Combine(modelsPath, "model.yaml"));

            var validator = new Validator(_serializer);

            // Act
            var result = validator.ValidateProject(tempPath);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Message.Contains("not found in registry"));
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    [Fact]
    public void ValidateRelationship_InvalidColumn_ShouldFail()
    {
        // Arrange
        var tempPath = CreateTestProject();

        try
        {
            // Create tables
            var table1 = new TableDefinition
            {
                Name = "Table1",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "ID", Type = "Int64" }
                }
            };

            var table2 = new TableDefinition
            {
                Name = "Table2",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "ID", Type = "Int64" }
                }
            };

            var tablesPath = Path.Combine(tempPath, "tables");
            _serializer.SaveToFile(table1, Path.Combine(tablesPath, "table1.yaml"));
            _serializer.SaveToFile(table2, Path.Combine(tablesPath, "table2.yaml"));

            // Create model with invalid relationship
            var modelDef = new ModelDefinition
            {
                Name = "TestModel",
                Tables = new List<TableReference>
                {
                    new() { Ref = "Table1" },
                    new() { Ref = "Table2" }
                },
                Relationships = new List<RelationshipDefinition>
                {
                    new()
                    {
                        FromTable = "Table1",
                        FromColumn = "NonExistentColumn",
                        ToTable = "Table2",
                        ToColumn = "ID"
                    }
                }
            };

            var modelsPath = Path.Combine(tempPath, "models");
            _serializer.SaveToFile(modelDef, Path.Combine(modelsPath, "model.yaml"));

            var validator = new Validator(_serializer);

            // Act
            var result = validator.ValidateProject(tempPath);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Message.Contains("from_column") &&
                                                 e.Message.Contains("not found"));
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    [Fact]
    public void ValidateMeasure_UnbalancedBrackets_ShouldFail()
    {
        // Arrange
        var tempPath = CreateTestProject();

        try
        {
            var tableDef = new TableDefinition
            {
                Name = "Sales",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "Amount", Type = "Decimal" }
                }
            };

            var tablesPath = Path.Combine(tempPath, "tables");
            _serializer.SaveToFile(tableDef, Path.Combine(tablesPath, "sales.yaml"));

            var modelDef = new ModelDefinition
            {
                Name = "TestModel",
                Tables = new List<TableReference>
                {
                    new() { Ref = "Sales" }
                },
                Measures = new List<MeasureDefinition>
                {
                    new()
                    {
                        Name = "Total Sales",
                        Table = "Sales",
                        Expression = "SUM(Sales[Amount" // Missing closing bracket
                    }
                }
            };

            var modelsPath = Path.Combine(tempPath, "models");
            _serializer.SaveToFile(modelDef, Path.Combine(modelsPath, "model.yaml"));

            var validator = new Validator(_serializer);

            // Act
            var result = validator.ValidateProject(tempPath);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Message.Contains("unbalanced brackets"));
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    [Fact]
    public void ValidateModel_InvalidCardinality_ShouldFail()
    {
        // Arrange
        var tempPath = CreateTestProject();

        try
        {
            var table1 = new TableDefinition
            {
                Name = "Table1",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "ID", Type = "Int64" }
                }
            };

            var table2 = new TableDefinition
            {
                Name = "Table2",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "ID", Type = "Int64" }
                }
            };

            var tablesPath = Path.Combine(tempPath, "tables");
            _serializer.SaveToFile(table1, Path.Combine(tablesPath, "table1.yaml"));
            _serializer.SaveToFile(table2, Path.Combine(tablesPath, "table2.yaml"));

            var modelDef = new ModelDefinition
            {
                Name = "TestModel",
                Tables = new List<TableReference>
                {
                    new() { Ref = "Table1" },
                    new() { Ref = "Table2" }
                },
                Relationships = new List<RelationshipDefinition>
                {
                    new()
                    {
                        FromTable = "Table1",
                        FromColumn = "ID",
                        ToTable = "Table2",
                        ToColumn = "ID",
                        Cardinality = "InvalidCardinality"
                    }
                }
            };

            var modelsPath = Path.Combine(tempPath, "models");
            _serializer.SaveToFile(modelDef, Path.Combine(modelsPath, "model.yaml"));

            var validator = new Validator(_serializer);

            // Act
            var result = validator.ValidateProject(tempPath);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Message.Contains("Unknown cardinality"));
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    [Fact]
    public void ValidationResult_FormatMessages_ShouldBeReadable()
    {
        // Arrange
        var result = new ValidationResult();
        result.AddError("Test error", "file.yaml", "Context info", "Try this");
        result.AddWarning("Test warning", "another.yaml");

        // Act
        var formatted = result.FormatMessages();

        // Assert
        Assert.Contains("1 error(s)", formatted);
        Assert.Contains("Warnings (1)", formatted);
        Assert.Contains("Test error", formatted);
        Assert.Contains("Test warning", formatted);
        Assert.Contains("file.yaml", formatted);
    }

    [Fact]
    public void ValidateProject_NoTables_ShouldWarn()
    {
        // Arrange
        var tempPath = CreateTestProject();

        try
        {
            // Create model with no tables
            var modelDef = new ModelDefinition
            {
                Name = "EmptyModel",
                Tables = new List<TableReference>()
            };

            var modelsPath = Path.Combine(tempPath, "models");
            _serializer.SaveToFile(modelDef, Path.Combine(modelsPath, "model.yaml"));

            var validator = new Validator(_serializer);

            // Act
            var result = validator.ValidateProject(tempPath);

            // Assert - Should have warnings about no tables
            Assert.True(result.HasWarnings || result.Errors.Count > 0);
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    [Fact]
    public void ValidateRelationship_CrossFilterDirectionNone_ShouldFail()
    {
        // Arrange
        var tempPath = CreateTestProject();

        try
        {
            var table1 = new TableDefinition
            {
                Name = "Table1",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "ID", Type = "Int64" }
                }
            };

            var table2 = new TableDefinition
            {
                Name = "Table2",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "ID", Type = "Int64" }
                }
            };

            var tablesPath = Path.Combine(tempPath, "tables");
            _serializer.SaveToFile(table1, Path.Combine(tablesPath, "table1.yaml"));
            _serializer.SaveToFile(table2, Path.Combine(tablesPath, "table2.yaml"));

            var modelDef = new ModelDefinition
            {
                Name = "TestModel",
                Tables = new List<TableReference>
                {
                    new() { Ref = "Table1" },
                    new() { Ref = "Table2" }
                },
                Relationships = new List<RelationshipDefinition>
                {
                    new()
                    {
                        FromTable = "Table1",
                        FromColumn = "ID",
                        ToTable = "Table2",
                        ToColumn = "ID",
                        CrossFilterDirection = "None"
                    }
                }
            };

            var modelsPath = Path.Combine(tempPath, "models");
            _serializer.SaveToFile(modelDef, Path.Combine(modelsPath, "model.yaml"));

            var validator = new Validator(_serializer);

            // Act
            var result = validator.ValidateProject(tempPath);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Message.Contains("None") &&
                                                 e.Message.Contains("not supported"));
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    [Fact]
    public void ValidateColumn_InvalidSummarizeBy_ShouldFail()
    {
        // Arrange
        var tempPath = CreateTestProject();

        try
        {
            var tableDef = new TableDefinition
            {
                Name = "TestTable",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "Amount", Type = "Decimal", SummarizeBy = "InvalidAgg" }
                }
            };

            var tablesPath = Path.Combine(tempPath, "tables");
            _serializer.SaveToFile(tableDef, Path.Combine(tablesPath, "test.yaml"));

            var validator = new Validator(_serializer);

            // Act
            var result = validator.ValidateProject(tempPath);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Message.Contains("summarize_by") &&
                                                 e.Message.Contains("InvalidAgg"));
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    [Fact]
    public void ValidateColumn_InvalidSortByColumn_ShouldFail()
    {
        // Arrange
        var tempPath = CreateTestProject();

        try
        {
            var tableDef = new TableDefinition
            {
                Name = "TestTable",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "MonthName", Type = "String", SortByColumn = "NonExistentColumn" }
                }
            };

            var tablesPath = Path.Combine(tempPath, "tables");
            _serializer.SaveToFile(tableDef, Path.Combine(tablesPath, "test.yaml"));

            var validator = new Validator(_serializer);

            // Act
            var result = validator.ValidateProject(tempPath);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Message.Contains("sort_by_column") &&
                                                 e.Message.Contains("NonExistentColumn"));
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    [Fact]
    public void ValidateColumn_ValidSortByColumn_ShouldPass()
    {
        // Arrange
        var tempPath = CreateTestProject();

        try
        {
            var tableDef = new TableDefinition
            {
                Name = "TestTable",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "MonthNum", Type = "Int64" },
                    new() { Name = "MonthName", Type = "String", SortByColumn = "MonthNum" }
                }
            };

            var tablesPath = Path.Combine(tempPath, "tables");
            _serializer.SaveToFile(tableDef, Path.Combine(tablesPath, "test.yaml"));

            // Need a model that references this table
            var modelDef = new ModelDefinition
            {
                Name = "TestModel",
                Tables = new List<TableReference> { new() { Ref = "TestTable" } }
            };

            var modelsPath = Path.Combine(tempPath, "models");
            _serializer.SaveToFile(modelDef, Path.Combine(modelsPath, "model.yaml"));

            var validator = new Validator(_serializer);

            // Act
            var result = validator.ValidateProject(tempPath);

            // Assert
            Assert.True(result.IsValid, result.FormatMessages());
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    [Fact]
    public void ValidatePartition_InvalidMode_ShouldFail()
    {
        // Arrange
        var tempPath = CreateTestProject();

        try
        {
            var tableDef = new TableDefinition
            {
                Name = "TestTable",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "ID", Type = "Int64" }
                },
                Partitions = new List<PartitionDefinition>
                {
                    new() { Name = "Part1", Mode = "InvalidMode", MExpression = "let Source = ... in Source" }
                }
            };

            var tablesPath = Path.Combine(tempPath, "tables");
            _serializer.SaveToFile(tableDef, Path.Combine(tablesPath, "test.yaml"));

            var validator = new Validator(_serializer);

            // Act
            var result = validator.ValidateProject(tempPath);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Message.Contains("InvalidMode"));
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    [Fact]
    public void ValidatePartition_MissingName_ShouldFail()
    {
        // Arrange
        var tempPath = CreateTestProject();

        try
        {
            var tableDef = new TableDefinition
            {
                Name = "TestTable",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "ID", Type = "Int64" }
                },
                Partitions = new List<PartitionDefinition>
                {
                    new() { Mode = "Import", MExpression = "let Source = ... in Source" }
                }
            };

            var tablesPath = Path.Combine(tempPath, "tables");
            _serializer.SaveToFile(tableDef, Path.Combine(tablesPath, "test.yaml"));

            var validator = new Validator(_serializer);

            // Act
            var result = validator.ValidateProject(tempPath);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Message.Contains("Partition name is required"));
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    private string CreateTestProject()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"validator_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempPath);
        Directory.CreateDirectory(Path.Combine(tempPath, "tables"));
        Directory.CreateDirectory(Path.Combine(tempPath, "models"));

        // Create a minimal model file so ValidateProject doesn't short-circuit
        var modelDef = new ModelDefinition
        {
            Name = "TestModel",
            Tables = new List<TableReference> { new() { Ref = "TestTable" } }
        };
        _serializer.SaveToFile(modelDef, Path.Combine(tempPath, "models", "model.yaml"));

        return tempPath;
    }

    private string? FindProjectRoot()
    {
        var directory = Directory.GetCurrentDirectory();
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory, "pbicomposer.sln")))
            {
                return directory;
            }
            directory = Directory.GetParent(directory)?.FullName;
        }
        return null;
    }
}
