using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// The application header bar. Owner-drawn so the "Tempo" wordmark renders as a
    /// vivid true-colour gradient with a rounded accent logo tile and a metronome
    /// glyph — rather than flat themed text.
    /// </summary>
    public sealed class BrandHeader : Panel
    {
        private Theme _theme = Theme.ForKind(Models.ThemeKind.Dark);
        private Image _sharedBg;   // shared window backdrop — NOT owned/disposed here
        private int _bgDim = 55;
        private string _profileText = "";
        private int _profileRightEdge = -1;
        // Right edge of the painted "Tempo" wordmark, recorded each paint so the profile
        // caption on the far side of the header knows where it must stop.
        private float _wordmarkRight;

        /// <summary>
        /// The "Profile • Name" caption, drawn by the header itself (right side).
        /// Owner-drawing it — instead of hosting a transparent child Label over
        /// this UserPaint panel — is what removes the mismatched dark box that a
        /// transparent Label produced against the gradient background.
        /// </summary>
        public string ProfileText
        {
            get => _profileText;
            set { _profileText = value ?? ""; Invalidate(); }
        }

        /// <summary>
        /// X coordinate the profile caption is right-aligned to (set by the layout
        /// pass to sit just left of the state pill).
        /// </summary>
        public int ProfileRightEdge
        {
            get => _profileRightEdge;
            set { if (_profileRightEdge != value) { _profileRightEdge = value; Invalidate(); } }
        }

        /// <summary>
        /// Assigns the SHARED window backdrop image (owned and animated by the form —
        /// never disposed here) and the dim percentage for its readability scrim. The
        /// header paints the aligned top slice of it so it lines up seamlessly with the
        /// page and footer below. Pass null to clear.
        /// </summary>
        public void SetSharedBackdrop(Image img, int dimPercent)
        {
            _sharedBg = img;
            _bgDim = dimPercent;
            Invalidate();
        }

        public BrandHeader()
        {
            Dock = DockStyle.Top;
            Height = 66;
            DoubleBuffered = true;
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            // Pick up a logo the user sets (or clears) in About, without a restart.
            Utils.CustomLogo.Changed += OnCustomLogoChanged;
        }

        private void OnCustomLogoChanged()
        {
            // The event can be raised from any thread; touching the control must not be.
            try
            {
                if (IsDisposed || !IsHandleCreated) { ReloadLogo(); return; }
                BeginInvoke((Action)ReloadLogo);
            }
            catch { /* the header is going away — nothing to refresh */ }
        }

        public void ApplyTheme(Theme theme)
        {
            if (theme != null)
            {
                _theme = theme;
                BackColor = theme.Surface;
                Invalidate();
            }
        }

        // ── Custom caption buttons ─────────────────────────────────────────────
        // The window is borderless (see MainForm.CreateParams/WndProc) so this header IS
        // the title bar. Windows won't let an app restyle the system caption buttons, so
        // Tempo draws its own here: they follow the theme, and close turns red on hover
        // the way Windows 11's does. Dragging, snapping, double-click-to-maximise and
        // edge resizing are still handled by WINDOWS itself via WM_NCHITTEST — those are
        // fiddly to reimplement and there is no need to.
        // Scale for everything this control PAINTS by hand.
        //
        // WinForms' AutoScaleMode.Dpi scales control bounds, and fonts are in points so
        // Windows renders them larger too — but literal pixel numbers inside paint code
        // are scaled by nothing at all. On a 100% desktop that is invisible; on a 150%
        // laptop the wordmark and the (real, DPI-scaled) state pill both grow while the
        // logo tile, paddings and caption buttons stay put, and the row runs out of
        // middle. That mismatch is why laptops showed overlapping header text while
        // desktops looked fine. Every hand-drawn measurement below goes through this.
        private float Sc => DeviceDpi / 96f;

        private int BtnW => Round(46);
        private int BtnH => Round(32);

        /// <summary>A 96-DPI pixel measurement scaled to this screen.</summary>
        private int Round(double px) => (int)Math.Round(px * Sc);
        private int _hotBtn = -1;          // 0 = minimise, 1 = maximise/restore, 2 = close

        /// <summary>Raised when a caption button is clicked (0 min, 1 max/restore, 2 close).</summary>
        public event Action<int> CaptionButtonClicked;

        /// <summary>
        /// Width the caption buttons occupy at the top-right. The form's header layout
        /// subtracts this so the state pill and profile caption never sit under them.
        /// Zero when custom chrome is off, so the old layout is unchanged.
        /// </summary>
        public int CaptionStripWidth => ShowCaptionButtons ? BtnW * 3 : 0;

        /// <summary>Draw the custom ─ □ ✕ (set when the form is borderless).</summary>
        public bool ShowCaptionButtons { get; set; }

        /// <summary>True while the window is maximised, so the middle glyph can change.</summary>
        private bool _windowMaximized;

        /// <summary>
        /// Whether the host window is maximised, which decides if the middle caption
        /// button draws "maximise" or "restore". Repaints itself when it changes — it
        /// used to be a plain auto-property that only the button's OWN click handler
        /// ever wrote, so maximising any other way (double-clicking the header, Aero
        /// Snap, Win+Up/Down, the taskbar, restoring from the tray) left the wrong glyph
        /// on screen. The button still did the right thing, but it showed the opposite
        /// of what it would do — which reads as the button being broken.
        /// </summary>
        public bool WindowMaximized
        {
            get => _windowMaximized;
            set
            {
                if (_windowMaximized == value) { return; }
                _windowMaximized = value;
                Invalidate();
            }
        }

        /// <summary>Rectangle of caption button <paramref name="i"/>, right-aligned.</summary>
        public Rectangle ButtonRect(int i)
        {
            // Right-aligned: close (i=2) is the last 46 px, so its left edge is
            // Width-46 — NOT Width, which pushed it entirely off-screen.
            int left = Width - (3 - i) * BtnW;
            return new Rectangle(left, 0, BtnW, BtnH);
        }

        /// <summary>Which caption button is under <paramref name="p"/>, or -1. Used by the
        /// form's hit-testing so a button click isn't swallowed as a window drag.</summary>
        public int ButtonAt(Point p)
        {
            if (!ShowCaptionButtons) { return -1; }
            for (int i = 0; i < 3; i++)
            {
                if (ButtonRect(i).Contains(p)) { return i; }
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hot = ButtonAt(e.Location);
            if (hot != _hotBtn) { _hotBtn = hot; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hotBtn != -1) { _hotBtn = -1; Invalidate(); }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCAPTION_MSG = 2;

        private const int WM_NCHITTEST_MSG = 0x0084;
        private const int HTTRANSPARENT = -1;

        /// <summary>
        /// Makes everything except the caption buttons INVISIBLE to hit-testing, so the
        /// click falls through to the form and Windows treats the header as a real title
        /// bar.
        ///
        /// This replaces a ReleaseCapture + WM_NCLBUTTONDOWN(HTCAPTION) hand-off done on
        /// mouse-down. That moved the window, but it started Windows' modal drag loop on
        /// the FIRST click — which then swallowed the second one, so double-clicking the
        /// header to maximise could never fire. (Verified: a double-click on the header
        /// left the window un-maximised.)
        ///
        /// Answering the hit-test instead of faking a click means Windows drives all of
        /// it natively and consistently: dragging, double-click to maximise/restore,
        /// Aero Snap, snap-to-edge and the Alt+Space system menu. The buttons keep
        /// returning a normal hit so they still receive their own clicks and hover.
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST_MSG && ShowCaptionButtons)
            {
                // LParam packs screen coords as two SIGNED 16-bit values — a window on a
                // second monitor to the left has negative X, and masking without sign
                // extension would send the hit-test to the wrong place entirely.
                int lp = m.LParam.ToInt32();
                var screen = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
                Point local = PointToClient(screen);

                if (ButtonAt(local) < 0)
                {
                    m.Result = (IntPtr)HTTRANSPARENT;
                    return;
                }
            }

            base.WndProc(ref m);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) { return; }

            int i = ButtonAt(e.Location);
            if (i >= 0) { CaptionButtonClicked?.Invoke(i); }
        }

        /// <summary>
        /// Double-clicking the title bar maximises/restores. Only reachable when the
        /// caption buttons are hidden (full screen), because otherwise the header is
        /// hit-test-transparent and Windows handles the double-click itself.
        /// </summary>
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left || !ShowCaptionButtons) { return; }
            if (ButtonAt(e.Location) >= 0) { return; }
            CaptionButtonClicked?.Invoke(1);   // same path as the □ button
        }

        /// <summary>Paints the three caption buttons at the top-right.</summary>
        private void DrawCaptionButtons(Graphics g)
        {
            for (int i = 0; i < 3; i++)
            {
                Rectangle r = ButtonRect(i);
                bool hot = _hotBtn == i;
                if (hot)
                {
                    // Close goes Windows-11 red; the others take a soft theme wash.
                    Color hover = i == 2
                        ? Color.FromArgb(232, 17, 35)
                        : Blend(_theme.Surface, _theme.Text, 0.18);
                    using (var hb = new SolidBrush(hover)) { g.FillRectangle(hb, r); }
                }

                Color fg = hot && i == 2 ? Color.White : _theme.TextMuted;
                using (var pen = new Pen(fg, 1.3f))
                {
                    float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
                    if (i == 0)                       // minimise
                    {
                        g.DrawLine(pen, cx - 5, cy, cx + 5, cy);
                    }
                    else if (i == 1)                  // maximise / restore
                    {
                        if (WindowMaximized)
                        {
                            g.DrawRectangle(pen, cx - 5, cy - 3, 8, 8);
                            g.DrawLine(pen, cx - 2, cy - 5, cx + 5, cy - 5);
                            g.DrawLine(pen, cx + 5, cy - 5, cx + 5, cy + 2);
                        }
                        else
                        {
                            g.DrawRectangle(pen, cx - 5, cy - 5, 10, 10);
                        }
                    }
                    else                              // close
                    {
                        g.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5);
                        g.DrawLine(pen, cx + 5, cy - 5, cx - 5, cy + 5);
                    }
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var rect = new Rectangle(0, 0, Width, Height);

            // ── Background ──────────────────────────────────────────────────────
            // With a shared window backdrop, paint the header's aligned TOP slice of
            // it (so it's continuous with the page/footer below); otherwise the
            // subtle vertical surface gradient.
            if (!WindowBackdrop.Paint(g, this, _sharedBg, _bgDim, _theme.Surface))
            {
                using (var bg = new LinearGradientBrush(rect,
                           Blend(_theme.Surface, _theme.Background, 0.0),
                           Blend(_theme.Surface, _theme.Background, 0.35),
                           LinearGradientMode.Vertical))
                {
                    g.FillRectangle(bg, rect);
                }
            }

            // Accent hairline along the bottom edge — a full true-colour sweep.
            var underline = new Rectangle(0, Height - 3, Width, 3);
            using (var ub = new LinearGradientBrush(underline,
                       _theme.Accent, _theme.AccentHover, LinearGradientMode.Horizontal))
            {
                g.FillRectangle(ub, underline);
            }

            // ── Logo tile (soft shadow + top highlight for a bit of depth) ──────
            int tileSz = Round(38);
            var tile = new Rectangle(Round(18), (Height - tileSz) / 2, tileSz, tileSz);

            // Soft accent glow blooming out from behind the tile — subtle depth.
            using (var glowPath = new GraphicsPath())
            {
                var glowRect = new Rectangle(tile.Left - 14, tile.Top - 14, tile.Width + 28, tile.Height + 28);
                glowPath.AddEllipse(glowRect);
                using (var glow = new PathGradientBrush(glowPath)
                {
                    CenterColor = Color.FromArgb(70, _theme.Accent),
                    SurroundColors = new[] { Color.FromArgb(0, _theme.Accent) },
                    CenterPoint = new PointF(tile.Left + tile.Width / 2f, tile.Top + tile.Height / 2f)
                })
                {
                    g.FillPath(glow, glowPath);
                }
            }

            using (var shadowPath = Rounded(new Rectangle(tile.Left + 1, tile.Top + 3, tile.Width, tile.Height), 10))
            using (var sb = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
            {
                g.FillPath(sb, shadowPath);
            }

            // Tempo's REAL logo — the user's custom logo if they set one, otherwise the
            // app's own icon. The header used to draw a hand-made bolt glyph and never
            // touched the actual brand artwork, so the header didn't match the icon in the
            // taskbar, the tray or the notification cards.
            //
            // Real artwork REPLACES the accent tile rather than sitting on top of it: the
            // tile is a bright accent square built as a backdrop for a white glyph, and a
            // full-bleed logo (Tempo's is dark and detailed) reads as a murky blob dropped
            // in a coloured ring. The logo already is a finished piece of art with its own
            // background, so it gets the rounded tile to itself — which is also how every
            // other app draws its icon.
            Image logo = LogoImage();
            using (var tilePath = Rounded(tile, 10))
            {
                if (logo != null)
                {
                    var saved = g.Save();
                    g.SetClip(tilePath, CombineMode.Intersect);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(logo, tile);
                    g.Restore(saved);

                    // Hairline rim in the accent colour so the artwork still belongs to the
                    // theme and its edge stays crisp against a same-tone header.
                    using (var rim = new Pen(Color.FromArgb(120, _theme.Accent), 1f))
                    {
                        g.DrawPath(rim, tilePath);
                    }
                }
                else
                {
                    using (var tileBrush = new LinearGradientBrush(tile,
                               _theme.Accent, _theme.AccentHover, LinearGradientMode.ForwardDiagonal))
                    {
                        g.FillPath(tileBrush, tilePath);
                    }

                    // Subtle top highlight, clipped to the rounded tile.
                    g.SetClip(tilePath);
                    var hiRect = new Rectangle(tile.Left, tile.Top, tile.Width, tile.Height / 2);
                    using (var hi = new LinearGradientBrush(hiRect,
                               Color.FromArgb(55, 255, 255, 255), Color.FromArgb(0, 255, 255, 255),
                               LinearGradientMode.Vertical))
                    {
                        g.FillRectangle(hi, hiRect);
                    }
                    g.ResetClip();

                    DrawBolt(g, tile);
                }
            }

            // ── Wordmark: "Tempo" in a horizontal accent gradient, centred ──────
            float textLeft = tile.Right + Round(14);
            using (var titleFont = new Font("Segoe UI", 19f, FontStyle.Bold))
            {
                string word = "Tempo";
                SizeF size = g.MeasureString(word, titleFont);
                // Remember where the wordmark ends so the profile caption on the right
                // can refuse to cross it (see DrawProfile).
                _wordmarkRight = textLeft + size.Width;
                float ty = (Height - size.Height) / 2f;

                var textRect = new RectangleF(textLeft, ty, size.Width + 2, size.Height);
                using (var grad = new LinearGradientBrush(textRect,
                           _theme.Accent, _theme.AccentHover, LinearGradientMode.Horizontal))
                {
                    // Brighten the gradient stops for a vivid, true-colour wordmark.
                    grad.InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { _theme.Accent, _theme.AccentHover, Brighten(_theme.AccentHover, 0.25) },
                        Positions = new[] { 0f, 0.6f, 1f }
                    };
                    g.DrawString(word, titleFont, grad, textLeft, ty);
                }
            }

            // ── Profile caption on the right: "Profile •" muted, name in accent ──
            DrawProfile(g);
        }

        /// <summary>
        /// Longest leading part of <paramref name="s"/> that fits in <paramref name="max"/>
        /// pixels, with an ellipsis. Returns "" when not even one character plus the
        /// ellipsis fits, so the caller draws nothing rather than something clipped.
        /// </summary>
        private static string Ellipsize(Graphics g, string s, Font f, StringFormat fmt, float max)
        {
            if (string.IsNullOrEmpty(s) || max <= 0) { return ""; }
            if (g.MeasureString(s, f, PointF.Empty, fmt).Width <= max) { return s; }

            const string dots = "…";
            for (int len = s.Length - 1; len > 0; len--)
            {
                string candidate = s.Substring(0, len).TrimEnd() + dots;
                if (g.MeasureString(candidate, f, PointF.Empty, fmt).Width <= max)
                {
                    return candidate;
                }
            }
            return "";
        }

        private void DrawProfile(Graphics g)
        {
            if (string.IsNullOrEmpty(_profileText))
            {
                return;
            }
            int rightEdge = _profileRightEdge > 0 ? _profileRightEdge : Width - 130;

            // Split "Profile  •  Name" into a muted prefix (through the dot) and
            // the profile name, which gets the accent so it reads at a glance.
            string prefix = _profileText;
            string name = "";
            int dot = _profileText.IndexOf('•');
            if (dot >= 0)
            {
                prefix = _profileText.Substring(0, dot + 1);        // through the dot
                name = _profileText.Substring(dot + 1).Trim();
            }

            // A fixed gap between the muted "Profile •" and the accent name — a
            // trailing space can't do it because GenericTypographic trims it.
            const float gapPx = 6f;
            var fmt = StringFormat.GenericTypographic;
            using (var preFont = new Font("Segoe UI", 9.5f, FontStyle.Regular))
            using (var nameFont = new Font("Segoe UI", 9.5f, FontStyle.Bold))
            {
                SizeF nameSz = name.Length > 0 ? g.MeasureString(name, nameFont, PointF.Empty, fmt) : SizeF.Empty;
                SizeF preSz = g.MeasureString(prefix, preFont, PointF.Empty, fmt);

                // Never draw across the wordmark.
                //
                // This block used to right-align from the pill and walk left with no
                // lower bound at all, so whenever the middle of the header ran short the
                // muted "Profile •" simply printed on top of "Tempo". A wide desktop
                // window hid it; a laptop did not, because at 150% both the wordmark and
                // the (DPI-scaled) state pill are half again as wide while the row is
                // narrower. A long profile name did the same thing at any DPI.
                float minX = _wordmarkRight + Round(16);
                float available = rightEdge - minX;

                if (available <= Round(24))
                {
                    return;                 // genuinely no room — better blank than on top
                }

                // Give up the muted "Profile •" prefix first: the NAME is the part that
                // carries information, so it is the last thing to go.
                if (preSz.Width + gapPx + nameSz.Width > available)
                {
                    prefix = "";
                    preSz = SizeF.Empty;
                }

                // Still short? Shorten the name itself rather than let it run left.
                if (name.Length > 0 && nameSz.Width > available - preSz.Width - (prefix.Length > 0 ? gapPx : 0))
                {
                    float room = available - preSz.Width - (prefix.Length > 0 ? gapPx : 0);
                    name = Ellipsize(g, name, nameFont, fmt, room);
                    nameSz = name.Length > 0 ? g.MeasureString(name, nameFont, PointF.Empty, fmt) : SizeF.Empty;
                }

                float h = Math.Max(nameSz.Height, preSz.Height);
                float y = (Height - h) / 2f;
                float nameX = rightEdge - nameSz.Width;
                float preX = nameX - (name.Length > 0 && prefix.Length > 0 ? gapPx : 0) - preSz.Width;

                if (prefix.Length > 0)
                {
                    using (var mut = new SolidBrush(_theme.TextMuted))
                    {
                        g.DrawString(prefix, preFont, mut, preX, y, fmt);
                    }
                }
                if (name.Length > 0)
                {
                    using (var acc = new SolidBrush(Brighten(_theme.Accent, 0.10)))
                    {
                        g.DrawString(name, nameFont, acc, nameX, y, fmt);
                    }
                }
            }

            // Last, so the buttons sit above the header's own artwork.
            if (ShowCaptionButtons)
            {
                DrawCaptionButtons(g);
            }
        }

        // Cached brand artwork. Decoding an icon per repaint would be wasteful on a
        // control that repaints on every theme change, resize and hover.
        private Image _logo;
        private bool _logoTried;

        /// <summary>
        /// Tempo's logo for the header tile: the user's custom logo if one is set,
        /// otherwise the app's own icon. Null only when neither can be loaded, in which
        /// case the caller draws the bolt. Cached; call <see cref="ReloadLogo"/> after
        /// the custom logo changes.
        /// </summary>
        private Image LogoImage()
        {
            if (_logoTried) { return _logo; }
            _logoTried = true;
            try
            {
                string custom = Utils.CustomLogo.GetPath();
                if (!string.IsNullOrEmpty(custom) && System.IO.File.Exists(custom))
                {
                    // Copy into an independent bitmap so the file isn't kept locked.
                    using (var tmp = Image.FromFile(custom))
                    {
                        _logo = new Bitmap(tmp);
                    }
                    return _logo;
                }
            }
            catch { /* a bad custom logo must never break the header */ }

            // The tile holds 32 px of artwork, so take the 64 px frame and downscale it —
            // sharper than upscaling the 32 px one Icon.ToBitmap() would have given us.
            try { _logo = Utils.AppIcon.GetBitmap(64); }
            catch { }
            return _logo;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // A static event outlives the control; staying subscribed would keep the
                // whole header (and its form) alive and fire into a dead control.
                try { Utils.CustomLogo.Changed -= OnCustomLogoChanged; } catch { }
                try { _logo?.Dispose(); } catch { }
                _logo = null;
            }
            base.Dispose(disposing);
        }

        /// <summary>Drops the cached logo so the next paint picks up a changed one.</summary>
        public void ReloadLogo()
        {
            try { _logo?.Dispose(); } catch { }
            _logo = null;
            _logoTried = false;
            Invalidate();
        }

        private void DrawBolt(Graphics g, Rectangle tile)
        {
            // A simple lightning bolt centred in the tile.
            float cx = tile.Left + tile.Width / 2f;
            float cy = tile.Top + tile.Height / 2f;
            PointF[] bolt =
            {
                new PointF(cx + 3, cy - 11),
                new PointF(cx - 7, cy + 2),
                new PointF(cx - 1, cy + 2),
                new PointF(cx - 3, cy + 11),
                new PointF(cx + 7, cy - 3),
                new PointF(cx + 1, cy - 3),
            };
            using (var b = new SolidBrush(Color.White))
            {
                g.FillPolygon(b, bolt);
            }
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static Color Blend(Color a, Color b, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        private static Color Brighten(Color c, double amount)
        {
            return Color.FromArgb(
                Math.Min(255, (int)(c.R + (255 - c.R) * amount)),
                Math.Min(255, (int)(c.G + (255 - c.G) * amount)),
                Math.Min(255, (int)(c.B + (255 - c.B) * amount)));
        }
    }
}
