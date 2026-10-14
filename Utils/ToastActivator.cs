using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml;
using Microsoft.Win32;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Does what Windows does when one of an app's notifications is clicked, so a click on Tempo's card goes where a
    /// click on Windows' own pop-up would have gone — the page a web site's notification is about, the channel a
    /// chat message came from — instead of only opening the app (or, for a browser, nothing at all).
    ///
    /// HOW WINDOWS DOES IT. A desktop app that sends notifications registers a "toast activator": a COM server that
    /// Windows starts when one of its toasts is clicked, calling INotificationActivationCallback.Activate with the
    /// app's id and the toast's launch arguments. Edge's and Chrome's is notification_helper.exe, which starts the
    /// browser with --notification-launch-id=&lt;those arguments&gt;; the browser then runs the site's own click
    /// handler, which opens the page. Windows finds an app's activator in one of three places:
    ///   * its Start-menu shortcut (System.AppUserModel.ToastActivatorCLSID) — Chrome, Discord, Medal;
    ///   * Software\Classes\AppUserModelId\&lt;AUMID&gt;\CustomActivator — Windows App SDK apps, Windows' own senders;
    ///   * a packaged app's manifest (desktop:ToastNotificationActivation) — Edge (every web site), Teams, Outlook.
    /// (Measured on this PC 2026-10-12: all of those resolved; every Edge site to Edge's notification_helper.exe.)
    /// A toast with activationType="protocol" is simpler: Windows opens its launch address (unigetui://…).
    ///
    /// Tempo calls exactly what Windows would — the app's own registered activator, OUT of process
    /// (CLSCTX_LOCAL_SERVER: never a DLL loaded into Tempo), with the arguments the app itself put in its toast —
    /// and only from a click on the card.
    /// </summary>
    public static class ToastActivator
    {
        private static readonly object Lock = new object();
        private const int RescanMs = 10 * 60 * 1000;   // look for new shortcuts at most this often

        private static Dictionary<string, Guid> _shortcuts;   // AUMID → activator, from Start-menu shortcuts
        private static long _shortcutsTick;
        private static readonly object ScanLock = new object();
        private static readonly Dictionary<string, (Guid? clsid, long tick)> _found =
            new Dictionary<string, (Guid?, long)>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Guid?> _packages = new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase);

        private static int _handled, _failed, _protocols;
        private static volatile string _lastError;

        /// <summary>One line for Live debug.</summary>
        public static string Describe()
        {
            int shortcuts;
            lock (Lock) { shortcuts = _shortcuts?.Count ?? -1; }
            var sb = new StringBuilder("clicks: ");
            sb.Append(_handled).Append(" handed to the app's own notification handler");
            if (_protocols > 0) { sb.Append(" · ").Append(_protocols).Append(" opened its link (protocol)"); }
            if (_failed > 0) { sb.Append(" · ").Append(_failed).Append(" refused (fell back)"); }
            sb.Append(" · ").Append(shortcuts < 0 ? "shortcuts not read yet" : shortcuts + " shortcut(s) name a handler");
            if (_lastError != null) { sb.Append(" · last error: ").Append(_lastError); }
            return sb.ToString();
        }

        /// <summary>Reads the Start-menu shortcuts in the background, so the first notification does not wait for it.</summary>
        public static void Warm()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { lock (ScanLock) { if (_shortcuts == null) { Rescan(); } } }
                catch (Exception ex) { Logger.Swallow("ToastActivator.Warm", ex); }
            });
        }

        /// <summary>
        /// The activator Windows would start for a click on one of <paramref name="aumid"/>'s toasts, or null when the
        /// app has none (a Store app, a script). Cached; a miss is looked up again after a while (an app installed since).
        /// </summary>
        public static Guid? Find(string aumid)
        {
            if (string.IsNullOrWhiteSpace(aumid)) { return null; }
            long now = Environment.TickCount64;
            lock (Lock)
            {
                if (_found.TryGetValue(aumid, out var hit) && (hit.clsid.HasValue || now - hit.tick < RescanMs)) { return hit.clsid; }
            }
            Guid? clsid = null;
            try { clsid = FromShortcuts(aumid) ?? FromRegistry(aumid) ?? FromPackage(aumid); }
            catch (Exception ex) { Logger.Swallow("ToastActivator.Find", ex); }
            lock (Lock)
            {
                if (_found.Count > 512) { _found.Clear(); }
                _found[aumid] = (clsid, now);
            }
            return clsid;
        }

        // ── where Windows looks ───────────────────────────────────────────────────────

        private static Guid? FromShortcuts(string aumid)
        {
            Dictionary<string, Guid> map;
            lock (ScanLock)
            {
                lock (Lock) { map = _shortcuts; }
                bool stale = Environment.TickCount64 - _shortcutsTick > RescanMs;
                if (map == null || (!map.ContainsKey(aumid) && stale)) { map = Rescan(); }
            }
            return map.TryGetValue(aumid, out Guid g) ? g : (Guid?)null;
        }

        /// <summary>Every Start-menu shortcut that names an app id and a toast activator. Call under ScanLock.</summary>
        private static Dictionary<string, Guid> Rescan()
        {
            var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 6 };
            int seen = 0;
            foreach (string root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                                            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) })
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) { continue; }
                foreach (string lnk in Directory.EnumerateFiles(root, "*.lnk", options))
                {
                    if (++seen > 3000) { break; }
                    if (ReadShortcut(lnk, out string id, out Guid clsid)) { map[id] = clsid; }
                }
            }
            lock (Lock)
            {
                _shortcuts = map;
                _shortcutsTick = Environment.TickCount64;
            }
            return map;
        }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            int GetCount(out uint count);
            int GetAt(uint index, out PropertyKey key);
            [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
            int SetValue(ref PropertyKey key, ref PropVariant value);
            int Commit();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey { public Guid FormatId; public uint PropertyId; }

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        private struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Pointer; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHGetPropertyStoreFromParsingName(string path, IntPtr bindContext, int flags, ref Guid iid,
                                                                    out IPropertyStore store);
        [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);

        // System.AppUserModel.* — ID is pid 5, ToastActivatorCLSID pid 26.
        private static readonly Guid AppUserModelProps = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
        private const ushort VT_LPWSTR = 31, VT_CLSID = 72;

        private static bool ReadShortcut(string lnk, out string aumid, out Guid clsid)
        {
            aumid = null;
            clsid = Guid.Empty;
            Guid iid = typeof(IPropertyStore).GUID;
            IPropertyStore store = null;
            try
            {
                if (SHGetPropertyStoreFromParsingName(lnk, IntPtr.Zero, 0, ref iid, out store) != 0 || store == null) { return false; }
                var idKey = new PropertyKey { FormatId = AppUserModelProps, PropertyId = 5 };
                if (store.GetValue(ref idKey, out PropVariant v) == 0)
                {
                    if (v.Type == VT_LPWSTR && v.Pointer != IntPtr.Zero) { aumid = Marshal.PtrToStringUni(v.Pointer); }
                    PropVariantClear(ref v);
                }
                if (string.IsNullOrEmpty(aumid)) { return false; }
                var clsidKey = new PropertyKey { FormatId = AppUserModelProps, PropertyId = 26 };
                if (store.GetValue(ref clsidKey, out PropVariant c) == 0)
                {
                    if (c.Type == VT_CLSID && c.Pointer != IntPtr.Zero) { clsid = Marshal.PtrToStructure<Guid>(c.Pointer); }
                    PropVariantClear(ref c);
                }
                return clsid != Guid.Empty;
            }
            catch { return false; }
            finally { if (store != null) { try { Marshal.ReleaseComObject(store); } catch { } } }
        }

        private static Guid? FromRegistry(string aumid)
        {
            // A registry key name cannot hold a backslash; such an id (an exe path) has no entry here.
            if (aumid.IndexOf('\\') >= 0) { return null; }
            foreach (RegistryKey hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using (RegistryKey k = hive.OpenSubKey(@"Software\Classes\AppUserModelId\" + aumid))
                {
                    if (k?.GetValue("CustomActivator") is string s && Guid.TryParse(s, out Guid g) && g != Guid.Empty) { return g; }
                }
            }
            return null;
        }

        private static Guid? FromPackage(string aumid)
        {
            int bang = aumid.IndexOf('!');
            if (bang <= 0) { return null; }
            string family = aumid.Substring(0, bang);
            // A package family name is "<name>_<publisher id>".
            if (family.IndexOf('_') < 0 || family.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { return null; }
            lock (Lock) { if (_packages.TryGetValue(family, out Guid? known)) { return known; } }

            Guid? found = null;
            try
            {
                var manager = new Windows.Management.Deployment.PackageManager();
                foreach (var package in manager.FindPackagesForUser("", family))
                {
                    string manifest = Path.Combine(package.InstalledLocation.Path, "AppxManifest.xml");
                    found = ActivatorInManifest(manifest);
                    if (found.HasValue) { break; }
                }
            }
            catch (Exception ex) { Logger.Swallow("ToastActivator.FromPackage", ex); }
            lock (Lock) { _packages[family] = found; }
            return found;
        }

        /// <summary>The ToastActivatorCLSID a package manifest declares (desktop:ToastNotificationActivation), or null.</summary>
        internal static Guid? ActivatorInManifest(string manifest)
        {
            if (!File.Exists(manifest)) { return null; }
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using (XmlReader r = XmlReader.Create(manifest, settings))
            {
                while (r.Read())
                {
                    if (r.NodeType == XmlNodeType.Element && r.LocalName == "ToastNotificationActivation"
                        && Guid.TryParse(r.GetAttribute("ToastActivatorCLSID"), out Guid g) && g != Guid.Empty)
                    {
                        return g;
                    }
                }
            }
            return null;
        }

        // ── the click ────────────────────────────────────────────────────────────────

        [ComImport, Guid("53E31837-6600-4A81-9395-75CFFE746F94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface INotificationActivationCallback
        {
            [PreserveSig]
            int Activate([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
                         [MarshalAs(UnmanagedType.LPWStr)] string invokedArgs,
                         IntPtr data, uint count);
        }

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid,
                                                   [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [DllImport("ole32.dll")]
        private static extern int CoAllowSetForegroundWindow([MarshalAs(UnmanagedType.IUnknown)] object instance, IntPtr reserved);
        private const uint CLSCTX_LOCAL_SERVER = 0x4;

        /// <summary>
        /// Hands <paramref name="args"/> (the toast's launch arguments, or a button's) to the app's activator, exactly as
        /// Windows does on a click. Blocking — the activator may have to start first — so call it off the UI thread.
        /// </summary>
        public static bool Activate(Guid clsid, string aumid, string args, out string error)
        {
            error = null;
            object server = null;
            // The input array is a [ref] pointer in the interface: never null, even with nothing in it.
            IntPtr noInput = Marshal.AllocCoTaskMem(2 * IntPtr.Size);
            try
            {
                for (int i = 0; i < 2 * IntPtr.Size; i++) { Marshal.WriteByte(noInput, i, 0); }
                Guid iid = typeof(INotificationActivationCallback).GUID;
                int hr = CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_LOCAL_SERVER, ref iid, out server);
                if (hr < 0 || server == null)
                {
                    error = "its handler would not start (0x" + hr.ToString("X8") + ")";
                    Fail(error);
                    return false;
                }
                // Let it come to the front, as Windows lets the app it activates; without this its window
                // only flashes on the taskbar.
                try { CoAllowSetForegroundWindow(server, IntPtr.Zero); } catch { }
                hr = ((INotificationActivationCallback)server).Activate(aumid ?? "", args ?? "", noInput, 0);
                if (hr < 0)
                {
                    error = "its handler refused (0x" + hr.ToString("X8") + ")";
                    Fail(error);
                    return false;
                }
                Interlocked.Increment(ref _handled);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Fail(error);
                return false;
            }
            finally
            {
                if (server != null) { try { Marshal.ReleaseComObject(server); } catch { } }
                Marshal.FreeCoTaskMem(noInput);
            }
        }

        private static void Fail(string why)
        {
            Interlocked.Increment(ref _failed);
            _lastError = why;
        }

        // Schemes never opened from a notification, whatever the app asked: they run files and scripts, or are the
        // usual vehicles for that (Windows' own protocol launches refuse most of them too).
        private static readonly HashSet<string> NeverOpened = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "file", "javascript", "vbscript", "data", "about", "res", "mk", "mhtml", "its", "ms-its", "ms-itss",
            "hcp", "help", "search", "search-ms", "ms-search", "shell", "ms-msdt", "ms-officecmd", "ms-appinstaller",
            "ms-cxh", "ms-cxh-full", "jar",
        };

        /// <summary>
        /// Whether a toast's protocol launch (unigetui://…, nvidiaapp://…) is one Tempo opens: an address an app on
        /// this PC registered to open, and not a scheme that runs files or scripts. <paramref name="why"/> says why not.
        /// </summary>
        public static bool ProtocolAllowed(string address, out string why)
        {
            why = null;
            address = (address ?? "").Trim();
            if (address.Length == 0 || address.Length > 8192 || !Uri.TryCreate(address, UriKind.Absolute, out Uri u))
            {
                why = "not an address";
                return false;
            }
            string scheme = u.Scheme;
            if (NeverOpened.Contains(scheme) || u.IsUnc) { why = scheme + ": is never opened from a notification"; return false; }
            if (scheme == Uri.UriSchemeHttp || scheme == Uri.UriSchemeHttps || scheme == Uri.UriSchemeMailto) { return true; }
            try
            {
                using (RegistryKey k = Registry.ClassesRoot.OpenSubKey(scheme))
                {
                    if (k == null || k.GetValue("URL Protocol") == null) { why = "no app on this PC opens " + scheme + ":"; return false; }
                }
            }
            catch { why = "could not check " + scheme + ":"; return false; }
            return true;
        }

        /// <summary>Opens a toast's protocol launch the way Windows would, if <see cref="ProtocolAllowed"/> says so.</summary>
        public static bool OpenProtocol(string address, out string error)
        {
            if (!ProtocolAllowed(address, out error)) { return false; }
            try
            {
                Process.Start(new ProcessStartInfo { FileName = address.Trim(), UseShellExecute = true });
                Interlocked.Increment(ref _protocols);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Fail(error);
                return false;
            }
        }

        /// <summary>For the tests.</summary>
        internal static void ResetForTest()
        {
            lock (Lock) { _found.Clear(); _packages.Clear(); }
            _handled = _failed = _protocols = 0;
            _lastError = null;
        }
    }
}
