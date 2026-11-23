using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AutoClicker.UI
{
    /// <summary>
    /// A 32-bit top-down picture in memory with a GDI device context selected into it — what a layered window is given
    /// to show (UpdateLayeredWindow), what BitBlt and AlphaBlend draw into, and (through <see cref="AsBitmap"/>) what
    /// GDI+ can draw on, premultiplied. For the animations' pictures: the tab switch's, the window's.
    /// </summary>
    internal sealed class GdiDib : IDisposable
    {
        public int Width { get; }
        public int Height { get; }
        public IntPtr Dc { get; private set; }
        public IntPtr Bits { get; private set; }
        private IntPtr _dib, _old;

        public GdiDib(int width, int height)
        {
            Width = Math.Max(1, width);
            Height = Math.Max(1, height);
            var bmi = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER)),
                biWidth = Width,
                biHeight = -Height,   // top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            };
            _dib = CreateDIBSection(IntPtr.Zero, ref bmi, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (_dib == IntPtr.Zero || bits == IntPtr.Zero)
            {
                throw new InvalidOperationException("CreateDIBSection failed for " + Width + "x" + Height + ".");
            }
            Bits = bits;
            Dc = CreateCompatibleDC(IntPtr.Zero);
            _old = SelectObject(Dc, _dib);
        }

        /// <summary>A GDI+ view of the same memory (premultiplied ARGB). Dispose the view; the memory stays.</summary>
        public Bitmap AsBitmap() => new Bitmap(Width, Height, Width * 4, PixelFormat.Format32bppPArgb, Bits);

        /// <summary>Every pixel's alpha set to 255 — what a copy off a window's surface needs before it is shown with per-pixel alpha.</summary>
        public void MakeOpaque()
        {
            var row = new int[Width];
            for (int y = 0; y < Height; y++)
            {
                IntPtr at = Bits + y * Width * 4;
                Marshal.Copy(at, row, 0, Width);
                for (int x = 0; x < row.Length; x++) { row[x] |= unchecked((int)0xFF000000); }
                Marshal.Copy(row, 0, at, Width);
            }
        }

        /// <summary>All transparent black.</summary>
        public void Clear()
        {
            using (Bitmap b = AsBitmap())
            using (Graphics g = Graphics.FromImage(b))
            {
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.Clear(Color.Transparent);
            }
        }

        public bool CopyFrom(IntPtr srcDc, int x, int y) => BitBlt(Dc, 0, 0, Width, Height, srcDc, x, y, SRCCOPY);

        /// <summary>
        /// Shows this picture as <paramref name="hwnd"/>'s content at <paramref name="screen"/>, <paramref name="alpha"/>
        /// (0..255) over all of it. <paramref name="perPixelAlpha"/>: the picture's own alpha counts (premultiplied).
        /// </summary>
        public bool Push(IntPtr hwnd, Point screen, byte alpha, bool perPixelAlpha)
        {
            if (hwnd == IntPtr.Zero || Dc == IntPtr.Zero) { return false; }
            var dst = new POINT { x = screen.X, y = screen.Y };
            var size = new SIZE { cx = Width, cy = Height };
            var src = new POINT();
            var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = alpha, AlphaFormat = (byte)(perPixelAlpha ? 1 : 0) };
            return UpdateLayeredWindow(hwnd, IntPtr.Zero, ref dst, ref size, Dc, ref src, 0, ref blend, ULW_ALPHA);
        }

        /// <summary>
        /// Moves a layered window that already shows a picture (pushed once with <see cref="Push"/>) and changes its
        /// overall alpha — without handing the picture over again. No pixels are copied: the compositor keeps the
        /// surface and only places and blends it, so a frame costs next to nothing however big the window is, and does
        /// not wait on anything another thread is drawing.
        /// </summary>
        public static bool Move(IntPtr hwnd, Point screen, byte alpha)
        {
            if (hwnd == IntPtr.Zero) { return false; }
            var dst = new POINT { x = screen.X, y = screen.Y };
            var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = alpha, AlphaFormat = 1 };
            return UpdateLayeredWindowMove(hwnd, IntPtr.Zero, ref dst, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref blend, ULW_ALPHA);
        }

        public void Dispose()
        {
            if (Dc != IntPtr.Zero)
            {
                SelectObject(Dc, _old);
                DeleteDC(Dc);
                Dc = IntPtr.Zero;
            }
            if (_dib != IntPtr.Zero)
            {
                DeleteObject(_dib);
                _dib = IntPtr.Zero;
            }
            Bits = IntPtr.Zero;
        }

        // ── Win32 ────────────────────────────────────────────────────────────────────────────────────

        internal const int SRCCOPY = 0x00CC0020;
        private const int ULW_ALPHA = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] internal struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER pbmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] internal static extern bool BitBlt(IntPtr hdc, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, int rop);
        [DllImport("msimg32.dll")]
        internal static extern bool AlphaBlend(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
                                               IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, BLENDFUNCTION blend);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
                                                       IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        // The same call with no source and no size: "If the shape and visual context of the window are not changing,
        // hdcSrc can be NULL" (and then pptSrc and psize must be NULL too).
        [DllImport("user32.dll", SetLastError = true, EntryPoint = "UpdateLayeredWindow")]
        private static extern bool UpdateLayeredWindowMove(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, IntPtr psize,
                                                           IntPtr hdcSrc, IntPtr pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    }
}
