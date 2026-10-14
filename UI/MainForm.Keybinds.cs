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
            SetKeybindNotice(_keybindsTraySleepLabel, !sleeping ? null : Utils.Localization.T(
                "⏾  “Sleep in tray” is on: after 30 minutes hidden with nothing running, Tempo sleeps and "
                + "its start hotkeys pause — the first press then only wakes it, so press again. Emergency "
                + "stop and Show / hide window always work. Turn it off in Settings → Startup & Window."));
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
                SetKeybindNotice(_keybindsRouteLabel, null);
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
                        "⚠ Windows wouldn't reserve {0} — another program already owns it. Tempo catches it "
                        + "with a keyboard hook instead and keeps the key to itself, so that program no longer "
                        + "reacts to it while Tempo is open. Pick a different combination if you need that "
                        + "program's shortcut.",
                        names)
                    : Utils.Localization.F(
                        "⚠ Windows wouldn't reserve {0} — another program already owns them. Tempo catches them "
                        + "with a keyboard hook instead and keeps the keys to itself, so that program no longer "
                        + "reacts to them while Tempo is open. Pick different combinations if you need that "
                        + "program's shortcuts.",
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

            SetKeybindNotice(_keybindsRouteLabel, string.Join("\r\n", parts.ToArray()));
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
                    combo.Items.Add(QuickPlayFallbackText(pair.Key));
                    foreach (var m in _macros.Macros)
                    {
                        if (m != null) { combo.Items.Add(m.Name); }
                    }

                    string assigned = MacroSlotName(pair.Key);
                    // Matched the way playback matches it (MacroStore.GetByName ignores case): a slot
                    // saved as "farm" for the macro "Farm" — a case-only rename left exactly that —
                    // played it, but the picker said "farm  (missing)".
                    int idx = string.IsNullOrEmpty(assigned) ? 0 : -1;
                    for (int i = 1; idx < 0 && i < combo.Items.Count; i++)
                    {
                        if (combo.Items[i] is string item && string.Equals(item, assigned, StringComparison.OrdinalIgnoreCase))
                        {
                            idx = i;
                        }
                    }

                    // Assigned to something that is gone. Keep the name visible rather
                    // than silently snapping back to "first in the list" — the hotkey
                    // does nothing right now and the user needs to see why.
                    if (idx < 0)
                    {
                        combo.Items.Add(new MissingMacroSlot(assigned));
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

            // The "Name  (missing)" entry stands for the assignment as it was: picking it (back)
            // restores that name. It used to save the whole LABEL as the macro's name —
            // "Name  (missing)" — a macro that could never exist, and the next refresh showed
            // "Name  (missing)  (missing)".
            // Index 0 is "(first/second/third in the list)" — i.e. clear the assignment and
            // fall back to the old positional behaviour.
            string name = combo.SelectedItem is MissingMacroSlot missing ? missing.Name
                        : combo.SelectedIndex <= 0 ? "" : combo.SelectedItem as string;
            SetMacroSlotName(slot, name);
            try { Persistence.SettingsManager.Save(_settings); } catch { }

            Utils.Logger.Info("[Macros] quick-play slot " + slot + " -> " +
                              (string.IsNullOrEmpty(name) ? "(macro #" + slot + " in the list)" : "'" + name + "'"));
        }

        /// <summary>
        /// The picker's "nothing chosen" entry: an unassigned slot N plays the Nth macro in the list. All
        /// three said "(first in the list)", so slot 2's picker named a different macro than its key played.
        /// </summary>
        private static string QuickPlayFallbackText(int slot)
        {
            switch (slot)
            {
                case 2: return Utils.Localization.T("(second in the list)");
                case 3: return Utils.Localization.T("(third in the list)");
                default: return Utils.Localization.T("(first in the list)");
            }
        }

        /// <summary>A quick-play choice naming a macro that is no longer in the library.</summary>
        private sealed class MissingMacroSlot
        {
            private readonly string _text;
            public string Name { get; }
            public MissingMacroSlot(string name)
            {
                Name = name;
                _text = Utils.Localization.F("{0}  (missing)", name);
            }
            public override string ToString() => _text;
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
            // "Bare left & right click": without this the label hid the "&" as an accelerator marker.
            help.UseMnemonic = false;
            help.MaximumSize = new Size(760, 0);
            help.AutoSize = true;
            help.ForeColor = _theme.TextMuted;

            // Measure the wrapped height so the controls below never collide with it,
            // regardless of translation length.
            // In the window's units (MeasureLayout): built before the window is scaled.
            int helpHeight = MeasureLayout(help.Text, help.Font, 760, TextFormatFlags.NoPrefix).Height;
            int rowY = 12 + helpHeight + 12;

            var saveBtn = UiFactory.PrimaryButton("Save Keybinds", 12, rowY, 150, 32, _theme);
            // "Saved ✓" for a moment after a save (ConfirmOnButton): measured both ways.
            LayoutFitter.DeclareCaptions(saveBtn, Utils.Localization.T("Save Keybinds"), Utils.Localization.T("Saved  ✓"));
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

            // Hotkeys also fire with Shift/Ctrl held on top (AppSettings.HotkeysIgnoreExtraShiftCtrl).
            // Applies at once, like the quick-play pickers: it changes how keys are caught, not what
            // is bound. Under the button row; the notices stack below it.
            _extraShiftCtrlCheck = UiFactory.Check("Hotkeys also work while you hold Shift or Ctrl", 12, rowY + 44,
                                                   _settings == null || _settings.HotkeysIgnoreExtraShiftCtrl);
            _extraShiftCtrlCheck.CheckedChanged += OnExtraShiftCtrlChanged;
            page.Controls.Add(_extraShiftCtrlCheck);

            // The notices below share one stack under the Save/Reset row (see
            // RelayoutKeybindNotices): each takes the height its text needs, only while it is
            // showing. Every one of them used to own a band reserved whether it showed or not —
            // seven bands, 280 px — so on a normal setup the list began more than half-way down
            // the window below a wall of empty space, and only six of the 26 rows fitted.
            //
            // A bare key is grabbed SYSTEM-WIDE, so it stops working in every other app
            // while Tempo runs. That is fine for F6/F8 and disastrous for "A" or Tab —
            // and the user has no way to guess which is which. Say it, in place.
            // Past five lines (many keys listed), it ends in "…" and the whole warning is on hover.
            _keybindsWarnLabel = new Label
            {
                AutoSize = false,
                Location = new Point(12, rowY + 46),
                Size = new Size(760, 46),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = _theme.WarningText,
                AutoEllipsis = true,
                Visible = false
            };
            page.Controls.Add(_keybindsWarnLabel);
            TipStyle.FollowText(_keybindsWarnLabel);

            // Which keyboard is Tempo actually talking to? Hotkeys bind by VIRTUAL-KEY
            // code, and what a virtual key MEANS depends on the layout — so when a bind
            // "doesn't work on my keyboard", the device and the layout are the first two
            // facts anyone needs. Until now Tempo couldn't tell you either.
            _keyboardInfoLabel = UiFactory.Caption("Detecting keyboard…", 12, rowY + 94);
            _keyboardInfoLabel.AutoSize = false;
            _keyboardInfoLabel.Width = 760;
            _keyboardInfoLabel.Height = 16;
            _keyboardInfoLabel.ForeColor = _theme.TextMuted;
            _keyboardInfoLabel.AutoEllipsis = true;
            page.Controls.Add(_keyboardInfoLabel);
            RefreshKeyboardInfo();

            // Windows can REFUSE a hotkey (another program already owns the combo).
            // Tempo then quietly falls back to a keyboard hook: the action still fires,
            // and the hook keeps the key from the other program — which no longer gets
            // its shortcut. Until it did that, the key ALSO kept doing its normal job there
            // (the classic "my hotkey half works"), and nothing anywhere said so. It also
            // carries a second sentence for bindings that got no route at all, which is
            // a different problem from "another app owns it".
            _keybindsRouteLabel = NewKeybindNotice(rowY + 112, _theme.WarningText);
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
            _keybindsTraySleepLabel = NewKeybindNotice(rowY + 162, _theme.TextMuted);
            // "Settings → Startup & Window": a label takes "&" as an accelerator marker and hides it,
            // so this read "Startup  Window".
            _keybindsTraySleepLabel.UseMnemonic = false;
            page.Controls.Add(_keybindsTraySleepLabel);

            // "That key was taken from another action." Assigning a combination already
            // in use clears the previous owner — the right resolution, but it used to
            // happen in complete silence, and the evidence was one row out of 26 going
            // blank somewhere off-screen.
            _keybindsTakenLabel = NewKeybindNotice(rowY + 212, _theme.AccentText);
            page.Controls.Add(_keybindsTakenLabel);

            // The hold trigger is the button the clicker itself clicks.
            _keybindsHoldLabel = NewKeybindNotice(rowY + 244, _theme.WarningText);
            page.Controls.Add(_keybindsHoldLabel);

            // A key Tempo presses itself is bound here: names the key, where Tempo presses
            // it from, and which action it fires.
            _keybindsSelfKeyLabel = NewKeybindNotice(rowY + 276, _theme.WarningText);
            page.Controls.Add(_keybindsSelfKeyLabel);

            // Top to bottom: what the last edit did or risks first, the standing problems next,
            // the background facts last.
            _keybindNotices.Clear();
            _keybindNotices.AddRange(new[] { _keybindsWarnLabel, _keybindsTakenLabel, _keybindsRouteLabel,
                                             _keybindsSelfKeyLabel, _keybindsHoldLabel, _keybindsTraySleepLabel,
                                             _keyboardInfoLabel });
            _keybindNoticeShown.Clear();
            _keybindNoticeShown.Add(_keyboardInfoLabel);   // always there
            RefreshTraySleepNotice();

            var stepCaption = UiFactory.Caption("Interval step (ms):", 344, rowY + 1);
            stepCaption.AutoSize = false;
            // Sized to the TEXT, not a round 250. The old fixed box was more than twice
            // the width of its caption, which hid the real problem: in Spanish, French,
            // Italian and Portuguese the words themselves run past x=448, where the
            // "Unsaved changes" label starts, and the two overprint. Measuring is also
            // what the help label above already does, for the same reason.
            stepCaption.Width = MeasureLayout(stepCaption.Text, stepCaption.Font, 0, TextFormatFlags.Default).Width + 4;
            stepCaption.Height = 16;
            page.Controls.Add(stepCaption);

            // Park the dirty label after the caption rather than at a hard-coded 448.
            // This row is the only place it can live — everything below it moves with the
            // notices — so it has to share the row without colliding, in every language.
            // The page is ~815 wide and the longest of these (Portuguese) needs about 748,
            // so this fits without a second line.
            _keybindsDirtyLabel.Left = Math.Max(448, stepCaption.Right + 16);
            _intervalStepNum = UiFactory.Numeric(344, rowY + 18, 90, 1, 600000, 10);
            _intervalStepNum.ValueChanged += OnKeybindEdited;

            page.Controls.Add(help);
            page.Controls.Add(saveBtn);
            page.Controls.Add(resetBtn);
            page.Controls.Add(_intervalStepNum);

            // Column headers, below the Save / Reset / interval-step row (rowY .. rowY+42), the
            // Shift/Ctrl tick box and the notice stack under them. Placed here as if only the
            // keyboard line showed; RelayoutKeybindNotices moves the headers and every row below
            // them to wherever the stack really ends.
            int headerY = rowY + 100;
            _keybindTable.Clear();
            int firstTableControl = page.Controls.Count;
            _keybindsHeaderLabel = UiFactory.Label(Utils.Localization.T("Action"), 16, headerY, FontStyle.Bold);
            page.Controls.Add(_keybindsHeaderLabel);
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
            // "Copied", not "Saved": it said "Saved ✓" after copying, about a page whose edits
            // were still unsaved — the opposite of the truth one line above it.
            LayoutFitter.DeclareCaptions(_copyKeybindsBtn, Utils.Localization.T("Copy keybinds"), Utils.Localization.T("Copied  ✓"));
            _copyKeybindsBtn.Click += OnCopyKeybinds;
            page.Controls.Add(_copyKeybindsBtn);

            // Everything from the column headers down moves as one block with the notice stack.
            for (int i = firstTableControl; i < page.Controls.Count; i++)
            {
                _keybindTable.Add(page.Controls[i]);
            }
            _keybindsPage = page;
            RelayoutKeybindNotices();
            // Coming into view (the window may have been scaled, or a notice changed while the
            // page was hidden) and on resize: cheap when nothing moved.
            page.VisibleChanged += (s, e) => { if (page.Visible) { RelayoutKeybindNotices(); } };
            page.Resize += (s, e) => ScheduleKeybindRelayout();
            // The stack hangs from the bottom of the button row, and that row is still adjusted after
            // the page's first layout (the start-up layout fitter moves the interval box down 4 px).
            // Queued, never done on the spot: a scroll moves every control one message at a time, and
            // part-way through the button row has moved and the table has not — re-stacking then
            // shifted the table by the scroll a second time.
            foreach (Control row in new Control[] { _intervalStepNum, saveBtn, _extraShiftCtrlCheck })
            {
                row.SizeChanged += (s, e) => ScheduleKeybindRelayout();
                row.LocationChanged += (s, e) => ScheduleKeybindRelayout();
            }
            // And the notices themselves: the start-up layout pass may grow or move one.
            foreach (Label notice in _keybindNotices)
            {
                notice.SizeChanged += (s, e) => ScheduleKeybindRelayout();
                notice.LocationChanged += (s, e) => ScheduleKeybindRelayout();
            }

            _tabs.TabPages.Add(page);
        }

        // ── The notice stack ────────────────────────────────────────────────────────────────

        /// <summary>Every notice, in stack order (see BuildKeybindsTab).</summary>
        private readonly List<Label> _keybindNotices = new List<Label>();

        /// <summary>
        /// Which notices are meant to show. Not <c>Label.Visible</c>: that reads false for every
        /// control on a page that is not the selected tab, so the stack would collapse to nothing
        /// whenever it was worked out with the page out of view.
        /// </summary>
        private readonly HashSet<Label> _keybindNoticeShown = new HashSet<Label>();

        /// <summary>The column headers, the rows and the Copy button: moved together.</summary>
        private readonly List<Control> _keybindTable = new List<Control>();

        private Label _keybindsHeaderLabel;
        private TabPage _keybindsPage;
        private bool _keybindRelayoutBusy;
        private bool _keybindRelayoutQueued;

        /// <summary>"Hotkeys also work while you hold Shift or Ctrl".</summary>
        private CheckBox _extraShiftCtrlCheck;

        private void OnExtraShiftCtrlChanged(object sender, EventArgs e)
        {
            if (_suppressKeybindEvents || _settings == null || _extraShiftCtrlCheck == null) { return; }
            bool on = _extraShiftCtrlCheck.Checked;
            if (_settings.HotkeysIgnoreExtraShiftCtrl == on) { return; }
            _settings.HotkeysIgnoreExtraShiftCtrl = on;
            try { SettingsManager.Save(_settings); } catch (Exception ex) { Utils.Logger.Swallow("OnExtraShiftCtrlChanged", ex); }
            ApplyHotkeysFromSettings();
            HighlightConflicts();   // which keys Tempo itself presses would fire a hotkey changes with it
            Utils.Logger.Info("[Hotkeys] Shift/Ctrl held on top of a hotkey " + (on ? "still fires it" : "no longer fires it") + ".");
        }

        /// <summary>Re-stacks once the current message is done (see where the button row is watched).</summary>
        private void ScheduleKeybindRelayout()
        {
            if (_keybindRelayoutQueued || IsDisposed) { return; }
            if (!IsHandleCreated) { RelayoutKeybindNotices(); return; }
            _keybindRelayoutQueued = true;
            try
            {
                BeginInvoke((Action)(() => { _keybindRelayoutQueued = false; RelayoutKeybindNotices(); }));
            }
            catch (InvalidOperationException) { _keybindRelayoutQueued = false; }
        }

        /// <summary>A notice label: hidden, as wide as the page's text column, cut with "…" past its cap.</summary>
        private Label NewKeybindNotice(int y, Color ink)
        {
            var label = new Label
            {
                AutoSize = false,
                Location = new Point(12, y),
                Size = new Size(760, 30),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = ink,
                AutoEllipsis = true,
                Visible = false
            };
            TipStyle.FollowText(label);
            return label;
        }

        /// <summary>
        /// Shows a notice with this text, or hides it (null/empty), then re-stacks the notices —
        /// unless <paramref name="restack"/> is false because the caller changes several and
        /// re-stacks once at the end.
        /// </summary>
        private void SetKeybindNotice(Label notice, string text, bool restack = true)
        {
            if (notice == null || notice.IsDisposed) { return; }
            bool show = !string.IsNullOrEmpty(text);
            if (show && !string.Equals(notice.Text, text, StringComparison.Ordinal)) { notice.Text = text; }
            if (show) { _keybindNoticeShown.Add(notice); } else { _keybindNoticeShown.Remove(notice); }
            notice.Visible = show;
            if (restack) { RelayoutKeybindNotices(); }
        }

        /// <summary>
        /// Stacks the showing notices under the Save/Reset row, each as tall as its text, and moves
        /// the table to just below them.
        ///
        /// A notice can appear while you are editing a field (a bare key, a key taken from another
        /// action), and a row that jumps away from under the pointer is worse than the news. So the
        /// field being edited is held still: when the table moves by N, the page scrolls by N with
        /// it, and what moves is the help text above — as far as the Save row, which stays in view.
        /// The old answer was to reserve every notice's space whether it showed or not, which
        /// pushed the list half-way down the window.
        /// </summary>
        private void RelayoutKeybindNotices()
        {
            TabPage page = _keybindsPage;
            if (page == null || page.IsDisposed || _keybindsHeaderLabel == null || _keybindRelayoutBusy) { return; }
            if (_keybindsSaveBtn == null || _intervalStepNum == null) { return; }
            // Page centring is moving everything at once (and back to the places noted below):
            // half-way through, the button row and the table disagree about where they are.
            if (_centeringBusy) { return; }
            _keybindRelayoutBusy = true;
            try
            {
                int gap = L(4);
                // Under the button row and the Shift/Ctrl tick box as they stand — positions already in
                // the window's current units and scrolled with the page, so no scale or scroll
                // arithmetic is needed.
                int y = Math.Max(_keybindsSaveBtn.Bottom, _intervalStepNum.Bottom);
                if (_extraShiftCtrlCheck != null) { y = Math.Max(y, _extraShiftCtrlCheck.Bottom); }
                y += L(5);
                var moves = new List<KeyValuePair<Label, Rectangle>>();
                foreach (Label notice in _keybindNotices)
                {
                    if (notice == null || notice.IsDisposed) { continue; }
                    if (!_keybindNoticeShown.Contains(notice))
                    {
                        // A hidden notice takes no room — and HAS none: the layout checks judge
                        // controls by size, not visibility (a page that is not the selected tab
                        // reports every control hidden), so one left full-size at its old place
                        // was an empty box lying across the rows.
                        var parked = new Rectangle(notice.Left, y, notice.Width, 0);
                        if (notice.Bounds != parked) { moves.Add(new KeyValuePair<Label, Rectangle>(notice, parked)); }
                        continue;
                    }
                    int h = KeybindNoticeHeight(notice);
                    var want = new Rectangle(notice.Left, y, notice.Width, h);
                    if (notice.Bounds != want) { moves.Add(new KeyValuePair<Label, Rectangle>(notice, want)); }
                    y += h + gap;
                }
                int delta = (y - gap + L(10)) - _keybindsHeaderLabel.Top;
                if (moves.Count == 0 && delta == 0) { return; }

                // The control being edited, if it is in the table: it stays where it is on screen.
                Control focused = ActiveControl;
                bool onScreen = page.Visible && page.IsHandleCreated;
                bool anchor = delta != 0 && onScreen && focused != null && page.ContainsFocus &&
                              _keybindTable.Contains(focused);

                // Page centring (CenterPageContent) puts every control back at the place it noted
                // when the window was built — on each tab switch, resize and scrollbar change — and
                // that undid the stack: the rows went back up under the notices. So the new places
                // become the noted ones, BEFORE anything moves, because a move can itself toggle the
                // scrollbar and set the centring off in the middle of this.
                // Noted relative to the Save button's own noted place, so neither the scroll nor
                // full-screen centring's offset has to be read: both move the button with the rest.
                if (_autoFitBaseTop.TryGetValue(_keybindsSaveBtn, out int saveBase))
                {
                    int rel = saveBase - _keybindsSaveBtn.Top;
                    foreach (var move in moves)
                    {
                        if (_autoFitBaseTop.ContainsKey(move.Key)) { _autoFitBaseTop[move.Key] = move.Value.Y + rel; }
                    }
                    if (delta != 0)
                    {
                        foreach (Control c in _keybindTable)
                        {
                            if (_autoFitBaseTop.ContainsKey(c)) { _autoFitBaseTop[c] = c.Top + delta + rel; }
                        }
                    }
                }

                // Frozen only while on screen. WM_SETREDRAW(TRUE) makes its window VISIBLE, so a
                // freeze-and-thaw of a page that is not the selected tab would put it up over the
                // one that is.
                if (onScreen) { SetRedraw(page, false); }
                page.SuspendLayout();
                try
                {
                    foreach (var move in moves) { move.Key.Bounds = move.Value; }
                    if (delta != 0)
                    {
                        foreach (Control c in _keybindTable) { c.Top += delta; }
                    }
                }
                finally
                {
                    page.ResumeLayout(true);
                }
                if (anchor)
                {
                    Point at = page.AutoScrollPosition;   // negative offsets; set as positive ones
                    int scroll = -at.Y + delta;
                    // ...but not by scrolling the Save row out of view while it is in view: "Unsaved
                    // changes — click Save Keybinds" lives there. The help text above it gives way
                    // first; past that the rows move down instead.
                    if (delta > 0 && _keybindsSaveBtn.Top >= 0)
                    {
                        int saveRow = _keybindsSaveBtn.Top - at.Y;   // its place on the unscrolled page
                        scroll = Math.Min(scroll, Math.Max(-at.Y, saveRow - L(6)));
                    }
                    page.AutoScrollPosition = new Point(-at.X, Math.Max(0, scroll));
                }
                if (onScreen)
                {
                    SetRedraw(page, true);
                    page.Invalidate(true);
                }
            }
            catch (Exception ex) { Utils.Logger.Swallow("RelayoutKeybindNotices", ex); }
            finally { _keybindRelayoutBusy = false; }
        }

        /// <summary>
        /// A notice's height: its text wrapped at its width, at most eight lines (the warning can
        /// carry four kinds of trouble, and French needs six lines for three). Past that it ends in
        /// "…" and the whole text is on hover.
        /// </summary>
        private int KeybindNoticeHeight(Label notice)
        {
            if (ReferenceEquals(notice, _keyboardInfoLabel)) { return notice.Height; }   // one line, fixed
            TextFormatFlags flags = notice.UseMnemonic ? TextFormatFlags.Default : TextFormatFlags.NoPrefix;
            int width = Math.Max(1, notice.Width - notice.Padding.Horizontal);
            int line = MeasureLayout("Ag", notice.Font, 0, flags).Height;
            int text = MeasureLayout(notice.Text, notice.Font, width, flags).Height;
            return Math.Max(line, Math.Min(text, line * 8)) + notice.Padding.Vertical;
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

            if (_extraShiftCtrlCheck != null)
            {
                _extraShiftCtrlCheck.Checked = _settings.HotkeysIgnoreExtraShiftCtrl;   // events suppressed above
            }

            // "X was already used by Y" described an edit these fields no longer hold (a reset, a
            // reload). It stayed up after Reset to defaults, naming bindings that were gone.
            SetKeybindNotice(_keybindsTakenLabel, null, restack: false);
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
                if (taken.Count > 0)
                {
                    var names = new List<string>();
                    foreach (HotkeyAction a in taken)
                    {
                        names.Add(HotkeyActions.LabelFor(a));
                    }
                    names.Sort(StringComparer.CurrentCultureIgnoreCase);

                    SetKeybindNotice(_keybindsTakenLabel, Utils.Localization.F(
                        "{0} was already used by {1} — that action is now unbound.",
                        combo.ToDisplayString(), string.Join(", ", names.ToArray())), restack: false);
                }
                else
                {
                    SetKeybindNotice(_keybindsTakenLabel, null, restack: false);
                }
            }

            HighlightConflicts();   // re-stacks the notices
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
            var common = new List<string>();     // Ctrl+C, Ctrl+V, Alt+Enter…: taken from every program
            var typed = new List<string>();      // Ctrl+Alt+Q = AltGr+Q = "@" on this layout
            var ownKeys = new List<string>();    // F11, Ctrl+1…9, Ctrl+Tab: Tempo's own window shortcuts

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

                // A MODIFIED combination counted as safe, full stop — and the warning for bare
                // keys even says "add Ctrl/Alt/Shift". Three kinds are not safe at all.
                string altGr = valid && !isRisky ? KeyNames.AltGrCharacter(hk) : null;
                bool isCommon = valid && !isRisky && altGr == null && KeyNames.IsCommonShortcut(hk);
                bool isOwn = valid && !isRisky && altGr == null && !isCommon && KeyNames.IsTempoWindowShortcut(hk);

                if (!isClash)
                {
                    if (isRisky) { risky.Add(hk.ToDisplayString()); }
                    if (isCommon) { common.Add(hk.ToDisplayString()); }
                    if (isOwn) { ownKeys.Add(hk.ToDisplayString()); }
                    if (altGr != null) { typed.Add(hk.ToDisplayString() + " → " + altGr); }
                }

                // A clash is the more serious problem, so it wins the colour.
                bool amber = isRisky || isCommon || isOwn || altGr != null;
                pair.Value.BackColor =
                    isClash ? BlendColors(normal, _theme.Danger, 0.35)
                  : amber ? BlendColors(normal, _theme.Warning, 0.30)
                  : normal;
            }

            // Hold mode + a trigger that is the button being clicked = a hold that can
            // never be released, so PollHoldKey refuses it. Say why here, where the
            // binding is chosen, rather than leaving hold mode mysteriously inert.
            HotkeyDefinition holdTrigger =
                _bindingControls.TryGetValue(HotkeyAction.ToggleStartStop, out HotkeyCaptureControl tc)
                    ? tc.Hotkey : null;
            bool fights = holdTrigger != null && holdTrigger.IsValid &&
                          HoldTriggerFightsClickButton(holdTrigger);
            SetKeybindNotice(_keybindsHoldLabel, !fights ? null : Utils.Localization.F(
                "⚠ {0} is also the button the clicker clicks. In Hold-to-click mode Tempo's own "
                + "clicks would keep that button reading as held, so the hold could never be "
                + "released — it will not engage. Pick a different button, or a side button.",
                holdTrigger.ToDisplayString()), restack: false);

            // The keyboard half of the same idea: a key Tempo sends that is bound here.
            UpdateSelfPressedKeyNotice();

            // One warning, a sentence per kind of trouble, worst first.
            var sentences = new List<string>();
            if (risky.Count > 0)
            {
                risky.Sort(StringComparer.OrdinalIgnoreCase);
                string bare = string.Join(", ", risky.ToArray());
                sentences.Add(risky.Count == 1
                    ? Utils.Localization.F(
                        "⚠ {0} is bound bare. A global hotkey is taken system-wide, so that key "
                        + "will stop working in every other program while Tempo is running. "
                        + "Add Ctrl/Alt/Shift, or use an F-key, unless that is exactly what you want.",
                        bare)
                    : Utils.Localization.F(
                        "⚠ {0} are bound bare. A global hotkey is taken system-wide, so those keys "
                        + "will stop working in every other program while Tempo is running. "
                        + "Add Ctrl/Alt/Shift, or use an F-key, unless that is exactly what you want.",
                        bare));
            }
            if (common.Count > 0)
            {
                common.Sort(StringComparer.OrdinalIgnoreCase);
                string list = string.Join(", ", common.ToArray());
                sentences.Add(common.Count == 1
                    ? Utils.Localization.F(
                        "⚠ {0} is a shortcut almost every program uses. Bound here, it stops working in all "
                        + "of them while Tempo is running.", list)
                    : Utils.Localization.F(
                        "⚠ {0} are shortcuts almost every program uses. Bound here, they stop working in all "
                        + "of them while Tempo is running.", list));
            }
            if (typed.Count > 0)
            {
                typed.Sort(StringComparer.OrdinalIgnoreCase);
                string list = string.Join(", ", typed.ToArray());
                sentences.Add(typed.Count == 1
                    ? Utils.Localization.F(
                        "⚠ {0}: on your keyboard layout Ctrl + Alt is the AltGr key, so this combination types "
                        + "a character. Bound here, that character can't be typed in any other program while "
                        + "Tempo is running.", list)
                    : Utils.Localization.F(
                        "⚠ {0}: on your keyboard layout Ctrl + Alt is the AltGr key, so these combinations type "
                        + "characters. Bound here, those characters can't be typed in any other program while "
                        + "Tempo is running.", list));
            }
            if (ownKeys.Count > 0)
            {
                ownKeys.Sort(StringComparer.OrdinalIgnoreCase);
                string list = string.Join(", ", ownKeys.ToArray());
                sentences.Add(ownKeys.Count == 1
                    ? Utils.Localization.F(
                        "⚠ {0} is also one of Tempo's own window shortcuts (full screen, switching tabs). "
                        + "Bound here, pressing it inside Tempo runs this hotkey instead.", list)
                    : Utils.Localization.F(
                        "⚠ {0} are also Tempo's own window shortcuts (full screen, switching tabs). "
                        + "Bound here, pressing them inside Tempo runs these hotkeys instead.", list));
            }
            SetKeybindNotice(_keybindsWarnLabel,
                sentences.Count == 0 ? null : string.Join("\r\n", sentences.ToArray()), restack: false);

            RelayoutKeybindNotices();
        }

        /// <summary>
        /// Which action a key press with these modifiers would fire, or null. Reads the
        /// CONTROLS, so it follows unsaved edits like the rest of this page.
        /// </summary>
        private HotkeyAction? ActionFiredBy(int vk, bool ctrl, bool alt, bool shift, bool win)
        {
            HotkeyAction? loose = null;
            int looseRank = -1;
            bool extra = _settings != null && _settings.HotkeysIgnoreExtraShiftCtrl;
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
                // ...except the extra Shift/Ctrl the hotkeys themselves now accept: a macro holding
                // Shift while it presses F6 fires an F6 hotkey. Same rule, same priority (exact
                // first, then the most specific) as GlobalHotkeyManager.
                if (extra && hk.ToleratesExtraShiftCtrl() && (ctrl || !hk.Control) && (shift || !hk.Shift) &&
                    hk.Alt == alt && hk.Win == win && hk.ModifierCount() > looseRank)
                {
                    loose = pair.Key;
                    looseRank = hk.ModifierCount();
                }
            }
            return loose;
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
            UpdateSelfPressedKeyNotice();
            RelayoutKeybindNotices();
        }

        /// <summary>The notice's text and visibility only; the caller re-stacks.</summary>
        private void UpdateSelfPressedKeyNotice()
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
                SetKeybindNotice(_keybindsSelfKeyLabel, null, restack: false);
                return;
            }

            hits.Sort(StringComparer.CurrentCultureIgnoreCase);
            // Capped, rather than letting a macro-heavy setup push the half that says what
            // to do about it past the notice's five lines.
            string list = hits.Count <= 3
                ? string.Join("; ", hits.ToArray())
                : string.Join("; ", hits.GetRange(0, 3).ToArray()) + "; "
                  + Utils.Localization.F("and {0} more", hits.Count - 3);

            SetKeybindNotice(_keybindsSelfKeyLabel, hits.Count == 1
                ? Utils.Localization.F(
                    "⚠ {0} — Tempo presses this key itself, and Windows can't tell its keystroke from "
                    + "yours, so every press runs that action too. Change the key or the binding.", list)
                : Utils.Localization.F(
                    "⚠ {0} — Tempo presses these keys itself, and Windows can't tell its keystrokes "
                    + "from yours, so every press runs that action too. Change the keys or the bindings.",
                    list), restack: false);
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
                // "Only one responds", which is true again. This said a single press would run
                // BOTH actions — true once, when the second action fell back to the keyboard hook.
                // GlobalHotkeyManager now leaves the second one unbound on every route (reserved,
                // hook, mouse) and reports it, so the warning described the opposite of what happens.
                ShowWarning(
                    Utils.Localization.F("Keybinds saved, but some combinations are set for more than one "
                    + "action. Only one of them will respond to the key — give the others a different one:"
                    + "\n\n{0}", conflicts));
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
                Clipboard.SetText(KeybindsAsText());
                ConfirmOnButton(_copyKeybindsBtn, Utils.Localization.T("Copied  ✓"));
            }
            catch (Exception ex)
            {
                Utils.Logger.Swallow("OnCopyKeybinds", ex);
                ShowWarning(Utils.Localization.T("Could not copy the keybinds to the clipboard."));
            }
        }

        /// <summary>The text Copy keybinds puts on the clipboard (kept apart so it can be checked without it).</summary>
        private string KeybindsAsText()
        {
            var free = new System.Text.StringBuilder();
            var rows = new List<KeyValuePair<string, string>>();   // label, what is bound (and played)
            int total = 0;

            foreach (HotkeyActions.Info info in HotkeyActions.All)
            {
                total++;
                string label = Utils.Localization.T(info.Label);
                HotkeyDefinition hk = _bindingControls.TryGetValue(info.Action, out var ctl)
                    ? ctl.Hotkey
                    : null;

                if (hk != null && hk.IsValid)
                {
                    string bound = hk.ToDisplayString();
                    // "Play macro #1  F10" left the one question this page answers — which macro? —
                    // out of the copy. Its picker says, so the copy says too.
                    int slot = SlotForAction(info.Action);
                    if (slot > 0)
                    {
                        string picked = _macroSlotCombos.TryGetValue(slot, out ComboBox pick) && pick != null &&
                                        pick.SelectedItem != null
                            ? pick.SelectedItem.ToString()
                            : QuickPlayFallbackText(slot);
                        bound += "  →  " + picked;
                    }
                    rows.Add(new KeyValuePair<string, string>(label, bound));
                }
                else
                {
                    free.Append("  ").Append(label).AppendLine();
                }
            }

            // One column for the keys, as wide as the longest label in THIS language. A fixed 28
            // fitted English, and the German and French labels ran past it, so their keys no
            // longer lined up.
            int width = 0;
            foreach (var row in rows) { width = Math.Max(width, row.Key.Length); }
            width = Math.Min(width, 44) + 3;

            var sb = new System.Text.StringBuilder();
            sb.Append("Tempo ").Append(Utils.AppVersion.Stamp)
              .Append("  ·  ").Append(Utils.Localization.T("Keybinds")).AppendLine();
            sb.AppendLine(Utils.Localization.F("{0} of {1} actions bound", rows.Count, total));
            sb.AppendLine();
            foreach (var row in rows)
            {
                // Past the cap a label keeps a gap of its own rather than running into its key.
                sb.Append("  ").Append(row.Key.Length < width ? row.Key.PadRight(width) : row.Key + "   ")
                  .Append(row.Value).AppendLine();
            }
            // The two settings on this page that change what a key press does.
            if (_extraShiftCtrlCheck != null)
            {
                sb.AppendLine();
                sb.Append("  ").Append(_extraShiftCtrlCheck.Checked ? "☑ " : "☐ ")
                  .AppendLine(Utils.Localization.T("Hotkeys also work while you hold Shift or Ctrl"));
            }
            if (_intervalStepNum != null)
            {
                sb.Append("  ").Append(Utils.Localization.T("Interval step (ms):")).Append(' ')
                  .Append(((int)_intervalStepNum.Value).ToString(System.Globalization.CultureInfo.CurrentCulture)).AppendLine();
            }
            if (free.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Utils.Localization.T("Not bound:"));
                sb.Append(free);
            }

            return sb.ToString();
        }

        private void OnResetKeybinds(object sender, EventArgs e)
        {
            // Translated: the text was in all five tables and never looked up, so this asked in
            // English in every language.
            var confirm = MessageBox.Show(this,
                Utils.Localization.T("Reset all keybinds to their defaults?"),
                "Tempo",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
            {
                return;
            }

            _settings.Bindings = HotkeyActions.DefaultBindings();
            _settings.IntervalStepMilliseconds = 10;
            _settings.HotkeysIgnoreExtraShiftCtrl = true;
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
