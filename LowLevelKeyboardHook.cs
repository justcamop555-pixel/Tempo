using System;
using System.Runtime.InteropServices;
using AutoClicker.Native;
using AutoClicker.Utils;

namespace AutoClicker.Native
{
    /// <summary>
    /// Data for a low-level keyboard event.
    /// </summary>
    public sealed class KeyboardHookEventArgs : EventArgs
    {
        public int VirtualKey { get; }
        public bool IsKeyDown { get; }

        public KeyboardHookEventArgs(int virtualKey, bool isKeyDown)
        {
            VirtualKey = virtualKey;
            IsKeyDown = isKeyDown;
        }
    }

    /// <summary>
    /// Installs a system wide low-level keyboard hook (WH_KEYBOARD_LL). Used to
    /// detect a cancel/stop key while recording a macro, independent of focus.
    /// </summary>
    public sealed class LowLevelKeyboardHook : IDisposable
    {
        private readonly NativeMethods.LowLevelProc _proc;
        private IntPtr _hookHandle = IntPtr.Zero;
        private bool _disposed;

        public event EventHandler<KeyboardHookEventArgs> KeyEvent;

        public bool IsRunning => _hookHandle != IntPtr.Zero;

        public LowLevelKeyboardHook()
        {
            _proc = HookCallback;
        }

        public bool Start()
        {
            ThrowIfDisposed();

            if (IsRunning)
            {
                return true;
            }

            IntPtr moduleHandle = NativeMethods.GetModuleHandle(null);
            _hookHandle = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_KEYBOARD_LL,
                _proc,
                moduleHandle,
                0);

            if (_hookHandle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                Logger.Error($"Failed to install keyboard hook. Win32 error {error}.");
                return false;
            }

            Logger.Info("Low-level keyboard hook installed.");
            return true;
        }

        public void Stop()
        {
            if (_hookHandle != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
                Logger.Info("Low-level keyboard hook removed.");
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                    int message = wParam.ToInt32();

                    bool isDown = message == NativeMethods.WM_KEYDOWN || message == NativeMethods.WM_SYSKEYDOWN;
                    bool isUp = message == NativeMethods.WM_KEYUP || message == NativeMethods.WM_SYSKEYUP;

                    if (isDown || isUp)
                    {
                        KeyEvent?.Invoke(this, new KeyboardHookEventArgs((int)data.vkCode, isDown));
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Exception inside keyboard hook callback.", ex);
                }
            }

            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LowLevelKeyboardHook));
            }
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
