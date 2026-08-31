using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using AutoClicker.Utils;

namespace AutoClicker.Native
{
    /// <summary>
    /// Reports RAW, RELATIVE mouse movement (counts, not pixels) using the Win32 Raw
    /// Input API.
    ///
    /// Why not the low-level mouse hook? WH_MOUSE_LL reports the CURSOR POSITION, and
    /// the moment a game takes mouse-look it hides the cursor, clips it to the window
    /// centre, or reads raw input itself — at which point the cursor stops moving and
    /// the hook reports deltas of zero even though the player is swinging the mouse
    /// hard. Camera tracking built on the hook would simply go blind exactly when it
    /// matters. Raw Input reports the physical movement the mouse reported to the OS,
    /// independent of where (or whether) a cursor exists.
    ///
    /// RIDEV_INPUTSINK means we keep receiving movement even though our window is not
    /// the foreground one — essential, since the game is what has focus.
    ///
    /// Deltas are accumulated with Interlocked and drained by the consumer thread, so
    /// the (UI-thread) message handler stays allocation-free and lock-free.
    /// </summary>
    public sealed class RawMouseInput : IDisposable
    {
        private const int WM_INPUT = 0x00FF;
        private const uint RID_INPUT = 0x10000003;
        private const uint RIDEV_INPUTSINK = 0x00000100;
        private const uint RIDEV_REMOVE = 0x00000001;
        private const uint RIM_TYPEMOUSE = 0;

        // usFlags: movement is relative (nearly all mice) or absolute (tablets, some
        // remote-desktop/VM pointers). Only relative motion can drive a camera.
        private const ushort MOUSE_MOVE_RELATIVE = 0x00;
        private const ushort MOUSE_MOVE_ABSOLUTE = 0x01;

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTHEADER
        {
            public uint dwType;
            public uint dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        // Mirrors RAWMOUSE. The real struct unions ulButtons with
        // {usButtonFlags, usButtonData}; the explicit padding field below reproduces
        // the compiler's alignment so the lLastX/lLastY offsets land correctly.
        [StructLayout(LayoutKind.Sequential)]
        private struct RAWMOUSE
        {
            public ushort usFlags;
            public ushort _padding;
            public ushort usButtonFlags;
            public ushort usButtonData;
            public uint ulRawButtons;
            public int lLastX;
            public int lLastY;
            public uint ulExtraInformation;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUT
        {
            public RAWINPUTHEADER header;
            public RAWMOUSE mouse;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(
            [In] RAWINPUTDEVICE[] devices, uint numDevices, uint size);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(
            IntPtr hRawInput, uint command, out RAWINPUT data, ref uint size, uint headerSize);

        /// <summary>Accumulated raw counts since the last drain. Interlocked only.</summary>
        private long _accumX;
        private long _accumY;

        private MessageWindow _window;
        private bool _disposed;

        /// <summary>True once the OS accepted our raw-input registration.</summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// Starts listening. MUST be called from a thread with a message pump (the UI
        /// thread) — WM_INPUT is delivered to a window, and without a pump no movement
        /// would ever arrive.
        /// </summary>
        public bool Start()
        {
            if (_disposed) { throw new ObjectDisposedException(nameof(RawMouseInput)); }
            if (IsRunning) { return true; }

            try
            {
                _window = new MessageWindow(OnRawInput);

                var dev = new RAWINPUTDEVICE[1];
                dev[0].usUsagePage = 0x01;             // Generic Desktop Controls
                dev[0].usUsage = 0x02;                 // Mouse
                dev[0].dwFlags = RIDEV_INPUTSINK;      // keep receiving without focus
                dev[0].hwndTarget = _window.Handle;

                if (!RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
                {
                    int err = Marshal.GetLastWin32Error();
                    Logger.Error("[Movement] raw mouse input registration failed. Win32 error " + err + ".");
                    _window.Dispose();
                    _window = null;
                    return false;
                }

                IsRunning = true;
                Logger.Info("[Movement] raw mouse input active (relative deltas, survives cursor capture).");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("[Movement] could not start raw mouse input.", ex);
                return false;
            }
        }

        public void Stop()
        {
            if (!IsRunning) { return; }
            IsRunning = false;
            try
            {
                // Unregister before the window dies, or the OS keeps posting to a dead HWND.
                var dev = new RAWINPUTDEVICE[1];
                dev[0].usUsagePage = 0x01;
                dev[0].usUsage = 0x02;
                dev[0].dwFlags = RIDEV_REMOVE;
                dev[0].hwndTarget = IntPtr.Zero;       // must be NULL for RIDEV_REMOVE
                RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
            }
            catch { }

            try { _window?.Dispose(); } catch { }
            _window = null;
            Interlocked.Exchange(ref _accumX, 0);
            Interlocked.Exchange(ref _accumY, 0);
            Logger.Info("[Movement] raw mouse input stopped.");
        }

        /// <summary>
        /// Takes everything accumulated since the previous call and resets the
        /// counters, so no movement is ever counted twice or silently dropped.
        /// </summary>
        public void Drain(out int dx, out int dy)
        {
            dx = (int)Interlocked.Exchange(ref _accumX, 0);
            dy = (int)Interlocked.Exchange(ref _accumY, 0);
        }

        private void OnRawInput(IntPtr lParam)
        {
            uint size = (uint)Marshal.SizeOf<RAWINPUT>();
            if (GetRawInputData(lParam, RID_INPUT, out RAWINPUT raw, ref size,
                    (uint)Marshal.SizeOf<RAWINPUTHEADER>()) == unchecked((uint)-1))
            {
                return;
            }
            if (raw.header.dwType != RIM_TYPEMOUSE)
            {
                return;
            }
            // Absolute-positioning devices (tablet, RDP, some VMs) report a screen
            // coordinate here, not a delta. Integrating that as if it were movement
            // would send the camera estimate flying, so ignore it outright.
            if ((raw.mouse.usFlags & MOUSE_MOVE_ABSOLUTE) == MOUSE_MOVE_ABSOLUTE)
            {
                return;
            }
            if (raw.mouse.lLastX != 0) { Interlocked.Add(ref _accumX, raw.mouse.lLastX); }
            if (raw.mouse.lLastY != 0) { Interlocked.Add(ref _accumY, raw.mouse.lLastY); }
        }

        /// <summary>Message-only window (HWND_MESSAGE) that exists purely to receive WM_INPUT.</summary>
        private sealed class MessageWindow : NativeWindow, IDisposable
        {
            private const int HWND_MESSAGE = -3;
            private readonly Action<IntPtr> _onInput;

            public MessageWindow(Action<IntPtr> onInput)
            {
                _onInput = onInput;
                CreateHandle(new CreateParams { Parent = new IntPtr(HWND_MESSAGE) });
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_INPUT)
                {
                    // A throwing window proc would take the whole message loop down.
                    try { _onInput(m.LParam); } catch { }
                }
                base.WndProc(ref m);
            }

            public void Dispose()
            {
                try { DestroyHandle(); } catch { }
            }
        }

        public void Dispose()
        {
            if (_disposed) { return; }
            Stop();
            _disposed = true;
        }
    }
}
