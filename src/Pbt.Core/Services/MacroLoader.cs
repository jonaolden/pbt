using Pbt.Core.Infrastructure;
using Pbt.Core.Models;

namespace Pbt.Core.Services;

public class MacroLoader
{
    private readonly YamlSerializer _serializer;

    public MacroLoader(YamlSerializer serializer)
    {
        _serializer = serializer;
    }

    /// <summary>
    /// Load a macro from multiple paths with priority ordering
    /// Macros from earlier paths (higher priority) are found first
    /// </summary>
    /// <param name="macroPaths">List of macro directory paths, ordered by priority (first = highest)</param>
    /// <param name="macroName">Name of the macro to load</param>
    /// <returns>Loaded and validated macro definition</returns>
    public MacroDefinition LoadMacroFromPaths(List<string> macroPaths, string macroName)
    {
        if (macroPaths == null || macroPaths.Count == 0)
        {
            throw new ArgumentException("At least one macro path must be provided", nameof(macroPaths));
        }

        // Search paths in priority order
        foreach (var macrosPath in macroPaths)
        {
            if (!Directory.Exists(macrosPath)) continue;

            var yamlPath = Path.Combine(macrosPath, $"{macroName}.yaml");
            var ymlPath = Path.Combine(macrosPath, $"{macroName}.yml");

            string? macroFilePath = null;
            if (File.Exists(yamlPath))
            {
                macroFilePath = yamlPath;
            }
            else if (File.Exists(ymlPath))
            {
                macroFilePath = ymlPath;
            }

            if (macroFilePath != null)
            {
                var macro = _serializer.LoadFromFile<MacroDefinition>(macroFilePath);
                ValidateMacro(macro, macroFilePath);
                ResolveMacroPipeline(macro, macroPaths);
                return macro;
            }
        }

        throw new FileNotFoundException(
            $"Macro '{macroName}' not found in any configured macro paths:\n" +
            string.Join("\n", macroPaths.Select(p => $"  - {p}")));
    }

    /// <summary>
    /// Load a macro from project root (legacy method)
    /// </summary>
    public MacroDefinition LoadMacro(string projectRoot, string macroName)
    {
        var macrosPath = Path.Combine(projectRoot, "macros");

        if (!Directory.Exists(macrosPath))
        {
            throw new DirectoryNotFoundException(
                $"Macros directory not found at: {macrosPath}\n" +
                $"Expected structure: <project_root>/macros/");
        }

        // Try .yaml first, then .yml
        var yamlPath = Path.Combine(macrosPath, $"{macroName}.yaml");
        var ymlPath = Path.Combine(macrosPath, $"{macroName}.yml");

        string macroFilePath;
        if (File.Exists(yamlPath))
        {
            macroFilePath = yamlPath;
        }
        else if (File.Exists(ymlPath))
        {
            macroFilePath = ymlPath;
        }
        else
        {
            throw new FileNotFoundException(
                $"Macro '{macroName}' not found.\n" +
                $"Expected location: {yamlPath} or {ymlPath}");
        }

        var macro = _serializer.LoadFromFile<MacroDefinition>(macroFilePath);
        ValidateMacro(macro, macroFilePath);
        ResolveMacroPipeline(macro, new List<string> { macrosPath });
        return macro;
    }

    /// <summary>
    /// Resolve macro pipeline references by loading referenced macros
    /// </summary>
    private void ResolveMacroPipeline(MacroDefinition macro, List<string> macroPaths)
    {
        // If this macro doesn't reference other macros, nothing to do
        if (macro.Macros == null || macro.Macros.Count == 0)
        {
            return;
        }

        // Detect circular dependencies
        var visitedMacros = new HashSet<string> { macro.Name };
        
        // Load and merge referenced macros
        foreach (var referencedMacroName in macro.Macros)
        {
            var referencedMacro = LoadMacroFromPathsRecursive(
                macroPaths, 
                referencedMacroName, 
                visitedMacros);

            // Merge the referenced macro's pipeline steps into this macro
            if (referencedMacro.Pipeline != null && referencedMacro.Pipeline.Count > 0)
            {
                macro.Pipeline.AddRange(referencedMacro.Pipeline);
            }
        }
    }

