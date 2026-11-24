# ClipFlyout Manual

Details that do not fit in the README. Written against v2.2.0.

## Installation

### Installer (recommended)

1. Download `ClipFlyout-Setup-v2.2.0.exe` from [GitHub Releases](https://github.com/featlis/ClipFlyout/releases/latest)
2. Run the installer (standard user permissions are enough)
3. On the first-run setup screen, apply the defaults or pick your language and basic behavior

### Portable (ZIP)

1. Download `ClipFlyout-v2.2.0-win-x64.zip` and extract it anywhere
2. Run `ClipFlyout.exe` inside the folder (settings are stored in your local AppData)

Each release also ships a SHA-256 checksum list (`checksums-sha256.txt`) for verifying downloads.

## Basic usage

1. When running, an icon appears in the system tray. Left-click recalls the last flyout; double-click opens Settings
2. Copy text or an image in any app (`Ctrl + C`)
3. Click a button in the flyout at the edge of the screen to run a conversion. After a conversion, the next flyout offers a Paste action. On the keyboard, `Esc` closes the flyout and `↑` / `↓` + `Enter` or the number keys `1`–`9` pick an action
4. After the flyout disappears, the recall shortcut (default `Alt + Shift + C`, changeable in settings) brings the last one back
5. Clicking the taskbar widget also recalls the flyout. Right-click it for position and visibility options. It hides itself while a fullscreen app is in use
6. Inside the flyout you can also edit the copied text in place (quick edit) or join it with the previous clip

### From the tray

- **History**: recent copies, with pinning. Pinned items survive a history clear. Persisting history to disk is off by default and can be turned on in settings
- **Snippets**: save frequently used text and insert it directly from the tray. Manage them in Settings
- **Pause**: stop monitoring for 15 minutes or 1 hour, and resume from the tray
- **Theme and language**: also switchable from the tray

## Supported data types and actions

| Data Type | Detection Criteria | Available Actions |
| :--- | :--- | :--- |
| **Color Code** | `#RGB`, `#RRGGBB`, `rgb()`, `hsl()` | Color swatch preview, Copy HEX / RGB / HSL / RGBA values |
| **Unix Timestamp** | 10-digit (s) or 13-digit (ms) numbers | Convert to local date string, ISO 8601 UTC, Copy current epoch |
| **ISO 8601 Date/Time** | `YYYY-MM-DDTHH:mm:ssZ` strings | Local time, Unix seconds / milliseconds copy |
| **Files & Folders** | Explorer FileDropList items | Copy full / quoted paths, file names, reveal in Explorer |
| **JSON Text** | JSON object `{...}` or array `[...]` | Format with indentation, Minify to single line |
| **Web URL** | `http://` or `https://` links | Open in browser, Generate & copy QR code, Copy domain |
| **Clean URL** | URLs containing tracking parameters | Strip tracking parameters (UTM, fbclid, etc.) and copy clean URL |
| **Email Address** | Standard email format | Open default mail client, Copy domain, Copy username |
| **Base64 / Data URI** | Base64 strings or Data URIs | Decode to plain text, Unpack image to clipboard |
| **Table Data (CSV / TSV)** | Delimited tabular text | Convert to Markdown table, Convert to JSON array |
| **Code Snippet** | Programming language syntax and structure | Normalize indentation (2 spaces), Escape HTML entities |
| **Identifier Case** | camelCase, snake_case, PascalCase, kebab-case | Convert between common naming conventions |
| **JWT Token** | Signed JWT string | Decode and inspect claims payload (JSON) |
| **File Path** | Windows / `/` / UNC / WSL (`/mnt/...`) paths | Convert slashes, Windows ⇔ WSL mapping, escaped path, Open in Terminal |
| **Math / Base Calc** | Arithmetic expressions, `0x...`, `0b...` literals | Copy decimal / `0xHEX` / `0bBIN` (strictly filters dates, phones, versions) |
| **UUID / GUID** | Hyphenated or `{braced}` UUID strings | Copy lowercase / uppercase / compact, generate new UUID v4 or v7 |
| **Clipboard Image** | Bitmap image on clipboard | Inspect resolution and aspect ratio, Save PNG / JPG / BMP, On-device OCR text extraction |
| **Plain Text** | General text strings | Trim, character count, case transforms, SHA-256, Base64 encode, Join with Previous Clip |

## Settings

Right-click the tray icon and choose Settings to open the Mica-backed settings window.

### General

- Enable / disable clipboard monitoring
- Taskbar widget visibility, position (tray left, center right, center left, far left, above taskbar), fine horizontal adjustment, target monitor, and text color (auto / light / dark)
- Password manager exclusion on / off, plus your own per-process exclusion list
- Recall shortcut on / off and key assignment
- Launch on Windows logon
- Theme (system / light / dark)
- Display language (auto / Japanese / English)
- History persistence on / off
- Snippet management

### Flyout appearance and behavior

- Placement (bottom right / top right / top left / bottom left / near cursor)
- Acrylic opacity (20%–100%)
- Accent color (system accent, presets, or custom RGB, with automatic theme-aware adjustment)
- Display duration (1.5–10 seconds)
- Wait after the cursor leaves (0.5–5 seconds)
- Suppress while a fullscreen app is in use

### Detection filters

- Each data-type detector and helper feature (clean URL, case conversion, JWT, OCR, and so on) can be enabled or disabled individually

### Updates

- Check for and install updates from GitHub Releases, from Settings or the tray
- Update files are downloaded only from this project's GitHub Releases and verified against the release SHA-256 manifest before they are started

## Privacy details

- All analysis and conversion happens locally; clipboard contents are never sent to an external server. Image OCR also runs on-device, using the built-in Windows OCR
- History persistence is off by default
- Password manager exclusion is based on the process name of the window the copy came from. Covered: 1Password, Bitwarden, KeePass, KeePassXC, LastPass, Dashlane, Enpass, NordPass, RoboForm, and Proton Pass
- Exclusion markers apps put on the clipboard are honored too (`Clipboard Viewer Ignore`, `CanIncludeInClipboardHistory` set to 0, and `ExcludeClipboardContentFromMonitorProcessing`)

## Troubleshooting

### The flyout does not appear

Check in the tray menu that clipboard monitoring is enabled and not paused. Copies from password managers are excluded by design, and display is suppressed during fullscreen apps if that setting is on.

### Adjusting the taskbar widget position

Right-click the widget, or use the position presets and the fine-adjustment control under "Taskbar widget" in Settings.

### The acrylic effect is not applied

Make sure "Transparency effects" is enabled in Windows Settings, under Accessibility or Personalization > Colors. Windows itself may disable acrylic in power-saving mode or over Remote Desktop.
