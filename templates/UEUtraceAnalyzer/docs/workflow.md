# 開発フロー

1. GitHub Issueに要望・再現手順・期待する動作を登録します。
2. Codex Cloudのチャットで対象Issueとリリース版番号を指定し、実装と公開を依頼します。
3. CodexがGitHubからIssueとソースを確認し、developから作業ブランチを作ります。
4. 変更とPRをGitHubに送信し、Windows Actionsでビルド・コア/UIテストを行います。
5. 対象Issueの機能完成とCI成功後にdevelopへマージします。
6. リリースの対象Issueがすべて完了したらmainへマージし、Windows Actionsが配布用ZIPとソースZIPを公開します。
7. 更新機能を画面に接続した後は、アプリから正式版を確認・取得できます。

アプリのIssue作成コードとCodex Cloudは直接通信しません。GitHubのIssueとPRを通じて情報を共有し、実装の開始はチャットで依頼します。共通コードだけではMainWindowに要望・更新ボタンは表示されません。
