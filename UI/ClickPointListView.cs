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
    /// </summary>
    public sealed class ClickPointListView : ThemedListView
    {
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
    }
}
