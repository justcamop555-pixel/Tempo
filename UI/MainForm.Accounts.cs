using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Persistence;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// The Roblox Account Manager page.
    ///
    /// Stores Roblox logins on this PC and switches between them without re-typing a password.
    /// Accounts are added by signing in through an embedded browser (<see cref="RobloxLoginForm"/>) —
    /// the user logs in themselves; Tempo captures the session cookie from its own browser and keeps
    /// it in the encrypted vault (<see cref="AccountVault"/>). "Launch home" opens the client to the
    /// Roblox home with no game; "Join place ID" joins a specific game (and optional private server)
    /// via Roblox's own authentication-ticket API. An opt-in multi-instance toggle lets several run
    /// at once, and an opt-in localhost API lets other tools drive it.
    ///
    /// Tempo never types a password and never signs anyone up. The launch/multi-instance parts are
    /// USER-TESTED (live client + anti-cheat). Ships OFF behind an informed opt-in; the page is a
    /// state machine (gate → create vault → unlock → list) whose panels are DETACHED not hidden,
    /// because the layout suite reads controls even when invisible.
    /// </summary>
    public partial class MainForm
    {
        private readonly AccountVault _accounts = new AccountVault();

        private TabPage _accountsPage;
        private Panel _acctGate;
        private Panel _acctCreate;
        private Panel _acctUnlock;
        private Panel _acctList;

        private TextBox _createPw1;
        private TextBox _createPw2;
        private Label _createError;

        private TextBox _unlockPw;
        private Label _unlockError;

        private AccountListView _accountList;
        private Label _accountEmptyHint;
        private Label _accountSummaryLabel;
        private Label _accountDetailLabel;
        private TextBox _placeIdBox;
        private TextBox _privateServerBox;
        private TextBox _followUserBox;
        private Button _followBtn;
        private PictureBox _gamePreviewPic;                 // game icon for the typed Place ID
        private Label _gamePreviewName;                     // game name (bold)
        private Label _gamePreviewBy;                       // "by <creator>"
        private Label _gamePreviewStats;                    // players · like% · visits · max
        private System.Windows.Forms.Timer _placeLookupTimer;   // debounce typing before we hit Roblox
        private long _lastResolvedPlace;                    // don't re-lookup the same id
        private int _placeLookupSeq;                        // drop stale async results
        private Button _joinBtn;
        private Button _launchHomeBtn;
        private Button _loginBtn;
        private Button _reloginBtn;
        private Button _editAccountBtn;
        private Button _removeAccountBtn;
        private Button _openBrowserBtn;
        private CheckBox _multiInstanceCheck;
        private Label _multiInstanceStatus;                 // live "● Active / Waiting / Blocked" dot
        private System.Windows.Forms.Timer _multiStatusTimer;
        private System.Windows.Forms.Timer _sessionRescanTimer;   // periodic re-validate of saved sessions
        private bool _updatingMultiInstance;
        private CheckBox _autoLockCheck;                    // opt-in: lock the vault on tab switch
        private ToolTip _accountsTip;                       // explains Hardware-encrypted / Lock vault
        private ToolStripMenuItem _copyUserItem;            // context-menu "Copy username"
        private ToolStripMenuItem _copyPassItem;            // context-menu "Copy password" (relabels when passwordless)
        private Label _robloxStatus;
        private Button _getRobloxBtn;
        private ComboBox _browserCombo;
        private bool _updatingBrowserCombo;
        private System.Windows.Forms.Timer _robloxWatch;   // auto-refreshes the official/modified status
        private string _lastRobloxSig;                     // path|mtime of the client we last verified
        private Button _apiBtn;
        private AccountApiServer _apiServer;
        private string _lastLaunchedAccountName = "";

        /// <summary>Per-account session health, shown as the coloured dot. In-memory only.</summary>
        private enum SessionHealth { None, Unknown, Valid, Expired }
        private readonly Dictionary<string, SessionHealth> _sessionHealth =
            new Dictionary<string, SessionHealth>(StringComparer.OrdinalIgnoreCase);
        private bool _checkingSessions;

        private void BuildAccountsTab()
        {
            var page = new BackdropTabPage(Utils.Localization.T("Accounts")) { AutoScroll = true };
            page.Name = "accounts";
            _accountsPage = page;

            _acctGate = BuildAccountsGate(); _acctGate.Height = 380;
            _acctCreate = BuildAccountsCreate(); _acctCreate.Height = 350;
            _acctUnlock = BuildAccountsUnlock(); _acctUnlock.Height = 320;
            _acctList = BuildAccountsList(); _acctList.Height = 632;

            _tabs.TabPages.Add(page);

            _accounts.Refresh();
            RefreshAccountsView();
        }

        /// <summary>A container that blends with the page so only the active state shows.</summary>
        private Panel StatePanel()
        {
            return new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(760, 620),
                BackColor = _theme.Background,
                Visible = false
            };
        }

        /// <summary>Parent only <paramref name="active"/> to the page; detach the other states.</summary>
        private void ShowOnly(Panel active)
        {
            if (_accountsPage == null) { return; }
            foreach (var pnl in new[] { _acctGate, _acctCreate, _acctUnlock, _acctList })
            {
                if (pnl == null) { continue; }
                if (pnl == active)
                {
                    if (!_accountsPage.Controls.Contains(pnl)) { _accountsPage.Controls.Add(pnl); }
                    pnl.Visible = true;
                    pnl.BringToFront();
                }
                else if (_accountsPage.Controls.Contains(pnl))
                {
                    _accountsPage.Controls.Remove(pnl);
                }
            }
        }

        private void RefreshAccountsView()
        {
            bool enabled = _settings != null && _settings.AccountsEnabled;
            if (enabled) { _accounts.Refresh(); }

            bool create = enabled && _accounts.State == VaultState.NoVault;
            bool unlock = enabled && _accounts.State == VaultState.Locked;
            bool list = enabled && _accounts.State == VaultState.Unlocked;

            Panel active = !enabled ? _acctGate
                : create ? _acctCreate
                : unlock ? _acctUnlock
                : _acctList;
            ShowOnly(active);

            if (create && _createPw1 != null) { _createPw1.Text = ""; _createPw2.Text = ""; _createError.Text = ""; }
            if (unlock && _unlockPw != null) { _unlockPw.Text = ""; _unlockError.Text = ""; }
            if (list)
            {
                // The singleton-handle closer and the local API server run ONLY while the unlocked
                // list is on screen. Tying their lifetime here — not to a launch — means locking the
                // vault, turning the feature off, or simply never unlocking all stop them, so Tempo
                // isn't closing Roblox's single-client lock (or serving the API) in the background
                // when the manager isn't even open. Re-assert the state each time the list appears.
                if (_settings != null && _settings.RobloxMultiInstance) { RobloxLauncher.EnableMultiInstance(); }
                else { RobloxLauncher.DisableMultiInstance(); }
                SyncMultiInstanceCheck();
                PopulateBrowserCombo();
                RefreshRobloxStatus();
                StartRobloxWatch();
                StartMultiStatusTimer();
                StartSessionRescanTimer();
                StartApiServerIfEnabled();
                RefreshApiButton();
                RefreshAccountList();
            }
            else
            {
                // Gate, create, locked, or feature off: the manager isn't in use, so shut down the
                // background workers it owns instead of leaving them running unattended.
                RobloxLauncher.DisableMultiInstance();
                StopRobloxWatch();
                StopMultiStatusTimer();
                StopSessionRescanTimer();
                StopApiServer();
            }
        }

        // ── Gate ─────────────────────────────────────────────────────────────────

        private Panel BuildAccountsGate()
        {
            var p = StatePanel();

            var title = UiFactory.Label(Utils.Localization.T("Roblox Account Manager"), 12, 14, FontStyle.Bold);
            title.Font = new Font("Segoe UI Semibold", 15f, FontStyle.Bold);
            title.AutoSize = true;

            var body = new Label
            {
                Text = Utils.Localization.T(
                    "Sign in to your Roblox accounts once, and switch between them without typing a password "
                    + "again. You log in through a browser window; Tempo remembers the session, encrypted on your disk."),
                Location = new Point(12, 52),
                Size = new Size(700, 40),
                Font = new Font("Segoe UI", 9.75f),
                ForeColor = _theme.Text
            };

            var locks = new Label
            {
                Text = Utils.Localization.T(
                    "Two locks guard the vault: your Windows account, and a master password only you know. "
                    + "Without both, the saved sessions cannot be read — not by another Windows user, not on "
                    + "another PC, and not by Tempo itself."),
                Location = new Point(12, 96),
                Size = new Size(700, 40),
                Font = new Font("Segoe UI", 9.75f),
                ForeColor = _theme.Text
            };

            var warnHead = UiFactory.Label(Utils.Localization.T("⚠ Before you turn this on"), 12, 150, FontStyle.Bold);
            warnHead.ForeColor = _theme.WarningText;
            warnHead.AutoSize = true;

            var warn = new Label
            {
                Text = Utils.Localization.T(
                    "Because Tempo is unsigned and this feature stores logins, some antivirus tools may flag it "
                    + "as suspicious — software that holds account sessions looks the same as a stealer from the "
                    + "outside. Storing Roblox session cookies, and running several clients at once, also cut "
                    + "against Roblox's rules and can get accounts banned. Only enable this if you got Tempo from "
                    + "its official page, you trust this copy, and you accept that risk."),
                Location = new Point(12, 176),
                Size = new Size(700, 72),
                Font = new Font("Segoe UI", 9.25f),
                ForeColor = _theme.WarningText
            };

            var boundary = new Label
            {
                Text = Utils.Localization.T(
                    "Tempo never types your password — you sign in yourself, in a real Roblox page."),
                Location = new Point(12, 256),
                Size = new Size(700, 34),
                Font = new Font("Segoe UI", 9.25f),
                ForeColor = _theme.TextMuted
            };

            var enable = UiFactory.PrimaryButton("Enable Account Manager", 12, 300, 220, 38, _theme);
            enable.Click += (s, e) => EnableAccountsFeature();

            p.Controls.Add(title);
            p.Controls.Add(body);
            p.Controls.Add(locks);
            p.Controls.Add(warnHead);
            p.Controls.Add(warn);
            p.Controls.Add(boundary);
            p.Controls.Add(enable);
            return p;
        }

        private void EnableAccountsFeature()
        {
            if (_settings == null) { return; }
            _settings.AccountsEnabled = true;
            try { SettingsManager.Save(_settings); } catch { }
            _accounts.Refresh();
            RefreshAccountsView();
            Utils.Logger.Info("[Accounts] feature enabled by the user.");
        }

        private void DisableAccountsFeature()
        {
            var confirm = MessageBox.Show(this,
                Utils.Localization.T(
                    "Turn off the Account Manager? Your vault file stays on disk, encrypted, and returns when you "
                    + "turn it back on — nothing is deleted."),
                "Tempo", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) { return; }

            StopApiServer();
            _accounts.Lock();
            if (_settings != null)
            {
                _settings.AccountsEnabled = false;
                try { SettingsManager.Save(_settings); } catch { }
            }
            RefreshAccountsView();
            Utils.Logger.Info("[Accounts] feature disabled by the user.");
        }

        // ── Create vault ─────────────────────────────────────────────────────────

        private Panel BuildAccountsCreate()
        {
            var p = StatePanel();

            var title = UiFactory.Label(Utils.Localization.T("Set a master password"), 12, 14, FontStyle.Bold);
            title.Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold);
            title.AutoSize = true;

            var intro = new Label
            {
                Text = Utils.Localization.T(
                    "This password unlocks your vault. It is never stored anywhere — if you forget it, the accounts "
                    + "inside cannot be recovered, only cleared and started over. Pick something strong that you "
                    + "will remember."),
                Location = new Point(12, 50),
                Size = new Size(700, 48),
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = _theme.Text
            };

            p.Controls.Add(UiFactory.Label(Utils.Localization.T("Master password"), 12, 112, FontStyle.Bold));
            _createPw1 = UiFactory.Text(180, 108, 260);
            _createPw1.UseSystemPasswordChar = true;

            p.Controls.Add(UiFactory.Label(Utils.Localization.T("Confirm password"), 12, 150, FontStyle.Bold));
            _createPw2 = UiFactory.Text(180, 146, 260);
            _createPw2.UseSystemPasswordChar = true;
            _createPw2.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { CreateVault(); } };

            var show = new CheckBox
            {
                Text = Utils.Localization.T("Show"),
                Location = new Point(450, 110),
                AutoSize = true,
                ForeColor = _theme.Text
            };
            show.CheckedChanged += (s, e) =>
            {
                _createPw1.UseSystemPasswordChar = !show.Checked;
                _createPw2.UseSystemPasswordChar = !show.Checked;
            };

            _createError = new Label
            {
                Location = new Point(12, 186),
                Size = new Size(560, 40),
                Font = new Font("Segoe UI", 9f),
                ForeColor = _theme.DangerText
            };

            var create = UiFactory.PrimaryButton("Create vault", 180, 236, 160, 36, _theme);
            create.Click += (s, e) => CreateVault();

            var off = UiFactory.Button(Utils.Localization.T("Turn off this feature"), 12, 300, 200, 30);
            off.Click += (s, e) => DisableAccountsFeature();

            p.Controls.Add(intro);
            p.Controls.Add(_createPw1);
            p.Controls.Add(_createPw2);
            p.Controls.Add(show);
            p.Controls.Add(_createError);
            p.Controls.Add(create);
            p.Controls.Add(off);
            return p;
        }

        private void CreateVault()
        {
            if (_createPw1 == null) { return; }
            string a = _createPw1.Text ?? "";
            string b = _createPw2.Text ?? "";

            if (a.Length < 8)
            {
                _createError.Text = Utils.Localization.T("Use at least 8 characters.");
                return;
            }
            if (a != b)
            {
                _createError.Text = Utils.Localization.T("The two passwords do not match.");
                return;
            }
            if (!_accounts.CreateNew(a))
            {
                _createError.Text = Utils.Localization.T("The vault could not be created.");
                return;
            }
            _createPw1.Text = "";
            _createPw2.Text = "";
            RefreshAccountsView();
        }

        // ── Unlock ─────────────────────────────────────────────────────────────

        private Panel BuildAccountsUnlock()
        {
            var p = StatePanel();

            var title = UiFactory.Label(Utils.Localization.T("Unlock your accounts"), 12, 14, FontStyle.Bold);
            title.Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold);
            title.AutoSize = true;

            p.Controls.Add(UiFactory.Label(Utils.Localization.T("Master password"), 12, 70, FontStyle.Bold));
            _unlockPw = UiFactory.Text(180, 66, 260);
            _unlockPw.UseSystemPasswordChar = true;
            _unlockPw.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { UnlockVault(); } };

            var show = new CheckBox
            {
                Text = Utils.Localization.T("Show"),
                Location = new Point(450, 68),
                AutoSize = true,
                ForeColor = _theme.Text
            };
            show.CheckedChanged += (s, e) => _unlockPw.UseSystemPasswordChar = !show.Checked;

            _unlockError = new Label
            {
                Location = new Point(12, 104),
                Size = new Size(600, 56),
                Font = new Font("Segoe UI", 9f),
                ForeColor = _theme.DangerText
            };

            var unlock = UiFactory.PrimaryButton("Unlock", 180, 168, 160, 36, _theme);
            unlock.Click += (s, e) => UnlockVault();

            var forgot = UiFactory.Button(Utils.Localization.T("Forgot password — reset vault…"), 12, 232, 260, 30);
            forgot.Click += (s, e) => ForgotResetVault();

            var off = UiFactory.Button(Utils.Localization.T("Turn off this feature"), 12, 272, 200, 30);
            off.Click += (s, e) => DisableAccountsFeature();

            p.Controls.Add(title);
            p.Controls.Add(_unlockPw);
            p.Controls.Add(show);
            p.Controls.Add(_unlockError);
            p.Controls.Add(unlock);
            p.Controls.Add(forgot);
            p.Controls.Add(off);
            return p;
        }

        private void UnlockVault()
        {
            if (_unlockPw == null) { return; }
            string pw = _unlockPw.Text ?? "";
            if (pw.Length == 0)
            {
                _unlockError.Text = Utils.Localization.T("Enter your master password.");
                return;
            }
            if (!_accounts.Unlock(pw, out string error))
            {
                _unlockError.Text = error ?? Utils.Localization.T("The vault could not be unlocked.");
                return;
            }
            _unlockPw.Text = "";
            RefreshAccountsView();
        }

        private void ForgotResetVault()
        {
            var confirm = MessageBox.Show(this,
                Utils.Localization.T(
                    "Reset the vault? Every account stored in it will be permanently deleted — there is no way to "
                    + "recover them without the master password. This cannot be undone."),
                "Tempo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) { return; }

            _accounts.DeleteVaultFile();
            RefreshAccountsView();
            Utils.Logger.Info("[Accounts] vault reset by the user (forgot password).");
        }

        private void LockVault()
        {
            StopApiServer();
            _accounts.Lock();
            RefreshAccountsView();
        }

        private void OnAutoLockToggled()
        {
            if (_settings == null || _autoLockCheck == null) { return; }
            _settings.AutoLockOnTabSwitch = _autoLockCheck.Checked;
            try { SettingsManager.Save(_settings); } catch { }
        }

        /// <summary>
        /// Lock the vault when the user switches AWAY from the Accounts tab — but only if they opted in.
        /// Called from the tab-switch handler; a no-op unless the setting is on, the vault is currently
        /// unlocked, and the newly-selected tab isn't the Accounts page itself.
        /// </summary>
        private void AutoLockAccountsOnTabSwitch()
        {
            if (_settings == null || !_settings.AutoLockOnTabSwitch) { return; }
            if (_accounts == null || !_accounts.IsUnlocked) { return; }
            if (_accountsPage != null && _tabs != null && ReferenceEquals(_tabs.SelectedTab, _accountsPage)) { return; }
            LockVault();
            Utils.Logger.Info("[Accounts] vault auto-locked on leaving the tab.");
        }

        private void ChangeMasterPassword()
        {
            using (var dlg = new MasterPasswordDialog(_theme))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) { return; }
                if (!_accounts.ChangeMasterPassword(dlg.Current, dlg.Next, out string error))
                {
                    ShowWarning(error ?? Utils.Localization.T("The master password could not be changed."));
                    return;
                }
                ShowInfo(Utils.Localization.T("Master password changed."));
            }
        }

        // ── Accounts list (RAM-style) ──────────────────────────────────────────────

        private Panel BuildAccountsList()
        {
            var p = StatePanel();

            var listLabel = UiFactory.Label(Utils.Localization.T("Account list"), 12, 12, FontStyle.Bold);
            var encrypted = UiFactory.Label(Utils.Localization.T("🔒 Hardware-encrypted"), 150, 12, FontStyle.Bold);
            encrypted.ForeColor = _theme.SuccessText;
            encrypted.AutoSize = true;
            encrypted.Cursor = Cursors.Help;

            // Explain, in plain words, what actually protects the vault — so "Hardware-encrypted" is
            // informative on hover rather than just a badge.
            _accountsTip = new ToolTip { AutoPopDelay = 30000, InitialDelay = 250, ReshowDelay = 100 };
            _accountsTip.SetToolTip(encrypted, Utils.Localization.T(
                "How your accounts are protected:\n"
                + "• Sealed with AES-256-GCM under YOUR master password (PBKDF2-SHA256, 1,200,000 iterations).\n"
                + "• Wrapped again in Windows DPAPI — bound to your Windows account on THIS PC.\n"
                + "• Never written in plain text; can't be opened on another machine or by another user.\n"
                + "• Tempo never sends your sessions or passwords anywhere."));

            // Discoverability for the drag reorder (the Move Up/Down buttons are gone).
            var reorderHint = UiFactory.Caption(Utils.Localization.T("· drag to reorder"),
                150 + encrypted.PreferredSize.Width + 12, 14);
            reorderHint.ForeColor = _theme.TextMuted;

            _accountList = new AccountListView
            {
                Left = 12,
                Top = 40,
                Width = 400,
                Height = 320,
                Font = UiFactory.BodyFont,
                BorderStyle = BorderStyle.FixedSingle,
                MultiSelect = true,           // select two or more to launch together (multi-instance)
                HighlightSelection = true,    // draw the selection blue (the theme accent)
                ShowItemToolTips = true       // hover a row for its full session-health explanation
            };
            _accountList.RowColorProvider = SessionDotColor;   // the green/red/orange/grey status dot
            _accountList.Columns.Add(Utils.Localization.T("Account"), 192);
            _accountList.Columns.Add(Utils.Localization.T("Username"), 130);
            _accountList.Columns.Add(Utils.Localization.T("Used"), 72, HorizontalAlignment.Right);
            _accountList.DoubleClick += (s, e) => LaunchSelected(home: true);
            _accountList.SelectedIndexChanged += (s, e) => RefreshAccountButtons();

            // Drag a row to reorder (replaces the old Move Up/Down buttons). The dragged item's name
            // travels as the drag data; the drop handler reorders the vault and shows an insertion line.
            _accountList.AllowDrop = true;
            _accountList.ItemDrag += (s, e) =>
            {
                if (e.Item is ListViewItem it) { _accountList.DoDragDrop(it, DragDropEffects.Move); }
            };
            _accountList.DragEnter += (s, e) =>
            {
                e.Effect = (e.Data != null && e.Data.GetDataPresent(typeof(ListViewItem)))
                    ? DragDropEffects.Move : DragDropEffects.None;
            };
            _accountList.DragOver += OnAccountDragOver;
            _accountList.DragLeave += (s, e) => { _accountList.DropLineIndex = -1; _accountList.Invalidate(); };
            _accountList.DragDrop += OnAccountDragDrop;

            var menu = new ContextMenuStrip();
            menu.Items.Add(Utils.Localization.T("Launch (home)"), null, (s, e) => LaunchSelected(home: true));
            menu.Items.Add(Utils.Localization.T("Join place ID"), null, (s, e) => JoinSelectedAccount());
            menu.Items.Add(Utils.Localization.T("Follow player (username)"), null, (s, e) => FollowSelected());
            menu.Items.Add(Utils.Localization.T("Open in browser (signed in)"), null, (s, e) => OpenSelectedInBrowser());
            menu.Items.Add(Utils.Localization.T("Re-login…"), null, (s, e) => ReloginSelectedAccount());
            menu.Items.Add(Utils.Localization.T("Check sessions now"), null, (s, e) => RecheckAllSessions());
            menu.Items.Add(new ToolStripSeparator());
            _copyUserItem = (ToolStripMenuItem)menu.Items.Add(Utils.Localization.T("Copy username"), null, (s, e) => CopyUsername());
            _copyPassItem = (ToolStripMenuItem)menu.Items.Add(Utils.Localization.T("Copy password"), null, (s, e) => CopyPassword());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Utils.Localization.T("Edit…"), null, (s, e) => EditSelectedAccount());
            menu.Items.Add(Utils.Localization.T("Remove"), null, (s, e) => RemoveSelectedAccount());
            // Reflect the selected account's state each time the menu opens: "Copy password" is
            // disabled and relabelled when there's no saved password (a passwordless sign-in).
            menu.Opening += (s, e) => UpdateCopyMenuItems();
            _accountList.ContextMenuStrip = menu;

            // Right-click should act on the row under the cursor. A ListView doesn't reliably select
            // on right-click, so the context menu could otherwise target the previously-selected
            // account — select the clicked row first (keeping an existing multi-selection intact).
            _accountList.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Right) { return; }
                ListViewHitTestInfo hit = _accountList.HitTest(e.Location);
                if (hit.Item != null && !hit.Item.Selected)
                {
                    _accountList.SelectedItems.Clear();
                    hit.Item.Selected = true;
                }
            };

            _accountEmptyHint = new Label
            {
                Text = Utils.Localization.T("No accounts yet.\r\nUse “Log in to Roblox” to add your first one."),
                Left = _accountList.Left + 1,
                Top = _accountList.Top + 64,
                Width = _accountList.Width - 2,
                Height = 60,
                TextAlign = ContentAlignment.TopCenter,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Italic),
                ForeColor = _theme.TextMuted,
                BackColor = _accountList.BackColor,
                Visible = false
            };

            // ── Right column: join-a-game config (like RAM). ──
            int rx = 428, rw = 300;
            p.Controls.Add(UiFactory.Label(Utils.Localization.T("Place ID"), rx, 42, FontStyle.Bold));
            _placeIdBox = UiFactory.Text(rx, 60, rw);
            _placeIdBox.TextChanged += (s, e) => OnPlaceIdChanged();
            // The GAME PREVIEW card (created further down) sits right under the Place ID box, at
            // y88–138 — the same slot the "Get Roblox" button uses when no client is installed (the
            // two never apply at once, so they share the space).

            p.Controls.Add(UiFactory.Label(Utils.Localization.T("Private server ID (optional)"), rx, 146, FontStyle.Bold));
            _privateServerBox = UiFactory.Text(rx, 164, rw);

            // Follow a player (RAM's "Follow"): type a Roblox username and every selected account
            // joins whatever game that user is currently in. Box + button share one row.
            p.Controls.Add(UiFactory.Label(Utils.Localization.T("Follow player (username)"), rx, 192, FontStyle.Bold));
            _followUserBox = UiFactory.Text(rx, 210, rw - 100);
            _followBtn = UiFactory.PrimaryButton("Follow", rx + rw - 92, 208, 92, 28, _theme);
            _followBtn.Click += (s, e) => FollowSelected();

            _joinBtn = UiFactory.PrimaryButton("Join place ID", rx, 242, rw, 28, _theme);
            _joinBtn.Click += (s, e) => JoinSelectedAccount();
            _launchHomeBtn = UiFactory.PrimaryButton("Launch home (no game)", rx, 274, rw, 28, _theme);
            _launchHomeBtn.Click += (s, e) => LaunchSelected(home: true);

            // ── Right column: account actions ──
            int cw = 145;
            _loginBtn = UiFactory.Button(Utils.Localization.T("Log in to Roblox"), rx, 308, cw, 28);
            _loginBtn.Click += (s, e) => LoginNewAccount();
            _reloginBtn = UiFactory.Button(Utils.Localization.T("Re-login…"), rx + 155, 308, cw, 28);
            _reloginBtn.Click += (s, e) => ReloginSelectedAccount();
            _editAccountBtn = UiFactory.Button(Utils.Localization.T("Edit…"), rx, 340, cw, 28);
            _editAccountBtn.Click += (s, e) => EditSelectedAccount();
            _removeAccountBtn = UiFactory.Button(Utils.Localization.T("Remove"), rx + 155, 340, cw, 28);
            _removeAccountBtn.Click += (s, e) => RemoveSelectedAccount();
            // Move Up / Move Down are gone — accounts are reordered by DRAGGING a row (see the
            // drag-and-drop wiring below). Open-in-browser fills the row they used to occupy.
            _openBrowserBtn = UiFactory.Button(Utils.Localization.T("Open in browser (signed in)"), rx, 372, rw, 28);
            _openBrowserBtn.Click += (s, e) => OpenSelectedInBrowser();

            _accountSummaryLabel = UiFactory.Caption("", 12, 366);
            _accountSummaryLabel.AutoSize = false;
            _accountSummaryLabel.Width = 120;
            _accountSummaryLabel.Height = 16;
            _accountSummaryLabel.ForeColor = _theme.TextMuted;

            // Sign-in browser picker (shares the summary row, so it needs no extra height). Lets the
            // user choose their browser instead of Tempo always forcing Chrome-then-Edge.
            var browserLbl = UiFactory.Caption(Utils.Localization.T("Sign-in browser"), 138, 366);
            browserLbl.ForeColor = _theme.TextMuted;
            _browserCombo = UiFactory.Combo(248, 362, 152);
            _browserCombo.SelectedIndexChanged += (s, e) => OnBrowserPicked();

            _robloxStatus = new Label
            {
                AutoSize = false,
                Location = new Point(12, 388),
                Size = new Size(400, 20),
                Font = new Font("Segoe UI", 8.75f),
                ForeColor = _theme.TextMuted
            };
            // Lives in the game-preview slot under the Place ID box — shown only when no client is
            // installed (when you can't preview a game to join anyway).
            _getRobloxBtn = UiFactory.Button(Utils.Localization.T("Get Roblox"), 428, 90, 150, 28);
            _getRobloxBtn.Visible = false;
            _getRobloxBtn.Click += (s, e) => { RobloxInstall.OpenDownloadPage(); };

            _accountDetailLabel = new Label
            {
                AutoSize = false,
                Location = new Point(12, 412),
                Size = new Size(720, 20),
                Font = new Font("Segoe UI", 8.75f),
                ForeColor = _theme.TextMuted
            };

            // ── Game preview: when a valid Place ID is typed, confirm the game with its name + icon.
            //    Sits directly under the Place ID box (shares the slot with the Get-Roblox button). ──
            _gamePreviewPic = new PictureBox
            {
                Location = new Point(428, 88),
                Size = new Size(52, 52),
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false
            };
            _gamePreviewName = new Label
            {
                Location = new Point(486, 86),
                Size = new Size(242, 20),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = _theme.Text,
                AutoEllipsis = true,
                Visible = false
            };
            _gamePreviewBy = new Label
            {
                Location = new Point(486, 106),
                Size = new Size(242, 15),
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = _theme.TextMuted,
                AutoEllipsis = true,
                Visible = false
            };
            _gamePreviewStats = new Label
            {
                Location = new Point(486, 121),
                Size = new Size(252, 15),
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = _theme.TextMuted,
                AutoEllipsis = true,
                Visible = false
            };

            _multiInstanceCheck = new CheckBox
            {
                Text = Utils.Localization.T("Run several accounts at once (multi-instance)"),
                Location = new Point(12, 438),
                AutoSize = true,
                ForeColor = _theme.Text
            };
            _multiInstanceCheck.CheckedChanged += (s, e) => OnMultiInstanceToggled();

            // Live status dot to the right of the toggle: green when the closer is actively managing a
            // running Roblox, amber while waiting for one, red if the anti-cheat is blocking it. Placed
            // off the checkbox's own width so it clears the text at any DPI.
            _multiInstanceStatus = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(12 + _multiInstanceCheck.PreferredSize.Width + 16, 440),
                Text = ""
            };

            var multiWarn = new Label
            {
                Text = Utils.Localization.T(
                    "Closes Roblox's single-client lock from outside so several clients run at once. Against "
                    + "Roblox's rules — ban risk. Runs only while this list is open."),
                Location = new Point(32, 460),
                Size = new Size(700, 28),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = _theme.WarningText
            };

            var lockBtn = UiFactory.Button(Utils.Localization.T("🔒 Lock vault"), 12, 496, 130, 30);
            lockBtn.Click += (s, e) => LockVault();
            var changeBtn = UiFactory.Button(Utils.Localization.T("Change master password…"), 148, 496, 206, 30);
            changeBtn.Click += (s, e) => ChangeMasterPassword();
            _apiBtn = UiFactory.Button(Utils.Localization.T("Local API…"), 360, 496, 100, 30);
            _apiBtn.Click += (s, e) => ConfigureApiServer();
            var disableBtn = UiFactory.Button(Utils.Localization.T("Turn off"), 466, 496, 100, 30);
            disableBtn.Click += (s, e) => DisableAccountsFeature();
            _accountsTip.SetToolTip(lockBtn, Utils.Localization.T(
                "Lock the vault now: it clears the decryption key from memory and hides every account "
                + "behind your master password until you unlock again."));

            // Auto-lock (opt-in — the user's call): lock the vault the instant they leave the Accounts tab.
            _autoLockCheck = new CheckBox
            {
                Text = Utils.Localization.T("Auto-lock the vault when I switch to another tab"),
                Location = new Point(12, 532),
                AutoSize = true,
                Checked = _settings != null && _settings.AutoLockOnTabSwitch,
                ForeColor = _theme.Text
            };
            _autoLockCheck.CheckedChanged += (s, e) => OnAutoLockToggled();
            _accountsTip.SetToolTip(_autoLockCheck, Utils.Localization.T(
                "When on, leaving this tab locks the vault automatically — so it's never left unlocked "
                + "on a tab you walked away from. You'll re-enter your master password next time."));

            var safety = new Label
            {
                Text = Utils.Localization.T(
                    "Status dot — green: session active · red: expired, use “Re-login” · orange: not checked yet · "
                    + "grey: no saved session.\r\n"
                    + "Sessions are stored only in this encrypted vault and are never logged or sent anywhere. A "
                    + "saved session is like being signed in, so keep your vault to yourself."),
                AutoSize = false,
                Location = new Point(12, 558),
                Size = new Size(720, 52),
                Font = new Font("Segoe UI", 8.75f),
                ForeColor = _theme.TextMuted
            };

            p.Controls.Add(listLabel);
            p.Controls.Add(encrypted);
            p.Controls.Add(reorderHint);
            p.Controls.Add(_accountList);
            p.Controls.Add(_accountEmptyHint);
            _accountEmptyHint.BringToFront();
            p.Controls.Add(_placeIdBox);
            p.Controls.Add(_privateServerBox);
            p.Controls.Add(_followUserBox);
            p.Controls.Add(_followBtn);
            p.Controls.Add(_joinBtn);
            p.Controls.Add(_launchHomeBtn);
            p.Controls.Add(_loginBtn);
            p.Controls.Add(_reloginBtn);
            p.Controls.Add(_editAccountBtn);
            p.Controls.Add(_removeAccountBtn);
            p.Controls.Add(_openBrowserBtn);
            p.Controls.Add(_accountSummaryLabel);
            p.Controls.Add(browserLbl);
            p.Controls.Add(_browserCombo);
            p.Controls.Add(_gamePreviewPic);
            p.Controls.Add(_gamePreviewName);
            p.Controls.Add(_gamePreviewBy);
            p.Controls.Add(_gamePreviewStats);
            p.Controls.Add(_robloxStatus);
            p.Controls.Add(_getRobloxBtn);
            p.Controls.Add(_accountDetailLabel);
            p.Controls.Add(_multiInstanceCheck);
            p.Controls.Add(_multiInstanceStatus);
            p.Controls.Add(multiWarn);
            p.Controls.Add(lockBtn);
            p.Controls.Add(changeBtn);
            p.Controls.Add(_apiBtn);
            p.Controls.Add(disableBtn);
            p.Controls.Add(_autoLockCheck);
            p.Controls.Add(safety);
            return p;
        }

        private void RefreshAccountList()
        {
            if (_accountList == null) { return; }

            string keep = SelectedAccount()?.Name;

            _accountList.BeginUpdate();
            _accountList.Items.Clear();
            foreach (RobloxAccount a in _accounts.Accounts)
            {
                // The coloured session dot in column 0 now carries "has a saved session" (and its
                // health), so the old 🔑 mark is gone — it would just be redundant with the dot.
                string icon = string.IsNullOrWhiteSpace(a.Icon) ? "" : a.Icon + "  ";
                var item = new ListViewItem(icon + a.Name);
                item.SubItems.Add(string.IsNullOrWhiteSpace(a.Username) ? "—" : a.Username);
                item.SubItems.Add(a.TimesUsed.ToString("N0"));
                item.Tag = a.Name;
                item.ToolTipText = SessionTooltip(SessionHealthFor(a));   // hover explains the dot
                _accountList.Items.Add(item);
            }
            _accountList.EndUpdate();

            if (keep != null) { SelectAccount(keep); }

            if (_accountEmptyHint != null)
            {
                _accountEmptyHint.BackColor = _accountList.BackColor;
                _accountEmptyHint.ForeColor = _theme.TextMuted;
                _accountEmptyHint.Visible = _accounts.Accounts.Count == 0;
                if (_accountEmptyHint.Visible) { _accountEmptyHint.BringToFront(); }
            }

            if (_accountSummaryLabel != null)
            {
                int n = _accounts.Accounts.Count;
                _accountSummaryLabel.Text = n == 1
                    ? Utils.Localization.T("1 account")
                    : Utils.Localization.F("{0} accounts", n);
            }

            RefreshAccountButtons();

            // Detect expiry in the background: validate any not-yet-checked sessions and recolour
            // their dots. Only touches Unknown accounts, so this is a no-op once everything is checked.
            _ = AutoCheckSessionsAsync();
        }

        // ── session health (the coloured status dot) ─────────────────────────────

        /// <summary>The health of an account's saved session — the meaning of its dot.</summary>
        private SessionHealth SessionHealthFor(RobloxAccount a)
        {
            if (a == null || string.IsNullOrEmpty(a.Cookie)) { return SessionHealth.None; }
            return _sessionHealth.TryGetValue(a.Name, out SessionHealth h) ? h : SessionHealth.Unknown;
        }

        /// <summary>Dot colour for a row: green active / red expired / orange unchecked / grey none.</summary>
        private Color SessionDotColor(ListViewItem item)
        {
            RobloxAccount a = _accounts != null ? _accounts.GetByName(item.Tag as string) : null;
            switch (SessionHealthFor(a))
            {
                case SessionHealth.Valid:   return _theme.SuccessText;   // green — no expiry detected
                case SessionHealth.Expired: return _theme.DangerText;    // red — expired, re-login
                case SessionHealth.Unknown: return _theme.WarningText;   // orange — not checked yet
                default:                    return _theme.TextMuted;     // grey — no saved session
            }
        }

        private string SessionTooltip(SessionHealth h)
        {
            switch (h)
            {
                case SessionHealth.Valid:   return Utils.Localization.T("Session active — no expiry detected.");
                case SessionHealth.Expired: return Utils.Localization.T("Session expired — use “Re-login” to sign in again.");
                case SessionHealth.Unknown: return Utils.Localization.T("Session not checked yet — checking…");
                default:                    return Utils.Localization.T("No saved session — use “Log in to Roblox”.");
            }
        }

        /// <summary>
        /// Validate every saved session whose dot is still orange (Unknown) against Roblox and recolour
        /// it green (works) or red (expired). Sequential with a short gap so a big vault doesn't hammer
        /// the API; runs only while the unlocked list is on screen; single-flight via <c>_checkingSessions</c>.
        /// </summary>
        private async Task AutoCheckSessionsAsync(bool forceAll = false)
        {
            if (_checkingSessions || _accounts == null || !_accounts.IsUnlocked) { return; }

            // Snapshot the accounts to check, so the list can change underneath us. Normally just the
            // not-yet-checked (orange) ones; a periodic re-scan (forceAll) re-validates ALL saved
            // sessions so one that expires WHILE the list is open still flips green→red on its own —
            // and it updates dots in place without flashing them back to orange first.
            var todo = new List<RobloxAccount>();
            foreach (RobloxAccount a in _accounts.Accounts)
            {
                if (string.IsNullOrEmpty(a.Cookie)) { continue; }
                if (forceAll || SessionHealthFor(a) == SessionHealth.Unknown) { todo.Add(a); }
            }
            if (todo.Count == 0) { return; }

            _checkingSessions = true;
            try
            {
                foreach (RobloxAccount a in todo)
                {
                    if (!_accounts.IsUnlocked) { break; }
                    RobloxApi.SessionCheck res = await RobloxApi.ValidateSessionAsync(a.Cookie);
                    if (res == RobloxApi.SessionCheck.Valid) { SetSessionHealth(a.Name, SessionHealth.Valid); }
                    else if (res == RobloxApi.SessionCheck.Expired) { SetSessionHealth(a.Name, SessionHealth.Expired); }
                    // Unknown (network/5xx/rate-limit) → leave the dot orange; retried on the next
                    // list refresh or a manual "Check sessions now", never falsely painted red.
                    await Task.Delay(250);   // gentle on Roblox's API for a large vault
                }
            }
            finally
            {
                _checkingSessions = false;
            }
        }

        /// <summary>Record a session-health result and repaint that row's dot + tooltip.</summary>
        private void SetSessionHealth(string accountName, SessionHealth health)
        {
            if (string.IsNullOrEmpty(accountName)) { return; }
            _sessionHealth[accountName] = health;
            if (_accountList == null) { return; }
            foreach (ListViewItem it in _accountList.Items)
            {
                if (string.Equals(it.Tag as string, accountName, StringComparison.OrdinalIgnoreCase))
                {
                    it.ToolTipText = SessionTooltip(health);
                    break;
                }
            }
            _accountList.Invalidate();
        }

        /// <summary>“Check sessions now”: forget every cached result and re-validate from scratch.</summary>
        private void RecheckAllSessions()
        {
            if (_accounts == null || !_accounts.IsUnlocked) { return; }
            _sessionHealth.Clear();
            RefreshAccountList();   // resets dots to orange, then AutoCheck repaints them
        }

        private RobloxAccount SelectedAccount()
        {
            if (_accountList == null || _accountList.SelectedItems.Count == 0)
            {
                return null;
            }
            return _accounts.GetByName(_accountList.SelectedItems[0].Tag as string);
        }

        /// <summary>Every selected account (for launch / remove that act on the whole selection).</summary>
        private List<RobloxAccount> SelectedAccounts()
        {
            var list = new List<RobloxAccount>();
            if (_accountList == null) { return list; }
            foreach (ListViewItem it in _accountList.SelectedItems)
            {
                RobloxAccount a = _accounts.GetByName(it.Tag as string);
                if (a != null) { list.Add(a); }
            }
            return list;
        }

        private void SelectAccount(string name)
        {
            foreach (ListViewItem it in _accountList.Items)
            {
                if (string.Equals(it.Tag as string, name, StringComparison.OrdinalIgnoreCase))
                {
                    it.Selected = true;
                    it.EnsureVisible();
                    return;
                }
            }
        }

        private void RefreshAccountButtons()
        {
            var sel = SelectedAccounts();
            int count = sel.Count;
            bool any = count > 0;
            bool single = count == 1;
            bool anyLaunchable = false;
            foreach (RobloxAccount s in sel) { if (!string.IsNullOrEmpty(s.Cookie)) { anyLaunchable = true; break; } }

            RobloxAccount first = any ? sel[0] : null;

            // Launch/join/follow act on the WHOLE selection; per-account edits stay single-target.
            // (Reordering is by drag now, so there are no Move Up/Down buttons to enable.)
            if (_joinBtn != null) { _joinBtn.Enabled = anyLaunchable; }
            if (_followBtn != null) { _followBtn.Enabled = anyLaunchable; }
            if (_launchHomeBtn != null) { _launchHomeBtn.Enabled = anyLaunchable; }
            if (_openBrowserBtn != null) { _openBrowserBtn.Enabled = anyLaunchable; }
            // Re-login is a single-account fix, but also offer it whenever a selected session has
            // expired (its dot is red) so an expired account can be refreshed without first narrowing
            // the selection down to that one row.
            if (_reloginBtn != null) { _reloginBtn.Enabled = single || FirstExpiredSelected() != null; }
            if (_editAccountBtn != null) { _editAccountBtn.Enabled = single; }
            if (_removeAccountBtn != null) { _removeAccountBtn.Enabled = any; }
            if (_accountDetailLabel != null)
            {
                _accountDetailLabel.Text = count > 1
                    ? Utils.Localization.F("{0} accounts selected — launch runs them all.", count)
                    : (single ? DescribeAccount(first) : "");
            }
        }

        // ── drag-to-reorder ───────────────────────────────────────────────────────

        private void OnAccountDragOver(object sender, DragEventArgs e)
        {
            if (_accountList == null || e.Data == null || !e.Data.GetDataPresent(typeof(ListViewItem)))
            {
                e.Effect = DragDropEffects.None;
                return;
            }
            e.Effect = DragDropEffects.Move;
            int idx = _accountList.DropIndexAt(_accountList.PointToClient(new Point(e.X, e.Y)));
            if (_accountList.DropLineIndex != idx)
            {
                _accountList.DropLineIndex = idx;   // move the insertion line and repaint
                _accountList.Invalidate();
            }
        }

        private void OnAccountDragDrop(object sender, DragEventArgs e)
        {
            if (_accountList == null) { return; }
            _accountList.DropLineIndex = -1;
            if (e.Data == null || !e.Data.GetDataPresent(typeof(ListViewItem)))
            {
                _accountList.Invalidate();
                return;
            }
            string name = (e.Data.GetData(typeof(ListViewItem)) as ListViewItem)?.Tag as string;
            int target = _accountList.DropIndexAt(_accountList.PointToClient(new Point(e.X, e.Y)));
            if (!string.IsNullOrEmpty(name) && _accounts.MoveTo(name, target))
            {
                _accounts.Save();
                RefreshAccountList();
                SelectAccount(name);
                Utils.Logger.Info("[Accounts] reordered an account by drag.");
            }
            else
            {
                _accountList.Invalidate();   // no-op drop — just clear the line
            }
        }

        // ── copy username / password ────────────────────────────────────────────

        /// <summary>Reflect the selected account when the context menu opens: no saved password ⇒
        /// the "Copy password" item is disabled and relabelled to say the sign-in was passwordless.</summary>
        private void UpdateCopyMenuItems()
        {
            RobloxAccount a = SelectedAccount();
            bool hasUser = a != null && !string.IsNullOrWhiteSpace(a.Username);
            bool hasPass = a != null && !string.IsNullOrEmpty(a.Password);
            if (_copyUserItem != null) { _copyUserItem.Enabled = hasUser; }
            if (_copyPassItem != null)
            {
                _copyPassItem.Enabled = hasPass;
                _copyPassItem.Text = hasPass
                    ? Utils.Localization.T("Copy password")
                    : Utils.Localization.T("Copy password — none (passwordless)");
            }
        }

        private void CopyUsername()
        {
            RobloxAccount a = SelectedAccount();
            if (a == null) { return; }
            if (string.IsNullOrWhiteSpace(a.Username))
            {
                ShowWarning(Utils.Localization.T("This account has no username saved — use “Edit” to add one."));
                return;
            }
            if (CopyToClipboard(a.Username, secret: false))
            {
                ShowInfo(Utils.Localization.T("Username copied to the clipboard."));
            }
        }

        private void CopyPassword()
        {
            RobloxAccount a = SelectedAccount();
            if (a == null) { return; }
            if (string.IsNullOrEmpty(a.Password))
            {
                ShowWarning(Utils.Localization.T(
                    "No password is saved for this account — it signed in without one (a passkey or quick log in), "
                    + "so only the username is available."));
                return;
            }
            if (CopyToClipboard(a.Password, secret: true))
            {
                ShowInfo(Utils.Localization.T("Password copied — the clipboard clears itself in 25 seconds."));
            }
        }

        /// <summary>
        /// Put text on the clipboard. For a secret (the password), a one-shot 25s timer wipes it —
        /// but ONLY if the clipboard still holds exactly what we put there, so it never clobbers
        /// something the user copied afterwards. Username is not a secret, so it isn't wiped.
        /// </summary>
        private bool CopyToClipboard(string value, bool secret)
        {
            if (string.IsNullOrEmpty(value)) { return false; }
            try { Clipboard.SetText(value); }
            catch { ShowWarning(Utils.Localization.T("Couldn't reach the clipboard — try again.")); return false; }

            if (secret)
            {
                var wipe = new System.Windows.Forms.Timer { Interval = 25000 };
                wipe.Tick += (s, e) =>
                {
                    wipe.Stop();
                    wipe.Dispose();
                    try
                    {
                        if (Clipboard.ContainsText() && string.Equals(Clipboard.GetText(), value, StringComparison.Ordinal))
                        {
                            Clipboard.Clear();
                        }
                    }
                    catch { /* clipboard busy/owned elsewhere — leave it */ }
                };
                wipe.Start();
            }
            return true;
        }

        private string DescribeAccount(RobloxAccount a)
        {
            var bits = new List<string>();
            bits.Add(a.HasSecret
                ? Utils.Localization.T("Session saved")
                : Utils.Localization.T("No saved session — use Re-login"));
            if (!string.IsNullOrWhiteSpace(a.Alias)) { bits.Add(Utils.Localization.F("alias {0}", a.Alias.Trim())); }
            if (!string.IsNullOrWhiteSpace(a.UserId)) { bits.Add(Utils.Localization.F("ID {0}", a.UserId.Trim())); }
            if (a.LastUsedUtc.HasValue)
            {
                bits.Add(Utils.Localization.F("Last used {0}", a.LastUsedUtc.Value.ToLocalTime().ToString("g")));
            }
            return string.Join("  ·  ", bits.ToArray());
        }

        // ── actions ───────────────────────────────────────────────────────────

        private async void LoginNewAccount()
        {
            if (!_accounts.IsUnlocked) { return; }

            string cookie = null;
            string password = null;
            using (var login = new RobloxLoginForm(_theme, _settings?.LoginBrowser))
            {
                if (login.ShowDialog(this) != DialogResult.OK) { return; }
                cookie = login.CapturedCookie;
                password = login.CapturedPassword;   // "" for a passwordless sign-in (passkey / quick log in)
            }
            if (string.IsNullOrEmpty(cookie)) { return; }

            var acct = new RobloxAccount { Cookie = cookie, Password = password ?? "", AddedVia = RobloxAccount.ViaLogin };
            RobloxApi.RobloxUser user = null;
            for (int attempt = 0; attempt < 2 && user == null; attempt++)
            {
                if (attempt > 0) { await Task.Delay(800); }
                // A transient network fault here just means we can't read the name yet — fall through
                // to the "couldn't read the account name" path below rather than crashing the sign-in.
                try { user = await RobloxApi.GetAuthenticatedUserAsync(cookie); }
                catch (Exception ex) { Utils.Logger.Warn("[Accounts] user lookup after sign-in failed: " + ex.Message); }
            }
            if (user != null)
            {
                acct.Username = user.Name ?? "";
                acct.UserId = user.Id > 0 ? user.Id.ToString() : "";
                acct.Alias = user.DisplayName ?? "";
                acct.Name = !string.IsNullOrWhiteSpace(user.DisplayName) ? user.DisplayName
                          : !string.IsNullOrWhiteSpace(user.Name) ? user.Name
                          : Utils.Localization.T("Roblox account");
            }
            else
            {
                acct.Name = Utils.Localization.T("Roblox account");
                ShowWarning(Utils.Localization.T(
                    "Signed in and saved the session, but Tempo couldn't read the account name from Roblox — "
                    + "you can fill it in on the next screen."));
            }

            using (var dlg = new AccountDialog(_theme, acct, isAdd: true))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) { return; }
                try
                {
                    RobloxAccount added = _accounts.Add(dlg.Result);
                    // Fresh sign-in: if Roblox told us who the cookie is, the session is good (green);
                    // otherwise leave it orange to be re-checked rather than falsely marking it expired.
                    _sessionHealth[added.Name] = user != null ? SessionHealth.Valid : SessionHealth.Unknown;
                    _accounts.Save();
                    RefreshAccountList();
                    SelectAccount(added.Name);
                    Utils.Logger.Info("[Accounts] added an account via sign-in ("
                        + (string.IsNullOrEmpty(added.Password) ? "username only — passwordless sign-in" : "username + password saved")
                        + ").");
                }
                catch (Exception ex)
                {
                    Utils.Logger.Warn("[Accounts] saving the new account failed: " + ex.Message);
                    ShowWarning(Utils.Localization.T("Tempo couldn't save the account — try again."));
                }
            }
        }

        private void ReloginSelectedAccount()
        {
            // With several rows selected, re-login the one whose session has expired (the reason the
            // button is enabled for a multi-selection); otherwise just the first/only selected row.
            RobloxAccount a = FirstExpiredSelected() ?? SelectedAccount();
            if (a != null) { ReloginAccount(a); }
        }

        /// <summary>The first selected account whose saved session has expired (red dot), or null.</summary>
        private RobloxAccount FirstExpiredSelected()
        {
            foreach (RobloxAccount s in SelectedAccounts())
            {
                if (!string.IsNullOrEmpty(s.Cookie)
                    && _sessionHealth.TryGetValue(s.Name, out SessionHealth h) && h == SessionHealth.Expired)
                {
                    return s;
                }
            }
            return null;
        }

        private void ReloginAccountByName(string name)
        {
            RobloxAccount a = _accounts.GetByName(name);
            if (a != null) { ReloginAccount(a); }
        }

        private async void ReloginAccount(RobloxAccount a)
        {
            if (a == null) { return; }

            string cookie = null;
            string password = null;
            using (var login = new RobloxLoginForm(_theme, _settings?.LoginBrowser))
            {
                if (login.ShowDialog(this) != DialogResult.OK) { return; }
                cookie = login.CapturedCookie;
                password = login.CapturedPassword;
            }
            if (string.IsNullOrEmpty(cookie)) { return; }

            a.Cookie = cookie;
            // Refresh the saved password only if one was typed this time — a passwordless re-login
            // (passkey / quick log in) must not wipe a password saved earlier.
            if (!string.IsNullOrEmpty(password)) { a.Password = password; }
            try
            {
                RobloxApi.RobloxUser user = await RobloxApi.GetAuthenticatedUserAsync(cookie);
                if (user != null)
                {
                    if (string.IsNullOrWhiteSpace(a.Username)) { a.Username = user.Name ?? ""; }
                    if (string.IsNullOrWhiteSpace(a.UserId) && user.Id > 0) { a.UserId = user.Id.ToString(); }
                }
                // A just-refreshed session goes back to green (or orange to re-check on a transient miss).
                _sessionHealth[a.Name] = user != null ? SessionHealth.Valid : SessionHealth.Unknown;
                _accounts.Save();
                RefreshAccountList();
                SelectAccount(a.Name);
                ShowInfo(Utils.Localization.T("Session refreshed."));
            }
            catch (Exception ex)
            {
                // The new cookie is already stored; only the verify/save follow-up failed. Keep the
                // session saved, mark it to be re-checked, and tell the user rather than crashing.
                Utils.Logger.Warn("[Accounts] re-login follow-up failed: " + ex.Message);
                _sessionHealth[a.Name] = SessionHealth.Unknown;
                try { _accounts.Save(); } catch { }
                RefreshAccountList();
                ShowWarning(Utils.Localization.T(
                    "Signed in and saved the session, but Tempo couldn't reach Roblox to verify it — it'll be re-checked shortly."));
            }
        }

        /// <summary>
        /// Launch every selected account. <paramref name="home"/> opens Roblox with no game;
        /// otherwise each joins the Place ID (and optional private server). Two or more at once
        /// needs multi-instance, so it prompts (with the ban warning) to turn it on if it's off.
        /// </summary>
        private async void LaunchSelected(bool home)
        {
            var launchable = new List<RobloxAccount>();
            foreach (RobloxAccount s in SelectedAccounts())
            {
                if (!string.IsNullOrEmpty(s.Cookie)) { launchable.Add(s); }
            }
            if (launchable.Count == 0)
            {
                ShowWarning(Utils.Localization.T("Select an account with a saved session — or use “Log in to Roblox”."));
                return;
            }

            long placeId = 0;
            string privateServer = null;
            if (!home)
            {
                placeId = ParsePlaceId(_placeIdBox?.Text);
                if (placeId <= 0)
                {
                    ShowWarning(Utils.Localization.T("Enter a Place ID to join a game, or use “Launch home” for no game."));
                    return;
                }
                privateServer = (_privateServerBox?.Text ?? "").Trim();
            }

            if (!RobloxInstall.IsInstalled())
            {
                RefreshRobloxStatus();
                ShowWarning(Utils.Localization.T(
                    "Roblox isn't installed on this PC. Use “Get Roblox” to install it, then try again."));
                return;
            }

            // Running two or more clients at once requires holding Roblox's single-client lock.
            if (launchable.Count > 1 && !(_settings != null && _settings.RobloxMultiInstance))
            {
                var r = MessageBox.Show(this,
                    Utils.Localization.F(
                        "Launch {0} accounts at once? Running several clients needs multi-instance, which closes "
                        + "Roblox's single-client lock so extra clients can start — it is against Roblox's rules "
                        + "and CAN GET ACCOUNTS BANNED. Turn it on and launch?", launchable.Count),
                    "Tempo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) { return; }
                if (_settings != null)
                {
                    _settings.RobloxMultiInstance = true;
                    try { SettingsManager.Save(_settings); } catch { }
                    SyncMultiInstanceCheck();
                }
            }
            if (_settings != null && _settings.RobloxMultiInstance) { RobloxLauncher.EnableMultiInstance(); }

            SetLaunchButtonsEnabled(false);
            int ok = 0;
            string lastError = null;
            RobloxAccount firstExpired = null;
            try
            {
                for (int i = 0; i < launchable.Count; i++)
                {
                    RobloxAccount a = launchable[i];
                    RobloxLauncher.LaunchResult res = await RobloxLauncher.LaunchAsync(a.Cookie, placeId, privateServer, home);
                    if (res == RobloxLauncher.LaunchResult.Launched)
                    {
                        ok++;
                        a.TimesUsed++;
                        a.LastUsedUtc = DateTime.UtcNow;
                        _lastLaunchedAccountName = a.Name;
                        SetSessionHealth(a.Name, SessionHealth.Valid);     // a minted ticket proves it works
                    }
                    else
                    {
                        lastError = LaunchErrorText(res);
                        if (res == RobloxLauncher.LaunchResult.NoTicket)   // couldn't mint = expired session
                        {
                            SetSessionHealth(a.Name, SessionHealth.Expired);
                            if (firstExpired == null) { firstExpired = a; }
                        }
                    }
                    // Stagger so each client has a moment to come up before the next starts.
                    if (i < launchable.Count - 1) { await Task.Delay(2500); }
                }
            }
            catch (Exception ex)
            {
                Utils.Logger.Warn("[Accounts] launch failed: " + ex.Message);
                lastError = Utils.Localization.T("Something went wrong starting the client — try again.");
            }
            finally
            {
                SetLaunchButtonsEnabled(true);   // never leave the launch buttons stuck, even if one throws
            }

            try { _accounts.Save(); } catch (Exception ex) { Utils.Logger.Warn("[Accounts] save after launch failed: " + ex.Message); }
            RefreshAccountList();

            if (ok == 0)
            {
                // The launch failed — if it was because a session expired, offer to fix it right here
                // instead of making the user hunt for "Re-login".
                if (firstExpired != null && OfferReloginForExpired(firstExpired)) { return; }
                ShowWarning(lastError ?? Utils.Localization.T("Tempo couldn't start the Roblox client."));
            }
            else if (lastError != null)
            {
                ShowWarning(Utils.Localization.F("Launched {0} of {1}. {2}", ok, launchable.Count, lastError));
            }
        }

        /// <summary>
        /// A launch just failed because a saved session expired — offer to re-login that account on the
        /// spot. Returns true if the user took the offer (the sign-in window is opening), so the caller
        /// can skip the generic "launch failed" warning.
        /// </summary>
        private bool OfferReloginForExpired(RobloxAccount a)
        {
            if (a == null) { return false; }
            var r = MessageBox.Show(this,
                Utils.Localization.F(
                    "The saved session for “{0}” has expired, so it couldn't launch. Re-login now to fix it?", a.Name),
                "Tempo", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) { return false; }
            ReloginAccount(a);   // opens the sign-in window for this account
            return true;
        }

        private static string LaunchErrorText(RobloxLauncher.LaunchResult res)
        {
            switch (res)
            {
                case RobloxLauncher.LaunchResult.NoTicket:
                    return Utils.Localization.T("A saved session had expired — use “Re-login” for that account.");
                case RobloxLauncher.LaunchResult.NotInstalled:
                    return Utils.Localization.T("Roblox isn't installed.");
                default:
                    return Utils.Localization.T("A launch failed.");
            }
        }

        private void JoinSelectedAccount() => LaunchSelected(home: false);

        /// <summary>
        /// Follow a player: every selected account joins whatever game the typed username is in right
        /// now (RAM's "Follow"). Resolves the username to a user id, then launches each account with
        /// the client's follow request. Two or more at once needs multi-instance, same as Launch.
        /// </summary>
        private async void FollowSelected()
        {
            var launchable = new List<RobloxAccount>();
            foreach (RobloxAccount s in SelectedAccounts())
            {
                if (!string.IsNullOrEmpty(s.Cookie)) { launchable.Add(s); }
            }
            if (launchable.Count == 0)
            {
                ShowWarning(Utils.Localization.T("Select an account with a saved session — or use “Log in to Roblox”."));
                return;
            }

            string username = (_followUserBox?.Text ?? "").Trim();
            if (username.Length == 0)
            {
                ShowWarning(Utils.Localization.T("Type the Roblox username of the player to follow into their game."));
                return;
            }

            if (!RobloxInstall.IsInstalled())
            {
                RefreshRobloxStatus();
                ShowWarning(Utils.Localization.T(
                    "Roblox isn't installed on this PC. Use “Get Roblox” to install it, then try again."));
                return;
            }

            SetLaunchButtonsEnabled(false);
            try
            {
                RobloxApi.RobloxUser target = await RobloxApi.ResolveUsernameAsync(username);
                if (target == null || target.Id <= 0)
                {
                    ShowWarning(Utils.Localization.F("No Roblox user named “{0}” was found.", username));
                    return;
                }

                // Two or more clients at once needs multi-instance (ban warning), same as Launch.
                if (launchable.Count > 1 && !(_settings != null && _settings.RobloxMultiInstance))
                {
                    var r = MessageBox.Show(this,
                        Utils.Localization.F(
                            "Follow {0} with {1} accounts at once? Running several clients needs multi-instance, "
                            + "which closes Roblox's single-client lock so extra clients can start — it is against "
                            + "Roblox's rules and CAN GET ACCOUNTS BANNED. Turn it on and follow?",
                            target.Name, launchable.Count),
                        "Tempo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (r != DialogResult.Yes) { return; }
                    if (_settings != null)
                    {
                        _settings.RobloxMultiInstance = true;
                        try { SettingsManager.Save(_settings); } catch { }
                        SyncMultiInstanceCheck();
                    }
                }
                if (_settings != null && _settings.RobloxMultiInstance) { RobloxLauncher.EnableMultiInstance(); }

                int ok = 0;
                string lastError = null;
                RobloxAccount firstExpired = null;
                for (int i = 0; i < launchable.Count; i++)
                {
                    RobloxAccount a = launchable[i];
                    RobloxLauncher.LaunchResult res = await RobloxLauncher.LaunchFollowAsync(a.Cookie, target.Id);
                    if (res == RobloxLauncher.LaunchResult.Launched)
                    {
                        ok++;
                        a.TimesUsed++;
                        a.LastUsedUtc = DateTime.UtcNow;
                        _lastLaunchedAccountName = a.Name;
                        SetSessionHealth(a.Name, SessionHealth.Valid);
                    }
                    else
                    {
                        lastError = LaunchErrorText(res);
                        if (res == RobloxLauncher.LaunchResult.NoTicket)
                        {
                            SetSessionHealth(a.Name, SessionHealth.Expired);
                            if (firstExpired == null) { firstExpired = a; }
                        }
                    }
                    if (i < launchable.Count - 1) { await Task.Delay(2500); }
                }

                try { _accounts.Save(); } catch (Exception ex) { Utils.Logger.Warn("[Accounts] save after follow failed: " + ex.Message); }
                RefreshAccountList();

                if (ok == 0)
                {
                    // Follow failed — if a session had expired, offer to re-login right here.
                    if (firstExpired != null && OfferReloginForExpired(firstExpired)) { return; }
                    ShowWarning(lastError ?? Utils.Localization.T("Tempo couldn't start the Roblox client."));
                }
                else if (lastError != null)
                {
                    ShowWarning(Utils.Localization.F("Followed with {0} of {1}. {2}", ok, launchable.Count, lastError));
                }
                else
                {
                    ShowInfo(Utils.Localization.F("Following {0} with {1} account(s). If it doesn't join, that "
                        + "player's join privacy may be blocking followers.", target.Name, ok));
                }
            }
            catch (Exception ex)
            {
                Utils.Logger.Warn("[Accounts] follow failed: " + ex.Message);
                ShowWarning(Utils.Localization.T("Something went wrong following that player — try again."));
            }
            finally
            {
                SetLaunchButtonsEnabled(true);
            }
        }

        /// <summary>
        /// Open each selected account SIGNED IN, in a real browser with a persistent per-account
        /// profile (its session/history is kept between opens). Tempo injects the stored cookie so
        /// the browser is logged in as that account, then leaves the window open.
        /// </summary>
        private async void OpenSelectedInBrowser()
        {
            var accounts = new List<RobloxAccount>();
            foreach (RobloxAccount s in SelectedAccounts())
            {
                if (!string.IsNullOrEmpty(s.Cookie)) { accounts.Add(s); }
            }
            if (accounts.Count == 0)
            {
                ShowWarning(Utils.Localization.T("Select an account with a saved session — or use “Log in to Roblox”."));
                return;
            }

            if (_openBrowserBtn != null) { _openBrowserBtn.Enabled = false; }
            try
            {
                for (int i = 0; i < accounts.Count; i++)
                {
                    RobloxAccount a = accounts[i];
                    await RobloxAccountBrowser.OpenAsync(a.Name, a.Cookie);
                    a.TimesUsed++;
                    a.LastUsedUtc = DateTime.UtcNow;
                    _lastLaunchedAccountName = a.Name;
                    if (i < accounts.Count - 1) { await Task.Delay(1200); }
                }
            }
            catch (Exception ex)
            {
                Utils.Logger.Warn("[Accounts] open-in-browser failed: " + ex.Message);
                ShowWarning(Utils.Localization.T("Tempo couldn't open the account in a browser — try again."));
            }
            finally
            {
                RefreshAccountButtons();   // restore the button's enabled state even if an open threw
            }
            try { _accounts.Save(); } catch (Exception ex) { Utils.Logger.Warn("[Accounts] save after open-in-browser failed: " + ex.Message); }
            RefreshAccountList();
        }

        private void SetLaunchButtonsEnabled(bool on)
        {
            if (on)
            {
                RefreshAccountButtons();   // restore proper enabled state from the selection
            }
            else
            {
                if (_joinBtn != null) { _joinBtn.Enabled = false; }
                if (_followBtn != null) { _followBtn.Enabled = false; }
                if (_launchHomeBtn != null) { _launchHomeBtn.Enabled = false; }
            }
        }

        private static long ParsePlaceId(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) { return 0; }
            text = text.Trim();
            // Accept a bare id or a roblox.com/games/<id> link pasted in.
            long id = RobloxLauncher.PlaceIdFromUrl(text);
            if (id > 0) { return id; }
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long p) && p > 0 ? p : 0;
        }

        // ── game preview for the typed Place ID ──────────────────────────────────

        /// <summary>Place ID box changed: debounce, then look the game up (or clear when empty/invalid).</summary>
        private void OnPlaceIdChanged()
        {
            long pid = ParsePlaceId(_placeIdBox?.Text);
            if (pid <= 0) { ResetGamePreview(); return; }
            if (_placeLookupTimer == null)
            {
                _placeLookupTimer = new System.Windows.Forms.Timer { Interval = 500 };
                _placeLookupTimer.Tick += (s, e) => { _placeLookupTimer.Stop(); _ = RunPlaceLookup(); };
            }
            _placeLookupTimer.Stop();          // wait until typing pauses before hitting Roblox
            _placeLookupTimer.Start();
        }

        private async Task RunPlaceLookup()
        {
            long pid = ParsePlaceId(_placeIdBox?.Text);
            if (pid <= 0) { HideGamePreview(); return; }
            if (pid == _lastResolvedPlace) { return; }                                 // already shown
            if (_getRobloxBtn != null && _getRobloxBtn.Visible) { HideGamePreview(); return; }  // no client installed

            int seq = ++_placeLookupSeq;
            _gamePreviewPic.Visible = false;
            _gamePreviewBy.Visible = false;
            _gamePreviewStats.Visible = false;
            _gamePreviewName.ForeColor = _theme.TextMuted;
            _gamePreviewName.Text = Utils.Localization.T("Looking up game…");
            _gamePreviewName.Visible = true;

            RobloxApi.GameInfo info = await RobloxApi.GetGameInfoAsync(pid);
            if (seq != _placeLookupSeq) { return; }                                    // superseded by newer typing
            if (info == null)
            {
                _gamePreviewPic.Visible = false;
                _gamePreviewBy.Visible = false;
                _gamePreviewStats.Visible = false;
                _gamePreviewName.ForeColor = _theme.WarningText;
                _gamePreviewName.Text = Utils.Localization.T("No game found for that Place ID.");
                _gamePreviewName.Visible = true;
                _lastResolvedPlace = 0;
                return;
            }

            _lastResolvedPlace = pid;
            _gamePreviewName.ForeColor = _theme.Text;
            _gamePreviewName.Text = string.IsNullOrWhiteSpace(info.Name)
                ? Utils.Localization.T("Roblox game") : info.Name;
            _gamePreviewName.Visible = true;
            if (!string.IsNullOrWhiteSpace(info.Creator))
            {
                _gamePreviewBy.Text = Utils.Localization.F("by {0}", info.Creator);
                _gamePreviewBy.Visible = true;
            }

            // More details: active players · like % · total visits · server size. Only the parts
            // Roblox returned are shown (each is best-effort), joined compactly.
            var stats = new List<string>();
            if (info.Playing >= 0) { stats.Add(Utils.Localization.F("{0} playing", FormatCount(info.Playing))); }
            if (info.LikePercent >= 0) { stats.Add(Utils.Localization.F("{0}% liked", info.LikePercent)); }
            if (info.Visits >= 0) { stats.Add(Utils.Localization.F("{0} visits", FormatCount(info.Visits))); }
            if (info.MaxPlayers > 0) { stats.Add(Utils.Localization.F("max {0}", info.MaxPlayers)); }
            if (stats.Count > 0)
            {
                _gamePreviewStats.Text = string.Join(" · ", stats);
                _gamePreviewStats.Visible = true;
            }

            if (!string.IsNullOrWhiteSpace(info.IconUrl))
            {
                byte[] bytes = await RobloxApi.GetImageBytesAsync(info.IconUrl);
                if (seq != _placeLookupSeq) { return; }
                if (bytes != null)
                {
                    try
                    {
                        var img = System.Drawing.Image.FromStream(new System.IO.MemoryStream(bytes));
                        System.Drawing.Image old = _gamePreviewPic.Image;
                        _gamePreviewPic.Image = img;
                        old?.Dispose();
                        _gamePreviewPic.Visible = true;
                    }
                    catch { }
                }
            }
        }

        private void HideGamePreview()
        {
            if (_gamePreviewName != null) { _gamePreviewName.Visible = false; }
            if (_gamePreviewBy != null) { _gamePreviewBy.Visible = false; }
            if (_gamePreviewStats != null) { _gamePreviewStats.Visible = false; }
            if (_gamePreviewPic != null) { _gamePreviewPic.Visible = false; }
        }

        /// <summary>Compact human count: 12,345 → "12.3K", 1,200,000 → "1.2M", 32,000,000,000 → "32B".</summary>
        private static string FormatCount(long n)
        {
            if (n < 0) { return ""; }
            if (n >= 1_000_000_000) { return (n / 1_000_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "B"; }
            if (n >= 1_000_000) { return (n / 1_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M"; }
            if (n >= 1_000) { return (n / 1_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "K"; }
            return n.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Clear any resolved game and show the idle hint — unless the Get-Roblox button owns the slot.</summary>
        private void ResetGamePreview()
        {
            _lastResolvedPlace = 0;
            _placeLookupSeq++;                 // cancel any in-flight lookup
            _placeLookupTimer?.Stop();
            if (_gamePreviewPic != null) { _gamePreviewPic.Visible = false; }
            if (_gamePreviewBy != null) { _gamePreviewBy.Visible = false; }
            if (_gamePreviewStats != null) { _gamePreviewStats.Visible = false; }
            if (_gamePreviewName == null) { return; }
            if (_getRobloxBtn != null && _getRobloxBtn.Visible)
            {
                _gamePreviewName.Visible = false;   // "Get Roblox" is using this slot
                return;
            }
            _gamePreviewName.ForeColor = _theme.TextMuted;
            _gamePreviewName.Text = Utils.Localization.T("Type a Place ID to preview the game.");
            _gamePreviewName.Visible = true;
        }

        private void EditSelectedAccount()
        {
            RobloxAccount a = SelectedAccount();
            if (a == null) { return; }

            using (var dlg = new AccountDialog(_theme, a))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) { return; }

                RobloxAccount edited = dlg.Result;
                if (!string.Equals(edited.Name, a.Name, StringComparison.Ordinal)
                    && !_accounts.Rename(a.Name, edited.Name))
                {
                    ShowWarning(Utils.Localization.F("Another account is already called “{0}”.", edited.Name));
                    return;
                }

                a.Username = edited.Username;
                a.Alias = edited.Alias;
                a.UserId = edited.UserId;
                a.Icon = edited.Icon;
                a.Note = edited.Note;
                _accounts.Save();
                RefreshAccountList();
                SelectAccount(a.Name);
            }
        }

        private void RemoveSelectedAccount()
        {
            var sel = SelectedAccounts();
            if (sel.Count == 0) { return; }

            string prompt = sel.Count == 1
                ? Utils.Localization.F("Remove the account “{0}”?", sel[0].Name)
                : Utils.Localization.F("Remove {0} selected accounts?", sel.Count);
            var confirm = MessageBox.Show(this,
                prompt + Utils.Localization.T(
                    " Any saved sessions will be erased. Nothing happens to the Roblox accounts themselves."),
                "Tempo", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) { return; }

            foreach (RobloxAccount a in sel) { _accounts.Remove(a.Name); }
            _accounts.Save();
            RefreshAccountList();
            Utils.Logger.Info("[Accounts] removed " + sel.Count + " account(s).");
        }

        // ── multi-instance ──────────────────────────────────────────────────────

        private void SyncMultiInstanceCheck()
        {
            if (_multiInstanceCheck == null) { return; }
            _updatingMultiInstance = true;
            _multiInstanceCheck.Checked = _settings != null && _settings.RobloxMultiInstance;
            _updatingMultiInstance = false;
        }

        private void OnMultiInstanceToggled()
        {
            if (_updatingMultiInstance || _settings == null) { return; }
            if (_multiInstanceCheck.Checked)
            {
                var r = MessageBox.Show(this,
                    Utils.Localization.T(
                        "Turn on multi-instance? Tempo closes Roblox's single-client lock from outside so several "
                        + "accounts can run at once. It is against Roblox's rules and CAN GET ACCOUNTS BANNED. "
                        + "Turn it on anyway?"),
                    "Tempo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes)
                {
                    _updatingMultiInstance = true;
                    _multiInstanceCheck.Checked = false;
                    _updatingMultiInstance = false;
                    return;
                }
                _settings.RobloxMultiInstance = true;
                RobloxLauncher.EnableMultiInstance();
            }
            else
            {
                _settings.RobloxMultiInstance = false;
                RobloxLauncher.DisableMultiInstance();
            }
            try { SettingsManager.Save(_settings); } catch { }
            UpdateMultiInstanceStatus();   // reflect on/off instantly, then the timer keeps it live
        }

        /// <summary>
        /// Live multi-instance status dot. When the toggle is on, the closer runs a scanner: this
        /// reads what it found and shows GREEN "active" while it is managing a running Roblox client
        /// (that's the "it's actually working" the user wants to see), amber while waiting for Roblox
        /// to start, and red if Roblox is up but the anti-cheat is blocking access. Blank when off.
        /// </summary>
        private void UpdateMultiInstanceStatus()
        {
            if (_multiInstanceStatus == null) { return; }
            if (_settings == null || !_settings.RobloxMultiInstance)
            {
                _multiInstanceStatus.Text = "";
                return;
            }

            switch (RobloxSingletonKiller.Status)
            {
                case RobloxSingletonKiller.KillerStatus.Active:
                    int n = RobloxSingletonKiller.ActiveClientCount;
                    _multiInstanceStatus.ForeColor = _theme.SuccessText;
                    _multiInstanceStatus.Text = "● " + (n == 1
                        ? Utils.Localization.T("Active — managing 1 running Roblox client")
                        : Utils.Localization.F("Active — managing {0} running Roblox clients", n));
                    break;
                case RobloxSingletonKiller.KillerStatus.Blocked:
                    _multiInstanceStatus.ForeColor = _theme.DangerText;
                    _multiInstanceStatus.Text = "● " + Utils.Localization.T(
                        "Roblox is running but the anti-cheat is blocking access — can't multi-instance.");
                    break;
                default: // Waiting (on, no Roblox yet)
                    _multiInstanceStatus.ForeColor = _theme.WarningText;
                    _multiInstanceStatus.Text = "● " + Utils.Localization.T("On — scanning; start Roblox to run several.");
                    break;
            }
        }

        private void StartMultiStatusTimer()
        {
            if (_multiStatusTimer == null)
            {
                _multiStatusTimer = new System.Windows.Forms.Timer { Interval = 1000 };
                _multiStatusTimer.Tick += (s, e) => UpdateMultiInstanceStatus();
            }
            _multiStatusTimer.Start();
            UpdateMultiInstanceStatus();   // paint once immediately
        }

        private void StopMultiStatusTimer()
        {
            _multiStatusTimer?.Stop();
        }

        /// <summary>
        /// While the unlocked list is on screen, re-validate every saved session every few minutes so
        /// the dots stay honest — a session that expires mid-session flips to red on its own without
        /// the user having to run "Check sessions now". Gentle (single-flight, 250ms/account), and it
        /// updates in place rather than flashing everything orange.
        /// </summary>
        private void StartSessionRescanTimer()
        {
            if (_sessionRescanTimer == null)
            {
                _sessionRescanTimer = new System.Windows.Forms.Timer { Interval = 180000 };   // every 3 min
                _sessionRescanTimer.Tick += (s, e) => { _ = AutoCheckSessionsAsync(forceAll: true); };
            }
            _sessionRescanTimer.Start();
        }

        private void StopSessionRescanTimer()
        {
            _sessionRescanTimer?.Stop();
        }

        // ── Roblox client status ────────────────────────────────────────────────

        private void RefreshRobloxStatus()
        {
            if (_robloxStatus == null) { return; }

            string player = RobloxInstall.LocatePlayer();
            _lastRobloxSig = SigFor(player);   // remember what we're reporting on, for the auto-refresh watch
            if (player == null)
            {
                _robloxStatus.ForeColor = _theme.WarningText;
                _robloxStatus.Text = Utils.Localization.T("Roblox isn't installed — install it to launch accounts.");
                if (_getRobloxBtn != null) { _getRobloxBtn.Visible = true; }
                HideGamePreview();   // the Get-Roblox button uses this same spot
                return;
            }

            if (_getRobloxBtn != null) { _getRobloxBtn.Visible = false; }
            if (ParsePlaceId(_placeIdBox?.Text) <= 0) { ResetGamePreview(); }   // free slot → show the idle hint
            RobloxAuthenticity verdict = RobloxInstall.CheckAuthenticity(player, out _);
            switch (verdict)
            {
                case RobloxAuthenticity.Official:
                    _robloxStatus.ForeColor = _theme.SuccessText;
                    _robloxStatus.Text = Utils.Localization.T("Roblox: official client detected.");
                    break;
                case RobloxAuthenticity.Modified:
                    _robloxStatus.ForeColor = _theme.DangerText;
                    _robloxStatus.Text = Utils.Localization.T("⚠ Your Roblox client looks modified — launching may be unsafe.");
                    break;
                case RobloxAuthenticity.WrongPublisher:
                    _robloxStatus.ForeColor = _theme.WarningText;
                    _robloxStatus.Text = Utils.Localization.T("⚠ Roblox is signed by someone other than Roblox Corporation.");
                    break;
                default: // Unsigned
                    _robloxStatus.ForeColor = _theme.WarningText;
                    _robloxStatus.Text = Utils.Localization.T("⚠ Your Roblox client isn't signed — can't confirm it's official.");
                    break;
            }
        }

        /// <summary>A cheap fingerprint of the installed client — its path + last-write time — so the
        /// watch can tell whether anything changed without re-verifying the signature every tick.</summary>
        private static string SigFor(string player)
        {
            if (string.IsNullOrEmpty(player)) { return "none"; }
            try { return player + "|" + System.IO.File.GetLastWriteTimeUtc(player).Ticks; }
            catch { return player; }
        }

        /// <summary>
        /// Auto-refresh the official/modified badge: while the unlocked list is on screen, a light 3s
        /// timer re-fingerprints the install (a directory scan, off the UI thread) and re-runs the FULL
        /// signature check ONLY when the client path or its bytes changed — installed, updated, or
        /// swapped for a modified exe. So if the client changes under the user, the badge follows on
        /// its own; no work when nothing changed.
        /// </summary>
        private void StartRobloxWatch()
        {
            if (_robloxWatch == null)
            {
                _robloxWatch = new System.Windows.Forms.Timer { Interval = 3000 };
                _robloxWatch.Tick += (s, e) => CheckRobloxChanged();
            }
            _robloxWatch.Start();
        }

        private void StopRobloxWatch()
        {
            _robloxWatch?.Stop();
        }

        private async void CheckRobloxChanged()
        {
            string sig;
            try { sig = await Task.Run(() => SigFor(RobloxInstall.LocatePlayer())); }
            catch { return; }
            if (IsDisposed || _robloxStatus == null) { return; }
            if (sig == _lastRobloxSig) { return; }   // unchanged — the cheap path, no signature re-verify
            RefreshRobloxStatus();                   // changed → re-verify + relabel (also refreshes _lastRobloxSig)
        }

        // ── sign-in browser picker ───────────────────────────────────────────────

        private void PopulateBrowserCombo()
        {
            if (_browserCombo == null) { return; }
            _updatingBrowserCombo = true;
            try
            {
                _browserCombo.Items.Clear();
                _browserCombo.Items.Add(Utils.Localization.T("Automatic"));
                List<RealBrowser.Info> installed = RealBrowser.Installed();
                foreach (RealBrowser.Info b in installed) { _browserCombo.Items.Add(b.Name); }

                // Select the saved choice if it's still installed, else fall back to Automatic.
                string want = _settings?.LoginBrowser ?? "";
                int idx = 0;
                if (!string.IsNullOrWhiteSpace(want))
                {
                    for (int i = 0; i < installed.Count; i++)
                    {
                        if (string.Equals(installed[i].Name, want, StringComparison.OrdinalIgnoreCase))
                        {
                            idx = i + 1; break;
                        }
                    }
                }
                _browserCombo.SelectedIndex = idx;
            }
            finally { _updatingBrowserCombo = false; }
        }

        private void OnBrowserPicked()
        {
            if (_updatingBrowserCombo || _settings == null || _browserCombo == null) { return; }
            // Item 0 is "Automatic"; items 1.. are installed browser names (Chrome/Edge/Brave/Opera).
            int i = _browserCombo.SelectedIndex;
            _settings.LoginBrowser = i <= 0 ? "" : (_browserCombo.SelectedItem as string ?? "");
            try { SettingsManager.Save(_settings); } catch { }
        }

        // ── local API server ──────────────────────────────────────────────────

        private void ConfigureApiServer()
        {
            if (_settings == null) { return; }
            using (var dlg = new AccountApiDialog(_theme, _settings))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) { return; }
                try { SettingsManager.Save(_settings); } catch { }
                StopApiServer();
                StartApiServerIfEnabled();
                RefreshApiButton();
            }
        }

        private void RefreshApiButton()
        {
            if (_apiBtn == null) { return; }
            bool on = _apiServer != null && _apiServer.IsRunning;
            _apiBtn.Text = on ? Utils.Localization.T("Local API: on") : Utils.Localization.T("Local API…");
        }

        private void StartApiServerIfEnabled()
        {
            if (_settings == null || !_settings.AccountApiEnabled) { return; }
            if (!_accounts.IsUnlocked) { return; }
            if (_apiServer != null && _apiServer.IsRunning) { return; }

            if (string.IsNullOrEmpty(_settings.AccountApiToken))
            {
                _settings.AccountApiToken = AccountApiServer.NewToken();
                try { SettingsManager.Save(_settings); } catch { }
            }

            _apiServer = new AccountApiServer(
                _settings.AccountApiPort,
                _settings.AccountApiToken,
                ApiAccountNames,
                ApiLaunchCore,
                ApiCurrentAccount,
                ApiReloginCore,
                () => _accounts.IsUnlocked);

            if (!_apiServer.Start())
            {
                _apiServer = null;
                ShowWarning(Utils.Localization.F(
                    "Couldn't start the local API server on port {0}. Another program may be using it — pick a different port.",
                    _settings.AccountApiPort));
            }
        }

        private void StopApiServer()
        {
            if (_apiServer != null)
            {
                _apiServer.Stop();
                _apiServer = null;
            }
        }

        private string[] ApiAccountNames()
        {
            if (InvokeRequired) { return (string[])Invoke(new Func<string[]>(ApiAccountNames)); }
            var names = new List<string>();
            foreach (RobloxAccount a in _accounts.Accounts) { names.Add(a.Name); }
            return names.ToArray();
        }

        private ApiLaunchResult ApiLaunchCore(string name, long placeId)
        {
            if (InvokeRequired)
            {
                return (ApiLaunchResult)Invoke(new Func<string, long, ApiLaunchResult>(ApiLaunchCore), name, placeId);
            }
            if (!_accounts.IsUnlocked) { return ApiLaunchResult.Locked; }
            RobloxAccount a = _accounts.GetByName(name);
            if (a == null) { return ApiLaunchResult.UnknownAccount; }
            if (string.IsNullOrEmpty(a.Cookie)) { return ApiLaunchResult.NoSession; }
            if (!RobloxInstall.IsInstalled()) { return ApiLaunchResult.NotInstalled; }

            if (_settings != null && _settings.RobloxMultiInstance) { RobloxLauncher.EnableMultiInstance(); }
            bool home = placeId <= 0;   // no place id over the API means "launch home"
            _ = RobloxLauncher.LaunchAsync(a.Cookie, placeId, null, home);

            a.TimesUsed++;
            a.LastUsedUtc = DateTime.UtcNow;
            _lastLaunchedAccountName = a.Name;
            _accounts.Save();
            RefreshAccountList();
            SelectAccount(a.Name);
            Utils.Logger.Info("[AccountApi] launch requested for an account.");
            return ApiLaunchResult.Launched;
        }

        private string ApiCurrentAccount()
        {
            return _lastLaunchedAccountName ?? "";
        }

        private ApiLaunchResult ApiReloginCore(string name)
        {
            if (InvokeRequired)
            {
                return (ApiLaunchResult)Invoke(new Func<string, ApiLaunchResult>(ApiReloginCore), name);
            }
            if (!_accounts.IsUnlocked) { return ApiLaunchResult.Locked; }
            RobloxAccount a = _accounts.GetByName(name);
            if (a == null) { return ApiLaunchResult.UnknownAccount; }
            BeginInvoke(new Action(() => ReloginAccountByName(name)));
            Utils.Logger.Info("[AccountApi] re-login requested for an account.");
            return ApiLaunchResult.Launched;
        }
    }
}
