using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ClipFlyout.Native;

public static class Win32
{
    public const int WM_CLIPBOARDUPDATE = 0x031D;
    public const int WM_HOTKEY = 0x0312;
    public const int GWL_EXSTYLE = -20;
    public const int GWL_HWNDPARENT = -8;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_TOPMOST = 0x00000008;

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);
    public static readonly IntPtr HWND_TOP = new(0);
    public static readonly IntPtr HWND_BOTTOM = new(1);

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_NOREDRAW = 0x0008;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const int WM_WINDOWPOSCHANGING = 0x0046;
    public const int WM_SETTINGCHANGE = 0x001A;
    public const int WM_SETICON = 0x0080;
    public const int WM_GETICON = 0x007F;
    public const int ICON_SMALL = 0;
    public const int ICON_BIG = 1;
    public const int GCLP_HICON = -14;
    public const int GCLP_HICONSM = -34;

    // Low-level hooks (active only while FlyoutWindow is open)
    public const int WH_KEYBOARD_LL = 13;
    public const int WH_MOUSE_LL = 14;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_MBUTTONDOWN = 0x0207;

    public delegate IntPtr LowLevelHookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelHookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    public const uint GW_HWNDNEXT = 2;
    public const uint GW_HWNDPREV = 3;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    public const ushort VK_SHIFT = 0x10;
    public const ushort VK_CONTROL = 0x11;
    public const ushort VK_MENU = 0x12; // Alt
    public const ushort VK_ESCAPE = 0x1B;
    public const ushort VK_RETURN = 0x0D;
    public const ushort VK_UP = 0x26;
    public const ushort VK_DOWN = 0x28;
    public const ushort VK_V = 0x56;
    public const ushort VK_LWIN = 0x5B;
    public const ushort VK_RWIN = 0x5C;

    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    public const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;
    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    public const int MDT_EFFECTIVE_DPI = 0;

    // DWM Attributes
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    public const int DWMWCP_ROUND = 2;
    public const int DWMSBT_AUTO = 0;
    public const int DWMSBT_NONE = 1;
    public const int DWMSBT_MAINWINDOW = 2;      // Mica
    public const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic
    public const int DWMSBT_TABBEDWINDOW = 4;    // Mica Alt

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    public static extern IntPtr GetClipboardOwner();

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern uint MapVirtualKey(uint uCode, uint uMapType);

    public enum QUERY_USER_NOTIFICATION_STATE
    {
        QUNS_NOT_PRESENT = 1,
        QUNS_BUSY = 2,
        QUNS_RUNNING_D3D_FULL_SCREEN = 3,
        QUNS_PRESENTATION_MODE = 4,
        QUNS_ACCEPTS_NOTIFICATIONS = 5,
        QUNS_QUIET_TIME = 6,
        QUNS_APP = 7
    }

    [DllImport("shell32.dll")]
    public static extern int SHQueryUserNotificationState(out QUERY_USER_NOTIFICATION_STATE pquns);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;
        [FieldOffset(0)]
        public KEYBDINPUT ki;
        [FieldOffset(0)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    /// <summary>
    /// Releases interfering held modifiers and sends a clean Ctrl+V chord with hardware scan codes.
    /// Returns true if all input events were accepted by Windows.
    /// </summary>
    public static bool PasteToActiveWindow()
    {
        var list = new List<INPUT>(8);

        // Release held Alt / Shift / Win if user just triggered via shortcut
        ushort[] interferingModifiers = [VK_MENU, VK_SHIFT, VK_LWIN, VK_RWIN];
        foreach (var mod in interferingModifiers)
        {
            if ((GetAsyncKeyState(mod) & 0x8000) != 0)
            {
                list.Add(MakeKeyInput(mod, keyUp: true));
            }
        }

        list.Add(MakeKeyInput(VK_CONTROL, keyUp: false));
        list.Add(MakeKeyInput(VK_V, keyUp: false));
        list.Add(MakeKeyInput(VK_V, keyUp: true));
        list.Add(MakeKeyInput(VK_CONTROL, keyUp: true));

        var inputs = list.ToArray();
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        return sent == inputs.Length;
    }

    private static INPUT MakeKeyInput(ushort vk, bool keyUp)
    {
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = (ushort)MapVirtualKey(vk, 0),
                    dwFlags = keyUp ? KEYEVENTF_KEYUP : 0
                }
            }
        };
    }

    /// <summary>
    /// Detects whether a fullscreen game, presentation, or borderless fullscreen video is currently active.
    /// </summary>
    public static bool IsFullscreenAppActive(IntPtr excludeHwnd = default)
    {
        try
        {
            if (SHQueryUserNotificationState(out var state) == 0)
            {
                if (state == QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN ||
                    state == QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE)
                {
                    return true;
                }
            }

            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == excludeHwnd || fg == GetDesktopWindow() || fg == GetShellWindow())
            {
                return false;
            }

            var sb = new StringBuilder(128);
            if (GetClassName(fg, sb, sb.Capacity) > 0)
            {
                string cls = sb.ToString();
                if (cls.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||
                    cls.Equals("WorkerW", StringComparison.OrdinalIgnoreCase) ||
                    cls.Equals("Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
                    cls.Equals("Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (GetWindowRect(fg, out var fgRect))
            {
                IntPtr hMon = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(hMon, ref mi))
                {
                    var m = mi.rcMonitor;
                    bool coversMonitor = fgRect.Left <= m.Left &&
                                         fgRect.Top <= m.Top &&
                                         fgRect.Right >= m.Right &&
                                         fgRect.Bottom >= m.Bottom;
                    if (coversMonitor)
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
        }

        return false;
    }

    /// <summary>
    /// Detects whether the Windows Start Menu, Search, Quick Settings, or Shell Flyout is currently foreground.
    /// </summary>
    public static bool IsShellOverlayActive()
    {
        try
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;

            var sb = new StringBuilder(128);
            if (GetClassName(fg, sb, sb.Capacity) > 0)
            {
                string cls = sb.ToString();
                if (cls.Equals("Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase) ||
                    cls.Equals("XamlExplorerHostIslandWindow", StringComparison.OrdinalIgnoreCase) ||
                    cls.Equals("TopLevelWindowForOverflowXamlIsland", StringComparison.OrdinalIgnoreCase) ||
                    cls.Equals("NotifyIconOverflowWindow", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : SetWindowLong32(hWnd, nIndex, dwNewLong);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    public static List<IntPtr> GetAllMonitors()
    {
        var monitorsWithRect = new List<(IntPtr hMon, int left, int top)>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data) =>
        {
            monitorsWithRect.Add((hMon, rc.Left, rc.Top));
            return true;
        }, IntPtr.Zero);

        // Sort deterministically left-to-right, then top-to-bottom for vertically stacked setups
        monitorsWithRect.Sort((a, b) =>
        {
            int cmp = a.left.CompareTo(b.left);
            return cmp != 0 ? cmp : a.top.CompareTo(b.top);
        });

        var list = new List<IntPtr>(monitorsWithRect.Count);
        foreach (var item in monitorsWithRect)
        {
            list.Add(item.hMon);
        }
        return list;
    }

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [DllImport("dwmapi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);

    public enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
        ACCENT_INVALID_STATE = 5
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    public enum WindowCompositionAttribute
    {
        WCA_ACCENT_POLICY = 19
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    public static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    public const uint WM_NULL = 0x0000;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Retrieves the effective DPI scaling factor, prioritizing the target monitor's DPI
    /// for accurate Per-Monitor V2 multi-monitor positioning.
    /// </summary>
    public static double GetMonitorDpiScale(IntPtr hMonitor, IntPtr hwnd)
    {
        try
        {
            if (hMonitor != IntPtr.Zero && GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
            {
                return dpiX / 96.0;
            }

            if (hwnd != IntPtr.Zero)
            {
                uint dpi = GetDpiForWindow(hwnd);
                if (dpi > 0) return dpi / 96.0;
            }
        }
        catch
        {
        }

        return 1.0;
    }

    /// <summary>
    /// Enables Windows 11 native transient acrylic backdrop (DWMSBT_TRANSIENTWINDOW)
    /// without invoking conflicting Win10 Accent Policy on Win11 22H2+.
    /// Falls back cleanly to Accent Policy only on older Windows builds.
    /// </summary>
    public static void EnableAcrylicBlur(IntPtr hwnd, bool isDark, double opacityPercent = 85.0, bool enableTransparency = true)
    {
        try
        {
            int darkModeVal = isDark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkModeVal, sizeof(int));

            int cornerVal = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerVal, sizeof(int));

            var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);

            if (!enableTransparency)
            {
                int noneBackdrop = DWMSBT_NONE;
                DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref noneBackdrop, sizeof(int));
                SetAccentPolicy(hwnd, new AccentPolicy { AccentState = AccentState.ACCENT_DISABLED });
                return;
            }

            // Windows 11 22H2+ native Desktop Acrylic backdrop
            int backdropVal = DWMSBT_TRANSIENTWINDOW;
            int dwmRes = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropVal, sizeof(int));

            if (dwmRes == 0)
            {
                // Disable legacy accent policy so it never fights Win11 DWM composition
                SetAccentPolicy(hwnd, new AccentPolicy { AccentState = AccentState.ACCENT_DISABLED });
                return;
            }

            // Windows 10 / 11 21H2 fallback
            byte r = isDark ? (byte)32 : (byte)249;
            byte g = isDark ? (byte)32 : (byte)249;
            byte b = isDark ? (byte)32 : (byte)249;
            byte alpha = (byte)Math.Clamp((int)Math.Round((opacityPercent - 20.0) * (180.0 / 80.0) + 30.0), 30, 220);
            uint abgrColor = ((uint)alpha << 24) | ((uint)b << 16) | ((uint)g << 8) | r;

            SetAccentPolicy(hwnd, new AccentPolicy
            {
                AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                AccentFlags = 0,
                GradientColor = abgrColor,
                AnimationId = 0
            });
        }
        catch
        {
        }
    }

    private static void SetAccentPolicy(IntPtr hwnd, AccentPolicy policy)
    {
        int size = Marshal.SizeOf(policy);
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, buffer, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                Data = buffer,
                SizeOfData = size
            };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Enables genuine Windows 11 Mica backdrop by extending DWM frame into client area.
    /// Requires Window.Background = Brushes.Transparent (or translucent brush) to reveal Mica.
    /// </summary>
    public static void EnableMica(IntPtr hwnd, bool isDark, bool useMicaAlt = false)
    {
        try
        {
            int darkModeVal = isDark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkModeVal, sizeof(int));

            int cornerVal = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerVal, sizeof(int));

            var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);

            int micaVal = useMicaAlt ? DWMSBT_TABBEDWINDOW : DWMSBT_MAINWINDOW;
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref micaVal, sizeof(int));
        }
        catch
        {
        }
    }

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern uint GetPixel(IntPtr hdc, int nXPos, int nYPos);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    public static IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        return IntPtr.Size == 8
            ? SetClassLongPtr64(hWnd, nIndex, dwNewLong)
            : new IntPtr(SetClassLong32(hWnd, nIndex, dwNewLong.ToInt32()));
    }

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtr")]
    private static extern IntPtr SetClassLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetClassLong")]
    private static extern int SetClassLong32(IntPtr hWnd, int nIndex, int dwNewLong);
}
