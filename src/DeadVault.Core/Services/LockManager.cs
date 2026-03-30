using System.Diagnostics;
using System.Text.Json;
using DeadVault.Core.Interfaces;
using DeadVault.Store.Models;

namespace DeadVault.Core.Services;

public class LockManager : ILockManager
{
    private class LockInfo
    {
        public int Pid { get; set; }
        public string Owner { get; set; } = string.Empty;
        public string Acquired { get; set; } = string.Empty;
    }

    public string GetLockPath(ProjectConfig project)
        => Path.Combine(project.FolderPath, ".deadvault.lock");

    public bool TryAcquire(ProjectConfig project, out string? existingOwner)
    {
        existingOwner = null;
        string lockPath = GetLockPath(project);

        if (File.Exists(lockPath))
        {
            var lockInfo = ReadLockInfo(lockPath);
            if (lockInfo != null && lockInfo.Pid > 0 && !IsProcessAlive(lockInfo.Pid))
            {
                // Stale lock from dead process — remove it
                VaultLogger.Warn($"Removing stale lock from PID {lockInfo.Pid} on {project.Name}");
                File.Delete(lockPath);
            }
            else
            {
                existingOwner = lockInfo?.Owner ?? "unknown";
                return false;
            }
        }

        try
        {
            var info = new LockInfo
            {
                Pid = Environment.ProcessId,
                Owner = $"{Environment.MachineName}/{Environment.UserName}",
                Acquired = DateTime.UtcNow.ToString("o"),
            };
            File.WriteAllText(lockPath, JsonSerializer.Serialize(info));
            return true;
        }
        catch (Exception ex)
        {
            VaultLogger.Error("Failed to acquire lock", ex);
            return false;
        }
    }

    public void Release(ProjectConfig project)
    {
        string lockPath = GetLockPath(project);
        try
        {
            if (File.Exists(lockPath))
                File.Delete(lockPath);
        }
        catch (Exception ex)
        {
            VaultLogger.Error("Failed to release lock", ex);
        }
    }

    public bool IsLocked(ProjectConfig project)
        => File.Exists(GetLockPath(project));

    public bool IsStale(ProjectConfig project)
    {
        string lockPath = GetLockPath(project);
        if (!File.Exists(lockPath)) return false;

        var lockInfo = ReadLockInfo(lockPath);
        if (lockInfo == null) return true;
        return !IsProcessAlive(lockInfo.Pid);
    }

    public void ForceRelease(ProjectConfig project)
    {
        string lockPath = GetLockPath(project);
        try
        {
            if (File.Exists(lockPath))
            {
                VaultLogger.Warn($"Force releasing lock on {project.Name}");
                File.Delete(lockPath);
            }
        }
        catch (Exception ex)
        {
            VaultLogger.Error("Failed to force release lock", ex);
        }
    }

    private static LockInfo? ReadLockInfo(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<LockInfo>(json);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            Process.GetProcessById(pid);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
