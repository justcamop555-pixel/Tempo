using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AutoClicker.Models;

namespace AutoClicker.UI
{
    /// <summary>
    /// Tempo's caption overlay bar - the display half of Live Captions.
    ///
    /// The text shown here can come from either of Tempo's two caption engines:
    /// Tempo's own offline Whisper transcriber (Utils/TempoTranscriber.cs), or
    /// Windows 11 Live Captions mirrored in (Utils/LiveCaptionReader.cs). Tempo
    /// tries its own engine first and falls back to mirroring Windows when its own
    /// engine can't run. Either way <see cref="SetCaption"/> is the single entry
    /// point text is fed into, so the bar looks identical whichever engine is live.
    ///
    /// Before the first words arrive the bar shows a short, normal-size
    /// "Captions on - listening..." line (see <see cref="SetPending"/>) rather than
    /// an empty panel, so there's no blank box flashing on screen while the engine
    /// spins up.
    ///
    /// This is a full custom-rendered overlay. Rather than a colour-key (which can
    /// only do fully-transparent backgrounds and leaves jagged text edges), it
    /// paints itself through UpdateLayeredWindow with true per-pixel alpha. That
    /// unlocks a soft rounded panel, a gentle fade in/out, a glow behind the text
    /// for readability over any scene, a thin "live" accent line that pulses while
    /// new speech is arriving, and crisp anti-aliased text - all while staying
    /// always-on-top and click-through so it never blocks the game underneath.
    /// </summary>
    public sealed class CaptionOverlayForm : Form
    {
        private Theme _theme;
        private string _text = "";
        // True while captions are on but no real text has arrived yet. In this
        // state the bar shows a short, normal-size "Captions on - listening..." line
        // instead of an empty panel, so there's no blank-box flash on startup.
        private bool _pending;
        private int _opacityPercent = 50;
        private int _fontPoints = 20;
        private string _fontFamily = "Segoe UI";

        /// <summary>
        /// A fallback font family the caller OWNS and may dispose.
        ///
        /// The obvious choice, <see cref="FontFamily.GenericSansSerif"/>, hands back a
        /// single PROCESS-WIDE cached instance — not a fresh object. The caption painter
        /// wraps its family in a `using`, so assigning the generic one there disposed it
        /// for the whole process, and every later use of GenericSansSerif anywhere in
        /// Tempo then threw "Object is in a disposed state". The bar repaints several
        /// times a second, so one font that failed to construct — a family with no Bold
        /// face, or a font uninstalled while Tempo was running — permanently broke the
        /// shared family. It looked like the font picker corrupting the caption bar.
        ///
        /// Building a family BY NAME returns an instance we own, so the `using` is safe.
        /// </summary>
        internal static FontFamily SafeFallbackFamily()
        {
            foreach (string name in new[] { "Segoe UI", "Tahoma", "Arial" })
            {
                try { return new FontFamily(name); }
                catch { /* not installed — try the next */ }
            }
            // Last resort: a copy of the generic family BY NAME, so it is still ours.
            try { return new FontFamily(FontFamily.GenericSansSerif.Name); }
            catch { return new FontFamily("Microsoft Sans Serif"); }
        }
        private Color _customColor = Color.FromArgb(255, 244, 191, 79); // amber
        private bool _useCustomColor = true;
        private bool _showBackground = true;

        // Panel background opacity as a fraction of the text opacity, so turning the
        // text down also softens the panel. Tuned to read well over bright scenes.
        private const double PanelAlphaFactor = 0.62;

        /// <summary>
        /// True while a fullscreen game owns the screen: the bar's animation slows
        /// from ~30 fps to ~10 and the glow draws with fewer strokes — every layered
        /// -window update costs the game compositor time, so the chrome eases off
        /// while frame rate matters most. Text updates are unaffected.
        /// </summary>
        public static volatile bool LowImpactMode;

        // Fade animation (0..1) and the "speaking" pulse.
        private readonly System.Windows.Forms.Timer _anim;
        private double _fade;            // current opacity multiplier 0..1
        private double _fadeTarget = 1;  // where we're heading
        private double _pulse;           // 0..1 glow strength on the accent line
        private DateTime _lastTextChange = DateTime.MinValue;

        // ── Per-speaker label colours ────────────────────────────────────────
        // Each "Speaker N:" gets a stable, distinct colour so following who says
        // what takes a glance, not reading. Chosen to stay clearly apart from the
        // default amber body text and readable on the dark panel.
        private static readonly Color[] SpeakerPalette =
        {
            Color.FromArgb(79, 195, 247),   // 1 · sky blue
            Color.FromArgb(129, 199, 132),  // 2 · green
            Color.FromArgb(240, 98, 146),   // 3 · pink
            Color.FromArgb(186, 104, 200),  // 4 · purple
            Color.FromArgb(77, 182, 172),   // 5 · teal
            Color.FromArgb(255, 138, 101),  // 6 · coral
            Color.FromArgb(255, 241, 118),  // 7 · yellow
            Color.FromArgb(144, 164, 174),  // 8 · steel
            Color.FromArgb(161, 136, 127),  // 9 · taupe
        };
        // The "♪ App ·" source tag: informative but muted, so it stops competing
        // with the actual words.
        private static readonly Color SourceTagColor = Color.FromArgb(170, 178, 192);

        /// <summary>One word (or an atomic "Speaker N:" label) with its colour.</summary>
        private struct TextRun
        {
            public string Text;
            public Color Color;
            // True for the continuation pieces of one long token that was broken to
            // fit the bar (Chinese/Japanese runs without spaces, long URLs): no word
            // gap is drawn before a glued piece, so the run reads continuously.
            public bool Glue;
            // How many of _targetWords must be revealed before this run is drawn. Lets
            // the layout be built from the FULL caption text (so word positions never
            // shift) while only the revealed prefix is painted — no per-word wobble.
            public int RevealAt;
        }

