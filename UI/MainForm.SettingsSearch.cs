using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// Two things the Settings tab had grown too long to live without: cards that lay
    /// themselves out, and a way to find one setting among sixty.
    ///
    /// The tab is a ~2,100px scroll of eight cards, and every card's Y was a hand-written
    /// constant. The comments in BuildSettingsTab record what that cost — "these y
    /// positions must move whenever a group above grows", "they were left behind twice",
    /// "resizing a card here silently pushed it under the next one, hiding that card's
    /// title and half its first row". Nothing threw; it just looked broken, and only in a
    /// screenshot. <see cref="RestackSettingsCards"/> derives the positions instead, so
    /// that whole class of bug stops being possible and a card can be resized freely.
    /// </summary>
    public partial class MainForm
    {
        private BackdropTabPage _settingsPage;
        private TextBox _settingsSearch;
        private Label _settingsSearchInfo;

        /// <summary>Card holding the first hit for the current query — where Enter goes.</summary>
        private GroupBox _settingsFirstMatch;

        /// <summary>The cards, top to bottom. Order is fixed at build time.</summary>
        private readonly List<GroupBox> _settingsCards = new List<GroupBox>();

        /// <summary>
        /// Everything below the last card — the action row, the notes, the version stamp —
        /// each paired with its distance from the bottom of the card stack, so the whole
        /// tail follows the cards up or down as one.
        /// </summary>
        private readonly List<KeyValuePair<Control, int>> _settingsTail =
            new List<KeyValuePair<Control, int>>();

        /// <summary>ForeColor each highlighted control had before the search recoloured it.</summary>
        private readonly Dictionary<Control, Color> _settingsHighlightWas =
            new Dictionary<Control, Color>();

        private int _settingsCardTop = SettingsFirstCardTop;
        private const int SettingsCardGap = 14;

        /// <summary>Y the first card starts at, leaving room for the search row above it.</summary>
        internal const int SettingsFirstCardTop = 56;

        // ── build ──────────────────────────────────────────────────────────────

        /// <summary>The search row that sits above the first card.</summary>
        private void AddSettingsSearchRow(Control page)
        {
            _settingsSearch = UiFactory.Text(12, 14, 300);
            _settingsSearch.PlaceholderText = Localization.T("Search settings…");
            _settingsSearch.TextChanged += (s, e) => ApplySettingsFilter();
            _settingsSearch.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape && _settingsSearch.Text.Length > 0)
                {
                    _settingsSearch.Clear();
                    // Swallow it: Escape is also the emergency stop, and clearing the box
                    // is plainly what Escape means while a search is showing.
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Enter)
                {
                    // Jump on Enter, not while typing. Scrolling on every keystroke takes
                    // the search box itself off the top of the page, so you lose sight of
                    // what you typed and of the match count while you are still typing it.
                    // Type to light the matches up, Enter to go — the same bargain a
                    // browser's find bar makes.
                    ScrollSettingsTo(_settingsFirstMatch);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };

            _settingsSearchInfo = UiFactory.Caption("", 324, 20);
            _settingsSearchInfo.AutoSize = false;
            _settingsSearchInfo.Width = 384;
            _settingsSearchInfo.Height = 18;

            page.Controls.Add(_settingsSearch);
            page.Controls.Add(_settingsSearchInfo);
        }

        /// <summary>
        /// Records the card order and how far each trailing control sits below the stack.
        /// Call once, at the very end of BuildSettingsTab — after the last note has been
        /// added, or it is not in the tail and gets left behind when the stack moves.
        /// </summary>
        private void CaptureSettingsLayout(BackdropTabPage page)
        {
            _settingsPage = page;
            _settingsCards.Clear();
            _settingsTail.Clear();

            int lastBottom = _settingsCardTop;
            foreach (Control c in page.Controls)
            {
                if (c is GroupBox g)
                {
                    _settingsCards.Add(g);
                    if (g.Bottom > lastBottom) { lastBottom = g.Bottom; }
                }
            }
            _settingsCards.Sort((a, b) => a.Top.CompareTo(b.Top));

            foreach (Control c in page.Controls)
            {
                if (c is GroupBox) { continue; }
                // Anything above the first card is the search row, which does not move.
                if (c.Top < _settingsCardTop) { continue; }
                _settingsTail.Add(new KeyValuePair<Control, int>(c, c.Top - lastBottom));
            }
        }

        /// <summary>
        /// Puts the cards one under the other and drags the tail along behind.
        ///
        /// Runs at build and again after LayoutFitter.FitAll, because the fitter is
        /// allowed to change what a card contains — and therefore, in principle, how tall
        /// it is — after these positions were first worked out.
        ///
        /// This lays out ALL the cards, every time. An earlier version let the search box
        /// hide the non-matching ones and restacked the survivors, and that fought the
        /// framework and lost: moving the tail up shrinks the page's scrollable height by
        /// well over a thousand pixels, WinForms answers by relaying the page out, and the
        /// cards go back where the previous stack had them. Measured with the first card
        /// hidden — the next card was assigned 56, read back 56, and then read back 238
        /// once the tail moved, which is exactly its position with the hidden card still
        /// counted. Re-asserting afterwards only turned a gap into an overlap. So the
        /// search highlights and scrolls instead, and nothing moves while you type.
        /// </summary>
        private void RestackSettingsCards()
        {
            BackdropTabPage page = _settingsPage;
            if (page == null || page.IsDisposed || _settingsCards.Count == 0) { return; }

            try
            {
                // Children of an AutoScroll container sit at (design position + scroll
                // offset), and AutoScrollPosition reports that offset as a NEGATIVE
                // number — so a design Y written straight into Top lands that far out
                // whenever the page is scrolled.
                int dy = page.AutoScrollPosition.Y;

                int y = _settingsCardTop;
                foreach (GroupBox g in _settingsCards)
                {
                    g.Top = y + dy;
                    // Height, not Bottom: Bottom already carries dy, and adding it to a
                    // design-space running total would compound the offset per card.
                    y += g.Height + SettingsCardGap;
                }

                int bottom = y - SettingsCardGap;
                foreach (KeyValuePair<Control, int> kv in _settingsTail)
                {
                    if (kv.Key == null || kv.Key.IsDisposed) { continue; }
                    kv.Key.Top = bottom + kv.Value + dy;
                }

                // Same [layout] channel the fitter reports on.
                Logger.Info("[layout] settings stack: " + _settingsCards.Count +
                            " cards, bottom " + bottom + ", " + _settingsTail.Count + " tail");
            }
            catch (Exception ex) { Logger.Swallow("RestackSettingsCards", ex); }
        }

        // ── search ─────────────────────────────────────────────────────────────

        private static bool Has(string haystack, string needle)
        {
            return !string.IsNullOrEmpty(haystack)
                && haystack.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        /// <summary>
        /// Whether one control answers to <paramref name="needle"/>.
        ///
        /// AccessibleDescription is searched as well as the visible caption, and that is
        /// most of the value here: every tooltip on this page is mirrored into it, so
        /// typing "CPU" finds the anti-lock limit whose label never says CPU, and the
        /// wording a screen reader gets is the wording the search box gets.
        /// </summary>
        private static bool ControlMatches(Control c, string needle)
        {
            return Has(c.Text, needle)
                || Has(c.AccessibleName, needle)
                || Has(c.AccessibleDescription, needle);
        }

        private void Highlight(Control c)
        {
            // Only the controls that carry the words. Recolouring a text box or a combo
            // would recolour the user's own value, which is not a search hit.
            if (!(c is Label || c is CheckBox || c is RadioButton)) { return; }
            if (!_settingsHighlightWas.ContainsKey(c)) { _settingsHighlightWas[c] = c.ForeColor; }
            c.ForeColor = _theme.Accent;
        }

        private void ClearSettingsHighlight()
        {
            foreach (KeyValuePair<Control, Color> kv in _settingsHighlightWas)
            {
                try { if (!kv.Key.IsDisposed) { kv.Key.ForeColor = kv.Value; } }
                catch { }
            }
            _settingsHighlightWas.Clear();
        }

        /// <summary>Matching descendants of <paramref name="parent"/>, highlighted as they are found.</summary>
        private int HighlightMatches(Control parent, string needle)
        {
            int hits = 0;
            foreach (Control c in parent.Controls)
            {
                if (ControlMatches(c, needle)) { hits++; Highlight(c); }
                hits += HighlightMatches(c, needle);
            }
            return hits;
        }

        private void ApplySettingsFilter()
        {
            if (_settingsSearch == null || _settingsCards.Count == 0) { return; }

            string needle = _settingsSearch.Text.Trim();
            ClearSettingsHighlight();

            if (needle.Length == 0)
            {
                _settingsFirstMatch = null;
                if (_settingsSearchInfo != null) { _settingsSearchInfo.Text = ""; }
                return;
            }

            int cards = 0, hits = 0;
            GroupBox first = null;
            foreach (GroupBox g in _settingsCards)
            {
                int n = HighlightMatches(g, needle);
                // A card whose own title matches counts as a hit even when nothing inside
                // it does — searching "Notifications" should take you to that card.
                if (n == 0 && Has(g.Text, needle)) { n = 1; }
                if (n > 0)
                {
                    cards++;
                    hits += n;
                    if (first == null) { first = g; }
                }
            }

            _settingsFirstMatch = first;

            if (_settingsSearchInfo != null)
            {
                _settingsSearchInfo.Text = hits == 0
                    ? Localization.T("Nothing matches that.")
                    : Localization.F("{0} match(es) in {1} section(s)", hits, cards)
                      + "   ·   " + Localization.T("Enter to jump");
                _settingsSearchInfo.ForeColor = hits == 0 ? _theme.Warning : _theme.TextMuted;
            }
        }

        /// <summary>
        /// Brings the first matching card to the top of the view without disturbing the
        /// stack.
        ///
        /// ScrollControlIntoView was the obvious call and it did nothing here — it only
        /// scrolls far enough to make a control merely visible, and it declines outright
        /// when it reckons the control already is. Searching should PUT the hit at the
        /// top, so the scroll offset is set directly.
        /// </summary>
        private void ScrollSettingsTo(GroupBox card)
        {
            try
            {
                BackdropTabPage page = _settingsPage;
                if (page == null || page.IsDisposed || card == null) { return; }

                // Top is (design position + scroll offset) and the offset reads back
                // negative, so subtracting it recovers the design position — which is the
                // space AutoScrollPosition's setter expects.
                int design = card.Top - page.AutoScrollPosition.Y;
                page.AutoScrollPosition = new Point(0, Math.Max(0, design - SettingsCardGap));
            }
            catch (Exception ex) { Logger.Swallow("ScrollSettingsTo", ex); }
        }

        /// <summary>
        /// Re-applies the highlight after a theme change.
        ///
        /// Re-theming rewrites ForeColor on every control, so the colours cached before it
        /// ran describe the OLD theme and restoring them would smear it across the new
        /// one. Dropping the cache without restoring is right precisely because the
        /// re-theme has already set each control back to its correct normal colour.
        /// </summary>
        private void ReapplySettingsHighlightAfterTheme()
        {
            if (_settingsSearch == null) { return; }
            _settingsHighlightWas.Clear();
            if (_settingsSearch.Text.Trim().Length > 0) { ApplySettingsFilter(); }
        }
    }
}
