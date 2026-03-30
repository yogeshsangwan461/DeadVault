using DeadVault.Store.Models;

namespace DeadVault.Core.Interfaces;

public interface IExportManager
{
    Task<string> ExportAsZipAsync(ProjectConfig project, string commitSha, string outputPath);
}
