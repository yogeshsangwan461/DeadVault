using System.IO.Pipes;
using System.Text.Json;
using DeadVault.Core.Interfaces;
using DeadVault.Core.Models;
using DeadVault.Core.Services;
using DeadVault.Core.Ipc;
using DeadVault.Store.Interfaces;
using DeadVault.Store.Models;
using ModelContextProtocol.Server;

namespace DeadVault.McpServer;

[McpServerToolType]
public class DeadVaultMcpTools
{
    private readonly IMetadataStore _store;
    private readonly IRepoManager _repoManager;
    private readonly ISnapshotManager _snapshotManager;
    private readonly IDiffManager _diffManager;
    private readonly IRestoreManager _restoreManager;
    private readonly ILockManager _lockManager;
    private readonly VersionManager _versionManager;

    public DeadVaultMcpTools(
        IMetadataStore store,
        IRepoManager repoManager,
        ISnapshotManager snapshotManager,
        IDiffManager diffManager,
        IRestoreManager restoreManager,
        ILockManager lockManager)
    {
        _store = store;
        _repoManager = repoManager;
        _snapshotManager = snapshotManager;
        _diffManager = diffManager;
        _restoreManager = restoreManager;
        _lockManager = lockManager;
        _versionManager = new VersionManager(store);
    }

    [McpServerTool(Name = "list_projects", Title = "List DeadVault projects", ReadOnly = true, Idempotent = true)]
    public async Task<ListProjectsResult> ListProjects()
    {
        var projects = await _store.GetAllProjectsAsync();

        return new ListProjectsResult
        {
            Projects = projects
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(p => new ProjectRecord
                {
                    ProjectId = p.Id,
                    ProjectName = p.Name,
                    FolderPath = p.FolderPath,
                    IsActive = p.IsActive,
                    CurrentVersion = p.CurrentVersion,
                    RegisteredAt = p.RegisteredAt,
                    IsInitialized = _repoManager.IsInitialized(p),
                })
                .ToList(),
        };
    }

