using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AutoClicker.Models;

namespace AutoClicker.UI
{
    /// <summary>
    /// "Full history" view for Live Captions: a frameless, always-on-top, click-through panel with
    /// a faint (~20% opaque) background, in the caption bar's font and colour, that rolls the most
    /// recent lines of the session's transcript — newest at the bottom — so a line that has already
    /// left the bar can still be read back without the panel ever stealing focus or a click.
    ///
    /// What it draws, and why:
    ///   • Each speaker's label in that speaker's colour and the words in the caption colour — the
    ///     bar's own colours (<see cref="CaptionOverlayForm.SpeakerColor"/>), so a person has ONE
    ///     colour on screen. Every line used to be the same colour here while the bar coloured them.
    ///   • Older lines step down a little in strength (<see cref="RecencyAlpha"/>), so the newest
    ///     reads first.
    ///   • A new line glides in from the bottom and the lines above make room, instead of the whole
    ///     panel jumping. Not while a fullscreen game has the screen
    ///     (<see cref="CaptionOverlayForm.LowImpactMode"/>): every frame of a layered window costs
    ///     the game's compositor.
    ///   • A thin marker beside the line being spoken, glowing while its words arrive.
    ///   • What it is waiting for, in the user's language, when there is nothing to show — and that
    ///     captions are OFF when they are, rather than promising words that will not come.
    ///
    /// Move mode (<see cref="SetMovable"/>, drawn by <see cref="CaptionWindowChrome"/>): drag the
    /// body to move it, an edge to resize it, double-click when done, right-click for a menu. It
    /// snaps to the screen's edges and middle and to the caption bar. Once resized, its size is the
    /// user's (<see cref="SetUserSize"/>) instead of following the font.
    ///
    /// The faint background needs true per-pixel alpha (a colour key can only make pixels fully
    /// transparent, not semi), so this window paints itself through UpdateLayeredWindow.
    /// </summary>
    public sealed class CaptionHistoryForm : Form
    {
        /// <summary>One transcript line as the panel draws it.</summary>
        public struct Entry
        {
            /// <summary>The whole line, label included.</summary>
            public string Text;
            /// <summary>The speaker's number, 0 when the line has no label.</summary>
            public int Speaker;
            /// <summary>How many characters at the start of <see cref="Text"/> are the label.</summary>
            public int LabelLength;
        }

        private Theme _theme;
        private readonly List<Entry> _entries = new List<Entry>();
        private int _fontPoints = 18;
        private string _fontFamily = "Segoe UI";
        private Color _textColor = Color.FromArgb(255, 244, 191, 79);
        private bool _useCustomColor = true;
        private int _textOpacityPercent = 50;
        private bool _captionsOn = true;
        private bool _movable;

        // The user's size in 96-DPI pixels, once they have resized the panel; Empty = follow the font.
        private Size _userSize = Size.Empty;

        // Background panel opacity at rest: "80% transparent" => ~20% opaque.
        private const int RestPanelAlpha = 51;
        // In move mode the panel is solid enough to see what you are grabbing.
        private const int MovePanelAlpha = 176;
        // How many lines the panel is sized for until the user sizes it themselves.
        private const int VisibleLines = 7;
        // How many of the newest lines are kept to draw from: more than any panel can show.
        private const int KeepEntries = 80;

        // ── animation ──────────────────────────────────────────────────────────
        private readonly System.Windows.Forms.Timer _anim;
        private double _arrive = 1.0;      // the newest line's fade-in, 0..1
        private float _slide;              // px the content is still drawn below its resting place
        private float _slideStart;         // how far the current glide started from
        private bool _slidePending;        // a line just arrived: measure how far to glide
        private double _pulse;             // the live marker's glow, 0..1
        private long _lastGrowthTick;
        private string _newestText;        // to tell a NEW line from the newest one growing

        public CaptionHistoryForm(Theme theme)
        {
            _theme = theme ?? Theme.ForKind(ThemeKind.Dark);

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;

            _anim = new System.Windows.Forms.Timer { Interval = 33 };
            _anim.Tick += (s, e) => AnimationTick();

            Bounds = DefaultBounds(Screen.PrimaryScreen);
        }

        private float Dpi => DeviceDpi / 96f;

        /// <summary>Where the panel goes by default on <paramref name="screen"/>: its upper left.</summary>
        private Rectangle DefaultBounds(Screen screen)
        {
            Rectangle wa = (screen ?? Screen.PrimaryScreen).WorkingArea;
            Size sz = FitToArea(AutoSizeFor(wa), wa);
            return new Rectangle(wa.Left + (int)(24 * Dpi), wa.Top + (int)(80 * Dpi), sz.Width, sz.Height);
        }

