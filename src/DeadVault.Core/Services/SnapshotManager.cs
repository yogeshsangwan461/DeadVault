using LibGit2Sharp;
using DeadVault.Core.Interfaces;
using DeadVault.Core.Models;
using DeadVault.Store.Models;

namespace DeadVault.Core.Services;

public class SnapshotManager : ISnapshotManager
{
    private readonly AttributionService _attributionService = new();

    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public async Task<SnapshotInfo?> CreateSnapshotAsync(ProjectConfig project, string? message = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(project.FolderPath);

                var preStageStatus = repo.RetrieveStatus(new StatusOptions
                {
                    IncludeUntracked = true,
                    RecurseUntrackedDirs = true,
                });

                var changedPaths = preStageStatus
                    .Where(entry => HasWorkdirChanges(entry.State))
                    .Select(entry => entry.FilePath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var snapshotAuthor = AttributionService.ResolveSnapshotAuthor(project, message);
                var skippedInvalidPaths = new List<string>();

                foreach (var path in changedPaths)
                {
                    if (AttributionService.IsInternalPath(path) &&
                        !path.Equals(AttributionService.ManifestFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (IsInvalidWindowsPath(path))
                    {
                        skippedInvalidPaths.Add(path);
                        continue;
                    }

                    try
                    {
                        Commands.Stage(repo, path);
                    }
                    catch (LibGit2SharpException ex) when (ex.Message.Contains("invalid path:", StringComparison.OrdinalIgnoreCase))
                    {
                        skippedInvalidPaths.Add(path);
                    }
                }

                if (skippedInvalidPaths.Count > 0)
                {
                    VaultLogger.Warn($"[{project.Name}] Skipped invalid path(s): {string.Join(", ", skippedInvalidPaths)}");
                }

                var attributionSummary = _attributionService.UpdateWorkingTreeManifest(project, repo, changedPaths, snapshotAuthor);
                if (File.Exists(_attributionService.GetManifestPath(project)))
                {
                    Commands.Stage(repo, AttributionService.ManifestFileName);
                }

                var status = repo.RetrieveStatus(new StatusOptions());
                bool hasStagedChanges = status.Any(IsDisplayableStagedChange);

                if (!hasStagedChanges)
                {
                    if (changedPaths.Count > 0 && skippedInvalidPaths.Count == changedPaths.Count)
                    {
                        VaultLogger.Warn($"[{project.Name}] Only invalid Windows path(s) changed; skipping snapshot");
                    }
                    else
                    {
                        VaultLogger.Info($"[{project.Name}] No changes to snapshot");
                    }
                    return null;
                }

                int filesChanged = status.Count(IsDisplayableStagedChange);

                string commitMsg = message ?? $"autosnap: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                commitMsg = _attributionService.AppendCommitTrailers(commitMsg, attributionSummary);

                var sig = RepoManager.GetSignature();
                var commit = repo.Commit(commitMsg, sig, sig);

                VaultLogger.Info($"[{project.Name}] Snapshot {commit.Sha[..8]}: {filesChanged} files - {commit.MessageShort}");

                return new SnapshotInfo
                {
                    CommitSha = commit.Sha,
                    Message = commit.MessageShort,
                    Timestamp = commit.Author.When.LocalDateTime,
                    FilesChanged = filesChanged,
                    IsManual = message != null && !message.StartsWith("autosnap:", StringComparison.OrdinalIgnoreCase),
                    IsPreRestore = message?.StartsWith("PRE-RESTORE:", StringComparison.OrdinalIgnoreCase) ?? false,
                    IsDemo = message?.StartsWith("release:", StringComparison.OrdinalIgnoreCase) ?? false,
                    AuthorKind = attributionSummary.PrimaryAuthor,
                    HumanFiles = attributionSummary.HumanFiles,
                    AiFiles = attributionSummary.AiFiles,
                    MixedFiles = attributionSummary.MixedFiles,
                    SystemFiles = attributionSummary.SystemFiles,
                    UnknownFiles = attributionSummary.UnknownFiles,
                    WatermarkedFiles = attributionSummary.WatermarkedFiles,
                    ProcessedTextBytes = attributionSummary.ProcessedTextBytes,
                };
            }
            catch (EmptyCommitException)
            {
                return null;
            }
            catch (Exception ex)
            {
                VaultLogger.Error($"[{project.Name}] Snapshot failed", ex);
                throw;
            }
        });
    }

