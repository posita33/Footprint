using Footprint.Core;

var directory = Path.Combine(Path.GetTempPath(), "Footprint-tests-" + Guid.NewGuid().ToString("N"));
try
{
    var store = new HistoryStore(directory);
    var record = new CommandRecord
    {
        Command = "Write-Output 'こんにちは'",
        WorkingDirectory = directory,
        Shell = ShellKind.PowerShell
    };
    await store.SaveAsync(record);
    record.Status = ExecutionStatus.Completed;
    record.ExitCode = 7;
    record.Output = "こんにちは\n";
    await store.SaveAsync(record);
    var (records, skipped) = await store.LoadAsync();
    Require(records.Count == 1 && skipped == 0, "Updating must not duplicate history.");
    Require(records[0].Output == record.Output && records[0].ExitCode == 7, "Result must survive reload.");
    Require(records[0].Matches("POWERSHELL") && records[0].Matches("こんにちは"), "Search must match shell and command.");
    await File.WriteAllTextAsync(Path.Combine(directory, "broken.json"), "not json");
    await File.WriteAllTextAsync(Path.Combine(directory, "null.json"), "null");
    (records, skipped) = await store.LoadAsync();
    Require(records.Count == 1 && skipped == 2, "Corrupt records must not hide valid history.");
    Require(File.Exists(Path.Combine(directory, "broken.json")), "Corrupt files must be preserved.");
    Require(!Directory.EnumerateFiles(directory, "*.tmp").Any(), "Temporary writes must be cleaned up.");
    Console.WriteLine("PASS: persistence, update, Unicode, search, corrupt history, temporary cleanup");
    if (OperatingSystem.IsWindows())
    {
        var runner = new CommandRunner();
        foreach (var shell in new[] { ShellKind.CommandPrompt, ShellKind.PowerShell })
        {
            var command = shell == ShellKind.CommandPrompt
                ? "echo こんにちは\r\ntree /a\r\necho %CD%\r\nexit /b 7"
                : "Write-Output 'こんにちは'; (Get-Location).Path; exit 7";
            var result = new CommandRecord { Command = command, WorkingDirectory = directory, Shell = shell };
            var capture = new OutputCapture();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await runner.RunAsync(result, capture, timeout.Token);
            Require(result.Status == ExecutionStatus.Completed && result.ExitCode == 7, $"{shell}: exit status");
            Require(capture.Text.Contains("こんにちは") && capture.Text.Contains(directory), $"{shell}: Unicode and working directory");
            Console.WriteLine($"PASS: {shell} execution, multiline command, Unicode, working directory, exit code");
        }
        var stopped = new CommandRecord
        {
            Command = "Start-Sleep -Seconds 30",
            WorkingDirectory = directory,
            Shell = ShellKind.PowerShell
        };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await runner.RunAsync(stopped, new OutputCapture(), cancellation.Token);
        Require(stopped.Status == ExecutionStatus.Cancelled, "Stopping must cancel the shell.");
        Console.WriteLine("PASS: cancellation");
    }
    else Console.WriteLine("SKIP: Windows shell execution and cancellation (Windows required)");
}
finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class OutputCapture : IProgress<string>
{
    private readonly object _gate = new();
    private string _text = "";
    public string Text { get { lock (_gate) return _text; } }
    public void Report(string value) { lock (_gate) _text += value; }
}
