using System.Windows;

namespace Footprint;

public partial class MainWindow
{
    private bool _historyExpanded = true;
    private GridLength _expandedWorkspaceHeight;
    private GridLength _expandedHistoryHeight;
    private GridLength _expandedCommandHeight;
    private GridLength _expandedOutputHeight;

    private void ToggleHistory_Click(object sender, RoutedEventArgs e) => SetHistoryExpanded(!_historyExpanded);

    private void SetHistoryExpanded(bool expanded)
    {
        if (_historyExpanded == expanded) return;
        if (!expanded)
        {
            var moveFocus = HistoryPanel.IsKeyboardFocusWithin;
            _expandedWorkspaceHeight = WorkspaceRow.Height;
            _expandedHistoryHeight = HistoryRow.Height;
            _expandedCommandHeight = CommandRow.Height;
            _expandedOutputHeight = OutputRow.Height;
            // Keep command input at its current height and give the freed space to output.
            CommandRow.Height = new GridLength(Math.Max(CommandRow.ActualHeight, CommandRow.MinHeight));
            OutputRow.Height = new GridLength(1, GridUnitType.Star);
            WorkspaceRow.Height = new GridLength(1, GridUnitType.Star);
            HistoryRow.MinHeight = 0;
            HistoryRow.Height = new GridLength(0);
            HistoryDividerRow.Height = new GridLength(0);
            HistoryPanel.Visibility = HistorySplitter.Visibility = Visibility.Collapsed;
            if (moveFocus)
            {
                if (CommandBox.IsEnabled) CommandBox.Focus();
                else OutputBox.Focus();
            }
        }
        else
        {
            HistoryPanel.Visibility = HistorySplitter.Visibility = Visibility.Visible;
            HistoryRow.MinHeight = 160;
            HistoryRow.Height = _expandedHistoryHeight;
            WorkspaceRow.Height = _expandedWorkspaceHeight;
            CommandRow.Height = _expandedCommandHeight;
            OutputRow.Height = _expandedOutputHeight;
            HistoryDividerRow.Height = new GridLength(8);
        }
        _historyExpanded = expanded;
        HistoryToggleButton.Content = expanded ? "▼ 履歴を折りたたむ" : "▲ 履歴を開く";
    }
}
