using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using DeadVault.Core.Interfaces;
using DeadVault.Core.Models;
using DeadVault.Core.Services;
using DeadVault.Store.Interfaces;
using DeadVault.Store.Models;

namespace DeadVault.UI.Views;

public class SnapshotDisplay : SnapshotInfo
{
    public bool HasTag => !string.IsNullOrEmpty(TagName);
    public bool HasVersion => !string.IsNullOrEmpty(Version);
    public bool HasBumpKind => !string.IsNullOrEmpty(BumpKind);

    public System.Windows.Media.SolidColorBrush BumpKindColor => BumpKind switch
    {
        "patch" => new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x22, 0xC5, 0x5E)), // green
        "minor" => new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x3B, 0x82, 0xF6)), // blue
        "major" => new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xF9, 0x73, 0x16)), // orange
        _ => new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x99, 0x99, 0x99)), // gray
    };

    public string RestoreLabel
    {
        get
        {
            var versionPart = !string.IsNullOrWhiteSpace(Version)
                ? Version!
                : ShortSha;

            var kindPart = !string.IsNullOrWhiteSpace(BumpKind)
                ? $" [{BumpKind}]"
                : string.Empty;

            return $"{versionPart}{kindPart} - {Timestamp:yyyy-MM-dd HH:mm}";
        }
    }

    public string AuthorBadgeText => AuthorKind switch
    {
        AttributionAuthorKinds.AI => "AI",
        AttributionAuthorKinds.Mixed => "Mixed",
        AttributionAuthorKinds.System => "System",
        _ => "Human",
    };
}

public partial class TimelineView : UserControl
{
    private readonly IMetadataStore _store;
    private readonly ISnapshotManager _snapshotManager;
    private readonly IRestoreManager _restoreManager;
    private readonly IDiffManager _diffManager;
    private readonly IExportManager _exportManager;
    private readonly ILockManager _lockManager;
    private ProjectConfig? _currentProject;
    private string? _initialProjectId;

    public TimelineView(IMetadataStore store, ISnapshotManager snapshotManager,
                         IRestoreManager restoreManager, IDiffManager diffManager,
                         IExportManager exportManager, ILockManager lockManager,
                         string? initialProjectId = null)
    {
        _store = store;
        _snapshotManager = snapshotManager;
        _restoreManager = restoreManager;
        _diffManager = diffManager;
        _exportManager = exportManager;
        _lockManager = lockManager;
        _initialProjectId = initialProjectId;

        InitializeComponent();

        Loaded += async (s, e) => await Initialize();
    }

    private async Task Initialize()
    {
        var projects = await _store.GetAllProjectsAsync();
        ProjectSelector.ItemsSource = projects.Select(p => p.Name).ToList();

        if (_initialProjectId != null)
        {
            var idx = projects.FindIndex(p => p.Id == _initialProjectId);
            if (idx >= 0)
            {
                ProjectSelector.SelectedIndex = idx;
                _currentProject = projects[idx];
            }
        }
        else if (projects.Count > 0)
        {
            ProjectSelector.SelectedIndex = 0;
            _currentProject = projects[0];
        }

        if (_currentProject != null)
            await LoadSnapshots();
    }

