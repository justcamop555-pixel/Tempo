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

        // Save, on the tab where the points are actually edited. The tab used to say only "press
        // Save on the Clicker tab", and switching profile discards unsaved point edits silently —
        // the only warning lived on another tab.
        private Button _pointsSaveBtn;
        private Label _pointsSaveNote;

        // True while a row is being dragged: the live marker must not scroll the list under the drag.
        private bool _pointsDragging;

        // Picks every point (Ctrl+A does the same).
        private Button _selectAllPointsBtn;

        // The top row the list had after the live marker last scrolled it; -1 while no run has. A list scrolled
        // somewhere else since is being read, and is left there (UpdateMultiPointLive).
        private int _pointsFollowTop = -1;

        // The live marker is an index into the list the RUN started with. Once the working list is
        // reordered or edited mid-run that index names a different row, so the marker is withheld
        // until the run ends rather than pointing at the wrong point.
        private bool _pointsLiveStale;

        private void BuildMultiPointTab()
        {
            var page = new BackdropTabPage(Utils.Localization.T("Multi-Point")) { AutoScroll = true };
            page.Name = "multipoint";   // stable key for LastTabKey

            string helpText =
                "Define a sequence of points. In Multi-Point mode the engine visits the " +
                "enabled points using the chosen order. Tick a row to enable/disable it, and drag rows to reorder them. " +
                "Ctrl+A, Shift+click or Ctrl+click selects several at once: Delete removes them, Ctrl+D duplicates, " +
                "Ctrl+\u2191/\u2193 reorders, Ctrl+Home/End jumps to the ends, and Alt+arrows nudge a pixel at a time (hold Shift for 10).";
            var help = UiFactory.Label(helpText, 12, 12);
            help.MaximumSize = new Size(720, 0);
            help.AutoSize = true;
            help.ForeColor = _theme.TextMuted;

            // ── Order + cycle info row ─────────────────────────────────────────
            var orderLabel = UiFactory.Label(Utils.Localization.T("Order:"), 12, 56, FontStyle.Bold);
            _pointOrderCombo = UiFactory.Combo(66, 53, 150, Utils.Localization.T("Sequential"), Utils.Localization.T("Reverse"), Utils.Localization.T("Random"), Utils.Localization.T("Ping-Pong"));
            _pointOrderCombo.SelectedIndex = 0;
            // The summary beside it depends on the order — Ping-Pong's cycle includes the way back,
            // Random has no cycle at all — yet nothing recomputed it when the order changed, so it
            // kept describing whichever order was showing when the list last changed.
            _pointOrderCombo.SelectedIndexChanged += (s, e) => UpdateCycleInfo();

            _cycleInfoLabel = UiFactory.Label("", 232, 56, FontStyle.Italic, 9f);
            _cycleInfoLabel.AutoSize = false;
            // 520, not 360: this row is empty all the way to the button column at x≈568,
            // and at 360 the third clause ("about 600 ms per cycle") was already being
            // ellipsised away in English, never mind the longer translations.
            _cycleInfoLabel.Width = 520;
            _cycleInfoLabel.Height = 18;
            _cycleInfoLabel.AutoEllipsis = true;
            _cycleInfoLabel.ForeColor = _theme.TextMuted;

            _pointsList = new ClickPointListView
            {
                Left = 12,
                Top = 86,
                Width = 552,
                Height = 444,
                // Several points at once (Ctrl+A, Shift/Ctrl+click): every action below takes them all.
                MultiSelect = true,
                // And the picked rows are drawn picked. The list draws its own rows and drew a selection only
                // where asked to — the point Delete or Alt+arrows would act on looked like every other row.
                HighlightSelection = true,
            };
            // Column widths sum to 528, leaving room inside the 552-px list for its border
            // and a vertical scrollbar — so the last "Rep" column is never clipped once the
            // list has enough rows to scroll (it used to be cut off at the right edge).
            _pointsList.Columns.Add("#", 36);
            // Dwell 58, not 54 (Label gives up the 4): French "Attente" needed 46 px of the 42 its title
            // had, and was cut for as long as the list was on screen.
            _pointsList.Columns.Add(Utils.Localization.T("Label"), 126);
            _pointsList.Columns.Add("X", 56, HorizontalAlignment.Right);
            _pointsList.Columns.Add("Y", 56, HorizontalAlignment.Right);
            _pointsList.Columns.Add(Utils.Localization.T("Button"), 72);
            _pointsList.Columns.Add(Utils.Localization.T("Type"), 72);
            _pointsList.Columns.Add(Utils.Localization.T("Dwell"), 58, HorizontalAlignment.Right);
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

            // Reorder by dragging a row — the Move Up / Move Down buttons are gone. ListView raises
            // ItemDrag itself once the pointer passes the system drag threshold, so a plain click, or a
            // tick in a row's checkbox, never starts one.
            _pointsList.AllowDrop = true;
            _pointsList.ItemDrag += OnPointItemDrag;
            _pointsList.DragEnter += OnPointDragOver;
            _pointsList.DragOver += OnPointDragOver;
            _pointsList.DragLeave += (s, e) => _pointsList.DropLineIndex = -1;
            _pointsList.DragDrop += OnPointDragDrop;

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
            pointMenu.Items.Add(Utils.Localization.T("Select all"), null, (s, e) => SelectAllPoints());
            pointMenu.Items.Add(Utils.Localization.T("Remove"), null, OnRemovePoint);
            _pointsList.ContextMenuStrip = pointMenu;
            _pointsList.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    // A right-click on a row already picked keeps the whole pick, so the menu acts on all of it;
                    // on any other row it picks just that one, as a left click would.
                    var hit = _pointsList.HitTest(e.Location);
                    if (hit.Item != null && !hit.Item.Selected)
                    {
                        _pointsList.SelectedIndices.Clear();
                        hit.Item.Selected = true;
                        hit.Item.Focused = true;
                    }
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

            // With the actions that take a selection: every one of them takes all the points picked.
            _selectAllPointsBtn = UiFactory.Button(Utils.Localization.T("Select all"), bx, 290, 130, 30);
            _selectAllPointsBtn.Click += (s, e) => SelectAllPoints();

            // Move Up / Move Down are gone: rows are reordered by dragging them in the list (and
            // Ctrl+Up/Down, Ctrl+Home/End still work from the keyboard). The rest close the gap.
            _showPointsBtn = UiFactory.Button(Utils.Localization.T("Show on screen"), bx, 334, 130, 30);
            _showPointsBtn.Click += OnShowPointsOverlay;

            _clearPointsBtn = UiFactory.Button(Utils.Localization.T("Clear All"), bx, 368, 130, 30);
            _clearPointsBtn.Click += OnClearPoints;

            _toggleAllPointsBtn = UiFactory.Button(Utils.Localization.T("Enable / Disable all"), bx, 402, 130, 30);
            _toggleAllPointsBtn.Click += OnToggleAllPoints;

            // Save right here. This column used to end in a tip — "press Save on the Clicker tab to
            // store these points" — while switching profile throws unsaved point edits away, and the only
            // unsaved-changes warning was on that other tab. Always enabled, like the Clicker tab's own
            // Save; the note below carries the state.
            _pointsSaveBtn = UiFactory.Button(Utils.Localization.T("Save"), bx, 448, 130, 30);
            _pointsSaveBtn.Click += OnSaveProfile;
            StyleAccentButton(_pointsSaveBtn);
            SetGlyph(_pointsSaveBtn, ActionGlyph.Save);

            _pointsSaveNote = new Label
            {
                Text = Utils.Localization.T("● Unsaved changes — click Save"),
                AutoSize = false,
                AutoEllipsis = true,
                Location = new Point(bx, 484),
                // Three lines at this width: the longer translations need them.
                Size = new Size(130, 48),
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = _theme.AccentText,
                Visible = false
            };

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
            LayoutFitter.DeclareOverlay(_pointsEmptyHint, "drawn on top of the points list while it is empty");

            // The two things that stop this page's sequence working, given room to say
            // themselves. They started life spliced onto the cycle summary at the top,
            // which is 360px of AutoEllipsis label — so the sentence was cut mid-word at
            // "these points are…" and the half carrying the counts never appeared at all.
            // Below the list (which ends at y=530) there is nothing but empty page.
            _pointsWarnLabel = new Label
            {
                AutoSize = false,
                Location = new Point(12, 538),
                // 64, not 40: two warnings at once need four lines at this width, and the
                // page below the list is otherwise empty. At 40 the off-screen sentence
                // lost the half that explains WHY ("a monitor may have been unplugged").
                Size = new Size(552, 64),
                Font = new Font("Segoe UI", 8.75f),
                ForeColor = _theme.WarningText,
                Visible = false
            };
            page.Controls.Add(_pointsWarnLabel);
            page.Controls.Add(_addPointBtn);
            page.Controls.Add(_capturePointBtn);
            page.Controls.Add(_editPointBtn);
            page.Controls.Add(_duplicatePointBtn);
            page.Controls.Add(_togglePointBtn);
            page.Controls.Add(_removePointBtn);
            page.Controls.Add(_selectAllPointsBtn);
            page.Controls.Add(_showPointsBtn);
            page.Controls.Add(_clearPointsBtn);
            page.Controls.Add(_toggleAllPointsBtn);
            page.Controls.Add(_pointsSaveBtn);
            page.Controls.Add(_pointsSaveNote);

            ShiftBelowIntro(page, help, 720, firstRowY: 52);

            _tabs.TabPages.Add(page);
        }

        private void RefreshPointsList()
        {
            if (_pointsList == null)
            {
                return;
            }

            // Edited while a run is going: the run keeps using the list it started with, so its point
            // numbers no longer line up with these rows. Withhold the live marker until the run ends
            // rather than let it point at the wrong row. (Every caller is an edit or a profile load —
            // nothing refreshes this list just because a run started.)
            if (_engine != null && _engine.IsRunning)
            {
                _pointsLiveStale = true;
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
                // In the reader's language: these were the enum's own names — "Left", "Single" — in every
                // language. The button rides along for the row's colour, which used to be read off the English.
                var buttonCell = item.SubItems.Add(Utils.Localization.T(p.Button.ToString()));
                buttonCell.Tag = p.Button;
                item.SubItems.Add(Utils.Localization.T(p.Style.ToString()));
                item.SubItems.Add(p.DwellMilliseconds.ToString());
                item.SubItems.Add((p.Repeat < 1 ? 1 : p.Repeat).ToString());
                item.Tag = i;
                item.Checked = p.Enabled;

                // A point that is not on any monitor cannot be clicked. Windows does not
                // fail the move, it CLAMPS it — measured: asking for (3000, 400) lands the
                // cursor at (1919, 400), and (-5000, 400) at (0, 400) — so the click still
                // happens, just somewhere the user never chose. Tempo has warned about this
                // at start time for a while, in the log and a toast; the list where the
                // coordinates actually live said nothing, so there was no way to tell WHICH
                // row was the bad one. (Typically a monitor unplugged since the profile was
                // saved.)
                bool offScreen = !Utils.ScreenGeometry.IsOnScreen(p.X, p.Y);
                if (offScreen)
                {
                    // The marker goes on the LABEL column, not X. X is 56px and right
                    // aligned, so "3000 ⚠" ellipsised to "3000 …" — a marker you cannot
                    // see is worse than none, because the row still looks ordinary.
                    item.SubItems[1].Text = "⚠ " + p.Label;
                    item.ToolTipText = TipStyle.Wrap(Utils.Localization.T(
                        "This point is not on any monitor. The click will be clamped to the "
                        + "screen edge and land somewhere you didn't choose."));
                }

                if (!p.Enabled)
                {
                    item.ForeColor = _theme.TextMuted;
                }
                else if (offScreen)
                {
                    item.ForeColor = _theme.DangerText;
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
        /// Greys out the per-point action buttons when nothing is selected, so the buttons
        /// reflect what's actually possible rather than always looking clickable.
        /// </summary>
        private void UpdatePointButtonStates()
        {
            int picked = _pointsList.SelectedIndices.Count;
            bool hasSelection = picked > 0;

            if (_editPointBtn != null) _editPointBtn.Enabled = hasSelection;
            if (_duplicatePointBtn != null) _duplicatePointBtn.Enabled = hasSelection;
            if (_togglePointBtn != null) _togglePointBtn.Enabled = hasSelection;
            if (_removePointBtn != null) _removePointBtn.Enabled = hasSelection;
            if (_selectAllPointsBtn != null) _selectAllPointsBtn.Enabled = _workingPoints.Count > 0 && picked < _workingPoints.Count;
        }

        /// <summary>The picked points' places in the working list, in list order.</summary>
        private List<int> SelectedPointIndices()
        {
            var picked = new List<int>();
            foreach (int row in _pointsList.SelectedIndices)
            {
                if (row < 0 || row >= _pointsList.Items.Count) { continue; }
                ListViewItem item = _pointsList.Items[row];
                int index = item.Tag is int tag ? tag : row;
                if (index >= 0 && index < _workingPoints.Count) { picked.Add(index); }
            }
            picked.Sort();
            return picked;
        }

        /// <summary>Picks these rows again after a refresh (which drops every pick), with <paramref name="focus"/> in view.</summary>
        private void SelectPoints(IEnumerable<int> indices, int focus)
        {
            _pointsList.SelectedIndices.Clear();
            foreach (int i in indices)
            {
                if (i >= 0 && i < _pointsList.Items.Count) { _pointsList.Items[i].Selected = true; }
            }
            if (focus >= 0 && focus < _pointsList.Items.Count)
            {
                _pointsList.Items[focus].Focused = true;
                _pointsList.Items[focus].EnsureVisible();
            }
        }

        /// <summary>"Select all" and Ctrl+A: every point picked, ready for one action on them all.</summary>
        private void SelectAllPoints()
        {
            if (_pointsList == null || _pointsList.Items.Count == 0) { return; }
            _pointsList.BeginUpdate();
            try
            {
                foreach (ListViewItem item in _pointsList.Items) { item.Selected = true; }
            }
            finally { _pointsList.EndUpdate(); }
            _pointsList.Focus();
            UpdatePointButtonStates();
        }

        /// <summary>Shows how many points are active and the clicks per full cycle.</summary>
        private void UpdateCycleInfo()
        {
            if (_cycleInfoLabel == null)
            {
                return;
            }

            int enabled = 0;
            int offScreen = 0;
            foreach (ClickPoint p in _workingPoints)
            {
                if (p.Enabled)
                {
                    enabled++;
                    if (!Utils.ScreenGeometry.IsOnScreen(p.X, p.Y)) { offScreen++; }
                }
            }

            // The order decides what a "cycle" even is: ping-pong bounces back through the interior
            // points (2n-2 visits), and random has no cycle at all. The old line reported n visits
            // and one click each for every order, so a ping-pong list understated its own pass and
            // three double-click points read "3 clicks" while Statistics recorded 6.
            MultiPointOrder order = _pointOrderCombo != null && _pointOrderCombo.SelectedIndex >= 0
                ? (MultiPointOrder)_pointOrderCombo.SelectedIndex
                : MultiPointOrder.Sequential;
            int holdMs = _holdMsNum != null ? (int)_holdMsNum.Value : 0;
            MultiPointCycle.Estimate est = MultiPointCycle.Measure(
                _workingPoints, order, CurrentIntervalMs(), EffectiveCap(), holdMs);

            // This whole page describes a sequence that only runs in Multi-point mode, and
            // it said so nowhere. With the Clicker tab on "Current cursor position" the
            // line still read "5 active points · 5 clicks per cycle · about 100 ms per
            // cycle" — a confident description of something that will not happen. Same
            // shape as the background-click checkbox that was live in a mode that ignores
            // it; this is the other half of that page's silence.
            bool mpSelected = _posMultiRadio != null && _posMultiRadio.Checked;
            if (_pointsWarnLabel != null)
            {
                var lines = new List<string>();
                if (!mpSelected && _workingPoints.Count > 0)
                {
                    lines.Add(Utils.Localization.T(
                        "⚠ Multi-point isn't selected on the Clicker tab, so these points aren't used."));
                }
                if (offScreen > 0)
                {
                    lines.Add(offScreen == 1
                        ? Utils.Localization.T(
                            "⚠ 1 point is not on any monitor — its click gets clamped to the screen edge. "
                            + "A monitor may have been unplugged since this profile was saved.")
                        : Utils.Localization.F(
                            "⚠ {0} points are not on any monitor — their clicks get clamped to the screen "
                            + "edge. A monitor may have been unplugged since this profile was saved.",
                            offScreen));
                }
                _pointsWarnLabel.Text = string.Join("\r\n", lines.ToArray());
                _pointsWarnLabel.Visible = lines.Count > 0;
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

                // Random never repeats a pattern, so "per cycle" would be a promise it cannot keep;
                // it gets "per pass over the points" instead. Ping-Pong's cycle includes the way back.
                bool fixedCycle = MultiPointCycle.HasFixedCycle(order);
                string text = pointsText + "  \u2022  " + (fixedCycle
                    ? Utils.Localization.F("{0} click(s) per cycle", est.Clicks)
                    : Utils.Localization.F("{0} click(s) per pass", est.Clicks));

                // How long one pass takes: every visit's dwell (or the interval the engine will
                // really use \u2014 Anti-Freeze floors it) plus the time each click is held down.
                if (est.Milliseconds > 0)
                {
                    text += "  \u2022  " + (fixedCycle
                        ? Utils.Localization.F("about {0} per cycle", FormatDuration(est.Milliseconds))
                        : Utils.Localization.F("about {0} per pass", FormatDuration(est.Milliseconds)));
                }
                if (order == MultiPointOrder.PingPong && enabled > 2)
                {
                    text += "  \u2022  " + Utils.Localization.F("{0} visits, bouncing back", est.Visits);
                }
                if (est.Capped)
                {
                    text += "  \u2022  " + Utils.Localization.T("held back by the Max CPS cap");
                }
                // The off-screen tally used to be appended here as well. It is said in full
                // \u2014 with the reason \u2014 by the warning label under the list, and repeating it
                // on a single ellipsised line only pushed the cycle time off the end.
                _cycleInfoLabel.Text = text;
            }

            // The counts line only turns amber for the off-screen tally it now carries;
            // the mode warning has its own label below the list.
            _cycleInfoLabel.ForeColor = offScreen > 0 ? _theme.WarningText : _theme.TextMuted;
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

        /// <summary>The one point Edit opens: the focused row of the pick, else its first.</summary>
        private int GetSelectedPointIndex()
        {
            if (_pointsList.SelectedItems.Count == 0)
            {
                return -1;
            }

            ListViewItem focused = _pointsList.FocusedItem;
            ListViewItem item = focused != null && focused.Selected ? focused : _pointsList.SelectedItems[0];
            return item.Tag is int tag ? tag : item.Index;
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
            // Every Tempo window that is up steps aside — the main one, and the tabs in windows of their
            // own (the Multi-Point tab itself, when it is in one).
            bool wasVisible = Visible && WindowState != FormWindowState.Minimized;
            if (wasVisible) { Hide(); }
            List<Form> hiddenWindows = HidePageWindowsForPick();
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
                            // In the reader's language ("Point 3" was English in every one).
                            Label = Utils.Localization.F("Point {0}", _workingPoints.Count + 1),
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

                ShowPageWindowsAfterPick(hiddenWindows);
                if (wasVisible)
                {
                    // Same repair the coordinate picker needs: without it the window
                    // returns from Hide() with its backdrop composite unbuilt, and with a
                    // wallpaper every control is transparent — so the window comes back as
                    // a hole showing the desktop through it. Cloaked until it is whole.
                    ShowWholeAfterHide(() =>
                    {
                        Show();
                        EnsureOnScreen();
                        Activate();
                        ReassertTopMost();
                    });
                }
                // The Multi-Point tab in a window of its own ends in front: the points went there.
                TabWindow pointsWindow = OpenTabWindowOf(TabByKey("multipoint"));
                if (pointsWindow != null && hiddenWindows.Contains(pointsWindow.Window)) { RaiseTabWindow(pointsWindow); }
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
            else if (e.Control && e.KeyCode == Keys.A)
            {
                SelectAllPoints();
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
            List<int> picked = SelectedPointIndices();
            if (picked.Count == 0) { return; }
            int focus = GetSelectedPointIndex();

            foreach (int i in picked)
            {
                _workingPoints[i].X += dx;
                _workingPoints[i].Y += dy;
            }
            RefreshPointsList();
            SelectPoints(picked, focus);
            _pointsList.Focus();

            // Say where it landed, and say so plainly when that is off every monitor —
            // a point nudged off-screen still looks perfectly ordinary in the list.
            ClickPoint p = _workingPoints[picked.Contains(focus) ? focus : picked[0]];
            string where = picked.Count == 1
                ? Utils.Localization.F("Point moved to ({0}, {1})", p.X, p.Y)
                : Utils.Localization.F("{0} points moved by ({1}, {2})", picked.Count, dx, dy);
            if (picked.Exists(i => !AutoClicker.Utils.ScreenGeometry.IsOnScreen(_workingPoints[i].X, _workingPoints[i].Y)))
            {
                where += "  •  " + Utils.Localization.T("off-screen");
            }
            _statusState.Text = where;
        }

        private void OnRemovePoint(object sender, EventArgs e)
        {
            List<int> picked = SelectedPointIndices();
            if (picked.Count == 0)
            {
                return;
            }

            // From the end, so the places before each one hold still.
            for (int k = picked.Count - 1; k >= 0; k--)
            {
                _workingPoints.RemoveAt(picked[k]);
            }
            RefreshPointsList();

            // Keep a row selected: the one that moved up into the first slot, or the new last row. The refresh
            // rebuilds every item and drops the selection, so pressing Delete a second time used to do
            // nothing at all — even though the help text says Delete "removes the selected point".
            if (_pointsList.Items.Count > 0)
            {
                int next = Math.Min(picked[0], _pointsList.Items.Count - 1);
                SelectPoints(new[] { next }, next);
            }
        }

        // ── Drag to reorder ─────────────────────────────────────────────────────────
        // What rides the drag: the rows' places in the working list — the whole pick when the dragged row is
        // in it. A private type, so nothing dragged in from outside Tempo can ever be mistaken for these rows.
        private sealed class PointDragData
        {
            public List<int> Indices;
        }

        private void OnPointItemDrag(object sender, ItemDragEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _workingPoints.Count < 2) { return; }
            if (!(e.Item is ListViewItem item)) { return; }

            int from = item.Tag is int tag ? tag : item.Index;
            if (from < 0 || from >= _workingPoints.Count) { return; }

            // The pick moves as one when the row taken is part of it; a row outside it goes alone.
            List<int> moving = SelectedPointIndices();
            if (!moving.Contains(from)) { moving = new List<int> { from }; }
            if (moving.Count >= _workingPoints.Count) { return; }   // every row: nowhere else to put them

            _pointsDragging = true;
            try
            {
                _pointsList.DoDragDrop(new PointDragData { Indices = moving }, DragDropEffects.Move);
            }
            finally
            {
                // A drag cancelled with Esc, or released outside the list, never raises DragDrop.
                _pointsDragging = false;
                _pointsList.DropLineIndex = -1;
            }
        }

        private void OnPointDragOver(object sender, DragEventArgs e)
        {
            if (e.Data == null || !e.Data.GetDataPresent(typeof(PointDragData)))
            {
                e.Effect = DragDropEffects.None;   // something from outside Tempo — not ours to take
                return;
            }
            e.Effect = DragDropEffects.Move;

            Point local = _pointsList.PointToClient(new Point(e.X, e.Y));

            // Near the top or bottom edge, scroll one row, so a point can be dragged to a place that is
            // not on screen yet. Scroll FIRST and place the line after: the row rectangles the line is
            // measured against only hold once the scroll has happened.
            ListViewItem top = _pointsList.TopItem;
            if (top != null && top.Index > 0 && local.Y < 44)
            {
                _pointsList.Items[top.Index - 1].EnsureVisible();
            }
            else if (local.Y > _pointsList.ClientSize.Height - 20)
            {
                ListViewItem lastShown = _pointsList.GetItemAt(4, _pointsList.ClientSize.Height - 6);
                int next = lastShown != null ? lastShown.Index + 1 : -1;
                if (next > 0 && next < _pointsList.Items.Count)
                {
                    _pointsList.Items[next].EnsureVisible();
                }
            }

            _pointsList.DropLineIndex = _pointsList.DropIndexAt(local);
        }

        private void OnPointDragDrop(object sender, DragEventArgs e)
        {
            int dropAt = _pointsList.DropLineIndex;
            _pointsList.DropLineIndex = -1;

            var data = e.Data?.GetData(typeof(PointDragData)) as PointDragData;
            if (data?.Indices == null || dropAt < 0) { return; }

            var moving = new List<int>();
            foreach (int i in data.Indices)
            {
                if (i >= 0 && i < _workingPoints.Count && !moving.Contains(i)) { moving.Add(i); }
            }
            if (moving.Count == 0) { return; }
            moving.Sort();

            // dropAt is a gap in the list as it stands, counted BEFORE the dragged rows are taken out;
            // taking them out first moves every later gap up by however many of them sat above it.
            int to = dropAt - moving.FindAll(i => i < dropAt).Count;

            var block = moving.ConvertAll(i => _workingPoints[i]);
            for (int k = moving.Count - 1; k >= 0; k--)
            {
                _workingPoints.RemoveAt(moving[k]);
            }
            to = Math.Max(0, Math.Min(_workingPoints.Count, to));
            _workingPoints.InsertRange(to, block);

            RefreshPointsList();
            var landed = new List<int>();
            for (int k = 0; k < block.Count; k++) { landed.Add(to + k); }
            SelectPoints(landed, to);
        }

        /// <summary>
        /// Shows or hides this tab's unsaved-changes note. Called from UpdateProfileDirty on the UI tick,
        /// so it follows exactly the same snapshot comparison as the Clicker tab's own warning.
        /// </summary>
        private void UpdatePointsSaveState(bool dirty)
        {
            if (_pointsSaveNote == null) { return; }
            if (_pointsSaveNote.Visible != dirty) { _pointsSaveNote.Visible = dirty; }
            // ThemeManager resets label colours on a theme switch; keep this one in the accent.
            if (dirty && _theme != null && _pointsSaveNote.ForeColor != _theme.AccentText)
            {
                _pointsSaveNote.ForeColor = _theme.AccentText;
            }
        }

        /// <summary>Ctrl+↑/↓: the picked points a row up or down, as one block; never past either end.</summary>
        private void MovePoint(int direction)
        {
            List<int> picked = SelectedPointIndices();
            if (picked.Count == 0)
            {
                return;
            }
            if (direction < 0 ? picked[0] == 0 : picked[picked.Count - 1] == _workingPoints.Count - 1)
            {
                return;
            }

            // Up from the top of the pick, down from its bottom, so each swap moves a picked row past an unpicked one.
            var moved = new List<int>();
            for (int k = 0; k < picked.Count; k++)
            {
                int i = direction < 0 ? picked[k] : picked[picked.Count - 1 - k];
                int target = i + direction;
                ClickPoint temp = _workingPoints[i];
                _workingPoints[i] = _workingPoints[target];
                _workingPoints[target] = temp;
                moved.Add(target);
            }

            RefreshPointsList();
            moved.Sort();
            SelectPoints(moved, direction < 0 ? moved[0] : moved[moved.Count - 1]);
        }

        /// <summary>Moves the picked points straight to the top or bottom of the list, keeping their order.</summary>
        private void MovePointToEnd(bool toTop)
        {
            List<int> picked = SelectedPointIndices();
            if (picked.Count == 0 || _workingPoints.Count < 2)
            {
                return;
            }

            var block = picked.ConvertAll(i => _workingPoints[i]);
            for (int k = picked.Count - 1; k >= 0; k--)
            {
                _workingPoints.RemoveAt(picked[k]);
            }
            int at = toTop ? 0 : _workingPoints.Count;
            _workingPoints.InsertRange(at, block);

            RefreshPointsList();
            var landed = new List<int>();
            for (int k = 0; k < block.Count; k++) { landed.Add(at + k); }
            SelectPoints(landed, toTop ? landed[0] : landed[landed.Count - 1]);
        }

        private void OnClearPoints(object sender, EventArgs e)
        {
            if (_workingPoints.Count == 0)
            {
                return;
            }

            var confirm = MessageBox.Show(this,
                Utils.Localization.T("Remove all points?"),
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
            List<int> picked = SelectedPointIndices();
            if (picked.Count == 0)
            {
                return;
            }

            // Each copy right after its point; from the end, so the places before it hold still. The copies
            // land at picked[k] + k + 1, and those are the rows picked afterwards.
            for (int k = picked.Count - 1; k >= 0; k--)
            {
                int i = picked[k];
                ClickPoint copy = _workingPoints[i].Clone();
                // In the reader's language: " copy" was added in English whatever the language.
                copy.Label = Utils.Localization.F("{0} copy", copy.Label);
                _workingPoints.Insert(i + 1, copy);
            }
            RefreshPointsList();
            var copies = new List<int>();
            for (int k = 0; k < picked.Count; k++) { copies.Add(picked[k] + k + 1); }
            SelectPoints(copies, copies[copies.Count - 1]);
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
            List<int> picked = SelectedPointIndices();
            if (picked.Count == 0)
            {
                return;
            }

            // One state for the whole pick: on when any of it is off, otherwise off — never a mix flipped both ways.
            bool turnOn = picked.Exists(i => !_workingPoints[i].Enabled);
            foreach (int i in picked)
            {
                _workingPoints[i].Enabled = turnOn;
            }
            int focus = GetSelectedPointIndex();
            RefreshPointsList();
            SelectPoints(picked, focus);
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
                Label = Utils.Localization.F("Point {0}", _workingPoints.Count + 1)
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

            // The checkbox's event is suppressed above, so the lines that quote the cap would
            // otherwise keep saying "max 200" with protection off (or nothing, with it on).
            UpdateIntervalHint();
            RepaintSpeedLabel();

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
                // A finished run leaves no marker, and the next run starts with a trustworthy one.
                _pointsLiveStale = false;
                _pointsList.LiveIndex = -1;
                _pointsFollowTop = -1;
                return;
            }

            int idx = _engine.CurrentPointIndex;
            if (_pointsLiveStale || idx < 0 || idx >= _pointsList.Items.Count)
            {
                _pointsList.LiveIndex = -1;
                return;
            }

            if (_pointsList.LiveIndex == idx)
            {
                return;
            }

            // A MARKER, not the selection. This used to clear the selection and select the live row on
            // every tick, so during a run the "selected point" that Delete, Duplicate, Toggle and the
            // Alt+arrow nudge act on was whichever point the engine had just reached rather than the row
            // the user picked — Delete could remove a different point from the one they clicked.
            //
            // Scroll first, mark after (ThemedListView's rule: a rectangle invalidated before a scroll is
            // stale after it), and never scroll while a row is being dragged, or the list would move out
            // from under the pointer.
            //
            // Nor while the list has been scrolled away from the marker since it last followed: it used to be pulled
            // back to the point being clicked many times a second, so nothing else could be read during a run. It
            // follows again once the marker comes back into view.
            bool readingElsewhere = _pointsFollowTop >= 0 && _pointsList.TopRowIndex != _pointsFollowTop
                                    && !_pointsList.RowInView(idx);
            if (!_pointsDragging && !readingElsewhere)
            {
                _pointsList.Items[idx].EnsureVisible();
                _pointsFollowTop = _pointsList.TopRowIndex;
            }
            _pointsList.LiveIndex = idx;
        }
    }
}
