using System.Net;
using System.Text.Json;
using Footprint.Core;

internal static class IssueRequestChecks
{
    public static async Task RunAsync()
    {
        var calls = 0;
        using var http = new HttpClient(new FakeHttpHandler(request =>
        {
            calls++;
            Require(request.RequestUri!.Host == "api.github.com" && request.Headers.Authorization?.Scheme == "Bearer" &&
                request.Headers.Authorization.Parameter == "test-token", "Requests must authenticate only to GitHub API.");
            if (request.RequestUri.AbsolutePath.EndsWith("/dispatches"))
            {
                using var json = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                Require(json.RootElement.GetProperty("ref").GetString() == "main" &&
                    json.RootElement.GetProperty("inputs").GetProperty("issue_number").GetString() == "72", "Dispatch must target main and the selected Issue.");
                return new(HttpStatusCode.NoContent);
            }
            if (request.Method == HttpMethod.Post)
            {
                using var json = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                Require(json.RootElement.GetProperty("title").GetString() == "日本語の要望" &&
                    json.RootElement.GetProperty("body").GetString() == "再現手順\n\n送信元: Footprint v1.3.7", "Issue must contain only user text and app version.");
            }
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"number\":72,\"state\":\"open\",\"html_url\":\"https://evil.example\"}") };
        }));
        var client = new IssueRequestClient(http, "test-token");
        var result = await client.CreateAsync(" 日本語の要望 ", "再現手順", "v1.3.7");
        Require(result.Number == 72 && result.Url.AbsoluteUri == IssueRequestClient.RepositoryUrl + "/issues/72", "Issue URL must be trusted and based on its number.");
        await client.DispatchAsync(72);
        Require(calls == 3, "Dispatch must validate the Issue before submitting.");
        try { await client.CreateAsync("", "body", "v1.3.7"); throw new Exception("Empty title accepted"); } catch (ArgumentException) { }
        Require(calls == 3, "Invalid content must not be sent.");
        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.UnprocessableEntity })
        {
            using var failed = new HttpClient(new FakeHttpHandler(_ => new(status) { Content = new StringContent("secret-token") }));
            try { await new IssueRequestClient(failed, "test-token").CreateAsync("title", "body", "v1.3.7"); throw new Exception("HTTP error accepted"); }
            catch (HttpRequestException error) { Require(error.StatusCode == status && !error.Message.Contains("secret-token"), "Error must preserve status without response secrets."); }
        }
        foreach (var json in new[] { "{\"state\":\"closed\"}", "{\"state\":\"open\",\"pull_request\":{}}" })
        {
            var count = 0;
            using var rejected = new HttpClient(new FakeHttpHandler(_ => { count++; return new(HttpStatusCode.OK) { Content = new StringContent(json) }; }));
            try { await new IssueRequestClient(rejected, "test-token").DispatchAsync(72); throw new Exception("Invalid Issue accepted"); }
            catch (InvalidOperationException) { Require(count == 1, "Closed Issues and PRs must not dispatch."); }
        }
        Console.WriteLine("PASS: Issue submission, authenticated workflow dispatch, validation and safe GitHub errors");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
