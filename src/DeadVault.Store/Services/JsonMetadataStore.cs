using System.Text.Json;
using DeadVault.Store.Interfaces;
using DeadVault.Store.Models;

namespace DeadVault.Store.Services;

public class JsonMetadataStore : IMetadataStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly SemaphoreSlim _lock = new(1, 1);

    public string StorePath => DeadVaultPaths.GetDataPath("index.json");
    private string AppMetaPath => DeadVaultPaths.GetDataPath("appmeta.json");

    public async Task<VaultIndex> LoadIndexAsync()
    {
        EnsureDataDir();
        if (!File.Exists(StorePath))
            return new VaultIndex();

        await _lock.WaitAsync();
        try
        {
            var json = await File.ReadAllTextAsync(StorePath);
            var index = JsonSerializer.Deserialize<VaultIndex>(json, JsonOpts) ?? new VaultIndex();
            index.Normalize();
            return index;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveIndexAsync(VaultIndex index)
    {
        EnsureDataDir();
        index.LastUpdated = DateTime.UtcNow;

        await _lock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(index, JsonOpts);
            await File.WriteAllTextAsync(StorePath, json);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ProjectConfig?> GetProjectAsync(string projectId)
    {
        var index = await LoadIndexAsync();
        return index.Projects.FirstOrDefault(p => p.Id == projectId);
    }

    public async Task AddProjectAsync(ProjectConfig project)
    {
        project.Normalize();
        await MutateIndexAsync(index => index.Projects.Add(project));
    }

    public async Task UpdateProjectAsync(ProjectConfig project)
    {
        project.Normalize();
        await MutateIndexAsync(index =>
        {
            var existing = index.Projects.FindIndex(p => p.Id == project.Id);
            if (existing >= 0)
                index.Projects[existing] = project;
        });
    }

    public async Task RemoveProjectAsync(string projectId)
    {
        await MutateIndexAsync(index => index.Projects.RemoveAll(p => p.Id == projectId));
    }

    public async Task<List<ProjectConfig>> GetAllProjectsAsync()
    {
        var index = await LoadIndexAsync();
        return index.Projects;
    }

    public async Task<AppMetadata> LoadAppMetadataAsync()
    {
        EnsureDataDir();
        if (!File.Exists(AppMetaPath))
            return new AppMetadata();

        await _lock.WaitAsync();
        try
        {
            var json = await File.ReadAllTextAsync(AppMetaPath);
            return JsonSerializer.Deserialize<AppMetadata>(json, JsonOpts) ?? new AppMetadata();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAppMetadataAsync(AppMetadata metadata)
    {
        EnsureDataDir();

        await _lock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(metadata, JsonOpts);
            await File.WriteAllTextAsync(AppMetaPath, json);
        }
        finally
        {
            _lock.Release();
        }
    }

    private void EnsureDataDir()
    {
        DeadVaultPaths.EnsureDirectory(DeadVaultPaths.DataDirectory);
    }

    private async Task MutateIndexAsync(Action<VaultIndex> mutation)
    {
        EnsureDataDir();

        await _lock.WaitAsync();
        try
        {
            VaultIndex index;
            if (!File.Exists(StorePath))
            {
                index = new VaultIndex();
            }
            else
            {
                var json = await File.ReadAllTextAsync(StorePath);
                index = JsonSerializer.Deserialize<VaultIndex>(json, JsonOpts) ?? new VaultIndex();
            }

            index.Normalize();
            mutation(index);
            index.LastUpdated = DateTime.UtcNow;

            var updatedJson = JsonSerializer.Serialize(index, JsonOpts);
            await File.WriteAllTextAsync(StorePath, updatedJson);
        }
        finally
        {
            _lock.Release();
        }
    }
}
