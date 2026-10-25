using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A still picture of an area of a window, held over it while the area repaints underneath, so the change
    /// reaches the screen in one piece.
    ///
    /// A tab switch repaints the page control by control, and the screen shows the window as it is at every
    /// refresh: until a control has painted, its rectangle still holds the OLD page's pixels. Recorded off the
    /// screen (scratchpad/tabframes), each switch passed through 2–11 half-drawn pictures — the Clicker tab's
    /// buttons and first card over the Macros page, stripes of new background through the old cards.
    /// Painting the tab area into one buffer (WS_EX_COMPOSITED) removed almost all of them but cannot draw a
    /// list view: every list view, even a plain one, comes out BLACK inside a composited window
    /// (scratchpad/lvprobe), and Multi-Point's points list was a black box for a frame.
    ///
    /// This needs nothing from the controls. The picture is copied from the window's own surface (not the
    /// screen, so a window over Tempo is not copied) the moment before the switch, shown in a layered,
    /// click-through, never-activated window sitting directly above Tempo, and taken away once the new page
    /// has painted completely. The picture is exactly what was already on screen, so putting it up changes
    /// nothing; taking it down shows the finished page.
    /// </summary>
    internal sealed class SwitchCurtain : NativeWindow, IDisposable
    {
        private bool _up;

        /// <summary>Whether the picture is up.</summary>
        public bool IsUp => _up;

        /// <summary>
        /// Puts up a picture of what <paramref name="area"/> shows now, over it. False (and nothing shown) when
        /// it cannot — no window yet, nothing on screen, the copy failed.
        /// </summary>
        public bool Cover(Control area, Form owner)
        {
            if (_up) { Lift(); }
            if (area == null || owner == null || !area.IsHandleCreated || !owner.IsHandleCreated) { return false; }
            if (!owner.Visible || owner.WindowState == FormWindowState.Minimized || owner.Opacity <= 0.0) { return false; }

            Rectangle r = area.RectangleToScreen(area.ClientRectangle);
            if (r.Width <= 0 || r.Height <= 0) { return false; }

            if (Handle == IntPtr.Zero)
            {
                var cp = new CreateParams
                {
                    Caption = string.Empty,
                    Style = WS_POPUP,
                    // Layered (its picture is given whole, never painted a piece at a time), click-through,
                    // never activated, never in Alt-Tab or on the taskbar.
                    ExStyle = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW,
                    // Owned by Tempo's window, so it stays with it in the stacking order.
                    Parent = owner.Handle,
                    X = r.X, Y = r.Y, Width = r.Width, Height = r.Height
                };
                CreateHandle(cp);
                // No fade on show or hide: it has to be there, and gone, in the very next frame.
                try { int on = 1; DwmSetWindowAttribute(Handle, DWMWA_TRANSITIONS_FORCEDISABLED, ref on, sizeof(int)); } catch { }
            }

            IntPtr screenDc = IntPtr.Zero, areaDc = IntPtr.Zero, memDc = IntPtr.Zero, bmp = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                screenDc = GetDC(IntPtr.Zero);
                areaDc = GetDC(area.Handle);
                memDc = CreateCompatibleDC(screenDc);
                bmp = CreateCompatibleBitmap(screenDc, r.Width, r.Height);
                if (screenDc == IntPtr.Zero || areaDc == IntPtr.Zero || memDc == IntPtr.Zero || bmp == IntPtr.Zero) { return false; }
                old = SelectObject(memDc, bmp);
                // What is on screen there now. From the window's own surface when it is opaque — whatever sits
                // over Tempo on the screen is not copied. A see-through window (Settings › window opacity) is
                // copied off the SCREEN, already blended with what is behind it: a see-through picture over a
                // see-through window let the page being repainted underneath show through it (recorded at 85%,
                // 58 half-drawn frames), and blended the old page twice.
                bool seeThrough = owner.Opacity < 0.999;
                bool copied = seeThrough
                    ? BitBlt(memDc, 0, 0, r.Width, r.Height, screenDc, r.X, r.Y, SRCCOPY)
                    : BitBlt(memDc, 0, 0, r.Width, r.Height, areaDc, 0, 0, SRCCOPY);
                if (!copied) { return false; }

                var pos = new POINT { x = r.X, y = r.Y };
                var size = new SIZE { cx = r.Width, cy = r.Height };
                var src = new POINT { x = 0, y = 0 };
                // Opaque: it must hide the repaint under it completely.
                var blend = new BLENDFUNCTION
                {
                    BlendOp = 0,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = 0   // the copy has no alpha of its own
                };
                if (!UpdateLayeredWindow(Handle, screenDc, ref pos, ref size, memDc, ref src, 0, ref blend, ULW_ALPHA)) { return false; }

                SetWindowPos(Handle, PlaceAbove(owner.Handle), r.X, r.Y, r.Width, r.Height,
                             SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_NOOWNERZORDER);
                _up = true;
                StartSafety();
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (old != IntPtr.Zero) { SelectObject(memDc, old); }
                if (bmp != IntPtr.Zero) { DeleteObject(bmp); }
                if (memDc != IntPtr.Zero) { DeleteDC(memDc); }
                if (areaDc != IntPtr.Zero) { ReleaseDC(area.Handle, areaDc); }
                if (screenDc != IntPtr.Zero) { ReleaseDC(IntPtr.Zero, screenDc); }
            }
        }

        /// <summary>
        /// What to put the picture after so it sits DIRECTLY above <paramref name="owner"/>: not on top of
        /// everything, where it would cover another program's window (or a dialog of Tempo's) lying over the tabs.
        /// </summary>
        private IntPtr PlaceAbove(IntPtr owner)
        {
            IntPtr above = GetWindow(owner, GW_HWNDPREV);
            if (above == Handle) { above = GetWindow(above, GW_HWNDPREV); }   // our own, hidden, from last time
            if (IsTopmost(owner))
            {
                // Tempo kept on top: among the always-on-top windows, directly above it.
                return above == IntPtr.Zero ? HWND_TOPMOST : above;
            }
            // An ordinary window, and never always-on-top itself — it may be left so from a time Tempo was
            // kept on top. Nothing ordinary above Tempo: the top of the ordinary windows is directly above it.
            if (above == IntPtr.Zero || IsTopmost(above)) { return HWND_NOTOPMOST; }
            return above;
        }

        private static bool IsTopmost(IntPtr hwnd) => (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;

        /// <summary>
        /// Takes the picture away. Safe to call at any time, as often as wanted.
        ///
        /// Decided by what is ON SCREEN, not by <see cref="_up"/> alone. The picture is a window of its own and
        /// does not follow Tempo's: one left showing while the flag said "down" could never be taken away again,
        /// and stayed behind as a stale copy of an old page — cut along its edges, with the real page offset
        /// beside it once the window had moved or resized (a friend's screenshots, 1.0.325).
        /// </summary>
        public void Lift()
        {
            _up = false;
            _safety?.Stop();
            try
            {
                if (Handle != IntPtr.Zero && IsWindowVisible(Handle))
                {
                    if (!SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
                                      SWP_NOACTIVATE | SWP_HIDEWINDOW | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOOWNERZORDER))
                    {
                        ShowWindow(Handle, SW_HIDE);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// A switch paints and takes the picture down in 20–100 ms (a first visit before the warm-up, up to about
        /// 1.4 s). Past that, something went wrong — take it down whatever the switch is doing.
        /// </summary>
        private System.Windows.Forms.Timer _safety;

        private void StartSafety()
        {
            if (_safety == null)
            {
                _safety = new System.Windows.Forms.Timer { Interval = 2000 };
                _safety.Tick += (s, e) => Lift();
            }
            _safety.Stop();
            _safety.Start();
        }

        protected override void WndProc(ref Message m)
        {
            // Owned by Tempo's window, the picture is hidden WITH it when Tempo is minimised — and Windows shows
            // owned pop-ups it hid that way again on restore (SW_PARENTOPENING). A picture up at that moment came
            // back on restore: a frozen copy of the old page, floating where the window used to be. Shown only
            // by Cover().
            if (m.Msg == WM_SHOWWINDOW && m.WParam != IntPtr.Zero && (int)(long)m.LParam == SW_PARENTOPENING && !_up)
            {
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            Lift();
            try { _safety?.Dispose(); } catch { }
            _safety = null;
            try { if (Handle != IntPtr.Zero) { DestroyHandle(); } } catch { }
        }

        // ── Win32 ──────────────────────────────────────────────────────────────────────────────────────
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_EX_LAYERED = 0x00080000, WS_EX_TRANSPARENT = 0x00000020,
                          WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080;
        private const int SRCCOPY = 0x00CC0020;
        private const int ULW_ALPHA = 0x00000002;
        private const uint GW_HWNDPREV = 3;
        private const int GWL_EXSTYLE = -20, WS_EX_TOPMOST = 0x00000008;
        private const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010,
                           SWP_SHOWWINDOW = 0x0040, SWP_HIDEWINDOW = 0x0080, SWP_NOOWNERZORDER = 0x0200;

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr ho);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hdc, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, int rop);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
                                                       IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_HIDE = 0;
        private const int WM_SHOWWINDOW = 0x0018, SW_PARENTOPENING = 3;
    }
}
