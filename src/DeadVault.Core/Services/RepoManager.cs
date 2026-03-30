using LibGit2Sharp;
using DeadVault.Core.Interfaces;
using DeadVault.Store.Models;

namespace DeadVault.Core.Services;

public class RepoManager : IRepoManager
{
    public async Task InitializeRepoAsync(ProjectConfig project)
    {
        string projectPath = project.FolderPath;
        string deadvaultDir = Path.Combine(projectPath, ".deadvault");
        string dotGitPath = Path.Combine(projectPath, ".git");

        if (IsInitialized(project))
        {
            VaultLogger.Info($"Repo already initialized at {projectPath}");
            return;
        }

        await Task.Run(() =>
        {
            // If a .git directory already exists (e.g. user's own repo), abort
            if (Directory.Exists(dotGitPath))
            {
                throw new InvalidOperationException(
                    $"A .git directory already exists in {projectPath}. " +
                    "DeadVault cannot manage folders that are already git repos. " +
                    "Remove .git first or choose a different folder.");
            }

            // If a .git file already exists, remove it
            if (File.Exists(dotGitPath))
                File.Delete(dotGitPath);

            // 1. Init a standard git repo — creates .git/ directory
            Repository.Init(projectPath);

            // 2. Move .git directory to .deadvault
            Directory.Move(dotGitPath, deadvaultDir);

            // 3. Create .git file that redirects to .deadvault
            File.WriteAllText(dotGitPath, "gitdir: .deadvault\n");

            // 4. Hide the .deadvault directory
            try
            {
                File.SetAttributes(deadvaultDir,
                    File.GetAttributes(deadvaultDir) | FileAttributes.Hidden);
            }
            catch
            {
                // Non-critical, some file systems don't support hidden attr
            }

            VaultLogger.Info($"Initialized DeadVault repo at {projectPath}");
        });

        // 5. Write .gitignore based on exclusion rules
        await WriteGitIgnoreAsync(project);

        // 6. Create initial commit
        await Task.Run(() =>
        {
            using var repo = new Repository(projectPath);

            Commands.Stage(repo, ".gitignore");
            // Also stage the .git file (it's a text file pointing to .deadvault)
            Commands.Stage(repo, ".git");

            var sig = GetSignature();
            repo.Commit("DeadVault: repository initialized", sig, sig);
        });

        VaultLogger.Info($"Initial commit created for {project.Name}");
    }

    public bool IsInitialized(ProjectConfig project)
    {
        string deadvaultDir = Path.Combine(project.FolderPath, ".deadvault");
        return Directory.Exists(deadvaultDir) &&
               Directory.Exists(Path.Combine(deadvaultDir, "objects"));
    }

    public string GetGitDirPath(ProjectConfig project)
        => Path.Combine(project.FolderPath, ".deadvault");

    public async Task WriteGitIgnoreAsync(ProjectConfig project)
    {
        var lines = new List<string>
        {
            "# DeadVault managed .gitignore",
            "# Edit exclusions through the DeadVault UI",
            ".deadvault/",
            ".deadvault.lock",
        };

        var exclusions = project.Exclusions;
        if (exclusions.Count == 0)
            exclusions = ProjectConfig.GetDefaultExclusions();

        foreach (var rule in exclusions)
        {
            if (rule.Pattern == ".deadvault/" || rule.Pattern == ".deadvault.lock")
                continue; // already added
            lines.Add(rule.Pattern);
        }

        string gitignorePath = Path.Combine(project.FolderPath, ".gitignore");
        await File.WriteAllLinesAsync(gitignorePath, lines);
        VaultLogger.Info($"Updated .gitignore for {project.Name} ({exclusions.Count} rules)");
    }

    internal static Signature GetSignature()
        => new("DeadVault", "deadvault@local", DateTimeOffset.Now);
}
