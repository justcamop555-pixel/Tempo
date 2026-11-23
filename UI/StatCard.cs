using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A rounded "stat card": a small surface panel with a muted caption at the
    /// top and a large bold value below it. Used to build the Statistics
    /// dashboard. Owner-drawn so it looks consistent across themes and DPI.
    /// </summary>
    public sealed class StatCard : Control, IPaintedText
    {
        private string _caption = "";
        private string _value = "0";
        private string _sub = "";
        private Color _cardColor = Color.FromArgb(24, 27, 39);
        private Color _captionColor = Color.FromArgb(132, 142, 168);
        private Color _valueColor = Color.FromArgb(232, 236, 246);
        private Color _accentBar = Color.FromArgb(124, 92, 255);
        private bool _showAccent = true;

        public Color CardColor { get => _cardColor; set { if (value != _cardColor) { _cardColor = value; DropPicture(); } } }
        public Color CaptionColor { get => _captionColor; set { if (value != _captionColor) { _captionColor = value; DropPicture(); } } }
        public Color ValueColor { get => _valueColor; set { if (value != _valueColor) { _valueColor = value; DropPicture(); } } }
        public Color AccentBar { get => _accentBar; set { if (value != _accentBar) { _accentBar = value; DropPicture(); } } }
        public bool ShowAccent { get => _showAccent; set { if (value != _showAccent) { _showAccent = value; DropPicture(); } } }

        // ── The card's picture ───────────────────────────────────────────────────────────────────────
        //
        // A card is drawn once — shadow, gradient, accent, three texts in GDI+ with antialiasing — into a picture of its
        // own, and every paint after that copies the picture. Drawing it took about a millisecond a time, and a fast scroll
        // of the Statistics page repaints every card its uncovered strip crosses on every step: that drawing was most of a
        // step, and the longer a step, the likelier the screen shows the strip before it is painted (scratchpad/scrollstress).
        // The picture is drawn on the page's own colour, so it is opaque and copied straight; a page with a wallpaper
        // behind it (which differs from place to place) is drawn as before, every time. New texts, colours, size or
        // screen scale draw it again.
        private Bitmap _picture;
        private Color _pictureBack;

        private void DropPicture()
        {
            _picture?.Dispose();
            _picture = null;
        }

        /// <summary>The solid colour behind the card, or Color.Empty when what is behind it is not one colour (a wallpaper).</summary>
        private Color SolidBack()
        {
            if (Parent is BackdropTabPage page && page.HasBackdropImage) { return Color.Empty; }
            Color c = Parent?.BackColor ?? Color.Empty;
            return c.A == 255 ? c : Color.Empty;
        }

        protected override void OnResize(EventArgs e)
        {
            DropPicture();
            base.OnResize(e);
        }

        protected override void OnParentChanged(EventArgs e)
        {
            DropPicture();
            base.OnParentChanged(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { DropPicture(); }
            base.Dispose(disposing);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // The picture covers every pixel when there is one to copy; only a card drawn live needs what is behind it.
            if (!SolidBack().IsEmpty && Width >= 8 && Height >= 10) { return; }
            base.OnPaintBackground(e);
        }

        public StatCard()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);
            // A card to read, not to use (round 44): as a plain Control it took the focus, so a switch to Statistics put
            // the keyboard on its first card and Tab stopped at every one.
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            BackColor = Color.Transparent;
            Size = new Size(168, 78);
        }

        // Every offset below is a 100% number scaled by Px. They were plain pixels inside a card the
        // window scales: on a 150% laptop the caption and value sat in the top half of a card half as
        // tall again, the trend beside the caption was cut to its top half, and the shadow and corners
        // shrank relative to the card.
        private float DpiScale => DeviceDpi / 96f;
        private int Px(float size96) => (int)Math.Round(size96 * DpiScale, MidpointRounding.AwayFromZero);

        // The dashboard sets every card's texts on each UI tick (five times a second), changed or not. Each set used
        // to repaint the card — every card on screen, five times a second, while nothing changed — and that steady
        // paint was what a fast scroll had to queue behind (the smeared Statistics page, 2026-11-08). Only a
        // change repaints now.
        public string Caption
        {
            get => _caption;
            set { string v = value ?? ""; if (v == _caption) { return; } _caption = v; DropPicture(); Invalidate(); }
        }

        public string Value
        {
            get => _value;
            set { string v = value ?? ""; if (v == _value) { return; } _value = v; DropPicture(); Invalidate(); }
        }

        /// <summary>Optional small text under the value (e.g. units or context).</summary>
        public string Sub
        {
            get => _sub;
            set { string v = value ?? ""; if (v == _sub) { return; } _sub = v; DropPicture(); Invalidate(); }
        }

        // Made once: three fonts and a string format were created and thrown away on every paint of every card.
        private static readonly Font CaptionFont = new Font("Segoe UI", CaptionFontSize, FontStyle.Regular);
        private static readonly Font SubFont = new Font("Segoe UI", 8f, FontStyle.Regular);
        private static readonly Font ValueFont = new Font("Segoe UI", 18f, FontStyle.Bold);
        private static readonly StringFormat SubFormat = new StringFormat
        {
            Alignment = StringAlignment.Far,
            FormatFlags = StringFormatFlags.NoWrap,
            Trimming = StringTrimming.EllipsisCharacter
        };

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 8 || Height < 10)
            {
                return;
            }
            Color back = SolidBack();
            if (back.IsEmpty)
            {
                DropPicture();
                DrawCard(e.Graphics);   // over a wallpaper: drawn live, on what is behind it
                return;
            }
            if (_picture == null || _picture.Width != Width || _picture.Height != Height || _pictureBack != back)
            {
                DropPicture();
                // Opaque (no alpha), as the screen buffer it used to be drawn on: ClearType text needs that to come out the same.
                _picture = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                _pictureBack = back;
                using (Graphics pg = Graphics.FromImage(_picture))
                {
                    pg.Clear(back);
                    DrawCard(pg);
                }
            }
            e.Graphics.CompositingMode = CompositingMode.SourceCopy;
            e.Graphics.DrawImageUnscaled(_picture, 0, 0);
        }

        /// <summary>The card itself: shadow, surface, accent and texts.</summary>
        private void DrawCard(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Reserve a few pixels at the bottom/right for a soft drop shadow.
            var card = new Rectangle(Px(1), Px(1), Width - Px(4), Height - Px(6));
            int radius = Px(12);

            // 1) Soft drop shadow for depth.
            using (var shadowPath = Rounded(new Rectangle(card.Left + Px(1), card.Top + Px(4), card.Width, card.Height), radius))
            using (var sh = new SolidBrush(Color.FromArgb(38, 0, 0, 0)))
            {
                g.FillPath(sh, shadowPath);
            }

            // 2) Card surface with a gentle top-to-bottom gradient.
            using (var path = Rounded(card, radius))
            {
                using (var fill = new LinearGradientBrush(
                           new Rectangle(card.Left, card.Top, card.Width, card.Height),
                           Lighten(CardColor, 0.06), CardColor, LinearGradientMode.Vertical))
                {
                    g.FillPath(fill, path);
                }

                // Hairline edge highlight (subtle on dark themes, invisible on light).
                using (var border = new Pen(Lighten(CardColor, 0.10)))
                {
                    g.DrawPath(border, path);
                }
            }

            // 3) Rounded accent pill, with a little horizontal gradient.
            if (ShowAccent)
            {
                var accentRect = new Rectangle(card.Left + Px(13), card.Top + Px(14), Px(26), Px(4));
                using (var ap = Rounded(accentRect, Px(2)))
                using (var ag = new LinearGradientBrush(accentRect, AccentBar, Lighten(AccentBar, 0.22), LinearGradientMode.Horizontal))
                {
                    g.FillPath(ag, ap);
                }
            }

            using (var capBrush = new SolidBrush(CaptionColor))
            {
                Font capFont = CaptionFont;
                string cap = _caption.ToUpperInvariant();
                g.DrawString(cap, capFont, capBrush, Px(CaptionX), Px(CaptionY));

                // The sublabel goes on the CAPTION row, right-aligned — not beside the
                // value, where it used to live. The value is 18pt bold and a six-figure
                // click count is most of a 172px card, so "+86,377 vs previous 7 days"
                // had about 70px to sit in and was cut to "+86,377 vs". The caption row
                // is short ("THIS WEEK") and leaves roughly a hundred, and the trend
                // reads naturally as an annotation on the label rather than on the number.
                if (!string.IsNullOrEmpty(_sub))
                {
                    float capW = g.MeasureString(cap, capFont).Width;
                    float subLeft = Px(14) + capW + Px(6);
                    float subWidth = Width - subLeft - Px(12);
                    if (subWidth > Px(24))
                    {
                        using (var subBrush = new SolidBrush(CaptionColor))
                        {
                            // As tall as its line at this DPI (14 px at 100%), so it is never cut across.
                            float subH = Math.Max(Px(14), (float)Math.Ceiling(SubFont.GetHeight(g)));
                            g.DrawString(_sub, SubFont, subBrush,
                                new RectangleF(subLeft, Px(23), subWidth, subH), SubFormat);
                        }
                    }
                }
            }

            using (var valBrush = new SolidBrush(ValueColor))
            {
                g.DrawString(_value, ValueFont, valBrush, Px(12), Px(40));
            }
        }

        private const float CaptionFontSize = 8.5f;
        private const int CaptionX = 14, CaptionY = 24;

        /// <summary>
        /// The upper-case caption, for the layout checks. It is drawn with nothing to clip or shorten
        /// it, so a long translation simply runs off the card; its room ends inside the card's border.
        /// </summary>
        IEnumerable<PaintedText> IPaintedText.PaintedTexts(Graphics g, float unitsPerPixel)
        {
            if (string.IsNullOrEmpty(_caption) || g == null) { yield break; }
            float u = unitsPerPixel > 0f ? unitsPerPixel : 1f;
            string cap = _caption.ToUpperInvariant();
            Size size;
            using (var capFont = new Font("Segoe UI", CaptionFontSize, FontStyle.Regular))
            {
                size = Size.Ceiling(g.MeasureString(cap, capFont));
            }
            // The card is drawn from x = 1 and 4 px narrower than the control (its shadow), with a
            // rounded border: the caption should end 8 px inside it. Painted pixels × u; the card's
            // width is already in the layout's units.
            int x = (int)Math.Round(Px(CaptionX) * u);
            var ink = new Rectangle(x, (int)Math.Round(Px(CaptionY) * u), (int)Math.Ceiling(size.Width * u), (int)Math.Ceiling(size.Height * u));
            var room = Rectangle.FromLTRB(x, 0, Math.Max(x, Width - (int)Math.Ceiling(Px(12) * u)), Height);
            yield return new PaintedText(cap, ink, room);
        }

        private static Color Lighten(Color c, double f)
        {
            int r = (int)Math.Round(c.R + (255 - c.R) * f);
            int g = (int)Math.Round(c.G + (255 - c.G) * f);
            int b = (int)Math.Round(c.B + (255 - c.B) * f);
            return Color.FromArgb(c.A,
                Math.Min(255, Math.Max(0, r)),
                Math.Min(255, Math.Max(0, g)),
                Math.Min(255, Math.Max(0, b)));
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
    }
}
