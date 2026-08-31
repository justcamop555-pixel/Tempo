using System;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Models;

namespace AutoClicker.UI
{
    /// <summary>
    /// The Macros tab's Live Monitor list. All the drawing lives in
    /// <see cref="ThemedListView"/>; this only decides what colour a step is.
    ///
    /// Every action type gets its own saturated hue so a recording can be read at a
    /// glance — the green run of clicks, the grey waits between them, the amber
    /// keystrokes — rather than as four columns of identical text. The hues are fixed
    /// rather than tints of the theme accent, so "Left click" is the same green on
    /// every theme; the base class then lightens or darkens each one until it clears
    /// 4.5:1 against the row behind it, which is what keeps them readable on light
    /// themes as well as dark.
    /// </summary>
    public sealed class LiveStepListView : ThemedListView
    {
        public LiveStepListView()
        {
            HeaderStyle = ColumnHeaderStyle.Nonclickable;
            HideSelection = false;
        }

        /// <summary>The "Action" column carries the chip and the colour.</summary>
        protected override int ChipColumn => 2;

        /// <summary>Step number and elapsed time are labels, not data.</summary>
        protected override bool IsMutedColumn(int column) => column == 0 || column == 1;

        protected override Color RowAccent(ListViewItem item) => ColourFor(TypeOf(item));

        public static Color ColourFor(MacroActionType t)
        {
            switch (t)
            {
                case MacroActionType.LeftDown:
                case MacroActionType.LeftUp:
                    return Color.FromArgb(61, 220, 132);    // green — the common click
                case MacroActionType.RightDown:
                case MacroActionType.RightUp:
                    return Color.FromArgb(255, 159, 67);    // orange
                case MacroActionType.MiddleDown:
                case MacroActionType.MiddleUp:
                    return Color.FromArgb(176, 133, 245);   // violet
                case MacroActionType.KeyDown:
                case MacroActionType.KeyUp:
                    return Color.FromArgb(255, 209, 102);   // amber — keystrokes
                case MacroActionType.MouseMove:
                    return Color.FromArgb(79, 168, 255);    // blue — travel
                case MacroActionType.Wheel:
                    return Color.FromArgb(34, 211, 238);    // cyan
                case MacroActionType.Delay:
                    return Color.FromArgb(138, 151, 165);   // grey — waiting, not doing
                case MacroActionType.Script:
                    return Color.FromArgb(120, 222, 160);   // green — the one step that
                                                            // runs something outside Tempo
                default:
                    return Color.FromArgb(200, 200, 200);
            }
        }

        /// <summary>
        /// The action type for a row, taken from <see cref="ListViewItem.Tag"/> where the
        /// row set one. Falls back to matching the displayed name so a row built
        /// elsewhere still colours correctly rather than throwing.
        /// </summary>
        private static MacroActionType TypeOf(ListViewItem item)
        {
            if (item?.Tag is MacroActionType t)
            {
                return t;
            }

            try
            {
                string name = item != null && item.SubItems.Count > 2 ? item.SubItems[2].Text : null;
                if (!string.IsNullOrEmpty(name))
                {
                    foreach (MacroActionType candidate in Enum.GetValues(typeof(MacroActionType)))
                    {
                        if (string.Equals(MacroAction.FriendlyType(candidate), name, StringComparison.Ordinal))
                        {
                            return candidate;
                        }
                    }
                }
            }
            catch { }

            return MacroActionType.Delay;
        }
    }
}
