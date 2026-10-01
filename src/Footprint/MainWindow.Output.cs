using System.Windows;

namespace Footprint;

public partial class MainWindow
{
    private void AppendLiveOutput(string text)
    {
        OutputBox.AppendText(text);
        if (AutoScrollBox.IsChecked == true) OutputBox.ScrollToEnd();
    }

    private void AutoScroll_Checked(object sender, RoutedEventArgs e) => OutputBox?.ScrollToEnd();

    private void CopyOutput_Click(object sender, RoutedEventArgs e)
    {
        var text = OutputBox.Text;
        if (text.Length == 0)
        {
            StatusText.Text = "コピーする出力はありません。";
            return;
        }
        try
        {
            Clipboard.SetText(text);
            StatusText.Text = "表示中の出力をコピーしました。";
        }
        catch (Exception error) { ShowError("出力をコピーできませんでした。", error); }
    }
}
