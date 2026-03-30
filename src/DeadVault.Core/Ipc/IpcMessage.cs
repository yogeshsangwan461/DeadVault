namespace DeadVault.Core.Ipc;

public class IpcMessage
{
    public string Command { get; set; } = string.Empty;
    public string? ProjectId { get; set; }
    public string? Payload { get; set; }

    public static class Commands
    {
        public const string Status = "status";
        public const string Pause = "pause";
        public const string Resume = "resume";
        public const string SnapshotNow = "snapshot_now";
        public const string ReloadConfig = "reload_config";

        // Agent → UI: ask user to pick version bump
        public const string VersionPrompt = "version_prompt";
    }
}

public class IpcResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Data { get; set; }
}

/// <summary>
/// Sent from Agent → UI via the reverse pipe to request a version prompt.
/// </summary>
public class VersionPromptRequest
{
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string CurrentVersion { get; set; } = "1.0.0";
    public int FilesChanged { get; set; }
}

/// <summary>
/// Returned from UI → Agent with the user's version choice.
/// </summary>
public class VersionPromptResponse
{
    public bool Answered { get; set; }
    public string BumpKind { get; set; } = "patch"; // "patch", "minor", "major"
    public bool DontAskJustPatch { get; set; }
}
