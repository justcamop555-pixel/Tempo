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
        private readonly Label _previewHead;
        private readonly TextBox _preview;
        private readonly PictureBox _picture;              // the selected notification's picture (this session's only)
        private NotificationHistory.Entry _pictureFor;     // whose picture _picture holds
        private bool _pictureChecked;

        public NotificationHistoryForm(Theme theme)
        {
            _theme = theme ?? Theme.ForKind(ThemeKind.Dark);

            // Built in 100% units; scaled with the screen at the end (UiFactory.ScaleToScreen).

            Text = Localization.T("Notification history");
            // The client area (was a 760×480 window): see UiFactory.ScaleToScreen.
            ClientSize = new Size(744, 441);
            // Taller than before: the preview under the list needs its room at the smallest size too.
            MinimumSize = new Size(560, 400);
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
                // 216, not 340: the preview of the selected notification sits underneath.
                Height = 216,
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
            _list.SelectedIndexChanged += (s, e) => ShowPreview();
            Controls.Add(_list);

            // The whole of the selected notification. The Message column cuts it off with "…" and the
            // tooltip only shows on a hover nobody knew about: here it is in full, selectable to copy —
            // where it came from, when (and how often), and why it was held back if it was.
            _previewHead = UiFactory.Label("", 16, 280, FontStyle.Bold);
            _previewHead.AutoSize = false;
            _previewHead.Size = new Size(712, 20);
            _previewHead.AutoEllipsis = true;
            _previewHead.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(_previewHead);

            _preview = new TextBox
            {
                Left = 16,
                Top = 302,
                Width = 712,
                Height = 92,
                Multiline = true,
                ReadOnly = true,
                WordWrap = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = _theme.InputBackground,
                ForeColor = _theme.Text,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            };
            Controls.Add(_preview);

            // The picture the notification carried, beside its text — held for this session only (the
            // history never writes pictures to disk), so older entries simply have none.
            _picture = new PictureBox
            {
                Left = 568,
                Top = 302,
                Width = 160,
                Height = 92,
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = _theme.InputBackground,
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
                Visible = false,
            };
            Controls.Add(_picture);
            // Hidden, it shares the preview's corner; shown, the preview is narrowed first (ShowPicture),
            // so the two never meet on screen.
            LayoutFitter.DeclareOverlay(_picture, "shown only beside a preview narrowed to make room for it");
            FormClosed += (s, e) => { var img = _picture.Image; _picture.Image = null; img?.Dispose(); };

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

                int missed = 0, total = 0;
                foreach (NotificationHistory.Entry e in items)
                {
                    int times = Math.Max(1, e.Count);
                    total += times;
                    string message = e.Title;
                    if (!string.IsNullOrEmpty(e.Body))
                    {
                        message = message.Length > 0 ? message + " — " + e.Body : e.Body;
                    }
                    // Repeats are counted on one row (NotificationHistory.FindRowToCount); the count
                    // leads the message so a long text can't push it out of sight.
                    if (times > 1) { message = "×" + times + "  " + message; }

                    var row = new ListViewItem(e.WhenLocal.ToString("ddd HH:mm, d MMM"));
                    // The entry itself, so a selection survives a reload: Recent() copies the
                    // LIST, not the entries, so the same notification is the same object.
                    row.Tag = e;
                    // Named as its card named it: "www.kenherbert.dev · Microsoft Edge", not just the browser.
                    row.SubItems.Add(e.SourceLine);
                    row.SubItems.Add(message);

                    switch (e.How)
                    {
                        case NotificationHistory.Outcome.Missed:
                            missed += times;
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
                    : Localization.F("{0} recorded · {1} you didn't see", total, missed);
                _summary.ForeColor = missed > 0 ? _theme.WarningText : _theme.TextMuted;
                ShowPreview();   // the selected row's count can have grown
            }
            catch (Exception ex) { Logger.Swallow("NotificationHistoryForm.Reload", ex); }
        }

        /// <summary>The selected notification, whole, in the preview under the list.</summary>
        private void ShowPreview()
        {
            if (_preview == null) { return; }
            try
            {
                var e = _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as NotificationHistory.Entry : null;
                ShowPicture(e);
                if (e == null)
                {
                    _previewHead.Text = "";
                    _preview.ForeColor = _theme.TextMuted;
                    _preview.Text = Localization.T("Select a notification to read all of it.");
                    return;
                }

                string status = e.How == NotificationHistory.Outcome.Missed ? Localization.T("missed")
                              : e.How == NotificationHistory.Outcome.Repeated ? Localization.T("repeat")
                              : Localization.T("shown");
                _previewHead.Text = e.SourceLine + "  ·  " + e.WhenLocal.ToString("ddd HH:mm, d MMM") + "  ·  " + status;
                _previewHead.ForeColor = e.How == NotificationHistory.Outcome.Missed ? _theme.WarningText : _theme.Text;

                var sb = new System.Text.StringBuilder();
                if (!string.IsNullOrEmpty(e.Title)) { sb.Append(e.Title); }
                if (!string.IsNullOrEmpty(e.Body))
                {
                    if (sb.Length > 0) { sb.Append("\r\n"); }
                    sb.Append(e.Body);
                }
                if (e.How == NotificationHistory.Outcome.Missed && !string.IsNullOrEmpty(e.Reason))
                {
                    sb.Append("\r\n\r\n").Append(Localization.T("Held back:")).Append(' ').Append(e.Reason);
                }
                if (e.Count > 1)
                {
                    DateTime last = (e.LastUtc == default ? e.WhenUtc : e.LastUtc).ToLocalTime();
                    sb.Append("\r\n\r\n").Append(Localization.F("Arrived {0} times, the last at {1}.", e.Count, last.ToString("HH:mm:ss")));
                }
                _preview.ForeColor = _theme.Text;
                _preview.Text = sb.ToString();
            }
            catch (Exception ex) { Logger.Swallow("NotificationHistoryForm.ShowPreview", ex); }
        }

        /// <summary>
        /// The selected notification's picture beside its text, the text taking the whole width when
        /// there is none. Copied only when the selection changes: a reload (anything recorded) must not
        /// re-copy it every time.
        /// </summary>
        private void ShowPicture(NotificationHistory.Entry e)
        {
            if (_pictureChecked && ReferenceEquals(e, _pictureFor)) { return; }
            _pictureChecked = true;
            _pictureFor = e;
            Image copy = NotificationHistory.CopyPicture(e);
            Image old = _picture.Image;
            _picture.Image = copy;
            old?.Dispose();
            _picture.Visible = copy != null;
            // The gap at the window's own scale: the picture box was built 160 wide at 100%.
            int gap = (int)Math.Round(8 * _picture.Width / 160.0);
            _preview.Width = (copy != null ? _picture.Left - gap : _picture.Right) - _preview.Left;
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
