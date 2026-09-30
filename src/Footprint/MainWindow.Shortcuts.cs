using System.Windows.Input;

namespace Footprint;

public partial class MainWindow
{
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.Control && e.Key == Key.F)
        {
            e.Handled = true;
            SearchBox.Focus();
            SearchBox.SelectAll();
            return;
        }

        if (modifiers == ModifierKeys.Control && e.Key is Key.T or Key.W)
        {
            e.Handled = true;
            if (_cancellation is not null) return;
            if (e.Key == Key.T)
            {
                AddTab_Click(sender, e);
                CommandBox.Focus();
            }
            else CloseTab_Click(sender, e);
            return;
        }

        if (e.Key == Key.Tab &&
            (modifiers == ModifierKeys.Control || modifiers == (ModifierKeys.Control | ModifierKeys.Shift)))
        {
            e.Handled = true;
            if (_cancellation is not null || _workspaces.Count < 2) return;
            var step = modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1;
            WorkspaceTabs.SelectedIndex = (_activeWorkspaceIndex + step + _workspaces.Count) % _workspaces.Count;
            CommandBox.Focus();
        }
    }
}
