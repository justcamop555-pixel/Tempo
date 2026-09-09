using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// Everything Tempo has notified about, newest first — including the cards it
    /// deliberately did NOT show.
    ///
    /// The reason this is worth a window rather than a log line: notifications are
    /// dropped while a fullscreen app is running (NotificationCenter.ShowOrQueue), and
    /// Tempo is an auto-clicker, so that is exactly when it has something to say. The
    /// count was already surfaced in Live debug; the message was not surfaced anywhere.
    /// Missed rows are marked and carry the reason they were held.
    /// </summary>
    public sealed class NotificationHistoryForm : Form
    {
        private readonly Theme _theme;
        private readonly ListView _list;
        private readonly Label _summary;

        public NotificationHistoryForm(Theme theme)
        {
            _theme = theme ?? Theme.ForKind(ThemeKind.Dark);

            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);

            Text = Localization.T("Notification history");
            Size = new Size(760, 480);
            MinimumSize = new Size(560, 320);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = _theme.Background;
            ForeColor = _theme.Text;
            Font = UiFactory.BodyFont;
            ThemeManager.ApplyWindowChrome(this, _theme);

            var help = UiFactory.Label(
                "Everything Tempo has notified about. Anything held back while a game or "
                + "presentation was on screen is marked “missed”.", 16, 14);
            help.MaximumSize = new Size(700, 0);
            help.AutoSize = true;
            help.ForeColor = _theme.TextMuted;
            Controls.Add(help);

            _list = new ListView
            {
                Left = 16,
                Top = 56,
                Width = 712,
                Height = 340,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                GridLines = false,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = _theme.InputBackground,
                ForeColor = _theme.Text,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            };
            _list.Columns.Add(Localization.T("When"), 128);
            _list.Columns.Add(Localization.T("From"), 130);
            _list.Columns.Add(Localization.T("Message"), 380);
            _list.Columns.Add(Localization.T("Status"), 68);
            // The body is often longer than the column; a tooltip beats truncation and
            // costs nothing, and the same trick the layout fitter uses for captions.
            _list.ShowItemToolTips = true;
            Controls.Add(_list);

            _summary = UiFactory.Label("", 16, 408);
            _summary.AutoSize = true;
            _summary.ForeColor = _theme.TextMuted;
            _summary.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            Controls.Add(_summary);

            var clear = UiFactory.Button(Localization.T("Clear history"), 508, 404, 106, 30);
            clear.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            clear.Click += (s, e) =>
            {
                if (MessageBox.Show(this,
                        Localization.T("Delete every notification Tempo has recorded?"),
                        "Tempo", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    NotificationHistory.Clear();
                    Reload();
                }
            };
            Controls.Add(clear);

            var close = UiFactory.PrimaryButton(Localization.T("Close"), 622, 404, 106, 30, _theme);
            close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            close.Click += (s, e) => Close();
            Controls.Add(close);

            ThemeManager.Apply(this, _theme);
            Reload();
        }

        private void Reload()
        {
            try
            {
                List<NotificationHistory.Entry> items = NotificationHistory.Recent();
                _list.BeginUpdate();
                _list.Items.Clear();

                int missed = 0;
                foreach (NotificationHistory.Entry e in items)
                {
                    string message = e.Title;
                    if (!string.IsNullOrEmpty(e.Body))
                    {
                        message = message.Length > 0 ? message + " — " + e.Body : e.Body;
                    }

                    var row = new ListViewItem(e.WhenLocal.ToString("ddd HH:mm, d MMM"));
                    row.SubItems.Add(string.IsNullOrEmpty(e.Source) ? "Tempo" : e.Source);
                    row.SubItems.Add(message);

                    switch (e.How)
                    {
                        case NotificationHistory.Outcome.Missed:
                            missed++;
                            row.SubItems.Add(Localization.T("missed"));
                            // The one thing worth colour in this list.
                            row.ForeColor = _theme.WarningText;
                            row.ToolTipText = string.IsNullOrEmpty(e.Reason)
                                ? message
                                : Localization.T("Held back:") + " " + e.Reason + "\n" + message;
                            break;
                        case NotificationHistory.Outcome.Repeated:
                            row.SubItems.Add(Localization.T("repeat"));
                            row.ForeColor = _theme.TextMuted;
                            row.ToolTipText = message;
                            break;
                        default:
                            row.SubItems.Add(Localization.T("shown"));
                            row.ToolTipText = message;
                            break;
                    }
                    _list.Items.Add(row);
                }

                _list.EndUpdate();

                _summary.Text = items.Count == 0
                    ? Localization.T("Nothing recorded yet.")
                    : Localization.F("{0} recorded · {1} you didn't see", items.Count, missed);
                _summary.ForeColor = missed > 0 ? _theme.WarningText : _theme.TextMuted;
            }
            catch (Exception ex) { Logger.Swallow("NotificationHistoryForm.Reload", ex); }
        }

        /// <summary>Opens the history, or brings the open one forward.</summary>
        private static NotificationHistoryForm _open;

        public static void ShowFor(IWin32Window owner, Theme theme)
        {
            try
            {
                if (_open != null && !_open.IsDisposed)
                {
                    _open.Reload();
                    _open.BringToFront();
                    _open.Activate();
                    return;
                }
                _open = new NotificationHistoryForm(theme);
                _open.FormClosed += (s, e) => _open = null;
                _open.Show(owner);
            }
            catch (Exception ex) { Logger.Swallow("NotificationHistoryForm.ShowFor", ex); }
        }
    }
}
