using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Persistence;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// The Profiles tab — a library view over the saved click profiles.
    ///
    /// WHY THIS EXISTS. Profiles were always more than a name. A
    /// <see cref="ClickProfile"/> carries a description, an icon, a colour tag, a
    /// category, a favourite flag, a created date, a use count and a cumulative
    /// runtime; <see cref="ProfileManager"/> backs them with a recycle bin,
    /// favourites, JSON import/export and usage tracking. All of it was reachable
    /// through a single 220px combo box on the Clicker tab that displayed the name
    /// and nothing else — so the metadata was written to disk and never read, and
    /// the recycle bin caught deletions where nobody could see them.
    ///
    /// <c>ProfileCategory</c>'s own doc comment has said "the library bucket a
    /// profile belongs to in the Profiles tab" since before the tab existed. This
    /// is that tab.
    ///
    /// The grid reflows to the window width rather than sitting at a fixed column
    /// count. Every other tab is hand-placed at a fixed width, which is right for a
    /// form; a library is a different shape — the number of things is up to the
    /// user, so the number of columns should follow the space available.
    /// </summary>
    public partial class MainForm
    {
        private const int ProfileGridGap = 12;
        private const int ProfileGridLeft = 16;

        private BackdropTabPage _profilesPage;
        private TextBox _profileSearch;
        private ComboBox _profileCategoryFilter;
        private ComboBox _profileSortCombo;
        private CheckBox _profileFavOnly;
        private Label _profileCountLabel;
        private Label _profileEmptyLabel;
        private Button _profileRecycleButton;
        private ContextMenuStrip _profileCardMenu;

        /// <summary>Y of the first card row, in unscrolled page coordinates.</summary>
        private int _profileGridTop;

        private readonly List<ProfileCard> _profileCards = new List<ProfileCard>();

        /// <summary>Guards filter events while the toolbar is being populated.</summary>
        private bool _suppressProfileFilterEvents;

        /// <summary>True while a grid rebuild is already posted, so several requests collapse into one.</summary>
        private bool _profileGridRebuildQueued;

        /// <summary>Stops an activation from re-entering itself mid-rebuild.</summary>
        private bool _activatingProfile;

        // ─────────────────────────────────────────────────────────────────────
        //  Build
        // ─────────────────────────────────────────────────────────────────────

        private void BuildProfilesTab()
        {
            var page = new BackdropTabPage(Localization.T("Profiles")) { AutoScroll = true };
            page.Name = "profiles";   // stable key for LastTabKey
            _profilesPage = page;

            var title = UiFactory.Label("Profile library", 16, 14, FontStyle.Bold, 13f);
            page.Controls.Add(title);

            _profileCountLabel = UiFactory.Caption("", 16, 38);
            _profileCountLabel.ForeColor = _theme.TextMuted;
            page.Controls.Add(_profileCountLabel);

            // ── Filter row ────────────────────────────────────────────────────
            int row = 62;

            _profileSearch = UiFactory.Text(16, row, 210);
            _profileSearch.PlaceholderText = Localization.T("Search profiles…");
            _profileSearch.AccessibleName = Localization.T("Search profiles");
            _profileSearch.TextChanged += OnProfileFilterChanged;
            page.Controls.Add(_profileSearch);

            _suppressProfileFilterEvents = true;
            try
            {
                // Combo items are not auto-translated by UiFactory; labels are.
                _profileCategoryFilter = UiFactory.Combo(236, row, 140,
                    Localization.T("All categories"),
                    Localization.T("Gaming"),
                    Localization.T("Work"),
                    Localization.T("Productivity"),
                    Localization.T("Custom"));
                _profileCategoryFilter.AccessibleName = Localization.T("Filter by category");
                _profileCategoryFilter.SelectedIndexChanged += OnProfileFilterChanged;
                page.Controls.Add(_profileCategoryFilter);

                _profileSortCombo = UiFactory.Combo(386, row, 160,
                    Localization.T("Recently used"),
                    Localization.T("Name (A–Z)"),
                    Localization.T("Most used"),
                    Localization.T("Newest first"));
                _profileSortCombo.AccessibleName = Localization.T("Sort profiles");
                _profileSortCombo.SelectedIndexChanged += OnProfileFilterChanged;
                page.Controls.Add(_profileSortCombo);
            }
            finally
            {
                _suppressProfileFilterEvents = false;
            }

            _profileFavOnly = UiFactory.Check("Favourites only", 560, row + 3);
            _profileFavOnly.CheckedChanged += OnProfileFilterChanged;
            page.Controls.Add(_profileFavOnly);

            // ── Action row ────────────────────────────────────────────────────
            row += 36;

            var newBtn = UiFactory.PrimaryButton("New profile", 16, row, 122, 30, _theme);
            newBtn.Click += OnProfileLibNew;
            page.Controls.Add(newBtn);

            var importBtn = UiFactory.Button("Import…", 146, row, 96, 30);
            importBtn.Click += OnProfileLibImport;
            page.Controls.Add(importBtn);

            _profileRecycleButton = UiFactory.Button("Recycle bin", 250, row, 122, 30);
            _profileRecycleButton.Click += OnProfileLibRecycleBin;
            page.Controls.Add(_profileRecycleButton);

            var hint = UiFactory.Caption("Click a card to switch to it. Right-click for more.", 386, row + 8);
            hint.ForeColor = _theme.TextMuted;
            page.Controls.Add(hint);

            // ── Grid ──────────────────────────────────────────────────────────
            //
            // The cards go straight onto the page rather than into a container. A
            // BackdropTabPage paints a wallpaper that is meant to show through the
            // gaps between cards, and a WinForms panel with a transparent BackColor
            // paints its parent's flat BackColor, not the parent's custom painting —
            // so a container would have covered the wallpaper with a grey slab.
            // StatCard sits directly on the Statistics page for the same reason.
            row += 40;
            _profileGridTop = row;

            _profileEmptyLabel = UiFactory.Label("No profile matches that search.", ProfileGridLeft, row + 12);
            _profileEmptyLabel.ForeColor = _theme.TextMuted;
            _profileEmptyLabel.Visible = false;
            page.Controls.Add(_profileEmptyLabel);

            BuildProfileCardMenu();

            // Reflow when the page width changes so the grid uses the space it has.
            page.ClientSizeChanged += (s, e) => LayoutProfileGrid();

            _tabs.TabPages.Add(page);
        }

        private void BuildProfileCardMenu()
        {
            _profileCardMenu = new ContextMenuStrip { ShowImageMargin = false };
            _profileCardMenu.Disposed += (s, e) =>
                (_profileCardMenu.Renderer as ThemedMenuRenderer)?.Dispose();
            ApplyThemeToProfileMenu();

            _profileCardMenu.Items.Add(Localization.T("Switch to this profile"), null,
                (s, e) => WithMenuProfile(ActivateProfileByName));
            _profileCardMenu.Items.Add(new ToolStripSeparator());
            _profileCardMenu.Items.Add(Localization.T("Edit details…"), null,
                (s, e) => WithMenuProfile(OnProfileLibEdit));
            _profileCardMenu.Items.Add(Localization.T("Duplicate"), null,
                (s, e) => WithMenuProfile(OnProfileLibDuplicate));
            _profileCardMenu.Items.Add(Localization.T("Export…"), null,
                (s, e) => WithMenuProfile(OnProfileLibExport));
            _profileCardMenu.Items.Add(new ToolStripSeparator());
            _profileCardMenu.Items.Add(Localization.T("Delete"), null,
                (s, e) => WithMenuProfile(OnProfileLibDelete));
        }

        /// <summary>
        /// Runs an action against the profile whose card opened the context menu.
        /// SourceControl is read at click time because the menu is shared by every
        /// card rather than built per card.
        /// </summary>
        private void WithMenuProfile(Action<string> action)
        {
            var card = _profileCardMenu?.SourceControl as ProfileCard;
            if (card == null || string.IsNullOrEmpty(card.ProfileName)) { return; }
            action(card.ProfileName);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Filtering, sorting and the grid
        // ─────────────────────────────────────────────────────────────────────

        private void OnProfileFilterChanged(object sender, EventArgs e)
        {
            if (_suppressProfileFilterEvents) { return; }
            RefreshProfileGrid();
        }

        /// <summary>Applies the search box, the category filter and the sort order.</summary>
        private List<ClickProfile> VisibleProfiles()
        {
            var result = new List<ClickProfile>();
            if (_profiles == null) { return result; }

            string search = _profileSearch != null ? _profileSearch.Text.Trim() : "";
            int categoryIndex = _profileCategoryFilter != null ? _profileCategoryFilter.SelectedIndex : 0;
            bool favOnly = _profileFavOnly != null && _profileFavOnly.Checked;

            foreach (var p in _profiles.Profiles)
            {
                if (p == null) { continue; }
                if (favOnly && !p.Favorite) { continue; }
                if (categoryIndex > 0 && (int)p.Category != categoryIndex - 1) { continue; }

                if (search.Length > 0)
                {
                    // Name and description both match, so "afk" finds a profile called
                    // "Fishing" whose description explains that it is for AFK farming.
                    bool hit =
                        (p.Name ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (p.Description ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!hit) { continue; }
                }

                result.Add(p);
            }

            int sort = _profileSortCombo != null ? _profileSortCombo.SelectedIndex : 0;
            result.Sort((a, b) =>
            {
                // Starred profiles lead regardless of the sort — that is what the star
                // is for. Within each group the chosen order applies.
                if (a.Favorite != b.Favorite) { return a.Favorite ? -1 : 1; }

                switch (sort)
                {
                    case 1:
                        return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
                    case 2:
                        return b.TimesUsed.CompareTo(a.TimesUsed);
                    case 3:
                        return b.CreatedUtc.CompareTo(a.CreatedUtc);
                    default:
                        // Recently used, with never-used profiles falling to the back.
                        int byUse = b.LastUsedUtc.CompareTo(a.LastUsedUtc);
                        if (byUse != 0) { return byUse; }
                        return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
                }
            });

            return result;
        }

        /// <summary>
        /// Asks for the card grid to be rebuilt.
        ///
        /// THE REBUILD CANNOT HAPPEN INLINE. It disposes every card, and almost every
        /// caller reaches here from a card's own event — click a card, it activates,
        /// which re-sorts the library and rebuilds the grid. Disposing a control while
        /// its WndProc is still on the stack leaves WinForms finishing WmMouseUp on a
        /// dead object, which is undefined at best. Posting the work puts it after the
        /// current message instead, when the card is safe to destroy.
        ///
        /// It also coalesces: one activation refreshes the combo, marks the profile
        /// used and re-sorts, and each of those wants a rebuild. Only one runs.
        ///
        /// Before the form has a handle there is no message loop to post to, so the
        /// startup path builds inline.
        /// </summary>
        private void RefreshProfileGrid()
        {
            if (_profilesPage == null) { return; }

            if (!IsHandleCreated)
            {
                RebuildProfileGrid();
                return;
            }

            if (_profileGridRebuildQueued) { return; }
            _profileGridRebuildQueued = true;

            BeginInvoke(new Action(() =>
            {
                _profileGridRebuildQueued = false;
                RebuildProfileGrid();
            }));
        }

        /// <summary>Does the actual teardown and rebuild. Only call via <see cref="RefreshProfileGrid"/>.</summary>
        private void RebuildProfileGrid()
        {
            if (_profilesPage == null || IsDisposed) { return; }

            var shown = VisibleProfiles();

            _profilesPage.SuspendLayout();
            try
            {
                foreach (var card in _profileCards)
                {
                    _profilesPage.Controls.Remove(card);
                    card.Dispose();
                }
                _profileCards.Clear();

                foreach (var p in shown)
                {
                    var card = new ProfileCard
                    {
                        ProfileName = p.Name,
                        // Translated on DISPLAY, not on creation. The description is
                        // stored in profiles.json, so translating it in
                        // CreateDefaultProfile would freeze the seeded profile in
                        // whichever language happened to run first and leave it there
                        // after a language change. T() returns its input on a miss, so
                        // a description the user typed passes through untouched.
                        // The NAME is deliberately NOT translated: it is the identity
                        // key GetByName looks up, and a translated one would not match.
                        Description = Localization.T(p.Description ?? ""),
                        Glyph = string.IsNullOrEmpty(p.Icon) ? "🎯" : p.Icon,
                        CategoryText = ProfileCategoryName(p.Category),
                        UsageText = ProfileUsageText(p),
                        TagColor = p.ColorTagArgb == 0 ? Color.Empty : Color.FromArgb(p.ColorTagArgb),
                        Favorite = p.Favorite,
                        IsActive = string.Equals(p.Name, _currentProfileName, StringComparison.OrdinalIgnoreCase),
                        CarriesExtras = p.Keybinds != null || p.AppSettings != null,
                        ContextMenuStrip = _profileCardMenu,
                        TabStop = true
                    };
                    card.AccessibleName = p.Name;
                    card.AccessibleDescription = ProfileUsageText(p);

                    string name = p.Name;
                    card.Activated += (s, e) => ActivateProfileByName(name);
                    card.FavoriteToggled += (s, e) => ToggleProfileFavorite(name);

                    _profileCards.Add(card);
                    _profilesPage.Controls.Add(card);
                }
            }
            finally
            {
                _profilesPage.ResumeLayout(false);
            }

            ApplyThemeToProfileCards();
            LayoutProfileGrid();

            int total = _profiles != null ? _profiles.Count : 0;
            if (_profileCountLabel != null)
            {
                _profileCountLabel.Text = shown.Count == total
                    ? Localization.F("{0} profiles", total.ToString())
                    : Localization.F("{0} of {1} profiles", shown.Count.ToString(), total.ToString());
            }

            if (_profileEmptyLabel != null)
            {
                _profileEmptyLabel.Visible = shown.Count == 0;
                _profileEmptyLabel.Text = total == 0
                    ? Localization.T("No profiles yet. Create one to get started.")
                    : Localization.T("No profile matches that search.");
            }

            UpdateProfileRecycleButton();
        }

        /// <summary>Positions the cards into as many columns as the width allows.</summary>
        private void LayoutProfileGrid()
        {
            if (_profilesPage == null) { return; }

            // Use the page's client width, less the left margin and a right margin
            // wide enough that the vertical scrollbar never sits on top of a card.
            int available = _profilesPage.ClientSize.Width - (ProfileGridLeft * 2) - 8;
            if (available < ProfileCard.CardWidth) { available = ProfileCard.CardWidth; }

            int step = ProfileCard.CardWidth + ProfileGridGap;
            int columns = Math.Max(1, (available + ProfileGridGap) / step);

            // Child coordinates on an AutoScroll page are relative to the SCROLLED
            // origin, so a relayout while scrolled down would otherwise plant every
            // card one viewport too low. AutoScrollPosition is zero or negative.
            Point origin = _profilesPage.AutoScrollPosition;

            for (int i = 0; i < _profileCards.Count; i++)
            {
                int col = i % columns;
                int rowIndex = i / columns;
                _profileCards[i].Left = origin.X + ProfileGridLeft + (col * step);
                _profileCards[i].Top = origin.Y + _profileGridTop +
                                       (rowIndex * (ProfileCard.CardHeight + ProfileGridGap));
            }

            if (_profileEmptyLabel != null)
            {
                _profileEmptyLabel.Left = origin.X + ProfileGridLeft;
                _profileEmptyLabel.Top = origin.Y + _profileGridTop + 12;
            }
        }

        /// <summary>
        /// Re-marks which card is the active profile without rebuilding the grid.
        /// Switching profiles is common — the Clicker combo, the tray menu and the
        /// Ctrl+Tab cycle all do it — and tearing down every card to move one ring
        /// would flicker the whole library each time.
        /// </summary>
        private void RefreshProfileGridActive()
        {
            for (int i = 0; i < _profileCards.Count; i++)
            {
                var card = _profileCards[i];
                bool active = string.Equals(card.ProfileName, _currentProfileName,
                    StringComparison.OrdinalIgnoreCase);
                if (card.IsActive != active)
                {
                    card.IsActive = active;
                    card.Invalidate();
                }
            }
        }

        private void UpdateProfileRecycleButton()
        {
            if (_profileRecycleButton == null) { return; }
            int binned = _profiles?.RecycleBin?.Count ?? 0;
            _profileRecycleButton.Text = binned > 0
                ? Localization.F("Recycle bin ({0})", binned.ToString())
                : Localization.T("Recycle bin");
            _profileRecycleButton.Enabled = binned > 0;
        }

        /// <summary>
        /// Themes the card context menu.
        ///
        /// The renderer alone is NOT enough — it paints the items, but the strip's own
        /// BackColor and ForeColor still come from the system, which is why the menu
        /// opened as a light-grey Windows menu in the middle of a dark app. Every other
        /// themed menu in Tempo (the tray menu, the captions menu) sets all three, and
        /// so does this one. The renderer owns an animation timer, so the old one is
        /// disposed rather than dropped.
        /// </summary>
        private void ApplyThemeToProfileMenu()
        {
            if (_profileCardMenu == null || _theme == null) { return; }

            try
            {
                (_profileCardMenu.Renderer as ThemedMenuRenderer)?.Dispose();
                _profileCardMenu.Renderer = new ThemedMenuRenderer(_theme);
                _profileCardMenu.BackColor = _theme.Surface;
                _profileCardMenu.ForeColor = _theme.Text;
            }
            catch { }
        }

        private void ApplyThemeToProfileCards()
        {
            if (_theme == null) { return; }

            ApplyThemeToProfileMenu();

            foreach (var card in _profileCards)
            {
                card.CardColor = _theme.Surface;
                card.TextColor = _theme.Text;
                card.MutedColor = _theme.TextMuted;
                card.AccentColor = _theme.Accent;
                card.BorderColor = _theme.Border;
                card.Invalidate();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Presentation helpers
        // ─────────────────────────────────────────────────────────────────────

        private static string ProfileCategoryName(ProfileCategory c)
        {
            switch (c)
            {
                case ProfileCategory.Gaming: return Localization.T("Gaming");
                case ProfileCategory.Work: return Localization.T("Work");
                case ProfileCategory.Productivity: return Localization.T("Productivity");
                default: return Localization.T("Custom");
            }
        }

        /// <summary>
        /// The card's footer line. Deliberately says "never used" rather than
        /// "0 times" — a profile you have not tried yet is a different thing from
        /// one you tried and abandoned, and the grid is where you would notice.
        /// </summary>
        private static string ProfileUsageText(ClickProfile p)
        {
            if (p == null) { return ""; }
            if (p.TimesUsed <= 0) { return Localization.T("never used"); }

            string used = p.TimesUsed == 1
                ? Localization.T("used once")
                : Localization.F("used {0}×", p.TimesUsed.ToString());

            if (p.TotalRuntimeSeconds <= 0) { return used; }
            return used + "  ·  " + FormatProfileRuntime(p.TotalRuntimeSeconds);
        }

        private static string FormatProfileRuntime(long seconds)
        {
            if (seconds < 60) { return Localization.F("{0}s", seconds.ToString()); }
            if (seconds < 3600)
            {
                return Localization.F("{0}m", (seconds / 60).ToString());
            }

            long hours = seconds / 3600;
            long minutes = (seconds % 3600) / 60;
            return minutes > 0
                ? Localization.F("{0}h {1}m", hours.ToString(), minutes.ToString())
                : Localization.F("{0}h", hours.ToString());
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Commands
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Makes a profile the active one: loads it into the Clicker tab, records
        /// the use, and applies whatever optional snapshots it carries.
        /// </summary>
        private void ActivateProfileByName(string name)
        {
            if (string.IsNullOrEmpty(name) || _profiles == null) { return; }

            var profile = _profiles.GetByName(name);
            if (profile == null) { return; }

            // Activating rebuilds the grid, which destroys and recreates the cards.
            // A second activation arriving while that is in flight would bump the use
            // count for a click nobody made, so one at a time.
            if (_activatingProfile) { return; }
            _activatingProfile = true;

            try
            {
                LoadProfileIntoUi(profile);
                SelectProfileInCombo(profile.Name);
                _settings.LastProfileName = profile.Name;

                // The use count and last-used stamp are history, so they follow the
                // same privacy switch as the session history: with recording off,
                // activating a profile leaves no trace.
                if (_settings.RecordSessionHistory)
                {
                    _profiles.MarkUsed(profile.Name);
                }

                ApplyProfileExtras(profile);
                _profiles.Save();

                // ApplyProfileExtras writes the profile's keybinds and appearance into
                // _settings, and LastProfileName changed above. None of that is worth
                // anything if it is not written down — without this the theme a profile
                // restored would be back to the old one after a restart.
                try { SettingsManager.Save(_settings); } catch { }
            }
            finally
            {
                _activatingProfile = false;
            }

            RefreshProfileGrid();
            Logger.Info("[profiles] switched to '" + profile.Name + "'.");
        }

        /// <summary>
        /// Applies a profile's optional keybind and appearance snapshots.
        ///
        /// Both are null unless the user ticked the box in the details dialog, and a
        /// null one is left strictly alone — a profile never changes something it
        /// was not asked to carry.
        /// </summary>
        private void ApplyProfileExtras(ClickProfile profile)
        {
            if (profile == null || _settings == null) { return; }

            if (profile.Keybinds != null)
            {
                profile.Keybinds.ApplyTo(_settings);

                // Push the new bindings into the Keybinds tab's fields without letting
                // the change handler run: it exists to resolve conflicts the user just
                // created by typing, and a wholesale profile swap would have it clear
                // rows against each other as they are filled in.
                _suppressKeybindEvents = true;
                try
                {
                    foreach (var binding in _settings.Bindings)
                    {
                        if (binding == null || binding.Hotkey == null) { continue; }
                        if (_bindingControls.TryGetValue(binding.Action, out HotkeyCaptureControl ctl) &&
                            ctl != null && !ctl.IsDisposed)
                        {
                            ctl.Hotkey = binding.Hotkey.Clone();
                        }
                    }
                }
                finally
                {
                    _suppressKeybindEvents = false;
                }

                ApplyHotkeysFromSettings();
                HighlightConflicts();
                Logger.Info("[profiles] applied the keybinds saved with '" + profile.Name + "'.");
            }

            if (profile.AppSettings != null)
            {
                profile.AppSettings.ApplyTo(_settings);
                LoadSettingsIntoUi();
                ApplyThemeToEverything();
                Logger.Info("[profiles] applied the appearance saved with '" + profile.Name + "'.");
            }
        }

        private void ToggleProfileFavorite(string name)
        {
            if (string.IsNullOrEmpty(name) || _profiles == null) { return; }
            _profiles.ToggleFavorite(name);
            _profiles.Save();
            RefreshProfileGrid();
        }

        private void OnProfileLibNew(object sender, EventArgs e)
        {
            var profile = new ClickProfile(Localization.T("New Profile"))
            {
                IntervalMilliseconds = 100
            };

            // Offer the details editor straight away: a new profile with no name of
            // its own is the thing the old combo-only flow produced over and over.
            if (!ProfileDetailsForm.Edit(this, _theme, profile, _settings)) { return; }

            _profiles.Add(profile);
            _profiles.Save();
            RefreshProfileCombo();
            ActivateProfileByName(profile.Name);
        }

        private void OnProfileLibEdit(string name)
        {
            var profile = _profiles?.GetByName(name);
            if (profile == null) { return; }

            string oldName = profile.Name;
            if (!ProfileDetailsForm.Edit(this, _theme, profile, _settings)) { return; }

            // The dialog writes the new name straight onto the object, which would
            // happily create a second "Fishing". Check for the clash here, where the
            // old name is still known, and put it back rather than silently merging.
            if (!string.Equals(oldName, profile.Name, StringComparison.OrdinalIgnoreCase))
            {
                var clash = _profiles.GetByName(profile.Name);
                if (clash != null && !ReferenceEquals(clash, profile))
                {
                    ShowWarning(Localization.F("A profile called '{0}' already exists.", profile.Name));
                    profile.Name = oldName;
                }
                else if (string.Equals(_currentProfileName, oldName, StringComparison.OrdinalIgnoreCase))
                {
                    // The active profile was renamed underneath us; follow it.
                    _currentProfileName = profile.Name;
                    _settings.LastProfileName = profile.Name;
                }
            }

            _profiles.Save();
            RefreshProfileCombo();
            if (string.Equals(_currentProfileName, profile.Name, StringComparison.OrdinalIgnoreCase))
            {
                SelectProfileInCombo(profile.Name);
            }
        }

        private void OnProfileLibDuplicate(string name)
        {
            var copy = _profiles?.Duplicate(name);
            if (copy == null) { return; }
            _profiles.Save();
            RefreshProfileCombo();
        }

        private void OnProfileLibDelete(string name)
        {
            if (string.IsNullOrEmpty(name) || _profiles == null) { return; }

            var confirm = MessageBox.Show(this,
                Localization.F("Delete profile '{0}'? You can restore it from the recycle bin.", name),
                "Tempo", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) { return; }

            bool wasActive = string.Equals(name, _currentProfileName, StringComparison.OrdinalIgnoreCase);
            _profiles.Remove(name);
            _profiles.Save();
            RefreshProfileCombo();

            // Removing the active profile leaves the Clicker tab showing settings that
            // no longer belong to anything, so move it onto whatever is left. The
            // manager guarantees the library is never empty.
            if (wasActive && _profiles.Count > 0)
            {
                ActivateProfileByName(_profiles.Profiles[0].Name);
            }
        }

        private void OnProfileLibExport(string name)
        {
            var profile = _profiles?.GetByName(name);
            if (profile == null) { return; }

            string json = ProfileManager.ExportToJson(profile);
            if (string.IsNullOrEmpty(json))
            {
                ShowWarning(Localization.T("Couldn't prepare that profile for export."));
                return;
            }

            using (var dlg = new SaveFileDialog
            {
                Title = Localization.T("Export profile"),
                Filter = Localization.T("Tempo profile") + " (*.json)|*.json",
                FileName = SafeProfileFileName(profile.Name) + ".json",
                OverwritePrompt = true
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) { return; }

                try
                {
                    File.WriteAllText(dlg.FileName, json);
                    Logger.Info("[profiles] exported '" + profile.Name + "'.");
                    ShowInfo(Localization.F("Exported '{0}'.", profile.Name));
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to export a profile.", ex);
                    ShowWarning(Localization.F("Couldn't write the file: {0}", ex.Message));
                }
            }
        }

        private void OnProfileLibImport(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog
            {
                Title = Localization.T("Import profile"),
                Filter = Localization.T("Tempo profile") + " (*.json)|*.json|" +
                         Localization.T("All files") + " (*.*)|*.*",
                Multiselect = false
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) { return; }

                string json;
                try
                {
                    json = File.ReadAllText(dlg.FileName);
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to read a profile file.", ex);
                    ShowWarning(Localization.F("Couldn't read the file: {0}", ex.Message));
                    return;
                }

                var parsed = ProfileManager.ParseExported(json, out string error);
                if (parsed == null)
                {
                    ShowWarning(Localization.T(error ?? "That isn't a valid Tempo profile."));
                    return;
                }

                // A name clash is the user's call, not a silent overwrite of something
                // they may still be using.
                var conflict = ProfileManager.ImportConflict.Rename;
                if (_profiles.Exists(parsed.Name))
                {
                    var answer = MessageBox.Show(this,
                        Localization.F(
                            "You already have a profile called '{0}'. Replace it? " +
                            "Choose No to keep both.", parsed.Name),
                        "Tempo", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

                    if (answer == DialogResult.Cancel) { return; }
                    conflict = answer == DialogResult.Yes
                        ? ProfileManager.ImportConflict.Overwrite
                        : ProfileManager.ImportConflict.Rename;
                }

                var stored = _profiles.Import(parsed, conflict);
                if (stored == null) { return; }

                _profiles.Save();
                RefreshProfileCombo();
                Logger.Info("[profiles] imported '" + stored.Name + "'.");

                // Overwriting the profile that is currently loaded leaves the Clicker
                // tab showing the settings of a profile that no longer exists. Re-load
                // it so the two agree.
                if (string.Equals(stored.Name, _currentProfileName, StringComparison.OrdinalIgnoreCase))
                {
                    LoadProfileIntoUi(stored);
                }

                ShowInfo(Localization.F("Imported '{0}'.", stored.Name));
            }
        }

        private void OnProfileLibRecycleBin(object sender, EventArgs e)
        {
            if (_profiles == null) { return; }

            if (ProfileRecycleBinForm.Show(this, _theme, _profiles))
            {
                RefreshProfileCombo();
            }
            else
            {
                UpdateProfileRecycleButton();
            }
        }

        /// <summary>Strips characters Windows will not accept in a file name.</summary>
        private static string SafeProfileFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) { return "profile"; }
            var chars = name.ToCharArray();
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0) { chars[i] = '_'; }
            }

            // A name made entirely of characters Windows rejects (or of trailing dots
            // and spaces, which Trim removes) would leave nothing at all, and the
            // dialog would open on a file called ".json".
            string safe = new string(chars).Trim(' ', '.');
            return safe.Length > 0 ? safe : "profile";
        }
    }
}
