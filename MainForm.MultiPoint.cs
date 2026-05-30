using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Persistence;

namespace AutoClicker.UI
{
    public partial class MainForm
    {
        // The working list of points being edited. Copied to/from the active
        // profile by the Clicker tab's mapping methods.
        private readonly List<ClickPoint> _workingPoints = new List<ClickPoint>();

        // Guards programmatic checkbox updates during list population so the
        // ItemChecked handler doesn't fight RefreshPointsList.
        private bool _suppressPointCheck;

        private void BuildMultiPointTab()
        {
            var page = new TabPage("Multi-Point");

            var help = UiFactory.Label(
                "Define a sequence of points. In Multi-Point mode the engine visits the " +
                "enabled points using the chosen order. Tick a row to enable/disable it; " +
                "Delete removes the selected point, Ctrl+D duplicates it.",
                12, 12);
            help.MaximumSize = new Size(720, 0);
            help.AutoSize = true;
            help.ForeColor = _theme.TextMuted;

            // ── Order + cycle info row ─────────────────────────────────────────
            var orderLabel = UiFactory.Label("Order:", 12, 56, FontStyle.Bold);
            _pointOrderCombo = UiFactory.Combo(66, 53, 150, "Sequential", "Reverse", "Random", "Ping-Pong");
            _pointOrderCombo.SelectedIndex = 0;

            _cycleInfoLabel = UiFactory.Label("", 232, 56, FontStyle.Italic, 9f);
            _cycleInfoLabel.AutoSize = true;
            _cycleInfoLabel.ForeColor = _theme.TextMuted;

            _pointsList = new ListView
            {
                Left = 12,
                Top = 86,
                Width = 540,
                Height = 444,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = false,
                CheckBoxes = true
            };
            _pointsList.Columns.Add("#", 40);
            _pointsList.Columns.Add("Label", 130);
            _pointsList.Columns.Add("X", 66);
            _pointsList.Columns.Add("Y", 66);
            _pointsList.Columns.Add("Button", 70);
            _pointsList.Columns.Add("Type", 64);
            _pointsList.Columns.Add("Dwell", 56);
            _pointsList.Columns.Add("Rep", 44);
            _pointsList.DoubleClick += (s, e) => EditSelectedPoint();
            _pointsList.ItemActivate += (s, e) => EditSelectedPoint();
            _pointsList.ItemChecked += OnPointItemChecked;
            _pointsList.KeyDown += OnPointsListKeyDown;

            int bx = 568;
            _addPointBtn = UiFactory.Button("Add…", bx, 86, 130, 30);
            _addPointBtn.Click += OnAddPoint;

            _capturePointBtn = UiFactory.Button("Quick Capture", bx, 120, 130, 30);
            _capturePointBtn.Click += OnQuickCapturePoint;

            _editPointBtn = UiFactory.Button("Edit…", bx, 154, 130, 30);
            _editPointBtn.Click += (s, e) => EditSelectedPoint();

            _duplicatePointBtn = UiFactory.Button("Duplicate", bx, 188, 130, 30);
            _duplicatePointBtn.Click += OnDuplicatePoint;

            _togglePointBtn = UiFactory.Button("Toggle On/Off", bx, 222, 130, 30);
            _togglePointBtn.Click += OnTogglePoint;

            _removePointBtn = UiFactory.Button("Remove", bx, 256, 130, 30);
            _removePointBtn.Click += OnRemovePoint;

            _movePointUpBtn = UiFactory.Button("Move Up", bx, 300, 130, 30);
            _movePointUpBtn.Click += (s, e) => MovePoint(-1);

            _movePointDownBtn = UiFactory.Button("Move Down", bx, 334, 130, 30);
            _movePointDownBtn.Click += (s, e) => MovePoint(1);

            _showPointsBtn = UiFactory.Button("Show on screen", bx, 380, 130, 30);
            _showPointsBtn.Click += OnShowPointsOverlay;

            _clearPointsBtn = UiFactory.Button("Clear All", bx, 414, 130, 30);
            _clearPointsBtn.Click += OnClearPoints;

            var applyNote = UiFactory.Label(
                "Tip: press Save on the\nClicker tab to store these\npoints in the profile.",
                bx, 470, FontStyle.Italic, 8.25f);
            applyNote.ForeColor = _theme.TextMuted;

            page.Controls.Add(help);
            page.Controls.Add(orderLabel);
            page.Controls.Add(_pointOrderCombo);
            page.Controls.Add(_cycleInfoLabel);
            page.Controls.Add(_pointsList);
            page.Controls.Add(_addPointBtn);
            page.Controls.Add(_capturePointBtn);
            page.Controls.Add(_editPointBtn);
            page.Controls.Add(_duplicatePointBtn);
            page.Controls.Add(_togglePointBtn);
            page.Controls.Add(_removePointBtn);
            page.Controls.Add(_movePointUpBtn);
            page.Controls.Add(_movePointDownBtn);
            page.Controls.Add(_showPointsBtn);
            page.Controls.Add(_clearPointsBtn);
            page.Controls.Add(applyNote);

            _tabs.TabPages.Add(page);
        }

