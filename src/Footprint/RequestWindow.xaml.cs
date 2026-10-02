using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using Footprint.Core;

namespace Footprint;

public partial class RequestWindow : Window
{
    private readonly GitHubTokenStore _tokens;
    private readonly CancellationTokenSource _cancellation = new();
    private bool _busy;
    private int? _createdIssue;

    public RequestWindow(GitHubTokenStore? tokens = null)
    {
        InitializeComponent();
        _tokens = tokens ?? new GitHubTokenStore();
        try { TokenBox.Password = _tokens.Load(); }
        catch (Exception) { StatusText.Text = "保存済みトークンを読み込めませんでした。入力し直して保存するか、保存済みトークンを削除してください。"; }
        Loaded += (_, _) => TitleBox.Focus();
        Closed += (_, _) => { TokenBox.Password = ""; _cancellation.Cancel(); _cancellation.Dispose(); };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_busy) { e.Cancel = true; StatusText.Text = "送信結果を確認中です。完了してから閉じてください。"; }
        base.OnClosing(e);
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _createdIssue != null) return;
        if (string.IsNullOrWhiteSpace(TitleBox.Text) || string.IsNullOrWhiteSpace(BodyBox.Text) ||
            string.IsNullOrWhiteSpace(TokenBox.Password))
        { StatusText.Text = "GitHubトークン、タイトル、要望を入力してください。"; return; }
        _busy = true;
        SetInputsEnabled(false);
        BusyIndicator.Visibility = Visibility.Visible;
        StatusText.Text = "Issueを送信中…";
        try
        {
            if (SaveTokenBox.IsChecked == true) _tokens.Save(TokenBox.Password);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var issue = await new IssueRequestClient(http, TokenBox.Password.Trim())
                .CreateAsync(TitleBox.Text, BodyBox.Text, MainWindow.VersionLabel, _cancellation.Token);
            _createdIssue = issue.Number;
            StatusText.Text = $"Issue #{issue.Number} を作成しました。「Issue・進捗」で確認できます。";
        }
        catch (Exception error)
        {
            StatusText.Text = error is TaskCanceledException or HttpRequestException { StatusCode: null }
                ? "通信結果を確認できませんでした。重複送信を避けるため、Issue一覧を確認してから再試行してください。"
                : error.Message;
        }
        finally
        {
            _busy = false;
            SetInputsEnabled(true);
            CreateButton.IsEnabled = _createdIssue == null;
            BusyIndicator.Visibility = Visibility.Collapsed;
        }
    }

    private void SetInputsEnabled(bool enabled)
    {
        CreateButton.IsEnabled = TokenBox.IsEnabled = TitleBox.IsEnabled = BodyBox.IsEnabled =
            SaveTokenBox.IsEnabled = DeleteTokenButton.IsEnabled = enabled;
    }

    private void DeleteToken_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _tokens.Delete();
            TokenBox.Password = "";
            SaveTokenBox.IsChecked = false;
            StatusText.Text = "保存済みトークンを削除しました。GitHub側のトークンを失効する場合はGitHubで操作してください。";
        }
        catch (Exception) { StatusText.Text = "保存済みトークンを削除できませんでした。設定ファイルへのアクセス権限を確認してください。"; }
    }

    private void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception error) { StatusText.Text = "ブラウザーを開けませんでした。" + error.Message; }
    }
    private void Setup_Click(object sender, RoutedEventArgs e) => Open(IssueRequestClient.RepositoryUrl + "/blob/main/docs/issue-implementation.md");
    private void BrowserIssue_Click(object sender, RoutedEventArgs e) => Open(IssueRequestClient.RepositoryUrl + "/issues/new?title=" + Uri.EscapeDataString(TitleBox.Text) + "&body=" + Uri.EscapeDataString(BodyBox.Text.Length > 4000 ? BodyBox.Text[..4000] : BodyBox.Text));
    private void Issue_Click(object sender, RoutedEventArgs e) => Open(IssueRequestClient.RepositoryUrl + "/issues" + (_createdIssue is int number ? "/" + number : ""));
}
