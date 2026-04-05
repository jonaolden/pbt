using Microsoft.AnalysisServices.Tabular;
using Pbt.Core.Infrastructure;
using Pbt.Core.Models;

namespace Pbt.Core.Services;

/// <summary>
/// Result of a build operation for a single model
/// </summary>
public sealed class ModelBuildResult
{
    public required Database Database { get; init; }
    public required string ModelFileName { get; init; }
    public required string ModelName { get; init; }
}

/// <summary>
/// Orchestrates the build pipeline: load models, resolve assets, validate, compose TOM databases.
/// Extracted from CLI BuildCommand to enable testing and reuse.
/// </summary>
public sealed class BuildService
{
    private readonly YamlSerializer _serializer;
    private readonly AssetLoader _assetLoader;
    private readonly Validator _validator;

    public BuildService(YamlSerializer serializer)
    {
        _serializer = serializer;
        _assetLoader = new AssetLoader(serializer);
        _validator = new Validator(serializer);
    }

    /// <summary>
    /// Execute the core build pipeline for all matching models in a project.
    /// </summary>
    public List<ModelBuildResult> Build(
        string projectPath,
        string? modelFilter,
        LineageManifestService? lineageService,
        EnvironmentDefinition? environment = null)
    {
        var modelFiles = _assetLoader.FindModelFiles(projectPath);

        if (modelFilter != null)
        {
            modelFiles = modelFiles.Where(f =>
                Path.GetFileNameWithoutExtension(f).Equals(modelFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (modelFiles.Count == 0)
                throw new FileNotFoundException($"Model '{modelFilter}' not found in models/ directory");
        }

        if (modelFiles.Count == 0)
            throw new FileNotFoundException($"No model files found in '{Path.Combine(projectPath, "models")}'");

        var results = new List<ModelBuildResult>();

        foreach (var modelFile in modelFiles)
        {
            var result = BuildModel(modelFile, projectPath, lineageService, environment);
            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// Build a single model from its YAML file path.
    /// </summary>
    public ModelBuildResult BuildModel(
        string modelFile,
        string projectPath,
        LineageManifestService? lineageService,
        EnvironmentDefinition? environment = null)
    {
        var modelDef = _serializer.LoadFromFile<ModelDefinition>(modelFile);
        var modelFileName = Path.GetFileNameWithoutExtension(modelFile);

        // Resolve asset paths
        var assetPaths = _assetLoader.ResolveAssetPaths(modelDef, projectPath);

        // Validate
        var validationResult = _validator.ValidateProjectWithAssets(modelDef, modelFile, assetPaths);
        if (!validationResult.IsValid)
            throw new InvalidOperationException($"Validation failed for model '{modelDef.Name}':\n{validationResult.FormatMessages()}");

        // Load table registry
        var registry = _assetLoader.CreateTableRegistry(assetPaths);

        // Scan for connector configs
        var connectorConfigs = ScanForConnectorConfigs(projectPath, registry);

        // Compose TOM database
        var composer = new ModelComposer(registry);
        foreach (var connector in connectorConfigs)
            composer.RegisterConnector(connector);

        var database = composer.ComposeModel(modelDef, lineageService, projectPath, environment);

        // Validate TOM model
        var tomResult = _validator.ValidateTomModel(database, modelDef.Name);
        if (!tomResult.IsValid)
            throw new InvalidOperationException($"TOM validation failed for '{modelDef.Name}':\n{tomResult.FormatMessages()}");

        return new ModelBuildResult
        {
            Database = database,
            ModelFileName = modelFileName,
            ModelName = modelDef.Name
        };
    }

    /// <summary>
    /// Load a named environment configuration from the environments/ directory.
    /// </summary>
    public EnvironmentDefinition LoadEnvironment(string projectPath, string envName)
    {
        var envDir = Path.Combine(projectPath, "environments");
        var envFile = Path.Combine(envDir, $"{envName}.env.yml");

        if (!File.Exists(envFile))
            envFile = Path.Combine(envDir, $"{envName}.yml");

        if (!File.Exists(envFile))
        {
            throw new FileNotFoundException(
                $"Environment '{envName}' not found. Expected file: {Path.Combine(envDir, $"{envName}.env.yml")}");
        }

        return _serializer.LoadFromFile<EnvironmentDefinition>(envFile);
    }

    /// <summary>
    /// Scan for source type config files and extract connector configurations.
    /// </summary>
    public List<ConnectorConfig> ScanForConnectorConfigs(string projectPath, TableRegistry registry)
    {
        var connectors = new List<ConnectorConfig>();
        var connectorNames = new HashSet<string>();

        var configFiles = Directory.GetFiles(projectPath, "*_config.yaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains(".pbiscaffold") && !f.Contains("scaffold_config"));

        foreach (var configFile in configFiles)
        {
            try
            {
                var sourceConfig = _serializer.LoadFromFile<SourceTypeConfig>(configFile);
                if (sourceConfig.Connector != null && !connectorNames.Contains(sourceConfig.Connector.Name))
                {
                    connectors.Add(sourceConfig.Connector);
                    connectorNames.Add(sourceConfig.Connector.Name);
                }
            }
            catch (YamlDotNet.Core.YamlException)
            {
                // Skip files that cannot be parsed as SourceTypeConfig
            }
            catch (IOException)
            {
                // Skip unreadable config files
            }
        }

        return connectors;
    }
}
