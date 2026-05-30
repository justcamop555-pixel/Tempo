using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// A step-level editor for a recorded macro. Works on a private clone and only
    /// returns the edited macro (via <see cref="Result"/>) when the user clicks OK,
    /// so cancelling leaves the original untouched.
    /// </summary>
    public sealed class MacroEditorForm : Form
    {
        private readonly Theme _theme;
        private readonly Macro _working;
        private readonly ListView _list;
        private readonly Label _summary;

        public Macro Result { get; private set; }

        public MacroEditorForm(Theme theme, Macro macro)
        {
            _theme = theme ?? Theme.ForKind(ThemeKind.Dark);
            _working = (macro ?? new Macro("Macro")).Clone();

            Text = "Edit Macro – " + _working.Name;
            Size = new Size(660, 648);
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(600, 580);
            MaximizeBox = true;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = _theme.Background;
            ForeColor = _theme.Text;
            Font = UiFactory.BodyFont;

            _list = new ListView
            {
                Left = 12,
                Top = 12,
                Width = 420,
                Height = 470,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            _list.Columns.Add("#", 44);
            _list.Columns.Add("Time", 78);
            _list.Columns.Add("Action", 130);
            _list.Columns.Add("Detail", 168);
            _list.DoubleClick += (s, e) => EditDelay();

            int bx = 444;
            int bw = 168;
            int y = 12;
            int gap = 36;

            Button Add(string text, EventHandler handler)
            {
                var b = UiFactory.Button(text, bx, y, bw, 30);
                b.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                b.Click += handler;
                Controls.Add(b);
                y += gap;
                return b;
            }

            Add("Delete selected", (s, e) => DeleteSelected());
            Add("Move up", (s, e) => MoveSelected(-1));
            Add("Move down", (s, e) => MoveSelected(1));
            y += 12;
            Add("Edit delay…", (s, e) => EditDelay());
            Add("Insert delay…", (s, e) => InsertDelay());
            Add("Scale all delays…", (s, e) => ScaleDelays());
            y += 12;
            Add("Remove all moves", (s, e) => RemoveOfType(MacroActionType.MouseMove));
            Add("Remove all keys", (s, e) => RemoveKeys());
            y += 12;
            Add("Insert click…", (s, e) => InsertClick());
            Add("Insert key…", (s, e) => InsertKey());
            Add("Edit position…", (s, e) => EditPosition());

            _summary = UiFactory.Label("", bx, y + 8, FontStyle.Italic, 8.5f);
            _summary.ForeColor = _theme.TextMuted;
            _summary.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _summary.MaximumSize = new Size(bw, 0);
            Controls.Add(_summary);

            var ok = UiFactory.PrimaryButton("Save", 444 - 0, 0, 80, 30, _theme);
            ok.Text = "Save";
            ok.SetBounds(bx, ClientSize.Height - 42, 80, 30);
            ok.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            ok.Click += (s, e) => { Result = _working; DialogResult = DialogResult.OK; Close(); };

            var cancel = UiFactory.Button("Cancel", bx + 88, ClientSize.Height - 42, 80, 30);
            cancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(_list);
            Controls.Add(ok);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;

            RefreshList();
            ThemeManager.Apply(this, _theme);
        }

        private void RefreshList()
        {
            _list.BeginUpdate();
            _list.Items.Clear();

            long cumulativeMs = 0;

            for (int i = 0; i < _working.Actions.Count; i++)
            {
                MacroAction a = _working.Actions[i];
                var item = new ListViewItem((i + 1).ToString());
                item.SubItems.Add(FormatTimestamp(cumulativeMs));
                item.SubItems.Add(a.Type.ToString());
                item.SubItems.Add(DetailFor(a));
                item.Tag = i;
                _list.Items.Add(item);

                // The Time column shows when each step *starts*. A Delay step
                // therefore advances the clock for the next row, not its own.
                if (a.Type == MacroActionType.Delay)
                {
                    cumulativeMs += a.DelayMilliseconds;
                }
            }

            _list.EndUpdate();

            _summary.Text =
                $"{_working.StepCount} steps\n" +
                $"≈ {_working.EstimatedDurationMs} ms total";
        }

        private static string FormatTimestamp(long ms)
        {
            if (ms >= 60_000)
            {
                long min = ms / 60_000;
                long rem = ms - min * 60_000;
                return $"{min}m {rem / 1000.0:0.0}s";
            }
            if (ms >= 1_000)
            {
                return $"{ms / 1000.0:0.00}s";
            }
            return ms + "ms";
        }

        private static string DetailFor(MacroAction a)
        {
            switch (a.Type)
            {
                case MacroActionType.Delay:
                    return a.DelayMilliseconds + " ms";
                case MacroActionType.MouseMove:
                    return $"({a.X}, {a.Y})";
                case MacroActionType.Wheel:
                    return "delta " + a.WheelDelta;
                case MacroActionType.KeyDown:
                case MacroActionType.KeyUp:
                    return MacroAction.KeyName(a.VirtualKey);
                default:
                    return $"({a.X}, {a.Y})";
            }
        }

        private int FirstSelectedIndex()
        {
            return _list.SelectedItems.Count == 0 ? -1 : _list.SelectedItems[0].Index;
        }

        private void DeleteSelected()
        {
            if (_list.SelectedItems.Count == 0)
            {
                return;
            }

            // Collect indices descending so removals do not shift the rest.
            var indices = new System.Collections.Generic.List<int>();
            foreach (ListViewItem item in _list.SelectedItems)
            {
                indices.Add(item.Index);
            }
            indices.Sort();
            indices.Reverse();

            foreach (int idx in indices)
            {
                if (idx >= 0 && idx < _working.Actions.Count)
                {
                    _working.Actions.RemoveAt(idx);
                }
            }

            RefreshList();
        }

        private void MoveSelected(int delta)
        {
            int index = FirstSelectedIndex();
            if (index < 0)
            {
                return;
            }

            int target = index + delta;
            if (target < 0 || target >= _working.Actions.Count)
            {
                return;
            }

            MacroAction temp = _working.Actions[index];
            _working.Actions[index] = _working.Actions[target];
            _working.Actions[target] = temp;

            RefreshList();
            if (target < _list.Items.Count)
            {
                _list.Items[target].Selected = true;
                _list.Items[target].Focused = true;
            }
        }

        private void EditDelay()
        {
            int index = FirstSelectedIndex();
            if (index < 0)
            {
                return;
            }

            MacroAction a = _working.Actions[index];
            if (a.Type != MacroActionType.Delay)
            {
                MessageBox.Show(this, "Select a delay step to edit its duration.",
                    "Tempo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string input = TextPromptForm.Show(this, _theme, "Edit Delay",
                "Delay in milliseconds:", a.DelayMilliseconds.ToString());

            if (TryParseInt(input, out int ms) && ms >= 0)
            {
                a.DelayMilliseconds = ms;
                RefreshList();
                if (index < _list.Items.Count)
                {
                    _list.Items[index].Selected = true;
                }
            }
        }

        private void InsertDelay()
        {
            string input = TextPromptForm.Show(this, _theme, "Insert Delay",
                "Delay in milliseconds:", "100");

            if (!TryParseInt(input, out int ms) || ms < 0)
            {
                return;
            }

            int index = FirstSelectedIndex();
            var step = new MacroAction(MacroActionType.Delay) { DelayMilliseconds = ms };

            if (index < 0)
            {
                _working.Actions.Add(step);
            }
            else
            {
                _working.Actions.Insert(index + 1, step);
            }

            RefreshList();
        }

        private void ScaleDelays()
        {
            string input = TextPromptForm.Show(this, _theme, "Scale Delays",
                "Scale all delays by percent (e.g. 50 = half speed gaps, 200 = double):", "100");

            if (!TryParseDouble(input, out double percent) || percent <= 0)
            {
                return;
            }

            double factor = percent / 100.0;
            foreach (var a in _working.Actions)
            {
                if (a.Type == MacroActionType.Delay)
                {
                    long scaled = (long)Math.Round(a.DelayMilliseconds * factor);
                    if (scaled < 0) scaled = 0;
                    if (scaled > int.MaxValue) scaled = int.MaxValue;
                    a.DelayMilliseconds = (int)scaled;
                }
            }

            RefreshList();
        }

        private void RemoveOfType(MacroActionType type)
        {
            _working.Actions.RemoveAll(a => a.Type == type);
            RefreshList();
        }

        private int InsertIndex()
        {
            int sel = FirstSelectedIndex();
            return sel < 0 ? _working.Actions.Count : sel + 1;
        }

        private void InsertClick()
        {
            using (var dlg = new InsertClickForm(_theme))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                int x = 0, y = 0;

                if (dlg.PickPosition)
                {
                    Hide();
                    System.Threading.Thread.Sleep(120);
                    try
                    {
                        using (var picker = new CoordinatePickerForm(_theme))
                        {
                            if (picker.ShowDialog() != DialogResult.OK)
                            {
                                return;
                            }
                            x = picker.PickedX;
                            y = picker.PickedY;
                        }
                    }
                    finally
                    {
                        Show();
                        Activate();
                    }
                }
                else
                {
                    ScreenGeometry.TryGetCursorPosition(out x, out y);
                }

                MacroActionType down, up;
                switch (dlg.SelectedButton)
                {
                    case MouseButtonType.Right:
                        down = MacroActionType.RightDown; up = MacroActionType.RightUp; break;
                    case MouseButtonType.Middle:
                        down = MacroActionType.MiddleDown; up = MacroActionType.MiddleUp; break;
                    default:
                        down = MacroActionType.LeftDown; up = MacroActionType.LeftUp; break;
                }

                int at = InsertIndex();
                _working.Actions.Insert(at, new MacroAction(down) { X = x, Y = y });
                _working.Actions.Insert(at + 1, new MacroAction(MacroActionType.Delay) { DelayMilliseconds = 40 });
                _working.Actions.Insert(at + 2, new MacroAction(up) { X = x, Y = y });
                RefreshList();
            }
        }

        private void InsertKey()
        {
            using (var dlg = new KeyCaptureForm(_theme))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.VirtualKey == 0)
                {
                    return;
                }

                int at = InsertIndex();
                _working.Actions.Insert(at, new MacroAction(MacroActionType.KeyDown) { VirtualKey = dlg.VirtualKey });
                _working.Actions.Insert(at + 1, new MacroAction(MacroActionType.Delay) { DelayMilliseconds = 30 });
                _working.Actions.Insert(at + 2, new MacroAction(MacroActionType.KeyUp) { VirtualKey = dlg.VirtualKey });
                RefreshList();
            }
        }

        private void EditPosition()
        {
            int index = FirstSelectedIndex();
            if (index < 0)
            {
                return;
            }

            MacroAction a = _working.Actions[index];
            bool hasPosition =
                a.Type == MacroActionType.MouseMove ||
                a.Type == MacroActionType.LeftDown || a.Type == MacroActionType.LeftUp ||
                a.Type == MacroActionType.RightDown || a.Type == MacroActionType.RightUp ||
                a.Type == MacroActionType.MiddleDown || a.Type == MacroActionType.MiddleUp;

            if (!hasPosition)
            {
                MessageBox.Show(this, "Select a move or click step to edit its position.",
                    "Tempo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dlg = new PointInputForm(_theme, a.X, a.Y))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    a.X = dlg.X;
                    a.Y = dlg.Y;
                    RefreshList();
                    if (index < _list.Items.Count)
                    {
                        _list.Items[index].Selected = true;
                    }
                }
            }
        }

        private void RemoveKeys()
        {
            _working.Actions.RemoveAll(a =>
                a.Type == MacroActionType.KeyDown || a.Type == MacroActionType.KeyUp);
            RefreshList();
        }

        private static bool TryParseInt(string text, out int value)
        {
            return int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out value);
        }

        private static bool TryParseDouble(string text, out double value)
        {
            return double.TryParse((text ?? string.Empty).Trim(),
                NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
