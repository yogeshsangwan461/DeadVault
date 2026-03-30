using System.IO.Compression;
using LibGit2Sharp;
using DeadVault.Core.Interfaces;
using DeadVault.Store.Models;

namespace DeadVault.Core.Services;

public class ExportManager : IExportManager
{
    public async Task<string> ExportAsZipAsync(ProjectConfig project, string commitSha, string outputPath)
    {
        return await Task.Run(() =>
        {
            VaultLogger.Info($"[{project.Name}] Exporting {commitSha[..8]} to {outputPath}");

            using var repo = new Repository(project.FolderPath);
            var commit = repo.Lookup<Commit>(commitSha);

            if (commit == null)
                throw new InvalidOperationException($"Commit {commitSha} not found.");

            // Ensure output directory exists
            var dir = Path.GetDirectoryName(outputPath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using var zipStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);

            WriteTreeToZip(commit.Tree, archive, string.Empty);

            VaultLogger.Info($"[{project.Name}] Export complete: {outputPath}");
            return outputPath;
        });
    }

    private static void WriteTreeToZip(Tree tree, ZipArchive archive, string prefix)
    {
        foreach (var entry in tree)
        {
            string entryPath = string.IsNullOrEmpty(prefix)
                ? entry.Name
                : $"{prefix}/{entry.Name}";

            // Skip .deadvault and .git entries
            if (entry.Name == ".deadvault" || entry.Name == ".git" ||
                entry.Name == ".deadvault.lock" || entry.Name == ".gitignore")
                continue;

            if (entry.TargetType == TreeEntryTargetType.Blob)
            {
                var blob = (Blob)entry.Target;
                var zipEntry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
                using var entryStream = zipEntry.Open();
                using var blobStream = blob.GetContentStream();
                blobStream.CopyTo(entryStream);
            }
            else if (entry.TargetType == TreeEntryTargetType.Tree)
            {
                WriteTreeToZip((Tree)entry.Target, archive, entryPath);
            }
        }
    }
}