        // Word-advance cache so the custom line layout doesn't re-measure the same
        // words every animation frame. Keyed by the active font; cleared on change.
        private readonly System.Collections.Generic.Dictionary<string, float> _wordW =
            new System.Collections.Generic.Dictionary<string, float>();
        private string _metricsKey;
        private float _spaceW;
        private float _lineH;

        // ── Word-by-word reveal ──────────────────────────────────────────────
        // The engine emits a few WORDS at a time every couple of seconds (Whisper
        // needs that much audio per pass — true word-level streaming isn't possible
        // offline). Slamming the whole chunk onto the bar at once reads as a wall
        // of text appearing from nowhere; revealing it ONE WORD AT A TIME, each
        // word fading in with a little rise, reads like live speech being heard —
        // the Windows 11 captions feel. The pacing (~90 ms/word) finishes a normal
        // chunk comfortably before the next one lands; if the next chunk arrives
        // early, its words simply queue behind and the reveal never stalls.
        private string[] _targetWords = Array.Empty<string>();
        private int _revealed;                    // words currently on the bar
        private long _lastRevealTick;
        private double _newWordFade = 1.0;        // newest word's fade-in (0..1)

        // READING-paced reveal. 90 ms/word (~660 wpm) blasted words past readers at
        // triple reading speed — "people did not finish reading". The base pace now
        // matches comfortable caption reading (~210 wpm, broadcast standard is
        // 160–180); when the unrevealed backlog builds (rapid speech, lyrics), the
        // pace tightens smoothly so captions never drift far behind the audio —
        // the classic captioning trade: readable normally, catch-up under flood.
        // Bonus: while words meter out at reading pace, the earlier words HOLD
        // their place on the bar longer, so the eye gets to finish them.
        private const int RevealBaseMs = 280;     // ≈ 210 wpm
        private const int RevealRushMs = 80;      // flood ceiling (backlog very deep)

        /// <summary>Per-word reveal delay for the CURRENT backlog depth.</summary>
        private int CurrentPaceMs()
        {
            int backlog = _targetWords.Length - _revealed;
            if (backlog > 24) { return RevealRushMs; }        // way behind — rush
            if (backlog > 16) { return 130; }
            if (backlog > 8) { return 190; }                  // a chunk queued — brisk
            return RevealBaseMs;                              // normal speech — readable
        }

        /// <summary>The pace currently in force, for Live debug.</summary>
        public int RevealPaceMs => CurrentPaceMs();

        /// <summary>Words currently visible on the bar (Live debug).</summary>
        public int RevealShownWords => Math.Min(_revealed, _targetWords.Length);
        /// <summary>Total words of the current caption target (Live debug).</summary>
        public int RevealTotalWords => _targetWords.Length;

