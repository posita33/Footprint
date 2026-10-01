using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Footprint;
using Footprint.Core;

internal static class PagedOutputUiChecks
{
    public static void Run(MainWindow window)
    {
        var output = (TextBox)window.FindName("OutputBox");
        var follow = (CheckBox)window.FindName("AutoScrollBox");
        var pageLabel = (TextBlock)window.FindName("OutputPageText");
        var set = typeof(MainWindow).GetMethod("SetOutput", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var append = typeof(MainWindow).GetMethod("AppendLiveOutput", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var first = string.Concat(Enumerable.Repeat("a 日本語😀\r\n", OutputPages.LinesPerPage));
        var second = string.Concat(Enumerable.Repeat("b\n", OutputPages.LinesPerPage));
        var text = first + second + "日本語😀TAIL";
        follow.IsChecked = true;
        set.Invoke(window, [text]);
        Require(output.Text == "日本語😀TAIL" && pageLabel.Text.StartsWith("3 / 3"), "Loading large output must render only the latest page while following.");
        ((Button)window.FindName("FirstOutputPageButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(follow.IsChecked == false && output.Text == first, "Manual navigation must disable automatic following.");
        append.Invoke(window, ["MORE"]);
        Require(output.Text == first, "New live output must not interrupt reading an earlier page.");
        ((Button)window.FindName("NextOutputPageButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(output.Text == second, "Next must display the middle page.");
        ((Button)window.FindName("LastOutputPageButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(output.Text == "日本語😀TAILMORE", "Last must display all trailing text.");
        Require(pageLabel.Text.Contains("1,001–1,001 行"), "The current page must show its logical line range.");
        follow.IsChecked = true;
        var newLines = string.Concat(Enumerable.Repeat("c\r\n", OutputPages.LinesPerPage));
        append.Invoke(window, [newLines + "final line"]);
        Require(output.Text == "final line" && pageLabel.Text.StartsWith("4 / 4") && pageLabel.Text.Contains("1,501–1,501 行"),
            "Live following must advance only at complete line boundaries.");
        typeof(MainWindow).GetMethod("CaptureWorkspace", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
        var workspaces = (List<WorkspaceState>)typeof(MainWindow).GetField("_workspaces", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        Require(workspaces[0].Output == text + "MORE" + newLines + "final line", "Capturing tabs must retain all pages, not just the visible textbox.");
        ((Button)window.FindName("CopyOutputPageButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(Clipboard.GetText() == output.Text, "Page copy must copy only the visible page.");
        ((Button)window.FindName("CopyFullOutputButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(Clipboard.GetText() == workspaces[0].Output, "Full copy must include every page.");
        var longLine = new string('x', 120_000) + "😀complete line";
        set.Invoke(window, [longLine]);
        Require(output.Text == longLine && pageLabel.Text.StartsWith("1 / 1") && pageLabel.Text.Contains("1–1 行"),
            "A single long line must display intact on one page.");
        set.Invoke(window, [""]);
        Require(pageLabel.Text.StartsWith("1 / 1") && !((Button)window.FindName("SaveOutputButton")).IsEnabled &&
            !((Button)window.FindName("CopyFullOutputButton")).IsEnabled, "Empty output must disable export and copy.");
        Console.WriteLine("PASS: real WPF output pages, navigation, paused following, bounded live output, complete tab capture and page/full copy");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
