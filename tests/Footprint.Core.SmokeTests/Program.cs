using Footprint.Core;
using System.Runtime.InteropServices;

var directory = Path.Combine(Path.GetTempPath(), "Footprint-tests-" + Guid.NewGuid().ToString("N"));
try
{
    Require(WorkspaceNames.Next(["タブ 1", "タブ 2", "タブ 4", "タブ 5"]) == "タブ 6",
        "Removing a middle tab must not duplicate the next tab name.");
    var outputRecord = new CommandRecord();
    var displayedOutput = new System.Text.StringBuilder();
    var boundedOutput = new CommandOutputCapture(outputRecord, chunk => displayedOutput.Append(chunk));
    boundedOutput.Receive(new string('a', 99_999));
    boundedOutput.Receive("日本語");
    boundedOutput.Receive("ignored");
    Require(outputRecord.Output == new string('a', 99_999) + "日\n[出力の保存上限に達しました]\n",
        "Output must stop at the limit and report truncation only once.");
    Require(displayedOutput.ToString() == outputRecord.Output, "Displayed and stored output must agree.");
    var store = new HistoryStore(directory);
    var settingsPath = Path.Combine(directory, "Settings", "preferences.json");
    var settingsStore = new AppearanceSettingsStore(settingsPath);
    Require(settingsStore.Load().CommandFontSize == 12 && !settingsStore.Load().DarkTheme,
        "A first launch must use the default appearance.");
    settingsStore.Save(new AppearanceSettings { DarkTheme = true, CommandFontSize = 20 });
    Require(settingsStore.Load() is { DarkTheme: true, CommandFontSize: 20 }, "Appearance settings must survive reload.");
    try
    {
        settingsStore.Save(new AppearanceSettings { CommandFontSize = 100 });
        throw new InvalidOperationException("Invalid appearance settings must be rejected.");
    }
    catch (ArgumentException) { }
    Require(settingsStore.Load().CommandFontSize == 20, "A rejected save must retain the previous settings.");
    await File.WriteAllTextAsync(settingsPath, "null");
    try
    {
        settingsStore.Load();
        throw new InvalidOperationException("Corrupt appearance data must be rejected.");
    }
    catch (System.Text.Json.JsonException) { }
    var sessionPath = Path.Combine(directory, "Session.json");
    var sessionStore = new WorkspaceSessionStore(sessionPath);
    Require(sessionStore.Load() is null, "First launch must not offer an empty saved session.");
    sessionStore.Save(new WorkspaceSession
    {
        ActiveIndex = 1,
        Window = new WindowPlacement { Left = -1200, Top = 80, Width = 1100, Height = 800, Maximized = true },
        Workspaces =
        [
            new WorkspaceState { Title = "タブ 1", Command = "echo hello", WorkingDirectory = directory },
            new WorkspaceState { Title = "タブ 2", ShellIndex = 1, Command = "Write-Output '日本語'", Output = "日本語\n", WorkingDirectory = directory }
        ]
    });
    var restored = sessionStore.Load()!;
    Require(restored.Window is { Left: -1200, Top: 80, Width: 1100, Height: 800, Maximized: true },
        "Session must preserve window position, size and maximized state.");
    Require(restored.ActiveIndex == 1 && restored.Workspaces.Count == 2 &&
        restored.Workspaces[1].ShellIndex == 1 && restored.Workspaces[1].Output == "日本語\n" &&
        restored.Workspaces[1].Command == "Write-Output '日本語'" && restored.Workspaces[1].WorkingDirectory == directory,
        "Session restore must preserve tabs, selected tab, shell, folder, command and Unicode output.");
    sessionStore.Save(new WorkspaceSession { Workspaces = [restored.Workspaces[0]] });
    Require(sessionStore.Load()!.Workspaces.Count == 1, "Saving a session must replace the previous tabs.");
    Require(sessionStore.Load()!.Window is null, "Sessions without window placement must remain readable.");
    await File.WriteAllTextAsync(sessionPath, "{\"Workspaces\":null}");
    try
    {
        sessionStore.Load();
        throw new InvalidOperationException("Invalid session data must be rejected.");
    }
    catch (System.Text.Json.JsonException) { }
    Require(File.Exists(sessionPath), "Invalid session data must be preserved.");
    File.Delete(sessionPath);
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
    record.StartedAt = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 9, 30)));
    records[0].StartedAt = record.StartedAt;
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
    Require(nextBackup.Count == 0,
        "A different execution date must not enter the daily backup.");
    var extra = new CommandRecord { StartedAt = record.StartedAt.AddDays(1), Command = "extra", WorkingDirectory = directory, Shell = ShellKind.PowerShell };
    await store.SaveAsync(extra);
    await store.ReplaceAsync([record, extra], backup);
    (records, skipped) = await store.LoadAsync();
    Require(records.Count == 1 && records[0].IsFavorite, "Restore must replace valid history files.");
    var manager = new HistoryManager(store);
    await manager.ToggleFavoriteAsync(records[0]);
    Require(!(await store.LoadAsync()).Records[0].IsFavorite, "Favorite changes must be persisted.");
    await manager.ToggleFavoriteAsync(records[0]);
    await store.SaveAsync(extra);
    records.Add(extra);
    await manager.ClearNonFavoritesAsync(records, nextBackupDate);
    Require(records.Count == 1 && records[0].IsFavorite, "Clear must retain favorite records.");
    var beforeClear = await store.LoadDailyBackupAsync(nextBackupDate);
    Require(beforeClear.Count == 1 && beforeClear[0].Id == extra.Id, "Clear must back up history before deleting records.");
        var favorites = await store.LoadFavoriteBackupAsync(nextBackupDate);
    Require(favorites.Count == 1 && favorites[0].Id == record.Id, "Favorite backup includes favorites from previous execution dates.");
    Require((await store.LoadDailyBackupAsync(backupDate)).Count == 1, "Repeated saves must not duplicate records.");
    records = await manager.RestoreFavoritesAsync(records, favorites, nextBackupDate.AddDays(1));
    Require(records.Count == 1 && records[0].IsFavorite, "Favorite restore must recover favorite state.");
    await store.SaveAsync(extra);
    records.Add(extra);
    records = await manager.RestoreFavoritesAsync(records, [], nextBackupDate.AddDays(2));
    Require(records.Count == 2 && records.All(item => !item.IsFavorite), "Empty favorite restore must retain normal history and clear stars.");
    await manager.RestoreAsync(records, backup.Concat(beforeClear).ToList(), nextBackupDate.AddDays(3));
    Require((await store.LoadAsync()).Records.Count == 2, "Manager restore must persist replacement history.");
    await store.DeleteAsync(extra);
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
