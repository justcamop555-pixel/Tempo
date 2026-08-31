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
    /// "Full history" view for Live Captions, styled exactly like Tempo's live
    /// caption overlay: a frameless, always-on-top, click-through panel with a
    /// faint (~20% opaque) dark background so several lines stay readable over a
    /// game, and outlined/shadowed text in the same colour/font as the live bar.
    ///
    /// Where the live bar shows only the newest line or two, this panel shows the
    /// most recent several lines of the session transcript and auto-scrolls to the
    /// bottom as new captions arrive - a rolling history you can glance back at
    /// without it ever stealing focus or blocking clicks.
    ///
    /// The faint background needs true per-pixel alpha (a colour key can only make
    /// pixels fully transparent, not semi), so this window paints itself through
    /// UpdateLayeredWindow with a constant panel alpha.
    /// </summary>
    public sealed class CaptionHistoryForm : Form
    {
        private Theme _theme;
        private readonly List<string> _lines = new List<string>();
        private int _fontPoints = 18;
        private string _fontFamily = "Segoe UI";
        private Color _textColor = Color.FromArgb(255, 244, 191, 79);
        private bool _useCustomColor = true;
        private int _textOpacityPercent = 50;

        // Background panel opacity. "80% transparent" => ~20% opaque (alpha ~51).
        private const int PanelAlpha = 51;
        // How many recent lines the panel shows at once.
        private const int VisibleLines = 7;

        public CaptionHistoryForm(Theme theme)
        {
            _theme = theme ?? Theme.ForKind(ThemeKind.Dark);

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;

            LayoutPanel();
        }

        /// <summary>Position: upper-left area, matching the earlier sketch.</summary>
        private void LayoutPanel()
        {
            try
            {
                var wa = Screen.PrimaryScreen.WorkingArea;
                int w = (int)(wa.Width * 0.42);
                int lineH = (int)(_fontPoints * 1.7);
                int h = lineH * VisibleLines + 28;
                int x = wa.Left + 24;
                int y = wa.Top + 80;
                Bounds = new Rectangle(x, y, w, h);
            }
            catch { }
        }

        public void SetFontSize(int points)
        {
            points = Math.Max(10, Math.Min(72, points));
            if (points != _fontPoints) { _fontPoints = points; ResizeForFont(); Repaint(); }
        }

        /// <summary>Resize for the current font but keep the current top-left.</summary>
        private void ResizeForFont()
        {
            try
            {
                // Size and clamp against the monitor the panel is ACTUALLY on — using
                // the primary screen teleported a panel dragged to a second monitor
                // back onto the primary on every font-size change.
                var wa = Screen.FromRectangle(Bounds).WorkingArea;
                int w = (int)(wa.Width * 0.42);
                int lineH = (int)(_fontPoints * 1.7);
                int h = lineH * VisibleLines + 28;
                int x = Left;
                int y = Top;
                if (x + w > wa.Right) x = Math.Max(wa.Left, wa.Right - w);
                if (y + h > wa.Bottom) y = Math.Max(wa.Top, wa.Bottom - h);
                Bounds = new Rectangle(x, y, w, h);
            }
            catch { }
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

        /// <summary>Replaces the displayed history; repaints and auto-scrolls.</summary>
        public void SetHistory(IList<string> lines)
        {
            _lines.Clear();
            if (lines != null) _lines.AddRange(lines);
            Repaint();
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

        private bool _movable;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT_FLAG = 0x00000020;

        /// <summary>Turns drag-to-move on/off (off = click-through, as normal).</summary>
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

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        private const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004,
                           SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020;

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
                PositionChanged?.Invoke(Location);
            }
        }

        public void MoveTo(Point p)
        {
            try { Location = p; Repaint(); } catch { }
        }

        /// <summary>Clamps the history window back onto the visible work area.</summary>
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

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Repaint();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Repaint();
        }

        // ── Layered-window painting ──────────────────────────────────────────
        private void Repaint()
        {
            if (!IsHandleCreated || Width <= 0 || Height <= 0)
            {
                return;
            }

            try
            {
                using (var bmp = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    bmp.SetResolution(96f, 96f);   // measure/draw in 96-DPI space (see overlay)
                    using (var g = Graphics.FromImage(bmp))
                    {
                        DrawContent(g);
                    }
                    PushLayered(bmp);
                }
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

        private void DrawContent(Graphics g)
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;

            // Faint rounded background panel (~20% opaque) so several lines read.
            using (var bg = new SolidBrush(Color.FromArgb(PanelAlpha, 8, 10, 16)))
            using (var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 14))
            {
                g.FillPath(bg, path);
            }

            // Say something while there is nothing to show.
            //
            // With no lines this drew the faint ~20%-opaque panel and returned — an
            // almost invisible rectangle over the wallpaper. Opening the history before
            // any speech had been transcribed therefore looked exactly like the window
            // failing to open, and it only "appeared" when the first caption landed.
            // That is the reported delay: the panel was there the whole time with
            // nothing written on it.
            // Rendered from a LOCAL list — never by adding to _lines, which would write
            // the placeholder into the real history and then keep re-adding it on every
            // repaint.
            IList<string> render = _lines.Count > 0
                ? (IList<string>)_lines
                : new List<string> { "Captions will appear here as they're transcribed…" };

            int alpha = (int)Math.Round(255 * (_textOpacityPercent / 100.0));
            Color baseColor = _useCustomColor ? _textColor : _theme.Text;
            Color textColor = Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B);
            Color outline = Color.FromArgb(alpha, 0, 0, 0);

            // Same trap the caption bar had: FontFamily.GenericSansSerif is a single
            // PROCESS-WIDE cached instance, and `using (fam)` below disposes whatever it
            // is handed. One font that failed to construct therefore destroyed the shared
            // generic family for the whole process, and every later use of it anywhere in
            // Tempo threw "Object is in a disposed state". This panel repaints on every
            // caption line, so it had just as many chances to trip it as the bar did.
            // SafeFallbackFamily always returns an instance we own.
            FontFamily fam;
            try { fam = new FontFamily(_fontFamily); }
            catch { fam = CaptionOverlayForm.SafeFallbackFamily(); }

            // Some installed fonts have no Bold face; constructing a Bold Font from them
            // throws and would blank the panel (or crash via OnShown). Pick a supported
            // style and fall back to a safe sans-serif — same defence as the overlay.
            FontStyle hstyle = fam.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold
                             : (fam.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular : FontStyle.Italic);
            float hdpi = DeviceDpi / 96f;
            Font font;
            try { font = new Font(fam, _fontPoints * hdpi, hstyle, GraphicsUnit.Point); }
            catch
            {
                try { fam.Dispose(); } catch { }
                fam = CaptionOverlayForm.SafeFallbackFamily();
                hstyle = fam.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;
                font = new Font(fam, _fontPoints * hdpi, hstyle, GraphicsUnit.Point);
            }

            using (fam)
            using (font)
            using (var fmt = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Near,
                Trimming = StringTrimming.None,        // never cut with "…"
                FormatFlags = StringFormatFlags.NoClip  // allow full wrapping
            })
            {
                float textWidth = Width - 28;
                float gap = font.GetHeight(g) * 0.25f;

                // Each history entry wraps to however many rows it needs. We walk
                // the newest entries first, measuring real wrapped height, and only
                // keep what fits the panel - then draw them top-to-bottom so the
                // most recent text is always fully visible (never truncated).
                //
                // IMPORTANT: GraphicsPath.AddString renders glyphs taller than
                // Graphics.MeasureString reports for the same font, so spacing rows
                // by MeasureString made consecutive lines overlap. We size each row
                // by the path's OWN bounds (what's actually drawn) plus a guaranteed
                // gap, so lines never touch.
                float emSize = font.SizeInPoints * 1.333f;

                Func<string, float> rowHeight = (line) =>
                {
                    using (var mp = new GraphicsPath())
                    {
                        mp.AddString(line, font.FontFamily, (int)font.Style, emSize,
                            new RectangleF(0, 0, textWidth, 100000f), fmt);
                        RectangleF b = mp.GetBounds();
                        return (b.Height > 1 ? b.Height : font.GetHeight(g)) + gap;
                    }
                };

                var toDraw = new System.Collections.Generic.List<string>();
                float used = 0;
                float avail = Height - 24;
                for (int i = render.Count - 1; i >= 0; i--)
                {
                    string line = render[i];
                    float h = rowHeight(line);
                    if (used + h > avail && toDraw.Count > 0)
                    {
                        break;
                    }
                    toDraw.Insert(0, line);
                    used += h;
                    if (used >= avail) break;
                }

                float y = 12;
                foreach (string line in toDraw)
                {
                    float h = rowHeight(line);
                    var rect = new RectangleF(14, y, textWidth, h);
                    using (var path = new GraphicsPath())
                    {
                        path.AddString(line, font.FontFamily, (int)font.Style,
                            emSize, rect, fmt);

                        var saved = g.Transform;
                        g.TranslateTransform(1.5f, 1.5f);
                        using (var sh = new SolidBrush(Color.FromArgb((int)(alpha * 0.55), 0, 0, 0)))
                        {
                            g.FillPath(sh, path);
                        }
                        g.Transform = saved;

                        using (var pen = new Pen(outline, 2.6f) { LineJoin = LineJoin.Round })
                        {
                            g.DrawPath(pen, path);
                        }
                        using (var brush = new SolidBrush(textColor))
                        {
                            g.FillPath(brush, path);
                        }
                    }
                    y += h;
                }
            }
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
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
    }
}
