using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// The window onto a soft-delete bin, shared by profiles and macros.
    ///
    /// It started as ProfileRecycleBinForm. When macros needed the same thing the
    /// obvious move was to copy it — and this session had just finished cataloguing
    /// nine methods in CrashReporter that exist only because someone did exactly that
    /// and the two copies drifted. So it takes its rows and its three verbs from the
    /// caller instead, and there is one implementation of the list, the confirmation
    /// and the theming.
    ///
    /// The caller supplies: what the columns are called, how to list the bin, how to
    /// restore one entry, and how to empty it. Everything else is here.
    /// </summary>
    public sealed class RecycleBinForm : Form
    {
        /// <summary>One row: an id the restore callback understands, plus its cells.</summary>
        public sealed class Entry
        {
            public string Id { get; set; }
            public string[] Cells { get; set; }
        }

        private readonly ThemedListView _list;
        private readonly Button _restore;
        private readonly Button _empty;
        private readonly Label _emptyNote;

        private readonly Func<List<Entry>> _load;
        private readonly Func<string, bool> _restoreOne;
        private readonly Action _emptyAll;
        private readonly string _emptyPrompt;
        private readonly bool _closeOnRestore;

        /// <summary>True when anything was restored or purged, so the caller can refresh.</summary>
        public bool Changed { get; private set; }

        /// <param name="emptyLabel">Caption for the button that empties the list; "Empty the bin" when null.</param>
        /// <param name="closeOnRestore">Close once something is restored — for a list where picking one ends the job.</param>
        public RecycleBinForm(Theme theme, string title, string help,
                              string[] headers, int[] widths, string emptyNote, string emptyPrompt,
                              Func<List<Entry>> load, Func<string, bool> restoreOne, Action emptyAll,
                              string emptyLabel = null, bool closeOnRestore = false)
        {
            // Built in 100% units; scaled with the screen at the end (UiFactory.ScaleToScreen).
            theme = theme ?? Theme.ForKind(ThemeKind.Dark);

            _load = load;
            _restoreOne = restoreOne;
            _emptyAll = emptyAll;
            _emptyPrompt = emptyPrompt;
            _closeOnRestore = closeOnRestore;

            Text = title;
            // The client area (was a 560×400 window): see UiFactory.ScaleToScreen.
            ClientSize = new Size(544, 361);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = theme.Background;
            ForeColor = theme.Text;
            Font = UiFactory.BodyFont;

            var helpLabel = UiFactory.Label(help, 18, 16);
            helpLabel.MaximumSize = new Size(508, 0);
            helpLabel.AutoSize = true;
            helpLabel.ForeColor = theme.TextMuted;
            Controls.Add(helpLabel);

            // Everything below the help starts below it, however many lines it wraps to. The restore
            // points' help is three lines in five languages, and its third line ran under the list's
            // header (scratchpad/dialogscan).
            // (Measured in 100% units: the window is scaled after it is built — UiFactory.Measure96.)
            int helpBottom = helpLabel.Top + UiFactory.Measure96(help, helpLabel.Font, 508, TextFormatFlags.Default).Height;
            int shift = Math.Max(0, helpBottom + 10 - 58);
            ClientSize = new Size(544, 361 + shift);

            _list = new ThemedListView
            {
                Left = 18,
                Top = 58 + shift,
                Width = 508,
                Height = 224,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = true,
                HideSelection = false
            };
            for (int i = 0; i < headers.Length; i++)
            {
                _list.Columns.Add(headers[i], i < widths.Length ? widths[i] : 120);
            }
            _list.SelectedIndexChanged += (s, e) => UpdateButtons();
            _list.DoubleClick += (s, e) => OnRestore(null, EventArgs.Empty);
            Controls.Add(_list);

            _emptyNote = UiFactory.Label(emptyNote, 18, 140 + shift);
            _emptyNote.AutoSize = false;
            _emptyNote.Width = 508;
            // As tall as its words wrapped to the list's width: the restore points' note is two lines
            // in five languages, and a one-line box drew only the first.
            _emptyNote.Height = Math.Max(24, UiFactory.Measure96(emptyNote, _emptyNote.Font, 508, TextFormatFlags.Default).Height + 6);
            _emptyNote.TextAlign = ContentAlignment.MiddleCenter;
            _emptyNote.ForeColor = theme.TextMuted;
            _emptyNote.Visible = false;
            Controls.Add(_emptyNote);
            LayoutFitter.DeclareOverlay(_emptyNote, "drawn on top of the list while it is empty");

            // Each as wide as its word ("Wiederherstellen" needs 131 px), packed from its own end.
            int by = 296 + shift;
            _restore = UiFactory.PrimaryButton("Restore", 18, by, 110, 32, theme);
            UiFactory.FitToText96(_restore, 110);
            _restore.Click += OnRestore;
            Controls.Add(_restore);

            _empty = UiFactory.Button(string.IsNullOrEmpty(emptyLabel) ? "Empty the bin" : emptyLabel, 136, by, 130, 32);
            UiFactory.FitToText96(_empty, 130);
            _empty.Left = _restore.Right + 8;
            _empty.Click += OnEmpty;
            Controls.Add(_empty);

            var close = UiFactory.Button("Close", 436, by, 90, 32);
            UiFactory.FitToText96(close, 90);
            close.Left = _list.Right - close.Width;
            close.Click += (s, e) => Close();
            Controls.Add(close);
            CancelButton = close;

            ThemeManager.Apply(this, theme);
            _list.ApplyTheme(theme);

            Reload();
            UiFactory.ScaleToScreen(this);
        }

        private void Reload()
        {
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                List<Entry> rows = null;
                try { rows = _load != null ? _load() : null; }
                catch (Exception ex) { Logger.Swallow("RecycleBinForm.load", ex); }

                if (rows != null)
                {
                    foreach (var row in rows)
                    {
                        if (row == null || row.Cells == null || row.Cells.Length == 0) { continue; }
                        var item = new ListViewItem(row.Cells[0]) { Tag = row.Id };
                        for (int i = 1; i < row.Cells.Length; i++)
                        {
                            item.SubItems.Add(row.Cells[i] ?? "");
                        }
                        _list.Items.Add(item);
                    }
                }
            }
            finally
            {
                _list.EndUpdate();
            }

            // After the rows exist, so the leftover width accounts for the vertical
            // scrollbar the list may just have grown.
            _list.FitLastColumn();

            bool any = _list.Items.Count > 0;
            _list.Visible = any;
            _emptyNote.Visible = !any;
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            _restore.Enabled = _list.Visible && _list.SelectedItems.Count > 0;
            _empty.Enabled = _list.Items.Count > 0;
        }

        private void OnRestore(object sender, EventArgs e)
        {
            if (_list.SelectedItems.Count == 0 || _restoreOne == null) { return; }

            // Snapshot the ids first: restoring mutates the bin underneath us.
            var ids = new List<string>();
            foreach (ListViewItem item in _list.SelectedItems)
            {
                ids.Add(item.Tag as string);
            }

            int restored = 0;
            foreach (string id in ids)
            {
                if (string.IsNullOrEmpty(id)) { continue; }
                try { if (_restoreOne(id)) { restored++; } }
                catch (Exception ex) { Logger.Swallow("RecycleBinForm.restore", ex); }
            }

            if (restored > 0)
            {
                Changed = true;
                // Picking a restore point is a one-shot choice that restarts Tempo; the list has nothing
                // more to offer after it, so a caller can ask for it to close.
                if (_closeOnRestore) { Close(); return; }
                Reload();
            }
        }

        private void OnEmpty(object sender, EventArgs e)
        {
            int count = _list.Items.Count;
            if (count == 0 || _emptyAll == null) { return; }

            var confirm = MessageBox.Show(this,
                Localization.F(_emptyPrompt, count.ToString()),
                "Tempo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) { return; }

            try { _emptyAll(); }
            catch (Exception ex) { Logger.Swallow("RecycleBinForm.empty", ex); return; }

            Changed = true;
            Reload();
        }

        /// <summary>Shows the bin; returns true when something was restored or purged.</summary>
        public static bool Show(IWin32Window owner, Theme theme, string title, string help,
                                string[] headers, int[] widths, string emptyNote, string emptyPrompt,
                                Func<List<Entry>> load, Func<string, bool> restoreOne, Action emptyAll,
                                string emptyLabel = null, bool closeOnRestore = false)
        {
            using (var form = new RecycleBinForm(theme, title, help, headers, widths,
                                                 emptyNote, emptyPrompt, load, restoreOne, emptyAll,
                                                 emptyLabel, closeOnRestore))
            {
                form.ShowDialog(owner);
                return form.Changed;
            }
        }
    }
}
