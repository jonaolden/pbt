using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Pbt.Core.Models;

namespace Pbt.Core.Services;

public class MacroFileResolver
{
    public List<string> ResolveTargetFiles(
        string projectPath,
        MacroDefinition macro,
        RunOperationArgs args)
    {
        // Determine the base path
        string basePath;
        bool userProvidedPath = !string.IsNullOrEmpty(args.Path);

        if (userProvidedPath)
        {
            basePath = Path.IsPathRooted(args.Path)
                ? args.Path
                : Path.Combine(Directory.GetCurrentDirectory(), args.Path);
        }
        else
        {
            basePath = projectPath;
        }

        // If basePath is a file, return it directly
        if (File.Exists(basePath))
        {
            return new List<string> { basePath };
        }

        // If basePath is a directory, glob for files
        if (!Directory.Exists(basePath))
        {
            throw new DirectoryNotFoundException($"Path not found: {basePath}");
        }

        // Determine include/exclude patterns
        var includePatterns = GetIncludePatterns(macro, args, userProvidedPath);
        var excludePatterns = GetExcludePatterns(macro, args);

        // Perform globbing
        var matcher = new Matcher();
        foreach (var pattern in includePatterns)
        {
            matcher.AddInclude(pattern);
        }
        foreach (var pattern in excludePatterns)
        {
            matcher.AddExclude(pattern);
        }

        var directoryInfo = new DirectoryInfoWrapper(new DirectoryInfo(basePath));
        var result = matcher.Execute(directoryInfo);

        var files = result.Files
            .Select(f => Path.Combine(basePath, f.Path))
            .ToList();

        if (files.Count == 0)
        {
            throw new InvalidOperationException(
                $"No files matched the specified patterns.\n" +
                $"Base path: {basePath}\n" +
                $"Include patterns: {string.Join(", ", includePatterns)}\n" +
                $"Exclude patterns: {string.Join(", ", excludePatterns)}");
        }

        return files;
    }

    private List<string> GetIncludePatterns(MacroDefinition macro, RunOperationArgs args, bool userProvidedPath)
    {
        // Priority: args.include > (if user provided path: simple patterns) > macro.targets.include > default
        if (args.Include != null && args.Include.Count > 0)
        {
            return args.Include;
        }

        // If user provided a specific path, use simple patterns to match files in that directory
        if (userProvidedPath)
        {
            return new List<string> { "**/*.yaml", "**/*.yml" };
        }

        if (macro.Targets != null && macro.Targets.Count > 0)
        {
            var patterns = macro.Targets
                .SelectMany(t => t.Files.Include)
                .ToList();

            if (patterns.Count > 0)
                return patterns;
        }

        // Default to models/**/*.yaml
        return new List<string> { "models/**/*.yaml", "models/**/*.yml" };
    }

    private List<string> GetExcludePatterns(MacroDefinition macro, RunOperationArgs args)
    {
        // Combine args.exclude and macro.targets.exclude
        var patterns = new List<string>();

        if (macro.Targets != null)
        {
            patterns.AddRange(macro.Targets.SelectMany(t => t.Files.Exclude));
        }

        if (args.Exclude != null)
        {
            patterns.AddRange(args.Exclude);
        }

        return patterns;
    }
}
