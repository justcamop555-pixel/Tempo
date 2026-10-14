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

        /// <summary>
        /// Fits every card on every page below <paramref name="root"/> — and below each of
        /// <paramref name="alsoCheck"/>, which is for screens the window owns but is not
        /// SHOWING at start-up.
        ///
        /// The Accounts page is four screens swapped in and out (enable, set a password,
        /// unlock, the list), and only the one on the page when this runs is in the control
        /// tree; the other three are detached. So three of them were never fitted and never
        /// checked in any language — which is why the unlocked account list had only ever
        /// been checked by eye. Handing them in here puts them through the same pass, and
        /// into the same counts the six-language suite reads.
        /// </summary>
        internal static void FitAll(Control root, IEnumerable<Control> alsoCheck = null)
        {
            if (root == null) { return; }
            List<Control> roots = RootsOf(root, alsoCheck);

            try
            {
                _grown = 0;
                _ellipsized = 0;
                // In each window's own units (SetUnitsPerPixel): this runs while the main window is
                // being built, in 100% numbers, with its text already 1.5× on a 150% screen.
                _u = UnitsFor(root);
                int trimmed = Walk(root);
                for (int i = 1; i < roots.Count; i++)
                {
                    // A detached screen is its own top container: fit what sits directly on
                    // it (Walk only fits containers it finds BELOW the one it is given).
                    _u = UnitsFor(roots[i]);
                    if (roots[i].HasChildren) { trimmed += FitContainer(roots[i]); }
                    trimmed += Walk(roots[i]);
                }
                _u = 1f;
                if (trimmed > 0)
                {
                    Utils.Logger.Info("[layout] " + trimmed + " caption(s) were wider than their column " +
                                      "at this language/scale and have been clamped (full text on hover).");
                }
                if (_grown + _ellipsized > 0)
                {
                    Utils.Logger.Info("[layout] " + (_grown + _ellipsized) + " fixed text box(es) were too small for their " +
                                      "words at this language/scale: " + _grown + " made taller, " + _ellipsized +
                                      " ended with … (full text on hover).");
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
                    // Timed: it runs at idle once the window is up, and every check added to it is time
                    // spent on the window's thread. The log says how much.
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        // The screens handed in are asked for AGAIN, not reused: by now one can have
                        // been put on the page, and on a 125%/150% screen the window has scaled since
                        // the constructor — the Accounts screens are brought to its scale as they are
                        // handed over (MainForm.DetachedLayoutRoots), or they are measured with 150%
                        // text in 100% boxes.
                        List<Control> now = RootsOf(root, alsoCheck);

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
                        foreach (Control r in now)
                        {
                            _u = UnitsFor(r);
                            RefitButtons(r);
                        }
                        _u = 1f;

                        // The counts are logged alongside the faults on purpose. "0
                        // remaining" is only good news if the check actually looked at
                        // something; a scanner that silently examines nothing reports a
                        // clean layout just as loudly as a clean layout does.
                        Report rep = Measure(now);
                        Utils.Logger.Info("[layout] overlap check for " + Utils.Localization.Current +
                                          ": " + rep.Overlaps + " remaining, " + rep.PairsCompared + " pairs compared.");
                        Utils.Logger.Info("[layout] button check for " + Utils.Localization.Current +
                                          ": " + rep.Clipped + " clipped.");
                        Utils.Logger.Info("[layout] overflow check for " + Utils.Localization.Current +
                                          ": " + rep.Overflows + " out of bounds, " + rep.OverflowExamined + " examined.");
                        Utils.Logger.Info("[layout] sideways check for " + Utils.Localization.Current +
                                          ": " + rep.Sideways + " page(s) scroll sideways, " +
                                          rep.SidewaysExamined + " examined.");
                        Utils.Logger.Info("[layout] text check for " + Utils.Localization.Current +
                                          ": " + rep.EnglishText + " showing English that is translated, " +
                                          rep.TextExamined + " examined.");
                        Utils.Logger.Info("[layout] cut-text check for " + Utils.Localization.Current +
                                          ": " + rep.TextCut + " cut off, " + rep.CutExamined + " examined.");
                        Utils.Logger.Info("[layout] item check for " + Utils.Localization.Current +
                                          ": " + rep.ItemsCut + " cut off, " + rep.ItemsExamined + " examined.");
                        Utils.Logger.Info("[layout] ampersand check for " + Utils.Localization.Current +
                                          ": " + rep.Ampersands + " hiding an \"&\", " + rep.AmpExamined + " examined.");
                        if (rep.IconsDropped > 0)
                        {
                            Utils.Logger.Info("[layout] icon check for " + Utils.Localization.Current + ": " +
                                              rep.IconsDropped + " button icon(s) left off so the caption fits.");
                        }
                        Utils.Logger.Info("[layout] start-up check took " + clock.ElapsedMilliseconds + " ms.");
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
        /// <paramref name="root"/>, then each of <paramref name="alsoCheck"/> that is NOT already in its
        /// tree — a screen that happens to be on the page would otherwise be fitted and counted twice.
        /// </summary>
        private static List<Control> RootsOf(Control root, IEnumerable<Control> alsoCheck)
        {
            var roots = new List<Control> { root };
            if (alsoCheck == null) { return roots; }
            try
            {
                foreach (Control extra in alsoCheck)
                {
                    if (extra != null && !extra.IsDisposed && !IsInside(extra, root) && !roots.Contains(extra))
                    {
                        roots.Add(extra);
                    }
                }
            }
            catch (Exception ex) { Utils.Logger.Swallow("LayoutFitter.RootsOf", ex); }
            return roots;
        }

        /// <summary>Controls deliberately drawn over their siblings, and why.</summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, string> _overlays =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Control, string>();

        /// <summary>
        /// Declares <paramref name="c"/> as MEANT to sit on top of (or under) its siblings, so
        /// the overlap check stops reporting it — an empty-state message drawn over an empty
        /// list, a backdrop layer under a header, a button that takes a preview's place while
        /// the preview is hidden.
        ///
        /// Declared at the control, with the reason, rather than excused inside the checker by
        /// type or position: now that the check sees every kind of control, the handful of
        /// deliberate layers have to say so themselves, and a new accidental overlap still
        /// cannot hide among them. It is still checked for running out of its container.
        /// </summary>
        internal static void DeclareOverlay(Control c, string why)
        {
            if (c == null) { return; }
            try
            {
                _overlays.Remove(c);
                _overlays.Add(c, why ?? "");
            }
            catch { }
        }

        private static bool IsOverlay(Control c) => c != null && _overlays.TryGetValue(c, out _);

        /// <summary>Every caption a control can show besides the one it has now, declared where it is built.</summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, string[]> _captions =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Control, string[]>();

        /// <summary>
        /// Declares every caption <paramref name="c"/> can show — Start captions / Stop captions, Copy / Copied,
        /// Show / Hide — already translated, so the checks measure all of them and the fitter makes room
        /// for the widest.
        ///
        /// THE BLIND SPOT THIS CLOSES. Every check runs over the captions a control has NOW. A button that
        /// swaps its caption at runtime is checked for whichever one it had at start-up: the transcript's
        /// "Copy all" becomes "Copy shown" when a filter is typed, and German "Angezeigtes kopieren" needed
        /// 120 px in a 104 px button while the suite, dialogscan and the start-up check were all clean,
        /// because none of them types a filter. Declared here, it is measured whatever is showing.
        /// </summary>
        internal static void DeclareCaptions(Control c, params string[] captions)
        {
            if (c == null || captions == null) { return; }
            try
            {
                _captions.Remove(c);
                _captions.Add(c, captions);
            }
            catch { }
        }

        /// <summary>The caption <paramref name="c"/> shows now and every one declared for it: distinct, none empty.</summary>
        internal static List<string> CaptionsOf(Control c)
        {
            var all = new List<string>();
            if (c == null) { return all; }
            if (!string.IsNullOrEmpty(c.Text)) { all.Add(c.Text); }
            if (_captions.TryGetValue(c, out string[] declared))
            {
                foreach (string s in declared)
                {
                    if (!string.IsNullOrEmpty(s) && !all.Contains(s)) { all.Add(s); }
                }
            }
            return all;
        }

        private static bool HasDeclaredCaptions(Control c) => c != null && _captions.TryGetValue(c, out _);

        /// <summary>
        /// Captions this pass shortened ON PURPOSE (AutoSize off, an ellipsis, the whole text on hover).
        /// The cut-caption check leaves them out: they are the fix, not the fault.
        /// </summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object> _clamped =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Control, object>();

        private static bool IsClamped(Control c) => c != null && _clamped.TryGetValue(c, out _);

        /// <summary>Combo boxes whose closed box may end a long choice in "…", and why.</summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, string> _shortChoices =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Control, string>();

        /// <summary>
        /// Declares that <paramref name="combo"/>'s CLOSED box may end a long choice in "…": descriptive
        /// choices ("Large v3 (most accurate, GPU recommended)") in a column that has no more room. A
        /// FlatComboBox opens its list as wide as its widest choice and shows a shortened choice whole on
        /// hover, so every word can still be read; a choice the open list cannot show stays a fault.
        /// Declared at the control, with the reason, like <see cref="DeclareOverlay"/>.
        /// </summary>
        internal static void DeclareShortenedChoices(Control combo, string why)
        {
            if (combo == null) { return; }
            try { _shortChoices.AddOrUpdate(combo, why ?? ""); }
            catch { }
        }

        /// <summary>What one run of the four checks found, and how much it looked at.</summary>
        internal sealed class Report
        {
            public int Overlaps, Clipped, Overflows, Sideways;
            public int PairsCompared, OverflowExamined, SidewaysExamined;
            public int Windows;
            /// <summary>Glyph buttons drawing their caption without the icon. Informational, not a fault.</summary>
            public int IconsDropped;
            /// <summary>Controls showing English the active language translates — a missed lookup.</summary>
            public int EnglishText, TextExamined;
            /// <summary>
            /// Text that does not fit its box and is not drawn: fixed-size labels, fixed-size tick boxes,
            /// and text a control paints itself (a card's title, a stat card's caption).
            /// </summary>
            public int TextCut, CutExamined;
            /// <summary>Combo-box choices and list column titles too wide for the box that shows them.</summary>
            public int ItemsCut, ItemsExamined;
            /// <summary>Text whose "&amp;" the control draws as a keyboard-shortcut underline instead of showing it.</summary>
            public int Ampersands, AmpExamined;
            public int Faults => Overlaps + Clipped + Overflows + Sideways + EnglishText + TextCut + ItemsCut + Ampersands;
        }

        /// <summary>Every check over every root, summed. Counts and logs; moves nothing.</summary>
        private static Report Measure(IEnumerable<Control> roots)
        {
            var rep = new Report();
            try
            {
                foreach (Control r in roots)
                {
                    if (r == null || r.IsDisposed) { continue; }
                    // Each window in its own units (SetUnitsPerPixel).
                    _u = UnitsFor(r);
                    MeasureOne(r, rep);
                }
            }
            finally { _u = 1f; }
            return rep;
        }

        private static void MeasureOne(Control r, Report rep)
        {
            rep.Overlaps += CountOverlaps(r);
            rep.PairsCompared += _pairsCompared;
            rep.Clipped += CountClippedButtons(r);
            rep.Overflows += CountOverflows(r);
            rep.OverflowExamined += _overflowExamined;
            rep.Sideways += CountStraySideways(r);
            rep.SidewaysExamined += _sidewaysExamined;
            rep.IconsDropped += CountDroppedIcons(r);
            rep.EnglishText += CountEnglishText(r);
            rep.TextExamined += _textExamined;
            rep.TextCut += CountCutText(r);
            rep.CutExamined += _cutExamined;
            // Text a control paints itself: cut is cut text, and a child sitting on it is an
            // overlap — the same two faults, where no child control carries the words.
            rep.TextCut += CountPaintedText(r);
            rep.CutExamined += _paintedExamined;
            rep.Overlaps += _paintedCovered;
            rep.PairsCompared += _paintedCompared;
            rep.ItemsCut += CountCutItems(r);
            rep.ItemsExamined += _itemsExamined;
            rep.Ampersands += CountHiddenAmpersands(r);
            rep.AmpExamined += _ampExamined;
            rep.Windows++;
        }

        // ── Units ───────────────────────────────────────────────────────────────────────────────
        //
        // Every check compares text it MEASURES (screen pixels) with the layout's own numbers. Those
        // are the same thing once a window has been scaled to the screen — and always at 100% — but not
        // while a window with AutoScaleMode.Dpi is still being built on a 125%/150% screen: its numbers
        // are 100% ones then, its text already 1.5×. The main window's whole fitting pass runs at that
        // moment, so every decision it made compared 1.5× text with 100% boxes, and its fixes (a width,
        // a height) were scaled again with the window. Each window says how its units relate to a
        // screen pixel; measured text and self-sized controls are read through that.

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, Func<float>> _units =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Control, Func<float>>();

        /// <summary>
        /// Says how many of <paramref name="root"/>'s layout units one screen pixel of text is: 1/1.5 while
        /// a window that will be scaled ×1.5 is still being built, 1 once it has been scaled (the default,
        /// and always at 100%). The ×1.5 test, which scales a layout but not its fonts, answers 1.5 — which
        /// makes every check read the text as if the fonts had grown with the layout.
        /// </summary>
        internal static void SetUnitsPerPixel(Control root, Func<float> provider)
        {
            if (root == null || provider == null) { return; }
            try { _units.AddOrUpdate(root, provider); }
            catch { }
        }

        private static float UnitsFor(Control root)
        {
            try
            {
                if (root != null && _units.TryGetValue(root, out Func<float> provider))
                {
                    float u = provider();
                    if (u > 0.1f && u < 10f) { return u; }
                }
            }
            catch { }
            return 1f;
        }

        /// <summary>Units per screen pixel for the window being fitted or checked now.</summary>
        private static float _u = 1f;

        /// <summary>A screen-pixel measurement in the current window's layout units.</summary>
        private static int U(int px) => _u == 1f ? px : (int)Math.Ceiling(px * _u);

        /// <summary>A size measured in screen pixels, in the current window's layout units.</summary>
        private static Size U(Size s) => _u == 1f ? s : new Size(U(s.Width), U(s.Height));

        /// <summary>A layout width as screen pixels: the width to wrap text at when measuring it.</summary>
        private static int ToPx(int units) => _u == 1f ? units : Math.Max(1, (int)Math.Round(units / _u));

        /// <summary>
        /// Runs the four checks NOW over whatever is on screen, without fitting anything.
        ///
        /// The start-up pass runs once, over the main window, before anything has been
        /// used. Everything that changes afterwards — a status line whose text grows, a
        /// button that appears, a dialog, the detached Accounts window — it never sees.
        /// This is the same scanner on demand (Live debug's "Check layout"), over every open
        /// Tempo window plus anything handed in, with the window really laid out — so it
        /// measures the layout the user is looking at, not the one Tempo started with.
        ///
        /// Logged with its own prefix, so it can never be mistaken for the start-up lines
        /// the six-language suite parses.
        /// </summary>
        internal static Report CheckNow(IEnumerable<Control> roots)
        {
            var list = new List<Control>();
            foreach (Control r in roots ?? new Control[0])
            {
                if (r == null || r.IsDisposed || list.Contains(r)) { continue; }
                bool nested = false;
                foreach (Control other in list) { if (IsInside(r, other)) { nested = true; break; } }
                if (!nested) { list.Add(r); }
            }

            Report rep;
            try { rep = Measure(list); }
            catch (Exception ex)
            {
                Utils.Logger.Swallow("LayoutFitter.CheckNow", ex);
                rep = new Report();
            }
            Utils.Logger.Info("[layout] on-demand check (" + Utils.Localization.Current + ", " + rep.Windows +
                              " window(s)/screen(s)): " + rep.Overlaps + " overlapping, " + rep.Clipped +
                              " clipped button(s), " + rep.Overflows + " out of bounds, " + rep.Sideways +
                              " scrolling sideways, " + rep.EnglishText + " showing untranslated English, " +
                              rep.TextCut + " with text cut off, " + rep.ItemsCut + " list item(s) or column title(s) cut off, " +
                              rep.Ampersands + " hiding an \"&\" — " +
                              rep.PairsCompared + " pairs, " + rep.OverflowExamined + " controls, " +
                              rep.TextExamined + " texts, " + rep.CutExamined + " fixed boxes and " +
                              rep.ItemsExamined + " list items examined.");
            return rep;
        }

        /// <summary>True when <paramref name="c"/> is <paramref name="ancestor"/> or sits somewhere below it.</summary>
        private static bool IsInside(Control c, Control ancestor)
        {
            for (Control p = c; p != null; p = p.Parent)
            {
                if (ReferenceEquals(p, ancestor)) { return true; }
            }
            return false;
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
                // Act when the caption is cut — or when a glyph button has had to leave its
                // icon off and there may be free space beside it to bring it back. (What the
                // caption needs is measured in screen pixels; U puts it in the window's units.)
                if (U(ButtonNeeds(b)) <= b.Width && U(ButtonWants(b)) <= b.Width) { continue; }

                // How far right can this button reach before it touches something?
                // Same unselected-TabPage caveat as the caption pass below.
                int limit = ContainerWidth(card) - CardPad;
                foreach (Control other in all)
                {
                    if (ReferenceEquals(other, b)) { continue; }
                    if (other.Left < b.Right) { continue; }
                    bool sameRow = other.Top < LaidOutBounds(b).Bottom - RowSlack && b.Top < LaidOutBounds(other).Bottom - RowSlack;
                    if (!sameRow) { continue; }
                    if (other.Left - Gap < limit) { limit = other.Left - Gap; }
                }

                int before = b.Width;
                int room = limit - b.Left;
                int want = Math.Max(U(ButtonWants(b)), U(ButtonNeeds(b)) - ButtonClipPad + ButtonComfortPad);
                if (room > b.Width) { b.Width = Math.Min(want, room); }

                // A smaller font only when the words really do not fit: compared in screen pixels with
                // 100% boxes, a 150% screen shrank captions that fit once the window was scaled.
                if (U(ButtonNeeds(b)) > b.Width)
                {
                    Font original = b.Font;
                    foreach (float size in new[] { 9f, 8.25f, 7.5f })
                    {
                        if (size >= original.Size) { continue; }
                        var trial = new Font(original.FontFamily, size, original.Style);
                        b.Font = trial;
                        if (U(ButtonNeeds(b)) <= b.Width) { break; }
                        b.Font = original;
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
                    int r = LaidOutBounds(c).Right;
                    if (r > right) { right = r; }
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
                    bool sameRow = other.Top < LaidOutBounds(c).Bottom - RowSlack && c.Top < LaidOutBounds(other).Bottom - RowSlack;
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
                // Its width in the window's units: an AutoSize caption is sized in screen pixels.
                if (LaidOutBounds(c).Width <= available + MinOverflow) { continue; }

                // A paragraph — a label wrapping inside its MaximumSize — is re-wrapped narrower
                // first, onto more lines, when the card has room below it for them. Shortening it
                // keeps its height, so the narrower text runs out of lines and AutoEllipsis cuts
                // its END off: the Background Clicking explanation lost its last sentences that
                // way, in English, beside a warning that is empty most of the time. That stays
                // the last resort, for a paragraph with nowhere to grow.
                if (c is Label para && para.MaximumSize.Width > 0 && para.Height > para.Font.Height * 3 / 2
                    && TryRewrap(card, para, available, kids))
                {
                    trimmed++;
                    continue;
                }

                string full = c.Text;
                // Width AND height, explicitly. Switching AutoSize off makes a Label go back to
                // the size it was last ASKED for — and one that was only ever auto-sized was
                // asked for nothing: it came out 0 px tall. Every label this pass shortened
                // vanished outright ("Meta da sessão (cliques, 0 = off):" in Portuguese, the
                // whole Background Clicking explanation in English), and a control with no
                // height collides with nothing, so the check below then called it clean.
                // In the window's units: the AutoSize height is screen pixels while it is being built.
                int height = LaidOutBounds(c).Height;
                c.AutoSize = false;
                c.Size = new Size(available, height);

                // Labels need telling; ModernCheckBox already paints with WordEllipsis.
                var lbl = c as Label;
                if (lbl != null) { lbl.AutoEllipsis = true; }
                _clamped.AddOrUpdate(c, null);

                // One tip: the full caption on top of the control's help, not a second popup
                // over it (see TipStyle). As SHOWN — "record && playback" is one "&" on screen.
                // A label's tip FOLLOWS its text: it was set once, here, so a line whose text
                // changed later kept offering the old words on hover. It also only shows while the
                // text really is cut.
                try
                {
                    if (lbl != null) { TipStyle.FollowText(lbl); }
                    else { TipStyle.SetCaption(c, TipStyle.Shown(c)); }
                }
                catch { }

                Utils.Logger.Info("[layout] clamped \"" + Shorten(full) + "\" to " + available +
                                  "px inside \"" + Shorten(card.Text) + "\" — it needed more room " +
                                  "than the column allows.");
                trimmed++;
            }

            // Then the fixed-size text boxes whose words do not fit them. Top to bottom, so a
            // box grown into free space is measured before the one under it looks for room.
            kids.Sort((a, b) => a.Top.CompareTo(b.Top));
            foreach (Control c in kids)
            {
                if (c is Label l)
                {
                    try { RepairCutText(card, l, kids); }
                    catch (Exception ex) { Utils.Logger.Swallow("LayoutFitter.RepairCutText", ex); }
                }
            }
            return trimmed;
        }

        /// <summary>
        /// Re-wraps a paragraph to <paramref name="width"/>, keeping every word: it grows taller
        /// instead. Refused — and put back exactly as it was — when the taller paragraph would
        /// run off the bottom of its card or into a control it did not already touch.
        /// </summary>
        private static bool TryRewrap(Control card, Label para, int width, List<Control> kids)
        {
            Size oldMax = para.MaximumSize;
            Size oldSize = para.Size;
            Rectangle before = LaidOutBounds(para);
            try
            {
                para.MaximumSize = new Size(width, 0);
                para.Size = para.PreferredSize;
                Rectangle after = LaidOutBounds(para);

                bool fits = after.Width <= width && after.Bottom <= card.ClientSize.Height - 4;
                if (fits)
                {
                    foreach (Control other in kids)
                    {
                        if (ReferenceEquals(other, para)) { continue; }
                        Rectangle o = LaidOutBounds(other);
                        if (o.IntersectsWith(after) && !o.IntersectsWith(before)) { fits = false; break; }
                    }
                }
                if (!fits)
                {
                    para.MaximumSize = oldMax;
                    para.Size = oldSize;
                    return false;
                }
                Utils.Logger.Info("[layout] re-wrapped \"" + Shorten(para.Text) + "\" to " + width + "px inside \"" +
                                  Shorten(card.Text) + "\" — " + before.Height + " → " + after.Height + " px tall, every word kept.");
                return true;
            }
            catch (Exception ex)
            {
                Utils.Logger.Swallow("LayoutFitter.TryRewrap", ex);
                try { para.MaximumSize = oldMax; para.Size = oldSize; } catch { }
                return false;
            }
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

                // Each control's inked box once, not once per PAIR. It measures the caption's text,
                // and inside the pair loop that was ~20,000 measurements for 10,000 pairs: 193 of
                // the ~200 ms this check spent on the UI thread at every start.
                var boxes = new Rectangle[kids.Count];
                for (int i = 0; i < kids.Count; i++) { boxes[i] = CollisionBounds(kids[i]); }

                for (int i = 0; i < kids.Count; i++)
                {
                    if (IsOverlay(kids[i])) { continue; }
                    for (int k = i + 1; k < kids.Count; k++)
                    {
                        if (IsOverlay(kids[k])) { continue; }
                        // Two DOCKED siblings cannot overlap: the layout engine hands each one
                        // its own strip of the parent. Their rectangles only coincide before the
                        // first layout — which, on a start-to-tray launch, is when this runs:
                        // the header and the footer band both still sat at 0,0.
                        if (kids[i].Dock != DockStyle.None && kids[k].Dock != DockStyle.None) { continue; }
                        // Compare INKED text, not control bounds. A fixed-width label is
                        // routinely far wider than its caption — "Interval step (ms):" is
                        // given a 250px box for ~110px of text — so comparing Bounds
                        // reported a 146px "overlap" that is entirely empty box against
                        // empty box, in every language including English. What a reader
                        // can actually see collide is the glyphs.
                        _pairsCompared++;
                        Rectangle hit = Rectangle.Intersect(boxes[i], boxes[k]);
                        if (hit.Width < MinOverlapPx || hit.Height < MinOverlapPx) { continue; }
                        found++;
                        Utils.Logger.Warn("[layout] STILL OVERLAPPING in \"" + Shorten(container.Text) + "\": \"" +
                            Name(kids[i]) + "\"@" + kids[i].Left + "," + kids[i].Top + " and \"" +
                            Name(kids[k]) + "\"@" + kids[k].Left + "," + kids[k].Top +
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
        /// The width a button needs to draw its caption uncut. A RoundedButton answers for
        /// itself, because it knows what else it paints beside the text (a glyph, a nav icon,
        /// a shortcut digit); measuring the text alone under-counted the Start/Stop buttons by
        /// the width of their play/stop glyph.
        /// </summary>
        private static int ButtonNeeds(Button b)
        {
            // The widest caption it can show, not only the one it has now: see DeclareCaptions.
            int need = 0;
            foreach (string caption in CaptionsOf(b)) { need = Math.Max(need, ButtonNeedsFor(b, caption)); }
            return need;
        }

        /// <summary>What <paramref name="b"/> needs to draw <paramref name="caption"/> uncut.</summary>
        private static int ButtonNeedsFor(Button b, string caption)
        {
            if (b is RoundedButton rb) { return rb.CaptionWidthNeeded(ButtonClipPad, caption); }
            return TextRenderer.MeasureText(caption ?? string.Empty, b.Font).Width + ButtonClipPad;
        }

        /// <summary>
        /// The width a button would LIKE: for a glyph button, room for the icon as well as
        /// the caption. Not a fault when it is not met — the icon gives way (RoundedButton.
        /// GlyphFits) — but free space beside the button is used to bring it back.
        /// </summary>
        private static int ButtonWants(Button b)
        {
            int want = 0;
            foreach (string caption in CaptionsOf(b))
            {
                want = Math.Max(want, b is RoundedButton rb ? rb.CaptionWidthWanted(caption) : ButtonNeedsFor(b, caption));
            }
            return want;
        }

        private static int _textExamined;

        /// <summary>
        /// Text a control is SHOWING in English although the active language translates it —
        /// a lookup that was never made. See Localization.ShowsUntranslated for why this beats
        /// reading the source.
        ///
        /// Only controls whose text is the app's own words: labels, buttons, tick boxes, card
        /// titles, page titles, list-view column headers and menu items. Text boxes, combo
        /// boxes and list rows hold the USER's data — a profile called "Default" is not an
        /// untranslated button — so they are left out, as are custom controls that draw a
        /// name they were given.
        /// </summary>
        private static int CountEnglishText(Control root)
        {
            _textExamined = 0;
            if (Utils.Localization.Current == Models.Language.English) { return 0; }
            int found = 0;
            var stack = new Stack<Control>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                Control c = stack.Pop();
                foreach (Control child in c.Controls) { stack.Push(child); }

                if (c is Label || c is ButtonBase || c is GroupBox || c is TabPage)
                {
                    found += CheckShown(c, c.Text);
                }
                // A window's title, the items of a status or menu bar, every tooltip and every caption a
                // control switches to later: the app's own words, reaching the screen by roads the
                // types above never covered.
                if (c is Form) { found += CheckShown(c, c.Text); }
                if (c is ToolStrip strip && !(c is ToolStripDropDown)) { found += CheckMenu(c, strip.Items); }
                foreach (string tip in TipStyle.TextsFor(c)) { found += CheckShown(c, tip); }
                if (HasDeclaredCaptions(c))
                {
                    foreach (string caption in CaptionsOf(c))
                    {
                        if (!string.Equals(caption, c.Text, StringComparison.Ordinal)) { found += CheckShown(c, caption); }
                    }
                }
                if (c is ListView lv)
                {
                    foreach (ColumnHeader h in lv.Columns) { found += CheckShown(c, h.Text); }
                }
                if (c.ContextMenuStrip != null)
                {
                    found += CheckMenu(c, c.ContextMenuStrip.Items);
                }
            }
            return found;
        }

        private static int CheckMenu(Control owner, ToolStripItemCollection items)
        {
            int n = 0;
            foreach (ToolStripItem item in items)
            {
                n += CheckShown(owner, item.Text);
                if (item is ToolStripDropDownItem dd && dd.HasDropDownItems) { n += CheckMenu(owner, dd.DropDownItems); }
            }
            return n;
        }

        private static int CheckShown(Control where, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) { return 0; }
            _textExamined++;
            Control container = where.Parent ?? where;
            if (Utils.Localization.ShowsUntranslated(text))
            {
                Utils.Logger.Warn("[layout] SHOWS ENGLISH in \"" + Shorten(container.Text) + "\": \"" + Shorten(text) +
                                  "\" (" + where.GetType().Name + ") — " + Utils.Localization.Current +
                                  " has a translation for it, but it was never looked up.");
                return 1;
            }
            // English that has no translation at all — built at run time, or never given a key — which the
            // check above cannot see: it only knows the phrases the tables hold.
            if (Utils.Localization.LooksEnglish(text))
            {
                Utils.Logger.Warn("[layout] LOOKS ENGLISH in \"" + Shorten(container.Text) + "\": \"" + Shorten(text) +
                                  "\" (" + where.GetType().Name + ") — no " + Utils.Localization.Current +
                                  " translation exists for it.");
                return 1;
            }
            return 0;
        }

        /// <summary>How many fixed-size labels the last CountCutText measured.</summary>
        private static int _cutExamined;

        /// <summary>
        /// Fixed-size labels whose text does not fit their box, below root.
        ///
        /// A label switched off AutoSize wraps inside the size it was given and simply stops
        /// drawing at its bottom edge: no "…", no tooltip — the rest of the sentence is not
        /// there. No other check can see it, because the BOX is exactly where it was put, so
        /// a translated warning needing three lines in a two-line box passed as a clean layout
        /// in every language. Labels that ellipsize (AutoEllipsis) shorten by design and carry
        /// the full text on hover, so they are left out.
        /// </summary>
        private static int CountCutText(Control root)
        {
            int found = 0;
            _cutExamined = 0;
            var stack = new Stack<Control>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                Control c = stack.Pop();
                foreach (Control child in c.Controls) { stack.Push(child); }

                if (c is CheckBox || c is RadioButton)
                {
                    if (IsFixedCaption(c))
                    {
                        _cutExamined++;
                        found += CountCutCaption(c);
                    }
                    continue;
                }

                if (!(c is Label l) || !IsFixedTextBox(l)) { continue; }
                _cutExamined++;
                // Every text it can show (DeclareCaptions), not only the one it has now; one fault each.
                foreach (string text in CaptionsOf(l))
                {
                    if (!TextCut(l, text, out Size need, out Size room)) { continue; }
                    found++;
                    Control container = l.Parent ?? l;
                    bool later = !string.Equals(text, l.Text, StringComparison.Ordinal);
                    Utils.Logger.Warn("[layout] TEXT CUT in \"" + Shorten(container.Text) + "\": \"" + Shorten(text) +
                                      "\"@" + l.Left + "," + l.Top + " needs " + need.Width + "x" + need.Height +
                                      " px but its box is " + room.Width + "x" + room.Height + " — the rest is not drawn" +
                                      (later ? " (a text it switches to; it shows \"" + Shorten(l.Text) + "\" now)." : "."));
                    break;
                }
            }
            return found;
        }

        /// <summary>A label that wraps inside a size it was given, and has text to wrap.</summary>
        private static bool IsFixedTextBox(Label l) =>
            !(l is LinkLabel) && !l.AutoSize && !l.AutoEllipsis && l.Image == null &&
            !string.IsNullOrWhiteSpace(l.Text) && l.Width > 0 && l.Height > 0;

        /// <summary>
        /// A tick box, radio button or toggle given a fixed width — one this pass did not shorten on
        /// purpose. Its caption is drawn on one line inside that width and simply ends in "…" when it
        /// does not fit, which no other check measured: the caption pass only looks at AutoSize captions,
        /// the cut-text pass only at labels, and the button pass only at buttons.
        /// </summary>
        private static bool IsFixedCaption(Control c)
        {
            if (c.AutoSize || string.IsNullOrWhiteSpace(c.Text) || c.Width <= 0 || c.Height <= 0) { return false; }
            if (IsClamped(c)) { return false; }
            if (c is CheckBox cb && cb.Appearance == Appearance.Button) { return false; }
            if (c is RadioButton rb && rb.Appearance == Appearance.Button) { return false; }
            return true;
        }

        /// <summary>1 when a fixed-width tick box cuts one of its captions (logged), else 0.</summary>
        private static int CountCutCaption(Control c)
        {
            // Where the words start: the control's own answer, or a native box and its gap.
            int lead = c is ICaptionOffset own ? own.CaptionLeft : 24;
            // Tempo's own tick boxes (the ones that say where their caption starts) paint ONE line and end
            // it in "…". Windows' own wrap the words onto as many lines as their height holds — a tall
            // fixed box is how a two-line caption is made — so for those it is the height that runs out.
            bool oneLine = c is ICaptionOffset;
            TextFormatFlags flags = TextFormatFlags.WordBreak | (UsesMnemonic(c) ? 0 : TextFormatFlags.NoPrefix);
            foreach (string text in CaptionsOf(c))
            {
                // The box and the words it paints are screen pixels inside the control; its Width is
                // in the window's units (U converts, ToPx goes back).
                string need;
                if (oneLine)
                {
                    int w = U(lead + TextRenderer.MeasureText(text, c.Font).Width + 2);
                    if (w <= c.Width + 2) { continue; }
                    need = w + " px but its box is " + c.Width + " px";
                }
                else
                {
                    Size s = U(TextRenderer.MeasureText(text, c.Font, new Size(Math.Max(1, ToPx(c.Width) - lead), int.MaxValue), flags));
                    if (s.Height <= c.Height + 2 && s.Width + U(lead) <= c.Width + 2) { continue; }
                    need = (U(lead) + s.Width) + "x" + s.Height + " px but its box is " + c.Width + "x" + c.Height;
                }
                Control container = c.Parent ?? c;
                bool later = !string.Equals(text, c.Text, StringComparison.Ordinal);
                Utils.Logger.Warn("[layout] CAPTION CUT in \"" + Shorten(container.Text) + "\": \"" + Shorten(text) +
                                  "\"@" + c.Left + "," + c.Top + " needs " + need + " — the end of it is not drawn" +
                                  (later ? " (a caption it switches to; it shows \"" + Shorten(c.Text) + "\" now)." : "."));
                return 1;
            }
            return 0;
        }

        private static bool UsesMnemonic(Control c) => c is ButtonBase b ? b.UseMnemonic : !(c is Label l) || l.UseMnemonic;

        /// <summary>
        /// Whether <paramref name="l"/>'s words run past its box: <paramref name="need"/> is the
        /// size they take wrapped at its width, <paramref name="room"/> the box.
        ///
        /// A little over the height is not "cut": a single line's box includes blank leading
        /// above the letters and room for descenders below them, and a big-font status word
        /// ("IDLE", 47 px of line in a 42 px box) loses none of its ink. More than ~15% of a
        /// line starts clipping letters; a whole line missing is the usual case.
        /// </summary>
        private static bool TextCut(Label l, out Size need, out Size room) => TextCut(l, l.Text, out need, out room);

        /// <summary><see cref="TextCut(Label, out Size, out Size)"/> for a text the label can show besides its current one.</summary>
        private static bool TextCut(Label l, string text, out Size need, out Size room)
        {
            room = new Size(l.ClientSize.Width - l.Padding.Horizontal, l.ClientSize.Height - l.Padding.Vertical);
            need = Size.Empty;
            TextFormatFlags flags = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl;
            if (!l.UseMnemonic) { flags |= TextFormatFlags.NoPrefix; }
            int line;
            try
            {
                // Wrapped at the box's width in screen pixels, and read back in the window's units.
                need = U(TextRenderer.MeasureText(text ?? string.Empty, l.Font, new Size(ToPx(Math.Max(1, room.Width)), int.MaxValue), flags));
                // One line, with no width to wrap in: Size.Empty with WordBreak is a width of
                // nothing, which breaks even "Ag" in two and doubled this reference.
                line = U(TextRenderer.MeasureText("Ag", l.Font, new Size(int.MaxValue, int.MaxValue), flags).Height);
            }
            catch { return false; }
            const int Tolerance = 2;      // rounding
            return need.Width > room.Width + Tolerance
                || need.Height - room.Height > Math.Max(Tolerance, line * 15 / 100);
        }

        /// <summary>How many fixed text boxes the start-up pass made taller, or ended with "…".</summary>
        private static int _grown, _ellipsized;

        /// <summary>
        /// A fixed-size label whose words run past its box (see <see cref="CountCutText"/>): made tall
        /// enough when it reads from the top and the card has room below it — every word shown — or
        /// else ended with "…" and given its whole text as a tip, kept current as the text changes.
        /// Either beats what it did before, which was to stop drawing at its bottom edge with nothing
        /// to say the rest existed. Returns true when it changed the label.
        /// </summary>
        private static bool RepairCutText(Control card, Label l, List<Control> kids)
        {
            if (!IsFixedTextBox(l) || !TextCut(l, out Size need, out Size room)) { return false; }

            bool topAligned = l.TextAlign == ContentAlignment.TopLeft || l.TextAlign == ContentAlignment.TopCenter ||
                              l.TextAlign == ContentAlignment.TopRight;
            if (topAligned && need.Width <= room.Width + 2)
            {
                Rectangle before = LaidOutBounds(l);
                var after = new Rectangle(before.X, before.Y, before.Width, before.Height + (need.Height - room.Height));
                bool scrolls = card is ScrollableControl sc && sc.AutoScroll;
                bool fits = scrolls || after.Bottom <= card.ClientSize.Height - 4;
                if (fits)
                {
                    foreach (Control other in kids)
                    {
                        if (ReferenceEquals(other, l)) { continue; }
                        Rectangle o = LaidOutBounds(other);
                        if (o.IntersectsWith(after) && !o.IntersectsWith(before)) { fits = false; break; }
                    }
                }
                if (fits)
                {
                    l.Height = after.Height;
                    _grown++;
                    Utils.Logger.Info("[layout] grew \"" + Shorten(l.Text) + "\" inside \"" + Shorten(card.Text) + "\" to " +
                                      after.Height + " px so every line shows (was " + before.Height + ").");
                    return true;
                }
            }

            l.AutoEllipsis = true;
            TipStyle.FollowText(l);
            _ellipsized++;
            Utils.Logger.Info("[layout] ended \"" + Shorten(l.Text) + "\" inside \"" + Shorten(card.Text) + "\" with … — it needs " +
                              need.Width + "x" + need.Height + " px and has no room to grow past " + room.Width + "x" + room.Height +
                              "; the whole text is on hover.");
            return true;
        }

        /// <summary>
        /// What the last CountPaintedText looked at: painted texts, and children compared against them —
        /// and how many children it found sitting on one.
        /// </summary>
        private static int _paintedExamined, _paintedCompared, _paintedCovered;

        /// <summary>
        /// Text a control PAINTS itself — a card's title, a stat card's caption, the status pill's word —
        /// measured like any other: cut when it runs out of the room it is drawn in, covered when a child
        /// control sits on it. Returns how many are cut; how many are covered is left in _paintedCovered.
        ///
        /// Every other check reads child controls, so text with no control of its own was invisible to
        /// all of them. A card's title is drawn upper-case and letter-spaced, a good third wider than the
        /// same words in a label, and nothing clips it short of the card's own edge: a long translated
        /// title could run off its card — or a control placed a little too high could sit on it — in a
        /// layout every language called clean.
        /// </summary>
        private static int CountPaintedText(Control root)
        {
            const int Tolerance = 2;
            const int MinCoverPx = 3;
            int cut = 0;
            _paintedExamined = 0;
            _paintedCompared = 0;
            _paintedCovered = 0;
            Graphics g = null;
            try
            {
                var stack = new Stack<Control>();
                stack.Push(root);
                while (stack.Count > 0)
                {
                    Control c = stack.Pop();
                    foreach (Control child in c.Controls) { stack.Push(child); }
                    if (!(c is IPaintedText painter) || c.Width <= 0 || c.Height <= 0) { continue; }
                    if (g == null) { g = Graphics.FromHwnd(IntPtr.Zero); }

                    string where = Shorten((c.Parent ?? c).Text);
                    // In the window's units: the control paints in pixels, its size is a layout number.
                    foreach (PaintedText p in painter.PaintedTexts(g, _u))
                    {
                        if (string.IsNullOrWhiteSpace(p.Text) || p.Ink.Width <= 0 || p.Ink.Height <= 0) { continue; }
                        _paintedExamined++;

                        int overRight = p.Ink.Right - p.Room.Right;
                        int overLeft = p.Room.Left - p.Ink.Left;
                        if (overRight > Tolerance || overLeft > Tolerance)
                        {
                            cut++;
                            Utils.Logger.Warn("[layout] TEXT CUT in \"" + where + "\": \"" + Shorten(p.Text) + "\", painted by a " +
                                              c.GetType().Name + "@" + c.Left + "," + c.Top + ", needs " + p.Ink.Width + " px but has " +
                                              p.Room.Width + " — " + Math.Max(overRight, overLeft) + " px of it is not drawn.");
                        }

                        foreach (Control child in c.Controls)
                        {
                            if (child.Width <= 0 || child.Height <= 0 || !IsCollidable(child) || IsOverlay(child)) { continue; }
                            _paintedCompared++;
                            Rectangle hit = Rectangle.Intersect(CollisionBounds(child), p.Ink);
                            if (hit.Width < MinCoverPx || hit.Height < MinCoverPx) { continue; }
                            _paintedCovered++;
                            Utils.Logger.Warn("[layout] STILL OVERLAPPING in \"" + where + "\": \"" + Name(child) + "\"@" +
                                              child.Left + "," + child.Top + " sits on \"" + Shorten(p.Text) + "\", painted by the " +
                                              c.GetType().Name + " under it, by " + hit.Width + "x" + hit.Height + "px.");
                        }
                    }
                }
            }
            catch (Exception ex) { Utils.Logger.Swallow("LayoutFitter.CountPaintedText", ex); }
            finally { g?.Dispose(); }
            return cut;
        }

        /// <summary>How many combo-box choices and column titles the last CountCutItems measured.</summary>
        private static int _itemsExamined;

        /// <summary>
        /// Combo boxes whose choices do not fit them, and list columns whose titles do not fit theirs.
        ///
        /// Both are fixed widths holding translated words, and no check measured either: a combo box is
        /// an input, so the overlap pass sees its box and not its text, and a column title is not a
        /// control at all. A choice too wide for the closed box ends in "…" whenever it is the one
        /// chosen; a title too wide for its column ends in "…" for as long as the list is on screen.
        /// Only Tempo's own words are judged (Localization.IsAppText) — a profile, macro or device name
        /// is the user's, and how much of it fits is not a layout fault.
        /// </summary>
        private static int CountCutItems(Control root)
        {
            const int Tolerance = 2;
            int found = 0;
            _itemsExamined = 0;
            var stack = new Stack<Control>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                Control c = stack.Pop();
                foreach (Control child in c.Controls) { stack.Push(child); }
                if (c.Width <= 0 || c.Height <= 0) { continue; }

                if (c is ComboBox cb && cb.Items.Count > 0)
                {
                    var flat = cb as FlatComboBox;
                    // A native box keeps its drop button and a few pixels of edge. In the window's units:
                    // the box's width is a layout number, its arrow and padding are pixels it paints.
                    int closed = flat != null
                        ? (_u == 1f ? flat.ClosedTextRoom : flat.ClosedTextRoomIn(_u))
                        : cb.Width - U(SystemInformation.VerticalScrollBarWidth + 6);
                    int open = flat != null
                        ? (_u == 1f ? flat.ListTextRoom : flat.ListTextRoomIn(_u))
                        : cb.DropDownWidth - U(8 + (cb.Items.Count > cb.MaxDropDownItems ? SystemInformation.VerticalScrollBarWidth : 0));
                    // Declared: its closed box may end a long, descriptive choice in "…" (the open list
                    // and the hover still show it whole). Only a choice the open list cuts is a fault.
                    bool mayShorten = flat != null && _shortChoices.TryGetValue(cb, out _);
                    string worst = null;
                    int worstNeed = 0, cutCount = 0, shortened = 0;
                    bool cutOpen = false;
                    foreach (object item in cb.Items)
                    {
                        string s = cb.GetItemText(item);
                        if (!Utils.Localization.IsAppText(s)) { continue; }
                        _itemsExamined++;
                        int need = U(flat != null ? flat.ChoiceWidth(s) : TextRenderer.MeasureText(s, cb.Font).Width);
                        bool cutClosed = need > closed + Tolerance;
                        bool cutList = need > open + Tolerance;
                        if (!cutClosed && !cutList) { continue; }
                        if (!cutList && mayShorten) { shortened++; continue; }
                        cutCount++;
                        if (need > worstNeed) { worstNeed = need; worst = s; }
                        cutOpen |= cutList;
                    }
                    Control container = cb.Parent ?? cb;
                    if (cutCount > 0)
                    {
                        found++;
                        Utils.Logger.Warn("[layout] ITEM CUT in \"" + Shorten(container.Text) + "\": the combo box @" + cb.Left + "," +
                                          cb.Top + " cuts " + cutCount + " of its choices — \"" + Shorten(worst) + "\" needs " + worstNeed +
                                          " px, the closed box shows " + closed + (cutOpen ? " and the open list " + open : "") + ".");
                    }
                    else if (shortened > 0)
                    {
                        // Debug: this is the declared, expected state, repeated at every start.
                        Utils.Logger.Debug("[layout] " + shortened + " long choice(s) of the combo box @" + cb.Left + "," + cb.Top + " in \"" +
                                          Shorten(container.Text) + "\" end in … when chosen, as declared — the open list shows them whole, " +
                                          "and so does the hover.");
                    }
                }

                if (c is ListView lv && lv.View == View.Details)
                {
                    var themed = lv as ThemedListView;
                    foreach (ColumnHeader h in lv.Columns)
                    {
                        if (string.IsNullOrWhiteSpace(h.Text) || h.Width <= 0) { continue; }
                        _itemsExamined++;
                        // A native header pads its title by about 6 px a side.
                        int need = U(themed != null ? themed.HeaderTextNeed(h) : TextRenderer.MeasureText(h.Text, lv.Font).Width);
                        int room = themed != null
                            ? (_u == 1f ? themed.HeaderTextRoom(h) : themed.HeaderTextRoomIn(h, _u))
                            : h.Width - U(12);
                        if (need <= room + Tolerance) { continue; }
                        found++;
                        Control container = lv.Parent ?? lv;
                        Utils.Logger.Warn("[layout] ITEM CUT in \"" + Shorten(container.Text) + "\": the column title \"" +
                                          Shorten(h.Text) + "\" needs " + need + " px, its " + h.Width + " px column leaves " + room + ".");
                    }
                }
            }
            return found;
        }

        /// <summary>How many texts the last CountHiddenAmpersands read.</summary>
        private static int _ampExamined;

        /// <summary>
        /// Text whose "&amp;" the control does not show as written.
        ///
        /// Two opposite mistakes, one rule: say what the control does with "&amp;". A label, or a tick box
        /// drawn with keyboard shortcuts on, takes a lone "&amp;" as a shortcut marker — it vanishes and
        /// underlines the next letter: the API dialog showed "&amp;placeId=ID&amp;privateServer=LINK" as
        /// "placeId=IDprivateServer=LINK". A control that draws its text exactly as written shows "&amp;&amp;"
        /// as two. No bounds check can see a character that is not drawn, or one drawn twice.
        /// </summary>
        private static int CountHiddenAmpersands(Control root)
        {
            int found = 0;
            _ampExamined = 0;
            var stack = new Stack<Control>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                Control c = stack.Pop();
                foreach (Control child in c.Controls) { stack.Push(child); }

                bool? eats = EatsAmpersand(c);
                if (eats.HasValue)
                {
                    foreach (string text in CaptionsOf(c))
                    {
                        _ampExamined++;
                        if (AmpersandWrong(text, eats.Value)) { found++; ReportAmpersand(c, text, eats.Value); break; }
                    }
                }
                // Combo choices and column titles are drawn as written (see FlatComboBox, ThemedListView).
                if (c is ComboBox cb)
                {
                    foreach (object item in cb.Items)
                    {
                        string s = cb.GetItemText(item);
                        if (string.IsNullOrEmpty(s)) { continue; }
                        _ampExamined++;
                        if (AmpersandWrong(s, false)) { found++; ReportAmpersand(c, s, false); break; }
                    }
                }
                if (c is ListView lv)
                {
                    foreach (ColumnHeader h in lv.Columns)
                    {
                        if (string.IsNullOrEmpty(h.Text)) { continue; }
                        _ampExamined++;
                        if (AmpersandWrong(h.Text, false)) { found++; ReportAmpersand(c, h.Text, false); }
                    }
                }
            }
            return found;
        }

        /// <summary>
        /// Whether <paramref name="c"/> takes "&amp;" as a shortcut marker (true), draws it as written
        /// (false), or shows no text of its own here (null).
        /// </summary>
        private static bool? EatsAmpersand(Control c)
        {
            // Painted by Tempo with NoPrefix: exactly as written.
            if (c is RoundedButton) { return false; }
            // Painted by Tempo WITHOUT NoPrefix, whatever UseMnemonic says.
            if (c is ModernCheckBox || c is ToggleSwitch || c is ModernRadioButton) { return true; }
            if (c is Label l) { return l.UseMnemonic; }
            if (c is ButtonBase b) { return b.UseMnemonic; }
            return null;
        }

        /// <summary>A lone "&amp;" where it would be eaten, or a doubled one where it is drawn as written.</summary>
        private static bool AmpersandWrong(string text, bool eats)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('&') < 0) { return false; }
            return eats ? text.Replace("&&", "").IndexOf('&') >= 0 : text.Contains("&&");
        }

        private static void ReportAmpersand(Control c, string text, bool eats)
        {
            Control container = c.Parent ?? c;
            Utils.Logger.Warn("[layout] " + (eats ? "HIDES AN &" : "DOUBLED &") + " in \"" + Shorten(container.Text) + "\": \"" +
                              Shorten(text) + "\" (" + c.GetType().Name + ") — " + (eats
                                  ? "a lone \"&\" is taken for a shortcut marker: it is not shown, and the letter after it is underlined."
                                  : "this control draws its text as written, so \"&&\" shows as two."));
        }

        /// <summary>Glyph buttons that are drawing their caption without the icon, below root.</summary>
        private static int CountDroppedIcons(Control root)
        {
            int n = 0;
            foreach (Control container in Containers(root))
            {
                foreach (Control c in container.Controls)
                {
                    if (c is RoundedButton rb && rb.Glyph != ActionGlyph.None && !rb.GlyphFits && rb.Width > 0)
                    {
                        n++;
                        Utils.Logger.Info("[layout] icon left off \"" + Shorten(rb.Text) + "\" so its caption fits (" +
                                          rb.CaptionWidthWanted() + "px wanted, " + rb.Width + "px available).");
                    }
                }
            }
            return n;
        }

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
                    // Every caption it can show (DeclareCaptions), and the worst of them named.
                    string worst = b.Text;
                    int need = 0;
                    foreach (string caption in CaptionsOf(b))
                    {
                        int n = U(ButtonNeedsFor(b, caption));
                        if (n > need) { need = n; worst = caption; }
                    }
                    if (need <= b.Width) { continue; }
                    found++;
                    bool later = !string.Equals(worst, b.Text, StringComparison.Ordinal);
                    Utils.Logger.Warn("[layout] CLIPPED BUTTON in \"" + Shorten(container.Text) + "\": \"" +
                        Shorten(worst) + "\" needs " + need + "px but is " + b.Width + "px" +
                        (later ? " — a caption it switches to (it shows \"" + Shorten(b.Text) + "\" now)." : "."));
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
                var scroll = container as ScrollableControl;
                bool scrolls = scroll != null && scroll.AutoScroll;

                // A WINDOW's own controls were never checked at all: a dialog's button pushed off its
                // right edge by a longer caption — the "last control in a row" shape, one level up —
                // passed in every language. A window that scrolls (ScreenFit makes one that does not fit
                // the screen scroll) holds more than it shows by design, like a scrolling page; its
                // docked parts are placed by the window itself.
                bool window = container is Form;
                if (window && scrolls) { continue; }

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

                // A scrolled page moves its children up and left by however far it has scrolled:
                // measure where each one sits in the PAGE, not in the view of it.
                Point origin = scrolls ? new Point(-scroll.AutoScrollPosition.X, -scroll.AutoScrollPosition.Y) : Point.Empty;

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
                    if (window && c.Dock != DockStyle.None) { continue; }
                    _overflowExamined++;

                    Rectangle laid = LaidOutBounds(c);
                    // An AutoSize control grows to the widest caption it can switch to.
                    laid.Width = ProjectedWidth(c, laid.Width);
                    laid.Offset(origin);
                    int overRight = laid.Right - limitW;
                    int overBottom = limitH == int.MaxValue ? 0 : laid.Bottom - limitH;

                    // Past the LEFT or TOP edge was never checked either — and no scrollbar can reach
                    // it, because a page only scrolls right and down. It is what a right-anchored or
                    // centred control does when its container is narrower than it was designed for: a
                    // laptop. Docked controls are placed by the layout engine, and one WHOLLY outside
                    // was put there on purpose (parked until it slides in); nobody sees half of it.
                    bool placed = c.Dock == DockStyle.None && laid.Right > 0 && laid.Bottom > 0;
                    int overLeft = placed ? -laid.Left : 0;
                    int overTop = placed ? -laid.Top : 0;
                    if (overRight <= Tolerance && overBottom <= Tolerance && overLeft <= Tolerance && overTop <= Tolerance) { continue; }

                    found++;
                    var edges = new List<string>();
                    if (overRight > Tolerance) { edges.Add(overRight + "px past the right edge"); }
                    if (overBottom > Tolerance) { edges.Add(overBottom + "px past the bottom"); }
                    if (overLeft > Tolerance) { edges.Add(overLeft + "px past the left edge"); }
                    if (overTop > Tolerance) { edges.Add(overTop + "px past the top"); }
                    Utils.Logger.Warn("[layout] OVERFLOWS \"" + Shorten(container.Text) + "\": \"" +
                        Name(c) + "\"@" + c.Left + "," + c.Top + " " + laid.Width + "x" + laid.Height +
                        " runs " + string.Join(" and ", edges) + " of a " + limitW + "x" +
                        (limitH == int.MaxValue ? "unbounded" : limitH.ToString()) + " box.");
                }
            }
            return found;
        }

        /// <summary>
        /// The width an AutoSize control takes when it shows the widest caption declared for it — it
        /// grows to fit its text — or <paramref name="width"/> for anything else. The padding around the
        /// text stays what it is now; only the text's own width changes.
        /// </summary>
        private static int ProjectedWidth(Control c, int width)
        {
            if (!c.AutoSize || !HasDeclaredCaptions(c)) { return width; }
            // In a flow or table layout a control that grows pushes its neighbours along: it cannot
            // run into them. (Projecting it in place reported Live debug's Copy button — "Copied ✓" —
            // on top of Save, in a strip that simply re-flows.)
            if (c.Parent is FlowLayoutPanel || c.Parent is TableLayoutPanel) { return width; }
            try
            {
                int now = TextRenderer.MeasureText(c.Text ?? string.Empty, c.Font).Width;
                int widest = now;
                foreach (string s in CaptionsOf(c)) { widest = Math.Max(widest, TextRenderer.MeasureText(s, c.Font).Width); }
                return width + U(widest - now);
            }
            catch { return width; }
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
                if (!string.IsNullOrEmpty(c.Text)) { return true; }
                // An empty caption usually means nothing is there. A text SLOT is the
                // exception: see IsTextSlot.
                return c is Label slot && IsTextSlot(slot);
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

            // Everything else paints its rectangle, and so can be painted over or paint over
            // something else.
            //
            // This used to be a list — NumericUpDown, ComboBox, TextBox, Button, TrackBar —
            // and a list is a blind spot for everything left off it. Eleven of Tempo's own
            // controls derive straight from Control, so none of them matched: SmoothTrackBar,
            // ThemedProgressBar, StatCard, StatusPill, ProfileCard, the charts and meters, the
            // verified badge — and the list views and list boxes as well. SmoothTrackBar is
            // the one that already cost something: left at the default 45 px height it
            // painted straight over the "Unlock max speed" checkbox, both sat inside their
            // card, and this check reported 0 overlaps in all six languages. A screenshot
            // caught it. It was put down to "bounds, not paint order"; it was this list.
            return true;
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
            return c.HasChildren ? LaidOutBounds(c) : InkedBounds(c);
        }

        /// <summary>
        /// A control's bounds as they WILL be on screen. A FlatComboBox is built without its window
        /// (see its constructor: that was half of start-up), and until it has one WinForms reports the
        /// height of its FONT — 23 px — where Windows will make it 30, and will not let it be told
        /// otherwise. Everything here that reads a height reads it through this, so a combo on a tab
        /// nobody has opened is still checked at the size it will really have.
        /// </summary>
        private static Rectangle LaidOutBounds(Control c)
        {
            Rectangle b = c.Bounds;
            if (c is FlatComboBox fc && !fc.IsHandleCreated)
            {
                int h = fc.ClosedHeight;
                if (h > b.Height) { b.Height = h; }
            }
            // A text box, spinner or combo box is auto-sized in HEIGHT only — one line of its font — and its
            // width is a layout number like any placed control's. (Converting the width as well put every
            // text box half as wide again as it is, at 150%: 80 overlaps that were not there.)
            bool heightOnly = c is TextBoxBase || c is UpDownBase || c is ComboBox;
            if (c.AutoSize && !heightOnly && !c.HasChildren && !(c is Label wrap && wrap.MaximumSize.Width > 0) &&
                SelfSize.Applies(c))
            {
                // The size it measures, not the one it has now: WinForms' DPI scaling sets every control to
                // 1.5× its bounds and leaves an auto-sized one to its container's next layout to be brought
                // back — 52 tick boxes were still 1.5× when the start-up check ran. A native one is still
                // left so (Tempo's own refuse it now, SelfSize), and it is laid out at this size.
                try { b.Size = c.GetPreferredSize(Size.Empty); } catch { }
            }
            if (_u != 1f)
            {
                // Sized by its own text — or, for a combo box, its font — so in screen pixels, not in the
                // window's units (see SetUnitsPerPixel). A label wrapping inside a MaximumSize is measured:
                // that width is a layout number, and it wraps at 1.5× the pixels once the window is scaled.
                if (c is Label l && l.AutoSize && l.MaximumSize.Width > 0)
                {
                    TextFormatFlags flags = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl |
                                            (l.UseMnemonic ? 0 : TextFormatFlags.NoPrefix);
                    Size s = U(TextRenderer.MeasureText(l.Text ?? string.Empty, l.Font,
                        new Size(ToPx(Math.Max(1, l.MaximumSize.Width - l.Padding.Horizontal)), int.MaxValue), flags));
                    b.Width = Math.Min(l.MaximumSize.Width, s.Width + l.Padding.Horizontal);
                    b.Height = s.Height + l.Padding.Vertical;
                }
                else if (heightOnly)
                {
                    if (!(c is TextBoxBase tb) || (tb.AutoSize && !tb.Multiline)) { b.Height = U(b.Height); }
                }
                else if (c.AutoSize)
                {
                    b.Width = U(b.Width);
                    b.Height = U(b.Height);
                }
            }
            return b;
        }

        /// <summary>
        /// A label that is a place for text which arrives LATER: a fixed size with an ellipsis,
        /// which is how a live status line is built ("● Active — managing 1 running Roblox
        /// client"). It is empty when this check runs, so the rule "no text, nothing to collide"
        /// waved it through — and the multi-instance status was placed under the end of the
        /// checkbox caption beside it, hiding "● Ac", in a row this check called clean. Its box is
        /// what matters: the text will be laid out in it, and its background already covers it.
        /// </summary>
        private static bool IsTextSlot(Label l) => l != null && !l.AutoSize && l.AutoEllipsis;

        /// <summary>
        /// What the control actually PAINTS. Text controls ink only as far as their
        /// caption reaches; an input paints its whole box, so its bounds are its ink.
        /// </summary>
        private static Rectangle InkedBounds(Control c)
        {
            Rectangle b = LaidOutBounds(c);
            // As wide as the widest caption it can switch to, for one that grows to fit its text.
            b.Width = ProjectedWidth(c, b.Width);
            if (!(c is CheckBox || c is RadioButton || c is Label)) { return b; }
            // An empty slot has no ink YET; what it will have is anywhere in its box, and its
            // background is painted over the whole box already.
            if (c is Label slot && string.IsNullOrEmpty(c.Text) && IsTextSlot(slot)) { return b; }
            try
            {
                int lead = 0;
                if (c is ICaptionOffset own)
                {
                    // The control's own answer, from the constants it paints with: 20 px for
                    // a ModernCheckBox, 46 px for a ToggleSwitch.
                    lead = own.CaptionLeft;
                }
                else if (c is CheckBox || c is RadioButton)
                {
                    lead = 24;   // a native tick box + gap before the caption starts
                }
                int textWidth = 0;
                foreach (string s in CaptionsOf(c)) { textWidth = Math.Max(textWidth, TextRenderer.MeasureText(s, c.Font).Width); }
                // The box, the gap and the words are pixels the control paints: in the window's units.
                int inked = Math.Min(b.Width, U(lead + textWidth + 2));
                if (inked > 0 && inked < b.Width)
                {
                    // WHERE the ink is, not only how wide. A right- or centre-aligned label
                    // paints at the right or the middle of its box, and measuring it as if it
                    // started at the left edge put the "ink" where there was none — reporting
                    // a collision with empty box on one side while missing the real text on
                    // the other.
                    if (c is Label lbl)
                    {
                        switch (lbl.TextAlign)
                        {
                            case ContentAlignment.TopRight:
                            case ContentAlignment.MiddleRight:
                            case ContentAlignment.BottomRight:
                                b.X = b.Right - inked;
                                break;
                            case ContentAlignment.TopCenter:
                            case ContentAlignment.MiddleCenter:
                            case ContentAlignment.BottomCenter:
                                b.X += (b.Width - inked) / 2;
                                break;
                        }
                    }
                    b.Width = inked;
                }
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

        /// <summary>
        /// How a control is named in a fault line: its caption, or — for the sliders, meters
        /// and charts this check now also covers, which have no caption — its type and
        /// name, so the line still says which control to go and look at.
        /// </summary>
        private static string Name(Control c)
        {
            if (c == null) { return ""; }
            if (!string.IsNullOrEmpty(c.Text)) { return Shorten(c.Text); }
            return "[" + c.GetType().Name + (string.IsNullOrEmpty(c.Name) ? "" : " " + c.Name) + "]";
        }

        private static string Shorten(string s)
        {
            if (string.IsNullOrEmpty(s)) { return ""; }
            s = s.Replace("\n", " ");
            return s.Length <= 46 ? s : s.Substring(0, 44) + "…";
        }
    }

    /// <summary>
    /// A control that PAINTS text of its own — no child label carries it — and says where, so the
    /// layout checks can measure it like any other text (LayoutFitter.CountPaintedText).
    /// </summary>
    internal interface IPaintedText
    {
        /// <summary>
        /// Each piece of text the control paints: the words, where their ink falls and the room they must
        /// stay inside, in the control's own coordinates. <paramref name="g"/> is a screen graphics to
        /// measure with. Everything the control PAINTS (its offsets, its text) is screen pixels and is
        /// multiplied by <paramref name="unitsPerPixel"/>; its own Width and Height are already in the
        /// layout's units (LayoutFitter.SetUnitsPerPixel — 1 once the window is scaled).
        /// </summary>
        IEnumerable<PaintedText> PaintedTexts(Graphics g, float unitsPerPixel);
    }

    /// <summary>One piece of painted text (see <see cref="IPaintedText"/>).</summary>
    internal readonly struct PaintedText
    {
        public readonly string Text;
        public readonly Rectangle Ink;
        public readonly Rectangle Room;

        public PaintedText(string text, Rectangle ink, Rectangle room)
        {
            Text = text;
            Ink = ink;
            Room = room;
        }
    }
}
