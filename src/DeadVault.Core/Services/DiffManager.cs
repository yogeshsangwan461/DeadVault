using System.Text.Json;
using LibGit2Sharp;
using DeadVault.Core.Interfaces;
using DeadVault.Core.Models;
using DeadVault.Store.Models;

namespace DeadVault.Core.Services;

public class DiffManager : IDiffManager
{
    private readonly AttributionService _attributionService = new();

    public async Task<DiffResult> GetCommitDiffAsync(ProjectConfig project, string commitSha)
    {
        return await Task.Run(() =>
        {
            using var repo = new Repository(project.FolderPath);
            var commit = repo.Lookup<Commit>(commitSha);
            if (commit == null)
                return new DiffResult { CommitSha = commitSha };

            var parent = commit.Parents.FirstOrDefault();
            return BuildDiffResult(repo, commit, parent, commitSha);
        });
    }

    public async Task<DiffResult> GetDiffBetweenAsync(ProjectConfig project, string fromSha, string toSha)
    {
        return await Task.Run(() =>
        {
            using var repo = new Repository(project.FolderPath);
            var fromCommit = repo.Lookup<Commit>(fromSha);
            var toCommit = repo.Lookup<Commit>(toSha);

            if (fromCommit == null || toCommit == null)
                return new DiffResult();

            return BuildDiffResult(repo, toCommit, fromCommit, toSha);
        });
    }

    public async Task<DiffResult> GetWorkingDiffAsync(ProjectConfig project)
    {
        return await Task.Run(() =>
        {
            using var repo = new Repository(project.FolderPath);
            var treeChanges = repo.Diff.Compare<TreeChanges>(repo.Head.Tip.Tree, DiffTargets.WorkingDirectory);
            var patch = repo.Diff.Compare<Patch>(repo.Head.Tip.Tree, DiffTargets.WorkingDirectory);
            var currentManifest = LoadWorkingManifest(project);
            var previousManifest = _attributionService.LoadManifestFromCommit(repo, repo.Head.Tip);

            return new DiffResult
            {
                CommitSha = "working",
                Changes = treeChanges
                    .Where(change => !AttributionService.IsInternalPath(change.Path))
                    .Select(change => CreateFileChange(change, patch, currentManifest, previousManifest))
                    .ToList(),
                PatchText = FilterPatch(patch.Content),
            };
        });
    }

    private DiffResult BuildDiffResult(Repository repo, Commit currentCommit, Commit? previousCommit, string commitSha)
    {
        var previousTree = previousCommit?.Tree;
        var treeChanges = repo.Diff.Compare<TreeChanges>(previousTree, currentCommit.Tree);
        var patch = repo.Diff.Compare<Patch>(previousTree, currentCommit.Tree);
        var currentManifest = _attributionService.LoadManifestFromCommit(repo, currentCommit);
        var previousManifest = _attributionService.LoadManifestFromCommit(repo, previousCommit);
        var attribution = _attributionService.ParseCommitTrailers(currentCommit.Message);

        return new DiffResult
        {
            CommitSha = commitSha,
            Changes = treeChanges
                .Where(change => !AttributionService.IsInternalPath(change.Path))
                .Select(change => CreateFileChange(change, patch, currentManifest, previousManifest))
                .ToList(),
            PatchText = FilterPatch(patch.Content),
            Attribution = attribution,
        };
    }

    private AttributionManifest LoadWorkingManifest(ProjectConfig project)
    {
        var manifestPath = _attributionService.GetManifestPath(project);
        if (!File.Exists(manifestPath))
            return new AttributionManifest();

        return JsonSerializer.Deserialize<AttributionManifest>(
                   File.ReadAllText(manifestPath),
                   new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
               ?? new AttributionManifest();
    }

    private static FileChange CreateFileChange(
        TreeEntryChanges change,
        Patch patch,
        AttributionManifest currentManifest,
        AttributionManifest previousManifest)
    {
        var normalizedPath = AttributionService.NormalizeGitPath(change.Path);
        var oldPath = string.IsNullOrWhiteSpace(change.OldPath)
            ? null
            : AttributionService.NormalizeGitPath(change.OldPath);

        var attributionRecord = ResolveRecord(currentManifest, previousManifest, normalizedPath, oldPath);

        return new FileChange
        {
            Path = change.Path,
            Kind = MapChangeKind(change.Status),
            LinesAdded = GetLinesAdded(patch, change.Path),
            LinesRemoved = GetLinesRemoved(patch, change.Path),
            AuthorKind = attributionRecord?.Author ?? AttributionAuthorKinds.Unknown,
            Watermark = attributionRecord?.Watermark,
        };
    }

    private static AttributionFileRecord? ResolveRecord(
        AttributionManifest currentManifest,
        AttributionManifest previousManifest,
        string currentPath,
        string? oldPath)
    {
        if (currentManifest.Files.TryGetValue(currentPath, out var current))
            return current;

        if (!string.IsNullOrWhiteSpace(oldPath) && previousManifest.Files.TryGetValue(oldPath, out var previousByOldPath))
            return previousByOldPath;

        return previousManifest.Files.TryGetValue(currentPath, out var previousByPath)
            ? previousByPath
            : null;
    }

    private static string FilterPatch(string? patchContent)
    {
        if (string.IsNullOrWhiteSpace(patchContent))
            return string.Empty;

        var sections = patchContent
            .Split("\ndiff --git ", StringSplitOptions.None)
            .Select((section, index) => index == 0 ? section : $"diff --git {section}")
            .Where(section => !section.Contains($" a/{AttributionService.ManifestFileName}", StringComparison.Ordinal))
            .ToList();

        return string.Join("\n", sections).Trim();
    }

    private static FileChangeKind MapChangeKind(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => FileChangeKind.Added,
        ChangeKind.Deleted => FileChangeKind.Deleted,
        ChangeKind.Modified => FileChangeKind.Modified,
        ChangeKind.Renamed => FileChangeKind.Renamed,
        ChangeKind.Copied => FileChangeKind.Copied,
        _ => FileChangeKind.Modified,
    };

    private static int GetLinesAdded(Patch patch, string path)
    {
        try { return patch[path]?.LinesAdded ?? 0; }
        catch { return 0; }
    }

    private static int GetLinesRemoved(Patch patch, string path)
    {
        try { return patch[path]?.LinesDeleted ?? 0; }
        catch { return 0; }
    }
}
