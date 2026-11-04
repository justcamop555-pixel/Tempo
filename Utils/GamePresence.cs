using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Answers one question, cheaply: is a fullscreen GAME in the foreground RIGHT NOW? Used to switch the caption
    /// stack into a low-impact mode while someone is playing — captions during games were measurably costing FPS
    /// (GPU inference bursts, per-frame window captures, 30 fps overlay animation), and every one of those can afford
    /// to ease off exactly while a game needs the machine.
    ///
    /// Detection: the foreground window covers its whole monitor and is not an app window that merely fills it
    /// (<see cref="IsFullscreen"/>) — the borderless-fullscreen signature every modern game and video player uses —
    /// excluding the desktop and Tempo itself, and, for game mode, browsers, video players and slideshows
    /// (<see cref="NotGames"/>), which are full screen without being games. Start, search, Game Bar or a snip in
    /// front change nothing either way (<see cref="Overlays"/>). Polls every 2 s while running — negligible cost,
    /// and only runs while captions are active.
    /// </summary>
    public sealed class GamePresence : IDisposable
    {
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
        [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr h, ref POINT p);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumChildProc callback, IntPtr lParam);

        private delegate bool EnumChildProc(IntPtr h, IntPtr lParam);

        private const int GWL_STYLE = -16;
        private const int WS_CAPTION = 0x00C00000;   // a title bar: WS_BORDER | WS_DLGFRAME
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_MAXIMIZE = 0x01000000;

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
        /// <summary>For the tests: a hold reason to report (non-null = hold), instead of asking Windows.</summary>
        internal static Func<string> HoldForTest;

        public static bool ShouldHoldNotifications(out string reason)
        {
            reason = null;
            if (HoldForTest != null)
            {
                reason = HoldForTest();
                return reason != null;
            }
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
        internal struct RECT
        {
            public int L, T, R, B;
            public RECT(int l, int t, int r, int b) { L = l; T = t; R = r; B = b; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int Size;
            public RECT Monitor;
            public RECT Work;
            public uint Flags;
        }

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        // The desktop and Tempo: nobody is playing. "tempoclicker" is the installed exe's name (the Discord
        // rename): "tempo" alone let Tempo's own screen-sized windows — a maximised main window, the point
        // picker — count as a game. Tempo is also skipped by process id, whatever it is called.
        private static readonly string[] IgnoreProcesses = { "explorer", "tempo", "tempoclicker" };

        /// <summary>
        /// Shell surfaces that open OVER whatever is in front: Start, search, the action centre, the touch keyboard,
        /// a snip, Game Bar (Win+G, itself a screen-sized window), the lock screen. They change nothing either way —
        /// Game Bar on the desktop is not a game, and Game Bar over a game must not end game mode once it has been
        /// open for the three polls an exit takes.
        /// </summary>
        private static readonly string[] Overlays =
        {
            "searchhost", "searchapp", "shellexperiencehost", "startmenuexperiencehost", "textinputhost", "lockapp",
            "dwm", "screenclippinghost", "snippingtool", "gamebar", "gamebarftserver"
        };

        private static readonly int OwnPid = System.Diagnostics.Process.GetCurrentProcess().Id;

        /// <summary>
        /// Full screen but not a GAME: a browser (YouTube full screen, F11), a video player, a slideshow, a call, a
        /// viewer. They still hold notification cards (ShouldHoldNotifications) — but game mode slowed the captions
        /// down, turned beam search off and stopped re-raising the bar over exactly what people watch WITH captions,
        /// and said "Game mode" over a video. A Store app is named by its own process, not ApplicationFrameHost
        /// (<see cref="FrontWindow"/>), so Movies &amp; TV is a player and a Store game is still a game.
        /// </summary>
        private static readonly string[] NotGames =
        {
            "chrome", "msedge", "firefox", "opera", "opera_gx", "brave", "vivaldi", "librewolf", "waterfox", "arc",
            "yandex", "iexplore", "thorium", "chromium", "zen", "msedgewebview2",
            "vlc", "mpc-hc", "mpc-hc64", "mpc-be", "mpc-be64", "potplayer", "potplayermini", "potplayermini64",
            "wmplayer", "mpv", "mpvnet", "kodi", "plex", "plexhtpc", "jellyfinmediaplayer", "stremio", "smplayer",
            "kmplayer", "kmplayer64", "gom", "video.ui", "microsoft.media.player", "netflix", "primevideo",
            "powerpnt", "pptview", "soffice.bin", "teams", "ms-teams", "zoom", "discord", "telegram", "obs64", "obs32",
            "spotify", "acrord32", "acrobat", "sumatrapdf", "photos", "microsoft.photos", "i_view64", "i_view32",
            "imageglass", "code", "windowsterminal"
        };

        /// <summary>True for a full-screen app that is not a game (see <see cref="NotGames"/>).</summary>
        internal static bool IsNotAGame(string process)
        {
            if (string.IsNullOrEmpty(process)) { return false; }
            foreach (string n in NotGames)
            {
                if (process == n) { return true; }
            }
            return false;
        }

        /// <summary>
        /// The full-screen test on its parts: the window covers its whole monitor (within a hair) — and is not an app
        /// window that merely fills it. A MAXIMISED window hangs a few pixels past every edge, so on a monitor with no
        /// taskbar — a second screen, or an auto-hidden taskbar — a plain maximised browser or chat app covered "the
        /// whole monitor" and was a game; a maximised window with a frame (a title bar, or a resizing border, as the
        /// custom-drawn ones have) is an app. A window sized to the monitor with its title bar showing is too. A game
        /// that keeps the title-bar style but pushes the bar off screen covers it with its client area, and counts.
        /// </summary>
        internal static bool IsFullscreen(RECT window, RECT client, RECT monitor, int style)
        {
            bool framed = (style & WS_CAPTION) == WS_CAPTION || (style & WS_THICKFRAME) != 0;
            if ((style & WS_MAXIMIZE) != 0 && framed) { return false; }
            if (!Covers(window, monitor)) { return false; }
            return (style & WS_CAPTION) != WS_CAPTION || Covers(client, monitor);
        }

        private static bool Covers(RECT r, RECT m) => r.L <= m.L + 2 && r.T <= m.T + 2 && r.R >= m.R - 2 && r.B >= m.B - 2;

        private enum Front { No, Fullscreen, Overlay }

        /// <summary>The app game mode was entered for (a process name, never a window title), or null.</summary>
        public string FullscreenApp { get; private set; }

        /// <summary>The process game mode was entered for, 0 when none — which Roblox client, so the host can name the
        /// game it is in.</summary>
        public int FullscreenPid { get; private set; }

        /// <summary>
        /// Set by the host: true when a full-screen app is KNOWN to be at its own menu, not in a game — Roblox on its
        /// home screen (its log says so). Roblox draws its home screen with the game engine, full screen, and game mode
        /// came on over a menu. It answers false whenever it can't tell, so a game is never missed.
        /// </summary>
        public static Func<string, int, bool> AtMenu;

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
                // A GAME: full screen, and not a browser, a player or a slideshow (NotGames), nor a game at its menu.
                Front front = FrontWindow(out string app, out uint pid);
                bool now = front == Front.Fullscreen && !IsNotAGame(app) && !IsAtMenu(app, (int)pid);
                if (front == Front.Overlay)
                {
                    // Start, search or Game Bar over whatever was in front: the state stays as it is.
                }
                else if (now)
                {
                    _exitStreak = 0;
                    if (!_fullscreen)
                    {
                        FullscreenApp = app;
                        FullscreenPid = (int)pid;
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

        /// <summary>Is ANY full-screen app in front — a game, a video, a slideshow? (Notification cards wait for all of them.)</summary>
        private static bool DetectFullscreen() => FrontWindow(out _, out _) == Front.Fullscreen;

        /// <summary>The host's <see cref="AtMenu"/>, never throwing: a failed look is "in a game".</summary>
        private static bool IsAtMenu(string app, int pid)
        {
            try { return AtMenu != null && app != null && AtMenu(app, pid); }
            catch { return false; }
        }

        /// <summary>What is in front: a full-screen window (<see cref="IsFullscreen"/>) and <paramref name="process"/>,
        /// whose it is (<paramref name="pid"/> its process); a shell overlay (<see cref="Overlays"/>); or neither.</summary>
        private static Front FrontWindow(out string process, out uint pid) => Judge(GetForegroundWindow(), out process, out pid);

        /// <summary>
        /// The process name of the full-screen app in front (game mode's own test), or null. For anti-AFK, which never
        /// takes the focus from one: a game in exclusive full screen minimises the moment it loses it.
        /// </summary>
        internal static string FullscreenAppInFront() => FrontWindow(out string process, out _) == Front.Fullscreen ? process : null;

        /// <summary><see cref="FrontWindow"/> for any window (the tests judge windows that are not in front).</summary>
        private static Front Judge(IntPtr h, out string process) => Judge(h, out process, out _);

        private static Front Judge(IntPtr h, out string process, out uint pid)
        {
            process = null;
            pid = 0;
            if (h == IntPtr.Zero) { return Front.No; }

            GetWindowThreadProcessId(h, out uint owner);
            if (owner == 0 || owner == OwnPid) { return Front.No; }
            string name = ProcessName(owner);
            if (name == null) { return Front.No; }
            // A Store app's window belongs to ApplicationFrameHost; the app itself owns a child of it.
            if (name == "applicationframehost")
            {
                uint appPid = 0;
                EnumChildWindows(h, (c, _) =>
                {
                    GetWindowThreadProcessId(c, out uint cp);
                    if (cp == 0 || cp == owner) { return true; }
                    appPid = cp;
                    return false;
                }, IntPtr.Zero);
                if (appPid != 0)
                {
                    string appName = ProcessName(appPid);
                    if (appName != null) { name = appName; owner = appPid; }
                }
            }
            pid = owner;
            foreach (string ig in IgnoreProcesses)
            {
                if (name == ig) { return Front.No; }
            }
            foreach (string o in Overlays)
            {
                if (name == o) { return Front.Overlay; }
            }

            if (!GetWindowRect(h, out RECT wr) || !GetClientRect(h, out RECT cr)) { return Front.No; }
            var topLeft = new POINT { X = cr.L, Y = cr.T };
            var bottomRight = new POINT { X = cr.R, Y = cr.B };
            if (!ClientToScreen(h, ref topLeft) || !ClientToScreen(h, ref bottomRight)) { return Front.No; }
            IntPtr mon = MonitorFromWindow(h, MONITOR_DEFAULTTONEAREST);
            if (mon == IntPtr.Zero) { return Front.No; }
            var mi = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(mon, ref mi)) { return Front.No; }

            // Covers the WHOLE monitor (not just the work area) within a hair, and is not merely an app window
            // filling it: the borderless-fullscreen signature.
            if (!IsFullscreen(wr, new RECT(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y), mi.Monitor,
                              GetWindowLong(h, GWL_STYLE)))
            {
                return Front.No;
            }
            process = name;
            return Front.Fullscreen;
        }

        private static string ProcessName(uint pid)
        {
            try
            {
                using (var p = System.Diagnostics.Process.GetProcessById((int)pid))
                {
                    return (p.ProcessName ?? "").ToLowerInvariant();
                }
            }
            catch { return null; }
        }
    }
}
