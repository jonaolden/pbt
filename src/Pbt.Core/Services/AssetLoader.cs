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
/// Service to resolve and load assets from model configuration.
/// Model files are discovered by convention (models/ subdirectory of the project root).
/// Each model carries its own asset configuration.
/// </summary>
public class AssetLoader
{
    private readonly YamlSerializer _serializer;

    public AssetLoader(YamlSerializer serializer)
    {
        _serializer = serializer;
    }

    /// <summary>
    /// Find all model files in the project by convention.
    /// Scans the models/ subdirectory of the given project path.
    /// </summary>
    /// <param name="projectPath">Path to the project root directory</param>
    /// <returns>List of model file paths</returns>
    public List<string> FindModelFiles(string projectPath)
    {
        var modelsPath = Path.Combine(projectPath, "models");
        if (!Directory.Exists(modelsPath))
        {
            return new List<string>();
        }

        return Directory.GetFiles(modelsPath, "*.yaml")
            .Concat(Directory.GetFiles(modelsPath, "*.yml"))
            .ToList();
    }

    /// <summary>
    /// Resolve asset paths from a model definition.
    /// If the model has an explicit assets configuration it is used; otherwise the
    /// convention-based layout (tables/ and macros/ relative to projectPath) is used.
    /// </summary>
    /// <param name="model">Model definition (may contain an assets section)</param>
    /// <param name="projectPath">Project root — used as the base for relative paths</param>
    /// <returns>Resolved asset paths</returns>
    public ResolvedAssetPaths ResolveAssetPaths(ModelDefinition model, string projectPath)
    {
        var resolved = new ResolvedAssetPaths();

        if (model.Assets != null && model.Assets.Count > 0)
        {
            // Process asset groups in order (first = highest priority)
            foreach (var (groupName, pathConfigs) in model.Assets)
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
                }
            }
        }
        else
        {
            // Convention-based defaults: look for tables/ and macros/ next to the project root
            var tablesPath = Path.Combine(projectPath, "tables");
            if (Directory.Exists(tablesPath))
            {
                resolved.TablePaths.Add(tablesPath);
            }

            var macrosPath = Path.Combine(projectPath, "macros");
            if (Directory.Exists(macrosPath))
            {
                resolved.MacroPaths.Add(macrosPath);
            }
        }

        // Resolve build configuration from model
        if (model.Builds != null)
        {
            resolved.BuildPath = ResolvePath(model.Builds.Path, projectPath);
            resolved.BuildIncludeSubdirectories = model.Builds.IncludeSubdirectories;
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
    /// Create a TableRegistry loaded with tables from all configured paths.
    /// Tables are loaded in priority order — higher priority tables override lower priority ones.
    /// </summary>
    public TableRegistry CreateTableRegistry(ResolvedAssetPaths assetPaths)
    {
        var registry = new TableRegistry(_serializer);
        registry.LoadTablesWithPriority(assetPaths.TablePaths);
        return registry;
    }

    /// <summary>
    /// Get all macro paths that can be searched for macros
    /// </summary>
    public List<string> GetMacroPaths(ResolvedAssetPaths assetPaths)
    {
        return assetPaths.MacroPaths
            .Where(Directory.Exists)
            .ToList();
    }
}
