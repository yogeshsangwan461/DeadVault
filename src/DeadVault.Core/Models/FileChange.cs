namespace DeadVault.Core.Models;

public enum FileChangeKind
{
    Added,
    Modified,
    Deleted,
    Renamed,
    Copied,
}

public class FileChange
{
    public string Path { get; set; } = string.Empty;
    public FileChangeKind Kind { get; set; }
    public int LinesAdded { get; set; }
    public int LinesRemoved { get; set; }
    public string AuthorKind { get; set; } = "unknown";
    public string? Watermark { get; set; }
    public bool IsWatermarked => !string.IsNullOrWhiteSpace(Watermark);

    public string KindSymbol => Kind switch
    {
        FileChangeKind.Added => "+",
        FileChangeKind.Deleted => "-",
        FileChangeKind.Modified => "~",
        FileChangeKind.Renamed => "R",
        FileChangeKind.Copied => "C",
        _ => "?",
    };
}
