# ClipFlyout へのコントリビュートについて

ClipFlyout にご関心をお寄せいただきありがとうございます！
バグ報告、機能提案、Pull Request (PR) など、あらゆる形での貢献を歓迎します。

## Issue の報告

バグや機能要望がある場合は、まず [Issues](https://github.com/featlis/ClipFlyout/issues) を検索し、既に類似の報告がないか確認してください。
新しいIssueを作成する場合は、用意されているテンプレート（バグ報告・機能要望）に沿って必要な情報を記載してください。

## 開発環境の構築

ClipFlyout の開発を始めるための要件は以下の通りです：

- Windows 11 (または Windows 10 x64)
- .NET 9.0 SDK
- Visual Studio 2022 または Visual Studio Code (C# Dev Kit 拡張機能)

リポジトリをクローンしたら、`dotnet build` を実行してプロジェクトが正常にビルドできることを確認してください。

## Pull Request の作成手順

1. このリポジトリをフォークします。
2. 作業用のブランチを作成します。（例: `feature/add-new-detector` や `fix/memory-leak`）
3. コードを修正し、必要であればテストを追加します。
4. `dotnet format` を実行し、コードのスタイルを統一してください。
5. `dotnet test` を実行し、すべてのテストがパスすることを確認します。
6. コミットを作成し、フォークしたリポジトリにプッシュします。
7. 本リポジトリに対して Pull Request を作成します。PRテンプレートに沿って変更内容を記載してください。

## コーディング規約について

- モダンなC# (.NET 9 / C# 13 推奨記法) を活用してください。
- リソース（IDisposable）の解放漏れに注意してください。
- 追加する機能がタスクバーや他のウィンドウのZ-orderに影響を与える場合は、Windowsの挙動（マルチモニター環境含む）を考慮してください。

不明な点があれば、お気軽に Issue や Discussions でご質問ください。
