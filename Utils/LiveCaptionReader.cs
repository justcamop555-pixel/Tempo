using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Bridges Windows Live Captions into Tempo's own caption overlay.
    ///
    /// This is the WINDOWS-MIRROR caption source: it finds the Windows "Live
    /// Captions" window (LiveCaptions.exe), reads the transcribed text out of its
    /// UI Automation tree, and can shove that window off-screen so only Tempo's
    /// clean overlay bar is visible. Windows does the speech-to-text here.
    ///
    /// Tempo also has its OWN offline speech engine (see TempoTranscriber, which
    /// uses Whisper on captured system audio); the Auto caption source tries that
    /// first and falls back to this Windows mirror when it can't run. This class is
    /// only the mirror half.
    ///
    /// It is deliberately defensive: Live Captions is an OS component whose
    /// internals aren't a public contract, so every lookup is best-effort and
    /// failures are swallowed - the worst case is simply "no mirrored text",
    /// never a crash.
    /// </summary>
    public sealed class LiveCaptionReader
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_MINIMIZE = 6;
        private const int SW_HIDE = 0;
        private const int SW_SHOWNOACTIVATE = 4;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        // The Live Captions top-level window. Class name has been stable across
        // Windows 11 builds; the visible title is "Live Captions".
        // Verified on Windows 11 via the in-app diagnostic: the captions window reports
        // class "LiveCaptionsDesktopWindow" and title "Live Captions" (owned by the
        // LiveCaptions process). The old "LiveCaptionsWindow" guess never matched.
        private const string LiveCaptionsClass = "LiveCaptionsDesktopWindow";
        private const string LiveCaptionsTitle = "Live Captions";

        private IntPtr _hwnd = IntPtr.Zero;
        private RECT _originalRect;
        private bool _haveOriginalRect;
        private bool _movedOffscreen;
        // The specific caption-text element found by the last full tree walk. Re-reading
        // just this element each poll avoids re-walking the whole UIA tree (dozens of slow
        // cross-process COM calls) ~every tick. It's re-validated with an occasional full
        // walk and dropped whenever it stops yielding text or throws.
        private AutomationElement _cachedTextEl;
        private int _cacheReadsSinceWalk;
        private const int CacheReadsBeforeRewalk = 12;
        // Set true if UI Automation's core fails to initialize on this PC (the
        // "type initializer for CacheRequest threw an exception" condition). When
        // this happens, NO UIA reading can work here, so the Windows-mirror caption
        // path is impossible and the caller should fall back to Tempo's own engine.
        private volatile bool _uiaBroken;

        /// <summary>
        /// True if UI Automation itself is broken on this machine, so the Windows
        /// Live Captions mirror can never read text here. Detected on first use.
        /// </summary>
        public bool UiaBroken => _uiaBroken;

        /// <summary>True once a Live Captions window has been located.</summary>
        public bool Found => _hwnd != IntPtr.Zero && IsWindow(_hwnd);

        /// <summary>
        /// Tries to (re)locate the Windows Live Captions window. Returns true if a
        /// window handle is currently held.
        /// </summary>
        public bool Locate()
        {
            if (Found)
            {
                return true;
            }

            // Try class name first, then the window title, then known alternates.
            IntPtr h = FindWindow(LiveCaptionsClass, null);
            if (h == IntPtr.Zero) h = FindWindow(null, LiveCaptionsTitle);
            if (h == IntPtr.Zero) h = FindWindow(null, "Live Captions");

            // If those missed (class/title vary by build/locale), find a visible
            // top-level window owned by the LiveCaptions process.
            if (h == IntPtr.Zero)
            {
                h = FindWindowByProcessName("LiveCaptions");
            }

            // Last resort, and important on Windows 11 where the captions window is
            // often NOT owned by the LiveCaptions.exe process we can see in Task
            // Manager (so the process lookup above finds nothing): scan every visible
            // top-level window for one whose title looks like Live Captions. Our own
            // windows are excluded so we can never hide Tempo's bar by mistake.
            if (h == IntPtr.Zero)
            {
                h = FindWindowByTitleContains("live caption");
            }

            // Final fallback: ask UI Automation. This is the one most likely to succeed on
            // Windows 11, where the captions window has no stable Win32 class/title and may
            // be hosted outside the LiveCaptions.exe process. (Instance variant so it can
            // flag a broken-UIA machine, where this whole path is impossible.)
            if (h == IntPtr.Zero)
            {
                h = LocateViaAutomationInstance();
            }

            if (h != IntPtr.Zero)
            {
                _hwnd = h;
                // Do NOT seed the "original" position here. If a previous Tempo session was
                // force-killed while the bar was parked off-screen, the window can still be
                // at -32000 when we first find it; capturing that as the restore target
                // would put the user's Live Captions back off-screen forever. Only
                // HideWindowsBar records the original rect, and only when it catches the
                // window clearly ON-screen — so the restore target is always a real spot.
                _haveOriginalRect = false;
                _movedOffscreen = false;
                return true;
            }
            return false;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        /// <summary>Finds the most likely Live Captions window owned by the named process.</summary>
        private static IntPtr FindWindowByProcessName(string processName)
        {
            uint[] pids;
            IntPtr mainHandle = IntPtr.Zero;
            try
            {
                Process[] procs = Process.GetProcessesByName(processName);
                pids = new uint[procs.Length];
                for (int i = 0; i < procs.Length; i++)
                {
                    try { pids[i] = (uint)procs[i].Id; } catch { }
                    // The process's own main window handle is the most reliable pointer to
                    // the caption bar; prefer it whenever the OS exposes a visible one.
                    try
                    {
                        if (mainHandle == IntPtr.Zero)
                        {
                            IntPtr mh = procs[i].MainWindowHandle;
                            if (mh != IntPtr.Zero && IsWindowVisible(mh)) mainHandle = mh;
                        }
                    }
                    catch { }
                    try { procs[i].Dispose(); } catch { }
                }
            }
            catch { return IntPtr.Zero; }

            if (mainHandle != IntPtr.Zero) return mainHandle;
            if (pids.Length == 0) return IntPtr.Zero;

            // Fallback: the LARGEST visible top-level window owned by the process. Picking
            // the biggest (rather than the first match) skips tiny helper/tooltip windows
            // that "first visible window" could otherwise grab and try to hide.
            IntPtr best = IntPtr.Zero;
            long bestArea = 0;
            try
            {
                EnumWindows((hWnd, l) =>
                {
                    if (!IsWindowVisible(hWnd)) return true;
                    GetWindowThreadProcessId(hWnd, out uint wpid);
                    foreach (uint pid in pids)
                    {
                        if (pid != 0 && wpid == pid)
                        {
                            if (GetWindowRect(hWnd, out RECT r))
                            {
                                long area = (long)Math.Max(0, r.Right - r.Left)
                                          * Math.Max(0, r.Bottom - r.Top);
                                if (area > bestArea) { bestArea = area; best = hWnd; }
                            }
                            break;
                        }
                    }
                    return true; // keep scanning for the largest
                }, IntPtr.Zero);
            }
            catch { }
            return best;
        }

        /// <summary>
        /// Finds the largest visible top-level window whose title contains the given text
        /// (case-insensitive), excluding our own process so Tempo's overlay is never a
        /// match. Process-independent, which is what makes it work on Windows 11 where the
        /// captions window isn't owned by the LiveCaptions.exe process.
        /// </summary>
        private static IntPtr FindWindowByTitleContains(string needle)
        {
            if (string.IsNullOrEmpty(needle)) return IntPtr.Zero;

            uint ownPid = 0;
            try { ownPid = (uint)Process.GetCurrentProcess().Id; } catch { }

            IntPtr best = IntPtr.Zero;
            long bestArea = 0;
            try
            {
                var sb = new StringBuilder(256);
                EnumWindows((hWnd, l) =>
                {
                    if (!IsWindowVisible(hWnd)) return true;
                    GetWindowThreadProcessId(hWnd, out uint wpid);
                    if (ownPid != 0 && wpid == ownPid) return true; // never our own windows
                    sb.Length = 0;
                    if (GetWindowText(hWnd, sb, sb.Capacity) <= 0) return true;
                    if (sb.ToString().IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        return true;
                    }
                    if (GetWindowRect(hWnd, out RECT r))
                    {
                        long area = (long)Math.Max(0, r.Right - r.Left)
                                  * Math.Max(0, r.Bottom - r.Top);
                        if (area > bestArea) { bestArea = area; best = hWnd; }
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
            return best;
        }

        /// <summary>
        /// Finds the Live Captions window through UI Automation rather than Win32. On
        /// Windows 11 the captions window often has no matching Win32 class/title and
        /// isn't owned by the LiveCaptions.exe process, so plain FindWindow/process scans
        /// miss it - but it's always present in the UI Automation tree. Returns its native
        /// window handle, or zero. Excludes our own process so Tempo's bar is never picked.
        /// </summary>
        private IntPtr LocateViaAutomationInstance()
        {
            try
            {
                AutomationElement root = AutomationElement.RootElement;
                if (root == null) return IntPtr.Zero;

                uint ownPid = 0;
                try { ownPid = (uint)Process.GetCurrentProcess().Id; } catch { }

                // TreeWalker over the desktop's direct children (top-level windows),
                // avoiding FindAll/CacheRequest which fails on some PCs.
                TreeWalker walker = TreeWalker.RawViewWalker;
                AutomationElement child = null;
                try { child = walker.GetFirstChild(root); } catch { child = null; }
                while (child != null)
                {
                    try
                    {
                        int pid = 0;
                        try { object pv = child.GetCurrentPropertyValue(AutomationElement.ProcessIdProperty); if (pv is int pi) pid = pi; } catch { }
                        if (!(ownPid != 0 && (uint)pid == ownPid))
                        {
                            string name = "", cls = "";
                            try { name = (child.GetCurrentPropertyValue(AutomationElement.NameProperty) as string) ?? ""; } catch { }
                            try { cls = (child.GetCurrentPropertyValue(AutomationElement.ClassNameProperty) as string) ?? ""; } catch { }
                            if (name.IndexOf("live caption", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                cls.IndexOf("livecaption", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                cls.IndexOf("caption", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                int nh = 0;
                                try { object nhv = child.GetCurrentPropertyValue(AutomationElement.NativeWindowHandleProperty); if (nhv is int nhi) nh = nhi; } catch { }
                                if (nh != 0) return new IntPtr(nh);
                            }
                        }
                    }
                    catch { }
                    AutomationElement next = null;
                    try { next = walker.GetNextSibling(child); } catch { next = null; }
                    child = next;
                }
            }
            catch (TypeInitializationException)
            {
                // UI Automation's core (CacheRequest et al.) failed to initialize on
                // this PC. Nothing UIA-based can work here; flag it so the caption
                // system falls back to Tempo's own engine instead of silently failing.
                _uiaBroken = true;
            }
            catch { }
            return IntPtr.Zero;
        }

        /// <summary>
        /// Diagnostic: lists every visible top-level window (Win32 title/class/process) and
        /// every UI Automation root-child window (name/class/pid). Used to discover how the
        /// Windows Live Captions window actually identifies itself on a given PC, so the
        /// lookups above can be made to match it. Best-effort; never throws.
        /// </summary>
        public string DescribeCandidateWindows()
        {
            var sb = new StringBuilder();

            // Probe UI Automation health up front. If its core can't initialize on this
            // PC (the recurring CacheRequest fault), say so in plain language with the
            // repair steps - because when that's broken, NO app can read Windows Live
            // Captions text, and the rest of the diagnostic will be empty.
            bool uiaOk = false;
            string uiaErr = null;
            try
            {
                var probe = AutomationElement.RootElement;
                uiaOk = probe != null;
            }
            catch (Exception ex)
            {
                uiaErr = ex.GetType().Name + ": " + ex.Message;
            }
            sb.AppendLine("[caption-diag] UI Automation health: " + (uiaOk ? "OK" : "BROKEN"));
            if (!uiaOk)
            {
                sb.AppendLine("  " + (uiaErr ?? "UI Automation root could not be read."));
                sb.AppendLine("  >> Windows' accessibility engine (UI Automation) is broken on this PC.");
                sb.AppendLine("  >> No app can read Windows Live Captions text until it's repaired.");
                sb.AppendLine("  >> Repair (admin terminal): sfc /scannow  then  DISM /Online /Cleanup-Image /RestoreHealth  then reboot.");
                sb.AppendLine("  >> If it returns: reinstall the .NET Desktop Runtime x64, and clear any DEVPATH / COMPLUS_InstallRoot machine env vars.");
                sb.AppendLine("  >> Or use Tempo's own offline captions instead (Settings > Live Captions), which don't need UI Automation.");
            }
            sb.AppendLine();

            sb.AppendLine("[caption-diag] visible top-level windows (title | class | process):");
            try
            {
                var title = new StringBuilder(256);
                var cls = new StringBuilder(256);
                EnumWindows((hWnd, l) =>
                {
                    try
                    {
                        if (!IsWindowVisible(hWnd)) return true;
                        title.Length = 0;
                        cls.Length = 0;
                        GetWindowText(hWnd, title, title.Capacity);
                        GetClassName(hWnd, cls, cls.Capacity);
                        string t = title.ToString();
                        string c = cls.ToString();
                        // Skip titleless utility windows unless the class looks caption-
                        // related, to keep the dump short and readable.
                        if (t.Length == 0 &&
                            c.IndexOf("caption", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            return true;
                        }
                        GetWindowThreadProcessId(hWnd, out uint pid);
                        string proc = "";
                        try { using (var p = Process.GetProcessById((int)pid)) proc = p.ProcessName; }
                        catch { }
                        sb.AppendLine("  '" + t + "' | '" + c + "' | " + proc);
                    }
                    catch { }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception ex) { sb.AppendLine("  (window enum failed: " + ex.Message + ")"); }

            sb.AppendLine("[caption-diag] UIA root child windows (name | class | pid):");
            try
            {
                AutomationElement root = AutomationElement.RootElement;
                if (root != null)
                {
                    // TreeWalker instead of FindAll: FindAll uses CacheRequest, whose static
                    // initializer fails on some PCs, which is why this previously reported a
                    // CacheRequest exception instead of the window list.
                    TreeWalker walker = TreeWalker.RawViewWalker;
                    AutomationElement child = null;
                    try { child = walker.GetFirstChild(root); } catch { child = null; }
                    int guard = 0;
                    while (child != null && guard++ < 200)
                    {
                        try
                        {
                            string nm = "", cls = "";
                            int pid = 0;
                            try { nm = (child.GetCurrentPropertyValue(AutomationElement.NameProperty) as string) ?? ""; } catch { }
                            try { cls = (child.GetCurrentPropertyValue(AutomationElement.ClassNameProperty) as string) ?? ""; } catch { }
                            try { object pv = child.GetCurrentPropertyValue(AutomationElement.ProcessIdProperty); if (pv is int pi) pid = pi; } catch { }
                            sb.AppendLine("  '" + nm + "' | '" + cls + "' | " + pid);
                        }
                        catch { }
                        AutomationElement next = null;
                        try { next = walker.GetNextSibling(child); } catch { next = null; }
                        child = next;
                    }
                }
            }
            catch (Exception ex) { sb.AppendLine("  (uia enum failed: " + ex.Message + ")"); }

            // Deep dump of the captions window's own text tree. THIS is the part that
            // shows why reading may fail: it lists every element under the Live Captions
            // window with its control type, Name, and any Text-pattern content - so we
            // can see exactly which element holds the words on this specific PC.
            sb.AppendLine("[caption-diag] caption window text tree:");
            try
            {
                if (Locate())
                {
                    AutomationElement capRoot = AutomationElement.FromHandle(_hwnd);
                    if (capRoot != null)
                    {
                        DumpTextTree(TreeWalker.RawViewWalker, capRoot, sb, 0);
                    }
                    else
                    {
                        sb.AppendLine("  (FromHandle returned null)");
                    }
                }
                else
                {
                    sb.AppendLine("  (caption window not located - DETECT failed)");
                }
            }
            catch (Exception ex) { sb.AppendLine("  (text-tree dump failed: " + ex.Message + ")"); }

            return sb.ToString();
        }

        // Recursively lists elements under the captions window, showing control type,
        // Name, and Text-pattern content. Used only by the diagnostic.
        private static void DumpTextTree(TreeWalker walker, AutomationElement el,
            StringBuilder sb, int depth)
        {
            if (el == null || depth > 14) return;
            try
            {
                // Use per-property reads (not .Current) so this works even where the
                // CacheRequest type initializer is broken.
                string ctName = "?";
                try
                {
                    object ctObj = el.GetCurrentPropertyValue(AutomationElement.ControlTypeProperty);
                    var ct = ctObj as ControlType;
                    if (ct != null) ctName = ct.ProgrammaticName;
                }
                catch { }
                string nm = "";
                try
                {
                    object nmObj = el.GetCurrentPropertyValue(AutomationElement.NameProperty);
                    nm = (nmObj as string) ?? "";
                }
                catch { }
                string pat = "";
                try
                {
                    if (el.TryGetCurrentPattern(TextPattern.Pattern, out object tp) &&
                        tp is TextPattern textPattern)
                    {
                        pat = textPattern.DocumentRange.GetText(120) ?? "";
                    }
                }
                catch { }
                string indent = new string(' ', 2 + depth * 2);
                string nmShort = nm.Length > 80 ? nm.Substring(0, 80) + "..." : nm;
                string patShort = pat.Length > 80 ? pat.Substring(0, 80) + "..." : pat;
                sb.AppendLine(indent + ctName + " | name='" + nmShort + "'" +
                    (patShort.Length > 0 ? " | text='" + patShort + "'" : ""));
            }
            catch { }

            AutomationElement child = null;
            try { child = walker.GetFirstChild(el); } catch { child = null; }
            int guard = 0;
            while (child != null && guard++ < 60)
            {
                DumpTextTree(walker, child, sb, depth + 1);
                AutomationElement next = null;
                try { next = walker.GetNextSibling(child); } catch { next = null; }
                child = next;
            }
        }

        public string ReadText()
        {
            if (!Locate())
            {
                return null;
            }

            try
            {
                // FAST PATH: re-read only the element the last full walk settled on. The
                // caption text lives in one stable element that updates its content in
                // place, so this returns fresh text without the expensive whole-tree walk.
                // Re-validate with a full walk occasionally, and whenever the cached
                // element stops producing text (the winning element can differ per build).
                AutomationElement cached = _cachedTextEl;
                if (cached != null && _cacheReadsSinceWalk < CacheReadsBeforeRewalk)
                {
                    string quick = CleanOrNull(ReadElementText(cached));
                    if (quick != null)
                    {
                        _cacheReadsSinceWalk++;
                        return quick;
                    }
                    _cachedTextEl = null; // gave nothing usable — force a re-walk
                }

                AutomationElement root = AutomationElement.FromHandle(_hwnd);
                if (root == null)
                {
                    return null;
                }

                // Collect text from EVERY text-bearing element under the window, then
                // pick the best candidate. Windows 11 builds differ in where the caption
                // text lives (Name vs TextPattern, and at varying depths), so casting a
                // wide net and choosing the longest real line is far more reliable than
                // betting on one element.
                string bestText = null;
                int bestLen = -1;
                AutomationElement bestEl = null;
                CollectCaptionText(TreeWalker.RawViewWalker, root, ref bestText, ref bestLen, ref bestEl, 0);

                _cachedTextEl = bestEl;      // cache the winner for the fast path
                _cacheReadsSinceWalk = 0;
                return CleanOrNull(bestText);
            }
            catch (TypeInitializationException)
            {
                // UIA core failed to initialize - reading is impossible on this PC.
                _uiaBroken = true;
                _hwnd = IntPtr.Zero;
                _cachedTextEl = null;
                return null;
            }
            catch
            {
                // UIA throws if the window vanishes mid-read; drop the handle so
                // the next call re-locates.
                _hwnd = IntPtr.Zero;
                _cachedTextEl = null;
                return null;
            }
        }

        /// <summary>
        /// Reads the caption text out of a single element: the TextPattern document range
        /// if present, else the Name property. Returns null if neither yields real text.
        /// Deliberately avoids the AutomationElement.Current bag (broken-CacheRequest PCs).
        /// </summary>
        private static string ReadElementText(AutomationElement el)
        {
            if (el == null)
            {
                return null;
            }
            try
            {
                if (el.TryGetCurrentPattern(TextPattern.Pattern, out object tp) &&
                    tp is TextPattern textPattern)
                {
                    string doc = textPattern.DocumentRange.GetText(-1);
                    if (!string.IsNullOrWhiteSpace(doc))
                    {
                        return doc;
                    }
                }
            }
            catch { }
            try
            {
                object nmObj = el.GetCurrentPropertyValue(AutomationElement.NameProperty);
                string nm = nmObj as string;
                if (!string.IsNullOrWhiteSpace(nm))
                {
                    return nm;
                }
            }
            catch { }
            return null;
        }

        // Walks the captions window and keeps the longest readable text found in any
        // text/document/edit element - checking both Name and TextPattern content. This
        // replaces betting on a single "best" element, which missed captions on builds
        // that expose the words differently.
        //
        // IMPORTANT: this uses GetCurrentPropertyValue / TryGetCurrentPattern and AVOIDS
        // the AutomationElement.Current property bag. On PCs where UIA's CacheRequest
        // type initializer is broken, touching .Current throws (it builds a default
        // CacheRequest internally), which killed reading even when the window was found.
        // The per-property calls below do not go through CacheRequest.
        private static void CollectCaptionText(TreeWalker walker, AutomationElement el,
            ref string bestText, ref int bestLen, ref AutomationElement bestEl, int depth)
        {
            if (el == null || depth > 16) return;
            try
            {
                bool texty = false;
                try
                {
                    object ctObj = el.GetCurrentPropertyValue(AutomationElement.ControlTypeProperty);
                    var ct = ctObj as ControlType;
                    texty = ct == ControlType.Text || ct == ControlType.Document ||
                            ct == ControlType.Edit;
                }
                catch { texty = true; } // if we can't read the type, still try to read text

                if (texty)
                {
                    string candidate = ReadElementText(el);
                    if (candidate != null)
                    {
                        string cleaned = Clean(candidate);
                        if (cleaned != null && cleaned.Length > bestLen)
                        {
                            bestLen = cleaned.Length;
                            bestText = cleaned;
                            bestEl = el; // remember the element so the next poll can re-read just it
                        }
                    }
                }
            }
            catch { }

            AutomationElement child = null;
            try { child = walker.GetFirstChild(el); } catch { child = null; }
            while (child != null)
            {
                CollectCaptionText(walker, child, ref bestText, ref bestLen, ref bestEl, depth + 1);
                AutomationElement next = null;
                try { next = walker.GetNextSibling(child); } catch { next = null; }
                child = next;
            }
        }

        // Like Clean, but returns null for Windows' own "Ready to show live
        // captions..." placeholder line. That placeholder is NOT a real caption, so
        // mirroring it would make Tempo's bar show a misleading line; returning null
        // keeps Tempo in its "listening..." state until actual words arrive.
        private static string CleanOrNull(string s)
        {
            string c = Clean(s);
            if (string.IsNullOrWhiteSpace(c)) return null;
            string low = c.ToLowerInvariant();
            if (low.StartsWith("ready to show live captions") ||
                low.Contains("ready to show live captions") ||
                low == "live captions" ||
                low.StartsWith("turn on live captions"))
            {
                return null;
            }
            return c;
        }

        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            // Collapse whitespace/newlines into single spaces so it fits a one or
            // two line bar nicely.
            var sb = new StringBuilder(s.Length);
            bool lastSpace = false;
            foreach (char c in s)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (!lastSpace) { sb.Append(' '); lastSpace = true; }
                }
                else { sb.Append(c); lastSpace = false; }
            }
            return sb.ToString().Trim();
        }

        /// <summary>
        /// Moves the Windows Live Captions window far off-screen, so only Tempo's
        /// overlay shows. UIA still reads it fine while it's off-screen. Safe to
        /// call repeatedly.
        /// </summary>
        public void HideWindowsBar()
        {
            if (!Locate())
            {
                return;
            }
            try
            {
                if (GetWindowRect(_hwnd, out RECT cur))
                {
                    bool onScreen = cur.Left > -20000 && cur.Top > -20000;

                    // Remember its real spot the first time we catch it on-screen, so it
                    // can be put back when captions are switched off.
                    if (onScreen && !_haveOriginalRect)
                    {
                        _originalRect = cur;
                        _haveOriginalRect = true;
                    }

                    // Shove it off-screen whenever it's on-screen. This is re-checked on
                    // every poll, so if Windows re-docks or re-shows the window (it owns
                    // and manages this window's placement), it gets pushed straight back
                    // off instead of sticking around after a single move - which is why a
                    // one-time move wasn't enough.
                    if (onScreen)
                    {
                        SetWindowPos(_hwnd, IntPtr.Zero, -32000, -32000, 0, 0,
                            SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                        _movedOffscreen = true;
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// True while Windows' own Live Captions window is parked off-screen by us.
        /// Shutdown checks this before deciding whether it is worth blocking to let an
        /// in-flight poll finish: if we never moved the window, no late poll can strand
        /// it, and the wait is pure delay on the way out.
        /// </summary>
        public bool WindowsBarMovedOffscreen => _movedOffscreen;

        /// <summary>Restores the Windows Live Captions window to where it was.</summary>
        public void RestoreWindowsBar()
        {
            if (_hwnd == IntPtr.Zero || !_movedOffscreen || !_haveOriginalRect)
            {
                _movedOffscreen = false;
                return;
            }
            try
            {
                // Defence-in-depth: never restore to an off-screen coordinate. _originalRect
                // should only ever hold an on-screen spot now, but if it somehow doesn't,
                // drop the window at a safe visible position instead of hiding it again.
                int x = _originalRect.Left;
                int y = _originalRect.Top;
                if (x <= -20000 || y <= -20000)
                {
                    x = 80;
                    y = 80;
                }
                SetWindowPos(_hwnd, IntPtr.Zero, x, y,
                    0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            }
            catch { }
            _movedOffscreen = false;
        }

        /// <summary>Forget the current handle (e.g. after captions are toggled off).</summary>
        public void Reset()
        {
            _hwnd = IntPtr.Zero;
            _movedOffscreen = false;
            _haveOriginalRect = false;
            _cachedTextEl = null;
            _cacheReadsSinceWalk = 0;
        }

        /// <summary>
        /// Whether a Live Captions window currently exists, regardless of where it
        /// has been moved. A blind Win+Ctrl+L is a toggle, so callers use this to
        /// decide whether the keypress would turn captions ON or accidentally OFF.
        /// </summary>
        public bool IsWindowPresent()
        {
            // Decide by the actual caption WINDOW, not the process. LiveCaptions.exe can
            // stay resident ("warm") while the caption bar is toggled OFF — the old
            // process-first check then read "already on", so the auto-enable skipped its
            // Win+Ctrl+L toggle and captions never appeared. The window is the real
            // on/off signal (and on some builds the window isn't even owned by
            // LiveCaptions.exe, which made the process check doubly unreliable).
            // …and by a VISIBLE window. The same resident process can leave its caption
            // window behind, hidden, after the bar is toggled off — FindWindow still
            // returns it, so "is it on?" answered yes for a bar that is not on screen.
            // That reading is worse than useless here: EnsureWindowsCaptions compares it
            // against what it wants and toggles on any mismatch, so a stale hidden window
            // made Tempo send Win+Ctrl+L and switch Windows Live Captions ON at the exact
            // moment it was trying to make sure it was off.
            IntPtr h = FindVisible();
            if (h != IntPtr.Zero)
            {
                return true;
            }

            // Last resort: the reader's own locator, which can also find caption windows
            // hosted outside LiveCaptions.exe via UI Automation.
            try { return Locate(); } catch { return false; }
        }

        /// <summary>
        /// Cheap window-only presence check (no UI Automation fallback) — safe to
        /// poll every couple of seconds by the external-toggle watcher without
        /// paying UIA's cross-process cost while captions are off.
        /// </summary>
        public bool IsWindowPresentFast()
        {
            return FindVisible() != IntPtr.Zero;
        }

        /// <summary>
        /// Closes the Windows Live Captions bar by closing its WINDOW, and reports
        /// whether there was one to close.
        ///
        /// Win+Ctrl+L is the documented way to toggle Live Captions, and it is not
        /// dependable. Measured on a clean run: the shortcut worked twice and then
        /// stopped responding entirely while LiveCaptions.exe stayed resident —
        ///
        ///     toggle 1: no window -> hidden     changed=True
        ///     toggle 2: hidden    -> VISIBLE    changed=True
        ///     toggle 3: VISIBLE   -> VISIBLE    changed=False
        ///     toggle 4: VISIBLE   -> VISIBLE    changed=False
        ///
        /// which is the "it's in Task Manager but Tempo can't get it" report. Closing the
        /// window does what pressing its X does, and in the same measurement it worked
        /// every time — the bar went away and the process exited with it.
        /// </summary>
        public bool CloseWindowsBar()
        {
            try
            {
                IntPtr h = FindVisible();
                if (h == IntPtr.Zero) { return false; }
                PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

                // WAIT for it to actually go. PostMessage returns the instant the message
                // is queued, and the next presence check then still sees the closing
                // window — which read as "Live Captions is on", so Tempo recorded the
                // wrong state to restore AND decided it had nothing to turn on next time.
                // Observed as: close it, and one second later every subsequent toggle
                // logged "already on; no toggle sent" and did nothing, forever.
                //
                // Bounded and short: it normally goes in a few hundred ms, and this only
                // runs on a deliberate captions-off, not on any hot path.
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < 2000)
                {
                    if (FindVisible() == IntPtr.Zero)
                    {
                        Logger.Info("[Captions] closed the Windows Live Captions window in " +
                                    clock.ElapsedMilliseconds + " ms.");
                        return true;
                    }
                    System.Threading.Thread.Sleep(50);
                }
                Logger.Warn("[Captions] the Windows Live Captions window didn't close within 2s.");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn("[Captions] couldn't close the Windows Live Captions window: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Starts Windows Live Captions by launching its executable, for when the
        /// keyboard shortcut is being ignored. Returns false if it isn't present on this
        /// build of Windows (Live Captions is Windows 11 22H2 and later).
        /// </summary>
        public static bool LaunchWindowsCaptions()
        {
            try
            {
                string exe = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System), "LiveCaptions.exe");
                if (!System.IO.File.Exists(exe))
                {
                    Logger.Info("[Captions] LiveCaptions.exe isn't on this version of Windows.");
                    return false;
                }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
                {
                    UseShellExecute = true
                });
                Logger.Info("[Captions] launched LiveCaptions.exe directly (the shortcut wasn't working).");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn("[Captions] couldn't launch LiveCaptions.exe: " + ex.Message);
                return false;
            }
        }

        private const uint WM_CLOSE = 0x0010;

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// The Windows Live Captions bar, but only when it is actually ON SCREEN.
        ///
        /// Both presence checks used a bare FindWindow, which also matches the window
        /// Windows leaves behind — hidden — when the bar is toggled off and the process
        /// stays warm. Measured on this machine: after toggling the bar off, the process
        /// remained resident and a plain FindWindow still answered "present".
        /// </summary>
        private static IntPtr FindVisible()
        {
            try
            {
                IntPtr h = FindWindow(LiveCaptionsClass, null);
                if (h != IntPtr.Zero && IsWindowVisible(h)) { return h; }
                h = FindWindow(null, LiveCaptionsTitle);
                if (h != IntPtr.Zero && IsWindowVisible(h)) { return h; }
                h = FindWindow(null, "Live Captions");
                if (h != IntPtr.Zero && IsWindowVisible(h)) { return h; }
            }
            catch { }
            return IntPtr.Zero;
        }
    }
}
