using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Footprint.Core;

namespace Footprint;

public partial class MainWindow : Window
{
    private const int OutputLimit = 100_000;
    private readonly HistoryStore _store = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Footprint", "History"));
    private List<CommandRecord> _history = [];
    private CancellationTokenSource? _cancellation;
    private Task? _executionTask;
    private bool _closing;
    private bool _allowClose;
    private int _skipped;

    public MainWindow()
    {
        InitializeComponent();
        DirectoryBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            (_history, _skipped) = await _store.LoadAsync();
            RefreshHistory();
            StatusText.Text = $"履歴 {_history.Count} 件" +
                (_skipped > 0 ? $"（読み込めない {_skipped} 件は保持しています）" : "");
            RunButton.IsEnabled = true;
        }
        catch (Exception error)
        {
            ShowError("履歴を読み込めません。保存先のアクセス権を確認し、再起動してください。", error);
        }
    }

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
        var truncated = false;
        // Synchronous delivery on the dispatcher ensures all output is captured before saving.
        var output = new DispatcherOutput(text =>
        {
            var remaining = OutputLimit - record.Output.Length;
            if (remaining > 0)
            {
                var chunk = text[..Math.Min(text.Length, remaining)];
                record.Output += chunk;
                OutputBox.AppendText(chunk);
                OutputBox.ScrollToEnd();
            }
            if (text.Length > remaining && !truncated)
            {
                truncated = true;
                const string notice = "\n[出力の保存上限に達しました]\n";
                record.Output += notice;
                OutputBox.AppendText(notice);
            }
        }, Dispatcher);
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
        CommandBox.IsEnabled = DirectoryBox.IsEnabled = ShellBox.IsEnabled = !running;
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _cancellation?.Cancel();
        StopButton.IsEnabled = false;
    }

    private void RefreshHistory()
    {
        if (HistoryGrid is null || SearchBox is null) return;
        HistoryGrid.ItemsSource = _history.Where(record => record.Matches(SearchBox.Text.Trim())).ToList();
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) => RefreshHistory();

    private void History_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_cancellation is null && HistoryGrid.SelectedItem is CommandRecord record)
            OutputBox.Text = record.Output;
    }

    private void Reuse_Click(object sender, RoutedEventArgs e) => ReuseSelected();
    private void History_DoubleClick(object sender, MouseButtonEventArgs e) => ReuseSelected();

    private void ReuseSelected()
    {
        if (_cancellation is not null || HistoryGrid.SelectedItem is not CommandRecord record) return;
        CommandBox.Text = record.Command;
        DirectoryBox.Text = record.WorkingDirectory;
        ShellBox.SelectedIndex = record.Shell == ShellKind.CommandPrompt ? 0 : 1;
        CommandBox.Focus();
        StatusText.Text = "コマンドを入力に戻しました。内容を確認して実行してください。";
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not CommandRecord record) return;
        try { Clipboard.SetText(record.Command); StatusText.Text = "コマンドをコピーしました。"; }
        catch (Exception error) { ShowError("コピーできませんでした。", error); }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || _cancellation is null) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        _cancellation.Cancel();
        if (_executionTask is not null) await _executionTask;
        _allowClose = true;
        Close();
    }

    private void ShowError(string message, Exception error)
    {
        StatusText.Text = message;
        MessageBox.Show(this, message + "\n\n" + error.Message, "Footprint", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private sealed class DispatcherOutput(Action<string> receive, System.Windows.Threading.Dispatcher dispatcher) : IProgress<string>
    {
        public void Report(string value) => dispatcher.Invoke(() => receive(value));
    }
}
