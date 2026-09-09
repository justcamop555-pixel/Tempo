using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Persistence;

namespace AutoClicker.UI
{
    public partial class MainForm
    {
        private readonly Dictionary<HotkeyAction, HotkeyCaptureControl> _bindingControls
            = new Dictionary<HotkeyAction, HotkeyCaptureControl>();

        private Label _keybindsDirtyLabel;
        private Label _keybindsWarnLabel;
        private Label _keyboardInfoLabel;
        private Label _keybindsTraySleepLabel;

        /// <summary>
        /// Shows or hides the "these hotkeys pause while Tempo sleeps in the tray"
        /// notice. Called when the page is built and whenever settings are saved, so
        /// turning "Sleep in tray" on or off updates it without a restart.
        /// </summary>
        private void RefreshTraySleepNotice()
        {
            if (_keybindsTraySleepLabel == null || _keybindsTraySleepLabel.IsDisposed) { return; }
            bool sleeping = _settings != null && _settings.TraySleepEnabled;
            _keybindsTraySleepLabel.Visible = sleeping;
            if (sleeping)
            {
                // Kept to two lines on purpose: the band is 38 px, and the half that
                // tells you what to DO must not be the half that gets clipped.
                _keybindsTraySleepLabel.Text = Utils.Localization.T(
                    "⏾  “Sleep in tray” is on: while Tempo sits hidden with nothing running, hotkeys that "
                    + "START clicking, playback or recording are paused so a forgotten Tempo can't run by "
                    + "itself. Emergency stop and Show / hide window keep working. Turn it off in "
                    + "Settings → Startup & Window.");
            }
        }
        private Label _keybindsRouteLabel;

        /// <summary>Says which action just lost its key to the combination you assigned.</summary>
        private Label _keybindsTakenLabel;

        /// <summary>Warns when the hold trigger is the button the clicker itself clicks.</summary>
        private Label _keybindsHoldLabel;

        /// <summary>Warns when a key Tempo PRESSES is also bound as one of its hotkeys.</summary>
        private Label _keybindsSelfKeyLabel;
        private Button _keybindsSaveBtn;
        private bool _suppressKeybindEvents;
        private NumericUpDown _intervalStepNum;

        // Rows currently flashing because their hotkey just fired, and when the flash
        // should end. Answering "is my hotkey even reaching Tempo?" used to mean
        // guessing; now the row lights up the instant the key is pressed, from any app.
        private readonly Dictionary<HotkeyAction, long> _keybindFlashUntil =
            new Dictionary<HotkeyAction, long>();
        private System.Windows.Forms.Timer _keybindFlashTimer;

        /// <summary>
        /// Flashes a binding's row green — called whenever that hotkey actually fires,
        /// wherever the user pressed it. This is the difference between "I think F6 is
        /// bound" and "F6 is reaching Tempo right now".
        /// </summary>
        private void FlashKeybind(HotkeyAction action)
        {
            if (_bindingControls == null || !_bindingControls.ContainsKey(action))
            {
                return;
            }
            UiInvoke(() =>
            {
                _keybindFlashUntil[action] = Environment.TickCount64 + 450;
                if (_keybindFlashTimer == null)
                {
                    _keybindFlashTimer = new System.Windows.Forms.Timer { Interval = 90 };
                    _keybindFlashTimer.Tick += (s, e) => TickKeybindFlash();
                }
                if (!_keybindFlashTimer.Enabled)
                {
                    _keybindFlashTimer.Start();
                }
                PaintKeybindFlash(action, true);
            });
        }

        private void TickKeybindFlash()
        {
            long now = Environment.TickCount64;
            var done = new List<HotkeyAction>();
            foreach (var pair in _keybindFlashUntil)
            {
                if (now >= pair.Value)
                {
                    done.Add(pair.Key);
                }
            }
            foreach (HotkeyAction a in done)
            {
                _keybindFlashUntil.Remove(a);
                PaintKeybindFlash(a, false);
            }
            if (_keybindFlashUntil.Count == 0)
            {
                _keybindFlashTimer?.Stop();
                // Restore whatever colour the conflict/risk rules say the row should be.
                HighlightConflicts();
            }
        }

        private void PaintKeybindFlash(HotkeyAction action, bool on)
        {
            if (!_bindingControls.TryGetValue(action, out HotkeyCaptureControl ctl) ||
                ctl == null || ctl.IsDisposed)
            {
                return;
            }
            ctl.BackColor = on
                ? BlendColors(_theme.InputBackground, _theme.Success, 0.55)
                : _theme.InputBackground;
        }

