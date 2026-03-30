namespace DeadVault.Core.Models;

public class RestoreResult
{
    public bool Success { get; set; }
    public string PreRestoreCommitSha { get; set; } = string.Empty;
    public string RestoredToCommitSha { get; set; } = string.Empty;
    public int FilesRestored { get; set; }
    public string? ErrorMessage { get; set; }
}
