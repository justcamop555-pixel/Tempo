using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// Paints ONE shared background image as a single seamless wallpaper spanning the
    /// whole window. Every surface that wants to show the backdrop (the header, the
    /// sidebar, each tab page, the footer band) calls <see cref="Paint"/> with the
    /// SAME image instance; each draws only the slice that falls behind its own bounds,
    /// computed against the form's client area. Because the cover-fit is calculated
    /// once against the whole window and every surface offsets into it by its own
    /// position, the slices line up edge-to-edge — no more separate, misaligned copies
    /// in the header vs. the page vs. the footer (the "overlap" this replaces).
    ///
    /// The image is owned and animated by ONE owner (MainForm) with a single
    /// ImageAnimator; surfaces here never dispose it and never register their own
    /// animator (multiple animators on one image make it play at multiple speeds).
    /// A shared instance also means every region shows the SAME frame, so the seam is
    /// invisible even mid-animation.
    /// </summary>
    public static class WindowBackdrop
    {
        /// <summary>Cover-fit rectangle (form-client coords) for <paramref name="img"/> over the form.</summary>
        public static Rectangle CoverRect(Size img, Size form)
        {
            if (img.Width <= 0 || img.Height <= 0 || form.Width <= 0 || form.Height <= 0)
            {
                return Rectangle.Empty;
            }
            double scale = Math.Max(form.Width / (double)img.Width, form.Height / (double)img.Height);
            int w = (int)Math.Ceiling(img.Width * scale);
            int h = (int)Math.Ceiling(img.Height * scale);
            int x = (form.Width - w) / 2;
            int y = (form.Height - h) / 2;
            return new Rectangle(x, y, w, h);
        }

        /// <summary>
        /// Fills <paramref name="surface"/> with its aligned slice of the shared image,
        /// then a readability scrim of <paramref name="baseColor"/> at
        /// <paramref name="dimPercent"/>. Returns false (drawing nothing) when there is
        /// no image or the surface isn't parented to a form yet, so callers can fall
        /// back to their normal background.
        /// </summary>
        // ── Per-surface cache of the finished slice ────────────────────────────
        //
        // Painting used to rescale the whole image at HighQualityBilinear and refill the
        // scrim on EVERY paint, for every surface. The page had been given a cached
        // composite for exactly this reason; the header, sidebar and footer band never
        // were, so they kept paying full price.
        //
        // That was the app's idle cost. A wallpaper also puts the window into
        // WS_EX_COMPOSITED, so any repaint anywhere — the 200 ms status tick is enough —
        // redraws the whole window and made all three surfaces rescale a 900x507 image
        // up to ~1850 px wide from scratch. Measured at idle, window open and nothing
        // running: ~103% of one CPU core with a wallpaper set against ~13% without it.
        // A saturated UI thread is what made dragging the window and the notification
        // animations stutter — they were queued behind this.
        //
        // The scaled slice only changes when the image, the window size, the surface's
        // position/size or the dim setting change, so it is composed once and blitted
        // afterwards. Keyed weakly off the control so nothing is kept alive.
        private sealed class Slice
        {
            public Bitmap Bmp;
            public Image Img;
            public Size FormClient;
            public Point Offset;
            public Size SurfaceSize;
            public int Dim;
            public int BaseArgb;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, Slice> Cache =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Control, Slice>();

        /// <summary>
        /// Drops every cached slice. Call when the shared image itself is swapped or its
        /// animation advances a frame, so the next paint recomposes.
        /// </summary>
        public static void InvalidateCache()
        {
            // ConditionalWeakTable has no Clear on this target, so entries are expired by
            // stamping a null image — the next paint sees the mismatch and rebuilds.
            lock (CacheLock) { CacheEpoch++; }
        }

        private static readonly object CacheLock = new object();
        private static int CacheEpoch;
        private static int _lastSeenEpoch;

        public static bool Paint(Graphics g, Control surface, Image img, int dimPercent, Color baseColor)
        {
            if (g == null || surface == null || img == null)
            {
                return false;
            }
            Form f = surface.FindForm();
            if (f == null)
            {
                return false;
            }
            Size fc = f.ClientSize;
            Rectangle cover = CoverRect(img.Size, fc);
            if (cover.IsEmpty)
            {
                return false;
            }

            // The surface's top-left in FORM-client coordinates. For a viewport-pinned
            // scrolling page this is the fixed viewport origin (PointToScreen of client
            // (0,0) doesn't move as content scrolls), so the wallpaper stays pinned.
            Point off;
            try { off = f.PointToClient(surface.PointToScreen(Point.Empty)); }
            catch { return false; }

            int w = surface.Width, h = surface.Height;
            if (w <= 0 || h <= 0)
            {
                return false;
            }

            int epoch;
            lock (CacheLock) { epoch = CacheEpoch; }
            if (epoch != _lastSeenEpoch)
            {
                _lastSeenEpoch = epoch;
                foreach (var pair in Cache) { pair.Value.Img = null; }   // force a rebuild
            }

            Slice slice = Cache.GetValue(surface, _ => new Slice());
            bool usable = slice.Bmp != null
                          && ReferenceEquals(slice.Img, img)
                          && slice.FormClient == fc
                          && slice.Offset == off
                          && slice.SurfaceSize.Width == w && slice.SurfaceSize.Height == h
                          && slice.Dim == dimPercent
                          && slice.BaseArgb == baseColor.ToArgb();

            if (!usable)
            {
                if (slice.Bmp == null || slice.Bmp.Width != w || slice.Bmp.Height != h)
                {
                    try { slice.Bmp?.Dispose(); } catch { }
                    slice.Bmp = new Bitmap(w, h);
                }

                using (Graphics bg = Graphics.FromImage(slice.Bmp))
                {
                    bg.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    bg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    try { ImageAnimator.UpdateFrames(img); } catch { }
                    bg.DrawImage(img, new Rectangle(cover.X - off.X, cover.Y - off.Y, cover.Width, cover.Height));

                    int alpha = Math.Max(0, Math.Min(95, dimPercent)) * 255 / 100;
                    using (var scrim = new SolidBrush(Color.FromArgb(alpha, baseColor)))
                    {
                        bg.FillRectangle(scrim, new Rectangle(0, 0, w, h));
                    }
                }

                slice.Img = img;
                slice.FormClient = fc;
                slice.Offset = off;
                slice.SurfaceSize = new Size(w, h);
                slice.Dim = dimPercent;
                slice.BaseArgb = baseColor.ToArgb();
            }

            // The whole point: a plain unscaled blit instead of a rescale + scrim fill.
            g.DrawImageUnscaled(slice.Bmp, 0, 0);
            return true;
        }
    }
}
