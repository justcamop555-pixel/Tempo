using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using AutoClicker.Utils;

namespace AutoClicker.Native
{
    /// <summary>
    /// While the clicker runs or a macro plays or records, Windows' own shortcuts are held back, so
    /// only Tempo's keybinds act: a stray press of the Windows key cannot open Start over the game
    /// (taking the focus the clicks and keys were going to), Win+G, Win+D, Win+Tab and the rest do
    /// nothing, Ctrl+Esc does not open Start either, and Shift pressed five times — a sprinting macro
    /// does exactly that — cannot bring up the Sticky Keys question in the middle of the run.
    ///
    /// Only the user's own key presses are held back, never injected ones (the macro's own keys go
    /// through as recorded). Left alone on purpose: Alt+Tab, so there is always a way out of the game;
    /// Ctrl+Shift+Esc (Task Manager); and Ctrl+Alt+Del and Win+L, which Windows reserves and no
    /// program can block. A Windows-key combination that is one of Tempo's own hotkeys still works.
    ///
    /// The Windows key itself is let through (so the system knows it is down, and a Tempo hotkey that
    /// uses it still sees it); the key pressed WITH it is held back. Releasing the Windows key with
    /// nothing else pressed is what opens Start, so the guard puts a key no keyboard has (VK E8) in
    /// between — the same "menu mask" AutoHotkey uses for the same purpose.
    ///
    /// The hook lives on a thread of its own. It is on for every run, and every key — the user's, and
    /// each one a macro or the clicker presses — waits for a low-level hook's callback before it reaches
    /// the game. On the UI thread that wait would include whatever the window happened to be doing
    /// (painting the Live Monitor, say); here it is only this class's few comparisons.
    /// </summary>
    public sealed class ShortcutGuard : IDisposable
    {
        private const int VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_ESCAPE = 0x1B;
        private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
        /// <summary>Unassigned: no keyboard sends it and no program acts on it.</summary>
        internal const int VK_MASK = 0xE8;

        private readonly LowLevelKeyboardHook _hook = new LowLevelKeyboardHook();
        // Hook-thread state. SetActive touches it only while that thread is not running.
        private readonly HashSet<int> _heldBack = new HashSet<int>();
        private bool _winDown;
        private bool _winCombined;
        private volatile bool _active;
        private bool _disposed;
        private Thread _thread;
        private uint _threadId;

        /// <summary>
        /// True when this key, with the modifiers held now, is one of Tempo's own hotkeys — those always
        /// go through (GlobalHotkeyManager.FiresHotkeyNow). Called on the guard's thread.
        /// </summary>
        public Func<int, bool> IsTempoHotkey { get; set; }

        /// <summary>Something was held back. Raised on the guard's own thread: marshal before touching UI.</summary>
        public event Action HeldBackShortcut;

        /// <summary>How many shortcuts were held back since Tempo started (Live debug, the tests).</summary>
        public int HeldBackCount => Volatile.Read(ref _heldBackCount);
        private int _heldBackCount;

        /// <summary>For the tests: sends the mask key / a key release instead of SendInput.</summary>
        internal Action<int, bool> SendKeyForTest;

        /// <summary>For the tests: SetActive installs no hook (the test feeds <see cref="OnKey"/> itself).</summary>
        internal bool SkipHookForTest;

        public bool Active => _active;

        public ShortcutGuard()
        {
            _hook.KeyEvent += OnKey;
            _hook.Quiet = true;   // in and out with every run: Live debug lines, not log-file ones
        }

        /// <summary>Starts or stops holding back. Cheap when it is already in that state. UI thread.</summary>
        public void SetActive(bool on)
        {
            if (_disposed || on == _active) { return; }
            if (on)
            {
                _heldBack.Clear();
                _winDown = false;
                _winCombined = false;
                _active = true;
                if (!StartHookThread())
                {
                    Logger.Warn("[Guard] the keyboard hook would not install — Windows shortcuts are not held back this time.");
                }
                AccessibilityShortcuts.Suspend();
            }
            else
            {
                _active = false;
                StopHookThread();
                AccessibilityShortcuts.Restore();
            }
            Logger.Debug("[Guard] Windows shortcuts " + (on ? "held back" : "free again") + ".");
        }