        /// <summary>The size that follows the font: 42% of the screen wide, <see cref="VisibleLines"/> tall.</summary>
        private Size AutoSizeFor(Rectangle wa)
        {
            float dpi = Dpi;
            int w = (int)(wa.Width * 0.42);
            // Scaled for the display, as the bar is. It wasn't: on a 150% screen the text was
            // built 1.5x bigger inside a box of the same size, and seven lines became four.
            int lineH = (int)(_fontPoints * 1.7f * dpi);
            int h = lineH * VisibleLines + (int)(28 * dpi);
            return new Size(w, h);
        }

        private Size MinSize => new Size((int)(260 * Dpi), (int)(110 * Dpi));

        private Size FitToArea(Size s, Rectangle wa)
        {
            Size min = MinSize;
            int w = Math.Max(min.Width, Math.Min(s.Width, wa.Width));
            int h = Math.Max(min.Height, Math.Min(s.Height, wa.Height));
            return new Size(w, h);
        }

        /// <summary>
        /// Sizes the panel — the user's size if they have set one, else the font's — and keeps it on
        /// the monitor it is on. The top-left stays put unless the panel would hang off the screen.
        /// </summary>
        private void ApplySize()
        {
            try
            {
                Rectangle wa = Screen.FromRectangle(Bounds).WorkingArea;
                float dpi = Dpi;
                Size want = _userSize.IsEmpty
                    ? AutoSizeFor(wa)
                    : new Size((int)Math.Round(_userSize.Width * dpi), (int)Math.Round(_userSize.Height * dpi));
                want = FitToArea(want, wa);
                int x = Left, y = Top;
                if (x + want.Width > wa.Right) { x = Math.Max(wa.Left, wa.Right - want.Width); }
                if (y + want.Height > wa.Bottom) { y = Math.Max(wa.Top, wa.Bottom - want.Height); }
                var b = new Rectangle(x, y, want.Width, want.Height);
                if (b != Bounds) { Bounds = b; }
            }
            catch { }
        }

        /// <summary>The panel's size in 96-DPI pixels — what is saved, so it means the same on any display.</summary>
        public Size LogicalSize
        {
            get
            {
                float dpi = Math.Max(0.5f, Dpi);
                return new Size((int)Math.Round(Width / dpi), (int)Math.Round(Height / dpi));
            }
        }

        /// <summary>
        /// The size the user chose (96-DPI pixels), or <see cref="Size.Empty"/> to follow the font again.
        /// </summary>
        public void SetUserSize(Size logical)
        {
            _userSize = logical.Width > 0 && logical.Height > 0 ? logical : Size.Empty;
            ApplySize();
            Repaint();
        }

        /// <summary>True once the user has sized the panel themselves.</summary>
        public bool HasUserSize => !_userSize.IsEmpty;

        /// <summary>
        /// Back to the default place and size on the monitor the panel is on now — "Default place and
        /// size" in its menu.
        /// </summary>
        public void ResetToDefault()
        {
            try
            {
                _userSize = Size.Empty;
                Bounds = DefaultBounds(Screen.FromRectangle(Bounds));
                Repaint();
            }
            catch { }
        }

        public void SetFontSize(int points)
        {
            points = Math.Max(10, Math.Min(72, points));
            if (points != _fontPoints)
            {
                _fontPoints = points;
                // A size the user chose is theirs: the text re-wraps inside it instead.
                if (_userSize.IsEmpty) { ApplySize(); }
                Repaint();
            }
        }

        public void SetFontFamily(string family)
        {
            if (!string.IsNullOrWhiteSpace(family) && family != _fontFamily)
            {
                _fontFamily = family; Repaint();
            }
        }

        public void SetTextColor(Color color, bool use)
        {
            _textColor = color; _useCustomColor = use; Repaint();
        }

        public void SetTextOpacity(int percent)
        {
            _textOpacityPercent = Math.Max(10, Math.Min(100, percent)); Repaint();
        }

        /// <summary>
        /// Whether captions are running. Only changes what an EMPTY panel says, and whether the
        /// newest line is marked as the one being spoken.
        /// </summary>
        public void SetCaptionsOn(bool on)
        {
            if (on == _captionsOn) { return; }
            _captionsOn = on;
            Repaint();
        }

        /// <summary>The newest lines of <paramref name="transcript"/>, speaker labels and all.</summary>
        public void SetHistory(Utils.CaptionTranscript transcript)
        {
            var list = new List<Entry>();
            if (transcript != null)
            {
                int n = transcript.Lines.Count;
                for (int i = Math.Max(0, n - KeepEntries); i < n; i++)
                {
                    list.Add(new Entry
                    {
                        Text = transcript.Lines[i] ?? "",
                        Speaker = transcript.SpeakerOf(i),
                        LabelLength = transcript.LabelLengthOf(i)
                    });
                }
            }
            SetEntries(list);
        }

        /// <summary>Plain lines, no speaker colours.</summary>
        public void SetHistory(IList<string> lines)
        {
            var list = new List<Entry>();
            if (lines != null)
            {
                for (int i = Math.Max(0, lines.Count - KeepEntries); i < lines.Count; i++)
                {
                    list.Add(new Entry { Text = lines[i] ?? "" });
                }
            }
            SetEntries(list);
        }

