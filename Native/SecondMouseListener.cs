using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using AutoClicker.Utils;

namespace AutoClicker.Native
{
    /// <summary>A physical mouse the OS reports through Raw Input.</summary>
    public sealed class MouseDeviceInfo
    {
        public IntPtr Handle { get; }
        /// <summary>The raw device path, e.g. \\?\HID#VID_046D&amp;PID_C52B#...</summary>
        public string DeviceName { get; }
        /// <summary>A short, human-friendly label (VID/PID) derived from the path.</summary>
        public string FriendlyName { get; }

        public MouseDeviceInfo(IntPtr handle, string deviceName, string friendlyName)
        {
            Handle = handle;
            DeviceName = deviceName;
            FriendlyName = friendlyName;
        }
    }

    /// <summary>
    /// Raw Input, but PER DEVICE. Unlike <see cref="RawMouseInput"/> (which only sums
    /// movement to drive the camera), this reports which physical mouse produced each
    /// event via the device handle in <c>RAWINPUTHEADER.hDevice</c> — so a SECOND real
    /// mouse can be told apart from the main one and used to drive the second cursor.
    ///
    /// Also enumerates the connected mice (<see cref="EnumerateRealMice"/>) so Tempo can
    /// tell the user "2 mice detected" and refuse the second-mouse mode when there's
    /// only one (which would leave the machine with no usable cursor).
    ///
    /// RIDEV_INPUTSINK keeps input flowing while the game — not Tempo — has focus.
    /// Movement is RELATIVE counts (not pixels); button transitions come as flags.
    /// </summary>
    public sealed class SecondMouseListener : IDisposable
    {
        private const int WM_INPUT = 0x00FF;
        private const int WM_INPUT_DEVICE_CHANGE = 0x00FE;
        private const uint RID_INPUT = 0x10000003;
        private const uint RIDI_DEVICENAME = 0x20000007;
        private const uint RIDEV_INPUTSINK = 0x00000100;
        private const uint RIDEV_DEVNOTIFY = 0x00002000;   // also send WM_INPUT_DEVICE_CHANGE
        private const uint RIDEV_REMOVE = 0x00000001;
        private const uint RIM_TYPEMOUSE = 0;
        private const ushort MOUSE_MOVE_ABSOLUTE = 0x01;

        // For reading a device's real product name (e.g. "Razer DeathAdder") via HID.
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;

