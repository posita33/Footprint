using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using Footprint.Core;

namespace Footprint;

public partial class MainWindow
{
    private static bool _updateInProgress;
    private static bool _updateRestarting;
    private static Version CurrentVersion => typeof(MainWindow).Assembly.GetName().Version!;
    internal static string VersionLabel => $"v{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

    private async void UpdateApplication_Click(object sender, RoutedEventArgs e)
    {
        var windows = Application.Current.Windows.OfType<MainWindow>().ToList();
        if (_updateInProgress || windows.Any(window => window._cancellation is not null))
        {
            StatusText.Text = "コマンドの実行が終了してから更新してください。";
            return;
        }
        _updateInProgress = true;
        foreach (var window in windows) { window.UpdateApplicationButton.IsEnabled = false; window.UpdateApplicationButton.Content = "更新確認中…"; window.RunButton.IsEnabled = false; }
        string? staging = null;
        try
        {
            StatusText.Text = "最新版を確認しています…";
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            var client = new ReleaseUpdateClient(http);
            var release = await client.GetLatestAsync();
            // Assembly versions have a revision, release tags usually do not.
            if (release.Version <= new Version(CurrentVersion.Major, CurrentVersion.Minor, CurrentVersion.Build))
            {
                StatusText.Text = $"{VersionLabel} は最新版です。";
                return;
            }
            if (MessageBox.Show(this, $"{VersionLabel} → v{release.Version} に更新します。\nタブを保存し、アプリを終了して差し替えた後に再起動します。",
                "最新版に更新", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;
            var target = Environment.ProcessPath ?? throw new IOException("実行ファイルの場所を確認できません。");
            if (!Path.GetFileName(target).Equals("Footprint.exe", StringComparison.OrdinalIgnoreCase))
                throw new IOException("自動更新は配布ZIPのFootprint.exeから起動した場合に利用できます。");
            var installDirectory = Path.GetDirectoryName(target)!;
            var probe = Path.Combine(installDirectory, ".footprint-update-" + Guid.NewGuid().ToString("N"));
            using (File.Create(probe)) { }
            File.Delete(probe);
            staging = Path.Combine(Path.GetTempPath(), "Footprint-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            StatusText.Text = $"v{release.Version} をダウンロード・検証しています…";
            foreach (var window in windows) window.UpdateApplicationButton.Content = "ダウンロード中…";
            var zip = Path.Combine(staging, "update.zip");
            await client.DownloadAsync(release, zip);
            var extracted = Path.Combine(staging, "app");
            ZipFile.ExtractToDirectory(zip, extracted);
            var executable = Path.Combine(extracted, "Footprint.exe");
            if (!File.Exists(executable) || Directory.EnumerateFiles(extracted, "*", SearchOption.AllDirectories).Count() != 1)
                throw new InvalidDataException("自動更新は単一EXEの配布ZIPに対応しています。");
            var product = FileVersionInfo.GetVersionInfo(executable).ProductVersion?.Split('+')[0];
            if (!Version.TryParse(product, out var downloadedVersion) || downloadedVersion != release.Version)
                throw new InvalidDataException("ダウンロードしたアプリのバージョンが一致しません。");
            if (!(_primaryWindow ?? this).SaveWorkspaceSession()) return;
            var config = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                pid = Environment.ProcessId, target, source = executable, staging,
                backup = target + "." + Guid.NewGuid().ToString("N") + ".previous",
                pending = target + "." + Guid.NewGuid().ToString("N") + ".new",
                expected = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target)))
            })));
            var script = Path.Combine(staging, "install.ps1");
            File.WriteAllText(script, "$config = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + config + "')) | ConvertFrom-Json\r\n" + InstallScript,
                new UTF8Encoding(true));
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
                UseShellExecute = false, CreateNoWindow = true
            };
            start.Environment.Remove("PSModulePath");
            foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script }) start.ArgumentList.Add(argument);
            using var installer = Process.Start(start) ?? throw new IOException("更新処理を開始できませんでした。");
            _updateRestarting = true;
            staging = null; // Installer owns cleanup after this process exits.
            Application.Current.Shutdown();
        }
        catch (Exception error) { ShowError("更新できませんでした。現在のバージョンはそのまま利用できます。", error); }
        finally
        {
            if (staging is not null)
            {
                try { Directory.Delete(staging, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            _updateInProgress = false;
            if (!_updateRestarting)
                foreach (var window in windows)
                {
                    window.UpdateApplicationButton.IsEnabled = true;
                    window.UpdateApplicationButton.Content = "最新版に更新";
                    window.RunButton.IsEnabled = window._isReady && window._cancellation is null;
                }
        }
    }

    private const string InstallScript = """
$ErrorActionPreference = 'Stop'
$moved = $false
try {
    $process = Get-Process -Id $config.pid -ErrorAction SilentlyContinue
    if ($process -and -not $process.WaitForExit(60000)) { throw 'アプリが終了しなかったため更新を中止しました。' }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = [BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes([string]$config.target))).Replace('-', '') }
    finally { $sha.Dispose() }
    if ($hash -ne $config.expected) { throw '更新対象が変更されたため中止しました。' }
    Copy-Item -LiteralPath $config.source -Destination $config.pending
    Move-Item -LiteralPath $config.target -Destination $config.backup
    $moved = $true
    Move-Item -LiteralPath $config.pending -Destination $config.target
    Start-Process -FilePath $config.target -WorkingDirectory ([IO.Path]::GetDirectoryName([string]$config.target))
    Remove-Item -LiteralPath $config.backup -ErrorAction SilentlyContinue
} catch {
    if ($moved) {
        Remove-Item -LiteralPath $config.target -ErrorAction SilentlyContinue
        Move-Item -LiteralPath $config.backup -Destination $config.target -ErrorAction SilentlyContinue
    }
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show('更新に失敗しました。旧版を保持しています。' + [Environment]::NewLine + $_.Exception.Message, 'Footprint 更新') | Out-Null
    if ($moved -and (Test-Path -LiteralPath $config.target)) { Start-Process -FilePath $config.target -WorkingDirectory ([IO.Path]::GetDirectoryName([string]$config.target)) }
} finally {
    Remove-Item -LiteralPath $config.pending -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $config.staging -Recurse -Force -ErrorAction SilentlyContinue
}
""";
}
