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

        /// <summary>Stops a switch from re-entering itself (a prompt below it runs a message loop).</summary>
        private bool _activatingProfile;

        private Button _profileNewButton;
        private Button _profileImportButton;
        private Label _profileHintLabel;

        /// <summary>Where a profile switch came from. It decides what happens to unsaved edits.</summary>
        private enum ProfileSwitchSource
        {
            /// <summary>The Clicker dropdown or the Profiles tab: the window is in front, so it can ask.</summary>
            Ui,
            /// <summary>The Next/Previous-profile hotkeys: no dialog, it would take focus from the game.</summary>
            Hotkey,
            /// <summary>The tray menu: the window may be hidden, so no dialog either.</summary>
            Tray
        }

        /// <summary>True while a profile hotkey drives the Clicker dropdown (see CycleProfile).</summary>
        private bool _profileSwitchByHotkey;

        /// <summary>
        /// Unsaved Clicker-tab edits of profiles that were switched away from without a question
        /// (by a hotkey or from the tray), by profile name. They come back, still marked unsaved,
        /// when that profile is chosen again. Kept for this session only.
        ///
        /// Every switch used to reload the Clicker tab from the saved profile and drop whatever
        /// had been changed there. The "Unsaved changes" note warned about exactly this, and no
        /// way of switching ever asked: one press of Next profile in a game threw the edits away.
        /// </summary>
        private readonly Dictionary<string, ClickProfile> _profileDrafts =
            new Dictionary<string, ClickProfile>(StringComparer.OrdinalIgnoreCase);

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
                // As wide as its longest choice: German "Am häufigsten verwendet" needed 143 px of the
                // 136 it had. The tick box after it moves along.
                if (_profileSortCombo is FlatComboBox sortBox) { sortBox.Width = sortBox.WidthForChoices96(160); }
            }
            finally
            {
                _suppressProfileFilterEvents = false;
            }

            _profileFavOnly = UiFactory.Check("Favourites only", Math.Max(560, _profileSortCombo.Right + 14), row + 3);
            _profileFavOnly.CheckedChanged += OnProfileFilterChanged;
            page.Controls.Add(_profileFavOnly);

            // ── Action row ────────────────────────────────────────────────────
            row += 36;

            var newBtn = UiFactory.PrimaryButton("New profile", 16, row, 122, 30, _theme);
            newBtn.Click += OnProfileLibNew;
            page.Controls.Add(newBtn);
            _profileNewButton = newBtn;

            var importBtn = UiFactory.Button("Import…", 146, row, 96, 30);
            importBtn.Click += OnProfileLibImport;
            page.Controls.Add(importBtn);
            _profileImportButton = importBtn;

            _profileRecycleButton = UiFactory.Button("Recycle bin", 250, row, 122, 30);
            _profileRecycleButton.Click += OnProfileLibRecycleBin;
            page.Controls.Add(_profileRecycleButton);

            // Also where the tab says why a card will not switch right now (RefreshProfileLock).
            var hint = UiFactory.Caption(ProfileHintText, 386, row + 8);
            hint.ForeColor = _theme.TextMuted;
            page.Controls.Add(hint);
            _profileHintLabel = hint;

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
            LayoutFitter.DeclareOverlay(_profileEmptyLabel,
                "shown in the card grid's place only when a search matches no card");

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

            var switchItem = new ToolStripMenuItem(Localization.T("Switch to this profile"));
            switchItem.Click += (s, e) => WithMenuProfile(ActivateProfileByName);
            _profileCardMenu.Items.Add(switchItem);

            // The star on the card is a mouse-only target; this is the same toggle for the
            // keyboard (Shift+F10 / the menu key on a focused card) and for anyone who never
            // noticed the star. Its wording follows the card it was opened on.
            var favItem = new ToolStripMenuItem(Localization.T("Add to favourites"));
            favItem.Click += (s, e) => WithMenuProfile(ToggleProfileFavorite);
            _profileCardMenu.Items.Add(favItem);
            _profileCardMenu.Items.Add(new ToolStripSeparator());
            _profileCardMenu.Items.Add(Localization.T("Edit details…"), null,
                (s, e) => WithMenuProfile(OnProfileLibEdit));
            _profileCardMenu.Items.Add(Localization.T("Duplicate"), null,
                (s, e) => WithMenuProfile(OnProfileLibDuplicate));
            _profileCardMenu.Items.Add(Localization.T("Export…"), null,
                (s, e) => WithMenuProfile(OnProfileLibExport));
            _profileCardMenu.Items.Add(new ToolStripSeparator());
            var deleteItem = new ToolStripMenuItem(Localization.T("Delete"));
            deleteItem.Click += (s, e) => WithMenuProfile(OnProfileLibDelete);
            _profileCardMenu.Items.Add(deleteItem);

            _profileCardMenu.Opening += (s, e) =>
            {
                var card = _profileCardMenu.SourceControl as ProfileCard;
                favItem.Text = Localization.T(card != null && card.Favorite
                    ? "Remove from favourites" : "Add to favourites");

                // Mid-run, the same rule as the Clicker tab's dropdown and the tray: no switching,
                // and the loaded profile cannot be deleted from under the run. "Switch" to the
                // profile already loaded does nothing, so it says so by being greyed.
                bool locked = ProfileLockReason() != null;
                bool active = card != null && card.IsActive;
                switchItem.Enabled = !locked && !active;
                deleteItem.Enabled = !(locked && active);
            };
        }

        private const string ProfileHintText = "Click a card to switch to it. Right-click for more.";

        /// <summary>
        /// Why profiles cannot be switched right now, or null when they can. One rule for the
        /// Clicker tab's dropdown, the Profiles tab and the tray: while Tempo is clicking, playing
        /// a macro or recording one. (The Next/Previous-profile hotkeys still switch mid-run, on
        /// purpose — see NoteSwitchDuringRun.)
        ///
        /// Only the dropdown and the tray were locked. A card on the Profiles tab switched mid-run
        /// — handing the running clicker another profile's settings — and "New profile",
        /// "Import…" (replacing the loaded profile) and deleting the loaded profile all did the
        /// same thing by another route.
        /// </summary>
        private string ProfileLockReason()
        {
            if (_liveRecording) { return "Stop recording to switch profile"; }
            if (_player != null && _player.IsPlaying) { return "Stop the macro to switch profile"; }
            if (_engine != null && (_engine.IsRunning || _engine.IsPaused)) { return "Stop clicking to switch profile"; }
            return null;
        }

        /// <summary>The loaded profile's name for display, falling back to the one remembered for start-up.</summary>
        private string ActiveProfileName()
        {
            if (!string.IsNullOrWhiteSpace(_currentProfileName)) { return _currentProfileName; }
            if (_settings != null && !string.IsNullOrEmpty(_settings.LastProfileName)) { return _settings.LastProfileName; }
            return "Default";
        }

        /// <summary>Greys what would switch or replace the loaded profile, and says why. Called from RefreshBusyLock.</summary>
        private void RefreshProfileLock()
        {
            string reason = ProfileLockReason();
            if (_profileNewButton != null) { _profileNewButton.Enabled = reason == null; }
            if (_profileImportButton != null) { _profileImportButton.Enabled = reason == null; }
            if (_profileHintLabel != null)
            {
                string text = Localization.T(reason ?? ProfileHintText);
                if (!string.Equals(_profileHintLabel.Text, text, StringComparison.Ordinal))
                {
                    _profileHintLabel.Text = text;
                }
            }
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

                // Every order ends in the name. List.Sort is not stable, so two profiles
                // with the same use count (every never-used one, for a start) could swap
                // places on any rebuild — and a rebuild follows every click on a card, so
                // cards you did not touch jumped around under the pointer.
                int byName = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
                switch (sort)
                {
                    case 1:
                        return byName;
                    case 2:
                        int byCount = b.TimesUsed.CompareTo(a.TimesUsed);
                        return byCount != 0 ? byCount : byName;
                    case 3:
                        int byAge = b.CreatedUtc.CompareTo(a.CreatedUtc);
                        return byAge != 0 ? byAge : byName;
                    default:
                        // Recently used, with never-used profiles falling to the back.
                        int byUse = b.LastUsedUtc.CompareTo(a.LastUsedUtc);
                        return byUse != 0 ? byUse : byName;
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

            // Which card had the keyboard. Every card is about to be replaced, and pressing
            // Enter on one (switch) or toggling its star is exactly what triggers this — so
            // focus used to vanish with the old card and a keyboard user was sent back to
            // the start of the tab order after every action.
            string focusedName = null;
            foreach (var card in _profileCards)
            {
                if (card.Focused) { focusedName = card.ProfileName; break; }
            }

            _profilesPage.SuspendLayout();
            try
            {
                foreach (var card in _profileCards)
                {
                    TipStyle.Set(_tips, card, null);   // the tooltip lets go of it too
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
                    card.TipText = ProfileCardTip(p);
                    // At the window's scale: the grid is rebuilt long after the window has scaled,
                    // and new cards came out at 100% size on a 125%/150% laptop, the first row on
                    // top of the New profile / Import buttons (scratchpad/mainscan, "dpi").
                    float k = AppliedWindowScale;
                    card.Size = new Size((int)Math.Round(ProfileCard.CardWidth * k), (int)Math.Round(ProfileCard.CardHeight * k));

                    string name = p.Name;
                    card.Activated += (s, e) => ActivateProfileByName(name);
                    card.FavoriteToggled += (s, e) => ToggleProfileFavorite(name);
                    card.KeyDown += (s, e) => OnProfileCardKey(name, e);

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
            RefreshProfileCardTips();

            // Hand the keyboard back to the same profile's new card (wherever the sort has
            // put it now).
            if (focusedName != null)
            {
                var again = _profileCards.Find(c =>
                    string.Equals(c.ProfileName, focusedName, StringComparison.OrdinalIgnoreCase));
                if (again != null && again.CanFocus) { again.Focus(); }
            }

            int total = _profiles != null ? _profiles.Count : 0;
            if (_profileCountLabel != null)
            {
                // "1 profiles" is the sort of thing that sits on screen all day. The
                // singular gets its own key rather than an "(s)" suffix, matching how
                // ProfileManager.Load already picks between "profile" and "profiles".
                _profileCountLabel.Text = shown.Count == total
                    ? Localization.F(total == 1 ? "{0} profile" : "{0} profiles", total.ToString())
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

            // Every number here is a 100% one, multiplied by the scale the window has applied:
            // this runs again on every rebuild and resize, long after the window scaled, and at
            // 100% numbers the grid sat on the buttons above it on a 125%/150% laptop.
            float k = AppliedWindowScale;
            int gridLeft = (int)Math.Round(ProfileGridLeft * k);
            int gap = (int)Math.Round(ProfileGridGap * k);
            int cardWidth = (int)Math.Round(ProfileCard.CardWidth * k);
            int cardHeight = (int)Math.Round(ProfileCard.CardHeight * k);
            int gridTop = (int)Math.Round(_profileGridTop * k);

            // Use the page's client width, less the left margin and a right margin
            // wide enough that the vertical scrollbar never sits on top of a card.
            int available = _profilesPage.ClientSize.Width - (gridLeft * 2) - (int)Math.Round(8 * k);
            if (available < cardWidth) { available = cardWidth; }

            int step = cardWidth + gap;
            int columns = Math.Max(1, (available + gap) / step);

            // Child coordinates on an AutoScroll page are relative to the SCROLLED
            // origin, so a relayout while scrolled down would otherwise plant every
            // card one viewport too low. AutoScrollPosition is zero or negative.
            Point origin = _profilesPage.AutoScrollPosition;

            for (int i = 0; i < _profileCards.Count; i++)
            {
                int col = i % columns;
                int rowIndex = i / columns;
                _profileCards[i].Left = origin.X + gridLeft + (col * step);
                _profileCards[i].Top = origin.Y + gridTop + (rowIndex * (cardHeight + gap));
            }

            if (_profileEmptyLabel != null)
            {
                _profileEmptyLabel.Left = origin.X + gridLeft;
                _profileEmptyLabel.Top = origin.Y + gridTop + (int)Math.Round(12 * k);
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

        /// <summary>
        /// A card's tooltip: the whole name and description — a card shows about 20 characters
        /// of a name the editors allow 60 of, and cut both off with "…" and no way to read the
        /// rest — and what switching to it puts back besides the clicking setup.
        /// </summary>
        private static string ProfileCardTip(ClickProfile p)
        {
            if (p == null) { return ""; }
            var sb = new System.Text.StringBuilder(p.Name ?? "");
            string desc = Localization.T(p.Description ?? "");
            if (!string.IsNullOrWhiteSpace(desc)) { sb.Append('\n').Append(desc.Trim()); }
            if (p.Keybinds != null || p.AppSettings != null)
            {
                sb.Append("\n\n").Append(Localization.T(
                    p.Keybinds != null && p.AppSettings != null
                        ? "Switching to it also puts back its saved keybinds, theme, overlay and caption look."
                        : p.Keybinds != null
                            ? "Switching to it also puts back its saved keybinds."
                            : "Switching to it also puts back its saved theme, overlay and caption look."));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Hands each card's tip to the main tooltip. Also called once the tooltips exist: the
        /// first grid is built during start-up, before them, and its cards had no tip at all.
        /// </summary>
        private void RefreshProfileCardTips()
        {
            if (_tips == null) { return; }
            foreach (var card in _profileCards)
            {
                if (card != null && !card.IsDisposed) { TipStyle.Set(_tips, card, card.TipText); }
            }
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

        /// <summary>A card, the card menu or a new profile: switched from the Profiles tab.</summary>
        private void ActivateProfileByName(string name)
        {
            ActivateProfileByName(name, ProfileSwitchSource.Ui);
        }

        /// <summary>
        /// Switches from the Profiles tab or the tray, which are locked while Tempo is busy — as
        /// the Clicker tab's dropdown always was (see ProfileLockReason).
        /// </summary>
        private void ActivateProfileByName(string name, ProfileSwitchSource source)
        {
            if (string.IsNullOrEmpty(name) || _profiles == null) { return; }

            string locked = ProfileLockReason();
            if (locked != null)
            {
                Logger.Info("[profiles] not switching to '" + name + "': " + locked + ".");
                RefreshProfileLock();   // the tab says why
                return;
            }

            SwitchToProfile(name, source);
        }

        /// <summary>
        /// THE way to make a profile the loaded one; the Clicker dropdown, the profile hotkeys,
        /// the Profiles tab, the tray, New, Duplicate and Delete all come through here. Loads it
        /// into the Clicker and Multi-Point tabs, brings back any unsaved edits kept for it,
        /// remembers it for the next start and applies the keybinds and look saved with it.
        ///
        /// Before, each way of switching did its own subset. New, Duplicate and Delete on the
        /// Clicker tab loaded the next profile directly: its keybinds and look were not applied
        /// and it was not remembered for the next start, so the tray kept ticking the old one
        /// and a restart reopened it. The Profiles tab counted a use and nothing else did, and a
        /// hotkey switch into clashing keybinds put a dialog over the game.
        ///
        /// Returns false when nothing changed: the profile is gone, a switch is already under
        /// way, or the user chose to keep working on unsaved edits.
        ///
        /// <paramref name="askAboutEdits"/> false: the edits are already dealt with — the caller
        /// asked (New, Duplicate), or they belonged to a profile that is gone (Delete) — and the
        /// target is loaded even if it has the same name (deleting the only "Default" makes a
        /// fresh one).
        /// </summary>
        private bool SwitchToProfile(string name, ProfileSwitchSource source, bool askAboutEdits = true)
        {
            if (string.IsNullOrEmpty(name) || _profiles == null) { return false; }

            var profile = _profiles.GetByName(name);
            if (profile == null) { return false; }

            // One at a time: the unsaved-edits question runs a message loop, and a hotkey or a
            // second click arriving under it would start another switch half-way through this one.
            if (_activatingProfile) { return false; }

            bool same = string.Equals(profile.Name, _currentProfileName, StringComparison.OrdinalIgnoreCase);
            _activatingProfile = true;
            try
            {
                if (same && askAboutEdits)
                {
                    // Already loaded. Reloading it threw the Clicker tab's unsaved edits away
                    // for a click on the card that was already ringed as active.
                    SelectProfileInCombo(profile.Name);
                    return true;
                }

                if (askAboutEdits && !HandleUnsavedEditsBeforeSwitch(source))
                {
                    return false;
                }

                // Asking may have saved (and renamed) the outgoing profile, which rebuilds the
                // library — so look the target up again rather than trusting the old reference.
                profile = _profiles.GetByName(name);
                if (profile == null) { return false; }

                LoadProfileIntoUi(profile);
                RestoreDraftIfAny(profile);
                SelectProfileInCombo(profile.Name);
                NoteSwitchDuringRun(profile.Name);
                _settings.LastProfileName = profile.Name;
                ApplyProfileExtras(profile, quiet: source != ProfileSwitchSource.Ui);

                // ApplyProfileExtras writes the profile's keybinds and look into _settings, and
                // LastProfileName changed above. Without saving, the theme a profile restored
                // was back to the old one after a restart.
                try { SettingsManager.Save(_settings); } catch (Exception ex) { Logger.Swallow("profiles", ex); }
            }
            finally
            {
                _activatingProfile = false;
            }

            Logger.Info("[profiles] switched to '" + profile.Name + "' (" + source + ").");
            return true;
        }

        /// <summary>True when the Clicker/Multi-Point tabs differ from the loaded profile as saved.</summary>
        private bool ProfileHasUnsavedEdits()
        {
            if (_profileSnapshotJson == null || string.IsNullOrEmpty(_currentProfileName)) { return false; }
            string now = SerializeProfileSafe();
            return now != null && !string.Equals(now, _profileSnapshotJson, StringComparison.Ordinal);
        }

        /// <summary>
        /// Unsaved edits of the loaded profile, before another is loaded over them. From the
        /// window: asked — save, discard, or stay. From a hotkey or the tray: kept as a draft
        /// that comes back with the profile, because a dialog there would take focus from the
        /// game or pop up over a hidden window. False means stay.
        /// </summary>
        private bool HandleUnsavedEditsBeforeSwitch(ProfileSwitchSource source)
        {
            if (!ProfileHasUnsavedEdits()) { return true; }

            if (source != ProfileSwitchSource.Ui)
            {
                StashProfileDraft();
                return true;
            }

            var answer = MessageBox.Show(this,
                Localization.F("“{0}” has unsaved changes. Save them before switching?", _currentProfileName),
                "Tempo", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) { return false; }
            if (answer == DialogResult.Yes)
            {
                // A save that fails (validation, a name clash) has already said why; stay put
                // with the edits rather than lose them.
                return SaveCurrentProfile(announce: false);
            }

            // Don't save: the edits go, and so does any older draft of them.
            _profileDrafts.Remove(_currentProfileName);
            return true;
        }

        /// <summary>Keeps the loaded profile's unsaved edits to come back with it (see _profileDrafts).</summary>
        private void StashProfileDraft()
        {
            if (string.IsNullOrEmpty(_currentProfileName)) { return; }
            try
            {
                _profileDrafts[_currentProfileName] = BuildProfileFromUi();
                Logger.Info("[profiles] kept the unsaved changes to '" + _currentProfileName
                            + "' for when it is chosen again.");
            }
            catch (Exception ex)
            {
                Logger.Swallow("profiles.StashProfileDraft", ex);
            }
        }

        /// <summary>
        /// Puts a kept draft back over the profile just loaded, still marked unsaved: the saved
        /// profile stays the baseline, so Save and the unsaved note work as if it had never left.
        /// </summary>
        private bool RestoreDraftIfAny(ClickProfile profile)
        {
            if (profile == null || !_profileDrafts.TryGetValue(profile.Name, out ClickProfile draft) || draft == null)
            {
                return false;
            }
            _profileDrafts.Remove(profile.Name);

            LoadProfileIntoUi(draft, asBaseline: false);
            // The draft carries the name as it was typed, which may not be saved yet; the loaded
            // profile is still this one.
            _currentProfileName = profile.Name;
            RefreshProfileGridActive();
            UpdateProfileDirty();
            Logger.Info("[profiles] brought back the unsaved changes to '" + profile.Name + "'.");
            return true;
        }

        /// <summary>
        /// Applies a profile's optional keybind and appearance snapshots.
        ///
        /// Both are null unless the user ticked the box in the details dialog, and a
        /// null one is left strictly alone — a profile never changes something it
        /// was not asked to carry.
        ///
        /// <paramref name="quiet"/>: switched by a hotkey or from the tray, so a clash is reported
        /// as a notification instead of a dialog that would take focus from the game.
        /// </summary>
        private void ApplyProfileExtras(ClickProfile profile, bool quiet = false)
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

                // The tab's fields have just been overwritten wholesale, so whatever the
                // user had half-typed in there is gone — and the "Unsaved changes" banner
                // was left standing, still promising work that no longer exists. Every
                // other writer of these controls clears it (LoadKeybindsIntoUi,
                // OnSaveKeybinds); this one did not, purely because it writes them under
                // _suppressKeybindEvents and so never reached the handler that does.
                if (_keybindsDirtyLabel != null)
                {
                    _keybindsDirtyLabel.Visible = false;
                }
                SetKeybindNotice(_keybindsTakenLabel, null, restack: false);   // it described the old binding

                ApplyHotkeysFromSettings();
                HighlightConflicts();   // re-stacks the notices

                // Say it out loud if the profile's keys now collide with something.
                //
                // ApplyTo MERGES — deliberately, because replacing wholesale would unbind
                // actions an older snapshot never knew about. But that means an incomplete
                // snapshot can leave the outgoing profile's key on an action it does not
                // mention, colliding with one it does restore. Profile activation is the
                // only path that can create a duplicate — the tab's auto-clear is
                // suppressed here on purpose — and it was the only one that never checked,
                // so the sole signal was a recoloured row on a tab the user is not looking
                // at, while the health panel blamed "another program" for a key Tempo
                // itself had taken twice.
                string clashes = FindConflicts();
                if (!string.IsNullOrEmpty(clashes))
                {
                    // "Only one responds": the hotkey manager leaves the second action on a shared
                    // combination unbound (it used to say a press would run both).
                    string message = Utils.Localization.F(
                        "The keybinds saved with “{0}” collide with combinations already in use. Only one "
                        + "action will respond to each of these keys — give the others a different one on "
                        + "the Keybinds tab:\n\n{1}", profile.Name, clashes);
                    Logger.Warn("[profiles] the keybinds saved with '" + profile.Name + "' clash: " + clashes.Replace("\n", "; "));
                    if (quiet)
                    {
                        TempoNotify(8000, "Tempo", message, ToolTipIcon.Warning, always: true);
                    }
                    else
                    {
                        ShowWarning(message);
                    }
                }

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

        /// <summary>
        /// Keys on a focused card, matching the macro list: Delete deletes (it still asks,
        /// and the recycle bin keeps a copy), F2 opens the details to rename.
        /// </summary>
        private void OnProfileCardKey(string name, KeyEventArgs e)
        {
            if (e == null || string.IsNullOrEmpty(name)) { return; }
            if (e.KeyCode == Keys.Delete && e.Modifiers == Keys.None)
            {
                e.Handled = true;
                OnProfileLibDelete(name);
            }
            else if (e.KeyCode == Keys.F2 && e.Modifiers == Keys.None)
            {
                e.Handled = true;
                OnProfileLibEdit(name);
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
            // It switches to the new profile, so it follows the switching rule (the button is
            // greyed as well; this covers a click already on its way).
            if (ProfileLockReason() != null) { RefreshProfileLock(); return; }

            var profile = new ClickProfile(Localization.T("New Profile"))
            {
                IntervalMilliseconds = 100
            };

            // Offer the details editor straight away: a new profile with no name of
            // its own is the thing the old combo-only flow produced over and over.
            if (!ProfileDetailsForm.Edit(this, _theme, profile, _settings)) { return; }

            // Then the loaded profile's unsaved edits, before anything is created: Cancel stays
            // on it and makes nothing.
            if (!HandleUnsavedEditsBeforeSwitch(ProfileSwitchSource.Ui)) { return; }

            _profiles.Add(profile);
            _profiles.Save();
            RefreshProfileCombo();
            SwitchToProfile(profile.Name, ProfileSwitchSource.Ui, askAboutEdits: false);
        }

        private void OnProfileLibEdit(string name)
        {
            var profile = _profiles?.GetByName(name);
            if (profile == null) { return; }

            string oldName = profile.Name;
            // Asked BEFORE the name can change under the Clicker tab (see FollowProfileRename).
            bool loaded = string.Equals(_currentProfileName, oldName, StringComparison.OrdinalIgnoreCase);
            bool hadEdits = loaded && ProfileHasUnsavedEdits();
            if (!ProfileDetailsForm.Edit(this, _theme, profile, _settings)) { return; }

            // The dialog writes the new name straight onto the object, which would
            // happily create a second "Fishing". Check for the clash here, where the
            // old name is still known, and put it back rather than silently merging.
            // Compared exactly: a change of case alone ("fishing" → "Fishing") is a rename too,
            // and the Clicker tab's Save used to put the old spelling straight back.
            if (!string.Equals(oldName, profile.Name, StringComparison.Ordinal))
            {
                // Every other profile, not GetByName: that returns the FIRST match, which is this
                // profile itself whenever it comes earlier in the list — and the clash went
                // unnoticed, leaving two profiles with one name.
                bool taken = false;
                foreach (var other in _profiles.Profiles)
                {
                    if (other != null && !ReferenceEquals(other, profile) &&
                        string.Equals(other.Name, profile.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        taken = true;
                        break;
                    }
                }

                if (taken)
                {
                    ShowWarning(Localization.F("A profile called '{0}' already exists.", profile.Name));
                    profile.Name = oldName;
                }
                else
                {
                    FollowProfileRename(oldName, profile.Name, hadEdits);
                }
            }

            _profiles.Save();
            RefreshProfileCombo();
            if (string.Equals(_currentProfileName, profile.Name, StringComparison.OrdinalIgnoreCase))
            {
                SelectProfileInCombo(profile.Name);
            }
        }

        /// <summary>
        /// Everything that holds a profile's name follows a rename made in the details dialog.
        ///
        /// The Clicker tab's name box did not. It kept the old name, so the next press of its Save
        /// — which saves under whatever the box says — renamed the profile straight back.
        /// </summary>
        private void FollowProfileRename(string oldName, string newName, bool hadEdits)
        {
            if (string.Equals(_currentProfileName, oldName, StringComparison.OrdinalIgnoreCase))
            {
                _currentProfileName = newName;
                _settings.LastProfileName = newName;

                _suppressProfileEvents = true;
                try { _profileNameText.Text = newName; }
                finally { _suppressProfileEvents = false; }

                // The saved state now carries the new name. With nothing else unsaved, the box and
                // the profile agree again; with edits waiting, they stay unsaved, but the name is
                // not one of them.
                if (!hadEdits)
                {
                    _profileSnapshotJson = SerializeProfileSafe();
                }
                else if (_profileSnapshotJson != null)
                {
                    try
                    {
                        var baseline = System.Text.Json.JsonSerializer.Deserialize<ClickProfile>(_profileSnapshotJson);
                        if (baseline != null)
                        {
                            baseline.Name = newName;
                            _profileSnapshotJson = System.Text.Json.JsonSerializer.Serialize(baseline);
                        }
                    }
                    catch (Exception ex) { Logger.Swallow("profiles.FollowProfileRename", ex); }
                }
                UpdateProfileDirty();
                ShowProfileNameInStatus(newName);
            }

            // A run in progress is credited by NAME when it stops. Renamed mid-run,
            // that name no longer matched anything and the run's time was dropped.
            if (string.Equals(_runProfileName, oldName, StringComparison.OrdinalIgnoreCase))
            {
                _runProfileName = newName;
            }

            // Unsaved edits kept for it follow it too; a name typed into them stays as typed.
            if (_profileDrafts.TryGetValue(oldName, out ClickProfile draft))
            {
                _profileDrafts.Remove(oldName);
                if (draft != null)
                {
                    if (string.Equals(draft.Name, oldName, StringComparison.Ordinal)) { draft.Name = newName; }
                    _profileDrafts[newName] = draft;
                }
            }
            Logger.Info("[profiles] renamed '" + oldName + "' to '" + newName + "'.");
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

            bool wasActive = string.Equals(name, _currentProfileName, StringComparison.OrdinalIgnoreCase);

            // The loaded profile, mid-run: deleting it switches to another (below), which is
            // what the lock is there to stop. The menu item is greyed; this is the Delete key.
            if (wasActive && ProfileLockReason() != null)
            {
                System.Media.SystemSounds.Beep.Play();
                RefreshProfileLock();
                return;
            }

            var confirm = MessageBox.Show(this,
                Localization.F("Delete profile '{0}'? You can restore it from the recycle bin.", name),
                "Tempo", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) { return; }

            _profiles.Remove(name);
            _profileDrafts.Remove(name);   // its unsaved edits went with it
            _profiles.Save();
            RefreshProfileCombo();

            // Removing the active profile leaves the Clicker tab showing settings that
            // no longer belong to anything, so move it onto whatever is left. The
            // manager guarantees the library is never empty. Nothing to ask about: the
            // edits belonged to the profile that is gone.
            if (wasActive && _profiles.Count > 0)
            {
                SwitchToProfile(_profiles.Profiles[0].Name, ProfileSwitchSource.Ui, askAboutEdits: false);
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
            // Replacing the loaded profile reloads the Clicker tab, which mid-run hands the running
            // clicker the imported settings — the switching rule applies.
            if (ProfileLockReason() != null) { RefreshProfileLock(); return; }

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

                // Keybinds and a look saved on someone else's PC come in with the file, and would
                // replace this user's own the first time they switch to it — their hotkeys moved,
                // their theme changed, from a file they only meant to try. Their call, asked here.
                if (parsed.Keybinds != null || parsed.AppSettings != null)
                {
                    var keep = MessageBox.Show(this,
                        Localization.F(
                            "“{0}” brings its own keybinds or theme and caption look, saved on the PC it " +
                            "came from. Switching to it would put them in place of yours.\n\n" +
                            "Keep them with the profile? Choose No to import only its clicking setup.",
                            parsed.Name),
                        "Tempo", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (keep == DialogResult.Cancel) { return; }
                    if (keep == DialogResult.No)
                    {
                        parsed.Keybinds = null;
                        parsed.AppSettings = null;
                    }
                }

                // A name clash is the user's call, not a silent overwrite of something
                // they may still be using.
                var conflict = ProfileManager.ImportConflict.Rename;
                if (_profiles.Exists(parsed.Name))
                {
                    var answer = MessageBox.Show(this,
                        Localization.F(
                            "You already have a profile called '{0}'. Replace it? The one you have now " +
                            "goes to the recycle bin. Choose No to keep both.", parsed.Name),
                        "Tempo", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

                    if (answer == DialogResult.Cancel) { return; }
                    conflict = answer == DialogResult.Yes
                        ? ProfileManager.ImportConflict.Overwrite
                        : ProfileManager.ImportConflict.Rename;
                }

                var stored = _profiles.Import(parsed, conflict);
                if (stored == null) { return; }

                // Edits kept for the setup that was just replaced belong to it, not to this one.
                if (conflict == ProfileManager.ImportConflict.Overwrite) { _profileDrafts.Remove(stored.Name); }

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

            // Each row stands for one binned profile OBJECT. The rows were keyed by name, and the
            // bin can hold several with one name (delete "Fishing", make another, delete that, or
            // replace one by importing) — restoring the older one brought back the newer.
            var rowProfiles = new Dictionary<string, ClickProfile>(StringComparer.Ordinal);
            bool changed = RecycleBinForm.Show(this, _theme,
                Localization.T("Recently deleted profiles"),
                Localization.T("Deleting a profile keeps a copy here so it can be brought back. " +
                               "Restoring one that clashes with a current profile gives it a new name."),
                new[] { Localization.T("Profile"), Localization.T("Category"), Localization.T("Last used") },
                new[] { 210, 110, 160 },
                Localization.T("The bin is empty."),
                "Permanently delete {0} profile(s)? This cannot be undone.",
                () => BuildProfileBinRows(rowProfiles),
                id =>
                {
                    if (!rowProfiles.TryGetValue(id, out ClickProfile binned)) { return false; }
                    var restored = _profiles.RestoreFromRecycleBin(binned);
                    if (restored == null) { return false; }
                    _profiles.Save();
                    Logger.Info("[profiles] restored '" + restored.Name + "' from the recycle bin.");
                    return true;
                },
                () =>
                {
                    int n = _profiles.RecycleBin.Count;
                    _profiles.EmptyRecycleBin();
                    _profiles.Save();
                    Logger.Info("[profiles] recycle bin emptied (" + n + " profile(s)).");
                });

            if (changed)
            {
                RefreshProfileCombo();
            }
            else
            {
                UpdateProfileRecycleButton();
            }
        }

        /// <summary>
        /// Rows for the shared recycle-bin window, newest deletion first. <paramref name="byId"/>
        /// is refilled with the profile each row's id stands for.
        /// </summary>
        private List<RecycleBinForm.Entry> BuildProfileBinRows(Dictionary<string, ClickProfile> byId)
        {
            var rows = new List<RecycleBinForm.Entry>();
            byId?.Clear();
            if (_profiles?.RecycleBin == null) { return rows; }

            // The bin appends, so walk it backwards.
            for (int i = _profiles.RecycleBin.Count - 1; i >= 0; i--)
            {
                var p = _profiles.RecycleBin[i];
                if (p == null) { continue; }
                string id = "bin" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (byId != null) { byId[id] = p; }
                rows.Add(new RecycleBinForm.Entry
                {
                    Id = id,
                    Cells = new[]
                    {
                        p.Name,
                        ProfileCategoryName(p.Category),
                        p.LastUsedUtc == DateTime.MinValue
                            ? Localization.T("never")
                            : p.LastUsedUtc.ToLocalTime().ToString("g")
                    }
                });
            }
            return rows;
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
