using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Launches an application by its AppUserModelID — the same identity Windows uses to
    /// activate a notification's source app when you click one of its toasts. Works for
    /// Store/UWP apps and for desktop apps that registered an AUMID (most notification
    /// senders, including the browsers). Used to make Tempo's mirrored notification cards
    /// clickable: click the card, the app it came from opens.
    ///
    /// Built on the documented shell interface <c>IApplicationActivationManager</c>
    /// (CLSID ApplicationActivationManager). If the app can't be activated by AUMID the
    /// call simply returns false — the card still dismisses.
    /// </summary>
    public static class AppActivator
    {
        [Flags]
        private enum ActivateOptions
        {
            None = 0,
            DesignMode = 1,
            NoErrorUI = 2,
            NoSplashScreen = 4
        }

        [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IApplicationActivationManager
        {
            // Vtable order matters: ActivateApplication, then ActivateForFile, then
            // ActivateForProtocol (not declared — we never call it).
            [PreserveSig]
            int ActivateApplication(
                [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
                [MarshalAs(UnmanagedType.LPWStr)] string arguments,
                ActivateOptions options,
                out uint processId);

            [PreserveSig]
            int ActivateForFile(
                [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
                [MarshalAs(UnmanagedType.Interface)] object itemArray,   // IShellItemArray
                [MarshalAs(UnmanagedType.LPWStr)] string verb,
                out uint processId);
        }

        [ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
        private class ApplicationActivationManager { }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            string pszPath, IntPtr pbc, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);

        [DllImport("shell32.dll", PreserveSig = false)]
        private static extern void SHCreateShellItemArrayFromShellItem(
            [MarshalAs(UnmanagedType.Interface)] object psi, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);

        private static readonly Guid IID_IShellItem =
            new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
        private static readonly Guid IID_IShellItemArray =
            new Guid("b63ea76d-1f85-456f-a19c-48159efa858b");

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHELLEXECUTEINFO
        {
            public int cbSize;
            public uint fMask;
            public IntPtr hwnd;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpVerb;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpFile;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpParameters;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpDirectory;
            public int nShow;
            public IntPtr hInstApp;
            public IntPtr lpIDList;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpClass;
            public IntPtr hkeyClass;
            public uint dwHotKey;
            public IntPtr hIcon;
            public IntPtr hProcess;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);
        private const uint SEE_MASK_CLASSNAME = 0x00000001;
        private const uint SEE_MASK_NOASYNC = 0x00000100;

        /// <summary>
        /// Finds the file-association ProgID that a given app registered for an extension,
        /// by matching the AppUserModelID recorded under it. This is how a packaged app
        /// like Snipping Tool appears in Explorer's "Open with" for .png even though it
        /// declines direct file ACTIVATION. Nothing is hard-coded: the ProgID is looked up
        /// live, so it keeps working across Windows versions and for any capture tool.
        /// </summary>
        private static string FindProgIdForAumid(string extension, string aumid)
        {
            try
            {
                using (var list = Microsoft.Win32.Registry.ClassesRoot
                           .OpenSubKey(extension + "\\OpenWithProgids", false))
                {
                    if (list == null) { return null; }
                    foreach (string progId in list.GetValueNames())
                    {
                        if (string.IsNullOrEmpty(progId)) { continue; }
                        using (var app = Microsoft.Win32.Registry.ClassesRoot
                                   .OpenSubKey(progId + "\\Application", false))
                        {
                            string id = app?.GetValue("AppUserModelID") as string;
                            if (!string.IsNullOrEmpty(id) &&
                                string.Equals(id, aumid, StringComparison.OrdinalIgnoreCase))
                            {
                                return progId;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Logger.Swallow("AppActivator.FindProgId", ex); }
            return null;
        }

        /// <summary>
        /// Opens <paramref name="path"/> with the app registered under
        /// <paramref name="progId"/> (Explorer's own "Open with" mechanism).
        /// </summary>
        private static bool OpenWithProgId(string path, string progId)
        {
            try
            {
                var info = new SHELLEXECUTEINFO();
                info.cbSize = Marshal.SizeOf(typeof(SHELLEXECUTEINFO));
                info.fMask = SEE_MASK_CLASSNAME | SEE_MASK_NOASYNC;
                info.lpVerb = "open";
                info.lpFile = path;
                info.lpClass = progId;
                info.nShow = 1;   // SW_SHOWNORMAL
                return ShellExecuteEx(ref info);
            }
            catch (Exception ex)
            {
                Logger.Swallow("AppActivator.OpenWithProgId", ex);
                return false;
            }
        }

        /// <summary>
        /// Opens <paramref name="path"/> INSIDE the app with the given AppUserModelID —
        /// e.g. a screenshot back in Snipping Tool rather than whatever app happens to own
        /// .png (Photos). This is what Windows itself does when you click a source app's
        /// toast. Two routes are tried, because packaged apps differ: the file association
        /// that app registered for the extension (Snipping Tool's route), then direct file
        /// activation. Returns false only if BOTH fail, so the caller can fall back to the
        /// default handler.
        /// </summary>
        public static bool OpenFileWithAumid(string appUserModelId, string path)
        {
            if (string.IsNullOrWhiteSpace(appUserModelId) || string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            // Route 1: the app's own file association. Snipping Tool DECLINES file
            // activation (hr 0x80270254) but does register an "Open with" ProgID, so this
            // is the route that actually shows the shot in its editor.
            try
            {
                string ext = System.IO.Path.GetExtension(path);
                if (!string.IsNullOrEmpty(ext))
                {
                    string progId = FindProgIdForAumid(ext, appUserModelId);
                    if (progId != null && OpenWithProgId(path, progId))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex) { Logger.Swallow("AppActivator.OpenViaProgId", ex); }

            // Route 2: direct file activation, for apps that do declare it.
            try
            {
                Guid iidItem = IID_IShellItem;
                SHCreateItemFromParsingName(path, IntPtr.Zero, ref iidItem, out object item);
                if (item == null) { return false; }

                Guid iidArray = IID_IShellItemArray;
                SHCreateShellItemArrayFromShellItem(item, ref iidArray, out object arr);
                if (arr == null) { return false; }

                var mgr = (IApplicationActivationManager)new ApplicationActivationManager();
                int hr = mgr.ActivateForFile(appUserModelId, arr, "Open", out _);
                if (hr < 0)
                {
                    Logger.Info("[Notify] " + appUserModelId + " refused the file (hr=0x"
                                + hr.ToString("X8") + ") — falling back to the default app.");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Swallow("AppActivator.OpenFileWithAumid", ex);
                return false;
            }
        }

        /// <summary>
        /// Brings the app with the given AppUserModelID to the foreground (launching it
        /// if needed). Returns true on success.
        /// </summary>
        public static bool LaunchByAumid(string appUserModelId, string arguments = "")
        {
            if (string.IsNullOrWhiteSpace(appUserModelId)) { return false; }
            try
            {
                var mgr = (IApplicationActivationManager)new ApplicationActivationManager();
                int hr = mgr.ActivateApplication(appUserModelId, arguments ?? "",
                                                 ActivateOptions.NoErrorUI, out _);
                return hr >= 0;   // S_OK (and other success codes) are >= 0
            }
            catch (Exception ex)
            {
                Logger.Swallow("AppActivator", ex);
                return false;
            }
        }

        // First http/https link in a piece of notification text. Windows does NOT expose
        // a mirrored notification's real click-target (deep link) to listener apps, so a
        // browser notification clicked through Tempo would only re-open the browser to an
        // empty tab. When the notification's own text carries a link, opening THAT is the
        // real redirect the user expected.
        private static readonly Regex UrlRx =
            new Regex(@"https?://[^\s""'<>\)\]]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Extracts the first sane http/https URL from text, or null.</summary>
        public static string FindUrl(string text)
        {
            if (string.IsNullOrEmpty(text)) { return null; }
            var m = UrlRx.Match(text);
            if (!m.Success) { return null; }
            // Trim trailing sentence punctuation the regex may have swept up.
            string url = m.Value.TrimEnd('.', ',', '!', '?', ';', ':', '"', '\'');
            if (url.Length < 8 || url.Length > 2048) { return null; }
            return url;
        }

        /// <summary>
        /// True when the notification came from a web browser. For a browser, a click
        /// that carries no link in its text has nowhere useful to go — activating the
        /// browser just opens a blank new tab (which reads as broken) because Windows
        /// does NOT expose a web-push notification's real destination to listener apps.
        /// So those cards are made non-clickable instead. Non-browser apps (Discord,
        /// Slack, games…) are still worth activating.
        /// </summary>
        public static bool LooksLikeBrowser(string appName, string aumid)
        {
            string s = ((appName ?? "") + " " + (aumid ?? "")).ToLowerInvariant();
            return s.Contains("edge") || s.Contains("chrome") || s.Contains("chromium")
                || s.Contains("firefox") || s.Contains("mozilla") || s.Contains("opera")
                || s.Contains("brave") || s.Contains("vivaldi");
        }

        /// <summary>
        /// Builds the click action for a mirrored notification card — as close to the
        /// Windows 11 "click to open" behaviour as Windows lets a listener app get:
        ///  • a link found in the notification's text → open THAT (a real redirect);
        ///  • otherwise a non-browser app → activate the app;
        ///  • a browser with no link → null (don't open a blank tab).
        /// Returns null when there's nowhere useful to go, so the card isn't clickable.
        /// Only ever runs from an explicit user click, and only follows http/https links.
        /// </summary>
        public static Action BuildNotificationClickAction(string appName, string title, string body, string aumid)
        {
            string url = FindUrl((title ?? "") + "\n" + (body ?? ""));
            if (url != null)
            {
                return () => { Logger.Info("[Notify] card clicked → opening link " + url); OpenUrl(url); };
            }
            if (!string.IsNullOrWhiteSpace(aumid) && !LooksLikeBrowser(appName, aumid))
            {
                return () => { Logger.Info("[Notify] card clicked → activating app " + aumid); LaunchByAumid(aumid); };
            }
            // Browser web-push with no link in the text: Windows doesn't hand us the
            // destination, so there's nowhere to go — don't open a blank browser tab.
            return null;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetClipboardOwner();
        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int pid);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetApplicationUserModelId(IntPtr hProcess, ref uint len,
            System.Text.StringBuilder id);
        private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        /// <summary>
        /// Identifies the app that put the current image on the clipboard — i.e. the tool
        /// that just took the screenshot — and returns its AppUserModelID and a friendly
        /// name. Asking Windows directly (via the clipboard OWNER) is far more reliable
        /// than waiting for that app's notification to arrive and reading the source from
        /// it: the notification can be slow, can be turned off for that app, or the user
        /// can click the card before it lands — and in every one of those cases the card
        /// fell back to whatever program owns .png, which is why a screenshot opened in
        /// Photos instead of Snipping Tool. This is known the instant the image appears.
        /// Returns false when the owner can't be resolved (then the caller falls back).
        /// </summary>
        public static bool TryGetClipboardOwnerApp(out string aumid, out string friendlyName,
                                                   out System.Drawing.Image icon)
        {
            aumid = null;
            friendlyName = null;
            icon = null;
            IntPtr h = IntPtr.Zero;
            try
            {
                IntPtr hwnd = GetClipboardOwner();
                if (hwnd == IntPtr.Zero) { return false; }
                GetWindowThreadProcessId(hwnd, out int pid);
                if (pid <= 0) { return false; }

                using (var proc = Process.GetProcessById(pid))
                {
                    // A readable product name for the card ("Snipping Tool"), falling
                    // back to the process name.
                    try
                    {
                        string exe = proc.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(exe))
                        {
                            var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe);
                            friendlyName = !string.IsNullOrWhiteSpace(fvi.FileDescription)
                                ? fvi.FileDescription.Trim()
                                : proc.ProcessName;

                            // The app's REAL icon, straight off its executable. Without
                            // this a screenshot card wore Tempo's own logo until (and
                            // unless) the capture app's notification arrived with one —
                            // so the card claimed "Snipping Tool" beside Tempo's icon.
                            icon = ExtractExeIcon(exe);
                        }
                    }
                    catch { /* packaged apps can refuse MainModule — name below */ }
                    if (string.IsNullOrWhiteSpace(friendlyName)) { friendlyName = proc.ProcessName; }
                    friendlyName = PrettifyAppName(friendlyName);

                    // The AUMID lets the file be opened back INSIDE that app.
                    h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                    if (h != IntPtr.Zero)
                    {
                        uint len = 260;
                        var sb = new System.Text.StringBuilder((int)len);
                        if (GetApplicationUserModelId(h, ref len, sb) == 0 && sb.Length > 0)
                        {
                            aumid = sb.ToString();
                        }
                    }
                }
                return !string.IsNullOrEmpty(aumid) || !string.IsNullOrEmpty(friendlyName);
            }
            catch (Exception ex)
            {
                Logger.Swallow("ClipboardOwnerApp", ex);
                return false;
            }
            finally
            {
                if (h != IntPtr.Zero) { try { CloseHandle(h); } catch { } }
            }
        }

        /// <summary>
        /// The real icon of the app a notification came from, for cards whose source is a
        /// classic desktop program.
        ///
        /// Windows only hands the notification listener a logo through
        /// AppInfo.DisplayInfo.GetLogo, and that is a PACKAGED-app facility: Store/UWP
        /// apps have a logo in their manifest, ordinary Win32 programs — Discord, Chrome,
        /// Steam, Telegram, most of what actually notifies people — have nothing there
        /// and it returns null. Every one of those cards therefore fell back to Tempo's
        /// own logo, so a Discord message arrived wearing Tempo's icon.
        ///
        /// This finds the icon the other way round: locate the running process that owns
        /// the notification's AUMID and take the icon off its executable. Matching on the
        /// AUMID is exact; the app NAME is only used as a second pass, because two
        /// processes can share a name but the shell identity is unique.
        ///
        /// Returns null if nothing matches — the caller keeps its own fallback.
        /// </summary>
        public static System.Drawing.Image TryGetIconForApp(string aumid, string appName)
        {
            if (string.IsNullOrWhiteSpace(aumid) && string.IsNullOrWhiteSpace(appName))
            {
                return null;
            }

            try
            {
                Process[] all = Process.GetProcesses();
                try
                {
                    // Pass 1 — exact AUMID match. Costs an OpenProcess per candidate, so
                    // only processes that could plausibly own a window are considered.
                    if (!string.IsNullOrWhiteSpace(aumid))
                    {
                        foreach (Process p in all)
                        {
                            string got = AumidOf(p);
                            if (!string.IsNullOrEmpty(got) &&
                                string.Equals(got, aumid, StringComparison.OrdinalIgnoreCase))
                            {
                                System.Drawing.Image icon = IconOfProcess(p);
                                if (icon != null) { return icon; }
                            }
                        }
                    }

                    // Pass 2 — the display name the notification carried, matched against
                    // the executable's description or its process name ("Discord").
                    if (!string.IsNullOrWhiteSpace(appName))
                    {
                        string want = appName.Trim();
                        foreach (Process p in all)
                        {
                            string exe = ExePathOf(p);
                            if (string.IsNullOrEmpty(exe)) { continue; }

                            string desc = null;
                            try { desc = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).FileDescription; }
                            catch { }

                            bool hit =
                                string.Equals(p.ProcessName, want, StringComparison.OrdinalIgnoreCase) ||
                                (!string.IsNullOrWhiteSpace(desc) &&
                                 desc.Trim().StartsWith(want, StringComparison.OrdinalIgnoreCase));

                            if (hit)
                            {
                                System.Drawing.Image icon = ExtractExeIcon(exe);
                                if (icon != null) { return icon; }
                            }
                        }
                    }
                }
                finally
                {
                    foreach (Process p in all) { try { p.Dispose(); } catch { } }
                }
            }
            catch (Exception ex)
            {
                Logger.Swallow("IconForApp", ex);
            }

            // Pass 3 — ask the SHELL for the app's icon by its AUMID.
            //
            // Both passes above can only find an app that is RUNNING RIGHT NOW: they walk
            // Process.GetProcesses() and read the icon off a live executable. That is the
            // wrong requirement for a notification, because a notification routinely
            // outlives the thing that sent it — an installer that finished, a chat client
            // that was closed after the message arrived, anything scheduled. Those cards
            // got no logo at all and fell back to a generic glyph.
            //
            // The AppsFolder is the shell namespace behind the Start menu's app list, and
            // an AUMID is exactly the key into it, so this resolves an icon for anything
            // INSTALLED whether or not it happens to be running — and for packaged and
            // ordinary desktop apps alike. It is the same path Explorer uses to draw those
            // icons, which is what makes the cards look like every other app's.
            try
            {
                Image shell = TryGetIconFromAppsFolder(aumid);
                if (shell != null) { return shell; }
            }
            catch (Exception ex)
            {
                Logger.Swallow("IconFromAppsFolder", ex);
            }

            return null;
        }

        // ── Shell icon lookup by AUMID (AppsFolder) ──────────────────────────

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHParseDisplayName(string pszName, IntPtr pbc,
            out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHGetFileInfoW")]
        private static extern IntPtr SHGetFileInfoPidl(IntPtr pidl, int fileAttrs,
            ref SHFILEINFO psfi, int cbFileInfo, uint flags);

        [DllImport("shell32.dll")]
        private static extern void ILFree(IntPtr pidl);

        private const uint SHGFI_PIDL = 0x000000008;

        /// <summary>
        /// The icon the Start menu draws for this AUMID. Null when the AUMID isn't a known
        /// installed app — web-push senders and some browser notifications carry one that
        /// was never registered in the AppsFolder.
        ///
        /// WHY NOT IShellItemImageFactory, the obvious API for this: it extracts
        /// ASYNCHRONOUSLY and answers E_PENDING (0x8000000A) until the shell has finished,
        /// which it will not do on a thread that is not pumping messages. Measured here —
        /// every valid AUMID returned E_PENDING, and it kept returning it through twenty
        /// retries over half a second, so an icon would simply never have appeared. That
        /// matters because this runs on the notification mirror's background thread.
        ///
        /// SHGetFileInfo against the item's PIDL is synchronous, needs no apartment, and
        /// hands back an HICON whose alpha survives. Verified on this machine against
        /// Notepad and Calculator (packaged) and Discord and Chrome (ordinary desktop
        /// apps), from BOTH an STA and an MTA thread, all four with transparency intact.
        /// </summary>
        private static Image TryGetIconFromAppsFolder(string aumid)
        {
            if (string.IsNullOrWhiteSpace(aumid)) { return null; }

            IntPtr pidl = IntPtr.Zero;
            IntPtr hIcon = IntPtr.Zero;
            try
            {
                if (SHParseDisplayName(@"shell:AppsFolder\" + aumid, IntPtr.Zero,
                                       out pidl, 0, out _) != 0 || pidl == IntPtr.Zero)
                {
                    return null;      // not an installed app — expected, not an error
                }

                var info = new SHFILEINFO();
                if (SHGetFileInfoPidl(pidl, 0, ref info, Marshal.SizeOf(typeof(SHFILEINFO)),
                                      SHGFI_PIDL | SHGFI_ICON | SHGFI_LARGEICON) == IntPtr.Zero)
                {
                    return null;
                }

                hIcon = info.hIcon;
                if (hIcon == IntPtr.Zero) { return null; }
                using (var ico = System.Drawing.Icon.FromHandle(hIcon))
                {
                    return ico.ToBitmap();   // independent copy; the handle is freed below
                }
            }
            finally
            {
                if (hIcon != IntPtr.Zero) { try { DestroyIcon(hIcon); } catch { } }
                if (pidl != IntPtr.Zero) { try { ILFree(pidl); } catch { } }
            }
        }

        private static string AumidOf(Process p)
        {
            IntPtr h = IntPtr.Zero;
            try
            {
                h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, p.Id);
                if (h == IntPtr.Zero) { return null; }
                uint len = 260;
                var sb = new System.Text.StringBuilder((int)len);
                return GetApplicationUserModelId(h, ref len, sb) == 0 && sb.Length > 0
                    ? sb.ToString()
                    : null;
            }
            catch { return null; }
            finally { if (h != IntPtr.Zero) { try { CloseHandle(h); } catch { } } }
        }

        private static string ExePathOf(Process p)
        {
            try { return p.MainModule?.FileName; }
            catch { return null; }   // protected/packaged processes refuse MainModule
        }

        private static System.Drawing.Image IconOfProcess(Process p)
        {
            string exe = ExePathOf(p);
            return string.IsNullOrEmpty(exe) ? null : ExtractExeIcon(exe);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string path, int fileAttrs,
            ref SHFILEINFO psfi, int cbSizeFileInfo, uint flags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);
        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_LARGEICON = 0x000000000;

        /// <summary>
        /// The icon an executable shows in Explorer, as an independent bitmap the caller
        /// owns. Uses the shell (SHGetFileInfo) rather than ExtractAssociatedIcon so the
        /// properly-composed shell icon is used, and always frees the native handle.
        /// Returns null when the file has no icon or can't be read.
        /// </summary>
        private static System.Drawing.Image ExtractExeIcon(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath)) { return null; }
            var info = new SHFILEINFO();
            IntPtr hIcon = IntPtr.Zero;
            try
            {
                SHGetFileInfo(exePath, 0, ref info, Marshal.SizeOf(typeof(SHFILEINFO)),
                              SHGFI_ICON | SHGFI_LARGEICON);
                hIcon = info.hIcon;
                if (hIcon == IntPtr.Zero) { return null; }
                using (var ico = System.Drawing.Icon.FromHandle(hIcon))
                {
                    return ico.ToBitmap();   // independent copy; the handle is freed below
                }
            }
            catch (Exception ex)
            {
                Logger.Swallow("ExtractExeIcon", ex);
                return null;
            }
            finally
            {
                if (hIcon != IntPtr.Zero) { try { DestroyIcon(hIcon); } catch { } }
            }
        }

        /// <summary>
        /// Turns a raw executable/description string into something fit for a card:
        /// "SnippingTool.exe" → "Snipping Tool". Some apps set a poor FileDescription, so
        /// the instant label would otherwise show the file name until the app's own
        /// notification arrived with a nicer one.
        /// </summary>
        private static string PrettifyAppName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) { return name; }
            string s2 = name.Trim();
            if (s2.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                s2 = s2.Substring(0, s2.Length - 4);
            }
            // Split CamelCase into words, but leave names that already have spaces alone.
            if (!s2.Contains(" "))
            {
                var sb = new System.Text.StringBuilder(s2.Length + 4);
                for (int i = 0; i < s2.Length; i++)
                {
                    if (i > 0 && char.IsUpper(s2[i]) && !char.IsUpper(s2[i - 1]))
                    {
                        sb.Append(' ');
                    }
                    sb.Append(s2[i]);
                }
                s2 = sb.ToString();
            }
            return s2;
        }

        /// <summary>Opens a URL in the user's default browser.</summary>
        public static bool OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) { return false; }
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                return true;
            }
            catch (Exception ex)
            {
                Logger.Swallow("AppActivator.OpenUrl", ex);
                return false;
            }
        }
    }
}
