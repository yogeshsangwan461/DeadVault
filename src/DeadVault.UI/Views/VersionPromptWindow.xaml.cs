using System.Windows;
using DeadVault.Core.Models;

namespace DeadVault.UI.Views;

public partial class VersionPromptWindow : Window
{
    public VersionBumpKind? SelectedBump { get; private set; }
    public bool DontAskJustPatch { get; private set; }

    public VersionPromptWindow(string projectName, SemanticVersion currentVersion, int filesChanged)
    {
        InitializeComponent();

        ProjectNameText.Text = projectName;
        CurrentVersionText.Text = $"Current version: {currentVersion}";
        FilesChangedText.Text = $"{filesChanged} file(s) changed";

        // Show version previews in the named TextBlocks
        SetVersionPreviews(currentVersion);
    }

    private void SetVersionPreviews(SemanticVersion current)
    {
        // Find the named elements inside the button templates after rendering
        Loaded += (s, e) =>
        {
            var patch = FindVisualChild<System.Windows.Controls.TextBlock>(this, "PatchVersionPreview");
            var minor = FindVisualChild<System.Windows.Controls.TextBlock>(this, "MinorVersionPreview");
            var major = FindVisualChild<System.Windows.Controls.TextBlock>(this, "MajorVersionPreview");

            if (patch != null) patch.Text = $"→ {current.BumpPatch()}";
            if (minor != null) minor.Text = $"→ {current.BumpMinor()}";
            if (major != null) major.Text = $"→ {current.BumpMajor()}";
        };
    }

    private void Patch_Click(object sender, RoutedEventArgs e)
    {
        SelectedBump = VersionBumpKind.Patch;
        DialogResult = true;
        Close();
    }

    private void Minor_Click(object sender, RoutedEventArgs e)
    {
        SelectedBump = VersionBumpKind.Minor;
        DialogResult = true;
        Close();
    }

    private void Major_Click(object sender, RoutedEventArgs e)
    {
        SelectedBump = VersionBumpKind.Major;
        DialogResult = true;
        Close();
    }

    private void DontAsk_Click(object sender, RoutedEventArgs e)
    {
        SelectedBump = VersionBumpKind.Patch;
        DontAskJustPatch = true;
        DialogResult = true;
        Close();
    }

    private static T? FindVisualChild<T>(System.Windows.DependencyObject parent, string name)
        where T : System.Windows.FrameworkElement
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T fe && fe.Name == name)
                return fe;
            var found = FindVisualChild<T>(child, name);
            if (found != null)
                return found;
        }
        return null;
    }
}
