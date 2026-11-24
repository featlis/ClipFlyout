# 開発者向けメモ

## 開発要件

- Windows 11 / 10 (x64)
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Inno Setup 6](https://jrsoftware.org/isdl.php)（インストーラーパッケージ生成時のみ）

Issue や PR の進め方は [CONTRIBUTING.md](../CONTRIBUTING.md) を参照してください。
なお、CONTRIBUTING.md の開発要件は .NET 9 のまま古くなっているので、実態はこちらの .NET 10 が正です。

## ビルドと単体テスト

```powershell
# プロジェクトのビルド
dotnet build ClipFlyout.csproj -c Release

# 単体テストの実行
dotnet test tests/ClipFlyout.Tests/ClipFlyout.Tests.csproj -c Release
```

## 配布パッケージの生成

```powershell
# バージョンを指定してインストーラーとポータブル ZIP を生成
powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1 -Version 2.2.0
```

実行後、`dist/` ディレクトリに以下のファイルが出力されます。

- `ClipFlyout-Setup-v2.2.0.exe`（Inno Setup インストーラー）
- `ClipFlyout-v2.2.0-win-x64.zip`（自己完結型ポータブルパッケージ）
- `checksums-sha256.txt`（SHA-256 チェックサム一覧）

## アーキテクチャ概要

- **`Services/ClipboardMonitor.cs`**: `AddClipboardFormatListener` (Win32 API) によるイベント駆動型クリップボード監視。テキスト / FileDropList / PNG・ビットマップ画像に対応し、アプリ自身の書き込みはシーケンス番号で判定してループを防ぎます。パスワードマネージャー等の除外判定（コピー元プロセス名と、クリップボード側の除外指定形式の検証）もここです。一時停止（15 分 / 1 時間）の状態も持ちます
- **`Services/DataTypeDetector.cs`**: 優先度順の正規表現と構文解析によるデータ型判定。巨大データ向けの保護リミットと、日付・電話番号・バージョン文字列などの誤検知防止フィルタ付き
- **`Services/ActionExecutor.cs`**: 判定結果に対応する変換・整形・保存アクションの実行
- **`Services/FlyoutWindowManager.cs`**: `WS_EX_NOACTIVATE` によるフォーカスを奪わないウィンドウ制御、Per-Monitor V2 DPI 対応の配置、全画面アプリ使用中の自動抑制、キーボード操作（`Esc` / `↑` `↓` / `Enter` / 数字キー）
- **`Services/HotkeyService.cs`**: リコールショートカット（既定は `Alt + Shift + C`、変更可能）の登録と監視
- **`Services/HistoryService.cs` / `Services/SnippetService.cs`**: コピー履歴（ピン留め対応、永続化は設定でオプトイン）と定型文の管理
- **`Services/OcrService.cs`**: Windows 標準 OCR によるオンデバイスの画像文字起こし
- **`Services/DeveloperToolsHelper.cs`**: 数式・進数変換、UUID 生成、パス変換などの開発者向け変換処理
- **`Views/TaskbarWidgetWindow.xaml.cs`**: タスクバー上の常時表示ウィジェット。自動非表示への追従と、全画面アプリ表示中の自動抑止を行います
- **`Services/TaskbarColorDetector.cs`**: タスクバーの背景色から輝度を判定し、ウィジェットの文字色を切り替えます
- **`Services/ThemeService.cs`**: システム / ライト / ダークのテーマ適用と、アクセントカラーの自動調整（テーマに調和する色への変換）
- **`Views/SettingsWindow.xaml`**: 実 Mica 背景＋PowerToys スタイルのナビゲーションを持つ設定画面。外観、配置、検出フィルター、除外リスト、定型文、アップデート確認を扱います
- **`Services/UpdateService.cs`**: GitHub Releases からの更新確認と取得。実行前にリリースの SHA-256 一覧で検証します
- **`Controls/ToggleSwitch.xaml.cs`**: WinUI 3 準拠のホバー・ストレッチアニメーション付きトグルスイッチ

主な依存パッケージは H.NotifyIcon.Wpf（トレイアイコン）と QRCoder（QR コード生成）です。詳細は `ClipFlyout.csproj` を参照してください。
