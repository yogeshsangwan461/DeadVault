using System.Collections.Concurrent;
using DeadVault.Core.Interfaces;
using DeadVault.Core.Ipc;
using DeadVault.Core.Models;
using DeadVault.Core.Services;
using DeadVault.Store.Interfaces;
using DeadVault.Store.Models;

namespace DeadVault.Agent.Services;

public class AgentOrchestrator
{
    private readonly IMetadataStore _store;
    private readonly IRepoManager _repoManager;
    private readonly ISnapshotManager _snapshotManager;
    private readonly ILockManager _lockManager;
    private readonly VersionManager _versionManager;
    private readonly UiPipeClient _uiPipeClient;

    private readonly ConcurrentDictionary<string, FileWatcherService> _watchers = new();
    private readonly ConcurrentDictionary<string, DebounceService> _debouncers = new();
    private readonly ConcurrentDictionary<string, ProjectConfig> _projects = new();

    private bool _paused;

    public AgentOrchestrator(IMetadataStore store, IRepoManager repoManager,
                              ISnapshotManager snapshotManager, ILockManager lockManager)
    {
        _store = store;
        _repoManager = repoManager;
        _snapshotManager = snapshotManager;
        _lockManager = lockManager;
        _versionManager = new VersionManager(store);
        _uiPipeClient = new UiPipeClient();
    }

    public async Task StartAsync()
    {
        var index = await _store.LoadIndexAsync();
        int watchCount = 0;

        foreach (var project in index.Projects.Where(p => p.IsActive))
        {
            if (!_repoManager.IsInitialized(project))
            {
                VaultLogger.Warn($"Skipping {project.Name}: not initialized");
                continue;
            }

            if (!Directory.Exists(project.FolderPath))
            {
                VaultLogger.Warn($"Skipping {project.Name}: folder not found at {project.FolderPath}");
                continue;
            }

            StartWatching(project);
            watchCount++;
        }

        VaultLogger.Info($"Agent started watching {watchCount} project(s)");
        Console.WriteLine($"[DeadVault Agent] Watching {watchCount} project(s)");
    }

    private void StartWatching(ProjectConfig project)
    {
        StopWatching(project.Id); // Clean up any existing watcher

        _projects[project.Id] = project;

        var delay = project.DebounceSeconds <= 0
            ? Timeout.InfiniteTimeSpan
            : TimeSpan.FromSeconds(project.DebounceSeconds);

        var debouncer = new DebounceService(delay, () => OnDebounceElapsedAsync(project));

        var watcher = new FileWatcherService(project.FolderPath, debouncer.Signal);
        watcher.Start();

        _watchers[project.Id] = watcher;
        _debouncers[project.Id] = debouncer;

        var debounceLabel = project.DebounceSeconds <= 0 ? "disabled" : $"{project.DebounceSeconds}s";
        VaultLogger.Info($"Started watching: {project.Name} (debounce: {debounceLabel})");
    }

    private void StopWatching(string projectId)
    {
        if (_watchers.TryRemove(projectId, out var watcher))
        {
            watcher.Stop();
            watcher.Dispose();
        }
        if (_debouncers.TryRemove(projectId, out var debouncer))
        {
            debouncer.Dispose();
        }
        _projects.TryRemove(projectId, out _);
    }

