using System.Windows;
using DeadVault.Store.Models;

namespace DeadVault.UI.Views;

public class ExclusionItem
{
    public string Pattern { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsChecked { get; set; } = true;
    public bool HasDescription => !string.IsNullOrEmpty(Description);
}

public partial class ExclusionPickerWindow : Window
{
    private readonly List<ExclusionItem> _items = new();

    public List<ExclusionRule>? SelectedExclusions { get; private set; }

    public ExclusionPickerWindow(string folderPath)
    {
        InitializeComponent();

        FolderPathText.Text = folderPath;

        // Load defaults
        foreach (var rule in ProjectConfig.GetDefaultExclusions())
        {
            _items.Add(new ExclusionItem
            {
                Pattern = rule.Pattern,
                Description = rule.Description,
                IsChecked = true,
            });
        }

        ExclusionItems.ItemsSource = _items;
    }

    private void AddCustom_Click(object sender, RoutedEventArgs e)
    {
        var pattern = CustomPatternBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(pattern)) return;

        // Check for duplicates
        if (_items.Any(i => i.Pattern == pattern)) return;

        _items.Add(new ExclusionItem
        {
            Pattern = pattern,
            Description = "Custom",
            IsChecked = true,
        });

        CustomPatternBox.Clear();

        // Refresh
        ExclusionItems.ItemsSource = null;
        ExclusionItems.ItemsSource = _items;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        SelectedExclusions = _items
            .Where(i => i.IsChecked)
            .Select(i => new ExclusionRule
            {
                Pattern = i.Pattern,
                Description = i.Description,
                IsDefault = i.Description != "Custom",
            })
            .ToList();

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
