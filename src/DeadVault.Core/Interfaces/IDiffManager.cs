using DeadVault.Core.Models;
using DeadVault.Store.Models;

namespace DeadVault.Core.Interfaces;

public interface IDiffManager
{
    Task<DiffResult> GetCommitDiffAsync(ProjectConfig project, string commitSha);
    Task<DiffResult> GetDiffBetweenAsync(ProjectConfig project, string fromSha, string toSha);
    Task<DiffResult> GetWorkingDiffAsync(ProjectConfig project);
}
