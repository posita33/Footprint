using System.Diagnostics;
using System.Text;

namespace Footprint.Core;

public sealed class CommandRunner
{
    public async Task RunAsync(CommandRecord record, IProgress<string> output, CancellationToken cancellation)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows が必要です。");
        var start = new ProcessStartInfo
        {
            WorkingDirectory = record.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (record.Shell == ShellKind.CommandPrompt)
        {
            // cmd.exe receives the command as a Unicode command-line argument. Its /u
            // switch writes redirected built-in output (including tree) as UTF-16LE.
            // This avoids depending on a Japanese or English system code page.
            start.StandardOutputEncoding = Encoding.Unicode;
            start.StandardErrorEncoding = Encoding.Unicode;
            start.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            start.ArgumentList.Add("/d");
            start.ArgumentList.Add("/u");
            start.ArgumentList.Add("/s");
            start.ArgumentList.Add("/c");
            start.ArgumentList.Add(record.Command);
        }
        else
        {
            start.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
            start.ArgumentList.Add("-NoLogo");
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-OutputFormat");
            start.ArgumentList.Add("Text");
            start.ArgumentList.Add("-EncodedCommand");
            start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(
                "[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding; " +
                "$OutputEncoding = [Console]::OutputEncoding;\n" + record.Command)));
        }

        cancellation.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = start };
        process.Start();
        process.StandardInput.Close();
        // Kill synchronously on cancellation, including when the window is closing.
        using var registration = cancellation.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        });
        var stdout = PumpAsync(process.StandardOutput, output, cancellation);
        var stderr = PumpAsync(process.StandardError, output, cancellation);
        try
        {
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(cancellation));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            record.Status = ExecutionStatus.Cancelled;
            return;
        }
        record.ExitCode = process.ExitCode;
        record.Status = cancellation.IsCancellationRequested ? ExecutionStatus.Cancelled : ExecutionStatus.Completed;
    }

    private static async Task PumpAsync(StreamReader reader, IProgress<string> output, CancellationToken cancellation)
    {
        var buffer = new char[2048];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellation)) != 0)
            output.Report(new string(buffer, 0, count));
    }
}
