namespace Pbt.Core.Models;

public class RunOperationArgs
{
    public string? Path { get; set; }
    public List<string>? Include { get; set; }
    public List<string>? Exclude { get; set; }
    public bool DryRun { get; set; } = false;
    public string OnMissing { get; set; } = "skip"; // "skip" or "error"
    public int PrintChangesLimit { get; set; } = 20;

    // Merge-specific arguments
    public string? Source { get; set; }
    public string? Target { get; set; }
}
