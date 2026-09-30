using System.IO;
using System.Windows;
using Footprint.Core;

namespace Footprint;

public partial class MainWindow
{
    private readonly WorkspaceSessionStore _sessionStore = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Footprint", "Session.json"));
    private bool _sessionChecked;
    private WorkspaceSession? _pendingSession;

    private void OfferSessionRestore()
    {
        if (_isDetached || _sessionChecked) return;
        _sessionChecked = true;
        try
        {
            _pendingSession = _sessionStore.Load();
            if (_pendingSession is not null) RestoreTabsButton.Visibility = Visibility.Visible;
        }
        catch (Exception error) { ShowError("前回のタブを読み込めませんでした。", error); }
    }

    private void RestoreTabs_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellation is not null || _pendingSession is null) return;
        var session = _pendingSession;
        DismissSessionRestore();
        _workspaces.Clear();
        _workspaces.AddRange(session.Workspaces);
        EnsureUniqueWorkspaceTitles();
        RefreshTabs(session.ActiveIndex);
    }

    private void DismissSessionRestore()
    {
        _pendingSession = null;
        RestoreTabsButton.Visibility = Visibility.Collapsed;
    }

    private void SaveWorkspaceSession()
    {
        if (_isDetached) return;
        CaptureWorkspace();
        var workspaces = _workspaces.ToList();
        foreach (var window in DetachedWindows)
        {
            window.CaptureWorkspace();
            workspaces.AddRange(window._workspaces);
        }
        try
        {
            _sessionStore.Save(new WorkspaceSession { Workspaces = workspaces, ActiveIndex = _activeWorkspaceIndex });
        }
        catch (Exception error) { ShowError("タブの状態を保存できませんでした。", error); }
    }
}