        /// <summary>
        /// Works out how many words of the NEW caption text are already on screen,
        /// so only the genuinely new tail animates in. The rolling line normally
        /// GROWS at the end (common prefix), but it also gets front-trimmed and its
        /// "♪ source · Speaker N:" prefix can change — those are re-anchored by
        /// finding the last already-revealed words inside the new text. When the
        /// text changed beyond recognition, everything shows instantly (a wrong
        /// instant-show is better than animating words the user already read).
        /// </summary>
        private void ComputeReveal(string newText)
        {
            string[] nw = (newText ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string[] ow = _targetWords;
            int carried;
            if (ow.Length == 0 || _revealed <= 0)
            {
                carried = 0;
            }
            else
            {
                int lim = Math.Min(ow.Length, nw.Length);
                int lcp = 0;
                while (lcp < lim && ow[lcp] == nw[lcp]) { lcp++; }
                if (lcp >= Math.Min(_revealed, ow.Length))
                {
                    carried = Math.Min(_revealed, nw.Length);   // pure append
                }
                else if (lcp > 0)
                {
                    carried = Math.Min(lcp, _revealed);         // shared start, rest changed
                }
                else
                {
                    // Front-trim / prefix change: re-anchor on the last few revealed
                    // words; matched from the END so a repeated phrase anchors to its
                    // latest occurrence. No anchor → show instantly.
                    int take = Math.Min(4, Math.Min(_revealed, ow.Length));
                    carried = nw.Length;
                    for (int p = nw.Length - take; p >= 0 && take > 0; p--)
                    {
                        bool match = true;
                        for (int k = 0; k < take; k++)
                        {
                            if (nw[p + k] != ow[Math.Min(_revealed, ow.Length) - take + k]) { match = false; break; }
                        }
                        if (match) { carried = p + take; break; }
                    }
                }
            }
            _targetWords = nw;
            _revealed = Math.Min(carried, nw.Length);
            if (_revealed < nw.Length)
            {
                // Backdate the clock by one pace so the FIRST new word appears on the
                // very next animation frame — the batch's opening word used to sit a
                // full pace interval (~280 ms) doing nothing, pure added latency; the
                // words AFTER it still meter out at reading pace.
                _lastRevealTick = Environment.TickCount64 - CurrentPaceMs();
                EnsureAnimating();
            }
        }

        public CaptionOverlayForm(Theme theme)
        {
            _theme = theme ?? Theme.ForKind(ThemeKind.Dark);

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;

            _anim = new System.Windows.Forms.Timer { Interval = 33 }; // ~30 fps
            _anim.Tick += (s, e) => AnimationTick();

            LayoutToBottom();
        }

        // The screen the bar currently lives on. Default: the primary. Multi-monitor
        // ("the game is on the DisplayPort monitor") support: the bar can be laid out
        // on ANY screen — the host moves it to the monitor of whatever is making the
        // sound, so captions appear over the game/video instead of a different screen.
        private Screen _homeScreen;

        private Screen HomeScreen => _homeScreen ?? Screen.PrimaryScreen;

        // Physical-pixels-per-point factor for this window's display. The window is
        // system-DPI-aware (bounds are physical px), so on a 125%/150% screen we must
        // scale the bar box AND the glyphs up by this, or captions render tiny in a
        // big half-empty bar. 1.0 at 100% scaling — the fix is a no-op there.
        private float Dpi => DeviceDpi / 96f;

        /// <summary>The bar's width/height for the current font size and display scale.
        /// One formula for both layout paths so they can never drift apart (they did:
        /// a font-size change used to snap the bar to a smaller box and clip line 3).</summary>
        private Size BarSizeFor(Rectangle wa)
        {
            float dpi = Dpi;
            int w = (int)(wa.Width * 0.80);
            // Height follows the number of lines the bar is keeping. It used to be a flat
            // 150px — room for three lines at the default font — so asking for more lines
            // would simply have drawn them outside the panel.
            //
            // The per-line figure is taken from the old constant: 6.2 × font points held
            // three lines, so 2.07 × points is one line. At the default 20pt and 3 lines
            // this returns exactly the previous 150px, so nothing moves for anyone who
            // leaves the count alone.
            int perLine = (int)(_fontPoints * 2.07f * dpi);
            int padding = (int)(26 * dpi);
            int h = Math.Max((int)(60 * dpi), perLine * _maxLines + padding);
            // Never taller than half the screen: the bar is an overlay, not a document.
            h = Math.Min(h, (int)(wa.Height * 0.5));
            return new Size(w, h);
        }

        /// <summary>Re-positions the bar across the bottom of its home screen.</summary>
        private void LayoutToBottom()
        {
            try
            {
                var wa = HomeScreen.WorkingArea;
                // Wider and taller than before: three lines of caption text, like the
                // Windows Live Captions bar, instead of two cramped ones.
                Size sz = BarSizeFor(wa);
                int x = wa.Left + (wa.Width - sz.Width) / 2;
                int y = wa.Bottom - sz.Height - 56;
                Bounds = new Rectangle(x, y, sz.Width, sz.Height);
            }
            catch { }
        }

        /// <summary>
        /// Moves the bar to the bottom of the monitor holding <paramref name="hwnd"/>
        /// (the window making the sound — a game on the DisplayPort monitor, a video
        /// on the other screen). No-op when the window is unknown or the bar is
        /// already on that monitor. The host only calls this while the user hasn't
        /// dragged the bar to a custom spot, so a hand-placed bar is never yanked.
        /// </summary>
        public void MoveToScreenOf(IntPtr hwnd)
        {
            try
            {
                if (hwnd == IntPtr.Zero) { return; }
                Screen target = Screen.FromHandle(hwnd);
                if (target == null) { return; }
                Screen current = _homeScreen ?? Screen.FromControl(this);
                if (current != null && target.Bounds == current.Bounds) { return; }
                _homeScreen = target;
                LayoutToBottom();
            }
            catch { }
        }

        // ── Public surface (unchanged signatures) ───────────────────────────
        public void SetCaption(string text)
        {
            text = text ?? "";
            // Real text means we're no longer "pending" - drop the listening cue and
            // show the live text from here on.
            if (_pending && text.Length > 0)
            {
                _pending = false;
            }
            if (text != _text)
            {
                ComputeReveal(text);        // animate only the genuinely new words in
                _text = text;
                _lastTextChange = DateTime.UtcNow;
                _pulse = 1.0;               // kick the live accent
                _fadeTarget = 1.0;          // make sure we're visible
                EnsureAnimating();
                Repaint();
            }
        }

        /// <summary>
        /// Puts the bar in its "starting up, no words yet" look: a short, normal-size
        /// "Captions on - listening..." line in the full bar, instead of an empty
        /// panel. This is what the user sees in the brief gap between turning captions
        /// on and the first transcribed words, so that gap no longer looks
        /// like a big empty caption box. The first call to <see cref="SetCaption"/>
        /// with real text clears it automatically.
        /// </summary>
        public void SetPending(bool pending)
        {
            if (pending == _pending) return;
            _pending = pending;
            if (pending)
            {
                _text = "";
                _fadeTarget = 1.0;
                EnsureAnimating();
            }
            Repaint();
        }

        // How many lines of text the bar keeps on screen. Was a hard-coded 3, which is
        // why a phrase survived only a few seconds: the next couple of chunks wrapped
        // past three lines and pushed it off, and it was gone for good. Now the user's
        // choice — see AppSettings.CaptionMaxLines.
        private int _maxLines = 6;

        /// <summary>Sets how many lines of caption history stay on the bar (1–12).</summary>
        public void SetMaxLines(int lines)
        {
            lines = Math.Max(1, Math.Min(12, lines));
            if (lines == _maxLines) { return; }
            _maxLines = lines;
            ResizeForFont();      // the bar's height is derived from the line count
            Repaint();
        }

        /// <summary>How many lines the bar is currently keeping.</summary>
        public int MaxLines => _maxLines;

        public void SetFontSize(int points)
        {
            points = Math.Max(10, Math.Min(72, points));
            if (points != _fontPoints)
            {
                _fontPoints = points;
                ResizeForFont();
                Repaint();
            }
        }

        /// <summary>
        /// Recomputes width/height for the current font size but KEEPS the current
        /// top-left, so changing the size never yanks a bar the user has dragged
        /// somewhere back to the default position. Only re-centres horizontally if
        /// the bar is still at its default spot.
        /// </summary>
        private void ResizeForFont()
        {
            try
            {
                var wa = HomeScreen.WorkingArea;
                // SAME geometry as LayoutToBottom — using different (smaller) constants
                // here shrank the bar on every font-size change and clipped the third
                // caption line.
                Size sz = BarSizeFor(wa);
                int w = sz.Width, h = sz.Height;
                // Keep current position; just clamp so it stays on-screen.
                int x = Left;
                int y = Top;
                if (x + w > wa.Right) x = Math.Max(wa.Left, wa.Right - w);
                if (y + h > wa.Bottom) y = Math.Max(wa.Top, wa.Bottom - h);
                Bounds = new Rectangle(x, y, w, h);
            }
            catch { }
        }

        public void SetOpacity(int percent)
        {
            _opacityPercent = Math.Max(10, Math.Min(100, percent));
            Repaint();
        }

        public void SetFontFamily(string family)
        {
            if (!string.IsNullOrWhiteSpace(family) && family != _fontFamily)
            {
                _fontFamily = family;
                Repaint();
            }
        }

        public void SetTextColor(Color color, bool use)
        {
            _customColor = color;
            _useCustomColor = use;
            Repaint();
        }

        /// <summary>
        /// When false, the rounded panel and accent line are not drawn - just the
        /// caption text (with its glow/outline) floats over the screen with nothing
        /// behind it. Lets users pick a clean text-only look.
        /// </summary>
        public void SetShowBackground(bool show)
        {
            if (show != _showBackground)
            {
                _showBackground = show;
                Repaint();
            }
        }

        public void ApplyTheme(Theme theme)
        {
            if (theme != null)
            {
                _theme = theme;
                Repaint();
            }
        }

        // ── Window style: layered, click-through, no activation ─────────────
        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_LAYERED = 0x00080000;
                const int WS_EX_NOACTIVATE = 0x08000000;
                const int WS_EX_TOOLWINDOW = 0x00000080;
                const int WS_EX_TRANSPARENT = 0x00000020;
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                if (!_movable)
                {
                    cp.ExStyle |= WS_EX_TRANSPARENT;
                }
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            OverlayTopmost.Register(Handle);   // stay above fullscreen games / video
            // Fade in from nothing.
            _fade = 0;
            _fadeTarget = 1;
            EnsureAnimating();
            Repaint();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            OverlayTopmost.Unregister(Handle);
            base.OnFormClosed(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Repaint();
        }

        // ── Move mode (drag to reposition) ──────────────────────────────────
        private bool _movable;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT_FLAG = 0x00000020;
        private const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004,
                           SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020;

        public void SetMovable(bool movable)
        {
            _movable = movable;
            if (IsHandleCreated)
            {
                try
                {
                    int ex = GetWindowLong(Handle, GWL_EXSTYLE);
                    if (movable) ex &= ~WS_EX_TRANSPARENT_FLAG;
                    else ex |= WS_EX_TRANSPARENT_FLAG;
                    SetWindowLong(Handle, GWL_EXSTYLE, ex);
                    SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
                }
                catch { }
            }
            Repaint();
        }

        public event Action<Point> PositionChanged;

        private const int WM_NCHITTEST = 0x0084;
        private const int HTCAPTION = 2;
        private const int WM_EXITSIZEMOVE = 0x0232;

        protected override void WndProc(ref Message m)
        {
            if (_movable && m.Msg == WM_NCHITTEST)
            {
                m.Result = (IntPtr)HTCAPTION;
                return;
            }
            base.WndProc(ref m);
            if (m.Msg == WM_EXITSIZEMOVE)
            {
                // Wherever the user dropped it is now home — font resizes and layout
                // must respect THAT monitor, not the primary.
                try { _homeScreen = Screen.FromControl(this); } catch { }
                PositionChanged?.Invoke(Location);
            }
        }

        public void MoveTo(Point p)
        {
            try
            {
                Location = p;
                _homeScreen = Screen.FromPoint(p);   // restored position defines home
                Repaint();
            }
            catch { }
        }

        /// <summary>
        /// Clamps the bar back onto the visible work area, in case a resolution
        /// change left it partly or fully off-screen.
        /// </summary>
        public void EnsureOnScreen()
        {
            try
            {
                var wa = Screen.FromControl(this).WorkingArea;
                int x = Left, y = Top;
                if (x + Width > wa.Right) x = wa.Right - Width;
                if (y + Height > wa.Bottom) y = wa.Bottom - Height;
                if (x < wa.Left) x = wa.Left;
                if (y < wa.Top) y = wa.Top;
                if (x != Left || y != Top)
                {
                    Location = new Point(x, y);
                    Repaint();
                }
            }
            catch { }
        }

        // ── Animation ───────────────────────────────────────────────────────
        private void EnsureAnimating()
        {
            if (!_anim.Enabled) _anim.Start();
        }

        private void AnimationTick()
        {
            // Ease the animation cadence while a game needs the compositor.
            int wantInterval = LowImpactMode ? 100 : 33;
            if (_anim.Interval != wantInterval)
            {
                _anim.Interval = wantInterval;
            }

            bool busy = false;

            // Ease the fade toward its target.
            double fd = _fadeTarget - _fade;
            if (Math.Abs(fd) > 0.01)
            {
                _fade += fd * 0.22;
                busy = true;
            }
            else
            {
                _fade = _fadeTarget;
            }

            // Decay the speaking pulse; re-energise if text changed very recently.
            if ((DateTime.UtcNow - _lastTextChange).TotalMilliseconds < 350)
            {
                _pulse = 1.0;
                busy = true;
            }
            else if (_pulse > 0.01)
            {
                _pulse *= 0.90;
                busy = true;
            }
            else
            {
                _pulse = 0;
            }

            // Word-by-word reveal: advance by wall-clock (not by frame count), so
            // game mode's slower animation cadence reveals in coarser but correctly
            // paced steps instead of slowing the words down. The per-word delay is
            // re-read every step — the pace loosens back to reading speed the moment
            // the backlog drains.
            if (_revealed < _targetWords.Length)
            {
                long nowT = Environment.TickCount64;
                int pace = CurrentPaceMs();
                while (_revealed < _targetWords.Length && nowT - _lastRevealTick >= pace)
                {
                    _revealed++;
                    _lastRevealTick += pace;
                    _newWordFade = 0;         // the freshly revealed word fades in
                    pace = CurrentPaceMs();
                }
                busy = true;
            }
            if (_newWordFade < 1.0)
            {
                _newWordFade = Math.Min(1.0, _newWordFade + 0.25);
                busy = true;
            }

            Repaint();
            if (!busy) _anim.Stop();
        }

        // ── Painting (per-pixel alpha) ──────────────────────────────────────
        private void Repaint()
        {
            if (!IsHandleCreated || Width <= 0 || Height <= 0) return;
            try
            {
                // Reuse the drawing surface instead of building a new one every frame.
                //
                // At the bar's current size this bitmap is ~1.5 MB, and it was being
                // created and destroyed on every repaint — up to 30 times a second while
                // text animates in. GDI+ keeps the pixels in unmanaged memory, so this
                // never showed up as GC pressure; it was simply the allocate-and-free
                // itself, and it is not cheap. Measured at 1467×272:
                //
                //     new bitmap each frame : 0.479 ms/frame   (14.4 ms per second)
                //     reused surface        : 0.091 ms/frame   ( 2.7 ms per second)
                //
                // The cost scales with the bar's AREA, so it grew when the bar gained
                // lines. Kept until the size changes, then rebuilt once.
                if (_surface == null || _surface.Width != Width || _surface.Height != Height)
                {
                    var old = _surface;
                    _surface = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    // Pin the drawing surface to 96 DPI so MeasureString/GetHeight agree
                    // with the pixel-space em math below. On a scaled display a fresh
                    // GDI+ bitmap inherits the screen DPI (120/144), which would inflate
                    // measurements while the glyphs stay 96-sized — stretched word gaps
                    // and wrong wrapping. No-op at 100%.
                    _surface.SetResolution(96f, 96f);
                    try { old?.Dispose(); } catch { }
                }

                using (var g = Graphics.FromImage(_surface))
                {
                    // No Clear needed here: DrawContent opens with g.Clear(Transparent),
                    // which is what makes reuse safe — a surface still holding the
                    // previous frame would otherwise show old glyphs through the new
                    // ones, since everything below composites with alpha.
                    DrawContent(g);
                }
                PushLayered(_surface);
            }
            catch (Exception ex)
            {
                // A paint failure (odd glyphs, GDI+ edge case) must NEVER take the
                // app down — worst case this frame isn't drawn and the next one is.
                // (A crash report proved this path: one unrenderable word killed the
                // whole app from inside SetCaption's repaint.)
                if (!_paintFailLogged)
                {
                    _paintFailLogged = true;
                    Utils.Logger.Warn("[Captions] bar paint failed (skipped frame): " + ex.Message);
                }
            }
        }

        private bool _paintFailLogged;
        // The reused drawing surface. Rebuilt only when the bar changes size —
        // see Repaint for why it is not created per frame.
        private Bitmap _surface;

        private void DrawContent(Graphics g)
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            // Path-drawn glyphs (AddString) don't use font hinting, so edge quality
            // is entirely down to the rasteriser: HighQuality pixel offset aligns
            // path edges on half-pixel centres (visibly crisper letter stems at
            // caption sizes) and HighQuality compositing removes the slight fringe
            // where the glow, outline and fill layers blend. Same fonts — just
            // rendered cleaner.
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;

            double globalFade = _movable ? 1.0 : Math.Max(0, Math.Min(1, _fade));
            if (globalFade <= 0 && !_movable) return;

            double op = _opacityPercent / 100.0;
            int textA = (int)Math.Round(255 * op * globalFade);
            int panelA = (int)Math.Round(255 * op * PanelAlphaFactor * globalFade);
            if (_movable) panelA = Math.Max(panelA, 160); // clearly grabbable

            Color baseColor = _useCustomColor ? _customColor : _theme.Text;
            var rect = new Rectangle(2, 2, Width - 5, Height - 5);

            // "Starting up" state: captions are on but no words have arrived yet. We
            // show a short, NORMAL-SIZE line ("Captions on \u00b7 listening\u2026") in the
            // full bar - not a tiny pill - so the bar is its usual size from the start
            // and there's no empty-panel flash. The first real caption replaces it.
            string drawText = _text;
            if (_pending && !_movable)
            {
                drawText = "Captions on \u00b7 listening\u2026";
            }
            else if (!_movable && _targetWords.Length > 0)
            {
                // Lay out the FULL caption text so every word sits in its FINAL
                // position; the reveal is applied as a per-run paint cutoff (RevealAt)
                // below, not by shrinking the layout. This stops the whole block
                // sliding sideways/up as each word appears (the old revealed-only
                // layout re-centred every line ~3.5\u00d7/second while the user was reading).
                drawText = string.Join(" ", _targetWords);
            }
            int revealCount = _pending ? int.MaxValue
                            : (!_movable && _targetWords.Length > 0
                               ? Math.Max(0, Math.Min(_revealed, _targetWords.Length))
                               : int.MaxValue);

            // Panel colours are derived from the active theme's Surface so the bar
            // matches the rest of Tempo instead of a hardcoded near-black. A gentle
            // top-to-bottom gradient (Surface -> a darker Surface) keeps the depth.
            Color panelTop = _theme != null ? _theme.Surface : Color.FromArgb(14, 16, 26);
            Color panelBot = Darken(panelTop, 0.34);
            Color borderCol = _theme != null ? _theme.Border : Color.FromArgb(255, 255, 255);

            // Soft rounded panel with a subtle top-to-bottom gradient. Skipped when
            // the user has turned the background off (text-only look) - but always
            // drawn in move mode so there's something solid to grab.
            if (_showBackground || _movable)
            {
                using (var path = RoundedRect(rect, 18))
                {
                    using (var lg = new LinearGradientBrush(rect,
                        Color.FromArgb(panelA, panelTop.R, panelTop.G, panelTop.B),
                        Color.FromArgb((int)(panelA * 0.92), panelBot.R, panelBot.G, panelBot.B), 90f))
                    {
                        g.FillPath(lg, path);
                    }
                    // Hairline border for definition, tinted from the theme border.
                    using (var bp = new Pen(Color.FromArgb((int)(panelA * 0.55), borderCol.R, borderCol.G, borderCol.B), 1f))
                    {
                        g.DrawPath(bp, path);
                    }
                }
            }

            // "Live" accent line along the bottom that glows while speech arrives.
            // Part of the panel chrome, so it follows the same background toggle.
            if (_showBackground || _movable)
            {
                DrawAccent(g, rect, baseColor, globalFade);
            }

            if (_movable && string.IsNullOrEmpty(_text))
            {
                DrawHint(g, rect, textA);
                return;
            }
            if (string.IsNullOrEmpty(drawText)) return;

            // Small "where this audio comes from" tag in the top-right corner. Full
            // text alpha — the bar's background is colour-keyed, so faded pixels turn
            // near-invisible on dark wallpapers; the tag stays subordinate through its
            // small size instead. A soft dark halo keeps it readable over anything.

            // Text area leaves room for the accent line.
            var layout = new RectangleF(rect.Left + 26, rect.Top + 10,
                rect.Width - 52, rect.Height - 28);

            // See SafeFallbackFamily: NEVER let the shared generic family reach `using`.
            //
            // FontFamily.GenericSansSerif hands back a single PROCESS-WIDE cached
            // instance, not a fresh object. The fallbacks below assigned it to `fam`,
            // which was then handed to `using (fam)` at the bottom and disposed — so the
            // first caption frame that had to fall back permanently destroyed the shared
            // generic family for the whole process. Every later use of it, anywhere in
            // Tempo, then throws "Object is in a disposed state". This runs on EVERY
            // caption repaint, several times a second, so one bad font turned into an
            // app-wide font failure that looked like the font picker breaking the bar.
            // SafeFallbackFamily always returns a family we own, so `using` is correct.
            FontFamily fam;
            try { fam = new FontFamily(_fontFamily); }
            catch { fam = SafeFallbackFamily(); }

            // Pick a style the chosen family actually supports. Some installed fonts
            // have no Bold face; constructing a Bold Font from them throws and used to
            // make the whole caption fail to draw - which looked like "the font picker
            // does nothing / looks wrong". Prefer Bold for readability, but fall back to
            // Regular, and if even the family is unusable, drop to a safe sans-serif.
            FontStyle style = FontStyle.Bold;
            if (!fam.IsStyleAvailable(FontStyle.Bold))
            {
                style = fam.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular
                      : (fam.IsStyleAvailable(FontStyle.Italic) ? FontStyle.Italic : FontStyle.Regular);
            }
            // Scale the point size to physical pixels for the display: the surface is
            // pinned to 96 DPI, so on a 150% screen a "20pt" caption must be built at
            // 30pt to occupy the physical size 20pt has everywhere else. 1.0 at 100%.
            float fontPts = _fontPoints * Dpi;
            Font font;
            try { font = new Font(fam, fontPts, style, GraphicsUnit.Point); }
            catch
            {
                // Only dispose a family this frame actually built — never the shared one.
                try { fam.Dispose(); } catch { }
                fam = SafeFallbackFamily();
                style = fam.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;
                font = new Font(fam, fontPts, style, GraphicsUnit.Point);
            }

            using (fam)
            using (font)
            using (var typo = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                typo.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoClip;
                float emSize = font.SizeInPoints * 1.333f;
                RefreshTextMetrics(g, font, typo, emSize);

                // Colour-tagged words: "Speaker N:" labels in per-speaker colours and
                // the "♪ App ·" source tag muted; everything else in the text colour.
                var toks = TokenizeRuns(drawText, baseColor);

                // Space-less scripts (Chinese/Japanese) — and very long words — arrive
                // as ONE token wider than the whole bar; the space-based wrap below
                // could never break it. Split any oversized token into fitting pieces
                // by characters (glued, so no fake word gaps appear inside the run).
                var flat = new System.Collections.Generic.List<TextRun>(toks.Count);
                foreach (TextRun t in toks)
                {
                    if (t.Text.Length > 2 && WordWidth(g, font, typo, t.Text) > layout.Width)
                    {
                        int start = 0;
                        bool first = true;
                        while (start < t.Text.Length)
                        {
                            int len = 1;
                            while (start + len < t.Text.Length &&
                                   WordWidth(g, font, typo, t.Text.Substring(start, len + 1)) <= layout.Width)
                            {
                                len++;
                            }
                            flat.Add(new TextRun
                            {
                                Text = t.Text.Substring(start, len),
                                Color = t.Color,
                                Glue = !first,
                                // All pieces of one split token reveal together, when
                                // that token's word(s) are revealed.
                                RevealAt = t.RevealAt
                            });
                            start += len;
                            first = false;
                        }
                    }
                    else
                    {
                        flat.Add(t);
                    }
                }

                // Wrap into lines with the cached word advances, then keep only the
                // NEWEST lines that fit — captions roll, so the latest words win.
                var lines = new System.Collections.Generic.List<System.Collections.Generic.List<TextRun>>();
                var lineWidths = new System.Collections.Generic.List<float>();
                var cur = new System.Collections.Generic.List<TextRun>();
                float curW = 0;
                foreach (TextRun t in flat)
                {
                    float w = WordWidth(g, font, typo, t.Text);
                    float gap = (cur.Count == 0 || t.Glue) ? 0 : _spaceW;
                    if (cur.Count > 0 && curW + gap + w > layout.Width)
                    {
                        lines.Add(cur); lineWidths.Add(curW);
                        cur = new System.Collections.Generic.List<TextRun>(); curW = 0;
                        gap = 0;
                    }
                    curW += gap + w;
                    cur.Add(t);
                }
                if (cur.Count > 0) { lines.Add(cur); lineWidths.Add(curW); }

                int MaxLines = _maxLines;
                // Anchor the window on the line holding the LAST revealed run,
                // not blindly on the newest line: the full text can wrap to >3 lines
                // while the reveal still lags, and a blind newest-3 trim would hide the
                // words being revealed. The window scrolls one line at a time as the
                // reveal cursor crosses out of it — same cadence as before, minus the
                // per-word wobble.
                int cursorLine = lines.Count - 1;
                for (int li = 0; li < lines.Count; li++)
                {
                    bool anyRevealed = false;
                    foreach (TextRun t in lines[li])
                    {
                        if (t.RevealAt <= revealCount) { anyRevealed = true; }
                    }
                    if (anyRevealed) { cursorLine = li; }
                }
                if (lines.Count > MaxLines)
                {
                    int end = Math.Min(lines.Count - 1, cursorLine);
                    int startLine = Math.Max(0, end - (MaxLines - 1));
                    // Prefer to keep the window full even if the cursor is near the top.
                    int drop = startLine;
                    int keep = Math.Min(MaxLines, lines.Count - drop);
                    lines = lines.GetRange(drop, keep);
                    lineWidths = lineWidths.GetRange(drop, keep);
                }
                if (lines.Count == 0) { return; }

                float blockH = _lineH * lines.Count;
                float y = layout.Top + Math.Max(0, (layout.Height - blockH) / 2f);

                // Build one path per run for the coloured fills plus a combined path
                // the glow/outline strokes once — so the halo stays seamless across
                // colour boundaries instead of double-striking between words.
                var runPaths = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<GraphicsPath, Color>>();
                var combined = new GraphicsPath();
                try
                {
                    // While a freshly revealed word is still fading in, it is drawn
                    // SEPARATELY (own alpha + a little rise) — kept out of the shared
                    // glow/outline so the halo doesn't pop in at full strength ahead
                    // of its letters.
                    bool animateLast = !_pending && !_movable && _newWordFade < 1.0;
                    for (int li = 0; li < lines.Count; li++)
                    {
                        // Centre each line on its FULL width (all words, revealed or
                        // not) so revealed words never shift as later ones appear.
                        float x = layout.Left + Math.Max(0, (layout.Width - lineWidths[li]) / 2f);
                        bool firstInLine = true;
                        foreach (TextRun t in lines[li])
                        {
                            if (!firstInLine && !t.Glue) { x += _spaceW; }
                            firstInLine = false;
                            // Advance x for EVERY word to keep positions fixed, but only
                            // paint the ones the reveal has reached.
                            if (t.RevealAt <= revealCount)
                            {
                                var p = new GraphicsPath();
                                p.AddString(t.Text, font.FontFamily, (int)font.Style, emSize,
                                    new PointF(x, y), typo);
                                runPaths.Add(new System.Collections.Generic.KeyValuePair<GraphicsPath, Color>(p, t.Color));
                            }
                            x += WordWidth(g, font, typo, t.Text);
                        }
                        y += _lineH;
                    }
                    int lastRun = runPaths.Count - 1;
                    for (int ri = 0; ri < runPaths.Count; ri++)
                    {
                        if (animateLast && ri == lastRun) { continue; }
                        // AddString yields an EMPTY path for a word whose characters
                        // have no glyphs in this font (a foreign-script word while the
                        // bar font is Latin-only) — and AddPath THROWS "Parameter is
                        // not valid" on an empty path. That was a real crash report:
                        // one unrenderable word took the whole app down mid-caption.
                        if (runPaths[ri].Key.PointCount == 0) { continue; }
                        combined.AddPath(runPaths[ri].Key, false);
                    }

                    // Glow behind the text: several soft, expanding strokes. This is
                    // what keeps captions readable over bright/cluttered scenes
                    // without a hard box. With the panel off we lean on this more
                    // heavily, so text-only mode stays legible over anything.
                    bool noPanel = !_showBackground && !_movable;
                    // Fewer glow strokes while a game owns the screen — each stroke
                    // is a full-path draw the compositor pays for.
                    int passes = LowImpactMode ? (noPanel ? 2 : 1) : (noPanel ? 5 : 3);
                    double glowMul = noPanel ? 0.30 : 0.18;
                    float glowStep = noPanel ? 5f : 4f;
                    for (int i = passes; i >= 1; i--)
                    {
                        int ga = (int)(textA * glowMul);
                        using (var glow = new Pen(Color.FromArgb(ga, 0, 0, 0), i * glowStep)
                        { LineJoin = LineJoin.Round })
                        {
                            g.DrawPath(glow, combined);
                        }
                    }

                    // Crisp outline, then the per-colour fills. Thicker with no panel.
                    using (var pen = new Pen(Color.FromArgb(textA, 0, 0, 0), noPanel ? 3.6f : 3f)
                    { LineJoin = LineJoin.Round })
                    {
                        g.DrawPath(pen, combined);
                    }
                    for (int ri = 0; ri < runPaths.Count; ri++)
                    {
                        if (animateLast && ri == lastRun) { continue; }
                        var rp = runPaths[ri];
                        if (rp.Key.PointCount == 0) { continue; }
                        using (var brush = new SolidBrush(Color.FromArgb(textA, rp.Value.R, rp.Value.G, rp.Value.B)))
                        {
                            g.FillPath(brush, rp.Key);
                        }
                    }

                    // The word being "heard" right now: fades in and settles with a
                    // small rise — outline and fill together at the same partial
                    // alpha, so the letters materialise as one piece.
                    if (animateLast && lastRun >= 0 && runPaths[lastRun].Key.PointCount > 0)
                    {
                        var rp = runPaths[lastRun];
                        int wa = (int)(textA * _newWordFade);
                        float rise = (float)((1.0 - _newWordFade) * 4.0);
                        g.TranslateTransform(0, rise);
                        using (var pen = new Pen(Color.FromArgb(wa, 0, 0, 0), noPanel ? 3.6f : 3f)
                        { LineJoin = LineJoin.Round })
                        {
                            g.DrawPath(pen, rp.Key);
                        }
                        using (var brush = new SolidBrush(Color.FromArgb(wa, rp.Value.R, rp.Value.G, rp.Value.B)))
                        {
                            g.FillPath(brush, rp.Key);
                        }
                        g.ResetTransform();
                    }
                }
                finally
                {
                    try { combined.Dispose(); } catch { }
                    foreach (var rp in runPaths) { try { rp.Key.Dispose(); } catch { } }
                }
            }
        }

