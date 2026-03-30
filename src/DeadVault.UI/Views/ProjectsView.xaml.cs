using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
}

public partial class ProjectsView : UserControl
{
    private readonly IMetadataStore _store;
    private readonly IRepoManager _repoManager;
    private readonly ISnapshotManager _snapshotManager;
    private readonly ILockManager _lockManager;
    private readonly MainWindow _mainWindow;

    public ProjectsView(IMetadataStore store, IRepoManager repoManager,
                         ISnapshotManager snapshotManager, ILockManager lockManager,
                         MainWindow mainWindow)
    {
        _store = store;
        _repoManager = repoManager;
        _snapshotManager = snapshotManager;
        _lockManager = lockManager;
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

                displayList.Add(new ProjectDisplay
                {
                    Id = p.Id,
                    Name = p.Name,
                    FolderPath = p.FolderPath,
                    IsActive = p.IsActive,
                    StatusText = !initialized ? "Not initialized"
                        : locked ? "Locked (operation in progress)"
                        : $"Registered {p.RegisteredAt:yyyy-MM-dd}",
                });
            }

            ProjectsList.ItemsSource = displayList;
        }
        catch (Exception ex)
        {
            VaultLogger.Error("Failed to load projects", ex);
            MessageBox.Show($"Failed to load projects: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
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

        if (!_lockManager.TryAcquire(project, out string? owner))
        {
            MessageBox.Show($"Project is locked by {owner}. Try again later.", "Locked",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var result = await _snapshotManager.CreateSnapshotAsync(project, $"manual: {DateTime.Now:HH:mm:ss}");
            if (result != null)
            {
                MessageBox.Show($"Version created!\n{result.FilesChanged} files, SHA: {result.ShortSha}",
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
