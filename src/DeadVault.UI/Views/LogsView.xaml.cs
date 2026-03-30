using System.Windows;
using System.Windows.Controls;
using DeadVault.Core.Services;

namespace DeadVault.UI.Views;

public partial class LogsView : UserControl
{
    public LogsView()
    {
        InitializeComponent();
        LogPathText.Text = VaultLogger.GetLogPath();
        Loaded += (s, e) => LoadLog();
    }

    private void LoadLog()
    {
        LogTextBox.Text = VaultLogger.ReadLog(1000);
        LogTextBox.ScrollToEnd();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadLog();

    private void ClearLogs_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("Clear all logs?", "Confirm",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            VaultLogger.ClearLog();
            LoadLog();
        }
    }
}
