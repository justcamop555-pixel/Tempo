using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using AutoClicker.Utils;

namespace AutoClicker.Native
{
    /// <summary>One raw mouse report, as the hub hands it to its listeners.</summary>
    public readonly struct RawMouseEvent
    {
        public RawMouseEvent(IntPtr device, int dx, int dy, ushort buttonFlags, short buttonData, bool absolute, uint time)
        {
            Device = device;
            Dx = dx;
            Dy = dy;
            ButtonFlags = buttonFlags;
            ButtonData = buttonData;
            Absolute = absolute;
            Time = time;
        }

        /// <summary>
        /// The physical mouse that sent it. <see cref="IntPtr.Zero"/> for input some program
        /// injected (SendInput) — Tempo's own playback included.
        /// </summary>
        public IntPtr Device { get; }

        /// <summary>Movement in mouse counts, not pixels (0 when <see cref="Absolute"/>).</summary>
        public int Dx { get; }
        public int Dy { get; }

        /// <summary>RAWMOUSE.usButtonFlags: button transitions and the wheel flag.</summary>
        public ushort ButtonFlags { get; }

        /// <summary>RAWMOUSE.usButtonData: the signed wheel delta when the wheel flag is set.</summary>
        public short ButtonData { get; }

        /// <summary>
        /// A tablet, touchscreen or remote-desktop pointer reporting a screen coordinate
        /// instead of a movement. Its Dx/Dy are left at 0: integrating a coordinate as if it
        /// were movement flings whatever is listening.
        /// </summary>
        public bool Absolute { get; }

        /// <summary>GetTickCount-clock time of the report (the message time), in ms.</summary>
        public uint Time { get; }
    }

    /// <summary>
    /// The process's ONE Raw Input registration for the mouse, shared by everything that
    /// wants raw movement.
    ///
    /// Windows keeps a single raw-input registration per usage per process: registering
    /// the mouse again simply moves it to the newest window, and removing it removes it for
    /// everyone. Camera-relative movement, its calibration dialog and the second cursor
    /// each registered their own, so whichever started last silently took the input from
    /// the others, and whichever stopped first switched it off for all of them. (Opening
    /// the calibration dialog while movement was armed left movement blind as soon as the
    /// dialog closed.) Macro recording needs it too, so it is shared here instead:
    /// registered on the first listener, removed after the last.
    ///
    /// UI thread only: WM_INPUT arrives at a window, and every caller already runs there.
    /// </summary>
    public static class RawMouseHub
    {
        private const int WM_INPUT = 0x00FF;
        private const int WM_INPUT_DEVICE_CHANGE = 0x00FE;
        private const uint RID_INPUT = 0x10000003;
        private const uint RIDEV_INPUTSINK = 0x00000100;
        private const uint RIDEV_DEVNOTIFY = 0x00002000;
        private const uint RIDEV_REMOVE = 0x00000001;
        private const uint RIM_TYPEMOUSE = 0;
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

