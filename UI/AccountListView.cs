using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// The Roblox account list. Adds a per-row SESSION-HEALTH dot in the first column on top of the
    /// themed list drawing: green = session active, red = session expired (re-login), orange = not
    /// checked yet, grey = no saved session. The colour for each row comes from
    /// <see cref="RowColorProvider"/>, which the accounts page sets from its live health map — the
    /// list itself owns no state, it just asks for the colour when it paints.
    ///
    /// It reuses <see cref="ThemedListView"/>'s existing chip mechanism (an 8&#215;8 dot drawn before
    /// the cell text when the column is the chip column), so the dot is drawn, sized and made
    /// contrast-safe by the same tested code every other themed list uses. The account NAME is left
    /// in the ordinary text colour — only the DOT carries the status colour.
    /// </summary>
    internal sealed class AccountListView : ThemedListView
    {
        /// <summary>Maps a row to its status colour. Null (or a null return) → ordinary text colour.</summary>
        public Func<ListViewItem, Color> RowColorProvider { get; set; }

        // The dot sits before the account name in column 0.
        protected override int ChipColumn => 0;

        // Colour the DOT only — do NOT tint/bold the account name (the default would, because the
        // chip column is also the accent column). A red account name reads as an error, not a status.
        protected override bool IsAccentColumn(int column) => false;

        protected override Color RowAccent(ListViewItem item)
        {
            return RowColorProvider != null ? RowColorProvider(item) : _theme.Text;
        }

        // ── drag-to-reorder insertion line ───────────────────────────────────────

        /// <summary>Where a drag would drop: the index to insert BEFORE (0..Count), or -1 for none.</summary>
        public int DropLineIndex { get; set; } = -1;

        /// <summary>The insertion index for a point in client coordinates (0..Count).</summary>
        public int DropIndexAt(Point clientPt)
        {
            for (int i = 0; i < Items.Count; i++)
            {
                Rectangle b = Items[i].Bounds;
                if (clientPt.Y < b.Top + b.Height / 2) { return i; }   // upper half → drop before this row
            }
            return Items.Count;                                         // below everything → drop at the end
        }

        protected override void OnDrawItem(DrawListViewItemEventArgs e)
        {
            base.OnDrawItem(e);
            if (DropLineIndex < 0) { return; }

            int y;
            if (DropLineIndex == e.ItemIndex) { y = e.Bounds.Top + 1; }                       // line above this row
            else if (DropLineIndex >= Items.Count && e.ItemIndex == Items.Count - 1) { y = e.Bounds.Bottom - 2; }  // at the very end
            else { return; }

            using (var pen = new Pen(_theme.Accent, 2f))
            {
                e.Graphics.DrawLine(pen, e.Bounds.Left + 3, y, e.Bounds.Right - 3, y);
            }
        }
    }
}
