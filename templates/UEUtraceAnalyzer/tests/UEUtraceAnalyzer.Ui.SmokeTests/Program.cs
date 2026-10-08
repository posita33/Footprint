using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using UEUtraceAnalyzer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Console.Error.WriteLine("::error::" + e.ExceptionObject.ToString()!.Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A"));
        var application = new App();
        application.InitializeComponent();
        var window = new MainWindow();
        Require(window.Content is Grid grid && grid.Children.Count == 0, "MainWindow must contain no UI controls.");
        Require(ApplicationInfo.Version == new Version(0, 1, 0), "Initial application version");
        TestToken();
        TestInstaller(false);
        TestInstaller(true);
        window.Close();
        Console.WriteLine("PASS: empty WPF window, encrypted credentials, dialogs and installer swap/rollback");
    }

    private static void TestToken()
    {
        var directory = Path.Combine(Path.GetTempPath(), "UEUtraceAnalyzer-token-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "GitHubSettings.json");
        var tokens = new GitHubTokenStore(path);
        try
        {
            Require(tokens.Load() == "", "Missing credentials must be empty");
            tokens.Save("test-token-not-real");
            Require(tokens.Load() == "test-token-not-real" && !File.ReadAllText(path).Contains("test-token-not-real"),
                "Credentials must be protected with Windows DPAPI");
            var requests = new RequestWindow(tokens);
            Require(((PasswordBox)requests.FindName("TokenBox")).Password == "test-token-not-real", "Dialog must restore credentials");
            ((Button)requests.FindName("DeleteTokenButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(!File.Exists(path) && ((PasswordBox)requests.FindName("TokenBox")).Password == "", "Credential deletion");
            requests.Close();
            var settings = new SettingsWindow();
            settings.Close();
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    private static void TestInstaller(bool failLaunch)
    {
        var root = Path.Combine(Path.GetTempPath(), "UEUtraceAnalyzer update 日本語 ' " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var staging = Path.Combine(root, "stage");
            Directory.CreateDirectory(staging);
            var target = Path.Combine(root, "UEUtraceAnalyzer.exe");
            var source = Path.Combine(staging, "UEUtraceAnalyzer.exe");
            File.WriteAllText(target, "old");
            File.WriteAllText(source, "new");
            var backup = target + ".previous";
            var config = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                pid = int.MaxValue, target, source, staging, backup, pending = target + ".new", fail = failLaunch,
                expected = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target)))
            })));
            var installer = (string)typeof(ApplicationUpdater).GetField("InstallScript", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
            // Exercise the real installer file operations; substitute launching and the error dialog only.
            installer = installer.Replace("[System.Windows.MessageBox]::Show('更新に失敗しました。旧版を保持しています。' + [Environment]::NewLine + $_.Exception.Message, 'UEUtraceAnalyzer 更新') | Out-Null",
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
            start.Environment.Remove("PSModulePath");
            foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(30000)) { process.Kill(entireProcessTree: true); throw new Exception("Installer test timed out."); }
            var errors = process.StandardError.ReadToEnd();
            Require(process.ExitCode == 0, "PowerShell installer must parse and run: " + errors);
            Require(File.ReadAllText(target) == (failLaunch ? "old" : "new"), "Swap or rollback must retain the correct application: " +
                (File.Exists(target + ".error") ? File.ReadAllText(target + ".error") : errors));
            Require(File.ReadAllText(target + ".launched").Trim() == (failLaunch ? "old" : "new"), "Installer must relaunch the installed or restored application.");
            Require(File.Exists(target + ".error") == failLaunch && !File.Exists(backup) && !Directory.Exists(staging),
                "Installer must report failures and clean staged files after swap or rollback.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Require(bool success, string message)
    {
        if (!success) throw new InvalidOperationException(message);
    }
}
