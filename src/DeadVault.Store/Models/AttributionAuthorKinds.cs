namespace DeadVault.Store.Models;

public static class AttributionAuthorKinds
{
    public const string Human = "human";
    public const string AI = "ai";
    public const string Mixed = "mixed";
    public const string System = "system";
    public const string Unknown = "unknown";

    /// <summary>
    /// Normalize to a known kind (human/ai/mixed/system/unknown), discarding any optional detail.
    /// </summary>
    public static string Normalize(string? value)
        => NormalizeKind(value);

    /// <summary>
    /// Returns the normalized kind while accepting strings like "ai:gpt-5.4" or "human:alice".
    /// </summary>
    public static string NormalizeKind(string? value)
    {
        var (kind, _) = Parse(value);
        return kind switch
        {
            Human => Human,
            AI => AI,
            Mixed => Mixed,
            System => System,
            _ => Unknown,
        };
    }

    /// <summary>
    /// Normalizes kind and preserves an optional detail: "ai:gpt-5.4", "human:alice", etc.
    /// </summary>
    public static string NormalizeWithDetail(string? value)
    {
        var (kind, detail) = Parse(value);
        kind = NormalizeKind(kind);
        if (kind == Unknown)
            return Unknown;

        detail = detail?.Trim();
        return string.IsNullOrWhiteSpace(detail)
            ? kind
            : $"{kind}:{detail}";
    }

    /// <summary>
    /// Parse an author string into (kind, detail). Accepts "ai", "ai:gpt-5.4", "human:alice".
    /// </summary>
    public static (string Kind, string? Detail) Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return (Unknown, null);

        var trimmed = value.Trim();
        var idx = trimmed.IndexOf(':');
        if (idx < 0)
            return (trimmed.ToLowerInvariant(), null);

        var kind = trimmed[..idx].Trim().ToLowerInvariant();
        var detail = trimmed[(idx + 1)..].Trim();
        return (kind, string.IsNullOrWhiteSpace(detail) ? null : detail);
    }
}
