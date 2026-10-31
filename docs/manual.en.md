# ClipFlyout Manual

Details that don't fit in the README live here.

## Installation

### Installer (recommended)

1. Download `ClipFlyout-Setup-*.exe` from [GitHub Releases](https://github.com/featlis/ClipFlyout/releases/latest)
2. Run the installer (standard user permissions are enough)
3. On first launch, a setup window lets you apply the defaults or pick a language and basic behavior

### Portable (ZIP)

1. Download `ClipFlyout-*-win-x64.zip` and extract it anywhere
2. Run `ClipFlyout.exe` (settings are stored in your local AppData folder)

## Basic usage

1. After launch, the ClipFlyout icon sits in the system tray
2. Copy text or an image in any app (`Ctrl + C`)
3. Click an action in the flyout at the edge of the screen. After a conversion, the next flyout offers a Paste action
4. Press `Alt + Shift + C` to bring back the last flyout after it hides
5. Clicking the taskbar widget also opens the last flyout; right-click it for position and visibility options

## Supported data types & actions

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

## Settings

Right-click the tray icon and choose Settings.

- **General**: clipboard monitoring on/off, taskbar widget visibility and position (including a -300px to +300px fine-adjust slider and target monitor), password manager exclusion on/off, recall shortcut (`Alt + Shift + C`) on/off, start at logon, theme (system / light / dark), language (auto / Japanese / English)
- **Flyout appearance & behavior**: position (bottom-right / top-right / top-left / bottom-left / near cursor), acrylic opacity (20%–100%), accent color (presets or custom RGB), display duration (1.5s–10s), delay after the cursor leaves (0.5s–5s)
- **Detection filters**: each of the 13 data types can be enabled or disabled individually
- **Updates**: check GitHub Releases for the latest version and install it from the settings window

## Privacy details

- Analysis and conversion happen entirely on your PC; clipboard contents are never sent to a server
- Password manager exclusion is based on the process name of the window the copy came from: 1Password, Bitwarden, KeePass, KeePassXC, LastPass, Dashlane, Enpass, NordPass
- Copies that carry a "keep out of clipboard history" format (such as `Clipboard Viewer Ignore`) are also ignored

## Troubleshooting

### The flyout does not appear

Check that clipboard integration is enabled in the tray menu. Copies from password managers are excluded by design.

### Adjusting the taskbar widget position

Right-click the widget, or use the placement presets and the fine-adjust slider under "Taskbar widget" in Settings.

### The acrylic effect is not applied

Make sure "Transparency effects" is on in Windows Settings (Accessibility, or Personalization > Colors). Windows itself disables acrylic in power-saving mode and during Remote Desktop sessions.
