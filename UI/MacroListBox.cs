using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using AutoClicker.Models;

namespace AutoClicker.UI
{
    /// <summary>
    /// The Saved-macros list, drawn by Tempo.
    ///
    /// This was the last stock WinForms ListBox left in the app: the pass that made the
    /// other lists readable and colour-coded (Live Monitor, Recent sessions, Multi-Point,
    /// the step editor) never reached it. So it rendered one flat line per macro straight
    /// from ToString — "Macro 16-11-12 • 341 steps • 2.7 s" — every row the same weight
    /// and colour, with Windows' own blue selection bar that ignores the theme entirely.
    ///
    /// Two problems with that beyond looks. The NAME is what you scan for and it competed
    /// for attention with two numbers you rarely need. And the macro already knows things
    /// the row never showed: how many times you have played it, and when you last did —
    /// which is exactly how you tell "grind" apart from "grind Copy" and the eight
    /// auto-named recordings around them.
    ///
    /// So: name on its own line in full-strength text, everything else demoted to a muted
    /// second line, play count and recency added, favourites starred, and a selection that
    /// matches the rest of Tempo.
    /// </summary>
    internal sealed class MacroListBox : ListBox
    {
        private Theme _theme;
        private int _hoverIndex = -1;

        private const int RowHeight = 40;
        private const int PadX = 10;

        public MacroListBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = RowHeight;
            BorderStyle = BorderStyle.FixedSingle;
            IntegralHeight = false;   // otherwise WinForms trims the control's height to whole rows
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }

        public void ApplyTheme(Theme theme)
        {
            if (theme == null) { return; }
            _theme = theme;
            BackColor = theme.Surface;
            ForeColor = theme.Text;
            Invalidate();
        }

