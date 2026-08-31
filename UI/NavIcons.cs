using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace AutoClicker.UI
{
    /// <summary>Which little glyph a sidebar nav button draws to its left.</summary>
    public enum NavIconKind
    {
        None,
        Cursor,    // Clicker
        Points,    // Multi-Point
        Macro,     // Macros
        Chart,     // Statistics
        Keyboard,  // Keybinds
        Caption,   // Live Captions
        Gear       // Settings
    }

    /// <summary>
    /// Draws small, single-colour vector icons for the navigation sidebar so each
    /// section is recognisable at a glance. Everything is drawn from primitives in a
    /// normalised box, so the icons scale cleanly and tint to any colour (they follow
    /// the button's text colour).
    /// </summary>
    internal static class NavIcons
    {
        public static void Draw(Graphics g, NavIconKind kind, RectangleF b, Color color)
        {
            if (kind == NavIconKind.None || b.Width < 4 || b.Height < 4)
            {
                return;
            }

            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(color))
            using (var pen = new Pen(color, Math.Max(1.4f, b.Width * 0.10f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                switch (kind)
                {
                    case NavIconKind.Cursor: DrawCursor(g, brush, b); break;
                    case NavIconKind.Points: DrawPoints(g, brush, b); break;
                    case NavIconKind.Macro: DrawMacro(g, brush, b); break;
                    case NavIconKind.Chart: DrawChart(g, brush, b); break;
                    case NavIconKind.Keyboard: DrawKeyboard(g, brush, pen, b); break;
                    case NavIconKind.Caption: DrawCaption(g, brush, pen, b); break;
                    case NavIconKind.Gear: DrawGear(g, brush, b); break;
                }
            }
            g.SmoothingMode = old;
        }

        private static PointF Pt(RectangleF b, float fx, float fy)
        {
            return new PointF(b.X + b.Width * fx, b.Y + b.Height * fy);
        }

        private static void DrawCursor(Graphics g, Brush brush, RectangleF b)
        {
            PointF[] arrow =
            {
                Pt(b, 0.18f, 0.05f), Pt(b, 0.18f, 0.78f), Pt(b, 0.37f, 0.60f),
                Pt(b, 0.49f, 0.92f), Pt(b, 0.61f, 0.86f), Pt(b, 0.48f, 0.55f),
                Pt(b, 0.74f, 0.55f)
            };
            g.FillPolygon(brush, arrow);
        }

        private static void DrawPoints(Graphics g, Brush brush, RectangleF b)
        {
            float r = b.Width * 0.14f;
            FillDot(g, brush, Pt(b, 0.30f, 0.28f), r);
            FillDot(g, brush, Pt(b, 0.73f, 0.40f), r);
            FillDot(g, brush, Pt(b, 0.43f, 0.74f), r);
        }

        private static void FillDot(Graphics g, Brush brush, PointF c, float r)
        {
            g.FillEllipse(brush, c.X - r, c.Y - r, r * 2, r * 2);
        }

        private static void DrawMacro(Graphics g, Brush brush, RectangleF b)
        {
            PointF[] tri = { Pt(b, 0.26f, 0.16f), Pt(b, 0.26f, 0.84f), Pt(b, 0.84f, 0.50f) };
            g.FillPolygon(brush, tri);
        }

        private static void DrawChart(Graphics g, Brush brush, RectangleF b)
        {
            float bw = b.Width * 0.20f;
            float gap = b.Width * 0.08f;
            float x = b.X + b.Width * 0.10f;
            float baseY = b.Bottom - b.Height * 0.08f;
            float[] h = { 0.42f, 0.72f, 1.0f };
            for (int i = 0; i < 3; i++)
            {
                float hh = b.Height * 0.80f * h[i];
                g.FillRectangle(brush, x, baseY - hh, bw, hh);
                x += bw + gap;
            }
        }

        private static void DrawKeyboard(Graphics g, Brush brush, Pen pen, RectangleF b)
        {
            var body = new RectangleF(
                b.X + b.Width * 0.05f, b.Y + b.Height * 0.26f,
                b.Width * 0.90f, b.Height * 0.48f);
            using (var path = Rounded(body, b.Width * 0.10f))
            {
                g.DrawPath(pen, path);
            }

            float ks = b.Width * 0.11f;
            float ky = body.Y + body.Height * 0.24f;
            for (int i = 0; i < 3; i++)
            {
                g.FillRectangle(brush, body.X + body.Width * (0.16f + 0.27f * i), ky, ks, ks);
            }
            g.FillRectangle(brush,
                body.X + body.Width * 0.24f, body.Bottom - body.Height * 0.36f,
                body.Width * 0.52f, ks * 0.7f);
        }

        /// <summary>A speech bubble with two caption lines in it.</summary>
        private static void DrawCaption(Graphics g, Brush brush, Pen pen, RectangleF b)
        {
            var body = new RectangleF(
                b.X + b.Width * 0.06f, b.Y + b.Height * 0.20f,
                b.Width * 0.88f, b.Height * 0.52f);
            using (var path = Rounded(body, b.Width * 0.14f))
            {
                g.DrawPath(pen, path);
            }

            // Tail, bottom-left, so it reads as speech rather than a plain box.
            var tail = new PointF[]
            {
                new PointF(body.X + body.Width * 0.20f, body.Bottom - pen.Width * 0.5f),
                new PointF(body.X + body.Width * 0.20f, body.Bottom + b.Height * 0.16f),
                new PointF(body.X + body.Width * 0.44f, body.Bottom - pen.Width * 0.5f)
            };
            g.FillPolygon(brush, tail);

            // Two text lines, the second short, the way a caption wraps.
            float h = b.Height * 0.075f;
            g.FillRectangle(brush, body.X + body.Width * 0.16f,
                body.Y + body.Height * 0.28f, body.Width * 0.68f, h);
            g.FillRectangle(brush, body.X + body.Width * 0.16f,
                body.Y + body.Height * 0.56f, body.Width * 0.44f, h);
        }

        private static void DrawGear(Graphics g, Brush brush, RectangleF b)
        {
            float cx = b.X + b.Width / 2f;
            float cy = b.Y + b.Height / 2f;
            float rOut = Math.Min(b.Width, b.Height) * 0.48f;
            float rIn = rOut * 0.72f;
            float rHole = rOut * 0.40f;
            const int teeth = 8;

            var pts = new System.Collections.Generic.List<PointF>();
            double per = Math.PI * 2.0 / teeth;
            for (int i = 0; i < teeth; i++)
            {
                double a = i * per;
                pts.Add(Polar(cx, cy, rIn, a));
                pts.Add(Polar(cx, cy, rOut, a + per * 0.12));
                pts.Add(Polar(cx, cy, rOut, a + per * 0.38));
                pts.Add(Polar(cx, cy, rIn, a + per * 0.50));
            }

            using (var path = new GraphicsPath())
            {
                path.AddPolygon(pts.ToArray());
                path.AddEllipse(cx - rHole, cy - rHole, rHole * 2, rHole * 2);
                path.FillMode = FillMode.Alternate;   // inner circle becomes a hole
                g.FillPath(brush, path);
            }
        }

        private static PointF Polar(float cx, float cy, float r, double a)
        {
            return new PointF((float)(cx + r * Math.Cos(a)), (float)(cy + r * Math.Sin(a)));
        }

        private static GraphicsPath Rounded(RectangleF r, float radius)
        {
            float d = radius * 2;
            var p = new GraphicsPath();
            if (d <= 0)
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
    }
}
