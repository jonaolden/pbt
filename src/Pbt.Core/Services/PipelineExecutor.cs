using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Pbt.Core.Models;
using YamlDotNet.RepresentationModel;

namespace Pbt.Core.Services;

public class PipelineExecutor
{
    private readonly YamlNodeSelector _selector;
    private readonly TableMerger _merger;

    public PipelineExecutor()
    {
        _selector = new YamlNodeSelector();
        _merger = new TableMerger();
    }

    public MacroExecutionResult ExecuteMacro(
        MacroDefinition macro,
        List<string> targetFiles,
        RunOperationArgs args)
    {
        // Check if this is a merge operation
        if (macro.Merge != null)
        {
            return ExecuteMergeOperation(macro, args);
        }

        var result = new MacroExecutionResult();

        foreach (var filePath in targetFiles)
        {
            try
            {
                var fileResult = ProcessFile(filePath, macro, args);
                result.FilesProcessed++;

                if (fileResult.NodesChanged > 0)
                {
                    result.FilesChanged++;
                    result.FileChangeCounts[filePath] = fileResult.NodesChanged;
                }

                result.NodesMatched += fileResult.NodesMatched;
                result.NodesChanged += fileResult.NodesChanged;
                result.Changes.AddRange(fileResult.Changes);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Error processing file '{filePath}': {ex.Message}", ex);
            }
        }

        return result;
    }

    private MacroExecutionResult ExecuteMergeOperation(
        MacroDefinition macro,
        RunOperationArgs args)
    {
        var result = new MacroExecutionResult();

        // Validate that source and target are provided
        if (string.IsNullOrWhiteSpace(args.Source))
        {
            throw new ArgumentException(
                "Source file path is required for merge operations. " +
                "Provide it via --args '{\"source\": \"path/to/source.yaml\"}'");
        }

        if (string.IsNullOrWhiteSpace(args.Target))
        {
            throw new ArgumentException(
                "Target file path is required for merge operations. " +
                "Provide it via --args '{\"target\": \"path/to/target.yaml\"}'");
        }

        // Resolve file paths (handle relative paths)
        var sourceFilePath = ResolveFilePath(args.Source, args.Path);
        var targetFilePath = ResolveFilePath(args.Target, args.Path);

        // Validate that files exist
        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException($"Source file not found: {sourceFilePath}");
        }

        if (!File.Exists(targetFilePath))
        {
            throw new FileNotFoundException($"Target file not found: {targetFilePath}");
        }

        // Perform merge
        var mergeResult = _merger.MergeFiles(
            sourceFilePath,
            targetFilePath,
            macro.Merge!,
            args.DryRun);

        // Convert MergeResult to MacroExecutionResult
        result.FilesProcessed = 2; // Source and target
        result.FilesChanged = mergeResult.HasChanges ? 1 : 0; // Only target file changes
        result.NodesMatched = mergeResult.SourceNodesMatched + mergeResult.TargetNodesMatched;
        result.NodesChanged = mergeResult.NodesAdded + mergeResult.NodesRemoved + mergeResult.NodesUpdated;

        if (mergeResult.HasChanges)
        {
            result.FileChangeCounts[targetFilePath] = result.NodesChanged;
        }

        // Convert merge changes to macro changes
        foreach (var change in mergeResult.Changes)
        {
            result.Changes.Add(new MacroChange
            {
                FilePath = targetFilePath,
                Path = "merge",
                StepId = "merge",
                Before = "",
                After = change
            });
        }