        /// <summary>Installs the hook on a new thread that does nothing but receive its callbacks.</summary>
        private bool StartHookThread()
        {
            if (SkipHookForTest) { return true; }
            var ready = new ManualResetEventSlim(false);
            bool installed = false;
            var thread = new Thread(() =>
            {
                _threadId = NativeMethods.GetCurrentThreadId();
                // A message queue before anyone is told the thread is up: WM_QUIT posted to a thread
                // without one is lost, and the thread would never end.
                PeekMessage(out MSG peek, IntPtr.Zero, 0, 0, PM_NOREMOVE);
                installed = _hook.Start();
                ready.Set();
                if (!installed) { return; }
                try
                {
                    // The hook's callbacks are delivered inside GetMessage. Nothing is ever posted here
                    // but the WM_QUIT that ends it.
                    while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0) { }
                }
                finally
                {
                    _hook.Stop();
                }
            })
            {
                IsBackground = true,
                Name = "Tempo shortcut guard",
                // Every key waits for this thread, so it must not queue behind a busy game's threads.
                Priority = ThreadPriority.AboveNormal
            };
            thread.Start();
            _thread = thread;   // even if it is slow to start: StopHookThread must still be able to end it
            return ready.Wait(3000) && installed;
        }

        private void StopHookThread()
        {
            Thread thread = _thread;
            _thread = null;
            if (thread == null || !thread.IsAlive) { return; }
            PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            if (!thread.Join(2000))
            {
                // Still listening, but _active is off, so every key passes straight through.
                Logger.Warn("[Guard] the hook thread did not stop in time.");
            }
        }

        /// <summary>The hook callback, separate so the tests can feed it events.</summary>
        internal void OnKey(object sender, KeyboardHookEventArgs e)
        {
            if (!_active || e.Injected) { return; }
            int vk = e.VirtualKey;

            if (vk == VK_LWIN || vk == VK_RWIN)
            {
                if (e.IsKeyDown)
                {
                    if (!_winDown) { _winDown = true; _winCombined = false; }
                    return;   // let it through: the system must know it is held
                }
                _winDown = false;
                if (!_winCombined)
                {
                    // Pressed and released alone: this release opens Start. Hold it back, and send the
                    // mask and then the release, in that order, so the key is still released.
                    e.Suppress = true;
                    Send(VK_MASK, true);
                    Send(VK_MASK, false);
                    Send(vk, false);
                    Note();
                }
                return;
            }

            if (!e.IsKeyDown)
            {
                // The release of a press that was held back is held back with it.
                if (_heldBack.Remove(vk)) { e.Suppress = true; }
                return;
            }
            if (_heldBack.Contains(vk))
            {
                e.Suppress = true;   // auto-repeat of a press already held back
                return;
            }

            if (_winDown)
            {
                // Shift, Ctrl and Alt with it change nothing: released after only those, the Windows key
                // is still "alone" and still gets the mask.
                if (IsModifier(vk)) { return; }
                _winCombined = true;   // Start will not open on release now
                if (IsTempoHotkeySafe(vk)) { return; }   // Tempo's own Win+… hotkey
                e.Suppress = true;
                _heldBack.Add(vk);
                // The shell saw the Windows key go down, then nothing: release it now and Start opens.
                Send(VK_MASK, true);
                Send(VK_MASK, false);
                Note();
                return;
            }

            if (vk == VK_ESCAPE && IsDown(VK_CONTROL) && !IsDown(VK_SHIFT) && !IsDown(VK_MENU))
            {
                if (IsTempoHotkeySafe(vk)) { return; }
                e.Suppress = true;   // Ctrl+Esc opens Start; Ctrl+Shift+Esc (Task Manager) is not touched
                _heldBack.Add(vk);
                Note();
            }
        }

        private bool IsTempoHotkeySafe(int vk)
        {
            try { return IsTempoHotkey != null && IsTempoHotkey(vk); }
            catch { return false; }
        }

        private void Note()
        {
            Interlocked.Increment(ref _heldBackCount);
            try { HeldBackShortcut?.Invoke(); } catch { }
        }

