using DeadVault.Store.Models;

namespace DeadVault.Core.Interfaces;

public interface IRepoManager
{
    Task InitializeRepoAsync(ProjectConfig project);
    bool IsInitialized(ProjectConfig project);
    string GetGitDirPath(ProjectConfig project);
    Task WriteGitIgnoreAsync(ProjectConfig project);
}