        /// <summary>How many lines the panel holds to draw from (the newest ones).</summary>
        public int EntryCount => _entries.Count;

        private void SetEntries(List<Entry> list)
        {
            string oldNewest = _newestText;
            int n = list.Count;
            string newest = n > 0 ? list[n - 1].Text : null;

            // A NEW line: the one that was newest is now second-newest. Growth of the newest line
            // (more words, a revision) leaves the second-newest alone.
            bool arrived = n > 0 && oldNewest != null
                ? (n >= 2 && list[n - 2].Text == oldNewest)
                : (n > 0 && _entries.Count == 0);
            bool grew = !arrived && n > 0 && newest != oldNewest;

            _entries.Clear();
            _entries.AddRange(list);
            _newestText = newest;

            if (n == 0)
            {
                _arrive = 1; _slide = 0; _slidePending = false; _pulse = 0;
            }
            else if ((arrived || grew) && Visible && !CaptionOverlayForm.LowImpactMode)
            {
                // Only on screen, and never while a fullscreen game has it: a hidden panel filling
                // up in the background must open at rest, not replay every arrival.
                _pulse = 1.0;
                _lastGrowthTick = Environment.TickCount64;
                if (arrived)
                {
                    _arrive = 0;
                    _slidePending = true;
                }
                EnsureAnimating();
            }
            Repaint();
        }

        /// <summary>
        /// How strongly a line <paramref name="age"/> lines older than the newest is drawn. The newest
        /// is full strength; the ones above step down gently and level off, so old lines stay easy to
        /// read — they just stop competing with the new one.
        /// </summary>
        internal static double RecencyAlpha(int age)
        {
            switch (age)
            {
                case 0: return 1.0;
                case 1: return 0.90;
                case 2: return 0.82;
                case 3: return 0.76;
                case 4: return 0.72;
                default: return 0.70;
            }
        }

        public void ApplyTheme(Theme theme)
        {
            if (theme != null) { _theme = theme; Repaint(); }
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_LAYERED = 0x00080000;
                const int WS_EX_TRANSPARENT = 0x00000020;
                const int WS_EX_NOACTIVATE = 0x08000000;
                const int WS_EX_TOOLWINDOW = 0x00000080;
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                if (!_movable)
                {
                    cp.ExStyle |= WS_EX_TRANSPARENT;
                }
                return cp;
            }
        }

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT_FLAG = 0x00000020;

