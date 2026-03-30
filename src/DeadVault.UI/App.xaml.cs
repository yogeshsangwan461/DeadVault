using System.Diagnostics;
using System.Drawing;
using DeadVault.Core.Services;
using DeadVault.UI.Services;
using DeadVault.Core.Ipc;
using Forms = System.Windows.Forms;

namespace DeadVault.UI;

public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _trayIcon;
    private readonly PipeClient _pipeClient = new();
    private readonly UiPipeServer _uiPipeServer = new();
    private bool _isPaused;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        VaultLogger.Info("DeadVault UI starting");

        _uiPipeServer.Start();
        BuildTrayIcon();
        _ = AutoStartAgentAsync();
    }

    private async Task AutoStartAgentAsync()
    {
        try
        {
            if (await _pipeClient.IsAgentRunningAsync())
            {
                VaultLogger.Info("Agent is already reachable on IPC pipe");
                return;
            }

            if (Process.GetProcessesByName("DeadVault.Agent").Length > 0)
            {
                VaultLogger.Info("Agent process already running; waiting for IPC pipe");
                await WaitForAgentPipeAsync();
                return;
            }

            var agentPath = TaskSchedulerService.GetAgentExePath();
            if (!System.IO.File.Exists(agentPath))
            {
                var checkedPaths = string.Join(" | ", TaskSchedulerService.GetAgentExePathCandidates());
                VaultLogger.Warn($"Agent executable not found. Checked paths: {checkedPaths}");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = agentPath,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                UseShellExecute = false,
            });

            VaultLogger.Info($"Auto-started Agent process from: {agentPath}");
            await WaitForAgentPipeAsync();
        }
        catch (Exception ex)
        {
            VaultLogger.Warn($"Failed to auto-start Agent: {ex.Message}");
        }
    }

    private async Task WaitForAgentPipeAsync()
    {
        for (int i = 0; i < 60; i++)
        {
            await Task.Delay(500);
            if (await _pipeClient.IsAgentRunningAsync())
            {
                VaultLogger.Info("Agent pipe is now available");
                return;
            }
        }

        if (Process.GetProcessesByName("DeadVault.Agent").Length > 0)
            VaultLogger.Info("Agent process is running, but IPC pipe did not become reachable during startup wait");
        else
            VaultLogger.Warn("Agent did not become reachable on IPC pipe after startup wait");
    }

    private void BuildTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = CreateDefaultIcon(),
            Text = "DeadVault - File Versioning",
            Visible = true,
        };

        _trayIcon.DoubleClick += (s, e) => ShowMainWindow();

        var menu = new Forms.ContextMenuStrip();

        var openItem = menu.Items.Add("Open DeadVault");
        openItem.Font = new Font(openItem.Font, System.Drawing.FontStyle.Bold);
        openItem.Click += (s, e) => ShowMainWindow();

        menu.Items.Add(new Forms.ToolStripSeparator());

        var pauseItem = menu.Items.Add("Pause Watching");
        pauseItem.Click += async (s, e) =>
        {
            _isPaused = !_isPaused;
            var cmd = _isPaused ? IpcMessage.Commands.Pause : IpcMessage.Commands.Resume;
            await _pipeClient.SendCommandAsync(new IpcMessage { Command = cmd });
            pauseItem.Text = _isPaused ? "Resume Watching" : "Pause Watching";
        };

        var snapshotItem = menu.Items.Add("Create Version Now");
        snapshotItem.Click += async (s, e) =>
        {
            await _pipeClient.SendCommandAsync(new IpcMessage { Command = IpcMessage.Commands.SnapshotNow });
        };

        menu.Items.Add(new Forms.ToolStripSeparator());

        var exitItem = menu.Items.Add("Exit");
        exitItem.Click += (s, e) =>
        {
            var result = System.Windows.MessageBox.Show(
                "Exit DeadVault UI?\n\nThe background agent will keep running.",
                "DeadVault",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _trayIcon!.Visible = false;
                _trayIcon.Dispose();
                Shutdown();
            }
        };

        _trayIcon.ContextMenuStrip = menu;
    }

    private void ShowMainWindow()
    {
        Dispatcher.Invoke(() =>
        {
            if (MainWindow == null)
                MainWindow = new MainWindow();
            MainWindow.Show();
            MainWindow.WindowState = WindowState.Normal;
            MainWindow.Activate();
        });
    }

    private static Icon CreateDefaultIcon()
    {
        var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            var accent = System.Drawing.Color.FromArgb(0x3B, 0x82, 0xF6);
            var dark = System.Drawing.Color.FromArgb(0x00, 0x00, 0x00);
            g.Clear(accent);
            g.FillRectangle(new System.Drawing.SolidBrush(dark), 2, 2, 12, 12);
            g.FillRectangle(new System.Drawing.SolidBrush(accent), 5, 5, 6, 6);
            g.FillRectangle(new System.Drawing.SolidBrush(dark), 7, 7, 2, 2);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _uiPipeServer.Stop();
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        VaultLogger.Info("DeadVault UI exiting");
        base.OnExit(e);
    }
}
