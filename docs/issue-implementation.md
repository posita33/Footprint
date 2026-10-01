# 要望から実装・ビルド・更新まで

v1.3.7の画面上部、ヘルプ・設定の左側に「要望」「実装・ビルド」があります。

1. 「要望」でタイトル、再現手順、期待する動作を入力します。要望は公開GitHub Issueになります。個人情報、トークン、秘密のパス等は含めないでください。コマンド・履歴・出力は自動添付しません。
2. GitHubトークンを入力して「Issueを追加」を押します。トークンはメモリ上でのみ使用し、画面を閉じると破棄します。保存ファイルには書きません。トークンがなくても「ブラウザーでIssue作成」からGitHubにログインして作成できます（本文はURL長を抑えるため先頭4,000文字まで）。
3. 作成された番号が自動入力されます。他の既存Issue番号も指定できます。「実装・ビルドを依頼」でGitHub Actionsへ送信します。実装が完了したという意味ではありません。GitHub Actions・OpenAI APIの利用料金が発生する場合があります。
4. 「実行状況・ビルド結果」でIssue番号の実行を開きます。成功するとdevelop向けPRとIssueコメントが作られ、実行ページのArtifactsに `Footprint-Issue-番号-win-x64` が表示されます。GitHubにログインしてダウンロードし、外側のZIPと中の `Footprint-win-x64.zip` を展開して試用できます。Artifactは30日で期限切れになります。
5. 管理者がPRを確認してdevelopへマージします。対象リリースのIssueが完了したら版番号を更新し、mainへマージして `vX.Y.Z` タグをpushします。既存のReleaseワークフローが正式版ZIPを公開します。
6. アプリの「最新版を確認・更新」で正式版を確認し、更新できます。試用ビルドは正式版更新には含まれません。

通信が切れた場合、送信が成功している可能性があります。再送する前にIssue一覧／実行状況を確認してください。同じ画面から同じIssueを連続依頼できません。別画面から依頼した場合はGitHubの同じIssueの実行が直列化されます。

## アプリ側の認証

GitHub Settings → Developer settings → Personal access tokens → Fine-grained tokensで、対象リポジトリ `posita33/Footprint` にアクセスできるトークンを作成します。

- Issue作成: Repository permissions → Issues: Read and write。
- 実装依頼: Issues: Read、Actions: Read and write。リポジトリでActionsを実行できるユーザー権限が必要です。
- 不要になったトークンはGitHubで失効させてください。画面を閉じる操作はGitHub側の失効ではありません。

## 管理者の事前設定

- リポジトリSettings → Secrets and variables → Actionsに `OPENAI_API_KEY` を登録します。キーはチャット、Issue、アプリへ入力せずGitHub Secretに直接登録してください。APIの利用枠も必要です。
- Settings → Actions → GeneralでActionsを有効化し、`openai/codex-action` と使用するGitHub公式Actionsを許可します。Workflow permissionsの「Allow GitHub Actions to create and approve pull requests」を有効にします。
- ワークフロー `.github/workflows/implement-issue.yml` がmainにあること、developが存在することを確認します。手動実行はmainを選択してください。
- GitHub側のブランチ保護や組織のポリシーでPR作成が禁止されている場合は管理者が設定を調整してください。自動処理は保護ルールを迂回せず、PRのマージ・リリースは管理者が行います。

自動実装はUbuntuのCodexで変更を作成し、Windows上でビルド、コア・UIスモークテスト、自己完結ZIP生成を行います。Codexには書き込み用GitHubトークンを渡さず、ワークフロー変更は拒否します。Issueの解釈や実装が誤る場合もあるためPRと試用版を確認してください。Actionsの手動実行で `check_setup_only` を有効にすると、OpenAI APIを呼び出さずSecretの有無を確認できます（Issue番号欄は任意の正整数を入力）。キー未設定の場合は実行が明確なエラーで停止します。失敗したビルドから正式リリースは作りません。