        /// <summary>Turns drag-to-move on/off (off = click-through, as normal).</summary>
        public void SetMovable(bool movable)
        {
            // Every show now passes the current mode; only a real change touches the window.
            if (movable == _movable) { return; }
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

        /// <summary>True while the panel can be dragged (move mode).</summary>
        public bool Movable => _movable;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        private const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004,
                           SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020;

        /// <summary>
        /// A drag or resize ended somewhere new: the bounds, and whether the SIZE changed (a plain
        /// move leaves a font-following size alone).
        /// </summary>
        public event Action<Rectangle, bool> PlacementCommitted;

        /// <summary>Double-clicked while movable: "I'm done moving".</summary>
        public event Action LockRequested;

        /// <summary>Right-clicked while movable, at this screen point.</summary>
        public event Action<Point> ContextRequested;

        /// <summary>The other caption window's bounds while it is showing, to snap to.</summary>
        internal Func<Rectangle?> SnapPartner;

        private Rectangle _sizeMoveStart;

        protected override void WndProc(ref Message m)
        {
            if (_movable)
            {
                switch (m.Msg)
                {
                    case CaptionWindowChrome.WM_NCHITTEST:
                    {
                        Point client = PointToClient(CaptionWindowChrome.PointFromLParam(m.LParam));
                        m.Result = (IntPtr)CaptionWindowChrome.EdgeHitTest(client, ClientSize, (int)Math.Round(8 * Dpi));
                        return;
                    }
                    case CaptionWindowChrome.WM_MOVING:
                    {
                        Rectangle? partner = null;
                        try { partner = SnapPartner?.Invoke(); } catch { }
                        if (CaptionWindowChrome.SnapMoving(ref m, Dpi, partner)) { return; }
                        break;
                    }
                    case CaptionWindowChrome.WM_NCLBUTTONDBLCLK:
                        // Never to Windows: a double-click on a "caption" maximises a window whose
                        // style has WS_MAXIMIZEBOX — the same bit as the WS_TABSTOP every WinForms
                        // form carries — so it would blow the panel up to the whole screen.
                        m.Result = IntPtr.Zero;
                        LockRequested?.Invoke();
                        return;
                    case CaptionWindowChrome.WM_NCRBUTTONUP:
                        m.Result = IntPtr.Zero;
                        ContextRequested?.Invoke(CaptionWindowChrome.PointFromLParam(m.LParam));
                        return;
                    case CaptionWindowChrome.WM_CONTEXTMENU:
                        m.Result = IntPtr.Zero;
                        return;
                }
            }

            if (m.Msg == CaptionWindowChrome.WM_GETMINMAXINFO && m.LParam != IntPtr.Zero)
            {
                base.WndProc(ref m);
                try
                {
                    var mmi = Marshal.PtrToStructure<CaptionWindowChrome.MINMAXINFO>(m.LParam);
                    Size min = MinSize;
                    mmi.MinTrackSize.X = min.Width;
                    mmi.MinTrackSize.Y = min.Height;
                    Marshal.StructureToPtr(mmi, m.LParam, false);
                }
                catch { }
                return;
            }

            if (m.Msg == CaptionWindowChrome.WM_ENTERSIZEMOVE)
            {
                _sizeMoveStart = Bounds;
            }
            base.WndProc(ref m);
            if (m.Msg == CaptionWindowChrome.WM_EXITSIZEMOVE && Bounds != _sizeMoveStart)
            {
                bool resized = Bounds.Size != _sizeMoveStart.Size;
                if (resized) { _userSize = LogicalSize; }
                PlacementCommitted?.Invoke(Bounds, resized);
            }
        }

        public void MoveTo(Point p)
        {
            try { Location = p; Repaint(); } catch { }
        }

        /// <summary>Clamps the history window back onto the visible work area (shrinking it if it must).</summary>
        public void EnsureOnScreen()
        {
            try
            {
                var wa = Screen.FromControl(this).WorkingArea;
                Size sz = FitToArea(Size, wa);
                int x = Left, y = Top;
                if (x + sz.Width > wa.Right) x = wa.Right - sz.Width;
                if (y + sz.Height > wa.Bottom) y = wa.Bottom - sz.Height;
                if (x < wa.Left) x = wa.Left;
                if (y < wa.Top) y = wa.Top;
                var b = new Rectangle(x, y, sz.Width, sz.Height);
                if (b != Bounds)
                {
                    Bounds = b;
                    Repaint();
                }
            }
            catch { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Kept above borderless-fullscreen games like the bar. It never was: the first game
            // that took the foreground buried the history under it for good.
            OverlayTopmost.Register(Handle);
            // The size was worked out before the window existed, at the primary screen's scale;
            // now the window knows the scale of the monitor it is actually on.
            ApplySize();
            Repaint();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            OverlayTopmost.Unregister(Handle);
            base.OnFormClosed(e);
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            _metricsKey = null;
            _wrapCache.Clear();
            ApplySize();
            Repaint();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible)
            {
                // Nothing to animate while hidden; settle so the next show starts at rest.
                _anim.Stop();
                _arrive = 1; _slide = 0; _slidePending = false; _pulse = 0;
            }
            else
            {
                Repaint();
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Repaint();
        }

        // ── Animation ──────────────────────────────────────────────────────────
        private void EnsureAnimating()
        {
            if (!Visible) { return; }
            if (CaptionOverlayForm.LowImpactMode)
            {
                // A fullscreen game has the screen: no frames for show.
                _anim.Stop();
                _arrive = 1; _slide = 0; _slidePending = false; _pulse = 0;
                return;
            }
            if (!_anim.Enabled) { _anim.Start(); }
        }

        private void AnimationTick()
        {
            if (CaptionOverlayForm.LowImpactMode || !Visible)
            {
                _anim.Stop();
                _arrive = 1; _slide = 0; _slidePending = false; _pulse = 0;
                Repaint();
                return;
            }
            bool busy = false;
            if (_arrive < 1.0)
            {
                _arrive = Math.Min(1.0, _arrive + 0.14);
                busy = true;
            }
            if (_slide > 0.5f)
            {
                _slide *= 0.70f;
                busy = true;
            }
            else
            {
                _slide = 0;
            }
            if (Environment.TickCount64 - _lastGrowthTick < 350)
            {
                _pulse = 1.0;
                busy = true;
            }
            else if (_pulse > 0.02)
            {
                _pulse *= 0.90;
                busy = true;
            }
            else
            {
                _pulse = 0;
            }
            Repaint();
            if (!busy) { _anim.Stop(); }
        }

        // ── Layered-window painting ──────────────────────────────────────────
        private Bitmap _surface;

        private void Repaint()
        {
            if (!IsHandleCreated || Width <= 0 || Height <= 0)
            {
                return;
            }

            try
            {
                // One drawing surface, rebuilt only when the size changes — the bar measured a
                // fresh bitmap per frame at ~5x the cost of reusing one, and this panel now
                // animates too.
                if (_surface == null || _surface.Width != Width || _surface.Height != Height)
                {
                    var old = _surface;
                    _surface = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    _surface.SetResolution(96f, 96f);   // measure/draw in 96-DPI space (see overlay)
                    try { old?.Dispose(); } catch { }
                }
                using (var g = Graphics.FromImage(_surface))
                {
                    DrawContent(g);
                }
                PushLayered(_surface);
            }
            catch (Exception ex)
            {
                // A paint failure (odd glyphs, a font with no Bold face) must never
                // escape onto the UI thread and crash Tempo — skip the frame instead.
                // The caption overlay carries the same guard from a real crash report.
                if (!_paintFailLogged)
                {
                    _paintFailLogged = true;
                    Utils.Logger.Warn("[Captions] history paint failed (skipped frame): " + ex.Message);
                }
            }
        }

        private bool _paintFailLogged;

        // Word widths and wrapped lines, so a repaint only measures what is new. Keyed by the font
        // (and, for wrapping, the width and colour); cleared when either changes.
        private readonly Dictionary<string, float> _wordW = new Dictionary<string, float>();
        private readonly Dictionary<string, List<Row>> _wrapCache = new Dictionary<string, List<Row>>();
        private string _metricsKey;
        private string _wrapKey;
        private float _spaceW;
        private float _lineH;

        private struct Run
        {
            public string Text;
            public Color Color;
            // A continuation piece of one token too wide for a row: no word gap before it.
            public bool Glue;
        }

        private sealed class Row
        {
            public readonly List<Run> Runs = new List<Run>();
            public float Width;
        }

        private void DrawContent(Graphics g)
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;

            float dpi = Dpi;
            int radius = Math.Max(8, (int)Math.Round(14 * dpi));
            var panelRect = new Rectangle(0, 0, Width - 1, Height - 1);

            // The panel: the theme's surface, deepened so light text reads on it, top to bottom a
            // touch darker — the bar's gradient. It was a fixed near-black in every theme.
            Color surface = _theme != null ? _theme.Surface : Color.FromArgb(14, 16, 26);
            Color top = Darken(surface, 0.45), bottom = Darken(surface, 0.62);
            int panelA = _movable ? MovePanelAlpha : RestPanelAlpha;
            using (var path = CaptionWindowChrome.Rounded(panelRect, radius))
            {
                using (var bg = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height)),
                    Color.FromArgb(panelA, top), Color.FromArgb(panelA, bottom), 90f))
                {
                    g.FillPath(bg, path);
                }
                if (!_movable)
                {
                    // A hairline so the panel's edge reads over a busy game without a heavier fill.
                    Color border = _theme != null ? _theme.Border : Color.FromArgb(90, 90, 110);
                    using (var pen = new Pen(Color.FromArgb(60, border), 1f))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }

