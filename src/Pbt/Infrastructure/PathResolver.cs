namespace Pbt.Infrastructure;

/// <summary>
/// Resolves CLI input paths to a project root directory and optional model file filter.
/// Supports both directory paths (conventional) and direct model file paths.
///
/// Examples:
///   "."                        → projectRoot = ".",            modelFilter = null
///   "my_project"               → projectRoot = "my_project",  modelFilter = null
///   "models/sales_model.yaml"  → projectRoot = ".",           modelFilter = "sales_model"
///   "/abs/path/to/model.yml"   → projectRoot = "/abs/path",   modelFilter = "model"
/// </summary>
public static class PathResolver
{
    /// <summary>
    /// Resolve an input path to a project root and optional model name filter.
    /// If the input is a YAML file inside a models/ directory, the project root
    /// is inferred as the parent of models/, and the file name becomes a model filter.
    /// </summary>
    public static (string ProjectRoot, string? ModelFilter) Resolve(string inputPath)
    {
        // If the path is a directory, use it directly
        if (Directory.Exists(inputPath))
        {
            return (inputPath, null);
        }

        // If the path is a YAML file, derive project root from its location
        if (IsYamlFile(inputPath) && File.Exists(inputPath))
        {
            var fullPath = Path.GetFullPath(inputPath);
            var directory = Path.GetDirectoryName(fullPath)!;
            var directoryName = Path.GetFileName(directory);
            var modelFilter = Path.GetFileNameWithoutExtension(fullPath);

            // If the file is inside a models/ directory, project root is one level up
            if (directoryName.Equals("models", StringComparison.OrdinalIgnoreCase))
            {
                var projectRoot = Path.GetDirectoryName(directory)!;
                return (projectRoot, modelFilter);
            }

            // YAML file not in models/ — treat its directory as project root
            // and the file as a model definition (user may have a flat layout)
            return (directory, modelFilter);
        }

        // Path doesn't exist — could be a typo or not-yet-created.
        // Check if it looks like a file path (has a YAML extension).
        if (IsYamlFile(inputPath))
        {
            throw new FileNotFoundException(
                $"Model file not found: {inputPath}");
        }

        // Doesn't look like a file — treat as a missing directory
        throw new DirectoryNotFoundException(
            $"Project directory not found: {inputPath}");
    }

    private static bool IsYamlFile(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".yaml", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".yml", StringComparison.OrdinalIgnoreCase);
    }
}
