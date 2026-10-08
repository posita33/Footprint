# UEUtraceAnalyzer

Footprintと同じ開発・配布の流れを使う、新しいWPFツールの初期ソースです。MainWindowは空のGridのみで、ボタン・メニュー・入力欄はありません。初期バージョンは0.1.0です。

## ZIPの内容

- UEUtraceAnalyzer.sln：Visual Studioのソリューション。
- src/UEUtraceAnalyzer：空のMainWindow、App、設定画面、要望画面、暗号化トークン保存、更新処理。
- src/UEUtraceAnalyzer.Core：外観設定、GitHub Issue作成、最新版確認・SHA-256検証付きダウンロード。
- tests：コアとWindows UIのスモークテスト。
- .github：Issue/PRテンプレート、Windowsビルド、正式リリース用ワークフロー。
- AGENTS.md：Codexが作業する際のプロジェクト説明。
- docs：共通機能の接続方法、GitHub・Codexとの開発フロー、リリース説明。

コマンド実行・履歴・バックアップ・タブ・UEトレース解析は含めていません。新しい解析ツールに必要な機能は、この土台へ追加してください。共通画面はソースだけを用意してあり、MainWindowからはまだ呼び出しません。

## 開発と起動

Windows 10/11、Visual Studio 2022 17.8以降の「.NETデスクトップ開発」、.NET 8 SDKを使用します。ZIPを展開し、UEUtraceAnalyzer.slnを開いてUEUtraceAnalyzerをスタートアッププロジェクトに指定し、F5で起動してください。

```powershell
dotnet build UEUtraceAnalyzer.sln --configuration Release
dotnet run --project src/UEUtraceAnalyzer
dotnet run --project tests/UEUtraceAnalyzer.Core.SmokeTests --configuration Release
dotnet run --project tests/UEUtraceAnalyzer.Ui.SmokeTests --configuration Release
```

UIテストは実際のWindows WPFとDPAPI、PowerShellの更新用ファイル差し替え・失敗時の復元を確認します。テストは一時フォルダーとダミーのトークンだけを使用し、実際のIssueを作成しません。

## GitHubリポジトリの初期設定

GitHubで空のリポジトリ `posita33/UEUtraceAnalyzer` を作成し、展開したフォルダーで以下を実行します。GitとGitHubへの認証は事前に設定してください。GitHub側でREADME等を自動追加しない空のリポジトリを使います。

```powershell
git init -b main
git add .
git commit -m "Initial UEUtraceAnalyzer WPF template"
git remote add origin https://github.com/posita33/UEUtraceAnalyzer.git
git push -u origin main
git switch -c develop
git push -u origin develop
```

mainへの初回pushでv0.1.0のビルド・テスト・公開が始まります。Actionsを無効にしている場合は先に有効化してください。共通Issue・更新コードの接続先はこのリポジトリ名です。別の所有者を使う場合はCoreのIssueRequestClient.cs、ReleaseUpdateClient.csのGitHub URLを変更してください。

- GitHub Settings → Actions → GeneralでGitHub公式Actionsを許可します。
- リリース用ワークフローはcontents: writeを要求します。組織のポリシーがある場合は管理者が許可してください。
- main/developには必要に応じてPRとCI成功を必須にする保護ルールを設定します。
- Codex Cloudに新しいリポジトリを接続し、環境で.NET 8とGitHub CLIが使えるようにします。WPFの実行確認はWindows Actionsで行います。
- 必要な外部通信はGitHub APIとNuGet/.NETの取得先です。認証情報はCodex環境のSecretとして設定し、ソースには保存しません。
- OpenAI APIキーを使う自動実装ワークフローはありません。Issueの実装はCodex Cloudのチャットで依頼します。
- 最新版確認コードは公開GitHub Release用です。非公開リポジトリを使う場合は更新取得にも認証を追加してください。

## リリース

`src/UEUtraceAnalyzer/UEUtraceAnalyzer.csproj` のVersionを更新し、`docs/releases/vX.Y.Z.md` とUIテストの版番号チェックも更新します。対象Issueの実装とWindows CIが完了した後にdevelopからmainへPRでマージします。

Windowsビルド・コア/UIテストに成功すると、対応するvX.Y.Zタグと正式リリースを作成します。

- UEUtraceAnalyzer-win-x64.zip：自己完結・単一EXEの実行用ZIP。
- UEUtraceAnalyzer-source.zip：ソースコードとリポジトリ設定のZIP。

mainの公開済みバージョンは再公開しません。タグpushも利用できます。手動実行では成果物を生成します。ワークフロー変更のPRでは配布ZIPまで検証しますが、正式版は公開しません。GitHub公式ActionsとGitHub CLIを使うので、Footprintで発生した外部リリースActionの許可エラーを避けられます。

共通機能を画面へ接続する方法は[共通機能](docs/common-services.md)、運用は[開発フロー](docs/workflow.md)を参照してください。
