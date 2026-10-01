using System.Windows;
using System.Windows.Controls;
using Footprint;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
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
        Console.WriteLine("PASS: real WPF drawer layout, freed output height, command height, restored ratio and retained contents");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