    private async Task OnDebounceElapsedAsync(ProjectConfig project)
    {
        if (_paused)
        {
            VaultLogger.Info($"[{project.Name}] Skipping autosnap (paused)");
            return;
        }

        if (!_lockManager.TryAcquire(project, out string? owner))
        {
            VaultLogger.Warn($"[{project.Name}] Skipping autosnap (locked by {owner})");
            return;
        }

        try
        {
            // Record activity to keep the session alive
            _versionManager.RecordActivity(project);

            if (!await _snapshotManager.HasVersionableChangesAsync(project))
            {
                VaultLogger.Info($"[{project.Name}] No versionable changes detected; skipping");
                return;
            }

            // Determine the version bump kind
            VersionBumpKind bumpKind;

            if (_versionManager.IsAutoPatchSession(project))
            {
                // User previously chose "Don't ask, just patch"
                bumpKind = VersionBumpKind.Patch;
                VaultLogger.Info($"[{project.Name}] Auto-patch session — using Patch");
            }
            else
            {
                // Ask the UI to show the version prompt
                var currentVer = _versionManager.GetCurrentVersion(project);
                var promptResponse = await _uiPipeClient.RequestVersionPromptAsync(new VersionPromptRequest
                {
                    ProjectId = project.Id,
                    ProjectName = project.Name,
                    CurrentVersion = currentVer.ToString().TrimStart('v'),
                    FilesChanged = 0, // will be counted during snapshot
                });

                if (promptResponse == null || !promptResponse.Answered)
                {
                    // UI not running or user didn't respond — fall back to patch
                    bumpKind = VersionBumpKind.Patch;
                    VaultLogger.Info($"[{project.Name}] No UI response — defaulting to Patch");
                }
                else
                {
                    bumpKind = promptResponse.BumpKind switch
                    {
                        "minor" => VersionBumpKind.Minor,
                        "major" => VersionBumpKind.Major,
                        _ => VersionBumpKind.Patch,
                    };

                    if (promptResponse.DontAskJustPatch)
                    {
                        _versionManager.EnableAutoPatch(project);
                    }
                }
            }

            // Preview the next version and include it in the commit message.
            // Persisting the version happens only after a successful snapshot commit.
            var previousVersion = _versionManager.GetCurrentVersion(project);
            var newVersion = _versionManager.GetNextVersion(project, bumpKind);
            var commitMsg = _versionManager.BuildCommitMessage(newVersion, bumpKind);

            var result = await _snapshotManager.CreateSnapshotAsync(project, commitMsg);
            if (result != null)
            {
                await _versionManager.SetCurrentVersionAsync(
                    project,
                    newVersion,
                    $"[{project.Name}] Version bumped: {previousVersion} -> {newVersion} ({bumpKind})");

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{project.Name}] Snapshot: {result.ShortSha} [{newVersion}] [{bumpKind.ToString().ToLower()}] ({result.FilesChanged} files)");
            }
        }
        catch (Exception ex)
        {
            VaultLogger.Error($"[{project.Name}] Autosnap failed", ex);
        }
        finally
        {
            _lockManager.Release(project);
        }
    }

    public async Task<IpcResponse> HandleCommandAsync(IpcMessage message)
    {
        if (!string.Equals(message.Command, IpcMessage.Commands.Status, StringComparison.OrdinalIgnoreCase))
            VaultLogger.Info($"IPC command: {message.Command} (project: {message.ProjectId ?? "all"})");

        switch (message.Command)
        {
            case IpcMessage.Commands.Status:
                return new IpcResponse
                {
                    Success = true,
                    Message = _paused ? "paused" : "running",
                    Data = $"Watching {_watchers.Count} project(s)",
                };

            case IpcMessage.Commands.Pause:
                _paused = true;
                VaultLogger.Info("Agent paused");
                Console.WriteLine("[DeadVault Agent] Paused");
                return new IpcResponse { Success = true, Message = "Paused" };

            case IpcMessage.Commands.Resume:
                _paused = false;
                VaultLogger.Info("Agent resumed");
                Console.WriteLine("[DeadVault Agent] Resumed");
                return new IpcResponse { Success = true, Message = "Resumed" };

            case IpcMessage.Commands.SnapshotNow:
                if (message.ProjectId != null && _projects.TryGetValue(message.ProjectId, out var p))
                {
                    await OnDebounceElapsedAsync(p);
                    return new IpcResponse { Success = true, Message = "Snapshot triggered" };
                }
                // Snapshot all projects
                foreach (var proj in _projects.Values)
                {
                    await OnDebounceElapsedAsync(proj);
                }
                return new IpcResponse { Success = true, Message = "Snapshot triggered for all projects" };

            case IpcMessage.Commands.ReloadConfig:
                await ReloadConfigAsync();
                return new IpcResponse { Success = true, Message = "Config reloaded" };

            default:
                return new IpcResponse { Success = false, Message = $"Unknown command: {message.Command}" };
        }
    }

    private async Task ReloadConfigAsync()
    {
        VaultLogger.Info("Reloading configuration...");

        // Stop all current watchers
        foreach (var id in _watchers.Keys.ToList())
            StopWatching(id);

        // Restart with fresh config
        await StartAsync();
    }

    public void Stop()
    {
        foreach (var id in _watchers.Keys.ToList())
            StopWatching(id);
        VaultLogger.Info("All watchers stopped");
    }
}
