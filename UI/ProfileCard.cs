using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// One profile in the Profiles tab's library grid.
    ///
    /// WHY A CONTROL AND NOT A LIST ROW. A profile carries more than a name — an
    /// icon, a colour tag, a category, a description, a favourite star and its
    /// usage history. A ListView row can show those as columns, but columns force
    /// every profile to be read left-to-right against a header; a card lets the
    /// name lead and pushes the rest into a quiet second rank, which is how the
    /// data is actually used ("which one is my Minecraft one?" — you look for the
    /// pickaxe, not for row 7).
    ///
    /// Everything is owner-drawn from primitives, like <see cref="StatCard"/>, so
    /// the card follows the theme and stays sharp at any DPI.
    /// </summary>
    public sealed class ProfileCard : Control
    {
        /// <summary>Nominal card size. The grid reflows around this.</summary>
        public const int CardWidth = 232;
        public const int CardHeight = 108;

        private bool _hover;
        private bool _starHover;

        public string ProfileName { get; set; } = "";
        public string Description { get; set; } = "";

        /// <summary>Emoji/glyph shown in the badge, e.g. "🎮".</summary>
        public string Glyph { get; set; } = "🎯";

        /// <summary>Localised category name shown in the footer chip.</summary>
        public string CategoryText { get; set; } = "";

        /// <summary>Localised usage line, e.g. "used 42 times · 3h 20m".</summary>
        public string UsageText { get; set; } = "";

        /// <summary>Colour tag; <see cref="Color.Empty"/> means "use the accent".</summary>
        public Color TagColor { get; set; } = Color.Empty;

        public bool Favorite { get; set; }

        /// <summary>The profile currently loaded in the Clicker tab.</summary>
        public bool IsActive { get; set; }

        /// <summary>True when this profile also restores keybinds or appearance.</summary>
        public bool CarriesExtras { get; set; }

        // Theme colours, pushed in by the tab so the card owns no theme lookup.
        public Color CardColor { get; set; } = Color.FromArgb(24, 27, 39);
        public Color TextColor { get; set; } = Color.FromArgb(232, 236, 246);
        public Color MutedColor { get; set; } = Color.FromArgb(132, 142, 168);
        public Color AccentColor { get; set; } = Color.FromArgb(124, 92, 255);
        public Color BorderColor { get; set; } = Color.FromArgb(44, 48, 64);

        /// <summary>Raised when the card body is clicked (activate this profile).</summary>
        public event EventHandler Activated;

        /// <summary>Raised when the star is clicked.</summary>
        public event EventHandler FavoriteToggled;

        public ProfileCard()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Color.Transparent;
            Size = new Size(CardWidth, CardHeight);
            Cursor = Cursors.Hand;

            // The grid is a wall of similar cards, so each one has to introduce
            // itself properly to a screen reader rather than announcing "control".
            AccessibleRole = AccessibleRole.ListItem;
        }

        /// <summary>The colour actually painted for the tag stripe.</summary>
        private Color EffectiveTag => TagColor.IsEmpty || TagColor.A == 0 ? AccentColor : TagColor;

        /// <summary>
        /// The tag colour adjusted until it is legible as TEXT on the card.
        ///
        /// ColorTagArgb is a free-form int, so a profile can carry any colour at all —
        /// and a dark one (say a deep green) painted as small bold text on top of a
        /// 30-alpha tint of itself, over a dark card, is effectively invisible. The
        /// stripe keeps the true colour, because a 4px block wants to be exactly the
        /// colour the user picked; only the text is nudged, and only far enough to
        /// clear a 4.5:1 contrast ratio.
        /// </summary>
        private Color ReadableTag
        {
            get
            {
                Color c = EffectiveTag;
                double bg = Luminance(CardColor);
                bool towardsWhite = bg < 0.5;

                for (int i = 0; i < 14 && Contrast(Luminance(c), bg) < 4.5; i++)
                {
                    c = towardsWhite ? Lighten(c, 0.12) : Darken(c, 0.12);
                }
                return c;
            }
        }

        /// <summary>WCAG relative luminance (sRGB linearised).</summary>
        private static double Luminance(Color c)
        {
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }

        private static double Channel(int v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        private static double Contrast(double a, double b)
        {
            double hi = Math.Max(a, b) + 0.05;
            double lo = Math.Min(a, b) + 0.05;
            return hi / lo;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Hit regions
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The star's clickable box. Deliberately larger than the drawn star —
        /// a 12px glyph is a coin-toss to hit with a mouse and impossible on a
        /// touchscreen, so the target is padded out to a comfortable 26px.
        /// </summary>
        private Rectangle StarRect
        {
            get
            {
                var card = CardRect;
                return new Rectangle(card.Right - 32, card.Top + 6, 26, 26);
            }
        }

        private Rectangle CardRect => new Rectangle(1, 1, Width - 4, Height - 6);

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool onStar = StarRect.Contains(e.Location);
            if (onStar != _starHover)
            {
                _starHover = onStar;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            _starHover = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            // Focus follows the click so the keyboard can take over from here.
            if (CanFocus) { Focus(); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) { return; }

            if (StarRect.Contains(e.Location))
            {
                FavoriteToggled?.Invoke(this, EventArgs.Empty);
                return;
            }

            Activated?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Enter/Space activate, and the card takes focus, so the whole library is
        /// reachable without a mouse. Without this a keyboard user could tab onto a
        /// card and have no way to open it.
        /// </summary>
        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Enter || keyData == Keys.Space) { return true; }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                Activated?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        // ─────────────────────────────────────────────────────────────────────
        //  Painting
        // ─────────────────────────────────────────────────────────────────────

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            if (Width < 40 || Height < 30) { return; }

            Rectangle card = CardRect;
            const int radius = 12;
            Color surface = _hover ? Lighten(CardColor, 0.05) : CardColor;

            // 1) Drop shadow, lifted a little further while hovered so the card
            //    reads as picked up rather than merely recoloured.
            using (var shadowPath = Rounded(
                       new Rectangle(card.Left + 1, card.Top + (_hover ? 5 : 4), card.Width, card.Height), radius))
            using (var sh = new SolidBrush(Color.FromArgb(_hover ? 52 : 38, 0, 0, 0)))
            {
                g.FillPath(sh, shadowPath);
            }

            // 2) Surface.
            using (var path = Rounded(card, radius))
            {
                using (var fill = new LinearGradientBrush(
                           new Rectangle(card.Left, card.Top, card.Width, card.Height),
                           Lighten(surface, 0.06), surface, LinearGradientMode.Vertical))
                {
                    g.FillPath(fill, path);
                }

                // The active profile is ringed in the accent; everything else gets a
                // hairline. Focus borrows the active ring so keyboard position is
                // never ambiguous.
                bool ring = IsActive || Focused;
                using (var border = new Pen(ring ? AccentColor : BorderColor, ring ? 2f : 1f))
                {
                    var inset = ring
                        ? new Rectangle(card.Left + 1, card.Top + 1, card.Width - 2, card.Height - 2)
                        : card;
                    using (var bp = Rounded(inset, radius - (ring ? 1 : 0)))
                    {
                        g.DrawPath(border, bp);
                    }
                }
            }

            // 3) Colour-tag stripe down the left edge, clipped to the card's corners.
            using (var clip = Rounded(card, radius))
            {
                var saved = g.Clip;
                g.SetClip(clip, CombineMode.Intersect);
                using (var tag = new SolidBrush(EffectiveTag))
                {
                    g.FillRectangle(tag, card.Left, card.Top, 4, card.Height);
                }
                g.Clip = saved;
            }

            int left = card.Left + 14;

            // 4) Glyph badge.
            var badge = new Rectangle(left, card.Top + 12, 30, 30);
            using (var bb = new SolidBrush(Color.FromArgb(38, EffectiveTag)))
            {
                g.FillEllipse(bb, badge);
            }
            if (!string.IsNullOrEmpty(Glyph))
            {
                // Segoe UI Emoji renders the colour glyphs; plain Segoe UI would draw
                // a hollow box for anything outside the BMP symbol range.
                using (var ef = new Font("Segoe UI Emoji", 12f))
                {
                    TextRenderer.DrawText(g, Glyph, ef, badge, TextColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPadding);
                }
            }

            int textLeft = badge.Right + 10;
            int textRight = card.Right - 36;          // keep clear of the star
            int textWidth = Math.Max(20, textRight - textLeft);

            // 5) Name.
            using (var nameFont = new Font("Segoe UI", 10f, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, ProfileName, nameFont,
                    new Rectangle(textLeft, card.Top + 12, textWidth, 20), TextColor,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding |
                    TextFormatFlags.VerticalCenter);
            }

            // 6) Description, or a muted placeholder so the card never looks broken.
            string desc = string.IsNullOrWhiteSpace(Description) ? "" : Description;
            using (var descFont = new Font("Segoe UI", 8.25f))
            {
                TextRenderer.DrawText(g, desc, descFont,
                    new Rectangle(textLeft, card.Top + 31, textWidth, 16), MutedColor,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding |
                    TextFormatFlags.VerticalCenter);
            }

            // 7) Footer: category chip, then the usage line in the space that is left.
            int footerY = card.Bottom - 28;
            int chipRight = left;
            if (!string.IsNullOrEmpty(CategoryText))
            {
                using (var chipFont = new Font("Segoe UI", 7.5f, FontStyle.Bold))
                {
                    Size sz = TextRenderer.MeasureText(CategoryText, chipFont,
                        new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
                    var chip = new Rectangle(left, footerY, sz.Width + 16, 18);
                    using (var cp = Rounded(chip, 9))
                    using (var cb = new SolidBrush(Color.FromArgb(30, EffectiveTag)))
                    {
                        g.FillPath(cb, cp);
                    }
                    TextRenderer.DrawText(g, CategoryText, chipFont, chip, ReadableTag,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPadding);
                    chipRight = chip.Right + 8;
                }
            }

            // A small link-in-a-chain mark when the profile also restores keybinds or
            // appearance, so "why did my theme change?" has a visible answer.
            if (CarriesExtras)
            {
                using (var xf = new Font("Segoe UI Symbol", 8f))
                {
                    TextRenderer.DrawText(g, "⚭", xf,
                        new Rectangle(chipRight, footerY, 16, 18), MutedColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPadding);
                }
                chipRight += 18;
            }

            if (!string.IsNullOrEmpty(UsageText))
            {
                using (var useFont = new Font("Segoe UI", 7.5f))
                {
                    TextRenderer.DrawText(g, UsageText, useFont,
                        new Rectangle(chipRight, footerY, Math.Max(10, card.Right - 14 - chipRight), 18),
                        MutedColor,
                        TextFormatFlags.EndEllipsis | TextFormatFlags.Right |
                        TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }

            // 8) Favourite star. Filled when set; a faint outline otherwise, which is
            //    what makes it discoverable as something you can click.
            DrawStar(g, StarRect);
        }

        private void DrawStar(Graphics g, Rectangle box)
        {
            // Centre a 15px star inside the padded hit box.
            var b = new RectangleF(box.X + (box.Width - 15) / 2f, box.Y + (box.Height - 15) / 2f, 15, 15);
            using (var star = StarPath(b))
            {
                if (Favorite)
                {
                    using (var f = new SolidBrush(Color.FromArgb(255, 199, 74)))
                    {
                        g.FillPath(f, star);
                    }
                }
                else if (_hover || _starHover)
                {
                    using (var p = new Pen(_starHover ? Color.FromArgb(255, 199, 74) : MutedColor, 1.4f))
                    {
                        g.DrawPath(p, star);
                    }
                }
            }
        }

        private static GraphicsPath StarPath(RectangleF b)
        {
            var path = new GraphicsPath();
            var pts = new PointF[10];
            float cx = b.X + b.Width / 2f;
            float cy = b.Y + b.Height / 2f;
            float outer = Math.Min(b.Width, b.Height) / 2f;
            float inner = outer * 0.42f;

            for (int i = 0; i < 10; i++)
            {
                // Start at the top point (-90°) and alternate outer/inner radius.
                double angle = (-Math.PI / 2) + (i * Math.PI / 5);
                float r = (i % 2 == 0) ? outer : inner;
                pts[i] = new PointF(
                    cx + (float)(Math.Cos(angle) * r),
                    cy + (float)(Math.Sin(angle) * r));
            }

            path.AddPolygon(pts);
            return path;
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            if (radius <= 0 || r.Width <= radius * 2 || r.Height <= radius * 2)
            {
                path.AddRectangle(r);
                return path;
            }

            int d = radius * 2;
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static Color Lighten(Color c, double amount)
        {
            return Color.FromArgb(
                c.A,
                (int)Math.Min(255, c.R + (255 - c.R) * amount),
                (int)Math.Min(255, c.G + (255 - c.G) * amount),
                (int)Math.Min(255, c.B + (255 - c.B) * amount));
        }

        private static Color Darken(Color c, double amount)
        {
            return Color.FromArgb(
                c.A,
                (int)Math.Max(0, c.R * (1 - amount)),
                (int)Math.Max(0, c.G * (1 - amount)),
                (int)Math.Max(0, c.B * (1 - amount)));
        }
    }
}