        /// <summary>
        /// Splits caption text into colour-tagged word tokens: the leading "♪ App ·"
        /// source tag muted, each "Speaker N:" label in its speaker's colour, and
        /// every other word in the body colour.
        /// </summary>
        private System.Collections.Generic.List<TextRun> TokenizeRuns(string text, Color body)
        {
            var toks = new System.Collections.Generic.List<TextRun>();
            // Track how many whole words have been consumed so each token can record
            // the reveal threshold (RevealAt) at which it becomes visible.
            int consumed = 0;
            int idx = 0;
            if (text.StartsWith("♪ ", StringComparison.Ordinal))
            {
                int dot = text.IndexOf(" · ", StringComparison.Ordinal);
                if (dot > 0)
                {
                    foreach (string w in text.Substring(0, dot + 2)
                             .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        consumed++;
                        toks.Add(new TextRun { Text = w, Color = SourceTagColor, RevealAt = consumed });
                    }
                    idx = dot + 3;
                }
            }
            if (idx > text.Length) { idx = text.Length; }
            string[] words = text.Substring(idx)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                string w = words[i];
                // "Speaker" + "N:" is kept as ONE token so the label never wraps
                // mid-label and colours as a unit.
                if (w == Utils.SpeakerTurnLabeler.SpeakerWord && i + 1 < words.Length && words[i + 1].Length == 2 &&
                    words[i + 1][1] == ':' && words[i + 1][0] >= '1' && words[i + 1][0] <= '9')
                {
                    int n = words[i + 1][0] - '1';
                    consumed += 2;   // two words fold into one label token
                    toks.Add(new TextRun
                    {
                        Text = Utils.SpeakerTurnLabeler.SpeakerWord + " " + words[i + 1],
                        Color = SpeakerPalette[n % SpeakerPalette.Length],
                        RevealAt = consumed
                    });
                    i++;
                    continue;
                }
                consumed++;
                toks.Add(new TextRun { Text = w, Color = body, RevealAt = consumed });
            }
            return toks;
        }

