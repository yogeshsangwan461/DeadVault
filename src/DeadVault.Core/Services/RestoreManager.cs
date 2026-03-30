using LibGit2Sharp;
using DeadVault.Core.Interfaces;
using DeadVault.Core.Models;
using DeadVault.Store.Models;

namespace DeadVault.Core.Services;

public class RestoreManager : IRestoreManager
{
    private readonly ISnapshotManager _snapshotManager;

    public RestoreManager(ISnapshotManager snapshotManager)
    {
        _snapshotManager = snapshotManager;
    }

    public async Task<RestoreResult> RestoreToSnapshotAsync(ProjectConfig project, string commitSha)
    {
        try
        {
            // 1. ALWAYS create a pre-restore snapshot
            VaultLogger.Info($"[{project.Name}] Creating pre-restore snapshot before restoring to {commitSha[..8]}");
            var preRestore = await _snapshotManager.CreateSnapshotAsync(
                project,
                $"PRE-RESTORE: backup before restoring to {commitSha[..8]}");

            string preRestoreSha = preRestore?.CommitSha ?? "(no changes to save)";

            // 2. Perform the restore
            return await Task.Run(() =>
            {
                using var repo = new Repository(project.FolderPath);
                var targetCommit = repo.Lookup<Commit>(commitSha);

                if (targetCommit == null)
                {
                    VaultLogger.Error($"[{project.Name}] Commit {commitSha} not found");
                    return new RestoreResult
                    {
                        Success = false,
                        ErrorMessage = $"Commit {commitSha} not found in repository.",
                    };
                }

                // Hard reset to target commit
                repo.Reset(ResetMode.Hard, targetCommit);

                // Count restored files
                int filesRestored = 0;
                try
                {
                    if (targetCommit.Parents.Any())
                    {
                        var parent = targetCommit.Parents.First();
                        var changes = repo.Diff.Compare<TreeChanges>(parent.Tree, targetCommit.Tree);
                        filesRestored = changes.Count;
                    }
                }
                catch { /* non-critical */ }

                VaultLogger.Info($"[{project.Name}] Restored to {commitSha[..8]} ({filesRestored} files). Pre-restore: {preRestoreSha}");

                return new RestoreResult
                {
                    Success = true,
                    PreRestoreCommitSha = preRestoreSha,
                    RestoredToCommitSha = commitSha,
                    FilesRestored = filesRestored,
                };
            });
        }
        catch (Exception ex)
        {
            VaultLogger.Error($"[{project.Name}] Restore failed", ex);
            return new RestoreResult
            {
                Success = false,
                ErrorMessage = ex.Message,
            };
        }
    }
}
