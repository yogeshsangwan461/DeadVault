using System.Text.Json.Serialization;
using DeadVault.Store.Models;

namespace DeadVault.Core.Models;

public class AttributionManifest
{
    public string SchemaVersion { get; set; } = "1";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Dictionary<string, AttributionFileRecord> Files { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public class AttributionFileRecord
{
    public string Path { get; set; } = string.Empty;
    public string Author { get; set; } = AttributionAuthorKinds.Unknown;
    public string? Watermark { get; set; }
    public string? ContentHash { get; set; }
    public bool IsText { get; set; }
    public long SizeBytes { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class SnapshotAttributionSummary
{
    public string PrimaryAuthor { get; set; } = AttributionAuthorKinds.Unknown;
    public int HumanFiles { get; set; }
    public int AiFiles { get; set; }
    public int MixedFiles { get; set; }
    public int SystemFiles { get; set; }
    public int UnknownFiles { get; set; }
    public int WatermarkedFiles { get; set; }
    public long ProcessedTextBytes { get; set; }

    [JsonIgnore]
    public int TotalFiles => HumanFiles + AiFiles + MixedFiles + SystemFiles + UnknownFiles;
}
