using System.Windows;

namespace Footprint;

public partial class MainWindow
{
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