        private static bool IsModifier(int vk)
        {
            switch (vk)
            {
                case 0x10: case 0x11: case 0x12:
                case 0xA0: case 0xA1: case 0xA2: case 0xA3: case 0xA4: case 0xA5:
                    return true;
                default:
                    return false;
            }
        }

        private bool IsDown(int vk) =>
            IsDownForTest != null ? IsDownForTest(vk) : (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

        /// <summary>For the tests: which keys count as held, instead of the real keyboard.</summary>
        internal Func<int, bool> IsDownForTest;

        private void Send(int vk, bool down)
        {
            if (SendKeyForTest != null) { SendKeyForTest(vk, down); return; }
            try
            {
                var input = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    ki = new KEYBDINPUT { wVk = (ushort)vk, dwFlags = down ? 0u : KEYEVENTF_KEYUP }
                };
                SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
            }
            catch (Exception ex) { Logger.Swallow("ShortcutGuard.Send", ex); }
        }

        public void Dispose()
        {
            if (_disposed) { return; }
            SetActive(false);
            _hook.KeyEvent -= OnKey;
            _hook.Dispose();
            _disposed = true;
        }

        // ── SendInput ─────────────────────────────────────────────────────────────────────────────
        private const int INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // The union's largest member is MOUSEINPUT; pad so INPUT has the size Windows expects.
        [StructLayout(LayoutKind.Explicit)]
        private struct INPUT
        {
            [FieldOffset(0)] public int type;
            [FieldOffset(8)] public KEYBDINPUT ki;
            [FieldOffset(8)] private MOUSEINPUT _mouseSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        // ── The hook thread's message loop ────────────────────────────────────────────────────────
        private const uint WM_QUIT = 0x0012, PM_NOREMOVE = 0x0000;

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG msg, IntPtr hWnd, uint filterMin, uint filterMax);

