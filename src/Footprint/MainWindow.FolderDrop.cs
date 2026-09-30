using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Footprint.Core;

namespace Footprint;

public partial class MainWindow
{
    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        // Handle before TextBox's own drop behavior so paths never enter the command field.
        e.Handled = true;
        e.Effects = _cancellation is null && GetDroppedDirectory(e.Data) is not null
            && (e.AllowedEffects & DragDropEffects.Copy) != 0
            ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (_cancellation is not null)
        {
            StatusText.Text = "実行中は作業フォルダーを変更できません。";
            return;
        }
        var directory = GetDroppedDirectory(e.Data);
        if (directory is null || (e.AllowedEffects & DragDropEffects.Copy) == 0)
        {
            StatusText.Text = "フォルダーを 1 つドロップしてください。";
            return;
        }
        DirectoryBox.Text = directory;
        e.Effects = DragDropEffects.Copy;
        StatusText.Text = "作業フォルダーを設定しました。";
    }

    private static string? GetDroppedDirectory(IDataObject data)
    {
        return data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } paths
            && Directory.Exists(paths[0]) ? paths[0] : null;
    }
}
