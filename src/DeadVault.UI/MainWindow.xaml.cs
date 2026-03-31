using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Diagnostics;
using DeadVault.UI.Views;
using DeadVault.UI.Services;
using DeadVault.Store.Services;
using DeadVault.Core.Services;

namespace DeadVault.UI;

public partial class MainWindow : Window
{
    private readonly JsonMetadataStore _store = new();
    private readonly RepoManager _repoManager = new();
    private readonly SnapshotManager _snapshotManager = new();
    private readonly RestoreManager _restoreManager;
    private readonly ExportManager _exportManager = new();
    private readonly LockManager _lockManager = new();
    private readonly PipeClient _pipeClient = new();
    private readonly DispatcherTimer _statusTimer;
    private int _statusCheckInProgress;

    public MainWindow()
    {
        _restoreManager = new RestoreManager(_snapshotManager, _store);
        InitializeComponent();

        // Initial navigation after component is ready
        NavigateTo("Projects");

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _statusTimer.Tick += async (s, e) => await CheckAgentStatus();
        _statusTimer.Start();

        // Delay first check to give Agent time to start
        _ = DelayedFirstCheck();
    }

    private async Task DelayedFirstCheck()
    {
        await Task.Delay(2500);
        await CheckAgentStatus();
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Guard: ContentArea isn't available during InitializeComponent
        if (ContentArea == null) return;
        if (NavList.SelectedItem is ListBoxItem item && item.Tag is string tag)
            NavigateTo(tag);
    }

    public void NavigateTo(string viewName, object? parameter = null)
    {
        if (ContentArea == null) return;
        ContentArea.Content = viewName switch
        {
            "Projects" => new ProjectsView(_store, _repoManager, _snapshotManager, _lockManager, this),
            "Timeline" => new TimelineView(_store, _snapshotManager, _restoreManager,
                                           _exportManager, _lockManager, parameter as string),
            "Watermark" => new WatermarkView(),
            "Settings" => new SettingsView(_store, _repoManager, _pipeClient),
            "Logs" => new LogsView(),
            "Diff" => new DiffView(parameter as DiffViewParams),
            _ => new ProjectsView(_store, _repoManager, _snapshotManager, _lockManager, this),
        };
    }

    public void NavigateToTimeline(string projectId)
    {
        // Update sidebar selection
        foreach (ListBoxItem item in NavList.Items)
        {
            if (item.Tag?.ToString() == "Timeline")
            {
                NavList.SelectedItem = item;
                break;
            }
        }
        NavigateTo("Timeline", projectId);
    }

    private async Task CheckAgentStatus()
    {
        if (Interlocked.Exchange(ref _statusCheckInProgress, 1) == 1)
            return;

        try
        {
            bool runningViaPipe = await _pipeClient.IsAgentRunningAsync();
            bool runningViaProcess = Process.GetProcessesByName("DeadVault.Agent").Length > 0;

            bool running = runningViaPipe || runningViaProcess;
            AgentStatusText.Text = running ? "Running" : "Not Running";
            AgentStatusText.Foreground = running
            ? (System.Windows.Media.Brush)FindResource("GreenBrush")
            : (System.Windows.Media.Brush)FindResource("RedBrush");
        }
        finally
        {
            Interlocked.Exchange(ref _statusCheckInProgress, 0);
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}

public class DiffViewParams
{
    public string ProjectId { get; set; } = string.Empty;
    public string CommitSha { get; set; } = string.Empty;
}
