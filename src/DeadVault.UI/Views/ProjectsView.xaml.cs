using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using DeadVault.Core.Models;
using DeadVault.Core.Interfaces;
using DeadVault.Core.Services;
using DeadVault.Store.Interfaces;
using DeadVault.Store.Models;

namespace DeadVault.UI.Views;

public class ProjectDisplay
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public string CurrentVersionLabel { get; set; } = "No version yet";
    public string SnapshotCountLabel { get; set; } = "0 versions";
    public string LastSnapshotText { get; set; } = "No snapshots yet.";
}

public partial class ProjectsView : UserControl
{
    private readonly IMetadataStore _store;
    private readonly IRepoManager _repoManager;
    private readonly ISnapshotManager _snapshotManager;
    private readonly ILockManager _lockManager;
    private readonly VersionManager _versionManager;
    private readonly MainWindow _mainWindow;

    public ProjectsView(IMetadataStore store, IRepoManager repoManager,
                         ISnapshotManager snapshotManager, ILockManager lockManager,
                         MainWindow mainWindow)
    {
        _store = store;
        _repoManager = repoManager;
        _snapshotManager = snapshotManager;
        _lockManager = lockManager;
        _versionManager = new VersionManager(store);
        _mainWindow = mainWindow;

        InitializeComponent();

        Loaded += async (s, e) => await LoadProjects();
    }