        private void RefreshPointsList()
        {
            if (_pointsList == null)
            {
                return;
            }

            _suppressPointCheck = true;
            _pointsList.BeginUpdate();
            _pointsList.Items.Clear();

            for (int i = 0; i < _workingPoints.Count; i++)
            {
                ClickPoint p = _workingPoints[i];
                var item = new ListViewItem((i + 1).ToString());
                item.SubItems.Add(p.Label);
                item.SubItems.Add(p.X.ToString());
                item.SubItems.Add(p.Y.ToString());
                item.SubItems.Add(p.Button.ToString());
                item.SubItems.Add(p.Style.ToString());
                item.SubItems.Add(p.DwellMilliseconds.ToString());
                item.SubItems.Add((p.Repeat < 1 ? 1 : p.Repeat).ToString());
                item.Tag = i;
                item.Checked = p.Enabled;

                if (!p.Enabled)
                {
                    item.ForeColor = _theme.TextMuted;
                }

                _pointsList.Items.Add(item);
            }

            _pointsList.EndUpdate();
            _suppressPointCheck = false;
            UpdateCycleInfo();
        }

        /// <summary>Shows how many points are active and the clicks per full cycle.</summary>
        private void UpdateCycleInfo()
        {
            if (_cycleInfoLabel == null)
            {
                return;
            }

            int enabled = 0;
            long clicksPerCycle = 0;
            foreach (ClickPoint p in _workingPoints)
            {
                if (p.Enabled)
                {
                    enabled++;
                    clicksPerCycle += p.Repeat < 1 ? 1 : p.Repeat;
                }
            }

            _cycleInfoLabel.Text = enabled == 0
                ? "No active points"
                : $"{enabled} active point(s)  •  {clicksPerCycle} click(s) per cycle";
        }

        private int GetSelectedPointIndex()
        {
            if (_pointsList.SelectedItems.Count == 0)
            {
                return -1;
            }

            return _pointsList.SelectedItems[0].Index;
        }

        private void OnAddPoint(object sender, EventArgs e)
        {
            using (var editor = new ClickPointEditorForm(_theme, null))
            {
                if (editor.ShowDialog(this) == DialogResult.OK && editor.Result != null)
                {
                    _workingPoints.Add(editor.Result);
                    RefreshPointsList();
                }
            }
        }

        private void OnQuickCapturePoint(object sender, EventArgs e)
        {
            bool wasVisible = Visible;
            Hide();
            System.Threading.Thread.Sleep(150);

            int added = 0;
            try
            {
                // Keep capturing points until the user cancels (Esc), so a whole
                // sequence can be defined in one go instead of one click per dialog.
                while (true)
                {
                    using (var picker = new CoordinatePickerForm(_theme))
                    {
                        if (picker.ShowDialog() != DialogResult.OK)
                        {
                            break;
                        }

                        _workingPoints.Add(new ClickPoint(picker.PickedX, picker.PickedY)
                        {
                            Label = "Point " + (_workingPoints.Count + 1)
                        });
                        added++;
                    }
                }
            }
            finally
            {
                if (added > 0)
                {
                    RefreshPointsList();
                    _statusState.Text = added == 1
                        ? "Captured 1 point"
                        : $"Captured {added} points";
                }

                if (wasVisible)
                {
                    Show();
                    EnsureOnScreen();
                    Activate();
                    ReassertTopMost();
                }
            }
        }

        private void EditSelectedPoint()
        {
            int index = GetSelectedPointIndex();
            if (index < 0 || index >= _workingPoints.Count)
            {
                return;
            }

            using (var editor = new ClickPointEditorForm(_theme, _workingPoints[index]))
            {
                if (editor.ShowDialog(this) == DialogResult.OK && editor.Result != null)
                {
                    _workingPoints[index] = editor.Result;
                    RefreshPointsList();
                    if (index < _pointsList.Items.Count)
                    {
                        _pointsList.Items[index].Selected = true;
                    }
                }
            }
        }

        /// <summary>Toggles a point's Enabled flag when its checkbox is ticked.</summary>
        private void OnPointItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (_suppressPointCheck || e.Item == null)
            {
                return;
            }

            int index = e.Item.Tag is int tag ? tag : e.Item.Index;
            if (index < 0 || index >= _workingPoints.Count)
            {
                return;
            }

            _workingPoints[index].Enabled = e.Item.Checked;
            e.Item.ForeColor = e.Item.Checked ? _theme.Text : _theme.TextMuted;
            UpdateCycleInfo();
        }

