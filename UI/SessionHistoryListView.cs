using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// The Statistics tab's "Recent sessions" list. Drawing comes from
    /// <see cref="ThemedListView"/>; this decides the colour of a row.
    ///
    /// Rows are coloured by PROFILE rather than by any performance number. Which
    /// profile a session ran under is the thing you actually scan this list for —
    /// "how did the grinding profile do last night" — and a stable colour per name
    /// groups those sessions visually without needing to read every row. Numbers stay
    /// in plain text, because colouring by CPS would imply a good/bad judgement the
    /// app has no business making.
    /// </summary>
    public sealed class SessionHistoryListView : ThemedListView
    {
        public SessionHistoryListView()
        {
            // Clickable: this list sorts by column, unlike the Live Monitor.
            HeaderStyle = ColumnHeaderStyle.Clickable;
            HideSelection = true;
        }

        /// <summary>The "Profile" column carries the chip.</summary>
        protected override int ChipColumn => 1;

        /// <summary>"When" is a timestamp — a label for the row, not its data.</summary>
        protected override bool IsMutedColumn(int column) => column == 0;

        protected override Color RowAccent(ListViewItem item)
        {
            string profile = item != null && item.SubItems.Count > 1 ? item.SubItems[1].Text : null;
            return ColourForProfile(profile);
        }

        // A fixed spread of hues, picked to stay distinguishable from each other and
        // to survive the base class's contrast correction on light and dark themes.
        private static readonly Color[] Palette =
        {
            Color.FromArgb(79, 168, 255),    // blue
            Color.FromArgb(61, 220, 132),    // green
            Color.FromArgb(255, 159, 67),    // orange
            Color.FromArgb(176, 133, 245),   // violet
            Color.FromArgb(34, 211, 238),    // cyan
            Color.FromArgb(255, 209, 102),   // amber
            Color.FromArgb(244, 114, 182),   // pink
            Color.FromArgb(163, 230, 53)     // lime
        };

        /// <summary>
        /// A stable colour for a profile name. Deliberately hashed by hand rather than
        /// with string.GetHashCode: that is randomised per process in .NET, so a profile
        /// would change colour every time Tempo restarted.
        /// </summary>
        public static Color ColourForProfile(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return Color.FromArgb(138, 151, 165);   // grey — unknown profile
            }

            unchecked
            {
                int hash = 17;
                foreach (char c in name.Trim().ToLowerInvariant())
                {
                    hash = hash * 31 + c;
                }
                int index = Math.Abs(hash) % Palette.Length;
                return Palette[index];
            }
        }
    }
}
