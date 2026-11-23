using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Lets Tempo's taskbar button PLAY an animated custom logo (round 39b: "need icon applied gif instead of one frame").
    ///
    /// Windows draws the button of a program that has a Start-menu shortcut with the SHORTCUT's icon and passes the
    /// window's own icon (WM_SETICON) over — so AnimatedLogo's frames reached the title bar and the tray while the taskbar
    /// held a still. A window carrying an AppUserModelID of its own, one no shortcut carries, is drawn with the window's
    /// icon, frame by frame. Measured on this PC with a stand-in program that has a Start-menu shortcut
    /// (scratchpad/traysnap, anim / animlnk2): no ID → the shortcut's icon, still; an ID of its own → the window's icon,
    /// animating, also when the program was started THROUGH the shortcut and with a relaunch command and name set (what
    /// "Pin to taskbar" uses); the ID taken away → the shortcut's icon again. A relaunch ICON wins over the window's icon
    /// again, so none is set.
    ///
    /// Taskbar pins: a pin made from the Start menu (or before this) knows Tempo by its exe path, and a window with an ID
    /// of its own no longer groups with it — a second Tempo button beside the pin. So while such a pin exists the windows
    /// keep the exe's identity (the pin's picture: the still logo ShellLogo gives it); a pin that carries Tempo's own ID
    /// (made from the running button) groups with the windows either way, and Windows draws pinned buttons from the pin.
    /// The pins folder is watched, so pinning or unpinning while Tempo runs switches at once. Nothing is needed at exit:
    /// each window's properties are cleared as it is destroyed.
    ///
    /// EVERY window, not only the main ones (round 42). WinForms puts a form on the taskbar unless ShowInTaskbar is false —
    /// owned dialogs too (WS_EX_APPWINDOW) — and only the main window, a tab in its own window and the update dialog
    /// followed the ID. About (where the logo is chosen), Live debug, the Recycle bin, the macro editors… kept the exe's
    /// identity, and Windows drew them as a second app beside the first (the user's screenshot, 2026-11-22: two Tempo
    /// buttons while About was open). A CBT hook on the UI thread (<see cref="WatchThisThread"/>) now sees every window as
    /// it is made, before it can show, and one that can reach the taskbar — WS_EX_APPWINDOW, or unowned and not a tool
    /// window — takes the same identity as the main window, whichever that is. A test that builds Tempo's windows inside
    /// another program leaves them alone: they are not Tempo, and must not join its button.
    /// </summary>
    public static class TaskbarIdentity
    {
        /// <summary>The ID Tempo's windows carry on the taskbar. No shortcut of Tempo's carries it (ShellLogo never sets one).</summary>
        public const string AppId = "Tempo.TempoClicker";

        private static readonly object Gate = new object();
        private static Task<bool> _check;
        private static volatile bool _useOwnId = true;
        private static volatile string _status = "not checked yet";
        private static string _lastLogged;
        private static SynchronizationContext _ui;
        private static readonly List<Form> Forms = new List<Form>();           // UI thread only
        private static readonly HashSet<IntPtr> Carrying = new HashSet<IntPtr>(); // windows with the ID set (under Gate)
        private static readonly HashSet<IntPtr> Seen = new HashSet<IntPtr>();     // taskbar-able windows the hook saw made (under Gate)
        private static FileSystemWatcher _watcher;
        private static System.Threading.Timer _debounce;

        /// <summary>For the harnesses: another pins folder and another exe than this one.</summary>
        public static string PinsFolderForTest;
        public static string ExeForTest;

        /// <summary>
        /// For the harnesses: an ID of their own — never Tempo's, so a test's windows cannot join the running Tempo's
        /// button — and leave to act inside another program (otherwise Follow and WatchThisThread do nothing there).
        /// </summary>
        public static string IdForTest;
        public static bool WhenHostedForTest;

        private static string Id => IdForTest ?? AppId;
        private static string DisplayName => IdForTest == null ? "Tempo" : "Tempo test";

        /// <summary>How many of Tempo's windows carry the ID now (Live debug, the harnesses).</summary>
        public static int WindowsCarrying { get { lock (Gate) { return Carrying.Count; } } }

        /// <summary>Running inside another program — a test that builds Tempo's windows — rather than as Tempo.</summary>
        private static bool HostedElsewhere
        {
            get
            {
                if (WhenHostedForTest) { return false; }
                try
                {
                    System.Reflection.Assembly entry = System.Reflection.Assembly.GetEntryAssembly();
                    return entry != null && entry != typeof(TaskbarIdentity).Assembly;
                }
                catch { return false; }
            }
        }

        /// <summary>One line for Live debug and the log.</summary>
        public static string Status => _status;

        /// <summary>Whether Tempo's windows carry their own ID now (the logo plays on the button).</summary>
        public static bool UsingOwnId => _useOwnId;

        private static string PinsFolder => PinsFolderForTest ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");

        private static string Exe => ExeForTest ?? Environment.ProcessPath ?? "";

        /// <summary>Looks at the taskbar pins off the UI thread, early, so the first window has the answer waiting.</summary>
        public static void BeginCheck()
        {
            lock (Gate)
            {
                if (_check == null) { _check = Task.Run(() => Decide("start-up")); }
            }
        }

        /// <summary>
        /// Gives <paramref name="form"/> Tempo's own taskbar identity whenever its window exists (and takes it away as
        /// the window goes), while no Start-menu-made pin holds Tempo. Call on the UI thread, before or after the handle.
        /// </summary>
        public static void Follow(Form form)
        {
            if (form == null || HostedElsewhere) { return; }
            if (_ui == null) { _ui = SynchronizationContext.Current; }
            WatchThisThread();
            if (Forms.Contains(form)) { return; }   // a second call would hook the events twice
            Forms.Add(form);
            form.HandleCreated += OnHandleCreated;
            form.HandleDestroyed += OnHandleDestroyed;
            form.Disposed += (s, e) => Forms.Remove(form);
            if (form.IsHandleCreated) { Apply(form.Handle); }
        }

        private static void OnHandleCreated(object sender, EventArgs e)
        {
            if (sender is Form f) { Apply(f.Handle); }
        }

        private static void OnHandleDestroyed(object sender, EventArgs e)
        {
            if (sender is Form f && f.IsHandleCreated) { Forget(f.Handle); }
        }

        private static void Apply(IntPtr h)
        {
            if (h == IntPtr.Zero) { return; }
            try
            {
                bool own = Decision();
                bool carrying;
                lock (Gate) { carrying = Carrying.Contains(h); }
                if (own && !carrying)
                {
                    if (SetProps(h, true)) { lock (Gate) { Carrying.Add(h); } }
                }
                else if (!own && carrying)
                {
                    SetProps(h, false);
                    lock (Gate) { Carrying.Remove(h); }
                }
            }
            catch (Exception ex) { Logger.Swallow("TaskbarIdentity.Apply", ex); }
        }

        /// <summary>A window going: its properties are cleared while it still exists (the shell keeps them until emptied).</summary>
        private static void Forget(IntPtr h)
        {
            bool carried;
            lock (Gate)
            {
                Seen.Remove(h);
                carried = Carrying.Remove(h);
            }
            if (carried) { SetProps(h, false); }
        }

        // ── Every window the UI thread makes (round 42) ─────────────────────────────────────────────────────────────

        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
        private static readonly HookProc CbtHook = OnCbt;   // a static field: the hook outlives any local
        [ThreadStatic] private static IntPtr t_hook;

        private const int WH_CBT = 5, HCBT_CREATEWND = 3, HCBT_DESTROYWND = 4;
        private const int WS_CHILD = 0x40000000, WS_EX_TOOLWINDOW = 0x00000080, WS_EX_APPWINDOW = 0x00040000;

        [StructLayout(LayoutKind.Sequential)]
        private struct CreateStruct
        {
            public IntPtr CreateParams, Instance, Menu, Parent;
            public int Cy, Cx, Y, X, Style;
            public IntPtr Name, Class;
            public int ExStyle;
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr module, uint threadId);

        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);

        /// <summary>For the harnesses: this thread's watch taken away again (A/B timing on one thread).</summary>
        public static void UnwatchThisThreadForTest()
        {
            if (t_hook == IntPtr.Zero) { return; }
            UnhookWindowsHookEx(t_hook);
            t_hook = IntPtr.Zero;
        }
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

        /// <summary>
        /// From now on every window this thread makes that can reach the taskbar carries the same identity as the main
        /// window (see the class note) — dialogs included, and ones added later without anybody remembering to ask.
        /// Once per thread; call on a UI thread before its first window. Does nothing inside another program.
        /// </summary>
        public static void WatchThisThread()
        {
            if (t_hook != IntPtr.Zero || HostedElsewhere) { return; }
            try
            {
                t_hook = SetWindowsHookEx(WH_CBT, CbtHook, IntPtr.Zero, GetCurrentThreadId());
                if (t_hook == IntPtr.Zero)
                {
                    Logger.Warn("[Icon] taskbar identity: could not watch this thread's windows (error "
                                + Marshal.GetLastWin32Error() + ") — a dialog may show as a second Tempo on the taskbar.");
                }
            }
            catch (Exception ex) { Logger.Swallow("TaskbarIdentity.WatchThisThread", ex); }
            StartWatching();
        }

        // Inside CreateWindowEx and DestroyWindow: quick, and nothing may escape into user32.
        private static IntPtr OnCbt(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (code == HCBT_CREATEWND) { OnWindowMade(wParam, lParam); }
                else if (code == HCBT_DESTROYWND) { Forget(wParam); }
            }
            catch { }
            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        private static void OnWindowMade(IntPtr hwnd, IntPtr lParam)
        {
            IntPtr cs = lParam == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(lParam);   // CBT_CREATEWND.lpcs
            if (cs == IntPtr.Zero) { return; }
            // A control inside a window — nearly every window made — costs one read: CREATESTRUCT.style, past four
            // pointers and four ints.
            if ((Marshal.ReadInt32(cs, IntPtr.Size * 4 + 16) & WS_CHILD) != 0) { return; }
            CreateStruct c = Marshal.PtrToStructure<CreateStruct>(cs);
            bool appWindow = (c.ExStyle & WS_EX_APPWINDOW) != 0;
            // Owned from the start (a menu, a tip, a message box or file dialog with an owner) or a tool window: never on the
            // taskbar; HWND_MESSAGE-parented helpers have a parent too. A form with ShowInTaskbar false takes the ID anyway:
            // WinForms gives a form its owner only after the window is made (tbcheck), so here it still looks unowned —
            // harmless, it never reaches the taskbar, and deciding now is what keeps a second button from ever flashing up.
            if (!appWindow && (c.Parent != IntPtr.Zero || (c.ExStyle & WS_EX_TOOLWINDOW) != 0)) { return; }
            if (IsImeWindow(c.Class)) { return; }
            if (_ui == null) { _ui = SynchronizationContext.Current; }
            lock (Gate) { Seen.Add(hwnd); }
            Apply(hwnd);
        }

        // The input method's own windows are made on every UI thread; they never show on the taskbar.
        private static bool IsImeWindow(IntPtr cls)
        {
            if (cls == IntPtr.Zero || ((long)cls >> 16) == 0) { return false; }   // a class atom, not a name
            string name = Marshal.PtrToStringUni(cls);
            return name == "IME" || name == "MSCTFIME UI";
        }

        /// <summary>The answer of the start-up look at the pins — waited for briefly the first time, never again.</summary>
        private static bool Decision()
        {
            Task<bool> t;
            lock (Gate)
            {
                if (_check == null) { _check = Task.Run(() => Decide("first window")); }
                t = _check;
            }
            if (!t.IsCompleted) { t.Wait(300); }
            return t.IsCompleted ? t.Result : _useOwnId;
        }

        /// <summary>Reads the pins and settles whether Tempo's windows carry their own ID. Any thread.</summary>
        internal static bool Decide(string why)
        {
            bool implicitPin = false, ownPin = false;
            try
            {
                string folder = PinsFolder, me = Full(Exe);
                if (Directory.Exists(folder) && me.Length > 0)
                {
                    foreach (string lnk in Directory.GetFiles(folder, "*.lnk", SearchOption.TopDirectoryOnly))
                    {
                        string target = SelfInstaller.ReadShortcutTarget(lnk);
                        if (string.IsNullOrEmpty(target) || !string.Equals(Full(target), me, StringComparison.OrdinalIgnoreCase)) { continue; }
                        if (string.Equals(ReadAppId(lnk), Id, StringComparison.OrdinalIgnoreCase)) { ownPin = true; }
                        else { implicitPin = true; }
                    }
                }
            }
            catch (Exception ex) { Logger.Swallow("TaskbarIdentity.Decide", ex); }

            bool own = ownPin || !implicitPin;
            _useOwnId = own;
            _status = own
                ? (ownPin ? "pinned (a pin made from the running button): Windows draws the pin's picture"
                          : "Tempo's own — the logo plays on the taskbar button")
                : "the pin's — Tempo is pinned from the Start menu, so the button shows the pin's still picture (an own ID would add a second button)";
            if (!string.Equals(_status, _lastLogged, StringComparison.Ordinal))
            {
                _lastLogged = _status;
                Logger.Info("[Icon] taskbar identity: " + _status + " (" + why + ").");
            }
            return own;
        }

        // ── Pinning or unpinning while Tempo runs ───────────────────────────────────────────────────────────────────

        private static void StartWatching()
        {
            lock (Gate)
            {
                if (_watcher != null) { return; }
                try
                {
                    string folder = PinsFolder;
                    if (!Directory.Exists(folder)) { return; }
                    _watcher = new FileSystemWatcher(folder, "*.lnk")
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                        IncludeSubdirectories = false,
                    };
                    FileSystemEventHandler changed = (s, e) => Soon();
                    _watcher.Created += changed;
                    _watcher.Deleted += changed;
                    _watcher.Changed += changed;
                    _watcher.Renamed += (s, e) => Soon();
                    _watcher.EnableRaisingEvents = true;
                }
                catch (Exception ex)
                {
                    Logger.Swallow("TaskbarIdentity.StartWatching", ex);
                    _watcher = null;
                }
            }
        }

        // Explorer writes a pin in several steps: one look, a moment after the last of them.
        private static void Soon()
        {
            lock (Gate)
            {
                _debounce?.Dispose();
                _debounce = new System.Threading.Timer(_ => Recheck(), null, 800, Timeout.Infinite);
            }
        }

        private static void Recheck()
        {
            bool before = _useOwnId;
            bool now = Decide("the taskbar pins changed");
            lock (Gate) { _check = Task.FromResult(now); }
            // A new pin wears the exe's icon until ShellLogo gives it the logo.
            ShellLogo.SyncSoon("the taskbar pins changed");
            if (before == now) { return; }
            SynchronizationContext ui = _ui;
            if (ui == null) { return; }
            ui.Post(_ =>
            {
                foreach (Form f in Forms.ToArray())
                {
                    if (f != null && !f.IsDisposed && f.IsHandleCreated) { Apply(f.Handle); }
                }
                // And every other window the hook saw made: a dialog left on the exe's identity, or carrying the ID
                // after the main window dropped it, is the second button all over again.
                IntPtr[] seen;
                lock (Gate)
                {
                    seen = new IntPtr[Seen.Count];
                    Seen.CopyTo(seen);
                }
                foreach (IntPtr h in seen)
                {
                    if (IsWindow(h)) { Apply(h); }
                    else { lock (Gate) { Seen.Remove(h); Carrying.Remove(h); } }
                }
            }, null);
        }

        // ── The window's property store ─────────────────────────────────────────────────────────────────────────────

        [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid FormatId; public int PropertyId; }
        [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Pointer; }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetAt(uint index, out PropertyKey key);
            [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
            [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
            [PreserveSig] int Commit();
        }

        [DllImport("shell32.dll")]
        private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHGetPropertyStoreFromParsingName(string path, IntPtr bindContext, int flags, ref Guid iid,
                                                                    [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

        [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);

        // System.AppUserModel.*: RelaunchCommand 2, RelaunchDisplayNameResource 4, ID 5.
        private static readonly Guid AppUserModelProps = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
        private const ushort VT_EMPTY = 0, VT_LPWSTR = 31;

        /// <summary>Sets (or empties) the window's ID and the relaunch command and name a pin made from it uses.</summary>
        private static bool SetProps(IntPtr hwnd, bool on)
        {
            Guid iid = typeof(IPropertyStore).GUID;
            IPropertyStore store = null;
            try
            {
                if (SHGetPropertyStoreForWindow(hwnd, ref iid, out store) != 0 || store == null) { return false; }
                if (on)
                {
                    // The relaunch command and name first: the shell reads them when the ID arrives.
                    Put(store, 2, "\"" + Exe + "\"");
                    Put(store, 4, DisplayName);
                    return Put(store, 5, Id);
                }
                bool ok = Put(store, 5, null);
                Put(store, 2, null);
                Put(store, 4, null);
                return ok;
            }
            catch (Exception ex)
            {
                Logger.Swallow("TaskbarIdentity.SetProps", ex);
                return false;
            }
            finally
            {
                if (store != null) { try { Marshal.ReleaseComObject(store); } catch { } }
            }
        }

        private static bool Put(IPropertyStore store, int pid, string value)
        {
            var key = new PropertyKey { FormatId = AppUserModelProps, PropertyId = pid };
            var v = new PropVariant { Type = value == null ? VT_EMPTY : VT_LPWSTR, Pointer = value == null ? IntPtr.Zero : Marshal.StringToCoTaskMemUni(value) };
            try { return store.SetValue(ref key, ref v) == 0; }
            finally { if (v.Pointer != IntPtr.Zero) { Marshal.FreeCoTaskMem(v.Pointer); } }
        }

        /// <summary>The AppUserModelID a shortcut carries, or null (a shortcut without one: Windows uses its target's path).</summary>
        internal static string ReadAppId(string lnk)
        {
            Guid iid = typeof(IPropertyStore).GUID;
            IPropertyStore store = null;
            try
            {
                if (SHGetPropertyStoreFromParsingName(lnk, IntPtr.Zero, 0, ref iid, out store) != 0 || store == null) { return null; }
                var key = new PropertyKey { FormatId = AppUserModelProps, PropertyId = 5 };
                if (store.GetValue(ref key, out PropVariant v) != 0) { return null; }
                try { return v.Type == VT_LPWSTR && v.Pointer != IntPtr.Zero ? Marshal.PtrToStringUni(v.Pointer) : null; }
                finally { PropVariantClear(ref v); }
            }
            catch { return null; }
            finally
            {
                if (store != null) { try { Marshal.ReleaseComObject(store); } catch { } }
            }
        }

        private static string Full(string p)
        {
            try { return Path.GetFullPath(p.Trim().Trim('"')); }
            catch { return p ?? ""; }
        }
    }
}
