using DeadVault.Core.Models;
using DeadVault.Store.Models;

namespace DeadVault.Core.Interfaces;

public interface IRestoreManager
{
    Task<RestoreResult> RestoreToSnapshotAsync(ProjectConfig project, string commitSha);
}
