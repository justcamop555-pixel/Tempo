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

            // Built in 100% units; scaled with the screen at the end (UiFactory.ScaleToScreen).

            Text = Localization.T("Notification history");
            // The client area (was a 760×480 window): see UiFactory.ScaleToScreen.
            ClientSize = new Size(744, 441);
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

            // Columns that scale with the window at 125%/150% (a plain ListView's stay at 100%).
            _list = new ColumnScalingListView
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

            // Right-aligned from their captions: "Effacer l'historique" and "Cancella
            // cronologia" were cut off at a fixed 106 px. Close keeps the right edge; Clear
            // sits to its left at whatever width its own words need.
            UiFactory.FitToText96(close, 106, 22);
            UiFactory.FitToText96(clear, 106, 22);
            close.Left = 728 - close.Width;
            clear.Left = close.Left - 8 - clear.Width;

            ThemeManager.Apply(this, _theme);
            Reload();

            // Live while it is open. It was a snapshot taken when the window opened, so a
            // card missed while you were reading it never appeared, and neither did the
            // "Nothing recorded yet" after the history was cleared from somewhere else.
            NotificationHistory.Changed += OnHistoryChanged;
            FormClosed += (s, e) => NotificationHistory.Changed -= OnHistoryChanged;
            UiFactory.ScaleToScreen(this);
        }

        /// <summary>1 while a reload is already queued, so a burst of entries costs one rebuild.</summary>
        private int _reloadQueued;

        /// <summary>
        /// NotificationHistory.Changed, raised on whichever thread added the entry and possibly
        /// many times in a row — marshalled to the UI thread and coalesced.
        /// </summary>
        private void OnHistoryChanged()
        {
            if (IsDisposed || !IsHandleCreated) { return; }
            if (System.Threading.Interlocked.Exchange(ref _reloadQueued, 1) == 1) { return; }
            try
            {
                BeginInvoke((Action)(() =>
                {
                    System.Threading.Interlocked.Exchange(ref _reloadQueued, 0);
                    if (!IsDisposed) { Reload(); }
                }));
            }
            catch { System.Threading.Interlocked.Exchange(ref _reloadQueued, 0); }
        }

        private void Reload()
        {
            try
            {
                List<NotificationHistory.Entry> items = NotificationHistory.Recent();

                // Keep the reader's place. New entries arrive at the TOP (newest first), so a
                // plain rebuild would drop someone reading further down back to the start
                // every time anything was recorded — and in a burst, again and again.
                int oldCount = _list.Items.Count;
                int oldTop = _list.TopItem != null ? _list.TopItem.Index : 0;
                object selectedEntry = _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag : null;

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
                    // The entry itself, so a selection survives a reload: Recent() copies the
                    // LIST, not the entries, so the same notification is the same object.
                    row.Tag = e;
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
                    // A list row's tip is as wide as the screen allows too, and a notification's
                    // text can be a paragraph: broken into lines like every other tip.
                    row.ToolTipText = TipStyle.Wrap(row.ToolTipText);
                    _list.Items.Add(row);
                    if (selectedEntry != null && ReferenceEquals(e, selectedEntry)) { row.Selected = true; }
                }

                _list.EndUpdate();

                // Only when the reader had actually scrolled, and only when entries were ADDED —
                // after a clear or a trim there is no old place to go back to.
                if (oldTop > 0 && items.Count >= oldCount && _list.Items.Count > 0)
                {
                    int newTop = Math.Min(_list.Items.Count - 1, oldTop + (items.Count - oldCount));
                    try { _list.TopItem = _list.Items[newTop]; } catch { }
                }

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
