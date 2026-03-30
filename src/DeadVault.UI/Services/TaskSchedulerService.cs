using System.Diagnostics;
using System.IO;
using System.Text;
using DeadVault.Core.Services;
using DeadVault.Store.Services;

namespace DeadVault.UI.Services;

public static class TaskSchedulerService
{
    private const string TaskName = "DeadVaultAgent";

    public static async Task<bool> RegisterAsync(string agentExePath)
    {
        try
        {
            string args = $"/Create /SC ONLOGON /TN \"{TaskName}\" " +
                          $"/TR \"\\\"{agentExePath}\\\"\" /RL LIMITED /F";

            var result = await RunSchtasksAsync(args);
            if (result.exitCode == 0)
            {
                VaultLogger.Info($"Registered startup task: {agentExePath}");
                return true;
            }

            VaultLogger.Warn($"Failed to register startup task: {result.output}");
            return false;
        }
        catch (Exception ex)
        {
            VaultLogger.Error("Failed to register startup task", ex);
            return false;
        }
    }

    public static async Task<bool> UnregisterAsync()
    {
        try
        {
            var result = await RunSchtasksAsync($"/Delete /TN \"{TaskName}\" /F");
            if (result.exitCode == 0)
            {
                VaultLogger.Info("Unregistered startup task");
                return true;
            }

            VaultLogger.Warn($"Failed to unregister startup task: {result.output}");
            return false;
        }
        catch (Exception ex)
        {
            VaultLogger.Error("Failed to unregister startup task", ex);
            return false;
        }
    }

    public static async Task<bool> IsRegisteredAsync()
    {
        try
        {
            var result = await RunSchtasksAsync($"/Query /TN \"{TaskName}\"");
            return result.exitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static string GetAgentExePath()
    {
        var candidates = GetAgentExePathCandidates()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return candidates.Count > 0
            ? candidates[0]
            : Path.Combine(AppContext.BaseDirectory, "DeadVault.Agent.exe");
    }

    public static IReadOnlyList<string> GetAgentExePathCandidates()
    {
        var candidates = new List<string>();

        var overridePath = Environment.GetEnvironmentVariable("DEADVAULT_AGENT_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
            candidates.Add(overridePath.Trim().Trim('"'));

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "DeadVault.Agent.exe"));

        var uiOutputDir = new DirectoryInfo(
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        var framework = uiOutputDir.Name;
        var configuration = uiOutputDir.Parent?.Name;
        var binDir = uiOutputDir.Parent?.Parent;
        var uiProjectDir = binDir?.Parent;
        var buildDir = uiProjectDir?.Parent;

        if (binDir != null &&
            uiProjectDir != null &&
            buildDir != null &&
            !string.IsNullOrWhiteSpace(configuration) &&
            !string.IsNullOrWhiteSpace(framework) &&
            string.Equals(binDir.Name, "bin", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(uiProjectDir.Name, "DeadVault.UI", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(buildDir.Name, "build", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(Path.Combine(
                buildDir.FullName,
                "DeadVault.Agent",
                "bin",
                configuration,
                framework,
                "DeadVault.Agent.exe"));
        }

        candidates.Add(DeadVaultPaths.GetBuildPath("DeadVault.Agent", "bin", "Debug", "net9.0-windows", "DeadVault.Agent.exe"));
        candidates.Add(DeadVaultPaths.GetBuildPath("DeadVault.Agent", "bin", "Release", "net9.0-windows", "DeadVault.Agent.exe"));

        return candidates;
    }

    private static async Task<(int exitCode, string output)> RunSchtasksAsync(string arguments)
    {
        var psi = new ProcessStartInfo("schtasks.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var proc = Process.Start(psi);
        if (proc == null)
            return (-1, "Failed to start schtasks.exe");

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();

        await proc.WaitForExitAsync();

        var builder = new StringBuilder();
        builder.Append((await stdoutTask).Trim());

        var stderr = (await stderrTask).Trim();
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            if (builder.Length > 0)
                builder.Append(" | ");
            builder.Append(stderr);
        }

        return (proc.ExitCode, builder.ToString());
    }
}
