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

        /// <summary>Whether the picture is up, waiting for the switch under it to finish painting.</summary>
        public bool IsUp => _up;

        /// <summary>Whether the switch's transition is playing (the page under it is already finished).</summary>
        public bool IsAnimating => _anim != null && _anim.Running;

        // ── The transition (2026-11-08, the user: "make new animation for switch tab") ─────────────────────
        //
        // The picture used to come down in one go once the new page had painted: correct, but a cut. Now, once
        // the page under it is finished, the new page is copied off the window's own surface too (the picture
        // covers it on the screen, not on the surface) and the picture plays the change — the new page rising
        // into place from a little below while it fades in over the old one (falling from a little above when
        // the tab is higher up the sidebar), 220 ms, decelerating. Every frame is two whole pictures blended:
        // no control paints during it, so it can never show a half-drawn page, and the last frame IS the page
        // under it, so taking the picture away changes nothing. The page is live underneath throughout (the
        // picture lets clicks through). Switching again mid-way continues from the frame on screen.
        private const double TransitionMs = 220;
        private const int TransitionDistance96 = 20;

        private GdiDib _old, _new, _frame;
        private Rectangle _rect;
        private int _dir, _distance;
        private Motion.Animation _anim;

        /// <summary>
        /// Puts up a picture of what <paramref name="area"/> shows now, over it. False (and nothing shown) when
        /// it cannot — no window yet, nothing on screen, the copy failed.
        /// </summary>
        public bool Cover(Control area, Form owner)
        {
            if (area == null || owner == null || !area.IsHandleCreated || !owner.IsHandleCreated)
            {
                Lift();
                return false;
            }
            Rectangle r = area.RectangleToScreen(area.ClientRectangle);

            // A switch while the last one is still moving: what is on the screen is that transition's frame, so it
            // is the picture to hold — copied off the window instead, the page would jump to where it is going.
            if (IsAnimating && _frame != null && _old != null && r == _rect && Handle != IntPtr.Zero && IsWindowVisible(Handle))
            {
                Motion.Animation running = _anim;
                _anim = null;
                running.Stop(false);   // its end sees it is no longer the current one, and leaves the picture up
                GdiDib held = _old;
                _old = _frame;
                _frame = held;
                _new?.Dispose();
                _new = null;
                _up = true;
                StartSafety();
                return true;
            }

            Lift();
            if (!owner.Visible || owner.WindowState == FormWindowState.Minimized || owner.Opacity <= 0.0) { return false; }
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

            IntPtr screenDc = IntPtr.Zero, areaDc = IntPtr.Zero;
            try
            {
                screenDc = GetDC(IntPtr.Zero);
                areaDc = GetDC(area.Handle);
                if (screenDc == IntPtr.Zero || areaDc == IntPtr.Zero) { return false; }
                // Kept while the picture is up: it is where the transition starts from.
                _old = new GdiDib(r.Width, r.Height);
                // What is on screen there now. From the window's own surface when it is opaque — whatever sits
                // over Tempo on the screen is not copied. A see-through window (Settings › window opacity) is
                // copied off the SCREEN, already blended with what is behind it: a see-through picture over a
                // see-through window let the page being repainted underneath show through it (recorded at 85%,
                // 58 half-drawn frames), and blended the old page twice.
                bool seeThrough = owner.Opacity < 0.999;
                bool copied = seeThrough ? _old.CopyFrom(screenDc, r.X, r.Y) : _old.CopyFrom(areaDc, 0, 0);
                // Opaque (the copy has no alpha of its own): it must hide the repaint under it completely.
                if (!copied || !_old.Push(Handle, r.Location, 255, false))
                {
                    FreePictures();
                    return false;
                }

                SetWindowPos(Handle, PlaceAbove(owner.Handle), r.X, r.Y, r.Width, r.Height,
                             SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_NOOWNERZORDER);
                _rect = r;
                _up = true;
                StartSafety();
                return true;
            }
            catch
            {
                FreePictures();
                return false;
            }
            finally
            {
                if (areaDc != IntPtr.Zero) { ReleaseDC(area.Handle, areaDc); }
                if (screenDc != IntPtr.Zero) { ReleaseDC(IntPtr.Zero, screenDc); }
            }
        }

        /// <summary>
        /// The switch has finished painting the new page under the picture: plays the change from the old page to
        /// it (see the note on the fields), and takes the picture away at the end. <paramref name="direction"/>: 1 for
        /// a tab further down the sidebar (the page rises into place), -1 for one further up (it settles down), 0 for a
        /// change in place (a plain cross-fade — MainForm.FadeChange).
        /// False — nothing started, the caller lifts the picture — when it can't: animations off in Windows, a
        /// see-through window, the area moved, the copy failed.
        /// <paramref name="pinned"/>: windows lying over the area that are not part of the page (the startup safety note
        /// card) — both pictures hold them, so they would slide and fade with the page, a doubled card jumping 20 px at
        /// every switch. They are drawn where they are, from the new picture, in every frame: they stand still.
        /// </summary>
        public bool Transition(Control area, Form owner, int direction, params Control[] pinned)
            => Play(area, owner, direction, TransitionMs, pinned);

        /// <summary>
        /// A cross-fade in place (direction 0) over <paramref name="durationMs"/> — MainForm.FadeChange: a feature's cover, the
        /// safety note, the Accounts screens, a theme change (longer: the whole window changes colour).
        /// </summary>
        public bool Fade(Control area, Form owner, double durationMs) => Play(area, owner, 0, durationMs, null);

        // ── A folded section opening or closing (2026-11-16, the user: "add animation for hide/show advanced setting") ──
        //
        // The Clicker and Settings tabs' Advanced cards appeared and vanished in one frame. The curtain now plays the
        // change from its two whole pictures, as for a tab switch, so no frame can be half-drawn: opening, the section
        // unrolls down from under its header and pushes what lies below it down as it grows; closing, it rolls back up
        // into the header and what lies below rises after it. The page moving is played too, never shown as a jump:
        // closing usually makes the page shorter than where it was scrolled to (you scroll down to the cards to close
        // them) and it scrolls back at once — the header settles down into its new place; opening scrolls the page
        // just far enough to bring the cards into view when they would open out of sight below it — the header rises
        // while the cards come out under it. A section with nothing below it (the Clicker tab's) has only the page's
        // background under it, and fades as it unrolls.
        private Rectangle _foldBox;     // the page's own area, in the pictures' pixels: outside it, the finished picture
        private int _foldAnchor;        // the bottom of the section's header in the OLD picture
        private int _foldShift;         // how far that line moved in the new picture: down when closing scrolled the page
                                        // back, up (negative) when opening scrolled the cards into view
        private int _foldExtent;        // the section's height: how far what lies below it moves
        private bool _foldOpening, _foldPush;
        private int _foldBack;          // the page's background (COLORREF): what no picture holds

        /// <summary>How many folds have played (tests).</summary>
        internal static int FoldsPlayed;

        /// <summary>
        /// The section under the picture has opened or closed (see the note above). <paramref name="box"/>: the page's
        /// own area, in <paramref name="area"/>'s client pixels. <paramref name="anchorY"/>: the bottom of the section's
        /// header there BEFORE the change; <paramref name="shift"/>: how far it has moved since — down (positive) when
        /// closing made the page scroll back, up (negative) when opening scrolled the cards into view, 0 otherwise.
        /// <paramref name="extent"/>: the section's height. <paramref name="push"/>: what lies below the section moves
        /// with it (false: only background below it). <paramref name="background"/>: the page's colour. False — nothing
        /// started — when it can't; the caller cross-fades or lifts the picture.
        /// </summary>
        public bool Unfold(Control area, Form owner, Rectangle box, int anchorY, int shift, int extent,
                           bool opening, bool push, Color background, double durationMs)
        {
            if (!_up || _old == null || extent <= 0 || (opening ? shift > 0 : shift < 0)) { return false; }
            box = Rectangle.Intersect(box, new Rectangle(0, 0, _old.Width, _old.Height));
            // Where the header ends, before and after: some of the section must come out (or go in) inside the page.
            int before = anchorY, after = anchorY + shift;
            if (box.Width <= 0 || box.Height <= 0 || Math.Min(before, after) < box.Top || Math.Min(before, after) >= box.Bottom) { return false; }
            _foldBox = box;
            _foldAnchor = anchorY;
            _foldShift = shift;
            _foldExtent = extent;
            _foldOpening = opening;
            _foldPush = push;
            _foldBack = background.R | (background.G << 8) | (background.B << 16);
            if (!Play(area, owner, 0, durationMs, null, DrawFold)) { return false; }
            FoldsPlayed++;
            return true;
        }

        private double _durationMs = TransitionMs;

        private bool Play(Control area, Form owner, int direction, double durationMs, Control[] pinned, Action<double> draw = null)
        {
            if (!_up || _old == null || area == null || owner == null || !area.IsHandleCreated
                || Handle == IntPtr.Zero || !Motion.Enabled || owner.Opacity < 0.999)
            {
                return false;
            }
            Rectangle r = area.RectangleToScreen(area.ClientRectangle);
            if (r != _rect || r.Width != _old.Width || r.Height != _old.Height) { return false; }

            IntPtr areaDc = IntPtr.Zero;
            try
            {
                areaDc = GetDC(area.Handle);
                if (areaDc == IntPtr.Zero) { return false; }
                // The finished page, off the window's surface (the picture covers it only on the screen).
                _new?.Dispose();
                _new = new GdiDib(r.Width, r.Height);
                if (!_new.CopyFrom(areaDc, 0, 0)) { return false; }
                if (_frame == null || _frame.Width != r.Width || _frame.Height != r.Height)
                {
                    _frame?.Dispose();
                    _frame = new GdiDib(r.Width, r.Height);
                }
            }
            catch
            {
                return false;
            }
            finally
            {
                if (areaDc != IntPtr.Zero) { ReleaseDC(area.Handle, areaDc); }
            }

            Pin(pinned, r);
            _dir = Math.Sign(direction);
            _durationMs = Math.Max(60, durationMs);
            _distance = Math.Max(6, (int)Math.Round(TransitionDistance96 * owner.DeviceDpi / 96.0));
            _up = false;            // the switch itself is over: what is left is the motion
            _safety?.Stop();
            Motion.Animation started = null;
            started = Motion.Start(_durationMs, draw ?? DrawTransition, () =>
            {
                // Ended by its clock: down it comes. Stopped because a new switch took its frame over: left up.
                if (started == null || ReferenceEquals(_anim, started))
                {
                    _anim = null;
                    Lift();
                }
            });
            if (started.Running) { _anim = started; }
            return true;
        }

        /// <summary>One frame of the transition: the old page, and the new one over it, shifted and faded by <paramref name="t"/>.</summary>
        private void DrawTransition(double t)
        {
            if (_old == null || _new == null || _frame == null || Handle == IntPtr.Zero) { return; }
            int w = _frame.Width, h = _frame.Height;
            double move = Motion.EaseOutCubic(t);
            // The fade leads the move, so the page is all there before it has quite settled. A cross-fade in place has no
            // move to lead: it is spread over its whole time (ease in and out). Front-loaded, 80% of a theme's change was over
            // by the first tick, and a late tick showed the rest as one step.
            double fade = _dir == 0 ? Motion.EaseInOutCubic(t) : Motion.EaseOutCubic(Math.Min(1.0, t / 0.7));
            int dy = (int)Math.Round(_dir * _distance * (1.0 - move));
            byte alpha = (byte)Math.Round(255 * fade);

            if (alpha >= 255 && dy == 0)
            {
                GdiDib.BitBlt(_frame.Dc, 0, 0, w, h, _new.Dc, 0, 0, GdiDib.SRCCOPY);
            }
            else
            {
                GdiDib.BitBlt(_frame.Dc, 0, 0, w, h, _old.Dc, 0, 0, GdiDib.SRCCOPY);
                int rows = h - Math.Abs(dy);
                if (alpha > 0 && rows > 0)
                {
                    var blend = new GdiDib.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = alpha, AlphaFormat = 0 };
                    if (dy >= 0) { GdiDib.AlphaBlend(_frame.Dc, 0, dy, w, rows, _new.Dc, 0, 0, w, rows, blend); }
                    else { GdiDib.AlphaBlend(_frame.Dc, 0, 0, w, rows, _new.Dc, 0, -dy, w, rows, blend); }
                }
                // What lies over the page without belonging to it, still and whole, in its own shape.
                foreach (Pinned p in _pinned)
                {
                    if (p.Shape != IntPtr.Zero) { SelectClipRgn(_frame.Dc, p.Shape); }
                    GdiDib.BitBlt(_frame.Dc, p.Box.X, p.Box.Y, p.Box.Width, p.Box.Height, _new.Dc, p.Box.X, p.Box.Y, GdiDib.SRCCOPY);
                    if (p.Shape != IntPtr.Zero) { SelectClipRgn(_frame.Dc, IntPtr.Zero); }
                }
            }
            _frame.Push(Handle, _rect.Location, 255, false);
        }

        /// <summary>
        /// One frame of a fold (see Unfold): the section out by EaseOutCubic(<paramref name="t"/>) of its height — or still
        /// in by that much, closing — with what lies below it right under it, and the page above it moving to its new place
        /// by the same share.
        /// </summary>
        private void DrawFold(double t)
        {
            if (_old == null || _new == null || _frame == null || Handle == IntPtr.Zero) { return; }
            int w = _frame.Width, h = _frame.Height;
            IntPtr f = _frame.Dc;
            // The finished picture whole: everything outside the page, all of the last frame.
            GdiDib.BitBlt(f, 0, 0, w, h, _new.Dc, 0, 0, GdiDib.SRCCOPY);
            if (t < 1.0)
            {
                Rectangle box = _foldBox;
                double e = Motion.EaseOutCubic(t);
                // A section with only background under it fades as it moves, the fade a little ahead of the motion.
                double fade = Motion.EaseOutCubic(Math.Min(1.0, t / 0.6));
                int a0 = _foldAnchor, a1 = a0 + _foldShift;   // the header's bottom before and after
                if (_foldOpening)
                {
                    // As much of the section as the finished page has room to show under its header.
                    int span = Math.Max(0, Math.Min(_foldExtent, box.Bottom - a1));
                    int d = (int)Math.Round(span * e);
                    int up = (int)Math.Round(-_foldShift * e);     // how far the page has risen so far
                    int a = a0 - up;                                // the header's bottom in this frame
                    if (_foldShift != 0)
                    {
                        // Above the line: the page on its way up — the rows the finished picture has scrolled past from the
                        // old one, the rest (the header, whose button already says what a click does now) from the new.
                        int fromOld = a - a1;
                        if (fromOld > 0) { PageRows(f, _old, box.Top, box.Top + up, fromOld); }
                        PageRows(f, _new, box.Top + fromOld, box.Top, a1 - box.Top);
                    }
                    // Below what has come out: the page as it was under the header, pushed down (with nothing below the
                    // section, only its background).
                    PageRows(f, _old, a + d, a0, box.Bottom - (a + d));
                    // The section, top first.
                    Section(f, _new, a, a1, d, fade);
                }
                else
                {
                    // As much of the section as the page had room to show under its header.
                    int span = Math.Max(0, Math.Min(_foldExtent, box.Bottom - a0));
                    int d = (int)Math.Round(span * (1.0 - e));
                    int o = (int)Math.Round(_foldShift * (1.0 - e));   // how far the page still has to settle
                    int a = a1 - o;                                     // the header's bottom in this frame
                    // Above the line: the finished page, still that much further up.
                    if (o > 0) { PageRows(f, _new, box.Top, box.Top + o, a - box.Top); }
                    // Below what is left of the section: what follows it in the finished page, right under it — and past the
                    // end of the finished page, which the page has not quite settled down to yet, its background.
                    PageRows(f, _new, a + d, a1, box.Bottom - (a + d));
                    Section(f, _old, a, a0, d, 1.0 - fade);
                }
            }
            _frame.Push(Handle, _rect.Location, 255, false);
        }

        /// <summary>The <paramref name="rows"/> of the section that are out, from <paramref name="src"/>'s rows at <paramref name="srcY"/>, at <paramref name="y"/>.</summary>
        private void Section(IntPtr f, GdiDib src, int y, int srcY, int rows, double fade)
        {
            if (rows <= 0) { return; }
            // Content right below it, moving with it: whole, as a page's content is.
            if (_foldPush) { PageRows(f, src, y, srcY, rows); return; }
            PageRows(f, src, y, srcY, rows, (byte)Math.Round(255 * Math.Max(0.0, Math.Min(1.0, fade))), true);
        }

        /// <summary>
        /// <paramref name="rows"/> rows of <paramref name="src"/>'s page from its row <paramref name="srcY"/>, at <paramref name="dstY"/> —
        /// inside the page only, at <paramref name="alpha"/> over the page's background when <paramref name="overBackground"/>.
        /// Rows the picture does not hold (outside the page: past the end of a page that has not finished moving) are the background.
        /// </summary>
        private void PageRows(IntPtr f, GdiDib src, int dstY, int srcY, int rows, byte alpha = 255, bool overBackground = false)
        {
            Rectangle box = _foldBox;
            int top = Math.Max(dstY, box.Top), bottom = Math.Min(dstY + rows, box.Bottom);
            if (bottom <= top) { return; }
            int s0 = srcY + (top - dstY);   // the source row for 'top'
            int heldTop = Math.Max(top, top + (box.Top - s0)), heldBottom = Math.Min(bottom, top + (box.Bottom - s0));
            if (overBackground) { FillBackground(f, box.X, top, box.Width, bottom - top); }
            else
            {
                if (heldTop > top) { FillBackground(f, box.X, top, box.Width, Math.Min(heldTop, bottom) - top); }
                if (heldBottom < bottom) { int from = Math.Max(heldBottom, top); FillBackground(f, box.X, from, box.Width, bottom - from); }
            }
            if (heldBottom <= heldTop || alpha == 0) { return; }
            int sy = s0 + (heldTop - top), n = heldBottom - heldTop;
            if (alpha >= 255) { GdiDib.BitBlt(f, box.X, heldTop, box.Width, n, src.Dc, box.X, sy, GdiDib.SRCCOPY); }
            else
            {
                var blend = new GdiDib.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = alpha, AlphaFormat = 0 };
                GdiDib.AlphaBlend(f, box.X, heldTop, box.Width, n, src.Dc, box.X, sy, box.Width, n, blend);
            }
        }

        private void FillBackground(IntPtr dc, int x, int y, int w, int h)
        {
            if (w <= 0 || h <= 0) { return; }
            IntPtr brush = CreateSolidBrush(_foldBack);
            if (brush == IntPtr.Zero) { return; }
            try
            {
                var r = new RECT { left = x, top = y, right = x + w, bottom = y + h };
                FillRect(dc, ref r, brush);
            }
            finally { DeleteObject(brush); }
        }

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int left, top, right, bottom; }
        [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(int color);
        [DllImport("user32.dll")] private static extern int FillRect(IntPtr hdc, ref RECT rect, IntPtr brush);

        // ── Held still through the transition (see Transition's pinned) ───────────────────────────────────
        private struct Pinned
        {
            public Rectangle Box;   // in the pictures' pixels
            public IntPtr Shape;    // its window region there (rounded corners), or zero for the whole box
        }
        private readonly System.Collections.Generic.List<Pinned> _pinned = new System.Collections.Generic.List<Pinned>();

        /// <summary>Where each of <paramref name="controls"/> lies in the pictures of <paramref name="screen"/>, and its shape.</summary>
        private void Pin(Control[] controls, Rectangle screen)
        {
            FreePinned();
            if (controls == null) { return; }
            var whole = new Rectangle(0, 0, screen.Width, screen.Height);
            foreach (Control c in controls)
            {
                try
                {
                    if (c == null || c.IsDisposed || !c.IsHandleCreated || !c.Visible || c.Parent == null) { continue; }
                    // The window's own rectangle: a window region is measured from its corner, not its client area's.
                    Rectangle s = c.Parent.RectangleToScreen(c.Bounds);
                    var box = new Rectangle(s.X - screen.X, s.Y - screen.Y, s.Width, s.Height);
                    Rectangle seen = Rectangle.Intersect(box, whole);
                    if (seen.Width <= 0 || seen.Height <= 0) { continue; }
                    IntPtr shape = IntPtr.Zero;
                    if (c.Region != null)
                    {
                        using (Graphics g = c.CreateGraphics()) { shape = c.Region.GetHrgn(g); }
                        if (shape != IntPtr.Zero) { OffsetRgn(shape, box.X, box.Y); }
                    }
                    _pinned.Add(new Pinned { Box = seen, Shape = shape });
                }
                catch { }
            }
        }

        private void FreePinned()
        {
            foreach (Pinned p in _pinned)
            {
                if (p.Shape != IntPtr.Zero) { DeleteObject(p.Shape); }
            }
            _pinned.Clear();
        }

        private void FreePictures()
        {
            _old?.Dispose(); _old = null;
            _new?.Dispose(); _new = null;
            _frame?.Dispose(); _frame = null;
            FreePinned();
        }

        // ── Holding a page still while it scrolls (BackdropTabPage) ───────────────────────────────────────
        //
        // The same picture for a moment far shorter than a tab switch: over a page for one scroll step, while the step
        // slides the page and paints what it uncovered, and gone straight after. The screen then shows the page before
        // the step or after it — never the instant between, when the uncovered strip still holds the pixels slid out of
        // it (the smear). Called up to once a frame while a scroll goes on, so it keeps its picture's memory from one
        // step to the next and has no safety timer of its own: HoldEnd is always called by the step that held it.
        private GdiDib _hold;

        /// <summary>Puts up what <paramref name="area"/> shows now, over it, for one step. False when it can't.</summary>
        public bool HoldStart(Control area, Form owner)
        {
            if (_up || IsAnimating || area == null || owner == null || !area.IsHandleCreated || !owner.IsHandleCreated
                || !owner.Visible || owner.WindowState == FormWindowState.Minimized || owner.Opacity < 0.999)
            {
                return false;
            }
            Rectangle r = area.RectangleToScreen(area.ClientRectangle);
            if (r.Width <= 0 || r.Height <= 0) { return false; }
            if (Handle == IntPtr.Zero)
            {
                var cp = new CreateParams
                {
                    Caption = string.Empty,
                    Style = WS_POPUP,
                    ExStyle = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW,
                    Parent = owner.Handle,
                    X = r.X, Y = r.Y, Width = r.Width, Height = r.Height
                };
                CreateHandle(cp);
                try { int on = 1; DwmSetWindowAttribute(Handle, DWMWA_TRANSITIONS_FORCEDISABLED, ref on, sizeof(int)); } catch { }
            }
            IntPtr areaDc = IntPtr.Zero;
            try
            {
                if (_hold == null || _hold.Width != r.Width || _hold.Height != r.Height)
                {
                    _hold?.Dispose();
                    _hold = new GdiDib(r.Width, r.Height);
                }
                areaDc = GetDC(area.Handle);
                if (areaDc == IntPtr.Zero || !_hold.CopyFrom(areaDc, 0, 0) || !_hold.Push(Handle, r.Location, 255, false)) { return false; }
                SetWindowPos(Handle, PlaceAbove(owner.Handle), r.X, r.Y, r.Width, r.Height,
                             SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_NOOWNERZORDER);
                _holding = true;
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (areaDc != IntPtr.Zero) { ReleaseDC(area.Handle, areaDc); }
            }
        }

        private bool _holding;

        /// <summary>Takes the step's picture down: what is under it is finished.</summary>
        public void HoldEnd()
        {
            if (!_holding) { return; }
            _holding = false;
            try
            {
                GdiFlush();   // every drawing of the step on the window's surface, before its picture goes
                if (Handle != IntPtr.Zero && IsWindowVisible(Handle))
                {
                    SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
                                 SWP_NOACTIVATE | SWP_HIDEWINDOW | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOOWNERZORDER);
                }
            }
            catch { }
        }

        /// <summary>Lets the step picture's memory go (the scroll has been over a while).</summary>
        public void ReleaseHold()
        {
            HoldEnd();
            _hold?.Dispose();
            _hold = null;
        }

        [DllImport("gdi32.dll")] private static extern bool GdiFlush();

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
            // A transition in progress ends here too (the window moved, resized or hid under it): the page under it
            // is already the finished one.
            Motion.Animation running = _anim;
            _anim = null;
            if (running != null && running.Running) { running.Stop(false); }
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
            FreePictures();
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
            if (m.Msg == WM_SHOWWINDOW && m.WParam != IntPtr.Zero && (int)(long)m.LParam == SW_PARENTOPENING && !_up && !_holding)
            {
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            ReleaseHold();
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
        [DllImport("gdi32.dll")] private static extern int SelectClipRgn(IntPtr hdc, IntPtr hrgn);
        [DllImport("gdi32.dll")] private static extern int OffsetRgn(IntPtr hrgn, int x, int y);
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
