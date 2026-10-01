using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Footprint;

internal static class NewUiFeatureChecks
{
    public static void Run(MainWindow window)
    {
        var command = (TextBox)window.FindName("CommandBox");
        var wrap = (ToggleButton)window.FindName("CommandWrapButton");
        var format = (Button)window.FindName("FormatCommandButton");
        var original = "ffmpeg -i \"input file.mp4\" -o output.mp4";
        command.Text = original;
        wrap.IsChecked = true;
        wrap.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Require(command.TextWrapping == TextWrapping.Wrap && command.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled &&
            command.Text == original, "Wrapping must change only presentation.");
        wrap.IsChecked = false;
        wrap.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Require(command.TextWrapping == TextWrapping.NoWrap && command.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto &&
            command.Text == original, "No-wrap must enable horizontal scrolling without altering text.");
        command.ClearUndo();
        format.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(command.Text.Contains("-i \"input file.mp4\"") && command.Text.Contains(" ^\r\n"), "Format icon must group values with shell continuation.");
        command.Undo();
        Require(command.Text == original, "One undo must restore the exact original command.");
        Require(((TextBlock)window.FindName("VersionText")).Text == "v1.3.4", "Current release version must be visible.");
        var running = typeof(MainWindow).GetMethod("SetRunning", BindingFlags.Instance | BindingFlags.NonPublic)!;
        running.Invoke(window, [true]);
        Require(!format.IsEnabled && !((Button)window.FindName("UpdateApplicationButton")).IsEnabled && wrap.IsEnabled,
            "Running must disable formatting/updating while allowing presentation changes.");
        running.Invoke(window, [false]);
        TestInstaller(false);
        TestInstaller(true);
        Console.WriteLine("PASS: wrap icons, horizontal scrolling, grouped formatting, undo, version, execution guards, installer swap and rollback");
    }

    private static void TestInstaller(bool failLaunch)
    {
        var root = Path.Combine(Path.GetTempPath(), "Footprint update 日本語 ' " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var staging = Path.Combine(root, "stage");
            Directory.CreateDirectory(staging);
            var target = Path.Combine(root, "Footprint.exe");
            var source = Path.Combine(staging, "Footprint.exe");
            File.WriteAllText(target, "old");
            File.WriteAllText(source, "new");
            var backup = target + ".previous";
            var config = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                pid = int.MaxValue, target, source, staging, backup, pending = target + ".new", fail = failLaunch,
                expected = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target)))
            })));
            var installer = (string)typeof(MainWindow).GetField("InstallScript", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
            // Exercise the real installer file operations; substitute launching and the error dialog only.
            installer = installer.Replace("[System.Windows.MessageBox]::Show('更新に失敗しました。旧版を保持しています。' + [Environment]::NewLine + $_.Exception.Message, 'Footprint 更新') | Out-Null",
                "Set-Content -LiteralPath ($config.target + '.error') -Value $_.Exception.Message");
            var shim = """
function Start-Process {
    param([string]$FilePath, [string]$WorkingDirectory)
    if ($config.fail -and -not (Test-Path -LiteralPath ($config.target + '.failed'))) {
        Set-Content -LiteralPath ($config.target + '.failed') -Value 'failed'
        throw 'Simulated launch failure'
    }
    Set-Content -LiteralPath ($config.target + '.launched') -Value (Get-Content -LiteralPath $FilePath)
}
""";
            var script = Path.Combine(staging, "install.ps1");
            File.WriteAllText(script, "$config = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + config + "')) | ConvertFrom-Json\r\n" + shim + "\r\n" + installer, new UTF8Encoding(true));
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(30000)) { process.Kill(entireProcessTree: true); throw new Exception("Installer test timed out."); }
            var errors = process.StandardError.ReadToEnd();
            Require(process.ExitCode == 0, "PowerShell installer must parse and run: " + errors);
            Require(File.ReadAllText(target) == (failLaunch ? "old" : "new"), "Swap or rollback must retain the correct application.");
            Require(File.ReadAllText(target + ".launched").Trim() == (failLaunch ? "old" : "new"), "Installer must relaunch the installed or restored application.");
            Require(File.Exists(target + ".error") == failLaunch && !File.Exists(backup) && !Directory.Exists(staging),
                "Installer must report failures and clean staged files after swap or rollback.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
