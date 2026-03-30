namespace DeadVault.Store.Models;

public class AppMetadata
{
    public bool AgentAutoStart { get; set; } = true;
    public bool AgentPaused { get; set; }
    public string LogLevel { get; set; } = "Info";
    public int MaxRetentionSnapshots { get; set; } = 0; // 0 = unlimited
}
