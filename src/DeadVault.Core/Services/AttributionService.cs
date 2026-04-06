using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LibGit2Sharp;
using DeadVault.Core.Models;
using DeadVault.Store.Models;

namespace DeadVault.Core.Services;

public class AttributionService
{
    public const string ManifestFileName = ".deadvault.attribution.json";
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public SnapshotAttributionSummary UpdateWorkingTreeManifest(
        ProjectConfig project,
        IReadOnlyCollection<string> changedPaths,
        string authorKind)
    {
        authorKind = AttributionAuthorKinds.NormalizeWithDetail(authorKind);

        if (!project.EnableTextWatermarking || changedPaths.Count == 0)
            return new SnapshotAttributionSummary { PrimaryAuthor = authorKind };

        var manifestPath = GetManifestPath(project);
        var manifest = LoadManifestFromFile(manifestPath);
        var remainingBudget = Math.Max(0, project.AttributionTextBudgetBytes);
        var summary = new SnapshotAttributionSummary { PrimaryAuthor = authorKind };
        var manifestChanged = false;

        foreach (var changedPath in changedPaths
            .Select(NormalizeGitPath)
            .Where(path => !IsInternalPath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var fullPath = Path.Combine(project.FolderPath, changedPath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(fullPath))
            {
                manifestChanged |= manifest.Files.Remove(changedPath);
                continue;
            }

            var fileInfo = new FileInfo(fullPath);
            var record = new AttributionFileRecord
            {
                Path = changedPath,
                Author = authorKind,
                SizeBytes = fileInfo.Length,
                UpdatedAtUtc = DateTime.UtcNow,
            };

            if (remainingBudget > 0 && fileInfo.Length <= remainingBudget && IsTextFile(fullPath))
            {
                var content = File.ReadAllText(fullPath);
                record.IsText = true;
                record.ContentHash = ComputeHash(content);
                record.Watermark = ComputeWatermark(content, changedPath, authorKind);
                remainingBudget -= (int)Math.Min(fileInfo.Length, remainingBudget);
                summary.ProcessedTextBytes += fileInfo.Length;
                summary.WatermarkedFiles++;
            }

            manifest.Files[changedPath] = record;
            manifestChanged = true;
            Count(summary, authorKind);
        }

        if (manifestChanged)
        {
            manifest.UpdatedAtUtc = DateTime.UtcNow;
            SaveManifestToFile(manifestPath, manifest);
        }

        summary.PrimaryAuthor = SelectPrimaryAuthor(summary, authorKind);
        return summary;
    }

    public string AppendCommitTrailers(string message, SnapshotAttributionSummary summary)
    {
        if (summary.TotalFiles == 0)
            return message;

        var builder = new StringBuilder(message.TrimEnd());
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine($"DeadVault-Author: {summary.PrimaryAuthor}");
        builder.AppendLine($"DeadVault-Human-Files: {summary.HumanFiles}");
        builder.AppendLine($"DeadVault-AI-Files: {summary.AiFiles}");
        builder.AppendLine($"DeadVault-Mixed-Files: {summary.MixedFiles}");
        builder.AppendLine($"DeadVault-System-Files: {summary.SystemFiles}");
        builder.AppendLine($"DeadVault-Unknown-Files: {summary.UnknownFiles}");
        builder.AppendLine($"DeadVault-Watermarked-Files: {summary.WatermarkedFiles}");
        builder.Append($"DeadVault-Processed-Text-Bytes: {summary.ProcessedTextBytes}");
        return builder.ToString();
    }

    public SnapshotAttributionSummary ParseCommitTrailers(string? commitMessage)
    {
        var summary = new SnapshotAttributionSummary();
        if (string.IsNullOrWhiteSpace(commitMessage))
            return summary;

        foreach (var line in commitMessage.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("DeadVault-Author:", StringComparison.OrdinalIgnoreCase))
                summary.PrimaryAuthor = AttributionAuthorKinds.NormalizeWithDetail(Value(trimmed));
            else if (trimmed.StartsWith("DeadVault-Human-Files:", StringComparison.OrdinalIgnoreCase))
                summary.HumanFiles = ParseInt(Value(trimmed));
            else if (trimmed.StartsWith("DeadVault-AI-Files:", StringComparison.OrdinalIgnoreCase))
                summary.AiFiles = ParseInt(Value(trimmed));
            else if (trimmed.StartsWith("DeadVault-Mixed-Files:", StringComparison.OrdinalIgnoreCase))
                summary.MixedFiles = ParseInt(Value(trimmed));
            else if (trimmed.StartsWith("DeadVault-System-Files:", StringComparison.OrdinalIgnoreCase))
                summary.SystemFiles = ParseInt(Value(trimmed));
            else if (trimmed.StartsWith("DeadVault-Unknown-Files:", StringComparison.OrdinalIgnoreCase))
                summary.UnknownFiles = ParseInt(Value(trimmed));
            else if (trimmed.StartsWith("DeadVault-Watermarked-Files:", StringComparison.OrdinalIgnoreCase))
                summary.WatermarkedFiles = ParseInt(Value(trimmed));
            else if (trimmed.StartsWith("DeadVault-Processed-Text-Bytes:", StringComparison.OrdinalIgnoreCase))
                summary.ProcessedTextBytes = ParseInt(Value(trimmed));
        }

        if (summary.TotalFiles > 0 && summary.PrimaryAuthor == AttributionAuthorKinds.Unknown)
            summary.PrimaryAuthor = SelectPrimaryAuthor(summary, AttributionAuthorKinds.Unknown);

        return summary;
    }