            int textA = (int)Math.Round(255 * (_textOpacityPercent / 100.0));
            // In move mode the lines are only a preview under the move chrome.
            if (_movable) { textA = (int)(textA * 0.6); }
            Color body = _useCustomColor ? _textColor : (_theme != null ? _theme.Text : Color.White);
            body = Color.FromArgb(255, body.R, body.G, body.B);

            // Same trap the caption bar had: FontFamily.GenericSansSerif is a single PROCESS-WIDE
            // cached instance, and `using (fam)` below disposes whatever it is handed.
            // SafeFallbackFamily always returns an instance we own.
            FontFamily fam;
            try { fam = new FontFamily(_fontFamily); }
            catch { fam = CaptionOverlayForm.SafeFallbackFamily(); }

            // Some installed fonts have no Bold face; constructing a Bold Font from them throws.
            FontStyle style = fam.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold
                            : (fam.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular : FontStyle.Italic);
            Font font;
            try { font = new Font(fam, _fontPoints * dpi, style, GraphicsUnit.Point); }
            catch
            {
                try { fam.Dispose(); } catch { }
                fam = CaptionOverlayForm.SafeFallbackFamily();
                style = fam.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;
                font = new Font(fam, _fontPoints * dpi, style, GraphicsUnit.Point);
            }

            using (fam)
            using (font)
            using (var typo = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                typo.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoClip;
                float emSize = font.SizeInPoints * 1.333f;
                RefreshTextMetrics(g, font, typo, emSize);

                float markerX = 9 * dpi;
                float left = 20 * dpi, right = Width - 16 * dpi;
                float innerTop = 12 * dpi, innerBottom = Height - 12 * dpi;
                if (_movable)
                {
                    // Below the move chip, so it never sits on the first line.
                    Rectangle chip = CaptionWindowChrome.ChipRect(g, Size, MoveHint, dpi);
                    if (!chip.IsEmpty) { innerTop = Math.Max(innerTop, chip.Bottom + 6 * dpi); }
                }
                float maxW = Math.Max(40, right - left);
                var inner = new RectangleF(3 * dpi, Math.Max(4 * dpi, innerTop - 8 * dpi), Width - 6 * dpi, 0);
                inner.Height = Height - 4 * dpi - inner.Top;

                if (_entries.Count == 0)
                {
                    DrawPlaceholder(g, font, emSize, body, textA, new RectangleF(left, innerTop, maxW, innerBottom - innerTop));
                }
                else
                {
                    DrawEntries(g, font, typo, emSize, body, textA, left, maxW, innerTop, innerBottom, markerX, inner);
                }
            }

