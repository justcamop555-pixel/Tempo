using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// A translucent full-screen overlay that lets the user pick a point on
    /// screen. Left-click confirms and stores the coordinate; Escape or
    /// right-click cancels.
    ///
    /// Drawn with per-pixel alpha (see <see cref="LayeredSurface"/>): only the dim over the
    /// screen is translucent. It used to be the whole form at 35% opacity, so the X/Y read-out
    /// and the instructions were 35% opaque too — hard to read over anything bright, which is
    /// exactly where people pick points.
    /// </summary>
    public sealed class CoordinatePickerForm : Form
    {
        private readonly Theme _theme;
        private Point _cursor;
        private LayeredSurface _surface;
        private bool _renderQueued;

        private readonly Font _readoutFont = new Font("Consolas", 11f, FontStyle.Bold);
        private readonly Font _hintFont = new Font("Segoe UI", 12f, FontStyle.Bold);

        /// <summary>The dim over the screen: 35% black, as the whole window used to be.</summary>
        private const int DimAlpha = 90;

        public int PickedX { get; private set; }
        public int PickedY { get; private set; }

        public CoordinatePickerForm(Theme theme)
        {
            AutoScaleMode = AutoScaleMode.None; // positioned in raw screen pixels
            _theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            Cursor = Cursors.Cross;

            // Cover the entire virtual desktop (all monitors).
            Bounds = SystemInformation.VirtualScreen;

            // Where the pointer already is, so the first frame has the crosshair under it
            // rather than in the top-left corner until the mouse moves.
            Point p = Control.MousePosition;
            _cursor = new Point(p.X - Bounds.Left, p.Y - Bounds.Top);

            KeyPreview = true;
            KeyDown += OnKeyDown;
            MouseMove += OnMouseMove;
            MouseDown += OnMouseDown;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_LAYERED = 0x00080000;
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // A layered window shows nothing until it is given its first frame.
            Render();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _surface?.Dispose();
            _surface = null;
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _surface?.Dispose();
                _surface = null;
                _readoutFont.Dispose();
                _hintFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            _cursor = e.Location;
            // One frame per batch of moves: a fast mouse reports far more often than a
            // full-screen frame is worth drawing.
            if (_renderQueued || !IsHandleCreated) { return; }
            _renderQueued = true;
            BeginInvoke(new Action(() =>
            {
                _renderQueued = false;
                Render();
            }));
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                // Convert the client point to absolute screen coordinates.
                PickedX = Bounds.Left + e.X;
                PickedY = Bounds.Top + e.Y;
                Logger.Info($"[UI] coordinate picked: ({PickedX}, {PickedY}).");
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private void Render()
        {
            if (!IsHandleCreated || IsDisposed) { return; }
            try
            {
                if (_surface == null || _surface.Width != Width || _surface.Height != Height)
                {
                    _surface?.Dispose();
                    _surface = new LayeredSurface(Width, Height);
                }
                using (Graphics g = Graphics.FromImage(_surface.Bitmap))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.Clear(Color.FromArgb(DimAlpha, 0, 0, 0));
                    g.CompositingMode = CompositingMode.SourceOver;
                    // Greyscale: ClearType fringes on a canvas that is not opaque.
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    Draw(g);
                }
                _surface.Push(Handle, Bounds.Location);
            }
            catch (Exception ex)
            {
                Logger.Swallow("CoordinatePicker.Render", ex);
            }
        }

        /// <summary>The monitor under the pointer, in this form's client coordinates.</summary>
        private Rectangle MonitorUnderCursor()
        {
            Rectangle m = Screen.FromPoint(new Point(Bounds.Left + _cursor.X, Bounds.Top + _cursor.Y)).Bounds;
            m.Offset(-Bounds.Left, -Bounds.Top);
            return m;
        }

        private void Draw(Graphics g)
        {
            // Crosshair lines across the whole overlay.
            using (var pen = new Pen(Color.FromArgb(200, _theme.Accent), 1f))
            {
                g.DrawLine(pen, 0, _cursor.Y, Width, _cursor.Y);
                g.DrawLine(pen, _cursor.X, 0, _cursor.X, Height);
            }

            Rectangle monitor = MonitorUnderCursor();

            // Coordinate read-out near the cursor.
            int absX = Bounds.Left + _cursor.X;
            int absY = Bounds.Top + _cursor.Y;
            string text = $"X: {absX}   Y: {absY}";

            using (var back = new SolidBrush(Color.FromArgb(235, 20, 20, 30)))
            using (var fore = new SolidBrush(Color.White))
            {
                SizeF size = g.MeasureString(text, _readoutFont);
                int boxW = (int)Math.Ceiling(size.Width) + 12;
                int boxH = (int)Math.Ceiling(size.Height) + 8;
                int boxX = _cursor.X + 16;
                int boxY = _cursor.Y + 16;

                // Keep the read-out on the monitor the pointer is on. The virtual desktop's
                // edges are not enough: with monitors of different sizes it has corners that
                // are on no screen at all, and a box there is drawn where nobody can see it.
                if (boxX + boxW > monitor.Right) { boxX = _cursor.X - boxW - 16; }
                if (boxY + boxH > monitor.Bottom) { boxY = _cursor.Y - boxH - 16; }
                if (boxX < monitor.Left) { boxX = monitor.Left; }
                if (boxY < monitor.Top) { boxY = monitor.Top; }

                g.FillRectangle(back, boxX, boxY, boxW, boxH);
                g.DrawString(text, _readoutFont, fore, boxX + 6, boxY + 4);
            }

            // What to do, on the monitor being looked at. It was centred on the whole virtual
            // desktop, which on two monitors put it across the gap between them, and it was
            // English in every language.
            string msg = Localization.T("Click to capture position  •  Esc or right-click to cancel");
            using (var back = new SolidBrush(Color.FromArgb(225, 20, 20, 30)))
            using (var brush = new SolidBrush(Color.White))
            {
                SizeF size = g.MeasureString(msg, _hintFont);
                float w = Math.Min(size.Width, monitor.Width - 40);
                float x = monitor.Left + (monitor.Width - w) / 2f;
                float y = monitor.Top + 24;
                g.FillRectangle(back, x - 14, y - 6, w + 28, size.Height + 12);
                g.DrawString(msg, _hintFont, brush, new RectangleF(x, y, w + 1, size.Height + 2));
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