    public async Task<bool> HasVersionableChangesAsync(ProjectConfig project)
    {
        return await Task.Run(() =>
        {
            using var repo = new Repository(project.FolderPath);
            var status = repo.RetrieveStatus(new StatusOptions
            {
                IncludeUntracked = true,
                RecurseUntrackedDirs = true,
            });

            return status
                .Where(entry => HasWorkdirChanges(entry.State))
                .Select(entry => entry.FilePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Any(path => !AttributionService.IsInternalPath(path) && !IsInvalidWindowsPath(path));
        });
    }

    public async Task<List<SnapshotInfo>> ListSnapshotsAsync(ProjectConfig project, int limit = 200, bool includeFileCounts = false)
    {
        return await Task.Run(() =>
        {
            var snapshots = new List<SnapshotInfo>();

            try
            {
                using var repo = new Repository(project.FolderPath);

                var tagLookup = new Dictionary<string, string>();
                foreach (var tag in repo.Tags)
                {
                    var target = tag.PeeledTarget ?? tag.Target;
                    if (target is Commit commit)
                        tagLookup[commit.Sha] = tag.FriendlyName;
                }

                foreach (var commit in repo.Commits.Take(limit))
                {
                    tagLookup.TryGetValue(commit.Sha, out string? tagName);
                    var (parsedVersion, parsedKind) = VersionManager.ParseCommitMessage(commit.Message);
                    var attribution = _attributionService.ParseCommitTrailers(commit.Message);

                    // Use attribution trailers as primary file count source (embedded in every DeadVault commit).
                    // Fall back to tree diff for commits that pre-date trailers or came from outside DeadVault.
                    int filesChanged = -1;
                    if (attribution.TotalFiles > 0)
                    {
                        filesChanged = attribution.TotalFiles;
                    }
                    else if (includeFileCounts && commit.Parents.Any())
                    {
                        try
                        {
                            var parent = commit.Parents.First();
                            var changes = repo.Diff.Compare<TreeChanges>(parent.Tree, commit.Tree);
                            filesChanged = changes.Count(change => !AttributionService.IsInternalPath(change.Path));
                        }
                        catch (Exception ex)
                        {
                            VaultLogger.Warn($"[{project.Name}] Could not count files for commit {commit.Sha[..8]}: {ex.Message}");
                            filesChanged = -1;
                        }
                    }

                    snapshots.Add(new SnapshotInfo
                    {
                        CommitSha = commit.Sha,
                        Message = commit.MessageShort,
                        Timestamp = commit.Author.When.LocalDateTime,
                        FilesChanged = filesChanged,
                        TagName = tagName,
                        IsPreRestore = commit.Message.StartsWith("PRE-RESTORE:", StringComparison.OrdinalIgnoreCase),
                        IsManual = !commit.Message.StartsWith("autosnap:", StringComparison.OrdinalIgnoreCase) &&
                                   !commit.Message.StartsWith("PRE-RESTORE:", StringComparison.OrdinalIgnoreCase) &&
                                   !commit.Message.StartsWith("DeadVault:", StringComparison.OrdinalIgnoreCase) &&
                                   !commit.Message.StartsWith("[v", StringComparison.OrdinalIgnoreCase),
                        IsDemo = commit.Message.StartsWith("release:", StringComparison.OrdinalIgnoreCase) ||
                                 (tagName?.StartsWith("demo-", StringComparison.OrdinalIgnoreCase) ?? false),
                        Version = parsedVersion?.ToString(),
                        BumpKind = parsedKind,
                        AuthorKind = attribution.PrimaryAuthor,
                        HumanFiles = attribution.HumanFiles,
                        AiFiles = attribution.AiFiles,
                        MixedFiles = attribution.MixedFiles,
                        SystemFiles = attribution.SystemFiles,
                        UnknownFiles = attribution.UnknownFiles,
                        WatermarkedFiles = attribution.WatermarkedFiles,
                        ProcessedTextBytes = attribution.ProcessedTextBytes,
                    });
                }
            }
            catch (Exception ex)
            {
                VaultLogger.Error($"[{project.Name}] Failed to list snapshots", ex);
            }

            return snapshots;
        });
    }

    public async Task<ProjectSnapshotSummary> GetProjectSummaryAsync(ProjectConfig project)
    {
        return await Task.Run(() =>
        {
            var summary = new ProjectSnapshotSummary();

            try
            {
                using var repo = new Repository(project.FolderPath);
                var latestCommit = repo.Head.Tip;
                if (latestCommit == null)
                    return summary;

                var parsed = VersionManager.ParseCommitMessage(latestCommit.Message);
                summary.LatestVersion = parsed.version?.ToString();
                summary.LatestMessage = latestCommit.MessageShort;
                summary.LatestTimestamp = latestCommit.Author.When.LocalDateTime;
                summary.SnapshotCount = repo.Commits.Count();
            }
            catch (Exception ex)
            {
                VaultLogger.Warn($"[{project.Name}] Failed to read project summary: {ex.Message}");
            }

            return summary;
        });
    }

    public async Task TagSnapshotAsync(ProjectConfig project, string commitSha, string tagName)
    {
        await Task.Run(() =>
        {
            using var repo = new Repository(project.FolderPath);
            var sig = RepoManager.GetSignature();
            repo.ApplyTag(tagName, commitSha, sig, $"Tagged by DeadVault: {tagName}");
            VaultLogger.Info($"[{project.Name}] Tagged {commitSha[..8]} as '{tagName}'");
        });
    }

    public async Task RemoveTagAsync(ProjectConfig project, string tagName)
    {
        await Task.Run(() =>
        {
            using var repo = new Repository(project.FolderPath);
            repo.Tags.Remove(tagName);
            VaultLogger.Info($"[{project.Name}] Removed tag '{tagName}'");
        });
    }

    public async Task<List<string>> GetTagsAsync(ProjectConfig project)
    {
        return await Task.Run(() =>
        {
            using var repo = new Repository(project.FolderPath);
            return repo.Tags.Select(t => t.FriendlyName).ToList();
        });
    }

    private static bool HasWorkdirChanges(FileStatus state)
    {
        return state.HasFlag(FileStatus.NewInWorkdir) ||
               state.HasFlag(FileStatus.ModifiedInWorkdir) ||
               state.HasFlag(FileStatus.DeletedFromWorkdir) ||
               state.HasFlag(FileStatus.RenamedInWorkdir) ||
               state.HasFlag(FileStatus.TypeChangeInWorkdir);
    }

    private static bool IsDisplayableStagedChange(StatusEntry entry)
    {
        return !AttributionService.IsInternalPath(entry.FilePath) &&
               (entry.State.HasFlag(FileStatus.NewInIndex) ||
                entry.State.HasFlag(FileStatus.ModifiedInIndex) ||
                entry.State.HasFlag(FileStatus.DeletedFromIndex) ||
                entry.State.HasFlag(FileStatus.RenamedInIndex));
    }

    private static bool IsInvalidWindowsPath(string gitPath)
    {
        var segments = gitPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            var trimmed = segment.TrimEnd(' ', '.');
            if (trimmed.Length == 0)
                return true;

            var stem = trimmed.Split('.', 2)[0];
            if (ReservedWindowsNames.Contains(stem))
                return true;
        }

        return false;
    }
}
