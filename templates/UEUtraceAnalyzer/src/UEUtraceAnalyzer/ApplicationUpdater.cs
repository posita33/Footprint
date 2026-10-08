using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using UEUtraceAnalyzer.Core;

namespace UEUtraceAnalyzer;

public enum UpdateResult { AlreadyLatest, Cancelled, Restarting }

/// <summary>Call from a future update button. Saving application state is the caller's responsibility.</summary>
public static class ApplicationUpdater
{
    private static bool _updating;

    public static async Task<UpdateResult> CheckAndUpdateAsync(Window owner, Func<bool>? saveBeforeRestart = null)
    {
        if (_updating) throw new InvalidOperationException("更新処理は実行中です。");
        _updating = true;
        string? staging = null;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            var client = new ReleaseUpdateClient(http);
            var release = await client.GetLatestAsync();
            if (release.Version <= ApplicationInfo.Version) return UpdateResult.AlreadyLatest;
            if (MessageBox.Show(owner, $"{ApplicationInfo.VersionLabel} → v{release.Version} に更新します。アプリを終了して差し替え、再起動します。",
                "最新版に更新", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
                return UpdateResult.Cancelled;
            var target = Environment.ProcessPath ?? throw new IOException("実行ファイルの場所を確認できません。");
            if (!Path.GetFileName(target).Equals("UEUtraceAnalyzer.exe", StringComparison.OrdinalIgnoreCase))
                throw new IOException("自動更新は配布ZIPのUEUtraceAnalyzer.exeから起動した場合に利用できます。");
            var probe = Path.Combine(Path.GetDirectoryName(target)!, ".ueutraceanalyzer-update-" + Guid.NewGuid().ToString("N"));
            using (File.Create(probe)) { }
            File.Delete(probe);
            staging = Path.Combine(Path.GetTempPath(), "UEUtraceAnalyzer-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            var zip = Path.Combine(staging, "update.zip");
            await client.DownloadAsync(release, zip);
            var extracted = Path.Combine(staging, "app");
            ZipFile.ExtractToDirectory(zip, extracted);
            var executable = Path.Combine(extracted, "UEUtraceAnalyzer.exe");
            if (!File.Exists(executable) || Directory.EnumerateFiles(extracted, "*", SearchOption.AllDirectories).Count() != 1)
                throw new InvalidDataException("自動更新は単一EXEの配布ZIPに対応しています。");
            var product = FileVersionInfo.GetVersionInfo(executable).ProductVersion?.Split('+')[0];
            if (!Version.TryParse(product, out var downloadedVersion) || downloadedVersion != release.Version)
                throw new InvalidDataException("ダウンロードしたアプリのバージョンが一致しません。");
            if (saveBeforeRestart != null && !saveBeforeRestart()) return UpdateResult.Cancelled;
            var config = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                pid = Environment.ProcessId, target, source = executable, staging,
                backup = target + "." + Guid.NewGuid().ToString("N") + ".previous",
                pending = target + "." + Guid.NewGuid().ToString("N") + ".new",
                expected = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target)))
            })));
            var script = Path.Combine(staging, "install.ps1");
            File.WriteAllText(script, "$config = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + config +
                "')) | ConvertFrom-Json\r\n" + InstallScript, new UTF8Encoding(true));
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
                UseShellExecute = false, CreateNoWindow = true
            };
            start.Environment.Remove("PSModulePath");
            foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script }) start.ArgumentList.Add(argument);
            using var installer = Process.Start(start) ?? throw new IOException("更新処理を開始できませんでした。");
            staging = null;
            Application.Current.Shutdown();
            return UpdateResult.Restarting;
        }
        finally
        {
            if (staging != null)
            {
                try { Directory.Delete(staging, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            _updating = false;
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
    [System.Windows.MessageBox]::Show('更新に失敗しました。旧版を保持しています。' + [Environment]::NewLine + $_.Exception.Message, 'UEUtraceAnalyzer 更新') | Out-Null
    if ($moved -and (Test-Path -LiteralPath $config.target)) { Start-Process -FilePath $config.target -WorkingDirectory ([IO.Path]::GetDirectoryName([string]$config.target)) }
} finally {
    Remove-Item -LiteralPath $config.pending -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $config.staging -Recurse -Force -ErrorAction SilentlyContinue
}
""";
}
