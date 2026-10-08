# UEUtraceAnalyzer

- WPF / C# / .NET 8。MainWindowは空の開発開始用テンプレート。
- ユーザーが依頼したIssueを確認し、developから作業ブランチを作る。
- 解析機能は未実装。機能追加はユーザーの依頼に合わせて行う。
- MainWindowに部品を置くのは、画面機能を追加する依頼を受けた後。
- Windowsで `dotnet build UEUtraceAnalyzer.sln --configuration Release`、コア/UIスモークテストを実行する。
- 機能完成後はdevelop向けPRを作り、ユーザーが許可した範囲でマージする。
- mainへの新しい版番号のマージで正式版が公開される。mainへマージする前に対象Issue完了とCI成功を確認する。
- 版番号はsrc/UEUtraceAnalyzer/UEUtraceAnalyzer.csprojで管理し、UIテストの初期版チェックとdocs/releases/vX.Y.Z.mdも更新する。
- GitHubトークンなどの秘密をコード、Issue、ログ、ZIPに入れない。
- 共通処理はCore、画面とWindows固有の処理はWPFプロジェクトへ置く。