        /// <summary>Re-primes the cached space/line metrics when the font changes.</summary>
        private void RefreshTextMetrics(Graphics g, Font font, StringFormat typo, float emSize)
        {
            string key = font.FontFamily.Name + "|" + font.SizeInPoints + "|" + (int)font.Style;
            if (key == _metricsKey) { return; }
            _metricsKey = key;
            _wordW.Clear();
            _spaceW = g.MeasureString(" ", font, PointF.Empty, typo).Width;
            if (_spaceW <= 0) { _spaceW = font.Size * 0.33f; }
            int em = font.FontFamily.GetEmHeight(font.Style);
            int ls = font.FontFamily.GetLineSpacing(font.Style);
            _lineH = em > 0 ? emSize * ls / em : font.GetHeight(g);
        }

        /// <summary>Advance width of one token at the current font, cached.</summary>
        private float WordWidth(Graphics g, Font font, StringFormat typo, string word)
        {
            float w;
            if (_wordW.TryGetValue(word, out w)) { return w; }
            w = g.MeasureString(word, font, PointF.Empty, typo).Width;
            if (_wordW.Count > 600) { _wordW.Clear(); }   // captions churn words; stay bounded
            _wordW[word] = w;
            return w;
        }

        private void DrawAccent(Graphics g, Rectangle rect, Color accent, double fade)
        {
            // A thin rounded bar centered along the bottom; brightness rises with
            // the speaking pulse so you can see at a glance that words are flowing.
            int baseA = (int)(70 * fade);
            int liveA = (int)(170 * _pulse * fade);
            int a = Math.Min(255, baseA + liveA);
            if (a <= 0) return;

            int w = (int)(rect.Width * (0.18 + 0.10 * _pulse));
            int x = rect.Left + (rect.Width - w) / 2;
            int y = rect.Bottom - 8;
            var bar = new Rectangle(x, y, w, 3);
            using (var p = RoundedRect(bar, 1))
            using (var b = new SolidBrush(Color.FromArgb(a, accent.R, accent.G, accent.B)))
            {
                g.FillPath(b, p);
            }
        }

