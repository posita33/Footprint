# 要望画面の共通コード

RequestWindow.xaml / .csはGitHub Issue作成用です。MainWindowにはまだ入口を置いていません。接続方法はcommon-services.mdを参照してください。

GitHubのFine-grained personal access tokenでposita33/UEUtraceAnalyzerへのIssues: Read and writeを許可します。トークンは保存を選択するとWindows DPAPIで暗号化し、%LOCALAPPDATA%\UEUtraceAnalyzer\GitHubSettings.jsonへ保存します。次回自動入力し、保存済みトークンを削除できます。

公開Issueに個人情報や秘密を含めないでください。送信するのは要望本文・タイトル・アプリの版番号だけです。トークンは認証に使い、本文へ追加しません。通信失敗時は重複作成を避けるためIssue一覧を確認してから再送してください。

OpenAI APIキーは不要です。実装依頼はCodex Cloudのチャットで行います。
