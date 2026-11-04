using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A plain ListView whose columns scale with the window at 125%/150%, as ThemedListView's do
    /// (WinForms scales the list's size but not its columns).
    /// </summary>
    internal sealed class ColumnScalingListView : ListView
    {
        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            base.ScaleControl(factor, specified);
            try
            {
                if ((specified & BoundsSpecified.Width) != 0 && Math.Abs(factor.Width - 1f) > 0.01f)
                {
                    foreach (ColumnHeader column in Columns)
                    {
                        column.Width = Math.Max(1, (int)Math.Round(column.Width * factor.Width));
                    }
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// A ListView that Tempo draws itself, so it can actually follow the theme.
    ///
    /// A stock ListView is only ever half-themed: BackColor/ForeColor colour the rows,
    /// but the COLUMN HEADER is drawn by the system and ignores them — which is why
    /// every list in Tempo had a strip of near-black text on a near-black background
    /// across the top. Owner-drawing fixes that, and once the drawing is ours the rows
    /// can carry meaning as colour instead of being four columns of identical text.
    ///
    /// Subclasses supply the per-row colour via <see cref="RowAccent"/> and say which
    /// column (if any) gets the colour chip via <see cref="ChipColumn"/>.
    ///
    /// Two hard-won rules are baked in here, both found by watching real lists misbehave:
    ///
    ///   • The whole row is drawn in OnDrawItem, never split across OnDrawSubItem. On a
    ///     partial repaint (after scrolling) WinForms can raise DrawItem for a row and
    ///     never raise DrawSubItem for it — leaving a painted but completely EMPTY row.
    ///   • Moving the highlight scrolls FIRST and invalidates the whole control after.
    ///     Invalidating individual row rectangles is cheaper but they are computed before
    ///     the scroll, so they address the wrong pixels and the scroll's blit leaves a
    ///     band of torn, half-drawn rows behind.
    /// </summary>
    public class ThemedListView : ListView
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int LVM_SETEXTENDEDLISTVIEWSTYLE = 0x1000 + 54;
        private const int LVS_EX_DOUBLEBUFFER = 0x00010000;
        private const int LVM_GETTOPINDEX = 0x1000 + 39;
        private const int LVM_GETCOUNTPERPAGE = 0x1000 + 40;

        /// <summary>The first row in view (0 with no window yet). Asked of the list itself: no row is made for it.</summary>
        public int TopRowIndex
        {
            get
            {
                try { return IsHandleCreated ? (int)(long)SendMessage(Handle, LVM_GETTOPINDEX, IntPtr.Zero, IntPtr.Zero) : 0; }
                catch { return 0; }
            }
        }

        /// <summary>How many whole rows the list shows at once.</summary>
        public int RowsPerPage
        {
            get
            {
                try { return IsHandleCreated ? Math.Max(1, (int)(long)SendMessage(Handle, LVM_GETCOUNTPERPAGE, IntPtr.Zero, IntPtr.Zero)) : 1; }
                catch { return 1; }
            }
        }

        /// <summary>Whether row <paramref name="index"/> is in view.</summary>
        public bool RowInView(int index)
        {
            int top = TopRowIndex;
            return index >= top && index < top + RowsPerPage;
        }

        protected Theme _theme = Theme.ForKind(Models.ThemeKind.Dark);
        private int _highlightIndex = -1;
        private bool _relayouting;

        // ── Wheel hand-off ────────────────────────────────────────────────────
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetScrollInfo(IntPtr hWnd, int bar, ref SCROLLINFO si);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct SCROLLINFO
        {
            public int cbSize, fMask, nMin, nMax, nPage, nPos, nTrackPos;
        }

        private const int SB_VERT = 1;
        private const int SIF_ALL = 0x17;

        /// <summary>
        /// True when the list still has somewhere to go in the wheel's direction.
        /// </summary>
        private bool CanScroll(int delta)
        {
            try
            {
                var si = new SCROLLINFO();
                si.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(SCROLLINFO));
                si.fMask = SIF_ALL;
                if (!GetScrollInfo(Handle, SB_VERT, ref si)) { return false; }
                if (si.nPage == 0) { return false; }              // nothing to scroll
                return delta < 0
                    ? si.nPos + si.nPage <= si.nMax               // room below
                    : si.nPos > si.nMin;                          // room above
            }
            catch { return true; }   // unknown — let the list keep the wheel
        }

        /// <summary>
        /// Hands the wheel to the page once this list has hit its end.
        ///
        /// A ListView swallows every wheel notch whether or not it can act on one. These
        /// lists sit on AutoScroll pages (Recent sessions on Statistics, the step editor,
        /// Multi-Point), so scrolling down with the pointer over one simply STOPPED: the
        /// list was already at its last row, the page never saw the notch, and the page
        /// would not move until the pointer was moved off the list. Passing the notch on
        /// once the list is at its end is what every browser and Explorer already do.
        /// </summary>
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (!CanScroll(e.Delta))
            {
                if (e is HandledMouseEventArgs h) { h.Handled = true; }
                WheelBubble.ToParent(this, e);
                return;
            }
            base.OnMouseWheel(e);
        }

        public ThemedListView()
        {
            View = View.Details;
            FullRowSelect = true;
            GridLines = false;
            MultiSelect = false;
            OwnerDraw = true;
            DoubleBuffered = true;
            BorderStyle = BorderStyle.None;
        }

        /// <summary>Alternating row tint. Off for very short lists where it just fidgets.</summary>
        public bool Striped { get; set; } = true;

        /// <summary>Column that shows the colour chip, or -1 for none.</summary>
        protected virtual int ChipColumn => -1;

        /// <summary>
        /// When true, a selected (accent-highlighted) row keeps its chip's own colour, ringed in white,
        /// instead of flattening it to white. For lists where the chip IS the information: flattened,
        /// the account list's session dot said nothing at all on the one row the user was looking at.
        /// </summary>
        protected virtual bool KeepChipColourWhenActive => false;

        /// <summary>Colour representing this row. Defaults to the ordinary text colour.</summary>
        protected virtual Color RowAccent(ListViewItem item) => _theme.Text;

        /// <summary>Columns drawn in the accent colour and bold.</summary>
        protected virtual bool IsAccentColumn(int column) => column == ChipColumn;

        /// <summary>Columns drawn muted (labels rather than data).</summary>
        protected virtual bool IsMutedColumn(int column) => false;

        /// <summary>One cell's own text colour — a word whose colour says something, like a session's state — or null
        /// for the column's usual one. Made readable on its row; a selected row stays white.</summary>
        protected virtual Color? CellColor(ListViewItem item, int column) => null;

        /// <summary>Whether a cell with its own colour is bold too.</summary>
        protected virtual bool CellBold(ListViewItem item, int column) => false;

        /// <summary>
        /// Whole row drawn muted. Owner-drawing ignores per-item ForeColor, so a list
        /// that used to grey out a row by setting that (Multi-Point does it for disabled
        /// points) has to say so here instead or the row comes back at full strength.
        /// </summary>
        protected virtual bool RowMuted(ListViewItem item) => false;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            // Control.DoubleBuffered barely helps a ListView — it is a native common
            // control that paints itself, so the flag never reaches the drawing code.
            // This is the switch that does.
            try
            {
                SendMessage(Handle, LVM_SETEXTENDEDLISTVIEWSTYLE,
                            (IntPtr)LVS_EX_DOUBLEBUFFER, (IntPtr)LVS_EX_DOUBLEBUFFER);
            }
            catch { }

            // A snap that was waiting for this window: after this message, once the rows are in it.
            if (_snapWhenCreated)
            {
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (!IsDisposed && IsHandleCreated) { RefreshLayout(); }
                    }));
                }
                catch { }
            }
        }

        public int HighlightIndex
        {
            get => _highlightIndex;
            set
            {
                if (_highlightIndex == value)
                {
                    return;
                }
                _highlightIndex = value;
                Invalidate();
            }
        }

        /// <summary>Moves the highlight and scrolls it into view, in the order that repaints cleanly.</summary>
        public void MoveHighlightTo(int index)
        {
            _highlightIndex = index;
            try
            {
                if (index >= 0 && index < Items.Count)
                {
                    // The list's own EnsureVisible, which works the same for a virtual list
                    // (the Live Monitor) and an ordinary one.
                    EnsureVisible(index);
                }
            }
            catch { }
            Invalidate();
        }

        /// <summary>
        /// When true, the REAL selection (SelectedItems) is drawn highlighted in the accent colour,
        /// not just <see cref="HighlightIndex"/>. Off by default so the single-select lists that
        /// drive their own highlight are unchanged; the multi-select accounts list turns it on so
        /// picking one or several rows shows blue.
        /// </summary>
        public bool HighlightSelection { get; set; }

        /// <summary>A row reads as "active" (accent-highlighted) if it's the highlight or selected.</summary>
        private bool IsActive(int index)
        {
            if (index == _highlightIndex) { return true; }
            return HighlightSelection && index >= 0 && index < Items.Count && Items[index].Selected;
        }

        protected override void OnItemSelectionChanged(ListViewItemSelectionChangedEventArgs e)
        {
            base.OnItemSelectionChanged(e);
            if (HighlightSelection) { Invalidate(); }
        }

        public virtual void ApplyTheme(Theme theme)
        {
            _theme = theme ?? _theme;
            BackColor = _theme.Surface2;
            ForeColor = _theme.Text;
            Invalidate();
        }

        /// <summary>
        /// The columns scale with the window. WinForms scales a list's size when a window is shown at
        /// 125% or 150%, but not its columns: the list grew while every column but the last (which takes
        /// the leftover room) kept its 100% width around 150% text — measured with the window scaled
        /// ×1.5 (scratchpad/mainscan "dpi"), the points list went 552 → 828 px, its columns 36 / 126 /
        /// 56 / … unchanged. The same holds when an account screen is brought to the window's scale.
        /// </summary>
        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            base.ScaleControl(factor, specified);
            try
            {
                if ((specified & BoundsSpecified.Width) != 0 && Math.Abs(factor.Width - 1f) > 0.01f)
                {
                    foreach (ColumnHeader column in Columns)
                    {
                        column.Width = Math.Max(1, (int)Math.Round(column.Width * factor.Width));
                    }
                    FitLastColumn();
                }
            }
            catch { }
        }

        /// <summary>
        /// Gives the last column the leftover width. Call AFTER rows are added: an empty
        /// list has no vertical scrollbar, so fitting the columns before rows exist
        /// leaves the last one a scrollbar's width too wide and the list grows a useless
        /// horizontal scrollbar the moment it fills.
        /// </summary>
        public void FitLastColumn()
        {
            // Never from inside itself. Setting a column's width can show or hide a scroll bar; that resizes the
            // client area, and WinForms asks for a fit again INSIDE this one. With two fitters on the Accounts list
            // (this and the list's own, their floors 60 and the title's width apart) each undid the other, every
            // undo flipped a scroll bar, and the recursion ran for minutes: "Not responding" on entering the
            // Accounts tab, 236 s after a vault unlock (2026-11-03, dotnet-stack on the hung window). Nested asks are
            // answered here instead: a few passes, each against the client width as it now is, until it holds.
            if (_fittingColumns) { return; }
            _fittingColumns = true;
            try
            {
                if (Columns.Count == 0)
                {
                    return;
                }
                for (int pass = 0; pass < 3; pass++)
                {
                    int used = 0;
                    for (int i = 0; i < Columns.Count - 1; i++)
                    {
                        used += Columns[i].Width;
                    }
                    // ALL of the room, to the pixel. The strip of header past the last column is painted by Windows,
                    // not by OnDrawColumnHeader: a black block at the end of the Live Monitor's header in the dark
                    // themes (2026-11-02). This left 4 px of it, and a whole scroll bar's width once the bar went away
                    // — the client width changes then, the control's does not (OnClientSizeChanged). Never narrower
                    // than the column's title. Columns that fill the client exactly raise no horizontal bar.
                    ColumnHeader last = Columns[Columns.Count - 1];
                    int width = Math.Max(HeaderTextNeed(last) + 12, ClientSize.Width - used);
                    if (last.Width == width)
                    {
                        break;
                    }
                    last.Width = width;
                }

                // Windows redoes a list's scroll bars when a column's width changes. A list refilled with drawing
                // suspended (BeginUpdate) can keep a horizontal bar sized for the columns as they WERE — on the Accounts
                // list after a refill, a range 17 px past columns that now fit exactly: a bar for nothing — and a width
                // that is already right sends no change to correct it. A nudge of the last column makes it redo them
                // (scratchpad/hsbar: the stale bar stays through a refit, and goes with the nudge).
                if (IsHandleCreated && (GetWindowLong(Handle, GWL_STYLE) & WS_HSCROLL) != 0)
                {
                    int total = 0;
                    foreach (ColumnHeader c in Columns) { total += c.Width; }
                    if (total <= ClientSize.Width)
                    {
                        ColumnHeader last = Columns[Columns.Count - 1];
                        int w = last.Width;
                        last.Width = w - 1;
                        last.Width = w;
                    }
                }
            }
            catch { }
            finally
            {
                _fittingColumns = false;
            }
        }

        private bool _fittingColumns;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        private const int GWL_STYLE = -16, WS_HSCROLL = 0x00100000;

        protected override void OnClientSizeChanged(EventArgs e)
        {
            base.OnClientSizeChanged(e);
            FitLastColumn();
        }

        /// <summary>Re-fits columns and trims the control to whole rows.</summary>
        public void RefreshLayout()
        {
            if (_relayouting)
            {
                return;
            }
            _relayouting = true;
            try
            {
                FitLastColumn();
                SnapHeightToWholeRows();
            }
            finally
            {
                _relayouting = false;
            }
        }

        /// <summary>A snap asked for before the list had a window, done once it has one.</summary>
        private bool _snapWhenCreated;

        /// <summary>Stops the last visible row being sliced in half, which reads as a fault.</summary>
        private void SnapHeightToWholeRows()
        {
            // Row rectangles come from the native list, and asking for one CREATES its window: at
            // start-up that was the Statistics history list's window, 32 ms, for a tab nobody had
            // opened. Wait for the real window instead (OnHandleCreated).
            if (!IsHandleCreated)
            {
                _snapWhenCreated = true;
                return;
            }
            _snapWhenCreated = false;
            try
            {
                if (Items.Count == 0)
                {
                    return;
                }

                Rectangle first = Items[0].Bounds;
                int headerHeight = first.Top;
                int rowHeight = first.Height;
                if (rowHeight <= 0 || headerHeight < 0)
                {
                    return;
                }

                int rows = (ClientSize.Height - headerHeight) / rowHeight;
                if (rows < 1)
                {
                    return;
                }

                int desired = headerHeight + rows * rowHeight + (Height - ClientSize.Height);
                if (desired >= 60 && desired < Height)
                {
                    Height = desired;
                }
            }
            catch { }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            FitLastColumn();
        }

        // ── colour helpers ─────────────────────────────────────────────────────

        protected static double Luminance(Color c)
        {
            double R = Chan(c.R), G = Chan(c.G), B = Chan(c.B);
            return 0.2126 * R + 0.7152 * G + 0.0722 * B;
        }

        private static double Chan(int v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        protected static double Contrast(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            double hi = Math.Max(la, lb), lo = Math.Min(la, lb);
            return (hi + 0.05) / (lo + 0.05);
        }

        /// <summary>
        /// Nudges a colour lighter or darker until it is readable on the given
        /// background, keeping its hue so the meaning survives. This is what lets the
        /// same fixed palette work on both dark and light themes.
        /// </summary>
        protected static Color Readable(Color colour, Color background)
        {
            if (Contrast(colour, background) >= 4.5)
            {
                return colour;
            }

            bool lightBackground = Luminance(background) > 0.35;
            Color best = colour;
            for (int step = 1; step <= 12; step++)
            {
                double f = step / 12.0;
                Color candidate = lightBackground
                    ? Blend(colour, Color.Black, f * 0.85)
                    : Blend(colour, Color.White, f * 0.85);
                best = candidate;
                if (Contrast(candidate, background) >= 4.5)
                {
                    break;
                }
            }
            return best;
        }

        protected static Color Blend(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        protected Color RowBackground(int index)
        {
            if (IsActive(index))
            {
                return _theme.Accent;
            }
            if (!Striped || index % 2 == 0)
            {
                return _theme.Surface2;
            }
            return Blend(_theme.Surface2,
                         Luminance(_theme.Surface2) > 0.35 ? Color.Black : Color.White, 0.045);
        }

        // ── drawing ────────────────────────────────────────────────────────────

        protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle r = e.Bounds;

            using (var back = new SolidBrush(_theme.Surface))
            {
                g.FillRectangle(back, r);
            }

            using (var line = new Pen(Color.FromArgb(140, _theme.Accent)))
            {
                g.DrawLine(line, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);
            }

            var textRect = new Rectangle(r.Left + HeaderTextLeft, r.Top, r.Width - HeaderTextLeft - HeaderTextRight, r.Height);
            TextRenderer.DrawText(g, e.Header.Text, HeaderFont, textRect,
                Readable(_theme.Accent, _theme.Surface), HeaderFlags);
        }

        // The titles' font, made once (a new Font per title, per paint and per fit was the most of each pass of the
        // fit loop above), and what each title needs, measured once per text.
        private Font _headerFont;
        private readonly System.Collections.Generic.Dictionary<string, int> _headerNeed =
            new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);

        private Font HeaderFont => _headerFont ??= new Font(Font.FontFamily, HeaderFontSize, FontStyle.Bold);

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            _headerFont?.Dispose();
            _headerFont = null;
            _headerNeed.Clear();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _headerFont?.Dispose();
                _headerFont = null;
            }
            base.Dispose(disposing);
        }

        // Where a column title is painted, shared with the layout check that measures it.
        private const int HeaderTextLeft = 8, HeaderTextRight = 4;
        private const float HeaderFontSize = 8.25f;
        // NoPrefix: a title, like a row, is shown as written — an "&" is a character, not a shortcut marker.
        private const TextFormatFlags HeaderFlags =
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

        /// <summary>How wide <paramref name="column"/>'s title is as painted (for the layout checks).</summary>
        internal int HeaderTextNeed(ColumnHeader column)
        {
            string text = column?.Text ?? string.Empty;
            if (_headerNeed.TryGetValue(text, out int need)) { return need; }
            need = TextRenderer.MeasureText(text, HeaderFont, new Size(int.MaxValue, int.MaxValue),
                HeaderFlags & ~TextFormatFlags.EndEllipsis).Width;
            _headerNeed[text] = need;
            return need;
        }

        /// <summary>The room <paramref name="column"/>'s title is painted in.</summary>
        internal int HeaderTextRoom(ColumnHeader column) => (column?.Width ?? 0) - HeaderTextLeft - HeaderTextRight;

        /// <summary>
        /// <see cref="HeaderTextRoom"/> in a layout's units, where a painted pixel is
        /// <paramref name="unitsPerPixel"/> of them (LayoutFitter.SetUnitsPerPixel).
        /// </summary>
        internal int HeaderTextRoomIn(ColumnHeader column, float unitsPerPixel) =>
            (column?.Width ?? 0) - (int)Math.Ceiling((HeaderTextLeft + HeaderTextRight) * unitsPerPixel);

        protected override void OnDrawItem(DrawListViewItemEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var back = new SolidBrush(RowBackground(e.ItemIndex)))
            {
                g.FillRectangle(back, e.Bounds);
            }

            if (IsActive(e.ItemIndex))
            {
                using (var bar = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
                {
                    g.FillRectangle(bar, new Rectangle(e.Bounds.Left, e.Bounds.Top, 3, e.Bounds.Height));
                }
            }

            // Cell rectangles come from the column widths, not SubItem.Bounds, which is
            // unreliable for column 0 in Details view.
            int x = e.Bounds.Left;
            for (int c = 0; c < Columns.Count; c++)
            {
                int w = Columns[c].Width;
                DrawCell(g, e.Item, e.ItemIndex, c, new Rectangle(x, e.Bounds.Top, w, e.Bounds.Height));
                x += w;
            }
        }

        /// <summary>Not used — the row is drawn whole in <see cref="OnDrawItem"/>.</summary>
        protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
        {
        }

        private void DrawCell(Graphics g, ListViewItem item, int itemIndex, int column, Rectangle r)
        {
            string text = column < item.SubItems.Count ? item.SubItems[column].Text : string.Empty;
            bool active = IsActive(itemIndex);
            Color rowBack = RowBackground(itemIndex);
            Color accent = Readable(RowAccent(item), rowBack);

            Color textColour;
            FontStyle style = FontStyle.Regular;

            if (active)
            {
                textColour = Color.White;
                if (IsAccentColumn(column)) { style = FontStyle.Bold; }
            }
            else if (IsAccentColumn(column))
            {
                textColour = accent;
                style = FontStyle.Bold;
            }
            else if (CellColor(item, column) is Color own)
            {
                textColour = Readable(own, rowBack);
                if (CellBold(item, column)) { style = FontStyle.Bold; }
            }
            else if (IsMutedColumn(column) || RowMuted(item))
            {
                textColour = Readable(_theme.TextMuted, rowBack);
            }
            else
            {
                textColour = Readable(_theme.Text, rowBack);
            }

            int textLeft = r.Left + 8;

            // Checkboxes are drawn by the system in a normal ListView, which means
            // owner-drawing makes them VANISH unless we draw them ourselves. Windows
            // still owns the hit-testing, so ticking one keeps working — only the
            // painting moves here, and it can now follow the theme instead of being a
            // stock light-grey box on a dark row.
            if (column == 0 && CheckBoxes)
            {
                int side = 13;
                var box = new Rectangle(r.Left + 5, r.Top + (r.Height - side) / 2, side, side);
                bool ticked = item.Checked;

                // Named once so the tick below can be derived from the box it lands on.
                Color boxFill = active ? Color.White : _theme.Accent;

                using (var fill = new SolidBrush(ticked
                           ? boxFill
                           : Blend(rowBack, Luminance(rowBack) > 0.35 ? Color.Black : Color.White, 0.12)))
                {
                    g.FillRectangle(fill, box);
                }
                using (var edge = new Pen(ticked
                           ? boxFill
                           : Readable(_theme.TextMuted, rowBack)))
                {
                    g.DrawRectangle(edge, box);
                }

                if (ticked)
                {
                    // Tick drawn as two strokes rather than a glyph font, so it lands
                    // identically at every DPI.
                    //
                    // The pair used to be fixed: Accent tick on a white box when the row
                    // is selected, white tick on an Accent box when it is not. Both are the
                    // same comparison — Accent against white — and it is below the 3:1 bar
                    // for a graphical indicator in 22 of the 38 palettes, bottoming out at
                    // 1.48:1 on Carbon. That is the mark that says whether a point or a
                    // macro row is enabled, invisible in more than half the themes.
                    Color mark = Theme.ReadableOn(boxFill);
                    using (var pen = new Pen(mark, 2f))
                    {
                        SmoothingMode old = g.SmoothingMode;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.DrawLines(pen, new[]
                        {
                            new Point(box.Left + 3, box.Top + 6),
                            new Point(box.Left + 5, box.Top + 9),
                            new Point(box.Right - 3, box.Top + 4)
                        });
                        g.SmoothingMode = old;
                    }
                }

                textLeft = box.Right + 6;
            }

            if (column == ChipColumn && ChipColumn >= 0)
            {
                var chip = new Rectangle(r.Left + 6, r.Top + (r.Height - 8) / 2, 8, 8);
                bool keepColour = active && KeepChipColourWhenActive;
                SmoothingMode old = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(chip);
                    using (var fill = new SolidBrush(!active ? accent : keepColour ? RowAccent(item) : Color.White))
                    {
                        g.FillPath(fill, path);
                    }
                }
                if (keepColour)
                {
                    // A white ring stands the real colour off the accent highlight behind it.
                    using (var ring = new Pen(Color.White, 1.5f))
                    {
                        g.DrawEllipse(ring, Rectangle.Inflate(chip, 1, 1));
                    }
                }
                g.SmoothingMode = old;
                textLeft = chip.Right + 7;
            }

            var textRect = new Rectangle(textLeft, r.Top, r.Right - textLeft - 4, r.Height);
            using (var font = new Font(Font.FontFamily, Font.Size, style))
            {
                // Through ColorEmoji so an account's (or macro's) emoji keeps its colours — TextRenderer
                // drew it as a flat silhouette in the row's text colour. Plain text draws as before.
                // NoPrefix: these are the user's names, and a lone "&" in one ("Jump & Run") was taken for
                // a shortcut marker — dropped, with the space after it underlined.
                ColorEmoji.DrawText(g, text, font, textRect, textColour,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
    }
}