    [McpServerTool(Name = "register_project", Title = "Register a folder with DeadVault", Destructive = true, Idempotent = false)]
    public async Task<RegisterProjectResult> RegisterProject(
        string folderPath,
        string? projectName = null,
        bool createFolderIfMissing = false,
        bool activate = true,
        int debounceSeconds = 60,
        bool enableAttributionManifest = true,
        string? watermarkingSource = null,
        bool reloadAgent = true)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return new RegisterProjectResult
            {
                Success = false,
                Message = "folderPath is required.",
            };
        }

        string fullFolderPath;
        try
        {
            fullFolderPath = Path.GetFullPath(folderPath.Trim());
        }
        catch (Exception ex)
        {
            return new RegisterProjectResult
            {
                Success = false,
                Message = $"Invalid folderPath: {ex.Message}",
            };
        }

        if (!Directory.Exists(fullFolderPath))
        {
            if (!createFolderIfMissing)
            {
                return new RegisterProjectResult
                {
                    Success = false,
                    Message = "Folder does not exist. Pass createFolderIfMissing=true to let DeadVault create it.",
                    FolderPath = fullFolderPath,
                };
            }

            Directory.CreateDirectory(fullFolderPath);
        }

        var existingProjects = await _store.GetAllProjectsAsync();
        var existing = existingProjects.FirstOrDefault(p =>
            p.FolderPath.Equals(fullFolderPath, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            var repoInitialized = _repoManager.IsInitialized(existing);
            if (!repoInitialized)
                await _repoManager.InitializeRepoAsync(existing);

            return new RegisterProjectResult
            {
                Success = true,
                Message = repoInitialized
                    ? "Project is already registered."
                    : "Project was already registered and its internal DeadVault repo has now been initialized.",
                ProjectId = existing.Id,
                ProjectName = existing.Name,
                FolderPath = existing.FolderPath,
                AlreadyRegistered = true,
                RepoInitialized = true,
                AgentReloaded = reloadAgent && await TryReloadAgentConfigAsync(),
            };
        }

        var resolvedName = string.IsNullOrWhiteSpace(projectName)
            ? Path.GetFileName(fullFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : projectName.Trim();

        if (string.IsNullOrWhiteSpace(resolvedName))
            resolvedName = "DeadVault Project";

        var project = new ProjectConfig
        {
            Name = resolvedName,
            FolderPath = fullFolderPath,
            IsActive = activate,
            DebounceSeconds = debounceSeconds,
            Exclusions = ProjectConfig.GetDefaultExclusions(),
            AttributionAuthor = AttributionAuthorKinds.Human,
            EnableTextWatermarking = enableAttributionManifest,
            WatermarkingSource = watermarkingSource,
        };

        try
        {
            await _store.AddProjectAsync(project);
            await _repoManager.InitializeRepoAsync(project);

            bool agentReloaded = false;
            if (reloadAgent)
                agentReloaded = await TryReloadAgentConfigAsync();

            return new RegisterProjectResult
            {
                Success = true,
                Message = agentReloaded
                    ? "Project registered and agent reloaded."
                    : "Project registered. If the agent is already running, reload or restart it to begin watching the new folder.",
                ProjectId = project.Id,
                ProjectName = project.Name,
                FolderPath = project.FolderPath,
                RepoInitialized = true,
                AgentReloaded = agentReloaded,
            };
        }
        catch (Exception ex)
        {
            await _store.RemoveProjectAsync(project.Id);
            return new RegisterProjectResult
            {
                Success = false,
                Message = $"Failed to register project: {ex.Message}",
                ProjectId = project.Id,
                ProjectName = project.Name,
                FolderPath = project.FolderPath,
            };
        }
    }

    [McpServerTool(Name = "list_versions", Title = "List project versions", ReadOnly = true, Idempotent = true)]
    public async Task<ListVersionsResult> ListVersions(
        string? projectId = null,
        string? projectName = null,
        int limit = 50)
    {
        var project = await ResolveProjectAsync(projectId, projectName);
        var snapshots = await _snapshotManager.ListSnapshotsAsync(project, Math.Clamp(limit, 1, 200), includeFileCounts: true);

        return new ListVersionsResult
        {
            ProjectId = project.Id,
            ProjectName = project.Name,
            CurrentVersion = project.CurrentVersion,
            Versions = snapshots.Select(snapshot => new VersionRecord
            {
                CommitSha = snapshot.CommitSha,
                ShortSha = snapshot.ShortSha,
                Message = snapshot.Message,
                Timestamp = snapshot.Timestamp,
                Version = snapshot.Version,
                BumpKind = snapshot.BumpKind,
                FilesChanged = snapshot.FilesChanged,
                Author = snapshot.AuthorKind,
                WatermarkedFiles = snapshot.WatermarkedFiles,
                IsPreRestore = snapshot.IsPreRestore,
            }).ToList(),
        };
    }

    [McpServerTool(Name = "get_diff", Title = "Get diff with attribution", ReadOnly = true, Idempotent = true)]
    public async Task<DiffToolResult> GetDiff(
        string commitSha,
        string? projectId = null,
        string? projectName = null)
    {
        var project = await ResolveProjectAsync(projectId, projectName);
        var diff = await _diffManager.GetCommitDiffAsync(project, commitSha);

        return new DiffToolResult
        {
            ProjectId = project.Id,
            ProjectName = project.Name,
            CommitSha = diff.CommitSha,
            Author = diff.Attribution.PrimaryAuthor,
            WatermarkedFiles = diff.Attribution.WatermarkedFiles,
            Changes = diff.Changes.Select(change => new DiffChangeRecord
            {
                Path = change.Path,
                Kind = change.Kind.ToString().ToLowerInvariant(),
                LinesAdded = change.LinesAdded,
                LinesRemoved = change.LinesRemoved,
                HumanLines = change.HumanLines,
                AiLines = change.AiLines,
                Author = change.AuthorKind,
                Watermark = change.Watermark,
                LineRanges = change.LineRanges
                    .Select(r => new DiffLineRange { StartLine = r.StartLine, EndLine = r.EndLine, Author = r.Author })
                    .ToList(),
            }).ToList(),
            PatchText = diff.PatchText,
        };
    }

    [McpServerTool(Name = "query_changes", Title = "Query changes by attribution", ReadOnly = true, Idempotent = true)]
    public async Task<QueryChangesResult> QueryChanges(
        string author,
        string? projectId = null,
        string? projectName = null,
        int limit = 50)
    {
        var project = await ResolveProjectAsync(projectId, projectName);

        // Resolve the query into a canonical prefix for matching.
        // Supported forms:
        //   "ai"              → match all AI authors
        //   "human"           → match all human authors
        //   "claude"          → bare model family → treated as "ai:claude" prefix
        //   "ai:claude"       → prefix match on "ai:claude/*"
        //   "ai:claude/claude-sonnet-4-6" → exact author match
        var (inputKind, inputDetail) = AttributionAuthorKinds.Parse(author);
        var normalizedKind = AttributionAuthorKinds.NormalizeKind(inputKind);

        // If the raw input is an unknown kind, it might be a bare model family name like "claude".
        // Promote it to "ai:{family}" prefix matching.
        string? resolvedFamily = null;
        if (normalizedKind == AttributionAuthorKinds.Unknown && string.IsNullOrWhiteSpace(inputDetail))
        {
            resolvedFamily = ResolveModelFamily(null, null, inputKind);
            if (resolvedFamily != null)
                normalizedKind = AttributionAuthorKinds.AI;
        }

        // Build the canonical prefix to match against stored author strings.
        // "ai:claude" matches "ai:claude", "ai:claude/claude-sonnet-4-6", etc.
        string matchPrefix = normalizedKind;
        if (resolvedFamily != null)
            matchPrefix = $"{AttributionAuthorKinds.AI}:{resolvedFamily}";
        else if (!string.IsNullOrWhiteSpace(inputDetail))
            matchPrefix = $"{normalizedKind}:{inputDetail.Trim()}";

        bool AuthorMatches(string storedAuthor)
        {
            var normalized = AttributionAuthorKinds.NormalizeWithDetail(storedAuthor);
            // Prefix match: "ai:claude" matches "ai:claude" and "ai:claude/claude-sonnet-4-6"
            return normalized.Equals(matchPrefix, StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(matchPrefix + "/", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(matchPrefix + ":", StringComparison.OrdinalIgnoreCase);
        }

        var snapshots = await _snapshotManager.ListSnapshotsAsync(project, Math.Clamp(limit, 1, 200));

        var snapshotHits = new List<SnapshotInfo>();
        var needsDiffCheck = new List<SnapshotInfo>();

        foreach (var snapshot in snapshots)
        {
            if (AuthorMatches(snapshot.AuthorKind))
                snapshotHits.Add(snapshot);
            else
                needsDiffCheck.Add(snapshot);
        }

        // Fetch diffs for non-matching snapshots concurrently instead of sequentially.
        var diffTasks = needsDiffCheck.Select(s => _diffManager.GetCommitDiffAsync(project, s.CommitSha)).ToList();
        var diffs = await Task.WhenAll(diffTasks);

        var diffHits = needsDiffCheck
            .Zip(diffs, (snapshot, diff) => (snapshot, diff))
            .Where(pair => pair.diff.Changes.Any(c => AuthorMatches(c.AuthorKind)))
            .Select(pair => pair.snapshot);

        var matches = snapshotHits.Concat(diffHits)
            .OrderByDescending(s => s.Timestamp)
            .Select(snapshot => new QueryChangeRecord
            {
                CommitSha = snapshot.CommitSha,
                ShortSha = snapshot.ShortSha,
                Timestamp = snapshot.Timestamp,
                Version = snapshot.Version,
                Message = snapshot.Message,
                Author = snapshot.AuthorKind,
                FilesChanged = snapshot.FilesChanged,
                WatermarkedFiles = snapshot.WatermarkedFiles,
            })
            .ToList();

        return new QueryChangesResult
        {
            ProjectId = project.Id,
            ProjectName = project.Name,
            Author = matchPrefix,
            Matches = matches,
        };
    }

    [McpServerTool(Name = "rollback", Title = "Rollback project to a prior version", Destructive = true, Idempotent = false)]
    public async Task<RollbackResult> Rollback(
        string commitSha,
        string? projectId = null,
        string? projectName = null)
    {
        var project = await ResolveProjectAsync(projectId, projectName);

        if (!_lockManager.TryAcquire(project, out string? owner))
        {
            return new RollbackResult
            {
                Success = false,
                Message = $"Project is locked by {owner}.",
            };
        }

        try
        {
            var result = await _restoreManager.RestoreToSnapshotAsync(project, commitSha);
            return new RollbackResult
            {
                Success = result.Success,
                Message = result.Success
                    ? $"Rolled back to {result.RestoredToCommitSha[..8]} (backup: {result.PreRestoreCommitSha})"
                    : result.ErrorMessage ?? "Rollback failed.",
                PreRestoreCommitSha = result.PreRestoreCommitSha,
                RestoredToCommitSha = result.RestoredToCommitSha,
                FilesRestored = result.FilesRestored,
            };
        }
        finally
        {
            _lockManager.Release(project);
        }
    }

    [McpServerTool(Name = "create_version", Title = "Create a versioned snapshot with an explicit bump kind", Destructive = true, Idempotent = false)]
    public async Task<CreateVersionResult> CreateVersion(
        string bumpKind = "patch",
        string? customVersion = null,
        string? note = null,
        string authorKind = "ai",
        string? model = null,
        string? modelFamily = null,
        string? clientName = null,
        string? clientTimestampUtc = null,
        string? projectId = null,
        string? projectName = null)
    {
        var project = await ResolveProjectAsync(projectId, projectName);
        var normalizedKind = NormalizeBumpKind(bumpKind);
        var normalizedAuthorKind = NormalizeAuthorKind(authorKind);

        if (!_lockManager.TryAcquire(project, out string? owner))
        {
            return new CreateVersionResult
            {
                Success = false,
                Message = $"Project is locked by {owner}.",
                ProjectId = project.Id,
                ProjectName = project.Name,
                CurrentVersion = project.CurrentVersion,
                RequestedBumpKind = normalizedKind,
                Author = normalizedAuthorKind,
            };
        }

        try
        {
            var effectiveClientName = string.IsNullOrWhiteSpace(clientName) ? "mcp-client" : clientName.Trim();
            var effectiveTimestamp = string.IsNullOrWhiteSpace(clientTimestampUtc) ? DateTime.UtcNow.ToString("O") : clientTimestampUtc.Trim();
            var effectiveFamily = ResolveModelFamily(modelFamily, model, effectiveClientName);
            var requestedAuthor = BuildRequestedAuthor(normalizedAuthorKind, effectiveClientName, effectiveFamily, model);

            if (requestedAuthor == null)
            {
                return new CreateVersionResult
                {
                    Success = false,
                    Message = "For AI attribution, provide modelFamily or enough client/model info for DeadVault to infer it.",
                    ProjectId = project.Id,
                    ProjectName = project.Name,
                    CurrentVersion = project.CurrentVersion,
                    RequestedBumpKind = normalizedKind,
                    Author = normalizedAuthorKind,
                };
            }

            SemanticVersion targetVersion;
            VersionBumpKind versionBumpKind;

            switch (normalizedKind)
            {
                case "minor":
                    versionBumpKind = VersionBumpKind.Minor;
                    targetVersion = _versionManager.GetNextVersion(project, versionBumpKind);
                    break;
                case "major":
                    versionBumpKind = VersionBumpKind.Major;
                    targetVersion = _versionManager.GetNextVersion(project, versionBumpKind);
                    break;
                case "dev":
                    versionBumpKind = VersionBumpKind.Dev;
                    targetVersion = _versionManager.GetNextVersion(project, versionBumpKind);
                    break;
                case "custom":
                    versionBumpKind = VersionBumpKind.Custom;
                    if (string.IsNullOrWhiteSpace(customVersion))
                    {
                        return new CreateVersionResult
                        {
                            Success = false,
                            Message = "customVersion is required when bumpKind is 'custom'.",
                            ProjectId = project.Id,
                            ProjectName = project.Name,
                            CurrentVersion = project.CurrentVersion,
                            RequestedBumpKind = normalizedKind,
                            Author = normalizedAuthorKind,
                        };
                    }
                    targetVersion = _versionManager.GetCustomVersion(customVersion);
                    break;
                default:
                    versionBumpKind = VersionBumpKind.Patch;
                    targetVersion = _versionManager.GetNextVersion(project, versionBumpKind);
                    break;
            }

            var previousVersion = _versionManager.GetCurrentVersion(project);
            var commitMessage = AppendMcpAttributionMetadata(
                _versionManager.BuildCommitMessage(targetVersion, versionBumpKind, note),
                requestedAuthor,
                effectiveClientName,
                effectiveFamily,
                model,
                effectiveTimestamp);
            var result = await _snapshotManager.CreateSnapshotAsync(project, commitMessage);

            if (result == null)
            {
                return new CreateVersionResult
                {
                    Success = false,
                    Message = "No changes to version.",
                    ProjectId = project.Id,
                    ProjectName = project.Name,
                    CurrentVersion = project.CurrentVersion,
                    RequestedBumpKind = normalizedKind,
                    Author = normalizedAuthorKind,
                };
            }

            if (versionBumpKind != VersionBumpKind.Dev)
            {
                await _versionManager.SetCurrentVersionAsync(
                    project,
                    targetVersion,
                    $"[{project.Name}] Version updated from MCP: {previousVersion} -> {targetVersion} ({versionBumpKind})");
            }

            return new CreateVersionResult
            {
                Success = true,
                Message = versionBumpKind == VersionBumpKind.Dev
                    ? $"Created dev snapshot at {previousVersion} ({result.ShortSha})."
                    : $"Created {targetVersion} ({normalizedKind}) as {result.ShortSha}.",
                ProjectId = project.Id,
                ProjectName = project.Name,
                CurrentVersion = versionBumpKind == VersionBumpKind.Dev ? project.CurrentVersion : targetVersion.ToString().TrimStart('v'),
                RequestedBumpKind = normalizedKind,
                Author = requestedAuthor,
                ModelFamily = effectiveFamily,
                CommitSha = result.CommitSha,
                ShortSha = result.ShortSha,
                Version = result.Version ?? targetVersion.ToString().TrimStart('v'),
                SnapshotMessage = result.Message,
            };
        }
        finally
        {
            _lockManager.Release(project);
        }
    }

    private async Task<ProjectConfig> ResolveProjectAsync(string? projectId, string? projectName)
    {
        var projects = await _store.GetAllProjectsAsync();

        ProjectConfig? project = null;
        if (!string.IsNullOrWhiteSpace(projectId))
        {
            project = projects.FirstOrDefault(p => string.Equals(p.Id, projectId, StringComparison.OrdinalIgnoreCase));
        }

        if (project == null && !string.IsNullOrWhiteSpace(projectName))
        {
            project = projects.FirstOrDefault(p => string.Equals(p.Name, projectName, StringComparison.OrdinalIgnoreCase));
        }

        if (project == null && projects.Count == 1)
            project = projects[0];

        if (project == null)
            throw new InvalidOperationException("Project not found. Pass projectId or projectName.");

        return project;
    }

    private static string NormalizeBumpKind(string? bumpKind)
    {
        var normalized = (bumpKind ?? "patch").Trim().ToLowerInvariant();
        return normalized switch
        {
            "patch" => "patch",
            "minor" => "minor",
            "major" => "major",
            "dev" => "dev",
            "custom" => "custom",
            _ => "patch",
        };
    }

    private static string NormalizeAuthorKind(string? authorKind)
    {
        var normalized = AttributionAuthorKinds.NormalizeKind(authorKind);
        return normalized == AttributionAuthorKinds.Unknown ? AttributionAuthorKinds.AI : normalized;
    }

    private static string? BuildRequestedAuthor(string normalizedAuthorKind, string clientName, string? family, string? model)
    {
        if (normalizedAuthorKind == AttributionAuthorKinds.AI)
        {
            if (string.IsNullOrWhiteSpace(family) && string.IsNullOrWhiteSpace(model))
                return null;

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(family))
                parts.Add(family.Trim());
            if (!string.IsNullOrWhiteSpace(model))
                parts.Add(model.Trim());
            else if (!string.IsNullOrWhiteSpace(clientName))
                parts.Add(clientName.Trim());

            return AttributionAuthorKinds.NormalizeWithDetail($"ai:{string.Join("/", parts)}");
        }

        if (normalizedAuthorKind == AttributionAuthorKinds.Mixed)
            return AttributionAuthorKinds.NormalizeWithDetail(
                string.IsNullOrWhiteSpace(family) ? $"mixed:{clientName}" : $"mixed:{family}/{clientName}");

        if (normalizedAuthorKind == AttributionAuthorKinds.Human)
            return AttributionAuthorKinds.NormalizeWithDetail($"human:{clientName}");

        return AttributionAuthorKinds.Human;
    }

    private static string AppendMcpAttributionMetadata(
        string commitMessage,
        string requestedAuthor,
        string clientName,
        string? family,
        string? model,
        string clientTimestampUtc)
    {
        var builder = new System.Text.StringBuilder(commitMessage.TrimEnd());
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine($"DeadVault-Requested-Author: {requestedAuthor}");
        builder.AppendLine("DeadVault-Attribution-Source: mcp");
        builder.AppendLine($"DeadVault-Mcp-Client: {clientName}");
        if (!string.IsNullOrWhiteSpace(family))
            builder.AppendLine($"DeadVault-Mcp-Family: {family.Trim()}");
        if (!string.IsNullOrWhiteSpace(model))
            builder.AppendLine($"DeadVault-Mcp-Model: {model.Trim()}");
        builder.Append($"DeadVault-Mcp-Time: {clientTimestampUtc}");
        return builder.ToString();
    }

    private static string? ResolveModelFamily(string? requestedFamily, string? model, string? clientName)
    {
        if (!string.IsNullOrWhiteSpace(requestedFamily))
            return requestedFamily.Trim().ToLowerInvariant();

        var haystack = $"{model} {clientName}".ToLowerInvariant();

        if (haystack.Contains("claude"))
            return "claude";
        if (haystack.Contains("gpt") || haystack.Contains("openai") || haystack.Contains("chatgpt"))
            return "gpt";
        if (haystack.Contains("gemini"))
            return "gemini";
        if (haystack.Contains("copilot"))
            return "copilot";
        if (haystack.Contains("cursor"))
            return "cursor";
        if (haystack.Contains("llama") || haystack.Contains("ollama"))
            return "llama";
        if (haystack.Contains("mistral"))
            return "mistral";
        if (haystack.Contains("deepseek"))
            return "deepseek";
        if (haystack.Contains("qwen"))
            return "qwen";

        return null;
    }

    private static async Task<bool> TryReloadAgentConfigAsync(int timeoutMs = 3000)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", "DeadVault_IPC_Pipe", PipeDirection.InOut, PipeOptions.Asynchronous);
            using var cts = new CancellationTokenSource(timeoutMs);
            await client.ConnectAsync(cts.Token);

            using var writer = new StreamWriter(client) { AutoFlush = true };
            using var reader = new StreamReader(client);

            var message = new IpcMessage { Command = IpcMessage.Commands.ReloadConfig };
            await writer.WriteLineAsync(JsonSerializer.Serialize(message)).WaitAsync(cts.Token);

            var response = await reader.ReadLineAsync().WaitAsync(cts.Token);
            if (string.IsNullOrWhiteSpace(response))
                return false;

            var parsed = JsonSerializer.Deserialize<IpcResponse>(response);
            return parsed?.Success == true;
        }
        catch
        {
            return false;
        }
    }
}

