namespace Footprint;

public sealed class WorkspaceState
{
    public string Title { get; set; } = "新しいタブ";
    public int ShellIndex { get; set; }
    public string WorkingDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string Command { get; set; } = "";
    public string Output { get; set; } = "";
}