        // Button transition flags (RAWMOUSE.usButtonFlags).
        public const ushort RI_MOUSE_LEFT_BUTTON_DOWN = 0x0001;
        public const ushort RI_MOUSE_LEFT_BUTTON_UP = 0x0002;
        public const ushort RI_MOUSE_RIGHT_BUTTON_DOWN = 0x0004;
        public const ushort RI_MOUSE_RIGHT_BUTTON_UP = 0x0008;
        public const ushort RI_MOUSE_MIDDLE_BUTTON_DOWN = 0x0010;
        public const ushort RI_MOUSE_MIDDLE_BUTTON_UP = 0x0020;
        public const ushort RI_MOUSE_WHEEL = 0x0400;

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICELIST
        {
            public IntPtr hDevice;
            public uint dwType;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTHEADER
        {
            public uint dwType;
            public uint dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        // See RawMouseInput for why the explicit padding reproduces the RAWMOUSE union
        // alignment so lLastX/lLastY land at the right offsets.
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
        private static extern uint GetRawInputDeviceList(
            [In, Out] RAWINPUTDEVICELIST[] pRawInputDeviceList, ref uint puiNumDevices, uint cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputDeviceInfo(
            IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess,
            uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition,
            uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetProductString(IntPtr hidDeviceObject, byte[] buffer, int bufferLength);

        /// <summary>
        /// Fired (on the UI thread) for every raw mouse event: which device, the relative
        /// movement, any button transitions, the wheel delta (WHEEL_DELTA units) this
        /// report carried, and whether the report was ABSOLUTE.
        ///
        /// The absolute flag matters because such a report carries a screen coordinate
        /// rather than a delta, so it is deliberately not turned into movement. Without
        /// the flag the listener reported dx=dy=0 and the controller could not tell "this
        /// device did not move" from "this device cannot drive the cursor at all" — which
        /// is what a drawing tablet, a touchscreen digitizer or an RDP/VM pointer looks
        /// like. Those still count as mice, so they arm the mode and appear in the picker,
        /// and wiggling one would silently never bind.
        /// </summary>
        public event Action<IntPtr, int, int, ushort, int, bool> Input;

        /// <summary>Fired (UI thread) when a mouse is plugged in or unplugged.</summary>
        public event Action DevicesChanged;

        private MessageWindow _window;
        private bool _disposed;

        // Product-name lookups open a HID handle, so cache them by device path.
        private static readonly Dictionary<string, string> _productCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public bool IsRunning { get; private set; }

        // Enumeration cache. The Live-debug window, the mice-UI refresh timer and the
        // controller's reconcile all ask for the mouse list — several times a second
        // combined, each hit walking the raw-input device list plus HID name queries.
        // The set of mice changes on the order of "someone plugs one in", so a short
        // TTL answers the repeats for free; a device-change notification invalidates
        // it instantly so plug/unplug is still seen immediately. UI-thread only.
        private static List<MouseDeviceInfo> _miceCache;
        private static int _miceCacheTick;
        private const int MiceCacheTtlMs = 1000;

        /// <summary>Drops the cached mouse list (called on WM_INPUT_DEVICE_CHANGE).</summary>
        private static void InvalidateMiceCache()
        {
            _miceCache = null;
        }

        /// <summary>
        /// Lists the physical mice Windows currently sees, skipping the Remote-Desktop
        /// mirror pointer (which isn't a real mouse). Safe to call any time. Answers
        /// from a ~1 s cache (invalidated instantly on plug/unplug).
        /// </summary>
        public static List<MouseDeviceInfo> EnumerateRealMice()
        {
            var cached = _miceCache;
            if (cached != null && unchecked(Environment.TickCount - _miceCacheTick) < MiceCacheTtlMs)
            {
                return cached;
            }
            var result = new List<MouseDeviceInfo>();
            try
            {
                uint count = 0;
                uint listSize = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();
                if (GetRawInputDeviceList(null, ref count, listSize) == unchecked((uint)-1) || count == 0)
                {
                    return result;
                }
                var list = new RAWINPUTDEVICELIST[count];
                if (GetRawInputDeviceList(list, ref count, listSize) == unchecked((uint)-1))
                {
                    return result;
                }

                for (int i = 0; i < count; i++)
                {
                    if (list[i].dwType != RIM_TYPEMOUSE)
                    {
                        continue;
                    }
                    string name = GetDeviceName(list[i].hDevice);
                    // The RDP mirror driver shows up as a "mouse" but isn't one; root-
                    // enumerated virtual pointers likewise shouldn't count as real mice.
                    if (name != null &&
                        (name.IndexOf("RDP_MOU", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        continue;
                    }
                    result.Add(new MouseDeviceInfo(list[i].hDevice, name ?? "", ResolveFriendly(name)));
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("[2nd mouse] could not enumerate mice: " + ex.Message);
            }
            _miceCache = result;
            _miceCacheTick = Environment.TickCount;
            return result;
        }

        private static string GetDeviceName(IntPtr hDevice)
        {
            uint size = 0;
            if (GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, IntPtr.Zero, ref size) != 0 || size == 0)
            {
                return null;
            }
            IntPtr buffer = Marshal.AllocHGlobal((int)size * 2);
            try
            {
                if (GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, buffer, ref size) == unchecked((uint)-1))
                {
                    return null;
                }
                return Marshal.PtrToStringAnsi(buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>
        /// The nicest label we can get for a device: its real HID product name (e.g.
        /// "Razer DeathAdder V2") if the device exposes one, else the VID/PID fallback.
        /// </summary>
        private static string ResolveFriendly(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName))
            {
                return "Mouse";
            }
            if (_productCache.TryGetValue(deviceName, out string cached))
            {
                return cached;
            }
            string product = GetProductName(deviceName);
            string friendly = string.IsNullOrEmpty(product) ? MakeFriendly(deviceName) : product;
            _productCache[deviceName] = friendly;
            return friendly;
        }

        /// <summary>Opens the HID device to read its product string. Null if unavailable.</summary>
        private static string GetProductName(string devicePath)
        {
            IntPtr h = IntPtr.Zero;
            try
            {
                h = CreateFileW(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero,
                    OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == IntPtr.Zero || h == new IntPtr(-1))
                {
                    return null;
                }
                var buf = new byte[256];
                if (HidD_GetProductString(h, buf, buf.Length))
                {
                    string s = Encoding.Unicode.GetString(buf).TrimEnd('\0').Trim();
                    if (s.Length > 0) { return s; }
                }
            }
            catch { }
            finally
            {
                if (h != IntPtr.Zero && h != new IntPtr(-1)) { CloseHandle(h); }
            }
            return null;
        }

        /// <summary>Turns \\?\HID#VID_046D&amp;PID_C52B#... into a short "Mouse VID_046D/PID_C52B".</summary>
        private static string MakeFriendly(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName))
            {
                return "Mouse";
            }
            try
            {
                string vid = null, pid = null;
                int vi = deviceName.IndexOf("VID_", StringComparison.OrdinalIgnoreCase);
                if (vi >= 0 && vi + 8 <= deviceName.Length) { vid = deviceName.Substring(vi, 8); }
                int pi = deviceName.IndexOf("PID_", StringComparison.OrdinalIgnoreCase);
                if (pi >= 0 && pi + 8 <= deviceName.Length) { pid = deviceName.Substring(pi, 8); }
                if (vid != null && pid != null) { return "Mouse " + vid + "/" + pid; }
            }
            catch { }
            return "Mouse";
        }

        /// <summary>Registers for raw mouse input. Call from the UI (message-pump) thread.</summary>
        public bool Start()
        {
            if (_disposed) { throw new ObjectDisposedException(nameof(SecondMouseListener)); }
            if (IsRunning) { return true; }
            try
            {
                _window = new MessageWindow(OnRawInput, () =>
                {
                    InvalidateMiceCache();   // handlers must see the post-change device list
                    try { DevicesChanged?.Invoke(); } catch { }
                });

                var dev = new RAWINPUTDEVICE[1];
                dev[0].usUsagePage = 0x01;             // Generic Desktop Controls
                dev[0].usUsage = 0x02;                 // Mouse
                // INPUTSINK: keep receiving without focus. DEVNOTIFY: also get told the
                // moment a mouse is plugged in / unplugged (WM_INPUT_DEVICE_CHANGE).
                dev[0].dwFlags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY;
                dev[0].hwndTarget = _window.Handle;

                if (!RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
                {
                    int err = Marshal.GetLastWin32Error();
                    Logger.Error("[2nd mouse] raw input registration failed. Win32 error " + err + ".");
                    _window.Dispose();
                    _window = null;
                    return false;
                }

                IsRunning = true;
                Logger.Info("[2nd mouse] per-device raw input active.");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("[2nd mouse] could not start per-device raw input.", ex);
                return false;
            }
        }

        public void Stop()
        {
            if (!IsRunning) { return; }
            IsRunning = false;
            try
            {
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
            Logger.Info("[2nd mouse] per-device raw input stopped.");
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
            // Absolute pointers (tablets, RDP, some VMs) report a screen coordinate, not
            // a delta; treating that as movement would fling the second cursor away.
            int dx = 0, dy = 0;
            bool absolute = (raw.mouse.usFlags & MOUSE_MOVE_ABSOLUTE) == MOUSE_MOVE_ABSOLUTE;
            if (!absolute)
            {
                dx = raw.mouse.lLastX;
                dy = raw.mouse.lLastY;
            }
            int wheel = 0;
            if ((raw.mouse.usButtonFlags & RI_MOUSE_WHEEL) != 0)
            {
                // usButtonData is a signed wheel delta (multiples of WHEEL_DELTA = 120).
                wheel = (short)raw.mouse.usButtonData;
            }
            Input?.Invoke(raw.header.hDevice, dx, dy, raw.mouse.usButtonFlags, wheel, absolute);
        }

        /// <summary>Message-only window (HWND_MESSAGE) that exists purely to receive WM_INPUT.</summary>
        private sealed class MessageWindow : NativeWindow, IDisposable
        {
            private const int HWND_MESSAGE = -3;
            private readonly Action<IntPtr> _onInput;
            private readonly Action _onDeviceChange;

            public MessageWindow(Action<IntPtr> onInput, Action onDeviceChange)
            {
                _onInput = onInput;
                _onDeviceChange = onDeviceChange;
                CreateHandle(new CreateParams { Parent = new IntPtr(HWND_MESSAGE) });
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_INPUT)
                {
                    try { _onInput(m.LParam); } catch { }
                }
                else if (m.Msg == WM_INPUT_DEVICE_CHANGE)
                {
                    try { _onDeviceChange(); } catch { }
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
