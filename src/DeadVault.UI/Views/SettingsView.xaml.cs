using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using DeadVault.Core.Interfaces;
using DeadVault.Core.Services;
using DeadVault.Store.Interfaces;
using DeadVault.Store.Models;
using DeadVault.UI.Services;

namespace DeadVault.UI.Views;

public partial class SettingsView : UserControl
{
    private readonly IMetadataStore _store;
    private readonly IRepoManager _repoManager;
    private readonly PipeClient _pipeClient;
    private static readonly string[] AttributionOptions = { "Human", "AI", "Mixed" };
    private ProjectConfig? _currentProject;
    private List<ProjectConfig> _projects = new();

    public SettingsView(IMetadataStore store, IRepoManager repoManager, PipeClient pipeClient)
    {
        _store = store;
        _repoManager = repoManager;
        _pipeClient = pipeClient;

        InitializeComponent();

        DebounceSlider.ValueChanged += (s, e) =>
            DebounceValueText.Text = $"{(int)DebounceSlider.Value}s";

        Loaded += async (s, e) => await Initialize();
    }

    private async Task Initialize()
    {
        _projects = await _store.GetAllProjectsAsync();
        ProjectSelector.ItemsSource = _projects.Select(p => p.Name).ToList();
        AttributionSelector.ItemsSource = AttributionOptions;

        if (_projects.Count > 0)
        {
            ProjectSelector.SelectedIndex = 0;
            _currentProject = _projects[0];
            LoadProjectSettings();
        }

        // Check startup task status
        bool registered = await TaskSchedulerService.IsRegisteredAsync();
        StartupStatusText.Text = registered
            ? "Agent is registered to start at login."
            : "Agent is NOT registered for auto-start.";
    }

    private void ProjectSelector_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectSelector.SelectedIndex < 0) return;
        _currentProject = _projects[ProjectSelector.SelectedIndex];
        LoadProjectSettings();
    }

    private void LoadProjectSettings()
    {
        if (_currentProject == null) return;

        var exclusions = _currentProject.Exclusions;
        if (exclusions.Count == 0)
            exclusions = ProjectConfig.GetDefaultExclusions();
        ExclusionsList.ItemsSource = exclusions;

        DebounceSlider.Value = _currentProject.DebounceSeconds;
        DebounceValueText.Text = $"{_currentProject.DebounceSeconds}s";
        AttributionSelector.SelectedItem = _currentProject.AttributionAuthor switch
        {
            AttributionAuthorKinds.AI => "AI",
            AttributionAuthorKinds.Mixed => "Mixed",
            _ => "Human",
        };
        EnableWatermarkingCheck.IsChecked = _currentProject.EnableTextWatermarking;
        AttributionBudgetText.Text = $"Text watermark budget: {_currentProject.AttributionTextBudgetBytes / (1024 * 1024)} MB per snapshot";
    }

    private void AddExclusion_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProject == null) return;
        var pattern = NewPatternBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(pattern)) return;

        if (_currentProject.Exclusions.Count == 0)
            _currentProject.Exclusions = ProjectConfig.GetDefaultExclusions();

        _currentProject.Exclusions.Add(new ExclusionRule { Pattern = pattern });
        NewPatternBox.Clear();
        ExclusionsList.ItemsSource = null;
        ExclusionsList.ItemsSource = _currentProject.Exclusions;
    }

    private void RemoveExclusion_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProject == null) return;
        var pattern = (string)((Button)sender).Tag;
        _currentProject.Exclusions.RemoveAll(r => r.Pattern == pattern);
        ExclusionsList.ItemsSource = null;
        ExclusionsList.ItemsSource = _currentProject.Exclusions;
    }

    private async void ApplyExclusions_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProject == null) return;

        try
        {
            await _store.UpdateProjectAsync(_currentProject);
            await _repoManager.WriteGitIgnoreAsync(_currentProject);

            // Tell agent to reload config
            await _pipeClient.SendCommandAsync(new Core.Ipc.IpcMessage
            {
                Command = Core.Ipc.IpcMessage.Commands.ReloadConfig
            });

            MessageBox.Show("Exclusions saved and .gitignore updated.", "Settings",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save exclusions: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SaveDebounce_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProject == null) return;
        _currentProject.DebounceSeconds = (int)DebounceSlider.Value;

        try
        {
            await _store.UpdateProjectAsync(_currentProject);
            await _pipeClient.SendCommandAsync(new Core.Ipc.IpcMessage
            {
                Command = Core.Ipc.IpcMessage.Commands.ReloadConfig
            });
            MessageBox.Show($"Debounce set to {_currentProject.DebounceSeconds}s.", "Settings",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SaveAttribution_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProject == null) return;

        _currentProject.AttributionAuthor = AttributionSelector.SelectedItem switch
        {
            "AI" => AttributionAuthorKinds.AI,
            "Mixed" => AttributionAuthorKinds.Mixed,
            _ => AttributionAuthorKinds.Human,
        };
        _currentProject.EnableTextWatermarking = EnableWatermarkingCheck.IsChecked == true;

        try
        {
            await _store.UpdateProjectAsync(_currentProject);
            MessageBox.Show("Attribution settings saved.", "Settings",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save attribution settings: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RegisterStartup_Click(object sender, RoutedEventArgs e)
    {
        var agentPath = TaskSchedulerService.GetAgentExePath();
        if (!System.IO.File.Exists(agentPath))
        {
            MessageBox.Show($"Agent not found at:\n{agentPath}\n\nBuild the solution first.",
                "Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        bool ok = await TaskSchedulerService.RegisterAsync(agentPath);
        StartupStatusText.Text = ok
            ? "Agent is registered to start at login."
            : "Failed to register startup task.";
    }

    private async void UnregisterStartup_Click(object sender, RoutedEventArgs e)
    {
        bool ok = await TaskSchedulerService.UnregisterAsync();
        StartupStatusText.Text = ok
            ? "Agent is NOT registered for auto-start."
            : "Failed to unregister startup task.";
    }

    private void StartAgent_Click(object sender, RoutedEventArgs e)
    {
        var agentPath = TaskSchedulerService.GetAgentExePath();
        if (!System.IO.File.Exists(agentPath))
        {
            MessageBox.Show($"Agent not found at:\n{agentPath}\n\nBuild the solution first.",
                "Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = agentPath,
                WindowStyle = ProcessWindowStyle.Minimized,
                UseShellExecute = true,
            });
            MessageBox.Show("Agent started.", "Success",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to start agent: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
