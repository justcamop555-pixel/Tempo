using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// What a tab shows while its feature is turned off in Settings → Disable features: the page's own backdrop with one
    /// card on it — what is off, that its hotkeys are let go of, and a button that turns it back on.
    ///
    /// It fills the page, and the page does not scroll meanwhile (MainForm.Features.cs turns its AutoScroll off), so none
    /// of the feature's controls under it can be clicked. Tab stays on its button and the wheel stops here, so none can be
    /// reached from the keyboard or scrolled into view either. The start paths refuse on their own as well — this is
    /// what the user SEES, not the lock.
    ///
    /// Everything but the button is painted here, not made of labels: ThemeManager re-themes a page's labels on every
    /// theme change and gives see-through ones the page's backdrop, which would have painted the wallpaper inside the card.
    /// It measures its own text and scales its spacing by the window's DPI, so it is right whenever it is made — a control
    /// added after the window scaled is not scaled by WinForms (MatchWindowScale's problem).
    /// </summary>
    public sealed class FeatureOffCover : Panel
    {
        private readonly Button _button;
        private Theme _theme;
        private string _title = "";
        private string _body = "";
        private Rectangle _card;
        private Rectangle _titleRect;
        private Rectangle _bodyRect;
        private readonly Font _titleFont = new Font("Segoe UI Semibold", 13f);
        private readonly Font _bodyFont = new Font("Segoe UI", 9.5f);

        /// <summary>The card's width at 100%: wide enough for two short lines, narrow enough for a laptop window.</summary>
        private const int CardWidth96 = 500;

        public event EventHandler TurnOnClicked;

        public FeatureOffCover()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            DoubleBuffered = true;
            TabStop = false;
            _button = UiFactory.Button("", 0, 0, 160, 34);
            _button.TabIndex = 0;
            _button.Click += (s, e) => TurnOnClicked?.Invoke(this, EventArgs.Empty);
            Controls.Add(_button);
        }

        /// <summary>The "Turn on" button, for the window to style as its primary button.</summary>
        public Button Button => _button;

        // ── In front of everything on its page, whatever is brought forward later ─────────────────

        private Control _watchedParent;
        private bool _refrontQueued;

        /// <summary>How many times something on the page was brought in front of the cover and it went back on top.</summary>
        public int Refronted { get; private set; }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            if (_watchedParent != null) { _watchedParent.Layout -= OnParentLayout; }
            _watchedParent = Parent;
            if (_watchedParent != null) { _watchedParent.Layout += OnParentLayout; }
        }

        /// <summary>
        /// A control of the page brought to the front (BringToFront lays the page out with "ChildIndex") lay over the
        /// cover: the Multi-Point tab's "No points yet" line did each time the empty list was refreshed, and showed on
        /// the turned-off tab. The cover goes back on top — a moment later, never inside the page's own layout.
        /// </summary>
        private void OnParentLayout(object sender, LayoutEventArgs e)
        {
            if (!Covering || _refrontQueued || IsDisposed || !(sender is Control page) || !ReferenceEquals(page, Parent)) { return; }
            if (page.Controls.Count == 0 || ReferenceEquals(page.Controls[0], this)) { return; }
            if (!IsHandleCreated) { BringToFront(); Refronted++; return; }
            _refrontQueued = true;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    _refrontQueued = false;
                    if (IsDisposed || !Covering || Parent == null) { return; }
                    if (Parent.Controls.Count > 0 && !ReferenceEquals(Parent.Controls[0], this))
                    {
                        BringToFront();
                        Refronted++;
                    }
                }));
            }
            catch { _refrontQueued = false; }
        }

        /// <summary>
        /// Set while the cover is meant to lie over its page (MainForm.CoverPage / Uncover). The layout checks read it to
        /// leave a covered page alone: Visible answers false for every child of a tab that is not on show, and a tab not
        /// yet visited has no window to ask instead.
        /// </summary>
        public bool Covering { get; internal set; }

        /// <summary>The words on the card. The title is one line; the body wraps.</summary>
        public void SetText(string title, string body, string button)
        {
            _title = title ?? "";
            _body = body ?? "";
            _button.Text = button ?? "";
            AccessibleName = _title;
            AccessibleDescription = _body;
            LayoutCard();
            Invalidate();
        }

        public string Title => _title;
        public string Body => _body;

        public void ApplyTheme(Theme theme)
        {
            _theme = theme;
            if (theme != null) { BackColor = theme.Background; }
            Invalidate();
        }

        private float Scale96 => Math.Max(1f, DeviceDpi / 96f);

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            LayoutCard();
        }

        /// <summary>
        /// Something lying over this cover from outside the page, in screen pixels, or an empty rectangle. Asked at every
        /// layout; the window lays its covers out again when that changes (MainForm.RelayoutFeatureCovers). It was the
        /// startup safety note in the window's top right, which sat on the right half of "Clicker is turned off"; since
        /// 2026-11-12 the note lies in the middle over a frosted copy of the whole window, the cover with it, and nothing
        /// sets this — kept for anything that lies over a page part of the way again.
        /// </summary>
        public static Func<FeatureOffCover, Rectangle> KeepClearOf;

        /// <summary>A card centred across, a third of the way down: title, words, button.</summary>
        private void LayoutCard()
        {
            float k = Scale96;
            int pad = (int)Math.Round(24 * k);
            int gap = (int)Math.Round(10 * k);
            int width = Math.Min(Math.Max(0, ClientSize.Width - 2 * pad), (int)Math.Round(CardWidth96 * k));
            if (width <= 2 * pad) { return; }
            int inner = width - 2 * pad;
            const TextFormatFlags wrap = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix;
            int titleH = TextRenderer.MeasureText(_title.Length > 0 ? _title : " ", _titleFont, new Size(inner, int.MaxValue), wrap).Height;
            int bodyH = _body.Length > 0 ? TextRenderer.MeasureText(_body, _bodyFont, new Size(inner, int.MaxValue), wrap).Height : 0;

            Size text = TextRenderer.MeasureText(_button.Text.Length > 0 ? _button.Text : " ", _button.Font);
            int buttonW = Math.Min(inner, Math.Max((int)Math.Round(150 * k), text.Width + (int)Math.Round(40 * k)));
            int buttonH = Math.Max((int)Math.Round(34 * k), text.Height + (int)Math.Round(14 * k));

            int height = pad + titleH + (bodyH > 0 ? gap + bodyH : 0) + 2 * gap + buttonH + pad;
            int left = (ClientSize.Width - width) / 2;
            int top = Math.Max(pad, (ClientSize.Height - height) / 3);
            // Clear of what lies over the page (the safety note): below it while there is room for the whole card there.
            Rectangle over = Rectangle.Empty;
            try
            {
                Rectangle s = KeepClearOf != null && IsHandleCreated ? KeepClearOf(this) : Rectangle.Empty;
                if (s.Width > 0 && s.Height > 0) { over = RectangleToClient(s); }
            }
            catch { }
            if (over.Width > 0 && new Rectangle(left, top, width, height).IntersectsWith(over))
            {
                int below = over.Bottom + 2 * gap;
                if (below + height <= ClientSize.Height - pad) { top = below; }
            }
            _card = new Rectangle(left, top, width, height);
            _titleRect = new Rectangle(left + pad, top + pad, inner, titleH);
            _bodyRect = new Rectangle(left + pad, _titleRect.Bottom + gap, inner, bodyH);
            var buttonBounds = new Rectangle(left + (width - buttonW) / 2, (bodyH > 0 ? _bodyRect.Bottom : _titleRect.Bottom) + 2 * gap,
                                             buttonW, buttonH);
            if (_button.Bounds != buttonBounds) { _button.Bounds = buttonBounds; }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // The page's own backdrop (the wallpaper and its scrim, or the plain page colour), so the cover reads as the
            // page with one card on it rather than a box over it.
            BackdropTabPage page = FindPage();
            if (page != null) { page.PaintBackdropSlice(e.Graphics, this); }
            else
            {
                using (var b = new SolidBrush(BackColor)) { e.Graphics.FillRectangle(b, ClientRectangle); }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_card.Width <= 0 || _theme == null) { return; }
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int r = (int)Math.Round(12 * Scale96);
            using (GraphicsPath path = Rounded(_card, r))
            using (var fill = new SolidBrush(_theme.Surface))
            using (var pen = new Pen(_theme.Border, 1f))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
            g.SmoothingMode = SmoothingMode.Default;
            const TextFormatFlags centred = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix
                                            | TextFormatFlags.HorizontalCenter;
            TextRenderer.DrawText(g, _title, _titleFont, _titleRect, _theme.Text, centred);
            if (_body.Length > 0) { TextRenderer.DrawText(g, _body, _bodyFont, _bodyRect, _theme.TextMuted, centred); }
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = Math.Max(2, radius * 2);
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private BackdropTabPage FindPage()
        {
            for (Control c = Parent; c != null; c = c.Parent)
            {
                if (c is BackdropTabPage p) { return p; }
            }
            return null;
        }

        /// <summary>Tab and Shift+Tab stay on the button: the controls under the cover are not to be reached.</summary>
        protected override bool ProcessDialogKey(Keys keyData)
        {
            if ((keyData & Keys.KeyCode) == Keys.Tab && (keyData & (Keys.Control | Keys.Alt)) == 0)
            {
                _button.Focus();
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }

        /// <summary>The wheel stops here: passed on, it would scroll the page — and the cover with it.</summary>
        protected override void WndProc(ref Message m)
        {
            const int WM_MOUSEWHEEL = 0x020A, WM_MOUSEHWHEEL = 0x020E;
            if (m.Msg == WM_MOUSEWHEEL || m.Msg == WM_MOUSEHWHEEL)
            {
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_watchedParent != null) { _watchedParent.Layout -= OnParentLayout; _watchedParent = null; }
                _titleFont.Dispose();
                _bodyFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
