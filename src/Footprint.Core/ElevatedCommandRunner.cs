using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Text;
using System.Text.Json;

namespace Footprint.Core;

/// <summary>Private, per-execution IPC keeps commands out of process arguments and retains redirected output.</summary>
public static class ElevatedCommandRunner
{
    public const string WorkerArgument = "--elevated-command";
    public static bool IsElevated
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static async Task RunAsync(CommandRecord record, IProgress<string> output, CancellationToken cancellation,
        string executable)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows が必要です。");
        cancellation.ThrowIfCancellationRequested();
        if (IsElevated) { await new CommandRunner().RunAsync(record, output, cancellation); return; }
        var pipeName = "Footprint-" + Guid.NewGuid().ToString("N");
        // Use user SID (not token owner): UAC changes the owner/elevation level.
        // Administrators also permits UAC credential prompts for another administrator account.
        using var identity = WindowsIdentity.GetCurrent();
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(identity.User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        using var pipe = NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 0, 0, security);
        var start = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" };
        start.ArgumentList.Add(WorkerArgument);
        start.ArgumentList.Add(pipeName);
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Process? worker;
        try { worker = await Task.Run(() => Process.Start(start)); }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223)
        {
            output.Report("管理者実行のUAC確認がキャンセルされました。\n");
            record.Status = ExecutionStatus.Cancelled;
            return;
        }
        using (worker)
        {
            if (worker == null) throw new IOException("管理者実行を開始できませんでした。");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            var connected = pipe.WaitForConnectionAsync(timeout.Token);
            var exited = worker.WaitForExitAsync();
            if (await Task.WhenAny(connected, exited) != connected)
                throw new IOException("管理者実行の接続に失敗しました。同じWindowsユーザーの管理者権限で承認してください。");
            await connected;
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var clientPid) || clientPid != worker.Id)
                throw new IOException("管理者実行の接続元を確認できませんでした。");
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(record));
            using var stopTimeout = new CancellationTokenSource();
            var writeLock = new object();
            using var registration = cancellation.Register(() =>
            {
                try { lock (writeLock) writer.WriteLine("cancel"); }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
                stopTimeout.CancelAfter(TimeSpan.FromSeconds(15));
            });
            while (await reader.ReadLineAsync(stopTimeout.Token) is { } line)
            {
                var message = JsonSerializer.Deserialize<WorkerMessage>(line) ?? throw new IOException("管理者実行の応答が不正です。");
                if (message.Kind == "output") output.Report(message.Text ?? "");
                else if (message.Kind == "started") record.WasElevated = true;
                else if (message.Kind == "error") throw new IOException(message.Text);
                else if (message.Kind == "done")
                {
                    record.Status = message.Status;
                    record.ExitCode = message.ExitCode;
                    return;
                }
                else throw new IOException("管理者実行の応答が不正です。");
            }
            cancellation.ThrowIfCancellationRequested();
            throw new IOException("管理者実行との接続が切れました。結果は確認できません。");
        }
    }

    /// <summary>Only the GUI executable invokes this entry point; never starts another elevated process.</summary>
    public static async Task<int> RunWorkerAsync(string pipeName, int parentProcessId)
    {
        if (!IsElevated || !pipeName.StartsWith("Footprint-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(pipeName[10..], "N", out _)) return 1;
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await pipe.ConnectAsync(lifetime.Token).ConfigureAwait(false);
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverPid) || serverPid != parentProcessId) return 1;
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        var request = await reader.ReadLineAsync(lifetime.Token).ConfigureAwait(false);
        var record = JsonSerializer.Deserialize<CommandRecord>(request ?? "") ?? throw new InvalidDataException("実行要求がありません。");
        if (!record.RunAsAdministrator || !Enum.IsDefined(record.Shell) ||
            string.IsNullOrWhiteSpace(record.Command) || !Directory.Exists(record.WorkingDirectory)) return 1;
        using var cancellation = new CancellationTokenSource();
        var writeLock = new object();
        void Send(WorkerMessage message) { lock (writeLock) writer.WriteLine(JsonSerializer.Serialize(message)); }
        var monitor = MonitorAsync(reader, cancellation);
        try
        {
            Send(new("started"));
            await new CommandRunner().RunAsync(record, new InlineProgress(text => Send(new("output", text))), cancellation.Token).ConfigureAwait(false);
            Send(new("done", Status: record.Status, ExitCode: record.ExitCode));
            return 0;
        }
        catch (OperationCanceledException) { Send(new("done", Status: ExecutionStatus.Cancelled)); return 0; }
        catch (Exception error) { try { Send(new("error", error.Message)); } catch (IOException) { } return 1; }
        finally
        {
            cancellation.Cancel();
            await monitor.ConfigureAwait(false);
        }
    }

    private static async Task MonitorAsync(StreamReader reader, CancellationTokenSource cancellation)
    {
        try { await reader.ReadLineAsync(cancellation.Token).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
        finally { cancellation.Cancel(); }
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);

    private sealed class InlineProgress(Action<string> report) : IProgress<string>
    { public void Report(string value) => report(value); }
    private sealed record WorkerMessage(string Kind, string? Text = null, ExecutionStatus Status = ExecutionStatus.Pending, int? ExitCode = null);
}
