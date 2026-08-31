using System;
using System.Collections;
using System.Windows.Forms;
using AutoClicker.Models;

namespace AutoClicker.UI
{
    /// <summary>
    /// Sorts session-history ListView rows by a chosen column, using the underlying
    /// <see cref="SessionRecord"/> (stored in each item's Tag) so numeric and date
    /// columns sort correctly rather than lexically.
    /// </summary>
    internal sealed class HistoryColumnComparer : IComparer
    {
        private readonly int _column;
        private readonly bool _ascending;

        public HistoryColumnComparer(int column, bool ascending)
        {
            _column = column;
            _ascending = ascending;
        }

        public int Compare(object x, object y)
        {
            var ix = x as ListViewItem;
            var iy = y as ListViewItem;
            if (ix == null || iy == null)
            {
                return 0;
            }

            var rx = ix.Tag as SessionRecord;
            var ry = iy.Tag as SessionRecord;

            int result;
            if (rx != null && ry != null)
            {
                switch (_column)
                {
                    case 0: result = rx.WhenUtc.CompareTo(ry.WhenUtc); break;
                    case 1: result = string.Compare(rx.Profile ?? "", ry.Profile ?? "", StringComparison.OrdinalIgnoreCase); break;
                    case 2: result = rx.Clicks.CompareTo(ry.Clicks); break;
                    case 3: result = rx.DurationSeconds.CompareTo(ry.DurationSeconds); break;
                    case 4: result = rx.AverageCps.CompareTo(ry.AverageCps); break;
                    case 5: result = rx.PeakCps.CompareTo(ry.PeakCps); break;
                    default: result = 0; break;
                }
            }
            else
            {
                result = string.Compare(ix.Text, iy.Text, StringComparison.OrdinalIgnoreCase);
            }

            return _ascending ? result : -result;
        }
    }
}
