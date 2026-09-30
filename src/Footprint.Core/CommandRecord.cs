namespace Footprint.Core;

public enum ShellKind { CommandPrompt, PowerShell }
public enum ExecutionStatus { Pending, Completed, Cancelled, Failed }

public sealed class CommandRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public string Command { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public ShellKind Shell { get; set; }
    public ExecutionStatus Status { get; set; }
    public int? ExitCode { get; set; }
    public string Output { get; set; } = "";
    public string ShellLabel => Shell == ShellKind.CommandPrompt ? "CMD" : "PowerShell";
    public string TimeLabel => StartedAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss");
    public string ResultLabel => Status switch
    {
        ExecutionStatus.Completed => $"終了 {ExitCode}",
        ExecutionStatus.Cancelled => "停止",
        ExecutionStatus.Failed => "実行エラー",
        _ => "結果未確定"
    };
    public bool Matches(string query) => Command.Contains(query, StringComparison.OrdinalIgnoreCase)
        || WorkingDirectory.Contains(query, StringComparison.OrdinalIgnoreCase)
        || ShellLabel.Contains(query, StringComparison.OrdinalIgnoreCase);
}
