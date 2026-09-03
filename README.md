# ClipFlyout

コピーした内容に合わせて、すぐ使える操作を表示する Windows 向け常駐ユーティリティです。

JSON、URL、カラーコード、画像、CSV / TSV などをコピーすると、画面端の小さなフライアウトに変換・保存・コピーの操作が現れます。フライアウトはフォーカスを奪わないため、作業を中断せずに使えます。

> 現在のバージョン: **v0.5.0**

## できること

| コピーしたもの | 主な操作 |
| --- | --- |
| HEX カラー | RGB / HSL / RGBA へ変換してコピー |
| Unix タイムスタンプ | ローカル日時・ISO 8601 へ変換 |
| JSON | 整形・圧縮してコピー |
| URL | ブラウザで開く、QR画像をコピー、ドメインをコピー |
| メールアドレス | 新規メールを作成、ドメイン・ユーザー名をコピー |
| Base64 / Data URI | テキストを復号、画像としてコピー |
| CSV / TSV | MarkdownテーブルまたはJSON配列へ変換 |
| コード | インデントを整える、HTMLエスケープ |
| 画像 | 解像度を確認、PNG / JPEG / BMPとして保存 |
| 通常のテキスト | 前後の空白を削除、文字数をコピー、大小文字を変換 |

## 使い方

1. ClipFlyout を起動します。アプリは通知領域に常駐します。
2. 対応するデータをコピーします。
3. 表示されたフライアウトから操作を選びます。先頭の青いボタンが主な操作です。

通知領域のアイコンを右クリックすると、監視の一時停止、テーマと言語の変更、設定画面の表示、終了ができます。

## インストール

### セットアップ版

[Releases](https://github.com/featlis/ClipFlyout/releases) から `ClipFlyout-Setup-vX.Y.Z.exe` をダウンロードして実行してください。ユーザー権限でインストールでき、デスクトップショートカットとスタートアップ登録を選べます。

### ポータブル版

`ClipFlyout-vX.Y.Z-win-x64.zip` を展開し、`ClipFlyout.exe` を起動します。インストールは不要です。

## 設定

設定画面では、以下を変更できます。

- 検出するデータ型ごとのオン・オフ
- 表示位置（四隅またはマウスカーソル付近）
- フライアウトの透明度と自動非表示時間
- アクセントカラー（ブルー、パープル、ピンク、グリーン、オレンジ）
- Windows起動時の開始
- システム連動 / ライト / ダークテーマ
- 日本語 / English / OS設定に合わせた表示言語
- GitHub Releases の SHA-256 を検証する自動アップデート（設定からオフにもできます）

クリップボードの解析はローカルで行われます。

## 開発

### 必要なもの

- Windows x64
- .NET 9 SDK
- Inno Setup 6（インストーラー作成時のみ）

### テスト

```powershell
dotnet test tests/ClipFlyout.Tests/ClipFlyout.Tests.csproj -c Release
```

テストは設定ファイルとスタートアップ登録を一時領域へ隔離して実行します。

### 配布物の作成

```powershell
./scripts/build-installer.ps1 -Version 0.5.0
```

以下が `dist/` に生成されます。

- `ClipFlyout-Setup-v0.5.0.exe`
- `ClipFlyout-v0.5.0-win-x64.zip`

`dist/`、`publish/`、`bin/`、`obj/` はGitの追跡対象外です。

### GitHub Release

タグをプッシュすると、GitHub Actions がZIP、インストーラー、SHA-256チェックサムを生成してReleaseへ公開します。

```bash
git add .
git commit -m "feat: release v0.5.0"
git tag v0.5.0
git push origin main
git push origin v0.3.0
```

## 技術概要

- C# / .NET 9 / WPF
- Windows のクリップボード更新通知 (`AddClipboardFormatListener`)
- 非アクティブなフライアウト (`WS_EX_NOACTIVATE`)
- QRCoder、Inno Setup、xUnit

開発時の構成メモは [docs/ai_state.json](docs/ai_state.json) にあります。
