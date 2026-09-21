using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// The apps whose mirrored notifications are muted, and the way back.
    ///
    /// Muting happens on the card itself (right-click → "Mute …"), which is the moment it
    /// is wanted. This is the other half: without it a mute would be a one-way door, and an
    /// app that had quietly stopped notifying would be indistinguishable from mirroring
    /// being broken. It also lists apps Tempo has SEEN — from the notification history —
    /// so an app can be muted before the next time it interrupts, rather than after.
    /// </summary>
    public sealed class MutedAppsDialog : Form
    {
        private readonly AppSettings _settings;
        private readonly Action<string> _unmute;
        private readonly ListBox _muted;
        private readonly ComboBox _seen;
        private readonly Label _empty;
        private readonly Label _seenLabel;
        private readonly Button _muteBtn;

        public MutedAppsDialog(Theme theme, AppSettings settings, Action<string> unmute)
        {
            _settings = settings;
            _unmute = unmute;
            theme = theme ?? Theme.ForKind(ThemeKind.Dark);

            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Text = Localization.T("Muted apps");
            FormBorderStyle = FormBorderStyle.FixedSingle;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(470, 378);   // +28: the intro needs three lines outside English
            BackColor = theme.Background;
            ForeColor = theme.Text;
            Font = UiFactory.BodyFont;

            const int lx = 16;

            var intro = new Label
            {
                Text = Localization.T(
                    "Notifications from these apps are not mirrored into Tempo's cards. They are still "
                    + "written to the notification history, and Windows still shows them its own way."),
                Location = new Point(lx, 14),
                // 56, not 42: every language but English needs a third line here, and the label
                // was cutting it off — measured once the layout harness began measuring a SHOWN
                // window instead of an unshown one (where every control reads as invisible).
                Size = new Size(438, 56),
                ForeColor = theme.TextMuted,
                Font = new Font("Segoe UI", 9f)
            };
            Controls.Add(intro);

            _muted = new ListBox
            {
                Location = new Point(lx, 78),
                Size = new Size(438, 150),
                BackColor = theme.InputBackground,
                ForeColor = theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false
            };
            _muted.DoubleClick += (s, e) => UnmuteSelected();
            Controls.Add(_muted);

            // Shown INSTEAD of an empty white box: a list with nothing in it and no
            // explanation reads as a feature that failed to load.
            _empty = new Label
            {
                Text = Localization.T("Nothing is muted."),
                Location = new Point(lx + 12, 98),
                Size = new Size(414, 20),
                ForeColor = theme.TextMuted,
                BackColor = theme.InputBackground,
                Font = new Font("Segoe UI", 9f)
            };
            Controls.Add(_empty);
            _empty.BringToFront();

            // Sized from its own word: German's "Stummschaltung aufheben" needs 151px, and a
            // button that clips its verb is a button nobody can act on.
            var unmuteBtn = UiFactory.Button(Localization.T("Unmute"), lx, 236, 110, 28);
            unmuteBtn.Width = Math.Max(110, TextRenderer.MeasureText(unmuteBtn.Text, unmuteBtn.Font).Width + 24);
            unmuteBtn.Click += (s, e) => UnmuteSelected();
            Controls.Add(unmuteBtn);

            _seenLabel = UiFactory.Label(Localization.T("Mute an app Tempo has seen:"), lx, 282);
            Controls.Add(_seenLabel);
            _seen = UiFactory.Combo(lx, 304, 250);
            Controls.Add(_seen);

            _muteBtn = UiFactory.Button(Localization.T("Mute"), lx + 258, 303, 90, 28);
            _muteBtn.Width = Math.Max(90, TextRenderer.MeasureText(_muteBtn.Text, _muteBtn.Font).Width + 24);
            _muteBtn.Click += (s, e) => MuteChosen();
            Controls.Add(_muteBtn);

            var close = UiFactory.PrimaryButton("Close", ClientSize.Width - 104, ClientSize.Height - 42, 88, 30, theme);
            close.Click += (s, e) => Close();
            Controls.Add(close);
            AcceptButton = close;
            CancelButton = close;

            ThemeManager.Apply(this, theme);
            // After the theme pass: it resets every control to the page colours, which
            // would make the "nothing here" label stand out as a patch on the list.
            _empty.BackColor = theme.InputBackground;
            _empty.ForeColor = theme.TextMuted;

            ReloadMuted();
            ReloadSeen();
        }

        private void ReloadMuted()
        {
            _muted.BeginUpdate();
            _muted.Items.Clear();
            if (_settings?.MirrorMutedApps != null)
            {
                foreach (string app in _settings.MirrorMutedApps)
                {
                    if (!string.IsNullOrWhiteSpace(app)) { _muted.Items.Add(app.Trim()); }
                }
            }
            _muted.EndUpdate();
            _empty.Visible = _muted.Items.Count == 0;
        }

        /// <summary>
        /// Apps that have actually sent something, newest first, minus the muted ones and
        /// Tempo itself. Typing an app's name from memory is guesswork — the display name
        /// Windows reports is what has to match, and this is where it is known.
        /// </summary>
        private void ReloadSeen()
        {
            _seen.BeginUpdate();
            _seen.Items.Clear();
            try
            {
                var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (NotificationHistory.Entry e in NotificationHistory.Recent())
                {
                    string app = (e.Source ?? "").Trim();
                    if (app.Length == 0) { continue; }
                    if (string.Equals(app, "Tempo", StringComparison.OrdinalIgnoreCase)) { continue; }
                    if (IsMuted(app)) { continue; }
                    if (!added.Add(app)) { continue; }
                    _seen.Items.Add(app);
                    if (_seen.Items.Count >= 40) { break; }
                }
            }
            catch (Exception ex) { Logger.Swallow("MutedAppsDialog.ReloadSeen", ex); }
            _seen.EndUpdate();
            if (_seen.Items.Count > 0) { _seen.SelectedIndex = 0; }

            // An empty dropdown beside a live Mute button reads as something that failed to
            // load. Nothing to choose from is the normal state while mirroring is off, so
            // the row says that instead of offering a choice that cannot be made.
            bool any = _seen.Items.Count > 0;
            _seen.Enabled = any;
            if (_muteBtn != null) { _muteBtn.Enabled = any; }
            if (_seenLabel != null)
            {
                _seenLabel.Text = any
                    ? Localization.T("Mute an app Tempo has seen:")
                    : Localization.T("No other app has notified yet.");
            }
        }

        private bool IsMuted(string app)
        {
            if (_settings?.MirrorMutedApps == null) { return false; }
            foreach (string m in _settings.MirrorMutedApps)
            {
                if (string.Equals(m, app, StringComparison.OrdinalIgnoreCase)) { return true; }
            }
            return false;
        }

        private void UnmuteSelected()
        {
            string app = _muted.SelectedItem as string;
            if (string.IsNullOrEmpty(app)) { return; }
            try { _unmute?.Invoke(app); } catch (Exception ex) { Logger.Swallow("MutedAppsDialog.Unmute", ex); }
            ReloadMuted();
            ReloadSeen();
        }

        private void MuteChosen()
        {
            string app = (_seen.SelectedItem as string ?? _seen.Text ?? "").Trim();
            if (app.Length == 0 || _settings == null) { return; }
            if (string.Equals(app, "Tempo", StringComparison.OrdinalIgnoreCase)) { return; }
            if (IsMuted(app)) { return; }
            if (_settings.MirrorMutedApps == null) { _settings.MirrorMutedApps = new List<string>(); }
            _settings.MirrorMutedApps.Add(app);
            try { Persistence.SettingsManager.Save(_settings); } catch { }
            Logger.Info("[Notify] muted mirrored notifications from " + app + " (from Settings).");
            ReloadMuted();
            ReloadSeen();
        }
    }
}