            if (_movable)
            {
                CaptionWindowChrome.DrawMoveChrome(g, Size, _theme, MoveHint, dpi, radius, resizeGrip: true);
            }
        }

        private static string MoveHint => Utils.Localization.T("Drag to move · drag an edge to resize · double-click when done");

        /// <summary>
        /// The newest lines, bottom-up: each wrapped to the panel's width, the newest at the bottom,
        /// older ones above for as far as they fit (a line cut by the top edge fades out into it).
        /// </summary>
        private void DrawEntries(Graphics g, Font font, StringFormat typo, float emSize, Color body, int textA,
                                 float left, float maxW, float innerTop, float innerBottom, float markerX, RectangleF clip)
        {
            float dpi = Dpi;
            float gap = _lineH * 0.35f;
            float avail = innerBottom - innerTop;

            // Wrap from the newest back until the rows cover the panel twice over — enough for a
            // glide that starts a whole panel low.
            var laid = new List<KeyValuePair<int, List<Row>>>();
            float total = 0;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                List<Row> rows = Wrap(g, font, typo, _entries[i], body, maxW);
                if (rows.Count == 0) { continue; }
                laid.Insert(0, new KeyValuePair<int, List<Row>>(i, rows));
                total += rows.Count * _lineH + (laid.Count > 1 ? gap : 0);
                if (total > avail * 2 + _lineH) { break; }
            }
            if (laid.Count == 0) { return; }

            float contentH = 0;
            foreach (var kv in laid) { contentH += kv.Value.Count * _lineH; }
            contentH += gap * (laid.Count - 1);

            // A new line glides up from below: it starts one line-height (its own) lower, and the
            // lines above make room as it rises. Only when the panel is full — until then nothing
            // above it moves.
            bool overflow = contentH > avail;
            if (_slidePending)
            {
                _slidePending = false;
                var newest = laid[laid.Count - 1].Value;
                _slide = overflow ? Math.Min(avail, newest.Count * _lineH + gap) : 0;
                _slideStart = _slide;
            }
            // Lines pushed past the top edge fade out as they go, and are gone once the glide ends:
            // at rest only WHOLE lines show (the newest excepted), never a fragment of one cut in
            // half by the edge with its speaker's label above it, out of sight.
            float leave = _slideStart > 0.5f ? Math.Max(0f, Math.Min(1f, _slide / _slideStart)) : 0f;

            // Top-anchored until the panel fills, then the newest line sits on the bottom edge.
            float restY = overflow ? innerBottom - contentH : innerTop;

            var saveClip = g.Clip;
            g.SetClip(clip);
            try
            {
                int newestIndex = _entries.Count - 1;
                for (int k = 0; k < laid.Count; k++)
                {
                    int entryIndex = laid[k].Key;
                    List<Row> rows = laid[k].Value;
                    int age = newestIndex - entryIndex;
                    bool isNewest = age == 0;
                    float entryH = rows.Count * _lineH;
                    float entryRestTop = restY;
                    restY += entryH + gap;

                    double a = RecencyAlpha(age);
                    if (!isNewest && entryRestTop < innerTop - 0.5f)
                    {
                        if (leave <= 0.01f) { continue; }
                        a *= leave;
                    }
                    float rise = 0;
                    if (isNewest)
                    {
                        a *= _arrive;
                        rise = (float)((1.0 - _arrive) * 6 * dpi);
                    }

                    float entryTop = entryRestTop + _slide + rise;
                    float rowY = entryTop;
                    foreach (Row row in rows)
                    {
                        if (rowY + _lineH > innerTop - _lineH && rowY < innerBottom + _lineH)
                        {
                            // A row the top edge cuts through fades into it (while gliding, or the
                            // newest line when it is taller than the whole panel).
                            double edge = Math.Max(0, Math.Min(1, (rowY + _lineH - innerTop) / _lineH));
                            int ra = (int)(textA * a * edge);
                            if (ra > 2) { DrawRow(g, font, typo, emSize, row, left, rowY, ra, dpi); }
                        }
                        rowY += _lineH;
                    }

                    if (isNewest && _captionsOn && !_movable)
                    {
                        DrawLiveMarker(g, entryIndex, body, textA, markerX, entryTop, rowY, innerTop, innerBottom, dpi);
                    }
                }
            }
            finally
            {
                g.Clip = saveClip;
                saveClip.Dispose();
            }
        }

        /// <summary>
        /// The thin bar beside the line being spoken: the speaker's colour (the caption colour for an
        /// unlabelled line), glowing while its words arrive and settling to a faint mark after.
        /// </summary>
        private void DrawLiveMarker(Graphics g, int entryIndex, Color body, int textA, float x,
                                    float top, float bottom, float innerTop, float innerBottom, float dpi)
        {
            top = Math.Max(top + 3 * dpi, innerTop);
            bottom = Math.Min(bottom - 3 * dpi, innerBottom);
            if (bottom - top < 4) { return; }
            Entry e = _entries[entryIndex];
            Color c = e.Speaker > 0 && e.LabelLength > 0 ? CaptionOverlayForm.SpeakerColor(e.Speaker) : body;
            int a = (int)(textA * _arrive * (0.35 + 0.65 * _pulse));
            if (a <= 2) { return; }
            float w = Math.Max(2f, 3 * dpi);
            var r = new RectangleF(x - w / 2, top, w, bottom - top);
            using (var path = new GraphicsPath())
            {
                float d = w;
                path.AddArc(r.X, r.Y, d, d, 180, 180);
                path.AddArc(r.X, r.Bottom - d, d, d, 0, 180);
                path.CloseFigure();
                if (_pulse > 0.05)
                {
                    using (var glow = new Pen(Color.FromArgb((int)(a * 0.35 * _pulse), c), w * 2.6f) { LineJoin = LineJoin.Round })
                    {
                        g.DrawPath(glow, path);
                    }
                }
                using (var b = new SolidBrush(Color.FromArgb(a, c)))
                {
                    g.FillPath(b, path);
                }
            }
        }

        /// <summary>One row of runs: a soft shadow, a dark outline, then each run in its colour.</summary>
        private void DrawRow(Graphics g, Font font, StringFormat typo, float emSize, Row row, float left, float y, int alpha, float dpi)
        {
            var runPaths = new List<KeyValuePair<GraphicsPath, Color>>(row.Runs.Count);
            var combined = new GraphicsPath();
            try
            {
                float x = left;
                bool first = true;
                foreach (Run t in row.Runs)
                {
                    if (!first && !t.Glue) { x += _spaceW; }
                    first = false;
                    var p = new GraphicsPath();
                    p.AddString(t.Text, font.FontFamily, (int)font.Style, emSize, new PointF(x, y), typo);
                    runPaths.Add(new KeyValuePair<GraphicsPath, Color>(p, t.Color));
                    // AddPath throws on an EMPTY path — a word with no glyphs in this font.
                    if (p.PointCount > 0) { combined.AddPath(p, false); }
                    x += WordWidth(g, font, typo, t.Text);
                }
                if (combined.PointCount == 0) { return; }

                var saved = g.Transform;
                g.TranslateTransform(1.5f * dpi, 1.5f * dpi);
                using (var sh = new SolidBrush(Color.FromArgb((int)(alpha * 0.55), 0, 0, 0)))
                {
                    g.FillPath(sh, combined);
                }
                g.Transform = saved;

                using (var pen = new Pen(Color.FromArgb(alpha, 0, 0, 0), 2.6f * dpi) { LineJoin = LineJoin.Round })
                {
                    g.DrawPath(pen, combined);
                }
                foreach (var rp in runPaths)
                {
                    if (rp.Key.PointCount == 0) { continue; }
                    using (var brush = new SolidBrush(Color.FromArgb(alpha, rp.Value.R, rp.Value.G, rp.Value.B)))
                    {
                        g.FillPath(brush, rp.Key);
                    }
                }
            }
            finally
            {
                try { combined.Dispose(); } catch { }
                foreach (var rp in runPaths) { try { rp.Key.Dispose(); } catch { } }
            }
        }

        /// <summary>
        /// Said while there is nothing to show — centred, smaller and softer than a caption.
        ///
        /// With no lines the panel used to draw only its faint ~20% background: an almost invisible
        /// rectangle, which looked exactly like the window failing to open. It said so in English
        /// only, and promised captions "as they're transcribed" with captions switched off.
        /// </summary>
        private void DrawPlaceholder(Graphics g, Font font, float emSize, Color body, int textA, RectangleF area)
        {
            string text = Utils.Localization.T(_captionsOn
                ? "Captions will appear here as they're transcribed…"
                : "Captions are off. What's said will collect here once they're on.");
            int a = (int)(textA * 0.85);
            using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var path = new GraphicsPath())
            {
                path.AddString(text, font.FontFamily, (int)font.Style, emSize * 0.72f, area, fmt);
                if (path.PointCount == 0) { return; }
                using (var pen = new Pen(Color.FromArgb(a, 0, 0, 0), 2.2f * Dpi) { LineJoin = LineJoin.Round })
                {
                    g.DrawPath(pen, path);
                }
                Color c = _captionsOn ? body : Blend(body, Color.FromArgb(200, 204, 214), 0.45);
                using (var b = new SolidBrush(Color.FromArgb(a, c)))
                {
                    g.FillPath(b, path);
                }
            }
        }

        /// <summary>
        /// One line split into coloured runs and wrapped to <paramref name="maxW"/>: the label as one
        /// unbreakable run in the speaker's colour, each word in the caption colour, and a token wider
        /// than a whole row (Chinese or Japanese without spaces, a long link) broken by characters.
        /// Cached: the lines above the newest never change, so a repaint re-wraps only the newest.
        /// </summary>
        private List<Row> Wrap(Graphics g, Font font, StringFormat typo, Entry e, Color body, float maxW)
        {
            string wrapKey = _metricsKey + "|" + maxW.ToString("0.0") + "|" + body.ToArgb();
            if (wrapKey != _wrapKey)
            {
                _wrapKey = wrapKey;
                _wrapCache.Clear();
            }
            string key = e.Speaker + "|" + e.LabelLength + "|" + (e.Text ?? "");
            if (_wrapCache.TryGetValue(key, out List<Row> cached)) { return cached; }

            var toks = new List<Run>();
            string text = e.Text ?? "";
            int labelLen = Math.Max(0, Math.Min(e.LabelLength, text.Length));
            if (e.Speaker > 0 && labelLen > 0)
            {
                string label = text.Substring(0, labelLen).Trim();
                if (label.Length > 0)
                {
                    toks.Add(new Run { Text = label, Color = CaptionOverlayForm.SpeakerColor(e.Speaker) });
                }
                text = text.Substring(labelLen);
            }
            foreach (string w in text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                toks.Add(new Run { Text = w, Color = body });
            }

            var rows = new List<Row>();
            var cur = new Row();
            foreach (Run tok in toks)
            {
                foreach (Run t in SplitToFit(g, font, typo, tok, maxW))
                {
                    float w = WordWidth(g, font, typo, t.Text);
                    float gapW = (cur.Runs.Count == 0 || t.Glue) ? 0 : _spaceW;
                    if (cur.Runs.Count > 0 && cur.Width + gapW + w > maxW)
                    {
                        rows.Add(cur);
                        cur = new Row();
                        gapW = 0;
                    }
                    cur.Width += gapW + w;
                    cur.Runs.Add(t);
                }
            }
            if (cur.Runs.Count > 0) { rows.Add(cur); }

            if (_wrapCache.Count > 400) { _wrapCache.Clear(); }
            _wrapCache[key] = rows;
            return rows;
        }

        /// <summary>A token as one run, or — when it is wider than a row — pieces that each fit, glued.</summary>
        private IEnumerable<Run> SplitToFit(Graphics g, Font font, StringFormat typo, Run t, float maxW)
        {
            if (t.Text.Length <= 2 || WordWidth(g, font, typo, t.Text) <= maxW)
            {
                yield return t;
                yield break;
            }
            int start = 0;
            bool first = true;
            while (start < t.Text.Length)
            {
                // The longest piece that fits, found by halving rather than one character at a time.
                int lo = 1, hi = t.Text.Length - start;
                while (lo < hi)
                {
                    int mid = (lo + hi + 1) / 2;
                    if (MeasureOnce(g, font, typo, t.Text.Substring(start, mid)) <= maxW) { lo = mid; }
                    else { hi = mid - 1; }
                }
                yield return new Run { Text = t.Text.Substring(start, lo), Color = t.Color, Glue = !first || t.Glue };
                start += lo;
                first = false;
            }
        }

        /// <summary>Re-primes the cached space/line metrics when the font changes.</summary>
        private void RefreshTextMetrics(Graphics g, Font font, StringFormat typo, float emSize)
        {
            string key = font.FontFamily.Name + "|" + font.SizeInPoints + "|" + (int)font.Style;
            if (key == _metricsKey) { return; }
            _metricsKey = key;
            _wordW.Clear();
            _wrapCache.Clear();
            _spaceW = g.MeasureString(" ", font, PointF.Empty, typo).Width;
            if (_spaceW <= 0) { _spaceW = font.Size * 0.33f; }
            int em = font.FontFamily.GetEmHeight(font.Style);
            int ls = font.FontFamily.GetLineSpacing(font.Style);
            _lineH = em > 0 ? emSize * ls / em : font.GetHeight(g);
            if (_lineH <= 1) { _lineH = emSize * 1.3f; }
        }

        /// <summary>Advance width of one word at the current font, cached.</summary>
        private float WordWidth(Graphics g, Font font, StringFormat typo, string word)
        {
            if (_wordW.TryGetValue(word, out float w)) { return w; }
            w = MeasureOnce(g, font, typo, word);
            if (_wordW.Count > 1500) { _wordW.Clear(); }
            _wordW[word] = w;
            return w;
        }

        private static float MeasureOnce(Graphics g, Font font, StringFormat typo, string text)
        {
            return g.MeasureString(text, font, PointF.Empty, typo).Width;
        }

        private static Color Darken(Color c, double amount)
        {
            amount = Math.Max(0, Math.Min(1, amount));
            return Color.FromArgb(c.A, (int)(c.R * (1 - amount)), (int)(c.G * (1 - amount)), (int)(c.B * (1 - amount)));
        }

        private static Color Blend(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(255,
                (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        // ── UpdateLayeredWindow plumbing (true per-pixel alpha) ──────────────
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
