namespace DeadVault.Store.Models;

public class ProjectConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int DebounceSeconds { get; set; } = 60;
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    public List<ExclusionRule> Exclusions { get; set; } = new();

    // Semantic Versioning
    public string CurrentVersion { get; set; } = "1.0.0";
    public int SessionTimeoutMinutes { get; set; } = 120; // 2 hours = new session
    public string AttributionAuthor { get; set; } = AttributionAuthorKinds.Human;
    public bool EnableTextWatermarking { get; set; } = true;
    public int AttributionTextBudgetBytes { get; set; } = 10 * 1024 * 1024;

    public static List<ExclusionRule> GetDefaultExclusions() => new()
    {
        new() { Pattern = ".deadvault/", Description = "DeadVault internal", IsDefault = true },
        new() { Pattern = ".deadvault.lock", Description = "DeadVault lock file", IsDefault = true },
        new() { Pattern = ".git/", Description = "Git directory", IsDefault = true },
        new() { Pattern = "bin/", Description = "Build output", IsDefault = true },
        new() { Pattern = "obj/", Description = "Build intermediates", IsDefault = true },
        new() { Pattern = "node_modules/", Description = "Node packages", IsDefault = true },
        new() { Pattern = ".vs/", Description = "Visual Studio cache", IsDefault = true },
        new() { Pattern = "*.exe", Description = "Executables", IsDefault = true },
        new() { Pattern = "*.dll", Description = "Dynamic libraries", IsDefault = true },
        new() { Pattern = "*.pdb", Description = "Debug symbols", IsDefault = true },
        new() { Pattern = "*.user", Description = "User settings", IsDefault = true },
        new() { Pattern = "*.suo", Description = "Solution user options", IsDefault = true },
        new() { Pattern = "__pycache__/", Description = "Python cache", IsDefault = true },
        new() { Pattern = "*.pyc", Description = "Python compiled", IsDefault = true },
        new() { Pattern = "build/", Description = "Build output", IsDefault = true },
        new() { Pattern = "dist/", Description = "Distribution", IsDefault = true },
        new() { Pattern = "target/", Description = "Rust/Maven target", IsDefault = true },
    };

    public static List<string> SecretPatterns => new()
    {
        ".env", "*.key", "*.pem", "*.pfx", "*.p12",
        "credentials.json", "secrets.json", "*.secret",
    };

    public void Normalize()
    {
        // DebounceSeconds == 0 means "disabled / infinite" (no auto-snap from file changes).
        if (DebounceSeconds < 0)
            DebounceSeconds = 60;
        if (DebounceSeconds > 0)
            DebounceSeconds = Math.Clamp(DebounceSeconds, 15, 600);

        SessionTimeoutMinutes = Math.Clamp(SessionTimeoutMinutes <= 0 ? 120 : SessionTimeoutMinutes, 15, 1440);
        AttributionAuthor = AttributionAuthorKinds.NormalizeWithDetail(AttributionAuthor);
        if (AttributionAuthorKinds.NormalizeKind(AttributionAuthor) == AttributionAuthorKinds.Unknown)
            AttributionAuthor = AttributionAuthorKinds.Human;

        AttributionTextBudgetBytes = AttributionTextBudgetBytes <= 0
            ? 10 * 1024 * 1024
            : AttributionTextBudgetBytes;
    }
}
