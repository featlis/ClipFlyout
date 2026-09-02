# ClipFlyout 🚀

Windows 11 Fluent Design に準拠した高機能・常駐型クリップボードデータ型検出フライアウトユーティリティ。

クリップボードにテキストや画像がコピーされた瞬間、データ形式（カラーコード、JSON、URL、コード、画像、Unixタイムスタンプ、Base64、表データ、テキスト）を瞬時に自動判別し、画面隅に作業を邪魔しない非アクティブフライアウト（`WS_EX_NOACTIVATE`）ですりガラス・アクリル調の洗練されたクイックアクションを表示します。

---

## ✨ 主な機能

- 🎨 **HEXカラー検出**: `#RGB`, `#RGBA`, `#RRGGBB`, `#RRGGBBAA` を即座にプレビュー。RGB / HSL / RGBA 形式へのワンクリック変換＆コピー。
- ⏱️ **Unixタイムスタンプ検出**: 10桁（秒）および13桁（ミリ秒）の数値を自動検知。人間が読みやすいローカル日時・UTC日時プレビュー、ISO 8601形式コピー、現在時刻Epochコピー。
- 📦 **JSON構文解析 & 整形**: JSON オブジェクト/配列を自動検出。インデント付き整形（Prettify）または1行化（Minify）してコピー。
- 🌐 **Web URL 検出**: ブラウザ起動、QRコード画像自動生成＆クリップボードコピー、ドメイン抽出。
- 🔒 **Base64 データ検出**: Base64文字列やData URI（`data:image/...`）を自動検知。プレーンテキスト復号コピー、画像データとしてのクリップボード展開。
- 📊 **表データ (CSV / TSV) 検出**: ExcelやGoogleスプレッドシートのセルコピー、TSV/CSVテキストを検知。Markdownテーブル表形式へのワンクリック整形コピー、JSON配列（Key-Value）への変換コピー。
- 💻 **コードスニペット検出**: 構文キーワード・インデント・記号を検知。インデント統一（2スペース化）やHTMLエンティティエスケープ。
- 🖼️ **画像プレビュー & 保存**: 解像度・アスペクト比の表示、PNGファイル保存、画像情報テキストコピー。
- 📝 **プレーンテキスト**: 空白トリム、文字数・単語数・行数統計、大文字/小文字変換。
- ⚙️ **PowerToys風 設定画面 (`SettingsWindow`)**:
  - 各種データ検出フィルターの個別ON/OFFトグル。
  - 表示位置のカスタマイズ（画面右下 / 画面右上 / 画面左上 / 画面左下 / マウスカーソル付近）。
  - 背景の透明度（アクリル効果）スライダー（50% 〜 100%）。
  - 自動非表示タイマー & ホバー解除後消滅タイマーの秒数調整。
  - Windows 起動時の自動開始トグル（スタートアップ同期）。
- 🌓 **テーマ切り替え (ライト / ダーク / システム連動)**:
  - Windows OSのテーマ設定（`AppsUseLightTheme`）にリアルタイム追従する「システム連動」を標準装備。「ライト」「ダーク」への手動固定も可能。
- 🌐 **多言語対応 (i18n)**: 日本語（Japanese）および英語（English）の自動検出および手動切り替えに対応。
- 🪟 **邪魔にならないUI/UX**:
  - `WS_EX_NOACTIVATE` 属性により、作業中のアプリからフォーカスを奪いません。
  - トースト通知がアクションボタンと被らないインラインフィードバック。
  - 長いボタンテキストでも折り返して美しく収まる `WrapPanel` レイアウト。
  - システムトレイ常駐（監視トグル、設定画面呼び出し、テーマ・言語切り替え、終了）。

---

## 🛠️ 技術スタック

- **言語 / ランタイム:** C# / .NET 9.0 (Windows Desktop WPF)
- **UI & スタイル:** Fluent Design (Acrylic すりガラス半透明マテリアル, 角丸, ドロップシャドウ, Windows 11 ToggleSwitch)
- **Win32 Interop:** `AddClipboardFormatListener`, `RemoveClipboardFormatListener`, `WM_CLIPBOARDUPDATE`, `WS_EX_NOACTIVATE`
- **QRコード生成:** QRCoder
- **インストーラー:** Inno Setup (スタンドアロン自己完結型、.NETの事前インストール不要)
- **テスト:** xUnit (17テスト完全パス)

---

## 📦 インストール & リリース

### 1. インストーラーによる導入
[Releases ページ](../../releases) より最新版のインストーラー（`ClipFlyout-Setup-vX.X.X.exe`）をダウンロードして実行してください。
デスクトップショートカットや Windows 起動時の自動開始を設定できます。

### 2. ポータブル版（インストール不要）
`ClipFlyout-vX.X.X-win-x64.zip` を解凍し、中にある `ClipFlyout.exe` を直接起動して利用することも可能です。

---

## 🔨 ローカルビルド & インストーラー作成

### インストーラーのローカル生成
```powershell
./scripts/build-installer.ps1 -Version 0.1.0
```
出力先: `dist/ClipFlyout-Setup-v0.1.0.exe` および `dist/ClipFlyout-v0.1.0-win-x64.zip`

### GitHub Release の自動公開手順
Git タグを付与してプッシュすると、GitHub Actions が自動でインストーラーと ZIP をビルドし、Release ページに公開します:
```bash
git add .
git commit -m "feat: release v0.1.0"
git tag v0.1.0
git push origin main
git push origin v0.1.0
```

---

## 🤖 AI状態管理メモ
開発経緯およびコンポーネント構成仕様は [docs/ai_state.json](docs/ai_state.json) にコンパクトに記録されています。