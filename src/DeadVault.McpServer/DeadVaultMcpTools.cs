using DeadVault.Core.Interfaces;
using DeadVault.Core.Models;
using DeadVault.Store.Interfaces;
using DeadVault.Store.Models;
using ModelContextProtocol.Server;

namespace DeadVault.McpServer;

[McpServerToolType]
public class DeadVaultMcpTools
{
    private readonly IMetadataStore _store;
    private readonly ISnapshotManager _snapshotManager;
    private readonly IDiffManager _diffManager;
    private readonly IRestoreManager _restoreManager;
    private readonly ILockManager _lockManager;

    public DeadVaultMcpTools(
        IMetadataStore store,
        ISnapshotManager snapshotManager,
        IDiffManager diffManager,
        IRestoreManager restoreManager,
        ILockManager lockManager)
    {
        _store = store;
        _snapshotManager = snapshotManager;
        _diffManager = diffManager;
        _restoreManager = restoreManager;
        _lockManager = lockManager;
    }

    [McpServerTool(Name = "list_versions", Title = "List project versions", ReadOnly = true, Idempotent = true)]
    public async Task<ListVersionsResult> ListVersions(
        string? projectId = null,
        string? projectName = null,
        int limit = 50)
    {
        var project = await ResolveProjectAsync(projectId, projectName);
        var snapshots = await _snapshotManager.ListSnapshotsAsync(project, Math.Clamp(limit, 1, 200));

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
                Author = change.AuthorKind,
                Watermark = change.Watermark,
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
        var normalizedAuthor = AttributionAuthorKinds.Normalize(author);
        var snapshots = await _snapshotManager.ListSnapshotsAsync(project, Math.Clamp(limit, 1, 200));

        var matches = new List<QueryChangeRecord>();
        foreach (var snapshot in snapshots)
        {
            if (!string.Equals(snapshot.AuthorKind, normalizedAuthor, StringComparison.OrdinalIgnoreCase))
            {
                var diff = await _diffManager.GetCommitDiffAsync(project, snapshot.CommitSha);
                if (!diff.Changes.Any(change => string.Equals(change.AuthorKind, normalizedAuthor, StringComparison.OrdinalIgnoreCase)))
                    continue;
            }

            matches.Add(new QueryChangeRecord
            {
                CommitSha = snapshot.CommitSha,
                ShortSha = snapshot.ShortSha,
                Timestamp = snapshot.Timestamp,
                Version = snapshot.Version,
                Message = snapshot.Message,
                Author = snapshot.AuthorKind,
                FilesChanged = snapshot.FilesChanged,
                WatermarkedFiles = snapshot.WatermarkedFiles,
            });
        }

        return new QueryChangesResult
        {
            ProjectId = project.Id,
            ProjectName = project.Name,
            Author = normalizedAuthor,
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
    public string Author { get; set; } = AttributionAuthorKinds.Unknown;
    public string? Watermark { get; set; }
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
