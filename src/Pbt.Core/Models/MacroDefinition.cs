namespace Pbt.Core.Models;

public class MacroDefinition
{
    public int Version { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<MacroTarget>? Targets { get; set; }
    public List<PipelineStep> Pipeline { get; set; } = new();
    public List<string>? Macros { get; set; }
    public MergeConfig? Merge { get; set; }
}

public class MacroTarget
{
    public MacroFiles Files { get; set; } = new();
}

public class MacroFiles
{
    public List<string> Include { get; set; } = new();
    public List<string> Exclude { get; set; } = new();
}

public class PipelineStep
{
    public string Id { get; set; } = string.Empty;
    public StepSelect Select { get; set; } = new();
    public StepReplace? Replace { get; set; }
    public StepTransform? Transform { get; set; }
}

public class StepSelect
{
    public string Path { get; set; } = string.Empty;
    public StepFilter? Filter { get; set; }
}

public class StepFilter
{
    public string Property { get; set; } = string.Empty; // Property name to filter on (e.g., "name")
    public string Pattern { get; set; } = string.Empty;  // Regex pattern to match
    public string? Flags { get; set; }                   // Regex flags like "i" for case-insensitive
}

public class StepReplace
{
    public string Kind { get; set; } = string.Empty; // "literal" or "regex"
    public string? Pattern { get; set; }  // for regex
    public string? From { get; set; }     // for literal
    public string With { get; set; } = string.Empty;
    public string? Flags { get; set; }    // regex flags like "gim"
}

public class StepTransform
{
    public string Kind { get; set; } = string.Empty; // "upper", "lower", "title", "trim", "collapse_whitespace"
}

public class MergeConfig
{
    public string Strategy { get; set; } = string.Empty; // "union", "intersection", "overwrite", "append"
    public string TargetNodes { get; set; } = string.Empty; // e.g., "columns[*]"
    public string Identifier { get; set; } = string.Empty; // e.g., "columns[*].source_column"
    public List<string>? Exclude { get; set; } // Optional exclusion patterns
}
