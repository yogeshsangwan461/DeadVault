using DeadVault.Store.Models;

namespace DeadVault.Store.Interfaces;

public interface IMetadataStore
{
    string StorePath { get; }

    Task<VaultIndex> LoadIndexAsync();
    Task SaveIndexAsync(VaultIndex index);

    Task<ProjectConfig?> GetProjectAsync(string projectId);
    Task AddProjectAsync(ProjectConfig project);
    Task UpdateProjectAsync(ProjectConfig project);
    Task RemoveProjectAsync(string projectId);
    Task<List<ProjectConfig>> GetAllProjectsAsync();

    Task<AppMetadata> LoadAppMetadataAsync();
    Task SaveAppMetadataAsync(AppMetadata metadata);
}
