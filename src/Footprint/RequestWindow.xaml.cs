using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using Footprint.Core;

namespace Footprint;

public partial class RequestWindow : Window
{
    private readonly Action _update;
    private readonly CancellationTokenSource _cancellation = new();
    private bool _busy;
    private int? _lastDispatched;

    public RequestWindow(bool implementation, Action update)
    {
        InitializeComponent();
        _update = update;
        Loaded += (_, _) => { if (implementation) IssueNumberBox.Focus(); else TitleBox.Focus(); };
        Closed += (_, _) => { TokenBox.Password = ""; _cancellation.Cancel(); _cancellation.Dispose(); };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Do not abandon an in-flight POST: its result determines whether retrying duplicates an Issue.
        if (_busy) { e.Cancel = true; StatusText.Text = "送信結果を確認中です。完了してから閉じてください。"; }
        base.OnClosing(e);
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        await SendAsync(async client =>
        {
            var issue = await client.CreateAsync(TitleBox.Text, BodyBox.Text, MainWindow.VersionLabel, _cancellation.Token);
            IssueNumberBox.Text = issue.Number.ToString();
            CreateButton.IsEnabled = false;
            StatusText.Text = $"Issue #{issue.Number} を作成しました。続けて実装・ビルドを依頼できます。";
        }, "Issueを送信中…");
    }

    private async void Implement_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(IssueNumberBox.Text.Trim().TrimStart('#'), out var number) || number <= 0)
        { StatusText.Text = "正しいIssue番号を入力してください。"; return; }
        if (_lastDispatched == number) { StatusText.Text = "このIssueは依頼済みです。「実行状況・ビルド結果」で確認してください。"; return; }
        if (MessageBox.Show(this, $"Issue #{number} の自動実装・ビルドを依頼します。GitHub ActionsとOpenAI APIの利用料金が発生する場合があります。依頼しますか？",
            "実装・ビルド", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        await SendAsync(async client =>
        {
            await client.DispatchAsync(number, _cancellation.Token);
            _lastDispatched = number;
            StatusText.Text = $"Issue #{number} の依頼を受け付けました。実装完了ではありません。「実行状況・ビルド結果」で処理状況とZIPを確認してください。";
        }, "実装・ビルドを依頼中…");
    }

    private async Task SendAsync(Func<IssueRequestClient, Task> send, string message)
    {
        if (_busy) return;
        _busy = true;
        var createEnabled = CreateButton.IsEnabled;
        CreateButton.IsEnabled = ImplementButton.IsEnabled = UpdateButton.IsEnabled = false;
        TokenBox.IsEnabled = TitleBox.IsEnabled = BodyBox.IsEnabled = IssueNumberBox.IsEnabled = false;
        BusyIndicator.Visibility = Visibility.Visible;
        StatusText.Text = message;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            await send(new IssueRequestClient(http, TokenBox.Password.Trim()));
            if (message.StartsWith("Issue")) createEnabled = false;
        }
        catch (Exception error)
        {
            StatusText.Text = error is TaskCanceledException or HttpRequestException { StatusCode: null }
                ? "通信結果を確認できませんでした。重複送信を避けるため、Issue・実行状況を確認してから再試行してください。"
                : error.Message;
        }
        finally
        {
            _busy = false;
            CreateButton.IsEnabled = createEnabled;
            ImplementButton.IsEnabled = UpdateButton.IsEnabled = true;
            TokenBox.IsEnabled = TitleBox.IsEnabled = BodyBox.IsEnabled = IssueNumberBox.IsEnabled = true;
            BusyIndicator.Visibility = Visibility.Collapsed;
        }
    }

    private void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception error) { StatusText.Text = "ブラウザーを開けませんでした。" + error.Message; }
    }
    private void Setup_Click(object sender, RoutedEventArgs e) => Open(IssueRequestClient.RepositoryUrl + "/blob/main/docs/issue-implementation.md");
    private void BrowserIssue_Click(object sender, RoutedEventArgs e) => Open(IssueRequestClient.RepositoryUrl + "/issues/new?title=" + Uri.EscapeDataString(TitleBox.Text) + "&body=" + Uri.EscapeDataString(BodyBox.Text.Length > 4000 ? BodyBox.Text[..4000] : BodyBox.Text));
    private void Issue_Click(object sender, RoutedEventArgs e) => Open(IssueRequestClient.RepositoryUrl + "/issues" + (int.TryParse(IssueNumberBox.Text.Trim().TrimStart('#'), out var number) && number > 0 ? "/" + number : ""));
    private void Runs_Click(object sender, RoutedEventArgs e) => Open(IssueRequestClient.WorkflowUrl);
    private void Update_Click(object sender, RoutedEventArgs e) { Close(); _update(); }
}