        /// <summary>
        /// Reports any hotkey that Windows would not register, so Tempo is driving it
        /// from a keyboard hook instead. Called after the bindings are (re)applied.
        /// </summary>
        private void RefreshKeybindRoutes()
        {
            if (_keybindsRouteLabel == null || _hotkeys == null || _settings == null)
            {
                return;
            }

            var fallbacks = new List<string>();
            foreach (var binding in _settings.Bindings)
            {
                if (binding?.Hotkey == null || !binding.Hotkey.IsValid || binding.Hotkey.IsMouse)
                {
                    continue;
                }
                // A combination Tempo itself already holds is not "another program" —
                // it used to be reported as one, which sent people hunting for a
                // conflicting app that did not exist. The clash is listed by the
                // conflict warning instead, which now names both actions.
                if (_hotkeys.SelfCollisionOwner(binding.Action.ToString()) != null)
                {
                    continue;
                }

                if (_hotkeys.RouteOf(binding.Action.ToString()) ==
                    Native.GlobalHotkeyManager.BindRoute.HookFallback)
                {
                    fallbacks.Add(binding.Hotkey.ToDisplayString() +
                                  " (" + HotkeyActions.LabelFor(binding.Action) + ")");
                }
            }

            // Bindings that got NO route at all. Taken from the apply pass rather than
            // worked out here: only what that pass actually TRIED can be judged, and its
            // skips (hold mode, tray sleep, an action this build does not have) are all
            // deliberate — re-deriving them here would report a sleeping hotkey as dead.
            // This is the worst of the three outcomes and was the only one the tab never
            // mentioned: the key silently does nothing, and the sole record was a line in
            // the diagnostics list.
            var dead = new List<string>(_hotkeyDead);

            if (fallbacks.Count == 0 && dead.Count == 0)
            {
                _keybindsRouteLabel.Visible = false;
                return;
            }

            fallbacks.Sort(StringComparer.OrdinalIgnoreCase);
            dead.Sort(StringComparer.OrdinalIgnoreCase);
            var parts = new List<string>();

            if (fallbacks.Count > 0)
            {
                // Singular and plural as two whole sentences. Splicing "it"/"them" into one
                // frame cannot work in languages where the pronoun agrees with the noun.
                string names = string.Join(", ", fallbacks.ToArray());
                parts.Add(fallbacks.Count == 1
                    ? Utils.Localization.F(
                        "⚠ Windows wouldn't reserve {0} — another program already owns it. Tempo still "
                        + "catches it with a keyboard hook, so the action works — but the key ALSO keeps "
                        + "doing its normal job in the other app. Pick a different combination to avoid that.",
                        names)
                    : Utils.Localization.F(
                        "⚠ Windows wouldn't reserve {0} — another program already owns them. Tempo still "
                        + "catches them with a keyboard hook, so the actions work — but the keys ALSO keep "
                        + "doing their normal job in the other app. Pick different combinations to avoid that.",
                        names));
            }

            if (dead.Count > 0)
            {
                string names = string.Join(", ", dead.ToArray());
                parts.Add(dead.Count == 1
                    ? Utils.Localization.F(
                        "⛔ {0} could not be bound at all — Windows refused it and the fallback hook "
                        + "would not install, so that key does nothing. Pick another combination.",
                        names)
                    : Utils.Localization.F(
                        "⛔ {0} could not be bound at all — Windows refused them and the fallback hook "
                        + "would not install, so those keys do nothing. Pick other combinations.",
                        names));
            }

            _keybindsRouteLabel.Text = string.Join("\r\n", parts.ToArray());
            _keybindsRouteLabel.Visible = true;
        }

        /// <summary>
        /// Fills in the detected keyboard line. Enumerating raw-input devices and reading
        /// the registry costs a few ms, so it runs off the UI thread — the Keybinds tab
        /// must not stall while it's built.
        /// </summary>
        private void RefreshKeyboardInfo()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                string summary;
                try { summary = Utils.KeyboardInfo.Summary(); }
                catch (Exception ex) { summary = "keyboard info unavailable (" + ex.Message + ")"; }

