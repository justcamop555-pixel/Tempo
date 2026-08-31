using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A small borderless "Tempo / loading…" splash shown the instant the process
    /// starts, while the main window is being built. It runs on its OWN UI thread (see
    /// Program.cs) so its loading bar keeps animating even while the main thread is busy
    /// constructing MainForm. The main window calls <see cref="RequestClose"/> once it is
    /// shown; the splash then fades out (after a short minimum on-screen time so it never
    /// just blinks) and closes itself. A hard timeout guarantees it can never linger.
    /// </summary>
    public sealed class SplashForm : Form
    {
        private static volatile bool _closeRequested;
        private static volatile bool _isClosed;

        private readonly System.Windows.Forms.Timer _timer;
        private readonly System.Diagnostics.Stopwatch _shown = System.Diagnostics.Stopwatch.StartNew();
        private float _phase;
        private float _progress;   // eased 0..1 fill for the determinate bar
        private bool _fadingOut;
        private readonly string _versionText;

        // Theme-aware palette, loaded once from the saved settings so the splash matches
        // the accent/theme the user has chosen (falls back to the brand violet/blue).
        private Color _bg = Color.FromArgb(17, 18, 22);
        private Color _accent = Color.FromArgb(96, 142, 255);
        private Color _accent2 = Color.FromArgb(124, 92, 232);
        private Color _titleColor = Color.FromArgb(236, 237, 242);
        private Color _subColor = Color.FromArgb(150, 152, 162);
        private Color _trackColor = Color.FromArgb(38, 40, 48);
        private Color _borderColor = Color.FromArgb(52, 54, 64);

        private const int MinVisibleMs = 1100;   // never flash by quicker than this
        private const int MaxVisibleMs = 8000;    // safety: never hang forever
        private const int StepMs = 320;           // cosmetic per-step tick cadence

        // Tagline under the wordmark \u2014 a one-line reminder of what Tempo is.
        private const string Tagline = "Precision auto-clicker  \u00b7  Macros  \u00b7  Offline live captions";

        // A checklist of the real stages startup goes through, ticked off in order so
        // the splash reads as genuinely doing something (users asked for more detail
        // than a single "loading\u2026" line). Cosmetic timing \u2014 the splash runs on its own
        // thread and doesn't instrument the actual steps \u2014 but these ARE the stages the
        // main window builds through, and the list completes as the window appears.
        private static readonly string[] LoadingSteps =
        {
            "Loading settings & profiles",
            "Restoring your saved macros",
            "Warming the speech-caption engine",
            "Detecting audio devices",
            "Registering global hotkeys",
        };

        // What each stage ACTUALLY found, filled in by MainForm as it gets there
        // (e.g. "11 macros · 2,860 steps"). The checklist used to be a pure timer guess
        // with no connection to the real work, so on a slow start it ticked stages off
        // that hadn't happened and on a fast one it lagged behind a window that was
        // already up. Reported stages now drive it, and the detail says what was found.
        private static readonly string[] StepDetail = new string[LoadingSteps.Length];
        private static volatile int _reportedStep = -1;

        /// <summary>
        /// Called by the app as each real startup stage completes. <paramref name="detail"/>
        /// is the concrete result to show beside it ("1 profile", "5 hotkeys"). Safe to
        /// call from any thread and before/after the splash exists.
        /// </summary>
        public static void Report(int step, string detail)
        {
            try
            {
                if (step < 0 || step >= LoadingSteps.Length) { return; }
                StepDetail[step] = detail;
                if (step > _reportedStep) { _reportedStep = step; }
            }
            catch { /* never let a progress note break start-up */ }
        }

        public SplashForm()
        {
            LoadThemeColors();

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            Size = new Size(468, 344);
            BackColor = _bg;
            Opacity = 1.0;

            try
            {
                Version v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                _versionText = v != null ? "v" + v.Major + "." + v.Minor + "." + v.Build : "";
            }
            catch { _versionText = ""; }

            // Soft rounded corners for a less boxy look.
            try
            {
                using (var path = RoundedPath(0, 0, Width, Height, 14))
                {
                    Region = new Region(path);
                }
            }
            catch { /* a square splash is fine if region fails */ }

            _timer = new System.Windows.Forms.Timer { Interval = 16 };
            _timer.Tick += OnTick;
            _timer.Start();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Make sure the splash actually paints and sits on top the instant it
            // opens, even on a fast machine where the main window is built almost
            // immediately. Without this the splash could be told to close before it
            // ever drew, so the launch effect was never seen.
            try { TopMost = true; BringToFront(); Update(); } catch { }
        }

        /// <summary>Signals the splash (on its own thread) to fade out and close.</summary>
        public static void RequestClose()
        {
            _closeRequested = true;
        }

        /// <summary>
        /// True once the splash has actually closed (or could never open). The main
        /// window waits on this before fading itself in, so the splash is fully seen
        /// first instead of being covered immediately on fast machines.
        /// </summary>
        public static bool IsClosed => _isClosed;

        /// <summary>Marks the splash finished - also called if it failed to open at all.</summary>
        public static void MarkClosed()
        {
            _isClosed = true;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _isClosed = true;
            base.OnFormClosed(e);
        }

        private void OnTick(object sender, EventArgs e)
        {
            _phase += 0.045f;

            long ms = _shown.ElapsedMilliseconds;
            bool shouldGo = (_closeRequested && ms >= MinVisibleMs) || ms >= MaxVisibleMs;

            // Ease the progress bar toward its target: creep up to ~90% over the first
            // couple of seconds while the window builds, then snap to 100% once the main
            // window has signalled it's ready (or the hard timeout hits).
            float target = (_closeRequested || shouldGo)
                ? 1f
                : Math.Min(0.9f, ms / 2400f);
            _progress += (target - _progress) * 0.18f;
            if (_progress > 0.999f) { _progress = 1f; }

            Invalidate();

            if (shouldGo)
            {
                _fadingOut = true;
            }

            if (_fadingOut)
            {
                double next = Opacity - 0.12;
                if (next <= 0)
                {
                    _timer.Stop();
                    try { Close(); } catch { }
                }
                else
                {
                    try { Opacity = next; } catch { }
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Soft accent-tinted radial wash behind everything, brightest up around the logo,
            // fading to the background — gives the splash depth instead of a flat panel.
            using (var glowPath = new GraphicsPath())
            {
                var glowRect = new Rectangle(-Width / 2, -Height / 2, Width * 2, Height + 60);
                glowPath.AddEllipse(glowRect);
                using (var glow = new PathGradientBrush(glowPath))
                {
                    glow.CenterPoint = new PointF(Width / 2f, 64f);
                    glow.CenterColor = Color.FromArgb(30, _accent);
                    glow.SurroundColors = new[] { Color.FromArgb(0, _accent) };
                    g.FillRectangle(glow, 0, 0, Width, Height);
                }
            }

            // Accent strip across the top.
            using (var strip = new LinearGradientBrush(
                new Rectangle(0, 0, Width, 3), _accent, _accent2, LinearGradientMode.Horizontal))
            {
                g.FillRectangle(strip, 0, 0, Width, 3);
            }

            // ── Brand row: logo tile (with breathing glow) + wordmark + version ──
            double breathe = 0.5 + 0.5 * Math.Sin(_phase * 1.6);
            const int logo = 52;
            int ly = 30;
            using (var titleFont = new Font("Segoe UI", 25f, FontStyle.Bold))
            using (var verFont = new Font("Segoe UI", 9.5f, FontStyle.Regular))
            {
                SizeF tsz = g.MeasureString("Tempo", titleFont);
                SizeF vsz = string.IsNullOrEmpty(_versionText) ? SizeF.Empty : g.MeasureString(_versionText, verFont);
                float gapLogo = 16f, gapVer = string.IsNullOrEmpty(_versionText) ? 0f : 8f;
                float rowW = logo + gapLogo + tsz.Width + gapVer + vsz.Width;
                float rowX = (Width - rowW) / 2f;
                int lx = (int)rowX;

                using (var gp = new GraphicsPath())
                {
                    gp.AddEllipse(lx - 15, ly - 15, logo + 30, logo + 30);
                    using (var pgb = new PathGradientBrush(gp))
                    {
                        pgb.CenterColor = Color.FromArgb(30 + (int)(46 * breathe), _accent);
                        pgb.SurroundColors = new[] { Color.FromArgb(0, _accent) };
                        g.FillPath(pgb, gp);
                    }
                }
                using (var lp = RoundedPath(lx, ly, logo, logo, 15))
                using (var lg = new LinearGradientBrush(
                    new Rectangle(lx, ly, logo, logo), _accent, _accent2, LinearGradientMode.ForwardDiagonal))
                {
                    g.FillPath(lg, lp);
                }
                DrawBolt(g, new RectangleF(lx + 12, ly + 11, 29, 31), Color.White);

                float textX = rowX + logo + gapLogo;
                float titleY = ly + (logo - tsz.Height) / 2f;
                using (var titleBrush = new SolidBrush(_titleColor))
                {
                    g.DrawString("Tempo", titleFont, titleBrush, textX, titleY);
                }
                if (!string.IsNullOrEmpty(_versionText))
                {
                    using (var verBrush = new SolidBrush(_subColor))
                    {
                        g.DrawString(_versionText, verFont, verBrush,
                            textX + tsz.Width + gapVer, titleY + tsz.Height - vsz.Height - 7f);
                    }
                }
            }

            // Tagline.
            using (var tagFont = new Font("Segoe UI", 9.5f, FontStyle.Regular))
            using (var tagBrush = new SolidBrush(_subColor))
            {
                // Translated at PAINT time, not when the string was declared.
                //
                // The splash thread starts before MainForm has read settings.json, so at
                // construction there is no language yet — anything captured then would be
                // English for the whole run, which is why this screen stayed English in
                // every language. Localization.Current is set a moment later (MainForm,
                // right after SettingsManager.Load), and the splash repaints continuously,
                // so translating here means it switches within the first frames and is
                // fully translated for the second or more that anyone actually reads it.
                string tag = Utils.Localization.T(Tagline);
                SizeF sz = g.MeasureString(tag, tagFont);
                g.DrawString(tag, tagFont, tagBrush, (Width - sz.Width) / 2f, 96f);
            }

            // Divider under the brand block.
            using (var dp = new Pen(_borderColor))
            {
                g.DrawLine(dp, 40, 124, Width - 40, 124);
            }

            // ── Checklist: real startup stages, ticked off as the window builds ──
            // Prefer what the app actually reported; the timer only carries the list
            // forward when nothing has reported yet (so it never sits frozen at zero).
            int timed = Math.Min(LoadingSteps.Length - 1, (int)(_shown.ElapsedMilliseconds / StepMs));
            int activeStep = _progress >= 1f
                ? LoadingSteps.Length
                : Math.Max(_reportedStep + 1, Math.Min(timed, _reportedStep + 2));
            int listX = 74;
            int rowH = 25;
            int listY = 140;
            using (var doneFont = new Font("Segoe UI", 9.75f, FontStyle.Regular))
            using (var activeFont = new Font("Segoe UI", 9.75f, FontStyle.Bold))
            {
                for (int i = 0; i < LoadingSteps.Length; i++)
                {
                    int rowY = listY + i * rowH;
                    float cy = rowY + rowH / 2f;
                    bool done = i < activeStep;
                    bool active = i == activeStep;

                    var glyphBox = new RectangleF(listX, cy - 8, 16, 16);
                    if (done)
                    {
                        using (var fill = new SolidBrush(_accent))
                        {
                            g.FillEllipse(fill, glyphBox);
                        }
                        using (var chk = new Pen(Color.White, 1.7f)
                        { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                        {
                            g.DrawLines(chk, new[]
                            {
                                new PointF(glyphBox.Left + 4, cy),
                                new PointF(glyphBox.Left + 7, cy + 3.2f),
                                new PointF(glyphBox.Right - 3.5f, cy - 3.6f)
                            });
                        }
                    }
                    else if (active)
                    {
                        // A pulsing accent ring with a filled core — the "in progress" dot.
                        float pulse = (float)(0.45 + 0.55 * breathe);
                        using (var ring = new Pen(Color.FromArgb((int)(150 + 105 * pulse), _accent), 2f))
                        {
                            g.DrawEllipse(ring, glyphBox.Left + 1, glyphBox.Top + 1, 14, 14);
                        }
                        using (var core = new SolidBrush(Color.FromArgb((int)(120 + 135 * pulse), _accent)))
                        {
                            g.FillEllipse(core, glyphBox.Left + 5, glyphBox.Top + 5, 6, 6);
                        }
                    }
                    else
                    {
                        using (var ring = new Pen(_trackColor, 2f))
                        {
                            g.DrawEllipse(ring, glyphBox.Left + 1, glyphBox.Top + 1, 14, 14);
                        }
                    }

                    Color textCol = done ? _titleColor : active ? _titleColor : _subColor;
                    Font f = active ? activeFont : doneFont;
                    using (var tb = new SolidBrush(textCol))
                    {
                        var sf = new StringFormat { LineAlignment = StringAlignment.Center };
                        // Translated per paint — see the note on the tagline above.
                        g.DrawString(Utils.Localization.T(LoadingSteps[i]), f, tb,
                            new RectangleF(listX + 26, rowY, Width - listX - 40, rowH), sf);

                        // What this stage actually found, right-aligned and dimmed.
                        string det = i < StepDetail.Length ? StepDetail[i] : null;
                        if (!string.IsNullOrEmpty(det))
                        {
                            using (var db = new SolidBrush(_subColor))
                            {
                                var rsf = new StringFormat
                                {
                                    LineAlignment = StringAlignment.Center,
                                    Alignment = StringAlignment.Far
                                };
                                g.DrawString(det, doneFont, db,
                                    new RectangleF(listX + 26, rowY, Width - listX - 46, rowH), rsf);
                                rsf.Dispose();
                            }
                        }
                    }
                }
            }

            // ── Determinate progress bar + percentage ───────────────────────────
            int barW = Width - 148, barH = 5;
            int barX = 74, barY = Height - 40;
            using (var track = new SolidBrush(_trackColor))
            {
                FillRoundedRect(g, track, barX, barY, barW, barH, 3);
            }
            int fillW = (int)Math.Round(barW * Math.Max(0f, Math.Min(1f, _progress)));
            if (fillW > 2)
            {
                using (var fill = new LinearGradientBrush(
                    new Rectangle(barX, barY, Math.Max(1, fillW), barH), _accent, _accent2, LinearGradientMode.Horizontal))
                {
                    FillRoundedRect(g, fill, barX, barY, fillW, barH, 3);
                }
            }
            using (var pctFont = new Font("Segoe UI", 9f, FontStyle.Bold))
            using (var pctBrush = new SolidBrush(_subColor))
            {
                string pct = (int)Math.Round(_progress * 100) + "%";
                SizeF psz = g.MeasureString(pct, pctFont);
                g.DrawString(pct, pctFont, pctBrush, Width - 74 + 12, barY - psz.Height / 2f + barH / 2f);
            }

            // Rounded border to match the rounded form region.
            using (var border = new Pen(_borderColor))
            using (var bpath = RoundedPath(0, 0, Width - 1, Height - 1, 14))
            {
                g.DrawPath(border, bpath);
            }
        }

        /// <summary>
        /// Loads the saved theme's colours (accent, background, text) so the splash
        /// matches the app the user will see. Best-effort: any failure keeps the brand
        /// violet/blue defaults. Read directly (a tiny JSON peek) because the splash runs
        /// on its own thread before the main window has loaded settings.
        /// </summary>
        private void LoadThemeColors()
        {
            try
            {
                string path = Persistence.SettingsManager.GetSettingsPath();
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return;
                }
                using (var doc = JsonDocument.Parse(File.ReadAllText(path)))
                {
                    var root = doc.RootElement;
                    int themeInt = root.TryGetProperty("Theme", out var tv) && tv.ValueKind == JsonValueKind.Number
                        ? tv.GetInt32() : 0;
                    Theme th = Theme.ForKind((Models.ThemeKind)themeInt);
                    Color accent = th.Accent;
                    Color accent2 = th.AccentHover;
                    if (root.TryGetProperty("CustomAccentEnabled", out var ce) && ce.ValueKind == JsonValueKind.True &&
                        root.TryGetProperty("CustomAccentArgb", out var ca) && ca.ValueKind == JsonValueKind.Number)
                    {
                        accent = Color.FromArgb(ca.GetInt32());
                        accent2 = Lighten(accent, 0.16);
                    }
                    _bg = th.Surface;
                    _accent = accent;
                    _accent2 = accent2;
                    _titleColor = th.Text;
                    _subColor = th.TextMuted;
                    _trackColor = th.Surface2;
                    _borderColor = th.Border;
                }
            }
            catch { /* keep brand defaults */ }
        }

        private static Color Lighten(Color c, double amt)
        {
            return Color.FromArgb(c.A,
                (int)(c.R + (255 - c.R) * amt),
                (int)(c.G + (255 - c.G) * amt),
                (int)(c.B + (255 - c.B) * amt));
        }

        /// <summary>Draws the Tempo lightning bolt (the 24x24 brand glyph) into a box.</summary>
        private static void DrawBolt(Graphics g, RectangleF box, Color color)
        {
            PointF[] src =
            {
                new PointF(13, 2), new PointF(4, 14), new PointF(10, 14),
                new PointF(9, 22), new PointF(18, 10), new PointF(12, 10)
            };
            float sx = box.Width / 24f, sy = box.Height / 24f;
            var pts = new PointF[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                pts[i] = new PointF(box.X + src[i].X * sx, box.Y + src[i].Y * sy);
            }
            using (var br = new SolidBrush(color))
            {
                g.FillPolygon(br, pts);
            }
        }

        private static GraphicsPath RoundedPath(int x, int y, int w, int h, int r)
        {
            var path = new GraphicsPath();
            int d = Math.Min(2 * r, Math.Min(w, h));
            if (d <= 0)
            {
                path.AddRectangle(new Rectangle(x, y, w, h));
                return path;
            }
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + w - d, y, d, d, 270, 90);
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            path.AddArc(x, y + h - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void FillRoundedRect(Graphics g, Brush brush, int x, int y, int w, int h, int r)
        {
            if (w <= 0 || h <= 0) return;
            int d = Math.Min(2 * r, Math.Min(w, h));
            using (var path = new GraphicsPath())
            {
                path.AddArc(x, y, d, d, 180, 90);
                path.AddArc(x + w - d, y, d, d, 270, 90);
                path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
                path.AddArc(x, y + h - d, d, d, 90, 90);
                path.CloseFigure();
                g.FillPath(brush, path);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
