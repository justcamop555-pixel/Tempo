using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A small frameless, always-on-top, click-through badge shown near the top of
    /// the primary screen while the clicker is running. It pulses an accent dot and
    /// shows the live click count and CPS, so you can always tell Tempo is active —
    /// even with the main window minimised or hidden to the tray (handy on Windows
    /// 11, where a background app gives no other visible sign it's clicking).
    ///
    /// The window is click-through (WS_EX_TRANSPARENT) and never activates, so it
    /// can never intercept the auto-clicks or steal focus from whatever you're using.
    /// </summary>
    public sealed class ClickingIndicatorForm : Form
    {
        private Theme _theme;
        private readonly Timer _tick;
        private bool _dotOn = true;
        private string _stats = "";
        private string _title = "Tempo \u2014 clicking";
        private string _hint = "";
        private Color _dotColor;

        public ClickingIndicatorForm(Theme theme) : this(theme, null, default(Color)) { }

        public ClickingIndicatorForm(Theme theme, string title, Color dotColor)
        {
            _theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);
            if (!string.IsNullOrEmpty(title)) _title = title;
            _dotColor = dotColor.A == 0 ? _theme.Success : dotColor;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(232, 46);
            DoubleBuffered = true;
            BackColor = _theme.Surface;

            // Anchor to the top-centre of the working area (clear of the top-right
            // recording badge), nudged down a little from the very edge.
            var wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + 12);

            _tick = new Timer { Interval = 500 };
            _tick.Tick += (s, e) => { _dotOn = !_dotOn; Invalidate(); };
        }

        /// <summary>Updates the live "N clicks · X CPS" text shown in the badge.</summary>
        public void SetStats(long clicks, double cps)
        {
            string next = $"{clicks:N0} clicks   \u00B7   {cps:0.0} CPS";
            if (next != _stats)
            {
                _stats = next;
                Invalidate();
            }
        }

        /// <summary>Sets the sub-line text directly (used for non-click activity).</summary>
        public void SetStatusText(string text)
        {
            if (text != _stats) { _stats = text ?? ""; Invalidate(); }
        }

        public void ApplyTheme(Theme theme)
        {
            if (theme != null) { _theme = theme; BackColor = theme.Surface; Invalidate(); }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _tick.Start();
            Invalidate();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _tick.Stop();
            _tick.Dispose();
            base.OnFormClosed(e);
        }

        // Never steal focus from the foreground window.
        protected override bool ShowWithoutActivation => true;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
        private const uint LWA_ALPHA = 0x2;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // WS_EX_LAYERED windows stay invisible until their layered attributes are
            // set. Give the whole window a near-opaque alpha so the painted badge
            // actually shows (while still being click-through via WS_EX_TRANSPARENT).
            try { SetLayeredWindowAttributes(Handle, 0, 245, LWA_ALPHA); } catch { }
            ReclipRegion();
        }

        private void ReclipRegion()
        {
            // Clip the window to a rounded shape so the corners aren't boxy.
            try
            {
                using (var path = Rounded(new Rectangle(0, 0, Width, Height), 10))
                {
                    Region = new Region(path);
                }
            }
            catch { }
        }

        /// <summary>
        /// Sets an optional small third line (e.g. "Press F8 to stop"). When set, the
        /// badge grows to make room; pass null/empty to remove it.
        /// </summary>
        public void SetHint(string hint)
        {
            _hint = hint ?? "";
            int targetH = string.IsNullOrEmpty(_hint) ? 46 : 62;
            if (Height != targetH)
            {
                Height = targetH;
                if (IsHandleCreated) ReclipRegion();
            }
            Invalidate();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_LAYERED = 0x00080000;
                const int WS_EX_TRANSPARENT = 0x00000020; // mouse events pass through
                const int WS_EX_NOACTIVATE = 0x08000000;
                const int WS_EX_TOOLWINDOW = 0x00000080;  // keep out of Alt-Tab
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Rounded(rect, 10))
            {
                using (var fill = new SolidBrush(_theme.Surface))
                {
                    g.FillPath(fill, path);
                }
                using (var pen = new Pen(_dotColor.A == 0 ? _theme.Success : _dotColor, 2))
                {
                    g.DrawPath(pen, path);
                }
            }

            // Pulsing status dot.
            int dotD = 12;
            var dot = new Rectangle(14, (Height - dotD) / 2, dotD, dotD);
            Color baseDot = _dotColor.A == 0 ? _theme.Success : _dotColor;
            Color dotColor = _dotOn ? baseDot : Blend(baseDot, _theme.Surface, 0.55);
            using (var db = new SolidBrush(dotColor))
            {
                g.FillEllipse(db, dot);
            }

            using (var title = new Font("Segoe UI", 10f, FontStyle.Bold))
            using (var sub = new Font("Segoe UI", 8.5f, FontStyle.Regular))
            using (var tb = new SolidBrush(_theme.Text))
            using (var mb = new SolidBrush(_theme.TextMuted))
            {
                g.DrawString(_title, title, tb, 34, 5);
                g.DrawString(string.IsNullOrEmpty(_stats) ? "running\u2026" : _stats, sub, mb, 34, 25);
                if (!string.IsNullOrEmpty(_hint))
                {
                    g.DrawString(_hint, sub, mb, 34, 43);
                }
            }
        }

        private static Color Blend(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