                Utils.Logger.Info("[Keyboard] " + summary);
                UiInvoke(() =>
                {
                    if (_keyboardInfoLabel != null && !_keyboardInfoLabel.IsDisposed)
                    {
                        _keyboardInfoLabel.Text = "⌨  " + summary;
                    }
                });
            });
        }

        /// <summary>The quick-play combos, keyed by slot number (1-3).</summary>
        private readonly Dictionary<int, ComboBox> _macroSlotCombos = new Dictionary<int, ComboBox>();

        /// <summary>Guards programmatic repopulation of those combos.</summary>
        private bool _suppressMacroSlotEvents;

        /// <summary>Which quick-play slot an action drives, or 0 for everything else.</summary>
        private static int SlotForAction(HotkeyAction action)
        {
            switch (action)
            {
                case HotkeyAction.PlayMacro1: return 1;
                case HotkeyAction.PlayMacro2: return 2;
                case HotkeyAction.PlayMacro3: return 3;
                default: return 0;
            }
        }

        /// <summary>
        /// Fills the quick-play pickers from the macro library and selects whatever
        /// each slot is assigned to. Called whenever the library changes, so a rename
        /// or a delete shows up here immediately.
        /// </summary>
        private void RefreshMacroSlotCombos()
        {
            if (_macroSlotCombos.Count == 0 || _macros == null) { return; }

            _suppressMacroSlotEvents = true;
            try
            {
                foreach (var pair in _macroSlotCombos)
                {
                    var combo = pair.Value;
                    if (combo == null || combo.IsDisposed) { continue; }

                    combo.Items.Clear();
                    combo.Items.Add(Utils.Localization.T("(first in the list)"));
                    foreach (var m in _macros.Macros)
                    {
                        if (m != null) { combo.Items.Add(m.Name); }
                    }

                    string assigned = MacroSlotName(pair.Key);
                    int idx = string.IsNullOrEmpty(assigned) ? 0 : combo.Items.IndexOf(assigned);

                    // Assigned to something that is gone. Keep the name visible rather
                    // than silently snapping back to "first in the list" — the hotkey
                    // does nothing right now and the user needs to see why.
                    if (idx < 0)
                    {
                        combo.Items.Add(Utils.Localization.F("{0}  (missing)", assigned));
                        idx = combo.Items.Count - 1;
                    }

                    combo.SelectedIndex = idx;
                }
            }
            finally
            {
                _suppressMacroSlotEvents = false;
            }
        }

        private void OnMacroSlotPicked(object sender, EventArgs e)
        {
            if (_suppressMacroSlotEvents) { return; }

            var combo = sender as ComboBox;
            if (combo == null || !(combo.Tag is int slot)) { return; }

            // Index 0 is "(first in the list)" — i.e. clear the assignment and fall
            // back to the old positional behaviour.
            string name = combo.SelectedIndex <= 0 ? "" : combo.SelectedItem as string;
            SetMacroSlotName(slot, name);
            try { Persistence.SettingsManager.Save(_settings); } catch { }

            Utils.Logger.Info("[Macros] quick-play slot " + slot + " -> " +
                              (string.IsNullOrEmpty(name) ? "(first in the list)" : "'" + name + "'"));
        }

        private void BuildKeybindsTab()
        {
            var page = new BackdropTabPage(Utils.Localization.T("Keybinds")) { AutoScroll = true };
            page.Name = "keybinds";   // stable key for LastTabKey

            string helpText =
                "Bind any action to a global hotkey. Click a field, then press a key " +
                "combo — any key, including Tab, Enter, Space and the arrows, alone or " +
                "with Ctrl/Alt/Shift/Win — OR click the middle / side mouse buttons. " +
                "Bare left & right click are reserved: add a modifier to bind them. " +
                "Backspace or Delete (pressed alone) clears a field; Esc leaves it. " +
                "Hotkeys work even when the window is in the tray. Save to apply.";

            var help = UiFactory.Label(helpText, 12, 12);
            help.MaximumSize = new Size(760, 0);
            help.AutoSize = true;
            help.ForeColor = _theme.TextMuted;

            // Measure the wrapped height so the controls below never collide with it,
            // regardless of translation length.
            int helpHeight = TextRenderer.MeasureText(
                helpText, help.Font, new Size(760, 0), TextFormatFlags.WordBreak).Height;
            int rowY = 12 + helpHeight + 12;

            var saveBtn = UiFactory.PrimaryButton("Save Keybinds", 12, rowY, 150, 32, _theme);
            saveBtn.Click += OnSaveKeybinds;
            _keybindsSaveBtn = saveBtn;      // Esc parks focus here (see EscapePressed)

            var resetBtn = UiFactory.Button(Utils.Localization.T("Reset to defaults"), 172, rowY, 150, 32);
            resetBtn.Click += OnResetKeybinds;


            // MaximumSize, or this runs off the page. The notice is AutoSize and its width
            // moves with the translation: at x=448 the French text measures 408px and the
            // German 405px, so both ended 53px and 43px past the page's own 772px content
            // edge \u2014 clipped mid-word, with no scrollbar to reach the rest. It is invisible
            // at rest, which is why it went unnoticed: the layout suite only sees it on a
            // run where something is actually unsaved. Capped here it wraps to two lines
            // (30px), still clear of the warning label at rowY+46.
            _keybindsDirtyLabel = new Label
            {
                Text = Utils.Localization.T("\u25CF Unsaved changes \u2014 click Save Keybinds"),
                AutoSize = true,
                MaximumSize = new Size(324, 0),
                Location = new Point(448, rowY + 8),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = _theme.WarningText,
                Visible = false
            };
            page.Controls.Add(_keybindsDirtyLabel);

            // A bare key is grabbed SYSTEM-WIDE, so it stops working in every other app
            // while Tempo runs. That is fine for F6/F8 and disastrous for "A" or Tab —
            // and the user has no way to guess which is which. Say it, in place.
            // Sits BELOW the Save/Reset row and the interval-step spinner (which end at
            // about rowY+42), not across them.
            _keybindsWarnLabel = new Label
            {
                AutoSize = false,
                Location = new Point(12, rowY + 46),
                Size = new Size(760, 32),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = _theme.WarningText,
                Visible = false
            };
            page.Controls.Add(_keybindsWarnLabel);

            // Which keyboard is Tempo actually talking to? Hotkeys bind by VIRTUAL-KEY
            // code, and what a virtual key MEANS depends on the layout — so when a bind
            // "doesn't work on my keyboard", the device and the layout are the first two
            // facts anyone needs. Until now Tempo couldn't tell you either.
            _keyboardInfoLabel = UiFactory.Caption("Detecting keyboard…", 12, rowY + 80);
            _keyboardInfoLabel.AutoSize = false;
            _keyboardInfoLabel.Width = 760;
            _keyboardInfoLabel.Height = 16;
            _keyboardInfoLabel.ForeColor = _theme.TextMuted;
            page.Controls.Add(_keyboardInfoLabel);
            RefreshKeyboardInfo();

            // Windows can REFUSE a hotkey (another program already owns the combo).
            // Tempo then quietly falls back to a keyboard hook: the action still fires,
            // but the key is no longer reserved, so it ALSO keeps doing its normal job
            // in whatever app you're using. That is the classic "my hotkey half works"
            // report, and until now nothing anywhere said it had happened.
            // 48, not 32: it can now carry a second sentence for bindings that got no
            // route at all, which is a different problem from "another app owns it".
            _keybindsRouteLabel = new Label
            {
                AutoSize = false,
                Location = new Point(12, rowY + 98),
                Size = new Size(760, 48),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = _theme.WarningText,
                Visible = false
            };
            page.Controls.Add(_keybindsRouteLabel);

            // A SETTING that switches these hotkeys off, said where they are configured.
            //
            // "Sleep in tray" unregisters every global hotkey while Tempo is hidden and
            // idle — deliberately, so a forgotten Tempo cannot start clicking on its own
            // hours later. But nothing on this page mentioned it, and the combination it
            // conflicts with is the DEFAULT one: Tempo closes to the tray, and with
            // "Start minimised to tray" it never shows a window at all. So you bind F6,
            // send Tempo to the tray, press F6 — and nothing happens, with no clue why.
            // The only hint was buried in the tray icon's tooltip.
            // Its own reserved band, rowY+148 .. rowY+186, ABOVE the column headers.
            // Space is reserved whether or not it is showing, for the same reason the two
            // notices above it reserve theirs: a label that pops in and shoves every
            // keybind row down the page is worse than the warning it carries.
            _keybindsTraySleepLabel = new Label
            {
                AutoSize = false,
                Location = new Point(12, rowY + 148),
                Size = new Size(760, 38),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = _theme.TextMuted,
                Visible = false
            };
            page.Controls.Add(_keybindsTraySleepLabel);
            RefreshTraySleepNotice();

            // "That key was taken from another action." Assigning a combination already
            // in use clears the previous owner — the right resolution, but it used to
            // happen in complete silence, and the evidence was one row out of 26 going
            // blank somewhere off-screen. Its own reserved band, rowY+190 .. rowY+220,
            // for the same reason as the notices above: a label that appears and shoves
            // every keybind row down the page is worse than the news it carries.
            _keybindsTakenLabel = new Label
            {
                AutoSize = false,
                Location = new Point(12, rowY + 190),
                Size = new Size(760, 30),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = _theme.AccentText,
                Visible = false
            };
            page.Controls.Add(_keybindsTakenLabel);

            // Its own reserved band, rowY+222 .. rowY+252.
            _keybindsHoldLabel = new Label
            {
                AutoSize = false,
                Location = new Point(12, rowY + 222),
                Size = new Size(760, 30),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = _theme.WarningText,
                Visible = false
            };
            page.Controls.Add(_keybindsHoldLabel);

            // Its own reserved band, rowY+254 .. rowY+302 — three lines, because it names
            // the key, where Tempo presses it from, and which action it fires, and the
            // translations of that run longer than the English.
            _keybindsSelfKeyLabel = new Label
            {
                AutoSize = false,
                Location = new Point(12, rowY + 254),
                Size = new Size(760, 48),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = _theme.WarningText,
                Visible = false
            };
            page.Controls.Add(_keybindsSelfKeyLabel);

            var stepCaption = UiFactory.Caption("Interval step (ms):", 344, rowY + 1);
            stepCaption.AutoSize = false;
            // Sized to the TEXT, not a round 250. The old fixed box was more than twice
            // the width of its caption, which hid the real problem: in Spanish, French,
            // Italian and Portuguese the words themselves run past x=448, where the
            // "Unsaved changes" label starts, and the two overprint. Measuring is also
            // what the help label above already does, for the same reason.
            stepCaption.Width = TextRenderer.MeasureText(stepCaption.Text, stepCaption.Font).Width + 4;
            stepCaption.Height = 16;
            page.Controls.Add(stepCaption);

            // Park the dirty label after the caption rather than at a hard-coded 448.
            // This row is the only place it can live — the bands below it are reserved,
            // as the comment further down sets out — so it has to share the row without
            // colliding, in every language. The page is ~815 wide and the longest of these
            // (Portuguese) needs about 748, so this fits without a second line.
            _keybindsDirtyLabel.Left = Math.Max(448, stepCaption.Right + 16);
            _intervalStepNum = UiFactory.Numeric(344, rowY + 18, 90, 1, 600000, 10);
            _intervalStepNum.ValueChanged += OnKeybindEdited;

            page.Controls.Add(help);
            page.Controls.Add(saveBtn);
            page.Controls.Add(resetBtn);
            page.Controls.Add(_intervalStepNum);

            // Column headers, below the whole header block:
            //   rowY      .. rowY+42    Save / Reset / interval-step
            //   rowY+46   .. rowY+78    bare-key warning (space RESERVED even when hidden —
            //                           a label that pops in and shoves every row down the
            //                           page would be worse than the warning it carries)
            //   rowY+80   .. rowY+96    detected keyboard
            //   rowY+98   .. rowY+146   "Windows refused / could not bind this hotkey" (reserved)
            //   rowY+148  .. rowY+186   "Sleep in tray pauses these" notice (also reserved)
            //   rowY+190  .. rowY+220   "that key was taken from another action"
            //   rowY+222  .. rowY+252   hold trigger vs the clicked button
            //   rowY+254  .. rowY+302   a key Tempo presses itself is bound here
            int headerY = rowY + 306;
            page.Controls.Add(UiFactory.Label(Utils.Localization.T("Action"), 16, headerY, FontStyle.Bold));
            page.Controls.Add(UiFactory.Label(Utils.Localization.T("Hotkey"), 300, headerY, FontStyle.Bold));

            int y = headerY + 26;
            const int rowHeight = 44;

            _bindingControls.Clear();
            foreach (var info in HotkeyActions.All)
            {
                var label = UiFactory.Label(info.Label, 16, y + 3);
                label.Width = 270;
                label.AutoSize = false;
                label.Height = 24;

                var capture = new HotkeyCaptureControl
                {
                    Left = 300,
                    Top = y,
                    Width = 220,
                    Font = UiFactory.BodyFont
                };

                // Silence the live hotkeys while this field is listening.
                capture.PauseHotkeys = paused =>
                {
                    if (_hotkeys != null) { _hotkeys.DeliveryPaused = paused; }
                };

                _bindingControls[info.Action] = capture;
                capture.HotkeyChanged += OnKeybindEdited;
                // Both translated: a screen reader announces these, and neither was
                // going through the translator — the name was raw concatenation and the
                // description came straight off the metadata table.
                capture.AccessibleName = Utils.Localization.F("Hotkey for {0}",
                    Utils.Localization.T(info.Label));
                capture.AccessibleDescription = Utils.Localization.T(info.Description);

                page.Controls.Add(label);
                page.Controls.Add(capture);

                // The three quick-play rows get a macro picker where the description
                // would go. That is the one place a user actually asks "play macro 1 —
                // which macro is that?", and until now nothing on this page answered
                // it. The description moves to the tooltip so nothing is lost.
                int slot = SlotForAction(info.Action);
                if (slot > 0)
                {
                    var pick = UiFactory.Combo(530, y, 186);
                    pick.DropDownStyle = ComboBoxStyle.DropDownList;
                    pick.AccessibleName = Utils.Localization.F("Macro for {0}",
                        Utils.Localization.T(info.Label));
                    pick.SelectedIndexChanged += OnMacroSlotPicked;
                    pick.Tag = slot;
                    _macroSlotCombos[slot] = pick;
                    page.Controls.Add(pick);
                    // The description tooltip is attached in SetupTooltips — _tips does
                    // not exist yet at tab-build time, so setting it here would be a
                    // silent no-op.
                }
                else
                {
                    var desc = UiFactory.Caption(info.Description, 530, y + 1);
                    desc.ForeColor = _theme.TextMuted;
                    desc.AutoSize = false;
                    desc.Size = new Size(186, 40);
                    page.Controls.Add(desc);
                }

                y += rowHeight;
            }

            // Twenty-six actions, and on a typical setup five or six are bound. Finding
            // out which means scrolling the whole list, and there was no way to get the
            // answer out of Tempo at all — for a note, a guide, or a bug report.
            //
            // BELOW the list, not up in the button row. That row looks like it has a gap
            // to the right of the interval step, and it has not: the "Unsaved changes"
            // notice lives there, hidden until you edit something, and its width moves
            // with the translation. A button at x=530 overlapped it in all six languages
            // — which the layout suite caught and I had not, because at rest the notice
            // is invisible. Down here the space is the page's own, after the last row.
            _copyKeybindsBtn = UiFactory.Button(Utils.Localization.T("Copy keybinds"), 12, y + 12, 190, 32);
            _copyKeybindsBtn.Click += OnCopyKeybinds;
            page.Controls.Add(_copyKeybindsBtn);

            _tabs.TabPages.Add(page);
        }

        private void LoadKeybindsIntoUi()
        {
            _suppressKeybindEvents = true;
            _settings.EnsureBindings();

            foreach (var pair in _bindingControls)
            {
                HotkeyDefinition hk = _settings.HotkeyFor(pair.Key);
                pair.Value.Hotkey = hk != null ? hk.Clone() : new HotkeyDefinition();
            }

            if (_intervalStepNum != null)
            {
                int step = _settings.IntervalStepMilliseconds;
                if (step < 1) step = 1;
                if (step > 600000) step = 600000;
                _intervalStepNum.Value = step;
            }

            HighlightConflicts();
            _suppressKeybindEvents = false;
            if (_keybindsDirtyLabel != null)
            {
                _keybindsDirtyLabel.Visible = false;
            }
        }

        /// <summary>Any user edit: refresh conflict colours and flag unsaved work.</summary>
        private void OnKeybindEdited(object sender, EventArgs e)
        {
            if (_suppressKeybindEvents)
            {
                return;
            }

            // Prevent conflicts instead of merely flagging them: when a field is set to a
            // combination already used by another action, clear the OTHER field(s) so the
            // key just assigned wins. A combo can then only ever trigger one action, which
            // removes the "two actions share a key, only one mysteriously works" confusion.
            var changed = sender as HotkeyCaptureControl;
            if (changed != null && changed.Hotkey != null && changed.Hotkey.IsValid)
            {
                // Compared structurally, not by the translated display string: this
                // branch DESTROYS the other binding, so a false match caused by two key
                // names translating alike would silently throw away the user's work.
                // See HotkeyDefinition.ToIdentityString.
                HotkeyDefinition combo = changed.Hotkey;
                var taken = new List<HotkeyAction>();
                _suppressKeybindEvents = true;
                try
                {
                    foreach (var pair in _bindingControls)
                    {
                        HotkeyCaptureControl other = pair.Value;
                        if (!ReferenceEquals(other, changed) &&
                            other.Hotkey != null && other.Hotkey.IsValid &&
                            other.Hotkey.SameCombination(combo))
                        {
                            other.Hotkey = new HotkeyDefinition(); // the previous owner loses the key
                            taken.Add(pair.Key);
                        }
                    }
                }
                finally
                {
                    _suppressKeybindEvents = false;
                }

                // SAY SO. Clearing the previous owner is the right resolution, but doing
                // it in silence is not: the only trace was one of 26 rows — almost
                // certainly scrolled out of view — quietly reading "(none)", and after
                // the clear there is no clash left for the colouring to show. A stray
                // keypress into the wrong field could therefore unbind Emergency stop
                // with nothing on screen to say it had happened, and nothing to undo it.
                if (taken.Count > 0 && _keybindsTakenLabel != null)
                {
                    var names = new List<string>();
                    foreach (HotkeyAction a in taken)
                    {
                        names.Add(HotkeyActions.LabelFor(a));
                    }
                    names.Sort(StringComparer.CurrentCultureIgnoreCase);

                    _keybindsTakenLabel.Text = Utils.Localization.F(
                        "{0} was already used by {1} — that action is now unbound.",
                        combo.ToDisplayString(), string.Join(", ", names.ToArray()));
                    _keybindsTakenLabel.Visible = true;
                }
                else if (_keybindsTakenLabel != null)
                {
                    _keybindsTakenLabel.Visible = false;
                }
            }

            HighlightConflicts();
            if (_keybindsDirtyLabel != null)
            {
                _keybindsDirtyLabel.Visible = true;
            }
        }

        /// <summary>
        /// Colours the hotkey fields by what is wrong with them:
        ///  • RED   — two actions share the combination (a real clash).
        ///  • AMBER — a bare key that Windows needs for typing or navigation.
        ///
        /// The amber case is the one that used to bite silently. A global hotkey is
        /// registered SYSTEM-WIDE, so a bare "A" means the letter A stops reaching every
        /// other program while Tempo runs — and a bare Tab (now bindable) would take Tab
        /// away from the entire desktop. It is still allowed; it just says so first.
        /// </summary>
        private void HighlightConflicts()
        {
            if (_bindingControls.Count == 0)
            {
                return;
            }

            // Count how many fields use each combination.
            var counts = new Dictionary<string, int>();
            foreach (var pair in _bindingControls)
            {
                HotkeyDefinition hk = pair.Value.Hotkey;
                if (hk == null || !hk.IsValid)
                {
                    continue;
                }

                // Identity, not display text — see HotkeyDefinition.ToIdentityString.
                string key = hk.ToIdentityString();
                counts[key] = counts.TryGetValue(key, out int c) ? c + 1 : 1;
            }

            Color normal = _theme.InputBackground;
            var risky = new List<string>();

            foreach (var pair in _bindingControls)
            {
                HotkeyDefinition hk = pair.Value.Hotkey;
                bool valid = hk != null && hk.IsValid;
                bool isClash = valid &&
                               counts.TryGetValue(hk.ToIdentityString(), out int c) && c > 1;
                // Mouse buttons count too. A bare Middle/X1/X2 is swallowed system-wide
                // exactly like a bare letter is, but IsRiskyBareKey returns false for
                // every mouse binding — so the one case where a user is MOST likely to
                // break something they use constantly (middle-click, Back) was the one
                // case that got no amber field and no warning at all.
                bool isRisky = valid && (hk.IsRiskyBareKey || hk.IsRiskyBareMouseButton);

                if (isRisky && !isClash)
                {
                    risky.Add(hk.ToDisplayString());
                }

                // A clash is the more serious problem, so it wins the colour.
                pair.Value.BackColor =
                    isClash ? BlendColors(normal, _theme.Danger, 0.35)
                  : isRisky ? BlendColors(normal, _theme.Warning, 0.30)
                  : normal;
            }

            // Hold mode + a trigger that is the button being clicked = a hold that can
            // never be released, so PollHoldKey refuses it. Say why here, where the
            // binding is chosen, rather than leaving hold mode mysteriously inert.
            HotkeyDefinition holdTrigger =
                _bindingControls.TryGetValue(HotkeyAction.ToggleStartStop, out HotkeyCaptureControl tc)
                    ? tc.Hotkey : null;
            if (_keybindsHoldLabel != null)
            {
                bool fights = holdTrigger != null && holdTrigger.IsValid &&
                              HoldTriggerFightsClickButton(holdTrigger);
                if (fights)
                {
                    _keybindsHoldLabel.Text = Utils.Localization.F(
                        "⚠ {0} is also the button the clicker clicks. In Hold-to-click mode Tempo's own "
                        + "clicks would keep that button reading as held, so the hold could never be "
                        + "released — it will not engage. Pick a different button, or a side button.",
                        holdTrigger.ToDisplayString());
                }
                _keybindsHoldLabel.Visible = fights;
            }

            // The keyboard half of the same idea: a key Tempo sends that is bound here.
            RefreshSelfPressedKeyNotice();

            if (_keybindsWarnLabel != null)
            {
                if (risky.Count == 0)
                {
                    _keybindsWarnLabel.Visible = false;
                }
                else
                {
                    risky.Sort(StringComparer.OrdinalIgnoreCase);
                    string bare = string.Join(", ", risky.ToArray());
                    _keybindsWarnLabel.Text = risky.Count == 1
                        ? Utils.Localization.F(
                            "⚠ {0} is bound bare. A global hotkey is taken system-wide, so that key "
                            + "will stop working in every other program while Tempo is running. "
                            + "Add Ctrl/Alt/Shift, or use an F-key, unless that is exactly what you want.",
                            bare)
                        : Utils.Localization.F(
                            "⚠ {0} are bound bare. A global hotkey is taken system-wide, so those keys "
                            + "will stop working in every other program while Tempo is running. "
                            + "Add Ctrl/Alt/Shift, or use an F-key, unless that is exactly what you want.",
                            bare);
                    _keybindsWarnLabel.Visible = true;
                }
            }
        }

        /// <summary>
        /// Which action a key press with these modifiers would fire, or null. Reads the
        /// CONTROLS, so it follows unsaved edits like the rest of this page.
        /// </summary>
        private HotkeyAction? ActionFiredBy(int vk, bool ctrl, bool alt, bool shift, bool win)
        {
            foreach (var pair in _bindingControls)
            {
                HotkeyDefinition hk = pair.Value.Hotkey;
                if (hk == null || !hk.IsValid || hk.IsMouse) { continue; }
                if ((int)hk.GetVirtualKey() != vk) { continue; }
                // Exact, not "at least": a Ctrl+F6 hotkey does not fire on a bare F6, and
                // reporting it would send someone rebinding a key that was never involved.
                if (hk.Control == ctrl && hk.Alt == alt && hk.Shift == shift && hk.Win == win)
                {
                    return pair.Key;
                }
            }
            return null;
        }

        /// <summary>
        /// Tracks a modifier key going down or up while walking a macro's steps, so the
        /// match above is made against the modifiers that will actually be held.
        /// Returns true when the key IS a modifier (and so is not itself a trigger).
        /// </summary>
        private static bool TrackModifier(int vk, bool down,
            ref bool ctrl, ref bool alt, ref bool shift, ref bool win)
        {
            switch (vk)
            {
                case 0x11: case 0xA2: case 0xA3: ctrl = down; return true;    // CONTROL / L / R
                case 0x12: case 0xA4: case 0xA5: alt = down; return true;     // MENU / L / R
                case 0x10: case 0xA0: case 0xA1: shift = down; return true;   // SHIFT / L / R
                case 0x5B: case 0x5C: win = down; return true;                // LWIN / RWIN
                default: return false;
            }
        }

        /// <summary>
        /// Warns when a key TEMPO ITSELF presses is also bound as one of its hotkeys.
        ///
        /// This is a real, silent, total failure and nothing anywhere reported it.
        /// Measured: with the clicker set to auto-press F6 — the DEFAULT Start/Stop key —
        /// pressing Start logged "engine started" and "engine stopped" 6 ms apart, every
        /// time. The very first synthetic press fired the toggle and shut the run down.
        /// A control run on F1 (unbound) ran the full four seconds.
        ///
        /// It cannot be fixed by ignoring Tempo's own input the way the mouse path does
        /// (<c>OnMouseHotkey</c> drops injected clicks): WM_HOTKEY carries no "injected"
        /// flag, so a RegisterHotKey binding cannot tell Tempo's keystroke from yours, and
        /// the keyboard-hook fallback deliberately accepts injected keys so remapper-driven
        /// keyboards keep working. So it is named here instead, on the page that owns one
        /// half of the clash.
        ///
        /// Both producers of self-sent keys are covered: the clicker's auto-press key and
        /// every macro's key steps — macro playback presses keys through the same
        /// InputSimulator, so it collides identically.
        /// </summary>
        private void RefreshSelfPressedKeyNotice()
        {
            if (_keybindsSelfKeyLabel == null || _keybindsSelfKeyLabel.IsDisposed) { return; }

            var hits = new List<string>();
            try
            {
                // 1. The clicker's auto-press key. Sent bare, so only an unmodified
                //    binding on the same key can catch it.
                if (_buttonCombo != null && _buttonCombo.SelectedIndex == KeyTargetIndex
                    && _selectedKeyVk != 0)
                {
                    HotkeyAction? a = ActionFiredBy(_selectedKeyVk, false, false, false, false);
                    if (a.HasValue)
                    {
                        hits.Add(Utils.Localization.F("{0} (the clicker's auto-press key) → \"{1}\"",
                            KeyLabel(_selectedKeyVk), HotkeyActions.LabelFor(a.Value)));
                    }
                }

                // 2. Macro key steps, with the modifiers the macro is holding at that point.
                if (_macros != null && _macros.Macros != null)
                {
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (Macro m in _macros.Macros)
                    {
                        if (m == null || m.Actions == null) { continue; }
                        bool ctrl = false, alt = false, shift = false, win = false;
                        foreach (MacroAction step in m.Actions)
                        {
                            if (step == null) { continue; }
                            if (step.Type == MacroActionType.KeyUp)
                            {
                                TrackModifier(step.VirtualKey, false, ref ctrl, ref alt, ref shift, ref win);
                                continue;
                            }
                            if (step.Type != MacroActionType.KeyDown) { continue; }
                            if (TrackModifier(step.VirtualKey, true, ref ctrl, ref alt, ref shift, ref win))
                            {
                                continue;
                            }
                            HotkeyAction? a = ActionFiredBy(step.VirtualKey, ctrl, alt, shift, win);
                            if (!a.HasValue) { continue; }
                            // One line per macro+key, however many times the macro presses it.
                            if (!seen.Add(m.Name + " " + step.VirtualKey)) { continue; }
                            hits.Add(Utils.Localization.F("{0} (macro “{1}”) → “{2}”",
                                MacroAction.KeyName(step.VirtualKey), m.Name,
                                HotkeyActions.LabelFor(a.Value)));
                        }
                    }
                }
            }
            catch (Exception ex) { Utils.Logger.Swallow("RefreshSelfPressedKeyNotice", ex); }

            if (hits.Count == 0)
            {
                _keybindsSelfKeyLabel.Visible = false;
                return;
            }

            hits.Sort(StringComparer.CurrentCultureIgnoreCase);
            // Two lines of band, so cap the list rather than letting a macro-heavy setup
            // clip the half that says what to do about it.
            string list = hits.Count <= 3
                ? string.Join("; ", hits.ToArray())
                : string.Join("; ", hits.GetRange(0, 3).ToArray()) + "; "
                  + Utils.Localization.F("and {0} more", hits.Count - 3);

            _keybindsSelfKeyLabel.Text = hits.Count == 1
                ? Utils.Localization.F(
                    "⚠ {0} — Tempo presses this key itself, and Windows can't tell its keystroke from "
                    + "yours, so every press runs that action too. Change the key or the binding.", list)
                : Utils.Localization.F(
                    "⚠ {0} — Tempo presses these keys itself, and Windows can't tell its keystrokes "
                    + "from yours, so every press runs that action too. Change the keys or the bindings.",
                    list);
            _keybindsSelfKeyLabel.Visible = true;
        }

        private static Color BlendColors(Color a, Color b, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            int r = (int)(a.R + (b.R - a.R) * t);
            int g = (int)(a.G + (b.G - a.G) * t);
            int bl = (int)(a.B + (b.B - a.B) * t);
            return Color.FromArgb(r, g, bl);
        }

        private void OnSaveKeybinds(object sender, EventArgs e)
        {
            _settings.EnsureBindings();

            foreach (var pair in _bindingControls)
            {
                HotkeyBinding binding = _settings.GetBinding(pair.Key);
                if (binding == null)
                {
                    binding = new HotkeyBinding(pair.Key, new HotkeyDefinition());
                    _settings.Bindings.Add(binding);
                }

                binding.Hotkey = pair.Value.Hotkey != null
                    ? pair.Value.Hotkey.Clone()
                    : new HotkeyDefinition();
            }

            _settings.IntervalStepMilliseconds = (int)_intervalStepNum.Value;

            string conflicts = FindConflicts();
            SettingsManager.Save(_settings);
            ApplyHotkeysFromSettings();

            if (!string.IsNullOrEmpty(conflicts))
            {
                // NOT "only one action will respond" — that was wrong, and wrong in the
                // direction that matters. Windows will not reserve one combination twice,
                // so the second action falls back to the low-level keyboard hook, which
                // still sees the keystroke WM_HOTKEY consumed: one press then runs BOTH
                // actions. Telling someone only one would fire meant the surprising case
                // (start clicking AND emergency-stop on one key) read as a Tempo bug with
                // no explanation. Mouse bindings really do stop at the first match, but
                // the keyboard case is the common one.
                ShowWarning(
                    Utils.Localization.F("Keybinds saved, but some combinations are used more than once. "
                    + "Windows cannot reserve one combination twice, so a single press will run BOTH "
                    + "actions:\n\n{0}", conflicts));
            }
            else
            {
                // Same as Settings: confirm in place instead of a modal that has to be
                // clicked away. (Conflicts still get a real dialog above — that one is
                // worth interrupting for.)
                ConfirmOnButton(_keybindsSaveBtn);
            }

            if (_keybindsDirtyLabel != null)
            {
                _keybindsDirtyLabel.Visible = false;
            }
        }

        private Button _copyKeybindsBtn;

        /// <summary>
        /// Puts the current bindings on the clipboard as plain text.
        ///
        /// Reads the CONTROLS, not the saved settings, so what you copy is what you are
        /// looking at — including edits you have not pressed Save on yet. The unbound
        /// actions are listed too, under their own heading: "what could I bind?" is the
        /// other half of the question, and the answer is otherwise a scroll through
        /// twenty-six rows.
        /// </summary>
        private void OnCopyKeybinds(object sender, EventArgs e)
        {
            try
            {
                var bound = new System.Text.StringBuilder();
                var free = new System.Text.StringBuilder();
                int boundCount = 0, total = 0;

                foreach (HotkeyActions.Info info in HotkeyActions.All)
                {
                    total++;
                    string label = Utils.Localization.T(info.Label);
                    HotkeyDefinition hk = _bindingControls.TryGetValue(info.Action, out var ctl)
                        ? ctl.Hotkey
                        : null;

                    if (hk != null && hk.IsValid)
                    {
                        boundCount++;
                        bound.Append("  ").Append(label.PadRight(28)).Append(hk.ToString()).AppendLine();
                    }
                    else
                    {
                        free.Append("  ").Append(label).AppendLine();
                    }
                }

                var sb = new System.Text.StringBuilder();
                sb.Append("Tempo ").Append(Utils.AppVersion.Stamp)
                  .Append("  ·  ").Append(Utils.Localization.T("Keybinds")).AppendLine();
                sb.AppendLine(Utils.Localization.F("{0} of {1} actions bound", boundCount, total));
                sb.AppendLine();
                if (boundCount > 0) { sb.Append(bound); }
                if (free.Length > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine(Utils.Localization.T("Not bound:"));
                    sb.Append(free);
                }

                Clipboard.SetText(sb.ToString());
                ConfirmOnButton(_copyKeybindsBtn);
            }
            catch (Exception ex)
            {
                Utils.Logger.Swallow("OnCopyKeybinds", ex);
                ShowWarning(Utils.Localization.T("Could not copy the keybinds to the clipboard."));
            }
        }

        private void OnResetKeybinds(object sender, EventArgs e)
        {
            var confirm = MessageBox.Show(this,
                "Reset all keybinds to their defaults?",
                "Tempo",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
            {
                return;
            }

            _settings.Bindings = HotkeyActions.DefaultBindings();
            _settings.IntervalStepMilliseconds = 10;
            SettingsManager.Save(_settings);
            LoadKeybindsIntoUi();
            ApplyHotkeysFromSettings();
        }

        /// <summary>
        /// Returns a human-readable list of duplicate key combinations, or an empty
        /// string when there are none.
        /// </summary>
        private string FindConflicts()
        {
            var seen = new Dictionary<string, HotkeyAction>();
            var clashes = new List<string>();

            foreach (var pair in _bindingControls)
            {
                HotkeyDefinition hk = pair.Value.Hotkey;
                if (hk == null || !hk.IsValid)
                {
                    continue;
                }

                // Grouped by identity, reported by display string: the first is the
                // question "same combination?", the second is what the user reads.
                // See HotkeyDefinition.ToIdentityString for why they must not be one.
                string key = hk.ToIdentityString();
                if (seen.TryGetValue(key, out HotkeyAction other))
                {
                    clashes.Add($"  • {hk.ToDisplayString()}: {HotkeyActions.LabelFor(other)} / {HotkeyActions.LabelFor(pair.Key)}");
                }
                else
                {
                    seen[key] = pair.Key;
                }
            }

            if (clashes.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            foreach (string c in clashes)
            {
                sb.AppendLine(c);
            }
            return sb.ToString();
        }
    }
}
