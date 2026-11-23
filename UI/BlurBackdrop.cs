using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A frosted copy of the window lying under a card that asks for a moment's attention — the startup safety note
    /// (2026-11-12, the user: "fix quick note it need center screen and background blur"). It shows what the window showed
    /// there, blurred and veiled with the theme's background colour, and takes the clicks and the wheel meant for what is
    /// under it: nothing hidden behind the frost reacts while the card is up. It covers the window below the header, so
    /// the title bar stays live — the window can still be moved, maximised, minimised and closed.
    ///
    /// The picture is made once from a copy (<see cref="Frost"/>, a few milliseconds): the window, shrunk to a quarter
    /// by averaging, box-blurred three times (close to a Gaussian), veiled, and drawn back at full size smoothly. Nothing
    /// here repaints the window under it — the copy is a still; the host takes a fresh one when what is under it changes
    /// (a resize, a theme, a tab).
    /// </summary>
    public sealed class BlurBackdrop : Control
    {
        private Bitmap _picture;
        private Color _veil = Color.Black;

        public BlurBackdrop()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.Opaque | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Name = "noteBackdrop";
            AccessibleRole = AccessibleRole.Pane;
            AccessibleName = "";
            Cursor = Cursors.Default;
        }

        /// <summary>The colour shown where there is no picture yet (and around a stretched one).</summary>
        public Color Veil
        {
            get => _veil;
            set { _veil = value; Invalidate(); }
        }

        /// <summary>Whether a frosted picture is shown (false: only the veil colour).</summary>
        public bool HasPicture => _picture != null;

        /// <summary>The picture's own size (it is stretched to the backdrop when they differ, until a fresh copy comes).</summary>
        public Size PictureSize => _picture?.Size ?? Size.Empty;

        /// <summary>Takes <paramref name="frosted"/> (owned from now on) as the picture, replacing the last one.</summary>
        public void SetPicture(Bitmap frosted)
        {
            Bitmap old = _picture;
            _picture = frosted;
            old?.Dispose();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (_picture == null)
            {
                using (var b = new SolidBrush(_veil)) { g.FillRectangle(b, ClientRectangle); }
                return;
            }
            if (_picture.Width == Width && _picture.Height == Height)
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImageUnscaled(_picture, 0, 0);
                return;
            }
            // A resize before the fresh copy: the frost stretched (it has no detail to lose).
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            using (var wrap = new ImageAttributes())
            {
                wrap.SetWrapMode(WrapMode.TileFlipXY);   // no dark seam at the edges
                g.DrawImage(_picture, new Rectangle(0, 0, Width, Height), 0, 0, _picture.Width, _picture.Height, GraphicsUnit.Pixel, wrap);
            }
        }

        private const int WM_MOUSEWHEEL = 0x020A, WM_MOUSEHWHEEL = 0x020E;

        protected override void WndProc(ref Message m)
        {
            // The wheel scrolls nothing hidden under the frost (nor is it handed up to the window, which would).
            if (m.Msg == WM_MOUSEWHEEL || m.Msg == WM_MOUSEHWHEEL)
            {
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _picture?.Dispose();
                _picture = null;
            }
            base.Dispose(disposing);
        }

        // ── The frost ─────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// <paramref name="shot"/> frosted: shrunk by <paramref name="shrink"/> (each output pixel the average of a block),
        /// box-blurred three times with <paramref name="radius"/> (in shrunk pixels), mixed with <paramref name="veil"/> at
        /// <paramref name="veilAmount"/> (0–1), and drawn back at <paramref name="shot"/>'s size with smooth interpolation.
        /// A new opaque bitmap; <paramref name="shot"/> is left as it is.
        /// </summary>
        public static Bitmap Frost(Bitmap shot, Color veil, double veilAmount, int shrink = 4, int radius = 3)
        {
            if (shot == null) { throw new ArgumentNullException(nameof(shot)); }
            int w = shot.Width, h = shot.Height;
            shrink = Math.Max(1, shrink);
            int sw = Math.Max(1, (w + shrink - 1) / shrink), sh = Math.Max(1, (h + shrink - 1) / shrink);

            // The shot's pixels.
            var whole = new int[w * h];
            BitmapData src = shot.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < h; y++) { Marshal.Copy(src.Scan0 + y * src.Stride, whole, y * w, w); }
            }
            finally { shot.UnlockBits(src); }

            // Shrunk, each pixel the average of its block (no aliasing, unlike a plain resample).
            var r = new float[sw * sh];
            var gr = new float[sw * sh];
            var b = new float[sw * sh];
            for (int sy = 0; sy < sh; sy++)
            {
                int y0 = sy * shrink, y1 = Math.Min(h, y0 + shrink);
                for (int sx = 0; sx < sw; sx++)
                {
                    int x0 = sx * shrink, x1 = Math.Min(w, x0 + shrink);
                    float tr = 0, tg = 0, tb = 0;
                    int n = 0;
                    for (int y = y0; y < y1; y++)
                    {
                        int row = y * w;
                        for (int x = x0; x < x1; x++)
                        {
                            int c = whole[row + x];
                            tr += (c >> 16) & 0xFF;
                            tg += (c >> 8) & 0xFF;
                            tb += c & 0xFF;
                            n++;
                        }
                    }
                    int i = sy * sw + sx;
                    if (n > 0) { r[i] = tr / n; gr[i] = tg / n; b[i] = tb / n; }
                }
            }

            // Three box blurs each way: close to a Gaussian.
            var tmp = new float[sw * sh];
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (float[] ch in new[] { r, gr, b })
                {
                    BoxRows(ch, tmp, sw, sh, radius);
                    BoxColumns(tmp, ch, sw, sh, radius);
                }
            }

            // Veiled, into a small bitmap.
            double keep = 1.0 - Math.Max(0, Math.Min(1, veilAmount));
            double vr = veil.R * (1 - keep), vg = veil.G * (1 - keep), vb = veil.B * (1 - keep);
            var small = new Bitmap(sw, sh, PixelFormat.Format32bppArgb);
            var px = new int[sw * sh];
            for (int i = 0; i < px.Length; i++)
            {
                int cr = Clamp(r[i] * keep + vr), cg = Clamp(gr[i] * keep + vg), cb = Clamp(b[i] * keep + vb);
                px[i] = unchecked((int)0xFF000000) | (cr << 16) | (cg << 8) | cb;
            }
            BitmapData dst = small.LockBits(new Rectangle(0, 0, sw, sh), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < sh; y++) { Marshal.Copy(px, y * sw, dst.Scan0 + y * dst.Stride, sw); }
            }
            finally { small.UnlockBits(dst); }

            // Back to full size, smoothly.
            var frosted = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (small)
            using (Graphics g = Graphics.FromImage(frosted))
            using (var wrap = new ImageAttributes())
            {
                wrap.SetWrapMode(WrapMode.TileFlipXY);
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(small, new Rectangle(0, 0, w, h), 0, 0, sw, sh, GraphicsUnit.Pixel, wrap);
            }
            return frosted;
        }

        private static int Clamp(double v) => v <= 0 ? 0 : v >= 255 ? 255 : (int)(v + 0.5);

        /// <summary>A box blur along each row, edges held (the edge pixel repeats).</summary>
        private static void BoxRows(float[] from, float[] to, int w, int h, int radius)
        {
            float scale = 1f / (radius * 2 + 1);
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                float sum = 0;
                for (int k = -radius; k <= radius; k++) { sum += from[row + Math.Max(0, Math.Min(w - 1, k))]; }
                for (int x = 0; x < w; x++)
                {
                    to[row + x] = sum * scale;
                    int add = Math.Min(w - 1, x + radius + 1), drop = Math.Max(0, x - radius);
                    sum += from[row + add] - from[row + drop];
                }
            }
        }

        /// <summary>A box blur down each column, edges held.</summary>
        private static void BoxColumns(float[] from, float[] to, int w, int h, int radius)
        {
            float scale = 1f / (radius * 2 + 1);
            for (int x = 0; x < w; x++)
            {
                float sum = 0;
                for (int k = -radius; k <= radius; k++) { sum += from[Math.Max(0, Math.Min(h - 1, k)) * w + x]; }
                for (int y = 0; y < h; y++)
                {
                    to[y * w + x] = sum * scale;
                    int add = Math.Min(h - 1, y + radius + 1), drop = Math.Max(0, y - radius);
                    sum += from[add * w + x] - from[drop * w + x];
                }
            }
        }

        /// <summary>
        /// What <paramref name="window"/> shows now in <paramref name="area"/> (its client coordinates), copied off its own
        /// surface — not the screen, so a window lying over Tempo is not copied. Null when it can't be read.
        /// </summary>
        public static Bitmap CopyOf(Control window, Rectangle area)
        {
            if (window == null || !window.IsHandleCreated || area.Width <= 0 || area.Height <= 0) { return null; }
            IntPtr dc = IntPtr.Zero;
            try
            {
                dc = GetDC(window.Handle);
                if (dc == IntPtr.Zero) { return null; }
                using (var dib = new GdiDib(area.Width, area.Height))
                {
                    if (!dib.CopyFrom(dc, area.X, area.Y)) { return null; }
                    dib.MakeOpaque();
                    using (Bitmap view = dib.AsBitmap())
                    {
                        var copy = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
                        using (Graphics g = Graphics.FromImage(copy))
                        {
                            g.CompositingMode = CompositingMode.SourceCopy;
                            g.DrawImageUnscaled(view, 0, 0);
                        }
                        return copy;
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.Logger.Swallow("BlurBackdrop.CopyOf", ex);
                return null;
            }
            finally
            {
                if (dc != IntPtr.Zero) { ReleaseDC(window.Handle, dc); }
            }
        }

        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    }
}
