namespace DeadVault.Store.Models;

public class VaultIndex
{
    public string Version { get; set; } = "1.0";
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public List<ProjectConfig> Projects { get; set; } = new();

    public void Normalize()
    {
        foreach (var project in Projects)
            project.Normalize();
    }
}
