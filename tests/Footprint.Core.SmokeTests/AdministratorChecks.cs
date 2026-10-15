using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Footprint.Core;

internal static class AdministratorChecks
{
    public static async Task RunAsync(string directory)
    {
        var record = new CommandRecord { Command = "test", WorkingDirectory = directory,
            RunAsAdministrator = true, WasElevated = true };
        var history = new HistoryStore(Path.Combine(directory, "admin-history"));
        await history.SaveAsync(record);
        var saved = (await history.LoadAsync()).Records.Single();
        Require(saved.RunAsAdministrator && saved.WasElevated == true && saved.AdministratorLabel == "管理者", "Administrator history persistence");
        var legacy = JsonSerializer.Deserialize<CommandRecord>("{\"Command\":\"legacy\"}")!;
        Require(!legacy.RunAsAdministrator && legacy.WasElevated == null && legacy.AdministratorLabel == "未確認", "Legacy privileges must remain unknown, never inferred");
        var session = new WorkspaceSessionStore(Path.Combine(directory, "admin-session.json"));
        session.Save(new WorkspaceSession { Workspaces = [new WorkspaceState { RunAsAdministrator = true }] });
        Require(session.Load()!.Workspaces[0].RunAsAdministrator, "Admin setting must survive session snapshot");
        if (ElevatedCommandRunner.IsElevated)
        {
            foreach (var shell in new[] { ShellKind.CommandPrompt, ShellKind.PowerShell })
                await TestWorkerAsync(directory, shell, stop: false);
            await TestWorkerAsync(directory, ShellKind.PowerShell, stop: true);
            Require(await ElevatedCommandRunner.RunWorkerAsync("untrusted", Environment.ProcessId) == 1, "Invalid worker arguments must fail closed");
            Console.WriteLine("PASS: real elevated worker IPC, CMD/PowerShell output, exit codes and cancellation");
        }
        else Console.WriteLine("SKIP: elevated Windows worker integration (administrator token required; no UAC prompts in tests)");
        Console.WriteLine("PASS: administrator history, legacy metadata and session persistence");
    }

    private static async Task TestWorkerAsync(string directory, ShellKind shell, bool stop)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var name = "Footprint-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var worker = Task.Run(() => ElevatedCommandRunner.RunWorkerAsync(name, Environment.ProcessId));
        await server.WaitForConnectionAsync(timeout.Token);
        using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        var command = shell == ShellKind.CommandPrompt ? "echo admin-out\necho admin-err 1>&2\nexit /b 7" :
            stop ? "Write-Output 'ready-to-stop'; Start-Sleep -Seconds 60" : "Write-Output 'admin-out 日本語'; [Console]::Error.WriteLine('admin-err'); exit 7";
        await writer.WriteLineAsync(JsonSerializer.Serialize(new CommandRecord
        { Command = command, WorkingDirectory = directory, Shell = shell, RunAsAdministrator = true }));
        var text = new StringBuilder();
        var started = false;
        var sentStop = false;
        var done = false;
        while (await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            using var json = JsonDocument.Parse(line);
            var message = json.RootElement;
            var kind = message.GetProperty("Kind").GetString();
            if (kind == "started") started = true;
            else if (kind == "output")
            {
                text.Append(message.GetProperty("Text").GetString());
                if (stop && !sentStop && text.ToString().Contains("ready-to-stop"))
                { await writer.WriteLineAsync("cancel"); sentStop = true; }
            }
            else if (kind == "error") throw new Exception(message.GetProperty("Text").GetString());
            else if (kind == "done")
            {
                Require((ExecutionStatus)message.GetProperty("Status").GetInt32() ==
                    (stop ? ExecutionStatus.Cancelled : ExecutionStatus.Completed), "Worker must preserve completion/cancellation");
                if (!stop) Require(message.GetProperty("ExitCode").GetInt32() == 7, "Worker exit code must reach caller");
                done = true;
                break;
            }
        }
        Require(started && done && (stop ? sentStop : text.ToString().Contains("admin-out") && text.ToString().Contains("admin-err")),
            "Worker must transmit start, stdout/stderr and final state");
        Require(await worker.WaitAsync(timeout.Token) == 0, "Worker must finish and release its pipe after cancellation/completion");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