    /// <summary>
    /// Recursively load a macro with circular dependency detection
    /// </summary>
    private MacroDefinition LoadMacroFromPathsRecursive(
        List<string> macroPaths, 
        string macroName, 
        HashSet<string> visitedMacros)
    {
        // Check for circular dependencies
        if (visitedMacros.Contains(macroName))
        {
            throw new InvalidOperationException(
                $"Circular macro dependency detected: {string.Join(" -> ", visitedMacros)} -> {macroName}");
        }

        // Search paths in priority order
        foreach (var macrosPath in macroPaths)
        {
            if (!Directory.Exists(macrosPath)) continue;

            var yamlPath = Path.Combine(macrosPath, $"{macroName}.yaml");
            var ymlPath = Path.Combine(macrosPath, $"{macroName}.yml");

            string? macroFilePath = null;
            if (File.Exists(yamlPath))
            {
                macroFilePath = yamlPath;
            }
            else if (File.Exists(ymlPath))
            {
                macroFilePath = ymlPath;
            }

            if (macroFilePath != null)
            {
                var macro = _serializer.LoadFromFile<MacroDefinition>(macroFilePath);
                ValidateMacro(macro, macroFilePath);
                
                // Add to visited set before resolving dependencies
                visitedMacros.Add(macroName);
                
                // Recursively resolve this macro's dependencies
                ResolveMacroPipelineRecursive(macro, macroPaths, visitedMacros);
                
                return macro;
            }
        }

        throw new FileNotFoundException(
            $"Referenced macro '{macroName}' not found in any configured macro paths:\n" +
            string.Join("\n", macroPaths.Select(p => $"  - {p}")));
    }

    /// <summary>
    /// Resolve macro pipeline references recursively with circular dependency detection
    /// </summary>
    private void ResolveMacroPipelineRecursive(
        MacroDefinition macro, 
        List<string> macroPaths, 
        HashSet<string> visitedMacros)
    {
        // If this macro doesn't reference other macros, nothing to do
        if (macro.Macros == null || macro.Macros.Count == 0)
        {
            return;
        }

        // Load and merge referenced macros
        foreach (var referencedMacroName in macro.Macros)
        {
            var referencedMacro = LoadMacroFromPathsRecursive(
                macroPaths, 
                referencedMacroName, 
                new HashSet<string>(visitedMacros));

            // Merge the referenced macro's pipeline steps into this macro
            if (referencedMacro.Pipeline != null && referencedMacro.Pipeline.Count > 0)
            {
                macro.Pipeline.AddRange(referencedMacro.Pipeline);
            }
        }
    }

