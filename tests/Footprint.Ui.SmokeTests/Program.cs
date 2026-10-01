using System.Windows;
using System.Windows.Controls;
using Footprint;
using Footprint.Core;
using System.Windows.Media;
using System.Windows.Documents;
using System.Reflection;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Console.WriteLine("::error::" +
            e.ExceptionObject.ToString()!.Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A"));
        // Measure the real WPF controls without showing windows or loading user history.
        var app = new App();
        app.InitializeComponent();
        var window = new MainWindow();
        Require(window.Icon is { Width: > 0 } && new HelpWindow().Icon is { Width: > 0 } &&
            new SettingsWindow().Icon is { Width: > 0 }, "Window icons must load from embedded WPF resources.");
        var layout = (Grid)window.FindName("LayoutGrid");
        var panel = (Grid)window.FindName("HistoryPanel");
        var splitter = (GridSplitter)window.FindName("HistorySplitter");
        var toggle = (Button)window.FindName("HistoryToggleButton");
        var command = (TextBox)window.FindName("CommandBox");
        var output = (TextBox)window.FindName("OutputBox");
        var search = (TextBox)window.FindName("SearchBox");
        var workspaceRow = (RowDefinition)window.FindName("WorkspaceRow");
        var historyRow = (RowDefinition)window.FindName("HistoryRow");
        command.Text = "echo keep";
        output.Text = "keep output";
        search.Text = "keep search";
        workspaceRow.Height = new GridLength(4, GridUnitType.Star);
        historyRow.Height = new GridLength(2, GridUnitType.Star);
        void Measure()
        {
            layout.Measure(new Size(1050, 700));
            layout.Arrange(new Rect(0, 0, 1050, 700));
            layout.UpdateLayout();
        }
        Measure();
        var outputHeight = output.ActualHeight;
        var commandHeight = command.ActualHeight;
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Measure();
        Require(panel.Visibility == Visibility.Collapsed && splitter.Visibility == Visibility.Collapsed,
            "History and its splitter must collapse together.");
        Require(historyRow.ActualHeight == 0 && output.ActualHeight > outputHeight,
            "Collapsing must release the history height to output.");
        Require(Math.Abs(command.ActualHeight - commandHeight) < 1,
            "Collapsing must keep the command height.");
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Measure();
        Require(panel.Visibility == Visibility.Visible && splitter.Visibility == Visibility.Visible &&
            workspaceRow.Height == new GridLength(4, GridUnitType.Star) &&
            historyRow.Height == new GridLength(2, GridUnitType.Star), "Expanding must restore the adjusted panel ratio.");
        Require(command.Text == "echo keep" && output.Text == "keep output" && search.Text == "keep search",
            "Toggling must preserve input, output and search.");
        var setRunning = typeof(MainWindow).GetMethod("SetRunning", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var appendLive = typeof(MainWindow).GetMethod("AppendLiveOutput", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var indicator = (StackPanel)window.FindName("ExecutionIndicator");
        var progress = (ProgressBar)window.FindName("ExecutionProgress");
        var state = (TextBlock)window.FindName("ExecutionStateText");
        var limit = (TextBlock)window.FindName("OutputLimitText");
        var autoScroll = (CheckBox)window.FindName("AutoScrollBox");
        var stop = (Button)window.FindName("StopButton");
        typeof(MainWindow).GetField("_isReady", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, true);
        using var cancellation = new CancellationTokenSource();
        typeof(MainWindow).GetField("_cancellation", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, cancellation);
        setRunning.Invoke(window, [true]);
        Measure();
        Require(indicator.Visibility == Visibility.Visible && progress.IsIndeterminate && !command.IsEnabled && stop.IsEnabled &&
            state.Text.Contains("実行中") && state.Text.Contains("経過"), "Running must visibly explain why input is disabled.");
        output.Clear();
        autoScroll.IsChecked = false;
        var liveRecord = new CommandRecord();
        var liveCapture = new CommandOutputCapture(liveRecord,
            text => appendLive.Invoke(window, [text]), () => limit.Visibility = Visibility.Visible);
        liveCapture.Receive(new string('x', CommandOutputCapture.OutputLimit));
        liveCapture.Receive("TAIL_AFTER_LIMIT");
        Require(output.Text.EndsWith("TAIL_AFTER_LIMIT") && limit.Visibility == Visibility.Visible &&
            indicator.Visibility == Visibility.Visible && !command.IsEnabled,
            "Reaching the storage limit must retain visible running state and continue displaying output with auto-scroll off.");
        autoScroll.IsChecked = true;
        Require(output.Text.EndsWith("TAIL_AFTER_LIMIT"), "Enabling auto-scroll must preserve full live output.");
        stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(cancellation.IsCancellationRequested && state.Text.Contains("停止処理中") && !stop.IsEnabled,
            "Stop must show pending cancellation while input remains disabled.");
        typeof(MainWindow).GetField("_cancellation", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, null);
        setRunning.Invoke(window, [false]);
        Require(indicator.Visibility == Visibility.Collapsed && !progress.IsIndeterminate && command.IsEnabled &&
            ((Button)window.FindName("RunButton")).IsEnabled && !stop.IsEnabled && output.Text.EndsWith("TAIL_AFTER_LIMIT"),
            "Finishing must restore input and preserve the complete displayed output.");
        setRunning.Invoke(window, [true]);
        Require(limit.Visibility == Visibility.Collapsed, "A new run must clear the prior truncation notice.");
        setRunning.Invoke(window, [false]);
        NewUiFeatureChecks.Run(window);
        var tabs = (TabControl)window.FindName("WorkspaceTabs");
        var first = new TabItem { Header = "テーマ確認" };
        var second = new TabItem { Header = "未選択" };
        tabs.Items.Clear();
        tabs.Items.Add(first);
        tabs.Items.Add(second);
        tabs.SelectedIndex = 0;
        var apply = typeof(MainWindow).Assembly.GetType("Footprint.Appearance")!
            .GetMethod("Apply", BindingFlags.Public | BindingFlags.Static)!;
        foreach (var dark in new[] { true, false, true })
        {
            apply.Invoke(null, [new AppearanceSettings { DarkTheme = dark }]);
            Measure();
            foreach (var tab in new[] { first, second })
            {
                var foreground = ((SolidColorBrush)tab.Foreground).Color;
                var background = ((SolidColorBrush)app.Resources[tab.IsSelected ? "WorkspaceBackground" : "WindowBackground"]).Color;
                double Luminance(Color color)
                {
                    double Channel(byte value) { var c = value / 255.0; return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
                    return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
                }
                double Contrast(Color bg) => (Math.Max(Luminance(foreground), Luminance(bg)) + 0.05) /
                    (Math.Min(Luminance(foreground), Luminance(bg)) + 0.05);
                Require(Contrast(background) >= 4.5 && Contrast(((SolidColorBrush)app.Resources["TabHoverBackground"]).Color) >= 4.5,
                    "Selected, unselected and hover tab text must meet 4.5:1 contrast after live theme changes.");
                tab.ApplyTemplate();
                var border = (Border)tab.Template.FindName("TabBorder", tab);
                var presenter = (ContentPresenter)border.Child;
                Require(TextElement.GetForeground(presenter).ToString() == tab.Foreground.ToString(),
                    "Header presenter must use the theme foreground.");
            }
        }
        Console.WriteLine("PASS: real WPF drawer layout, freed output height, command height, restored ratio and retained contents");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
