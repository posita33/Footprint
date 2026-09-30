using System.IO;
using System.Windows;
using Footprint.Core;

namespace Footprint;

public partial class MainWindow
{
    private readonly WorkspaceSessionStore _sessionStore = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Footprint", "Session.json"));
    private bool _sessionChecked;

    private void OfferSessionRestore()
    {
        if (_isDetached || _sessionChecked) return;
        _sessionChecked = true;
        try
        {
            var session = _sessionStore.Load();
            if (session is null) return;
            if (MessageBox.Show(this, $"前回のタブ（{session.Workspaces.Count} 件）を復元しますか？",
                "タブを復元", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _workspaces.Clear();
            _workspaces.AddRange(session.Workspaces);
            EnsureUniqueWorkspaceTitles();
            RefreshTabs(session.ActiveIndex);
        }
        catch (Exception error) { ShowError("前回のタブを読み込めませんでした。", error); }
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
