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
    private readonly HistoryStore _store = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Footprint", "History"));
    private List<CommandRecord> _history = [];
    private readonly HistoryManager _historyManager;
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
        _historyManager = new HistoryManager(_store);
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