    public AttributionManifest LoadManifestFromCommit(Repository repo, Commit? commit)
    {
        if (commit == null)
            return new AttributionManifest();

        var entry = commit[ManifestFileName];
        if (entry?.Target is not Blob blob)
            return new AttributionManifest();

        using var stream = blob.GetContentStream();
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        return JsonSerializer.Deserialize<AttributionManifest>(json, ManifestJsonOptions)
               ?? new AttributionManifest();
    }

    public string GetManifestPath(ProjectConfig project)
        => Path.Combine(project.FolderPath, ManifestFileName);

    public static string NormalizeGitPath(string path)
        => path.Replace('\\', '/').TrimStart('/');

    public static bool IsInternalPath(string gitPath)
    {
        var normalized = NormalizeGitPath(gitPath);
        return normalized.Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(".deadvault/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals(".deadvault.lock", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals(".git", StringComparison.OrdinalIgnoreCase);
    }

    public static string ResolveSnapshotAuthor(ProjectConfig project, string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            if (message.StartsWith("PRE-RESTORE:", StringComparison.OrdinalIgnoreCase) ||
                message.StartsWith("DeadVault:", StringComparison.OrdinalIgnoreCase))
            {
                return AttributionAuthorKinds.System;
            }

            if (TryGetRequestedAuthor(message, out var requestedAuthor))
                return requestedAuthor;
        }

        return AttributionAuthorKinds.Human;
    }

    public static bool TryGetRequestedAuthor(string? message, out string author)
    {
        author = AttributionAuthorKinds.Unknown;
        if (string.IsNullOrWhiteSpace(message))
            return false;

        foreach (var line in message.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("DeadVault-Requested-Author:", StringComparison.OrdinalIgnoreCase))
                continue;

            author = AttributionAuthorKinds.NormalizeWithDetail(Value(trimmed));
            return author != AttributionAuthorKinds.Unknown;
        }

        return false;
    }

    private static AttributionManifest LoadManifestFromFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new AttributionManifest();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AttributionManifest>(json, ManifestJsonOptions)
                   ?? new AttributionManifest();
        }
        catch
        {
            return new AttributionManifest();
        }
    }

    private static void SaveManifestToFile(string path, AttributionManifest manifest)
    {
        var orderedManifest = new AttributionManifest
        {
            SchemaVersion = manifest.SchemaVersion,
            UpdatedAtUtc = manifest.UpdatedAtUtc,
            Files = manifest.Files
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
        };

        var json = JsonSerializer.Serialize(orderedManifest, ManifestJsonOptions);
        File.WriteAllText(path, json);
    }

    private static void Count(SnapshotAttributionSummary summary, string authorKind)
    {
        switch (AttributionAuthorKinds.NormalizeKind(authorKind))
        {
            case AttributionAuthorKinds.Human:
                summary.HumanFiles++;
                break;
            case AttributionAuthorKinds.AI:
                summary.AiFiles++;
                break;
            case AttributionAuthorKinds.Mixed:
                summary.MixedFiles++;
                break;
            case AttributionAuthorKinds.System:
                summary.SystemFiles++;
                break;
            default:
                summary.UnknownFiles++;
                break;
        }
    }

    private static string SelectPrimaryAuthor(SnapshotAttributionSummary summary, string fallback)
    {
        var normalizedFallback = AttributionAuthorKinds.NormalizeWithDetail(fallback);
        var fallbackKind = AttributionAuthorKinds.NormalizeKind(normalizedFallback);

        var ranked = new Dictionary<string, int>
        {
            [AttributionAuthorKinds.Human] = summary.HumanFiles,
            [AttributionAuthorKinds.AI] = summary.AiFiles,
            [AttributionAuthorKinds.Mixed] = summary.MixedFiles,
            [AttributionAuthorKinds.System] = summary.SystemFiles,
            [AttributionAuthorKinds.Unknown] = summary.UnknownFiles,
        };

        var best = ranked
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .FirstOrDefault();

        if (best.Value <= 0)
            return normalizedFallback;

        // If the dominant kind matches the declared author kind, keep the detail (e.g. "ai:gpt-5.4").
        return string.Equals(best.Key, fallbackKind, StringComparison.OrdinalIgnoreCase)
            ? normalizedFallback
            : best.Key;
    }

    private static string ComputeWatermark(string content, string path, string authorKind)
    {
        var normalized = NormalizeLineEndings(content);
        var payload = $"{AttributionAuthorKinds.NormalizeWithDetail(authorKind)}|{path}|{normalized}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        return $"dvwm1-{hash[..12].ToLowerInvariant()}";
    }

    private static string ComputeHash(string content)
        => $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeLineEndings(content)))).ToLowerInvariant()}";

    private static string NormalizeLineEndings(string content)
        => content.Replace("\r\n", "\n").Replace('\r', '\n');

    private static bool IsTextFile(string path)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[Math.Min(4096, (int)stream.Length)];
        var bytesRead = stream.Read(buffer, 0, buffer.Length);

        for (var i = 0; i < bytesRead; i++)
        {
            if (buffer[i] == 0)
                return false;
        }

        return true;
    }

    private static string Value(string line)
        => line[(line.IndexOf(':') + 1)..].Trim();

    private static int ParseInt(string value)
        => int.TryParse(value, out var parsed) ? parsed : 0;
}
