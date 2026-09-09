using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// Stops translated text from running into the control beside it.
    ///
    /// THE PROBLEM THIS SOLVES. Tempo's cards are laid out with hard-coded coordinates —
    /// a left column at x=16 and a right column at x=360 inside a 696px card — and every
    /// checkbox is AutoSize, so it grows to whatever its caption needs. That is fine in
    /// English, which is what the numbers were chosen against. It is not fine in anything
    /// else: Spanish, French and German routinely run 20–35% longer, so "Minimise the
    /// window during macro record &amp; playback" becomes "Minimizar la ventana durante la
    /// grabación &amp; reproducción de macros", grows straight through x=360, and lands on
    /// top of the right column's text. The result is two captions overprinting each other
    /// with neither readable.
    ///
    /// It is not only a translation problem. The same overflow appears in ENGLISH at a
    /// higher display scale: at 125% or 150% DPI every caption is proportionally wider
    /// while the hard-coded column positions scale differently, so the columns collide on
    /// exactly the machines whose users need larger text most.
    ///
    /// THE APPROACH. Measuring beats predicting: rather than guessing widths per language,
    /// this runs after a page is built, asks each control how wide it ACTUALLY became at
    /// this language and this DPI, and constrains only the ones that would overlap. A
    /// control that fits is left completely alone, so English at 100% is byte-identical to
    /// before.
    ///
    /// Constrained controls ellipsize rather than shrink: ModernCheckBox already paints
    /// with TextFormatFlags.WordEllipsis, so clamping Width gives a clean "…" for free.
    /// A tooltip carries the full caption, so nothing is actually lost — and every
    /// truncation is logged, so a caption that is too long for its column is a fact
    /// someone can look up rather than a surprise in a screenshot.
    /// </summary>
    internal static class LayoutFitter
    {
        /// <summary>Gap left between a constrained control and its right-hand neighbour.</summary>
        private const int Gap = 10;

        /// <summary>Padding kept inside the card's right edge.</summary>
        private const int CardPad = 12;

        /// <summary>
        /// Rows are found by vertical overlap rather than equal Top, because a checkbox,
        /// a label and a combo on the same visual row rarely share a Y to the pixel.
        /// </summary>
        private const int RowSlack = 4;

        private static readonly ToolTip Tips = new ToolTip
        {
            AutoPopDelay = 20000,
            InitialDelay = 400,
            ReshowDelay = 150,
            ShowAlways = true
        };

        /// <summary>Fits every card on every page below <paramref name="root"/>.</summary>
        internal static void FitAll(Control root)
        {
            if (root == null) { return; }
            try
            {
                int trimmed = Walk(root);
                if (trimmed > 0)
                {
                    Utils.Logger.Info("[layout] " + trimmed + " caption(s) were wider than their column " +
                                      "at this language/scale and have been clamped (full text on hover).");
                }

                // VERIFY, don't assume. The clamp above cannot fix everything — two
                // controls can be placed on top of each other outright, and a slot can be
                // too narrow to be worth clamping into. Re-checking afterwards turns
                // "should be fine now" into a number, and prints anything still wrong with
                // enough detail to find it in the source. This is what makes the layout
                // checkable in EVERY language instead of one screenshot at a time.
                //
                // These two checks are PURE DIAGNOSTICS — they only count and log, they
                // never move a control (the clamping above is what fixes anything). Two
                // more full walks of the whole control tree is real time on a path the
                // user is waiting behind, so they run once the window is actually up.
                // Same lines in the log, no longer in front of the window.
                Action verify = () =>
                {
                    try
                    {
                        // Re-fit the BUTTON ROWS first, because some captions are not
                        // final when FitAll runs. The Start/Stop pair gains its shortcut
                        // suffix once the hotkeys are registered — "▶ Démarrer" becomes
                        // "▶ Démarrer · F6" — which is 14px wider than the slot it was
                        // fitted into. Checking here without re-fitting reports a fault
                        // the fitter was never given the chance to fix.
                        //
                        // Only the button pass, not the whole of FitAll: the caption
                        // clamping is already done and re-running it would log every
                        // "clamped …" line a second time.
                        RefitButtons(root);

                        // The counts are logged alongside the faults on purpose. "0
                        // remaining" is only good news if the check actually looked at
                        // something; a scanner that silently examines nothing reports a
                        // clean layout just as loudly as a clean layout does.
                        int left = CountOverlaps(root);
                        Utils.Logger.Info("[layout] overlap check for " + Utils.Localization.Current +
                                          ": " + left + " remaining, " + _pairsCompared + " pairs compared.");

                        int clipped = CountClippedButtons(root);
                        Utils.Logger.Info("[layout] button check for " + Utils.Localization.Current +
                                          ": " + clipped + " clipped.");

                        int over = CountOverflows(root);
                        Utils.Logger.Info("[layout] overflow check for " + Utils.Localization.Current +
                                          ": " + over + " out of bounds, " + _overflowExamined + " examined.");

                        int stray = CountStraySideways(root);
                        Utils.Logger.Info("[layout] sideways check for " + Utils.Localization.Current +
                                          ": " + stray + " page(s) scroll sideways, " +
                                          _sidewaysExamined + " examined.");
                    }
                    catch (Exception ex) { Utils.Logger.Swallow("LayoutFitter/verify", ex); }
                };

                // Deferred to Application.Idle, NOT to the form's Shown event.
                //
                // Shown looks like the obvious hook and is wrong here, measurably: with
                // "Start minimised to tray" — the default for launch-at-sign-in, and this
                // machine's own setting — the window is never shown, Shown never fires,
                // and the two checks below simply never run. Measured across five launches
                // each: on Shown they appeared in 0/5, on Application.Idle in 5/5. That
                // silently removes the only layout check that covers all six languages,
                // and it takes the six-language suite's input with it, so a real overlap
                // in Spanish would ship with the suite reporting nothing at all.
                //
                // This codebase has been bitten by that same hole repeatedly — the tray
                // start is why HookSystemEvents, ApplyNotificationSettings and the
                // clipboard watcher are all called from two places.
                //
                // Application.Idle fires once the message loop has nothing left to do,
                // on both start paths, whether or not a window was ever shown. Unhooked
                // on the first call so it runs exactly once.
                EventHandler onIdle = null;
                onIdle = (s, e) =>
                {
                    Application.Idle -= onIdle;
                    verify();
                };
                Application.Idle += onIdle;

                // ...unless there is no message loop to go idle (a bare control tree in a
                // probe). Nothing is on screen to hold up in that case, so run it now and
                // drop the handler again.
                if (root.FindForm() == null && !(root is Form))
                {
                    Application.Idle -= onIdle;
                    verify();
                }
            }
            catch (Exception ex) { Utils.Logger.Swallow("LayoutFitter", ex); }
        }

        /// <summary>
        /// Runs the button pass again over every card, without re-clamping captions.
        ///
        /// Used once at start-up idle, when every caption that is filled in late has
        /// settled. Widening is idempotent: a button already wide enough is skipped by
        /// the first test in <see cref="FitButtonRow"/>, so a second pass costs a
        /// measure per button and changes nothing unless the text actually grew.
        /// </summary>
        private static void RefitButtons(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (c.HasChildren) { FitButtonRow(c); }
                RefitButtons(c);
            }
        }

        private static int Walk(Control parent)
        {
            int n = 0;
            foreach (Control c in parent.Controls)
            {
                // A card (GroupBox-like) is a container whose children share its
                // coordinate space — exactly the scope a column collision lives in.
                if (c.HasChildren) { n += FitContainer(c); }
                n += Walk(c);
            }
            return n;
        }

        /// <summary>
        /// Widens buttons whose translated caption no longer fits, using only the free
        /// space already beside them.
        ///
        /// NOTHING IS MOVED. A button row is hand-placed, and shifting one button to make
        /// room shoves every button after it — turning one clipped caption into a whole row
        /// that no longer lines up with the card around it. So a button may grow into the
        /// gap that is genuinely there and no further.
        ///
        /// When that gap is not enough, the caption gets a smaller font rather than a
        /// chopped-off word: "Todos los ajustes de subtítulos…" at 9pt is readable, the same
        /// string cut to "Todos los ajustes de subtít…" is not. Below the smallest step the
        /// button keeps its text and the verifier reports it, because at that point the
        /// layout needs a human decision rather than another pixel of squeeze.
        /// </summary>
        private static void FitButtonRow(Control card)
        {
            var all = new List<Control>();
            foreach (Control c in card.Controls)
            {
                if (c.Width > 0 && c.Height > 0) { all.Add(c); }
            }

            foreach (Control c in all)
            {
                var b = c as Button;
                if (b == null || b.AutoSize || string.IsNullOrEmpty(b.Text)) { continue; }
                if (TextRenderer.MeasureText(b.Text, b.Font).Width + ButtonClipPad <= b.Width) { continue; }

                // How far right can this button reach before it touches something?
                // Same unselected-TabPage caveat as the caption pass below.
                int limit = ContainerWidth(card) - CardPad;
                foreach (Control other in all)
                {
                    if (ReferenceEquals(other, b)) { continue; }
                    if (other.Left < b.Right) { continue; }
                    bool sameRow = other.Top < b.Bottom - RowSlack && b.Top < other.Bottom - RowSlack;
                    if (!sameRow) { continue; }
                    if (other.Left - Gap < limit) { limit = other.Left - Gap; }
                }

                int before = b.Width;
                int room = limit - b.Left;
                int want = TextRenderer.MeasureText(b.Text, b.Font).Width + ButtonComfortPad;
                if (room > b.Width) { b.Width = Math.Min(want, room); }

                if (TextRenderer.MeasureText(b.Text, b.Font).Width + ButtonClipPad > b.Width)
                {
                    Font original = b.Font;
                    foreach (float size in new[] { 9f, 8.25f, 7.5f })
                    {
                        if (size >= original.Size) { continue; }
                        var trial = new Font(original.FontFamily, size, original.Style);
                        if (TextRenderer.MeasureText(b.Text, trial).Width + ButtonClipPad <= b.Width)
                        {
                            b.Font = trial;
                            break;
                        }
                        trial.Dispose();
                    }
                }

                if (b.Width != before)
                {
                    Utils.Logger.Info("[layout] widened button \"" + Shorten(b.Text) + "\" " +
                                      before + "px → " + b.Width + "px in \"" + Shorten(card.Text) + "\".");
                }
            }
        }

        /// <summary>The WinForms default control width; a page reporting this was never laid out.</summary>
        private const int UnsizedPageWidth = 200;

        /// <summary>
        /// The width to measure a page's contents against.
        ///
        /// WHY THIS IS NOT JUST ClientSize. Tempo normally starts minimised to the
        /// tray, so the window is never shown and NOTHING is ever laid out — measured,
        /// the whole ModernTabControl and every page inside it still report the
        /// WinForms default 200x100. The fitter then computed a limit of 200-12=188
        /// and clamped any wide caption to about 174px.
        ///
        /// That is why the Clicker tab's footer tip rendered as
        /// "Tip: Press F6 to start/stop    •    All t…". The clamp was a fixed 174px in
        /// every language, which is the giveaway — a real neighbour collision varies
        /// with the text, a bogus container width does not.
        ///
        /// Deferring the pass until the window is shown is NOT the fix: on a
        /// start-to-tray launch that never happens, and the six-language layout suite
        /// would lose the verification lines it reads (the same failure mode as
        /// deferring to Shown). Instead, when a page is still at the default size, its
        /// own content extent stands in — the cards are hand-placed at fixed
        /// coordinates, so the rightmost child's edge IS the designed page width.
        ///
        /// A page that HAS been laid out keeps using its real ClientSize, so nothing
        /// changes once the window is on screen.
        /// </summary>
        private static int ContainerWidth(Control card)
        {
            int width = card.ClientSize.Width;

            // Only pages get the fallback. A card really can be narrow, and widening
            // its limit would defeat the whole point of the pass; a TabPage is never
            // legitimately 200px in this app.
            if (card is TabPage && width <= UnsizedPageWidth)
            {
                int right = 0;
                foreach (Control c in card.Controls)
                {
                    if (c.Right > right) { right = c.Right; }
                }
                if (right + CardPad > width) { width = right + CardPad; }
            }

            return width;
        }

        private static int FitContainer(Control card)
        {
            int trimmed = 0;

            // Buttons first: widening one changes the space left for the captions measured
            // below, so the other order would measure a layout that is about to change.
            FitButtonRow(card);

            // Deliberately NOT filtered on Visible. This runs before the window is shown,
            // and Control.Visible is the COMPOSITE answer — it is false for every control
            // while any ancestor is still unshown, so filtering on it made the whole pass
            // silently do nothing. Width/Height are already correct at this point because
            // AutoSize controls measure themselves as soon as their text and font are set,
            // which is exactly the measurement this needs.
            var kids = new List<Control>();
            foreach (Control c in card.Controls)
            {
                if (c.Width > 0 && c.Height > 0) { kids.Add(c); }
            }

            foreach (Control c in kids)
            {
                if (!IsCaption(c)) { continue; }

                int limit = ContainerWidth(card) - CardPad;

                // The nearest thing to the right that shares this row sets the real limit.
                foreach (Control other in kids)
                {
                    if (ReferenceEquals(other, c)) { continue; }
                    if (other.Left <= c.Left) { continue; }
                    bool sameRow = other.Top < c.Bottom - RowSlack && c.Top < other.Bottom - RowSlack;
                    if (!sameRow) { continue; }
                    if (other.Left - Gap < limit) { limit = other.Left - Gap; }
                }

                int available = limit - c.Left;

                // Three guards, each learned from what the first pass got wrong.
                //
                // MinUsableWidth — refuse to "fix" a caption by cutting it to a stub. If
                // the slot cannot hold a readable amount of text, clamping is not an
                // improvement over a small overlap; the layout itself is at fault and the
                // log below is the useful output.
                //
                // MinOverflow — a caption ending a few pixels into its neighbour's box is
                // invisible; truncating it to avoid that is a net loss. Only act on an
                // overlap someone can actually see.
                //
                // (The third guard is in IsCaption: labels are excluded entirely, because
                // a label is normally placed hard against the input it names and a couple
                // of pixels of overlap there is by design, not a bug.)
                const int MinUsableWidth = 150;
                const int MinOverflow = 8;

                if (available < MinUsableWidth) { continue; }
                if (c.Width <= available + MinOverflow) { continue; }

                string full = c.Text;
                c.AutoSize = false;
                c.Width = available;

                // Labels need telling; ModernCheckBox already paints with WordEllipsis.
                var lbl = c as Label;
                if (lbl != null) { lbl.AutoEllipsis = true; }

                try { Tips.SetToolTip(c, full); } catch { }

                Utils.Logger.Info("[layout] clamped \"" + Shorten(full) + "\" to " + available +
                                  "px inside \"" + Shorten(card.Text) + "\" — it needed more room " +
                                  "than the column allows.");
                trimmed++;
            }
            return trimmed;
        }

        /// <summary>
        /// Text-bearing controls whose width follows their caption.
        ///
        /// Labels ARE included, on the second attempt. They were excluded at first because
        /// including them clamped "NOMBRE" to 48px and "Orden:" to 44px — a label sits hard
        /// against the input it names, so it reads as overlapping by a few pixels while
        /// being exactly where it was meant to be. But blanket exclusion was the wrong cure:
        /// it also let a full sentence run straight through the label beside it, which is
        /// how "Los subtítulos funcionan por completo en este PC…" came to overprint
        /// "Idioma hablado:" leaving only its stray colon visible.
        ///
        /// The two guards at the call site are what actually separate those cases: a
        /// label/input pair has only a few pixels of overlap (below MinOverflow) in a slot
        /// far narrower than MinUsableWidth, so it is skipped on both counts, while a
        /// sentence colliding with a distant control fails both and is clamped. Buttons
        /// stay out: they are sized deliberately where they are built.
        /// </summary>
        private static bool IsCaption(Control c)
        {
            if (!c.AutoSize) { return false; }
            if (string.IsNullOrEmpty(c.Text)) { return false; }
            return c is CheckBox || c is RadioButton || c is Label;
        }

        /// <summary>
        /// Reports every pair of controls still overlapping after fitting, and returns the
        /// count. Only text-bearing controls are considered: a panel deliberately sitting
        /// behind its children overlaps them by design and is not a fault.
        /// </summary>
        /// <summary>How many pairs the last CountOverlaps compared; see the log line.</summary>
        private static int _pairsCompared;

        private static int CountOverlaps(Control root)
        {
            const int MinOverlapPx = 6;   // ignore touching edges and 1px rounding
            int found = 0;
            _pairsCompared = 0;

            foreach (Control container in Containers(root))
            {
                var kids = new List<Control>();
                foreach (Control c in container.Controls)
                {
                    if (c.Width > 0 && c.Height > 0 && IsCollidable(c)) { kids.Add(c); }
                }

                for (int i = 0; i < kids.Count; i++)
                {
                    for (int k = i + 1; k < kids.Count; k++)
                    {
                        // Compare INKED text, not control bounds. A fixed-width label is
                        // routinely far wider than its caption — "Interval step (ms):" is
                        // given a 250px box for ~110px of text — so comparing Bounds
                        // reported a 146px "overlap" that is entirely empty box against
                        // empty box, in every language including English. What a reader
                        // can actually see collide is the glyphs.
                        _pairsCompared++;
                        Rectangle hit = Rectangle.Intersect(CollisionBounds(kids[i]), CollisionBounds(kids[k]));
                        if (hit.Width < MinOverlapPx || hit.Height < MinOverlapPx) { continue; }
                        found++;
                        Utils.Logger.Warn("[layout] STILL OVERLAPPING in \"" + Shorten(container.Text) + "\": \"" +
                            Shorten(kids[i].Text) + "\"@" + kids[i].Left + "," + kids[i].Top + " and \"" +
                            Shorten(kids[k].Text) + "\"@" + kids[k].Left + "," + kids[k].Top +
                            " by " + hit.Width + "x" + hit.Height + "px.");
                    }
                }
            }
            return found;
        }

        /// <summary>
        /// Below this much free space around the caption, a button is actually CLIPPING —
        /// the glyphs run into the edge. Deliberately tight: the first version used a
        /// comfort figure of 20px and duly reported "1×" as clipped in a 38px button, where
        /// the text is 21px wide and looks perfectly fine. A test that cries wolf on
        /// healthy buttons is worse than no test.
        /// </summary>
        private const int ButtonClipPad = 8;

        /// <summary>What a button is GROWN to when there is room: comfortable, not tight.</summary>
        private const int ButtonComfortPad = 20;

        /// <summary>
        /// Buttons whose caption does not fit the width they were built with. Reported
        /// rather than silently widened: growing a button shoves whatever sits beside it,
        /// so the repair has to be row-aware (see <see cref="FitButtonRow"/>).
        /// </summary>
        private static int CountClippedButtons(Control root)
        {
            int found = 0;
            foreach (Control container in Containers(root))
            {
                foreach (Control c in container.Controls)
                {
                    var b = c as Button;
                    if (b == null || b.AutoSize || string.IsNullOrEmpty(b.Text)) { continue; }
                    int need = TextRenderer.MeasureText(b.Text, b.Font).Width + ButtonClipPad;
                    if (need <= b.Width) { continue; }
                    found++;
                    Utils.Logger.Warn("[layout] CLIPPED BUTTON in \"" + Shorten(container.Text) + "\": \"" +
                        Shorten(b.Text) + "\" needs " + need + "px but is " + b.Width + "px.");
                }
            }
            return found;
        }

        /// <summary>
        /// The rectangle a control's TEXT actually occupies, which for a fixed-width label
        /// is much narrower than its box. Never wider than the control itself — a clamped
        /// caption paints an ellipsis inside its bounds and cannot spill past them.
        /// </summary>
        /// <summary>
        /// Controls a collision can involve: text that draws only as wide as its caption,
        /// and the opaque inputs that text runs into.
        ///
        /// The inputs were MISSING here, and that hole hid a real bug. The check only
        /// looked at CheckBox/RadioButton/Label, so a caption overprinting the numeric box
        /// beside it was invisible to it — which is exactly what Italian does on the
        /// Behaviour card, where "Ritardo prima del clic (s):" runs straight through the
        /// spinner at x=462. The older whole-card check that used to run during tab build
        /// DID see it and warned about it; when that check was removed as duplicated noise
        /// this one was assumed to cover the same ground, and it did not.
        /// </summary>
        /// <summary>
        /// Reports controls that stick out of the container holding them, and returns the
        /// count.
        ///
        /// Overlap alone does not catch this: a control can clear every sibling and still
        /// hang over the edge of its card, where it is drawn clipped or spills onto
        /// whatever is behind. That is the shape a too-long translated caption takes when
        /// it is the LAST control in a row — there is nothing to its right to collide
        /// with, so the overlap pass sees a clean layout.
        ///
        /// Scrolling containers are skipped: running past the bottom of a scrolling page
        /// is what scrolling is for.
        /// </summary>
        /// <summary>How many controls the last CountOverflows examined; see the log line.</summary>
        private static int _overflowExamined;

        private static int CountOverflows(Control root)
        {
            const int Tolerance = 2;      // rounding and 1px borders
            int found = 0;
            _overflowExamined = 0;

            foreach (Control container in Containers(root))
            {
                if (container is Form) { continue; }
                var scroll = container as ScrollableControl;
                bool scrolls = scroll != null && scroll.AutoScroll;

                Size box = container.ClientSize;
                if (box.Width <= 0 || box.Height <= 0) { continue; }

                // ContainerWidth, not ClientSize: on a start-to-tray launch every page
                // still reports the WinForms default 200x100, and measuring against that
                // reported 175 "out of bounds" controls in English alone — every one of
                // them a page that had simply never been laid out. See ContainerWidth.
                int limitW = ContainerWidth(container);

                // A page that was never laid out has no meaningful bottom either, and
                // unlike width there is no designed value to stand in for it, so the
                // vertical half of the check sits out rather than inventing a number.
                bool unsizedPage = container is TabPage && box.Width <= UnsizedPageWidth;
                int limitH = (scrolls || unsizedPage) ? int.MaxValue : box.Height;

                foreach (Control c in container.Controls)
                {
                    // NOT c.Visible. That getter reports the EFFECTIVE visibility of the
                    // whole parent chain, and this check runs at Application.Idle, which
                    // is the only hook that fires when Tempo starts to the tray — where
                    // no parent is visible and every control claims to be hidden. It made
                    // this examine exactly 0 controls while reporting a clean layout.
                    // Size is what the rest of the fitter uses, for the same reason.
                    if (c.Width <= 0 || c.Height <= 0) { continue; }
                    if (!IsCollidable(c)) { continue; }
                    _overflowExamined++;

                    int overRight = c.Right - limitW;
                    int overBottom = limitH == int.MaxValue ? 0 : c.Bottom - limitH;
                    if (overRight <= Tolerance && overBottom <= Tolerance) { continue; }

                    found++;
                    Utils.Logger.Warn("[layout] OVERFLOWS \"" + Shorten(container.Text) + "\": \"" +
                        Shorten(c.Text) + "\"@" + c.Left + "," + c.Top + " " + c.Width + "x" + c.Height +
                        " runs " + (overRight > Tolerance ? overRight + "px past the right edge" : "") +
                        (overRight > Tolerance && overBottom > Tolerance ? " and " : "") +
                        (overBottom > Tolerance ? overBottom + "px past the bottom" : "") +
                        " of a " + limitW + "x" +
                        (limitH == int.MaxValue ? "unbounded" : limitH.ToString()) + " box.");
                }
            }
            return found;
        }

        /// <summary>How many scrolling pages the last CountStraySideways looked at.</summary>
        private static int _sidewaysExamined;

        /// <summary>
        /// Reports pages whose SCROLL RANGE is wider than their viewport, and returns the
        /// count.
        ///
        /// This is a different fault from <see cref="CountOverflows"/> and neither one can
        /// see the other. Overflow compares each control's rectangle against its container:
        /// it catches a card that is too wide. This compares the page's own
        /// <see cref="ScrollableControl.DisplayRectangle"/> against its client area, which
        /// is what actually decides whether a horizontal scrollbar appears — and that range
        /// only ever GROWS in WinForms. A page whose cards were centred out to the right
        /// while the window was maximised keeps the wide range after the window is restored,
        /// so every control passes the overflow check while the page still scrolls sideways
        /// and drags the cards off the left edge. That shipped, unnoticed, past three
        /// "ALL LANGUAGES CLEAN" runs.
        ///
        /// Vertical range is deliberately not checked here: a long page scrolling down is
        /// the normal case, and there is no cheap way to tell a legitimate one from a stale
        /// one at this point. Sideways is unambiguous — nothing in this app is meant to.
        /// </summary>
        private static int CountStraySideways(Control root)
        {
            const int Tolerance = 2;
            int found = 0;
            _sidewaysExamined = 0;

            foreach (Control container in Containers(root))
            {
                var scroll = container as ScrollableControl;
                if (scroll == null || !scroll.AutoScroll) { continue; }

                // A page that has never been laid out reports the WinForms default 200x100
                // and would fail this every time on a start-to-tray launch — the same trap
                // that once made the overflow check report 175 phantom faults.
                int box = scroll.ClientSize.Width;
                if (box <= UnsizedPageWidth) { continue; }

                _sidewaysExamined++;
                int range = scroll.DisplayRectangle.Width;
                if (range - box <= Tolerance) { continue; }

                found++;
                Utils.Logger.Warn("[layout] SCROLLS SIDEWAYS \"" + Shorten(container.Text) +
                    "\": a " + range + "px range in a " + box + "px viewport — " +
                    (range - box) + "px of it is off to the right.");
            }
            return found;
        }

        private static bool IsCollidable(Control c)
        {
            if (c is CheckBox || c is RadioButton || c is Label)
            {
                return !string.IsNullOrEmpty(c.Text);
            }
            // A container full of controls is as visible as any of them, and two of them
            // landing on each other is the worst kind of overlap — it hides whole rows,
            // not a few glyphs. The Settings page derives each card's Y from the height
            // of the one above, so a card that grows past its allowance lands squarely on
            // its neighbour, and nothing here used to notice.
            //
            // Only ever compared against SIBLINGS, so a panel containing its own children
            // is never reported against them.
            //
            // Size, not Visible: see the note in CountOverflows. Gating on Visible here
            // silently excluded every container whenever the check ran with the window
            // still hidden, which is precisely when it runs.
            //
            // TabPages are the one kind of sibling container that SHARES its rectangle
            // by design — a tab control is a stack, and only one page shows at a time.
            // Counting them reported all 28 pairs of Tempo's eight tabs as collisions.
            if (c is TabPage) { return false; }
            if (c.HasChildren) { return true; }

            // Opaque inputs: they paint their whole rectangle whether or not they hold
            // text, so an empty box is just as much of a collision as a full one.
            return c is NumericUpDown || c is ComboBox || c is TextBox
                || c is Button || c is TrackBar;
        }

        /// <summary>
        /// What a collision should be measured against: the glyphs for a text control,
        /// the whole rectangle for a container.
        ///
        /// A card's Text is only its title, so measuring a container by its ink would
        /// compare two title strings and miss the bodies sliding through each other.
        /// </summary>
        private static Rectangle CollisionBounds(Control c)
        {
            return c.HasChildren ? c.Bounds : InkedBounds(c);
        }

        /// <summary>
        /// What the control actually PAINTS. Text controls ink only as far as their
        /// caption reaches; an input paints its whole box, so its bounds are its ink.
        /// </summary>
        private static Rectangle InkedBounds(Control c)
        {
            Rectangle b = c.Bounds;
            if (!(c is CheckBox || c is RadioButton || c is Label)) { return b; }
            try
            {
                int lead = 0;
                if (c is CheckBox || c is RadioButton)
                {
                    lead = 24;   // tick box + gap before the caption starts
                }
                int textWidth = TextRenderer.MeasureText(c.Text, c.Font).Width;
                int inked = Math.Min(b.Width, lead + textWidth + 2);
                if (inked > 0 && inked < b.Width) { b.Width = inked; }
            }
            catch { }
            return b;
        }

        /// <summary>
        /// Every container under <paramref name="parent"/>, INCLUDING parent itself.
        ///
        /// It used to start one level down, so whatever sits directly on the page — the
        /// cards, and any loose control placed straight onto it — was never compared
        /// against anything. The overlap check silently covered the inside of each card
        /// and nothing between them.
        /// </summary>
        private static IEnumerable<Control> Containers(Control parent)
        {
            if (parent.HasChildren) { yield return parent; }
            foreach (Control c in parent.Controls)
            {
                foreach (Control inner in Containers(c)) { yield return inner; }
            }
        }

        private static string Shorten(string s)
        {
            if (string.IsNullOrEmpty(s)) { return ""; }
            s = s.Replace("\n", " ");
            return s.Length <= 46 ? s : s.Substring(0, 44) + "…";
        }
    }
}
