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
    private static readonly string[] AttributionDetailPresets =
    {
        "",
        "gpt-5.4",
        "gpt-4.1",
        "claude-3.7",
        "claude-3.5-sonnet",
        "gemini-2.5",
        "copilot",
        "cursor",
        "local-llama",
    };
    private ProjectConfig? _currentProject;
    private List<ProjectConfig> _projects = new();
    private bool _updatingDebounceUi;

    public SettingsView(IMetadataStore store, IRepoManager repoManager, PipeClient pipeClient)
    {
        _store = store;
        _repoManager = repoManager;
        _pipeClient = pipeClient;

        InitializeComponent();

        DebounceSlider.ValueChanged += (s, e) =>
        {
            if (DisableAutoVersioningCheck.IsChecked == true)
                return;

            if (_updatingDebounceUi)
                return;

            _updatingDebounceUi = true;
            try
            {
                DebounceSecondsBox.Text = ((int)DebounceSlider.Value).ToString();
            }
            finally
            {
                _updatingDebounceUi = false;
            }
        };

        DebounceSecondsBox.TextChanged += (s, e) =>
        {
            if (DisableAutoVersioningCheck.IsChecked == true)
                return;
            if (_updatingDebounceUi)
                return;

            if (!int.TryParse(DebounceSecondsBox.Text.Trim(), out var seconds))
                return;

            if (seconds <= 0)
                return;

            _updatingDebounceUi = true;
            try
            {
                // Slider is a quick-pick; clamp for display, but preserve the typed value for saving.
                DebounceSlider.Value = Math.Clamp(seconds, (int)DebounceSlider.Minimum, (int)DebounceSlider.Maximum);
            }
            finally
            {
                _updatingDebounceUi = false;
            }
        };

        DisableAutoVersioningCheck.Checked += (s, e) => UpdateDebounceUi();
        DisableAutoVersioningCheck.Unchecked += (s, e) => UpdateDebounceUi();

        Loaded += async (s, e) => await Initialize();
    }

    private async Task Initialize()
    {
        _projects = await _store.GetAllProjectsAsync();
        ProjectSelector.ItemsSource = _projects.Select(p => p.Name).ToList();
        AttributionSelector.ItemsSource = AttributionOptions;
        AttributionDetailPresetSelector.ItemsSource = AttributionDetailPresets;

        AttributionDetailPresetSelector.SelectionChanged += (s, e) =>
        {
            if (AttributionDetailPresetSelector.SelectedItem is not string preset)
                return;
            if (string.IsNullOrWhiteSpace(preset))
                return;

            AttributionDetailBox.Text = preset;
        };

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

        DisableAutoVersioningCheck.IsChecked = _currentProject.DebounceSeconds <= 0;
        var seconds = _currentProject.DebounceSeconds;
        DebounceSlider.Value = seconds > 0
            ? Math.Clamp(seconds, (int)DebounceSlider.Minimum, (int)DebounceSlider.Maximum)
            : 60;

        _updatingDebounceUi = true;
        try
        {
            DebounceSecondsBox.Text = seconds > 0 ? seconds.ToString() : "Disabled";
        }
        finally
        {
            _updatingDebounceUi = false;
        }

        UpdateDebounceUi();

        var (authorKind, authorDetail) = AttributionAuthorKinds.Parse(_currentProject.AttributionAuthor);
        authorKind = AttributionAuthorKinds.NormalizeKind(authorKind);

        AttributionSelector.SelectedItem = authorKind switch
        {
            AttributionAuthorKinds.AI => "AI",
            AttributionAuthorKinds.Mixed => "Mixed",
            _ => "Human",
        };
        AttributionDetailBox.Text = authorDetail ?? string.Empty;
        AttributionDetailPresetSelector.SelectedItem = AttributionDetailPresets
            .FirstOrDefault(p => string.Equals(p, authorDetail ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            ?? AttributionDetailPresets[0];

        WatermarkingSourceBox.Text = _currentProject.WatermarkingSource ?? string.Empty;

        EnableWatermarkingCheck.IsChecked = _currentProject.EnableTextWatermarking;
        AttributionBudgetText.Text = $"Text watermark budget: {_currentProject.AttributionTextBudgetBytes / (1024 * 1024)} MB per snapshot";
    }

    private void UpdateDebounceUi()
    {
        bool disabled = DisableAutoVersioningCheck.IsChecked == true;
        DebounceSlider.IsEnabled = !disabled;
        DebounceSecondsBox.IsEnabled = !disabled;

        if (disabled)
        {
            _updatingDebounceUi = true;
            try
            {
                DebounceSecondsBox.Text = "Disabled";
            }
            finally
            {
                _updatingDebounceUi = false;
            }
        }
        else if (string.Equals(DebounceSecondsBox.Text, "Disabled", StringComparison.OrdinalIgnoreCase) ||
                 string.IsNullOrWhiteSpace(DebounceSecondsBox.Text))
        {
            _updatingDebounceUi = true;
            try
            {
                DebounceSecondsBox.Text = ((int)DebounceSlider.Value).ToString();
            }
            finally
            {
                _updatingDebounceUi = false;
            }
        }
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
        if (DisableAutoVersioningCheck.IsChecked == true)
        {
            _currentProject.DebounceSeconds = 0;
        }
        else
        {
            var raw = DebounceSecondsBox.Text.Trim();
            if (!int.TryParse(raw, out var seconds))
            {
                MessageBox.Show("Enter a valid number of seconds (or disable for infinity).", "Settings",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _currentProject.DebounceSeconds = seconds <= 0 ? 0 : seconds;
        }

        try
        {
            await _store.UpdateProjectAsync(_currentProject);
            await _pipeClient.SendCommandAsync(new Core.Ipc.IpcMessage
            {
                Command = Core.Ipc.IpcMessage.Commands.ReloadConfig
            });
            var label = _currentProject.DebounceSeconds <= 0 ? "disabled" : $"{_currentProject.DebounceSeconds}s";
            MessageBox.Show($"Auto-version delay set to {label}.", "Settings",
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

        var kind = AttributionSelector.SelectedItem switch
        {
            "AI" => AttributionAuthorKinds.AI,
            "Mixed" => AttributionAuthorKinds.Mixed,
            _ => AttributionAuthorKinds.Human,
        };
        var detail = AttributionDetailBox.Text.Trim();
        _currentProject.AttributionAuthor = AttributionAuthorKinds.NormalizeWithDetail(
            string.IsNullOrWhiteSpace(detail) ? kind : $"{kind}:{detail}");
        _currentProject.EnableTextWatermarking = EnableWatermarkingCheck.IsChecked == true;
        _currentProject.WatermarkingSource = WatermarkingSourceBox.Text.Trim();

        try
        {
            await _store.UpdateProjectAsync(_currentProject);

            // Apply changes immediately for the agent.
            await _pipeClient.SendCommandAsync(new Core.Ipc.IpcMessage
            {
                Command = Core.Ipc.IpcMessage.Commands.ReloadConfig
            });

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
