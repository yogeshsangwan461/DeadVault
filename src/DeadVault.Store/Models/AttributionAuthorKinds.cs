namespace DeadVault.Store.Models;

public static class AttributionAuthorKinds
{
    public const string Human = "human";
    public const string AI = "ai";
    public const string Mixed = "mixed";
    public const string System = "system";
    public const string Unknown = "unknown";

    public static string Normalize(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            Human => Human,
            AI => AI,
            Mixed => Mixed,
            System => System,
            _ => Unknown,
        };
    }
}
