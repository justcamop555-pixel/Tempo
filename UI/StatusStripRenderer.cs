using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// How the status bar draws one of its items — set as the item's Tag.
    /// </summary>
    public sealed class StatusSegment
    {
        /// <summary>
        /// The word the value follows ("CPU", "Clicks:"), drawn quietly; the value after it is drawn
        /// bright and semibold, so the number is what the eye lands on.
        /// </summary>
        public string Label;

        /// <summary>Items sharing a group number above 0 sit together on one rounded chip.</summary>
        public int Group;

        /// <summary>A click opens a tab: a soft wash under the pointer says so.</summary>
        public bool Clickable;

        /// <summary>The pointer is over it (kept by MainForm).</summary>
        public bool Hover;

        /// <summary>The state item: its leading "●" is drawn as a real dot, in the state's colour.</summary>
        public bool StateDot;

        /// <summary>
        /// The hint in the middle: centred; when its whole text doesn't fit, <see cref="Short"/> is
        /// drawn instead, and only that is cut with "…" if even it doesn't.
        /// </summary>
        public bool Hint;

        /// <summary>
        /// The hint's short form — just what to do ("F6 to start", "Clicking — F6 to stop"). The
        /// action ends the full text, so cutting the full text lost exactly the part that mattered.
        /// </summary>
        public string Short;
    }

    /// <summary>
    /// Dresses the bottom status bar in Tempo's theme: a flat surface with a fine accent-tinted
    /// hairline across the top, the figures grouped on rounded chips, each label quiet and each
    /// value bright and semibold, a real state dot, and a soft wash under the segments a click
    /// opens a tab from. Pairs with the painted stat icons (<see cref="StatusIcons"/>).
    ///
    /// It was 9 pt text with every word the same muted grey — the labels and the numbers alike —
    /// which read as one long, hard-to-read line. Nothing here animates: with a wallpaper the
    /// window is WS_EX_COMPOSITED, where any repaint of the bar redraws the whole window.
    /// </summary>
    public sealed class StatusStripRenderer : ToolStripProfessionalRenderer
    {
        private readonly Theme _theme;

        public StatusStripRenderer(Theme theme) : base(new ColorTable(theme))
        {
            _theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);
        }

        /// <summary>The chip colour: a step up from the bar's surface, toward the text.</summary>
        internal Color ChipColor => Blend(_theme.Surface, _theme.Text, SectionColors.IsLight(_theme) ? 0.055 : 0.07);

        /// <summary>A label's colour on <paramref name="bg"/>: the muted text, nudged until it reads (4.5:1).</summary>
        internal Color LabelColor(Color bg) => Theme.Readable(_theme.TextMuted, bg, 4.5);

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            var g = e.Graphics;
            var r = new Rectangle(Point.Empty, e.ToolStrip.Size);
            using (var b = new SolidBrush(_theme.Surface))
            {
                g.FillRectangle(b, r);
            }
            // Top hairline: border blended a touch toward the accent, so the bar
            // feels intentionally seated under the content rather than tacked on.
            using (var p = new Pen(Blend(_theme.Border, _theme.Accent, 0.35)))
            {
                g.DrawLine(p, 0, 0, r.Width, 0);
            }

            // The chips: one rounded pill under each group of figures, so "CPU · RAM · Up" and
            // "Clicks · CPS · Peak · Time" read as two sets instead of one run of words.
            var groups = new Dictionary<int, Rectangle>();
            foreach (ToolStripItem item in e.ToolStrip.Items)
            {
                if (!(item.Tag is StatusSegment seg) || seg.Group <= 0 || !item.Available || !item.Visible) { continue; }
                Rectangle b = item.Bounds;
                groups[seg.Group] = groups.TryGetValue(seg.Group, out Rectangle had) ? Rectangle.Union(had, b) : b;
            }
            if (groups.Count == 0) { return; }
            float dpi = e.ToolStrip.DeviceDpi / 96f;
            int padX = (int)Math.Round(4 * dpi), padY = (int)Math.Round(4 * dpi);
            var prev = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (Rectangle u in groups.Values)
            {
                var chip = new Rectangle(u.Left - padX, padY + 1, u.Width + 2 * padX, r.Height - 2 * padY - 1);
                using (var path = Pill(chip))
                {
                    using (var b = new SolidBrush(ChipColor)) { g.FillPath(b, path); }
                    using (var p = new Pen(Blend(_theme.Border, ChipColor, 0.35))) { g.DrawPath(p, path); }
                }
            }
            g.SmoothingMode = prev;
        }

        /// <summary>
        /// Nothing: the bar's edge is the hairline the background draws. The professional renderer's own
        /// border for a status strip is a line of the SYSTEM highlight colour across the top — pure white,
        /// drawn after the background and so over the hairline: a bright 1 px line above the bar in every
        /// dark theme (measured off the window: row 0 #FFFFFF on Nord's #384051 bar).
        /// </summary>
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
        }

        protected override void OnRenderLabelBackground(ToolStripItemRenderEventArgs e)
        {
            // Under the pointer, a segment that opens a tab gets the sidebar's soft accent wash.
            if (e.Item.Tag is StatusSegment seg && seg.Clickable && seg.Hover)
            {
                var g = e.Graphics;
                float dpi = e.Item.Owner != null ? e.Item.Owner.DeviceDpi / 96f : 1f;
                int inset = (int)Math.Round(3 * dpi);
                var rect = new Rectangle(1, inset, e.Item.Width - 2, e.Item.Height - 2 * inset);
                if (seg.Hint && !string.IsNullOrEmpty(e.Item.Text))
                {
                    // The hint springs across the middle: the wash hugs its words, not the gap.
                    string shown = HintTextFor(g, seg, e.Item.Text, e.Item.Font, e.Item.Width);
                    int tw = TextRenderer.MeasureText(g, shown, e.Item.Font, Size.Empty,
                        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
                    int w = Math.Min(e.Item.Width - 2, tw + (int)Math.Round(20 * dpi));
                    rect = new Rectangle((e.Item.Width - w) / 2, inset, w, e.Item.Height - 2 * inset);
                }
                Color under = seg.Group > 0 ? ChipColor : _theme.Surface;
                var prev = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Pill(rect))
                using (var b = new SolidBrush(Blend(under, _theme.Accent, 0.22)))
                {
                    g.FillPath(b, path);
                }
                g.SmoothingMode = prev;
                return;
            }
            base.OnRenderLabelBackground(e);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (!(e.Item.Tag is StatusSegment seg) || string.IsNullOrEmpty(e.Text))
            {
                base.OnRenderItemText(e);
                return;
            }

            var g = e.Graphics;
            string text = e.Text;
            Rectangle r = e.TextRectangle;
            Font font = e.TextFont ?? e.Item.Font;
            Color value = e.Item.ForeColor;
            Color under = seg.Hover && seg.Clickable
                ? Blend(seg.Group > 0 ? ChipColor : _theme.Surface, _theme.Accent, 0.22)
                : (seg.Group > 0 ? ChipColor : _theme.Surface);
            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine |
                                          TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;

            if (seg.Hint)
            {
                // The whole leftover width is the hint's (it springs); centred in it. The full text if
                // it fits, else its short form, and "…" only if even that doesn't.
                var box = new Rectangle(0, 0, e.Item.Width, e.Item.Height);
                string show = HintTextFor(g, seg, text, font, box.Width);
                TextRenderer.DrawText(g, show, font, box, Theme.Readable(value, under, 4.5),
                    flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
                return;
            }

            if (seg.StateDot && text.Length > 0 && text[0] == '●')
            {
                // A real dot in the state's colour, in the room the "●" took.
                string rest = text.TrimStart('●', ' ');
                int lead = TextRenderer.MeasureText(g, "●  ", font, Size.Empty, flags).Width;
                int d = Math.Max(6, (int)Math.Round(font.Height * 0.46));
                var dot = new Rectangle(r.Left + 1, r.Top + (r.Height - d) / 2, d, d);
                var prev = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var halo = new SolidBrush(Color.FromArgb(60, value)))
                {
                    var h = Rectangle.Inflate(dot, Math.Max(2, d / 3), Math.Max(2, d / 3));
                    g.FillEllipse(halo, h);
                }
                using (var b = new SolidBrush(value)) { g.FillEllipse(b, dot); }
                g.SmoothingMode = prev;
                TextRenderer.DrawText(g, rest, font, new Rectangle(r.Left + lead, r.Top, Math.Max(1, r.Width - lead), r.Height),
                    Theme.Readable(value, under, 4.5), flags);
                return;
            }

            // Quiet label, bright semibold value: "Profile:" names its label; a figure on a chip ("CPU 0.6%",
            // "Clicks: 12", "Up 0:04:26") has its words before the first digit as its label, found in the
            // text itself because every language words it differently. The figures used to be drawn whole
            // in the value's colour and weight, labels and all.
            int cut = !string.IsNullOrEmpty(seg.Label) && text.StartsWith(seg.Label, StringComparison.Ordinal)
                ? seg.Label.Length
                : (seg.Group > 0 ? FigureLabelLength(text) : 0);
            if (cut > 0)
            {
                string label = text.Substring(0, cut);
                string rest = text.Substring(cut);
                Font labelFont = LabelFont(font);
                int lw = TextRenderer.MeasureText(g, label, labelFont, Size.Empty, flags).Width;
                TextRenderer.DrawText(g, label, labelFont, new Rectangle(r.Left, r.Top, lw + 2, r.Height), LabelColor(under), flags);
                TextRenderer.DrawText(g, rest, font, new Rectangle(r.Left + lw, r.Top, Math.Max(1, r.Width - lw), r.Height),
                    Theme.Readable(value, under, 4.5), flags);
                return;
            }

            TextRenderer.DrawText(g, text, font, r, Theme.Readable(value, under, 4.5), flags);
        }

        /// <summary>
        /// How much of a figure's text is its label: everything before the first digit ("CPU " of
        /// "CPU 0.6%", "Klicks: " of "Klicks: 12"). 0 when there is no digit, or nothing before it.
        /// </summary>
        internal static int FigureLabelLength(string text)
        {
            if (string.IsNullOrEmpty(text)) { return 0; }
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsDigit(text[i])) { return i; }
            }
            return 0;
        }

        protected override void OnRenderStatusStripSizingGrip(ToolStripRenderEventArgs e)
        {
            // The default grip is three rows of light dots in the corner — noisy on a
            // dark theme. Draw a couple of subtle diagonal ticks in the border colour.
            var g = e.Graphics;
            var s = e.ToolStrip.ClientSize;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(Blend(_theme.Surface, _theme.TextMuted, 0.5)))
            {
                for (int i = 0; i < 3; i++)
                {
                    int o = 4 + i * 4;
                    g.DrawLine(p, s.Width - o, s.Height - 3, s.Width - 3, s.Height - o);
                }
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            // Slim, short, vertically-centred divider — nothing like the default
            // etched groove.
            int h = e.Item.Height;
            int top = h / 2 - 6;
            int bot = h / 2 + 6;
            int x = e.Item.Width / 2;
            using (var p = new Pen(_theme.Border))
            {
                e.Graphics.DrawLine(p, x, top, x, bot);
            }
        }

        /// <summary>What the hint shows in <paramref name="width"/>: its whole text, or its short form.</summary>
        internal static string HintTextFor(IDeviceContext g, StatusSegment seg, string text, Font font, int width)
        {
            if (string.IsNullOrEmpty(seg?.Short) || seg.Short == text) { return text; }
            const TextFormatFlags measure = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            int full = TextRenderer.MeasureText(g, text, font, Size.Empty, measure).Width;
            return full <= width - 4 ? text : seg.Short;
        }

        // The regular weight of a value's (semibold) font, for its label. Cached per size: the bar
        // repaints often, and a new renderer comes with every theme change.
        private static readonly Dictionary<float, Font> _labelFonts = new Dictionary<float, Font>();

        private static Font LabelFont(Font valueFont)
        {
            float size = valueFont.SizeInPoints;
            if (!_labelFonts.TryGetValue(size, out Font f))
            {
                try { f = new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Point); }
                catch { f = valueFont; }
                _labelFonts[size] = f;
            }
            return f;
        }

        private static GraphicsPath Pill(Rectangle r)
        {
            var p = new GraphicsPath();
            int d = Math.Max(2, Math.Min(r.Height, r.Width));
            if (r.Width <= 2 || r.Height <= 2) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 90, 180);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 180);
            p.CloseFigure();
            return p;
        }

        private static Color Blend(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        private sealed class ColorTable : ProfessionalColorTable
        {
            private readonly Theme _t;
            public ColorTable(Theme t) { _t = t ?? Theme.ForKind(Models.ThemeKind.Dark); }
            public override Color SeparatorDark => _t.Border;
            public override Color SeparatorLight => _t.Border;
        }
    }

    /// <summary>
    /// Vector icons for the status-bar stat labels, painted to bitmaps in a theme hue. Kept crisp
    /// and monochrome so the bar reads as one coherent dashboard rather than a row of mismatched OS
    /// emoji. Drawn on a 16-unit grid and scaled to <paramref name="size"/> pixels, so they grow
    /// with the display's scaling like the text beside them (they were a fixed 16 px).
    /// </summary>
    public static class StatusIcons
    {
        public enum Kind { Profile, Clicks, Cps, Peak, Time, Cpu, Ram, Uptime }

        public static Bitmap Make(Kind kind, Color color) => Make(kind, color, 16);

        public static Bitmap Make(Kind kind, Color color, int size)
        {
            size = Math.Max(12, size);
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.ScaleTransform(size / 16f, size / 16f);
                using (var pen = new Pen(color, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                using (var fill = new SolidBrush(color))
                {
                    switch (kind)
                    {
                        case Kind.Profile:
                        {
                            // A bookmark/tag.
                            var pts = new[]
                            {
                                new PointF(4, 2.5f), new PointF(12, 2.5f),
                                new PointF(12, 13.5f), new PointF(8, 10.5f), new PointF(4, 13.5f)
                            };
                            g.DrawPolygon(pen, pts);
                            break;
                        }
                        case Kind.Clicks:
                        {
                            // An arrow cursor.
                            var pts = new[]
                            {
                                new PointF(4, 2.5f), new PointF(4, 12.5f), new PointF(6.7f, 9.8f),
                                new PointF(8.6f, 13.6f), new PointF(10.1f, 12.9f),
                                new PointF(8.2f, 9.1f), new PointF(11.5f, 9f)
                            };
                            g.FillPolygon(fill, pts);
                            break;
                        }
                        case Kind.Cps:
                        {
                            // A speed gauge: a semicircle with a needle.
                            g.DrawArc(pen, 2.5f, 4f, 11f, 11f, 180, 180);
                            g.DrawLine(pen, 8f, 9.5f, 11f, 6.5f);
                            g.FillEllipse(fill, 6.8f, 8.3f, 2.4f, 2.4f);
                            break;
                        }
                        case Kind.Peak:
                        {
                            // An upward trend line with an arrowhead.
                            g.DrawLines(pen, new[]
                            {
                                new PointF(2.5f, 11.5f), new PointF(6.5f, 7.5f),
                                new PointF(9f, 10f), new PointF(13.5f, 4.5f)
                            });
                            g.DrawLines(pen, new[]
                            {
                                new PointF(10f, 4.5f), new PointF(13.5f, 4.5f), new PointF(13.5f, 8f)
                            });
                            break;
                        }
                        case Kind.Time:
                        {
                            // A clock.
                            g.DrawEllipse(pen, 2.5f, 2.5f, 11f, 11f);
                            g.DrawLine(pen, 8f, 8f, 8f, 4.8f);
                            g.DrawLine(pen, 8f, 8f, 10.6f, 9.2f);
                            break;
                        }
                        case Kind.Cpu:
                        {
                            // A processor: a die inside a package, with pins on all
                            // four sides — the shape everyone reads as "CPU".
                            g.DrawRectangle(pen, 4f, 4f, 8f, 8f);
                            g.DrawRectangle(pen, 6.5f, 6.5f, 3f, 3f);
                            for (int i = 0; i < 2; i++)
                            {
                                float o = 6.5f + i * 3f;
                                g.DrawLine(pen, o, 2f, o, 4f);      // top pins
                                g.DrawLine(pen, o, 12f, o, 14f);    // bottom pins
                                g.DrawLine(pen, 2f, o, 4f, o);      // left pins
                                g.DrawLine(pen, 12f, o, 14f, o);    // right pins
                            }
                            break;
                        }
                        case Kind.Ram:
                        {
                            // A memory module: a board with contact fingers below it.
                            g.DrawRectangle(pen, 2.5f, 4.5f, 11f, 6f);
                            g.DrawLine(pen, 5.5f, 6.5f, 5.5f, 8.5f);
                            g.DrawLine(pen, 8f, 6.5f, 8f, 8.5f);
                            g.DrawLine(pen, 10.5f, 6.5f, 10.5f, 8.5f);
                            for (int i = 0; i < 4; i++)
                            {
                                float x = 4f + i * 2.7f;
                                g.DrawLine(pen, x, 10.5f, x, 12.5f);
                            }
                            break;
                        }
                        case Kind.Uptime:
                        {
                            // A clock face with an anticlockwise arrow — "elapsed
                            // since start", distinct from the plain clock used for the
                            // click-run timer right beside it.
                            g.DrawArc(pen, 3f, 3f, 10f, 10f, 40, 290);
                            g.DrawLines(pen, new[]
                            {
                                new PointF(3f, 4.5f), new PointF(3.2f, 8.2f), new PointF(6.6f, 7.2f)
                            });
                            g.DrawLine(pen, 8f, 8f, 8f, 5.5f);
                            g.DrawLine(pen, 8f, 8f, 10.2f, 9.2f);
                            break;
                        }
                    }
                }
            }
            return bmp;
        }
    }
}
