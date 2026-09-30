using Footprint.Core;
using System.Runtime.InteropServices;

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
    record.IsFavorite = true;
    await store.SaveAsync(record);
    var (records, skipped) = await store.LoadAsync();
    Require(records.Count == 1 && skipped == 0, "Updating must not duplicate history.");
    Require(records[0].Output == record.Output && records[0].ExitCode == 7 && records[0].IsFavorite,
        "Result and favorite status must survive reload.");
    Require(records[0].Matches("POWERSHELL") && records[0].Matches("こんにちは"), "Search must match shell and command.");
    var backupDate = new DateOnly(2026, 9, 30);
    await store.SaveDailyBackupAsync(records, backupDate);
    record.Output = "new output";
    record.IsFavorite = false;
    await store.SaveAsync(record);
    await store.EnsureDailyBackupAsync([record], backupDate);
    Require((await store.LoadBackupDatesAsync()).SequenceEqual([backupDate]), "Daily backup must be listed.");
    var backup = await store.LoadDailyBackupAsync(backupDate);
    Require(backup.Count == 1 && backup[0].IsFavorite && backup[0].Output == "こんにちは\n",
        "Existing daily backup must be preserved.");
    var nextBackupDate = backupDate.AddDays(1);
    await store.EnsureDailyBackupAsync([record], nextBackupDate);
    var nextBackup = await store.LoadDailyBackupAsync(nextBackupDate);
    Require(nextBackup.Count == 1 && !nextBackup[0].IsFavorite && nextBackup[0].Output == "new output",
        "The first launch on a new date must save a new backup.");
    var extra = new CommandRecord { Command = "extra", WorkingDirectory = directory, Shell = ShellKind.PowerShell };
    await store.SaveAsync(extra);
    await store.ReplaceAsync([record, extra], backup);
    (records, skipped) = await store.LoadAsync();
    Require(records.Count == 1 && records[0].IsFavorite, "Restore must replace valid history files.");
    await File.WriteAllTextAsync(Path.Combine(directory, "broken.json"), "not json");
    await File.WriteAllTextAsync(Path.Combine(directory, "null.json"), "null");
    (records, skipped) = await store.LoadAsync();
    Require(records.Count == 1 && skipped == 2, "Corrupt records must not hide valid history.");
    Require(File.Exists(Path.Combine(directory, "broken.json")), "Corrupt files must be preserved.");
    Require(!Directory.EnumerateFiles(directory, "*.tmp").Any(), "Temporary writes must be cleaned up.");
    Console.WriteLine("PASS: persistence, update, favorite status, daily backup on date change, restore, Unicode, search, corrupt history, temporary cleanup");
    if (OperatingSystem.IsWindows())
    {
        var runner = new CommandRunner();
        var shells = NativeMethods.GetOEMCP() == 932
            ? new[] { ShellKind.CommandPrompt, ShellKind.PowerShell }
            : new[] { ShellKind.PowerShell };
        foreach (var shell in shells)
        {
            var marker = shell == ShellKind.CommandPrompt ? "こんにちは" : "Footprint smoke test";
            var command = shell == ShellKind.CommandPrompt
                ? "echo こんにちは\r\necho %CD%\r\nexit /b 7"
                : "Write-Output 'Footprint smoke test'; (Get-Location).Path; exit 7";
            var result = new CommandRecord { Command = command, WorkingDirectory = directory, Shell = shell };
            var capture = new OutputCapture();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await runner.RunAsync(result, capture, timeout.Token);
            Require(result.Status == ExecutionStatus.Completed && result.ExitCode == 7, $"{shell}: exit status");
            Require(capture.Text.Contains(marker) && capture.Text.Contains(directory), $"{shell}: output and working directory");
            Console.WriteLine($"PASS: {shell} execution, multiline command, output, working directory, exit code");
        }
        if (NativeMethods.GetOEMCP() != 932)
            Console.WriteLine("SKIP: Japanese CMD output test (Japanese OEM code page required)");
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

static class NativeMethods
{
    [DllImport("kernel32.dll")]
    public static extern uint GetOEMCP();
}