        private const int WM_ERASEBKGND = 0x0014;
        private const int WM_VSCROLL = 0x0115;
        private const int WM_MOUSEWHEEL = 0x020A;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_ERASEBKGND)
            {
                // Don't let Windows blank the client area before the rows are drawn.
                //
                // OnDrawItem already fills every pixel of every row, so the erase only
                // repaints ground that is about to be painted again — and when the list
                // SCROLLS, that happens for the whole visible area on every notch. Erase,
                // then draw, erase, then draw: that is the flicker. Suppressing it costs
                // nothing because nothing depends on it, except the strip below the last
                // row, which has no item to paint it and is handled below.
                EraseBelowLastRow(m.WParam);
                m.Result = (IntPtr)1;
                return;
            }

            base.WndProc(ref m);

            // A wheel notch or a scrollbar drag moves the ROWS, not the pointer, so no
            // MouseMove arrives — the highlight stayed on the same row INDEX, which now
            // holds a different macro. It looked like the highlight jumping to a row the
            // cursor wasn't on. Re-read what is actually under the pointer instead.
            if (m.Msg == WM_VSCROLL || m.Msg == WM_MOUSEWHEEL)
            {
                UpdateHoverFromCursor();
            }
        }

        /// <summary>
        /// Paints the strip below the last row — the only part of the client area no
        /// OnDrawItem covers, and therefore the only part the suppressed erase still
        /// owed. One fill, and only when the rows don't reach the bottom.
        /// </summary>
        private void EraseBelowLastRow(IntPtr hdc)
        {
            if (hdc == IntPtr.Zero) { return; }
            try
            {
                int visibleRows = Math.Max(0, Items.Count - TopIndex);
                int filled = visibleRows * ItemHeight;
                if (filled >= ClientSize.Height) { return; }
                var strip = new Rectangle(0, filled, ClientSize.Width, ClientSize.Height - filled);
                using (var g = Graphics.FromHdc(hdc))
                using (var b = new SolidBrush(_theme != null ? _theme.Surface : BackColor))
                {
                    g.FillRectangle(b, strip);
                }
            }
            catch { }
        }

        /// <summary>Re-reads which row the pointer is over, after the rows have moved.</summary>
        private void UpdateHoverFromCursor()
        {
            if (!IsHandleCreated) { return; }
            try
            {
                Point p = PointToClient(Cursor.Position);
                SetHover(ClientRectangle.Contains(p) ? IndexFromPoint(p) : -1);
            }
            catch { }
        }

        /// <summary>
        /// Moves the hover highlight, repainting ONLY the two rows involved. The old
        /// code invalidated the whole control for a one-row change, so every mouse move
        /// across the list redrew all eleven rows.
        /// </summary>
        private void SetHover(int index)
        {
            if (index == _hoverIndex) { return; }
            int previous = _hoverIndex;
            _hoverIndex = index;
            InvalidateRow(previous);
            InvalidateRow(index);
        }

        private void InvalidateRow(int index)
        {
            if (index < 0 || index >= Items.Count) { return; }
            try { Invalidate(GetItemRectangle(index)); } catch { }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            SetHover(IndexFromPoint(e.Location));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            SetHover(-1);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            Theme th = _theme;
            if (th == null || e.Index < 0 || e.Index >= Items.Count) { return; }

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var macro = Items[e.Index] as Macro;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            bool hover = e.Index == _hoverIndex && !selected;

            // Background. Rounded and inset, like the sidebar's nav buttons — the stock
            // full-bleed blue bar is the single most out-of-place thing on this page.
            using (var back = new SolidBrush(th.Surface))
            {
                g.FillRectangle(back, e.Bounds);
            }

            var row = new Rectangle(e.Bounds.X + 3, e.Bounds.Y + 2,
                                    e.Bounds.Width - 6, e.Bounds.Height - 4);
            if (selected || hover)
            {
                Color fill = selected
                    ? Blend(th.Accent, th.Surface, 0.72)
                    : Blend(th.Surface2, th.Surface, 0.45);
                using (var path = Rounded(row, 7))
                using (var b = new SolidBrush(fill))
                {
                    g.FillPath(b, path);
                }
                if (selected)
                {
                    // A short accent bar rather than a full-height block: it marks the row
                    // without drowning the text it is meant to highlight.
                    using (var bar = new SolidBrush(th.Accent))
                    {
                        g.FillRectangle(bar, row.X + 1, row.Y + 6, 3, row.Height - 12);
                    }
                }
            }

            if (macro == null) { return; }

            int textLeft = row.X + PadX;
            int textRight = row.Right - PadX;

            // ── Line 1: the name, plus a star for favourites ──────────────────
            string name = string.IsNullOrEmpty(macro.Name) ? "(unnamed)" : macro.Name;
            using (var nameFont = new Font(Font.FontFamily, 9.5f, FontStyle.Bold))
            using (var nameBrush = new SolidBrush(th.Text))
            {
                if (macro.IsFavorite)
                {
                    using (var star = new SolidBrush(th.Warning))
                    {
                        g.DrawString("★", nameFont, star, textLeft - 2, row.Y + 3);
                    }
                    textLeft += 15;
                }
                g.DrawString(name, nameFont, nameBrush,
                    new RectangleF(textLeft, row.Y + 3, textRight - textLeft, 17),
                    new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
            }

            // ── Line 2: the detail, demoted ───────────────────────────────────
            // A macro that has never been played used to print neither a play count nor a
            // recency — two blank columns, which reads as "this row failed to load" rather
            // than "this one is new". Both halves now always say something: how many times
            // it ran (or that it hasn't), and how long ago that was (or how old it is).
            // Translated. This whole line is painted straight onto the row, so it touches
            // no helper that could have caught it — and it is the most-read text on the
            // tab. Every macro's detail line was English in all five other languages.
            bool everPlayed = macro.TimesPlayed > 0;
            string meta = Utils.Localization.F("{0} steps", macro.StepCount.ToString("N0"))
                + "  ·  " + ShortTime(macro.EstimatedDurationMs);
            // COUNT FIRST — "2× played", not "played 2×". This line is trimmed with an
            // ellipsis when it runs out of room, and it does run out in the longer
            // languages: Spanish "reproducido 2×" came back as "reproducid…", throwing
            // away the only part that carried information. With the number in front, a
            // trim costs the word and keeps the count.
            meta += "  ·  " + (everPlayed
                ? Utils.Localization.F("{0}× played", macro.TimesPlayed)
                : Utils.Localization.T("never played"));
            using (var metaFont = new Font(Font.FontFamily, 8.25f, FontStyle.Regular))
            using (var metaBrush = new SolidBrush(th.TextMuted))
            {
                // 62px, measured against the longest translations of this column —
                // "il y a 2mo", "creado 2me", "2Mo alt" — which all sit inside it. Widening
                // it was tried and was the wrong trade: every pixel here comes straight out
                // of the meta line to its left, which is the one that was actually running
                // out of room. The draw below trims with an ellipsis rather than clipping
                // mid-glyph, so a language that still overruns degrades readably.
                const int AgoWidth = 62;
                // Last played when it has been, otherwise how old the recording is — so
                // the column is never empty. "old" rather than "ago" makes clear which
                // of the two dates is being shown without needing a second label.
                string ago = everPlayed
                    ? Ago(macro.LastPlayedUtc)
                    : Age(macro.CreatedUtc);
                int metaWidth = ago.Length > 0
                    ? textRight - textLeft - AgoWidth - 6
                    : textRight - textLeft;

                g.DrawString(meta, metaFont, metaBrush,
                    new RectangleF(textLeft, row.Y + 20, Math.Max(20, metaWidth), 15),
                    new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });

                // Recency, right-aligned: the fastest way to spot the one you actually use
                // among a pile of auto-named recordings.
                if (ago.Length > 0)
                {
                    var fmt = new StringFormat
                    {
                        Alignment = StringAlignment.Far,
                        FormatFlags = StringFormatFlags.NoWrap,
                        Trimming = StringTrimming.EllipsisCharacter
                    };
                    g.DrawString(ago, metaFont, metaBrush,
                        new RectangleF(textRight - AgoWidth, row.Y + 20, AgoWidth, 15), fmt);
                }
            }
        }

        /// <summary>"2.7 s" / "2.6 min" — the same shape the old single line used.</summary>
        private static string ShortTime(long ms)
        {
            if (ms <= 0) { return "0 s"; }
            double s = ms / 1000.0;
            if (s < 60) { return s.ToString("0.#") + " s"; }
            return (s / 60.0).ToString("0.#") + " min";
        }

        /// <summary>
        /// Compact "how old is this recording" for macros that have never been played,
        /// so their right-hand column carries something instead of nothing. Reads "3d
        /// old" against the played rows' "3d ago", which is enough to tell them apart.
        /// </summary>
        private static string Age(DateTime utc)
        {
            if (utc == default) { return ""; }
            try
            {
                TimeSpan d = DateTime.UtcNow - utc;
                if (d.TotalSeconds < 0) { return Utils.Localization.T("new"); }  // clock skew, or just saved
                if (d.TotalMinutes < 60) { return Utils.Localization.T("new"); }
                if (d.TotalHours < 24) { return Utils.Localization.F("{0}h old", (int)d.TotalHours); }
                if (d.TotalDays < 30) { return Utils.Localization.F("{0}d old", (int)d.TotalDays); }
                return Utils.Localization.F("{0}mo old", (int)(d.TotalDays / 30));
            }
            catch { return ""; }
        }

        /// <summary>Compact "when did I last run this" — empty when never played.</summary>
        private static string Ago(DateTime? utc)
        {
            if (utc == null) { return ""; }
            try
            {
                TimeSpan d = DateTime.UtcNow - utc.Value;
                if (d.TotalSeconds < 90) { return Utils.Localization.T("just now"); }
                if (d.TotalMinutes < 60) { return Utils.Localization.F("{0}m ago", (int)d.TotalMinutes); }
                if (d.TotalHours < 24) { return Utils.Localization.F("{0}h ago", (int)d.TotalHours); }
                if (d.TotalDays < 30) { return Utils.Localization.F("{0}d ago", (int)d.TotalDays); }
                return Utils.Localization.F("{0}mo ago", (int)(d.TotalDays / 30));
            }
            catch { return ""; }
        }

        private static Color Blend(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (int)(a.R * (1 - t) + b.R * t),
                (int)(a.G * (1 - t) + b.G * t),
                (int)(a.B * (1 - t) + b.B * t));
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            if (r.Width <= 0 || r.Height <= 0) { path.AddRectangle(r); return path; }
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
