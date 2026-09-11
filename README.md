<div align="center">

<img src="assets/app-icon.png" alt="ClipFlyout Logo" width="96" height="96" />

# ClipFlyout

**Windows 11 Fluent Design に準拠したクリップボード解析・フライアウトユーティリティ**  
*A privacy-focused clipboard utility with native DWM acrylic flyouts and taskbar integration for Windows.*

[![Version](https://img.shields.io/badge/version-1.1.0-blue.svg)](https://github.com/featlis/ClipFlyout/releases)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-green.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2011%20%7C%2010%20(x64)-0078D4.svg)](https://github.com/featlis/ClipFlyout)
[![.NET](https://img.shields.io/badge/.NET-9.0%20WPF-512BD4.svg)](https://dotnet.microsoft.com/)

<br />

<img src="assets/preview.jpg" alt="ClipFlyout Desktop Preview" width="840" />

<br />

[日本語マニュアル](#日本語マニュアル) • [English Manual](#english-user-manual) • [開発者ガイド](#開発者ガイド) • [ライセンス](#ライセンス)

</div>

---

## 日本語マニュアル

### 1. 概要

ClipFlyout は、クリップボードにコピーしたテキストや画像の形式をローカル環境で自動解析し、最適な変換・整形・保存アクションを画面端のすりガラス（アクリル）ウィンドウで提示する Windows 向け常駐ユーティリティです。

作業中のアプリケーションからウィンドウフォーカスを奪わないため、キーボード操作や入力を中断することなく、1クリックで必要な処理を実行できます。また、タスクバー上に最新のコピー内容を表示するタスクバーウィジェットも備えています。

---

### 2. 主な機能

- **Windows 11 DWM アクリル効果**  
  Windows 11 の Desktop Window Manager (DWM) が提供するネイティブアクリル（すりガラス効果）を採用。システムのダーク / ライトテーマに自動追従します。
- **タスクバー直結ウィジェット**  
  最新のコピー内容をタスクバー上にシームレスに表示します。タスクバーの自動非表示アニメーションに完全同期し、全画面アプリケーション起動時は自動で非表示になります。タスクバーの背景色をリアルタイム検出し、文字色を明色・暗色へ自動調整します。
- **即時アクション & ペースト連携**  
  カラーコード、タイムスタンプ、JSON、URL、CSV/TSV、Base64 などを自動判別。変換アクション実行後、次のフライアウトで即座にペーストボタンが提示されます。
- **プライバシー保護・ローカル処理**  
  外部サーバーへのデータ送信は一切行いません。1Password、Bitwarden、KeePass などの主要パスワード管理ツールからのコピーは自動検知して除外します。
- **初回セットアップウィザード**  
  初回起動時に設定案内ウィンドウが表示され、既定設定の適用または言語・基本動作の初期選択が可能です。
- **PowerToys スタイルの設定画面**  
  Mica デザインに準拠した設定画面で、外観、表示位置、アクリル透明度、検出フィルター、アップデート確認を集中管理できます。

---

### 3. 対応データ型とアクション一覧

| データ型 | 検出条件 | 提供される主なアクション |
| :--- | :--- | :--- |
| **HEX カラー** | `#RGB`, `#RRGGBB`, `#AARRGGBB` | カラープレビュー、RGB / HSL / RGBA 変換コピー |
| **Unix タイムスタンプ** | 10桁（秒）または 13桁（ミリ秒）の数値 | ローカル日時文字列変換、ISO 8601 変換、現在Epochコピー |
| **JSON テキスト** | JSON オブジェクト `{...}` または配列 `[...]` | インデント整形コピー、1行化（Minify）コピー |
| **Web URL** | `http://` または `https://` リンク | 既定ブラウザで開く、QRコード画像生成コピー、ホストドメインコピー |
| **クリーン URL** | UTM トラッカーや追跡パラメータを含むリンク | 追跡パラメータを除去したクリーン URL のコピー |
| **メールアドレス** | メールアドレス形式の文字列 | 既定メールアプリ起動、ドメインコピー、ユーザー名コピー |
| **Base64 / Data URI** | Base64 文字列、Data URI | プレーンテキスト復号、画像データ展開・クリップボード転送 |
| **表データ (CSV / TSV)** | カンマまたはタブ区切りの表形式テキスト | Markdown テーブル形式への変換、JSON 配列への変換 |
| **コードスニペット** | プログラミング言語構文・インデント構造 | インデント正規化（スペース2文字）、HTML 特殊文字エスケープ |
| **識別子ケース変換** | camelCase, snake_case, PascalCase, kebab-case | キャメルケース・スネークケース・ケバブケース相互変換 |
| **JWT トークン** | 署名付き JWT 文字列 | クレームペイロード（JSON）のデコード展開 |
| **クリップボード画像** | クリップボード内のビットマップ画像 | 解像度・アスペクト比の確認、PNG ファイルとして保存 |
| **プレーンテキスト** | 上記に該当しない通常のテキスト | 前後空白トリム、文字数 / 単語数 / 行数カウント、大文字 / 小文字変換 |

---

### 4. インストール方法

#### インストーラー版（推奨）
1. [GitHub Releases](https://github.com/featlis/ClipFlyout/releases/latest) から `ClipFlyout-Setup-v1.1.0.exe` をダウンロードします。
2. インストーラーを実行します（標準ユーザー権限でインストール可能です）。
3. インストール完了後、初回セットアップ画面が表示されます。

#### ポータブル版（ZIP）
1. `ClipFlyout-v1.1.0-win-x64.zip` をダウンロードし、任意のフォルダーに解凍します。
2. フォルダー内の `ClipFlyout.exe` を起動します（設定はローカル AppData に保存されます）。

---

### 5. 基本操作

1. **アプリの常駐**  
   起動後、システムトレイ（通知領域）に ClipFlyout のアイコンが表示されます。
2. **コピー操作**  
   任意のアプリケーションでテキストや画像をコピー（`Ctrl + C`）します。
3. **フライアウトからのアクション実行**  
   画面端に表示されるフライアウトのボタンをクリックすると、変換や整形が実行されます。
4. **リコールショートカット**  
   フライアウトが非表示になった後でも、**`Alt + Shift + C`** を押すと直前のフライアウトを再表示できます。
5. **タスクバーウィジェットの利用**  
   タスクバー上のウィジェットをクリックすると直前のフライアウトが表示されます。右クリックすると位置切り替えや非表示メニューが開きます。

---

### 6. 設定項目

タスクトレイアイコンを右クリックし、「設定」を選択すると設定画面が開きます。

- **全般**:
  - クリップボード監視の有効 / 無効
  - タスクバーウィジェットの表示 / 非表示、表示位置（トレイ左、中央右、中央左、左端、タスクバー直上）、左右位置微調整スライダー（-300px 〜 +300px）、対象モニター選択
  - パスワードマネージャー除外の有効 / 無効
  - リコールショートカット（`Alt + Shift + C`）の有効 / 無効
  - Windows ログオン時の自動起動
  - テーマ切り替え（システム準拠 / ライト / ダーク）
  - 表示言語（自動 / 日本語 / English）
- **フライアウト外観 & 動作**:
  - 表示位置（右下 / 右上 / 左上 / 左下 / カーソル付近）
  - アクリル透明度（20% 〜 100%）
  - アクセントカラー選択（プリセットおよび RGB カスタム指定）
  - 表示継続時間（1.5秒 〜 10秒）
  - カーソル離脱後の待機時間（0.5秒 〜 5秒）
- **検出フィルター**:
  - 13種類のデータ型検出ごとに個別の有効 / 無効を設定可能
- **アップデート**:
  - 設定画面内で GitHub Releases からの最新バージョン確認および更新インストールが可能

---

### 7. トラブルシューティング

- **フライアウトが表示されない**:  
  タスクトレイメニューで「クリップボード連携」が有効になっているか確認してください。また、1Password や Bitwarden 等のパスワードマネージャーからのコピーは安全のため自動的に除外されます。
- **タスクバーウィジェットの位置を調整したい**:  
  ウィジェットを右クリックするか、設定画面の「タスクバーウィジェット」項目から配置プリセットおよび「左右微調整」スライダーを使用してください。
- **すりガラス効果（アクリル）が適用されない**:  
  Windows の「設定」>「アクセシビリティ」または「個人用設定」>「色」で「透明効果」が有効になっていることを確認してください。省電力モード時やリモートデスクトップ接続時は Windows 側の制約によりアクリル効果が無効化される場合があります。

---

## English User Manual

### 1. Overview

ClipFlyout is a resident Windows utility that monitors clipboard changes locally, analyzes data formats in real time, and presents contextual formatting, conversion, and quick actions via an acrylic flyout.

Because the flyout uses the non-activating window style, your active window focus remains uninterrupted while typing or browsing. ClipFlyout also features a lightweight taskbar widget displaying the latest copied item directly on your taskbar.

---

### 2. Key Features

- **Native Windows 11 Acrylic Backdrop**: Rendered through the Desktop Window Manager (DWM) with full dark and light theme tracking.
- **Taskbar Widget**: Minimalist widget docked near your taskbar. Smoothly tracks taskbar auto-hide animation, suppresses itself during fullscreen applications, and automatically adjusts text contrast based on taskbar luminance.
- **One-Click Actions & Auto-Paste**: Execute conversions instantly. Transform operations immediately present a "Paste" action on the subsequent flyout.
- **Local & Privacy-First**: 100% local execution with no telemetry or network transmission. Automatically ignores copies originating from password managers (1Password, Bitwarden, KeePass).
- **Initial Setup Wizard**: First-run onboarding window for selecting language and default configuration with a single click.
- **Fluent Settings Interface**: Mica-backed settings window providing centralized control over appearance, flyout behavior, detection filters, and in-app updates.

---

### 3. Supported Data Types & Actions

| Data Type | Detection Criteria | Available Actions |
| :--- | :--- | :--- |
| **HEX Color** | `#RGB`, `#RRGGBB`, `#AARRGGBB` | Color swatch preview, Copy RGB / HSL / RGBA values |
| **Unix Timestamp** | 10-digit (s) or 13-digit (ms) numbers | Convert to local date string, ISO 8601 UTC, Copy current epoch |
| **JSON Text** | JSON object `{...}` or array `[...]` | Format with indentation, Minify to single line |
| **Web URL** | `http://` or `https://` links | Open in browser, Generate & copy QR code, Copy domain |
| **Clean URL** | URLs containing tracking parameters | Strip tracking parameters (UTM, fbclid, etc.) and copy clean URL |
| **Email Address** | Standard email format | Open default mail client, Copy domain, Copy username |
| **Base64 / Data URI** | Base64 strings or Data URIs | Decode to plain text, Unpack image to clipboard |
| **Table Data (CSV / TSV)** | Delimited tabular text | Convert to Markdown table, Convert to JSON array |
| **Code Snippet** | Programming language syntax and structure | Normalize indentation (2 spaces), Escape HTML entities |
| **Identifier Case** | camelCase, snake_case, PascalCase, kebab-case | Convert between common naming conventions |
| **JWT Token** | Signed JWT string | Decode and inspect claims payload (JSON) |
| **Clipboard Image** | Bitmap image on clipboard | Inspect resolution and aspect ratio, Save as PNG file |
| **Plain Text** | General text strings | Trim whitespace, Character/word/line count, UPPERCASE / lowercase |

---

### 4. Installation & Usage

1. Download `ClipFlyout-Setup-v1.1.0.exe` or portable `ClipFlyout-v1.1.0-win-x64.zip` from [GitHub Releases](https://github.com/featlis/ClipFlyout/releases/latest).
2. Run the installer or extract the portable ZIP.
3. Copy any supported content (`Ctrl + C`).
4. The flyout appears near the designated screen corner without stealing keyboard focus.
5. Press **`Alt + Shift + C`** at any time to recall the most recent flyout.

---

## 開発者ガイド

### 開発要件

- Windows 11 / 10 (x64)
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Inno Setup 6](https://jrsoftware.org/isdl.php)（インストーラーパッケージ生成時のみ）

### ビルドと単体テスト

```powershell
# ソリューションのビルド
dotnet build ClipFlyout.csproj -c Release

# 単体テストの実行 (100件のテストスイート)
dotnet test tests/ClipFlyout.Tests/ClipFlyout.Tests.csproj -c Release
```

### 配布パッケージの生成

```powershell
# バージョンを指定してインストーラーとポータブルZIPを生成
powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1 -Version 1.1.0
```

実行後、`dist/` ディレクトリに以下のファイルが出力されます：
- `dist/ClipFlyout-Setup-v1.1.0.exe`（Inno Setup インストーラー）
- `dist/ClipFlyout-v1.1.0-win-x64.zip`（自己完結型ポータブルパッケージ）
- `dist/checksums-sha256.txt`（SHA-256 チェックサム一覧）

### アーキテクチャ概要

- **`Services/ClipboardMonitor.cs`**: `AddClipboardFormatListener` (Win32 API) によるイベント駆動型クリップボード監視。自己書き込みループを防止するシーケンス番号検証と、他プロセスとの競合を処理する非同期リトライ機構を実装。
- **`Services/DataTypeDetector.cs`**: 優先度順の正規表現および構文解析エンジン。1MB 以上の巨大データに対する保護リミットを内包。
- **`Services/FlyoutWindowManager.cs`**: `WS_EX_NOACTIVATE` 属性による非アクティブウィンドウ制御、Per-Monitor DPI 対応の正確な画面端吸着配置。
- **`Views/TaskbarWidgetWindow.xaml.cs`**: タスクバー上にオーバーレイする常時表示ウィジェット。タスクバー自動非表示への追従（30ms ポーリング）、全画面アプリ時の自動抑止、クリック判定（Alpha=1 ブラシ）を実装。
- **`Services/TaskbarColorDetector.cs`**: タスクバー領域の背景ピクセルをサンプリングし、輝度に応じた文字色（明色・暗色）の自動判別を実施。
- **`Views/SettingsWindow.xaml`**: Mica デザインに準拠した設定画面。外観、配置、検出フィルター、アップデート確認、ライセンス表示をタブ分割で提供。
- **`Controls/ToggleSwitch.xaml.cs`**: WinUI 3 準拠のストレッチアニメーション付きトグルスイッチ。

---

## ライセンス

- **プロジェクト**: ClipFlyout
- **開発・著作**: [featlis](https://github.com/featlis) and contributors
- **ライセンス**: [GNU General Public License version 3 (GPL-3.0-or-later)](LICENSE)

本ソフトウェアは自由ソフトウェアです。フリーソフトウェア財団が公表した GNU General Public License version 3 (GPL-3.0-or-later) の条項に基づき、無償で自由に使用、研究、改変、再配布することができます。
