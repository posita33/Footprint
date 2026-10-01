using System.IO;
using System.Text;
using System.Windows;
using Footprint.Core;
using Microsoft.Win32;

namespace Footprint;

public partial class MainWindow
{
    private readonly OutputPages _outputPages = new();
    private int _outputPageIndex;

    private void SetOutput(string text)
    {
        _outputPages.Reset(text);
        _outputPageIndex = AutoScrollBox.IsChecked == true ? _outputPages.PageCount - 1 : 0;
        OutputLimitText.Visibility = text.Contains(CommandOutputCapture.LegacyTruncationNotice)
            ? Visibility.Visible : Visibility.Collapsed;
        RenderOutputPage();
    }

    private void AppendLiveOutput(string text)
    {
        var previousPage = _outputPages.GetPage(_outputPageIndex);
        _outputPages.Append(text);
        if (AutoScrollBox.IsChecked == true) _outputPageIndex = _outputPages.PageCount - 1;
        var page = _outputPages.GetPage(_outputPageIndex);
        // Keep the user's scroll position when the current page has not changed.
        if (page != previousPage || OutputBox.Text != page)
        {
            if (page.StartsWith(OutputBox.Text, StringComparison.Ordinal))
                OutputBox.AppendText(page[OutputBox.Text.Length..]);
            else OutputBox.Text = page;
        }
        UpdateOutputPageControls();
        if (AutoScrollBox.IsChecked == true) OutputBox.ScrollToEnd();
    }

    private void RenderOutputPage()
    {
        OutputBox.Text = _outputPages.GetPage(_outputPageIndex);
        UpdateOutputPageControls();
        if (AutoScrollBox.IsChecked == true) OutputBox.ScrollToEnd(); else OutputBox.ScrollToHome();
    }

    private void UpdateOutputPageControls()
    {
        OutputPageText.Text = $"{_outputPageIndex + 1} / {_outputPages.PageCount} ページ · {_outputPages.Length:N0} 文字";
        FirstOutputPageButton.IsEnabled = PreviousOutputPageButton.IsEnabled = _outputPageIndex > 0;
        LastOutputPageButton.IsEnabled = NextOutputPageButton.IsEnabled = _outputPageIndex < _outputPages.PageCount - 1;
        CopyFullOutputButton.IsEnabled = CopyOutputPageButton.IsEnabled = SaveOutputButton.IsEnabled = _outputPages.Length > 0;
    }

    private void GoToOutputPage(int page)
    {
        AutoScrollBox.IsChecked = false;
        _outputPageIndex = Math.Clamp(page, 0, _outputPages.PageCount - 1);
        RenderOutputPage();
    }

    private void FirstOutputPage_Click(object sender, RoutedEventArgs e) => GoToOutputPage(0);
    private void PreviousOutputPage_Click(object sender, RoutedEventArgs e) => GoToOutputPage(_outputPageIndex - 1);
    private void NextOutputPage_Click(object sender, RoutedEventArgs e) => GoToOutputPage(_outputPageIndex + 1);
    private void LastOutputPage_Click(object sender, RoutedEventArgs e) => GoToOutputPage(_outputPages.PageCount - 1);

    private void AutoScroll_Checked(object sender, RoutedEventArgs e)
    {
        if (OutputBox is null || OutputPageText is null) return;
        _outputPageIndex = _outputPages.PageCount - 1;
        RenderOutputPage();
    }

    private void CopyOutput_Click(object sender, RoutedEventArgs e) => CopyOutput(_outputPages.FullText, "全出力をコピーしました。");
    private void CopyOutputPage_Click(object sender, RoutedEventArgs e) => CopyOutput(OutputBox.Text, "表示ページをコピーしました。");

    private void CopyOutput(string text, string success)
    {
        if (text.Length == 0) { StatusText.Text = "コピーする出力はありません。"; return; }
        try { Clipboard.SetText(text); StatusText.Text = success; }
        catch (Exception error) { ShowError("出力をコピーできませんでした。", error); }
    }

    private async void SaveOutput_Click(object sender, RoutedEventArgs e)
    {
        var text = _outputPages.FullText;
        if (text.Length == 0) { StatusText.Text = "書き出す出力はありません。"; return; }
        var dialog = new SaveFileDialog { Filter = "テキストファイル (*.txt)|*.txt|すべてのファイル (*.*)|*.*", FileName = "Footprint-output.txt" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await File.WriteAllTextAsync(dialog.FileName, text, new UTF8Encoding(false));
            StatusText.Text = "全出力をUTF-8テキストに書き出しました。";
        }
        catch (Exception error) { ShowError("出力を書き出せませんでした。", error); }
    }
}
