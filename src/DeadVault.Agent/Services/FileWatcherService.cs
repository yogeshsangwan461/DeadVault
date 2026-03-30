using DeadVault.Core.Services;

namespace DeadVault.Agent.Services;

public class FileWatcherService : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly Action _onChangeDetected;

    public FileWatcherService(string path, Action onChangeDetected)
    {
        _onChangeDetected = onChangeDetected;

        _watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName |
                           NotifyFilters.DirectoryName |
                           NotifyFilters.LastWrite |
                           NotifyFilters.Size,
            InternalBufferSize = 65536, // 64KB to reduce missed events
            EnableRaisingEvents = false,
        };

        _watcher.Changed += OnEvent;
        _watcher.Created += OnEvent;
        _watcher.Deleted += OnEvent;
        _watcher.Renamed += OnRenamedEvent;
        _watcher.Error += OnError;
    }

    public void Start()
    {
        _watcher.EnableRaisingEvents = true;
        VaultLogger.Info($"Watcher started for: {_watcher.Path}");
    }

    public void Stop()
    {
        _watcher.EnableRaisingEvents = false;
        VaultLogger.Info($"Watcher stopped for: {_watcher.Path}");
    }

    private void OnEvent(object sender, FileSystemEventArgs e)
    {
        if (ShouldIgnore(e.FullPath)) return;
        _onChangeDetected();
    }

    private void OnRenamedEvent(object sender, RenamedEventArgs e)
    {
        if (ShouldIgnore(e.FullPath)) return;
        _onChangeDetected();
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        VaultLogger.Error($"FileSystemWatcher error: {e.GetException().Message}");
        // Try to restart the watcher
        try
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            VaultLogger.Error("Failed to restart watcher", ex);
        }
    }

    private static bool ShouldIgnore(string path)
    {
        // Ignore DeadVault internal files
        if (path.Contains(".deadvault") || path.Contains(".deadvault.lock") || path.Contains(".deadvault.attribution.json"))
            return true;
        // Ignore .git (the redirect file or any git internals)
        if (path.Contains(Path.DirectorySeparatorChar + ".git") ||
            path.EndsWith(".git"))
            return true;
        return false;
    }

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
    }
}
