using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// Gives a drop-down menu an entrance: it fades up while sliding a few pixels
    /// toward the cursor, instead of snapping into existence fully drawn.
    ///
    /// WinForms' ToolStripDropDown has no entrance animation of its own — it is a
    /// plain window that is simply shown — so Tempo's tray menu appeared instantly
    /// while every other surface in the app (the notification cards, the window
    /// itself) animates. This closes that gap.
    ///
    /// Like the notification cards, the motion is driven by ELAPSED TIME rather than
    /// by counting frames, so it always takes the same 130 ms and follows the same
    /// curve even when frames arrive late — which they will, because this runs on a
    /// WinForms timer whose WM_TIMER is the lowest-priority message Windows delivers.
    ///
    /// The fade uses a layered window, which the compositor applies for free: no part
    /// of the menu is repainted to fade it. The slide is a bare SetWindowPos on the
    /// drop-down's own handle, so WinForms' idea of the menu's bounds is untouched and
    /// item hit-testing (which is in client coordinates) stays correct throughout.
    /// </summary>
    internal static class MenuOpenAnimation
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x00080000;
        private const uint LWA_ALPHA = 0x2;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(
            IntPtr h, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(
            IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        /// <summary>Total entrance time. Short enough to never feel in the way.</summary>
        private const int DurationMs = 130;
        /// <summary>How far the menu travels, in pixels, as it fades up.</summary>
        private const int SlidePx = 7;

        /// <summary>
        /// Attaches the entrance to <paramref name="menu"/>. Safe to call once per menu;
        /// every failure path leaves the menu fully visible and un-animated rather than
        /// half-transparent, because a menu you cannot see is far worse than one that
        /// does not animate.
        /// </summary>
        public static void Attach(ToolStripDropDown menu)
        {
            if (menu == null) { return; }

            Timer timer = null;
            long startTick = 0;
            int finalY = 0;
            bool running = false;

            void Finish(IntPtr handle)
            {
                running = false;
                try { timer?.Stop(); } catch { }
                try
                {
                    if (handle != IntPtr.Zero)
                    {
                        SetLayeredWindowAttributes(handle, 0, 255, LWA_ALPHA);
                        SetWindowPos(handle, IntPtr.Zero, menu.Left, finalY, 0, 0,
                                     SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                    }
                }
                catch { }
            }

            menu.Opened += (s, e) =>
            {
                try
                {
                    if (!menu.IsHandleCreated) { return; }
                    IntPtr h = menu.Handle;

                    finalY = menu.Top;
                    // Drop-downs above the cursor (the usual case for a tray menu, which
                    // opens upward from the taskbar) should rise INTO place; ones below
                    // should settle downward. Either way it ends at finalY.
                    bool opensUpward = menu.Top < Cursor.Position.Y;
                    int fromY = finalY + (opensUpward ? SlidePx : -SlidePx);

                    int ex = GetWindowLong(h, GWL_EXSTYLE);
                    if ((ex & WS_EX_LAYERED) == 0)
                    {
                        SetWindowLong(h, GWL_EXSTYLE, ex | WS_EX_LAYERED);
                    }
                    SetLayeredWindowAttributes(h, 0, 0, LWA_ALPHA);
                    SetWindowPos(h, IntPtr.Zero, menu.Left, fromY, 0, 0,
                                 SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

                    startTick = Environment.TickCount64;
                    running = true;

                    if (timer == null)
                    {
                        timer = new Timer { Interval = 15 };
                        timer.Tick += (ts, te) =>
                        {
                            if (!running) { try { timer.Stop(); } catch { } return; }
                            try
                            {
                                if (menu.IsDisposed || !menu.Visible || !menu.IsHandleCreated)
                                {
                                    running = false;
                                    timer.Stop();
                                    return;
                                }
                                IntPtr hh = menu.Handle;
                                double t = (Environment.TickCount64 - startTick) / (double)DurationMs;
                                if (t >= 1) { Finish(hh); return; }

                                // Ease-out cubic: quick to appear, soft to settle.
                                double inv = 1 - t;
                                double k = 1 - inv * inv * inv;

                                byte alpha = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(255 * k)));
                                int y = (int)Math.Round(fromY + (finalY - fromY) * k);
                                SetLayeredWindowAttributes(hh, 0, alpha, LWA_ALPHA);
                                SetWindowPos(hh, IntPtr.Zero, menu.Left, y, 0, 0,
                                             SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                            }
                            catch
                            {
                                // Never leave the menu invisible because the animation
                                // failed — snap it to fully shown and stop.
                                try { Finish(menu.IsHandleCreated ? menu.Handle : IntPtr.Zero); } catch { }
                            }
                        };
                    }
                    timer.Start();
                }
                catch
                {
                    try { Finish(menu.IsHandleCreated ? menu.Handle : IntPtr.Zero); } catch { }
                }
            };

            // A menu that closes mid-entrance must not leave a part-faded window behind
            // for the next open to inherit.
            menu.Closed += (s, e) =>
            {
                running = false;
                try { timer?.Stop(); } catch { }
                try
                {
                    if (menu.IsHandleCreated)
                    {
                        SetLayeredWindowAttributes(menu.Handle, 0, 255, LWA_ALPHA);
                    }
                }
                catch { }
            };

            menu.Disposed += (s, e) =>
            {
                running = false;
                try { timer?.Stop(); timer?.Dispose(); timer = null; } catch { }
            };
        }
    }
}
