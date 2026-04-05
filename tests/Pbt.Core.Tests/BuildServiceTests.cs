using Pbt.Core.Infrastructure;
using Pbt.Core.Models;
using Pbt.Core.Services;

namespace Pbt.Core.Tests;

public class BuildServiceTests : IDisposable
{
    private readonly YamlSerializer _serializer = new();
    private readonly string _projectRoot;

    public BuildServiceTests()
    {
        // Use the example project if available
        _projectRoot = FindProjectRoot() ?? string.Empty;
    }

    public void Dispose() { }

    [Fact]
    public void Build_WithExampleProject_ShouldProduceResults()
    {
        if (string.IsNullOrEmpty(_projectRoot)) return;

        var examplePath = Path.Combine(_projectRoot, "examples", "sample_project");
        if (!Directory.Exists(examplePath)) return;

        var service = new BuildService(_serializer);
        var lineageService = new LineageManifestService(_serializer);

        var results = service.Build(examplePath, modelFilter: null, lineageService);

        Assert.Single(results);
        Assert.Equal("SalesAnalytics", results[0].ModelName);
        Assert.NotNull(results[0].Database);
        Assert.NotNull(results[0].Database.Model);
    }

    [Fact]
    public void Build_WithModelFilter_ShouldFilterCorrectly()
    {
        if (string.IsNullOrEmpty(_projectRoot)) return;

        var examplePath = Path.Combine(_projectRoot, "examples", "sample_project");
        if (!Directory.Exists(examplePath)) return;

        var service = new BuildService(_serializer);

        var results = service.Build(examplePath, modelFilter: "sales_model", lineageService: null);

        Assert.Single(results);
        Assert.Equal("SalesAnalytics", results[0].ModelName);
    }

    [Fact]
    public void Build_WithNonExistentFilter_ShouldThrow()
    {
        if (string.IsNullOrEmpty(_projectRoot)) return;

        var examplePath = Path.Combine(_projectRoot, "examples", "sample_project");
        if (!Directory.Exists(examplePath)) return;

        var service = new BuildService(_serializer);

        Assert.Throws<FileNotFoundException>(() =>
            service.Build(examplePath, modelFilter: "nonexistent", lineageService: null));
    }

    [Fact]
    public void Build_EmptyProject_ShouldThrow()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"build_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempPath);
        Directory.CreateDirectory(Path.Combine(tempPath, "models"));
        Directory.CreateDirectory(Path.Combine(tempPath, "tables"));

        try
        {
            var service = new BuildService(_serializer);
            Assert.Throws<FileNotFoundException>(() =>
                service.Build(tempPath, modelFilter: null, lineageService: null));
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void LoadEnvironment_NonExistentEnv_ShouldThrow()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"env_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempPath);

        try
        {
            var service = new BuildService(_serializer);
            Assert.Throws<FileNotFoundException>(() =>
                service.LoadEnvironment(tempPath, "production"));
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void ScanForConnectorConfigs_NoConfigs_ShouldReturnEmpty()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"connector_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempPath);

        try
        {
            var service = new BuildService(_serializer);
            var registry = new TableRegistry(_serializer);
            var result = service.ScanForConnectorConfigs(tempPath, registry);

            Assert.Empty(result);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    private string? FindProjectRoot()
    {
        var directory = Directory.GetCurrentDirectory();
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory, "pbicomposer.sln")))
                return directory;
            directory = Directory.GetParent(directory)?.FullName;
        }
        return null;
    }
}
