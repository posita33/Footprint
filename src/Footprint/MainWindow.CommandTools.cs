using System.Windows;
using System.Windows.Controls;
using Footprint.Core;

namespace Footprint;

public partial class MainWindow
{
    private void CommandWrap_Click(object sender, RoutedEventArgs e)
    {
        var wrap = CommandWrapButton.IsChecked == true;
        CommandBox.TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        CommandBox.HorizontalScrollBarVisibility = wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        CommandWrapButton.ToolTip = wrap ? "右端で折り返し中（クリックで折り返し解除）" : "折り返しなし（クリックで右端で折り返し）";
    }

    private void FormatCommand_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellation is not null || _updateInProgress) return;
        if (!CommandFormatter.TryFormat(CommandBox.Text,
            ShellBox.SelectedIndex == 0 ? ShellKind.CommandPrompt : ShellKind.PowerShell, out var formatted, out var reason))
        {
            StatusText.Text = reason;
            return;
        }
        CommandBox.BeginChange();
        try
        {
            CommandBox.SelectAll();
            CommandBox.SelectedText = formatted;
            CommandBox.Select(0, 0);
        }
        finally { CommandBox.EndChange(); }
        CommandBox.Focus();
        StatusText.Text = "オプションと値をまとめて複数行にしました。Ctrl + Z で元に戻せます。実行前に内容を確認してください。";
    }
}
