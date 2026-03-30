using DeadVault.Store.Models;

namespace DeadVault.Core.Interfaces;

public interface ILockManager
{
    string GetLockPath(ProjectConfig project);
    bool TryAcquire(ProjectConfig project, out string? existingOwner);
    void Release(ProjectConfig project);
    bool IsLocked(ProjectConfig project);
    bool IsStale(ProjectConfig project);
    void ForceRelease(ProjectConfig project);
}
