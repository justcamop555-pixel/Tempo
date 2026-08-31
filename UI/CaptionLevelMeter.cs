using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A small horizontal input-level meter for the Captions tab.
    ///
    /// Live Captions exists for people who cannot hear the audio, which makes "is Tempo
    /// actually receiving any sound?" the one question the UI most needed to answer — and
    /// could not. The engine recomputes its level roughly 25 times a second and the figure
    /// only ever reached the Live debug window, so silence, a muted speaker, a capture on
    /// the wrong device and a broken engine all looked identical: no captions appearing.
    ///
    /// The bar reads in dBFS across a -60..0 range, with a held peak so short syllables
    /// stay visible, and turns red when the engine reports clipping (which transcribes
    /// badly and needs the volume turned DOWN, not a smaller model).
    /// </summary>
    internal sealed class CaptionLevelMeter : Control
    {
        private Theme _theme;
        private int _peakDb = MinDb;
        private int _peakHoldTicks;

        private const int MinDb = -60;
        private const int PeakHoldFrames = 12;   // ~2.4 s at the 200 ms UI tick

        /// <summary>Current input level in dBFS (-60 = silence, 0 = full scale).</summary>
        public int LevelDb { get; set; } = MinDb;

        /// <summary>True while the engine reports the input is clipping.</summary>
        public bool Clipping { get; set; }

        /// <summary>False when captions are off — the meter greys out rather than lying at silence.</summary>
        public bool Live { get; set; }

        public CaptionLevelMeter()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false;
            Height = 14;
        }

        public void ApplyTheme(Theme theme)
        {
            if (theme == null) { return; }
            bool changed = !ReferenceEquals(_theme, theme);
            _theme = theme;
            if (changed) { Invalidate(); }
            else { Invalidate(); }   // level changes every tick anyway
        }

        private static double Fraction(int db)
        {
            if (db <= MinDb) { return 0; }
            if (db >= 0) { return 1; }
            return (db - MinDb) / (double)(-MinDb);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme th = _theme;
            if (th == null) { return; }

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var track = new Rectangle(0, (Height - 10) / 2, Math.Max(1, Width - 1), 10);
            using (var path = Rounded(track, 5))
            using (var back = new SolidBrush(th.Surface2))
            {
                g.FillPath(back, path);
            }

            int db = Live ? Math.Max(MinDb, Math.Min(0, LevelDb)) : MinDb;

            // Peak hold: speech is spiky, and a bar that only shows the instantaneous
            // value reads as "barely anything" even when the level is fine.
            if (db >= _peakDb) { _peakDb = db; _peakHoldTicks = PeakHoldFrames; }
            else if (_peakHoldTicks > 0) { _peakHoldTicks--; }
            else if (_peakDb > MinDb) { _peakDb -= 2; }

            double frac = Fraction(db);
            if (frac > 0.001)
            {
                int w = Math.Max(2, (int)Math.Round(track.Width * frac));
                var fill = new Rectangle(track.X, track.Y, w, track.Height);
                Color c = Clipping ? th.Danger
                        : db > -6 ? th.Warning
                        : th.Success;
                using (var path = Rounded(fill, 5))
                using (var brush = new SolidBrush(Live ? c : th.TextMuted))
                {
                    g.FillPath(brush, path);
                }
            }

            // Held peak tick.
            if (Live && _peakDb > MinDb)
            {
                int px = track.X + (int)Math.Round(track.Width * Fraction(_peakDb));
                px = Math.Min(px, track.Right - 2);
                using (var pen = new Pen(th.Text, 2))
                {
                    g.DrawLine(pen, px, track.Top, px, track.Bottom);
                }
            }
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            if (r.Width <= 0 || r.Height <= 0) { path.AddRectangle(r); return path; }
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
