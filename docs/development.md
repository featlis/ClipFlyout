# 開発者向けメモ

## 開発要件

- Windows 11 / 10 (x64)
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Inno Setup 6](https://jrsoftware.org/isdl.php)（インストーラーパッケージ生成時のみ）

Issue や PR の進め方は [CONTRIBUTING.md](../CONTRIBUTING.md) を参照してください。

## ビルドと単体テスト

```powershell
# ソリューションのビルド
dotnet build ClipFlyout.csproj -c Release

# 単体テストの実行
dotnet test tests/ClipFlyout.Tests/ClipFlyout.Tests.csproj -c Release
```

## 配布パッケージの生成

```powershell
# バージョンを指定してインストーラーとポータブル ZIP を生成
powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1 -Version <version>
```

実行後、`dist/` ディレクトリにインストーラー、ポータブル ZIP、SHA-256 チェックサム一覧（`checksums-sha256.txt`）が出力されます。

## アーキテクチャ概要

- **`Services/ClipboardMonitor.cs`**: `AddClipboardFormatListener` (Win32 API) によるイベント駆動型クリップボード監視。アプリ自身の書き込みをシーケンス番号で判定してループを防ぎ、他プロセスとの競合時はリトライします。パスワードマネージャーの除外判定（コピー元プロセス名）もここです
- **`Services/DataTypeDetector.cs`**: 優先度順の正規表現と構文解析によるデータ型判定。巨大データ向けの保護リミット付き
- **`Services/FlyoutWindowManager.cs`**: `WS_EX_NOACTIVATE` によるフォーカスを奪わないウィンドウ制御と、Per-Monitor DPI 対応の画面端配置
- **`Views/TaskbarWidgetWindow.xaml.cs`**: タスクバー上の常時表示ウィジェット。自動非表示への追従と、全画面アプリ表示中の自動抑止を行います
- **`Services/TaskbarColorDetector.cs`**: タスクバー領域の背景色をサンプリングし、輝度に応じてウィジェットの文字色を切り替えます
- **`Views/SettingsWindow.xaml`**: Mica デザインの設定画面。外観、配置、検出フィルター、アップデート確認、ライセンス表示を扱います
- **`Controls/ToggleSwitch.xaml.cs`**: アニメーション付きトグルスイッチのカスタムコントロール
