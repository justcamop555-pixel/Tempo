using System;
using System.Collections;
using System.Collections.Generic;
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
        /// <summary>The rows in the order shown. Windows asks only for the ones it draws (VirtualMode).</summary>
        private ListViewItem[] _rows = Array.Empty<ListViewItem>();

        public SessionHistoryListView()
        {
            // Clickable: this list sorts by column, unlike the Live Monitor.
            HeaderStyle = ColumnHeaderStyle.Clickable;
            HideSelection = true;
            // Virtual. Filled the ordinary way, every visit to Statistics put all 200 sessions back into the native list:
            // 1,200 inserts, 60–110 ms on this PC and now and then 1.6–1.9 s with the window not answering (the user's
            // "freezing for statistics", 2026-11-12; scratchpad/paintcost "stats"). Now the rows are kept here and
            // Windows makes the ten or so it shows.
            VirtualMode = true;
            // The row under the pointer and the row picked are shown. "Delete entry" acts on the selected row, and
            // nothing showed which row that was.
            HoverHighlight = true;
            HighlightSelection = true;
            // Up to 200 rows of six cells: assistive tools get the list as one element (ThemedListView).
            CellsForAccessibility = false;
        }

        /// <summary>How many rows the list holds.</summary>
        public int RowCount => _rows.Length;

        /// <summary>The row at <paramref name="index"/>, or null.</summary>
        public ListViewItem RowAt(int index) => index >= 0 && index < _rows.Length ? _rows[index] : null;

        /// <summary>The selected row, or null.</summary>
        public ListViewItem SelectedRow
        {
            get
            {
                try { return SelectedIndices.Count > 0 ? RowAt(SelectedIndices[0]) : null; }
                catch { return null; }
            }
        }

        /// <summary>
        /// Shows <paramref name="rows"/> (kept, not copied), keeping the selection on the same session (Tag) when it
        /// is still there.
        /// </summary>
        public void SetRows(ListViewItem[] rows)
        {
            object keep = SelectedRow?.Tag;
            _rows = rows ?? Array.Empty<ListViewItem>();
            try
            {
                SelectedIndices.Clear();
                VirtualListSize = _rows.Length;
                if (keep != null)
                {
                    int i = Array.FindIndex(_rows, r => ReferenceEquals(r.Tag, keep));
                    if (i >= 0) { SelectedIndices.Add(i); }
                }
            }
            catch { }
            Invalidate();
        }

        /// <summary>Puts the rows in <paramref name="comparer"/>'s order; rows it calls equal keep their order.</summary>
        public void SortRows(IComparer comparer)
        {
            if (comparer == null || _rows.Length < 2) { return; }
            var order = new List<ListViewItem>(_rows);
            var position = new Dictionary<ListViewItem, int>(order.Count);
            for (int i = 0; i < order.Count; i++) { position[order[i]] = i; }
            order.Sort((a, b) =>
            {
                int c = comparer.Compare(a, b);
                return c != 0 ? c : position[a].CompareTo(position[b]);
            });
            SetRows(order.ToArray());
        }

        protected override void OnRetrieveVirtualItem(RetrieveVirtualItemEventArgs e)
        {
            // Never null: Windows asks for rows the list says it has, and a null throws inside the paint.
            e.Item = RowAt(e.ItemIndex) ?? new ListViewItem(new string[Math.Max(1, Columns.Count)]);
            base.OnRetrieveVirtualItem(e);
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
