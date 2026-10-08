using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace UEUtraceAnalyzer.Core;

public sealed record SubmittedIssue(int Number, Uri Url);

public sealed class IssueRequestClient(HttpClient http, string token)
{
    public const string RepositoryUrl = "https://github.com/posita33/UEUtraceAnalyzer";
    private const string Api = "https://api.github.com/repos/posita33/UEUtraceAnalyzer/";

    public async Task<SubmittedIssue> CreateAsync(string title, string body, string version, CancellationToken cancellation = default)
    {
        title = title.Trim();
        if (title.Length is < 1 or > 200 || string.IsNullOrWhiteSpace(body) || body.Length > 60_000)
            throw new ArgumentException("タイトルは1～200文字、要望は1～60,000文字で入力してください。");
        using var response = await SendAsync(HttpMethod.Post, "issues", new { title, body = body.Trim() + "\n\n送信元: UEUtraceAnalyzer " + version }, cancellation);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        var number = json.RootElement.GetProperty("number").GetInt32();
        if (number <= 0) throw new InvalidDataException("Issue番号を確認できませんでした。");
        return new SubmittedIssue(number, new Uri(RepositoryUrl + "/issues/" + number));
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? data, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Any(char.IsWhiteSpace))
            throw new ArgumentException("GitHubトークンを入力してください。");
        using var request = new HttpRequestMessage(method, Api + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.ParseAdd("UEUtraceAnalyzer-Requests/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (data != null) request.Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");
        var response = await http.SendAsync(request, cancellation);
        if (response.IsSuccessStatusCode) return response;
        var status = response.StatusCode;
        response.Dispose();
        throw new HttpRequestException(status switch
        {
            HttpStatusCode.Unauthorized => "認証できません。GitHubトークンの期限・内容を確認してください。",
            HttpStatusCode.Forbidden => "GitHubの権限不足、またはAPIの利用制限です。Issue書き込み権限を確認してください。",
            HttpStatusCode.NotFound => "Issueまたはリポジトリが見つかりません。番号・リポジトリへのアクセスを確認してください。",
            HttpStatusCode.UnprocessableEntity => "GitHubが依頼を受け付けませんでした。Issue内容やリポジトリの設定を確認してください。",
            _ => "GitHubへの送信に失敗しました。HTTP " + (int)status
        }, null, status);
    }
}
