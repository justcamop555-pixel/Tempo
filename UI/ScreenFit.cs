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
                KeepAboveOwner(f);
                Fit(f);
            }
        }

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
            if (sender is Form f) { Fit(f); }
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
