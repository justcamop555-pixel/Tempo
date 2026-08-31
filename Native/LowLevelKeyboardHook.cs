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

        /// <summary>The OS event time (GetTickCount ms) captured when the key actually
        /// changed state — accurate even if this callback is dispatched late.</summary>
        public uint TimeStamp { get; }

        /// <summary>True if the event was synthesised (SendInput/keybd_event) rather than
        /// pressed by the user — the recorder ignores these so it never captures Tempo's
        /// own playback/clicking as part of a new recording.</summary>
        public bool Injected { get; }

        /// <summary>
        /// Set this in a handler to SWALLOW the key: it never reaches the focused app.
        /// Used by camera-relative movement, which must consume the physical W/A/S/D
        /// and inject its own re-mixed keys instead — if the original key also got
        /// through, the game would receive both and move in the wrong direction.
        ///
        /// Default false, so every existing listener behaves exactly as before.
        /// </summary>
        public bool Suppress { get; set; }

        public KeyboardHookEventArgs(int virtualKey, bool isKeyDown, uint timeStamp = 0, bool injected = false)
        {
            VirtualKey = virtualKey;
            IsKeyDown = isKeyDown;
            TimeStamp = timeStamp;
            Injected = injected;
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
                Logger.Error($"[Hooks] failed to install the keyboard hook. Win32 error {error}.");
                return false;
            }

            Logger.Info("[Hooks] low-level keyboard hook installed.");
            return true;
        }

        public void Stop()
        {
            if (_hookHandle != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
                Logger.Info("[Hooks] low-level keyboard hook removed.");
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
                        bool injected = (data.flags & NativeMethods.LLKHF_INJECTED) != 0;
                        var args = new KeyboardHookEventArgs(
                            (int)data.vkCode, isDown, data.time, injected);
                        KeyEvent?.Invoke(this, args);

                        // A listener claimed this key. Returning non-zero (instead of
                        // chaining) is what actually stops it reaching the focused app.
                        if (args.Suppress)
                        {
                            return (IntPtr)1;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("[Hooks] exception inside the keyboard hook callback.", ex);
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
