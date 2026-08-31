using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// Dresses the bottom status bar in Tempo's theme instead of the dated
    /// grey-gradient system look: a flat themed surface with a fine accent-tinted
    /// hairline across the top (separating it from the page), and slim, short
    /// centred separators in the border colour instead of the chunky etched
    /// double-lines the default renderer draws. Pairs with the small painted stat
    /// icons (see <see cref="StatusIcons"/>) to read as a compact dashboard.
    /// </summary>
    public sealed class StatusStripRenderer : ToolStripProfessionalRenderer
    {
        private readonly Theme _theme;

        public StatusStripRenderer(Theme theme) : base(new ColorTable(theme))
        {
            _theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);
        }

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
    /// Small (16 px) vector icons for the status-bar stat labels, painted to
    /// bitmaps in a theme hue. Kept crisp and monochrome so the compact bar reads
    /// as one coherent dashboard rather than a row of mismatched OS emoji.
    /// </summary>
    public static class StatusIcons
    {
        public enum Kind { Profile, Clicks, Cps, Peak, Time, Cpu, Ram, Uptime }

        public static Bitmap Make(Kind kind, Color color)
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
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
