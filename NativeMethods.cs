using System;
using System.Runtime.InteropServices;

namespace AutoClicker.Native
{
    /// <summary>
    /// Central location for all P/Invoke signatures, native structures and Win32
    /// constants used by the application. Keeping them together makes the rest of
    /// the codebase free of <c>DllImport</c> noise and easier to audit.
    /// </summary>
    internal static class NativeMethods
    {
        // ─────────────────────────────────────────────────────────────────────
        //  Cursor / position
        // ─────────────────────────────────────────────────────────────────────

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT lpPoint);

        // ─────────────────────────────────────────────────────────────────────
        //  Input synthesis (modern SendInput API)
        // ─────────────────────────────────────────────────────────────────────

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        // Legacy fallback used when SendInput is unavailable for any reason.
        [DllImport("user32.dll", SetLastError = true)]
        public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, IntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        public static extern IntPtr GetMessageExtraInfo();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetSystemMetrics(int nIndex);

        // ─────────────────────────────────────────────────────────────────────
        //  Keyboard state
        // ─────────────────────────────────────────────────────────────────────

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int nVirtKey);

        // ─────────────────────────────────────────────────────────────────────
        //  Global hotkeys
        // ─────────────────────────────────────────────────────────────────────

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // ─────────────────────────────────────────────────────────────────────
        //  Low level hooks (used for macro recording)
        // ─────────────────────────────────────────────────────────────────────

        public delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        // ─────────────────────────────────────────────────────────────────────
        //  Multimedia timer resolution (winmm)
        // ─────────────────────────────────────────────────────────────────────
        //
        //  By default the Windows scheduler quantum (and therefore the resolution
        //  of Sleep / wait-handle timeouts) is around 15.6 ms. Requesting a 1 ms
        //  period dramatically tightens the accuracy of timed waits, which is
        //  essential for an auto-clicker that must honour short intervals.

        [DllImport("winmm.dll", SetLastError = true)]
        public static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll", SetLastError = true)]
        public static extern uint timeEndPeriod(uint uPeriod);

        // TIMERR_NOERROR is returned by the two calls above on success.
        public const uint TIMERR_NOERROR = 0;

        // ─────────────────────────────────────────────────────────────────────
        //  Structures
        // ─────────────────────────────────────────────────────────────────────

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;

            public POINT(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
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
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
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
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Constants
        // ─────────────────────────────────────────────────────────────────────

        // INPUT.type values
        public const uint INPUT_MOUSE = 0;
        public const uint INPUT_KEYBOARD = 1;

        // MOUSEINPUT.dwFlags values
        public const uint MOUSEEVENTF_MOVE = 0x0001;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;
        public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        public const uint MOUSEEVENTF_XDOWN = 0x0080;
        public const uint MOUSEEVENTF_XUP = 0x0100;
        public const uint MOUSEEVENTF_WHEEL = 0x0800;
        public const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
        public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

        // XBUTTON identifiers for mouseData
        public const uint XBUTTON1 = 0x0001;
        public const uint XBUTTON2 = 0x0002;

        // KEYBDINPUT.dwFlags values
        public const uint KEYEVENTF_KEYUP = 0x0002;

        // Hotkey modifier flags
        public const uint MOD_NONE = 0x0000;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        // Window message constants
        public const int WM_HOTKEY = 0x0312;

        // Hook identifiers
        public const int WH_KEYBOARD_LL = 13;
        public const int WH_MOUSE_LL = 14;

        // Low level mouse messages
        public const int WM_MOUSEMOVE = 0x0200;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_LBUTTONUP = 0x0202;
        public const int WM_RBUTTONDOWN = 0x0204;
        public const int WM_RBUTTONUP = 0x0205;
        public const int WM_MBUTTONDOWN = 0x0207;
        public const int WM_MBUTTONUP = 0x0208;
        public const int WM_XBUTTONDOWN = 0x020B;
        public const int WM_XBUTTONUP = 0x020C;

        // Low-level mouse hook flag: event was injected by SendInput / mouse_event.
        public const uint LLMHF_INJECTED = 0x00000001;
        public const int WM_MOUSEWHEEL = 0x020A;

        // Low level keyboard messages
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;

        // GetSystemMetrics indices
        public const int SM_CXSCREEN = 0;
        public const int SM_CYSCREEN = 1;
        public const int SM_CXVIRTUALSCREEN = 78;
        public const int SM_CYVIRTUALSCREEN = 79;

        // High bit mask for key state queries
        public const int KEY_PRESSED_MASK = 0x8000;
    }
}
