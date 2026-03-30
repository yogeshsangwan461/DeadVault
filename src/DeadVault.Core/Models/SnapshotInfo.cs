namespace DeadVault.Core.Models;

public class SnapshotInfo
{
    public string CommitSha { get; set; } = string.Empty;
    public string ShortSha => CommitSha.Length >= 8 ? CommitSha[..8] : CommitSha;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public int FilesChanged { get; set; }
    public string? TagName { get; set; }
    public bool IsPreRestore { get; set; }
    public bool IsManual { get; set; }
    public bool IsDemo { get; set; }
    public string? Version { get; set; }
    public string? BumpKind { get; set; } // "patch", "minor", "major"
    public string AuthorKind { get; set; } = "unknown";
    public int HumanFiles { get; set; }
    public int AiFiles { get; set; }
    public int MixedFiles { get; set; }
    public int SystemFiles { get; set; }
    public int UnknownFiles { get; set; }
    public int WatermarkedFiles { get; set; }
    public long ProcessedTextBytes { get; set; }

    public string RelativeTime
    {
        get
        {
            var diff = DateTime.Now - Timestamp;
            if (diff.TotalSeconds < 60) return $"{(int)diff.TotalSeconds}s ago";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 30) return $"{(int)diff.TotalDays}d ago";
            return Timestamp.ToString("yyyy-MM-dd HH:mm");
        }
    }
}