        return result;
    }

    private string ResolveFilePath(string filePath, string? basePath)
    {
        // If path is already absolute, return it
        if (Path.IsPathRooted(filePath))
        {
            return Path.GetFullPath(filePath);
        }

        // Otherwise, resolve relative to base path or current directory
        var baseDir = string.IsNullOrWhiteSpace(basePath)
            ? Directory.GetCurrentDirectory()
            : basePath;

        return Path.GetFullPath(Path.Combine(baseDir, filePath));
    }

    private FileExecutionResult ProcessFile(
        string filePath,
        MacroDefinition macro,
        RunOperationArgs args)
    {
        var result = new FileExecutionResult();

        // Load YAML as representation model
        using var reader = new StreamReader(filePath);
        var yaml = new YamlStream();
        yaml.Load(reader);

        if (yaml.Documents.Count == 0)
        {
            return result; // Empty file
        }

        if (yaml.Documents.Count > 1)
        {
            throw new InvalidOperationException(
                "Multi-document YAML files are not supported in v1. " +
                "Please use separate files for each document.");
        }

        var document = yaml.Documents[0];
        if (document.RootNode is not YamlMappingNode rootMapping)
        {
            // Root is not a mapping, skip
            return result;
        }

        // Track if any changes were made
        bool fileChanged = false;

        // Apply each pipeline step sequentially
        foreach (var step in macro.Pipeline)
        {
            var matches = _selector.SelectNodes(rootMapping, step.Select.Path, step.Select.Filter);
            result.NodesMatched += matches.Count;

            if (matches.Count == 0)
            {
                if (args.OnMissing == "error")
                {
                    throw new InvalidOperationException(
                        $"Step '{step.Id}' matched 0 nodes for path '{step.Select.Path}' in file '{filePath}'");
                }
                // Otherwise skip (on_missing = "skip")
                continue;
            }

            foreach (var match in matches)
            {
                var originalValue = match.Node.Value ?? string.Empty;
                var transformedValue = ApplyOperation(originalValue, step);

                if (originalValue != transformedValue)
                {
                    // Update the node value
                    match.Node.Value = transformedValue;
                    fileChanged = true;
                    result.NodesChanged++;

                    result.Changes.Add(new MacroChange
                    {
                        FilePath = filePath,
                        Path = match.Path,
                        StepId = step.Id,
                        Before = originalValue,
                        After = transformedValue
                    });
                }
            }
        }

        // Write back to file if changes were made and not dry run
        if (fileChanged && !args.DryRun)
        {
            using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
            yaml.Save(writer, assignAnchors: false);
        }

        return result;
    }

    private string ApplyOperation(string value, PipelineStep step)
    {
        if (step.Replace != null)
        {
            return ApplyReplace(value, step.Replace);
        }
        else if (step.Transform != null)
        {
            return ApplyTransform(value, step.Transform);
        }

        return value;
    }

    private string ApplyReplace(string value, StepReplace replace)
    {
        var kind = replace.Kind.ToLower();

        if (kind == "literal")
        {
            var from = replace.From ?? string.Empty;
            if (string.IsNullOrEmpty(from))
            {
                return value; // Empty 'from' would insert text between every character
            }
            var with = replace.With;
            return value.Replace(from, with);
        }
        else if (kind == "regex")
        {
            var pattern = replace.Pattern ?? string.Empty;
            var with = replace.With;
            var options = ParseRegexFlags(replace.Flags);

            return Regex.Replace(value, pattern, with, options);
        }

        return value;
    }

    private RegexOptions ParseRegexFlags(string? flags)
    {
        if (string.IsNullOrEmpty(flags))
        {
            return RegexOptions.None;
        }

        var options = RegexOptions.None;

        if (flags.Contains('i'))
            options |= RegexOptions.IgnoreCase;

        if (flags.Contains('m'))
            options |= RegexOptions.Multiline;

        if (flags.Contains('s'))
            options |= RegexOptions.Singleline;

        if (flags.Contains('x'))
            options |= RegexOptions.IgnorePatternWhitespace;

        return options;
    }

    private string ApplyTransform(string value, StepTransform transform)
    {
        var kind = transform.Kind.ToLower();

        return kind switch
        {
            "upper" => value.ToUpperInvariant(),
            "lower" => value.ToLowerInvariant(),
            "title" => ToTitleCase(value),
            "trim" => value.Trim(),
            "collapse_whitespace" => Regex.Replace(value.Trim(), @"\s+", " "),
            _ => value
        };
    }

    private string ToTitleCase(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var textInfo = CultureInfo.InvariantCulture.TextInfo;
        return textInfo.ToTitleCase(value.ToLowerInvariant());
    }
}

public class FileExecutionResult
{
    public int NodesMatched { get; set; }
    public int NodesChanged { get; set; }
    public List<MacroChange> Changes { get; set; } = new();
}
