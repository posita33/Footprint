using System.Text.Json;

namespace Footprint.Core;

public sealed class WorkspaceSession
{
    public List<WorkspaceState> Workspaces { get; set; } = [];
    public int ActiveIndex { get; set; }
    public WindowPlacement? Window { get; set; }
}

public sealed class WindowPlacement
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }

    public bool IsValid => double.IsFinite(Left) && double.IsFinite(Top) &&
        double.IsFinite(Width) && double.IsFinite(Height) && Width > 0 && Height > 0;
}

public sealed class WorkspaceSessionStore(string path)
{
    public WorkspaceSession? Load()
    {
        if (!File.Exists(path)) return null;
        var session = JsonSerializer.Deserialize<WorkspaceSession>(File.ReadAllText(path));
        if (session is null || session.Workspaces is null || session.Workspaces.Count == 0 ||
            session.ActiveIndex < 0 || session.ActiveIndex >= session.Workspaces.Count ||
            session.Workspaces.Any(workspace => workspace is null ||
                string.IsNullOrWhiteSpace(workspace.Title) || workspace.ShellIndex is < 0 or > 1 ||
                string.IsNullOrWhiteSpace(workspace.WorkingDirectory) ||
                workspace.Command is null || workspace.Output is null))
            throw new JsonException("タブの保存データが不正です。");
        return session;
    }

    public void Save(WorkspaceSession session)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            // Serialize a complete snapshot without changing the live tabs.
            var snapshot = new WorkspaceSession
            {
                ActiveIndex = session.ActiveIndex,
                Window = session.Window,
                Workspaces = session.Workspaces.Select(workspace => new WorkspaceState
                {
                    Title = workspace.Title, ShellIndex = workspace.ShellIndex,
                    WorkingDirectory = workspace.WorkingDirectory, Command = workspace.Command,
                    Output = workspace.Output
                }).ToList()
            };
            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