        private static Color Darken(Color c, double amount)
        {
            amount = Math.Max(0, Math.Min(1, amount));
            return Color.FromArgb(c.A,
                (int)(c.R * (1 - amount)),
                (int)(c.G * (1 - amount)),
                (int)(c.B * (1 - amount)));
        }

        private void DrawHint(Graphics g, Rectangle rect, int textA)
        {
            using (var f = new Font("Segoe UI", Math.Max(11, _fontPoints * 0.6f), FontStyle.Bold))
            using (var b = new SolidBrush(Color.FromArgb(Math.Max(textA, 200), 235, 238, 245)))
            using (var fmt = new StringFormat
            { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString("Drag me where you want captions, then turn move mode off",
                    f, b, rect, fmt);
            }
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = Math.Max(1, radius * 2);
            d = Math.Min(d, Math.Min(r.Width, r.Height));
            var p = new GraphicsPath();
            if (d <= 1)
            {
                p.AddRectangle(r);
                return p;
            }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ── UpdateLayeredWindow plumbing ────────────────────────────────────
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst,
            ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc,
            int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hDC, IntPtr h);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; public POINT(int x, int y){X=x;Y=y;} }
        [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; public SIZE(int x, int y){cx=x;cy=y;} }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct BLENDFUNCTION
        { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        private const int ULW_ALPHA = 0x00000002;
        private const byte AC_SRC_OVER = 0x00;
        private const byte AC_SRC_ALPHA = 0x01;

        private void PushLayered(Bitmap bmp)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero, hOld = IntPtr.Zero;
            try
            {
                hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
                hOld = SelectObject(memDc, hBitmap);
                var size = new SIZE(bmp.Width, bmp.Height);
                var src = new POINT(0, 0);
                var dst = new POINT(Left, Top);
                var blend = new BLENDFUNCTION
                {
                    BlendOp = AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = AC_SRC_ALPHA
                };
                UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, ULW_ALPHA);
            }
            catch { }
            finally
            {
                ReleaseDC(IntPtr.Zero, screenDc);
                if (hOld != IntPtr.Zero) SelectObject(memDc, hOld);
                if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                DeleteDC(memDc);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _anim?.Stop(); _anim?.Dispose(); } catch { }
                try { _surface?.Dispose(); _surface = null; } catch { }
            }
            base.Dispose(disposing);
        }
    }
}
