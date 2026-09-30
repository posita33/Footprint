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
    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellation is not null) return;
        if (string.IsNullOrWhiteSpace(CommandBox.Text))
        {
            StatusText.Text = "コマンドを入力してください。";
            return;
        }
        string workingDirectory;
        try
        {
            workingDirectory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(DirectoryBox.Text.Trim()));
            if (!Directory.Exists(workingDirectory)) throw new DirectoryNotFoundException(workingDirectory);
        }
        catch (Exception error)
        {
            ShowError("作業フォルダーを確認してください。", error);
            return;
        }

        var record = new CommandRecord
        {
            Command = CommandBox.Text,
            WorkingDirectory = workingDirectory,
            Shell = ShellBox.SelectedIndex == 0 ? ShellKind.CommandPrompt : ShellKind.PowerShell
        };
        _cancellation = new CancellationTokenSource();
        SetRunning(true);
        OutputBox.Clear();
        StatusText.Text = "実行しています…";
        _executionTask = ExecuteAsync(record, _cancellation.Token);
        await _executionTask;
        _cancellation.Dispose();
        _cancellation = null;
        SetRunning(false);
    }

    private void CommandBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.Shift || _cancellation is not null)
            return;

        e.Handled = true;
        Run_Click(this, new RoutedEventArgs());
    }

    private async Task ExecuteAsync(CommandRecord record, CancellationToken cancellation)
    {
        try { await _store.SaveAsync(record); }
        catch (Exception error)
        {
            ShowError("履歴を保存できないため、実行しませんでした。", error);
            return;
        }
        _history.Insert(0, record);
        RefreshHistory();
        var capture = new CommandOutputCapture(record, text =>
        {
            OutputBox.AppendText(text);
            OutputBox.ScrollToEnd();
        });
        // Dispatcher delivery completes before the result is persisted.
        var output = new DispatcherOutput(capture.Receive, Dispatcher);
        try { await new CommandRunner().RunAsync(record, output, cancellation); }
        catch (OperationCanceledException) { record.Status = ExecutionStatus.Cancelled; }
        catch (Exception error)
        {
            record.Status = ExecutionStatus.Failed;
            output.Report("\n" + error.Message);
        }
        try
        {
            await _store.SaveAsync(record);
            StatusText.Text = $"{record.ResultLabel} · 履歴 {_history.Count} 件";
            NotifySharedHistoryChanged();
        }
        catch (Exception error)
        {
            ShowError("実行は終了しましたが、結果を保存できませんでした。", error);
        }
        RefreshHistory();
    }

    private void SetRunning(bool running)
    {
        RunButton.IsEnabled = !running;
        StopButton.IsEnabled = running;
        FavoriteButton.IsEnabled = !running && HistoryGrid.SelectedItem is CommandRecord;
        FavoritesOnlyBox.IsEnabled = !running;
        WorkspaceTabs.IsEnabled = !running;
        CloseTabButton.IsEnabled = !running && _workspaces.Count > 1;
        UpdateHistoryManagementControls();
        CommandBox.IsEnabled = DirectoryBox.IsEnabled = ShellBox.IsEnabled = !running;
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _cancellation?.Cancel();
        StopButton.IsEnabled = false;
    }
}
