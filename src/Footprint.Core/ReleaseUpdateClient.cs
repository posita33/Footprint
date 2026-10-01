using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Footprint.Core;

public sealed record ReleaseUpdate(Version Version, Uri DownloadUrl, string Sha256);

public sealed class ReleaseUpdateClient(HttpClient http)
{
    public static ReleaseUpdate Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
            throw new InvalidDataException("安定版のリリースではありません。");
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!tag.StartsWith('v') || !Version.TryParse(tag[1..], out var version))
            throw new InvalidDataException("リリースのバージョンを確認できません。");
        var asset = root.GetProperty("assets").EnumerateArray()
            .FirstOrDefault(item => item.GetProperty("name").GetString() == "Footprint-win-x64.zip");
        if (asset.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("更新用ZIPがまだ公開されていません。");
        var url = asset.GetProperty("browser_download_url").GetString() ?? "";
        var prefix = $"https://github.com/posita33/Footprint/releases/download/{tag}/";
        if (url != prefix + "Footprint-win-x64.zip") throw new InvalidDataException("更新ファイルの配布元が不正です。");
        var digest = asset.TryGetProperty("digest", out var value) ? value.GetString() ?? "" : "";
        if (!digest.StartsWith("sha256:") || digest.Length != 71 || !digest[7..].All(Uri.IsHexDigit))
            throw new InvalidDataException("更新ファイルの検証情報がありません。リリースページから更新してください。");
        return new ReleaseUpdate(version, new Uri(url), digest[7..]);
    }

    public async Task<ReleaseUpdate> GetLatestAsync(CancellationToken cancellation = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/posita33/Footprint/releases/latest");
        request.Headers.UserAgent.ParseAdd("Footprint-Updater/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, cancellation);
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(cancellation));
    }

    public async Task DownloadAsync(ReleaseUpdate release, string destination, CancellationToken cancellation = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, release.DownloadUrl);
        request.Headers.UserAgent.ParseAdd("Footprint-Updater/1.0");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        var created = false;
        try
        {
            using (var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                using var stream = await response.Content.ReadAsStreamAsync(cancellation);
                await stream.CopyToAsync(file, cancellation);
            }
            using var saved = File.OpenRead(destination);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(saved, cancellation));
            if (!hash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新ファイルの検証に失敗しました。");
        }
        catch
        {
            if (created && File.Exists(destination)) File.Delete(destination);
            throw;
        }
    }
}