public class ListProjectsResult
{
    public List<ProjectRecord> Projects { get; set; } = new();
}

public class ProjectRecord
{
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string CurrentVersion { get; set; } = string.Empty;
    public DateTime RegisteredAt { get; set; }
    public bool IsInitialized { get; set; }
}

public class RegisterProjectResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public string? FolderPath { get; set; }
    public bool AlreadyRegistered { get; set; }
    public bool RepoInitialized { get; set; }
    public bool AgentReloaded { get; set; }
}

public class ListVersionsResult
{
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string CurrentVersion { get; set; } = string.Empty;
    public List<VersionRecord> Versions { get; set; } = new();
}

public class VersionRecord
{
    public string CommitSha { get; set; } = string.Empty;
    public string ShortSha { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? Version { get; set; }
    public string? BumpKind { get; set; }
    public int FilesChanged { get; set; }
    public string Author { get; set; } = AttributionAuthorKinds.Unknown;
    public int WatermarkedFiles { get; set; }
    public bool IsPreRestore { get; set; }
}

public class DiffToolResult
{
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string CommitSha { get; set; } = string.Empty;
    public string Author { get; set; } = AttributionAuthorKinds.Unknown;
    public int WatermarkedFiles { get; set; }
    public List<DiffChangeRecord> Changes { get; set; } = new();
    public string PatchText { get; set; } = string.Empty;
}

public class DiffChangeRecord
{
    public string Path { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public int LinesAdded { get; set; }
    public int LinesRemoved { get; set; }
    public int HumanLines { get; set; }
    public int AiLines { get; set; }
    public string Author { get; set; } = AttributionAuthorKinds.Unknown;
    public string? Watermark { get; set; }
    public List<DiffLineRange> LineRanges { get; set; } = new();
}

public class DiffLineRange
{
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string Author { get; set; } = AttributionAuthorKinds.Unknown;
}

public class QueryChangesResult
{
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string Author { get; set; } = AttributionAuthorKinds.Unknown;
    public List<QueryChangeRecord> Matches { get; set; } = new();
}

public class QueryChangeRecord
{
    public string CommitSha { get; set; } = string.Empty;
    public string ShortSha { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? Version { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Author { get; set; } = AttributionAuthorKinds.Unknown;
    public int FilesChanged { get; set; }
    public int WatermarkedFiles { get; set; }
}

public class RollbackResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? PreRestoreCommitSha { get; set; }
    public string? RestoredToCommitSha { get; set; }
    public int FilesRestored { get; set; }
}

public class CreateVersionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string CurrentVersion { get; set; } = string.Empty;
    public string RequestedBumpKind { get; set; } = "patch";
    public string Author { get; set; } = AttributionAuthorKinds.Unknown;
    public string? ModelFamily { get; set; }
    public string? CommitSha { get; set; }
    public string? ShortSha { get; set; }
    public string? Version { get; set; }
    public string? SnapshotMessage { get; set; }
}
