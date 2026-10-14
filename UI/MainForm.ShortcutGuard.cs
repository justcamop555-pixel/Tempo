using System.Windows.Forms;
using AutoClicker.Native;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// Settings ▸ Behaviour ▸ "Hold back Windows shortcuts while clicking or running a macro". While the
    /// clicker runs or a macro plays or records, only Tempo's keybinds act: the Windows key and Win+…,
    /// Ctrl+Esc and the Sticky Keys / Filter Keys questions are held back (Native.ShortcutGuard), so a
    /// stray press cannot open Start over the game and take the focus the run's clicks and keys were
    /// going to.
    ///
    /// Not while the clicker is paused (the user stopped it to do something else), and not while it
    /// clicks in the background ("Click without moving my cursor"): the real mouse and keyboard are the
    /// user's the whole time then.
    /// </summary>
    public partial class MainForm
    {
        private ShortcutGuard _shortcutGuard;

        /// <summary>The "held back" notice went out this session — once explains it.</summary>
        private bool _shortcutGuardNoticeShown;

        // The same switch where a run is started and where it is stopped: the Clicker and Macros tabs,
        // and the tray (mid-run, the window is usually in it). Settings ▸ Behaviour has the fourth.
        private CheckBox _clickerGuardCheck;
        private CheckBox _macroGuardCheck;
        private ToolStripMenuItem _trayGuardShortcutsItem;

        /// <summary>
        /// The one way the switch is turned, wherever it was clicked: saved at once, every copy of it
        /// shown the same, and a run already going follows it straight away.
        /// </summary>
        private void SetGuardWindowsShortcuts(bool on, string from)
        {
            if (_settings == null) { return; }
            if (_settings.GuardWindowsShortcuts != on)
            {
                _settings.GuardWindowsShortcuts = on;
                PersistNotificationSettings();   // the shared "save settings now" helper
                Logger.Info("[Guard] Windows shortcuts " + (on ? "held back" : "left working") +
                            " during runs from now on (switched on the " + from + ").");
            }
            // A take already being recorded follows too: with the guard off the shortcuts reach
            // Windows, so they are recorded like any other keys.
            if (_recorder != null) { _recorder.LeaveOutWindowsShortcuts = on; }
            SyncGuardShortcutControls();
            UpdateShortcutGuard();
        }

        /// <summary>Shows the setting on every copy of the switch, without any of them firing.</summary>
        private void SyncGuardShortcutControls()
        {
            bool on = _settings != null && _settings.GuardWindowsShortcuts;
            bool was = _suppressSettingsEvents;
            _suppressSettingsEvents = true;
            try
            {
                if (_guardShortcutsCheck != null && _guardShortcutsCheck.Checked != on) { _guardShortcutsCheck.Checked = on; }
                if (_clickerGuardCheck != null && _clickerGuardCheck.Checked != on) { _clickerGuardCheck.Checked = on; }
                if (_macroGuardCheck != null && _macroGuardCheck.Checked != on) { _macroGuardCheck.Checked = on; }
                if (_trayGuardShortcutsItem != null && _trayGuardShortcutsItem.Checked != on) { _trayGuardShortcutsItem.Checked = on; }
            }
            finally
            {
                _suppressSettingsEvents = was;
            }
        }

        /// <summary>A copy of the switch for a tab: the setting, its tooltip, and the shared handler.</summary>
        private CheckBox MakeGuardShortcutsCheck(int x, int y, string from)
        {
            CheckBox box = UiFactory.Check("Hold back Windows shortcuts", x, y,
                                           _settings != null && _settings.GuardWindowsShortcuts);
            box.CheckedChanged += (s, e) =>
            {
                if (_suppressSettingsEvents) { return; }
                SetGuardWindowsShortcuts(box.Checked, from);
            };
            return box;
        }

        /// <summary>
        /// Turns the guard on or off to match what is running. Called on every busy-state change
        /// (RefreshBusyLock), from the status tick as a safety net, and when the setting changes;
        /// costs nothing while the answer is unchanged.
        /// </summary>
        private void UpdateShortcutGuard()
        {
            if (IsDisposed)
            {
                return;
            }
            if (InvokeRequired)
            {
                UiInvoke(UpdateShortcutGuard);
                return;
            }

            bool want = WantShortcutGuard();
            if (_shortcutGuard == null)
            {
                if (!want)
                {
                    return;
                }
                _shortcutGuard = new ShortcutGuard
                {
                    // Asked on the guard's own thread; FiresHotkeyNow only reads the bindings.
                    IsTempoHotkey = vk => _hotkeys != null && _hotkeys.FiresHotkeyNow(vk)
                };
                _shortcutGuard.HeldBackShortcut += OnShortcutHeldBack;
            }
            if (_shortcutGuard.Active != want)
            {
                _shortcutGuard.SetActive(want);
            }
        }

        private bool WantShortcutGuard()
        {
            if (_settings == null || !_settings.GuardWindowsShortcuts || Disposing)
            {
                return false;
            }
            bool clicking = _engine != null && _engine.IsRunning && !_engine.IsPaused && !_engine.ClicksInBackground;
            bool playing = _player != null && _player.IsPlaying;
            return clicking || playing || _liveRecording;
        }

        /// <summary>
        /// Something was held back. Raised on the guard's thread INSIDE the keyboard hook, so this only
        /// queues: the key waits for the callback to return.
        /// </summary>
        private void OnShortcutHeldBack()
        {
            if (_shortcutGuardNoticeShown)
            {
                return;
            }
            _shortcutGuardNoticeShown = true;
            UiInvoke(() =>
            {
                Logger.Info("[Guard] a Windows shortcut was held back during a run — said so once this session.");
                TempoNotify(6000, "Tempo",
                    Localization.T("A Windows shortcut was held back: while Tempo clicks, plays a macro or records one, only Tempo's keybinds work. You can turn this off in Settings ▸ Behaviour."),
                    ToolTipIcon.Info);
            });
        }
    }
}