        // RAWMOUSE: the real struct unions ulButtons with {usButtonFlags, usButtonData};
        // the explicit padding reproduces the compiler's alignment so lLastX/lLastY land at
        // the right offsets.
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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputBuffer(IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

        [DllImport("user32.dll")]
        private static extern int GetMessageTime();

        private sealed class Listener
        {
            public Action<RawMouseEvent> OnInput;
            public Action OnDevicesChanged;
        }

        private static readonly List<Listener> _listeners = new List<Listener>();
        private static Listener[] _snapshot = new Listener[0];
        private static MessageWindow _window;
        private static int _ownerThread;

        /// <summary>True while the registration is live (at least one listener).</summary>
        public static bool IsActive => _window != null;

        /// <summary>How many listeners share it right now (Live debug, tests).</summary>
        public static int ListenerCount => _listeners.Count;

        /// <summary>
        /// Adds a listener, registering for raw input if it is the first. False when Windows
        /// refused the registration. Call on the UI thread.
        /// </summary>
        public static bool Subscribe(Action<RawMouseEvent> onInput, Action onDevicesChanged = null)
        {
            if (onInput == null) { throw new ArgumentNullException(nameof(onInput)); }

            if (_window != null && Thread.CurrentThread.ManagedThreadId != _ownerThread)
            {
                Logger.Warn("[RawInput] a listener subscribed from another thread; raw input lives on the UI thread.");
                return false;
            }

            foreach (Listener l in _listeners)
            {
                if (l.OnInput == onInput) { return true; }   // already listening
            }

            if (_window == null && !Register())
            {
                return false;
            }

            _listeners.Add(new Listener { OnInput = onInput, OnDevicesChanged = onDevicesChanged });
            _snapshot = _listeners.ToArray();
            return true;
        }

        /// <summary>Removes a listener, and the registration with the last one.</summary>
        public static void Unsubscribe(Action<RawMouseEvent> onInput)
        {
            if (onInput == null) { return; }
            int removed = _listeners.RemoveAll(l => l.OnInput == onInput);
            if (removed == 0) { return; }
            _snapshot = _listeners.ToArray();
            if (_listeners.Count == 0)
            {
                Unregister();
            }
        }

        /// <summary>
        /// Hands every raw report still waiting in this thread's queue to the listeners NOW,
        /// instead of when the message loop next gets to them. Returns how many there were.
        ///
        /// For code that must see movement in order with something else: a low-level hook's
        /// callback is SENT to this thread and so runs ahead of WM_INPUT messages POSTED
        /// before it — the macro recorder would log a click before the last of the camera
        /// turn that preceded it. Draining first keeps "turn, then shoot" in that order.
        /// </summary>
        public static int DrainPending(uint time)
        {
            if (_window == null || Thread.CurrentThread.ManagedThreadId != _ownerThread)
            {
                return 0;
            }

            int count = 0;
            uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
            uint one = 0;
            if (GetRawInputBuffer(IntPtr.Zero, ref one, headerSize) != 0 || one == 0)
            {
                return 0;   // nothing queued
            }

            uint capacity = one * 32;
            IntPtr buffer = Marshal.AllocHGlobal((int)capacity);
            try
            {
                for (int rounds = 0; rounds < 64; rounds++)
                {
                    uint size = capacity;
                    uint n = GetRawInputBuffer(buffer, ref size, headerSize);
                    if (n == 0 || n == unchecked((uint)-1))
                    {
                        break;
                    }

                    IntPtr p = buffer;
                    for (uint i = 0; i < n; i++)
                    {
                        var raw = Marshal.PtrToStructure<RAWINPUT>(p);
                        if (raw.header.dwType == RIM_TYPEMOUSE)
                        {
                            Dispatch(raw, time);
                            count++;
                        }
                        // NEXTRAWINPUTBLOCK: each block is padded to pointer size.
                        long next = p.ToInt64() + raw.header.dwSize;
                        long align = IntPtr.Size;
                        p = new IntPtr((next + align - 1) & ~(align - 1));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Swallow("RawMouseHub.DrainPending", ex);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return count;
        }

        private static bool Register()
        {
            try
            {
                var window = new MessageWindow();
                var dev = new RAWINPUTDEVICE[1];
                dev[0].usUsagePage = 0x01;   // Generic Desktop Controls
                dev[0].usUsage = 0x02;       // Mouse
                // INPUTSINK: keep receiving while a game, not Tempo, has focus.
                // DEVNOTIFY: also hear about mice plugged in / unplugged.
                dev[0].dwFlags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY;
                dev[0].hwndTarget = window.Handle;

                if (!RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
                {
                    int err = Marshal.GetLastWin32Error();
                    Logger.Error("[RawInput] raw mouse input registration failed. Win32 error " + err + ".");
                    window.Dispose();
                    return false;
                }

                _window = window;
                _ownerThread = Thread.CurrentThread.ManagedThreadId;
                Logger.Info("[RawInput] raw mouse input registered (shared).");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("[RawInput] could not register raw mouse input.", ex);
                return false;
            }
        }

        private static void Unregister()
        {
            try
            {
                // Unregister before the window dies, or Windows keeps posting to a dead HWND.
                var dev = new RAWINPUTDEVICE[1];
                dev[0].usUsagePage = 0x01;
                dev[0].usUsage = 0x02;
                dev[0].dwFlags = RIDEV_REMOVE;
                dev[0].hwndTarget = IntPtr.Zero;   // must be NULL for RIDEV_REMOVE
                RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
            }
            catch { }

            try { _window?.Dispose(); } catch { }
            _window = null;
            Logger.Info("[RawInput] raw mouse input released (no listeners left).");
        }

        private static void OnInputMessage(IntPtr lParam)
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
            Dispatch(raw, unchecked((uint)GetMessageTime()));
        }

        private static void Dispatch(RAWINPUT raw, uint time)
        {
            bool absolute = (raw.mouse.usFlags & MOUSE_MOVE_ABSOLUTE) == MOUSE_MOVE_ABSOLUTE;
            var e = new RawMouseEvent(
                raw.header.hDevice,
                absolute ? 0 : raw.mouse.lLastX,
                absolute ? 0 : raw.mouse.lLastY,
                raw.mouse.usButtonFlags,
                unchecked((short)raw.mouse.usButtonData),
                absolute,
                time);

            foreach (Listener l in _snapshot)
            {
                // One listener's exception must not starve the others, nor take the
                // message loop down with it.
                try { l.OnInput(e); } catch (Exception ex) { Logger.Swallow("RawMouseHub listener", ex); }
            }
        }

        private static void OnDevicesChanged()
        {
            foreach (Listener l in _snapshot)
            {
                if (l.OnDevicesChanged == null) { continue; }
                try { l.OnDevicesChanged(); } catch (Exception ex) { Logger.Swallow("RawMouseHub device change", ex); }
            }
        }

        /// <summary>Message-only window (HWND_MESSAGE) that exists purely to receive WM_INPUT.</summary>
        private sealed class MessageWindow : NativeWindow, IDisposable
        {
            private const int HWND_MESSAGE = -3;

            public MessageWindow()
            {
                CreateHandle(new CreateParams { Parent = new IntPtr(HWND_MESSAGE) });
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_INPUT)
                {
                    // A throwing window proc would take the whole message loop down.
                    try { OnInputMessage(m.LParam); } catch { }
                }
                else if (m.Msg == WM_INPUT_DEVICE_CHANGE)
                {
                    try { OnDevicesChanged(); } catch { }
                }
                base.WndProc(ref m);
            }

            public void Dispose()
            {
                try { DestroyHandle(); } catch { }
            }
        }
    }
}
