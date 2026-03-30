namespace DeadVault.Store.Models;

public class ExclusionRule
{
    public string Pattern { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}
