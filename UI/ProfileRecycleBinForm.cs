using System;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Persistence;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// The recycle bin for deleted profiles.
    ///
    /// <see cref="ProfileManager"/> has soft-deleted into a bin since it was
    /// written — <c>Remove</c> moves the profile aside rather than dropping it, and
    /// <c>RestoreFromRecycleBin</c> puts it back under a de-duplicated name — but
    /// nothing ever showed the bin, so the safety net caught profiles where nobody
    /// could see them. This is the window into it.
    ///
    /// Restoring renames on collision rather than refusing, so bringing back
    /// "Fishing" while a new "Fishing" exists gives you "Fishing (2)" and never
    /// silently overwrites the profile you are using now.
    /// </summary>
    public sealed class ProfileRecycleBinForm : Form
    {
        private readonly ProfileManager _profiles;
        private readonly ThemedListView _list;
        private readonly Button _restore;
        private readonly Button _empty;
        private readonly Label _emptyNote;

        /// <summary>True when anything was restored or purged, so the caller can refresh.</summary>
        public bool Changed { get; private set; }

        public ProfileRecycleBinForm(Theme theme, ProfileManager profiles)
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            theme = theme ?? Theme.ForKind(ThemeKind.Dark);
            _profiles = profiles;

            Text = Localization.T("Recently deleted profiles");
            Size = new Size(560, 400);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = theme.Background;
            ForeColor = theme.Text;
            Font = UiFactory.BodyFont;

            var help = UiFactory.Label(
                "Deleting a profile keeps a copy here so it can be brought back. " +
                "Restoring one that clashes with a current profile gives it a new name.",
                18, 16);
            help.MaximumSize = new Size(508, 0);
            help.AutoSize = true;
            help.ForeColor = theme.TextMuted;
            Controls.Add(help);

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
            _list.Columns.Add(Localization.T("Profile"), 210);
            _list.Columns.Add(Localization.T("Category"), 110);
            _list.Columns.Add(Localization.T("Last used"), 160);
            _list.SelectedIndexChanged += (s, e) => UpdateButtons();
            _list.DoubleClick += (s, e) => OnRestore(null, EventArgs.Empty);
            Controls.Add(_list);

            _emptyNote = UiFactory.Label("The bin is empty.", 18, 140);
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
                if (_profiles?.RecycleBin != null)
                {
                    // Newest deletion first: the bin appends, so walk it backwards.
                    for (int i = _profiles.RecycleBin.Count - 1; i >= 0; i--)
                    {
                        var p = _profiles.RecycleBin[i];
                        if (p == null) { continue; }

                        var item = new ListViewItem(p.Name) { Tag = p.Name };
                        item.SubItems.Add(CategoryName(p.Category));
                        item.SubItems.Add(p.LastUsedUtc == DateTime.MinValue
                            ? Localization.T("never")
                            : p.LastUsedUtc.ToLocalTime().ToString("g"));
                        _list.Items.Add(item);
                    }
                }
            }
            finally
            {
                _list.EndUpdate();
            }

            // Stretch the last column across whatever is left. The three fixed widths
            // stopped short of the control's edge, and a ListView does not theme the
            // header strip past its final column — so the gap painted as a pale
            // Windows-grey block in the middle of a dark dialog. ClientSize is used
            // rather than Width because it already excludes the vertical scrollbar,
            // which only appears once the bin has enough rows to need one.
            int used = _list.Columns[0].Width + _list.Columns[1].Width;
            int fill = _list.ClientSize.Width - used;
            if (fill > 80) { _list.Columns[2].Width = fill; }

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

        private static string CategoryName(ProfileCategory c)
        {
            switch (c)
            {
                case ProfileCategory.Gaming: return Localization.T("Gaming");
                case ProfileCategory.Work: return Localization.T("Work");
                case ProfileCategory.Productivity: return Localization.T("Productivity");
                default: return Localization.T("Custom");
            }
        }

        private void OnRestore(object sender, EventArgs e)
        {
            if (_list.SelectedItems.Count == 0) { return; }

            // Snapshot the names first: restoring mutates the bin underneath us.
            var names = new System.Collections.Generic.List<string>();
            foreach (ListViewItem item in _list.SelectedItems)
            {
                names.Add(item.Tag as string);
            }

            int restored = 0;
            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name)) { continue; }
                if (_profiles.RestoreFromRecycleBin(name) != null) { restored++; }
            }

            if (restored > 0)
            {
                _profiles.Save();
                Changed = true;
                Logger.Info("[profiles] restored " + restored + " profile(s) from the recycle bin.");
                Reload();
            }
        }

        private void OnEmpty(object sender, EventArgs e)
        {
            int count = _profiles?.RecycleBin?.Count ?? 0;
            if (count == 0) { return; }

            var confirm = MessageBox.Show(this,
                Localization.F("Permanently delete {0} profile(s)? This cannot be undone.",
                    count.ToString()),
                "Tempo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) { return; }

            _profiles.EmptyRecycleBin();
            _profiles.Save();
            Changed = true;
            Logger.Info("[profiles] recycle bin emptied (" + count + " profile(s)).");
            Reload();
        }

        /// <summary>Shows the bin; returns true when the library changed.</summary>
        public static bool Show(IWin32Window owner, Theme theme, ProfileManager profiles)
        {
            using (var form = new ProfileRecycleBinForm(theme, profiles))
            {
                form.ShowDialog(owner);
                return form.Changed;
            }
        }
    }
}
