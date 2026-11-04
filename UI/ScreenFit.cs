using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// Keeps a Tempo window on the screen it opens on: never taller or wider than that screen's work
    /// area, and never hanging off its edge.
    ///
    /// THE BUG THIS FIXES: dialogs cut off on laptops. Tempo is System-DPI-aware, so a 125% or 150%
    /// laptop scales every window once, up front — and a dialog laid out at 648px tall at 96 dpi
    /// (the macro editor) is 810px at 125%, on a 1366x768 screen whose work area is 720px. Its Save
    /// button was below the bottom of the screen, where nothing can reach it, and a modal dialog nobody
    /// can close is Tempo "not responding" to the person looking at it. The macro editor, the live debug
    /// window, the bug-report chooser and the local-API dialog were all taller than that laptop's work
    /// area, and none of them — nor any other dialog — looked at the screen at all.
    ///
    /// Attached by <see cref="ThemeManager.ApplyWindowChrome"/>, which every themed window already calls,
    /// so no dialog has to remember it. A window that does not fit is made to fit and its content
    /// scrolls, so every control stays reachable; one that fits is only moved back onto the screen if it
    /// hangs off it. Borderless windows (the main window, overlays, toasts, the splash) are left alone:
    /// they place themselves.
    /// </summary>
    internal static class ScreenFit
    {
        private static readonly ConditionalWeakTable<Form, object> _attached = new ConditionalWeakTable<Form, object>();
        private static readonly object Marker = new object();

        /// <summary>For tests only: the work area to fit to instead of the real screen's (a laptop's, on a desktop).</summary>
        internal static Rectangle? WorkAreaForTest;

        /// <summary>Fits <paramref name="form"/> as it loads and again once it is shown. Idempotent.</summary>
        internal static void Attach(Form form)
        {
            if (form == null || form.IsDisposed) { return; }
            if (_attached.TryGetValue(form, out _)) { return; }
            _attached.Add(form, Marker);
            form.Load += OnLoad;
            form.Shown += OnShown;
        }

        private static void OnLoad(object sender, EventArgs e)
        {
            if (sender is Form f)
            {
                OwnByPageWindowInUse(f);
                KeepAboveOwner(f);
                Fit(f);
            }
        }

        /// <summary>
        /// A dialog asked for from a tab that is out in its own window belongs to THAT window.
        ///
        /// The page code that opens it passes the main window as the owner — about forty
        /// <c>ShowDialog(this)</c> calls, written when every page lived in the main window. With the
        /// page in a window of its own, that put the dialog over a main window that may be minimised
        /// (Windows put the dialog in the corner of the screen), in the tray, on the other monitor, or
        /// under the page window when it is kept on top — which the dialog had just disabled. Handed to
        /// the page window as it loads, the dialog opens centred over the page that asked for it.
        /// Only a dialog of the main window (or of nobody) moves: one opened by another dialog stays
        /// with that dialog.
        /// </summary>
        private static void OwnByPageWindowInUse(Form f)
        {
            try
            {
                if (!f.Modal || f is DetachedPageWindow || !f.IsHandleCreated) { return; }
                DetachedPageWindow inUse = DetachedPageWindow.InUse;
                if (inUse == null || inUse.IsDisposed || !inUse.IsHandleCreated || !inUse.Visible
                    || inUse.WindowState == FormWindowState.Minimized)
                {
                    return;
                }
                IntPtr owner = GetWindowLongPtr(f.Handle, GWL_HWNDPARENT);
                if (owner == inUse.Handle) { return; }
                if (owner != IntPtr.Zero && !(Control.FromHandle(owner) is MainForm)) { return; }

                SetWindowLongPtr(f.Handle, GWL_HWNDPARENT, inUse.Handle);
                // Form.OnLoad centred it on the old owner just before this ran.
                if (f.StartPosition == FormStartPosition.CenterParent || owner == IntPtr.Zero)
                {
                    Rectangle o = inUse.Bounds;
                    f.Location = new Point(o.Left + (o.Width - f.Width) / 2, o.Top + (o.Height - f.Height) / 2);
                }
                // Above a page window that is kept on top, not under it.
                if (inUse.TopMost && !f.TopMost) { f.TopMost = true; }
                Logger.Debug("[ui] \"" + f.Text + "\" opened over the \"" + inUse.Text + "\" window, where it was asked for.");
            }
            catch (Exception ex) { Logger.Swallow("ScreenFit.OwnByPageWindowInUse", ex); }
        }

        private const int GWL_HWNDPARENT = -8;

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);

        /// <summary>
        /// A dialog owned by a window that is kept on top is kept on top too. An owned window should sit
        /// above its owner, but WinForms hands a dialog its owner once the window exists, and a window
        /// that is not topmost sits under every window that is — so a confirmation asked from the
        /// Accounts window with "Always on top" on opened BEHIND that window, which the dialog had just
        /// disabled (measured, scratchpad/acctgate). Out of sight and in the way: the app looked frozen.
        /// </summary>
        private static void KeepAboveOwner(Form f)
        {
            try
            {
                Form owner = f.Owner;
                if (owner != null && !owner.IsDisposed && owner.TopMost && !f.TopMost) { f.TopMost = true; }
            }
            catch (Exception ex) { Logger.Swallow("ScreenFit.KeepAboveOwner", ex); }
        }

        // Again once shown: a few dialogs finish sizing themselves to their text only after Load.
        private static void OnShown(object sender, EventArgs e)
        {
            if (sender is Form f)
            {
                Fit(f);
                // Every Tempo window passes through here — the ones themed by hand, which never meet ThemeManager,
                // too — so this is where they all get Tempo's own scroll bars (ScrollSkins).
                ScrollSkins.ApplyTree(f);
            }
        }

        /// <summary>
        /// Makes <paramref name="f"/> fit the work area of the screen it is on (or its owner's) and puts
        /// it wholly on that screen. True when it had to be made smaller.
        /// </summary>
        internal static bool Fit(Form f)
        {
            try
            {
                if (f == null || f.IsDisposed || !f.TopLevel) { return false; }
                if (f.FormBorderStyle == FormBorderStyle.None) { return false; }
                if (f.WindowState != FormWindowState.Normal) { return false; }

                Rectangle wa = WorkAreaFor(f);
                if (wa.Width <= 0 || wa.Height <= 0) { return false; }

                bool shrunk = false;
                Size was = f.Size;
                if (was.Width > wa.Width || was.Height > wa.Height)
                {
                    int w = Math.Min(was.Width, wa.Width);
                    int h = Math.Min(was.Height, wa.Height);
                    // A minimum bigger than the screen would hold it there: let it go as small as the screen.
                    Size min = f.MinimumSize;
                    if (min.Width > w || min.Height > h)
                    {
                        f.MinimumSize = new Size(Math.Min(min.Width, w), Math.Min(min.Height, h));
                    }
                    // Whatever no longer fits scrolls, so the buttons at the bottom can still be reached.
                    f.AutoScroll = true;
                    f.Size = new Size(w, h);
                    shrunk = true;
                    Logger.Info("[ui] \"" + f.Text + "\" was " + was.Width + "x" + was.Height + " on a " + wa.Width + "x"
                                + wa.Height + " work area — made to fit; what does not fit scrolls.");
                }

                // Wholly on that screen: a dialog centred over a Tempo window near the edge of a small screen
                // hung off it.
                int left = Math.Max(wa.Left, Math.Min(f.Left, wa.Right - f.Width));
                int top = Math.Max(wa.Top, Math.Min(f.Top, wa.Bottom - f.Height));
                if (left != f.Left || top != f.Top)
                {
                    f.Location = new Point(left, top);
                }
                return shrunk;
            }
            catch (Exception ex)
            {
                Logger.Swallow("ScreenFit", ex);
                return false;
            }
        }

        /// <summary>
        /// The work area the window is meant for: the screen most of it is on, or — before it has been
        /// placed anywhere real — the screen its owner is on.
        /// </summary>
        private static Rectangle WorkAreaFor(Form f)
        {
            if (WorkAreaForTest.HasValue) { return WorkAreaForTest.Value; }
            Rectangle b = f.Bounds;
            foreach (Screen s in Screen.AllScreens)
            {
                if (s.Bounds.IntersectsWith(b)) { return Screen.FromRectangle(b).WorkingArea; }
            }
            Form owner = f.Owner;
            if (owner != null && !owner.IsDisposed) { return Screen.FromControl(owner).WorkingArea; }
            return Screen.FromRectangle(b).WorkingArea;
        }
    }
}
