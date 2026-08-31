using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Answers one question, cheaply: is a fullscreen game (or fullscreen video)
    /// in the foreground RIGHT NOW? Used to switch the caption stack into a
    /// low-impact mode while someone is playing — captions during games were
    /// measurably costing FPS (GPU inference bursts, per-frame window captures,
    /// 30 fps overlay animation), and every one of those can afford to ease off
    /// exactly while a game needs the machine.
    ///
    /// Detection: the foreground window's rect covers its whole monitor (the
    /// borderless-fullscreen signature every modern game and fullscreen video
    /// player uses), excluding the shell and Tempo itself. Polls every 2 s while
    /// running — negligible cost, and only runs while captions are active.
    /// </summary>
    public sealed class GamePresence : IDisposable
    {
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
        [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);

        // The shell's own "a Direct3D app owns the display EXCLUSIVELY" signal —
        // the same one Windows uses to suppress its notification toasts. Games in
        // this mode (old-style "Fullscreen" in video settings, incl. Vulkan titles
        // that acquire fullscreen-exclusive through DXGI) bypass the compositor:
        // NO ordinary window — Tempo's caption bar, or any other overlay tool that
        // doesn't inject into the game — can draw over them.
        private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;

        // The rest of the shell's notification-state answers. This is the API Windows
        // itself consults before deciding whether to put a toast on screen.
        private const int QUNS_BUSY = 2;                 // a fullscreen app owns the screen
        private const int QUNS_PRESENTATION_MODE = 4;    // presentation mode
        private const int QUNS_ACCEPTS_NOTIFICATIONS = 5;
        private const int QUNS_QUIET_TIME = 6;           // quiet hours / Focus Assist / DND
        private const int QUNS_APP = 7;                  // a fullscreen Store app

        /// <summary>
        /// True when a card must NOT be put on screen right now, with a reason.
        ///
        /// Tempo's notification cards are its own always-on-top windows, so none of
        /// Windows' own suppression applied to them. Windows hides toasts while a
        /// fullscreen app is running — that is Focus Assist's "when I'm playing a game"
        /// and "when I'm using an app in full screen mode", both ON out of the box —
        /// and Tempo ignored all of it. A card would pop over a fullscreen game (this is
        /// an auto-clicker; that is where it is used) or, worse, over a presentation,
        /// showing whatever a mirrored notification happened to contain to the room.
        ///
        /// Deliberately NOT suppressed on QUNS_QUIET_TIME. Tempo's own guidance tells
        /// users to switch Do Not Disturb ON so Windows' native toasts stay hidden and
        /// only Tempo's cards appear — so for this app quiet-time is the mirroring
        /// feature's normal operating state, not a request for silence. Honouring it
        /// here would break the feature the user turned it on for. The fullscreen and
        /// presentation states carry no such ambiguity.
        /// </summary>
        public static bool ShouldHoldNotifications(out string reason)
        {
            reason = null;
            try
            {
                if (SHQueryUserNotificationState(out int st) != 0)
                {
                    return false;       // couldn't ask — never suppress on a failed check
                }
                switch (st)
                {
                    case QUNS_RUNNING_D3D_FULL_SCREEN:
                        reason = "a fullscreen game owns the display";
                        return true;
                    // QUNS_BUSY and QUNS_APP are NOT trustworthy on their own, and taking
                    // them at face value silently disabled this whole feature.
                    //
                    // Measured on this machine: SHQueryUserNotificationState returns
                    // QUNS_BUSY permanently — no game, nothing maximised, just an animated
                    // wallpaper and a dock sitting behind the icons. Anything that keeps a
                    // screen-sized window around does it. Tempo believed it and dropped
                    // EVERY card, for ever: the Test pop-up button did nothing, and the log
                    // filled with "card suppressed — a fullscreen app is running" while no
                    // such app existed.
                    //
                    // So corroborate with the thing we can actually check: is the window in
                    // FRONT really covering a whole monitor? DetectFullscreen already
                    // answers that, and it skips the shell/desktop processes. Both must
                    // agree before a card is dropped. The two states below stay unqualified
                    // — an exclusive D3D game and presentation mode are unambiguous.
                    case QUNS_BUSY:
                    case QUNS_APP:
                        if (!DetectFullscreen())
                        {
                            return false;
                        }
                        reason = "a fullscreen app is running";
                        return true;
                    case QUNS_PRESENTATION_MODE:
                        reason = "presentation mode is on";
                        return true;
                    default:
                        return false;   // includes QUNS_ACCEPTS_NOTIFICATIONS and QUIET_TIME
                }
            }
            catch { return false; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int L, T, R, B; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int Size;
            public RECT Monitor;
            public RECT Work;
            public uint Flags;
        }

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        private static readonly string[] IgnoreProcesses =
        {
            "explorer", "tempo", "searchhost", "shellexperiencehost", "lockapp",
            "startmenuexperiencehost", "dwm"
        };

        private System.Threading.Timer _timer;
        private volatile bool _fullscreen;
        private int _guard;
        private int _exitStreak;

        /// <summary>
        /// Leaving game mode requires this many CONSECUTIVE non-fullscreen polls
        /// (~6 s). Real play flickers the fullscreen signature constantly — a match
        /// loading screen, the Ubisoft overlay, a popup — and the 1.0.304 log shows
        /// enter/exit flapping as fast as every 2 s during Rainbow Six. Each flap
        /// rebuilt the speech processor and resumed window captures mid-match, which
        /// is a freeze recipe, not a courtesy. Entering game mode stays immediate
        /// (protecting the game matters more than a false positive); only the exit
        /// is debounced.
        /// </summary>
        private const int ExitStableTicks = 3;

        /// <summary>True while a fullscreen app is in the foreground.</summary>
        public bool FullscreenActive => _fullscreen;

        /// <summary>Fired (worker thread) when the fullscreen state flips.</summary>
        public event Action<bool> FullscreenChanged;

        private volatile bool _exclusive;
        private int _exclusiveStreak;

        /// <summary>
        /// True while the fullscreen app holds the display in EXCLUSIVE mode —
        /// the state in which the caption bar physically cannot be shown. The
        /// host uses this to tell the user WHY captions are invisible and that
        /// switching the game to Borderless fixes it.
        /// </summary>
        public bool ExclusiveFullscreen => _exclusive;

        /// <summary>Fired (worker thread) when the exclusive state flips.</summary>
        public event Action<bool> ExclusiveChanged;

        public void Start()
        {
            if (_timer == null)
            {
                _timer = new System.Threading.Timer(Tick, null, 1000, 2000);
            }
        }

        public void Stop()
        {
            try { _timer?.Dispose(); } catch { }
            _timer = null;
            _exitStreak = 0;
            _exclusiveStreak = 0;
            if (_exclusive)
            {
                _exclusive = false;
                try { ExclusiveChanged?.Invoke(false); } catch { }
            }
            if (_fullscreen)
            {
                _fullscreen = false;
                try { FullscreenChanged?.Invoke(false); } catch { }
            }
        }

        public void Dispose() => Stop();

        private void Tick(object state)
        {
            if (System.Threading.Interlocked.CompareExchange(ref _guard, 1, 0) != 0)
            {
                return;
            }
            try
            {
                bool now = DetectFullscreen();
                if (now)
                {
                    _exitStreak = 0;
                    if (!_fullscreen)
                    {
                        _fullscreen = true;
                        try { FullscreenChanged?.Invoke(true); } catch { }
                    }
                }
                else if (_fullscreen)
                {
                    // Debounced exit: one stray poll (loading screen, overlay,
                    // brief alt-tab) must not bounce the whole caption stack.
                    if (++_exitStreak >= ExitStableTicks)
                    {
                        _exitStreak = 0;
                        _fullscreen = false;
                        try { FullscreenChanged?.Invoke(false); } catch { }
                    }
                }
                else
                {
                    _exitStreak = 0;
                }

                // Exclusive-mode check, only meaningful while fullscreen. Two
                // consecutive readings (~4 s) to enter — the shell state can
                // flicker during mode changes — but exit immediately, so the
                // "captions visible again" moment isn't delayed.
                bool exclusiveNow = false;
                if (_fullscreen)
                {
                    try
                    {
                        if (SHQueryUserNotificationState(out int st) == 0)
                        {
                            exclusiveNow = st == QUNS_RUNNING_D3D_FULL_SCREEN;
                        }
                    }
                    catch { }
                }
                if (exclusiveNow)
                {
                    if (++_exclusiveStreak >= 2 && !_exclusive)
                    {
                        _exclusive = true;
                        try { ExclusiveChanged?.Invoke(true); } catch { }
                    }
                }
                else
                {
                    _exclusiveStreak = 0;
                    if (_exclusive)
                    {
                        _exclusive = false;
                        try { ExclusiveChanged?.Invoke(false); } catch { }
                    }
                }
            }
            catch { }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _guard, 0);
            }
        }

        private static bool DetectFullscreen()
        {
            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero) { return false; }

            GetWindowThreadProcessId(h, out uint pid);
            if (pid == 0) { return false; }
            string name = "";
            try
            {
                using (var p = System.Diagnostics.Process.GetProcessById((int)pid))
                {
                    name = (p.ProcessName ?? "").ToLowerInvariant();
                }
            }
            catch { return false; }
            foreach (string ig in IgnoreProcesses)
            {
                if (name == ig) { return false; }
            }

            if (!GetWindowRect(h, out RECT wr)) { return false; }
            IntPtr mon = MonitorFromWindow(h, MONITOR_DEFAULTTONEAREST);
            if (mon == IntPtr.Zero) { return false; }
            var mi = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(mon, ref mi)) { return false; }

            // Covers the WHOLE monitor (not just the work area) within a hair:
            // the borderless-fullscreen signature.
            return wr.L <= mi.Monitor.L + 2 && wr.T <= mi.Monitor.T + 2 &&
                   wr.R >= mi.Monitor.R - 2 && wr.B >= mi.Monitor.B - 2;
        }
    }
}