        /// <summary>Keyboard shortcuts in the points list: Delete, Ctrl+D.</summary>
        private void OnPointsListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                OnRemovePoint(null, EventArgs.Empty);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.D)
            {
                OnDuplicatePoint(null, EventArgs.Empty);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void OnRemovePoint(object sender, EventArgs e)
        {
            int index = GetSelectedPointIndex();
            if (index < 0 || index >= _workingPoints.Count)
            {
                return;
            }

            _workingPoints.RemoveAt(index);
            RefreshPointsList();
        }

        private void MovePoint(int direction)
        {
            int index = GetSelectedPointIndex();
            if (index < 0)
            {
                return;
            }

            int target = index + direction;
            if (target < 0 || target >= _workingPoints.Count)
            {
                return;
            }

            ClickPoint temp = _workingPoints[index];
            _workingPoints[index] = _workingPoints[target];
            _workingPoints[target] = temp;

            RefreshPointsList();
            if (target < _pointsList.Items.Count)
            {
                _pointsList.Items[target].Selected = true;
            }
        }

        private void OnClearPoints(object sender, EventArgs e)
        {
            if (_workingPoints.Count == 0)
            {
                return;
            }

            var confirm = MessageBox.Show(this,
                "Remove all points?",
                "Tempo",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm == DialogResult.Yes)
            {
                _workingPoints.Clear();
                RefreshPointsList();
            }
        }

        private void OnDuplicatePoint(object sender, EventArgs e)
        {
            int index = GetSelectedPointIndex();
            if (index < 0 || index >= _workingPoints.Count)
            {
                return;
            }

            ClickPoint copy = _workingPoints[index].Clone();
            copy.Label = copy.Label + " copy";
            _workingPoints.Insert(index + 1, copy);
            RefreshPointsList();
            if (index + 1 < _pointsList.Items.Count)
            {
                _pointsList.Items[index + 1].Selected = true;
            }
        }

        private void OnTogglePoint(object sender, EventArgs e)
        {
            int index = GetSelectedPointIndex();
            if (index < 0 || index >= _workingPoints.Count)
            {
                return;
            }

            _workingPoints[index].Enabled = !_workingPoints[index].Enabled;
            RefreshPointsList();
            if (index < _pointsList.Items.Count)
            {
                _pointsList.Items[index].Selected = true;
            }
        }

        private void OnShowPointsOverlay(object sender, EventArgs e)
        {
            bool any = false;
            foreach (ClickPoint p in _workingPoints)
            {
                if (p.Enabled) { any = true; break; }
            }

            if (!any)
            {
                MessageBox.Show(this, "Add at least one enabled point first.",
                    "Tempo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Snapshot the points so edits during display don't matter.
            var snapshot = new List<ClickPoint>();
            foreach (ClickPoint p in _workingPoints)
            {
                snapshot.Add(p.Clone());
            }

            using (var overlay = new PointsOverlayForm(_theme, snapshot))
            {
                overlay.ShowDialog(this);
            }
        }

        /// <summary>
        /// Adds a multi-point target at the current cursor position (hotkey driven),
        /// so the user can build a point list without opening dialogs.
        /// </summary>
        private void AddPointAtCursor()
        {
            if (!AutoClicker.Utils.ScreenGeometry.TryGetCursorPosition(out int x, out int y))
            {
                return;
            }

            _workingPoints.Add(new ClickPoint(x, y)
            {
                Label = "Point " + (_workingPoints.Count + 1)
            });
            RefreshPointsList();

            _statusState.Text = $"Added point at ({x}, {y})";
        }

        /// <summary>Flips anti-freeze protection on/off and reflects it in the UI.</summary>
        private void ToggleAntiFreezeProtection()
        {
            _settings.AntiFreezeEnabled = !_settings.AntiFreezeEnabled;

            if (_antiFreezeCheck != null)
            {
                _suppressAntiFreeze = true;
                try { _antiFreezeCheck.Checked = _settings.AntiFreezeEnabled; }
                finally { _suppressAntiFreeze = false; }
                UpdateAntiFreezeControlsEnabled();
            }

            ApplyAntiFreezeToEngine();
            SettingsManager.Save(_settings);
            _statusState.Text = _settings.AntiFreezeEnabled ? "Anti-freeze on" : "Anti-freeze off";
        }

        /// <summary>
        /// Highlights the point the engine is currently clicking, when a multi-point
        /// run is in progress. Called from the live UI timer.
        /// </summary>
        private void UpdateMultiPointLive()
        {
            if (_pointsList == null)
            {
                return;
            }

            if (!_engine.IsRunning)
            {
                return;
            }

            int idx = _engine.CurrentPointIndex;
            if (idx < 0 || idx >= _pointsList.Items.Count)
            {
                return;
            }

            // Only move the selection if it actually changed, to avoid flicker.
            if (_pointsList.SelectedItems.Count == 1 && _pointsList.SelectedItems[0].Index == idx)
            {
                return;
            }

            _pointsList.SelectedItems.Clear();
            _pointsList.Items[idx].Selected = true;
            _pointsList.Items[idx].EnsureVisible();
        }
    }
}
