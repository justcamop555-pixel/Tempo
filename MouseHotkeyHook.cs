using System;
using System.Runtime.InteropServices;
using AutoClicker.Models;
using AutoClicker.Native;
using AutoClicker.Utils;

namespace AutoClicker.Native
{
    /// <summary>
    /// Event raised when a mouse button is pressed while the hotkey hook is
    /// active. Set <see cref="Suppress"/> to true to stop the click reaching other
    /// applications (used so a bound side button doesn't also trigger browser
    /// back/forward, etc.).
    /// </summary>
    public sealed class MouseHotkeyEventArgs : EventArgs
    {
        public HotkeyMouseButton Button { get; }
        public bool Injected { get; }
        public bool Suppress { get; set; }

        public MouseHotkeyEventArgs(HotkeyMouseButton button, bool injected)
        {
            Button = button;
            Injected = injected;
        }
    }

    /// <summary>
    /// A low-level mouse hook (WH_MOUSE_LL) used purely for hotkey detection. It
    /// only reports button-down transitions, ignores injected events by default
    /// reporting (so the auto-clicker's own clicks can be filtered by listeners),
    /// and lets the listener suppress the event by returning 1 from the hook.
    ///
    /// This is separate from <see cref="LowLevelMouseHook"/> (used by the macro
    /// recorder) because the hotkey use-case needs the injected flag and the
    /// ability to swallow the event.
    /// </summary>
    public sealed class MouseHotkeyHook : IDisposable
    {
        private readonly NativeMethods.LowLevelProc _proc;
        private IntPtr _hookHandle = IntPtr.Zero;
        private bool _disposed;

        public event EventHandler<MouseHotkeyEventArgs> ButtonDown;

        public bool IsRunning => _hookHandle != IntPtr.Zero;

        public MouseHotkeyHook()
        {
            _proc = HookCallback;
        }

        public bool Start()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MouseHotkeyHook));
            }

            if (IsRunning)
            {
                return true;
            }

            IntPtr moduleHandle = NativeMethods.GetModuleHandle(null);
            _hookHandle = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_MOUSE_LL, _proc, moduleHandle, 0);

            if (_hookHandle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                Logger.Error($"Failed to install mouse-hotkey hook. Win32 error {error}.");
                return false;
            }

            Logger.Info("Mouse-hotkey hook installed.");
            return true;
        }

        public void Stop()
        {
            if (_hookHandle != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
                Logger.Info("Mouse-hotkey hook removed.");
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    int message = wParam.ToInt32();
                    HotkeyMouseButton button = HotkeyMouseButton.None;

                    var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);

                    switch (message)
                    {
                        case NativeMethods.WM_LBUTTONDOWN:
                            button = HotkeyMouseButton.Left;
                            break;
                        case NativeMethods.WM_RBUTTONDOWN:
                            button = HotkeyMouseButton.Right;
                            break;
                        case NativeMethods.WM_MBUTTONDOWN:
                            button = HotkeyMouseButton.Middle;
                            break;
                        case NativeMethods.WM_XBUTTONDOWN:
                            // High word of mouseData says which X button.
                            uint which = (data.mouseData >> 16) & 0xFFFF;
                            button = which == NativeMethods.XBUTTON2
                                ? HotkeyMouseButton.XButton2
                                : HotkeyMouseButton.XButton1;
                            break;
                    }

                    if (button != HotkeyMouseButton.None && ButtonDown != null)
                    {
                        bool injected = (data.flags & NativeMethods.LLMHF_INJECTED) != 0;
                        var args = new MouseHotkeyEventArgs(button, injected);
                        ButtonDown.Invoke(this, args);

                        if (args.Suppress)
                        {
                            // Returning a non-zero value swallows the event so it
                            // never reaches the focused application.
                            return (IntPtr)1;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Exception inside mouse-hotkey hook callback.", ex);
                }
            }

            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Stop();
            _disposed = true;
        }
    }
}
