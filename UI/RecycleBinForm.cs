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

        /// <summary>True when anything was restored or purged, so the caller can refresh.</summary>
        public bool Changed { get; private set; }

        public RecycleBinForm(Theme theme, string title, string help,
                              string[] headers, int[] widths, string emptyNote, string emptyPrompt,
                              Func<List<Entry>> load, Func<string, bool> restoreOne, Action emptyAll)
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            theme = theme ?? Theme.ForKind(ThemeKind.Dark);

            _load = load;
            _restoreOne = restoreOne;
            _emptyAll = emptyAll;
            _emptyPrompt = emptyPrompt;

            Text = title;
            Size = new Size(560, 400);
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

            _list = new ThemedListView
            {
                Left = 18,
                Top = 58,
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

            _emptyNote = UiFactory.Label(emptyNote, 18, 140);
            _emptyNote.AutoSize = false;
            _emptyNote.Width = 508;
            _emptyNote.Height = 24;
            _emptyNote.TextAlign = ContentAlignment.MiddleCenter;
            _emptyNote.ForeColor = theme.TextMuted;
            _emptyNote.Visible = false;
            Controls.Add(_emptyNote);

            _restore = UiFactory.PrimaryButton("Restore", 18, 296, 110, 32, theme);
            _restore.Click += OnRestore;
            Controls.Add(_restore);

            _empty = UiFactory.Button("Empty the bin", 136, 296, 130, 32);
            _empty.Click += OnEmpty;
            Controls.Add(_empty);

            var close = UiFactory.Button("Close", 436, 296, 90, 32);
            close.Click += (s, e) => Close();
            Controls.Add(close);
            CancelButton = close;

            ThemeManager.Apply(this, theme);
            _list.ApplyTheme(theme);

            Reload();
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
                                Func<List<Entry>> load, Func<string, bool> restoreOne, Action emptyAll)
        {
            using (var form = new RecycleBinForm(theme, title, help, headers, widths,
                                                 emptyNote, emptyPrompt, load, restoreOne, emptyAll))
            {
                form.ShowDialog(owner);
                return form.Changed;
            }
        }
    }
}
