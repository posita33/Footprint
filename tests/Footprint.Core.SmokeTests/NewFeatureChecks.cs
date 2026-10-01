using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Footprint.Core;

internal static class NewFeatureChecks
{
    public static async Task RunAsync(string directory)
    {
        foreach (var shell in new[] { ShellKind.CommandPrompt, ShellKind.PowerShell })
        {
            var original = "ffmpeg -i \"C:\\動画\\input file.mp4\" -vf scale=1280:720 --flag --output=out.mp4 -n -2 output.mp4";
            Require(CommandFormatter.TryFormat(original, shell, out var formatted, out _), "Long commands must format.");
            var continuation = shell == ShellKind.CommandPrompt ? " ^\r\n" : " `\r\n";
            Require(formatted.Replace(continuation, " ") == original &&
                formatted.Contains("-i \"C:\\動画\\input file.mp4\"") && formatted.Contains("-n -2"),
                "Formatting must preserve tokens, quoted Unicode paths and option/value pairs.");
            foreach (var unsupported in new[] { "echo a | more", "echo a & echo b", "echo a > out", "echo ^ a", "echo \"open", "echo a\necho b" })
                Require(!CommandFormatter.TryFormat(unsupported, shell, out var unchanged, out var reason) &&
                    unchanged == unsupported && reason.Length > 0, "Ambiguous input must remain unchanged with an explanation.");
        }
        Require(CommandFormatter.TryFormat("tool -i 'input file' --output out", ShellKind.PowerShell, out var quoted, out _) &&
            quoted.Contains("-i 'input file'"), "PowerShell single-quoted values must remain grouped.");

        var payload = Encoding.UTF8.GetBytes("verified update payload");
        var digest = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        string Metadata(string hash, string url = "https://github.com/posita33/Footprint/releases/download/v1.3.5/Footprint-win-x64.zip", bool prerelease = false) =>
            JsonSerializer.Serialize(new { tag_name = "v1.3.5", draft = false, prerelease,
                assets = new[] { new { name = "Footprint-win-x64.zip", browser_download_url = url, digest = "sha256:" + hash } } });
        var release = ReleaseUpdateClient.Parse(Metadata(digest));
        Require(release.Version == new Version(1, 3, 5), "Release tags must parse numerically.");
        foreach (var invalid in new[] { Metadata(digest, "https://example.com/update.zip"), Metadata("bad"), Metadata(digest, prerelease: true) })
        {
            try { ReleaseUpdateClient.Parse(invalid); throw new Exception("Invalid release metadata was accepted."); }
            catch (InvalidDataException) { }
        }
        Directory.CreateDirectory(directory);
        using var http = new HttpClient(new FakeHttpHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = request.RequestUri!.Host == "api.github.com" ? new StringContent(Metadata(digest)) : new ByteArrayContent(payload)
        }));
        var client = new ReleaseUpdateClient(http);
        Require((await client.GetLatestAsync()).Version == release.Version, "Latest-release HTTP response must parse.");
        var download = Path.Combine(directory, "update.zip");
        await client.DownloadAsync(release, download);
        Require((await File.ReadAllBytesAsync(download)).SequenceEqual(payload), "Verified update bytes must be saved.");
        File.Delete(download);
        try { await client.DownloadAsync(release with { Sha256 = new string('0', 64) }, download); throw new Exception("Corrupt update was accepted."); }
        catch (InvalidDataException) { }
        Require(!File.Exists(download), "Failed verification must delete the staged download.");
        Console.WriteLine("PASS: command grouping, quotes, continuations, unchanged unsupported input, release metadata and update download verification");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal sealed class FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(respond(request));
}
