using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using DeadVault.Core.Models;
using DeadVault.Core.Services;
using DeadVault.Store.Services;

namespace DeadVault.UI.Views;

public partial class DiffView : UserControl
{
    private readonly DiffViewParams? _params;
    private DiffResult? _diffResult;

    public DiffView(DiffViewParams? parameters)
    {
        _params = parameters;
        InitializeComponent();
        Loaded += async (s, e) => await LoadDiff();
    }

    private async Task LoadDiff()
    {
        if (_params == null) return;

        var store = new JsonMetadataStore();
        var project = await store.GetProjectAsync(_params.ProjectId);
        if (project == null) return;

        DiffHeaderText.Text = $"Diff — {_params.CommitSha[..8]}";
        DiffInfoText.Text = $"Project: {project.Name}";

        try
        {
            var diffManager = new DiffManager();
            _diffResult = await diffManager.GetCommitDiffAsync(project, _params.CommitSha);

            FilesList.ItemsSource = _diffResult.Changes;
            if (_diffResult.Attribution.TotalFiles > 0)
            {
                DiffInfoText.Text = $"Project: {project.Name} | Author: {_diffResult.Attribution.PrimaryAuthor} | Watermarked files: {_diffResult.Attribution.WatermarkedFiles}";
            }
            RenderPatch(_diffResult.PatchText);
        }
        catch (Exception ex)
        {
            VaultLogger.Error("Failed to load diff", ex);
            var para = new Paragraph(new Run($"Error: {ex.Message}")
                { Foreground = Brushes.Red });
            DiffTextBox.Document.Blocks.Clear();
            DiffTextBox.Document.Blocks.Add(para);
        }
    }

    private void RenderPatch(string patchText)
    {
        DiffTextBox.Document.Blocks.Clear();

        if (string.IsNullOrEmpty(patchText))
        {
            DiffTextBox.Document.Blocks.Add(
                new Paragraph(new Run("(No text diff available)")
                    { Foreground = new SolidColorBrush(Color.FromRgb(0xA6, 0xAD, 0xC8)) }));
            return;
        }

        var paragraph = new Paragraph { LineHeight = 1.2 };

        foreach (var line in patchText.Split('\n'))
        {
            Brush fg;
            if (line.StartsWith('+') && !line.StartsWith("+++"))
                fg = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1)); // green
            else if (line.StartsWith('-') && !line.StartsWith("---"))
                fg = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8)); // red
            else if (line.StartsWith("@@"))
                fg = new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA)); // blue
            else if (line.StartsWith("diff ") || line.StartsWith("index ") ||
                     line.StartsWith("---") || line.StartsWith("+++"))
                fg = new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF)); // yellow
            else
                fg = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4)); // default text

            paragraph.Inlines.Add(new Run(line + "\n") { Foreground = fg });
        }

        DiffTextBox.Document.Blocks.Add(paragraph);
    }

    private void FilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // When a file is selected, scroll to its section in the patch
        if (FilesList.SelectedItem is not FileChange change) return;
        if (_diffResult == null) return;

        // Find the patch section for this file
        var marker = $"diff --git a/{change.Path}";
        var idx = _diffResult.PatchText.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return;

        // Extract just this file's patch
        var nextDiff = _diffResult.PatchText.IndexOf("\ndiff --git ", idx + 1, StringComparison.Ordinal);
        var filePatch = nextDiff > 0
            ? _diffResult.PatchText[idx..nextDiff]
            : _diffResult.PatchText[idx..];

        RenderPatch(filePatch);
    }

    private void BackToTimeline_Click(object sender, RoutedEventArgs e)
    {
        var mainWindow = Window.GetWindow(this) as MainWindow;
        mainWindow?.NavigateTo("Timeline", _params?.ProjectId);
    }
}
