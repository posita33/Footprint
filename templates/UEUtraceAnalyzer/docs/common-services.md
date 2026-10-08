# 共通処理を画面に接続する

MainWindowは空のままです。必要になったらボタンやメニューのイベントから次のコードを呼び出せます。

```csharp
// 要望の作成
new RequestWindow { Owner = this }.ShowDialog();

// ライト/ダーク・文字サイズの設定
new SettingsWindow { Owner = this }.ShowDialog();

// 現在のアプリの版番号
var version = ApplicationInfo.VersionLabel;

// 正式版の確認・ダウンロード・検証・再起動
var result = await ApplicationUpdater.CheckAndUpdateAsync(this);
// AlreadyLatest / Cancelled / Restarting
```

更新は例外を呼び出し元で捕捉し、画面に理由を表示してください。将来解析処理を追加したら、処理終了後に更新するようガードを付けてください。保存すべき状態がある場合は `saveBeforeRestart: () => SaveState()` を渡し、保存成功時にtrueを返します。falseなら更新を中止します。

更新コードは単一EXEの配布ZIP、インストール先への書き込み権限、Windows PowerShellを必要とします。最新版ZIPのSHA-256とEXEの製品バージョンを検証し、差し替え失敗時は旧EXEに戻します。開発中の起動からは正式版EXEの差し替えを行いません。

設定は%LOCALAPPDATA%\UEUtraceAnalyzer\Settings.json、トークンは同フォルダーのGitHubSettings.jsonに保存します。Windowsユーザー・PCが変わると暗号化トークンを再入力する必要があります。SettingsWindowで文字サイズを変更しても、空のMainWindowには入力・出力部品がないため見た目はまだ変わりません。

共通処理は起動時にGitHubへ通信しません。要望作成・更新を実際に呼び出したときにだけ通信します。
