using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// Tempo's own scroll bars, in place of Windows 11's ("replace new scrollbar up/down for UI instead of use windows
    /// 11 scrollbar up/down" — the user, 2026-10-25).
    ///
    /// Windows still OWNS every scroll bar: its range and position, the drag, the arrows, a click on the track, the
    /// wheel, the keyboard and accessibility all stay exactly what they were. Only what is SEEN changes. A paint-only
    /// window lies precisely over each native bar — GetScrollBarInfo gives its rectangle, its arrow buttons and its
    /// thumb — draws Tempo's bar there in the theme, and answers every hit test HTTRANSPARENT, so a click on it lands
    /// on the native bar underneath, which does what it always did. Nothing re-implements scrolling for ten kinds of
    /// control (a ListView counts rows, a RichTextBox pixels, an edit box lines, a page pixels…), so nothing can get
    /// it wrong: the picture follows the native state, read again whenever the control handles a message.
    ///
    /// Where a bar lives decides what covers it. A control's bar: a sibling child window in the control's parent,
    /// in front of it. A tab page's bar: a sibling of the TabControl (a TabControl takes nothing but pages). A
    /// top-level window's — a form ScreenFit made scroll, a combo's open list — an owned, click-through popup.
    /// WS_CLIPSIBLINGS keeps the native bar's own repaints (Windows 11 animates it on hover; every scroll redraws it)
    /// underneath the cover.
    /// </summary>
    internal static class ScrollSkins
    {
        private static readonly ConditionalWeakTable<Control, ScrollSkin> _byControl = new ConditionalWeakTable<Control, ScrollSkin>();
        private static readonly ConditionalWeakTable<ComboBox, ScrollSkin> _byList = new ConditionalWeakTable<ComboBox, ScrollSkin>();
        private static readonly List<WeakReference<ScrollSkin>> _all = new List<WeakReference<ScrollSkin>>();

        /// <summary>The theme the bars are drawn in: the last one a window was themed with.</summary>
        internal static Theme Theme { get; private set; }

        /// <summary>
        /// From ThemeManager's pass, for every control it themes: covers the control's scroll bars when it can have
        /// any, and redraws every cover when the theme is a new one.
        /// </summary>
        internal static void Apply(Control c, Theme theme)
        {
            if (theme != null && !ReferenceEquals(theme, Theme))
            {
                Theme = theme;
                RepaintAll();
            }
            if (c == null || c.IsDisposed || !Wants(c)) { return; }
            if (!_byControl.TryGetValue(c, out ScrollSkin skin))
            {
                skin = new ScrollSkin(c);
                _byControl.Add(c, skin);
                Track(skin);
            }
            skin.Repaint();
        }

        /// <summary>
        /// Covers the scroll bars of <paramref name="root"/> and everything in it, in the current theme — for a
        /// window themed by hand, which never comes through ThemeManager (ScreenFit calls this when it is shown).
        /// </summary>
        internal static void ApplyTree(Control root)
        {
            if (root == null || root.IsDisposed) { return; }
            Apply(root, null);
            foreach (Control c in root.Controls) { ApplyTree(c); }
        }

        /// <summary>The skin covering <paramref name="c"/>'s bars, or null (for tests and Live debug).</summary>
        internal static ScrollSkin For(Control c) => c != null && _byControl.TryGetValue(c, out ScrollSkin s) ? s : null;

        /// <summary>
        /// Every skin of a control in <paramref name="form"/> reads its bars again and puts its covers where they are now —
        /// after the window was maximised or restored. Each skin follows its own control's messages, and a cover beside a
        /// page lies in front of the whole tab area: one whose control was not told of the change stayed where the old
        /// size had its bar. Two GetScrollBarInfo calls a skin. Returns how many covers moved or went.
        /// </summary>
        internal static int SyncAll(Form form)
        {
            if (form == null || form.IsDisposed) { return 0; }
            var skins = new List<ScrollSkin>();
            lock (_all)
            {
                for (int i = _all.Count - 1; i >= 0; i--)
                {
                    if (_all[i].TryGetTarget(out ScrollSkin s) && !s.IsDead) { skins.Add(s); }
                    else { _all.RemoveAt(i); }
                }
            }
            int changed = 0;
            foreach (ScrollSkin s in skins)
            {
                try { if (s.BelongsTo(form)) { changed += s.Resync(); } }
                catch (Exception ex) { Utils.Logger.Swallow("ScrollSkins.SyncAll", ex); }
            }
            return changed;
        }

        /// <summary>
        /// Every skin of a control inside <paramref name="root"/> (not its own) reads its bars again: <paramref name="root"/>
        /// has just come out of a freeze, through which everything in it read as hidden (see ScrollSkin's WM_SETREDRAW).
        /// </summary>
        internal static void SyncWithin(Control root)
        {
            if (root == null || root.IsDisposed || !root.HasChildren) { return; }
            List<ScrollSkin> inside = null;
            lock (_all)
            {
                for (int i = _all.Count - 1; i >= 0; i--)
                {
                    if (!_all[i].TryGetTarget(out ScrollSkin s) || s.IsDead) { _all.RemoveAt(i); continue; }
                    Control c = s.Target;
                    if (c != null && !ReferenceEquals(c, root) && !c.IsDisposed && root.Contains(c)) { (inside ??= new List<ScrollSkin>()).Add(s); }
                }
            }
            if (inside == null) { return; }
            foreach (ScrollSkin s in inside)
            {
                try { s.Sync(); }
                catch (Exception ex) { Utils.Logger.Swallow("ScrollSkins.SyncWithin", ex); }
            }
        }

        /// <summary>
        /// The cover audit (MainForm runs it twice a second while it is on screen): every skin of a control in
        /// <paramref name="form"/> compares its covers with its bars as Windows has them now and puts right what does not
        /// match (ScrollSkin.Audit), and a cover in the form that no skin has any more is taken away. Returns how many things
        /// were put right; <paramref name="found"/>, when given, gets a line for the log about each. Cheap: a hidden page
        /// costs a flag or two, a control on show two GetScrollBarInfo calls.
        /// </summary>
        internal static int Audit(Form form, List<string> found = null)
        {
            if (form == null || form.IsDisposed || !form.IsHandleCreated) { return 0; }
            var skins = new List<ScrollSkin>();
            lock (_all)
            {
                for (int i = _all.Count - 1; i >= 0; i--)
                {
                    if (_all[i].TryGetTarget(out ScrollSkin s) && !s.IsDead) { skins.Add(s); }
                    else { _all.RemoveAt(i); }
                }
            }
            int put = 0;
            foreach (ScrollSkin s in skins)
            {
                try
                {
                    if (!s.BelongsTo(form)) { continue; }
                    string what = s.Audit();
                    if (what == null) { continue; }
                    put++;
                    found?.Add(what);
                }
                catch (Exception ex) { Utils.Logger.Swallow("ScrollSkins.Audit", ex); }
            }
            // A page's covers live in the form itself. One its skin no longer has (made again somewhere else, or the skin
            // gone) would stay wherever it was last put, for good.
            try
            {
                List<ScrollCover> leftover = null;
                foreach (Control c in form.Controls)
                {
                    if (c is ScrollCover cover && !cover.IsDisposed && !cover.Owned) { (leftover ??= new List<ScrollCover>()).Add(cover); }
                }
                if (leftover != null)
                {
                    foreach (ScrollCover cover in leftover)
                    {
                        bool shown = cover.Visible;
                        Rectangle was = cover.Bounds;
                        cover.Dispose();
                        if (!shown) { continue; }
                        put++;
                        found?.Add((cover.VerticalBar ? "an upright" : "a sideways") + " bar cover no skin has any more, at "
                                   + was.X + "," + was.Y + " " + was.Width + "x" + was.Height + " — taken away");
                    }
                }
            }
            catch (Exception ex) { Utils.Logger.Swallow("ScrollSkins.Audit leftovers", ex); }
            return put;
        }

        /// <summary>The skin covering <paramref name="combo"/>'s open list, or null.</summary>
        internal static ScrollSkin ForList(ComboBox combo) => combo != null && _byList.TryGetValue(combo, out ScrollSkin s) ? s : null;

        /// <summary>
        /// A combo's list has just opened. The list is a top-level window of its own, made once and shown again at
        /// every open, so its skin is made at the first and kept for the combo's life.
        /// </summary>
        internal static void CoverDropDown(ComboBox combo)
        {
            if (combo == null || combo.IsDisposed || !combo.IsHandleCreated) { return; }
            try
            {
                IntPtr list = ScrollNative.ComboList(combo.Handle);
                if (list == IntPtr.Zero) { return; }
                if (!_byList.TryGetValue(combo, out ScrollSkin skin) || skin.IsDead || skin.Handle != list)
                {
                    skin?.Dispose();
                    skin = new ScrollSkin(list, combo);
                    _byList.AddOrUpdate(combo, skin);
                    Track(skin);
                }
                // Posted from OnDropDown, so this runs once the call that opened the list has returned — Windows' slide
                // has played by then (it plays inside that call). The bar is covered from now until the list closes.
                skin.ListOpened();
            }
            catch (Exception ex) { Utils.Logger.Swallow("ScrollSkins.CoverDropDown", ex); }
        }

        private static void Track(ScrollSkin skin)
        {
            lock (_all) { _all.Add(new WeakReference<ScrollSkin>(skin)); }
        }

        private static void RepaintAll()
        {
            lock (_all)
            {
                for (int i = _all.Count - 1; i >= 0; i--)
                {
                    if (_all[i].TryGetTarget(out ScrollSkin s) && !s.IsDead) { s.Repaint(); }
                    else { _all.RemoveAt(i); }
                }
            }
        }

        /// <summary>The controls whose window can carry a native scroll bar.</summary>
        private static bool Wants(Control c)
        {
            switch (c)
            {
                case ScrollCover _:
                case UpDownBase _:
                case ComboBox _:          // its open list is a window of its own: CoverDropDown
                case TabControl _:
                case MainForm _:          // never scrolls; its pages do
                case DetachedPageWindow _:
                    return false;
                case Form _:
                    return true;          // a dialog scrolls when ScreenFit shrinks it to a small screen
                case RichTextBox _:
                    return true;
                case TextBox t:
                    return t.Multiline && !(t.Parent is UpDownBase);
                case ListBox _:
                case ListView _:
                    return true;
                case ScrollableControl s:
                    return s.AutoScroll;  // the pages, and any panel that scrolls
                default:
                    return false;
            }
        }
    }

    /// <summary>The parts of a scroll bar, as a hit on it would find them.</summary>
    internal enum ScrollPart { None, ArrowBack, TrackBack, Thumb, TrackForward, ArrowForward, Track }

    /// <summary>One native scroll bar as Windows has it now (GetScrollBarInfo).</summary>
    internal struct ScrollBarState : IEquatable<ScrollBarState>
    {
        public bool Visible;
        public bool Enabled;
        /// <summary>The bar, in screen pixels.</summary>
        public Rectangle Screen;
        /// <summary>Each arrow button's length along the bar.</summary>
        public int Arrow;
        /// <summary>The thumb, from the bar's start (the arrow button included).</summary>
        public int ThumbStart, ThumbEnd;

        public int Length(bool vertical) => vertical ? Screen.Height : Screen.Width;

        public static ScrollBarState Read(IntPtr hwnd, bool vertical)
        {
            var s = new ScrollBarState();
            var sbi = new ScrollNative.SCROLLBARINFO { cbSize = Marshal.SizeOf(typeof(ScrollNative.SCROLLBARINFO)) };
            if (hwnd == IntPtr.Zero || !ScrollNative.GetScrollBarInfo(hwnd, vertical ? ScrollNative.OBJID_VSCROLL : ScrollNative.OBJID_HSCROLL, ref sbi))
            {
                return s;
            }
            var r = Rectangle.FromLTRB(sbi.left, sbi.top, sbi.right, sbi.bottom);
            s.Visible = (sbi.state0 & (ScrollNative.STATE_SYSTEM_INVISIBLE | ScrollNative.STATE_SYSTEM_OFFSCREEN)) == 0
                        && r.Width > 0 && r.Height > 0;
            if (!s.Visible) { return s; }
            s.Enabled = (sbi.state0 & ScrollNative.STATE_SYSTEM_UNAVAILABLE) == 0;
            s.Screen = r;
            s.Arrow = Math.Max(0, sbi.dxyLineButton);
            s.ThumbStart = sbi.xyThumbTop;
            s.ThumbEnd = sbi.xyThumbBottom;
            return s;
        }

        /// <summary>The part under <paramref name="screen"/>, as Windows would hit it.</summary>
        public ScrollPart PartAt(Point screen, bool vertical)
        {
            if (!Visible) { return ScrollPart.None; }
            int along = vertical ? screen.Y - Screen.Top : screen.X - Screen.Left;
            int across = vertical ? screen.X - Screen.Left : screen.Y - Screen.Top;
            int len = Length(vertical);
            int thick = vertical ? Screen.Width : Screen.Height;
            if (along < 0 || along >= len || across < 0 || across >= thick) { return ScrollPart.None; }
            int arrow = Math.Min(Arrow, len / 2);
            if (along < arrow) { return ScrollPart.ArrowBack; }
            if (along >= len - arrow) { return ScrollPart.ArrowForward; }
            if (!Enabled || ThumbEnd <= ThumbStart) { return ScrollPart.Track; }
            if (along < ThumbStart) { return ScrollPart.TrackBack; }
            if (along < ThumbEnd) { return ScrollPart.Thumb; }
            return ScrollPart.TrackForward;
        }

        public bool Equals(ScrollBarState o) =>
            Visible == o.Visible && Enabled == o.Enabled && Screen == o.Screen && Arrow == o.Arrow
            && ThumbStart == o.ThumbStart && ThumbEnd == o.ThumbEnd;

        public override bool Equals(object obj) => obj is ScrollBarState o && Equals(o);
        public override int GetHashCode() => Screen.GetHashCode() ^ (ThumbStart * 397) ^ ThumbEnd;
    }

    /// <summary>
    /// Covers one window's scroll bars (see <see cref="ScrollSkins"/>). Subclasses the window only to watch it: every
    /// message is passed on untouched, and afterwards the bars are read again (two GetScrollBarInfo calls) — the
    /// cheap, sure way to follow a range or position that changed through any of the many messages that can change
    /// one. A press on a bar is only noted on its way to Windows' own tracking loop, so the cover can show it.
    /// </summary>
    internal sealed class ScrollSkin : NativeWindow, IDisposable
    {
        private readonly Control _control;      // the scrolling control; null for a combo's list
        private readonly Control _colourFrom;   // whose surface the bar's ground is
        private ScrollCover _coverV, _coverH;   // child covers
        private ScrollPopup _popupV, _popupH;   // popup covers, for a top-level window
        private ScrollBarState _v, _h;
        private bool _syncing, _dead, _coversPending, _trackingLeave, _redrawHeld;
        /// <summary>When the window's painting was last frozen (WM_SETREDRAW(FALSE)), Environment.TickCount64; for the cover audit.</summary>
        private long _heldSince;
        private ScrollPart _hot = ScrollPart.None, _pressed = ScrollPart.None;
        private bool _hotVertical, _pressedVertical;
        private bool _thumbTracking;             // Windows is dragging the pressed thumb (SB_THUMBTRACK seen)
        private readonly bool _listBox;          // a list box or a combo's list: scrolled without the smooth-scroll stall

        internal ScrollSkin(Control control)
        {
            _control = control;
            _colourFrom = control;
            _listBox = control is ListBox;
            control.HandleCreated += OnHandleCreated;
            control.HandleDestroyed += OnHandleDestroyed;
            control.Disposed += OnDisposed;
            control.VisibleChanged += OnVisibleChanged;
            control.ParentChanged += OnParentChanged;
            WatchTabControl();
            if (control.IsHandleCreated) { Hook(control.Handle); }
        }

        /// <summary>The tab control whose open tab decides whether this page's covers show, when the control is a page.</summary>
        private TabControl _tabs;

        /// <summary>
        /// A tab page's covers show while it is the open tab and at no other time: the selection is followed, not only the
        /// page's own window — a page left natively visible after it was switched away from (a stray WM_SETREDRAW(TRUE)
        /// sets the visible bit) sits out of sight behind the open one, while its covers, beside the tab control, lie in
        /// front of everything.
        /// </summary>
        private void WatchTabControl()
        {
            TabControl now = _control is TabPage ? _control.Parent as TabControl : null;
            if (ReferenceEquals(now, _tabs)) { return; }
            if (_tabs != null) { _tabs.SelectedIndexChanged -= OnTabSelected; }
            _tabs = now;
            if (_tabs != null)
            {
                _tabs.SelectedIndexChanged += OnTabSelected;
                _tabOpen = ReferenceEquals(_tabs.SelectedTab, _control);
            }
        }

        /// <summary>
        /// Whether this page was the open tab when the tab control last said its selection changed. SelectedIndexChanged
        /// comes once the pages have been swapped; SelectedTab read live does not wait for that. WinForms moves the
        /// selection (TCM_SETCURSEL) BEFORE it hides the page being left, so all through a switch the old page is still on
        /// screen while SelectedTab already names the new one. Read live, the old page's covers came off at the start of
        /// every switch and Windows' own light bar showed through; the switch's picture took it, and it faded out with the
        /// old page (filmed by animcheck, Macros → Settings, 2026-11-11).
        /// </summary>
        private bool _tabOpen;

        private void OnTabSelected(object sender, EventArgs e)
        {
            _tabOpen = _tabs != null && ReferenceEquals(_tabs.SelectedTab, _control);
            PostSync();
        }

        /// <summary>
        /// The page this skin covers is a tab that is not the open one: it was not open at the last selection change, and it
        /// is not the selected tab now either (should the selection ever move without the event, the page counts as open the
        /// moment it is chosen).
        /// </summary>
        private bool IsTabNotOpen => _tabs != null && _control is TabPage page && !_tabs.IsDisposed
                                     && !_tabOpen && !ReferenceEquals(_tabs.SelectedTab, page);

        /// <summary>The skin's control is in <paramref name="form"/> (a combo's list: its combo is).</summary>
        internal bool BelongsTo(Form form)
        {
            Control c = _control ?? _colourFrom;
            return c != null && !c.IsDisposed && ReferenceEquals(c.FindForm(), form);
        }

        /// <summary>Reads the bars afresh, whatever was read last; how many covers moved or went (see ScrollSkins.SyncAll).</summary>
        internal int Resync()
        {
            Rectangle v = _coverV != null && !_coverV.IsDisposed && _coverV.Visible ? _coverV.Bounds : Rectangle.Empty;
            Rectangle h = _coverH != null && !_coverH.IsDisposed && _coverH.Visible ? _coverH.Bounds : Rectangle.Empty;
            _v = default;
            _h = default;
            Sync();
            Rectangle v2 = _coverV != null && !_coverV.IsDisposed && _coverV.Visible ? _coverV.Bounds : Rectangle.Empty;
            Rectangle h2 = _coverH != null && !_coverH.IsDisposed && _coverH.Visible ? _coverH.Bounds : Rectangle.Empty;
            return (v != v2 ? 1 : 0) + (h != h2 ? 1 : 0);
        }

        /// <summary>
        /// The safety net under every path that moves a bar (ScrollSkins.Audit, twice a second while the main window is on
        /// screen). Reads both bars afresh and compares them with what the covers show: a cover shown with no bar under it,
        /// a cover somewhere its bar is not, a bar that shows with no cover over it. What it finds is put right and returned
        /// for the log; null when everything is as it should be.
        ///
        /// The user's screenshot of 2026-11-11 (10:44) was the one of the day before again — a maximised window, with
        /// covers where the restored one has its bars — after every repair made for it, and with the check after each
        /// maximise finding nothing to move. Whatever path leaves a cover behind, it does not stay behind for longer than
        /// this takes to come round, and the log line says which control, which bar, where, and what state this skin was
        /// in, so that the path can be found.
        ///
        /// A window frozen this very moment (its painting off for a batch) is left as it is: holding its covers where they
        /// were is what the hold is for (WM_SETREDRAW in WndProc). Not a tab page that is not the open tab — nothing of
        /// that is on show, frozen or not.
        /// </summary>
        internal string Audit()
        {
            if (_dead || _syncing || _pressed != ScrollPart.None) { return null; }   // a press on a bar: Windows' own loop has it
            IntPtr hwnd = Handle;
            bool notOnShow = IsPageNotSelected;
            // Between messages no switch is under way, so the page is the open tab exactly when it is the selected one.
            bool openWas = _tabOpen;
            if (_tabs != null) { _tabOpen = !notOnShow; }
            bool frozen = _redrawHeld && hwnd != IntPtr.Zero && !ScrollNative.HasVisibleBit(hwnd);
            if (frozen && !notOnShow) { return null; }
            Expect(out ScrollBarState v, out ScrollBarState h, notOnShow);
            string wrongV = Wrong(true, v, h), wrongH = Wrong(false, h, v);
            if (wrongV == null && wrongH == null) { return null; }

            var said = new System.Text.StringBuilder(Who);
            if (wrongV != null) { said.Append("'s upright bar cover ").Append(wrongV); }
            if (wrongH != null) { said.Append(wrongV != null ? ", and its sideways one " : "'s sideways bar cover ").Append(wrongH); }
            said.Append(" (");
            said.Append(_redrawHeld ? "held for " + (Environment.TickCount64 - _heldSince) + " ms" + (frozen ? ", painting frozen" : ", though it paints")
                                    : "not held");
            if (_tabs != null)
            {
                said.Append(notOnShow ? "; not the open tab" : "; the open tab");
                if (openWas == notOnShow) { said.Append(", which the skin had the other way round"); }
            }
            if (hwnd == IntPtr.Zero) { said.Append("; its window not watched"); }
            said.Append(')');

            // Put right. A hold over a window that paints again is over; a page that is not the open tab shows nothing.
            if (!frozen) { _redrawHeld = false; }
            if (hwnd == IntPtr.Zero || notOnShow) { HideCovers(); }
            else
            {
                _v = default;
                _h = default;
                Sync();
            }
            // Still wrong — a cover Sync could not place where the bar is: none at all is better than a wrong one. (A cover
            // still to be made is not wrong: MakeCovers comes on the next message turn.)
            Expect(out v, out h, notOnShow);
            bool stillV = Shows(true) && Wrong(true, v, h) != null, stillH = Shows(false) && Wrong(false, h, v) != null;
            if (stillV || stillH)
            {
                HideCovers();
                said.Append(" — could not be put where the bar is, so hidden");
            }
            else { said.Append(" — put right"); }
            return said.ToString();
        }

        /// <summary>
        /// This skin's page is not the selected tab, read live: for the audit, which runs between messages and so never in
        /// the middle of a switch, where SelectedTab already names the new page while the old one is still on screen (see
        /// <see cref="_tabOpen"/>).
        /// </summary>
        private bool IsPageNotSelected => _tabs != null && _control is TabPage page && !_tabs.IsDisposed
                                          && !ReferenceEquals(_tabs.SelectedTab, page);

        /// <summary>The bars as the covers should show them now: none for a window that is not on show.</summary>
        private void Expect(out ScrollBarState v, out ScrollBarState h, bool notOnShow)
        {
            v = default;
            h = default;
            IntPtr hwnd = Handle;
            if (_dead || hwnd == IntPtr.Zero || notOnShow || !ScrollNative.IsWindowVisible(hwnd)
                || (TopLevelTarget && ScrollNative.IsIconic(hwnd)) || ListNotOpenYet)
            {
                return;
            }
            v = ScrollBarState.Read(hwnd, true);
            h = ScrollBarState.Read(hwnd, false);
        }

        /// <summary>Whether the cover over the vertical/horizontal bar is on show.</summary>
        private bool Shows(bool vertical)
        {
            if (TopLevelTarget)
            {
                ScrollPopup p = vertical ? _popupV : _popupH;
                return p != null && p.Visible;
            }
            ScrollCover c = vertical ? _coverV : _coverH;
            return c != null && !c.IsDisposed && c.Visible;
        }

        /// <summary>What one cover shows that its bar, as it is now (<paramref name="s"/>), does not — or null.</summary>
        private string Wrong(bool vertical, ScrollBarState s, ScrollBarState other)
        {
            bool shows = Shows(vertical);
            if (TopLevelTarget)
            {
                ScrollPopup p = vertical ? _popupV : _popupH;
                if (!s.Visible) { return shows ? "shown at " + Where(p.Bounds) + " with no bar under it" : null; }
                Rectangle want = CoverRect(s, other, vertical);
                if (!shows) { return "missing: its bar shows at " + Where(want) + " uncovered"; }
                return p.Bounds == want ? null : "at " + Where(p.Bounds) + " while its bar is at " + Where(want);
            }
            ScrollCover c = vertical ? _coverV : _coverH;
            if (!s.Visible) { return shows ? "shown at " + Where(c.Bounds) + " with no bar under it" : null; }
            Control host = HostOf(out _);
            if (host == null || host.IsDisposed || !host.IsHandleCreated) { return null; }   // nowhere to put one yet
            Rectangle at = host.RectangleToClient(CoverRect(s, other, vertical));
            if (!shows) { return "missing: its bar shows at " + Where(at) + " uncovered"; }
            if (c.Parent != host) { return "in another window than its bar"; }
            return c.Bounds == at ? null : "at " + Where(c.Bounds) + " while its bar is at " + Where(at);
        }

        private static string Where(Rectangle r) => r.X + "," + r.Y + " " + r.Width + "x" + r.Height;

        /// <summary>The control, as the log names it: "the Keybinds page", "_macroListBox on the Macros page".</summary>
        internal string Who
        {
            get
            {
                Control c = _control ?? _colourFrom;
                if (c == null) { return "a control"; }
                if (c is TabPage page) { return "the " + page.Text + " page"; }
                string name = (string.IsNullOrEmpty(c.Name) ? c.GetType().Name : c.Name) + (_control == null ? "'s open list" : "");
                for (Control p = c.Parent; p != null; p = p.Parent)
                {
                    if (p is TabPage tp) { return name + " on the " + tp.Text + " page"; }
                }
                return name;
            }
        }

        /// <summary>Whether <paramref name="cover"/> is one of this skin's own (a cover no skin has is a leftover).</summary>
        internal bool Owns(ScrollCover cover) => !_dead && (ReferenceEquals(_coverV, cover) || ReferenceEquals(_coverH, cover));

        /// <summary>For a combo's open list: a native top-level window, its colours from the combo.</summary>
        internal ScrollSkin(IntPtr list, ComboBox combo)
        {
            _colourFrom = combo;
            _listBox = true;
            combo.Disposed += OnDisposed;
            Hook(list);
        }

        /// <summary>
        /// A combo's list skin covers the bar only from <see cref="ListOpened"/> until the list closes. Windows slides the
        /// list open (the combo-box animation, on by default) for about 0.2 s, and the list's own messages during the slide
        /// made the skin show its bar at once and at full length: a 30-row bar stood over the page beside a list still
        /// showing a few rows — the user's screenshot of the Theme list, the bar reaching on down past the window. On every
        /// open but the first, when the skin did not exist yet. Filmed by scratchpad/combodrop from a second thread.
        /// </summary>
        private bool _listOpen;

        /// <summary>The list has finished opening (ScrollSkins.CoverDropDown, posted from the combo's OnDropDown).</summary>
        internal void ListOpened()
        {
            _listOpen = true;
            Sync();
        }

        /// <summary>A combo's list, opening or closed: its bar is not covered (see <see cref="_listOpen"/>).</summary>
        private bool ListNotOpenYet => _control == null && !_listOpen;

        internal bool IsDead => _dead;

        /// <summary>The control whose bars this skin covers; null for a combo's open list.</summary>
        internal Control Target => _control;

        /// <summary>The bars as last read (for tests).</summary>
        internal ScrollBarState Vertical => _v;
        internal ScrollBarState Horizontal => _h;

        /// <summary>The child cover over the vertical/horizontal bar, when there is one (for tests).</summary>
        internal ScrollCover CoverOf(bool vertical) => vertical ? _coverV : _coverH;

        /// <summary>The popup cover over a top-level window's bar, when there is one (for tests).</summary>
        internal ScrollPopup PopupOf(bool vertical) => vertical ? _popupV : _popupH;

        internal ScrollPart HotPart => _hot;
        internal ScrollPart PressedPart => _pressed;

        /// <summary>A popup cover for a top-level window; a child cover beside a child control.</summary>
        private bool TopLevelTarget => _control == null || _control is Form;

        private void Hook(IntPtr hwnd)
        {
            if (Handle != IntPtr.Zero) { ReleaseHandle(); }
            try { AssignHandle(hwnd); }
            catch (Exception ex) { Utils.Logger.Swallow("ScrollSkin.Hook", ex); return; }
            if (!TopLevelTarget) { ScrollNative.EnsureClipSiblings(hwnd); }
            PostSync();
        }

        private void OnHandleCreated(object sender, EventArgs e)
        {
            if (!_dead && _control != null && _control.IsHandleCreated) { Hook(_control.Handle); }
        }

        private void OnHandleDestroyed(object sender, EventArgs e)
        {
            HideCovers();
            if (Handle != IntPtr.Zero) { ReleaseHandle(); }
        }

        private void OnVisibleChanged(object sender, EventArgs e) => Sync();

        private void OnParentChanged(object sender, EventArgs e)
        {
            // Covers live in the parent: a new parent wants new ones (made at the next look).
            DisposeChildCovers();
            WatchTabControl();
            PostSync();
        }

        private void OnDisposed(object sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (_dead) { return; }
            _dead = true;
            if (_tabs != null)
            {
                try { _tabs.SelectedIndexChanged -= OnTabSelected; } catch { }
                _tabs = null;
            }
            if (_control != null)
            {
                _control.HandleCreated -= OnHandleCreated;
                _control.HandleDestroyed -= OnHandleDestroyed;
                _control.Disposed -= OnDisposed;
                _control.VisibleChanged -= OnVisibleChanged;
                _control.ParentChanged -= OnParentChanged;
            }
            else if (_colourFrom != null)
            {
                _colourFrom.Disposed -= OnDisposed;
            }
            try { if (Handle != IntPtr.Zero) { ReleaseHandle(); } } catch { }
            DisposeChildCovers();
            try { _popupV?.Dispose(); _popupH?.Dispose(); } catch { }
            _popupV = _popupH = null;
        }

        private void DisposeChildCovers()
        {
            try
            {
                if (_coverV != null && !_coverV.IsDisposed) { _coverV.Dispose(); }
                if (_coverH != null && !_coverH.IsDisposed) { _coverH.Dispose(); }
            }
            catch { }
            _coverV = _coverH = null;
        }

        private void HideCovers()
        {
            try
            {
                if (_coverV != null && !_coverV.IsDisposed && _coverV.Visible) { _coverV.Visible = false; }
                if (_coverH != null && !_coverH.IsDisposed && _coverH.Visible) { _coverH.Visible = false; }
                _popupV?.HideCover();
                _popupH?.HideCover();
            }
            catch { }
            _v = default;
            _h = default;
        }

        /// <summary>Look again on the next message turn — not inside a theme pass or a layout that is walking the parent.</summary>
        private void PostSync()
        {
            Control c = _colourFrom;
            if (_dead || c == null || c.IsDisposed || !c.IsHandleCreated) { return; }
            try { c.BeginInvoke(new Action(Sync)); } catch { }
        }

        /// <summary>Redraws the covers (a theme change).</summary>
        internal void Repaint()
        {
            Redraw(true);
            Redraw(false);
            PostSync();
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case ScrollNative.WM_NCLBUTTONDOWN:
                case ScrollNative.WM_NCLBUTTONDBLCLK:
                {
                    int ht = (int)(long)m.WParam;
                    if (ht == ScrollNative.HTVSCROLL || ht == ScrollNative.HTHSCROLL)
                    {
                        Press(ht == ScrollNative.HTVSCROLL, ScreenPoint(m.LParam));
                        // Windows' own drag / repeat loop runs in here until the button is let go; the scrolls it
                        // makes arrive meanwhile and redraw the cover as they come.
                        try { base.WndProc(ref m); }
                        finally { Release(); }
                        return;
                    }
                    break;
                }
                case ScrollNative.WM_NCMOUSEMOVE:
                    base.WndProc(ref m);
                    Hover((int)(long)m.WParam, ScreenPoint(m.LParam));
                    return;
                case ScrollNative.WM_NCMOUSELEAVE:
                    base.WndProc(ref m);
                    _trackingLeave = false;
                    Unhover();
                    return;
                case ScrollNative.WM_MOUSEMOVE:
                    base.WndProc(ref m);
                    if (_hot != ScrollPart.None) { Unhover(); }
                    return;
                // Asked a lot, and never a change to a bar.
                case ScrollNative.WM_NCHITTEST:
                case ScrollNative.WM_SETCURSOR:
                case ScrollNative.WM_GETTEXT:
                case ScrollNative.WM_GETTEXTLENGTH:
                case ScrollNative.WM_GETDLGCODE:
                case ScrollNative.WM_GETOBJECT:
                case ScrollNative.WM_GETFONT:
                case ScrollNative.WM_ERASEBKGND:
                case ScrollNative.WM_ENTERIDLE:
                    base.WndProc(ref m);
                    return;
                case ScrollNative.WM_NCDESTROY:
                    base.WndProc(ref m);   // NativeWindow lets go of the handle here
                    HideCovers();
                    return;
                case ScrollNative.WM_VSCROLL:
                case ScrollNative.WM_HSCROLL:
                case ScrollNative.WM_MOUSEWHEEL:
                case ScrollNative.WM_MOUSEHWHEEL:
                    // Windows' drag of the pressed thumb has begun: from now until the button is let go the thumb is
                    // read from the drag (ScrollNative.TrackedThumb). Not before — nTrackPos still holds the last drag.
                    if (_pressed == ScrollPart.Thumb && m.Msg != ScrollNative.WM_MOUSEWHEEL && m.Msg != ScrollNative.WM_MOUSEHWHEEL
                        && ((int)(long)m.WParam & 0xFFFF) == ScrollNative.SB_THUMBTRACK)
                    {
                        _thumbTracking = true;
                    }
                    if (_listBox && WouldScroll(m))
                    {
                        ScrollWithoutAnimation(ref m);
                        return;
                    }
                    break;
                // Painting frozen around a batch (BeginUpdate, Live debug's rebuilds twice a second): Windows does
                // that by CLEARING the window's visible bit, so the bar read as hidden and its cover dropped out —
                // and the parent painted its own background into the gap — until painting came back. Hold the
                // covers as they are instead; nothing under them paints meanwhile.
                case ScrollNative.WM_SETREDRAW:
                    base.WndProc(ref m);
                    if (m.WParam == IntPtr.Zero && !_redrawHeld) { _heldSince = Environment.TickCount64; }
                    _redrawHeld = m.WParam == IntPtr.Zero;
                    if (!_redrawHeld)
                    {
                        Sync();
                        // Everything inside read as hidden all through the freeze (an ancestor's visible bit was gone),
                        // so a control in it that had a message meanwhile — moved by a re-centre, a list refilled — took
                        // its covers down, and only its next message brings them back. One below the fold never gets the
                        // repaint the thaw asks for: the Statistics page's history list kept Windows' own bar after a
                        // restore until it was scrolled to (found by the cover audit, layoutglitch stale, 2026-11-11).
                        ScrollSkins.SyncWithin(_control);
                    }
                    return;
                case ScrollNative.WM_SHOWWINDOW:
                    if (m.WParam != IntPtr.Zero) { _redrawHeld = false; }   // shown outright: nothing is held any more
                    break;
            }
            base.WndProc(ref m);
            Sync();
        }

        /// <summary>A scroll message a list box would act on now: its bar shows, it paints, and it is not the end-of-scroll note.</summary>
        private bool WouldScroll(Message m)
        {
            if (_redrawHeld || Handle == IntPtr.Zero || !ScrollNative.IsWindowVisible(Handle)) { return false; }
            switch (m.Msg)
            {
                case ScrollNative.WM_VSCROLL:
                    return _v.Visible && ((int)(long)m.WParam & 0xFFFF) != ScrollNative.SB_ENDSCROLL;
                case ScrollNative.WM_HSCROLL:
                    return _h.Visible && ((int)(long)m.WParam & 0xFFFF) != ScrollNative.SB_ENDSCROLL;
                case ScrollNative.WM_MOUSEWHEEL:
                    return _v.Visible;
                default:
                    return _h.Visible;
            }
        }

        /// <summary>
        /// Windows' "smooth-scroll list boxes" (on by default) makes a list box's first scroll after it is shown or
        /// moved take ~0.4 s, with the window answering nothing meanwhile: 465 ms for the first line of a fresh list box
        /// against 1 ms for the next, measured (scratchpad/arrowhold), and ~0.3 s at the first scroll of every drop-down
        /// list each time it opens. Holding a bar's arrow ran into it as a freeze — with Windows' own bars as well, but
        /// Tempo's arrows are always on show. With painting suspended the same scroll takes under a millisecond and
        /// animates nothing (the later ones never did); the list is then painted once, where it now is.
        /// </summary>
        private void ScrollWithoutAnimation(ref Message m)
        {
            bool vertical = m.Msg == ScrollNative.WM_VSCROLL || m.Msg == ScrollNative.WM_MOUSEWHEEL;
            int before = ScrollNative.Position(Handle, vertical);
            // WM_SETREDRAW(FALSE) clears the visible bit: a Sync in between would read the bar as hidden and drop the covers.
            _redrawHeld = true;
            try
            {
                Message off = Message.Create(Handle, ScrollNative.WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
                base.WndProc(ref off);
                try { base.WndProc(ref m); }
                finally
                {
                    Message on = Message.Create(Handle, ScrollNative.WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                    base.WndProc(ref on);
                }
            }
            finally { _redrawHeld = false; }
            // At the end of the list an arrow or a wheel turn moves nothing: nothing to paint again.
            if (ScrollNative.Position(Handle, vertical) != before) { ScrollNative.RepaintNow(Handle); }
            Sync();
        }

        /// <summary>Reads both bars and puts the covers where they are, redrawing the ones that changed.</summary>
        internal void Sync()
        {
            // A hold whose window paints again is over, however it came back: a frozen window has its visible bit cleared
            // (WM_SETREDRAW(FALSE) does that), so one that has it set is not frozen — and a hold that outlived its freeze
            // left the covers where they were for good.
            if (_redrawHeld && Handle != IntPtr.Zero && ScrollNative.HasVisibleBit(Handle)) { _redrawHeld = false; }
            // Frozen, and no longer the open tab (switched away from mid-freeze): nothing of it is on show, held or not.
            if (_redrawHeld && !_dead && IsTabNotOpen) { HideCovers(); return; }
            if (_syncing || _dead || _redrawHeld || Handle == IntPtr.Zero) { return; }
            // The whole window is hidden — sent to the tray: its covers, child windows of it, went with it and come back
            // with it. Hiding them as well (its bars read as hidden then) made every return from the tray show each one
            // again: a SetWindowPos apiece, synchronous, with the repaints it set off beneath them — profiled 2026-11-12,
            // most of what Show() cost. The first Sync after the window is back puts right anything that changed meanwhile.
            if (!TopLevelTarget && !ScrollNative.IsWindowVisible(ScrollNative.Root(Handle))) { return; }
            _syncing = true;
            try
            {
                IntPtr hwnd = Handle;
                ScrollBarState v = ScrollBarState.Read(hwnd, true);
                ScrollBarState h = ScrollBarState.Read(hwnd, false);
                // A thumb being dragged is drawn where the drag has it, not where it was when the drag began.
                if (_thumbTracking)
                {
                    if (_pressedVertical && v.Visible) { ScrollNative.TrackedThumb(hwnd, true, ref v); }
                    else if (!_pressedVertical && h.Visible) { ScrollNative.TrackedThumb(hwnd, false, ref h); }
                }
                // A hidden page, a closed list, a minimised dialog: nothing to cover. Nor a tab page that is not the open
                // tab, even one whose window says it shows (see WatchTabControl).
                bool shown = ScrollNative.IsWindowVisible(hwnd);
                // A combo's list that has closed is opening next time: not covered until it has finished (_listOpen).
                if (!shown && _control == null) { _listOpen = false; }
                if (!shown || (TopLevelTarget && ScrollNative.IsIconic(hwnd)) || IsTabNotOpen || ListNotOpenYet)
                {
                    v.Visible = false;
                    h.Visible = false;
                }
                bool changedV = !v.Equals(_v), changedH = !h.Equals(_h);
                _v = v;
                _h = h;
                if (TopLevelTarget)
                {
                    PlacePopup(ref _popupV, v, h, true, changedV);
                    PlacePopup(ref _popupH, h, v, false, changedH);
                }
                else
                {
                    PlaceCover(_coverV, v, h, true, changedV);
                    PlaceCover(_coverH, h, v, false, changedH);
                }
            }
            catch (Exception ex) { Utils.Logger.Swallow("ScrollSkin.Sync", ex); }
            finally { _syncing = false; }
        }

        /// <summary>
        /// Where a bar's cover goes, in screen pixels: the bar — and for the vertical one, when both show, the square
        /// between their ends as well, which Windows paints in its own grey.
        /// </summary>
        private static Rectangle CoverRect(ScrollBarState s, ScrollBarState other, bool vertical)
        {
            Rectangle r = s.Screen;
            if (vertical && other.Visible && other.Screen.Bottom > r.Bottom)
            {
                r = Rectangle.FromLTRB(r.Left, r.Top, r.Right, other.Screen.Bottom);
            }
            return r;
        }

        /// <summary>The window a child cover goes into, and the one it must sit in front of.</summary>
        private Control HostOf(out Control inFrontOf)
        {
            inFrontOf = _control;
            Control parent = _control?.Parent;
            // A tab page: its TabControl takes nothing but pages, so the cover goes beside the TabControl.
            if (parent is TabControl tabs)
            {
                inFrontOf = tabs;
                parent = tabs.Parent;
            }
            return parent;
        }

        private void PlaceCover(ScrollCover cover, ScrollBarState s, ScrollBarState other, bool vertical, bool changed)
        {
            if (!s.Visible)
            {
                if (cover != null && !cover.IsDisposed && cover.Visible) { cover.Visible = false; }
                return;
            }
            Control host = HostOf(out Control inFrontOf);
            if (host == null || host.IsDisposed || !host.IsHandleCreated) { return; }
            if (cover == null || cover.IsDisposed || cover.Parent != host)
            {
                // Made on the next message turn: this may be running inside the parent's own layout, or inside a
                // theme pass walking the parent's controls — adding one there throws. The native bar shows until then.
                if (!_coversPending)
                {
                    _coversPending = true;
                    try { host.BeginInvoke(new Action(MakeCovers)); } catch { _coversPending = false; }
                }
                return;
            }
            Rectangle screen = CoverRect(s, other, vertical);
            Rectangle r = host.RectangleToClient(screen);
            if (cover.Bounds != r)
            {
                cover.Bounds = r;
                Shape(cover, screen);
            }
            InFront(cover, inFrontOf);
            if (!cover.Visible) { cover.Visible = true; }
            if (changed)
            {
                cover.Invalidate();
                cover.Update();
            }
        }

        private void MakeCovers()
        {
            _coversPending = false;
            if (_dead || _control == null || _control.IsDisposed) { return; }
            Control host = HostOf(out Control inFrontOf);
            if (host == null || host.IsDisposed) { return; }
            // The window the cover is in front of must clip it out of its own painting, or the native bar it covers
            // would draw over the cover each time Windows repaints it.
            if (inFrontOf != null && inFrontOf.IsHandleCreated) { ScrollNative.EnsureClipSiblings(inFrontOf.Handle); }
            if (_coverV == null || _coverV.IsDisposed || _coverV.Parent != host) { _coverV = MakeCover(host, inFrontOf, true); }
            if (_coverH == null || _coverH.IsDisposed || _coverH.Parent != host) { _coverH = MakeCover(host, inFrontOf, false); }
            _v = default;
            _h = default;
            Sync();
        }

        private ScrollCover MakeCover(Control host, Control inFrontOf, bool vertical)
        {
            var cover = new ScrollCover(this, vertical) { Visible = false };
            LayoutFitter.DeclareOverlay(cover, "Tempo's scroll bar, drawn over the native one of the control it covers");
            host.Controls.Add(cover);
            InFront(cover, inFrontOf);
            return cover;
        }

        /// <summary>Directly in front of <paramref name="window"/> (a lower index is nearer the front).</summary>
        private static void InFront(Control cover, Control window)
        {
            Control.ControlCollection kids = cover.Parent?.Controls;
            if (kids == null || window == null || window.Parent != cover.Parent) { return; }
            int ci = kids.GetChildIndex(cover, false), wi = kids.GetChildIndex(window, false);
            if (ci < 0 || wi < 0 || ci < wi) { return; }
            kids.SetChildIndex(cover, wi);
        }

        /// <summary>A control cut to a shape of its own (a rounded box) cuts its cover the same way.</summary>
        private void Shape(ScrollCover cover, Rectangle coverScreen)
        {
            try
            {
                Region shape = _control?.Region;
                if (shape == null || !_control.IsHandleCreated)
                {
                    if (cover.Region != null) { cover.Region = null; }
                    return;
                }
                Point origin = _control.PointToScreen(Point.Empty);
                Rectangle window = ScrollNative.WindowRect(_control.Handle);
                if (!window.IsEmpty) { origin = window.Location; }
                Region r = shape.Clone();
                r.Translate(origin.X - coverScreen.Left, origin.Y - coverScreen.Top);
                cover.Region = r;
            }
            catch { }
        }

        private void PlacePopup(ref ScrollPopup popup, ScrollBarState s, ScrollBarState other, bool vertical, bool changed)
        {
            if (!s.Visible)
            {
                popup?.HideCover();
                return;
            }
            if (popup == null || popup.IsDisposed) { popup = new ScrollPopup(this, vertical, Handle); }
            popup.ShowAt(CoverRect(s, other, vertical));
            if (changed) { popup.Redraw(); }
        }

        private void Redraw(bool vertical)
        {
            try
            {
                ScrollCover c = vertical ? _coverV : _coverH;
                if (c != null && !c.IsDisposed && c.Visible)
                {
                    c.Invalidate();
                    c.Update();
                }
                (vertical ? _popupV : _popupH)?.Redraw();
            }
            catch { }
        }

        /// <summary>The screen point a non-client mouse message carries (signed: a second screen can be left of the first).</summary>
        private static Point ScreenPoint(IntPtr lParam)
        {
            long v = (long)lParam;
            return new Point((short)(v & 0xFFFF), (short)((v >> 16) & 0xFFFF));
        }

        private void Hover(int hit, Point screen)
        {
            if (hit != ScrollNative.HTVSCROLL && hit != ScrollNative.HTHSCROLL)
            {
                Unhover();
                return;
            }
            Sync();
            bool vertical = hit == ScrollNative.HTVSCROLL;
            if (!_trackingLeave)
            {
                ScrollNative.TrackNonClientLeave(Handle);
                _trackingLeave = true;
            }
            ScrollPart part = (vertical ? _v : _h).PartAt(screen, vertical);
            if (part == _hot && vertical == _hotVertical) { return; }
            bool wasVertical = _hotVertical;
            ScrollPart was = _hot;
            _hot = part;
            _hotVertical = vertical;
            if (was != ScrollPart.None && wasVertical != vertical) { Redraw(wasVertical); }
            Redraw(vertical);
        }

        private void Unhover()
        {
            if (_hot == ScrollPart.None) { return; }
            _hot = ScrollPart.None;
            Redraw(_hotVertical);
        }

        private void Press(bool vertical, Point screen)
        {
            Sync();
            _pressed = (vertical ? _v : _h).PartAt(screen, vertical);
            _pressedVertical = vertical;
            Redraw(vertical);
        }

        private void Release()
        {
            bool vertical = _pressedVertical;
            _pressed = ScrollPart.None;
            _thumbTracking = false;
            Sync();
            Redraw(vertical);
        }

        /// <summary>Paints a cover: the bar as Windows has it, in Tempo's look.</summary>
        internal void Paint(Graphics g, Rectangle client, bool vertical)
        {
            ScrollBarState s = vertical ? _v : _h;
            ScrollPart hot = _hot != ScrollPart.None && _hotVertical == vertical ? _hot : ScrollPart.None;
            ScrollPart pressed = _pressed != ScrollPart.None && _pressedVertical == vertical ? _pressed : ScrollPart.None;
            ScrollPainter.Paint(g, client, s, vertical, Ground(), ScrollSkins.Theme, hot, pressed);
        }

        /// <summary>The surface the bar sits on: the control's own, so it reads as part of it.</summary>
        private Color Ground()
        {
            Theme t = ScrollSkins.Theme;
            Control c = _colourFrom;
            if (c is ComboBox) { return t != null ? t.InputBackground : c.BackColor; }
            Color b = c?.BackColor ?? Color.Empty;
            for (Control p = c; p != null && b.A < 255; p = p.Parent) { b = p.BackColor; }
            if (b.A < 255) { b = t != null ? t.Background : SystemColors.Control; }
            return b;
        }
    }

    /// <summary>Draws a bar: a slim capsule thumb, chevron arrows, and a faint groove while the pointer is on it.</summary>
    internal static class ScrollPainter
    {
        internal static void Paint(Graphics g, Rectangle client, ScrollBarState s, bool vertical, Color ground, Theme theme,
                                   ScrollPart hot, ScrollPart pressed)
        {
            using (var b = new SolidBrush(ground)) { g.FillRectangle(b, client); }
            if (!s.Visible) { return; }

            int thick = vertical ? client.Width : client.Height;
            int len = Math.Min(s.Length(vertical), vertical ? client.Height : client.Width);
            if (thick < 4 || len < 8) { return; }

            Color ink = theme != null ? theme.Text : SystemColors.ControlText;
            Color muted = theme != null ? theme.TextMuted : SystemColors.GrayText;
            Color accent = theme != null ? theme.Accent : SystemColors.Highlight;
            bool barHot = hot != ScrollPart.None || pressed != ScrollPart.None;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            int arrow = Math.Min(s.Arrow, len / 2);
            int wide = Math.Max(5, (int)Math.Round(thick * 0.5));
            int slim = Math.Max(3, (int)Math.Round(thick * 0.3));

            // The groove: only while the pointer is on the bar, so a resting bar is just its thumb.
            if (barHot && len - 2 * arrow > wide)
            {
                Rectangle groove = Along(vertical, client, arrow + 1, len - arrow - 1, wide);
                using (var path = ModernPaint.Rounded(groove, wide / 2))
                using (var b = new SolidBrush(Theme.Blend(ground, ink, 0.07)))
                {
                    g.FillPath(b, path);
                }
            }

            // The arrows: always there (a bar's up and down), brighter on hover, the accent while held.
            if (arrow >= 6)
            {
                Chevron(g, vertical, client, 0, arrow, back: true,
                        ArrowInk(ScrollPart.ArrowBack, hot, pressed, barHot, s.Enabled, ground, ink, muted, accent));
                Chevron(g, vertical, client, len - arrow, len, back: false,
                        ArrowInk(ScrollPart.ArrowForward, hot, pressed, barHot, s.Enabled, ground, ink, muted, accent));
            }

            // The thumb: a capsule inside Windows' own thumb, so a press anywhere on it still lands on it.
            if (s.Enabled && s.ThumbEnd > s.ThumbStart)
            {
                int w = barHot ? wide : slim;
                int inset = Math.Max(1, thick / 8);
                int start = Math.Max(arrow, s.ThumbStart) + inset;
                int end = Math.Min(len - arrow, s.ThumbEnd) - inset;
                // Never a dot: on a long list Windows' own thumb is ~8 px, and the capsule in it was a speck. A short
                // bar at least, centred on Windows' thumb — a press on its middle still lands on that thumb.
                int minLen = Math.Max(w * 2, (int)Math.Round(thick * 1.4));
                if (end - start < minLen)
                {
                    int mid = (start + end) / 2;
                    int lo = arrow + 1, hi = len - arrow - 1;
                    start = Math.Max(lo, mid - minLen / 2);
                    end = Math.Min(hi, start + minLen);
                    start = Math.Max(lo, end - minLen);
                }
                Color fill = pressed == ScrollPart.Thumb ? Theme.Readable(accent, ground, 3.0)
                           : hot == ScrollPart.Thumb ? Theme.Readable(Theme.Blend(ground, ink, 0.72), ground, 3.0)
                           : barHot ? Theme.Readable(Theme.Blend(ground, ink, 0.55), ground, 3.0)
                           : Theme.Readable(Theme.Blend(ground, muted, 0.8), ground, 3.0);
                Rectangle thumb = Along(vertical, client, start, end, w);
                using (var path = ModernPaint.Rounded(thumb, w / 2))
                using (var b = new SolidBrush(fill))
                {
                    g.FillPath(b, path);
                }
            }
        }

        /// <summary>A strip <paramref name="width"/> across, from <paramref name="from"/> to <paramref name="to"/> along the bar, centred.</summary>
        private static Rectangle Along(bool vertical, Rectangle client, int from, int to, int width)
        {
            int thick = vertical ? client.Width : client.Height;
            int x = (thick - width) / 2;
            return vertical
                ? new Rectangle(client.Left + x, client.Top + from, width, Math.Max(1, to - from))
                : new Rectangle(client.Left + from, client.Top + x, Math.Max(1, to - from), width);
        }

        private static Color ArrowInk(ScrollPart part, ScrollPart hot, ScrollPart pressed, bool barHot, bool enabled,
                                      Color ground, Color ink, Color muted, Color accent)
        {
            if (!enabled) { return Theme.Blend(ground, muted, 0.4); }
            if (pressed == part) { return Theme.Readable(accent, ground, 3.0); }
            if (hot == part) { return Theme.Readable(ink, ground, 4.5); }
            return barHot ? Theme.Readable(Theme.Blend(ground, ink, 0.75), ground, 3.0)
                          : Theme.Readable(Theme.Blend(ground, muted, 0.85), ground, 3.0);
        }

        /// <summary>A small chevron in the arrow button from <paramref name="from"/> to <paramref name="to"/> along the bar.</summary>
        private static void Chevron(Graphics g, bool vertical, Rectangle client, int from, int to, bool back, Color colour)
        {
            int thick = vertical ? client.Width : client.Height;
            float size = Math.Max(4f, Math.Min(thick, to - from) * 0.32f);   // half the chevron's span
            float cx = vertical ? client.Left + thick / 2f : client.Left + (from + to) / 2f;
            float cy = vertical ? client.Top + (from + to) / 2f : client.Top + thick / 2f;
            float h = size * 0.55f;
            PointF[] pts;
            if (vertical)
            {
                pts = back
                    ? new[] { new PointF(cx - size, cy + h / 2), new PointF(cx, cy - h / 2), new PointF(cx + size, cy + h / 2) }
                    : new[] { new PointF(cx - size, cy - h / 2), new PointF(cx, cy + h / 2), new PointF(cx + size, cy - h / 2) };
            }
            else
            {
                pts = back
                    ? new[] { new PointF(cx + h / 2, cy - size), new PointF(cx - h / 2, cy), new PointF(cx + h / 2, cy + size) }
                    : new[] { new PointF(cx - h / 2, cy - size), new PointF(cx + h / 2, cy), new PointF(cx - h / 2, cy + size) };
            }
            using (var pen = new Pen(colour, Math.Max(1.3f, thick / 11f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                g.DrawLines(pen, pts);
            }
        }
    }

    /// <summary>
    /// The paint-only window over a control's native bar: it draws the bar (<see cref="ScrollSkin.Paint"/>) and is
    /// invisible to the mouse — every hit test answers HTTRANSPARENT, so input goes to the native bar under it.
    /// </summary>
    internal sealed class ScrollCover : Control
    {
        private readonly ScrollSkin _skin;
        internal readonly bool VerticalBar;

        /// <summary>Whether a live skin still has this cover: one that none has is a leftover that nothing moves any more.</summary>
        internal bool Owned => _skin != null && _skin.Owns(this);

        internal ScrollCover(ScrollSkin skin, bool vertical)
        {
            _skin = skin;
            VerticalBar = vertical;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false);
            TabStop = false;
            AccessibleRole = AccessibleRole.None;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= ScrollNative.WS_CLIPSIBLINGS;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == ScrollNative.WM_NCHITTEST)
            {
                m.Result = (IntPtr)ScrollNative.HTTRANSPARENT;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            try { _skin.Paint(e.Graphics, ClientRectangle, VerticalBar); }
            catch (Exception ex) { Utils.Logger.Swallow("ScrollCover.OnPaint", ex); }
        }
    }

    /// <summary>
    /// The cover for a top-level window's bar: an owned popup — always in front of its owner, and placed directly
    /// in front of it, so it never floats over another program — click-through (layered + transparent), never
    /// activated, never in the taskbar. A bare native window rather than a Form: a Form would join
    /// Application.OpenForms, which other code walks (and themes).
    /// </summary>
    internal sealed class ScrollPopup : NativeWindow, IDisposable
    {
        private readonly ScrollSkin _skin;
        private readonly bool _vertical;
        private readonly IntPtr _owner;
        private readonly bool _topmost;
        private Rectangle _bounds;
        private bool _shown, _disposed;

        internal ScrollPopup(ScrollSkin skin, bool vertical, IntPtr owner)
        {
            _skin = skin;
            _vertical = vertical;
            _owner = owner;
            _topmost = ScrollNative.IsTopMost(owner);   // in the owner's band: a combo's list is topmost, a dialog is not
            var cp = new CreateParams
            {
                Caption = string.Empty,
                Style = unchecked((int)ScrollNative.WS_POPUP),
                ExStyle = ScrollNative.WS_EX_TOOLWINDOW | ScrollNative.WS_EX_NOACTIVATE | ScrollNative.WS_EX_TRANSPARENT | ScrollNative.WS_EX_LAYERED
                          | (_topmost ? ScrollNative.WS_EX_TOPMOST : 0),
                Parent = owner                    // a popup's "parent" is its owner
            };
            CreateHandle(cp);
            ScrollNative.SetLayeredWindowAttributes(Handle, 0, 255, ScrollNative.LWA_ALPHA);   // opaque; painted as usual
        }

        internal bool IsDisposed => _disposed || Handle == IntPtr.Zero;
        internal bool Visible => _shown && !IsDisposed;
        internal Rectangle Bounds => _bounds;

        internal void ShowAt(Rectangle screen)
        {
            if (IsDisposed) { return; }
            bool moved = screen != _bounds;
            if (!moved && _shown) { return; }
            _bounds = screen;
            ScrollNative.PlaceOwned(Handle, _owner, screen, _topmost, show: !_shown);
            _shown = true;
            if (moved) { ScrollNative.Invalidate(Handle); }
        }

        internal void HideCover()
        {
            if (!_shown || IsDisposed) { return; }
            ScrollNative.Hide(Handle);
            _shown = false;
        }

        internal void Redraw()
        {
            if (!Visible) { return; }
            ScrollNative.Invalidate(Handle);
            ScrollNative.UpdateWindow(Handle);
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case ScrollNative.WM_NCHITTEST:
                    m.Result = (IntPtr)ScrollNative.HTTRANSPARENT;
                    return;
                case ScrollNative.WM_MOUSEACTIVATE:
                    m.Result = (IntPtr)ScrollNative.MA_NOACTIVATE;
                    return;
                case ScrollNative.WM_ERASEBKGND:
                    m.Result = (IntPtr)1;
                    return;
                case ScrollNative.WM_PAINT:
                    Paint();
                    m.Result = IntPtr.Zero;
                    return;
            }
            base.WndProc(ref m);
        }

        private void Paint()
        {
            IntPtr hdc = ScrollNative.BeginPaint(Handle, out ScrollNative.PAINTSTRUCT ps);
            try
            {
                var client = new Rectangle(0, 0, _bounds.Width, _bounds.Height);
                if (hdc == IntPtr.Zero || client.Width <= 0 || client.Height <= 0) { return; }
                using (var bmp = new Bitmap(client.Width, client.Height))
                {
                    using (Graphics g = Graphics.FromImage(bmp)) { _skin.Paint(g, client, _vertical); }
                    using (Graphics screen = Graphics.FromHdc(hdc)) { screen.DrawImageUnscaled(bmp, 0, 0); }
                }
            }
            catch (Exception ex) { Utils.Logger.Swallow("ScrollPopup.Paint", ex); }
            finally { ScrollNative.EndPaint(Handle, ref ps); }
        }

        public void Dispose()
        {
            if (_disposed) { return; }
            _disposed = true;
            try { if (Handle != IntPtr.Zero) { DestroyHandle(); } } catch { }
        }
    }

    internal static class ScrollNative
    {
        internal const int WM_SETREDRAW = 0x000B;
        internal const int WM_SHOWWINDOW = 0x0018;
        internal const int WM_ERASEBKGND = 0x0014;
        internal const int WM_SETCURSOR = 0x0020;
        internal const int WM_MOUSEACTIVATE = 0x0021;
        internal const int WM_GETTEXT = 0x000D;
        internal const int WM_GETTEXTLENGTH = 0x000E;
        internal const int WM_GETFONT = 0x0031;
        internal const int WM_GETOBJECT = 0x003D;
        internal const int WM_NCDESTROY = 0x0082;
        internal const int WM_NCHITTEST = 0x0084;
        internal const int WM_GETDLGCODE = 0x0087;
        internal const int WM_NCMOUSEMOVE = 0x00A0;
        internal const int WM_NCLBUTTONDOWN = 0x00A1;
        internal const int WM_NCLBUTTONDBLCLK = 0x00A3;
        internal const int WM_ENTERIDLE = 0x0121;
        internal const int WM_MOUSEMOVE = 0x0200;
        internal const int WM_NCMOUSELEAVE = 0x02A2;
        internal const int WM_HSCROLL = 0x0114;
        internal const int WM_VSCROLL = 0x0115;
        internal const int WM_MOUSEWHEEL = 0x020A;
        internal const int WM_MOUSEHWHEEL = 0x020E;
        internal const int SB_ENDSCROLL = 8;
        internal const int SB_THUMBTRACK = 5;
        internal const int HTTRANSPARENT = -1;
        internal const int HTHSCROLL = 6;
        internal const int HTVSCROLL = 7;
        internal const int MA_NOACTIVATE = 3;

        internal const int OBJID_VSCROLL = -5;
        internal const int OBJID_HSCROLL = -6;
        internal const int STATE_SYSTEM_UNAVAILABLE = 0x1;
        internal const int STATE_SYSTEM_INVISIBLE = 0x8000;
        internal const int STATE_SYSTEM_OFFSCREEN = 0x10000;

        internal const int WS_CLIPSIBLINGS = 0x04000000;
        internal const int WS_CHILD = 0x40000000;
        internal const uint WS_POPUP = 0x80000000;
        internal const int WS_EX_TOPMOST = 0x00000008;
        internal const int WS_EX_TRANSPARENT = 0x00000020;
        internal const int WS_EX_TOOLWINDOW = 0x00000080;
        internal const int WS_EX_LAYERED = 0x00080000;
        internal const int WS_EX_NOACTIVATE = 0x08000000;
        internal const uint LWA_ALPHA = 0x2;
        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;

        [StructLayout(LayoutKind.Sequential)]
        internal struct SCROLLBARINFO
        {
            public int cbSize;
            public int left, top, right, bottom;
            public int dxyLineButton;
            public int xyThumbTop;
            public int xyThumbBottom;
            public int reserved;
            public int state0, state1, state2, state3, state4, state5;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TRACKMOUSEEVENT
        {
            public int cbSize;
            public int dwFlags;
            public IntPtr hwndTrack;
            public int dwHoverTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct COMBOBOXINFO
        {
            public int cbSize;
            public RECT rcItem;
            public RECT rcButton;
            public int stateButton;
            public IntPtr hwndCombo;
            public IntPtr hwndItem;
            public IntPtr hwndList;
        }

        [DllImport("user32.dll")] internal static extern bool GetScrollBarInfo(IntPtr hwnd, int idObject, ref SCROLLBARINFO psbi);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        /// <summary>The top-level window <paramref name="hwnd"/> is in (itself, for a top-level one).</summary>
        internal static IntPtr Root(IntPtr hwnd)
        {
            IntPtr root = hwnd == IntPtr.Zero ? IntPtr.Zero : GetAncestor(hwnd, 2 /* GA_ROOT */);
            return root == IntPtr.Zero ? hwnd : root;
        }
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] private static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT tme);
        [DllImport("user32.dll")] private static extern bool GetComboBoxInfo(IntPtr hwndCombo, ref COMBOBOXINFO pcbi);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] internal static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        /// <summary>A child window that does not clip its siblings paints over the ones in front of it.</summary>
        internal static void EnsureClipSiblings(IntPtr hwnd)
        {
            try
            {
                int style = GetWindowLong(hwnd, GWL_STYLE);
                if ((style & WS_CHILD) != 0 && (style & WS_CLIPSIBLINGS) == 0)
                {
                    SetWindowLong(hwnd, GWL_STYLE, style | WS_CLIPSIBLINGS);
                }
            }
            catch { }
        }

        internal static bool IsTopMost(IntPtr hwnd)
        {
            try { return hwnd != IntPtr.Zero && (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0; }
            catch { return false; }
        }

        /// <summary>The window's OWN visible bit (not its parents'): WM_SETREDRAW(FALSE) clears it, (TRUE) sets it.</summary>
        internal static bool HasVisibleBit(IntPtr hwnd)
        {
            const int WS_VISIBLE = 0x10000000;
            try { return hwnd != IntPtr.Zero && (GetWindowLong(hwnd, GWL_STYLE) & WS_VISIBLE) != 0; }
            catch { return false; }
        }

        /// <summary>Asks for WM_NCMOUSELEAVE when the pointer leaves the window's frame (its scroll bars).</summary>
        internal static void TrackNonClientLeave(IntPtr hwnd)
        {
            try
            {
                var tme = new TRACKMOUSEEVENT { cbSize = Marshal.SizeOf(typeof(TRACKMOUSEEVENT)), dwFlags = 0x2 | 0x10, hwndTrack = hwnd };
                TrackMouseEvent(ref tme);
            }
            catch { }
        }

        /// <summary>A combo box's drop-down list window, or IntPtr.Zero.</summary>
        internal static IntPtr ComboList(IntPtr combo)
        {
            var info = new COMBOBOXINFO { cbSize = Marshal.SizeOf(typeof(COMBOBOXINFO)) };
            return GetComboBoxInfo(combo, ref info) ? info.hwndList : IntPtr.Zero;
        }

        internal static Rectangle WindowRect(IntPtr hwnd)
        {
            return GetWindowRect(hwnd, out RECT r) ? Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom) : Rectangle.Empty;
        }

        internal const int WM_PAINT = 0x000F;

        [StructLayout(LayoutKind.Sequential)]
        internal struct PAINTSTRUCT
        {
            public IntPtr hdc;
            public bool fErase;
            public int left, top, right, bottom;
            public bool fRestore, fIncUpdate;
            public int r0, r1, r2, r3, r4, r5, r6, r7;
        }

        [DllImport("user32.dll")] internal static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT ps);
        [DllImport("user32.dll")] internal static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT ps);
        [DllImport("user32.dll")] internal static extern bool UpdateWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);

        internal static void Invalidate(IntPtr hwnd) => InvalidateRect(hwnd, IntPtr.Zero, false);

        [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr hwnd, IntPtr rect, IntPtr region, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        private struct SCROLLINFO { public int cbSize, fMask, nMin, nMax, nPage, nPos, nTrackPos; }

        [DllImport("user32.dll")] private static extern bool GetScrollInfo(IntPtr hwnd, int bar, ref SCROLLINFO info);

        /// <summary>A window's scroll position on its vertical or horizontal bar.</summary>
        internal static int Position(IntPtr hwnd, bool vertical)
        {
            var si = new SCROLLINFO { cbSize = Marshal.SizeOf(typeof(SCROLLINFO)), fMask = 0x4 /* SIF_POS */ };
            return GetScrollInfo(hwnd, vertical ? 1 /* SB_VERT */ : 0 /* SB_HORZ */, ref si) ? si.nPos : 0;
        }

        /// <summary>
        /// Puts <paramref name="s"/>'s thumb where a drag has it. While the thumb is dragged Windows moves its own with
        /// the pointer, but GetScrollBarInfo (and nPos) report where it was when the drag began until the button is
        /// let go; only nTrackPos follows the drag. Read from that alone, Tempo's thumb sat still under a moving
        /// pointer while the text scrolled — "it didn't move while I scroll" (scratchpad/debugscroll, real mouse).
        /// </summary>
        internal static bool TrackedThumb(IntPtr hwnd, bool vertical, ref ScrollBarState s)
        {
            var si = new SCROLLINFO
            {
                cbSize = Marshal.SizeOf(typeof(SCROLLINFO)),
                fMask = 0x1 | 0x2 | 0x4 | 0x10,   // SIF_RANGE | SIF_PAGE | SIF_POS | SIF_TRACKPOS
            };
            if (!GetScrollInfo(hwnd, vertical ? 1 : 0, ref si)) { return false; }
            int span = si.nMax - si.nMin - Math.Max(si.nPage - 1, 0);   // the positions past the first one
            int thumb = s.ThumbEnd - s.ThumbStart;
            int room = s.Length(vertical) - 2 * s.Arrow - thumb;
            if (span <= 0 || thumb <= 0 || room <= 0) { return false; }
            int track = Math.Max(si.nMin, Math.Min(si.nMin + span, si.nTrackPos)) - si.nMin;
            int at = s.Arrow + (int)Math.Round((double)track * room / span);
            s.ThumbStart = at;
            s.ThumbEnd = at + thumb;
            return true;
        }

        /// <summary>The whole window painted again now, frame included: what a scroll made with painting suspended owes.</summary>
        internal static void RepaintNow(IntPtr hwnd)
        {
            const uint RDW_INVALIDATE = 0x1, RDW_ERASE = 0x4, RDW_ALLCHILDREN = 0x80, RDW_UPDATENOW = 0x100, RDW_FRAME = 0x400;
            RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero, RDW_INVALIDATE | RDW_ERASE | RDW_FRAME | RDW_ALLCHILDREN | RDW_UPDATENOW);
        }

        internal static void Hide(IntPtr hwnd) => ShowWindow(hwnd, 0 /* SW_HIDE */);

        /// <summary>
        /// Puts an owned popup at <paramref name="screen"/>, directly in front of its owner in the z-order (just
        /// under whatever was in front of the owner), without activating it.
        /// </summary>
        internal static void PlaceOwned(IntPtr popup, IntPtr owner, Rectangle screen, bool topmost, bool show)
        {
            const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_NOOWNERZORDER = 0x200;
            const uint GW_HWNDPREV = 3;
            IntPtr after;
            if (topmost)
            {
                after = new IntPtr(-1);   // HWND_TOPMOST
            }
            else
            {
                IntPtr inFront = GetWindow(owner, GW_HWNDPREV);
                after = inFront == IntPtr.Zero || inFront == popup ? IntPtr.Zero /* HWND_TOP */ : inFront;
            }
            SetWindowPos(popup, after, screen.X, screen.Y, screen.Width, screen.Height,
                         SWP_NOACTIVATE | SWP_NOOWNERZORDER | (show ? SWP_SHOWWINDOW : 0));
        }
    }
}
