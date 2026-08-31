using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
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

        /// <summary>Colour representing this row. Defaults to the ordinary text colour.</summary>
        protected virtual Color RowAccent(ListViewItem item) => _theme.Text;

        /// <summary>Columns drawn in the accent colour and bold.</summary>
        protected virtual bool IsAccentColumn(int column) => column == ChipColumn;

        /// <summary>Columns drawn muted (labels rather than data).</summary>
        protected virtual bool IsMutedColumn(int column) => false;

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
                    Items[index].EnsureVisible();
                }
            }
            catch { }
            Invalidate();
        }

        public virtual void ApplyTheme(Theme theme)
        {
            _theme = theme ?? _theme;
            BackColor = _theme.Surface2;
            ForeColor = _theme.Text;
            Invalidate();
        }

        /// <summary>
        /// Gives the last column the leftover width. Call AFTER rows are added: an empty
        /// list has no vertical scrollbar, so fitting the columns before rows exist
        /// leaves the last one a scrollbar's width too wide and the list grows a useless
        /// horizontal scrollbar the moment it fills.
        /// </summary>
        public void FitLastColumn()
        {
            try
            {
                if (Columns.Count == 0)
                {
                    return;
                }
                int used = 0;
                for (int i = 0; i < Columns.Count - 1; i++)
                {
                    used += Columns[i].Width;
                }
                int room = ClientSize.Width - used - 4;
                if (room > 60)
                {
                    Columns[Columns.Count - 1].Width = room;
                }
            }
            catch { }
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

        /// <summary>Stops the last visible row being sliced in half, which reads as a fault.</summary>
        private void SnapHeightToWholeRows()
        {
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
            if (index == _highlightIndex)
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

            var textRect = new Rectangle(r.Left + 8, r.Top, r.Width - 12, r.Height);
            using (var font = new Font(Font.FontFamily, 8.25f, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, e.Header.Text, font, textRect,
                    Readable(_theme.Accent, _theme.Surface),
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            }
        }

        protected override void OnDrawItem(DrawListViewItemEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var back = new SolidBrush(RowBackground(e.ItemIndex)))
            {
                g.FillRectangle(back, e.Bounds);
            }

            if (e.ItemIndex == _highlightIndex)
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
            bool active = itemIndex == _highlightIndex;
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

                using (var fill = new SolidBrush(ticked
                           ? (active ? Color.White : _theme.Accent)
                           : Blend(rowBack, Luminance(rowBack) > 0.35 ? Color.Black : Color.White, 0.12)))
                {
                    g.FillRectangle(fill, box);
                }
                using (var edge = new Pen(ticked
                           ? (active ? Color.White : _theme.Accent)
                           : Readable(_theme.TextMuted, rowBack)))
                {
                    g.DrawRectangle(edge, box);
                }

                if (ticked)
                {
                    // Tick drawn as two strokes rather than a glyph font, so it lands
                    // identically at every DPI.
                    Color mark = active ? _theme.Accent : Color.White;
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
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(chip);
                    using (var fill = new SolidBrush(active ? Color.White : accent))
                    {
                        SmoothingMode old = g.SmoothingMode;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.FillPath(fill, path);
                        g.SmoothingMode = old;
                    }
                }
                textLeft = chip.Right + 7;
            }

            var textRect = new Rectangle(textLeft, r.Top, r.Right - textLeft - 4, r.Height);
            using (var font = new Font(Font.FontFamily, Font.Size, style))
            {
                TextRenderer.DrawText(g, text, font, textRect, textColour,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            }
        }
    }
}
