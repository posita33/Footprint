using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Footprint.Core;

namespace Footprint;

public partial class MainWindow : Window
{
    private static MainWindow? _primaryWindow;
    private static readonly List<MainWindow> DetachedWindows = [];
    private static event EventHandler? SharedHistoryChanged;
    private const int OutputLimit = 100_000;
    private readonly HistoryStore _store = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Footprint", "History"));
    private List<CommandRecord> _history = [];
    private List<DateOnly> _backupDates = [];
    private CancellationTokenSource? _cancellation;
    private Task? _executionTask;
    private bool _closing;
    private bool _allowClose;
    private bool _isReady;
    private readonly List<WorkspaceState> _workspaces = [];
    private readonly bool _isDetached;
    private bool _switchingWorkspace;
    private int _activeWorkspaceIndex;
    private int _skipped;

    public MainWindow()
        : this(null, false) { }

    private MainWindow(WorkspaceState? workspace, bool isDetached)
    {
        _isDetached = isDetached;
        InitializeComponent();
        _workspaces.Add(workspace ?? new WorkspaceState { Title = "タブ 1" });
        RefreshTabs(0);
        if (isDetached) DetachedWindows.Add(this); else _primaryWindow ??= this;
        SharedHistoryChanged += OnSharedHistoryChanged;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            (_history, _skipped) = await _store.LoadAsync();
            // The first launch on a new date keeps a snapshot before the user changes history.
            await _store.EnsureDailyBackupAsync(_history, DateOnly.FromDateTime(DateTime.Today));
            await LoadBackupDatesAsync();
            RefreshHistory();
            StatusText.Text = $"履歴 {_history.Count} 件" +
                (_skipped > 0 ? $"（読み込めない {_skipped} 件は保持しています）" : "");
            RunButton.IsEnabled = true;
            _isReady = true;
            UpdateHistoryManagementControls();
        }
        catch (Exception error)
        {
            ShowError("履歴を読み込めません。保存先のアクセス権を確認し、再起動してください。", error);
        }
    }

    private void RefreshTabs(int selectedIndex)
    {
        _switchingWorkspace = true;
        WorkspaceTabs.Items.Clear();
        foreach (var workspace in _workspaces)
            WorkspaceTabs.Items.Add(new TabItem { Header = workspace.Title, Tag = workspace });
        WorkspaceTabs.SelectedIndex = Math.Clamp(selectedIndex, 0, _workspaces.Count - 1);
        _activeWorkspaceIndex = WorkspaceTabs.SelectedIndex;
        ApplyWorkspace(_workspaces[_activeWorkspaceIndex]);
        _switchingWorkspace = false;
        CloseTabButton.IsEnabled = _workspaces.Count > 1 && _cancellation is null;
        MergeTabsButton.IsEnabled = _isDetached || DetachedWindows.Count > 0;
    }

    private void CaptureWorkspace()
    {
        if (_workspaces.Count == 0 || _activeWorkspaceIndex < 0) return;
        var workspace = _workspaces[_activeWorkspaceIndex];
        workspace.ShellIndex = ShellBox.SelectedIndex;
        workspace.WorkingDirectory = DirectoryBox.Text;
        workspace.Command = CommandBox.Text;
        workspace.Output = OutputBox.Text;
    }

    private void ApplyWorkspace(WorkspaceState workspace)
    {
        ShellBox.SelectedIndex = workspace.ShellIndex;
        DirectoryBox.Text = workspace.WorkingDirectory;
        CommandBox.Text = workspace.Command;
        OutputBox.Text = workspace.Output;
    }

    private void WorkspaceTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_switchingWorkspace || _cancellation is not null || WorkspaceTabs.SelectedIndex < 0) return;
        CaptureWorkspace();
        _activeWorkspaceIndex = WorkspaceTabs.SelectedIndex;
        ApplyWorkspace(_workspaces[_activeWorkspaceIndex]);
    }

    private void AddTab_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellation is not null) return;
        CaptureWorkspace();
        _workspaces.Add(new WorkspaceState { Title = NextWorkspaceTitle() });
        RefreshTabs(_workspaces.Count - 1);
    }

    private string NextWorkspaceTitle()
    {
        var largestNumber = 0;
        foreach (var workspace in _workspaces)
        {
            const string prefix = "タブ ";
            if (!workspace.Title.StartsWith(prefix, StringComparison.Ordinal) ||
                !int.TryParse(workspace.Title[prefix.Length..], out var number))
                continue;

            largestNumber = Math.Max(largestNumber, number);
        }

        return $"タブ {largestNumber + 1}";
    }

    private void EnsureUniqueWorkspaceTitles()
    {
        var titles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var workspace in _workspaces)
        {
            if (!titles.Add(workspace.Title))
            {
                workspace.Title = NextWorkspaceTitle();
                titles.Add(workspace.Title);
            }
        }
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellation is not null || _workspaces.Count <= 1) return;
        _workspaces.RemoveAt(_activeWorkspaceIndex);
        RefreshTabs(Math.Min(_activeWorkspaceIndex, _workspaces.Count - 1));
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

    private void WorkspaceTabs_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _cancellation is not null || _workspaces.Count <= 1) return;
        var tab = FindParent<TabItem>(e.OriginalSource as DependencyObject);
        if (tab?.Tag is not WorkspaceState workspace) return;
        CaptureWorkspace();
        var result = DragDrop.DoDragDrop(tab, workspace, DragDropEffects.Move);
        if (result == DragDropEffects.None && !WorkspaceTabs.IsMouseOver) DetachWorkspace(workspace);
    }

    private void DetachWorkspace(WorkspaceState workspace)
    {
        var index = _workspaces.IndexOf(workspace);
        if (index < 0 || _workspaces.Count <= 1) return;
        _workspaces.RemoveAt(index);
        RefreshTabs(Math.Min(index, _workspaces.Count - 1));
        new MainWindow(workspace, true) { Owner = _primaryWindow }.Show();
    }

    private void MergeTabs_Click(object sender, RoutedEventArgs e)
    {
        var primary = _primaryWindow;
        if (primary is null) return;
        if (DetachedWindows.Any(window => window._cancellation is not null))
        {
            MessageBox.Show(this, "実行中の別ウィンドウを停止してからまとめてください。", "Footprint", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        foreach (var window in DetachedWindows.ToList())
        {
            window.CaptureWorkspace();
            primary._workspaces.AddRange(window._workspaces);
            window._workspaces.Clear();
            window.Close();
        }
        primary.EnsureUniqueWorkspaceTitles();
        primary.RefreshTabs(primary._workspaces.Count - 1);
        primary.Activate();
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T found) return found;
            child = System.Windows.Media.VisualTreeHelper.GetParent(child);
        }
        return null;
    }

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

    private void RefreshHistory()
    {
        if (HistoryGrid is null || SearchBox is null || FavoritesOnlyBox is null) return;
        HistoryGrid.ItemsSource = _history
            .Where(record => !FavoritesOnlyBox.IsChecked.GetValueOrDefault() || record.IsFavorite)
            .Where(record => record.Matches(SearchBox.Text.Trim()))
            .ToList();
        UpdateHistoryManagementControls();
    }

    private void NotifySharedHistoryChanged() => SharedHistoryChanged?.Invoke(this, EventArgs.Empty);

    private async void OnSharedHistoryChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, this) || !IsLoaded || _cancellation is not null) return;
        try
        {
            (_history, _skipped) = await _store.LoadAsync();
            await LoadBackupDatesAsync();
            RefreshHistory();
        }
        catch { }
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) => RefreshHistory();
    private void FavoriteFilter_Changed(object sender, RoutedEventArgs e) => RefreshHistory();

    private async Task LoadBackupDatesAsync()
    {
        var selected = BackupDateBox.SelectedItem as string;
        _backupDates = await _store.LoadBackupDatesAsync();
        BackupDateBox.ItemsSource = _backupDates
            .Select(date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToList();
        BackupDateBox.SelectedItem = BackupDateBox.Items.Cast<string>().FirstOrDefault(date => date == selected)
            ?? BackupDateBox.Items.Cast<string>().FirstOrDefault();
        UpdateHistoryManagementControls();
    }

    private void BackupDate_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateHistoryManagementControls();

    private async void CreateBackup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _store.SaveDailyBackupAsync(_history, DateOnly.FromDateTime(DateTime.Today));
            await LoadBackupDatesAsync();
            StatusText.Text = "今日の履歴をバックアップしました。";
            NotifySharedHistoryChanged();
        }
        catch (Exception error) { ShowError("バックアップを保存できませんでした。", error); }
    }

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedBackupDate(out var date)) return;
        List<CommandRecord> records;
        try { records = await _store.LoadDailyBackupAsync(date); }
        catch (Exception error)
        {
            ShowError("選択したバックアップを読み込めませんでした。", error);
            return;
        }
        if (MessageBox.Show(this,
            $"{date:yyyy-MM-dd} のバックアップ（{records.Count} 件）で現在の履歴を置き換えます。\n現在の履歴は今日のバックアップとして保存されます。",
            "履歴を復元", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            await _store.SaveDailyBackupAsync(_history, DateOnly.FromDateTime(DateTime.Today));
            await _store.ReplaceAsync(_history, records);
            _history = records;
            RefreshHistory();
            await LoadBackupDatesAsync();
            StatusText.Text = $"{date:yyyy-MM-dd} のバックアップから {_history.Count} 件を復元しました。";
            NotifySharedHistoryChanged();
        }
        catch (Exception error) { ShowError("履歴を復元できませんでした。", error); }
    }

    private async void ClearNonFavorite_Click(object sender, RoutedEventArgs e)
    {
        var records = _history.Where(record => !record.IsFavorite).ToList();
        if (records.Count == 0)
        {
            StatusText.Text = "削除できる履歴はありません。";
            return;
        }
        if (MessageBox.Show(this,
            $"お気に入り以外の履歴 {records.Count} 件を削除します。\n現在の履歴は今日のバックアップとして保存されます。",
            "履歴を削除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            await _store.SaveDailyBackupAsync(_history, DateOnly.FromDateTime(DateTime.Today));
            foreach (var record in records) await _store.DeleteAsync(record);
            _history.RemoveAll(record => !record.IsFavorite);
            RefreshHistory();
            await LoadBackupDatesAsync();
            StatusText.Text = $"お気に入り以外の履歴 {records.Count} 件を削除しました。";
            NotifySharedHistoryChanged();
        }
        catch (Exception error) { ShowError("履歴を削除できませんでした。", error); }
    }

    private bool TryGetSelectedBackupDate(out DateOnly date) =>
        DateOnly.TryParseExact(BackupDateBox.SelectedItem as string, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out date);

    private void UpdateHistoryManagementControls()
    {
        if (CreateBackupButton is null || RestoreBackupButton is null || ClearNonFavoriteButton is null) return;
        var canManage = _isReady && _cancellation is null;
        CreateBackupButton.IsEnabled = canManage;
        RestoreBackupButton.IsEnabled = canManage && TryGetSelectedBackupDate(out _);
        ClearNonFavoriteButton.IsEnabled = canManage && _history.Any(record => !record.IsFavorite);
    }

    private void History_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_cancellation is null && HistoryGrid.SelectedItem is CommandRecord record)
            OutputBox.Text = record.Output;
        UpdateFavoriteButton();
    }

    private async void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellation is not null || HistoryGrid.SelectedItem is not CommandRecord record) return;
        var wasFavorite = record.IsFavorite;
        record.IsFavorite = !wasFavorite;
        try
        {
            await _store.SaveAsync(record);
            RefreshHistory();
            StatusText.Text = record.IsFavorite ? "お気に入りに追加しました。" : "お気に入りから外しました。";
            NotifySharedHistoryChanged();
        }
        catch (Exception error)
        {
            record.IsFavorite = wasFavorite;
            RefreshHistory();
            ShowError("お気に入りの変更を保存できませんでした。", error);
        }
        UpdateFavoriteButton();
    }

    private void UpdateFavoriteButton()
    {
        if (FavoriteButton is null || HistoryGrid is null) return;
        if (HistoryGrid.SelectedItem is not CommandRecord record)
        {
            FavoriteButton.IsEnabled = false;
            FavoriteButton.Content = "☆ お気に入り";
            return;
        }
        FavoriteButton.IsEnabled = _cancellation is null;
        FavoriteButton.Content = record.IsFavorite ? "★ お気に入り解除" : "☆ お気に入り";
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

    private void OpenHistoryFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_store.StorageDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = _store.StorageDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception error) { ShowError("履歴フォルダーを開けませんでした。", error); }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose && _cancellation is not null)
        {
            e.Cancel = true;
            if (_closing) return;
            _closing = true;
            _cancellation.Cancel();
            if (_executionTask is not null) await _executionTask;
            _allowClose = true;
            Close();
            return;
        }
        SharedHistoryChanged -= OnSharedHistoryChanged;
        DetachedWindows.Remove(this);
        if (ReferenceEquals(_primaryWindow, this)) _primaryWindow = null;
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
