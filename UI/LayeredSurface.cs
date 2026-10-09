using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AutoClicker.UI
{
    /// <summary>
    /// A 32-bit canvas with per-pixel alpha for a layered window, pushed with UpdateLayeredWindow.
    ///
    /// The full-screen overlays (the position picker, the multi-point preview) dimmed the screen by
    /// setting the whole FORM's Opacity — 35% and 45% — so their text was drawn at 35% and 45% too:
    /// the picker's coordinate read-out and instructions, the preview's banner and point numbers were
    /// washed out over whatever was behind them, faint over anything bright. On this canvas the dim is
    /// just a translucent fill and everything drawn on top of it is as opaque as it is drawn.
    ///
    /// The DIB section is made once and drawn into in place, so an update costs a fill and a push, not
    /// a new full-screen bitmap each time (the picker redraws on every mouse move). Draw with a
    /// greyscale text hint: ClearType assumes an opaque background and fringes on a translucent one.
    /// </summary>
    internal sealed class LayeredSurface : IDisposable
    {
        private IntPtr _dib;
        private IntPtr _memDc;
        private IntPtr _oldBitmap;

        public int Width { get; }
        public int Height { get; }

        /// <summary>The canvas, premultiplied ARGB over the DIB's own memory.</summary>
        public Bitmap Bitmap { get; }

        public LayeredSurface(int width, int height)
        {
            Width = Math.Max(1, width);
            Height = Math.Max(1, height);
            var bmi = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER)),
                biWidth = Width,
                biHeight = -Height,     // top-down, so row 0 is the top as the Bitmap expects
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0       // BI_RGB
            };
            _dib = CreateDIBSection(IntPtr.Zero, ref bmi, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (_dib == IntPtr.Zero || bits == IntPtr.Zero)
            {
                throw new InvalidOperationException("CreateDIBSection failed for " + Width + "x" + Height + ".");
            }
            _memDc = CreateCompatibleDC(IntPtr.Zero);
            _oldBitmap = SelectObject(_memDc, _dib);
            Bitmap = new Bitmap(Width, Height, Width * 4, PixelFormat.Format32bppPArgb, bits);
        }

        /// <summary>Shows the canvas as <paramref name="hwnd"/>'s content, its top-left at <paramref name="screen"/>.</summary>
        public bool Push(IntPtr hwnd, Point screen)
        {
            if (hwnd == IntPtr.Zero || _memDc == IntPtr.Zero) { return false; }
            var dst = new NativePoint { X = screen.X, Y = screen.Y };
            var size = new NativeSize { Cx = Width, Cy = Height };
            var src = new NativePoint();
            var blend = new BlendFunction { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 }; // AC_SRC_OVER, AC_SRC_ALPHA
            return UpdateLayeredWindow(hwnd, IntPtr.Zero, ref dst, ref size, _memDc, ref src, 0, ref blend, 2 /* ULW_ALPHA */);
        }

        public void Dispose()
        {
            Bitmap?.Dispose();
            if (_memDc != IntPtr.Zero)
            {
                SelectObject(_memDc, _oldBitmap);
                DeleteDC(_memDc);
                _memDc = IntPtr.Zero;
            }
            if (_dib != IntPtr.Zero)
            {
                DeleteObject(_dib);
                _dib = IntPtr.Zero;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize { public int Cx; public int Cy; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BlendFunction
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER pbmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref NativePoint pptDst, ref NativeSize psize,
            IntPtr hdcSrc, ref NativePoint pptSrc, int crKey, ref BlendFunction pblend, int dwFlags);
    }
}
