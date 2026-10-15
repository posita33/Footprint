using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Footprint;
using Footprint.Core;

internal static class AdministratorHostChecks
{
    public static void Run()
    {
        if (!ElevatedCommandRunner.IsElevated)
        {
            Console.WriteLine("SKIP: WPF elevated worker bootstrap (administrator token required)");
            return;
        }
        TestAsync().GetAwaiter().GetResult();
        Console.WriteLine("PASS: actual Footprint.exe worker bootstrap, private IPC and administrative execution");
    }

    private static async Task TestAsync()
    {
        var executable = Path.GetFullPath("src/Footprint/bin/Release/net8.0-windows/Footprint.exe");
        if (!File.Exists(executable)) throw new IOException("Build the solution in Release from the repository root before running UI checks.");
        var name = "Footprint-" + Guid.NewGuid().ToString("N");
        using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { ElevatedCommandRunner.WorkerArgument, name, Environment.ProcessId.ToString() }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        try
        {
            await pipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(new CommandRecord
            {
                Command = "Write-Output 'WPF_ADMIN_HOST'; exit 3", WorkingDirectory = Path.GetTempPath(),
                RunAsAdministrator = true, Shell = ShellKind.PowerShell
            })).ConfigureAwait(false);
            var output = new StringBuilder();
            var completed = false;
            while (await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false) is { } line)
            {
                using var doc = JsonDocument.Parse(line);
                var message = doc.RootElement;
                if (message.GetProperty("Kind").GetString() == "output") output.Append(message.GetProperty("Text").GetString());
                if (message.GetProperty("Kind").GetString() == "error") throw new Exception(message.GetProperty("Text").GetString());
                if (message.GetProperty("Kind").GetString() == "done")
                {
                    completed = message.GetProperty("ExitCode").GetInt32() == 3 && output.ToString().Contains("WPF_ADMIN_HOST");
                    break;
                }
            }
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            if (!completed || process.ExitCode != 0) throw new Exception("The WPF helper must execute the requested shell and exit without opening a window.");
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }
}
