using DeadVault.Core.Models;
using DeadVault.Store.Models;

namespace DeadVault.Core.Interfaces;

public interface ISnapshotManager
{
    Task<bool> HasVersionableChangesAsync(ProjectConfig project);
    Task<SnapshotInfo?> CreateSnapshotAsync(ProjectConfig project, string? message = null);
    Task<List<SnapshotInfo>> ListSnapshotsAsync(ProjectConfig project, int limit = 200);
    Task TagSnapshotAsync(ProjectConfig project, string commitSha, string tagName);
    Task RemoveTagAsync(ProjectConfig project, string tagName);
    Task<List<string>> GetTagsAsync(ProjectConfig project);
}
