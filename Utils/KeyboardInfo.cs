using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Identifies the keyboard(s) actually attached to this PC, plus the active layout
    /// and key count.
    ///
    /// Why Tempo cares. Hotkeys are bound by VIRTUAL-KEY code, but what a virtual key
    /// MEANS depends on the keyboard layout: the physical key that produces VK_A sits in
    /// a different place on AZERTY than on QWERTY, and F13-F24 only exist on some
    /// keyboards at all. When a hotkey "doesn't work on my keyboard", the layout and the
    /// device are the first two things anyone would want to know — and until now Tempo
    /// could not tell you either.
    ///
    /// Everything here is best-effort and read-only: a PC that reports nothing simply
    /// shows "unknown" rather than failing.
    /// </summary>
    public static class KeyboardInfo
    {
        private const uint RIM_TYPEMOUSE = 0;
        private const uint RIM_TYPEKEYBOARD = 1;
        private const uint RIDI_DEVICENAME = 0x20000007;

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICELIST
        {
            public IntPtr hDevice;
            public uint dwType;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputDeviceList(
            [In, Out] RAWINPUTDEVICELIST[] deviceList, ref uint numDevices, uint size);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint GetRawInputDeviceInfo(
            IntPtr hDevice, uint command, [Out] StringBuilder data, ref uint size);

        /// <summary>
        /// Pulls "VID_1532&amp;PID_0098" out of a raw-input device path. This is the
        /// device's identity — every HID collection the same physical product exposes
        /// carries the same VID/PID.
        /// </summary>
        private static string VidPidOf(string devicePath)
        {
            if (string.IsNullOrEmpty(devicePath))
            {
                return null;
            }
            int v = devicePath.IndexOf("VID_", StringComparison.OrdinalIgnoreCase);
            int p = devicePath.IndexOf("PID_", StringComparison.OrdinalIgnoreCase);
            if (v < 0 || p < 0 || v + 8 > devicePath.Length || p + 8 > devicePath.Length)
            {
                return null;
            }
            return devicePath.Substring(v, 8) + "&" + devicePath.Substring(p, 8);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint threadId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetKeyboardLayoutName([Out] StringBuilder name);

        [DllImport("user32.dll")]
        private static extern int GetKeyboardType(int typeFlag);

        // ── HID product string ───────────────────────────────────────────────
        // The registry's DeviceDesc for a HID keyboard is almost always the generic
        // "HID Keyboard Device" — the driver's description, not the hardware. The real
        // model ("G915 TKL Gaming Keyboard") lives in the device's own USB product
        // string, which HidD_GetProductString reads straight off the device.
        //
        // The keyboard is already open elsewhere in the system, so we ask for NO access
        // (dwDesiredAccess = 0) and share everything: that is enough to query the
        // descriptor strings and is why this works without admin and without disturbing
        // the keyboard.
        private const uint GENERIC_NONE = 0;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFile(
            string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
            uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("hid.dll", CharSet = CharSet.Unicode)]
        private static extern bool HidD_GetProductString(IntPtr device, StringBuilder buffer, int bufferLength);

        [DllImport("hid.dll", CharSet = CharSet.Unicode)]
        private static extern bool HidD_GetManufacturerString(IntPtr device, StringBuilder buffer, int bufferLength);

        private static readonly IntPtr InvalidHandle = new IntPtr(-1);

        /// <summary>
        /// The device's own product string, e.g. "G915 TKL Gaming Keyboard", prefixed
        /// with the manufacturer when that adds something. Null when the device won't
        /// answer (plenty won't — internal laptop keyboards and PS/2 have no USB strings
        /// at all), in which case the caller falls back to the registry description.
        /// </summary>
        private static string HidProductName(string devicePath)
        {
            IntPtr h = InvalidHandle;
            try
            {
                h = CreateFile(devicePath, GENERIC_NONE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == InvalidHandle)
                {
                    return null;
                }

                var product = new StringBuilder(256);
                if (!HidD_GetProductString(h, product, product.Capacity * 2))
                {
                    return null;
                }
                string name = product.ToString().Trim();
                if (name.Length == 0)
                {
                    return null;
                }

                // Prepend the maker only when the product string doesn't already say it
                // ("Logitech" + "Logitech G915" would read badly).
                var maker = new StringBuilder(256);
                if (HidD_GetManufacturerString(h, maker, maker.Capacity * 2))
                {
                    string mfg = maker.ToString().Trim();
                    if (mfg.Length > 0 &&
                        name.IndexOf(mfg, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        name = mfg + " " + name;
                    }
                }
                return name;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (h != InvalidHandle && h != IntPtr.Zero)
                {
                    try { CloseHandle(h); } catch { }
                }
            }
        }

        /// <summary>
        /// Number of function keys the active keyboard reports (usually 12). When this
        /// is 12, F13-F24 do not physically exist — so binding them, while legal, means
        /// binding a key that can never be pressed.
        /// </summary>
        public static int FunctionKeyCount
        {
            get
            {
                try
                {
                    int n = GetKeyboardType(2);
                    return n > 0 ? n : 12;
                }
                catch { return 12; }
            }
        }

        /// <summary>"Enhanced (101/102-key)" and friends, from GetKeyboardType(0).</summary>
        public static string KeyboardType
        {
            get
            {
                try
                {
                    switch (GetKeyboardType(0))
                    {
                        case 1: return "IBM PC/XT (83-key)";
                        case 2: return "Olivetti ICO (102-key)";
                        case 3: return "IBM PC/AT (84-key)";
                        case 4: return "Enhanced (101/102-key)";
                        case 5: return "Nokia 1050";
                        case 6: return "Nokia 9140";
                        case 7: return "Japanese";
                        default: return "unknown type";
                    }
                }
                catch { return "unknown type"; }
            }
        }

        /// <summary>
        /// The ACTIVE keyboard layout, e.g. "English (United States)" or
        /// "French (France)". This is the one that decides which physical key produces
        /// which virtual key — change it in Windows and a bound hotkey can move to a
        /// different physical key without Tempo being told a thing.
        /// </summary>
        public static string LayoutName
        {
            get
            {
                try
                {
                    // Low word of the HKL is the language identifier.
                    IntPtr hkl = GetKeyboardLayout(0);
                    int lcid = (int)((long)hkl & 0xFFFF);
                    if (lcid != 0)
                    {
                        var ci = new CultureInfo(lcid);
                        return ci.DisplayName;
                    }
                }
                catch { }

                try
                {
                    var sb = new StringBuilder(16);       // KL_NAMELENGTH
                    if (GetKeyboardLayoutName(sb) && sb.Length > 0)
                    {
                        return "layout " + sb;
                    }
                }
                catch { }
                return "unknown layout";
            }
        }

        /// <summary>
        /// Friendly names of the physical keyboards attached, newest info first.
        /// Usually one entry; a laptop with an external keyboard reports both.
        /// </summary>
        public static List<string> Devices()
        {
            var names = new List<string>();
            try
            {
                uint count = 0;
                uint structSize = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();

                // First call with a null list asks "how many?".
                if (GetRawInputDeviceList(null, ref count, structSize) == unchecked((uint)-1) || count == 0)
                {
                    return names;
                }

                var list = new RAWINPUTDEVICELIST[count];
                if (GetRawInputDeviceList(list, ref count, structSize) == unchecked((uint)-1))
                {
                    return names;
                }

                // Which products ALSO register as a mouse? A gaming mouse exposes a HID
                // keyboard collection so its macro and media buttons can send keystrokes,
                // so it shows up in the keyboard list too — and on the machine this was
                // developed against, a Razer DeathAdder was being reported as the user's
                // keyboard. The device's key count is no help (Windows reports the same
                // generic 264-keys/12-F-keys for every entry), but the VID/PID is: the
                // mouse's keyboard collections carry the same VID/PID as the mouse.
                var mouseIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < count; i++)
                {
                    if (list[i].dwType != RIM_TYPEMOUSE) { continue; }
                    string id = VidPidOf(DevicePath(list[i].hDevice));
                    if (id != null) { mouseIds.Add(id); }
                }

                var keyboardOnly = new List<string>();   // real keyboards
                var alsoAMouse = new List<string>();     // a mouse wearing a keyboard hat

                for (int i = 0; i < count; i++)
                {
                    if (list[i].dwType != RIM_TYPEKEYBOARD)
                    {
                        continue;
                    }

                    string path = DevicePath(list[i].hDevice);
                    if (string.IsNullOrEmpty(path))
                    {
                        continue;
                    }
                    // Remote-desktop and other phantom keyboards are not real hardware
                    // and would only clutter the readout.
                    if (path.IndexOf("RDP_KBD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        path.IndexOf("Root#RDP", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        continue;
                    }

                    // Ask the DEVICE for its model first; fall back to the registry's
                    // driver description ("HID Keyboard Device") only when it won't say.
                    string friendly = HidProductName(path) ?? FriendlyNameFor(path);
                    if (string.IsNullOrEmpty(friendly))
                    {
                        continue;
                    }

                    string id = VidPidOf(path);
                    List<string> bucket = (id != null && mouseIds.Contains(id))
                        ? alsoAMouse : keyboardOnly;
                    // One product exposes several keyboard collections (media keys, macro
                    // keys); they all carry the same product string, so this collapses
                    // them to one entry.
                    if (!bucket.Contains(friendly))
                    {
                        bucket.Add(friendly);
                    }
                }

                // Prefer the genuine keyboards. Only if there are none — a keyboard with
                // an integrated pointing device could land in the other bucket — fall back
                // to the shared ones, because naming something beats naming nothing.
                names.AddRange(keyboardOnly.Count > 0 ? keyboardOnly : alsoAMouse);
            }
            catch (Exception ex)
            {
                Logger.Warn("[Keyboard] could not enumerate keyboards: " + ex.Message);
            }
            return names;
        }

        private static string DevicePath(IntPtr hDevice)
        {
            try
            {
                uint size = 0;
                if (GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, null, ref size) != 0 || size == 0)
                {
                    return null;
                }
                var sb = new StringBuilder((int)size + 2);
                if (GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, sb, ref size) == unchecked((uint)-1))
                {
                    return null;
                }
                return sb.ToString();
            }
            catch { return null; }
        }

        /// <summary>
        /// Turns a raw-input device path into a human name via the registry.
        ///
        /// The path looks like  \\?\HID#VID_046D&amp;PID_C33F&amp;MI_00#7&amp;1a2b#{guid}
        /// and the matching registry key is
        ///   SYSTEM\CurrentControlSet\Enum\HID\VID_046D&amp;PID_C33F&amp;MI_00\7&amp;1a2b
        /// so we strip the "\\?\" prefix, swap '#' for '\', and drop the trailing GUID.
        /// DeviceDesc arrives as "@input.inf,%hid%;HID Keyboard Device" — the part after
        /// the last ';' is the actual text.
        /// </summary>
        private static string FriendlyNameFor(string devicePath)
        {
            try
            {
                string p = devicePath;
                if (p.StartsWith("\\\\?\\", StringComparison.Ordinal))
                {
                    p = p.Substring(4);
                }
                int guid = p.IndexOf('{');
                if (guid > 0)
                {
                    p = p.Substring(0, guid).TrimEnd('#');
                }
                p = p.Replace('#', '\\');

                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    "SYSTEM\\CurrentControlSet\\Enum\\" + p))
                {
                    if (key == null)
                    {
                        return null;
                    }
                    // FriendlyName is the nicest when present; DeviceDesc always is.
                    string name = key.GetValue("FriendlyName") as string
                                  ?? key.GetValue("DeviceDesc") as string;
                    if (string.IsNullOrEmpty(name))
                    {
                        return null;
                    }
                    int semi = name.LastIndexOf(';');
                    if (semi >= 0 && semi + 1 < name.Length)
                    {
                        name = name.Substring(semi + 1);
                    }
                    return name.Trim();
                }
            }
            catch
            {
                return null;      // registry blocked or an unusual path — not fatal
            }
        }

        /// <summary>
        /// One line for the Keybinds tab and the Live Debug panel, e.g.
        /// "HID Keyboard Device · English (United States) · Enhanced (101/102-key), 12 F-keys".
        /// </summary>
        public static string Summary()
        {
            try
            {
                List<string> devices = Devices();
                string who = devices.Count == 0
                    ? "keyboard not identified"
                    : devices.Count == 1
                        ? devices[0]
                        : devices[0] + " (+" + (devices.Count - 1) + " more)";

                return who + " · " + LayoutName + " · " + KeyboardType +
                       ", " + FunctionKeyCount + " F-keys";
            }
            catch (Exception ex)
            {
                return "keyboard info unavailable (" + ex.Message + ")";
            }
        }
    }
}
