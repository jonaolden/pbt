namespace Pbt.Core.Models;

public class MacroChange
{
    public string FilePath { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string StepId { get; set; } = string.Empty;
    public string Before { get; set; } = string.Empty;
    public string After { get; set; } = string.Empty;
}

public class MacroExecutionResult
{
    public int FilesProcessed { get; set; }
    public int FilesChanged { get; set; }
    public int NodesMatched { get; set; }
    public int NodesChanged { get; set; }
    public List<MacroChange> Changes { get; set; } = new();
    public Dictionary<string, int> FileChangeCounts { get; set; } = new();
}
