namespace DeadVault.Core.Models;

public class DiffResult
{
    public string CommitSha { get; set; } = string.Empty;
    public List<FileChange> Changes { get; set; } = new();
    public string PatchText { get; set; } = string.Empty;
    public SnapshotAttributionSummary Attribution { get; set; } = new();
}
