namespace DeadVault.Core.Services;

using DeadVault.Store.Services;

public static class VaultLogger
{
    private const long MaxLogBytes = 5 * 1024 * 1024;
    private const int MaxArchivedLogs = 2;
    private static readonly string LogDir = DeadVaultPaths.LogsDirectory;
    private static readonly string LogFile = Path.Combine(LogDir, "deadvault.log");
    private static readonly object WriteLock = new();

    public static void Log(string level, string message)
    {
        try
        {
            DeadVaultPaths.EnsureDirectory(LogDir);

            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";

            lock (WriteLock)
            {
                RotateIfNeeded();
                File.AppendAllText(LogFile, line + Environment.NewLine);
            }
        }
        catch
        {
            // Never crash on logging failure
        }
    }

    public static void Info(string msg) => Log("INFO", msg);
    public static void Warn(string msg) => Log("WARN", msg);
    public static void Error(string msg) => Log("ERROR", msg);
    public static void Error(string msg, Exception ex) =>
        Log("ERROR", $"{msg}: {ex.Message}\n{ex.StackTrace}");

    public static string GetLogPath() => LogFile;

    public static string ReadLog(int maxLines = 500)
    {
        try
        {
            if (!File.Exists(LogFile)) return "(No logs yet)";

            var queue = new Queue<string>(maxLines);
            foreach (var line in File.ReadLines(LogFile))
            {
                if (queue.Count == maxLines)
                    queue.Dequeue();
                queue.Enqueue(line);
            }

            return string.Join(Environment.NewLine, queue);
        }
        catch
        {
            return "(Error reading log)";
        }
    }

    public static void ClearLog()
    {
        try
        {
            lock (WriteLock)
            {
                if (File.Exists(LogFile))
                    File.WriteAllText(LogFile, string.Empty);
            }
        }
        catch
        {
            // Ignore
        }
    }

    private static void RotateIfNeeded()
    {
        if (!File.Exists(LogFile))
            return;

        var info = new FileInfo(LogFile);
        if (info.Length < MaxLogBytes)
            return;

        for (var index = MaxArchivedLogs; index >= 1; index--)
        {
            var archivePath = $"{LogFile}.{index}";
            var nextArchivePath = $"{LogFile}.{index + 1}";

            if (!File.Exists(archivePath))
                continue;

            if (index == MaxArchivedLogs)
                File.Delete(archivePath);
            else
                File.Move(archivePath, nextArchivePath, overwrite: true);
        }

        File.Move(LogFile, $"{LogFile}.1", overwrite: true);
    }
}
