using Pbt.Core.Infrastructure;
using Pbt.Core.Models;

namespace Pbt.Core.Services;

/// <summary>
/// Represents the resolved asset paths for a project, organized by asset type
/// </summary>
public class ResolvedAssetPaths
{
    /// <summary>
    /// Table paths ordered by priority (first = highest)
    /// </summary>
    public List<string> TablePaths { get; set; } = new();

    /// <summary>
    /// Macro paths ordered by priority (first = highest)
    /// </summary>
    public List<string> MacroPaths { get; set; } = new();

    /// <summary>
    /// Model paths ordered by priority (first = highest)
    /// </summary>
    public List<string> ModelPaths { get; set; } = new();

    /// <summary>
    /// Build output path
    /// </summary>
    public string? BuildPath { get; set; }

    /// <summary>
    /// Whether to include subdirectories in build output
    /// </summary>
    public bool BuildIncludeSubdirectories { get; set; } = true;
}

/// <summary>
/// Service to resolve and load assets from project configuration
/// Handles priority-based asset loading from multiple configured paths
/// </summary>
public class AssetLoader
{
    private readonly YamlSerializer _serializer;

    public AssetLoader(YamlSerializer serializer)
    {
        _serializer = serializer;
    }

    /// <summary>
    /// Load project definition and resolve all asset paths
    /// </summary>
    /// <param name="projectPath">Path to project directory containing project.yml</param>
    /// <returns>Project definition and resolved asset paths</returns>
    public (ProjectDefinition Project, ResolvedAssetPaths AssetPaths) LoadProject(string projectPath)
    {
        var projectYamlPath = Path.Combine(projectPath, "project.yml");
        if (!File.Exists(projectYamlPath))
        {
            throw new FileNotFoundException("project.yml not found in project directory", projectYamlPath);
        }

        var project = _serializer.LoadFromFile<ProjectDefinition>(projectYamlPath);
        var assetPaths = ResolveAssetPaths(project, projectPath);

        return (project, assetPaths);
    }

    /// <summary>
    /// Resolve asset paths from project configuration
    /// </summary>
    /// <param name="project">Project definition</param>
    /// <param name="projectPath">Path to the project directory, used to resolve relative paths</param>
    /// <returns>Resolved asset paths ordered by priority</returns>
    public ResolvedAssetPaths ResolveAssetPaths(ProjectDefinition project, string projectPath)
    {
        var resolved = new ResolvedAssetPaths();

        if (project.Assets == null || project.Assets.Count == 0)
        {
            throw new InvalidOperationException(
                "Project 'assets' configuration is required. " +
                "Define at least one asset group with table, macro, or model paths.");
        }

        // Process asset groups in order (first = highest priority)
        foreach (var (groupName, pathConfigs) in project.Assets)
        {
            if (pathConfigs == null) continue;

            foreach (var config in pathConfigs)
            {
                // Handle 'path' - includes all asset types from subdirectories
                if (!string.IsNullOrWhiteSpace(config.Path))
                {
                    var basePath = ResolvePath(config.Path, projectPath);

                    if (!Directory.Exists(basePath))
                    {
                        Console.WriteLine($"Warning: Asset path '{config.Path}' (group '{groupName}') does not exist, skipping");
                    }
                    else
                    {
                        var tablesPath = Path.Combine(basePath, "tables");
                        if (Directory.Exists(tablesPath))
                        {
                            resolved.TablePaths.Add(tablesPath);
                        }

                        var macrosPath = Path.Combine(basePath, "macros");
                        if (Directory.Exists(macrosPath))
                        {
                            resolved.MacroPaths.Add(macrosPath);
                        }

                        var modelsPath = Path.Combine(basePath, "models");
                        if (Directory.Exists(modelsPath))
                        {
                            resolved.ModelPaths.Add(modelsPath);
                        }
                    }
                }

                // Handle specific asset type paths
                if (!string.IsNullOrWhiteSpace(config.Tables))
                {
                    var tablesPath = ResolvePath(config.Tables, projectPath);
                    if (Directory.Exists(tablesPath))
                    {
                        resolved.TablePaths.Add(tablesPath);
                    }
                    else
                    {
                        throw new DirectoryNotFoundException(
                            $"Tables path not found for group '{groupName}': {tablesPath}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(config.Macros))
                {
                    var macrosPath = ResolvePath(config.Macros, projectPath);
                    if (Directory.Exists(macrosPath))
                    {
                        resolved.MacroPaths.Add(macrosPath);
                    }
                    else
                    {
                        throw new DirectoryNotFoundException(
                            $"Macros path not found for group '{groupName}': {macrosPath}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(config.Models))
                {
                    var modelsPath = ResolvePath(config.Models, projectPath);
                    if (Directory.Exists(modelsPath))
                    {
                        resolved.ModelPaths.Add(modelsPath);
                    }
                    else
                    {
                        throw new DirectoryNotFoundException(
                            $"Models path not found for group '{groupName}': {modelsPath}");
                    }
                }
            }
        }

        // Resolve build configuration
        if (project.Builds != null)
        {
            resolved.BuildPath = ResolvePath(project.Builds.Path, projectPath);
            resolved.BuildIncludeSubdirectories = project.Builds.IncludeSubdirectories;
        }

        return resolved;
    }

    /// <summary>
    /// Resolve a path relative to the project directory if it is not absolute
    /// </summary>
    private static string ResolvePath(string path, string projectPath)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        return Path.Combine(projectPath, path);
    }

    /// <summary>
    /// Create a TableRegistry loaded with tables from all configured paths
    /// Tables are loaded in priority order - higher priority tables override lower priority
    /// </summary>
    /// <param name="assetPaths">Resolved asset paths</param>
    /// <returns>TableRegistry with all tables loaded</returns>
    public TableRegistry CreateTableRegistry(ResolvedAssetPaths assetPaths)
    {
        var registry = new TableRegistry(_serializer);
        registry.LoadTablesWithPriority(assetPaths.TablePaths);
        return registry;
    }

    /// <summary>
    /// Get all model files from configured paths
    /// Models are returned in priority order - higher priority first
    /// </summary>
    /// <param name="assetPaths">Resolved asset paths</param>
    /// <returns>List of model file paths ordered by priority</returns>
    public List<string> GetModelFiles(ResolvedAssetPaths assetPaths)
    {
        var modelFiles = new List<string>();
        var seenModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var modelsPath in assetPaths.ModelPaths)
        {
            if (!Directory.Exists(modelsPath)) continue;

            var files = Directory.GetFiles(modelsPath, "*.yaml")
                .Concat(Directory.GetFiles(modelsPath, "*.yml"))
                .ToList();

            foreach (var file in files)
            {
                var modelName = Path.GetFileNameWithoutExtension(file);
                
                // Higher priority paths are processed first, skip duplicates
                if (seenModels.Add(modelName))
                {
                    modelFiles.Add(file);
                }
            }
        }

        return modelFiles;
    }

    /// <summary>
    /// Get all macro paths that can be searched for macros
    /// </summary>
    /// <param name="assetPaths">Resolved asset paths</param>
    /// <returns>List of macro directory paths ordered by priority</returns>
    public List<string> GetMacroPaths(ResolvedAssetPaths assetPaths)
    {
        return assetPaths.MacroPaths
            .Where(Directory.Exists)
            .ToList();
    }
}