        [DllImport("user32.dll")]
        private static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint filterMin, uint filterMax, uint remove);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
    }

    /// <summary>
    /// The accessibility "shortcut" questions — Sticky Keys (Shift five times), Filter Keys (right
    /// Shift held 8 s), Toggle Keys (Num Lock held 5 s) — switched off while a run lasts and put back
    /// exactly as they were. Only the SHORTCUTS: a feature someone has actually turned on is left alone.
    /// The change is never written to the user's profile (no SPIF_UPDATEINIFILE), so even a crash
    /// mid-run cannot make it permanent — signing out undoes it.
    ///
    /// Put back however Tempo ends: by the guard when the run stops, at shutdown, at process exit, and —
    /// for a Tempo killed mid-run, which runs none of those — at its next start, from a note left in the
    /// data folder while the shortcuts are off (<see cref="RecoverLeftovers"/>).
    /// </summary>
    internal static class AccessibilityShortcuts
    {
        private const uint SPI_GETFILTERKEYS = 0x0032, SPI_SETFILTERKEYS = 0x0033;
        private const uint SPI_GETTOGGLEKEYS = 0x0034, SPI_SETTOGGLEKEYS = 0x0035;
        private const uint SPI_GETSTICKYKEYS = 0x003A, SPI_SETSTICKYKEYS = 0x003B;
        private const uint ON = 0x1, HOTKEYACTIVE = 0x4, CONFIRMHOTKEY = 0x8;
        private const string NoteName = "shortcut-guard.note";

        private static readonly object Gate = new object();
        private static uint? _sticky, _toggle, _filter;   // the flags as they were, while suspended

        /// <summary>For the tests: read and write a feature's flags without touching Windows.</summary>
        internal static Func<uint, uint?> GetForTest;
        internal static Action<uint, uint> SetForTest;

        /// <summary>For the tests: where the note goes (with the seams above, none is written otherwise).</summary>
        internal static string NotePathForTest;

        public static bool Suspended { get { lock (Gate) { return _sticky.HasValue || _toggle.HasValue || _filter.HasValue; } } }

        public static void Suspend()
        {
            lock (Gate)
            {
                if (Suspended) { return; }
                _sticky = SuspendOne(SPI_GETSTICKYKEYS, SPI_SETSTICKYKEYS);
                _toggle = SuspendOne(SPI_GETTOGGLEKEYS, SPI_SETTOGGLEKEYS);
                _filter = SuspendOne(SPI_GETFILTERKEYS, SPI_SETFILTERKEYS);
                if (!Suspended) { return; }
                WriteNote();
                if (!_exitHooked)
                {
                    // Put back however the process ends — not only through MainForm's shutdown. A process
                    // that just returned from Main or called Environment.Exit mid-run (a test harness did)
                    // left the shortcuts off until the user signed out. A hard kill is the note's job.
                    _exitHooked = true;
                    AppDomain.CurrentDomain.ProcessExit += (s, e) => Restore();
                }
            }
        }

        private static bool _exitHooked;

        public static void Restore()
        {
            lock (Gate)
            {
                bool any = Suspended;
                if (_sticky.HasValue) { Set(SPI_SETSTICKYKEYS, _sticky.Value); }
                if (_toggle.HasValue) { Set(SPI_SETTOGGLEKEYS, _toggle.Value); }
                if (_filter.HasValue) { Set(SPI_SETFILTERKEYS, _filter.Value); }
                _sticky = _toggle = _filter = null;
                if (any) { DeleteNote(); }
            }
        }

        /// <summary>
        /// At start-up: a note left by a Tempo that never got to <see cref="Restore"/> — killed mid-run,
        /// from Task Manager, say. Each shortcut it switched off is put back, but only while it is still
        /// exactly as Tempo left it, in the same Windows session: a value changed since is the user's (or
        /// another program's) and stays. Signing out had undone it anyway, so a note from before the last
        /// restart is simply dropped.
        /// </summary>
        public static int RecoverLeftovers()
        {
            string path = NotePath();
            if (path == null) { return 0; }
            int putBack = 0;
            lock (Gate)
            {
                try
                {
                    if (!System.IO.File.Exists(path)) { return 0; }
                    string[] lines = System.IO.File.ReadAllLines(path);
                    bool sameBoot = lines.Length > 0 && lines[0].StartsWith("boot ", StringComparison.Ordinal) &&
                                    long.TryParse(lines[0].Substring(5), out long boot) &&
                                    Math.Abs(BootTicks() - boot) < TimeSpan.TicksPerMinute * 2;
                    for (int i = 1; sameBoot && i < lines.Length; i++)
                    {
                        string[] p = lines[i].Split(' ');
                        if (p.Length != 2 ||
                            !uint.TryParse(p[0], System.Globalization.NumberStyles.HexNumber, null, out uint set) ||
                            !uint.TryParse(p[1], System.Globalization.NumberStyles.HexNumber, null, out uint original))
                        {
                            continue;
                        }
                        if (set != SPI_SETSTICKYKEYS && set != SPI_SETTOGGLEKEYS && set != SPI_SETFILTERKEYS) { continue; }
                        if (Get(set - 1) == (original & ~(HOTKEYACTIVE | CONFIRMHOTKEY)))
                        {
                            Set(set, original);
                            putBack++;
                        }
                    }
                }
                catch (Exception ex) { Logger.Swallow("AccessibilityShortcuts.RecoverLeftovers", ex); }
                finally
                {
                    try { System.IO.File.Delete(path); } catch { }
                }
            }
            if (putBack > 0)
            {
                Logger.Info("[Guard] Tempo was closed in the middle of a run last time: put back " + putBack +
                            " accessibility shortcut(s) (Sticky / Toggle / Filter Keys) it had switched off.");
            }
            return putBack;
        }

        /// <summary>When Windows started, in UTC ticks (to a few ms): tells this session's note from an old one.</summary>
        private static long BootTicks() =>
            DateTime.UtcNow.Ticks - Environment.TickCount64 * TimeSpan.TicksPerMillisecond;

        private static string NotePath()
        {
            if (NotePathForTest != null) { return NotePathForTest; }
            if (GetForTest != null || SetForTest != null) { return null; }   // a test without a note of its own
            try { return System.IO.Path.Combine(Persistence.SettingsManager.GetSettingsDirectory(), NoteName); }
            catch { return null; }
        }

        /// <summary>Under the lock. What a later start needs to undo this, if nothing else gets to.</summary>
        private static void WriteNote()
        {
            string path = NotePath();
            if (path == null) { return; }
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("boot ").Append(BootTicks()).Append('\n');
                if (_sticky.HasValue) { sb.Append(SPI_SETSTICKYKEYS.ToString("X")).Append(' ').Append(_sticky.Value.ToString("X")).Append('\n'); }
                if (_toggle.HasValue) { sb.Append(SPI_SETTOGGLEKEYS.ToString("X")).Append(' ').Append(_toggle.Value.ToString("X")).Append('\n'); }
                if (_filter.HasValue) { sb.Append(SPI_SETFILTERKEYS.ToString("X")).Append(' ').Append(_filter.Value.ToString("X")).Append('\n'); }
                System.IO.File.WriteAllText(path, sb.ToString());
            }
            catch (Exception ex) { Logger.Swallow("AccessibilityShortcuts.WriteNote", ex); }
        }

        private static void DeleteNote()
        {
            string path = NotePath();
            if (path == null) { return; }
            try { System.IO.File.Delete(path); }
            catch (Exception ex) { Logger.Swallow("AccessibilityShortcuts.DeleteNote", ex); }
        }

        /// <summary>The original flags if the shortcut was switched off, null if it was left alone.</summary>
        private static uint? SuspendOne(uint get, uint set)
        {
            uint? flags = Get(get);
            if (!flags.HasValue) { return null; }
            uint f = flags.Value;
            // In use: the person relies on it — leave everything as it is.
            if ((f & ON) != 0) { return null; }
            // The shortcut already off: nothing to switch off. (CONFIRMHOTKEY on its own means nothing
            // without it, and is left alone with the rest.)
            if ((f & HOTKEYACTIVE) == 0) { return null; }
            Set(set, f & ~(HOTKEYACTIVE | CONFIRMHOTKEY));
            return f;
        }

        private static uint? Get(uint action)
        {
            if (GetForTest != null) { return GetForTest(action); }
            try
            {
                if (action == SPI_GETFILTERKEYS)
                {
                    var fk = new FILTERKEYS { cbSize = (uint)Marshal.SizeOf(typeof(FILTERKEYS)) };
                    return SystemParametersInfo(action, fk.cbSize, ref fk, 0) ? fk.dwFlags : (uint?)null;
                }
                var sk = new STICKYKEYS { cbSize = (uint)Marshal.SizeOf(typeof(STICKYKEYS)) };
                return SystemParametersInfo(action, sk.cbSize, ref sk, 0) ? sk.dwFlags : (uint?)null;
            }
            catch { return null; }
        }

        private static void Set(uint action, uint flags)
        {
            if (SetForTest != null) { SetForTest(action, flags); return; }
            try
            {
                if (action == SPI_SETFILTERKEYS)
                {
                    // Read the timings first: FILTERKEYS carries them, and writing zeros would change them.
                    var fk = new FILTERKEYS { cbSize = (uint)Marshal.SizeOf(typeof(FILTERKEYS)) };
                    SystemParametersInfo(SPI_GETFILTERKEYS, fk.cbSize, ref fk, 0);
                    fk.dwFlags = flags;
                    SystemParametersInfo(action, fk.cbSize, ref fk, 0);   // 0: not saved to the profile
                    return;
                }
                var sk = new STICKYKEYS { cbSize = (uint)Marshal.SizeOf(typeof(STICKYKEYS)), dwFlags = flags };
                SystemParametersInfo(action, sk.cbSize, ref sk, 0);
            }
            catch (Exception ex) { Logger.Swallow("AccessibilityShortcuts.Set", ex); }
        }

        // STICKYKEYS and TOGGLEKEYS share this layout.
        [StructLayout(LayoutKind.Sequential)]
        private struct STICKYKEYS
        {
            public uint cbSize;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FILTERKEYS
        {
            public uint cbSize;
            public uint dwFlags;
            public uint iWaitMSec;
            public uint iDelayMSec;
            public uint iRepeatMSec;
            public uint iBounceMSec;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint action, uint param, ref STICKYKEYS value, uint winIni);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint action, uint param, ref FILTERKEYS value, uint winIni);
    }
}
