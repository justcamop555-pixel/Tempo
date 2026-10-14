using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// What the caption bar and the caption history share while they are being moved: the same
    /// look (a dashed accent outline and a chip that says what to do), the same magnet to the
    /// screen's edges and middle, and the same resize edges. One place, so the two windows can
    /// never teach two different ways of doing the same thing.
    /// </summary>
    internal static class CaptionWindowChrome
    {
        // Window messages both windows handle while movable.
        internal const int WM_GETMINMAXINFO = 0x0024;
        internal const int WM_CONTEXTMENU = 0x007B;
        internal const int WM_NCHITTEST = 0x0084;
        internal const int WM_NCLBUTTONDBLCLK = 0x00A3;
        internal const int WM_NCRBUTTONUP = 0x00A5;
        internal const int WM_MOVING = 0x0216;
        internal const int WM_ENTERSIZEMOVE = 0x0231;
        internal const int WM_EXITSIZEMOVE = 0x0232;

        internal const int HTCAPTION = 2, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13,
                           HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINTSTRUCT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MINMAXINFO
        {
            public POINTSTRUCT Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
        }

        /// <summary>
        /// <paramref name="r"/> pulled flush to an edge of <paramref name="area"/> or centred on it —
        /// and, given the other caption window as <paramref name="partner"/>, lined up with it or
        /// stacked against it <paramref name="gap"/> px apart — when it comes within
        /// <paramref name="threshold"/> px. A magnet, so a bar goes exactly in the middle, or the
        /// history exactly above the bar, without pixel-hunting. The nearest target wins on each
        /// axis; only the position changes, never the size.
        /// </summary>
        internal static Rectangle Snap(Rectangle r, Rectangle area, int threshold, Rectangle? partner = null, int gap = 0)
        {
            var xs = new System.Collections.Generic.List<int>
            {
                area.Left, area.Right - r.Width, area.Left + (area.Width - r.Width) / 2
            };
            var ys = new System.Collections.Generic.List<int>
            {
                area.Top, area.Bottom - r.Height, area.Top + (area.Height - r.Height) / 2
            };
            if (partner is Rectangle p && p.Width > 0 && p.Height > 0)
            {
                xs.Add(p.Left);                              // left edges lined up
                xs.Add(p.Right - r.Width);                   // right edges lined up
                xs.Add(p.Left + (p.Width - r.Width) / 2);    // centred on it
                xs.Add(p.Right + gap);                       // beside it, right
                xs.Add(p.Left - gap - r.Width);              // beside it, left
                ys.Add(p.Top);                               // tops lined up
                ys.Add(p.Bottom - r.Height);                 // bottoms lined up
                ys.Add(p.Bottom + gap);                      // stacked under it
                ys.Add(p.Top - gap - r.Height);              // stacked above it
            }
            return new Rectangle(Nearest(r.X, xs, threshold), Nearest(r.Y, ys, threshold), r.Width, r.Height);
        }

        private static int Nearest(int value, System.Collections.Generic.List<int> targets, int threshold)
        {
            int best = value, bestDistance = threshold + 1;
            foreach (int t in targets)
            {
                int d = Math.Abs(t - value);
                if (d < bestDistance) { bestDistance = d; best = t; }
            }
            return best;
        }

        /// <summary>
        /// WM_MOVING: puts the window where the POINTER says, pulled flush to an edge, the middle or
        /// the other caption window when it is within reach (see <see cref="Snap"/>). Holding Alt
        /// moves freely. Returns true when it changed the rectangle.
        ///
        /// It used to snap the rectangle Windows proposed — and Windows builds each proposal from where
        /// the window was put LAST (already snapped) plus the pointer's latest small step. So a window
        /// on a magnet could never leave it: every step under the 14 px reach landed back inside it and
        /// was snapped home. Measured with real drags (scratchpad/bardrag): from x=0 a 1200 px drag to
        /// the left went 110, 50, 0, 0, 0 … and stayed on the main screen's left edge; the history,
        /// sitting on that edge, did not move at all. Both caption windows had been parked there — "the
        /// live captions won't move". The free place now comes from the pointer and
        /// <paramref name="grab"/> (where on the window the pointer holds it), so the magnet lets go as
        /// soon as the pointer is out of reach. The window clears <paramref name="grab"/> at
        /// WM_ENTERSIZEMOVE and WM_EXITSIZEMOVE; the drag's first WM_MOVING sets it.
        /// </summary>
        internal static bool SnapMoving(ref Message m, float dpi, Rectangle? partner, ref Point? grab)
        {
            if (m.LParam == IntPtr.Zero) { return false; }
            try
            {
                RECT rc = Marshal.PtrToStructure<RECT>(m.LParam);
                var proposed = new Rectangle(rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top);
                Point p = Cursor.Position;
                // The drag's first step: Windows' proposal is still the window's place plus the pointer's
                // travel, untouched by any snap, so it says exactly where on the window the pointer is.
                // (Taken at WM_ENTERSIZEMOVE instead, it trailed the pointer by a step: Windows starts
                // the move loop only once the pointer has moved.)
                if (grab == null) { grab = new Point(p.X - proposed.X, p.Y - proposed.Y); }
                var free = new Rectangle(p.X - grab.Value.X, p.Y - grab.Value.Y, proposed.Width, proposed.Height);
                // Alt: no magnet — but still the pointer's place, not the proposal, which may carry the
                // offset of a snap made a moment before Alt went down.
                Rectangle s = (Control.ModifierKeys & Keys.Alt) == Keys.Alt
                    ? free
                    : Snap(free, Screen.FromRectangle(free).WorkingArea, (int)Math.Round(14 * dpi), partner, (int)Math.Round(8 * dpi));
                if (s.Location == proposed.Location) { return false; }
                rc.Left = s.Left; rc.Top = s.Top; rc.Right = s.Right; rc.Bottom = s.Bottom;
                Marshal.StructureToPtr(rc, m.LParam, false);
                m.Result = (IntPtr)1;
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// True when all of <paramref name="r"/> lies on the monitors' working areas — on one monitor,
        /// or across two side by side. The caption windows' keep-it-on-screen checks clamped a window
        /// into ONE monitor every time it was shown and on every display change (a monitor waking from
        /// sleep is one), so a bar placed across the line between two monitors later jumped onto one of
        /// them by itself. Working areas never overlap, so summing the overlaps is exact.
        /// </summary>
        internal static bool FullyOnScreens(Rectangle r)
        {
            if (r.Width <= 0 || r.Height <= 0) { return false; }
            long covered = 0;
            foreach (Screen s in Screen.AllScreens)
            {
                Rectangle i = Rectangle.Intersect(s.WorkingArea, r);
                if (!i.IsEmpty) { covered += (long)i.Width * i.Height; }
            }
            return covered >= (long)r.Width * r.Height;
        }

        /// <summary>
        /// Where a point in the window is, for a window that can also be RESIZED: an edge or
        /// corner within <paramref name="grip"/> px of the border, otherwise the body (move).
        /// </summary>
        internal static int EdgeHitTest(Point client, Size size, int grip)
        {
            bool left = client.X < grip, right = client.X >= size.Width - grip;
            bool top = client.Y < grip, bottom = client.Y >= size.Height - grip;
            if (bottom && right) { return HTBOTTOMRIGHT; }
            if (bottom && left) { return HTBOTTOMLEFT; }
            if (top && right) { return HTTOPRIGHT; }
            if (top && left) { return HTTOPLEFT; }
            if (right) { return HTRIGHT; }
            if (left) { return HTLEFT; }
            if (bottom) { return HTBOTTOM; }
            if (top) { return HTTOP; }
            return HTCAPTION;
        }

        /// <summary>The screen point a WM_NCHITTEST / WM_NCRBUTTONUP carries in its lParam.</summary>
        internal static Point PointFromLParam(IntPtr lParam)
        {
            long v = lParam.ToInt64();
            return new Point((short)(v & 0xFFFF), (short)((v >> 16) & 0xFFFF));
        }

        /// <summary>
        /// The "you are moving this" look, drawn over the window's content: a dashed accent outline
        /// just inside the panel's edge, a chip at the top saying what can be done, and — for a
        /// window that resizes — a grip in the bottom-right corner.
        /// </summary>
        internal static void DrawMoveChrome(Graphics g, Size size, Theme theme, string hint, float dpi,
                                            int radius, bool resizeGrip)
        {
            if (g == null || size.Width < 20 || size.Height < 20) { return; }
            Color accent = theme != null ? theme.Accent : Color.DodgerBlue;
            Color chipBack = theme != null ? theme.Surface2 : Color.FromArgb(44, 52, 64);
            Color chipText = theme != null ? Theme.Readable(theme.Text, chipBack, 4.5) : Color.White;

            var prevSmooth = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // The outline: dashed, in the accent, 4 px inside the edge.
            int inset = Inset(dpi);
            var outline = new Rectangle(inset, inset, size.Width - 2 * inset - 1, size.Height - 2 * inset - 1);
            using (var path = Rounded(outline, Math.Max(4, radius - inset)))
            using (var pen = new Pen(Color.FromArgb(235, accent), Math.Max(1.5f, 2f * dpi)))
            {
                pen.DashStyle = DashStyle.Custom;
                pen.DashPattern = new[] { 4f, 3f };
                g.DrawPath(pen, path);
            }

            // The chip: what to do, in words, at the top.
            Rectangle chip = ChipRect(g, size, hint, dpi);
            if (!chip.IsEmpty)
            {
                using (var font = ChipFont(dpi))
                using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    int padX = ChipPadX(dpi), padY = ChipPadY(dpi);
                    using (var path = Rounded(chip, Math.Min(chip.Height / 2, (int)Math.Round(12 * dpi))))
                    {
                        using (var b = new SolidBrush(Color.FromArgb(236, chipBack))) { g.FillPath(b, path); }
                        using (var p = new Pen(Color.FromArgb(220, accent), Math.Max(1f, 1.2f * dpi))) { g.DrawPath(p, path); }
                    }
                    var prevText = g.TextRenderingHint;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    using (var tb = new SolidBrush(chipText))
                    {
                        g.DrawString(hint, font, tb, new RectangleF(chip.X + padX, chip.Y + padY,
                            chip.Width - 2 * padX, chip.Height - 2 * padY), fmt);
                    }
                    g.TextRenderingHint = prevText;
                }
            }

            // The resize grip: three short diagonals in the corner.
            if (resizeGrip)
            {
                using (var pen = new Pen(Color.FromArgb(220, accent), Math.Max(1.5f, 1.8f * dpi)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    float s = 5 * dpi;
                    float right = size.Width - inset - 5 * dpi, bottom = size.Height - inset - 5 * dpi;
                    for (int i = 1; i <= 3; i++)
                    {
                        g.DrawLine(pen, right - i * s, bottom, right, bottom - i * s);
                    }
                }
            }
            g.SmoothingMode = prevSmooth;
        }

        private static int Inset(float dpi) => Math.Max(3, (int)Math.Round(4 * dpi));
        private static int ChipPadX(float dpi) => (int)Math.Round(14 * dpi);
        private static int ChipPadY(float dpi) => (int)Math.Round(6 * dpi);
        private static Font ChipFont(float dpi) => new Font("Segoe UI Semibold", 9.5f * dpi, FontStyle.Regular, GraphicsUnit.Point);

        /// <summary>
        /// Where the move-mode chip sits (top middle), sized to its words and wrapped to the
        /// window's width; empty with no words. The windows lay their text out BELOW it while
        /// moving, so the chip never sits on top of the first line.
        /// </summary>
        internal static Rectangle ChipRect(Graphics g, Size size, string hint, float dpi)
        {
            if (g == null || string.IsNullOrEmpty(hint) || size.Width < 20) { return Rectangle.Empty; }
            int inset = Inset(dpi), padX = ChipPadX(dpi), padY = ChipPadY(dpi);
            using (var font = ChipFont(dpi))
            using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                int maxText = Math.Max(60, size.Width - 2 * inset - 2 * padX - (int)(24 * dpi));
                SizeF ts = g.MeasureString(hint, font, maxText, fmt);
                int cw = (int)Math.Ceiling(ts.Width) + 2 * padX;
                int ch = (int)Math.Ceiling(ts.Height) + 2 * padY;
                return new Rectangle((size.Width - cw) / 2, inset + (int)Math.Round(8 * dpi), cw, ch);
            }
        }

        internal static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = Math.Max(2, radius * 2);
            d = Math.Min(d, Math.Min(r.Width, r.Height));
            var p = new GraphicsPath();
            if (d < 2) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
