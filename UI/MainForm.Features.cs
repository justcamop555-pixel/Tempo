using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// Settings → Disable features (2026-11-08, the user: "add feature for disable feature for every tab only; Clicker,
    /// Macros, Multi-Point, and Caption … it would not accidentally press exactly shortcut game and tempo conflict!").
    ///
    /// A feature that is off:
    ///  - lets go of its hotkeys: they are not registered, hooked, watched or polled (ApplyHotkeysFromSettings), so
    ///    Windows hands those keys to the game; one that arrives anyway is ignored (DispatchAction);
    ///  - stops if it is running, and refuses to start from anywhere: its own start functions ask (StartEngine,
    ///    StartRecording, PlayMacro, SetCaptionsActive), so a button, the tray, hold mode, auto-start and the resume after
    ///    a restart all get the same answer;
    ///  - drops out of the tray menu, and its tab is covered by one card that says so and turns it back on
    ///    (FeatureOffCover); its sidebar row says "Off".
    /// Multi-Point runs are the clicker's, so turning the Clicker off stops them too. Emergency stop belongs to no one
    /// feature and is let go only when nothing is left for it to stop: the Clicker and Macros both off.
    /// </summary>
    public partial class MainForm
    {
        internal enum TempoFeature { Clicker, Macros, MultiPoint, Captions }

        internal static readonly TempoFeature[] AllFeatures =
            { TempoFeature.Clicker, TempoFeature.Macros, TempoFeature.MultiPoint, TempoFeature.Captions };

        private readonly Dictionary<TempoFeature, FeatureOffCover> _featureCovers = new Dictionary<TempoFeature, FeatureOffCover>();

        /// <summary>The page each cover turned scrolling off on, so it is turned back on there — a tab's controls can
        /// move to a window of their own and back while the feature is off.</summary>
        private readonly Dictionary<TempoFeature, ScrollableControl> _featureScrollLocks = new Dictionary<TempoFeature, ScrollableControl>();

        private readonly Dictionary<TempoFeature, CheckBox> _featureChecks = new Dictionary<TempoFeature, CheckBox>();
        private readonly Dictionary<TempoFeature, Label> _featureKeyLabels = new Dictionary<TempoFeature, Label>();
        private GroupBox _featuresCard;
        private bool _updatingFeatureChecks;
        private readonly Dictionary<TempoFeature, long> _featureRefusedTicks = new Dictionary<TempoFeature, long>();

        internal bool FeatureOff(TempoFeature feature)
        {
            if (_settings == null) { return false; }
            switch (feature)
            {
                case TempoFeature.Clicker: return _settings.ClickerDisabled;
                case TempoFeature.Macros: return _settings.MacrosDisabled;
                case TempoFeature.MultiPoint: return _settings.MultiPointDisabled;
                case TempoFeature.Captions: return _settings.CaptionsDisabled;
                default: return false;
            }
        }

        private bool ClickerOff => FeatureOff(TempoFeature.Clicker);
        private bool MacrosOff => FeatureOff(TempoFeature.Macros);
        private bool MultiPointOff => FeatureOff(TempoFeature.MultiPoint);
        private bool CaptionsOff => FeatureOff(TempoFeature.Captions);

        private void StoreFeatureOff(TempoFeature feature, bool off)
        {
            switch (feature)
            {
                case TempoFeature.Clicker: _settings.ClickerDisabled = off; break;
                case TempoFeature.Macros: _settings.MacrosDisabled = off; break;
                case TempoFeature.MultiPoint: _settings.MultiPointDisabled = off; break;
                case TempoFeature.Captions: _settings.CaptionsDisabled = off; break;
            }
        }

        /// <summary>The feature a hotkey action belongs to; null for those of no one feature (show/hide, always on top,
        /// the profiles, camera movement — and emergency stop, see <see cref="HotkeyLetGo"/>).</summary>
        internal static TempoFeature? FeatureOf(HotkeyAction action)
        {
            switch (action)
            {
                case HotkeyAction.ToggleStartStop:
                case HotkeyAction.StartClicking:
                case HotkeyAction.StopClicking:
                case HotkeyAction.TogglePause:
                case HotkeyAction.PickPosition:
                case HotkeyAction.IncreaseInterval:
                case HotkeyAction.DecreaseInterval:
                case HotkeyAction.ToggleAntiFreeze:
                case HotkeyAction.GrabSecondCursor:
                case HotkeyAction.ToggleSecondCursorSpam:
                    return TempoFeature.Clicker;
                case HotkeyAction.ToggleRecordMacro:
                case HotkeyAction.PlayMacro:
                case HotkeyAction.StopMacro:
                case HotkeyAction.PlayMacro1:
                case HotkeyAction.PlayMacro2:
                case HotkeyAction.PlayMacro3:
                    return TempoFeature.Macros;
                case HotkeyAction.ShowPointsOverlay:
                case HotkeyAction.AddPointAtCursor:
                    return TempoFeature.MultiPoint;
                case HotkeyAction.ToggleLiveCaptions:
                    return TempoFeature.Captions;
                default:
                    return null;
            }
        }

        /// <summary>
        /// True for a hotkey a turned-off feature has let go of: never registered, hooked, watched or polled, and ignored
        /// should it arrive anyway. Emergency stop goes only when there is nothing left for it to stop — the Clicker and
        /// Macros both off (camera movement, which it also disarms, has its own toggle key).
        /// </summary>
        private bool HotkeyLetGo(HotkeyAction action)
        {
            if (_settings == null) { return false; }
            if (action == HotkeyAction.EmergencyStop) { return ClickerOff && MacrosOff; }
            TempoFeature? feature = FeatureOf(action);
            return feature.HasValue && FeatureOff(feature.Value);
        }

        /// <summary>The feature's name, as its tab says it.</summary>
        private static string FeatureName(TempoFeature feature) => Localization.T(FeatureEnglishName(feature));

        /// <summary>The same in English — the key the translation is looked up by (UiFactory translates its own text).</summary>
        private static string FeatureEnglishName(TempoFeature feature)
        {
            switch (feature)
            {
                case TempoFeature.Clicker: return "Clicker";
                case TempoFeature.Macros: return "Macros";
                case TempoFeature.MultiPoint: return "Multi-Point";
                default: return "Live Captions";
            }
        }

        /// <summary>The feature a tab is for (its sidebar row can turn it off); null for the other tabs.</summary>
        private static TempoFeature? FeatureForTab(TabPage tab)
        {
            if (tab == null) { return null; }
            foreach (TempoFeature f in AllFeatures)
            {
                if (string.Equals(tab.Name, FeatureKey(f), StringComparison.Ordinal)) { return f; }
            }
            return null;
        }

        private static string FeatureKey(TempoFeature feature)
        {
            switch (feature)
            {
                case TempoFeature.Clicker: return "clicker";
                case TempoFeature.Macros: return "macros";
                case TempoFeature.MultiPoint: return "multipoint";
                default: return "captions";
            }
        }

        /// <summary>The keys bound to the feature's own actions, as they are shown ("F6, F9, F7"); null when none is set.</summary>
        private string FeatureKeysText(TempoFeature feature)
        {
            if (_settings?.Bindings == null) { return null; }
            var keys = new List<string>();
            foreach (HotkeyBinding b in _settings.Bindings)
            {
                if (b?.Hotkey == null || !b.Hotkey.IsValid || FeatureOf(b.Action) != feature) { continue; }
                if (HotkeyActions.Get(b.Action) == null) { continue; }   // an action this build doesn't have
                string key = b.Hotkey.ToDisplayString().Replace(" + ", "+");
                if (!keys.Contains(key)) { keys.Add(key); }
            }
            return keys.Count == 0 ? null : string.Join(", ", keys);
        }

        /// <summary>
        /// For the Keybinds tab: each turned-off feature with the keys it let go of ("Clicker (F6, F9, F7), Live Captions
        /// (End)"), Emergency stop's too when it went with them; null when no key is let go.
        /// </summary>
        private string FeatureLetGoKeysText()
        {
            if (_settings == null) { return null; }
            var parts = new List<string>();
            foreach (TempoFeature f in AllFeatures)
            {
                if (!FeatureOff(f)) { continue; }
                string keys = FeatureKeysText(f);
                if (keys != null) { parts.Add(FeatureName(f) + " (" + keys + ")"); }
            }
            if (HotkeyLetGo(HotkeyAction.EmergencyStop))
            {
                HotkeyDefinition stop = _settings.HotkeyFor(HotkeyAction.EmergencyStop);
                if (stop != null && stop.IsValid)
                {
                    parts.Add(HotkeyActions.LabelFor(HotkeyAction.EmergencyStop) + " (" + stop.ToDisplayString().Replace(" + ", "+") + ")");
                }
            }
            return parts.Count == 0 ? null : string.Join(", ", parts);
        }

        // ── Turning one off or on ──────────────────────────────────────────────────────────────

        /// <summary>Turns a feature off or back on: saved at once, stopped if it was running, its keys let go or bound
        /// again, and every place that shows it brought up to date.</summary>
        private void SetFeatureOff(TempoFeature feature, bool off, string source)
        {
            if (_settings == null) { return; }
            if (FeatureOff(feature) == off) { RefreshFeatureUi(); return; }
            StoreFeatureOff(feature, off);
            try { SaveSettingsQuietly(); } catch (Exception ex) { Logger.Swallow("SetFeatureOff/save", ex); }
            string keys = FeatureKeysText(feature);
            Logger.Info("[Features] " + feature + " turned " + (off ? "off" : "on") + " (" + source + ")"
                        + (off ? "; its hotkeys let go: " + (keys ?? "none set") : "") + ".");
            if (off) { StopFeature(feature); }
            ApplyFeatureHotkeys();
            if (feature == TempoFeature.Clicker) { ApplySecondCursorToClicker(); }
            // On the tab being looked at, its cover fades in (or out) instead of snapping (FadeChange).
            TabPage shown = _tabs?.SelectedTab;
            if (shown != null && FeatureForTab(shown) == feature && !IsWindowedTab(shown)) { FadeChange(_tabs, RefreshFeatureUi); }
            else { RefreshFeatureUi(); }
        }

        /// <summary>The second cursor follows the Clicker (ApplySecondCursorSettings reads the switch) — only if there is
        /// one to follow: a controller is not made just to be told it is off.</summary>
        private void ApplySecondCursorToClicker()
        {
            if (_settings == null || (_secondCursor == null && !_settings.SecondCursorEnabled)) { return; }
            try { ApplySecondCursorSettings(); } catch (Exception ex) { Logger.Swallow("ApplySecondCursorToClicker", ex); }
        }

        /// <summary>
        /// After the settings were replaced (an import, Reset): whatever the new settings turn off stops, the second
        /// cursor follows the Clicker, and the switches, covers and rows show them. The keys are bound by the caller.
        /// </summary>
        private void ApplyFeatureStates(string source)
        {
            foreach (TempoFeature f in AllFeatures)
            {
                if (FeatureOff(f)) { StopFeature(f); }
            }
            ApplySecondCursorToClicker();
            RefreshFeatureUi();
            var off = new List<string>();
            foreach (TempoFeature f in AllFeatures) { if (FeatureOff(f)) { off.Add(f.ToString()); } }
            if (off.Count > 0) { Logger.Info("[Features] off (" + source + "): " + string.Join(", ", off) + "."); }
        }

        /// <summary>The bindings again, asleep or awake as Tempo is: the let-go keys go, the others stay.</summary>
        private void ApplyFeatureHotkeys()
        {
            try { ApplyHotkeysFromSettings(_traySleepActive); }
            catch (Exception ex) { Logger.Swallow("ApplyFeatureHotkeys", ex); }
        }

        /// <summary>Stops whatever of the feature is running, as its own stop would.</summary>
        private void StopFeature(TempoFeature feature)
        {
            try
            {
                switch (feature)
                {
                    case TempoFeature.Clicker:
                        _holdActive = false;
                        _activeCountdown?.CancelCountdown();
                        StopClickerBecause("the Clicker was turned off in Settings");
                        try { _secondCursor?.StopSpam(); } catch { }
                        break;
                    case TempoFeature.MultiPoint:
                        // Only a run in Multi-Point mode is this feature's; the Clicker's other modes carry on.
                        if (_engine != null && _engine.IsRunning && UiPositionModeIsMultiPoint())
                        {
                            StopClickerBecause("Multi-Point was turned off in Settings");
                        }
                        break;
                    case TempoFeature.Macros:
                        CancelMacroCountdownFor(HotkeyAction.StopMacro);
                        if (_player != null && _player.IsPlaying) { _player.Stop(); }
                        if (_recorder != null && _recorder.IsRecording) { StopRecording(); }
                        break;
                    case TempoFeature.Captions:
                        if (_captionMoveMode) { ToggleCaptionMoveMode(); }
                        if (_captionsActive && !_captionToggleBusy)
                        {
                            _captionToggleBusy = true;
                            try { SetCaptionsActive(false); }
                            finally { _captionToggleBusy = false; }
                        }
                        if (IsUp(_captionHistoryForm)) { ToggleCaptionHistoryWindow(); }
                        break;
                }
            }
            catch (Exception ex) { Logger.Swallow("StopFeature " + feature, ex); }
        }

        /// <summary>The Clicker tab's position mode is Multi-Point.</summary>
        private bool UiPositionModeIsMultiPoint()
        {
            try { return BuildProfileFromUi().PositionMode == PositionMode.MultiPoint; }
            catch { return false; }
        }

        /// <summary>
        /// True — and says so — when a feature that is off was asked to start. Its start functions call this, so every
        /// way in (a button, a hotkey that got through, the tray, hold mode, auto-start) meets the same answer. Said at
        /// most once in a few seconds per feature: hold mode asks on every poll while its key is down.
        /// </summary>
        private bool RefuseIfOff(TempoFeature feature)
        {
            if (!FeatureOff(feature)) { return false; }
            long now = Environment.TickCount64;
            if (_featureRefusedTicks.TryGetValue(feature, out long last) && now - last < 5000) { return true; }
            _featureRefusedTicks[feature] = now;
            Logger.Info("[Features] " + feature + " is turned off: start refused.");
            try
            {
                TempoNotify(6000, Localization.F("{0} is turned off", FeatureName(feature)),
                            Localization.T("Turn it back on in Settings, under Disable features."),
                            ToolTipIcon.Info, always: true, onClick: ShowFeaturesCard);
            }
            catch (Exception ex) { Logger.Swallow("RefuseIfOff", ex); }
            return true;
        }

        /// <summary>The Clicker refuses a run in Multi-Point mode while Multi-Point is off (its other modes still run).</summary>
        private bool RefuseMultiPointRun(ClickProfile profile)
        {
            return profile != null && profile.PositionMode == PositionMode.MultiPoint && RefuseIfOff(TempoFeature.MultiPoint);
        }

        /// <summary>Settings, scrolled to the Disable features card.</summary>
        private void ShowFeaturesCard()
        {
            try
            {
                ShowTab("settings");
                if (_featuresCard != null) { ScrollSettingsTo(_featuresCard); }
            }
            catch (Exception ex) { Logger.Swallow("ShowFeaturesCard", ex); }
        }

        // ── What shows it ──────────────────────────────────────────────────────────────────────

        /// <summary>The Settings switches, the tab covers and the sidebar rows, from the settings.</summary>
        private void RefreshFeatureUi()
        {
            SyncFeatureControls();
            RefreshFeatureCovers();
            RefreshFeatureNavRows();
        }

        /// <summary>
        /// After the keys were bound again (a keybind changed, a profile switched them): the key lines on the Settings
        /// card and the words on a cover that is up name the keys as they are now. Makes no cover — this runs at start-up
        /// too, before the pages' layout is noted (OnLoad makes them).
        /// </summary>
        private void RefreshFeatureTexts()
        {
            SyncFeatureControls();
            foreach (KeyValuePair<TempoFeature, FeatureOffCover> kv in _featureCovers)
            {
                // Placed, not Visible: a cover on a tab not on show reads Visible false, and is shown later as it is now.
                if (kv.Value == null || kv.Value.IsDisposed || kv.Value.Parent == null || !FeatureOff(kv.Key)) { continue; }
                kv.Value.SetText(Localization.F("{0} is turned off", FeatureName(kv.Key)), FeatureOffBody(kv.Key),
                                 Localization.F("Turn on {0}", FeatureName(kv.Key)));
            }
        }

        /// <summary>The Settings card's switches and key lines, from the settings (a load, an import, a reset).</summary>
        private void SyncFeatureControls()
        {
            _updatingFeatureChecks = true;
            try
            {
                foreach (TempoFeature f in AllFeatures)
                {
                    if (_featureChecks.TryGetValue(f, out CheckBox box) && box != null && !box.IsDisposed && box.Checked != FeatureOff(f))
                    {
                        box.Checked = FeatureOff(f);
                    }
                    if (_featureKeyLabels.TryGetValue(f, out Label keys) && keys != null && !keys.IsDisposed)
                    {
                        string list = FeatureKeysText(f);
                        string text = list != null ? Localization.F("Its hotkeys: {0}", list) : Localization.T("No hotkeys set");
                        if (keys.Text != text) { keys.Text = text; }
                    }
                }
            }
            finally { _updatingFeatureChecks = false; }
        }

        /// <summary>
        /// Each turned-off feature's tab is covered — on the page its controls are on now, the tab or its own window —
        /// and that page stops scrolling; each feature that is on has its cover gone and its page scrolling again.
        /// </summary>
        private void RefreshFeatureCovers()
        {
            foreach (TempoFeature f in AllFeatures)
            {
                try
                {
                    TabPage tab = TabByKey(FeatureKey(f));
                    TabPage host = tab != null ? PageHost(tab) : null;
                    if (FeatureOff(f) && host != null && !host.IsDisposed) { CoverPage(f, host); }
                    else { Uncover(f); }
                }
                catch (Exception ex) { Logger.Swallow("RefreshFeatureCovers " + f, ex); }
            }
        }

        private void CoverPage(TempoFeature feature, TabPage host)
        {
            if (!_featureCovers.TryGetValue(feature, out FeatureOffCover cover) || cover.IsDisposed)
            {
                cover = new FeatureOffCover { Name = "featureOff_" + FeatureKey(feature), Dock = DockStyle.Fill, Visible = false };
                // Lies over the whole page on purpose. Undeclared, the start-up layout check logged a "STILL OVERLAPPING"
                // warning for every control on a turned-off tab (~50 lines a start), and mainscan failed on them.
                LayoutFitter.DeclareOverlay(cover, "covers a turned-off feature's tab (Settings > Disable features)");
                TempoFeature f = feature;
                cover.TurnOnClicked += (s, e) => SetFeatureOff(f, false, "its tab");
                StyleAccentButton(cover.Button);
                _featureCovers[feature] = cover;
            }
            cover.SetText(Localization.F("{0} is turned off", FeatureName(feature)), FeatureOffBody(feature),
                          Localization.F("Turn on {0}", FeatureName(feature)));
            cover.ApplyTheme(_theme);

            // Scrolling off on the page it covers (and back on wherever it was covering before): a page that scrolls
            // carries its children — the cover with them — and shows what is under it.
            if (_featureScrollLocks.TryGetValue(feature, out ScrollableControl locked) && !ReferenceEquals(locked, host))
            {
                ReleaseScrollLock(feature);
            }
            if (!_featureScrollLocks.ContainsKey(feature))
            {
                bool wasFocusedInside = host.ContainsFocus;
                if (host is BackdropTabPage backdrop)
                {
                    // Held off for as long as the cover is up, whatever asks the page to scroll meanwhile (see
                    // BackdropTabPage.ScrollLocked) — not merely switched off once.
                    backdrop.ScrollLocked = true;
                    _featureScrollLocks[feature] = host;
                }
                else if (host.AutoScroll)
                {
                    host.AutoScrollPosition = Point.Empty;
                    host.AutoScroll = false;
                    _featureScrollLocks[feature] = host;
                }
                if (!ReferenceEquals(cover.Parent, host)) { host.Controls.Add(cover); }
                cover.Covering = true;
                cover.Visible = true;
                cover.BringToFront();
                // A control under the cover that had the focus would still take keys: the cover's button has it now.
                if (wasFocusedInside) { cover.Button.Focus(); }
                return;
            }
            if (!ReferenceEquals(cover.Parent, host)) { host.Controls.Add(cover); }
            cover.Covering = true;
            if (!cover.Visible) { cover.Visible = true; }
            cover.BringToFront();
            // A page whose scrolling was turned back on behind the lock's back (it is a plain ScrollableControl, or the
            // lock came off): held off again here, at every refresh.
            if (host is BackdropTabPage held && !held.ScrollLocked) { held.ScrollLocked = true; }
        }

        /// <summary>
        /// Every cover laid out again: what lies over the pages that they keep clear of (the safety note card,
        /// FeatureOffCover.KeepClearOf) came, went, moved or changed size.
        /// </summary>
        private void RelayoutFeatureCovers()
        {
            foreach (FeatureOffCover cover in _featureCovers.Values)
            {
                if (cover == null || cover.IsDisposed || cover.Parent == null) { continue; }
                try
                {
                    cover.PerformLayout();
                    cover.Invalidate();
                }
                catch (Exception ex) { Logger.Swallow("RelayoutFeatureCovers", ex); }
            }
        }

        private void Uncover(TempoFeature feature)
        {
            if (_featureCovers.TryGetValue(feature, out FeatureOffCover cover) && !cover.IsDisposed && cover.Parent != null)
            {
                cover.Covering = false;
                cover.Visible = false;
                // Off the page too: a hidden page-sized control would still be there for the layout checks, which
                // measure what a page holds. A moment later — this can run inside the cover's own button's click.
                FeatureOffCover leaving = cover;
                try
                {
                    if (IsHandleCreated)
                    {
                        // Unless the feature went off again meanwhile (its cover is wanted after all) — asked of the
                        // setting, not of Visible, which reads false for a cover on a tab not on show.
                        BeginInvoke((Action)(() =>
                        {
                            if (!leaving.IsDisposed && !FeatureOff(feature)) { leaving.Parent?.Controls.Remove(leaving); }
                        }));
                    }
                    else { leaving.Parent?.Controls.Remove(leaving); }
                }
                catch (Exception ex) { Logger.Swallow("Uncover", ex); }
            }
            ReleaseScrollLock(feature);
        }

        /// <summary>The page the cover was on scrolls again, laid out afresh (its scroll range was set aside).</summary>
        private void ReleaseScrollLock(TempoFeature feature)
        {
            if (!_featureScrollLocks.TryGetValue(feature, out ScrollableControl page)) { return; }
            _featureScrollLocks.Remove(feature);
            if (page == null || page.IsDisposed) { return; }
            if (page is BackdropTabPage backdrop && backdrop.ScrollLocked)
            {
                backdrop.ScrollLocked = false;
                // What it was before the cover — every feature page scrolls; asked for again in case the lock kept a
                // "no" that something set meanwhile.
                if (!backdrop.AutoScroll) { backdrop.AutoScroll = true; }
            }
            else { page.AutoScroll = true; }
            if (page is TabPage tp)
            {
                _lastCenteredWidth.Remove(tp);
                CenterPageContent(tp, true);
            }
        }

        /// <summary>The words under a cover's title: what being off means for this feature.</summary>
        private string FeatureOffBody(TempoFeature feature)
        {
            string keys = FeatureKeysText(feature);
            string what;
            switch (feature)
            {
                case TempoFeature.Clicker:
                    what = Localization.T("Nothing clicks: the Start button, hold mode, the second cursor and Multi-Point runs are off.");
                    break;
                case TempoFeature.Macros:
                    what = Localization.T("Nothing records or plays a macro.");
                    break;
                case TempoFeature.MultiPoint:
                    what = Localization.T("The Clicker won't run in Multi-Point mode; its other modes still work.");
                    break;
                default:
                    what = Localization.T("Captions don't start, not even by themselves.");
                    break;
            }
            string released = keys != null
                ? Localization.F("Its hotkeys ({0}) are let go, so a game can use those keys.", keys)
                : Localization.T("It has no hotkeys set.");
            return what + " " + released;
        }

        /// <summary>The sidebar row of each turned-off feature's tab says "Off".</summary>
        private void RefreshFeatureNavRows()
        {
            if (_tabs == null || _navButtons == null) { return; }
            for (int i = 0; i < _navButtons.Count && i < _tabs.TabPages.Count; i++)
            {
                RoundedButton nav = _navButtons[i];
                if (nav == null || nav.IsDisposed) { continue; }
                string note = null;
                foreach (TempoFeature f in AllFeatures)
                {
                    if (FeatureOff(f) && string.Equals(_tabs.TabPages[i].Name, FeatureKey(f), StringComparison.Ordinal))
                    {
                        note = Localization.T("Off");
                    }
                }
                if (!string.Equals(nav.TrailingNote, note, StringComparison.Ordinal))
                {
                    nav.TrailingNote = note;
                    nav.Invalidate();
                }
            }
        }

        // ── The Settings card ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Settings → Disable features: one switch per feature, each with the keys it lets go of beside it. At
        /// <paramref name="y"/> (100% numbers; the stack places it, the value only sets its order).
        /// </summary>
        private GroupBox BuildDisableFeaturesCard(int y)
        {
            GroupBox card = UiFactory.Group(Localization.T("Disable features"), 12, y, 696, 200, CardIcon.Keyboard);
            Label intro = UiFactory.Caption(
                "Turn off a feature you don't use. It stops, can't be started, and lets go of its hotkeys — so the same key "
                + "pressed in a game never reaches Tempo. Its tab shows that it's off.", 16, 30);
            intro.ForeColor = _theme.TextMuted;
            intro.AutoSize = false;
            intro.Width = 664;
            intro.Height = Math.Max(18, MeasureLayout(intro.Text, intro.Font, intro.Width, TextFormatFlags.WordBreak).Height);
            card.Controls.Add(intro);

            int row = intro.Bottom + 8;
            foreach (TempoFeature f in AllFeatures)
            {
                TempoFeature feature = f;
                CheckBox box = UiFactory.Check(FeatureEnglishName(f), 16, row);
                box.AutoSize = true;
                box.Checked = FeatureOff(f);
                box.CheckedChanged += (s, e) =>
                {
                    if (_updatingFeatureChecks || _suppressSettingsEvents) { return; }
                    SetFeatureOff(feature, box.Checked, "Settings");
                };
                box.AccessibleName = Localization.F("Disable {0}", FeatureName(f));
                card.Controls.Add(box);
                _featureChecks[f] = box;

                Label keys = UiFactory.Caption("", 200, row + 3);
                keys.AutoSize = false;
                keys.Width = 480;
                keys.Height = 18;
                keys.AutoEllipsis = true;
                keys.UseMnemonic = false;
                keys.ForeColor = _theme.TextMuted;
                card.Controls.Add(keys);
                _featureKeyLabels[f] = keys;
                row += 28;
            }
            // The key lines start after the longest name, however long it is in this language ("Sottotitoli in tempo
            // reale" ends past a fixed 200).
            int keysLeft = 200;
            foreach (CheckBox box in _featureChecks.Values) { keysLeft = Math.Max(keysLeft, LayoutRight(box) + 16); }
            foreach (Label keys in _featureKeyLabels.Values)
            {
                keys.Left = keysLeft;
                keys.Width = Math.Max(160, 680 - keysLeft);
            }
            card.Height = row + 6;
            _featuresCard = card;
            SyncFeatureControls();
            return card;
        }

        /// <summary>What each switch turns off — in its tooltip, which the Settings search reads too.</summary>
        private void SetupFeatureTips()
        {
            if (_tips == null) { return; }
            foreach (TempoFeature f in AllFeatures)
            {
                if (!_featureChecks.TryGetValue(f, out CheckBox box) || box == null) { continue; }
                string tip;
                switch (f)
                {
                    case TempoFeature.Clicker:
                        tip = Localization.T("Turns off clicking: the Start button, hold mode, the second cursor and Multi-Point runs "
                            + "(they are the clicker's). Lets go of its hotkeys: Start / Stop, Pause, Pick position, the interval "
                            + "keys, Anti-Freeze and the second cursor's. Emergency stop is let go too once Macros are off as well.");
                        break;
                    case TempoFeature.Macros:
                        tip = Localization.T("Turns off recording and playing macros. Lets go of the macro hotkeys: Record, Play, Stop "
                            + "and the three quick-play keys. Emergency stop is let go too once the Clicker is off as well.");
                        break;
                    case TempoFeature.MultiPoint:
                        tip = Localization.T("Turns off runs in Multi-Point mode — the Clicker still clicks in its other modes. Lets go "
                            + "of Show points on screen and Add point at cursor.");
                        break;
                    default:
                        tip = Localization.T("Turns off Live Captions, their automatic start and the caption windows. Lets go of the "
                            + "Toggle Live Captions key.");
                        break;
                }
                TipStyle.Set(_tips, box, tip);
                box.AccessibleDescription = tip;
            }
        }
    }
}
