using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// Any tab in a window of its own: right-click its row in the sidebar → "Open in its own window"
    /// (the user's idea, 2026-11-03, which replaced the Accounts-only "Tab or its own window" setting).
    ///
    /// The mechanism is the one the Accounts window proved: WinForms will not parent a TabPage to a
    /// Form, so the window (<see cref="DetachedPageWindow"/>) carries a page of its own and the tab's
    /// CONTROLS move onto it — every control, handler, timer and theme role comes along, and putting
    /// the tab back is the same move in reverse. The tab itself stays in <c>_tabs</c>, so its sidebar
    /// row, Ctrl+N and the remembered last tab keep pointing at something; while its controls are
    /// away it holds a card that says where they went and offers them back.
    ///
    /// Accounts keeps its own window code (MainForm.Accounts.cs, with its four vault screens and
    /// AccountsPlacement); everything here hands that tab to it.
    ///
    /// Closing a tab's window is not putting the tab back: its controls come home (a closing form
    /// disposes what is on it), but the tab stays "in its own window" and its row opens the window
    /// again, where it was. Only "Bring it back here" / "Put it back in the main window" docks it.
    /// No window opens by itself at start-up: the row, Ctrl+N or a link to the tab opens it.
    /// </summary>
    public partial class MainForm
    {
        /// <summary>One tab out in its own window.</summary>
        private sealed class TabWindow
        {
            public TabPage Tab;
            public DetachedPageWindow Window;
            /// <summary>The window's page, which the tab's controls are on while the window is open.</summary>
            public BackdropTabPage Page;
        }

        /// <summary>The tabs whose window is open right now (Accounts never: it has its own).</summary>
        private readonly Dictionary<TabPage, TabWindow> _tabWindows = new Dictionary<TabPage, TabWindow>();

        /// <summary>The card each tab holds while its controls are in the window, built once per tab.</summary>
        private readonly Dictionary<TabPage, Panel> _tabWindowNotes = new Dictionary<TabPage, Panel>();

        /// <summary>The tab windows minimised for a macro, to be put back after it (see MinimizeForMacro).</summary>
        private readonly List<DetachedPageWindow> _tabWindowsMinimizedForMacro = new List<DetachedPageWindow>();

        /// <summary>Set while Tempo exits, so a window's close handler stands down.</summary>
        private bool _tabWindowsTearingDown;

        /// <summary>The sidebar rows' right-click menu (one, shared; filled for the row it opens on).</summary>
        private ContextMenuStrip _navMenu;

        /// <summary>
        /// For tests only: tab windows open centred on this area (off every screen), stay off the
        /// taskbar, and never take the foreground — so a harness can open them while someone is using
        /// the PC. Null in Tempo itself.
        /// </summary>
        internal static Rectangle? TabWindowAreaForTest;

        // ── What is where ─────────────────────────────────────────────────────

        private TabPage TabByKey(string key)
        {
            int i = IndexOfTabKey(key);
            return i >= 0 ? _tabs.TabPages[i] : null;
        }

        /// <summary>
        /// True for a tab kept in its own window: its sidebar row, Ctrl+N and links to it open the
        /// window instead of selecting the tab. The setting, not whether the window is open now.
        /// </summary>
        private bool IsWindowedTab(TabPage tab)
        {
            if (tab == null || _settings == null) { return false; }
            if (_accountsPage != null && ReferenceEquals(tab, _accountsPage))
            {
                return _settings.AccountsPlacement == AccountsPlacement.Window;
            }
            return _settings.WindowedTabs != null && _settings.WindowedTabs.Contains(tab.Name);
        }

        /// <summary>The tab's window while it is open, or null.</summary>
        private TabWindow OpenTabWindowOf(TabPage tab)
        {
            if (tab == null || !_tabWindows.TryGetValue(tab, out TabWindow tw)) { return null; }
            return tw.Window != null && !tw.Window.IsDisposed ? tw : null;
        }

        /// <summary>
        /// The page a tab's controls are on right now: its window's page while that is open, the tab
        /// otherwise. Code that lays a page out, adds cards to it or scrolls it goes through this —
        /// working on the tab while its controls are in the window would move nothing anyone can see.
        /// </summary>
        private TabPage PageHost(TabPage tab)
        {
            if (tab == null) { return null; }
            if (_accountsPage != null && ReferenceEquals(tab, _accountsPage)) { return AccountsHost; }
            TabWindow tw = OpenTabWindowOf(tab);
            return tw != null ? tw.Page : tab;
        }

        /// <summary>
        /// True while a tab's controls are on screen: in its own window (shown and not minimised), or
        /// as the open tab of a main window that is up. The live parts of a page — the Statistics
        /// cards, the caption transcript, the Clicker's PC readings — update only while this holds.
        /// </summary>
        private bool IsPageShowing(TabPage tab)
        {
            if (tab == null) { return false; }
            if (_accountsPage != null && ReferenceEquals(tab, _accountsPage)) { return AccountsHostOnScreen(); }
            TabWindow tw = OpenTabWindowOf(tab);
            if (tw != null) { return tw.Window.Visible && tw.Window.WindowState != FormWindowState.Minimized; }
            return Visible && WindowState != FormWindowState.Minimized
                && _tabs != null && ReferenceEquals(_tabs.SelectedTab, tab);
        }

        /// <summary>
        /// <see cref="IsPageShowing"/> for a page found by walking up from one of its controls, which is
        /// the tab itself or, while its controls are away, its window's page.
        /// </summary>
        private bool IsHostPageShowing(TabPage page)
        {
            if (page == null) { return false; }
            foreach (TabWindow tw in _tabWindows.Values)
            {
                if (ReferenceEquals(tw.Page, page))
                {
                    return tw.Window != null && !tw.Window.IsDisposed && tw.Window.Visible
                        && tw.Window.WindowState != FormWindowState.Minimized;
                }
            }
            if (_accountsWindowPage != null && ReferenceEquals(page, _accountsWindowPage)) { return AccountsHostOnScreen(); }
            return Visible && WindowState != FormWindowState.Minimized
                && _tabs != null && ReferenceEquals(_tabs.SelectedTab, page);
        }

        /// <summary>
        /// True while any page window — a tab's, or the Accounts one — is up. Tempo is in use then even
        /// with the main window in the tray, so it must not go to sleep under it.
        /// </summary>
        private bool AnyTabWindowShowing()
        {
            foreach (TabWindow tw in _tabWindows.Values)
            {
                if (tw.Window != null && !tw.Window.IsDisposed && tw.Window.Visible
                    && tw.Window.WindowState != FormWindowState.Minimized)
                {
                    return true;
                }
            }
            return AccountsAreDetached && _accountsWindow.Visible && _accountsWindow.WindowState != FormWindowState.Minimized;
        }

        // ── Getting to a tab ──────────────────────────────────────────────────

        /// <summary>
        /// A click on a windowed tab's row, Ctrl+N, or a link to it: opens or raises its window rather
        /// than selecting a tab whose controls are elsewhere. True when it handled the ask.
        /// </summary>
        private bool TabWindowNavHandled(TabPage tab)
        {
            if (!IsWindowedTab(tab)) { return false; }
            if (_accountsPage != null && ReferenceEquals(tab, _accountsPage)) { return AccountsNavHandled(tab); }
            OpenTabWindow(tab, focus: true);
            return true;
        }

        /// <summary>
        /// Puts a tab in front of the user wherever it lives: its own window, or the main window —
        /// brought back from the tray or from under a tab window when it is not the one in front.
        /// For buttons that lead to another tab ("All caption settings…", the status bar's shortcuts),
        /// which used to switch the main window's tab even while the user was in a tab's window.
        /// </summary>
        private void ShowTab(string key)
        {
            TabPage tab = TabByKey(key);
            if (tab == null) { return; }
            if (TabWindowNavHandled(tab)) { return; }
            if (!Visible || WindowState == FormWindowState.Minimized)
            {
                ShowFromTrayOnTab(key);
                return;
            }
            SelectTabByKey(key);
            if (!ReferenceEquals(Form.ActiveForm, this) && !TabWindowAreaForTest.HasValue)
            {
                try { ForceForegroundOf(this); } catch (Exception ex) { Logger.Swallow("ShowTab", ex); }
            }
        }

        /// <summary>
        /// The main window's tab when the one it shows is leaving for its own window: the first tab
        /// that is not in a window of its own. None (every tab is out) leaves it where it is — on
        /// that tab's card.
        /// </summary>
        private void SelectAnotherTab(TabPage leaving)
        {
            if (_tabs == null) { return; }
            for (int i = 0; i < _tabs.TabPages.Count; i++)
            {
                TabPage t = _tabs.TabPages[i];
                if (ReferenceEquals(t, leaving) || IsWindowedTab(t)) { continue; }
                SaveActiveTabScroll();
                _tabs.SelectedIndex = i;
                return;
            }
        }

        // ── Into a window and back ────────────────────────────────────────────

        /// <summary>
        /// The right-click menu's choice: keep <paramref name="tab"/> in its own window (and open it), or
        /// put it back in the main window (and show it there). Remembered at once.
        /// </summary>
        private void SetTabWindowed(TabPage tab, bool windowed)
        {
            if (tab == null || _settings == null) { return; }
            if (_accountsPage != null && ReferenceEquals(tab, _accountsPage))
            {
                _settings.AccountsPlacement = windowed ? AccountsPlacement.Window : AccountsPlacement.Tab;
                SaveSettingsQuietly();
                if (windowed)
                {
                    if (ReferenceEquals(_tabs.SelectedTab, tab)) { SelectAnotherTab(tab); }
                    OpenAccountsWindow(focus: true);
                }
                else
                {
                    DockAccountsIntoTab();
                    ShowTab(tab.Name);
                }
                OnTabPlacementChanged(tab);
                return;
            }

            if (_settings.WindowedTabs == null) { _settings.WindowedTabs = new List<string>(); }
            if (windowed)
            {
                if (!_settings.WindowedTabs.Contains(tab.Name)) { _settings.WindowedTabs.Add(tab.Name); }
                SaveSettingsQuietly();
                OpenTabWindow(tab, focus: true);
            }
            else
            {
                _settings.WindowedTabs.Remove(tab.Name);
                SaveSettingsQuietly();
                DockTabWindow(tab);
                ShowTab(tab.Name);
            }
            OnTabPlacementChanged(tab);
        }

        /// <summary>A tab went into its own window or came back: the row's ↗ and its tip follow.</summary>
        private void OnTabPlacementChanged(TabPage tab)
        {
            RefreshSidebarSelection();
            RefreshNavTips();
            if (tab != null)
            {
                Logger.Info("[TabWindow] " + tab.Name + " now opens " + (IsWindowedTab(tab) ? "in its own window." : "as a tab in the main window."));
            }
        }

        /// <summary>
        /// Shows <paramref name="tab"/>'s controls in their own window, making the window the first time
        /// and raising it after that. Accounts is handed to its own code.
        /// </summary>
        private void OpenTabWindow(TabPage tab, bool focus)
        {
            if (tab == null || _tabs == null) { return; }
            if (_accountsPage != null && ReferenceEquals(tab, _accountsPage)) { OpenAccountsWindow(focus); return; }

            TabWindow open = OpenTabWindowOf(tab);
            if (open != null)
            {
                if (focus) { RaiseTabWindow(open); }
                return;
            }

            TabWindow tw = null;
            try
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                tw = BuildTabWindow(tab);
                ShowTabWindow(tw, focus, clock);
            }
            catch (Exception ex)
            {
                // A window that failed half-way must not hold the tab's controls: they go home, the
                // half-made window goes, and the tab is shown in the main window. The setting stays,
                // so the next click tries the window again.
                Logger.Error("[TabWindow] the " + tab.Name + " window could not open — the tab is shown in the main window instead.", ex);
                // Failed inside BuildTabWindow, it never handed the window back: the table has it.
                if (tw == null) { _tabWindows.TryGetValue(tab, out tw); }
                _tabWindows.Remove(tab);
                if (tw != null)
                {
                    try { MoveTabContent(tw.Page, tab); } catch (Exception ex2) { Logger.Swallow("OpenTabWindow/rehome", ex2); }
                    try { tw.Window.Close(); } catch { }
                    try { tw.Window.Dispose(); } catch { }
                }
                RemoveTabWindowNote(tab);
                try
                {
                    if (!Visible || WindowState == FormWindowState.Minimized) { ShowFromTrayOnTab(tab.Name, inMainWindow: true); }
                    else { SelectTabByKey(tab.Name); }
                }
                catch (Exception ex3) { Logger.Swallow("OpenTabWindow/fallback", ex3); }
            }
        }

        private TabWindow BuildTabWindow(TabPage tab)
        {
            int index = _tabs.TabPages.IndexOf(tab);
            string key = tab.Name;

            // Off the main window first when it is the tab on show there, so it is never seen with
            // half its controls gone. SelectAnotherTab saves where it was scrolled.
            if (ReferenceEquals(_tabs.SelectedTab, tab)) { SelectAnotherTab(tab); }

            // What the content needs, measured on the tab before anything moves: the noted places
            // (EnableAutoFit) are uncentred and unscrolled, and centring never pushes content left of
            // them, so the right-most one is the narrowest page that shows every card whole.
            int maxRight = 0;
            foreach (Control c in tab.Controls)
            {
                if (!_autoFitBaseLeft.TryGetValue(c, out int left)) { continue; }
                maxRight = Math.Max(maxRight, left + c.Width);
            }
            int contentWidth = maxRight > 0 ? maxRight : L(640);

            var page = new BackdropTabPage(tab.Text)
            {
                AutoScroll = tab.AutoScroll,
                Name = key + "-window"
            };

            NavIconKind glyph = index >= 0 && index < NavGlyphs.Length ? NavGlyphs[index] : NavIconKind.None;
            var win = new DetachedPageWindow(tab.Text, _theme, Icon, glyph);
            // The taskbar and Alt+Tab say whose window it is; the header shows the tab's name alone.
            win.Text = tab.Text + " — Tempo";
            win.GlyphColor = NavHueOf(tab);
            if (TabWindowAreaForTest.HasValue) { win.ShowInTaskbar = false; }

            var tw = new TabWindow { Tab = tab, Window = win, Page = page };
            _tabWindows[tab] = tw;
            win.HostPage(page);

            // Narrow enough to sit beside a game, never so narrow that a card is cut. The main window
            // never gives a tab less than it does at its own minimum size, where every tab is laid out
            // to fit (the start-up check's "sideways"); a tab whose content needs less, plus a vertical
            // scroll bar, may go narrower. (The content's noted widths alone were no floor: a hidden
            // label wider than the page counts there, and Keybinds came out 30 px wider than its tab in
            // Portuguese.) A page never shown yet is 200×100 (Tempo started in the tray), so that one
            // is sized from the main window's own numbers instead.
            Size frame = win.Size - win.ClientSize;
            int vbar = SystemInformation.VerticalScrollBarWidth;
            bool sized = tab.Width >= L(400) && tab.Height >= L(300);
            int besidePage = sized ? Width - tab.Width : (_sidebar != null ? _sidebar.Width : L(188));
            int mainMinWidth = MinimumSize.Width > 0 ? MinimumSize.Width : L(980);
            int narrowestTab = Math.Max(L(420), mainMinWidth - besidePage);
            int minClientW = Math.Max(L(420), Math.Min(narrowestTab, contentWidth + vbar));
            int minClientH = win.StripPixels + L(260);
            win.MinimumSize = new Size(minClientW + frame.Width, minClientH + frame.Height);
            // Never placed: as big as the tab is in the main window — its whole size, scroll bar and
            // all (its ClientSize is that much narrower) — so it looks the same.
            Size first = sized ? tab.Size : new Size(narrowestTab, L(700));
            win.ClientSize = new Size(Math.Max(minClientW, first.Width), Math.Max(minClientH, first.Height + win.StripPixels));

            TabWindowPlace place = PlaceOf(key);
            win.Pinned = place.OnTop;
            win.CompanionTopMost = TopMost;
            win.RestoreSavedBounds(place.Left, place.Top, place.Width, place.Height, TabWindowHome());

            win.CommandKey = TabWindowCommandKey;
            win.DockRequested += (s, e) => SetTabWindowed(tab, false);
            win.TopMostChanged += (s, on) =>
            {
                PlaceOf(key).OnTop = on;
                SaveSettingsQuietly();
            };
            // Dragging this window pauses what dragging the main one pauses (the logo frames, the
            // wallpaper animator): they share one UI thread (see DetachedPageWindow.MoveLoopChanged).
            win.MoveLoopChanged += (s, moving) =>
            {
                if (moving) { BeginMoveResizeLoop(tab.Text + " window"); }
                else { EndMoveResizeLoop(); }
            };
            win.BoundsSettled += (s, r) =>
            {
                TabWindowPlace p = PlaceOf(key);
                p.Left = r.Left;
                p.Top = r.Top;
                p.Width = r.Width;
                p.Height = r.Height;
                SaveSettingsQuietly();
            };
            win.FormClosed += (s, e) => OnTabWindowClosed(tw);
            // The main window's net under a keybind field (OnDeactivate), for the Keybinds tab out here: a field
            // listening when the window lost focus would leave every hotkey silenced while you play.
            win.Deactivate += (s, e) =>
            {
                if (_hotkeys != null && _hotkeys.DeliveryPaused)
                {
                    _hotkeys.DeliveryPaused = false;
                    Logger.Info("[Hotkeys] delivery resumed as the " + key + " window lost focus (a keybind field was still listening).");
                }
            };

            // The main window's pages are centred by EnableAutoFit's handlers; this page is not one
            // of them, so it centres itself — and runs what the tab's own resize handlers run.
            page.SizeChanged += (s, e) => OnTabWindowPageResized(tw);
            page.ClientSizeChanged += (s, e) => OnTabWindowPageResized(tw);

            MoveTabContent(tab, page);
            ShowTabWindowNote(tab);
            return tw;
        }

        /// <summary>
        /// Shows a freshly built window with its content laid out, themed and up to date. <paramref name="clock"/>
        /// started before the window was built, for the timings in the log.
        /// </summary>
        private void ShowTabWindow(TabWindow tw, bool focus, System.Diagnostics.Stopwatch clock)
        {
            long built = clock.ElapsedMilliseconds;
            // In one piece, the way the main window opens (SetShowCloak): shown CLOAKED — there for Windows
            // and painting as usual, but not drawn by the compositor — laid out, themed and painted, and
            // only then uncloaked. Shown plainly, its cards could be seen arriving and moving into place.
            IntPtr handle = tw.Window.Handle;
            bool cloaked = SetWindowCloak(handle, true);
            long shown, laid, themed;
            try
            {
                tw.Window.Show();
                shown = clock.ElapsedMilliseconds;
                _lastCenteredWidth.Remove(tw.Page);
                CenterPageContent(tw.Page, true);
                laid = clock.ElapsedMilliseconds;
                ThemeTabWindow(tw);
                themed = clock.ElapsedMilliseconds;
                OnTabContentMoved(tw.Tab);
                if (cloaked)
                {
                    RedrawWindow(handle, IntPtr.Zero, IntPtr.Zero, RDW_INVALIDATE | RDW_ERASE | RDW_FRAME | RDW_ALLCHILDREN | RDW_UPDATENOW);
                }
            }
            finally
            {
                if (cloaked) { SetWindowCloak(handle, false); }
            }
            if (focus) { RaiseTabWindow(tw); }
            long total = clock.ElapsedMilliseconds;
            Logger.Info("[TabWindow] " + tw.Tab.Name + " opened in its own window ("
                        + tw.Window.Bounds.Width + "×" + tw.Window.Bounds.Height + ") in " + total + " ms — build "
                        + built + " · show " + (shown - built) + " · layout " + (laid - shown) + " · theme " + (themed - laid)
                        + " · page " + (total - themed) + ".");
        }

        /// <summary>
        /// "Bring it back here": the tab's controls come home and its window closes. The caller has
        /// already taken the tab out of <see cref="AppSettings.WindowedTabs"/>.
        /// </summary>
        private void DockTabWindow(TabPage tab)
        {
            TabWindow tw = OpenTabWindowOf(tab);
            if (tw == null) { return; }
            // Out of the table FIRST, so the close handler knows this close is a dock and stands aside.
            _tabWindows.Remove(tab);
            try { MoveTabContent(tw.Page, tab); }
            catch (Exception ex) { Logger.Swallow("DockTabWindow/rehome", ex); }
            RemoveTabWindowNote(tab);
            try { tw.Window.Close(); } catch (Exception ex) { Logger.Swallow("DockTabWindow/close", ex); }
            try { tw.Window.Dispose(); } catch { }
            ForgetTabWindowPage(tw);
            OnTabContentMoved(tab);
            Logger.Info("[TabWindow] " + tab.Name + " docked back as a tab.");
        }

        /// <summary>
        /// The user closed a tab's window. The tab stays in its own window (its row opens the window
        /// again, where it was), but its controls must come off a form about to be disposed. WinForms
        /// raises FormClosed and only then disposes, so this runs in time.
        /// </summary>
        private void OnTabWindowClosed(TabWindow tw)
        {
            // Tempo is quitting and CloseTabWindowsOnExit has already put everything back.
            if (_tabWindowsTearingDown) { return; }
            // A dock (or a failed open) took it out of the table first and did the work itself.
            if (!_tabWindows.TryGetValue(tw.Tab, out TabWindow current) || !ReferenceEquals(current, tw)) { return; }
            _tabWindows.Remove(tw.Tab);
            try
            {
                MoveTabContent(tw.Page, tw.Tab);
                RemoveTabWindowNote(tw.Tab);
                ForgetTabWindowPage(tw);
                OnTabContentMoved(tw.Tab);
            }
            catch (Exception ex) { Logger.Swallow("Tab window closed", ex); }
            Logger.Info("[TabWindow] " + tw.Tab.Name + " window closed; its controls are back on the tab (the row opens the window again).");
        }

        /// <summary>
        /// Moves every control from one page to the other, in the same stacking order, and carries the
        /// scroll position across. Tempo's scroll-bar covers are left behind on purpose: each belongs to
        /// a control's parent, and the control makes new ones in its new parent (ScrollSkin).
        /// </summary>
        private void MoveTabContent(TabPage from, TabPage to)
        {
            if (from == null || to == null || from.IsDisposed || to.IsDisposed) { return; }

            // A child's Top includes the page's scroll offset; at 0 they are at their own places, and
            // the page they go to starts at the top. Where it was scrolled is restored over there.
            Point scroll = from.AutoScrollPosition;
            if (scroll != Point.Empty) { from.AutoScrollPosition = Point.Empty; }

            var kids = new List<Control>();
            foreach (Control c in from.Controls) { kids.Add(c); }

            from.SuspendLayout();
            to.SuspendLayout();
            try
            {
                foreach (Control c in kids)
                {
                    if (c == null || c.IsDisposed || c is ScrollCover || IsTabWindowNote(c)) { continue; }
                    from.Controls.Remove(c);
                    to.Controls.Add(c);
                }
            }
            finally
            {
                to.ResumeLayout(false);
                from.ResumeLayout(false);
            }

            _tabScroll[to] = scroll;
            _lastCenteredWidth.Remove(to);
            _lastCenteredWidth.Remove(from);
            if (_theme != null)
            {
                // See-through over a wallpaper, solid without one — whichever the new page has.
                try { ThemeManager.RefreshBackdropBackgrounds(to, _theme); } catch (Exception ex) { Logger.Swallow("MoveTabContent/backdrop", ex); }
            }
            // Laid out for the page they are on now. A page not on show yet is centred without a forced
            // pass: the tab switch forces one (and restores the scroll) when the tab is next shown, and
            // a window being opened does once it is up and has its real size.
            Form host = to.FindForm();
            bool shown = host is DetachedPageWindow
                ? host.Visible
                : _tabs != null && ReferenceEquals(_tabs.SelectedTab, to) && Visible;
            CenterPageContent(to, shown);
        }

        /// <summary>A window's page goes with the window: nothing keeps remembering its scroll or width.</summary>
        private void ForgetTabWindowPage(TabWindow tw)
        {
            if (tw?.Page == null) { return; }
            _tabScroll.Remove(tw.Page);
            _lastCenteredWidth.Remove(tw.Page);
            _scrollRepairQueued.Remove(tw.Page);
        }

        /// <summary>What a tab's own code does after a resize, run for its window's page too.</summary>
        private void OnTabWindowPageResized(TabWindow tw)
        {
            if (tw == null || tw.Page == null || tw.Page.IsDisposed) { return; }
            CenterPageContent(tw.Page);
            switch (tw.Tab.Name)
            {
                case "profiles": LayoutProfileGrid(); break;
                case "captions": FitCaptionsTranscriptToPage(); break;
                case "keybinds": ScheduleKeybindRelayout(); break;
            }
        }

        /// <summary>
        /// A tab's controls changed page: what is laid out by code rather than by the centring is laid
        /// out again for the page they are on, and what refreshes only while on screen catches up.
        /// </summary>
        private void OnTabContentMoved(TabPage tab)
        {
            if (tab == null) { return; }
            try
            {
                switch (tab.Name)
                {
                    case "profiles":
                        LayoutProfileGrid();
                        break;
                    case "captions":
                        FitCaptionsTranscriptToPage();
                        if (IsPageShowing(tab)) { RefreshCaptionsTab(); }
                        break;
                    case "keybinds":
                        RelayoutKeybindNotices();
                        break;
                    case "statistics":
                        if (IsPageShowing(tab))
                        {
                            UpdateStatisticsTab();
                            RefreshSessionHistory();
                        }
                        break;
                }
            }
            catch (Exception ex) { Logger.Swallow("OnTabContentMoved " + tab.Name, ex); }
        }

        /// <summary>Puts a tab's window in front, un-minimising it if needed.</summary>
        private void RaiseTabWindow(TabWindow tw)
        {
            if (tw == null || tw.Window == null || tw.Window.IsDisposed) { return; }
            try
            {
                // Above a floating main window, not under it.
                tw.Window.CompanionTopMost = TopMost;
                if (tw.Window.WindowState == FormWindowState.Minimized) { tw.Window.WindowState = FormWindowState.Normal; }
                tw.Window.Show();
                if (TabWindowAreaForTest.HasValue) { return; }
                // Through the foreground lock: a plain Activate() is refused whenever another app owns
                // the foreground (after the tray menu has closed, say).
                ForceForegroundOf(tw.Window);
                tw.Window.BringToFront();
            }
            catch (Exception ex) { Logger.Swallow("RaiseTabWindow", ex); }
        }

        /// <summary>
        /// The working area a new window opens on: the main window's screen while it shows, else the
        /// screen under the pointer (a click on the tray icon's menu). Each window already open moves
        /// the next one down and right a little, so two never open exactly on top of each other.
        /// </summary>
        private Rectangle TabWindowHome()
        {
            Rectangle area;
            try
            {
                Screen s = Visible && WindowState != FormWindowState.Minimized
                    ? Screen.FromControl(this)
                    : Screen.FromPoint(Cursor.Position);
                area = TabWindowAreaForTest ?? s.WorkingArea;
            }
            catch { area = TabWindowAreaForTest ?? Screen.PrimaryScreen.WorkingArea; }
            int open = Math.Max(0, _tabWindows.Count - 1);
            int step = L(28) * Math.Min(open, 6);
            area.Offset(step, step);
            return area;
        }

        /// <summary>Cloaks or uncloaks another window (DWMWA_CLOAK; see SetShowCloak). False when it could not.</summary>
        private static bool SetWindowCloak(IntPtr handle, bool on)
        {
            if (handle == IntPtr.Zero) { return false; }
            try
            {
                int value = on ? 1 : 0;
                return DwmSetWindowAttribute(handle, DWMWA_CLOAK, ref value, sizeof(int)) == 0;
            }
            catch { return false; }
        }

        /// <summary>The remembered place of a tab's window, made the first time it is asked for.</summary>
        private TabWindowPlace PlaceOf(string key)
        {
            if (_settings.TabWindowPlaces == null)
            {
                _settings.TabWindowPlaces = new Dictionary<string, TabWindowPlace>(StringComparer.OrdinalIgnoreCase);
            }
            if (!_settings.TabWindowPlaces.TryGetValue(key, out TabWindowPlace place) || place == null)
            {
                place = new TabWindowPlace();
                _settings.TabWindowPlaces[key] = place;
            }
            return place;
        }

        /// <summary>A tab's icon hue in the sidebar, for its window's header glyph.</summary>
        private Color NavHueOf(TabPage tab)
        {
            if (_theme == null || _tabs == null) { return Color.Empty; }
            Color[] hues = SectionColors.IsLight(_theme) ? NavHuesLight : NavHuesDark;
            int i = _tabs.TabPages.IndexOf(tab);
            return i >= 0 && i < hues.Length ? _theme.Painted(hues[i]) : _theme.Accent;
        }

        // ── The card on the tab while it is away ──────────────────────────────

        private bool IsTabWindowNote(Control c)
        {
            foreach (Panel note in _tabWindowNotes.Values)
            {
                if (ReferenceEquals(note, c)) { return true; }
            }
            return false;
        }

        /// <summary>
        /// Puts the "it's in its own window" card on the tab. The two ways onto a windowed tab are
        /// intercepted (its row and Ctrl+N raise the window), but not every way is — a remembered last
        /// tab, or every tab being out — so the tab is never blank.
        /// </summary>
        private void ShowTabWindowNote(TabPage tab)
        {
            if (!_tabWindowNotes.TryGetValue(tab, out Panel note) || note == null || note.IsDisposed)
            {
                note = BuildTabWindowNote(tab);
                _tabWindowNotes[tab] = note;
                // Into the page's centring: EnableAutoFit noted only the children a page had at start-up.
                _autoFitBaseLeft[note] = 0;
                _autoFitBaseTop[note] = 0;
            }
            if (!tab.Controls.Contains(note))
            {
                tab.Controls.Add(note);
                // Off the page it missed any theme change since it was last shown.
                if (_theme != null) { ThemeManager.Apply(note, _theme); }
            }
            note.Visible = true;
            note.BringToFront();
            _lastCenteredWidth.Remove(tab);
            CenterPageContent(tab, _tabs != null && ReferenceEquals(_tabs.SelectedTab, tab));
        }

        private void RemoveTabWindowNote(TabPage tab)
        {
            if (tab != null && _tabWindowNotes.TryGetValue(tab, out Panel note) && note != null
                && !note.IsDisposed && tab.Controls.Contains(note))
            {
                tab.Controls.Remove(note);
            }
        }

        /// <summary>
        /// The card itself: what happened, and the two ways on — the window, or the tab back. Built in
        /// 100% numbers, measured in them, and then brought to the window's scale like every screen made
        /// after the window scaled itself.
        /// </summary>
        private Panel BuildTabWindowNote(TabPage tab)
        {
            const int pad = 16, cardWidth = 520, fieldWidth = cardWidth - 2 * pad;
            var p = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(cardWidth + 2 * pad, 240),
                BackColor = _theme.Background,
                Tag = ThemeManager.PageTag
            };

            var card = UiFactory.Group(Localization.T("Open in its own window"), pad, 12, cardWidth, 188, CardIcon.Gauge);

            var body = new Label
            {
                Text = Localization.F("{0} is open in a window of its own, so it can sit beside your game. Click {0} in the sidebar to bring it to the front.", tab.Text),
                Location = new Point(pad, 34),
                Size = new Size(fieldWidth, 44),
                Font = new Font("Segoe UI", 9f),
                ForeColor = _theme.TextMuted
            };

            var show = UiFactory.PrimaryButton(Localization.T("Show the window"), pad, 90, 160, 34, _theme);
            show.Click += (s, e) => OpenTabWindow(tab, focus: true);
            var back = UiFactory.Button(Localization.T("Bring it back here"), pad, 90, 170, 34);
            back.Click += (s, e) => SetTabWindowed(tab, false);

            // Measured, not fixed: "Mostrar la ventana" and "Traerla aquí de nuevo" run past a fixed
            // width, and the card CLIPS, so an overflow would lose the button rather than look cramped.
            UiFactory.FitToText96(show, 130);
            UiFactory.FitToText96(back, 130);
            back.Left = show.Left + show.Width + 10;
            if (back.Left + back.Width > cardWidth - pad)
            {
                back.Left = show.Left;
                back.Top = show.Top + show.Height + 8;
            }

            int needed = UiFactory.Measure96(body.Text, body.Font, body.Width, TextFormatFlags.Default).Height;
            if (needed > body.Height)
            {
                int grew = needed + 4 - body.Height;
                body.Height = needed + 4;
                show.Top += grew;
                back.Top += grew;
            }
            card.Height = Math.Max(show.Bottom, back.Bottom) + 16;

            card.Controls.Add(body);
            card.Controls.Add(show);
            card.Controls.Add(back);
            p.Height = card.Top + card.Height + 12;
            p.Controls.Add(card);

            float k = AppliedWindowScale;
            if (Math.Abs(k - 1f) > 0.02f) { p.Scale(new SizeF(k, k)); }
            LayoutFitter.SetUnitsPerPixel(p, () => LayoutUnitsPerPixel);
            return p;
        }

        // ── Theme, wallpaper, on-top ──────────────────────────────────────────

        /// <summary>
        /// The controls in tab windows, in the new theme. ThemeManager.Apply(this) walks only THIS form's
        /// tree, and a tab window is not in it. Runs right after that walk, so everything that puts back
        /// colours with a meaning (the accent buttons, the stat cards, the status lines) runs after it.
        /// </summary>
        private void ThemeTabWindowPages()
        {
            if (_theme == null) { return; }
            foreach (TabWindow tw in _tabWindows.Values)
            {
                if (tw.Page == null || tw.Page.IsDisposed) { continue; }
                try { ThemeManager.Apply(tw.Page, _theme); }
                catch (Exception ex) { Logger.Swallow("ThemeTabWindowPages", ex); }
            }
        }

        /// <summary>Every tab window's own parts — title bar, header, buttons — and its wallpaper.</summary>
        private void ThemeTabWindows()
        {
            foreach (TabWindow tw in _tabWindows.Values) { ThemeTabWindow(tw); }
        }

        private void ThemeTabWindow(TabWindow tw)
        {
            if (tw?.Window == null || tw.Window.IsDisposed || _theme == null) { return; }
            try
            {
                tw.Window.GlyphColor = NavHueOf(tw.Tab);
                tw.Window.ApplyTheme(_theme);
                SyncTabWindowBackdrop(tw);
            }
            catch (Exception ex) { Logger.Swallow("ThemeTabWindow", ex); }
        }

        /// <summary>The main window's wallpaper in every tab window too, or none in any.</summary>
        private void SyncTabWindowsBackdrop()
        {
            foreach (TabWindow tw in _tabWindows.Values) { SyncTabWindowBackdrop(tw); }
        }

        private void SyncTabWindowBackdrop(TabWindow tw)
        {
            if (tw?.Window == null || tw.Window.IsDisposed) { return; }
            try { tw.Window.SetBackdrop(_fullBgImage, _settings != null ? _settings.BackgroundDim : 55); }
            catch (Exception ex) { Logger.Swallow("SyncTabWindowBackdrop", ex); }
        }

        /// <summary>One wallpaper frame for every tab window, from the main window's single animator.</summary>
        private void InvalidateTabWindowsBackdrop()
        {
            foreach (TabWindow tw in _tabWindows.Values)
            {
                if (tw.Window == null || tw.Window.IsDisposed) { continue; }
                try { tw.Window.InvalidateBackdrop(); } catch { }
            }
        }

        /// <summary>The main window floats ("Always on top", full screen): its tab windows float with it.</summary>
        private void SyncTabWindowsTopMost()
        {
            foreach (TabWindow tw in _tabWindows.Values)
            {
                if (tw.Window == null || tw.Window.IsDisposed) { continue; }
                try { tw.Window.CompanionTopMost = TopMost; }
                catch (Exception ex) { Logger.Swallow("SyncTabWindowsTopMost", ex); }
            }
        }

        // ── Out of the way, and back ──────────────────────────────────────────

        /// <summary>
        /// Hides every page window that is up, for a pick on the screen (the coordinate picker covers
        /// the desktop, and a Tempo window over the spot is what would be picked). Returns the windows
        /// it hid, for <see cref="ShowPageWindowsAfterPick"/>.
        /// </summary>
        private List<Form> HidePageWindowsForPick()
        {
            var hidden = new List<Form>();
            var all = new List<Form>();
            foreach (TabWindow tw in _tabWindows.Values) { all.Add(tw.Window); }
            if (AccountsAreDetached) { all.Add(_accountsWindow); }
            foreach (Form f in all)
            {
                if (f == null || f.IsDisposed || !f.Visible || f.WindowState == FormWindowState.Minimized) { continue; }
                try { f.Hide(); hidden.Add(f); }
                catch (Exception ex) { Logger.Swallow("HidePageWindowsForPick", ex); }
            }
            return hidden;
        }

        /// <summary>Brings back what <see cref="HidePageWindowsForPick"/> hid, repainted whole.</summary>
        private void ShowPageWindowsAfterPick(List<Form> hidden)
        {
            if (hidden == null) { return; }
            foreach (Form f in hidden)
            {
                if (f == null || f.IsDisposed) { continue; }
                // Cloaked until painted whole, like the main window's return from a pick (ShowWholeAfterHide).
                bool cloaked = f.IsHandleCreated && SetWindowCloak(f.Handle, true);
                try
                {
                    f.Show();
                    // A wallpaper makes the labels see-through, and the slices behind them were dropped
                    // while hidden: rebuilt, or the window came back with holes in it.
                    WindowBackdrop.InvalidateCache();
                    if (cloaked)
                    {
                        RedrawWindow(f.Handle, IntPtr.Zero, IntPtr.Zero, RDW_INVALIDATE | RDW_ERASE | RDW_FRAME | RDW_ALLCHILDREN | RDW_UPDATENOW);
                    }
                    else { f.Invalidate(true); }
                }
                catch (Exception ex) { Logger.Swallow("ShowPageWindowsAfterPick", ex); }
                finally
                {
                    if (cloaked) { SetWindowCloak(f.Handle, false); }
                }
            }
        }

        /// <summary>For "Minimise window during macro": the tab windows that are up go down with the main one.</summary>
        private void MinimizeTabWindowsForMacro()
        {
            foreach (TabWindow tw in _tabWindows.Values)
            {
                DetachedPageWindow w = tw.Window;
                if (w == null || w.IsDisposed || !w.Visible || w.WindowState == FormWindowState.Minimized) { continue; }
                try
                {
                    w.WindowState = FormWindowState.Minimized;
                    _tabWindowsMinimizedForMacro.Add(w);
                }
                catch (Exception ex) { Logger.Swallow("MinimizeTabWindowsForMacro", ex); }
            }
        }

        /// <summary>Puts back the tab windows <see cref="MinimizeTabWindowsForMacro"/> took down, when <paramref name="restore"/>.</summary>
        private void RestoreTabWindowsAfterMacro(bool restore)
        {
            var list = new List<DetachedPageWindow>(_tabWindowsMinimizedForMacro);
            _tabWindowsMinimizedForMacro.Clear();
            if (!restore) { return; }
            foreach (DetachedPageWindow w in list)
            {
                if (w == null || w.IsDisposed || w.WindowState != FormWindowState.Minimized) { continue; }
                try { w.WindowState = FormWindowState.Normal; }
                catch (Exception ex) { Logger.Swallow("RestoreTabWindowsAfterMacro", ex); }
            }
        }

        // ── Keys ──────────────────────────────────────────────────────────────

        /// <summary>
        /// The keys the main window handles for the whole window, for a tab window: Escape stops a run
        /// (when that safety is on) and leaves a hotkey field, and Ctrl+1…9 opens a tab wherever it
        /// lives. A page in a window of its own never reaches the main window's ProcessCmdKey, so the
        /// emergency stop on Escape did nothing in it.
        /// </summary>
        private bool TabWindowCommandKey(Keys keyData, Control active)
        {
            Keys key = keyData & Keys.KeyCode;
            if (keyData == Keys.Escape && _settings != null && _settings.SafetyStopOnEscape
                && _engine != null && _engine.IsRunning)
            {
                EmergencyStop();
                return true;
            }
            if (key == Keys.Escape && active is HotkeyCaptureControl capture)
            {
                capture.ReleaseFocusToParent();
                return true;
            }
            if ((keyData & Keys.Modifiers) == Keys.Control && _tabs != null)
            {
                int digit = -1;
                if (key >= Keys.D1 && key <= Keys.D9) { digit = key - Keys.D1; }
                else if (key >= Keys.NumPad1 && key <= Keys.NumPad9) { digit = key - Keys.NumPad1; }
                if (digit >= 0 && digit < _tabs.TabPages.Count)
                {
                    ShowTab(_tabs.TabPages[digit].Name);
                    return true;
                }
            }
            return false;
        }

        // ── The sidebar's right-click menu ────────────────────────────────────

        /// <summary>
        /// Every row's right-click menu (and Shift+F10 / the menu key on a focused row): open the tab in
        /// its own window, or — for one that is — show its window or put it back.
        /// </summary>
        private ContextMenuStrip NavMenu()
        {
            if (_navMenu != null && !_navMenu.IsDisposed) { return _navMenu; }
            _navMenu = new ContextMenuStrip { ShowImageMargin = true, Padding = new Padding(3, 6, 3, 6) };
            _navMenu.Disposed += (s, e) => { try { (_navMenu?.Renderer as ThemedMenuRenderer)?.Dispose(); } catch { } };
            ApplyThemeToNavMenu();
            try { MenuOpenAnimation.Attach(_navMenu); } catch (Exception ex) { Logger.Swallow("NavMenu animation", ex); }
            _navMenu.Opening += (s, e) =>
            {
                TabPage tab = NavMenuTab();
                if (tab == null) { e.Cancel = true; return; }
                FillNavMenu(tab);
            };
            return _navMenu;
        }

        /// <summary>The tab of the row the menu was opened on.</summary>
        private TabPage NavMenuTab()
        {
            int i = _navMenu?.SourceControl is RoundedButton row ? _navButtons.IndexOf(row) : -1;
            return _tabs != null && i >= 0 && i < _tabs.TabPages.Count ? _tabs.TabPages[i] : null;
        }

        private void FillNavMenu(TabPage tab)
        {
            _navMenu.Items.Clear();
            if (_trayMenu != null) { _navMenu.Font = _trayMenu.Font; }
            if (!IsWindowedTab(tab))
            {
                var open = new ToolStripMenuItem(Localization.T("Open in its own window"))
                {
                    Tag = new TrayItemStyle(TrayGlyph.Window)
                };
                open.Click += (s, e) => SetTabWindowed(tab, true);
                _navMenu.Items.Add(open);
                return;
            }
            var show = new ToolStripMenuItem(Localization.T("Show the window"))
            {
                Tag = new TrayItemStyle(TrayGlyph.Window)
            };
            show.Click += (s, e) => TabWindowNavHandled(tab);
            _navMenu.Items.Add(show);
            var back = new ToolStripMenuItem(Localization.T("Put it back in the main window"))
            {
                Tag = new TrayItemStyle(TrayGlyph.Move)
            };
            back.Click += (s, e) => SetTabWindowed(tab, false);
            _navMenu.Items.Add(back);
        }

        /// <summary>The menu's renderer holds an animation timer, so the old one is disposed, not dropped.</summary>
        private void ApplyThemeToNavMenu()
        {
            if (_navMenu == null || _navMenu.IsDisposed || _theme == null) { return; }
            try
            {
                (_navMenu.Renderer as ThemedMenuRenderer)?.Dispose();
                _navMenu.Renderer = new ThemedMenuRenderer(_theme);
                _navMenu.BackColor = _theme.Surface;
                _navMenu.ForeColor = _theme.Text;
            }
            catch (Exception ex) { Logger.Swallow("ApplyThemeToNavMenu", ex); }
        }

        /// <summary>
        /// Each row's tip: where the tab opens, its shortcut, and that the right button moves it. Built
        /// from translated pieces (a sentence assembled in English and then translated whole finds no
        /// key, and stays English). Again whenever a tab moves.
        /// </summary>
        private void RefreshNavTips()
        {
            if (_tips == null || _tabs == null) { return; }
            for (int i = 0; i < _navButtons.Count; i++)
            {
                if (i >= _tabs.TabPages.Count) { break; }
                TabPage tab = _tabs.TabPages[i];
                bool windowed = IsWindowedTab(tab);
                string tip = windowed
                    ? Localization.F("{0} opens in a window of its own", tab.Text)
                    : Localization.F("Open the {0} tab", tab.Text);
                if (i < 9) { tip += "  ·  " + Localization.F("Ctrl+{0}", (i + 1).ToString()); }
                tip += ". " + (windowed
                    ? Localization.T("Right-click to put it back in the main window.")
                    : Localization.T("Right-click to open it in a window of its own."));
                tip += " " + Localization.T("Ctrl+Tab cycles through the tabs. Hold Ctrl to see each tab's number.");
                TipStyle.Set(_tips, _navButtons[i], tip);
                // Screen readers read AccessibleDescription, so the same help is there without a mouse.
                _navButtons[i].AccessibleDescription = tip;
            }
        }

        // ── Going away ────────────────────────────────────────────────────────

        /// <summary>
        /// Takes the tab windows down as Tempo exits, WITHOUT changing which tabs are in their own
        /// window — they still are next launch. Their controls go home first, so the shut-down finds
        /// every control where it was built rather than on a disposed window.
        /// </summary>
        private void CloseTabWindowsOnExit()
        {
            if (_tabWindows.Count == 0) { return; }
            _tabWindowsTearingDown = true;
            foreach (TabWindow tw in new List<TabWindow>(_tabWindows.Values))
            {
                try { MoveTabContent(tw.Page, tw.Tab); } catch (Exception ex) { Logger.Swallow("CloseTabWindowsOnExit/rehome", ex); }
                try { tw.Window.Close(); } catch (Exception ex) { Logger.Swallow("CloseTabWindowsOnExit", ex); }
                try { tw.Window.Dispose(); } catch { }
            }
            _tabWindows.Clear();
        }

        /// <summary>One Live-debug line: which tabs are in their own window, and how each window is.</summary>
        private void AppendTabWindowStats(System.Text.StringBuilder sb)
        {
            if (sb == null || _settings == null) { return; }
            try
            {
                var windowed = _settings.WindowedTabs ?? new List<string>();
                sb.Append("  tab windows: ").Append(windowed.Count == 0 ? "none" : string.Join(", ", windowed));
                foreach (TabWindow tw in _tabWindows.Values)
                {
                    DetachedPageWindow w = tw.Window;
                    if (w == null || w.IsDisposed) { continue; }
                    sb.Append(" · ").Append(tw.Tab.Name).Append(": ").Append(w.WindowState)
                      .Append(w.Visible ? " shown" : " hidden")
                      .Append(w.TopMost ? " on top " : " ")
                      .Append(w.Bounds.Width).Append('×').Append(w.Bounds.Height);
                }
                sb.AppendLine();
            }
            catch (Exception ex)
            {
                sb.Append("  tab windows: (stats error: ").Append(ex.Message).Append(')').AppendLine();
            }
        }
    }
}