    private async void ProjectSelector_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectSelector.SelectedIndex < 0) return;
        var projects = await _store.GetAllProjectsAsync();
        if (ProjectSelector.SelectedIndex < projects.Count)
        {
            _currentProject = projects[ProjectSelector.SelectedIndex];
            await LoadSnapshots();
        }
    }

    private async Task LoadSnapshots()
    {
        if (_currentProject == null) return;

        ProjectNameText.Text = $"Timeline — {_currentProject.Name}";
        ProjectPathText.Text = _currentProject.FolderPath;

        // Show current version in header
        if (!string.IsNullOrEmpty(_currentProject.CurrentVersion))
        {
            CurrentVersionText.Text = $"v{_currentProject.CurrentVersion.TrimStart('v')}";
            CurrentVersionBadge.Visibility = Visibility.Visible;
        }
        else
        {
            CurrentVersionBadge.Visibility = Visibility.Collapsed;
        }

        try
        {
            var snapshots = await _snapshotManager.ListSnapshotsAsync(_currentProject);
            var displayList = snapshots.Select(s => new SnapshotDisplay
            {
                CommitSha = s.CommitSha,
                Message = s.Message,
                Timestamp = s.Timestamp,
                FilesChanged = s.FilesChanged,
                TagName = s.TagName,
                IsPreRestore = s.IsPreRestore,
                IsManual = s.IsManual,
                IsDemo = s.IsDemo,
                Version = s.Version,
                BumpKind = s.BumpKind,
            }).ToList();

            SnapshotsList.ItemsSource = displayList;
            RestoreSelector.ItemsSource = displayList;
            RestoreSelector.SelectedIndex = displayList.Count > 0 ? 0 : -1;

            // Toggle empty state
            if (displayList.Count == 0)
            {
                EmptyState.Visibility = Visibility.Visible;
                SnapshotsScroll.Visibility = Visibility.Collapsed;
            }
            else
            {
                EmptyState.Visibility = Visibility.Collapsed;
                SnapshotsScroll.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            VaultLogger.Error("Failed to load snapshots", ex);
            MessageBox.Show($"Failed to load timeline: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadSnapshots();
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProject == null) return;
        var sha = (string)((Button)sender).Tag;
        await RestoreToSnapshotAsync(sha);
    }

    private async void RestoreSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProject == null) return;
        if (RestoreSelector.SelectedItem is not SnapshotDisplay selected)
        {
            MessageBox.Show("Select a version first.", "DeadVault",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await RestoreToSnapshotAsync(selected.CommitSha);
    }

    private async Task RestoreToSnapshotAsync(string sha)
    {
        if (_currentProject == null) return;

        var selected = (SnapshotsList.ItemsSource as IEnumerable<SnapshotDisplay>)
            ?.FirstOrDefault(s => s.CommitSha == sha);

        var targetLabel = selected?.Version is { Length: > 0 }
            ? selected.Version
            : sha[..8];

        var result = MessageBox.Show(
            $"Restore to version {targetLabel} ({sha[..8]})?\n\n" +
            "A pre-restore backup will be created first.\n" +
            "You can undo this restore from the timeline.",
            "Confirm Restore",
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        if (!_lockManager.TryAcquire(_currentProject, out string? owner))
        {
            MessageBox.Show($"Project is locked by {owner}.", "Locked",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var restoreResult = await _restoreManager.RestoreToSnapshotAsync(_currentProject, sha);
            if (restoreResult.Success)
            {
                MessageBox.Show(
                    $"Restored successfully!\n\n" +
                    $"Pre-restore backup: {restoreResult.PreRestoreCommitSha}\n" +
                    $"Restored to: {restoreResult.RestoredToCommitSha[..8]}",
                    "Restore Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadSnapshots();
            }
            else
            {
                MessageBox.Show($"Restore failed: {restoreResult.ErrorMessage}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            _lockManager.Release(_currentProject);
        }
    }

    private async void Diff_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProject == null) return;
        var sha = (string)((Button)sender).Tag;

        try
        {
            var diffResult = await _diffManager.GetCommitDiffAsync(_currentProject, sha);
            var mainWindow = Window.GetWindow(this) as MainWindow;
            mainWindow?.NavigateTo("Diff", new DiffViewParams
            {
                ProjectId = _currentProject.Id,
                CommitSha = sha,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to generate diff: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Tag_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProject == null) return;
        var sha = (string)((Button)sender).Tag;

        var tagName = Microsoft.VisualBasic.Interaction.InputBox(
            "Enter a tag name (e.g., demo-v1.0):",
            "Tag Version",
            $"demo-{DateTime.Now:yyyy-MM-dd_HH-mm}");

        if (string.IsNullOrWhiteSpace(tagName)) return;

        try
        {
            await _snapshotManager.TagSnapshotAsync(_currentProject, sha, tagName);
            MessageBox.Show($"Tagged {sha[..8]} as '{tagName}'", "Tagged",
                MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadSnapshots();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to tag: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProject == null) return;
        var sha = (string)((Button)sender).Tag;

        var dialog = new SaveFileDialog
        {
            FileName = $"{_currentProject.Name}_{sha[..8]}.zip",
            Filter = "ZIP files|*.zip",
            InitialDirectory = "Z:\\",
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            await _exportManager.ExportAsZipAsync(_currentProject, sha, dialog.FileName);
            MessageBox.Show($"Exported to:\n{dialog.FileName}", "Export Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
