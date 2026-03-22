using Pbt.Core.Infrastructure;
using Pbt.Core.Models;
using Pbt.Core.Services;

namespace Pbt.Core.Tests;

public class LineageManifestServiceTests
{
    private readonly YamlSerializer _serializer = new();

    [Fact]
    public void GetOrGenerateTableTag_FirstTime_ShouldGenerateDeterministicTag()
    {
        // Arrange
        var service = new LineageManifestService(_serializer);

        // Act
        var tag1 = service.GetOrGenerateTableTag("Sales");
        var tag2 = service.GetOrGenerateTableTag("Sales");

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(tag1));
        Assert.Equal(tag1, tag2); // Same table name should produce same tag
        Assert.Equal(1, service.NewTagCount);
    }

    [Fact]
    public void GetOrGenerateColumnTag_SameColumnDifferentTables_ShouldGenerateDifferentTags()
    {
        // Arrange
        var service = new LineageManifestService(_serializer);

        // Act
        var tag1 = service.GetOrGenerateColumnTag("Sales", "ID");
        var tag2 = service.GetOrGenerateColumnTag("Customers", "ID");

        // Assert
        Assert.NotEqual(tag1, tag2); // Same column name but different tables
        Assert.Equal(2, service.NewTagCount);
    }

    [Fact]
    public void SaveAndLoadManifest_ShouldPersistTags()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), $"lineage_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempPath);

        try
        {
            var service1 = new LineageManifestService(_serializer);

            // Generate some tags
            var tableTag = service1.GetOrGenerateTableTag("Sales");
            var columnTag = service1.GetOrGenerateColumnTag("Sales", "Amount");
            var measureTag = service1.GetOrGenerateMeasureTag("Sales", "Total Sales");

            // Save
            service1.SaveManifest(tempPath);

            // Act - Load in new service
            var service2 = new LineageManifestService(_serializer);
            service2.LoadManifest(tempPath);

            var loadedTableTag = service2.GetOrGenerateTableTag("Sales");
            var loadedColumnTag = service2.GetOrGenerateColumnTag("Sales", "Amount");
            var loadedMeasureTag = service2.GetOrGenerateMeasureTag("Sales", "Total Sales");

            // Assert
            Assert.Equal(tableTag, loadedTableTag);
            Assert.Equal(columnTag, loadedColumnTag);
            Assert.Equal(measureTag, loadedMeasureTag);
            Assert.Equal(0, service2.NewTagCount); // No new tags generated
            Assert.Equal(3, service2.ExistingTagCount);
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
    public void DeterministicTags_SameInputs_ShouldProduceSameTags()
    {
        // Arrange
        var service1 = new LineageManifestService(_serializer);
        var service2 = new LineageManifestService(_serializer);

        // Act
        var tag1 = service1.GetOrGenerateTableTag("Sales");
        var tag2 = service2.GetOrGenerateTableTag("Sales");

        // Assert
        Assert.Equal(tag1, tag2); // Deterministic generation
    }

    [Fact]
    public void CleanOrphanedTags_RemovedColumn_ShouldRemoveTag()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), $"lineage_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempPath);

        try
        {
            var service = new LineageManifestService(_serializer);

            // Generate tags
            service.GetOrGenerateTableTag("Sales");
            service.GetOrGenerateColumnTag("Sales", "OldColumn");
            service.GetOrGenerateColumnTag("Sales", "CurrentColumn");

            // Create registry with only CurrentColumn
            var tableDef = new TableDefinition
            {
                Name = "Sales",
                Columns = new List<ColumnDefinition>
                {
                    new() { Name = "CurrentColumn", Type = "String" }
                }
            };

            var tablesPath = Path.Combine(tempPath, "tables");
            Directory.CreateDirectory(tablesPath);
            _serializer.SaveToFile(tableDef, Path.Combine(tablesPath, "sales.yaml"));

            var registry = new TableRegistry(_serializer);
            registry.LoadTables(tablesPath);

            // Act
            var removedCount = service.CleanOrphanedTags(registry, new List<ModelDefinition>());

            // Assert
            Assert.Equal(1, removedCount); // OldColumn tag removed
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
    public void Clear_ShouldRemoveAllTags()
    {
        // Arrange
        var service = new LineageManifestService(_serializer);
        service.GetOrGenerateTableTag("Sales");
        service.GetOrGenerateColumnTag("Sales", "Amount");
        Assert.Equal(2, service.NewTagCount);

        // Act
        service.Clear();

        // Assert
        Assert.Equal(0, service.NewTagCount);
        Assert.Equal(0, service.ExistingTagCount);
    }

    [Fact]
    public void GetTables_ShouldReturnAllTableNames()
    {
        // Arrange
        var service = new LineageManifestService(_serializer);
        service.GetOrGenerateTableTag("Sales");
        service.GetOrGenerateTableTag("Customers");
        service.GetOrGenerateColumnTag("Products", "ProductID");

        // Act
        var tables = service.GetTables().ToList();

        // Assert
        Assert.Equal(3, tables.Count);
        Assert.Contains("Sales", tables);
        Assert.Contains("Customers", tables);
        Assert.Contains("Products", tables);
    }

    [Fact]
    public void CollisionWarnings_ShouldBeEmptyByDefault()
    {
        // Arrange
        var service = new LineageManifestService(_serializer);

        // Act
        service.GetOrGenerateTableTag("Sales");
        service.GetOrGenerateColumnTag("Sales", "Amount");

        // Assert - no collisions expected with normal usage
        Assert.Empty(service.CollisionWarnings);
    }

    [Fact]
    public void LoadManifest_FileDoesNotExist_ShouldCreateEmptyManifest()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), $"lineage_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempPath);

        try
        {
            var service = new LineageManifestService(_serializer);

            // Act
            service.LoadManifest(tempPath); // Manifest file doesn't exist yet

            // Assert - Should not throw, creates empty manifest
            Assert.Equal(0, service.NewTagCount);
            Assert.Equal(0, service.ExistingTagCount);
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }
}
