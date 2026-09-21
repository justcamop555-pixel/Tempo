using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// The Multi-Point tab's list of click points. Drawing comes from
    /// <see cref="ThemedListView"/>; this decides the colour of a row.
    ///
    /// Rows are coloured by which mouse BUTTON the point uses, matching the colours the
    /// Live Monitor gives the same actions — left is green, right is orange, middle is
    /// violet — so a point means the same thing whichever screen you meet it on.
    /// Disabled points are drawn muted regardless, because "will this actually fire"
    /// matters more at a glance than which button it would have used.
    ///
    /// Two markers are drawn on top of the rows, and NEITHER touches the selection:
    ///  • <see cref="LiveIndex"/> — the point a running sequence is clicking right now. This used to be
    ///    shown by SELECTING that row on every tick, which stole the selection from the user: during a
    ///    run, Delete / Duplicate / Toggle / nudge acted on whichever point the engine had reached, not
    ///    the row the user had picked.
    ///  • <see cref="DropLineIndex"/> — the gap a drag would drop into. Rows are reordered by dragging
    ///    (the Move Up / Move Down buttons are gone); the upper half of a row inserts before it and the
    ///    lower half after, the same rule as the Macros list.
    /// </summary>
    public sealed class ClickPointListView : ThemedListView
    {
        private int _dropLine = -1;
        private int _live = -1;
        private Color _accent = Color.FromArgb(38, 139, 210);

        public ClickPointListView()
        {
            HeaderStyle = ColumnHeaderStyle.Nonclickable;
            HideSelection = false;
            CheckBoxes = true;
        }

        /// <summary>The "Button" column carries the chip.</summary>
        protected override int ChipColumn => 4;

        /// <summary>Coordinates and timings are supporting detail.</summary>
        protected override bool IsMutedColumn(int column) => column == 2 || column == 3 || column == 6 || column == 7;

        /// <summary>
        /// An unticked point is switched off and will not fire, so the whole row is
        /// dimmed — the list previously did this by setting the item's ForeColor, which
        /// owner-drawing no longer honours.
        /// </summary>
        protected override bool RowMuted(ListViewItem item) => item != null && !item.Checked;

        protected override Color RowAccent(ListViewItem item)
        {
            // An unticked point is not going to fire; say so in grey rather than
            // advertising a button colour for something that is switched off.
            if (item != null && !item.Checked)
            {
                return Color.FromArgb(138, 151, 165);
            }

            string button = item != null && item.SubItems.Count > 4 ? item.SubItems[4].Text : null;
            if (string.IsNullOrEmpty(button))
            {
                return Color.FromArgb(200, 200, 200);
            }

            if (button.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Color.FromArgb(61, 220, 132);     // green — matches Left in the Live Monitor
            }
            if (button.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Color.FromArgb(255, 159, 67);     // orange
            }
            if (button.IndexOf("Middle", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Color.FromArgb(176, 133, 245);    // violet
            }
            return Color.FromArgb(79, 168, 255);         // blue — anything else
        }

        public override void ApplyTheme(Theme theme)
        {
            base.ApplyTheme(theme);
            if (theme != null) { _accent = theme.Accent; }
        }

        /// <summary>The row a running sequence is clicking now, or -1. Drawn as a marker, never selected.</summary>
        public int LiveIndex
        {
            get { return _live; }
            set
            {
                if (_live == value) { return; }
                _live = value;
                // The whole control, never two row rectangles: ThemedListView's own rule, because a
                // row rectangle computed before a scroll is stale after it and leaves torn rows.
                Invalidate();
            }
        }

        /// <summary>
        /// Where a drag would drop, as an INSERTION POINT: 0..Count, where Count means "after the last
        /// row". -1 hides the line. Set while dragging.
        /// </summary>
        public int DropLineIndex
        {
            get { return _dropLine; }
            set
            {
                if (_dropLine == value) { return; }
                _dropLine = value;
                Invalidate();
            }
        }

        /// <summary>
        /// The insertion point a drop at <paramref name="clientPoint"/> means: the upper half of a row
        /// inserts before it, the lower half after it; above the first row is 0 and below the last row
        /// appends.
        /// </summary>
        public int DropIndexAt(Point clientPoint)
        {
            if (Items.Count == 0) { return 0; }

            // FullRowSelect is on, so any x inside a row finds it; a small x stays clear of the
            // right-hand columns when the list is narrower than its columns.
            ListViewItem hit = GetItemAt(4, clientPoint.Y);
            if (hit == null)
            {
                // Over the header or above the first row → 0; past the last row → append.
                Rectangle first = Items[0].Bounds;
                return clientPoint.Y < first.Top ? 0 : Items.Count;
            }

            Rectangle row = hit.Bounds;
            return clientPoint.Y > row.Top + row.Height / 2 ? hit.Index + 1 : hit.Index;
        }

        protected override void OnDrawItem(DrawListViewItemEventArgs e)
        {
            base.OnDrawItem(e);
            if (e.ItemIndex < 0) { return; }

            // Live point: a bar down the left edge and a thin outline. Deliberately no translucent fill
            // over the row — tinting the text would cut its contrast, which the contrast audit checks.
            if (_live == e.ItemIndex)
            {
                Rectangle r = e.Bounds;
                using (var bar = new SolidBrush(_accent))
                {
                    e.Graphics.FillRectangle(bar, r.Left, r.Top, 4, r.Height);
                }
                using (var pen = new Pen(_accent, 1f))
                {
                    e.Graphics.DrawRectangle(pen, r.Left, r.Top, r.Width - 1, r.Height - 1);
                }
            }

            // Drop line: along the top edge of the row BELOW the gap, or the bottom edge of the last
            // row when the gap is after it.
            if (_dropLine >= 0)
            {
                bool aboveThisRow = _dropLine == e.ItemIndex;
                bool belowLastRow = _dropLine == Items.Count && e.ItemIndex == Items.Count - 1;
                if (aboveThisRow || belowLastRow)
                {
                    int y = aboveThisRow ? e.Bounds.Top + 1 : e.Bounds.Bottom - 2;
                    using (var pen = new Pen(_accent, 2f))
                    {
                        e.Graphics.DrawLine(pen, e.Bounds.Left + 4, y, e.Bounds.Right - 4, y);
                    }
                }
            }
        }
    }
}
