using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// The main window's way in and out (2026-11-08, the user: "make new animation for … exit … and others").
    ///
    /// Out: Exit took the window off the screen in one frame — Tempo hid it first thing and then wound down behind it.
    /// Now a picture of the window sinks a little and fades where it stood, 220 ms, while the real window has
    /// already gone and the clean-up runs.
    ///
    /// In: coming back from the tray (and the first open after start-up), the window is painted while cloaked and then
    /// appeared at once. Now its picture rises the last few pixels into place as it fades in — like a page on a tab
    /// switch — and the real window is uncloaked under the last frame, which is the window itself, so nothing changes
    /// when it takes over.
    ///
    /// Both are pictures, not the window: fading the real one would make it a layered window, which throws its painted
    /// surface away (the half-drawn open of 2026-10-11). The picture is built once and handed to the compositor; each
    /// frame only moves it and sets its alpha (GdiDib.Move), so it runs at the screen's rate whatever the window's size.
    /// (Until 2026-11-12 it shrank/grew, redrawn with GDI+ every frame — 3–4 frames a way in on the user's PC: a dark
    /// window, a doubled one, then Tempo.) Each runs on a thread of its own with its own window, so the UI thread's
    /// work — the exit's clean-up, the open's first jobs — can't make it stutter, and it can't hold that work up. The
    /// picture never takes the focus and never shows in the taskbar. With Windows' "Animation effects" off, neither plays.
    /// </summary>
    internal static class WindowMotion
    {
        private const double ExitMs = 220, OpenMs = 210;

        private static Thread _exitThread;

        /// <summary>For Live debug: how the last way in and way out went, or null before the first.</summary>
        public static string LastWayIn, LastWayOut;

        /// <summary>Bumped by <see cref="CancelOpen"/>: a way in started before the bump ends at its next frame.</summary>
        private static int _openGeneration;

        /// <summary>The way in's picture while it is up (its own thread made it), so a cancel can take it off the screen.</summary>
        private static IntPtr _openPopup;

        /// <summary>
        /// Ends a way in that is still playing — the window's watchdog has put the window up without it (MainForm
        /// RevealWithMotion): its picture goes at its next frame instead of lying over the window that has taken over.
        /// And off the screen at once: the watchdog fires because the animation's thread is held up, and its picture takes
        /// clicks (SS_NOTIFY), so left up it would sit over the real window catching them. Hidden without waiting on that
        /// thread (SWP_ASYNCWINDOWPOS); the thread destroys it when it gets going again.
        /// </summary>
        public static void CancelOpen()
        {
            Interlocked.Increment(ref _openGeneration);
            IntPtr popup = Volatile.Read(ref _openPopup);
            if (popup == IntPtr.Zero) { return; }
            try
            {
                SetWindowPos(popup, IntPtr.Zero, 0, 0, 0, 0,
                             SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_HIDEWINDOW | SWP_ASYNCWINDOWPOS);
            }
            catch { }
        }

        private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_HIDEWINDOW = 0x0080,
                           SWP_ASYNCWINDOWPOS = 0x4000;

        private static string Stamp() => DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>How the window's picture was taken by the last Capture: off its surface, or rendered again by DWM.</summary>
        private static string _lastCaptureHow = "";

        /// <summary>
        /// Puts the exit animation over <paramref name="form"/> and returns once its first frame is on the screen —
        /// the caller then hides the window at once (DWM's own fade is switched off for it here). False when nothing
        /// was started: animations off, the window not on the screen, the picture couldn't be taken.
        /// </summary>
        public static bool PlayExit(Form form)
        {
            try
            {
                if (!Motion.Enabled || form == null || form.IsDisposed || !form.IsHandleCreated || !form.Visible
                    || form.WindowState == FormWindowState.Minimized || IsCloaked(form.Handle))
                {
                    LastWayOut = Stamp() + " · not played (" + (!Motion.Enabled ? "animations off in Windows" : "the window was not on screen") + ")";
                    return false;
                }
                var clock = System.Diagnostics.Stopwatch.StartNew();
                GdiDib picture = Capture(form.Handle, out Rectangle bounds);
                long pictureMs = clock.ElapsedMilliseconds;
                if (picture == null) { LastWayOut = Stamp() + " · not played (no picture of the window)"; return false; }
                var player = new Player(picture, bounds, form.Handle, exit: true,
                                        rounded: Utils.WindowsVersion.IsWindows11OrNewer && form.WindowState != FormWindowState.Maximized,
                                        dpi: form.DeviceDpi, clickThrough: true);
                var t = new Thread(player.Run) { IsBackground = true, Name = "Tempo exit animation" };
                t.Start();
                bool up = player.Settle();
                if (clock.ElapsedMilliseconds > 80)
                {
                    Utils.Logger.Info("[motion] the way-out animation took " + clock.ElapsedMilliseconds + " ms to start (picture " + pictureMs
                                      + " ms)" + (up ? "." : " — too long; the window went without it."));
                }
                LastWayOut = Stamp() + " · " + (up ? "started in " + clock.ElapsedMilliseconds + " ms" : "too slow to start, skipped")
                             + " (picture " + pictureMs + " ms, " + _lastCaptureHow + ")";
                if (!up) { return false; }   // too slow to start: it never shows
                _exitThread = t;
                SetTransitions(form.Handle, false);
                return true;
            }
            catch (Exception ex)
            {
                Utils.Logger.Swallow("WindowMotion.PlayExit", ex);
                return false;
            }
        }

        /// <summary>
        /// The way out to the tray: the same picture as Exit sinks and fades where the window stood, <paramref name="hide"/>
        /// takes the real window away at once under its first frame, and Windows' own show/hide transitions are put back
        /// for the window's next appearance (Exit leaves them off — the process is ending). True when the picture played.
        /// <paramref name="hide"/> runs either way.
        /// </summary>
        public static bool PlayHide(Form form, Action hide)
        {
            bool played = PlayExit(form);
            try { hide?.Invoke(); }
            finally
            {
                if (played && form != null && !form.IsDisposed && form.IsHandleCreated)
                {
                    try { SetTransitions(form.Handle, true); } catch { }
                }
            }
            return played;
        }

        /// <summary>
        /// Lets an exit animation finish (at most <paramref name="maxMs"/>) before the process ends: Program waits here
        /// on its way out, so a fast clean-up does not cut it off half-way.
        /// </summary>
        public static void WaitForExitAnimation(int maxMs)
        {
            try { _exitThread?.Join(maxMs); }
            catch { }
        }

        /// <summary>
        /// Plays the way in over <paramref name="form"/>, which is shown, painted and CLOAKED. At the end the window is
        /// uncloaked under the last frame and <paramref name="uncloaked"/> runs on the UI thread. True when started —
        /// the caller then leaves the uncloaking to it; false when not, and the caller uncloaks as before.
        /// </summary>
        public static bool PlayOpen(Form form, Action uncloaked)
        {
            try
            {
                if (!Motion.Enabled || form == null || form.IsDisposed || !form.IsHandleCreated || !IsWindowVisible(form.Handle)
                    || form.WindowState == FormWindowState.Minimized)
                {
                    LastWayIn = Stamp() + " · not played (" + (!Motion.Enabled ? "animations off in Windows" : "the window was not shown") + ")";
                    return false;
                }
                var clock = System.Diagnostics.Stopwatch.StartNew();
                GdiDib picture = Capture(form.Handle, out Rectangle bounds);
                long pictureMs = clock.ElapsedMilliseconds;
                if (picture == null) { LastWayIn = Stamp() + " · not played (no picture of the window)"; return false; }
                IntPtr hwnd = form.Handle;
                var player = new Player(picture, bounds, hwnd, exit: false,
                                        rounded: Utils.WindowsVersion.IsWindows11OrNewer && form.WindowState != FormWindowState.Maximized,
                                        dpi: form.DeviceDpi, clickThrough: false);
                // At the end, on the animation's thread: the real window comes back under the last frame. The UI thread is
                // asked to do it (its own record of the cloak stays true); if it is busy for a second, it is done from here.
                player.AtEnd = () =>
                {
                    using (var done = new ManualResetEventSlim(false))
                    {
                        bool asked = false;
                        try
                        {
                            if (!form.IsDisposed && form.IsHandleCreated)
                            {
                                form.BeginInvoke(new Action(() =>
                                {
                                    try { uncloaked?.Invoke(); }
                                    finally { try { done.Set(); } catch { } }
                                }));
                                asked = true;
                            }
                        }
                        catch { }
                        if (!asked || !done.Wait(1000)) { SetCloak(hwnd, false); }
                    }
                };
                var t = new Thread(player.Run) { IsBackground = true, Name = "Tempo open animation" };
                t.Start();
                // Too slow to start: the caller uncloaks the window now, and the animation never appears over it.
                bool up = player.Settle();
                if (clock.ElapsedMilliseconds > 60)
                {
                    Utils.Logger.Info("[motion] the way-in animation took " + clock.ElapsedMilliseconds + " ms to start (picture " + pictureMs
                                      + " ms)" + (up ? "." : " — too long; the window came up without it."));
                }
                LastWayIn = Stamp() + " · " + (up ? "started in " + clock.ElapsedMilliseconds + " ms" : "too slow to start, skipped")
                            + " (picture " + pictureMs + " ms, " + _lastCaptureHow + ")";
                return up;
            }
            catch (Exception ex)
            {
                Utils.Logger.Swallow("WindowMotion.PlayOpen", ex);
                return false;
            }
        }

        /// <summary>The window as it is now, from its own surface (cloaked or not), cut to what the screen shows of it.</summary>
        private static GdiDib Capture(IntPtr hwnd, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            if (!GetWindowRect(hwnd, out RECT wr)) { return null; }
            Rectangle win = Rectangle.FromLTRB(wr.L, wr.T, wr.R, wr.B);
            if (win.Width < 16 || win.Height < 16 || win.Width > 16384 || win.Height > 16384) { return null; }
            Rectangle shown = win;
            if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT eb, Marshal.SizeOf(typeof(RECT))) == 0)
            {
                Rectangle e = Rectangle.FromLTRB(eb.L, eb.T, eb.R, eb.B);
                e.Intersect(win);
                if (e.Width >= 16 && e.Height >= 16) { shown = e; }
            }
            GdiDib whole = new GdiDib(win.Width, win.Height);
            try
            {
                // Straight off the window's own surface first — a copy, a few ms. PrintWindow (asking DWM to render the
                // window again) took 66–113 ms, and the way in waits for the picture before it can start (logged
                // "[motion] … picture 113 ms"). A copy that came back empty (all black: no Tempo theme is) falls back to it.
                bool ok = false;
                IntPtr wdc = GetWindowDC(hwnd);
                if (wdc != IntPtr.Zero)
                {
                    try { ok = GdiDib.BitBlt(whole.Dc, 0, 0, win.Width, win.Height, wdc, 0, 0, GdiDib.SRCCOPY); }
                    finally { ReleaseDC(hwnd, wdc); }
                }
                _lastCaptureHow = "copied off its surface";
                if (!ok || LooksEmpty(whole))
                {
                    ok = PrintWindow(hwnd, whole.Dc, PW_RENDERFULLCONTENT);
                    _lastCaptureHow = "the first copy came back empty, so Windows drew it again";
                }
                if (!ok) { whole.Dispose(); return null; }
                if (shown != win)
                {
                    var cut = new GdiDib(shown.Width, shown.Height);
                    GdiDib.BitBlt(cut.Dc, 0, 0, shown.Width, shown.Height, whole.Dc, shown.X - win.X, shown.Y - win.Y, GdiDib.SRCCOPY);
                    whole.Dispose();
                    whole = cut;
                }
                whole.MakeOpaque();
                bounds = shown;
                return whole;
            }
            catch
            {
                whole.Dispose();
                return null;
            }
        }

        /// <summary>Whether a picture is black all over (8×8 points sampled): a copy that got nothing.</summary>
        private static bool LooksEmpty(GdiDib picture)
        {
            try
            {
                var px = new int[1];
                for (int sy = 0; sy < 8; sy++)
                {
                    for (int sx = 0; sx < 8; sx++)
                    {
                        int x = (int)((sx + 0.5) * picture.Width / 8), y = (int)((sy + 0.5) * picture.Height / 8);
                        Marshal.Copy(picture.Bits + (y * picture.Width + x) * 4, px, 0, 1);
                        if ((px[0] & 0x00FFFFFF) != 0) { return false; }
                    }
                }
                return true;
            }
            catch { return true; }
        }

        /// <summary>One animation: its own popup window and its own loop, on its own thread.</summary>
        private sealed class Player
        {
            private readonly GdiDib _picture;
            private readonly Rectangle _bounds;
            private readonly IntPtr _window;
            private readonly bool _exit, _rounded, _clickThrough;
            private readonly int _margin, _radius, _travel;
            public readonly ManualResetEventSlim Ready = new ManualResetEventSlim(false);
            public Action AtEnd;

            // 0 starting, 1 shown, 2 given up on by the caller. Whichever side moves it off 0 first decides: a caller
            // that stopped waiting can never be left with a popup appearing afterwards, nor think one is up that isn't.
            private int _state;
            public bool Shown => Volatile.Read(ref _state) == 1;

            /// <summary>The caller, done waiting: true if the popup is up (it will play), false if it never will be.</summary>
            public bool Settle()
            {
                Ready.Wait(250);
                return Interlocked.CompareExchange(ref _state, 2, 0) == 1 || Volatile.Read(ref _state) == 1;
            }

            public Player(GdiDib picture, Rectangle bounds, IntPtr window, bool exit, bool rounded, int dpi, bool clickThrough)
            {
                _picture = picture;
                _bounds = bounds;
                _window = window;
                _exit = exit;
                _rounded = rounded;
                _clickThrough = clickThrough;
                double scale = Math.Max(1.0, dpi / 96.0);
                _margin = (int)Math.Round(18 * scale);
                _radius = rounded ? (int)Math.Round(8 * scale) : 0;
                _travel = (int)Math.Round((exit ? 10 : 14) * scale);
            }

            public void Run()
            {
                IntPtr popup = IntPtr.Zero;
                GdiDib card = null;
                try
                {
                    int cw = _bounds.Width + 2 * _margin, ch = _bounds.Height + 2 * _margin;
                    var at = new Point(_bounds.X - _margin, _bounds.Y - _margin);
                    // Out: the window's picture must be up whole before the window goes, so it is built first. In: the
                    // first frame is invisible anyway, so the popup goes up at once with an empty one and the UI thread
                    // is let go; the picture is built after, and the clock starts once it is ready.
                    if (_exit) { card = BuildCard(); }

                    int ex = WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | (_clickThrough ? WS_EX_TRANSPARENT : 0);
                    bool topmost = (GetWindowLong(_window, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;
                    if (topmost) { ex |= WS_EX_TOPMOST; }
                    // A Static answers hit tests "transparent" unless SS_NOTIFY: the way in's picture must catch a click made
                    // during it (the window under it is still cloaked, so the click would land on whatever is behind
                    // Tempo); the way out's lets clicks through to what is behind, as the window is going.
                    popup = CreateWindowEx(ex, "Static", "", WS_POPUP | (_clickThrough ? 0 : SS_NOTIFY), at.X, at.Y, cw, ch,
                                           IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                    if (popup == IntPtr.Zero) { return; }
                    if (!_exit) { Volatile.Write(ref _openPopup, popup); }
                    try { int on = 1; DwmSetWindowAttribute(popup, DWMWA_TRANSITIONS_FORCEDISABLED, ref on, sizeof(int)); } catch { }

                    double duration = _exit ? ExitMs : OpenMs;
                    Point start = Place(at, 0.0);
                    if (card != null)
                    {
                        if (!card.Push(popup, start, Alpha(0.0), true)) { return; }
                    }
                    else
                    {
                        using (var empty = new GdiDib(cw, ch))   // a new picture is all transparent
                        {
                            if (!empty.Push(popup, start, 0, true)) { return; }
                        }
                    }
                    if (Interlocked.CompareExchange(ref _state, 1, 0) != 0) { return; }   // the caller gave up waiting
                    SetWindowPos(popup, PlaceAbove(_window, popup, topmost), start.X, start.Y, cw, ch,
                                 SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_NOOWNERZORDER);
                    Ready.Set();
                    if (card == null)
                    {
                        card = BuildCard();
                        // Handed over once, still invisible; every frame after only moves and fades it.
                        if (!card.Push(popup, start, Alpha(0.0), true)) { throw new InvalidOperationException("the picture could not be shown"); }
                    }

                    // Each frame places the finished picture and sets its alpha — the compositor does the rest. The picture
                    // used to be redrawn (scaled with GDI+) for every frame: tens of milliseconds a frame for a whole window,
                    // and GDI+ waits on a process-wide lock while the UI thread paints, so the way in showed three or four
                    // frames ~90 ms apart (filmed 2026-11-11: 274, 365, 408 ms) — a dark window, a doubled one, then Tempo.
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    int frames = 0;
                    int generation = Volatile.Read(ref _openGeneration);
                    while (true)
                    {
                        Pump();
                        double t = clock.Elapsed.TotalMilliseconds / duration;
                        if (t >= 1.0) { break; }
                        // The window's watchdog put the window up without this way in: it goes now.
                        if (!_exit && Volatile.Read(ref _openGeneration) != generation) { break; }
                        GdiDib.Move(popup, Place(at, t), Alpha(t));
                        frames++;
                        WaitForFrame(clock);
                    }
                    GdiDib.Move(popup, Place(at, 1.0), Alpha(1.0));
                    string played = ", " + (frames + 1) + " frames in " + (long)clock.Elapsed.TotalMilliseconds + " ms";
                    if (_exit) { LastWayOut = (LastWayOut ?? "") + played; } else { LastWayIn = (LastWayIn ?? "") + played; }

                    if (AtEnd != null)
                    {
                        AtEnd();
                        // Let the real window be composed before its picture goes, so there is no frame without either.
                        FramePacer.WaitFrame(50);
                        FramePacer.WaitFrame(50);
                    }
                }
                catch (Exception ex)
                {
                    Utils.Logger.Swallow("WindowMotion.Run", ex);
                    // An open that failed half-way must not leave the window cloaked.
                    if (!_exit && Shown && AtEnd != null) { try { AtEnd(); } catch { } }
                }
                finally
                {
                    if (popup != IntPtr.Zero)
                    {
                        Interlocked.CompareExchange(ref _openPopup, IntPtr.Zero, popup);
                        try { DestroyWindow(popup); } catch { }
                    }
                    card?.Dispose();
                    _picture.Dispose();
                    Ready.Set();
                }
            }

            /// <summary>
            /// The window's picture with its corners rounded as Windows 11 rounds them, on a soft shadow — worked out pixel
            /// by pixel, once. GDI+ built it before (anti-aliased rings, a texture fill) and took tens of milliseconds,
            /// longer still while the UI thread was painting; this is a copy and arithmetic on the margin and the corners.
            /// </summary>
            private GdiDib BuildCard()
            {
                int w = _bounds.Width, h = _bounds.Height, m = _margin;
                int cw = w + 2 * m, ch = h + 2 * m;
                var card = new GdiDib(cw, ch);   // all transparent
                GdiDib.BitBlt(card.Dc, m, m, w, h, _picture.Dc, 0, 0, GdiDib.SRCCOPY);   // the window, opaque

                double r = Math.Min(_radius, Math.Min(w, h) / 2.0);
                int shift = Math.Max(1, m / 6);   // the shadow sits a little lower than the window, as light from above
                // Shadow strength by distance from the window's (lowered) edge, the old look: rings of 5/255 every 2 px.
                var shadowAt = new byte[m + 2];
                for (int d = 0; d < shadowAt.Length; d++)
                {
                    double rings = d > m ? 0 : (m - d) / 2.0 + 1;
                    shadowAt[d] = (byte)Math.Round(255 * (1 - Math.Pow(1 - 5 / 255.0, rings)));
                }
                byte Shadow(double px, double py)
                {
                    double d = Sdf(px, py, m, m + shift, w, h, r);
                    if (d <= 0) { return shadowAt[0]; }
                    int i = (int)d;
                    return i >= shadowAt.Length - 1 ? (byte)0 : (byte)(shadowAt[i] + (shadowAt[i + 1] - shadowAt[i]) * (d - i));
                }

                var row = new int[cw];
                for (int y = 0; y < ch; y++)
                {
                    IntPtr line = card.Bits + y * cw * 4;
                    bool bodyRow = y >= m && y < m + h;
                    bool cornerRow = bodyRow && r > 0 && (y < m + r || y >= m + h - r);
                    if (bodyRow && !cornerRow)
                    {
                        // Only the side margins: the window fills the middle.
                        for (int x = 0; x < m; x++)
                        {
                            row[x] = Shadow(x + 0.5, y + 0.5) << 24;
                            row[cw - 1 - x] = Shadow(cw - 1 - x + 0.5, y + 0.5) << 24;
                        }
                        Marshal.Copy(row, 0, line, m);
                        Marshal.Copy(row, cw - m, line + (cw - m) * 4, m);
                        continue;
                    }
                    Marshal.Copy(line, row, 0, cw);
                    for (int x = 0; x < cw; x++)
                    {
                        bool inBody = bodyRow && x >= m && x < m + w;
                        if (inBody && !(cornerRow && (x < m + r || x >= m + w - r))) { continue; }   // plain window
                        byte s = Shadow(x + 0.5, y + 0.5);
                        if (!inBody) { row[x] = s << 24; continue; }
                        // A corner: the window where the rounded edge covers the pixel, the shadow where it does not.
                        double cover = Math.Max(0, Math.Min(1, 0.5 - Sdf(x + 0.5, y + 0.5, m, m, w, h, r)));
                        int p = row[x];
                        int b = (int)((p & 0xFF) * cover), g = (int)(((p >> 8) & 0xFF) * cover), rd = (int)(((p >> 16) & 0xFF) * cover);
                        int a = (int)Math.Round(255 * cover + s * (1 - cover));
                        row[x] = (a << 24) | (rd << 16) | (g << 8) | b;
                    }
                    Marshal.Copy(row, 0, line, cw);
                }
                return card;
            }

            /// <summary>Signed distance from (px, py) to a rounded rectangle (negative inside).</summary>
            private static double Sdf(double px, double py, double left, double top, double width, double height, double radius)
            {
                double hx = width / 2.0, hy = height / 2.0;
                double qx = Math.Abs(px - (left + hx)) - (hx - radius);
                double qy = Math.Abs(py - (top + hy)) - (hy - radius);
                double ox = Math.Max(qx, 0), oy = Math.Max(qy, 0);
                return Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0) - radius;
            }

            /// <summary>
            /// Where the picture is at <paramref name="t"/>. In: it rises the last few pixels into place — the way a new
            /// page comes in on a tab switch. Out: it sinks a little as it fades.
            /// </summary>
            private Point Place(Point at, double t)
            {
                double off = _exit ? _travel * Motion.EaseInQuad(t) : _travel * (1.0 - Motion.EaseOutCubic(t));
                return new Point(at.X, at.Y + (int)Math.Round(off));
            }

            private byte Alpha(double t)
            {
                // Out: the fade holds a moment and then goes (ease-in) — a fade that went at once (ease-out) left the
                // window too faint to see after ~70 ms. In: there almost at once, then settling.
                double a = _exit ? 1.0 - Motion.EaseInQuad(t) : Motion.EaseOutCubic(Math.Min(1.0, t / 0.8));
                return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(255 * a)));
            }

            /// <summary>Until the screen has shown the frame just given (DWM), or about a 60 Hz frame where it can't tell.</summary>
            private static void WaitForFrame(System.Diagnostics.Stopwatch clock)
            {
                double before = clock.Elapsed.TotalMilliseconds;
                // At most a frame and a bit: a stalled DWM used to hold the way in — and the window, cloaked until its end —
                // for as long as it stalled (FramePacer).
                bool frame = FramePacer.WaitFrame(20);
                if (frame && clock.Elapsed.TotalMilliseconds - before < 2.0) { Thread.Sleep(8); }
            }

            /// <summary>Answers what Windows sends the popup meanwhile, so nothing waits on this thread.</summary>
            private static void Pump()
            {
                while (PeekMessage(out MSG m, IntPtr.Zero, 0, 0, PM_REMOVE))
                {
                    TranslateMessage(ref m);
                    DispatchMessage(ref m);
                }
            }

        }

        /// <summary>
        /// Where the picture goes in the stacking order: directly above the window (a window lying over Tempo stays over
        /// its picture too), among the always-on-top ones when Tempo is kept on top.
        /// </summary>
        private static IntPtr PlaceAbove(IntPtr window, IntPtr self, bool topmost)
        {
            IntPtr above = GetWindow(window, GW_HWNDPREV);
            if (above == self) { above = GetWindow(above, GW_HWNDPREV); }
            if (topmost) { return above == IntPtr.Zero ? HWND_TOPMOST : above; }
            if (above == IntPtr.Zero || (GetWindowLong(above, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0) { return HWND_NOTOPMOST; }
            return above;
        }

        private static bool IsCloaked(IntPtr hwnd)
        {
            try { return DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int c, sizeof(int)) == 0 && c != 0; }
            catch { return false; }
        }

        private static void SetCloak(IntPtr hwnd, bool on)
        {
            try { int v = on ? 1 : 0; DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref v, sizeof(int)); }
            catch { }
        }

        /// <summary>DWM's own show/hide fade for the window, on or off (off while our picture plays it instead).</summary>
        private static void SetTransitions(IntPtr hwnd, bool on)
        {
            try { int v = on ? 0 : 1; DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref v, sizeof(int)); }
            catch { }
        }

        /// <summary>
        /// Windows' own transitions for <paramref name="hwnd"/> — the minimise and restore zoom among them — on or off. Off
        /// while a window waits minimised (MainForm.ArmRestoreWithoutTransition): the restore zoom plays the picture the
        /// window had, unpainted at its new size, and can start before the window's show cloak is on.
        /// </summary>
        internal static void SetWindowTransitions(IntPtr hwnd, bool on)
        {
            if (hwnd != IntPtr.Zero) { SetTransitions(hwnd, on); }
        }

        // ── Win32 ────────────────────────────────────────────────────────────────────────────────────

        private const int WS_POPUP = unchecked((int)0x80000000), SS_NOTIFY = 0x00000100;
        private const int WS_EX_LAYERED = 0x00080000, WS_EX_TRANSPARENT = 0x00000020, WS_EX_NOACTIVATE = 0x08000000,
                          WS_EX_TOOLWINDOW = 0x00000080, WS_EX_TOPMOST = 0x00000008;
        private const int GWL_EXSTYLE = -20;
        private const uint GW_HWNDPREV = 3;
        private const uint SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040, SWP_NOOWNERZORDER = 0x0200;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private const int DWMWA_TRANSITIONS_FORCEDISABLED = 3, DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAK = 13, DWMWA_CLOAKED = 14;
        private const uint PW_RENDERFULLCONTENT = 0x00000002;
        private const uint PM_REMOVE = 0x0001;

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public int message;
            public IntPtr wParam, lParam;
            public int time, ptX, ptY, lPrivate;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y,
                                                    int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("user32.dll")] private static extern bool PeekMessage(out MSG msg, IntPtr hwnd, int min, int max, uint remove);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
        [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
    }
}
