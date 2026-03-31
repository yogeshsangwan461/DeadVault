using System.Windows;
using System.Windows.Controls;
using DeadVault.Core.Services;

namespace DeadVault.UI.Views;

public partial class WatermarkView : UserControl
{
    public WatermarkView()
    {
        InitializeComponent();
    }

    private void GenerateTextUid_Click(object sender, RoutedEventArgs e)
    {
        TextUidBox.Text = TextCodeWatermarkService.GenerateUid();
    }

    private void EmbedText_Click(object sender, RoutedEventArgs e)
    {
        var input = TextInputBox.Text ?? string.Empty;
        var uid = TextUidBox.Text ?? string.Empty;

        TextOutputBox.Text = TextCodeWatermarkService.EmbedText(input, uid);
        TextStatusText.Text = "Embedded text watermark.";
    }

    private void DetectText_Click(object sender, RoutedEventArgs e)
    {
        var input = TextOutputBox.Text?.Length > 0 ? TextOutputBox.Text : TextInputBox.Text;
        input ??= string.Empty;

        if (TextCodeWatermarkService.TryExtractTextUid(input, out var uid, out var error))
            TextStatusText.Text = $"Detected text watermark UID: {uid}";
        else
            TextStatusText.Text = error;
    }

    private void GenerateCodeUid_Click(object sender, RoutedEventArgs e)
    {
        CodeUidBox.Text = TextCodeWatermarkService.GenerateUid();
    }

    private void MarkCode_Click(object sender, RoutedEventArgs e)
    {
        var code = CodeInputBox.Text ?? string.Empty;
        var uid = (CodeUidBox.Text ?? string.Empty).Trim();
        if (uid.Length == 0)
            uid = TextCodeWatermarkService.GenerateUid();

        var model = GetSelectedTag(AiModelSelector, "unknown-ai");
        var lang = GetSelectedTag(LanguageSelector, "auto");

        var payload = $"uid={uid};model={model};lang={lang}";

        CodeOutputBox.Text = TextCodeWatermarkService.EmbedCode(code, payload);
        CodeStatusText.Text = $"Marked code. UID: {uid}";
    }

    private void DetectCodeFromMarkTab_Click(object sender, RoutedEventArgs e)
    {
        var input = CodeOutputBox.Text?.Length > 0 ? CodeOutputBox.Text : CodeInputBox.Text;
        input ??= string.Empty;

        if (TextCodeWatermarkService.TryExtractCodePayload(input, out var payload, out var error))
            CodeStatusText.Text = $"Detected code watermark payload: {payload}";
        else
            CodeStatusText.Text = error;
    }

    private void DetectCode_Click(object sender, RoutedEventArgs e)
    {
        var input = CodeDetectInputBox.Text ?? string.Empty;
        if (TextCodeWatermarkService.TryExtractCodePayload(input, out var payload, out var error))
            CodeDetectStatusText.Text = $"Detected code watermark payload: {payload}";
        else
            CodeDetectStatusText.Text = error;
    }

    private void DetectAiText_Click(object sender, RoutedEventArgs e)
    {
        var input = AiTextDetectInputBox.Text ?? string.Empty;
        if (TextCodeWatermarkService.TryExtractTextUid(input, out var uid, out var error))
            AiTextDetectStatusText.Text = $"Detected text watermark UID: {uid}";
        else
            AiTextDetectStatusText.Text = error;
    }

    private static string GetSelectedTag(System.Windows.Controls.ComboBox comboBox, string fallback)
    {
        if (comboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag && !string.IsNullOrWhiteSpace(tag))
            return tag;
        return fallback;
    }
}
