using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// The keyboard's way to scroll a page (round 44: "fix scroll up/down and left/right that doesnt move while i move
    /// up/down and left/right … i see outline and my real mouse dont touch it"). A click on a sidebar row leaves the focus
    /// on that row, and a click on a page takes it from there only onto a box or a button, so the arrow keys went to the
    /// sidebar. There WinForms moves the focus to the next control on an arrow key: the next row lit up with its focus
    /// outline, and the page never moved.
    ///
    /// Now, when the focus came by a click, the arrow keys scroll the open page (Left and Right sideways where it scrolls
    /// that way), and so do Page Up/Down and Home/End wherever the focus is not using them. Text boxes, lists, combo boxes,
    /// spinners and sliders keep all of these keys (they take them before the form sees them). Radio buttons keep the arrow
    /// keys. After Tab, when someone is moving through the window by keyboard, the arrow keys go on stepping between rows
    /// and buttons as before (<see cref="RoundedButton.NavKeyboardCue"/>).
    /// </summary>
    internal static class KeyScroll
    {
        /// <summary>The control with the keyboard focus in <paramref name="form"/>, through nested containers.</summary>
        internal static Control FocusedIn(ContainerControl form)
        {
            Control c = form?.ActiveControl;
            for (int guard = 0; guard < 20 && c is ContainerControl cc && cc.ActiveControl != null && !ReferenceEquals(cc.ActiveControl, c); guard++)
            {
                c = cc.ActiveControl;
            }
            return c;
        }

        /// <summary>
        /// Scrolls <paramref name="page"/>, or the part of it that scrolls by itself and holds <paramref name="focus"/>, as
        /// <paramref name="keyData"/> asks. Called from a form's ProcessDialogKey, so the only keys that arrive are ones the
        /// focused control does not take itself. True when the key is used up: the page moved, or it is one of these keys
        /// with nothing to scroll that way. An arrow key let through would move the focus to a row nobody pointed at.
        /// </summary>
        internal static bool TryScroll(Keys keyData, Control focus, ScrollableControl page)
        {
            if ((keyData & (Keys.Control | Keys.Alt | Keys.Shift)) != Keys.None) { return false; }
            Keys key = keyData & Keys.KeyCode;
            bool arrow = key == Keys.Up || key == Keys.Down || key == Keys.Left || key == Keys.Right;
            bool paging = key == Keys.PageUp || key == Keys.PageDown || key == Keys.Home || key == Keys.End;
            if (!arrow && !paging) { return false; }
            if (arrow && (RoundedButton.NavKeyboardCue.Active || focus is RadioButton)) { return false; }
            if (page == null || page.IsDisposed || !page.IsHandleCreated || !page.Visible) { return false; }

            bool sideways = key == Keys.Left || key == Keys.Right;
            ScrollableControl target = page;
            if (focus != null && page.Contains(focus))
            {
                for (Control c = focus.Parent; c != null && !ReferenceEquals(c, page); c = c.Parent)
                {
                    if (c is ScrollableControl inner && inner.AutoScroll && Range(inner, sideways) > 0) { target = inner; break; }
                }
            }
            if (!target.AutoScroll) { return true; }   // a page that cannot scroll now (a cover over it): nothing moves

            int range = Range(target, sideways);
            int at = sideways ? -target.AutoScrollPosition.X : -target.AutoScrollPosition.Y;
            int line = Math.Max(1, (int)Math.Round(40 * target.DeviceDpi / 96f));
            int view = sideways ? target.ClientSize.Width : target.ClientSize.Height;
            int to = at;
            switch (key)
            {
                case Keys.Up:
                case Keys.Left: to = at - line; break;
                case Keys.Down:
                case Keys.Right: to = at + line; break;
                case Keys.PageUp: to = at - Math.Max(line, view - line); break;
                case Keys.PageDown: to = at + Math.Max(line, view - line); break;
                case Keys.Home: to = 0; break;
                case Keys.End: to = range; break;
            }
            to = Math.Max(0, Math.Min(range, to));
            if (to == at) { return true; }

            int x = sideways ? to : -target.AutoScrollPosition.X;
            int y = sideways ? -target.AutoScrollPosition.Y : to;
            if (target is BackdropTabPage backdrop) { backdrop.ScrollToByKey(x, y); }
            else { target.AutoScrollPosition = new Point(x, y); }
            return true;
        }

        /// <summary>How far <paramref name="c"/> scrolls one way, in pixels (0: not at all).</summary>
        private static int Range(ScrollableControl c, bool sideways)
        {
            Rectangle d = c.DisplayRectangle;
            return Math.Max(0, sideways ? d.Width - c.ClientSize.Width : d.Height - c.ClientSize.Height);
        }
    }
}
