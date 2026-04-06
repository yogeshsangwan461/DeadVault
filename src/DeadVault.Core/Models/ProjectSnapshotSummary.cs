namespace DeadVault.Core.Models;

public class ProjectSnapshotSummary
{
    public int SnapshotCount { get; set; }
    public string? LatestVersion { get; set; }
    public string? LatestMessage { get; set; }
    public DateTime? LatestTimestamp { get; set; }
}
