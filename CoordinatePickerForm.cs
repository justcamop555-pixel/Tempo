using System;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// A translucent full-screen overlay that lets the user pick a point on
    /// screen. Left-click confirms and stores the coordinate; Escape or
    /// right-click cancels.
    /// </summary>
    public sealed class CoordinatePickerForm : Form
    {
        private readonly Theme _theme;
        private Point _cursor;

        public int PickedX { get; private set; }
        public int PickedY { get; private set; }

        public CoordinatePickerForm(Theme theme)
        {
            _theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            Cursor = Cursors.Cross;

            // Cover the entire virtual desktop (all monitors).
            Bounds = SystemInformation.VirtualScreen;

            BackColor = Color.Black;
            Opacity = 0.35;

            KeyPreview = true;
            KeyDown += OnKeyDown;
            MouseMove += OnMouseMove;
            MouseDown += OnMouseDown;
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
            Invalidate();
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
                Logger.Info($"Coordinate picked: ({PickedX}, {PickedY}).");
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;

            // Crosshair lines across the whole overlay.
            using (var pen = new Pen(Color.FromArgb(220, _theme.Accent), 1f))
            {
                g.DrawLine(pen, 0, _cursor.Y, Width, _cursor.Y);
                g.DrawLine(pen, _cursor.X, 0, _cursor.X, Height);
            }

            // Coordinate read-out near the cursor.
            int absX = Bounds.Left + _cursor.X;
            int absY = Bounds.Top + _cursor.Y;
            string text = $"X: {absX}   Y: {absY}";

            using (var font = new Font("Consolas", 11f, FontStyle.Bold))
            using (var back = new SolidBrush(Color.FromArgb(230, 20, 20, 30)))
            using (var fore = new SolidBrush(Color.White))
            {
                SizeF size = g.MeasureString(text, font);
                int boxX = _cursor.X + 16;
                int boxY = _cursor.Y + 16;

                // Keep the read-out on screen.
                if (boxX + size.Width + 12 > Width) boxX = _cursor.X - (int)size.Width - 28;
                if (boxY + size.Height + 12 > Height) boxY = _cursor.Y - (int)size.Height - 28;

                g.FillRectangle(back, boxX, boxY, size.Width + 12, size.Height + 8);
                g.DrawString(text, font, fore, boxX + 6, boxY + 4);
            }

            using (var hint = new Font("Segoe UI", 12f, FontStyle.Bold))
            using (var brush = new SolidBrush(Color.FromArgb(230, Color.White)))
            {
                const string msg = "Click to capture position  •  Esc or right-click to cancel";
                SizeF size = g.MeasureString(msg, hint);
                g.DrawString(msg, hint, brush, (Width - size.Width) / 2f, 24);
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