    private async Task LoadProjects()
    {
        try
        {
            var projects = await _store.GetAllProjectsAsync();
            var displayList = new List<ProjectDisplay>();

            foreach (var p in projects)
            {
                bool initialized = _repoManager.IsInitialized(p);
                bool locked = _lockManager.IsLocked(p);
                var summary = initialized
                    ? await _snapshotManager.GetProjectSummaryAsync(p)
                    : new ProjectSnapshotSummary();
                var versionLabel = string.IsNullOrWhiteSpace(p.CurrentVersion)
                    ? summary.LatestVersion is { Length: > 0 } version ? $"v{version.TrimStart('v')}" : "No version yet"
                    : $"v{p.CurrentVersion.TrimStart('v')}";
                var snapshotCount = summary.SnapshotCount;

                displayList.Add(new ProjectDisplay
                {
                    Id = p.Id,
                    Name = p.Name,
                    FolderPath = p.FolderPath,
                    IsActive = p.IsActive,
                    StatusText = !initialized ? "Not initialized"
                        : locked ? "Locked (operation in progress)"
                        : $"Registered {p.RegisteredAt:yyyy-MM-dd}",
                    CurrentVersionLabel = versionLabel,
                    SnapshotCountLabel = snapshotCount == 1 ? "1 version" : $"{snapshotCount} versions",
                    LastSnapshotText = summary.LatestTimestamp == null
                        ? "No snapshots yet. Create one now or let DeadVault capture your next edit."
                        : $"Latest snapshot: {summary.LatestTimestamp:yyyy-MM-dd HH:mm}",
                });
            }

            ProjectsList.ItemsSource = displayList;
            EmptyStatePanel.Visibility = displayList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ProjectsScrollViewer.Visibility = displayList.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            ProjectsStatusText.Text = displayList.Count == 0
                ? "No projects registered yet."
                : $"Showing {displayList.Count} project{(displayList.Count == 1 ? string.Empty : "s")}.";
        }
        catch (Exception ex)
        {
            VaultLogger.Error("Failed to load projects", ex);
            MessageBox.Show($"Failed to load projects: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            ProjectsStatusText.Text = "Failed to load projects.";
        }
    }

    private async void AddProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select a project folder to version with DeadVault",
            ShowNewFolderButton = false,
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        string folderPath = dialog.SelectedPath;
        string name = Path.GetFileName(folderPath);

        // Check if already registered
        var existing = await _store.GetAllProjectsAsync();
        if (existing.Any(p => p.FolderPath.Equals(folderPath, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("This folder is already registered.", "DeadVault",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Show exclusion picker before adding
        var picker = new ExclusionPickerWindow(folderPath);
        if (picker.ShowDialog() != true || picker.SelectedExclusions == null)
            return;

        var project = new ProjectConfig
        {
            Name = name,
            FolderPath = folderPath,
            IsActive = true,
            Exclusions = picker.SelectedExclusions,
        };

        try
        {
            // Add to store
            await _store.AddProjectAsync(project);

            // Initialize git repo
            await _repoManager.InitializeRepoAsync(project);

            VaultLogger.Info($"Added project: {name} at {folderPath}");
            await LoadProjects();
        }
        catch (Exception ex)
        {
            VaultLogger.Error($"Failed to add project {name}", ex);
            MessageBox.Show($"Failed to add project:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            // Try to clean up
            await _store.RemoveProjectAsync(project.Id);
        }
    }

    private async void SnapshotNow_Click(object sender, RoutedEventArgs e)
    {
        var projectId = (string)((Button)sender).Tag;
        var project = await _store.GetProjectAsync(projectId);
        if (project == null) return;

        var lockAttempt = await TryAcquireProjectLockAsync(project);
        if (!lockAttempt.acquired)
        {
            var sameOwner = string.Equals(lockAttempt.owner, $"{Environment.MachineName}/{Environment.UserName}", StringComparison.OrdinalIgnoreCase);
            var message = sameOwner
                ? "DeadVault is already creating a version for this project in the background. Wait a moment and try again."
                : $"Project is locked by {lockAttempt.owner}. Try again later.";
            MessageBox.Show(message, "Locked",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var currentVersion = _versionManager.GetCurrentVersion(project);
            var prompt = new VersionPromptWindow(project.Name, currentVersion, 0)
            {
                Owner = Window.GetWindow(this),
            };

            if (prompt.ShowDialog() != true || !prompt.SelectedBump.HasValue)
                return;

            var bumpKind = prompt.SelectedBump.Value;
            var targetVersion = bumpKind switch
            {
                VersionBumpKind.Custom when !string.IsNullOrWhiteSpace(prompt.CustomVersion)
                    => _versionManager.GetCustomVersion(prompt.CustomVersion),
                _ => _versionManager.GetNextVersion(project, bumpKind),
            };

            var commitMessage = _versionManager.BuildCommitMessage(targetVersion, bumpKind);
            var result = await _snapshotManager.CreateSnapshotAsync(project, commitMessage);
            if (result != null)
            {
                if (bumpKind != VersionBumpKind.Dev)
                {
                    await _versionManager.SetCurrentVersionAsync(
                        project,
                        targetVersion,
                        $"[{project.Name}] Version updated from UI: {currentVersion} -> {targetVersion} ({bumpKind})");
                }

                await LoadProjects();

                var versionLabel = bumpKind == VersionBumpKind.Dev
                    ? $"{currentVersion} (dev snapshot)"
                    : targetVersion.ToString();

                MessageBox.Show($"Version created!\n{versionLabel}\nSHA: {result.ShortSha}",
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("No changes to version.", "DeadVault",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Version creation failed: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _lockManager.Release(project);
        }
    }

    private async Task<(bool acquired, string? owner)> TryAcquireProjectLockAsync(ProjectConfig project)
    {
        string? owner = null;

        if (_lockManager.TryAcquire(project, out owner))
            return (true, owner);

        // The background agent can hold the project lock briefly while finishing an autosnap.
        // Wait a moment before surfacing a lock error to the user.
        for (var attempt = 0; attempt < 12; attempt++)
        {
            await Task.Delay(250);
            if (_lockManager.IsStale(project))
                _lockManager.ForceRelease(project);

            if (_lockManager.TryAcquire(project, out owner))
                return (true, owner);
        }

        return (false, owner);
    }

    private void OpenTimeline_Click(object sender, RoutedEventArgs e)
    {
        var projectId = (string)((Button)sender).Tag;
        _mainWindow.NavigateToTimeline(projectId);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = (string)((Button)sender).Tag;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open folder: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RemoveProject_Click(object sender, RoutedEventArgs e)
    {
        var projectId = (string)((Button)sender).Tag;
        var project = await _store.GetProjectAsync(projectId);
        if (project == null) return;

        var result = MessageBox.Show(
            $"Remove '{project.Name}' from DeadVault?\n\n" +
            "This will NOT delete any files or the .deadvault folder.\n" +
            "You can re-add the project later.",
            "Confirm Remove",
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        await _store.RemoveProjectAsync(projectId);
        await LoadProjects();
    }
}
