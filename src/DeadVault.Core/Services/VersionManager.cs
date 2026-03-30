using DeadVault.Core.Models;
using DeadVault.Store.Interfaces;
using DeadVault.Store.Models;

namespace DeadVault.Core.Services;

public class VersionManager
{
    private readonly IMetadataStore _store;

    // Per-project session state (in memory, resets on Agent restart)
    private readonly Dictionary<string, SessionState> _sessions = new();

    public VersionManager(IMetadataStore store)
    {
        _store = store;
    }

    public SemanticVersion GetCurrentVersion(ProjectConfig project)
    {
        if (SemanticVersion.TryParse(project.CurrentVersion, out var ver) && ver != null)
            return ver;
        return SemanticVersion.Initial;
    }

    public SemanticVersion GetNextVersion(ProjectConfig project, VersionBumpKind kind)
    {
        var current = GetCurrentVersion(project);
        return kind switch
        {
            VersionBumpKind.Patch => current.BumpPatch(),
            VersionBumpKind.Minor => current.BumpMinor(),
            VersionBumpKind.Major => current.BumpMajor(),
            _ => current.BumpPatch(),
        };
    }

    public async Task<SemanticVersion> BumpVersionAsync(ProjectConfig project, VersionBumpKind kind)
    {
        var current = GetCurrentVersion(project);
        var next = GetNextVersion(project, kind);
        await SetCurrentVersionAsync(project, next, $"[{project.Name}] Version bumped: {current} -> {next} ({kind})");
        return next;
    }

    public async Task SetCurrentVersionAsync(ProjectConfig project, SemanticVersion version, string? logMessage = null)
    {
        project.CurrentVersion = version.ToString().TrimStart('v');
        await _store.UpdateProjectAsync(project);

        if (!string.IsNullOrWhiteSpace(logMessage))
            VaultLogger.Info(logMessage);
    }

    /// <summary>
    /// Check if the current editing session is still active.
    /// A session expires after SessionTimeoutMinutes of inactivity.
    /// </summary>
    public bool IsSessionActive(ProjectConfig project)
    {
        if (!_sessions.TryGetValue(project.Id, out var session))
            return false;

        var timeout = TimeSpan.FromMinutes(project.SessionTimeoutMinutes);
        return (DateTime.UtcNow - session.LastActivity) < timeout;
    }

    /// <summary>
    /// Returns true if the user chose "Don't ask, just patch" for this session.
    /// Resets when session expires (2h of inactivity).
    /// </summary>
    public bool IsAutoPatchSession(ProjectConfig project)
    {
        if (!_sessions.TryGetValue(project.Id, out var session))
            return false;

        // Session expired → reset auto-patch
        var timeout = TimeSpan.FromMinutes(project.SessionTimeoutMinutes);
        if ((DateTime.UtcNow - session.LastActivity) >= timeout)
        {
            session.AutoPatch = false;
            return false;
        }

        return session.AutoPatch;
    }

    /// <summary>
    /// Enable "Don't ask, just patch" for the current session.
    /// </summary>
    public void EnableAutoPatch(ProjectConfig project)
    {
        var session = GetOrCreateSession(project);
        session.AutoPatch = true;
        VaultLogger.Info($"[{project.Name}] Auto-patch enabled for this session");
    }

    /// <summary>
    /// Record activity — keeps the session alive.
    /// </summary>
    public void RecordActivity(ProjectConfig project)
    {
        var session = GetOrCreateSession(project);
        session.LastActivity = DateTime.UtcNow;
    }

    /// <summary>
    /// Check if this is a new session (first change after 2h+ of silence).
    /// </summary>
    public bool IsNewSession(ProjectConfig project)
    {
        if (!_sessions.TryGetValue(project.Id, out var session))
            return true;

        var timeout = TimeSpan.FromMinutes(project.SessionTimeoutMinutes);
        return (DateTime.UtcNow - session.LastActivity) >= timeout;
    }

    /// <summary>
    /// Build a commit message that includes version info.
    /// </summary>
    public string BuildCommitMessage(SemanticVersion version, VersionBumpKind kind, string? userNote = null)
    {
        string kindStr = kind switch
        {
            VersionBumpKind.Patch => "patch",
            VersionBumpKind.Minor => "minor",
            VersionBumpKind.Major => "major",
            _ => "patch",
        };

        string msg = $"[{version}] [{kindStr}]";
        if (!string.IsNullOrWhiteSpace(userNote))
            msg += $" {userNote}";
        else
            msg += $" {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

        return msg;
    }

    /// <summary>
    /// Extract version from a commit message like "[v1.2.3] [patch] some note"
    /// </summary>
    public static (SemanticVersion? version, string? kind) ParseCommitMessage(string message)
    {
        SemanticVersion? version = null;
        string? kind = null;

        // Match [vX.Y.Z]
        var vMatch = System.Text.RegularExpressions.Regex.Match(message, @"\[v?(\d+\.\d+\.\d+)\]");
        if (vMatch.Success && SemanticVersion.TryParse(vMatch.Groups[1].Value, out var v))
            version = v;

        // Match [patch], [minor], [major]
        var kMatch = System.Text.RegularExpressions.Regex.Match(message, @"\[(patch|minor|major)\]");
        if (kMatch.Success)
            kind = kMatch.Groups[1].Value;

        return (version, kind);
    }

    private SessionState GetOrCreateSession(ProjectConfig project)
    {
        if (!_sessions.TryGetValue(project.Id, out var session))
        {
            session = new SessionState();
            _sessions[project.Id] = session;
        }
        return session;
    }

    private class SessionState
    {
        public DateTime LastActivity { get; set; } = DateTime.UtcNow;
        public bool AutoPatch { get; set; }
    }
}
