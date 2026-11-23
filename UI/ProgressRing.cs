using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// The "working on it" ring: an arc that spins while it grows and shrinks, like the busy ring of Windows 11 — for
    /// anything whose end can't be told (a check, a restart, an install step).
    /// </summary>
    internal static class ProgressRing
    {
        private const double CycleSeconds = 1.5;   // one grow-and-shrink of the arc
        private const double SpinDegPerSecond = 290;
        private const double MaxSweep = 260, MinSweep = 16;

        /// <summary>
        /// Draws the ring in <paramref name="box"/> as it is at <paramref name="seconds"/> on any steady clock.
        /// <paramref name="track"/>: a faint full circle under the arc (Color.Empty: none).
        /// </summary>
        public static void Paint(Graphics g, RectangleF box, Color color, double seconds, float thickness, Color track = default)
        {
            if (box.Width < 4 || box.Height < 4) { return; }
            float size = Math.Min(box.Width, box.Height);
            var r = new RectangleF(box.Left + (box.Width - size) / 2f + thickness / 2f, box.Top + (box.Height - size) / 2f + thickness / 2f,
                                   size - thickness, size - thickness);
            SmoothingMode was = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                if (!track.IsEmpty && track.A > 0)
                {
                    using (var pen = new Pen(track, thickness))
                    {
                        g.DrawEllipse(pen, r);
                    }
                }

                // The head runs ahead in the first half of a cycle, the tail catches it up in the second; each cycle
                // starts where the last one's tail stopped, and the whole ring turns steadily underneath.
                double cycles = seconds / CycleSeconds;
                double n = Math.Floor(cycles);
                double p = cycles - n;
                double head = Motion.EaseInOutCubic(Math.Min(1, p * 2)) * (MaxSweep - MinSweep);
                double tail = Motion.EaseInOutCubic(Math.Max(0, p * 2 - 1)) * (MaxSweep - MinSweep);
                double sweep = MinSweep + head - tail;
                double start = (seconds * SpinDegPerSecond + tail + n * (MaxSweep - MinSweep)) % 360.0;
                using (var pen = new Pen(color, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawArc(pen, r, (float)(start - 90), (float)sweep);
                }
            }
            finally
            {
                g.SmoothingMode = was;
            }
        }
    }

    /// <summary>
    /// A <see cref="ProgressRing"/> as a control: spins while it is on screen, and costs nothing once hidden — it
    /// takes frames from <see cref="Motion"/> only while visible.
    /// </summary>
    internal sealed class SpinnerControl : Control
    {
        private readonly Action _tick;
        private readonly double _born = Motion.NowMs;
        private bool _subscribed;

        public Color RingColor { get; set; } = Color.FromArgb(96, 142, 255);
        public Color TrackColor { get; set; } = Color.Empty;

        public SpinnerControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            BackColor = Color.Transparent;
            Size = new Size(36, 36);
            _tick = () => { if (IsHandleCreated && !IsDisposed) { Invalidate(); } };
        }

        private void Sync()
        {
            // Visible already answers for every parent up the chain.
            bool want = IsHandleCreated && !IsDisposed && Visible;
            if (want == _subscribed) { return; }
            _subscribed = want;
            if (want) { Motion.Subscribe(_tick); } else { Motion.Unsubscribe(_tick); }
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Sync(); }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); Sync(); }
        protected override void OnParentVisibleChanged(EventArgs e) { base.OnParentVisibleChanged(e); Sync(); }
        protected override void OnHandleDestroyed(EventArgs e)
        {
            Motion.Unsubscribe(_tick);
            _subscribed = false;
            base.OnHandleDestroyed(e);
        }

        protected override void Dispose(bool disposing)
        {
            Motion.Unsubscribe(_tick);
            _subscribed = false;
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            float thick = Math.Max(2f, Math.Min(Width, Height) / 10f);
            // With animations off the ring stands still — a quarter arc, still saying "working".
            double seconds = Motion.Enabled ? (Motion.NowMs - _born) / 1000.0 : 0.45;
            ProgressRing.Paint(e.Graphics, new RectangleF(0, 0, Width, Height), RingColor, seconds, thick, TrackColor);
        }
    }
}
