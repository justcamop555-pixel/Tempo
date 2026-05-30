using System;
using System.Runtime.InteropServices;
using AutoClicker.Native;
using AutoClicker.Utils;

namespace AutoClicker.Native
{
    /// <summary>
    /// Kinds of raw mouse events surfaced to listeners.
    /// </summary>
    public enum MouseHookEventType
    {
        Move,
        LeftDown,
        LeftUp,
        RightDown,
        RightUp,
        MiddleDown,
        MiddleUp,
        Wheel
    }

    /// <summary>
    /// Data describing a single low-level mouse event.
    /// </summary>
    public sealed class MouseHookEventArgs : EventArgs
    {
        public MouseHookEventType Type { get; }
        public int X { get; }
        public int Y { get; }
        public int WheelDelta { get; }
        public uint TimeStamp { get; }

        public MouseHookEventArgs(MouseHookEventType type, int x, int y, int wheelDelta, uint timeStamp)
        {
            Type = type;
            X = x;
            Y = y;
            WheelDelta = wheelDelta;
            TimeStamp = timeStamp;
        }
    }

    /// <summary>
    /// Installs a system wide low-level mouse hook (WH_MOUSE_LL) and translates the
    /// raw messages into managed events. The hook is only active between calls to
    /// <see cref="Start"/> and <see cref="Stop"/> to minimise overhead.
    /// </summary>
    public sealed class LowLevelMouseHook : IDisposable
    {
        // The delegate must be stored in a field so the GC does not collect it
        // while the unmanaged hook still references it.
        private readonly NativeMethods.LowLevelProc _proc;
        private IntPtr _hookHandle = IntPtr.Zero;
        private bool _disposed;

        public event EventHandler<MouseHookEventArgs> MouseEvent;

        public bool IsRunning => _hookHandle != IntPtr.Zero;

        public LowLevelMouseHook()
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
                NativeMethods.WH_MOUSE_LL,
                _proc,
                moduleHandle,
                0);

            if (_hookHandle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                Logger.Error($"Failed to install mouse hook. Win32 error {error}.");
                return false;
            }

            Logger.Info("Low-level mouse hook installed.");
            return true;
        }

        public void Stop()
        {
            if (_hookHandle != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
                Logger.Info("Low-level mouse hook removed.");
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                    int message = wParam.ToInt32();
                    MouseHookEventType? type = null;
                    int wheel = 0;

                    switch (message)
                    {
                        case NativeMethods.WM_MOUSEMOVE:
                            type = MouseHookEventType.Move;
                            break;
                        case NativeMethods.WM_LBUTTONDOWN:
                            type = MouseHookEventType.LeftDown;
                            break;
                        case NativeMethods.WM_LBUTTONUP:
                            type = MouseHookEventType.LeftUp;
                            break;
                        case NativeMethods.WM_RBUTTONDOWN:
                            type = MouseHookEventType.RightDown;
                            break;
                        case NativeMethods.WM_RBUTTONUP:
                            type = MouseHookEventType.RightUp;
                            break;
                        case NativeMethods.WM_MBUTTONDOWN:
                            type = MouseHookEventType.MiddleDown;
                            break;
                        case NativeMethods.WM_MBUTTONUP:
                            type = MouseHookEventType.MiddleUp;
                            break;
                        case NativeMethods.WM_MOUSEWHEEL:
                            type = MouseHookEventType.Wheel;
                            // High word of mouseData is the signed wheel delta.
                            wheel = (short)((data.mouseData >> 16) & 0xFFFF);
                            break;
                    }

                    if (type.HasValue)
                    {
                        MouseEvent?.Invoke(this, new MouseHookEventArgs(
                            type.Value, data.pt.X, data.pt.Y, wheel, data.time));
                    }
                }
                catch (Exception ex)
                {
                    // A throwing hook can destabilise the whole desktop, so swallow
                    // and log rather than propagate.
                    Logger.Error("Exception inside mouse hook callback.", ex);
                }
            }

            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LowLevelMouseHook));
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
