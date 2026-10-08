using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UEUtraceAnalyzer.Core;

var directory = Path.Combine(Path.GetTempPath(), "UEUtraceAnalyzer-tests-" + Guid.NewGuid().ToString("N"));
try
{
    var settings = new AppearanceSettingsStore(Path.Combine(directory, "Settings.json"));
    Require(!settings.Load().DarkTheme && settings.Load().CommandFontSize == 12, "Default settings");
    settings.Save(new AppearanceSettings { DarkTheme = true, CommandFontSize = 20 });
    Require(settings.Load() is { DarkTheme: true, CommandFontSize: 20 }, "Settings round-trip");
    try { settings.Save(new AppearanceSettings { CommandFontSize = 99 }); throw new Exception("Invalid font accepted"); }
    catch (ArgumentException) { }
    Require(settings.Load().CommandFontSize == 20, "Invalid settings must retain the prior file");

    using var issueHttp = new HttpClient(new FakeHandler(request =>
    {
        Require(request.RequestUri!.Host == "api.github.com" &&
            request.Headers.Authorization?.Parameter == "test-token", "GitHub authentication");
        using var doc = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
        Require(doc.RootElement.GetProperty("title").GetString() == "日本語の要望" &&
            doc.RootElement.GetProperty("body").GetString() == "期待する動作\n\n送信元: UEUtraceAnalyzer v0.1.0",
            "Only requested content and app version must be sent");
        return new(HttpStatusCode.Created) { Content = new StringContent("{\"number\":10}") };
    }));
    var submitted = await new IssueRequestClient(issueHttp, "test-token").CreateAsync("日本語の要望", "期待する動作", "v0.1.0");
    Require(submitted.Url.ToString() == "https://github.com/posita33/UEUtraceAnalyzer/issues/10", "Trusted Issue URL");

    var payload = Encoding.UTF8.GetBytes("verified update payload 日本語");
    var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    string Metadata(string digest, string url = "https://github.com/posita33/UEUtraceAnalyzer/releases/download/v0.2.0/UEUtraceAnalyzer-win-x64.zip") =>
        JsonSerializer.Serialize(new { tag_name = "v0.2.0", draft = false, prerelease = false,
            assets = new[] { new { name = "UEUtraceAnalyzer-win-x64.zip", browser_download_url = url, digest = "sha256:" + digest } } });
    var release = ReleaseUpdateClient.Parse(Metadata(hash));
    Require(release.Version == new Version(0, 2, 0), "Stable release metadata");
    try { ReleaseUpdateClient.Parse(Metadata(hash, "https://example.com/app.zip")); throw new Exception("Untrusted update accepted"); }
    catch (InvalidDataException) { }
    using var updateHttp = new HttpClient(new FakeHandler(request => new(HttpStatusCode.OK)
    {
        Content = request.RequestUri!.Host == "api.github.com" ? new StringContent(Metadata(hash)) : new ByteArrayContent(payload)
    }));
    var updates = new ReleaseUpdateClient(updateHttp);
    Require((await updates.GetLatestAsync()).Version == release.Version, "Latest release HTTP parsing");
    var destination = Path.Combine(directory, "update.zip");
    await updates.DownloadAsync(release, destination);
    Require(File.ReadAllBytes(destination).SequenceEqual(payload), "SHA-256 verified download");
    File.Delete(destination);
    try { await updates.DownloadAsync(release with { Sha256 = new string('0', 64) }, destination); throw new Exception("Corrupt update accepted"); }
    catch (InvalidDataException) { }
    Require(!File.Exists(destination), "Corrupt staged update must be deleted");
    Console.WriteLine("PASS: settings, GitHub Issue request, release metadata and verified downloads");
}
finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }

static void Require(bool success, string message) { if (!success) throw new InvalidOperationException(message); }

sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(respond(request));
}
