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
            var page = new BackdropTabPage(Utils.Localization.T("Multi-Point")) { AutoScroll = true };
            page.Name = "multipoint";   // stable key for LastTabKey

            string helpText =
                "Define a sequence of points. In Multi-Point mode the engine visits the " +
                "enabled points using the chosen order. Tick a row to enable/disable it; " +
                "Delete removes the selected point, Ctrl+D duplicates it, Ctrl+\u2191/\u2193 reorders, Ctrl+Home/End jumps it to the ends, " +
                "and Alt+arrows nudge it a pixel at a time (hold Shift for 10).";
            var help = UiFactory.Label(helpText, 12, 12);
            help.MaximumSize = new Size(720, 0);
            help.AutoSize = true;
            help.ForeColor = _theme.TextMuted;

            // ── Order + cycle info row ─────────────────────────────────────────
            var orderLabel = UiFactory.Label(Utils.Localization.T("Order:"), 12, 56, FontStyle.Bold);
            _pointOrderCombo = UiFactory.Combo(66, 53, 150, Utils.Localization.T("Sequential"), Utils.Localization.T("Reverse"), Utils.Localization.T("Random"), Utils.Localization.T("Ping-Pong"));
            _pointOrderCombo.SelectedIndex = 0;

            _cycleInfoLabel = UiFactory.Label("", 232, 56, FontStyle.Italic, 9f);
            _cycleInfoLabel.AutoSize = false;
            _cycleInfoLabel.Width = 360;
            _cycleInfoLabel.Height = 18;
            _cycleInfoLabel.AutoEllipsis = true;
            _cycleInfoLabel.ForeColor = _theme.TextMuted;

            _pointsList = new ClickPointListView
            {
                Left = 12,
                Top = 86,
                Width = 552,
                Height = 444
            };
            // Column widths sum to 528, leaving room inside the 552-px list for its border
            // and a vertical scrollbar — so the last "Rep" column is never clipped once the
            // list has enough rows to scroll (it used to be cut off at the right edge).
            _pointsList.Columns.Add("#", 36);
            _pointsList.Columns.Add(Utils.Localization.T("Label"), 130);
            _pointsList.Columns.Add("X", 56, HorizontalAlignment.Right);
            _pointsList.Columns.Add("Y", 56, HorizontalAlignment.Right);
            _pointsList.Columns.Add(Utils.Localization.T("Button"), 72);
            _pointsList.Columns.Add(Utils.Localization.T("Type"), 72);
            _pointsList.Columns.Add(Utils.Localization.T("Dwell"), 54, HorizontalAlignment.Right);
            _pointsList.Columns.Add(Utils.Localization.T("Rep"), 52, HorizontalAlignment.Right);
            // The columns add up to 24px less than the control, leaving dead space at
            // the right edge. ThemedListView's own OnSizeChanged fires when Width is
            // set in the initializer above — BEFORE any column exists — so the only
            // fit that counts is one after the columns are added.
            _pointsList.FitLastColumn();
            _pointsList.DoubleClick += (s, e) => EditSelectedPoint();
            _pointsList.ItemActivate += (s, e) => EditSelectedPoint();
            _pointsList.ItemChecked += OnPointItemChecked;
            _pointsList.KeyDown += OnPointsListKeyDown;
            _pointsList.SelectedIndexChanged += (s, e) => UpdatePointButtonStates();

            // Every item goes through T(). Three of these — Edit, Duplicate, Remove —
            // were raw literals sitting between translated siblings, so the right-click
            // menu came up half in English in every other language.
            var pointMenu = new ContextMenuStrip();
            pointMenu.Items.Add(Utils.Localization.T("Edit…"), null, (s, e) => EditSelectedPoint());
            pointMenu.Items.Add(Utils.Localization.T("Duplicate"), null, OnDuplicatePoint);
            pointMenu.Items.Add(Utils.Localization.T("Toggle On / Off"), null, OnTogglePoint);
            pointMenu.Items.Add(new ToolStripSeparator());
            pointMenu.Items.Add(Utils.Localization.T("Move to top"), null, (s, e) => MovePointToEnd(true));
            pointMenu.Items.Add(Utils.Localization.T("Move to bottom"), null, (s, e) => MovePointToEnd(false));
            pointMenu.Items.Add(new ToolStripSeparator());
            pointMenu.Items.Add(Utils.Localization.T("Remove"), null, OnRemovePoint);
            _pointsList.ContextMenuStrip = pointMenu;
            _pointsList.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    var hit = _pointsList.HitTest(e.Location);
                    if (hit.Item != null) hit.Item.Selected = true;
                }
            };

            int bx = 568;
            _addPointBtn = UiFactory.Button(Utils.Localization.T("Add…"), bx, 86, 130, 30);
            _addPointBtn.Click += OnAddPoint;

            _capturePointBtn = UiFactory.Button(Utils.Localization.T("Quick Capture"), bx, 120, 130, 30);
            _capturePointBtn.Click += OnQuickCapturePoint;

            _editPointBtn = UiFactory.Button(Utils.Localization.T("Edit…"), bx, 154, 130, 30);
            _editPointBtn.Click += (s, e) => EditSelectedPoint();

            _duplicatePointBtn = UiFactory.Button(Utils.Localization.T("Duplicate"), bx, 188, 130, 30);
            _duplicatePointBtn.Click += OnDuplicatePoint;

            _togglePointBtn = UiFactory.Button(Utils.Localization.T("Toggle On/Off"), bx, 222, 130, 30);
            _togglePointBtn.Click += OnTogglePoint;

            _removePointBtn = UiFactory.Button(Utils.Localization.T("Remove"), bx, 256, 130, 30);
            _removePointBtn.Click += OnRemovePoint;

            _movePointUpBtn = UiFactory.Button(Utils.Localization.T("Move Up"), bx, 300, 130, 30);
            _movePointUpBtn.Click += (s, e) => MovePoint(-1);

            _movePointDownBtn = UiFactory.Button(Utils.Localization.T("Move Down"), bx, 334, 130, 30);
            _movePointDownBtn.Click += (s, e) => MovePoint(1);

            _showPointsBtn = UiFactory.Button(Utils.Localization.T("Show on screen"), bx, 380, 130, 30);
            _showPointsBtn.Click += OnShowPointsOverlay;

            _clearPointsBtn = UiFactory.Button(Utils.Localization.T("Clear All"), bx, 414, 130, 30);
            _clearPointsBtn.Click += OnClearPoints;

            _toggleAllPointsBtn = UiFactory.Button(Utils.Localization.T("Enable / Disable all"), bx, 448, 130, 30);
            _toggleAllPointsBtn.Click += OnToggleAllPoints;

            var applyNote = UiFactory.Label(
                "Tip: press Save on the\nClicker tab to store these\npoints in the profile.",
                bx, 488, FontStyle.Italic, 8.25f);
            applyNote.ForeColor = _theme.TextMuted;

            page.Controls.Add(help);
            page.Controls.Add(orderLabel);
            page.Controls.Add(_pointOrderCombo);
            page.Controls.Add(_cycleInfoLabel);
            page.Controls.Add(_pointsList);

            _pointsEmptyHint = new Label
            {
                Text = Utils.Localization.T(
                    "No points yet.\r\nUse \u201CAdd\u2026\u201D or \u201CQuick Capture\u201D to create one,\r\nthen tick its row to enable it."),
                Left = _pointsList.Left + 1,
                Top = _pointsList.Top + 70,
                Width = _pointsList.Width - 2,
                Height = 90,
                TextAlign = ContentAlignment.TopCenter,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Italic),
                ForeColor = _theme.TextMuted,
                BackColor = _pointsList.BackColor,
                Visible = false
            };
            page.Controls.Add(_pointsEmptyHint);
            _pointsEmptyHint.BringToFront();
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
            page.Controls.Add(_toggleAllPointsBtn);
            page.Controls.Add(applyNote);

            ShiftBelowIntro(page, help, helpText, 720, firstRowY: 52);

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

            if (_pointsEmptyHint != null)
            {
                _pointsEmptyHint.BackColor = _pointsList.BackColor;
                _pointsEmptyHint.ForeColor = _theme.TextMuted;
                _pointsEmptyHint.Visible = _workingPoints.Count == 0;
                if (_pointsEmptyHint.Visible) _pointsEmptyHint.BringToFront();
            }

            UpdateCycleInfo();
            UpdatePointButtonStates();
        }

        /// <summary>
        /// Greys out the per-point action buttons when nothing is selected (and the
        /// Move buttons at the ends of the list), so the buttons reflect what's
        /// actually possible rather than always looking clickable.
        /// </summary>
        private void UpdatePointButtonStates()
        {
            int index = GetSelectedPointIndex();
            bool hasSelection = index >= 0 && index < _workingPoints.Count;

            if (_editPointBtn != null) _editPointBtn.Enabled = hasSelection;
            if (_duplicatePointBtn != null) _duplicatePointBtn.Enabled = hasSelection;
            if (_togglePointBtn != null) _togglePointBtn.Enabled = hasSelection;
            if (_removePointBtn != null) _removePointBtn.Enabled = hasSelection;
            if (_movePointUpBtn != null) _movePointUpBtn.Enabled = hasSelection && index > 0;
            if (_movePointDownBtn != null) _movePointDownBtn.Enabled = hasSelection && index < _workingPoints.Count - 1;
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
            long dwellPerCycle = 0;
            foreach (ClickPoint p in _workingPoints)
            {
                if (p.Enabled)
                {
                    enabled++;
                    clicksPerCycle += p.Repeat < 1 ? 1 : p.Repeat;
                    if (p.DwellMilliseconds > 0) dwellPerCycle += p.DwellMilliseconds;
                }
            }

            if (enabled == 0)
            {
                _cycleInfoLabel.Text = Utils.Localization.T("No active points");
            }
            else
            {
                // Built with F(), not interpolation. Every piece of this line used to be
                // an interpolated string assigned straight to .Text, so the one summary
                // that says what the sequence will actually do was English in all five
                // other languages.
                int total = _workingPoints.Count;
                string pointsText = enabled == total
                    ? Utils.Localization.F("{0} active point(s)", enabled)
                    : Utils.Localization.F("{0} of {1} points active", enabled, total);
                string text = pointsText + "  \u2022  "
                    + Utils.Localization.F("{0} click(s) per cycle", clicksPerCycle);

                // How long one full pass takes. The dwell was already counted here, but
                // the clicks were not \u2014 and the clicks are most of it. Without them the
                // line said "0 ms" for a sequence that plainly takes a second to run.
                double cycleMs = dwellPerCycle + clicksPerCycle * CurrentIntervalMs();
                if (cycleMs > 0)
                {
                    text += "  \u2022  " + Utils.Localization.F("about {0} per cycle",
                        FormatDuration(cycleMs));
                }
                _cycleInfoLabel.Text = text;
            }
        }

        /// <summary>
        /// The Clicker tab's current interval in milliseconds, as the engine would use it
        /// between clicks. Sub-millisecond rates from the manual slider are honoured, so
        /// the estimate does not read as zero at high CPS.
        /// </summary>
        private double CurrentIntervalMs()
        {
            try
            {
                if (_speedTrack != null && _speedTrack.Value > 1000)
                {
                    return 1000.0 / _speedTrack.Value;
                }
                double ms = (double)_hoursNum.Value * 3600000
                          + (double)_minutesNum.Value * 60000
                          + (double)_secondsNum.Value * 1000
                          + (double)_millisNum.Value;
                return ms;
            }
            catch { return 0; }
        }

        /// <summary>"820 ms", "2.4 s" or "1 m 05 s" \u2014 whichever reads best at that scale.</summary>
        private static string FormatDuration(double ms)
        {
            if (ms < 1000) { return ((int)Math.Round(ms)) + " ms"; }
            if (ms < 60000) { return (ms / 1000.0).ToString("0.0") + " s"; }
            int totalSec = (int)Math.Round(ms / 1000.0);
            return (totalSec / 60) + " m " + (totalSec % 60).ToString("00") + " s";
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
                            Label = "Point " + (_workingPoints.Count + 1),
                            // Enabled by default so a freshly captured sequence is ready to
                            // run immediately (matching points added via the editor), instead
                            // of silently not clicking until each row is ticked. Untick to skip.
                            Enabled = true
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
                        ? Utils.Localization.T("Captured 1 point")
                        : Utils.Localization.F("Captured {0} points", added);
                }

                if (wasVisible)
                {
                    Show();
                    EnsureOnScreen();
                    Activate();
                    ReassertTopMost();

                    // Same repair the coordinate picker needs: without it the window
                    // returns from Hide() with its backdrop composite unbuilt, and with a
                    // wallpaper every control is transparent — so the window comes back as
                    // a hole showing the desktop through it.
                    TryRepairLayoutNow();
                    InvalidateBackdropSurfaces();
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
            else if (e.Control && e.KeyCode == Keys.Up)
            {
                MovePoint(-1);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.Down)
            {
                MovePoint(1);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.Home)
            {
                MovePointToEnd(true);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.End)
            {
                MovePointToEnd(false);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.Alt && (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right
                            || e.KeyCode == Keys.Up || e.KeyCode == Keys.Down))
            {
                // Alt+arrows nudge the point itself. Getting a target onto the right
                // pixel used to mean opening the editor, changing a number, closing it,
                // and looking again — for every attempt. Shift makes it 10px at a time.
                //
                // Alt rather than a bare arrow because bare arrows move the SELECTION
                // through the list, which is what a list is expected to do.
                int step = e.Shift ? 10 : 1;
                int dx = e.KeyCode == Keys.Left ? -step : e.KeyCode == Keys.Right ? step : 0;
                int dy = e.KeyCode == Keys.Up ? -step : e.KeyCode == Keys.Down ? step : 0;
                NudgeSelectedPoint(dx, dy);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        /// <summary>
        /// Moves the selected point by (dx, dy) pixels and keeps the row selected, so a
        /// run of nudges lands on one point instead of walking off it.
        /// </summary>
        private void NudgeSelectedPoint(int dx, int dy)
        {
            int index = GetSelectedPointIndex();
            if (index < 0 || index >= _workingPoints.Count) { return; }

            ClickPoint p = _workingPoints[index];
            p.X += dx;
            p.Y += dy;
            RefreshPointsList();

            if (index < _pointsList.Items.Count)
            {
                _pointsList.Items[index].Selected = true;
                _pointsList.Items[index].EnsureVisible();
                _pointsList.Focus();
            }

            // Say where it landed, and say so plainly when that is off every monitor —
            // a point nudged off-screen still looks perfectly ordinary in the list.
            string where = Utils.Localization.F("Point moved to ({0}, {1})", p.X, p.Y);
            if (!AutoClicker.Utils.ScreenGeometry.IsOnScreen(p.X, p.Y))
            {
                where += "  •  " + Utils.Localization.T("off-screen");
            }
            _statusState.Text = where;
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

        /// <summary>Moves the selected point straight to the top or bottom of the list.</summary>
        private void MovePointToEnd(bool toTop)
        {
            int index = GetSelectedPointIndex();
            if (index < 0 || _workingPoints.Count < 2)
            {
                return;
            }

            int target = toTop ? 0 : _workingPoints.Count - 1;
            if (target == index)
            {
                return;
            }

            ClickPoint p = _workingPoints[index];
            _workingPoints.RemoveAt(index);
            _workingPoints.Insert(target, p);

            RefreshPointsList();
            if (target < _pointsList.Items.Count)
            {
                _pointsList.Items[target].Selected = true;
                _pointsList.Items[target].EnsureVisible();
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

        private void OnToggleAllPoints(object sender, EventArgs e)
        {
            if (_workingPoints.Count == 0)
            {
                return;
            }

            // If every point is already enabled, turn them all off; otherwise on.
            bool allEnabled = _workingPoints.TrueForAll(p => p.Enabled);
            bool newState = !allEnabled;
            foreach (ClickPoint p in _workingPoints)
            {
                p.Enabled = newState;
            }
            RefreshPointsList();
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
                // ShowInfo, not a bare MessageBox: it translates the message and uses
                // Tempo's own themed dialog, which this call was bypassing.
                ShowInfo(Utils.Localization.T("Add at least one enabled point first."));
                return;
            }

            // Snapshot the points so edits during display don't matter.
            var snapshot = new List<ClickPoint>();
            foreach (ClickPoint p in _workingPoints)
            {
                snapshot.Add(p.Clone());
            }

            // Pass the order too, so the overlay can draw the route rather than just the
            // dots — Reverse and Ping-Pong were otherwise indistinguishable on screen.
            MultiPointOrder order = _pointOrderCombo != null
                ? (MultiPointOrder)_pointOrderCombo.SelectedIndex
                : MultiPointOrder.Sequential;

            using (var overlay = new PointsOverlayForm(_theme, snapshot, order))
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

            _statusState.Text = Utils.Localization.F("Added point at ({0}, {1})", x, y);
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
            _statusState.Text = Utils.Localization.T(
                _settings.AntiFreezeEnabled ? "Anti-freeze on" : "Anti-freeze off");
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