    private void ValidateMacro(MacroDefinition macro, string filePath)
    {
        var errors = new List<string>();

        // Validate version
        if (macro.Version != 1)
        {
            errors.Add($"Unsupported macro version: {macro.Version}. Only version 1 is supported.");
        }

        // Validate name
        if (string.IsNullOrWhiteSpace(macro.Name))
        {
            errors.Add("Macro 'name' is required.");
        }

        // Validate that either pipeline or merge is specified (but not both)
        var hasPipeline = macro.Pipeline != null && macro.Pipeline.Count > 0;
        var hasMacros = macro.Macros != null && macro.Macros.Count > 0;
        var hasMerge = macro.Merge != null;

        // Count operation types specified
        var operationCount = (hasPipeline ? 1 : 0) + (hasMacros ? 1 : 0) + (hasMerge ? 1 : 0);

        if (operationCount == 0)
        {
            errors.Add("Either 'pipeline', 'macros', or 'merge' must be specified.");
        }
        else if (operationCount > 1)
        {
            errors.Add("Cannot specify multiple of 'pipeline', 'macros', and 'merge'. Choose one operation type.");
        }

        // Validate pipeline if present
        if (hasPipeline)
        {
            for (int i = 0; i < macro.Pipeline.Count; i++)
            {
                var step = macro.Pipeline[i];
                var stepErrors = ValidateStep(step, i);
                errors.AddRange(stepErrors);
            }
        }

        // Validate merge if present
        if (hasMerge)
        {
            var mergeErrors = ValidateMerge(macro.Merge!);
            errors.AddRange(mergeErrors);
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Macro validation failed for '{filePath}':\n" +
                string.Join("\n", errors.Select(e => $"  - {e}")));
        }
    }

    private List<string> ValidateStep(PipelineStep step, int index)
    {
        var errors = new List<string>();
        var stepPrefix = $"Step {index} (id: '{step.Id}')";

        // Validate id
        if (string.IsNullOrWhiteSpace(step.Id))
        {
            errors.Add($"{stepPrefix}: 'id' is required.");
        }

        // Validate select.path
        if (step.Select == null || string.IsNullOrWhiteSpace(step.Select.Path))
        {
            errors.Add($"{stepPrefix}: 'select.path' is required.");
        }

        // Validate that exactly one operation is specified
        var hasReplace = step.Replace != null;
        var hasTransform = step.Transform != null;

        if (!hasReplace && !hasTransform)
        {
            errors.Add($"{stepPrefix}: Must specify either 'replace' or 'transform'.");
        }
        else if (hasReplace && hasTransform)
        {
            errors.Add($"{stepPrefix}: Cannot specify both 'replace' and 'transform'.");
        }

        // Validate replace operation
        if (hasReplace && step.Replace != null)
        {
            var replaceErrors = ValidateReplace(step.Replace, stepPrefix);
            errors.AddRange(replaceErrors);
        }

        // Validate transform operation
        if (hasTransform && step.Transform != null)
        {
            var transformErrors = ValidateTransform(step.Transform, stepPrefix);
            errors.AddRange(transformErrors);
        }

        return errors;
    }

    private List<string> ValidateReplace(StepReplace replace, string stepPrefix)
    {
        var errors = new List<string>();

        var validKinds = new[] { "literal", "regex" };
        if (!validKinds.Contains(replace.Kind?.ToLower()))
        {
            errors.Add($"{stepPrefix}: replace.kind must be 'literal' or 'regex'.");
        }

        if (replace.Kind?.ToLower() == "regex")
        {
            if (string.IsNullOrWhiteSpace(replace.Pattern))
            {
                errors.Add($"{stepPrefix}: replace.pattern is required for regex kind.");
            }
        }
        else if (replace.Kind?.ToLower() == "literal")
        {
            if (string.IsNullOrWhiteSpace(replace.From))
            {
                errors.Add($"{stepPrefix}: replace.from is required for literal kind.");
            }
        }

        if (replace.With == null)
        {
            errors.Add($"{stepPrefix}: replace.with is required.");
        }

        return errors;
    }

    private List<string> ValidateTransform(StepTransform transform, string stepPrefix)
    {
        var errors = new List<string>();

        var validKinds = new[] { "upper", "lower", "title", "trim", "collapse_whitespace" };
        if (!validKinds.Contains(transform.Kind?.ToLower()))
        {
            errors.Add(
                $"{stepPrefix}: transform.kind must be one of: {string.Join(", ", validKinds)}.");
        }

        return errors;
    }

    private List<string> ValidateMerge(MergeConfig merge)
    {
        var errors = new List<string>();

        // Validate strategy
        var validStrategies = new[] { "union", "intersection", "overwrite", "append" };
        if (string.IsNullOrWhiteSpace(merge.Strategy) ||
            !validStrategies.Contains(merge.Strategy.ToLower()))
        {
            errors.Add(
                $"merge.strategy must be one of: {string.Join(", ", validStrategies)}.");
        }

        // Validate target_nodes
        if (string.IsNullOrWhiteSpace(merge.TargetNodes))
        {
            errors.Add("merge.target_nodes is required (e.g., 'columns[*]').");
        }

        // Validate identifier
        if (string.IsNullOrWhiteSpace(merge.Identifier))
        {
            errors.Add("merge.identifier is required (e.g., 'columns[*].source_column').");
        }

        return errors;
    }
}
