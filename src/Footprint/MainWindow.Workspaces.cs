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
        workspace.Output = _outputPages.FullText;
    }

    private void ApplyWorkspace(WorkspaceState workspace)
    {
        ShellBox.SelectedIndex = workspace.ShellIndex;
        DirectoryBox.Text = workspace.WorkingDirectory;
        CommandBox.Text = workspace.Command;
        SetOutput(workspace.Output);
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
        DismissSessionRestore();
        CaptureWorkspace();
        _workspaces.Add(new WorkspaceState { Title = NextWorkspaceTitle() });
        RefreshTabs(_workspaces.Count - 1);
    }

    private string NextWorkspaceTitle() => WorkspaceNames.Next(_workspaces.Select(workspace => workspace.Title));

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
}
