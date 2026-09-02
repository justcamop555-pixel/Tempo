using System;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Engine;
using AutoClicker.Models;
using AutoClicker.Native;
using AutoClicker.Persistence;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// The application's main window. The implementation is split across several
    /// partial files, one per tab, to keep each section focused:
    ///   MainForm.cs            - fields, lifecycle, tray, hotkeys, engine events
    ///   MainForm.Clicker.cs    - the primary clicker tab
    ///   MainForm.MultiPoint.cs - the multi-point editor tab
    ///   MainForm.Macros.cs     - macro recording / playback tab
    ///   MainForm.Statistics.cs - live statistics tab
    ///   MainForm.Settings.cs   - settings tab
    /// </summary>
    public partial class MainForm : Form
    {
        // WS_EX_COMPOSITED composites this window and ALL its descendants (every tab
        // page and control) to one off-screen buffer in a single bottom-up pass. This is
        // the reliable place for it: setting it on individual TabPages doesn't stick
        // (the TabControl recreates page handles when switching tabs and drops the style),
        // which is why checkboxes/labels still corrupted after scrolling. At the Form
        // level the handle is stable, so the whole UI - including the scrolling settings
        // cards - paints without the scroll/child artifacts (hollow checkboxes, left-
        // clipped labels). The backdrop GIF is off by default, so the compositing cost is
        // negligible for typical use.
        private const int WS_EX_COMPOSITED_FORM = 0x02000000;

        /// <summary>Forces the shell to give this window a taskbar button — see CreateParams.</summary>
        private const int WS_EX_APPWINDOW = 0x00040000;

        // WS_EX_COMPOSITED is ONLY on while a wallpaper is showing. It was previously
        // applied unconditionally, and its cost is NOT "negligible when the GIF is off"
        // as the note above assumed: the price scales with the number of child windows
        // and the repainted area, not with whether an image exists. Every scroll tick
        // re-composited the entire window — header, sidebar, tab control, every card,
        // checkbox and combo, footer — bottom-up into one buffer, which dragged
        // scrolling on the tall pages (Settings, Keybinds, Statistics) down to a few
        // frames a second on any machine.
        //
        // It is only actually NEEDED with a wallpaper: that's the mode where page
        // labels/checkboxes switch to TRANSPARENT backgrounds over a viewport-pinned
        // backdrop, which is what produced the hollow-checkbox / clipped-label
        // artifacts. With no wallpaper (the default) those same controls are solid, the
        // stock scroll blit is already pixel-perfect, and compositing buys nothing.
        private bool _compositedOn;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                if (_compositedOn)
                {
                    cp.ExStyle |= WS_EX_COMPOSITED_FORM;
                }

                // ── Keep the window styles of a NORMAL app window ──────────────
                // FormBorderStyle.None makes WinForms drop WS_CAPTION, WS_THICKFRAME,
                // WS_SYSMENU and WS_MINIMIZEBOX. Those bits aren't just decoration — the
                // shell and DWM read them to decide what a window can do:
                //
                //   WS_MINIMIZEBOX  the taskbar offers click-to-minimise, and Win+Down,
                //                   "Minimise all" and Show Desktop work. Without it,
                //                   clicking Tempo's taskbar button while it was in front
                //                   did nothing at all.
                //   WS_SYSMENU      Alt+Space and the taskbar right-click menu.
                //   WS_CAPTION      DWM runs its open / close / minimise / restore
                //   WS_THICKFRAME   animations for windows with a real frame. A bare
                //                   popup gets none of them, which is why Tempo blinked
                //                   in and out while every other Windows 11 app glided.
                //
                // The styles are re-added here and the frame they would draw is removed
                // in WM_NCCALCSIZE, so Windows still treats this as an ordinary framed
                // window while Tempo keeps painting the whole surface itself.
                if (_customChrome)
                {
                    cp.Style |= WS_CAPTION | WS_THICKFRAME | WS_SYSMENU
                                | WS_MINIMIZEBOX | WS_MAXIMIZEBOX;
                }

                // ── Always claim a taskbar button ─────────────────────────────
                // "Start minimised to tray" (and a sign-in launch) creates this window's
                // handle while it is deliberately hidden — SetVisibleCore calls
                // CreateHandle() and then base.SetVisibleCore(false). A handle born
                // hidden does not get WS_EX_APPWINDOW, and the shell then declines to
                // give the window a taskbar button when it is finally shown: Tempo sits
                // on screen with no entry in the taskbar, and no way to Alt-Tab or
                // click-to-minimise it.
                //
                // Measured on the taskbar itself: restoring from a tray start changed 6
                // pixel columns (the clock only) — no button. After one hide/show cycle
                // the style appeared on its own and a 51-column button showed up. Asking
                // for it up front makes the first restore behave like every later one.
                // A hidden window never gets a button whatever its styles, so this is
                // safe to set unconditionally.
                cp.ExStyle |= WS_EX_APPWINDOW;
                return cp;
            }
        }

        /// <summary>
        /// Turns whole-window compositing on/off to match whether a wallpaper is showing.
        /// Flips the ex-style on the live handle (no handle recreation, which would drop
        /// the tray icon, z-order and hotkey registrations) and asks Windows to re-cache
        /// the frame so the change takes effect immediately.
        /// </summary>
        private const int WM_ENTERSIZEMOVE = 0x0231;
        private const int WM_EXITSIZEMOVE = 0x0232;
        private const int WM_DEVICECHANGE = 0x0219;
        private const int DBT_DEVNODES_CHANGED = 0x0007;

        private System.Windows.Forms.Timer _deviceRescanTimer;
        private string _lastDeviceSignature;

        /// <summary>
        /// Re-reads the attached keyboards and mice a moment after the device tree
        /// settles, and reports it when the set actually changed.
        /// </summary>
        private void OnDeviceNodesChanged()
        {
            try
            {
                if (IsDisposed) { return; }
                if (_deviceRescanTimer == null)
                {
                    _deviceRescanTimer = new System.Windows.Forms.Timer { Interval = 900 };
                    _deviceRescanTimer.Tick += (s, e) =>
                    {
                        _deviceRescanTimer.Stop();
                        RescanInputDevices();
                    };
                }
                // Restart on every notification: one plug-in emits a burst.
                _deviceRescanTimer.Stop();
                _deviceRescanTimer.Start();
            }
            catch (Exception ex) { Utils.Logger.Swallow("DeviceChange", ex); }
        }

        /// <summary>
        /// Drops the cached device readouts and refreshes anything showing them. Logs
        /// only when the set of devices really changed, so a monitor waking or a USB
        /// hub settling doesn't fill the log.
        /// </summary>
        private void RescanInputDevices()
        {
            try
            {
                if (IsDisposed) { return; }

                string kb = Utils.KeyboardInfo.Summary();
                int mice = 0;
                string miceSummary = "";
                try
                {
                    mice = Engine.SecondCursorController.DetectedMouseCount();
                    miceSummary = Engine.SecondCursorController.DetectedMouseSummary();
                }
                catch { }

                string signature = kb + " || " + mice + " || " + miceSummary;
                if (string.Equals(signature, _lastDeviceSignature, StringComparison.Ordinal))
                {
                    return;         // the tree moved but the input devices did not
                }
                _lastDeviceSignature = signature;

                _keyboardSummaryCache = kb;          // Live debug reads this
                try { RefreshMiceDetectedLabel(); } catch { }

                Utils.Logger.Info("[Devices] input devices changed — keyboard: " + kb);
                Utils.Logger.Info("[Devices] mice: " + (mice > 0 ? miceSummary : "none detected"));
            }
            catch (Exception ex) { Utils.Logger.Swallow("RescanInputDevices", ex); }
        }

        /// <summary>True while the user is dragging or resizing the window.</summary>
        private bool _inMoveLoop;
        /// <summary>Whether compositing was on before the drag, so it can be put back.</summary>
        private bool _compositedBeforeMove;

        /// <summary>
        /// Drops the expensive-but-pointless work for the duration of a drag/resize.
        /// Idempotent: Windows can send WM_ENTERSIZEMOVE more than once for one gesture.
        /// </summary>
        private void BeginMoveResizeLoop()
        {
            if (_inMoveLoop) { return; }
            _inMoveLoop = true;
            try
            {
                _compositedBeforeMove = _compositedOn;
                if (_compositedOn)
                {
                    ApplyCompositedForBackdrop(false);
                }
                StopSharedBgAnimation();
            }
            catch (Exception ex) { Utils.Logger.Swallow("BeginMoveResizeLoop", ex); }
        }

        /// <summary>Restores everything <see cref="BeginMoveResizeLoop"/> suspended.</summary>
        private void EndMoveResizeLoop()
        {
            if (!_inMoveLoop) { return; }
            _inMoveLoop = false;
            try
            {
                if (_compositedBeforeMove)
                {
                    ApplyCompositedForBackdrop(true);
                }
                // The wallpaper is pinned to the VIEWPORT, so a window that moved is
                // showing a composite built for the old position — rebuild before the
                // animator resumes, or the first frame back shows the stale slice.
                InvalidateBackdropSurfaces();
                UpdateGifAnimationState();
            }
            catch (Exception ex) { Utils.Logger.Swallow("EndMoveResizeLoop", ex); }
        }

        /// <summary>
        /// Whether a wallpaper is showing, i.e. whether compositing is needed AT ALL.
        /// Compositing itself is now switched on only for the duration of a scroll —
        /// see <see cref="NotifyBackdropScroll"/>.
        /// </summary>
        private bool _wallpaperShowing;

        private System.Windows.Forms.Timer _compositeOffTimer;

        /// <summary>
        /// Called by a backdrop page when it is actually being scrolled.
        ///
        /// WS_EX_COMPOSITED is what keeps a transparent control over a viewport-pinned
        /// wallpaper from leaving blit remnants while the page scrolls (the hollow
        /// checkboxes and clipped labels the style was added for — still reproducible:
        /// a mid-scroll capture with it off caught a stale button ghosted into the card
        /// below). But it was left switched on for as long as a wallpaper was SET, and
        /// its cost does not wait for a scroll.
        ///
        /// Measured at idle, window open, nothing running:
        ///     wallpaper + composited   ~108% of one CPU core
        ///     wallpaper, not composited  ~12%
        ///     no wallpaper               ~13%
        ///     minimised (not painting)   ~15%
        ///
        /// So the style alone burned about 95% of a core, permanently, for a
        /// scroll-only benefit — and a pegged UI thread is precisely why dragging the
        /// window and the notification card animations felt laggy: every one of those
        /// frames had to queue behind it.
        ///
        /// Now it is enabled on the first scroll message and dropped again shortly after
        /// scrolling stops, so scrolling keeps the artifact-free path and an idle window
        /// costs nothing.
        /// </summary>
        internal void NotifyBackdropScroll() => ArmCompositingBriefly();

        /// <summary>
        /// Switches WS_EX_COMPOSITED on for the next moment and lets it lapse again.
        ///
        /// Scrolling was the first caller; a TAB SWITCH is the same event and was not
        /// wired up. Showing a page means every control on it paints at once, and with
        /// compositing off they arrive one at a time over the wallpaper — so the macro
        /// list and its buttons appeared before the page they belong to had settled,
        /// which reads as the controls turning up "before the tab". Arming it for the
        /// switch buys the artifact-free path for the one frame that needs it, and the
        /// existing countdown drops it again so an idle window still costs nothing.
        /// </summary>
        private void ArmCompositingBriefly()
        {
            if (!_wallpaperShowing || IsDisposed) { return; }
            try
            {
                if (!_compositedOn) { ApplyCompositedForBackdrop(true); }

                if (_compositeOffTimer == null)
                {
                    _compositeOffTimer = new System.Windows.Forms.Timer { Interval = 700 };
                    _compositeOffTimer.Tick += (s, e) =>
                    {
                        _compositeOffTimer.Stop();
                        if (!IsDisposed && _compositedOn) { ApplyCompositedForBackdrop(false); }
                    };
                }
                // Restart the countdown — a continuing scroll keeps compositing alive.
                _compositeOffTimer.Stop();
                _compositeOffTimer.Start();
            }
            catch (Exception ex) { Utils.Logger.Swallow("NotifyBackdropScroll", ex); }
        }

        private void ApplyCompositedForBackdrop(bool wallpaperShowing)
        {
            if (_compositedOn == wallpaperShowing)
            {
                return;
            }
            _compositedOn = wallpaperShowing;
            if (!IsHandleCreated || IsDisposed)
            {
                return;   // CreateParams will pick it up when the handle is made
            }
            try
            {
                int ex = GetWindowLong(Handle, GWL_EXSTYLE_FORM);
                int want = wallpaperShowing
                    ? (ex | WS_EX_COMPOSITED_FORM)
                    : (ex & ~WS_EX_COMPOSITED_FORM);
                if (want != ex)
                {
                    SetWindowLong(Handle, GWL_EXSTYLE_FORM, want);
                    SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
                        SWP_NOMOVE_F | SWP_NOSIZE_F | SWP_NOZORDER_F | SWP_NOACTIVATE_F | SWP_FRAMECHANGED_F);
                    Invalidate(true);
                }
            }
            catch (Exception ex2) { Logger.Swallow("Composited", ex2); }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after,
            int x, int y, int cx, int cy, uint flags);
        private const int GWL_EXSTYLE_FORM = -20;
        private const uint SWP_NOMOVE_F = 0x0002, SWP_NOSIZE_F = 0x0001,
                           SWP_NOZORDER_F = 0x0004, SWP_NOACTIVATE_F = 0x0010,
                           SWP_FRAMECHANGED_F = 0x0020;

        // A registered window message a SECOND launch broadcasts so this (the running)
        // instance can pop its window forward instead of just telling the user to hunt
        // the tray. Registered names are unique per string across the whole session, so
        // both instances resolve to the same id. 0 means registration failed — harmless.
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string lpString);
        internal const string ShowInstanceMessageName = "TempoShowExistingInstance_v1";
        private static readonly int WM_SHOW_TEMPO_INSTANCE = RegisterWindowMessage(ShowInstanceMessageName);

        // Explorer broadcasts this registered message to every top-level window the moment
        // the taskbar's notification area exists. Nothing here was listening for it, and
        // that is a real fault for an app that is launched AT SIGN-IN and lives in the tray.
        //
        // A NotifyIcon is a Shell_NotifyIcon(NIM_ADD) call against a tray that has to
        // already exist. Tempo's icon is added exactly once, at construction, and the
        // result is never checked — WinForms swallows it. Windows starts HKCU\...\Run
        // entries while the shell is still coming up, so at sign-in that single attempt
        // can land before there is anything to add to, and then the icon simply is not
        // there. The same single-shot add is why an Explorer CRASH takes Tempo's icon away
        // permanently: the new taskbar has no memory of icons registered against the old
        // one, and only this message says so.
        //
        // Re-registering on the broadcast is the documented fix for both, and it is the
        // only one — there is no API to ask whether your own tray icon is present.
        //
        // ⚠ LANDMINE — DO NOT SET ShowInTaskbar = false ON THIS FORM. HWND_BROADCAST
        // reaches invisible top-level windows but NOT OWNED ones, and ShowInTaskbar=false
        // makes WinForms give the form a hidden owner window purely to keep it off the
        // taskbar. Measured on this machine, cross-process broadcast to a MainForm-shaped
        // window:
        //     visible, unowned .................. RECEIVED
        //     Hide(), unowned (tray state) ...... RECEIVED
        //     ShowInTaskbar=false (owned) ....... NOT received
        //     ShowInTaskbar=false + minimised ... NOT received
        // Tempo goes to the tray with Hide(), which keeps it unowned, so it still gets the
        // message — that is the only reason this works while minimised to tray, which is
        // precisely when the tray icon is the only way back to the app. Hiding the taskbar
        // button that way instead would silently break BOTH this and the single-instance
        // "surface the running copy" message above, and neither failure reports anything.
        private static readonly int WM_TASKBARCREATED = RegisterWindowMessage("TaskbarCreated");

        // Clipboard-image "screenshot alert": Windows sends WM_CLIPBOARDUPDATE to every
        // registered listener the instant the clipboard changes — zero polling, zero
        // delay. When it holds an image and the user opted in, Tempo pops a card showing
        // the actual picture.
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
        private const int WM_CLIPBOARDUPDATE = 0x031D;
        private bool _clipboardListenerOn;
        private long _lastClipImageTick;

        protected override void WndProc(ref Message m)
        {
            if (WM_SHOW_TEMPO_INSTANCE != 0 && m.Msg == WM_SHOW_TEMPO_INSTANCE)
            {
                try { ShowFromTrayAndActivate(); } catch { }
                return;
            }
            if (WM_TASKBARCREATED != 0 && m.Msg == WM_TASKBARCREATED)
            {
                try { ReassertTrayIcon(); } catch (Exception ex) { Utils.Logger.Swallow("TaskbarCreated", ex); }
                // Fall through to base: this is a broadcast, not ours to consume.
            }
            if (m.Msg == WM_CLIPBOARDUPDATE)
            {
                try { OnClipboardUpdate(); } catch (Exception ex) { Utils.Logger.Swallow("ClipboardUpdate", ex); }
                // fall through to base so other listeners still work
            }

            // ── Dragging / resizing: get out of Windows' way ────────────────────
            //
            // Moving the window was heavy for one specific reason: a wallpaper switches
            // the form into WS_EX_COMPOSITED, and this codebase already measured what
            // that costs — ~90 ms for a full-window repaint, about 11 fps, against 5.7 ms
            // with compositing off. Every step of a drag paid it. On top of that the
            // shared backdrop animator kept firing 30 times a second, repainting the
            // header, sidebar and the whole active page while the window was moving.
            //
            // None of that buys anything mid-drag: compositing exists to keep SCROLLING
            // free of tearing, and an animated wallpaper frame nobody can focus on while
            // the window is in motion is pure cost. Both are suspended between
            // WM_ENTERSIZEMOVE and WM_EXITSIZEMOVE and restored the moment the drag ends,
            // so the drag itself runs on the cheap path and everything comes straight
            // back afterwards.
            if (m.Msg == WM_ENTERSIZEMOVE)
            {
                BeginMoveResizeLoop();
            }
            else if (m.Msg == WM_EXITSIZEMOVE)
            {
                EndMoveResizeLoop();
            }
            else if (m.Msg == WM_DEVICECHANGE && m.WParam.ToInt32() == DBT_DEVNODES_CHANGED)
            {
                // A device was plugged in or pulled out.
                //
                // Everything Tempo knows about your keyboard and mice was read ONCE, at
                // startup, and cached for the life of the process — the keyboard summary
                // literally never re-read, and the mouse list only refreshed when
                // something in Settings happened to ask. So plugging in a keyboard, or
                // the second mouse the Second Cursor feature is built around, changed
                // nothing until Tempo was restarted: the readout kept naming hardware
                // that was no longer attached, and a freshly connected mouse could not be
                // picked because the list did not know about it.
                //
                // DBT_DEVNODES_CHANGED is broadcast to every top-level window with no
                // registration needed, so this costs nothing until something actually
                // changes. It fires several times for one physical plug-in (each
                // interface of a composite HID arrives separately), hence the debounce.
                OnDeviceNodesChanged();
            }

            // ── Custom chrome: let WINDOWS do the hard parts ────────────────────
            // A borderless window has no non-client area, so Windows stops offering
            // drag, Aero Snap, double-click-to-maximise and edge resizing. Rather than
            // reimplement those (badly), we answer WM_NCHITTEST with the zones a normal
            // window would have — Windows then provides all of that behaviour natively.
            if (_customChrome && m.Msg == WM_NCHITTEST && !_isFullScreen)
            {
                int hit = HitTestChrome(m.LParam);
                if (hit != HTNOWHERE)
                {
                    m.Result = (IntPtr)hit;
                    return;
                }
            }

            // The window carries WS_CAPTION | WS_THICKFRAME so the shell and DWM treat it
            // as a normal window (see CreateParams). Those styles would also make Windows
            // reserve — and paint — a title bar and resize border. Reporting the client
            // area as the WHOLE window rect removes that frame while keeping the styles:
            // Tempo draws every pixel, Windows still animates and manages the window.
            if (_customChrome && m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
            {
                // Normally: leave the proposed rect alone, so client == window and no
                // frame is reserved.
                //
                // MAXIMISED is the exception. Windows sizes a WS_THICKFRAME window to the
                // work area PLUS the resize frame on every side, because it expects that
                // border to be drawn off-screen. Taking the rect as-is made the client
                // area 16 px too wide and tall — measured (78,-8) 1850×1048 against a
                // 1834×1032 work area — pushing the header's top edge and the status bar
                // off the screen. Insetting by the frame puts the client area back on
                // exactly the work area, which is what a normal maximised window does.
                // IsZoomed, not WindowState: WinForms hasn't updated its property yet when
                // this message arrives mid-transition, so testing WindowState here silently
                // did nothing and the overhang stayed. IsZoomed reads the window's own
                // WS_MAXIMIZE bit, which Windows sets before it asks for the client area.
                if (IsHandleCreated && IsZoomed(Handle) && !_isFullScreen)
                {
                    try
                    {
                        int fx = GetSystemMetrics(SM_CXSIZEFRAME) + GetSystemMetrics(SM_CXPADDEDBORDER);
                        int fy = GetSystemMetrics(SM_CYSIZEFRAME) + GetSystemMetrics(SM_CXPADDEDBORDER);
                        var r = System.Runtime.InteropServices.Marshal.PtrToStructure<RECT>(m.LParam);
                        r.Left += fx; r.Top += fy; r.Right -= fx; r.Bottom -= fy;
                        System.Runtime.InteropServices.Marshal.StructureToPtr(r, m.LParam, false);
                    }
                    catch (Exception ex) { Utils.Logger.Swallow("NcCalcSizeMax", ex); }
                }

                m.Result = IntPtr.Zero;
                return;
            }

            // A borderless window maximises over the WHOLE screen, hiding the taskbar.
            // Clamp it to the work area of the monitor it's on so maximise behaves.
            if (_customChrome && m.Msg == WM_GETMINMAXINFO && !_isFullScreen)
            {
                if (ClampMaximizeToWorkArea(m.LParam)) { return; }
            }

            base.WndProc(ref m);
        }

        // ── Custom chrome plumbing ─────────────────────────────────────────────
        private bool _customChrome;
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_GETMINMAXINFO = 0x0024;
        private const int WM_NCCALCSIZE = 0x0083;
        private const int GWL_STYLE = -16;

        // Resize-frame metrics, used to undo the maximised overhang in WM_NCCALCSIZE.
        private const int SM_CXSIZEFRAME = 32, SM_CYSIZEFRAME = 33, SM_CXPADDEDBORDER = 92;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsZoomed(IntPtr hWnd);

        [System.Runtime.InteropServices.StructLayout(
            System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        // Window styles WinForms drops for FormBorderStyle.None and CreateParams re-adds.
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_SYSMENU = 0x00080000;
        private const int WS_MINIMIZEBOX = 0x00020000;
        private const int WS_MAXIMIZEBOX = 0x00010000;
        private const int HTNOWHERE = 0, HTCLIENT = 1, HTCAPTION = 2;
        private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13,
                          HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
        /// <summary>Thickness of the invisible resize border, in pixels.</summary>
        private const int ResizeGrip = 6;

        /// <summary>
        /// Maps a screen point to the window zone it belongs to: an 6 px resize border
        /// around the edges, the header as the draggable caption, everything else client.
        /// Returns <see cref="HTNOWHERE"/> to let the default handling run.
        /// </summary>
        private int HitTestChrome(IntPtr lParam)
        {
            try
            {
                if (WindowState == FormWindowState.Minimized) { return HTNOWHERE; }

                int x = unchecked((short)(long)lParam);
                int y = unchecked((short)((long)lParam >> 16));
                Point p = PointToClient(new Point(x, y));

                // Resize borders — but never while maximised (there is nothing to drag).
                if (WindowState == FormWindowState.Normal)
                {
                    bool left = p.X <= ResizeGrip;
                    bool right = p.X >= ClientSize.Width - ResizeGrip;
                    bool top = p.Y <= ResizeGrip;
                    bool bottom = p.Y >= ClientSize.Height - ResizeGrip;

                    if (top && left) { return HTTOPLEFT; }
                    if (top && right) { return HTTOPRIGHT; }
                    if (bottom && left) { return HTBOTTOMLEFT; }
                    if (bottom && right) { return HTBOTTOMRIGHT; }
                    if (left) { return HTLEFT; }
                    if (right) { return HTRIGHT; }
                    if (top) { return HTTOP; }
                    if (bottom) { return HTBOTTOM; }
                }

                // The header is the title bar — EXCEPT over the caption buttons, which
                // must stay clickable. Reporting HTCAPTION there would let Windows eat
                // the click as the start of a window drag and the buttons would be dead.
                if (_header != null && !_header.IsDisposed)
                {
                    Point h = _header.PointToClient(new Point(x, y));
                    if (_header.ClientRectangle.Contains(h))
                    {
                        return _header.ButtonAt(h) >= 0 ? HTCLIENT : HTCAPTION;
                    }
                }
            }
            catch (Exception ex) { Utils.Logger.Swallow("HitTestChrome", ex); }
            return HTNOWHERE;
        }

        [System.Runtime.InteropServices.StructLayout(
            System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
        }

        /// <summary>
        /// Constrains a maximised borderless window to the monitor's WORK AREA, so it
        /// doesn't cover the taskbar. Returns true when it handled the message.
        /// </summary>
        private bool ClampMaximizeToWorkArea(IntPtr lParam)
        {
            try
            {
                Screen scr = Screen.FromControl(this);
                Rectangle wa = scr.WorkingArea, b = scr.Bounds;

                var mmi = System.Runtime.InteropServices.Marshal.PtrToStructure<MINMAXINFO>(lParam);
                mmi.MaxPosition = new Point(wa.Left - b.Left, wa.Top - b.Top);
                mmi.MaxSize = new Point(wa.Width, wa.Height);
                // Keep the user's minimum window size honoured while maximised too.
                mmi.MinTrackSize = new Point(MinimumSize.Width, MinimumSize.Height);
                System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, false);
                return true;
            }
            catch (Exception ex)
            {
                Utils.Logger.Swallow("ClampMaximize", ex);
                return false;
            }
        }

        /// <summary>
        /// Backstop for keys that never make it to <c>ProcessCmdKey</c> because a focused
        /// child swallowed them. Only handles the two that must ALWAYS work — leaving
        /// full screen — so it can't interfere with normal typing or shortcuts.
        /// </summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            try
            {
                if (_isFullScreen && (e.KeyCode == Keys.Escape || e.KeyCode == Keys.F11))
                {
                    Utils.Logger.Info("[UI] " + e.KeyCode + " left full screen (KeyPreview backstop).");
                    ToggleFullScreen();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                }
            }
            catch (Exception ex) { Utils.Logger.Swallow("KeyDownBackstop", ex); }
            base.OnKeyDown(e);
        }

        /// <summary>Handles a click on the header's ─ □ ✕ buttons.</summary>
        // SetForegroundWindow / GetForegroundWindow / GetWindowThreadProcessId /
        // GetCurrentThreadId / AttachThreadInput / BringWindowToTop are declared once for
        // the whole partial class in MainForm.Clicker.cs.

        /// <summary>
        /// Brings Tempo genuinely to the front after a title-bar action.
        ///
        /// Windows refuses SetForegroundWindow from a process that doesn't already own
        /// the foreground, so a plain Activate() silently does nothing when something
        /// else is in front. On Windows 11 that "something else" is often invisible:
        /// measured on this machine, the Widgets board (Widgets.exe, class
        /// WindowsDashboard) was sitting TOPMOST over the whole work area — 1834x1032 at
        /// (86,0) — while drawing nothing at all, holding the foreground and swallowing
        /// clicks aimed at Tempo's caption buttons. The window would maximise or restore
        /// and then sit behind that phantom, which reads as "clicking the buttons loses
        /// focus".
        ///
        /// Briefly attaching to the current foreground thread's input queue makes
        /// Windows treat the call as coming from the active app, so the request is
        /// honoured. Detached again immediately — leaving input queues attached would
        /// couple the two apps' focus handling.
        /// </summary>
        private void ForceForeground()
        {
            try
            {
                if (!IsHandleCreated || !Visible || WindowState == FormWindowState.Minimized)
                {
                    return;
                }

                IntPtr fg = GetForegroundWindow();
                if (fg == Handle)
                {
                    return;              // already ours — nothing to reclaim
                }

                uint us = GetCurrentThreadId();
                uint them = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, out _);

                bool attached = them != 0 && them != us && AttachThreadInput(us, them, true);
                try
                {
                    SetForegroundWindow(Handle);
                    BringWindowToTop(Handle);
                    Activate();
                }
                finally
                {
                    if (attached) { AttachThreadInput(us, them, false); }
                }
            }
            catch (Exception ex) { Utils.Logger.Swallow("ForceForeground", ex); }
        }

        private void OnCaptionButtonClicked(int index)
        {
            try
            {
                switch (index)
                {
                    case 0:
                        WindowState = FormWindowState.Minimized;
                        break;
                    case 1:
                        // OnResize syncs the glyph for this and every other route into
                        // and out of maximised, so it isn't set here any more.
                        WindowState = WindowState == FormWindowState.Maximized
                            ? FormWindowState.Normal : FormWindowState.Maximized;
                        ForceForeground();
                        break;
                    default:
                        Close();   // honours minimise-to-tray, confirm-on-exit, etc.
                        break;
                }
            }
            catch (Exception ex) { Utils.Logger.Swallow("CaptionButton", ex); }
        }

        /// <summary>
        /// Registers (or unregisters) the clipboard listener to match the
        /// "screenshot alert" setting. Called on load and whenever settings are saved.
        /// </summary>
        private void ApplyClipboardImageWatcher()
        {
            try
            {
                bool want = _settings != null && _settings.CustomNotifications
                            && _settings.NotifyOnClipboardImage && IsHandleCreated;
                if (want && !_clipboardListenerOn)
                {
                    _clipboardListenerOn = AddClipboardFormatListener(Handle);
                }
                else if (!want && _clipboardListenerOn)
                {
                    try { RemoveClipboardFormatListener(Handle); } catch { }
                    _clipboardListenerOn = false;
                }
            }
            catch (Exception ex) { Utils.Logger.Swallow("ApplyClipboardWatcher", ex); }
        }

        /// <summary>
        /// The clipboard changed — if it now holds an IMAGE, pop a Tempo card showing a
        /// thumbnail of it (the "screenshot copied" alert, with the real photo). Rate-
        /// limited so an app that rewrites the clipboard rapidly can't spam cards.
        /// </summary>
        private void OnClipboardUpdate()
        {
            if (_settings == null || !_settings.NotifyOnClipboardImage
                || !_settings.CustomNotifications || _notifications == null)
            {
                return;
            }
            long now = Environment.TickCount64;
            if (now - _lastClipImageTick < 700) { return; }   // debounce

            System.Drawing.Image thumb = null;
            string dim = null;
            string savedPath = null;
            try
            {
                if (!System.Windows.Forms.Clipboard.ContainsImage()) { return; }
                using (var img = GrabClipboardImage())
                {
                    if (img == null || img.Width < 8 || img.Height < 8) { return; }
                    dim = img.Width + "×" + img.Height;
                    // Save the FULL shot to a temp file now, so clicking the card opens
                    // the actual image in the default viewer (the clipboard may have
                    // changed by click time).
                    savedPath = SaveImageToTemp(img);
                    thumb = MakeThumbnail(img, 380, 150);
                }
            }
            catch (Exception ex) { Utils.Logger.Swallow("ClipImage", ex); }
            if (thumb == null) { return; }

            // Same shot again? Snipping Tool re-copies the image to the clipboard on
            // EVERY EDIT, so drawing on a snip fired one "Screenshot copied" card per
            // brush stroke and buried the screen.
            //
            // Keying on the picture's DIMENSIONS (the previous attempt) does NOT work:
            // Snipping Tool crops to the drawing's bounding box, so the size changes as
            // the stroke extends — the log showed 318×173 then 337×294 from one editing
            // session, each read as a brand-new screenshot.
            //
            // The reliable discriminator is in the log too: taking a NEW snip also fires
            // a Snipping Tool NOTIFICATION, while an edit re-copy fires none. So the
            // FIRST image from an app shows its card immediately (no delay, no waiting),
            // and any further image from that same app within the window is held very
            // briefly and only shown if that app's notification turns up to confirm it's
            // a genuinely new capture. Edits have no notification, so they fold away.
            bool repeatFromSameApp =
                !string.IsNullOrEmpty(_lastShotSignature) &&
                now - _lastShotSignatureTick < ShotRepeatWindowMs;

            // Ask Windows WHO put this image on the clipboard, right now. This is the
            // reliable way to know the screenshot came from Snipping Tool: it needs no
            // notification, so it can't lose the race against a fast click and it still
            // works when that app's notifications are switched off. The mirrored
            // notification (if one arrives) only refines the label afterwards.
            try
            {
                if (Utils.AppActivator.TryGetClipboardOwnerApp(out string ownerAumid, out string ownerName,
                                                               out System.Drawing.Image ownerIcon))
                {
                    if (!string.IsNullOrWhiteSpace(ownerName)) { _shotApp = ownerName; }
                    if (!string.IsNullOrWhiteSpace(ownerAumid)) { _shotAumid = ownerAumid; }
                    if (ownerIcon != null)
                    {
                        // The capture app's own icon, so the card doesn't wear Tempo's
                        // logo while naming a different app.
                        try { _shotIcon?.Dispose(); } catch { }
                        _shotIcon = ownerIcon;
                    }
                    _shotTick = Environment.TickCount64;
                    Utils.Logger.Info("[Notify] clipboard image came from " +
                        (_shotApp ?? "?") + (string.IsNullOrEmpty(_shotAumid) ? "" : " (" + _shotAumid + ")"));
                }
            }
            catch (Exception oex) { Utils.Logger.Swallow("ClipOwner", oex); }

            _lastClipImageTick = now;
            // Hold the card for a beat instead of showing it at once. Taking a screenshot
            // fires TWO things: the clipboard changes (this, instantly) and the capture
            // app posts its own Windows notification a moment later — which the mirror
            // would show as a SECOND, near-identical card ("Snipping Tool: Screenshot
            // copied to clipboard" next to Tempo's). The wait lets that notification be
            // folded into this one card, wearing the real app's name and icon.
            // Key the repeat window on the SOURCE APP (falling back to a constant when
            // it can't be resolved), not on the picture's size.
            _lastShotSignature = !string.IsNullOrEmpty(_shotAumid) ? _shotAumid
                               : !string.IsNullOrEmpty(_shotApp) ? _shotApp : "clipboard";
            _lastShotSignatureTick = now;

            StashPendingClipCard(thumb, dim, savedPath, repeatFromSameApp);
        }

        // A clipboard-image card waiting briefly so the capture app's own notification can
        // be merged into it. 160 ms still covers the mirror's 120 ms poll, and halving it
        // from 320 ms is the difference between the card feeling instant and feeling like
        // it lagged the screenshot. If the capture app's notification lands after this,
        // the merge simply doesn't happen — one card either way, never two.
        private System.Windows.Forms.Timer _clipMergeTimer;
        private System.Drawing.Image _pendingClipThumb;
        private string _pendingClipDim;
        private string _pendingClipPath;

        // The capture app whose notification was swallowed, so the merged card can wear
        // its name/icon and open the shot back in it.
        private string _shotApp;
        private string _shotAumid;
        private System.Drawing.Image _shotIcon;
        private long _shotTick;

        /// <summary>True while a clipboard-image card is being held for merging.</summary>
        private bool ClipCardPending => _clipMergeTimer != null && _clipMergeTimer.Enabled;

        // "WxH:bytes" of the last picture we alerted about, so the same shot arriving on
        // the clipboard twice only pops one card.
        private string _lastShotSignature;
        private long _lastShotSignatureTick;
        // Re-copies of the same shot that were folded into its existing card (an editing
        // session in Snipping Tool is the big one). Surfaced in Live Debug.
        private int _shotRepeatsSuppressed;
        // A stashed card that only counts as a NEW screenshot if the source app's
        // notification arrives to confirm it (see OnClipboardUpdate).
        private bool _pendingClipNeedsConfirm;
        private long _pendingClipStashTick;
        // When the source app last posted a screenshot notification.
        private long _shotNotifyTick;
        // How long the same picture size keeps counting as "the shot we already announced".
        // Extended on every repeat, so it spans a whole editing session.
        private const int ShotRepeatWindowMs = 25000;

        private static long SafeFileLength(string path)
        {
            try { return path != null ? new System.IO.FileInfo(path).Length : 0; }
            catch { return 0; }
        }

        private void StashPendingClipCard(System.Drawing.Image thumb, string dim, string path,
                                          bool needsNotificationToConfirm = false)
        {
            _pendingClipNeedsConfirm = needsNotificationToConfirm;
            _pendingClipStashTick = Environment.TickCount64;
            try { _pendingClipThumb?.Dispose(); } catch { }
            _pendingClipThumb = thumb;
            _pendingClipDim = dim;
            _pendingClipPath = path;
            if (_clipMergeTimer == null)
            {
                _clipMergeTimer = new System.Windows.Forms.Timer { Interval = 160 };
                _clipMergeTimer.Tick += (s, e) =>
                {
                    _clipMergeTimer.Stop();
                    try { ShowPendingClipCard(); }
                    catch (Exception ex) { Utils.Logger.Swallow("ClipCard", ex); }
                };
            }
            // A first capture shows straight away; a repeat waits a beat for the
            // notification that would prove it's a new capture rather than an edit.
            _clipMergeTimer.Interval = needsNotificationToConfirm ? 400 : 160;
            _clipMergeTimer.Stop();
            _clipMergeTimer.Start();
        }

        /// <summary>
        /// Shows the single merged screenshot card: the capture app's name and icon when
        /// its notification was folded in, the shot itself as the picture, and a click
        /// that opens the image back in THAT app (Snipping Tool, ShareX, …) rather than
        /// whatever program happens to own .png.
        /// </summary>
        private void ShowPendingClipCard()
        {
            System.Drawing.Image thumb = _pendingClipThumb;
            _pendingClipThumb = null;
            string dim = _pendingClipDim;
            string path = _pendingClipPath;
            if (thumb == null || _notifications == null)
            {
                try { thumb?.Dispose(); } catch { }
                return;
            }

            // A repeat from the same app that no notification vouched for is an EDIT
            // (a brush stroke re-copying the canvas), not a new screenshot — fold it.
            if (_pendingClipNeedsConfirm && _shotNotifyTick < _pendingClipStashTick)
            {
                _shotRepeatsSuppressed++;
                try { thumb.Dispose(); } catch { }
                try { if (path != null) { System.IO.File.Delete(path); } } catch { }
                return;
            }

            bool haveSource = !string.IsNullOrEmpty(_shotApp) &&
                              Environment.TickCount64 - _shotTick < 5000;
            string app = haveSource ? _shotApp : "Tempo";
            System.Drawing.Image icon = haveSource && _shotIcon != null ? _shotIcon : TempoNotifyIcon();
            if (haveSource) { _shotIcon = null; }   // the card owns it now
            string aumid = haveSource ? _shotAumid : null;

            Action open = () =>
            {
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                {
                    OpenScreenshotsFolder();
                    return;
                }
                // Back into the app that took it, exactly like clicking its own toast.
                if (!string.IsNullOrWhiteSpace(aumid) &&
                    Utils.AppActivator.OpenFileWithAumid(aumid, path))
                {
                    Utils.Logger.Info("[Notify] screenshot card clicked → opened in " + app);
                    return;
                }
                OpenPath(path);
            };

            _shotCard = _notifications.NotifyCard(app, "Screenshot copied to clipboard",
                (dim != null ? dim + " · " : "") +
                (haveSource ? "Click to open in " + app : "Click to open the image"),
                UI.ToastKind.Info, icon, thumb, open);
            _shotCardDim = dim;
            _shotCardPath = path;
            _shotCardTick = Environment.TickCount64;

            _shotApp = null;
            _shotAumid = null;
        }

        // The screenshot card currently on screen, so the capture app's notification can
        // re-label it the instant it arrives instead of the card having to WAIT for it.
        private UI.NotificationToastForm _shotCard;
        private string _shotCardDim;
        private string _shotCardPath;
        private long _shotCardTick;

        /// <summary>
        /// Re-labels the screenshot card already on screen with the app that took the
        /// shot. This is what lets the card appear immediately AND still end up saying
        /// "Snipping Tool" — the identity arrives a fraction of a second later and is
        /// applied in place. Returns true when a card was upgraded.
        /// </summary>
        private bool UpgradeShotCard(string app, string aumid, System.Drawing.Image icon)
        {
            var card = _shotCard;
            if (card == null || card.IsDisposed ||
                Environment.TickCount64 - _shotCardTick > 6000)
            {
                return false;
            }
            string path = _shotCardPath;
            card.SetActivate(() =>
            {
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                {
                    OpenScreenshotsFolder();
                    return;
                }
                if (!string.IsNullOrWhiteSpace(aumid) &&
                    Utils.AppActivator.OpenFileWithAumid(aumid, path))
                {
                    Utils.Logger.Info("[Notify] screenshot card clicked → opened in " + app);
                    return;
                }
                OpenPath(path);
            });
            card.UpdateSource(app, icon,
                (_shotCardDim != null ? _shotCardDim + " · " : "") + "Click to open in " + app);
            return true;
        }

        /// <summary>
        /// Recognises a "you just took a screenshot" notification, so it can be folded
        /// into Tempo's own richer card instead of doubling up.
        /// </summary>
        private static bool LooksLikeScreenshotNotification(string app, string title, string body)
        {
            string s = ((app ?? "") + " " + (title ?? "") + " " + (body ?? "")).ToLowerInvariant();
            return s.Contains("screenshot") || s.Contains("snipping") || s.Contains("screen clip")
                || s.Contains("screen shot")
                || (s.Contains("clipboard") && s.Contains("cop"));
        }

        /// <summary>Reads the clipboard image with one retry (the setting app briefly
        /// locks the clipboard). Returns null if it can't be read.</summary>
        private static System.Drawing.Image GrabClipboardImage()
        {
            try { return System.Windows.Forms.Clipboard.GetImage(); }
            catch
            {
                try { System.Threading.Thread.Sleep(40); return System.Windows.Forms.Clipboard.GetImage(); }
                catch { return null; }
            }
        }

        /// <summary>Writes an image to a Tempo temp PNG so a notification click can open
        /// it. Prunes clips older than ~15 min so the folder doesn't grow. Path, or null.</summary>
        private static string SaveImageToTemp(System.Drawing.Image img)
        {
            try
            {
                string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Tempo", "clips");
                System.IO.Directory.CreateDirectory(dir);
                try
                {
                    var cutoff = DateTime.UtcNow.AddMinutes(-15);
                    foreach (var old in System.IO.Directory.GetFiles(dir, "shot-*.png"))
                    {
                        try { if (System.IO.File.GetLastWriteTimeUtc(old) < cutoff) { System.IO.File.Delete(old); } }
                        catch { }
                    }
                }
                catch { }
                string file = System.IO.Path.Combine(dir, "shot-" + Environment.TickCount64 + ".png");
                img.Save(file, System.Drawing.Imaging.ImageFormat.Png);
                return file;
            }
            catch { return null; }
        }

        /// <summary>Opens a file or folder with its default handler.</summary>
        private static void OpenPath(string path)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex) { Utils.Logger.Swallow("OpenPath", ex); }
        }

        /// <summary>A downscaled copy of an image that fits the notification card's hero
        /// slot (never larger than maxW×maxH; original aspect kept). The card owns it.</summary>
        private static System.Drawing.Image MakeThumbnail(System.Drawing.Image src, int maxW, int maxH)
        {
            try
            {
                double scale = Math.Min((double)maxW / src.Width, (double)maxH / src.Height);
                if (scale > 1.0) { scale = 1.0; }
                int w = Math.Max(1, (int)Math.Round(src.Width * scale));
                int h = Math.Max(1, (int)Math.Round(src.Height * scale));
                var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(src, 0, 0, w, h);
                }
                bmp.Tag = src.Width + "×" + src.Height;
                return bmp;
            }
            catch { return null; }
        }

        /// <summary>Opens the user's Screenshots folder (where PrtScn / Win+Shift+S save).</summary>
        private void OpenScreenshotsFolder()
        {
            try
            {
                string pics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                string shots = System.IO.Path.Combine(pics, "Screenshots");
                string target = System.IO.Directory.Exists(shots) ? shots : pics;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                { FileName = target, UseShellExecute = true });
            }
            catch (Exception ex) { Utils.Logger.Swallow("OpenScreenshots", ex); }
        }

        /// <summary>
        /// Brings the window out of the tray (if hidden) and to the foreground.
        /// Invoked when a second copy of Tempo is launched — the running one surfaces
        /// itself rather than leaving the user to find it.
        /// </summary>
        /// <summary>Window state before it was sent to the tray, so it comes back the same.</summary>
        private FormWindowState _preTrayState = FormWindowState.Normal;

        /// <summary>Last state the window was visible in, tracked in OnResize — see there.</summary>
        private FormWindowState _lastNonMinimizedState = FormWindowState.Normal;

        /// <summary>
        /// Sends the window to the tray using Windows' own minimise animation instead of
        /// having it blink out of existence. <see cref="Control.Hide"/> has no transition
        /// at all — the window is simply gone on the next frame, which reads as a crash
        /// rather than "it's still running, in the tray". Minimising first plays the
        /// native shrink-towards-the-taskbar animation; the window is hidden once that
        /// has finished, so the taskbar button disappears and only the tray icon is left.
        /// </summary>
        private void HideToTrayAnimated()
        {
            try
            {
                if (!Visible) { return; }
                if (WindowState == FormWindowState.Minimized)
                {
                    // Already minimised, so WindowState can no longer tell us whether the
                    // window was maximised before it went down — hence the tracked value.
                    // Without this, maximise → minimise → close-to-tray → reopen came back
                    // Normal, because _preTrayState still held the previous cycle's value.
                    _preTrayState = _lastNonMinimizedState;
                    Hide();
                    return;
                }

                _preTrayState = WindowState;
                WindowState = FormWindowState.Minimized;

                // Hide only after the animation has played. Hiding sooner cuts it off and
                // we are back to the window vanishing mid-flight. Note this deliberately
                // does NOT restore WindowState afterwards: setting it on a hidden form
                // would ask Windows to show the window again. ShowFromTrayAndActivate
                // restores it from _preTrayState when the user reopens.
                var settle = new Timer { Interval = 220 };
                settle.Tick += (s, e) =>
                {
                    settle.Stop();
                    settle.Dispose();
                    try { if (WindowState == FormWindowState.Minimized) { Hide(); } }
                    catch (Exception ex) { Utils.Logger.Swallow("HideToTray", ex); }
                };
                settle.Start();
            }
            catch (Exception ex)
            {
                Utils.Logger.Swallow("HideToTrayAnimated", ex);
                try { Hide(); } catch { }
            }
        }

        /// <summary>
        /// The 980x700 design minimum scaled for this screen's DPI — but never larger
        /// than the screen can actually show.
        /// </summary>
        /// <remarks>
        /// The scaling alone is what made Tempo cramped on laptops while desktops were
        /// fine. Laptops default to 125-150% scaling, and the minimum scales with it
        /// while the screen does not:
        ///
        ///   1920x1080 @100% (typical desktop)  →  needs  980x700   work area 1920x1032  fits
        ///   1920x1080 @150% (common laptop)    →  needs 1470x1050  work area 1920x1008  TOO TALL
        ///   1366x768  @125%                    →  needs 1225x875   work area 1366x708   TOO TALL
        ///   1366x768  @150%                    →  needs 1470x1050  work area 1366x696   TOO BIG
        ///
        /// A minimum bigger than the work area doesn't get ignored — the window really
        /// is forced to that size, so its content runs off the screen and every page
        /// laid out at absolute coordinates ends up crowded and overlapping. Clamping to
        /// the work area lets the window be as small as the screen requires; pages are
        /// AutoScroll, so a genuinely short screen scrolls instead of overlapping.
        /// </remarks>
        private Size ScaledMinimumSize(float uiScale)
        {
            int w = (int)Math.Round(980 * uiScale);
            int h = (int)Math.Round(700 * uiScale);
            try
            {
                Rectangle wa = Screen.FromControl(this).WorkingArea;
                if (wa.Width > 0 && wa.Height > 0)
                {
                    w = Math.Min(w, wa.Width);
                    h = Math.Min(h, wa.Height);
                }
            }
            catch { /* no screen info — keep the scaled figures */ }
            return new Size(w, h);
        }

        private void ShowFromTrayAndActivate()
        {
            try
            {
                // Come back the way we went in: a window that was maximised when it was
                // sent to the tray should reopen maximised, not normal.
                //
                // "Did we come from the tray?" is decided BEFORE Show(), because Show()
                // is what makes Visible true — a test afterwards cannot tell a genuine tray
                // restore from the tray MENU calling this on a window that is already open.
                // That distinction matters: the menu path must activate the window without
                // resizing it, and _preTrayState can still hold an earlier cycle's value.
                bool cameFromTray = !Visible || WindowState == FormWindowState.Minimized;
                FormWindowState want = _preTrayState == FormWindowState.Maximized
                    ? FormWindowState.Maximized
                    : FormWindowState.Normal;

                if (!Visible)
                {
                    Show();
                }
                // Only when actually returning from the tray — the tray menu also calls
                // this on an already-open window, and that must not resize it.
                if (cameFromTray && WindowState != want)
                {
                    WindowState = want;
                }
                BringToFront();
                EnsureOnScreen();
                Activate();
                ReassertTopMost();
                // Retrying repair, for the same reason as ToggleWindowVisibility: a
                // window opened straight from the tray was never minimised, so OnResize
                // never fires and this is the ONLY repair it gets. A single attempt that
                // happened to run before the client size settled left the window half
                // laid out — the intermittent "didn't fully load". This is also the path
                // every "start minimised to tray" launch takes when it is first opened.
                RepairLayoutAfterRestore();

                // Rebuild the backdrop composite, don't just repaint with it.
                // TryRepairLayoutNow invalidates the page, but that redraws using the
                // CACHED composite — which is stale (or was never built) after the window
                // spent time hidden. With a wallpaper every control is transparent, so a
                // page painted from a missing composite shows the desktop through itself.
                InvalidateBackdropSurfaces();
            }
            catch { }
        }

        // ── Core services ─────────────────────────────────────────────────────
        private readonly SessionStatistics _statistics = new SessionStatistics();
        private ClickEngine _engine;
        private GlobalHotkeyManager _hotkeys;
        private readonly ProfileManager _profiles = new ProfileManager();
        private readonly MacroStore _macros = new MacroStore();
        private readonly SessionHistoryStore _history = new SessionHistoryStore();
        private long _runStartClicks;
        private bool _runCompletedHandled;
        private MacroRecorder _recorder;
        private MacroPlayer _player;
        private AppSettings _settings;
        private Theme _theme;

        // ── Top level UI ──────────────────────────────────────────────────────
        private ModernTabControl _tabs;
        private Panel _sidebar;
        private readonly System.Collections.Generic.List<RoundedButton> _navButtons = new System.Collections.Generic.List<RoundedButton>();
        private StatusStrip _statusStrip;
        private BrandHeader _header;
        private GifBackdropPanel _footerGif;
        private Image _fullBgImage;
        private ClickingIndicatorForm _clickingIndicator;
        private SelfClickGuard _selfClickGuard;

        // The start-delay countdown overlay while it's on screen, so the stop hotkey
        // can abort it (see DispatchAction / BeginStartWithCountdown).
        private CountdownOverlayForm _activeCountdown;
        private CursorTrailForm _cursorTrail;
        private CaptionOverlayForm _captionOverlay;
        private bool _captionsActive;
        // Guards against re-entrant caption toggles (e.g. mashing the hotkey) and a
        // verify timer that must be cancelled if the state changes before it fires.
        private bool _captionToggleBusy;
        private System.Windows.Forms.Timer _captionVerifyTimer;
        private Utils.LiveCaptionReader _captionReader;
        private Utils.TempoTranscriber _captionTranscriber;
        // Session-only model override, set when the chosen model proved too slow for
        // real time on this PC. The user's SAVED choice is never touched.
        private string _captionModelOverrideKey;
        private string _captionModelActiveKey;
        // The Windows Live Captions mirror polls on a background (thread-pool) timer,
        // NOT a WinForms timer, because reading its text walks a UI Automation tree that
        // can block for seconds when that process is busy - doing that on the UI thread
        // froze Tempo ("not responding", force-kill). _captionMirrorTickGuard prevents a
        // slow read from overlapping the next poll; _captionMirrorRunning gates teardown.
        private System.Threading.Timer _captionMirrorTimer;
        // Short-lived fast timer that aggressively pushes the Windows Live Captions
        // bar off-screen for the first few seconds after captions turn on. The normal
        // 150ms read-poll also hides it, but Windows shows the bar in a "Ready to show
        // live captions" state before any text exists, and on some PCs that lingered
        // visible. This enforcer hammers HideWindowsBar on a fast cadence so the bar
        // can't sit on screen during that startup gap, then stops itself.
        private System.Windows.Forms.Timer _captionHideEnforcer;
        private int _captionHideEnforcerTicks;
        private volatile bool _captionMirrorRunning;
        private int _captionMirrorTickGuard;
        private string _lastMirroredCaption = "";
        private int _captionMirrorMisses;
        private volatile bool _captionHadAnyText;
        private readonly Utils.SpeakerTurnLabeler _speakerTurns = new Utils.SpeakerTurnLabeler();
        private Utils.VoiceProfiler _voiceProfiler;
        private Utils.FaceSpeakerAnalyzer _faceAnalyzer;
        private Utils.CaptionWordFixer _wordFixer;
        private Utils.MediaDetector _mediaDetector;
        private Utils.AudioDeviceWatcher _audioWatcher;
        private Utils.GamePresence _gamePresence;
        private Engine.SecondCursorController _secondCursor;
        // The "this game is Exclusive Fullscreen, captions can't draw over it"
        // balloon is shown at most once per run — it explains a Windows-level
        // limit, and repeating it every match would be nagging.
        private bool _exclusiveTipShown;
        private string _lastSpeakerName;   // previous default speaker, to detect switches
        // Auto-start guard: armed on each media transition to "playing"; disarmed when
        // the user turns captions OFF while that media is still active, so we never
        // fight a deliberate off.
        private bool _mediaAutoArmed = true;
        // What was playing when the user last switched captions OFF, and when the media
        // last fell quiet. Together these decide when auto-start may arm again: a
        // different app/site, or a genuinely long silence — never a mere pause in the
        // thing they just silenced.
        private string _autoStartOffSource = string.Empty;
        private DateTime _mediaInactiveSinceUtc = DateTime.MinValue;
        // How long the PC has to stay quiet before a deliberate "captions off" stops
        // suppressing auto-start. Comfortably longer than an ad break, a paused video or
        // the gap between two songs, short enough that a later sitting starts normally.
        private const double AutoStartRearmQuietSeconds = 90;
        // Music/sound note: when the last real caption text arrived, how long the
        // audio has been continuously non-speech, and whether the "♪ Music or
        // sounds" note currently occupies the bar.
        private DateTime _lastCaptionTextUtc = DateTime.MinValue;
        private DateTime _soundKindSinceUtc = DateTime.MinValue;
        private bool _soundNoteShown;
        // True once Tempo's own engine has auto-fallen-back to Windows mirroring for
        // the current caption session, so it only switches over once per toggle and
        // doesn't ping-pong between engines.
        private bool _captionFellBackToWindows;
        // True once we've switched away from the Windows mirror to Tempo's own engine
        // because UI Automation is broken on this PC. One-shot per caption session.
        private bool _captionUiaFallbackDone;
        private volatile bool _captionDiagLogged;
        private readonly System.Collections.Generic.List<string> _captionHistory = new System.Collections.Generic.List<string>();
        // Wall-clock time each history line was last updated (parallel to _captionHistory).
        private readonly System.Collections.Generic.List<DateTime> _captionHistoryTimes = new System.Collections.Generic.List<DateTime>();
        // External-toggle watcher state (user pressing Win+Ctrl+L themselves).
        private DateTime _externalWatchCooldownUntil = DateTime.MinValue;
        private int _externalWatchTick;
        // Whether the Windows Live Captions window was there LAST time the watcher
        // looked. The watcher must fire on the EDGE (it just appeared = the user
        // pressed Win+Ctrl+L themselves), never on the LEVEL (it is present) —
        // level-triggering meant that after you turned captions off, the watcher
        // saw the still-open Windows bar and switched Tempo straight back on, over
        // and over. Seeded to true on an explicit off so a lingering window has to
        // actually go away before it can count as a fresh external turn-on.
        private bool _externalLcWasPresent;
        private CaptionHistoryForm _captionHistoryForm;
        private ToolStripMenuItem _trayCaptionHistoryItem;
        private ToolStripMenuItem _trayMoveCaptionsItem;
        private ToolStripMenuItem _trayShowHideItem;
        private ToolStripMenuItem _trayStatusItem;
        private ToolStripMenuItem _trayStartStopItem;
        private bool _captionMoveMode;
        private ClickingIndicatorForm _macroIndicator;
        private bool _isFullScreen;
        private FormBorderStyle _fsPrevBorder;
        private FormWindowState _fsPrevState;
        private bool _wasMinimized;
        private Rectangle _fsPrevBounds;
        private bool _fsPrevTopMost;
        private Size _fsPrevMinSize;
        private int _fsPrevFooterHeight = -1;
        private readonly System.Collections.Generic.Dictionary<Control, int> _autoFitBaseTop
            = new System.Collections.Generic.Dictionary<Control, int>();
        private readonly System.Collections.Generic.Dictionary<Control, int> _autoFitBaseLeft
            = new System.Collections.Generic.Dictionary<Control, int>();
        // Remembers the client width each page was last centred against, so we can
        // skip re-centring on a ClientSizeChanged that was caused only by the
        // scrollbar appearing/disappearing (which would otherwise reposition controls
        // mid-scroll and flash an empty view). Width is the only thing centring
        // depends on, so if width is unchanged there is nothing to redo.
        private readonly System.Collections.Generic.Dictionary<TabPage, int> _lastCenteredWidth
            = new System.Collections.Generic.Dictionary<TabPage, int>();
        // Remembers each tab's vertical/horizontal scroll position so switching tabs and
        // coming back restores exactly where you were (stored as the AutoScrollPosition
        // value, i.e. negative offsets, matching how WinForms reports/accepts it).
        private readonly System.Collections.Generic.Dictionary<TabPage, Point> _tabScroll
            = new System.Collections.Generic.Dictionary<TabPage, Point>();
        private StatusPill _statePill;
        private ToolStripStatusLabel _statusState;
        private ToolStripStatusLabel _statusClicks;
        private ToolStripStatusLabel _statusCps;
        private ToolStripStatusLabel _statusElapsed;
        private ToolStripStatusLabel _statusProfile;
        private ToolStripStatusLabel _statusHint;
        private ToolStripStatusLabel _statusPeak;
        private ToolStripStatusLabel _statusProgress;
        private ToolStripStatusLabel _statusThrottle;

        // ── Resource readout (CPU / RAM / uptime) ────────────────────────────────
        //
        // Its own CpuMonitor rather than the engine's: that one is owned by anti-freeze
        // and only sampled while a click run is going, and CpuMonitor is delta-based, so
        // two callers sharing one instance would each see the other's interval and both
        // read nonsense.
        private ToolStripStatusLabel _statusCpu;
        private ToolStripStatusLabel _statusRam;
        private ToolStripStatusLabel _statusUptime;
        private ToolStripSeparator _statusResourceSep;
        private Utils.CpuMonitor _uiCpuMonitor;
        private DateTime _resourceNextSampleUtc = DateTime.MinValue;
        private readonly DateTime _appStartedUtc = DateTime.UtcNow;
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;
        private ToolStripMenuItem _trayCaptionsItem;
        private ToolStripMenuItem _trayNotifyItem;
        private ToolStripMenuItem _trayScreenshotItem;
        private ToolStripMenuItem _trayAlwaysOnTopItem;
        // Custom animated notification pop-ups + the Windows-notification mirror.
        private UI.NotificationCenter _notifications;
        private Utils.WindowsNotificationMirror _notifyMirror;
        private System.Windows.Forms.Timer _uiTimer;
        private System.Windows.Forms.Timer _holdPollTimer;
        private bool _holdActive;
        private bool _reallyClosing;
        // Set the moment OnFormClosing commits to actually exiting (past the
        // minimise-to-tray and "still running, exit anyway?" branches). Shutdown hides
        // the window straight away so the exit LOOKS instant, and routine state-tracking
        // has no business reacting to that — see UpdateTraySleepState, which would
        // otherwise read the hide as "gone to tray" and re-register hotkeys on the way
        // out. Distinct from _reallyClosing, which only marks the deliberate Exit paths.
        private bool _shuttingDown;
        // Times the whole exit so a slow teardown step names itself in the log instead
        // of being reported as a vague "Tempo freezes when I close it".
        private System.Diagnostics.Stopwatch _shutdownClock;
        // The same for the way IN. Startup is one straight run of build calls with no
        // visibility into which of them costs what, so "Tempo is slow to open" had
        // nothing behind it but guesswork.
        private System.Diagnostics.Stopwatch _startupClock;
        private long _lifetimeBaseline;

        // ── Clicker tab controls ──────────────────────────────────────────────
        private ComboBox _profileCombo;
        private Button _newProfileBtn;
        private Button _saveProfileBtn;
        private Button _deleteProfileBtn;
        private Button _duplicateProfileBtn;
        private TextBox _profileNameText;

        private NumericUpDown _hoursNum;
        private NumericUpDown _minutesNum;
        private NumericUpDown _secondsNum;
        private NumericUpDown _millisNum;

        private ComboBox _buttonCombo;
        private ComboBox _styleCombo;
        private ComboBox _modeCombo;
        private NumericUpDown _holdMsNum;
        private Button _setKeyBtn;
        private int _selectedKeyVk;

        private RadioButton _posCurrentRadio;
        private RadioButton _posFixedRadio;
        private RadioButton _posMultiRadio;
        private CheckBox _restoreCursorCheck;
        private CheckBox _backgroundClickCheck;
        private CheckBox _soundOnStartCheck;
        private CheckBox _soundOnStopCheck;
        private CheckBox _secondCursorEnableCheck;
        private CheckBox _secondMouseUseCheck;
        private Label _miceDetectedLabel;
        private ComboBox _secondMouseCombo;
        private System.Windows.Forms.Timer _miceRefreshTimer;
        private string _mouseComboSig = "";
        private bool _suppressMouseComboEvent;
        private NumericUpDown _fixedXNum;
        private NumericUpDown _fixedYNum;
        private Button _pickFixedBtn;

        private RadioButton _repeatUntilRadio;
        private RadioButton _repeatCountRadio;
        private NumericUpDown _repeatCountNum;
        private RadioButton _repeatDurationRadio;
        private NumericUpDown _repeatDurationNum;

        private NumericUpDown _burstSizeNum;
        private NumericUpDown _burstPauseNum;
        private GroupBox _burstGroup;

        private CheckBox _randIntervalCheck;
        private Button _humanizeBtn;
        private int _humanizeLevel;   // 0 off, 1 light, 2 medium, 3 heavy
        private NumericUpDown _intervalJitterNum;
        private CheckBox _randPosCheck;
        private NumericUpDown _posJitterNum;

        private Button _startBtn;
        private Button _stopBtn;
        private Button _cpsTestBtn;
        private Label _bigStatusLabel;
        private Label _liveCpsLabel;

        // ── Multi-point tab controls ──────────────────────────────────────────
        private ClickPointListView _pointsList;
        private Button _addPointBtn;
        private Button _editPointBtn;
        private Button _removePointBtn;
        private Button _clearPointsBtn;
        private Button _toggleAllPointsBtn;
        private Label _pointsEmptyHint;
        private Button _movePointUpBtn;
        private Button _movePointDownBtn;
        private Button _capturePointBtn;
        private Button _duplicatePointBtn;
        private Button _togglePointBtn;
        private Button _showPointsBtn;
        private ComboBox _pointOrderCombo;
        private Label _cycleInfoLabel;

        // ── Macros tab controls ───────────────────────────────────────────────
        private ListBox _macroListBox;
        private Button _recordBtn;
        private Button _stopRecordBtn;
        private Button _playMacroBtn;
        private Button _playOnceBtn;
        private Button _stopPlayBtn;
        private Button _deleteMacroBtn;
        private Button _macroRecycleBtn;
        private NumericUpDown _macroLoopNum;
        private NumericUpDown _macroSpeedNum;
        private CheckBox _recordMovesCheck;
        private Label _recordStatusLabel;
        private Macro _lastRecorded;

        // ── Statistics tab controls ───────────────────────────────────────────
        private StatCard _cardSessionClicks;
        private StatCard _cardTotalClicks;
        private StatCard _cardCurrentCps;
        private StatCard _cardPeakCps;
        private StatCard _cardAvgCps;
        private StatCard _cardClicksPerMin;
        private StatCard _cardElapsed;
        private StatCard _cardLeft;
        private StatCard _cardRight;
        private StatCard _cardMiddle;
        private StatCard _cardLifeClicks;
        private StatCard _cardLifeSessions;
        private StatCard _cardLifePeak;
        private StatCard _cardLifeRuntime;
        private StatCard _cardMostClicks;
        private StatCard _cardLongestRun;
        private StatCard _cardToday;
        private StatCard _cardAvgPerSession;
        private StatCard _cardAvgRunLength;
        // Insights section (new)
        private StatCard _cardThisWeek;
        private StatCard _cardThisMonth;
        private StatCard _cardLifeAvgCps;
        private StatCard _cardActiveDays;
        private StatCard _cardBestDay;
        private StatCard _cardBusiestWeekday;
        private StatCard _cardBusiestHour;
        private StatCard _cardTopProfile;
        private StatCard _cardStreak;
        private StatCard _cardLongestStreak;
        private StatCard _cardDailyAvg;
        private StatCard _cardThisYear;
        private TabPage _statsPage;
        private MiniBarChart _hourChart;
        private MiniBarChart _weekdayChart;
        private TextBox _historySearchBox;
        private string _historySearchText = "";
        private SparklineControl _cpsSparkline;
        private DistributionBar _distBar;
        private MiniBarChart _sessionBarChart;
        private MiniBarChart _dailyBarChart;
        private SessionHistoryListView _sessionHistoryList;
        private NumericUpDown _sessionGoalNum;
        private ThemedProgressBar _goalProgressBar;
        private Label _goalProgressLabel;
        private ThemedProgressBar _milestoneBar;
        private Label _milestoneLabel;
        private Label _milestoneBadges;
        private long _lastMilestoneClicks = -1; // highest tier reached at last check; -1 = not yet initialised
        private bool _goalReachedNotified;
        private double _displayCps;
        private ComboBox _historyProfileFilter;
        private Label _historySummaryLabel;
        private bool _suppressGoalEvent;
        private bool _suppressHistoryFilterEvent;
        private int _histSortColumn = -1;
        private bool _histSortAsc = true;
        private Button _resetStatsBtn;
        private Button _resetLifetimeBtn;

        // ── Settings tab controls ─────────────────────────────────────────────
        private ComboBox _themeCombo;
        private CheckBox _followSystemThemeCheck;
        private Label _lastCheckedLabel;
        private ComboBox _languageCombo;
        private CheckBox _minimizeToTrayCheck;
        private CheckBox _startMinimizedCheck;
        private CheckBox _traySleepCheck;
        private CheckBox _captionOverlayCheck;
        private CheckBox _captionSpeakerCheck;
        private CheckBox _captionOwnVoiceCheck;
        private Utils.SelfVoiceGuard _selfVoiceGuard;
        private CheckBox _captionAutoStartCheck;
        private CheckBox _captionFaceCheck;
        private CheckBox _captionTranscriptCheck;
        private ComboBox _captionSourceCombo;
        private ComboBox _captionModelCombo;
        private ComboBox _captionLangCombo;
        private Label _captionLangLabel;
        private Label _captionLangHint;
        private ComboBox _captionCaptureCombo;
        private Label _captionCaptureLabel;
        private Label _captionModelLabel;
        private Label _captionModelStatus;
        private Label _audioDeviceStatus;
        private ComboBox _speakerDeviceCombo;
        private ComboBox _micDeviceCombo;
        private CheckBox _captionSourceTagCheck;
        private CheckBox _captionGpuCheck;

        // Camera-relative movement (Engine/CameraRelativeMovement).
        private CheckBox _movementEnableCheck;
        private ComboBox _movementFrameCombo;
        private NumericUpDown _movementDegPerCountNum;
        private NumericUpDown _movementSmoothingNum;
        private NumericUpDown _movementHysteresisNum;
        private NumericUpDown _movementHzNum;
        private NumericUpDown _movementDeadzoneNum;
        private NumericUpDown _movementPadYawNum;
        private Label _movementStatus;
        private Engine.CameraRelativeMovement _movement;

        /// <summary>Detected-keyboard line, resolved once (see BuildDebugStats).</summary>
        private string _keyboardSummaryCache;
        private Button _liveDebugBtn;
        private DebugForm _debugForm;
        private NumericUpDown _captionFontNum;
        private NumericUpDown _captionOpacityNum;

        private CheckBox _captionBackgroundCheck;
        private ComboBox _captionFontCombo;
        private Button _captionColorBtn;
        private Button _captionDownloadModelBtn;
        private CheckBox _trayNotifyCheck;
        // Custom-notification settings controls.
        private CheckBox _customNotifyCheck;
        private CheckBox _mirrorNotifyCheck;
        private CheckBox _mirrorClearCheck;
        private ComboBox _notifyCornerCombo;
        private NumericUpDown _notifyDurationNum;
        private CheckBox _notifyScreenshotCheck;
        private CheckBox _notifyCloseCheck;
        private Label _notifyStatusLabel;
        private CheckBox _alwaysOnTopCheck;
        private CheckBox _customAccentCheck;
        private Button _chooseAccentBtn;
        private Panel _accentSwatch;
        private Panel[] _previewSwatches;
        private Button _previewButton;
        private Label _previewSample;
        private CheckBox _confirmExitCheck;
        private CheckBox _safetyEscapeCheck;
        private NumericUpDown _startDelayNum;
        private CheckBox _startDelayBeepCheck;
        private ComboBox _updateFreqCombo;
        private NumericUpDown _startupDelayNum;
        private Button _saveSettingsBtn;

        public MainForm()
        {
            Logger.Initialize();

            _settings = SettingsManager.Load();
            _lifetimeBaseline = _settings.LifetimeClicks;
            Logger.Enabled = _settings.WriteLogFile;

            // First run only: match Tempo to the user's Windows display language
            // using the translations already built in (no new languages added).
            // Only when still on the English default and not yet auto-detected, so
            // a manual choice in Settings is always respected.
            if (!_settings.LanguageAutoDetected && _settings.Language == Language.English)
            {
                _settings.Language = Localization.DetectSystemLanguage();
                _settings.LanguageAutoDetected = true;
                try { SettingsManager.Save(_settings); } catch { }
            }

            // Apply the chosen UI language before any tabs/controls are built.
            Localization.Current = _settings.Language;

            _profiles.Load();
            _macros.Load();
            _history.Load();
            // One-time: seed the rolling lifetime stat aggregates from existing history so
            // the all-time insight cards don't reset for users upgrading with past runs.
            SeedLifetimeAggregatesIfNeeded();

            _theme = BuildActiveTheme();
            // Paint the form its dark theme colour from the very first frame (before the
            // handle is created and any control is built), so the window never shows a
            // white default background behind/around the controls as it appears.
            BackColor = _theme.Background;
            ForeColor = _theme.Text;
            _engine = new ClickEngine(_statistics);
            _hotkeys = new GlobalHotkeyManager();
            _recorder = new MacroRecorder();
            _player = new MacroPlayer();

            _startupClock = System.Diagnostics.Stopwatch.StartNew();
            StartupStep("shell", InitializeShell);
            StartupStep("clicker tab", BuildClickerTab);
            StartupStep("profiles tab", BuildProfilesTab);
            StartupStep("multi-point tab", BuildMultiPointTab);
            StartupStep("macros tab", BuildMacrosTab);
            StartupStep("statistics tab", BuildStatisticsTab);
            StartupStep("keybinds tab", BuildKeybindsTab);
            // Before Settings on purpose: the tray's "Settings…" entry selects the LAST
            // tab, so anything appended after it would silently take over that menu item.
            StartupStep("captions tab", BuildCaptionsTab);
            StartupStep("settings tab", BuildSettingsTab);
            StartupStep("sidebar", BuildSidebar);

            WireEngineEvents();
            WireHotkeyEvents();
            WireMacroEvents();

            StartupStep("theme", ApplyThemeToEverything);

            // AFTER every tab is built and BEFORE the window is shown: the fitter measures
            // what each caption actually became at this language and this display scale,
            // and clamps only the ones that would run into the control beside them. Cards
            // are laid out on hard-coded columns chosen against English at 100%, so any
            // longer language — or any higher DPI — otherwise overprints two captions on
            // top of each other. Controls that fit are left untouched.
            StartupStep("fit captions", () => LayoutFitter.FitAll(this));

            StartupStep("background image", ApplyBackgroundGif);
            RefreshBusyLock();
            StartupStep("initial profile", LoadInitialProfile);
            StartupStep("macro list", RefreshMacroList);
            LoadKeybindsIntoUi();
            StartupStep("settings into UI", LoadSettingsIntoUi);

            StartupStep("hotkeys", ApplyHotkeysFromSettings);
            StartupStep("window preferences", ApplyWindowPreferences);

            StartupStep("tooltips", SetupTooltips);
            StartupStep("restore last tab", RestoreLastTab);

            // While a run is in progress, Tempo's own injected clicks must not operate
            // Tempo's own interface — see SelfClickGuard. Real clicks are untouched, so
            // Stop stays reachable.
            // Read live rather than captured, so toggling the setting takes effect at once
            // instead of at the next launch. If settings are somehow missing, guard anyway.
            _selfClickGuard = new SelfClickGuard(() =>
                (_settings == null || _settings.IgnoreOwnWindowWhileRunning) &&
                ((_engine != null && _engine.IsRunning) ||
                 (_player != null && _player.IsPlaying)));
            // Second-cursor spam is deliberately NOT listed here. It reaches its target
            // with PostMessage rather than SendInput, so it carries no dwExtraInfo for
            // this filter to recognise — adding it would have looked like protection
            // while doing nothing. It refuses to target Tempo's own windows instead,
            // at the point where the message is posted (see SecondCursorController).
            Application.AddMessageFilter(_selfClickGuard);

            StartMediaDetector();
            StartAudioDeviceWatcher();

            // Re-arm camera-relative movement if the user left it on. Safe here: raw
            // input needs a message pump, and by this point we have one.
            ApplyMovementSetting();

            // Second cursor ("second mouse"): create it and apply the saved look /
            // spam settings. Only shown once the user enables it.
            ApplySecondCursorSettings();

            // Surface ERROR events as a (rate-limited) tray notification — the user
            // should know when something breaks, not discover it in a log later.
            Utils.Logger.LineLogged += OnLoggerLineForNotify;

            MaybeCheckForUpdatesOnLaunch();
            MaybeWarnDiscordPath();

            if (_startupClock != null)
            {
                _startupClock.Stop();
                Logger.Info("[startup] window built in " + _startupClock.ElapsedMilliseconds + " ms.");
            }
        }

        /// <summary>
        /// Discord's game detection flags any process whose path ends in
        /// "tempo\tempo.exe" as the Steam game "Tempo" and shows the user as playing
        /// it. The installer now uses a "TempoClicker" folder, but a PORTABLE copy
        /// that the user unzipped into their own folder named "Tempo" still matches —
        /// tell them once how to stop it.
        /// </summary>
        private void MaybeWarnDiscordPath()
        {
            try
            {
                if (_settings == null || _settings.DiscordPathHintShown)
                {
                    return;
                }
                string p = (Environment.ProcessPath ?? "").ToLowerInvariant();
                if (!p.EndsWith("\\tempo\\tempo.exe", StringComparison.Ordinal))
                {
                    return;
                }
                _settings.DiscordPathHintShown = true;
                try { Persistence.SettingsManager.Save(_settings); } catch { }
                Utils.Logger.Info("[UI] Discord path hint shown (running from ...\\Tempo\\Tempo.exe).");
                if (_trayIcon != null && _settings.ShowTrayNotifications)
                {
                    TempoNotify(8000, "Tempo",
                        "Discord may show you as playing the Steam game \"Tempo\" because "
                        + "this folder is named Tempo. Rename the folder (e.g. TempoClicker) "
                        + "or reinstall with install.cmd to stop that.",
                        ToolTipIcon.Info);
                }
            }
            catch { }
        }

        /// <summary>
        /// Watches for a video site or game in the foreground with audio playing and
        /// turns captions on automatically (Settings → Live Captions → Auto-start).
        /// Runs for the app's whole life; the Enabled gate makes disabled ticks free.
        /// </summary>
        private void StartMediaDetector()
        {
            try
            {
                _mediaDetector = new Utils.MediaDetector
                {
                    Enabled = () => _settings != null && _settings.CaptionAutoStart
                };
                _mediaDetector.StateChanged += (active, reason) =>
                {
                    try
                    {
                        if (!IsHandleCreated || IsDisposed)
                        {
                            return;
                        }
                        BeginInvoke((Action)(() =>
                        {
                            if (!active)
                            {
                                // Media stopped. Note WHEN, but don't re-arm here: a
                                // pause, a gap between sentences and the silence between
                                // two tracks all land in this branch, and re-arming on
                                // any of them is what let captions the user had just
                                // turned off reappear moments later.
                                if (_mediaInactiveSinceUtc == DateTime.MinValue)
                                {
                                    _mediaInactiveSinceUtc = DateTime.UtcNow;
                                }
                                return;
                            }

                            // Media is playing again. If the user switched captions off,
                            // only a genuinely NEW listening situation may re-arm them:
                            // a different app/site than the one they silenced, or a long
                            // enough stretch of real quiet that this is plainly a new
                            // sitting rather than the same video resuming.
                            if (!_mediaAutoArmed)
                            {
                                string src = _mediaDetector != null
                                    ? (_mediaDetector.CurrentAudioSource ?? string.Empty)
                                    : string.Empty;
                                bool differentSource = src.Length > 0 &&
                                    !string.Equals(src, _autoStartOffSource, StringComparison.OrdinalIgnoreCase);
                                bool longQuiet = _mediaInactiveSinceUtc != DateTime.MinValue &&
                                    (DateTime.UtcNow - _mediaInactiveSinceUtc).TotalSeconds >= AutoStartRearmQuietSeconds;
                                if (differentSource || longQuiet)
                                {
                                    _mediaAutoArmed = true;
                                    Utils.Logger.Info("[Captions] auto-start re-armed (" +
                                        (differentSource ? "different source: " + src : "media quiet for a while") + ").");
                                }
                            }
                            _mediaInactiveSinceUtc = DateTime.MinValue;

                            if (!_mediaAutoArmed || _captionsActive ||
                                _settings == null || !_settings.CaptionAutoStart)
                            {
                                return;
                            }
                            Utils.Logger.Info("[Captions] auto-start: " + reason + " detected with audio.");
                            SetCaptionsActive(true);
                            if (_trayIcon != null && _settings.ShowTrayNotifications)
                            {
                                TempoNotify(4000, "Tempo",
                                    Localization.T("Captions started for") + " " + reason +
                                    ". " + Localization.T("Turn off with your caption hotkey; disable auto-start in Settings."),
                                    ToolTipIcon.Info);
                            }
                        }));
                    }
                    catch { }
                };
            }
            catch (Exception ex)
            {
                Utils.Logger.Warn("Media detector unavailable: " + ex.Message);
            }
        }

        /// <summary>
        /// Watches this PC's audio devices for the caption stack: shows plainly in
        /// Settings whether a speaker (and mic) exists RIGHT NOW, and auto-recovers
        /// Tempo's caption engine when a speaker appears or comes back — previously
        /// that needed Live Captions toggled off and on by hand.
        /// </summary>
        private void StartAudioDeviceWatcher()
        {
            try
            {
                // The user's saved device choices must be in force BEFORE any capture
                // opens — every consumer resolves through AudioDeviceSelection.
                Utils.AudioDeviceSelection.SpeakerId = _settings != null ? _settings.CaptionSpeakerDeviceId : "";
                Utils.AudioDeviceSelection.MicrophoneId = _settings != null ? _settings.CaptionMicDeviceId : "";

                _audioWatcher = new Utils.AudioDeviceWatcher();
                // Name the default output on the splash — concrete proof this stage ran.
                try
                {
                    string spk = _audioWatcher.SpeakerName;
                    SplashForm.Report(3, string.IsNullOrWhiteSpace(spk)
                        ? Localization.T("no speaker found")
                        // A device NAME is never translated — it is what Windows calls the
                        // hardware, and renaming it would make it unrecognisable.
                        : (spk.Length > 26 ? spk.Substring(0, 24) + "…" : spk));
                }
                catch { }
                _audioWatcher.DeviceListChanged += () =>
                {
                    try
                    {
                        if (!IsHandleCreated || IsDisposed) { return; }
                        BeginInvoke((Action)RefreshDeviceCombos);
                    }
                    catch { }
                };
                _audioWatcher.SpeakerChanged += name =>
                {
                    try
                    {
                        if (!IsHandleCreated || IsDisposed) { return; }
                        BeginInvoke((Action)(() =>
                        {
                            string previous = _lastSpeakerName;
                            _lastSpeakerName = name;
                            UpdateAudioDeviceStatusLabel();

                            if (name == null)
                            {
                                return;             // speaker gone — capture recovery
                            }                       // and the label already cover it

                            // A speaker (re)appeared or the DEFAULT CHANGED. Follow it
                            // with the keep-alive first.
                            Utils.LoopbackKeepAlive.Poke();

                            // A speaker arrived while captions run on the MICROPHONE
                            // by the user's explicit choice — offer the better source.
                            MaybeOfferSpeaker(previous, name);

                            // Default switched while the old device stayed alive: no
                            // RecordingStopped fires in that case, so the captures
                            // keep listening to the wrong (now silent) device. Point
                            // the engine and the voice profiler at the new default.
                            if (previous != null && previous != name)
                            {
                                Utils.Logger.Info("[Captions] default speaker changed (" +
                                    previous + " → " + name + ") - following.");
                                try
                                {
                                    if (_captionTranscriber != null && _captionTranscriber.IsRunning)
                                    {
                                        _captionTranscriber.FollowDefaultDevice();
                                    }
                                }
                                catch { }
                                try { _voiceProfiler?.FollowDefaultDevice(); } catch { }
                            }

                            // And if Tempo's caption engine is supposed to be running
                            // but died (started with no device, or lost it for good),
                            // bring it back without the user toggling captions.
                            if (_captionsActive && !_captionFellBackToWindows &&
                                _settings != null && _settings.CaptionSource != 0 &&
                                _captionTranscriber != null && !_captionTranscriber.IsRunning)
                            {
                                Utils.Logger.Info("[Captions] speaker appeared (" + name + ") - restarting engine.");
                                StartTempoCaptions();
                            }
                            // The engine is still "running" but its capture died for good
                            // (unplugged with no device, or the reopen budget ran out) —
                            // IsRunning stays true so the block above can't see it, and a
                            // freshly-replugged speaker arrives with previous == null so
                            // the follow branch skips. Re-follow explicitly on any speaker
                            // arrival while the capture is flagged lost.
                            else if (_captionTranscriber != null && _captionTranscriber.IsRunning
                                     && _captionTranscriber.CaptureLost)
                            {
                                Utils.Logger.Info("[Captions] speaker appeared (" + name + ") after capture was lost - reconnecting.");
                                try { _captionTranscriber.FollowDefaultDevice(); } catch { }
                                try { _voiceProfiler?.FollowDefaultDevice(); } catch { }
                            }
                        }));
                    }
                    catch { }
                };
                _audioWatcher.MicrophoneChanged += micName =>
                {
                    try
                    {
                        if (!IsHandleCreated || IsDisposed) { return; }
                        BeginInvoke((Action)(() =>
                        {
                            UpdateAudioDeviceStatusLabel();
                            MaybeOfferMicrophone(micName);
                        }));
                    }
                    catch { }
                };
            }
            catch (Exception ex)
            {
                Utils.Logger.Warn("Audio device watcher unavailable: " + ex.Message);
            }
        }

        /// <summary>
        /// Refreshes the Settings line that says which speaker/microphone this PC
        /// has right now — the plain answer to "can captions hear anything?". With
        /// BOTH devices present it names both, so the "Listen to" choice above it
        /// is an informed one.
        /// </summary>
        private void UpdateAudioDeviceStatusLabel()
        {
            if (_audioDeviceStatus == null || _audioWatcher == null)
            {
                return;
            }
            try
            {
                // The device PICKERS now carry the model names; this line only
                // speaks up when something needs the user's attention.
                string speaker = _audioWatcher.SpeakerName;
                string mic = _audioWatcher.MicrophoneName;
                if (speaker == null)
                {
                    _audioDeviceStatus.Text = "⚠ " + Localization.T(
                        "No speaker found — Tempo can't hear system audio.") + " " +
                        (mic != null ? Localization.T("Microphone available:") + " " + mic + " — " +
                                       Localization.T("set \"Listen to\" to Microphone.")
                                     : Localization.T("Connect a speaker or microphone to use Tempo's captions."));
                    _audioDeviceStatus.ForeColor = _theme != null ? _theme.Warning : Color.Orange;
                    return;
                }
                if (_settings != null && _settings.CaptionSpeakerDeviceId.Length > 0 &&
                    !DeviceIdPresent(_audioWatcher.Speakers, _settings.CaptionSpeakerDeviceId))
                {
                    _audioDeviceStatus.Text = "⚠ " + Localization.T(
                        "Your chosen speaker isn't connected — captions are using the Windows default for now.");
                    _audioDeviceStatus.ForeColor = _theme != null ? _theme.Warning : Color.Orange;
                    return;
                }
                if (_settings != null && _settings.CaptionMicDeviceId.Length > 0 &&
                    !DeviceIdPresent(_audioWatcher.Microphones, _settings.CaptionMicDeviceId))
                {
                    _audioDeviceStatus.Text = "⚠ " + Localization.T(
                        "Your chosen microphone isn't connected — the Windows default is used for now.");
                    _audioDeviceStatus.ForeColor = _theme != null ? _theme.Warning : Color.Orange;
                    return;
                }
                _audioDeviceStatus.Text = "";
            }
            catch { }
        }

        private static bool DeviceIdPresent(
            System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>> list, string id)
        {
            foreach (var kv in list)
            {
                if (kv.Key == id) { return true; }
            }
            return false;
        }

        // The endpoint ids behind the picker rows (index 0 = "" = Windows default).
        private readonly System.Collections.Generic.List<string> _speakerDeviceIds =
            new System.Collections.Generic.List<string>();
        private readonly System.Collections.Generic.List<string> _micDeviceIds =
            new System.Collections.Generic.List<string>();

        /// <summary>
        /// Rebuilds both device pickers from the watcher's live lists, keeping the
        /// saved selection when its device is still present. Runs on the UI thread.
        /// </summary>
        private void RefreshDeviceCombos()
        {
            if (_speakerDeviceCombo == null || _micDeviceCombo == null || _audioWatcher == null)
            {
                return;
            }
            try
            {
                _suppressSettingsEvents = true;

                FillDeviceCombo(_speakerDeviceCombo, _speakerDeviceIds, _audioWatcher.Speakers,
                    _settings != null ? _settings.CaptionSpeakerDeviceId : "");
                FillDeviceCombo(_micDeviceCombo, _micDeviceIds, _audioWatcher.Microphones,
                    _settings != null ? _settings.CaptionMicDeviceId : "");
            }
            catch { }
            finally
            {
                _suppressSettingsEvents = false;
            }
            UpdateAudioDeviceStatusLabel();
        }

        private void FillDeviceCombo(ComboBox combo,
            System.Collections.Generic.List<string> ids,
            System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>> devices,
            string savedId)
        {
            combo.BeginUpdate();
            try
            {
                combo.Items.Clear();
                ids.Clear();
                combo.Items.Add(Localization.T("Default (follow Windows)"));
                ids.Add("");
                int select = 0;
                foreach (var kv in devices)
                {
                    combo.Items.Add(kv.Value);
                    ids.Add(kv.Key);
                    if (savedId.Length > 0 && kv.Key == savedId)
                    {
                        select = ids.Count - 1;
                    }
                }
                if (combo.Items.Count == 1)
                {
                    // Honest emptiness: no device of this class AT ALL.
                    combo.Items.Add(Localization.T("(no device found)"));
                    ids.Add("");
                }
                combo.SelectedIndex = select;
            }
            finally
            {
                combo.EndUpdate();
            }
        }

        /// <summary>A device picker changed: save, apply everywhere, restart captures.</summary>
        private void OnCaptionDeviceChosen(bool speaker)
        {
            if (_suppressSettingsEvents || _settings == null)
            {
                return;
            }
            try
            {
                if (speaker)
                {
                    int i = _speakerDeviceCombo.SelectedIndex;
                    string id = i >= 0 && i < _speakerDeviceIds.Count ? _speakerDeviceIds[i] : "";
                    _settings.CaptionSpeakerDeviceId = id;
                    Utils.AudioDeviceSelection.SpeakerId = id;
                }
                else
                {
                    int i = _micDeviceCombo.SelectedIndex;
                    string id = i >= 0 && i < _micDeviceIds.Count ? _micDeviceIds[i] : "";
                    _settings.CaptionMicDeviceId = id;
                    Utils.AudioDeviceSelection.MicrophoneId = id;
                }
                try { Persistence.SettingsManager.Save(_settings); } catch { }
                Utils.Logger.Info("[Captions] " + (speaker ? "speaker" : "microphone") +
                                  " picker changed - re-pointing the audio stack.");

                // Everything that listens follows the choice immediately.
                Utils.LoopbackKeepAlive.Poke();
                try { _voiceProfiler?.FollowDefaultDevice(); } catch { }
                if (_captionsActive && !_captionFellBackToWindows &&
                    _captionTranscriber != null && _captionTranscriber.IsRunning)
                {
                    _captionTranscriber.FollowDefaultDevice();
                }
                UpdateAudioDeviceStatusLabel();
            }
            catch (Exception ex)
            {
                Utils.Logger.Warn("Device choice failed: " + ex.Message);
            }
        }

        // One-shot arming so each device ARRIVAL asks at most once: re-armed only
        // when that device class disappears again. Stops any Yes/No nagging loop.
        private bool _micOfferArmed = true;
        private bool _speakerOfferArmed = true;

        /// <summary>
        /// A microphone (dis)appeared. THE rescue case: captions are on but the PC
        /// has NO speaker — nothing to hear — and a microphone just got plugged in.
        /// Ask plainly (Yes/No) whether to caption from it, and switch on Yes.
        /// </summary>
        private void MaybeOfferMicrophone(string micName)
        {
            if (micName == null)
            {
                _micOfferArmed = true;                  // gone — next arrival may ask
                return;
            }
            if (!_micOfferArmed || !_captionsActive ||
                _settings == null || _settings.CaptionSource == 0 ||
                _settings.CaptionCaptureMode == 2 ||    // already listening to the mic
                _audioWatcher == null || _audioWatcher.HasSpeaker)
            {
                return;                                 // speaker exists → system audio is fine
            }
            _micOfferArmed = false;
            Utils.Logger.Info("[Audio] microphone connected (" + micName + ") with no speaker present — asking the user.");

            DialogResult r = MessageBox.Show(this,
                Localization.T("A microphone was just connected:") + "\n\n    🎙 " + micName + "\n\n" +
                Localization.T("This PC has no speaker, so Tempo's captions can't hear system audio right now. " +
                               "Use this microphone for captions instead?"),
                "Tempo — " + Localization.T("microphone detected"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes)
            {
                Utils.Logger.Info("[Audio] user declined the connected microphone.");
                return;
            }

            _settings.CaptionCaptureMode = 2;           // Microphone
            SyncCaptureModeCombo();
            try { Persistence.SettingsManager.Save(_settings); } catch { }
            Utils.Logger.Info("[Captions] user accepted the connected microphone (" + micName + ").");
            StopTempoCaptions();
            StartTempoCaptions();
        }

        /// <summary>
        /// A speaker appeared while the user had explicitly set captions to the
        /// MICROPHONE (usually because there was no speaker before). Offer the
        /// switch back to system audio — their choice, Yes/No, once per arrival.
        /// </summary>
        private void MaybeOfferSpeaker(string previous, string name)
        {
            if (name == null)
            {
                _speakerOfferArmed = true;
                return;
            }
            if (previous != null || !_speakerOfferArmed || !_captionsActive ||
                _settings == null || _settings.CaptionSource == 0 ||
                _settings.CaptionCaptureMode != 2)      // only when explicitly on mic
            {
                return;
            }
            _speakerOfferArmed = false;
            Utils.Logger.Info("[Audio] speaker connected (" + name + ") while captions use the mic — asking the user.");

            DialogResult r = MessageBox.Show(this,
                Localization.T("A speaker was just connected:") + "\n\n    🔊 " + name + "\n\n" +
                Localization.T("Captions are currently using the microphone. Switch to system audio, " +
                               "so videos and games get captioned directly?"),
                "Tempo — " + Localization.T("speaker detected"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes)
            {
                Utils.Logger.Info("[Audio] user declined switching to the connected speaker.");
                return;
            }

            _settings.CaptionCaptureMode = 0;           // Auto (prefers system audio)
            SyncCaptureModeCombo();
            try { Persistence.SettingsManager.Save(_settings); } catch { }
            Utils.Logger.Info("[Captions] user accepted the connected speaker (" + name + ").");
            StopTempoCaptions();
            StartTempoCaptions();
        }

        // Error notification: the user should KNOW when something went wrong, not
        // find out days later in a log. One tray balloon per minute at most, so an
        // error storm can't turn the tray into a strobe light.
        private long _lastErrorBalloonTick;

        private void OnLoggerLineForNotify(string line)
        {
            try
            {
                if (line == null || line.IndexOf("[ERROR]", StringComparison.Ordinal) < 0)
                {
                    return;
                }
                long now = Environment.TickCount64;
                if (now - _lastErrorBalloonTick < 60000)
                {
                    return;
                }
                _lastErrorBalloonTick = now;

                if (!IsHandleCreated || IsDisposed) { return; }
                BeginInvoke((Action)(() =>
                {
                    try
                    {
                        if (_trayIcon == null || _settings == null || !_settings.ShowTrayNotifications)
                        {
                            return;
                        }
                        int cut = line.IndexOf("] ", StringComparison.Ordinal);
                        string detail = cut > 0 && cut + 2 < line.Length ? line.Substring(cut + 2) : line;
                        if (detail.Length > 120) { detail = detail.Substring(0, 120) + "…"; }
                        TempoNotify(6000, "Tempo — " + Localization.T("something went wrong"),
                            detail + "\n" + Localization.T("Details: Settings → Data & Backup → Live debug."),
                            ToolTipIcon.Warning, always: true);
                    }
                    catch { }
                }));
            }
            catch { }
        }

        /// <summary>Opens (or re-focuses) the Live Debug window.</summary>
        private void OpenLiveDebug()
        {
            try
            {
                if (_debugForm != null && !_debugForm.IsDisposed)
                {
                    _debugForm.Activate();
                    return;
                }
                _debugForm = new DebugForm(_theme, BuildDebugStats);
                _debugForm.Show(this);
            }
            catch (Exception ex)
            {
                Utils.Logger.Warn("Live debug window failed: " + ex.Message);
            }
        }

        /// <summary>
        /// One readable snapshot of the caption stack for the Live Debug header.
        /// Every line is best-effort — a failing subsystem must not hide the rest.
        /// </summary>
        /// <summary>
        /// Collects every CURRENT problem condition Tempo can detect into plain-English
        /// lines, so the Live Debug panel answers "is anything wrong right now?" at a
        /// glance instead of making the user read the whole event log. Each returned
        /// line is prefixed with ⚠ (a warning state) or ✗ (a hard error) so the panel
        /// colours it. The list is empty when everything is healthy.
        /// </summary>
        private System.Collections.Generic.List<string> DetectIssues()
        {
            var issues = new System.Collections.Generic.List<string>();
            try
            {
                // The most recent logged error — the single most useful "what broke".
                if (Utils.Logger.ErrorCount > 0 && !string.IsNullOrEmpty(Utils.Logger.LastError))
                {
                    double ago = (DateTime.UtcNow - Utils.Logger.LastErrorUtc).TotalSeconds;
                    string when = ago < 0 ? "" : "  (" + FormatAgo(ago) + " ago)";
                    string msg = Utils.Logger.LastError;
                    if (msg.Length > 140) { msg = msg.Substring(0, 138) + "…"; }
                    issues.Add("✗ Last error" + when + ": " + msg);
                }

                // The GPU engine was asked for but this PC can't provide one. Reported
                // ahead of the restart hint below, which would otherwise tell the user to
                // restart over and over for a GPU that is never going to appear.
                string gpuWhyNot = Utils.TempoTranscriber.GpuUnavailableReason;
                if (gpuWhyNot != null && _settings != null && _settings.CaptionTryGpu)
                {
                    issues.Add("⚠ GPU captions are on, but this PC can't run them: " + gpuWhyNot +
                               ". Captions are using the CPU engine — turn the setting off to stop the warning.");
                }

                // Captions: GPU choice can't take effect until restart.
                if (gpuWhyNot == null && GpuSettingNeedsRestart())
                {
                    issues.Add("⚠ GPU captions setting is " + (_settings.CaptionTryGpu ? "ON" : "OFF") +
                               " but this run is on the " +
                               (Utils.TempoTranscriber.RuntimeGpuRequested ? "GPU" : "CPU") +
                               " engine — restart Tempo to apply it.");
                }
                if (_gpuTooSlowThisSession)
                {
                    issues.Add("⚠ The GPU engine couldn't keep up this session — captions fell back to Windows. " +
                               "Restart to try the GPU again.");
                }
                if (_captionModelOverrideKey != null)
                {
                    issues.Add("⚠ Speech model was auto-downgraded to '" + _captionModelOverrideKey +
                               "' to keep pace. " + (_modelRecoveryBlocked
                                   ? "The bigger model was re-tried and still couldn't keep up — staying here this session."
                                   : "Tempo steps back up by itself once your PC shows sustained headroom."));
                }
                var tr = _captionTranscriber;
                if (tr != null && tr.IsRunning && tr.BacklogDroppedSeconds > 0.5)
                {
                    issues.Add("⚠ Captions are behind live audio — dropped " +
                               tr.BacklogDroppedSeconds.ToString("0.0") + " s. A smaller model, or the GPU, would keep up.");
                }

                // The three silent failures. Each was already KNOWN to the engine and
                // reached no health list, so captions could be stone dead while every
                // panel reported a healthy running engine.
                if (tr != null && tr.CaptureLost)
                {
                    // IsRunning deliberately stays true when the device vanishes with no
                    // replacement, so nothing else in this panel would ever say so.
                    issues.Add("⚠ The audio device captions were using is gone and no replacement was found — " +
                               "the engine is still running but hearing nothing. Reconnect it, or pick another " +
                               "device in Settings → Live Captions.");
                }
                else if (tr != null && tr.IsRunning && tr.SecondsSinceLastCaption >= 45)
                {
                    // Cheapest possible detector for "captions went quiet": a wrong device,
                    // a muted app, or an engine that stopped producing all look identical
                    // to a quiet room without it.
                    issues.Add("⚠ Captions have produced nothing for " +
                               ((int)tr.SecondsSinceLastCaption) + " s. If audio IS playing, check the " +
                               "speaker/mic pick and the input level on the Captions tab.");
                }
                // Several apps audible at once. Loopback hands the engine ONE mixed
                // stream, so a game plus voice chat plus music is transcribed as a single
                // speaker — which is why the text comes out spliced and half-sensible.
                // Nothing said so; the bar just named whichever app was loudest.
                if (tr != null && tr.IsRunning && AudioSourcesAreMixed(out int mixedApps, out int _))
                {
                    issues.Add("⚠ " + mixedApps + " apps are playing audio at once and none is clearly " +
                               "loudest, so captions are transcribing all of them mixed together. Mute or lower " +
                               "the ones you don't need captioned — or switch the caption source to a microphone.");
                }

                if (tr != null && tr.IsRunning && tr.InputClipping)
                {
                    // Deliberately actionable: the fix is upstream, and it is the OPPOSITE
                    // of the advice the pace warning above gives.
                    issues.Add("⚠ Caption audio is clipping — it transcribes badly. Turn the app's or " +
                               "Windows' volume down; a smaller model will not help this.");
                }

                // Hotkeys Windows refused to reserve (hook-driven fallback).
                if (_hotkeys != null && _settings != null && _settings.Bindings != null)
                {
                    var hooked = new System.Collections.Generic.List<string>();
                    foreach (var b in _settings.Bindings)
                    {
                        if (b?.Hotkey == null || !b.Hotkey.IsValid || b.Hotkey.IsMouse) { continue; }
                        if (_hotkeys.RouteOf(b.Action.ToString()) ==
                            Native.GlobalHotkeyManager.BindRoute.HookFallback)
                        {
                            hooked.Add(b.Hotkey.ToDisplayString());
                        }
                    }
                    if (hooked.Count > 0)
                    {
                        issues.Add("⚠ Windows wouldn't reserve " + string.Join(", ", hooked.ToArray()) +
                                   " (another app owns it); Tempo catches it with a hook, but it also still works in that app.");
                    }
                }

                // Windows startup blocked in Task Manager while the setting is on.
                if (_settings != null && _settings.LaunchAtStartup &&
                    Utils.StartupManager.IsDisabledByTaskManager())
                {
                    issues.Add("⚠ 'Launch at sign-in' is on, but Windows has it disabled in Task Manager → Startup apps. " +
                               "Re-enable it there.");
                }

                // Single-file bundle didn't deliver its native libraries — the app
                // looks fine but Live Captions can never start. Name it explicitly.
                if (Utils.SelfCheck.NativesMissing)
                {
                    issues.Add("✗ " + Utils.SelfCheck.Summary);
                }

                // Tempo.exe is not the file that was installed. Health, not just a
                // toast: the card is shown once and can be dismissed or missed
                // entirely on a tray start, and "is my copy of this still the real
                // one" should be answerable at any time rather than only in the
                // second it was first noticed.
                if (Utils.IntegrityCheck.IsProblem)
                {
                    // Raw Summary, like SelfCheck above it: the Health panel is a
                    // diagnostic surface whose lines are English by convention.
                    issues.Add("✗ " + Utils.IntegrityCheck.Summary);
                }

                // ── Window / rendering faults ───────────────────────────────
                // These three were all real, shipped bugs that nothing surfaced: the
                // user had to notice a clipped card or count scroll frames by eye.
                if (IsHandleCreated && WindowState == FormWindowState.Normal)
                {
                    Rectangle wa = Screen.FromControl(this).WorkingArea;
                    if (Width > wa.Width + 2 || Height > wa.Height + 2)
                    {
                        issues.Add("⚠ The window is bigger than the screen's work area (" +
                                   Width + "×" + Height + " vs " + wa.Width + "×" + wa.Height +
                                   ") — its bottom edge sits under the taskbar, so the status strip and the " +
                                   "last card on a page are cut off. Settings → Window & Display → " +
                                   "\"Reset window position\" restores it.");
                    }
                }
                if (_compositedOn && _fullBgImage == null)
                {
                    issues.Add("⚠ Whole-window compositing is on with no background image — scrolling will " +
                               "crawl (it costs a full-window repaint per scroll step). It should only be on " +
                               "while a wallpaper is showing.");
                }
                // "Borderless" only implies full screen on the SYSTEM title bar. With
                // Tempo's own title bar the form is borderless at all times, so this test
                // reported every normal window as broken and told the user to press F11
                // twice to fix a window that was fine.
                if (!_customChrome && IsHandleCreated
                    && _isFullScreen != (FormBorderStyle == FormBorderStyle.None))
                {
                    issues.Add("⚠ Full-screen state is inconsistent (flag " + (_isFullScreen ? "ON" : "off") +
                               ", border " + FormBorderStyle + ") — press F11 twice to resync the window.");
                }

                // Camera movement armed globally (swallows W/A/S/D everywhere).
                var mv = _movement;
                if (mv != null && mv.IsRunning && mv.TargetWindow == IntPtr.Zero)
                {
                    issues.Add("⚠ Camera movement is armed for EVERY window — W/A/S/D are captured globally. " +
                               "Arm it with the hotkey while a game is focused instead.");
                }

                // Anti-freeze actively throttling the requested rate.
                if (_engine != null && _engine.IsRunning && _engine.IsThrottling)
                {
                    issues.Add("⚠ Anti-freeze is throttling the click rate (CPU " +
                               _engine.MeasuredCpuPercent.ToString("0") + "%). Real rate ≈ " +
                               _engine.EffectiveClicksPerSecond.ToString("0.0") + " CPS.");
                }

                // Game mode: not a fault, but the answer to "why are captions
                // suddenly a beat slower?" — say it where people look.
                if (_gamePresence != null && _gamePresence.ExclusiveFullscreen)
                {
                    issues.Add("⚠ The game runs in EXCLUSIVE fullscreen — it owns the whole display and NO app " +
                               "can draw captions over it (overlay tools that do inject into the game, which " +
                               "Tempo will never risk with anti-cheat). Fix: in the game's video settings choose " +
                               "'Borderless' / 'Windowed fullscreen' — it looks identical and captions appear.");
                }
                else if (_gamePresence != null && _gamePresence.FullscreenActive)
                {
                    issues.Add("🎮 A fullscreen app owns the screen — captions are in low-impact game mode " +
                               "(relaxed pace, beam off, 1 fps face analysis) to protect its frame rate. " +
                               "Full quality returns when it closes.");
                }

                // The user picked a specific audio device and it's gone.
                if (_settings != null && _audioWatcher != null)
                {
                    if (_settings.CaptionSpeakerDeviceId.Length > 0 &&
                        !DeviceIdPresent(_audioWatcher.Speakers, _settings.CaptionSpeakerDeviceId))
                    {
                        issues.Add("⚠ The chosen caption speaker isn't connected — using the Windows default. " +
                                   "Re-pick it under Settings → Live Captions.");
                    }
                    if (_settings.CaptionMicDeviceId.Length > 0 &&
                        !DeviceIdPresent(_audioWatcher.Microphones, _settings.CaptionMicDeviceId))
                    {
                        issues.Add("⚠ The chosen caption microphone isn't connected — using the Windows default. " +
                                   "Re-pick it under Settings → Live Captions.");
                    }
                }
            }
            catch (Exception ex)
            {
                issues.Add("✗ (issue-scan failed: " + ex.Message + ")");
            }
            return issues;
        }

        /// <summary>"12 s" / "3 min" / "2 h" for the health readout.</summary>
        private static string FormatAgo(double seconds)
        {
            if (seconds < 90) { return ((int)seconds) + " s"; }
            if (seconds < 5400) { return ((int)(seconds / 60)) + " min"; }
            return ((int)(seconds / 3600)) + " h";
        }

        /// <summary>First 12 hex characters of a hash — enough to compare by eye.</summary>
        private static string Short12(string hash)
        {
            if (string.IsNullOrEmpty(hash)) { return "?"; }
            return hash.Length <= 12 ? hash : hash.Substring(0, 12) + "…";
        }

        /// <summary>"#RRGGBB" for a colour, for the Live Debug theme line.</summary>
        private static string HexOf(Color c)
        {
            return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        private string BuildDebugStats()
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                // ── Health first: is anything wrong RIGHT NOW? ──────────────────
                var issues = DetectIssues();
                if (issues.Count == 0)
                {
                    sb.Append("Health: ✓ no issues detected");
                    if (Utils.Logger.WarnCount > 0)
                    {
                        sb.Append("  ·  ").Append(Utils.Logger.WarnCount).Append(" warning(s) this session");
                    }
                    sb.AppendLine();
                    // Even with no hard issue, name the most recent warning — it's usually
                    // the thing the user opened Live Debug to understand.
                    if (Utils.Logger.WarnCount > 0 && !string.IsNullOrEmpty(Utils.Logger.LastWarn))
                    {
                        double ago = (DateTime.UtcNow - Utils.Logger.LastWarnUtc).TotalSeconds;
                        string msg = Utils.Logger.LastWarn;
                        if (msg.Length > 130) { msg = msg.Substring(0, 128) + "…"; }
                        sb.Append("⚠ Last warning (").Append(FormatAgo(ago)).Append(" ago): ").Append(msg).AppendLine();
                    }
                }
                else
                {
                    sb.Append("Health: ⚠ ").Append(issues.Count).Append(issues.Count == 1 ? " issue" : " issues")
                      .Append("  ·  ").Append(Utils.Logger.WarnCount).Append(" warn / ")
                      .Append(Utils.Logger.ErrorCount).Append(" error this session").AppendLine();
                    foreach (string line in issues) { sb.AppendLine(line); }
                }
                sb.AppendLine();

                var t = _captionTranscriber;
                // IsRunning only. Anything that should also report during the model load
                // must test engineLive instead — see the caption engine block below,
                // where gating the "still loading" line on engineOn made it unreachable
                // during precisely the window it exists to describe.
                bool engineOn = t != null && t.IsRunning;
                bool engineLive = t != null && (t.IsRunning || t.IsStarting);
                sb.Append("Captions: ").Append(_captionsActive ? "ON" : "off");
                if (_captionsActive)
                {
                    sb.Append(_captionFellBackToWindows ? " · mirroring Windows LC" : " · Tempo engine");
                    if (engineOn)
                    {
                        sb.Append(" · model: ").Append(_captionModelActiveKey ?? "?");
                        string rt = t.RuntimeDescription;
                        if (rt != null) { sb.Append(" · ").Append(rt == "Vulkan" ? "GPU (Vulkan)" : rt); }
                    }
                }
                sb.AppendLine();

                // Self-click guard: how many of Tempo's own clicks were stopped from
                // operating Tempo's own UI. A non-zero count means a run was clicking
                // over this window — worth knowing, and previously invisible.
                if (_selfClickGuard != null && _selfClickGuard.BlockedCount > 0)
                {
                    sb.Append("  Self-input guard: ").Append(_selfClickGuard.BlockedCount)
                      .Append(" of Tempo's own clicks/keys ignored on its own UI (last ")
                      .Append((int)(DateTime.UtcNow - _selfClickGuard.LastBlockedUtc).TotalSeconds)
                      .Append("s ago)").AppendLine();
                }

                // Caption TIMING. "The video says something, then the caption turns up
                // late" is the single most common caption complaint, and every number
                // that explains it was already being tracked and never shown. Read it as:
                // pace over 1.00 means the model decodes slower than audio arrives, so
                // the backlog — and therefore the delay — grows without limit until
                // audio starts being dropped.
                // What the engine is actually doing, as opposed to what was requested.
                // ActiveMode/IsStarting/LastConfidence had NO reader anywhere in the app:
                // "Captions: ON" with a blank engine block was all a 20 s model load
                // looked like, and an Auto source that resolved to the microphone looked
                // exactly like one that resolved to system audio.
                if (engineLive)
                {
                    // A model quarantined after Tempo crashed while loading it is
                    // silently replaced. Without this the panel shows a model the user
                    // never chose and nothing explains why.
                    try
                    {
                        string wantKey = _settings != null ? _settings.CaptionModelKey : null;
                        if (!string.IsNullOrEmpty(wantKey) &&
                            !string.Equals(wantKey, _captionModelActiveKey, StringComparison.OrdinalIgnoreCase))
                        {
                            string wantFile = Utils.WhisperModelManager.ResolveInstalledPath(wantKey);
                            if (wantFile != null && Utils.CaptionCrashGuard.IsQuarantined(wantFile))
                            {
                                sb.Append("  ⚠ '").Append(wantKey)
                                  .Append("' is quarantined (Tempo crashed loading it before) — running '")
                                  .Append(_captionModelActiveKey ?? "?").Append("' instead").AppendLine();
                            }
                        }
                    }
                    catch { }

                    if (t.IsStarting && !t.IsRunning)
                    {
                        sb.Append("  Caption engine: loading the speech model (no audio is being transcribed yet)")
                          .AppendLine();
                    }
                    if (t.IsRunning || t.IsStarting)
                    {
                        sb.Append("  Listening to: ")
                          .Append(t.ActiveMode == Utils.CaptureMode.Microphone ? "microphone" : "system audio");
                        if (_settings != null
                            && _settings.CaptionCaptureMode == (int)Utils.CaptureMode.SystemAudio
                            && t.ActiveMode == Utils.CaptureMode.Microphone)
                        {
                            sb.Append("  ⚠ asked for system audio — no speaker found, fell back");
                        }
                        if (t.CaptureLost) { sb.Append("  ⚠ DEVICE LOST — hearing nothing"); }
                        double conf = t.LastConfidence;
                        if (conf >= 0)
                        {
                            sb.Append("   · confidence ").Append(Math.Round(conf * 100)).Append('%');
                            if (conf < 0.45) { sb.Append(" (low)"); }
                        }
                        sb.AppendLine();
                    }
                }

                if (engineOn && t != null && t.IsRunning)
                {
                    double rtf = t.RealTimeFactor;
                    sb.Append("  Caption timing: delay ~")
                      .Append(t.EstimatedDelaySeconds.ToString("0.0")).Append("s")
                      .Append("  (window ").Append(TempoTranscriber.WindowSizeSeconds.ToString("0.0"))
                      .Append("s + backlog ").Append(t.BacklogSeconds.ToString("0.0"))
                      .Append("s + decode ").Append(t.AverageInferenceMs).Append("ms)")
                      .AppendLine();
                    sb.Append("    pace ")
                      .Append(rtf <= 0 ? "—" : rtf.ToString("0.00") + "×")
                      .Append(rtf <= 0 ? "" : (rtf < 1.0 ? " (keeping up)" : " ⚠ SLOWER THAN REAL TIME"))
                      .Append("   · chunk ").Append(t.LastChunkMs).Append("ms")
                      .Append(" · last decode ").Append(t.LastInferenceMs).Append("ms");
                    if (t.CatchUpTakes > 0) { sb.Append(" · catch-up takes ").Append(t.CatchUpTakes); }
                    if (t.BacklogDroppedSeconds > 0.05)
                    {
                        sb.Append(" · ⚠ dropped ").Append(t.BacklogDroppedSeconds.ToString("0.0")).Append("s of audio");
                    }
                    sb.AppendLine();
                    if (rtf >= 1.0)
                    {
                        sb.Append("    → '").Append(_captionModelActiveKey ?? "?")
                          .Append("' cannot hold pace on this PC. A smaller model (small/base) or the GPU engine fixes the drift.")
                          .AppendLine();
                    }
                }

                // Caption overlays: where they actually ARE and whether that is on a
                // screen. "I turned captions on and nothing appeared" is almost always
                // one of these two windows sitting at a restored position that no
                // display covers any more — invisible, while everything reports fine.
                sb.Append("  Caption bar: ").Append(CaptionWindowState(_captionOverlay))
                  .Append("   · History: ").Append(CaptionWindowState(_captionHistoryForm))
                  .Append(" (").Append(_captionHistory != null ? _captionHistory.Count : 0)
                  .Append(" lines)")
                  .AppendLine();

                // Speaker volume/mute — loopback is captured AFTER the volume slider, so
                // a muted or near-silent speaker is a complete explanation for an empty
                // caption bar that nothing else in this panel would reveal.
                if (engineOn && t.SystemVolume >= 0)
                {
                    sb.Append("  System audio: speaker ")
                      .Append(Math.Round(t.SystemVolume * 100)).Append('%');
                    if (t.SystemMuted) { sb.Append("  ⚠ MUTED — captures silence"); }
                    else if (t.SystemVolume < 0.08) { sb.Append("  ⚠ too faint to caption well"); }
                    sb.AppendLine();
                }

                // Auto-start: why captions did or didn't come on by themselves.
                sb.Append("  Auto-start: ")
                  .Append(_settings != null && _settings.CaptionAutoStart ? "on" : "off")
                  .Append(_mediaAutoArmed ? " · armed" : " · suppressed by your last 'off'");
                if (!_mediaAutoArmed && _autoStartOffSource.Length > 0)
                {
                    sb.Append(" (").Append(_autoStartOffSource).Append(')');
                }
                if (_mediaDetector != null)
                {
                    string playing = _mediaDetector.CurrentAudioSource;
                    sb.Append("  · playing now: ")
                      .Append(string.IsNullOrEmpty(playing) ? "nothing" : playing);

                    // Loopback captures the MIX, so when several apps are audible the
                    // engine is transcribing all of them at once — a game, voice chat and
                    // music spliced into one stream. Naming only the loudest made that
                    // invisible, and it is the direct explanation for captions that read
                    // like two conversations interleaved.
                    int apps = _mediaDetector.AudibleAppCount;
                    if (apps > 1)
                    {
                        sb.Append("   ⚠ ").Append(apps).Append(" apps audible at once");
                        if (AudioSourcesAreMixed(out int _, out int domPct))
                        {
                            sb.Append(" — none clearly loudest (dominance ").Append(domPct)
                              .Append("%), so captions are mixing them");
                        }
                    }
                }
                sb.AppendLine();

                // Window painting — the setting that decides whether Tempo idles at a
                // few percent of a CPU core or at a whole one.
                //
                // WS_EX_COMPOSITED redraws the entire window, bottom-up, for any repaint
                // anywhere in it. Left on permanently it measured ~108% of one core at
                // idle against ~12% with it off, and a saturated UI thread is what makes
                // dragging and every animation stutter. It is now armed only while a
                // page is actually being scrolled. Reporting it here means the next
                // "everything feels laggy" is answerable by LOOKING instead of profiling.
                sb.Append("  Window paint: compositing ")
                  .Append(_compositedOn ? "ON" : "off")
                  .Append(_compositedOn ? " (scrolling — releases shortly after)" : "")
                  .Append("  · wallpaper ").Append(_wallpaperShowing ? "on" : "off")
                  .Append("  · backdrop surfaces ")
                  .Append(_fullBgImage != null ? "header+sidebar+page+footer" : "none");
                if (_inMoveLoop) { sb.Append("  · dragging (animation paused)"); }
                sb.AppendLine();

                // What the GPU engine can do on THIS machine, whether or not it is in
                // use — so "why isn't the GPU option working" is answerable without
                // enabling it, restarting, and guessing from the outcome.
                sb.Append("  GPU engine: ").Append(Utils.VulkanProbe.Summary);
                if (Utils.TempoTranscriber.GpuUnavailableReason != null)
                {
                    sb.Append("  ⚠ requested but declined this run");
                }
                if (Utils.TempoTranscriber.GpuWouldHelp)
                {
                    sb.Append("  ⚠ the CPU engine had to give up quality here — this GPU could run it far faster");
                }
                sb.AppendLine();

                if (engineOn)
                {
                    sb.Append("Engine: last chunk ").Append(t.LastChunkMs).Append(" ms heard in ")
                      .Append(t.LastInferenceMs).Append(" ms (avg ").Append(t.AverageInferenceMs)
                      .Append(") · backlog ")
                      .Append(t.BacklogSeconds.ToString("0.0")).Append(" s · language ")
                      .Append(t.LanguageState).AppendLine();
                    // The keep-up verdict in one line: pace under 1.00× keeps up,
                    // over it falls behind — with the honest label either way.
                    double rtf = t.RealTimeFactor;
                    sb.Append("Keep-up: ");
                    if (rtf <= 0)
                    {
                        sb.Append("measuring…");
                    }
                    else
                    {
                        sb.Append("pace ").Append(rtf.ToString("0.00")).Append("× RT · ")
                          .Append(rtf < 0.75 ? "keeping up easily"
                                : rtf < 0.95 ? "keeping up"
                                : rtf <= 1.05 ? "marginal — adapting"
                                : "⚠ FALLING BEHIND — stepping down");
                    }
                    if (t.CatchUpTakes > 0)
                    {
                        sb.Append(" · ").Append(t.CatchUpTakes).Append(" catch-up takes");
                    }
                    sb.AppendLine();
                    sb.Append("Decode: beam ").Append(t.BeamActive ? "ON" : "off")
                      .Append(" · threads ").Append(t.ThreadsActive)
                      .Append(t.HybridThreadsActive ? " (P-cores)" : "")
                      .Append(" · ctx carry ").Append(t.CarryContextSeconds.ToString("0.00")).Append(" s")
                      .Append(" · cadence ").Append(t.CadenceTier)
                      .Append(" · keep-alive ").Append(Utils.LoopbackKeepAlive.IsActive ? "playing" : "off")
                      .AppendLine();
                    sb.Append("Audio: ").Append(LevelBar(t.LevelDb)).Append(' ')
                      .Append(t.InputClipping ? "⚠ CLIPPING · " : "")
                      .Append(t.SurroundMixActive ? "5.1 dialogue mix · " : "")
                      .Append(t.LevelDb).Append(" dB · gain ")
                      .Append(t.AppliedGain.ToString("0.0")).Append("× · ")
                      .Append(t.SourceFormatDescription ?? "?")
                      .Append(" · buf ").Append(t.CaptureBufferMs).Append(" ms").AppendLine();
                    // (GPU-restart and model-downgrade warnings now live in the Health
                    // section at the top, so they're not duplicated here.)
                    sb.Append("Chunks: ").Append(t.ChunksProcessed).Append(" done · ")
                      .Append(t.SilentChunksSkipped).Append(" silent")
                      // Near-misses are the ones worth knowing about: skipped, but
                      // only just — the audio was almost loud enough to caption.
                      .Append(t.NearMissSkips > 0 ? " (" + t.NearMissSkips + " only just under the gate)" : "")
                      .Append(" · ")
                      .Append(t.CaptionsEmitted).Append(" captions · ")
                      .Append(t.EarlyTakes).Append(" quick takes");
                    if (t.BacklogDroppedSeconds > 0)
                    {
                        sb.Append(" · DROPPED ").Append(t.BacklogDroppedSeconds.ToString("0.0")).Append(" s");
                    }
                    double ago = t.SecondsSinceLastCaption;
                    sb.Append(" · last words ").Append(ago < 0 ? "—" : ago.ToString("0") + " s ago")
                      .AppendLine();
                    // The bar itself: word-by-word reveal progress and the rolling
                    // line's size — "reveal 38/45" mid-count means words are still
                    // animating in; a line hovering near 360 chars sheds its oldest
                    // sentence on the next emission.
                    if (_captionOverlay != null && !_captionOverlay.IsDisposed && _captionOverlay.Visible)
                    {
                        sb.Append("Bar: reveal ").Append(_captionOverlay.RevealShownWords)
                          .Append('/').Append(_captionOverlay.RevealTotalWords).Append(" words")
                          .Append(" · pace ").Append(_captionOverlay.RevealPaceMs).Append(" ms/word")
                          .Append(" · rolling line ").Append(_tempoRollingLine.Length).Append(" chars")
                          .AppendLine();
                    }
                    // Own-voice filter status — proves the mic monitor is live and
                    // shows how close the last chunk came to "that's the user".
                    var ovg = _selfVoiceGuard;
                    if (t.OwnVoiceGuard != null && ovg != null && ovg.Running)
                    {
                        sb.Append("Own-voice filter: on (").Append(ovg.DeviceName)
                          .Append(") · last similarity ").Append(t.LastOwnVoiceSimilarity.ToString("0.00"))
                          .Append(" · skipped ").Append(t.OwnVoiceSkippedChunks).Append(" chunks").AppendLine();
                    }
                    else if (_settings != null && _settings.CaptionFilterOwnVoice)
                    {
                        sb.Append("Own-voice filter: wanted but inactive (no microphone?)").AppendLine();
                    }
                }

                if (_audioWatcher != null)
                {
                    bool spkChosen = _settings != null && _settings.CaptionSpeakerDeviceId.Length > 0;
                    bool micChosen = _settings != null && _settings.CaptionMicDeviceId.Length > 0;
                    sb.Append("Devices: 🔊 ").Append(_audioWatcher.SpeakerName ?? "none")
                      .Append(spkChosen ? " [chosen]" : " [default]")
                      .Append(" · 🎙 ").Append(_audioWatcher.MicrophoneName ?? "none")
                      .Append(micChosen ? " [chosen]" : " [default]").AppendLine();
                }

                if (_gamePresence != null && _gamePresence.FullscreenActive)
                {
                    sb.AppendLine(_gamePresence.ExclusiveFullscreen
                        ? "Game mode: 🎮 EXCLUSIVE fullscreen — caption bar cannot be shown (switch game to Borderless)"
                        : "Game mode: 🎮 fullscreen app on screen — low-impact captions active");
                }

                string src = _mediaDetector != null ? _mediaDetector.CurrentAudioSource : "";
                sb.Append("Audio source: ").Append(string.IsNullOrEmpty(src) ? "(quiet)" : src);
                int voice = _voiceProfiler != null ? _voiceProfiler.CurrentSpeaker : 0;
                int face = _faceAnalyzer != null ? _faceAnalyzer.CurrentVisualSpeaker : 0;
                int faces = _faceAnalyzer != null ? _faceAnalyzer.FaceCount : 0;
                sb.Append(" · speaker: voice ").Append(voice).Append(" / face ").Append(face)
                  .Append(" (").Append(faces).Append(" faces)");
                if (_faceAnalyzer != null && _faceAnalyzer.Running && _faceAnalyzer.CrossTalk)
                {
                    sb.Append(" · CROSSTALK — ").Append(_faceAnalyzer.TalkingFaceCount)
                      .Append(" faces talking at once (label held)");
                }
                sb.AppendLine();
                // Per-face detail: where each tracked face is, its size, live mouth
                // motion, and whether it's coasting through a head turn — so you can
                // see exactly why the label chose (or didn't choose) a face.
                if (_faceAnalyzer != null && _faceAnalyzer.Running)
                {
                    // How often the OS face detector was skipped because the picture had
                    // not changed at all. On a paused video or a static screen this is
                    // where the analyzer's cost goes to almost nothing.
                    // "unchanged or blank", not "frozen frames": the same counter now
                    // also covers frames with no picture at all, and those are a
                    // different situation with a different answer.
                    sb.Append("  Face detector skipped (unchanged or blank): ")
                      .Append(_faceAnalyzer.FramesSkipped)
                      .Append("  · scene cuts ").Append(_faceAnalyzer.SceneCutCount)
                      .Append("  · frame motion ").Append(_faceAnalyzer.GlobalMotion.ToString("0.0"))
                      .AppendLine();
                    // Say the blank case out loud. Without this it reads as "0 faces,
                    // forever" and looks like the feature is broken, when in fact there
                    // is no picture to analyse and nothing the user can change.
                    if (_faceAnalyzer.WindowIsBlank)
                    {
                        sb.Append("  ⚠ the watched window returns a BLANK image — protected video, ")
                          .Append("or a capture Windows won't share. Face analysis is idling; captions are unaffected.")
                          .AppendLine();
                    }
                    string fd = _faceAnalyzer.DebugDetail();
                    if (!string.IsNullOrEmpty(fd)) { sb.Append(fd); }
                }
                // Per-voice detail: each learned voice's fingerprint (pitch,
                // brightness, intonation) and evidence — why "Speaker N" is who it is.
                if (_voiceProfiler != null && _voiceProfiler.Running)
                {
                    string vd = _voiceProfiler.DebugDetail();
                    if (!string.IsNullOrEmpty(vd)) { sb.Append(vd); }
                }
                // The AI word fixer's pulse: proof it's running, how often it acts,
                // and its most recent repair — the fastest way to spot both a dead
                // fixer (0 checked) and an over-eager one (last fix looks wrong).
                if (_wordFixer != null && _wordFixer.Available)
                {
                    sb.Append("AI fixer: ").Append(_wordFixer.WordsChecked).Append(" words checked · ")
                      .Append(_wordFixer.WordsFixed).Append(" repaired");
                    string lastFix = _wordFixer.LastFix;
                    if (lastFix != null)
                    {
                        sb.Append(" · last “").Append(lastFix).Append('”');
                    }
                    sb.Append(" · cache ").Append(_wordFixer.CacheSize).AppendLine();
                }

                // ── Camera-relative movement ────────────────────────────────
                var mv = _movement;
                if (mv != null && mv.IsRunning)
                {
                    sb.Append("Movement: ARMED · ")
                      .Append(mv.Frame == Engine.MovementFrame.WorldLocked
                          ? "world-locked" : "camera pass-through")
                      .Append(" · ").Append(WindowTitleOf(mv.TargetWindow))
                      .Append(mv.TargetIsForeground ? " [ACTING]" : " [background — keys pass through]")
                      .AppendLine();
                    sb.Append("  camera ≈ ").Append(mv.EstimatedYawDegrees.ToString("0.0"))
                      .Append("° · heading ").Append(mv.CommandedHeadingDegrees.ToString("0.0"))
                      .Append("° · mouse ").Append(mv.MouseCountsPerSecond).Append(" counts/s")
                      .Append(" · pad ").Append(mv.GamepadConnected ? "yes" : "no").AppendLine();
                    sb.Append("  you press ").Append(mv.PhysicalKeys)
                      .Append("  →  Tempo sends ").Append(mv.HeldKeys)
                      .Append(" · ").Append(_settings != null
                          ? _settings.MovementDegreesPerCount.ToString("0.####") : "?")
                      .Append(" °/count").AppendLine();
                    if (_settings != null && Math.Abs(_settings.MovementDegreesPerCount - 0.06) < 1e-9)
                    {
                        sb.Append("⚠ Camera sensitivity is still the default guess — ")
                          .Append("run Settings → Calibrate, or the heading will be wrong.").AppendLine();
                    }
                }
                else
                {
                    sb.Append("Movement: off").AppendLine();
                }

                // ── Second cursor ("second mouse") ──────────────────────────
                if (_secondCursor != null && _secondCursor.Enabled)
                {
                    sb.Append("2nd cursor: ON @ (").Append(_secondCursor.X).Append(", ").Append(_secondCursor.Y).Append(")")
                      .Append(_secondCursor.Placing ? " · PLACING (click to drop)" : "");
                    if (_secondCursor.Spamming)
                    {
                        // A paused spam and a running one look identical from outside — the
                        // clicks were never visible to begin with — so the reason has to be
                        // stated here or the user is left guessing why nothing is happening.
                        string paused = _secondCursor.SpamPausedReason;
                        sb.Append(paused.Length > 0 ? " · SPAM PAUSED — " + paused : " · SPAM-CLICKING");
                    }
                    else
                    {
                        sb.Append(" · idle");
                    }
                    sb.AppendLine();
                    sb.Append("  target under it: ").Append(_secondCursor.DescribeTarget()).AppendLine();
                    string aimed = _secondCursor.SpamAimedAt;
                    if (aimed.Length > 0)
                    {
                        sb.Append("  aimed at: ").Append(aimed).AppendLine();
                    }
                    // ── physical second mouse ──
                    sb.Append("  mice detected: ").Append(Engine.SecondCursorController.DetectedMouseSummary()).AppendLine();
                    if (!_secondCursor.UsePhysicalMouse)
                    {
                        sb.Append("  2nd-mouse mode: off").AppendLine();
                    }
                    else if (_secondCursor.Assigning)
                    {
                        sb.Append("  2nd-mouse mode: ON · WAITING — wiggle the mouse you want to bind").AppendLine();
                    }
                    else if (_secondCursor.SecondMouseBound)
                    {
                        string nm = _secondCursor.SecondMouseName;
                        sb.Append("  2nd-mouse mode: BOUND to ")
                          .Append(nm.Length > 0 ? nm : "a mouse")
                          .Append(" · speed ").Append(_secondCursor.SecondMouseSensitivityPercent).Append('%').AppendLine();
                        int idle = _secondCursor.SecondMouseIdleMs;
                        sb.Append("    2nd-mouse activity: moves ").Append(_secondCursor.SecondMouseMoveCount)
                          .Append(" · clicks ").Append(_secondCursor.SecondMouseClickCount)
                          .Append(idle < 0 ? " · (no input yet)" : " · last input " + idle + " ms ago").AppendLine();
                        sb.Append("    buttons: ")
                          .Append(_secondCursor.SecondMouseButtonHeld
                              ? "HOLDING " + _secondCursor.HeldButtonsText + " (click/hold/drag — pointer borrowed)"
                              : "up (idle)").AppendLine();
                    }
                    else
                    {
                        sb.Append("  2nd-mouse mode: ON · not bound");
                        string want = _secondCursor.PreferredDeviceName;
                        sb.Append(want.Length > 0
                            ? " · waiting for your chosen mouse to be plugged in"
                            : " · plug in / wiggle a 2nd mouse").AppendLine();
                    }
                    // Per-mouse live readout — wiggle each mouse to confirm BOTH are read.
                    if (_secondCursor.UsePhysicalMouse)
                    {
                        sb.Append("  real cursor: ")
                          .Append(_secondCursor.CursorHeldStill()
                              ? "HELD STILL (2nd mouse is moving — main pointer pinned)"
                              : "free (main mouse controls it)")
                          .Append(" · parked @ (").Append(_secondCursor.ParkedX)
                          .Append(", ").Append(_secondCursor.ParkedY).Append(')').AppendLine();
                        string dbg = _secondCursor.DevicesDebug();
                        if (!string.IsNullOrEmpty(dbg)) { sb.Append(dbg); }
                    }
                    if (_secondCursor.Spamming)
                    {
                        sb.Append("  (if the game ignores this, it reads the mouse via raw input — ")
                          .Append("posted clicks can't reach those; use it on windowed apps/games)").AppendLine();
                    }
                }
                else
                {
                    sb.Append("2nd cursor: off").AppendLine();
                }

                // Cached: enumerating raw-input devices and hitting the registry twice a
                // second would be absurd, and a keyboard doesn't change between blinks.
                if (_keyboardSummaryCache == null)
                {
                    _keyboardSummaryCache = Utils.KeyboardInfo.Summary();
                }
                sb.Append("Keyboard: ").Append(_keyboardSummaryCache).AppendLine();

                // Mice, alongside the keyboard — the Second Cursor feature is built on
                // them and there was no way to see what Tempo had actually found.
                try
                {
                    int mice = Engine.SecondCursorController.DetectedMouseCount();
                    sb.Append("  Mice: ")
                      .Append(mice > 0 ? Engine.SecondCursorController.DetectedMouseSummary() : "none detected")
                      .AppendLine();
                }
                catch { }

                // Are the global hotkeys ACTUALLY live right now? This is the answer to
                // "my hotkey does nothing", and until now nothing showed it: "Sleep in
                // tray" unregisters every one of them while Tempo is hidden and idle,
                // and a combo another app already owns falls back to a keyboard hook
                // (still works, but no longer reserved for Tempo).
                sb.Append("  Hotkeys: ");
                if (_traySleepActive)
                {
                    sb.Append("PAUSED — Tempo is asleep in the tray (open the window to wake them)");
                }
                else if (_hotkeys == null)
                {
                    sb.Append("not initialised");
                }
                else
                {
                    sb.Append("live");
                    try
                    {
                        string detail = _hotkeys.DebugSummary();
                        if (!string.IsNullOrEmpty(detail)) { sb.Append(" · ").Append(detail); }
                    }
                    catch { }
                }
                sb.Append("  · sleep-in-tray ")
                  .Append(_settings != null && _settings.TraySleepEnabled ? "on" : "off")
                  .AppendLine();

                // Notifications: style, geometry, live card counts, and the mirror's full
                // state — enough to spot a silent problem (mirror denied, cards piling up
                // in the queue, the fast-path never firing) without guessing.
                if (_settings != null)
                {
                    try
                    {
                        string[] corners = { "top-right", "top-left", "bottom-right", "bottom-left" };
                        int ci = Math.Max(0, Math.Min(3, _settings.NotificationCorner));
                        sb.Append("Notifications: ")
                          .Append(_settings.CustomNotifications ? "custom pop-ups" : "Windows balloons")
                          .Append(" · ").Append(corners[ci])
                          .Append(" · ").Append(_settings.NotificationDurationSeconds)
                          .Append("s min, longer for long text")
                          .Append(_settings.NotificationShowClose ? " · ✕ shown" : " · ✕ hidden");
                        if (_notifications != null)
                        {
                            sb.Append(" · ").Append(_notifications.ShownCount).Append(" shown");
                            if (_notifications.ActiveCount > 0) { sb.Append(" · ").Append(_notifications.ActiveCount).Append(" on screen"); }
                            if (_notifications.QueuedCount > 0) { sb.Append(" · ").Append(_notifications.QueuedCount).Append(" queued"); }
                            // Repeats folded onto an existing card. Shown so the
                            // collapsing reads as deliberate rather than as lost cards.
                            if (_notifications.RepeatsCollapsed > 0)
                            {
                                sb.Append(" · ").Append(_notifications.RepeatsCollapsed).Append(" repeats merged");
                            }
                            // Cards Windows told us not to show. Without this the
                            // suppression would look like notifications silently failing.
                            if (_notifications.SuppressedCount > 0)
                            {
                                sb.Append(" · ").Append(_notifications.SuppressedCount)
                                  .Append(" held back (").Append(_notifications.LastSuppressedReason).Append(')');
                            }
                            if (Utils.GamePresence.ShouldHoldNotifications(out string holdNow))
                            {
                                sb.Append(" · ⏸ NOT showing cards right now — ").Append(holdNow);
                            }
                        }
                        sb.AppendLine();

                        // Screenshot alert: say whether the clipboard listener is actually
                        // REGISTERED, not just whether the box is ticked. Those two
                        // disagreed silently when the whole notification subsystem failed
                        // to arm on a start-minimised-to-tray launch.
                        if (_settings.NotifyOnClipboardImage)
                        {
                            sb.Append("  Screenshot alert: ")
                              .Append(_clipboardListenerOn
                                  ? "on — listening to the clipboard"
                                  : "⚠ ON in settings but NOT listening (clipboard watcher failed to register)");
                            // Re-copies folded into the shot's existing card. Drawing on a
                            // snip re-copies on every stroke, so this climbs while editing
                            // — and proves the spam is being absorbed rather than shown.
                            if (_shotRepeatsSuppressed > 0)
                            {
                                sb.Append(" · ").Append(_shotRepeatsSuppressed)
                                  .Append(" re-copy/edit(s) folded in");
                            }
                            sb.AppendLine();
                        }

                        if (_settings.MirrorWindowsNotifications)
                        {
                            sb.Append("  Mirror: ")
                              .Append(_notifyMirror != null ? _notifyMirror.StatusText : "starting…");
                            if (_notifyMirror != null)
                            {
                                if (_notifyMirror.MirroredCount > 0)
                                {
                                    sb.Append(" · ").Append(_notifyMirror.MirroredCount).Append(" mirrored");
                                    if (!string.IsNullOrEmpty(_notifyMirror.LastApp))
                                    {
                                        sb.Append(" (last: ").Append(_notifyMirror.LastApp).Append(')');
                                    }
                                }
                                // Whether the instant fast-path is firing or we're relying
                                // on the 200 ms poll — a silent perf fact worth seeing.
                                sb.Append(_notifyMirror.EventFastPathHits > 0
                                    ? " · instant (" + _notifyMirror.EventFastPathHits + " events)"
                                    : " · polling every " + _notifyMirror.PollIntervalMs +
                                      " ms (each poll costs " + _notifyMirror.PollCostMs + " ms)");
                                // Cached logos: each one is a slow decode that no longer
                                // sits between the toast and Tempo's card.
                                if (_notifyMirror.CachedIcons > 0)
                                {
                                    sb.Append(" · ").Append(_notifyMirror.CachedIcons).Append(" icon(s) cached");
                                }
                                if (_settings.MirrorClearFromActionCenter) { sb.Append(" · clears Action Center"); }

                                // Where each card's icon came from. Windows only exposes a
                                // logo for PACKAGED apps; the other routes cover ordinary
                                // programs, and this says plainly how many cards still end
                                // up with no logo at all.
                                int pk = Utils.WindowsNotificationMirror.IconsFromPackagedLogo;
                                int dk = Utils.WindowsNotificationMirror.IconsFromDesktopApp;
                                int no = Utils.WindowsNotificationMirror.IconsUnresolved;
                                if (pk + dk + no > 0)
                                {
                                    sb.AppendLine();
                                    // "installed", not "running": the shell lookup resolves
                                    // an icon for an app that has since closed, too.
                                    sb.Append("    app icons: ").Append(pk).Append(" from packaged logo · ")
                                      .Append(dk).Append(" from the installed app");
                                    if (no > 0)
                                    {
                                        sb.Append("  ⚠ ").Append(no)
                                          .Append(" unresolved (those cards show a glyph badge, not a logo)");
                                    }
                                }
                                // Duplicate collapsing: the same site open in many tabs
                                // raises one Windows toast PER TAB. Showing the count
                                // proves the burst is being folded into one card.
                                if (_notifyMirror.SuppressedDuplicates > 0)
                                {
                                    sb.Append(" · ").Append(_notifyMirror.SuppressedDuplicates)
                                      .Append(" duplicate(s) collapsed");
                                    if (!string.IsNullOrEmpty(_notifyMirror.LastSuppressedApp))
                                    {
                                        sb.Append(" (last: ").Append(_notifyMirror.LastSuppressedApp).Append(')');
                                    }
                                }
                            }
                            sb.AppendLine();
                        }
                    }
                    catch (Exception nex)
                    {
                        sb.Append("Notifications: (stats error: ").Append(nex.Message).Append(')').AppendLine();
                    }
                }

                // ── Window, rendering & tabs ────────────────────────────────
                // None of this used to be reported, which is exactly why a window left
                // larger than the work area (bottom under the taskbar), a stale
                // full-screen flag, and whole-window compositing crushing scrolling to
                // ~11 FPS all had to be found by hand instead of being read off here.
                try
                {
                    Rectangle wa = Screen.FromControl(this).WorkingArea;
                    bool oversize = WindowState == FormWindowState.Normal &&
                                    (Width > wa.Width + 2 || Height > wa.Height + 2);
                    sb.Append("Window: ").Append(WindowState)
                      .Append(' ').Append(Width).Append('×').Append(Height)
                      .Append(" @ (").Append(Left).Append(',').Append(Top).Append(')')
                      .Append(" · client ").Append(ClientSize.Width).Append('×').Append(ClientSize.Height)
                      .Append(" · work area ").Append(wa.Width).Append('×').Append(wa.Height)
                      .Append(" · ").Append(DeviceDpi).Append(" dpi")
                      .Append(oversize ? "  ⚠ BIGGER THAN THE WORK AREA — bottom is under the taskbar" : "")
                      .AppendLine();

                    sb.Append("  full screen: ").Append(_isFullScreen ? "ON" : "off")
                      .Append(" · border ").Append(FormBorderStyle);
                    if (!_isFullScreen && _fsPrevBounds.Width > 0)
                    {
                        sb.Append(" · saved restore rect ").Append(_fsPrevBounds.Width).Append('×')
                          .Append(_fsPrevBounds.Height).Append(" (").Append(_fsPrevState).Append(')');
                    }
                    // Only meaningful with the system title bar — see the note on the
                    // matching health check: custom chrome is borderless permanently.
                    if (!_customChrome && IsHandleCreated
                        && _isFullScreen != (FormBorderStyle == FormBorderStyle.None))
                    {
                        sb.Append("  ⚠ flag disagrees with the border style");
                    }
                    sb.AppendLine();

                    // The scroll-speed switch, stated plainly: compositing is what makes
                    // scrolling crawl, and it should only ever be on with a wallpaper.
                    bool wallpaper = _fullBgImage != null;
                    sb.Append("  rendering: wallpaper ").Append(wallpaper ? "on" : "off")
                      .Append(" · whole-window compositing ").Append(_compositedOn ? "ON" : "off")
                      .Append(_compositedOn
                          ? (wallpaper ? " (needed for the wallpaper)"
                                       : "  ⚠ composited with NO wallpaper — scrolling will crawl")
                          : " · fast scrolling")
                      .AppendLine();

                    // Title-bar tint: proves the system chrome is wearing the theme
                    // rather than a plain black/white bar above a coloured app.
                    // Custom chrome: which parts are ours vs Windows'. If dragging or
                    // resizing ever breaks again, this says immediately whether the
                    // window is even in custom-chrome mode.
                    sb.Append("  chrome: ")
                      .Append(_customChrome ? "custom title bar (Tempo draws ─ □ ✕)" : "system title bar")
                      .Append(" · border ").Append(FormBorderStyle)
                      .Append(" · caption buttons ")
                      .Append(_header != null && _header.ShowCaptionButtons ? "shown" : "hidden")
                      .Append(" · drag/snap/resize: native via hit-test")
                      .AppendLine();

                    // The style bits the shell and DWM read. WinForms strips these for a
                    // borderless form and CreateParams puts them back; without them the
                    // taskbar button won't minimise the window and DWM plays no
                    // minimise/restore/close animation — both silent failures, so the
                    // actual live style word is reported rather than what we asked for.
                    if (IsHandleCreated)
                    {
                        int st = GetWindowLong(Handle, GWL_STYLE);
                        bool min = (st & WS_MINIMIZEBOX) != 0;
                        bool cap = (st & WS_CAPTION) == WS_CAPTION;
                        sb.Append("  window styles: 0x").Append(st.ToString("X8"))
                          .Append(" · minimisable ").Append(min ? "yes" : "NO")
                          .Append(" · framed ").Append(cap ? "yes" : "NO")
                          .Append(min && cap
                              ? " — taskbar click minimises, Windows animations on"
                              : "  ⚠ taskbar click-to-minimise and window animations are DEAD")
                          .AppendLine();
                    }

                    sb.Append("  colours: title bar ").Append(HexOf(_theme != null ? _theme.Surface : Color.Black))
                      .Append(" · text ").Append(HexOf(_theme != null ? _theme.Text : Color.White))
                      .Append(Environment.OSVersion.Version.Build >= 22000
                          ? " (themed)"
                          : " (Windows 10 — dark/light only)")
                      .AppendLine();

                    if (_tabs != null && _tabs.SelectedTab != null)
                    {
                        sb.Append("  tab: ").Append(_tabs.SelectedTab.Text)
                          .Append(" [").Append(_tabs.SelectedIndex).Append(']')
                          .Append(" · reopen on last tab ")
                          .Append(_settings != null && _settings.RememberLastTab ? "on" : "off")
                          .Append(" · remembered ").Append(_settings != null ? _settings.LastTabIndex : -1)
                          .AppendLine();
                    }
                }
                catch (Exception wex)
                {
                    sb.Append("Window: (stats error: ").Append(wex.Message).Append(')').AppendLine();
                }

                // ── Theme ───────────────────────────────────────────────────
                // Says WHICH colour is on screen and WHERE it came from, so a
                // "my theme looks wrong" report answers itself.
                try
                {
                    sb.Append("Theme: ").Append(_settings != null ? _settings.Theme.ToString() : "?")
                      .Append(" · Match Windows ")
                      .Append(_settings != null && _settings.FollowSystemTheme ? "on" : "off")
                      .Append(" · Windows is ").Append(Utils.SystemTheme.IsWindowsLight() ? "light" : "dark");
                    if (_theme != null)
                    {
                        sb.Append(" · accent ").Append(HexOf(_theme.Accent)).Append(" from ");
                        if (_settings != null && _settings.CustomAccentEnabled)
                        {
                            sb.Append("your custom colour");
                        }
                        else if (_settings != null && _settings.FollowSystemTheme &&
                                 (_settings.Theme == ThemeKind.Light || _settings.Theme == ThemeKind.Dark) &&
                                 Utils.SystemTheme.TryGetWindowsAccent(out Color winAcc))
                        {
                            sb.Append("Windows (").Append(HexOf(winAcc)).Append(')');
                        }
                        else
                        {
                            sb.Append("the theme");
                        }
                    }
                    sb.AppendLine();
                }
                catch (Exception tex)
                {
                    sb.Append("Theme: (stats error: ").Append(tex.Message).Append(')').AppendLine();
                }

                // Updates: the cached "latest seen" is only meaningful if the check that
                // wrote it actually succeeded — say so, so nobody (user or developer)
                // mistakes a stale cache for the released version.
                try
                {
                    if (_settings != null)
                    {
                        sb.Append("Updates: running v").Append(Application.ProductVersion);
                        string seen = _settings.LastKnownLatestVersion;
                        sb.Append(" · last seen on GitHub ")
                          .Append(string.IsNullOrWhiteSpace(seen) ? "(never checked)" : "v" + seen);
                        if (_settings.LastUpdateCheckUtc.HasValue)
                        {
                            double ago = (DateTime.UtcNow - _settings.LastUpdateCheckUtc.Value).TotalSeconds;
                            sb.Append(" · checked ").Append(FormatAgo(ago)).Append(" ago");
                        }
                        if (_settings.LastUpdateCheckFailed)
                        {
                            sb.Append("  ⚠ LAST CHECK FAILED — the version above is a stale cache, not what's released");
                        }
                        sb.AppendLine();
                    }
                }
                catch (Exception uex)
                {
                    sb.Append("Updates: (stats error: ").Append(uex.Message).Append(')').AppendLine();
                }

                sb.Append("Install: ").Append(Utils.SelfCheck.Summary).AppendLine();

                // Integrity: the verdict AND the evidence. A bare "modified" is not
                // actionable — the two fingerprints are what let someone confirm it
                // against the published checksum for their release.
                sb.Append("  Integrity: ").Append(Utils.IntegrityCheck.Summary)
                  .Append("  · GitHub: ").Append(Utils.IntegrityCheck.OnlineSummary);
                if (!string.IsNullOrEmpty(Utils.IntegrityCheck.CurrentHash))
                {
                    sb.Append("  · now ").Append(Short12(Utils.IntegrityCheck.CurrentHash));
                    if (!string.IsNullOrEmpty(Utils.IntegrityCheck.ExpectedHash) &&
                        !string.Equals(Utils.IntegrityCheck.ExpectedHash, Utils.IntegrityCheck.CurrentHash,
                                       StringComparison.OrdinalIgnoreCase))
                    {
                        sb.Append(" · expected ").Append(Short12(Utils.IntegrityCheck.ExpectedHash));
                    }
                }
                sb.AppendLine();

                // Backdrop image / GIF. Its cost is easy to miss: an animated wallpaper
                // repaints the whole window and forces compositing, so when the UI feels
                // sluggish this line is the first thing worth reading.
                try
                {
                    sb.Append("Backdrop: ");
                    if (_fullBgImage == null)
                    {
                        sb.Append("none · fast scrolling");
                    }
                    else
                    {
                        sb.Append(_fullBgImage.Width).Append('×').Append(_fullBgImage.Height);
                        int frames = 1;
                        try
                        {
                            var dim = new System.Drawing.Imaging.FrameDimension(
                                _fullBgImage.FrameDimensionsList[0]);
                            frames = _fullBgImage.GetFrameCount(dim);
                        }
                        catch { }
                        sb.Append(frames > 1 ? " animated, " + frames + " frames" : " still image");
                        sb.Append(" · ").Append(_bgAnimating ? "playing" : "paused");
                        if (frames > 1)
                        {
                            sb.Append(" · repaints capped at ").Append(BackdropMaxFps).Append(" fps");
                        }
                        sb.Append(" · dim ").Append(_settings != null ? _settings.BackgroundDim : 55).Append('%');
                        sb.Append(_compositedOn ? " · whole-window compositing ON (slower scrolling)" : "");
                    }
                    sb.AppendLine();
                }
                catch (Exception bex)
                {
                    sb.Append("Backdrop: (error: ").Append(bex.Message).Append(')').AppendLine();
                }

                // Language coverage. T() falls back to English when a phrase has no
                // translation, which is the right behaviour but used to be completely
                // silent — an untranslated button could ship for months unnoticed. The
                // miss count makes it visible, and names a few of the offenders.
                try
                {
                    sb.Append("Language: ").Append(Utils.Localization.Current);
                    if (Utils.Localization.Current == Models.Language.English)
                    {
                        sb.Append(" · source language, nothing to translate");
                    }
                    else
                    {
                        int hit = Utils.Localization.TranslatedCount;
                        int miss = Utils.Localization.UntranslatedCount;
                        sb.Append(" · ").Append(hit).Append(" phrase(s) translated");
                        if (miss > 0)
                        {
                            sb.Append("  ⚠ ").Append(miss)
                              .Append(" untranslated (showing English): ")
                              .Append(Utils.Localization.SampleUntranslated());
                        }
                        else
                        {
                            sb.Append(" · no gaps seen");
                        }

                        int dbl = Utils.Localization.DoubleTranslatedCount;
                        if (dbl > 0)
                        {
                            sb.Append(" · ").Append(dbl)
                              .Append(" redundant re-translation(s) ignored");
                        }
                    }
                    sb.AppendLine();
                }
                catch (Exception lex)
                {
                    sb.Append("Language: (error: ").Append(lex.Message).Append(')').AppendLine();
                }

                // Timing backend: what the click interval is actually waited on. A
                // kernel timer sleeps the interval; without one the engine has to
                // busy-wait the tail of every click, which at high CPS is the
                // difference between ~1% of a core and a whole core.
                if (_tabSwitchCount > 0)
                {
                    sb.Append("Tab switch: last ").Append(_lastTabSwitchMs.ToString("0.0"))
                      .Append(" ms · avg ").Append((_totalTabSwitchMs / _tabSwitchCount).ToString("0.0"))
                      .Append(" ms · worst ").Append(_worstTabSwitchMs.ToString("0.0"))
                      .Append(" ms over ").Append(_tabSwitchCount).Append(" switch(es)")
                      .Append(_worstTabSwitchMs > 100 ? "  ⚠ a switch over ~100 ms is felt" : "")
                      .AppendLine();
                }

                sb.Append("Click timing: ")
                  .Append(Engine.PreciseWait.HighResolutionAvailable
                      ? "high-resolution kernel timer · sleeps the interval (low CPU)"
                      : "coarse timer + 4 ms busy-wait per click · Windows 10 1803+ needed for the low-CPU path")
                  .AppendLine();

                // Logo: which artwork the header/cards are actually drawing, and at what
                // size it decoded. If this ever says "fallback bolt", the icon resource
                // didn't load — which is otherwise invisible, since the bolt looks fine.
                try
                {
                    string custom = Utils.CustomLogo.GetPath();
                    sb.Append("Logo: ");
                    if (!string.IsNullOrEmpty(custom))
                    {
                        sb.Append("custom · ").Append(System.IO.Path.GetFileName(custom));
                    }
                    else
                    {
                        using (var probe = Utils.AppIcon.GetBitmap(64))
                        {
                            sb.Append(probe != null
                                ? "app icon · tempo.ico decoded " + probe.Width + "x" + probe.Height
                                : "fallback bolt (tempo.ico did not decode)");
                        }
                    }
                    sb.AppendLine();
                }
                catch (Exception lex)
                {
                    sb.Append("Logo: (probe error: ").Append(lex.Message).Append(')').AppendLine();
                }

                sb.Append("Session: v").Append(Application.ProductVersion)
                  .Append(" · clicker ").Append(_engine != null && _engine.IsRunning ? "RUNNING" : "idle")
                  .Append(" · events: ").Append(Utils.Logger.WarnCount).Append(" warn / ")
                  .Append(Utils.Logger.ErrorCount).Append(" error");

                // Anti-freeze actively slowing the clicker is the whole explanation for
                // "my clicks are slower than the rate I set", and the flag that says so
                // had no reader outside the engine.
                if (_engine != null && _engine.IsRunning && _engine.IsThrottling)
                {
                    sb.Append("  ⚠ ANTI-FREEZE THROTTLING — clicks deliberately slowed to keep the system responsive");
                }

                // Why the last run ended. The engine already distinguishes "reached its
                // count/duration" from "you stopped it" (it drives the completion chime),
                // and a bug report saying "it stopped early" could not tell them apart.
                if (_engine != null && !_engine.IsRunning && _engine.RunActiveMs > 0)
                {
                    sb.Append("  · last run ")
                      .Append(_engine.LastRunCompletedNaturally ? "finished on its own" : "was stopped")
                      .Append(" after ").Append((_engine.RunActiveMs / 1000.0).ToString("0.0")).Append("s");
                }
            }
            catch (Exception ex)
            {
                // One failing section must not blank the whole readout: keep what
                // was already built, name the failure, and still show the session
                // line so version/error counts survive any stats-provider fault.
                sb.AppendLine().Append("(stats section failed: ").Append(ex.Message).Append(')').AppendLine();
                try
                {
                    sb.Append("Session: v").Append(Application.ProductVersion)
                      .Append(" · events: ").Append(Utils.Logger.WarnCount).Append(" warn / ")
                      .Append(Utils.Logger.ErrorCount).Append(" error");
                }
                catch { /* give back whatever we have */ }
            }
            return sb.ToString();
        }

        /// <summary>
        /// True when the user's GPU setting disagrees with the engine this process
        /// actually locked in — i.e. the setting cannot take effect until Tempo is
        /// restarted. (The native CPU/GPU choice is fixed at the first model load.)
        /// </summary>
        private bool GpuSettingNeedsRestart()
        {
            return _settings != null &&
                   Utils.TempoTranscriber.RuntimeLocked &&
                   Utils.TempoTranscriber.RuntimeGpuRequested != _settings.CaptionTryGpu;
        }

        /// <summary>
        /// Tells the user their GPU choice can't apply until Tempo restarts, and offers
        /// to restart now. Silent when the setting matches what's already running (a
        /// fresh Tempo, or a box ticked before captions ever started).
        /// </summary>
        private void PromptGpuRestartIfNeeded()
        {
            if (!GpuSettingNeedsRestart())
            {
                return;                     // will apply at the next caption start
            }
            bool want = _settings.CaptionTryGpu;
            bool r = AskToRestart(
                Localization.T(want ? "GPU speech engine enabled" : "GPU speech engine turned off"),
                Localization.T("Your choice is already saved — nothing is lost either way."),
                Localization.T(want
                    ? "The GPU engine can only be selected while Tempo starts up, so captions are still running on the CPU engine until it restarts."
                    : "The speech engine is fixed while Tempo runs, so captions are still using the GPU engine until it restarts."));
            if (!r)
            {
                return;
            }
            try { Persistence.SettingsManager.Save(_settings); } catch { }
            Utils.Logger.Info("[Captions] restarting Tempo to apply the GPU engine change.");
            // Same polished hand-over the language change uses — fade, a "Restarting…"
            // panel, captions resumed on the other side — instead of the window simply
            // vanishing mid-click. It also carries the relaunch-failed path, which the
            // bare RestartApp() call here silently swallowed into the log.
            FadeOutThenRestart("the speech engine change");
        }

        // ── Camera-relative movement ────────────────────────────────────────────

        /// <summary>Copies the saved settings into a tuning object for the engine.</summary>
        private Engine.MovementTuning BuildMovementTuning()
        {
            return new Engine.MovementTuning
            {
                Frame = _settings != null && _settings.MovementFrame == 1
                    ? Engine.MovementFrame.CameraRelative
                    : Engine.MovementFrame.WorldLocked,
                DegreesPerMouseCount = _settings != null ? _settings.MovementDegreesPerCount : 0.06,
                TurnSmoothingSeconds = _settings != null ? _settings.MovementTurnSmoothing : 0.0,
                SectorHysteresisDegrees = _settings != null ? _settings.MovementHysteresisDegrees : 8.0,
                UpdateHz = _settings != null ? _settings.MovementUpdateHz : 120,
                StickDeadzone = _settings != null ? _settings.MovementStickDeadzone : 0.20,
                GamepadYawDegreesPerSecond = _settings != null ? _settings.MovementGamepadYawDps : 220.0
            };
        }

        /// <summary>
        /// Pushes the current settings into a RUNNING movement engine. Values that the
        /// loop simply reads each tick (sensitivity, smoothing, hysteresis, deadzone)
        /// apply live; the frame and the tick rate are read once at start, so changing
        /// those re-arms the engine.
        /// </summary>
        private void ApplyMovementTuning()
        {
            if (_movement == null || !_movement.IsRunning || _settings == null)
            {
                return;
            }
            var t = _movement.Tuning;
            t.DegreesPerMouseCount = _settings.MovementDegreesPerCount;
            t.TurnSmoothingSeconds = _settings.MovementTurnSmoothing;
            t.SectorHysteresisDegrees = _settings.MovementHysteresisDegrees;
            t.StickDeadzone = _settings.MovementStickDeadzone;
            t.GamepadYawDegreesPerSecond = _settings.MovementGamepadYawDps;

            var wanted = _settings.MovementFrame == 1
                ? Engine.MovementFrame.CameraRelative
                : Engine.MovementFrame.WorldLocked;
            if (t.Frame != wanted || t.UpdateHz != _settings.MovementUpdateHz)
            {
                // These are baked in when the loop starts (and the frame decides whether
                // the keyboard hook swallows W/A/S/D at all), so re-arm to apply them.
                StopMovement();
                StartMovement();
            }
        }

        /// <summary>Arms or disarms the engine to match <see cref="AppSettings.MovementEnabled"/>.</summary>
        private void ApplyMovementSetting()
        {
            if (_settings != null && _settings.MovementEnabled) { StartMovement(); }
            else { StopMovement(); }
        }

        private void StartMovement()
        {
            if (_movement != null && _movement.IsRunning)
            {
                return;
            }
            try
            {
                _movement?.Dispose();
                _movement = new Engine.CameraRelativeMovement(BuildMovementTuning());

                // Pass our own handle so that arming from the Settings tab doesn't lock
                // the system to Tempo's window. Armed by HOTKEY (the normal way, while
                // the game is in front), the game becomes the target and W/A/S/D are
                // left alone in every other window.
                IntPtr own = IsHandleCreated ? Handle : IntPtr.Zero;
                if (!_movement.Start(own))
                {
                    _movement.Dispose();
                    _movement = null;
                    SetMovementStatus("Couldn't start (raw input or hook unavailable).", true);
                    return;
                }
                SetMovementStatus(_movement.TargetWindow == IntPtr.Zero
                    ? "ARMED everywhere — W/A/S/D captured globally. Arm with the hotkey in-game instead."
                    : "ARMED for the game that was in front. Other windows type normally.", false);
            }
            catch (Exception ex)
            {
                Utils.Logger.Warn("[Movement] start failed: " + ex.Message);
                SetMovementStatus("Couldn't start: " + ex.Message, true);
            }
        }

        private void StopMovement()
        {
            if (_movement == null)
            {
                return;
            }
            try { _movement.Stop(); } catch { }
            try { _movement.Dispose(); } catch { }
            _movement = null;
            SetMovementStatus("Off. Your keys go straight to the game.", false);
        }

        private void SetMovementStatus(string text, bool bad)
        {
            if (_movementStatus == null || _movementStatus.IsDisposed)
            {
                return;
            }
            UiInvoke(() =>
            {
                _movementStatus.Text = text;
                _movementStatus.ForeColor = bad ? _theme.Danger : _theme.TextMuted;
            });
        }

        /// <summary>Hotkey: arm/disarm camera-relative movement.</summary>
        private void ToggleCameraMovement()
        {
            if (_settings == null)
            {
                return;
            }
            bool on = !(_movement != null && _movement.IsRunning);
            _settings.MovementEnabled = on;
            ApplyMovementSetting();

            // Keep the checkbox honest — the hotkey is the primary way to use this, and
            // a checkbox that disagrees with reality is worse than no checkbox.
            if (_movementEnableCheck != null && !_movementEnableCheck.IsDisposed)
            {
                UiInvoke(() =>
                {
                    _suppressSettingsEvents = true;
                    try { _movementEnableCheck.Checked = on; }
                    finally { _suppressSettingsEvents = false; }
                });
            }
            try { Persistence.SettingsManager.Save(_settings); } catch { }

            if (_trayIcon != null && _settings.ShowTrayNotifications)
            {
                try
                {
                    TempoNotify(2500, "Tempo",
                        on ? "Camera-relative movement ARMED — W/A/S/D are being re-mixed."
                           : "Camera-relative movement off.",
                        ToolTipIcon.Info);
                }
                catch { }
            }
        }

        /// <summary>
        /// Stands the movement engine down because another Tempo feature needs the real
        /// keyboard, and keeps the setting and checkbox honest about it. Returns true if
        /// something was actually disarmed.
        ///
        /// WHY THIS IS NEEDED AT ALL: movement installs its own WH_KEYBOARD_LL hook and
        /// SUPPRESSES the physical W/A/S/D — the hook callback returns 1 instead of
        /// chaining, which ends the chain outright. Tempo runs four separate low-level
        /// keyboard hooks (movement, the macro recorder, the hotkey manager, the
        /// calibration dialog) and Windows calls them most-recently-installed first. Arming
        /// movement during play installs its hook LAST, so it runs FIRST and everything
        /// behind it goes blind to those four keys. Nothing about that is visible from the
        /// other features' side — they simply never receive the event.
        ///
        /// Deliberately does NOT re-arm afterwards. Re-arming would capture whatever window
        /// is in front at that moment — Tempo — and the engine treats "armed on Tempo" as
        /// "act in EVERY window", which is the one state that eats W/A/S/D system-wide.
        /// Leaving it off and saying so is the honest outcome; re-arming is one hotkey.
        /// </summary>
        private bool DisarmMovementBecause(string reason)
        {
            if (_movement == null || !_movement.IsRunning)
            {
                return false;
            }

            StopMovement();
            if (_settings != null) { _settings.MovementEnabled = false; }

            if (_movementEnableCheck != null && !_movementEnableCheck.IsDisposed)
            {
                UiInvoke(() =>
                {
                    _suppressSettingsEvents = true;
                    try { _movementEnableCheck.Checked = false; }
                    finally { _suppressSettingsEvents = false; }
                });
            }
            try { Persistence.SettingsManager.Save(_settings); } catch { }

            SetMovementStatus("Disarmed — " + reason + ". Re-arm with the hotkey when you're done.", false);
            Utils.Logger.Info("[Movement] disarmed: " + reason + ".");

            // Say it out loud. A movement system that stops working without a word is
            // indistinguishable from one that broke.
            if (_trayIcon != null && _settings != null && _settings.ShowTrayNotifications)
            {
                try
                {
                    TempoNotify(3000, "Tempo",
                        // Prefix translated on its own: the finished sentence contains a
                        // runtime reason, so it could never match a dictionary key. The
                        // reason arrives already translated from its caller.
                        Localization.T("Camera-relative movement disarmed") + " — " + reason + ".",
                        ToolTipIcon.Info);
                }
                catch { }
            }
            return true;
        }

        /// <summary>Hotkey: re-zero the estimated camera direction (cures drift).</summary>
        private void RecenterCameraMovement()
        {
            if (_movement == null || !_movement.IsRunning)
            {
                return;
            }
            _movement.ResetYaw();
            SetMovementStatus("ARMED · camera estimate re-centred.", false);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int max);

        /// <summary>
        /// Names the window movement is armed on, so the Live Debug line answers "is it
        /// pointed at my game?" instead of showing a meaningless handle.
        /// </summary>
        private static string WindowTitleOf(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
            {
                return "every window (armed from Tempo)";
            }
            try
            {
                var sb = new System.Text.StringBuilder(256);
                GetWindowText(hWnd, sb, sb.Capacity);
                string t = sb.ToString().Trim();
                if (t.Length == 0) { return "window " + hWnd.ToInt64().ToString("X"); }
                if (t.Length > 40) { t = t.Substring(0, 38) + "…"; }
                return "“" + t + "”";
            }
            catch
            {
                return "window " + hWnd.ToInt64().ToString("X");
            }
        }

        /// <summary>Ten-cell text level meter for the Live Debug header (−60 … 0 dB).</summary>
        private static string LevelBar(int db)
        {
            int cells = (int)Math.Round((db + 60) / 6.0);
            if (cells < 0) { cells = 0; }
            if (cells > 10) { cells = 10; }
            return "[" + new string('#', cells) + new string('-', 10 - cells) + "]";
        }

        /// <summary>Mirrors the saved capture mode into the Settings combo quietly.</summary>
        private void SyncCaptureModeCombo()
        {
            if (_captionCaptureCombo == null || _settings == null)
            {
                return;
            }
            try
            {
                _suppressSettingsEvents = true;
                int idx = _settings.CaptionCaptureMode;
                if (idx >= 0 && idx < _captionCaptureCombo.Items.Count)
                {
                    _captionCaptureCombo.SelectedIndex = idx;
                }
            }
            catch { }
            finally
            {
                _suppressSettingsEvents = false;
            }
        }

        // Debounce for persisting LastTabIndex: tab clicks are cheap, settings writes
        // aren't. Restarted on every switch so a burst of navigation writes once.
        private System.Windows.Forms.Timer _lastTabSaveTimer;

        /// <summary>
        /// Schedules a settings write a moment after the user stops switching tabs, so the
        /// remembered tab survives a reboot / force-kill without writing the whole settings
        /// file on every click. Safe to call repeatedly.
        /// </summary>
        private void QueueLastTabSave()
        {
            if (IsDisposed)
            {
                return;
            }
            try
            {
                if (_lastTabSaveTimer == null)
                {
                    _lastTabSaveTimer = new System.Windows.Forms.Timer { Interval = 1500 };
                    _lastTabSaveTimer.Tick += (s, e) =>
                    {
                        _lastTabSaveTimer.Stop();
                        SaveLastTabNow();
                    };
                }
                // Restart the countdown so rapid tab-hopping collapses into one write.
                _lastTabSaveTimer.Stop();
                _lastTabSaveTimer.Start();
            }
            catch (Exception ex) { Logger.Swallow("QueueLastTabSave", ex); }
        }

        /// <summary>Writes the remembered tab immediately (used by the debounce and on exit).</summary>
        private void SaveLastTabNow()
        {
            if (_settings == null)
            {
                return;
            }
            try { Persistence.SettingsManager.Save(_settings); }
            catch (Exception ex) { Logger.Swallow("SaveLastTab", ex); }
        }

        /// <summary>
        /// Selects the tab shown when Tempo opens: the one in use last when "Reopen on my
        /// last tab" is on (the default), otherwise Clicker.
        ///
        /// This USED to hard-select Clicker every launch. That silently lost your place in
        /// the exact situation it matters most: a long unattended macro run where Windows
        /// reboots (or launch-at-startup restarts Tempo) overnight — you come back, open
        /// Tempo from the tray, and it's sitting on Clicker instead of the Macros tab you
        /// left it on, which reads as "Tempo opened the wrong page".
        /// </summary>
        /// <summary>Position of the tab with this stable key, or -1.</summary>
        private int IndexOfTabKey(string key)
        {
            if (string.IsNullOrEmpty(key) || _tabs == null) { return -1; }
            for (int i = 0; i < _tabs.TabPages.Count; i++)
            {
                if (string.Equals(_tabs.TabPages[i].Name, key, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>Stable key of the tab on screen, or "".</summary>
        private string CurrentTabKey()
        {
            try { return _tabs?.SelectedTab?.Name ?? ""; }
            catch { return ""; }
        }

        /// <summary>Where Captions sits, for migrating pre-1.0.319 remembered indices.</summary>
        private int CaptionsTabIndex()
        {
            int i = IndexOfTabKey("captions");
            return i < 0 ? int.MaxValue : i;
        }

        private void RestoreLastTab()
        {
            if (_tabs == null || _tabs.TabPages.Count == 0)
            {
                return;
            }

            // Tabs are Clicker, Multi-Point, Macros, Statistics, Keybinds, Captions, Settings.
            // Clamp the remembered index: the tab list can shrink between versions, and a
            // corrupt/out-of-range value must never throw or land on nothing.
            int open = 0;
            if (_settings != null && _settings.RememberLastTab)
            {
                // Prefer the stable key. Falling back to the raw index is only for
                // settings written before LastTabKey existed, and those indices predate
                // the Captions tab — so anything at or past its slot has shifted by one.
                // Without that correction a remembered "Settings" reopened on Captions.
                int byKey = IndexOfTabKey(_settings.LastTabKey);
                if (byKey >= 0)
                {
                    open = byKey;
                }
                else
                {
                    int want = _settings.LastTabIndex;
                    if (want >= CaptionsTabIndex()) { want++; }
                    if (want >= 0 && want < _tabs.TabPages.Count)
                    {
                        open = want;
                    }
                }
            }
            _tabs.SelectedIndex = open;
            RefreshSidebarSelection();

            // Switch the wallpaper on for the page we just selected. This assignment does
            // NOT raise SelectedIndexChanged — the handler is only subscribed below, and
            // when the remembered tab is index 0 the value doesn't change anyway — so the
            // activation inside that handler never ran for the startup tab.
            UpdateBackdropActivePage();

            // Backup capture for native tab changes (e.g. Ctrl+Tab): the sidebar path
            // saves the scroll itself before switching, but this covers any change that
            // does raise Deselecting. SelectedTab is still the outgoing tab here.
            _tabs.Deselecting += (s, e) => SaveActiveTabScroll();

            // BEFORE the page changes, not after: the incoming page paints as soon as
            // this returns, so compositing has to already be on or its controls arrive
            // one by one over the wallpaper — the "controls show up before the tab does"
            // glitch. Selecting fires ahead of the switch, which is the only place this
            // can be armed in time.
            _tabs.Selecting += (s, e) => ArmCompositingBriefly();

            _tabs.SelectedIndexChanged += (s, e) =>
            {
                if (_settings == null)
                {
                    return;
                }
                var switchClock = System.Diagnostics.Stopwatch.StartNew();
                // The transcript is only rendered while the Captions tab is on screen, so
                // it needs one catch-up pass on the way in. Deferred for the same reason
                // Statistics is below: let the page paint first.
                if (IsCaptionsTabVisible())
                {
                    try { BeginInvoke((Action)(() => { if (!IsDisposed) { RefreshCaptionsTab(); } })); }
                    catch { }
                }
                // Bring the Statistics dashboard up to date when it's shown (the periodic
                // tick skips it while hidden) — but AFTER the page has painted. Doing this
                // synchronously here recomputed every card, chart and the history list
                // BEFORE the tab could draw, which is what made the Statistics tab feel
                // slow to open. Deferred, the page appears instantly (with its last-shown
                // values) and the fresh numbers land a frame later.
                if (_tabs.SelectedTab == _statsPage)
                {
                    try
                    {
                        BeginInvoke((Action)(() =>
                        {
                            if (!IsDisposed && _tabs != null && _tabs.SelectedTab == _statsPage)
                            {
                                UpdateStatisticsTab();
                                RefreshSessionHistory();
                            }
                        }));
                    }
                    catch { /* handle not ready — the periodic tick will catch up */ }
                }
                // Remember which tab this is so the next launch can reopen on it. The
                // index is recorded in memory immediately and written to disk on a short
                // debounce — clicking through tabs must not cost a full settings-file
                // write per click (that's why this was dropped before), but the value
                // still has to survive a reboot or a force-kill, not just a clean exit.
                if (_tabs.SelectedIndex >= 0 && _settings.LastTabIndex != _tabs.SelectedIndex)
                {
                    _settings.LastTabIndex = _tabs.SelectedIndex;
                    _settings.LastTabKey = CurrentTabKey();
                    QueueLastTabSave();
                }
                UpdateBackdropActivePage();
                RefreshSidebarSelection();

                // Force a clean, complete re-layout of the tab now that it's the active
                // one (sized to the real viewport). Forcing past the scroll skip-guard
                // is what fixes a tab coming back blank when you return to it: positions
                // and AutoScrollMinSize are refreshed before we reset the scroll, so the
                // content is always laid out and painted.
                if (_tabs.SelectedTab != null)
                {
                    // Freeze the page while it is re-laid-out. Centring the controls,
                    // PerformLayout and setting the scroll each trigger their own paints,
                    // and Refresh() below then forces a full one on top — so a single
                    // switch repainted the page several times over. That is expensive on
                    // any page and much worse with a wallpaper, which puts the window into
                    // whole-window compositing (measured earlier at ~11 fps for a
                    // full-window repaint). Frozen, the intermediate states cost nothing
                    // and the user sees exactly one paint: the finished page.
                    TabPage building = _tabs.SelectedTab;
                    var phase = System.Diagnostics.Stopwatch.StartNew();
                    double tCentre = 0, tLayout = 0, tScroll = 0, tPaint = 0;
                    SetRedraw(building, false);
                    try
                    {
                        CenterPageContent(building, true);
                        tCentre = phase.Elapsed.TotalMilliseconds; phase.Restart();
                        // Lay the page out FIRST so its real scroll range is established,
                        // THEN pin the scroll. Resetting the scroll before PerformLayout
                        // let a tall page (e.g. Keybinds scrolled down, then back to
                        // Settings) re-measure its range afterwards and come back parked
                        // low with an empty band above - the "switch tabs and the page is
                        // blank" report.
                        building.PerformLayout();
                        tLayout = phase.Elapsed.TotalMilliseconds; phase.Restart();
                        // Restore the scroll position this tab had last time it was shown
                        // (or top on first visit). Done AFTER PerformLayout so the scroll
                        // range is established and the value is clamped to something valid.
                        ApplyTabScroll(building);
                        tScroll = phase.Elapsed.TotalMilliseconds; phase.Restart();
                    }
                    finally
                    {
                        // Unfreeze, then paint once. Refresh() = invalidate + immediate
                        // paint, so the finished page is guaranteed on screen and can
                        // never show a stale or blank buffer after the switch.
                        SetRedraw(building, true);
                    }
                    building.Refresh();
                    tPaint = phase.Elapsed.TotalMilliseconds;

                    // Only report a switch that was actually slow, and break it down when
                    // reporting — measured, the layout work is ~5 ms and the PAINT is
                    // everything else, so a bare total would send the next person
                    // optimising the wrong half.
                    if (tCentre + tLayout + tScroll + tPaint > 100)
                    {
                        Utils.Logger.Info("[perf] slow switch to " + building.Text +
                            ": centre " + tCentre.ToString("0.0") +
                            " · layout " + tLayout.ToString("0.0") +
                            " · scroll " + tScroll.ToString("0.0") +
                            " · paint " + tPaint.ToString("0.0") + " ms" +
                            (_compositedOn ? " (wallpaper → whole-window compositing)" : ""));
                    }

                    // Belt-and-suspenders: repaint once more after the switch has fully
                    // settled (the page realised and its scroll range established), in
                    // case the synchronous pass ran a hair too early on a tall page.
                    // Cheap, and with composited painting it's flicker-free.
                    TabPage justShown = _tabs.SelectedTab;
                    try
                    {
                        BeginInvoke((Action)(() =>
                        {
                            try
                            {
                                if (!IsDisposed && justShown == _tabs.SelectedTab)
                                {
                                    // Always re-assert the layout after the message pump,
                                    // when the page's size and scrollbar are final. This is
                                    // the self-heal for a tab coming back blank - including
                                    // the case where the scroll reads 0 but the content was
                                    // laid out against a stale size and ended up off-screen
                                    // (which a "only fix if the scroll drifted" check misses,
                                    // leaving the page blank with no repaint - the reported
                                    // "empty, doesn't refresh" case). CenterPageContent only
                                    // moves controls that are actually out of place, and the
                                    // page is double-buffered, so when it's already correct
                                    // this repaints identically - no visible flash; when it's
                                    // wrong, it's corrected in one composited paint.
                                    // Only repaint if this pass actually CHANGED something.
                                    //
                                    // Measured: the synchronous paint above costs 36 ms on
                                    // a light page and 83 ms on the Clicker tab (a
                                    // composited whole-window repaint, which is what a
                                    // wallpaper forces). This pass then repainted
                                    // unconditionally, doubling that on every single
                                    // switch — and almost always repainting a page that was
                                    // already correct. Comparing the layout before and
                                    // after keeps the self-heal for the cases it exists for
                                    // (a page laid out against a stale size) while costing
                                    // nothing when there is nothing to heal.
                                    string before = LayoutSignature(justShown);
                                    CenterPageContent(justShown, true);
                                    ApplyTabScroll(justShown);
                                    if (!string.Equals(before, LayoutSignature(justShown), StringComparison.Ordinal))
                                    {
                                        justShown.Invalidate(true);
                                        Utils.Logger.Info("[perf] deferred pass corrected the layout of " +
                                                          justShown.Text + " — repainted.");
                                    }
                                }
                            }
                            catch { /* best-effort */ }
                        }));
                    }
                    catch { /* handle not ready */ }
                }

                RecordTabSwitchCost(switchClock.Elapsed.TotalMilliseconds);
            };
        }

        // How long the synchronous part of a tab switch took. Reported in Live debug so
        // "switching tabs feels slow" is a number instead of an impression.
        private double _lastTabSwitchMs;
        private double _worstTabSwitchMs;
        private int _tabSwitchCount;
        private double _totalTabSwitchMs;

        /// <summary>
        /// A cheap fingerprint of a page's layout: the scroll offset plus every child's
        /// position and size. Used to tell whether the deferred self-heal pass actually
        /// moved anything, so a page that was already correct isn't repainted for nothing.
        /// </summary>
        private static string LayoutSignature(TabPage page)
        {
            if (page == null)
            {
                return string.Empty;
            }
            var sb = new System.Text.StringBuilder(256);
            if (page is ScrollableControl sc)
            {
                sb.Append(sc.AutoScrollPosition.X).Append(',').Append(sc.AutoScrollPosition.Y).Append(';');
            }
            foreach (Control c in page.Controls)
            {
                sb.Append(c.Left).Append(',').Append(c.Top).Append(',')
                  .Append(c.Width).Append(',').Append(c.Height).Append(';');
            }
            return sb.ToString();
        }

        private void RecordTabSwitchCost(double ms)
        {
            _lastTabSwitchMs = ms;
            if (ms > _worstTabSwitchMs) { _worstTabSwitchMs = ms; }
            _tabSwitchCount++;
            _totalTabSwitchMs += ms;

            // The per-phase breakdown is logged by the switch handler itself when a
            // switch runs long; no second line needed here.
        }

        /// <summary>
        /// If enabled, checks for a newer version in the background shortly after
        /// launch and notifies the user only when an update is actually available.
        /// </summary>
        private void MaybeCheckForUpdatesOnLaunch()
        {
            if (_settings == null || !_settings.CheckForUpdatesOnLaunch)
            {
                return;
            }

            // Honour the chosen frequency: every launch, at most daily, or at most
            // weekly. (The master on/off is CheckForUpdatesOnLaunch, handled above.)
            double minHours;
            switch (_settings.UpdateCheckFrequency)
            {
                case 0: minHours = 0; break;        // every launch
                case 2: minHours = 24 * 7; break;   // weekly
                default: minHours = 20; break;      // daily (also the legacy behaviour)
            }
            if (minHours > 0 && _settings.LastUpdateCheckUtc != null &&
                (DateTime.UtcNow - _settings.LastUpdateCheckUtc.Value).TotalHours < minHours)
            {
                return;
            }

            // When Windows launched us at sign-in, hold the network check back by the
            // user's startup delay so it doesn't compete with everything else booting.
            int startupDelayMs = (Utils.StartupManager.LaunchedAtStartup() && _settings.StartupDelaySeconds > 0)
                ? _settings.StartupDelaySeconds * 1000 : 0;

            System.Threading.Tasks.Task.Run(() =>
            {
                // Small delay so the window settles before any dialog could appear,
                // plus the startup delay when auto-launched.
                System.Threading.Thread.Sleep(2500 + startupDelayMs);
                AutoClicker.Utils.UpdateChecker.UpdateResult result =
                    AutoClicker.Utils.UpdateChecker.Check();

                if (result == null || !result.Success)
                {
                    // Stay quiet on the launch check (and don't reset the timer), but DO
                    // record that it failed: otherwise LastKnownLatestVersion silently
                    // keeps an old value that reads like fact. Live Debug shows this.
                    UiInvoke(() =>
                    {
                        if (_settings == null) { return; }
                        _settings.LastUpdateCheckFailed = true;
                        try { SettingsManager.Save(_settings); } catch { }
                    });
                    return;
                }

                UiInvoke(() =>
                {
                    _settings.LastUpdateCheckUtc = DateTime.UtcNow;
                    _settings.LastUpdateCheckFailed = false;   // this value is now trustworthy
                    _settings.LastKnownLatestVersion = result.LatestVersion?.ToString();
                    _settings.LastCheckFoundUpdate = result.UpdateAvailable;
                    SettingsManager.Save(_settings);
                    UpdateLastCheckedLabel();

                    // Don't nag about a version the user chose to skip.
                    bool skipped = !string.IsNullOrWhiteSpace(_settings.SkippedUpdateVersion) &&
                                   string.Equals(_settings.SkippedUpdateVersion, result.LatestVersion?.ToString(),
                                       StringComparison.OrdinalIgnoreCase);

                    if (result.UpdateAvailable && !skipped)
                    {
                        // NOT necessarily right now — see PresentOrDeferUpdate.
                        PresentOrDeferUpdate(result);
                    }
                });
            });
        }

        private Utils.UpdateChecker.UpdateResult _pendingUpdate;
        private bool _presentingUpdate;

        /// <summary>
        /// True when a modal dialog would land on top of something the user is in the
        /// middle of — or on top of nothing at all, because Tempo is in the tray.
        /// </summary>
        private bool BadMomentForUpdatePrompt()
        {
            if (!Visible || WindowState == FormWindowState.Minimized) { return true; }
            if (_engine != null && _engine.IsRunning) { return true; }
            if (_recorder != null && _recorder.IsRecording) { return true; }
            if (_player != null && _player.IsPlaying) { return true; }

            // Never open ON TOP of another dialog.
            //
            // This deferral is released from the 200 ms UI tick, and a modal dialog
            // keeps pumping messages — so the tick goes on firing while one is open. On
            // a first launch that had an update waiting, the welcome notice appeared and
            // the update prompt then opened straight over it: two stacked modals before
            // the user had touched anything. The original conditions all looked fine
            // (window visible, nothing running), because none of them knew a dialog was
            // already up.
            try
            {
                foreach (Form f in Application.OpenForms)
                {
                    if (f != this && f.Visible && f.Modal) { return true; }
                }
            }
            catch { /* collection changed under us — just try again next tick */ }

            // A fullscreen app owning the screen is the same "don't interrupt" case the
            // notification cards already respect.
            if (Utils.GamePresence.ShouldHoldNotifications(out _)) { return true; }

            return false;
        }

        /// <summary>
        /// Shows the update prompt, or holds it until showing it is not an interruption.
        ///
        /// The automatic check fires a couple of seconds after launch and used to open a
        /// MODAL dialog unconditionally. Two ways that goes wrong, and both are the
        /// normal case rather than the edge case:
        ///
        ///  · Tempo starts in the tray for anyone with "Start minimised to tray" on, and
        ///    for EVERY launch-at-sign-in. A modal dialog then appears over whatever the
        ///    machine is doing at sign-in, from an app the user deliberately keeps out of
        ///    the way — with no window on screen to explain where it came from.
        ///
        ///  · Tempo is an auto-clicker. If a click run or a macro is going, a dialog
        ///    steals focus mid-automation: the run's synthetic clicks land on the dialog
        ///    instead of the target, which is precisely the failure the whole app exists
        ///    to avoid. Someone clicking in a game gets pulled out of it.
        ///
        /// A user who presses "Check for updates" themselves still gets an immediate
        /// answer — that path calls PresentUpdateResult directly, because they asked.
        /// </summary>
        private void PresentOrDeferUpdate(Utils.UpdateChecker.UpdateResult result)
        {
            if (result == null) { return; }
            if (BadMomentForUpdatePrompt())
            {
                _pendingUpdate = result;
                Utils.Logger.Info("[Update] " + result.LatestVersion +
                                  " is available — holding the prompt until Tempo is on screen and idle.");
                return;
            }
            PresentUpdateResult(result, announceUpToDate: false);
        }

        /// <summary>
        /// Called from the periodic UI tick: releases a held update prompt once the
        /// window is up and nothing is running.
        /// </summary>
        private void MaybeShowDeferredUpdate()
        {
            if (_pendingUpdate == null || _presentingUpdate) { return; }
            if (BadMomentForUpdatePrompt()) { return; }

            var result = _pendingUpdate;
            _pendingUpdate = null;
            _presentingUpdate = true;      // ShowDialog pumps messages — don't re-enter
            try
            {
                Utils.Logger.Info("[Update] showing the held prompt now.");
                PresentUpdateResult(result, announceUpToDate: false);
            }
            catch (Exception ex) { Utils.Logger.Swallow("DeferredUpdate", ex); }
            finally { _presentingUpdate = false; }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Shell construction
        // ─────────────────────────────────────────────────────────────────────

        private void InitializeShell()
        {
            Text = "Tempo";
            // Scale the whole UI to the display's DPI. Declaring a 96-DPI
            // design baseline makes WinForms multiply every control's bounds
            // by the real scale (125%, 150%, ...) at startup, so fonts (which
            // grow with DPI on their own) and layout finally grow together.
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            // Wider than before to make room for the left navigation sidebar while
            // still fitting the page content beside it (the Statistics grid is ~740px
            // wide, plus the 188px sidebar and a scrollbar).
            MinimumSize = new Size(980, 700);
            Size = new Size(1020, 824);
            StartPosition = FormStartPosition.CenterScreen;
            Font = UiFactory.BodyFont;
            Icon = Utils.AppIcon.Get();

            _tabs = new ModernTabControl
            {
                Dock = DockStyle.Fill,
                Font = UiFactory.BodyFont
            };

            _sidebar = new DoubleBufferedPanel
            {
                Dock = DockStyle.Left,
                Width = 188,
                Padding = new Padding(12, 14, 12, 12)
            };
            // A thin divider down the sidebar's right edge to separate it cleanly from
            // the page content, plus a subtle version stamp footer. Drawn directly with
            // the live theme so it recolours automatically on a theme change.
            _sidebar.Paint += (s, e) =>
            {
                if (_theme == null) return;
                var gfx = e.Graphics;

                int w = _sidebar.ClientSize.Width;
                int h = _sidebar.ClientSize.Height;

                // Background: the sidebar's aligned slice of the shared window backdrop
                // (so the wallpaper is continuous under the nav rail), or the flat
                // page colour when no backdrop is set. UserPaint means we own the fill.
                int dim = _settings != null ? _settings.BackgroundDim : 55;
                if (!WindowBackdrop.Paint(gfx, _sidebar, _fullBgImage, dim, _theme.Background))
                {
                    using (var bb = new SolidBrush(_theme.Background))
                    {
                        gfx.FillRectangle(bb, 0, 0, w, h);
                    }
                }

                gfx.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                int x = w - 1;
                using (var pen = new Pen(_theme.Border, 2))
                {
                    gfx.DrawLine(pen, x, 0, x, h);
                }

                // Footer: a faint separator line and the app version, bottom-centred, so
                // the nav rail reads as finished rather than trailing off into empty space.
                gfx.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                // The footer now carries the UPDATE STATE, not just a number.
                //
                // Tempo already knows whether a newer release exists — it records it on
                // every check — but that only showed on the Settings page, so the version
                // sitting at the bottom of every screen was inert text. When an update is
                // waiting it says so, in the accent colour, right where the version is
                // already being read.
                string ver = "Tempo " + VersionStamp();
                string note = null;
                Color noteColor = _theme.TextMuted;
                try
                {
                    if (_settings != null && _settings.LastCheckFoundUpdate &&
                        !string.IsNullOrWhiteSpace(_settings.LastKnownLatestVersion) &&
                        !string.Equals(_settings.LastKnownLatestVersion,
                                       Utils.UpdateChecker.CurrentVersion?.ToString(),
                                       StringComparison.OrdinalIgnoreCase))
                    {
                        note = "v" + _settings.LastKnownLatestVersion + " available";
                        noteColor = _theme.Accent;
                    }
                }
                catch { /* the footer must never be the thing that throws */ }

                using (var vf = new Font("Segoe UI", 8f, FontStyle.Regular))
                using (var vb = new SolidBrush(_theme.TextMuted))
                using (var lp = new Pen(_theme.Border))
                {
                    var sz = gfx.MeasureString(ver, vf);
                    float vy = h - sz.Height - 12f;
                    if (note != null)
                    {
                        // Lift the version line to make room for the notice under it.
                        vy -= sz.Height + 1f;
                    }
                    gfx.DrawLine(lp, _sidebar.Padding.Left, vy - 10f, w - _sidebar.Padding.Right - 2, vy - 10f);
                    gfx.DrawString(ver, vf, vb, (w - sz.Width) / 2f, vy);

                    if (note != null)
                    {
                        using (var nf = new Font("Segoe UI", 8f, FontStyle.Bold))
                        using (var nb = new SolidBrush(noteColor))
                        {
                            var nsz = gfx.MeasureString(note, nf);
                            gfx.DrawString(note, nf, nb, (w - nsz.Width) / 2f, vy + sz.Height + 1f);
                        }
                    }
                }
            };

            BuildHeader();

            _statusStrip = new StatusStrip
            {
                // Themed dashboard look instead of the grey system gradient (see
                // StatusStripRenderer): flat surface, accent top hairline, slim
                // separators, and small painted stat icons.
                Padding = new Padding(8, 0, 14, 0),
                SizingGrip = true
            };
            // A coloured dot precedes the state word and recolours with the engine
            // state (green running / amber paused / grey idle) for an at-a-glance read.
            _statusState = new ToolStripStatusLabel("\u25CF  Idle") { AutoSize = true };
            _statusProfile = new ToolStripStatusLabel("Profile: -")
            { AutoSize = true, DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, ImageScaling = ToolStripItemImageScaling.None };
            _statusClicks = new ToolStripStatusLabel(Utils.Localization.T("Clicks:") + " 0")
            { AutoSize = true, DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, ImageScaling = ToolStripItemImageScaling.None };
            _statusCps = new ToolStripStatusLabel(Utils.Localization.T("CPS:") + " 0.0")
            { AutoSize = true, DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, ImageScaling = ToolStripItemImageScaling.None };
            _statusPeak = new ToolStripStatusLabel("Peak 0.0")
            { AutoSize = true, DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, ImageScaling = ToolStripItemImageScaling.None };
            _statusElapsed = new ToolStripStatusLabel(Utils.Localization.T("Time:") + " 00:00")
            { AutoSize = true, DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, ImageScaling = ToolStripItemImageScaling.None };
            // What Tempo itself is costing, and how long it has been up. Placed at the
            // far left of the stats cluster so the click counters keep their positions.
            _statusCpu = new ToolStripStatusLabel(Utils.Localization.F("CPU {0:0.0}%", 0.0))
            { AutoSize = true, DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, ImageScaling = ToolStripItemImageScaling.None };
            _statusRam = new ToolStripStatusLabel(Utils.Localization.F("RAM {0:N0} MB", 0))
            { AutoSize = true, DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, ImageScaling = ToolStripItemImageScaling.None };
            _statusUptime = new ToolStripStatusLabel(Utils.Localization.T("Up") + " 0:00:00")
            { AutoSize = true, DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, ImageScaling = ToolStripItemImageScaling.None };
            _statusHint = new ToolStripStatusLabel("") { AutoSize = true };
            // Progress (target run) and throttle indicators are hidden until relevant.
            _statusProgress = new ToolStripStatusLabel("") { AutoSize = true, Visible = false };
            _statusThrottle = new ToolStripStatusLabel("\u26A1 throttling") { AutoSize = true, Visible = false };

            _statusStrip.Items.Add(_statusState);
            _statusStrip.Items.Add(new ToolStripSeparator());
            _statusStrip.Items.Add(_statusProfile);
            _statusStrip.Items.Add(new ToolStripSeparator());
            _statusStrip.Items.Add(_statusProgress);
            _statusStrip.Items.Add(new ToolStripStatusLabel { Spring = true });
            _statusStrip.Items.Add(_statusHint);
            _statusStrip.Items.Add(new ToolStripStatusLabel { Spring = true });
            _statusStrip.Items.Add(_statusThrottle);
            _statusResourceSep = new ToolStripSeparator();
            _statusStrip.Items.Add(_statusCpu);
            _statusStrip.Items.Add(_statusRam);
            _statusStrip.Items.Add(_statusUptime);
            _statusStrip.Items.Add(_statusResourceSep);
            _statusStrip.Items.Add(_statusClicks);
            _statusStrip.Items.Add(new ToolStripSeparator());
            _statusStrip.Items.Add(_statusCps);
            _statusStrip.Items.Add(_statusPeak);
            _statusStrip.Items.Add(new ToolStripSeparator());
            _statusStrip.Items.Add(_statusElapsed);

            StyleStatusBar();

            // Order matters for docking: status strip bottom first, header top
            // next, the left sidebar claims the left of the remaining area, then the
            // tab control fills what's left.
            Controls.Add(_tabs);
            Controls.Add(_sidebar);
            _footerGif = new GifBackdropPanel { Dock = DockStyle.Bottom, Height = 46, Visible = false };
            Controls.Add(_footerGif);
            Controls.Add(_header);
            Controls.Add(_statusStrip);

            // Keep the sidebar docked to the *inner* (post header/footer) region so it
            // sits between the header and the status bar rather than spanning the
            // whole window height.
            Controls.SetChildIndex(_sidebar, 1);

            SetupTray();

            // Custom notification pop-ups: create the stack manager now so any
            // notification raised during start-up already gets the animated card.
            _notifications = new UI.NotificationCenter(
                this,
                () => BuildActiveTheme(),
                () => _settings != null ? _settings.NotificationCorner : 0,
                () => (_settings != null ? Math.Max(2, Math.Min(20, _settings.NotificationDurationSeconds)) : 5) * 1000);

            _uiTimer = new System.Windows.Forms.Timer { Interval = 200 };
            _uiTimer.Tick += (s, e) =>
            {
                UpdateLiveDisplays();
                WatchExternalLiveCaptions();
                MaybeShowDeferredUpdate();
                // Released the moment the fullscreen app that was in the way closes.
                if (_welcomeDeferred && !_officialNoticeAttempted) { MaybeShowOfficialSourceNotice(); }
            };
            _uiTimer.Start();

            _holdPollTimer = new System.Windows.Forms.Timer { Interval = 15 };
            _holdPollTimer.Tick += (s, e) => PollHoldKey();
        }

        private void BuildHeader()
        {
            _header = new BrandHeader();

            // ── Custom window chrome ───────────────────────────────────────────
            // Windows won't let an app restyle the system caption buttons, so Tempo goes
            // borderless and the header becomes the title bar, drawing its own ─ □ ✕.
            // Everything a title bar has to DO — dragging, Aero Snap, double-click to
            // maximise, edge resizing — is still handled by Windows, because WndProc
            // answers WM_NCHITTEST with the right zones rather than reimplementing any
            // of it by hand. Set before the handle exists so there's no visible reflow.
            _customChrome = true;
            FormBorderStyle = FormBorderStyle.None;

            // Escape must always be able to leave full screen. The diagnostic breadcrumb
            // in ProcessCmdKey proved Escape was NOT reaching the form at all — a focused
            // child was consuming it first — so the window was stuck in full screen with
            // only F11 to get out. KeyPreview routes every key through the form BEFORE
            // the focused control sees it, and OnKeyDown below acts as the backstop.
            KeyPreview = true;
            _header.ShowCaptionButtons = true;
            _header.CaptionButtonClicked += OnCaptionButtonClicked;

            // The profile caption is drawn by the header itself (see
            // BrandHeader.ProfileText) rather than hosted as a transparent child
            // Label — a transparent Label over the owner-drawn header rendered as a
            // mismatched dark box against the gradient.
            _statePill = new StatusPill
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Width = 110,
                Height = 28,
                Text = Localization.T("IDLE")
            };

            _header.Controls.Add(_statePill);

            _header.Resize += (s, e) => LayoutHeader();
            LayoutHeader();
        }

        /// <summary>Toggles borderless full-screen (bound to F11; Esc also exits).</summary>
        internal void ToggleFullScreen()
        {
            if (!_isFullScreen)
            {
                // Self-heal: _isFullScreen says "normal" but the window is still
                // borderless — a previous EXIT failed part-way (one silent exception
                // used to abandon the whole restore sequence, leaving a window with
                // no title bar, no minimum size, and a glitched layout — the
                // reported screenshots). Repair instead of stacking full-screen on
                // top of a broken state, which would also poison the saved
                // "previous" values with full-screen ones.
                // NOTE: this self-heal reads "borderless" as "a previous exit failed".
                // That is only true WITHOUT custom chrome — with it, borderless is the
                // normal state, so this fired on every F11 and "repaired" a window that
                // was perfectly fine instead of going full screen. _isFullScreen is the
                // honest signal for a half-finished transition either way.
                if (!_customChrome && FormBorderStyle == FormBorderStyle.None)
                {
                    Utils.Logger.Warn("[UI] window was left border-less by a failed full-screen exit — repairing.");
                    RestoreFromFullScreen();
                    return;
                }
                try
                {
                    _fsPrevBorder = FormBorderStyle;
                    _fsPrevState = WindowState;
                    // While MAXIMISED, Bounds is the maximised rectangle — not the one
                    // Windows restores to. Saving it meant "Restore Down" after F11
                    // in-and-out handed back a screen-sized window at 0,0 whose bottom
                    // (status strip, the Macros LIVE MONITOR card) sat under the taskbar.
                    // RestoreBounds is the real restore rect once the form has left Normal.
                    _fsPrevBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                    _fsPrevTopMost = TopMost;
                    _fsPrevMinSize = MinimumSize;

                    // Pick the screen the window is currently on.
                    Rectangle screen = Screen.FromRectangle(Bounds).Bounds;

                    // Order matters: leave maximised state, drop the border, lift any
                    // minimum-size constraint, then cover the whole screen on top of
                    // the taskbar.
                    if (WindowState != FormWindowState.Normal)
                    {
                        WindowState = FormWindowState.Normal;
                    }
                    MinimumSize = new Size(0, 0);
                    FormBorderStyle = FormBorderStyle.None;
                    TopMost = true;
                    Bounds = screen;
                    _isFullScreen = true;

                    // Nothing to minimise/restore in full screen, and the buttons would
                    // sit over the content — Esc/F11 is the documented way out.
                    if (_customChrome && _header != null)
                    {
                        _header.ShowCaptionButtons = false;
                        LayoutHeader();
                        _header.Invalidate();
                    }

                    // Make the GIF footer band more prominent in full-screen (only if
                    // a GIF is set, so an empty band never grows for no reason).
                    if (_footerGif != null && _footerGif.HasGif)
                    {
                        _fsPrevFooterHeight = _footerGif.Height;
                        _footerGif.Height = Math.Min(160, Math.Max(120, screen.Height / 6));
                    }

                    // Briefly tell the user how to get back out — borderless
                    // full-screen has no close button, and not everyone knows F11.
                    ShowFullScreenToast();
                }
                catch (Exception ex)
                {
                    // A failed ENTER must not strand a half-fullscreen window: put
                    // everything back and stay in normal mode.
                    Utils.Logger.Warn("[UI] full-screen enter failed (" + ex.Message + ") — restoring window.");
                    _isFullScreen = true;      // so the restore path runs fully
                    RestoreFromFullScreen();
                    return;
                }
            }
            else
            {
                RestoreFromFullScreen();
                return;                        // RestoreFromFullScreen re-lays-out
            }

            // After the window finishes resizing, re-lay-out and force-repaint every
            // page (RepairLayoutAfterRestore also retries until the size settles) so
            // no stale pixels from the transition survive anywhere.
            try
            {
                BeginInvoke((Action)RepairLayoutAfterRestore);
            }
            catch { }
        }

        /// <summary>
        /// Leaves borderless full-screen, restoring border, minimum size, bounds and
        /// top-most EVEN IF individual steps fail — every restore is independent, so
        /// one exception can no longer abandon the window in a half-restored state
        /// (no title bar, no minimum size, stale layout). Falls back to sane defaults
        /// when the saved "previous" values are unusable.
        /// </summary>
        private void RestoreFromFullScreen()
        {
            _isFullScreen = false;

            // Bring the custom caption buttons back (and re-reserve their strip).
            if (_customChrome && _header != null)
            {
                _header.ShowCaptionButtons = true;
                try { LayoutHeader(); } catch { }
                _header.Invalidate();
            }
            try { if (_fsToast != null) _fsToast.Visible = false; } catch { }

            // Restore the GIF footer band height.
            try
            {
                if (_footerGif != null && _fsPrevFooterHeight > 0)
                {
                    _footerGif.Height = _fsPrevFooterHeight;
                    _fsPrevFooterHeight = -1;
                }
            }
            catch { }

            try
            {
                // A poisoned saved border (a failed exit followed by another enter used
                // to save None as "previous") must never be restored verbatim — EXCEPT
                // with custom chrome, where None is the correct normal state. Forcing
                // Sizable there would hand the system title bar back after one F11 and
                // leave the window with two title bars.
                FormBorderStyle = _customChrome
                    ? FormBorderStyle.None
                    : (_fsPrevBorder == FormBorderStyle.None
                        ? FormBorderStyle.Sizable : _fsPrevBorder);
            }
            catch (Exception ex) { Utils.Logger.Warn("[UI] border restore failed: " + ex.Message); }

            try
            {
                float uiScale = CurrentAutoScaleDimensions.Width / 96f;
                // Same work-area clamp as OnLoad — a saved minimum from a bigger monitor
                // must not be restored onto a smaller one either (undocking a laptop).
                Size fallbackMin = ScaledMinimumSize(uiScale);
                Size restored = _fsPrevMinSize.Width > 0 && _fsPrevMinSize.Height > 0
                    ? _fsPrevMinSize : fallbackMin;
                Rectangle wa = Screen.FromControl(this).WorkingArea;
                MinimumSize = new Size(
                    Math.Min(restored.Width, Math.Max(1, wa.Width)),
                    Math.Min(restored.Height, Math.Max(1, wa.Height)));
            }
            catch (Exception ex) { Utils.Logger.Warn("[UI] minimum-size restore failed: " + ex.Message); }

            try { TopMost = _fsPrevTopMost; } catch { }

            try
            {
                // Write the bounds while the window is still Normal (full screen is a
                // borderless NORMAL window, never Maximized). Entering full screen did
                // `Bounds = screen`, which overwrote the OS restore rectangle — putting
                // the real one back HERE, before the state is re-applied, is what stops a
                // previously maximised window from restoring down to a screen-sized
                // window with its bottom under the taskbar. Runs for every previous
                // state, not just Normal.
                Rectangle screen = Screen.FromRectangle(Bounds).Bounds;
                bool sane = _fsPrevBounds.Width > 0 && _fsPrevBounds.Height > 0 &&
                            (_fsPrevBounds.Width < screen.Width || _fsPrevBounds.Height < screen.Height);
                if (WindowState != FormWindowState.Normal)
                {
                    WindowState = FormWindowState.Normal;
                }
                if (sane)
                {
                    Bounds = _fsPrevBounds;
                }
                else
                {
                    // A poisoned saved rectangle (screen-sized or empty) gets a sane
                    // centred default instead of a border-less-looking full square.
                    Size = new Size(Math.Max(MinimumSize.Width, 1020), Math.Max(MinimumSize.Height, 824));
                    CenterToScreen();
                }
            }
            catch (Exception ex) { Utils.Logger.Warn("[UI] bounds restore failed: " + ex.Message); }

            try { WindowState = _fsPrevState; } catch { }

            // Restore the user's always-on-top preference precisely.
            try { if (_settings != null) { TopMost = _settings.AlwaysOnTop; } } catch { }

            try
            {
                BeginInvoke((Action)RepairLayoutAfterRestore);
            }
            catch { }
        }

        internal bool IsFullScreen => _isFullScreen;

        private Label _fsToast;
        private System.Windows.Forms.Timer _fsToastTimer;

        /// <summary>
        /// Shows "Full screen — press F11 or Esc to exit" top-centre for a few
        /// seconds after entering full-screen, then hides itself.
        /// </summary>
        private void ShowFullScreenToast()
        {
            try
            {
                if (_fsToast == null)
                {
                    _fsToast = new Label
                    {
                        AutoSize = false,
                        Size = new Size(340, 34),
                        TextAlign = ContentAlignment.MiddleCenter,
                        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                        Visible = false
                    };
                    Controls.Add(_fsToast);
                    _fsToastTimer = new System.Windows.Forms.Timer { Interval = 2800 };
                    _fsToastTimer.Tick += (s, e) =>
                    {
                        _fsToastTimer.Stop();
                        if (_fsToast != null) _fsToast.Visible = false;
                    };
                }

                _fsToast.BackColor = _theme != null ? _theme.Surface2 : SystemColors.ControlDark;
                _fsToast.ForeColor = _theme != null ? _theme.Text : SystemColors.ControlText;
                _fsToast.Text = Localization.T("Full screen  \u2014  press F11 or Esc to exit");
                _fsToast.Left = (ClientSize.Width - _fsToast.Width) / 2;
                _fsToast.Top = 56;
                _fsToast.Visible = true;
                _fsToast.BringToFront();
                _fsToastTimer.Stop();
                _fsToastTimer.Start();
            }
            catch
            {
                // Cosmetic only.
            }
        }

        /// <summary>
        /// Makes the fixed-width tab content sit centred when the window is wider than
        /// the content (e.g. maximised or full-screen), instead of clinging to the
        /// top-left corner. Only horizontal offsets change — nothing is resized — so
        /// layouts stay intact, and narrow windows keep their normal scroll behaviour.
        /// </summary>
        private void EnableAutoFit()
        {
            if (_tabs == null)
            {
                return;
            }
            foreach (TabPage page in _tabs.TabPages)
            {
                TabPage p = page;
                foreach (Control c in p.Controls)
                {
                    if (!_autoFitBaseLeft.ContainsKey(c))
                    {
                        _autoFitBaseLeft[c] = c.Left;
                        _autoFitBaseTop[c] = c.Top;
                    }
                }
                p.SizeChanged += (s, e) => CenterPageContent(p);
                // SizeChanged alone misses one case: when the vertical scrollbar
                // appears or disappears, only ClientSize changes (Size stays the
                // same) — content stays centred for the wrong width and can poke
                // out by the scrollbar's width. Recentring on ClientSizeChanged
                // closes that gap. (CenterPageContent only moves children's Left/Top,
                // which can't alter ClientSize, so this cannot loop.)
                p.ClientSizeChanged += (s, e) => CenterPageContent(p);
                CenterPageContent(p);
            }
        }

        /// <summary>
        /// Builds the left navigation sidebar — one rounded "card" button per tab,
        /// stacked vertically — that drives <c>_tabs.SelectedIndex</c>. This replaces
        /// the old horizontal tab strip across the top.
        /// </summary>
        /// <summary>App version like "v1.0.251" for the sidebar footer stamp.</summary>
        private static string VersionStamp()
        {
            try
            {
                var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return v != null ? "v" + v.Major + "." + v.Minor + "." + v.Build : "";
            }
            catch { return ""; }
        }

        private void BuildSidebar()
        {
            if (_sidebar == null || _tabs == null)
            {
                return;
            }

            _sidebar.Controls.Clear();
            _navButtons.Clear();

            int top = _sidebar.Padding.Top;
            int width = _sidebar.Width - _sidebar.Padding.Left - _sidebar.Padding.Right;
            const int btnHeight = 44;
            const int gap = 8;

            // Tab order is fixed (Clicker, Profiles, Multi-Point, Macros, Statistics,
            // Keybinds, Captions, Settings), so map a recognisable icon to each by
            // position. Keep this in step with the StartupStep order that builds them.
            NavIconKind[] icons =
            {
                NavIconKind.Cursor, NavIconKind.Profile, NavIconKind.Points,
                NavIconKind.Macro, NavIconKind.Chart, NavIconKind.Keyboard,
                NavIconKind.Caption, NavIconKind.Gear
            };

            for (int i = 0; i < _tabs.TabPages.Count; i++)
            {
                int index = i;
                var nav = new RoundedButton
                {
                    Text = _tabs.TabPages[i].Text,
                    Left = _sidebar.Padding.Left,
                    Top = top,
                    Width = width,
                    Height = btnHeight,
                    CornerRadius = 10,
                    IconKind = i < icons.Length ? icons[i] : NavIconKind.None,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(14, 0, 6, 0),
                    Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                    Cursor = Cursors.Hand,
                    // Ctrl+1…9 has always jumped straight to a tab, but nothing said so.
                    // The digit sits dimmed at the row's right edge — visible when you
                    // look for it, invisible when you're not.
                    ShortcutHint = i < 9 ? (i + 1).ToString() : null
                };
                nav.FlatAppearance.BorderSize = 0;
                // Screen readers announce the tab and its shortcut, not just "button".
                nav.AccessibleRole = AccessibleRole.PageTab;
                nav.AccessibleName = _tabs.TabPages[i].Text;
                if (i < 9)
                {
                    nav.AccessibleDescription = "Ctrl+" + (i + 1);
                }
                nav.Click += (s, e) =>
                {
                    if (index >= 0 && index < _tabs.TabPages.Count)
                    {
                        // Remember where the current tab is scrolled BEFORE we leave it.
                        // The sidebar changes SelectedIndex programmatically, which does
                        // NOT raise Deselecting, so this is where we must capture it.
                        SaveActiveTabScroll();
                        // Plain selection change. The native tab control briefly exposes
                        // its LIGHT visual-styles body while it swaps pages — the "labels
                        // and buttons flash light on every tab change" report — but that
                        // is killed by ModernTabControl now erasing WM_ERASEBKGND with the
                        // dark theme colour, plus the form's WS_EX_COMPOSITED. Do NOT wrap
                        // this in WM_SETREDRAW: freezing the control during the page swap
                        // re-sets WS_VISIBLE on the hidden pages when unfrozen and leaves
                        // the wrong page showing (the same mechanism as the grind bug).
                        _tabs.SelectedIndex = index;
                    }
                };
                _navButtons.Add(nav);
                _sidebar.Controls.Add(nav);
                top += btnHeight + gap;
            }

            RefreshSidebarSelection();
        }

        /// <summary>Highlights the sidebar button for the currently selected tab.</summary>
        private void RefreshSidebarSelection()
        {
            if (_tabs == null || _theme == null)
            {
                return;
            }
            int sel = _tabs.SelectedIndex;
            // Each tab's icon gets its own hue (dimmed a touch until selected) so
            // the sidebar reads at a glance instead of six identical grey glyphs.
            // Hues match the Live-debug category palette; a light theme gets the
            // saturated dark variants so they don't wash out.
            bool lightTheme = _theme.Background.GetBrightness() > 0.5f;
            Color[] iconHues = lightTheme
                ? new[]
                {
                    Color.FromArgb(180, 78, 22),    // Clicker — burnt orange
                    Color.FromArgb(12, 128, 84),    // Multi-Point — deep mint
                    Color.FromArgb(112, 66, 200),   // Macros — violet
                    Color.FromArgb(0, 100, 190),    // Statistics — deep sky blue
                    Color.FromArgb(150, 108, 0),    // Keybinds — dark gold
                    Color.FromArgb(24, 116, 130),    // Captions — deep teal
                    Color.FromArgb(160, 44, 128)    // Settings — orchid
                }
                : new[]
                {
                    Color.FromArgb(255, 170, 120),  // Clicker — soft orange
                    Color.FromArgb(120, 230, 175),  // Multi-Point — mint
                    Color.FromArgb(190, 165, 255),  // Macros — lavender
                    Color.FromArgb(120, 200, 255),  // Statistics — sky blue
                    Color.FromArgb(240, 205, 120),  // Keybinds — gold
                    Color.FromArgb(125, 220, 225),  // Captions — teal
                    Color.FromArgb(235, 150, 210)   // Settings — orchid
                };
            for (int i = 0; i < _navButtons.Count; i++)
            {
                RoundedButton nav = _navButtons[i];
                nav.FlatAppearance.BorderSize = 0;
                nav.AccentColor = _theme.Accent;
                // The sidebar sits on the page background; selected buttons get an
                // accent wash + left indicator bar + accent text (handled by the
                // button), unselected ones read as muted, flat list items.
                nav.BackColor = _theme.Background;
                nav.Selected = (i == sel);
                nav.ForeColor = (i == sel) ? _theme.Accent : _theme.TextMuted;
                Color hue = i < iconHues.Length ? iconHues[i] : Color.Empty;
                // Unselected icons sit at ~72% toward the hue from the muted text
                // colour — coloured but calm; the selected one shows its full hue.
                nav.IconColor = hue.IsEmpty ? Color.Empty
                    : (i == sel) ? hue
                    : Color.FromArgb(
                        (int)(_theme.TextMuted.R + (hue.R - _theme.TextMuted.R) * 0.72),
                        (int)(_theme.TextMuted.G + (hue.G - _theme.TextMuted.G) * 0.72),
                        (int)(_theme.TextMuted.B + (hue.B - _theme.TextMuted.B) * 0.72));
                nav.Invalidate();
            }
        }

        // See the re-entrancy guard inside CenterPageContent.
        private bool _centeringBusy;

        private void CenterPageContent(TabPage page)
        {
            CenterPageContent(page, false);
        }

        private void CenterPageContent(TabPage page, bool force)
        {
            // Never lay out while minimised. When the window is minimised (e.g. macro
            // recording auto-minimises it) the client size collapses, and measuring or
            // moving controls / touching the scroll position against that bogus size is
            // exactly what left the Statistics dashboard pushed far down with a huge
            // empty gap above it. We re-lay-out cleanly on restore instead (OnResize).
            if (WindowState == FormWindowState.Minimized)
            {
                return;
            }

            int minLeft = int.MaxValue;
            int maxRight = 0;
            foreach (Control c in page.Controls)
            {
                if (!_autoFitBaseLeft.TryGetValue(c, out int baseLeft))
                {
                    continue;
                }
                if (baseLeft < minLeft) minLeft = baseLeft;
                if (baseLeft + c.Width > maxRight) maxRight = baseLeft + c.Width;
            }
            if (minLeft == int.MaxValue)
            {
                return;
            }

            // Use the page's own client width — that is the exact width AutoScroll
            // measures against when it decides whether a horizontal scrollbar is
            // needed, and it already accounts for a vertical scrollbar when one is
            // showing. (Going through the tab's DisplayRectangle instead left a ~17px
            // mismatch whenever the vertical bar appeared, which is what intermittently
            // produced the stray bottom scrollbar.)
            int available = page.ClientSize.Width;
            if (available <= 0)
            {
                return;
            }

            // Skip when nothing that affects centring has changed, UNLESS forced.
            // Centring depends on the client WIDTH; scrolling up/down fires
            // ClientSizeChanged (the scrollbar toggles) without changing that width, so
            // for scroll events we skip to avoid repositioning every control mid-scroll
            // and momentarily blanking the page. But an explicit tab switch passes
            // force=true so the freshly shown tab is always given a clean, complete
            // re-layout (positions + AutoScrollMinSize + scroll reset) - otherwise a tab
            // could come back empty because its scroll metrics were left stale.
            if (!force
                && !_isFullScreen
                && _lastCenteredWidth.TryGetValue(page, out int prevW)
                && prevW == available)
            {
                return;
            }

            int contentWidth = maxRight - minLeft;
            int offset = (available - contentWidth) / 2 - minLeft;
            if (offset < 0) offset = 0;

            // Vertical centring is ONLY for true full-screen mode (F11), where a
            // short page would otherwise leave a tall empty band at the bottom.
            // In a normal or maximised window we must NEVER add a top offset - doing
            // so created a big empty gap above the content that you had to scroll
            // past (the reported bug). So outside full screen, content always starts
            // at the top and the page scrolls normally.
            int offsetY = 0;
            if (_isFullScreen)
            {
                int minTop = int.MaxValue;
                int maxBottom = 0;
                foreach (Control c in page.Controls)
                {
                    if (!_autoFitBaseTop.TryGetValue(c, out int bTop))
                    {
                        continue;
                    }
                    if (bTop < minTop) minTop = bTop;
                    if (bTop + c.Height > maxBottom) maxBottom = bTop + c.Height;
                }
                int availableH = page.ClientSize.Height;
                if (minTop != int.MaxValue && availableH > 0)
                {
                    int contentHeight = maxBottom - minTop;
                    // Only centre when there is clear headroom; never when content is
                    // near or over the page height (that is the scroll case).
                    if (contentHeight + 40 < availableH)
                    {
                        offsetY = (availableH - contentHeight) / 2 - minTop;
                        if (offsetY < 0) offsetY = 0;
                    }
                }
            }

            // Hard guarantee: never let the right-most control spill past the client
            // width. If it would, pull everything back so it fits exactly. With no
            // control past the right edge, AutoScroll has nothing to scroll sideways,
            // so the horizontal scrollbar simply never appears — no OS-level hacks, no
            // fighting AutoScroll, no intermittent behaviour.
            if (maxRight + offset > available)
            {
                offset = available - maxRight;
            }
            if (offset < 0) offset = 0;

            // Repositioning child controls inside an AutoScroll page makes WinForms
            // snap the view back to the top. Remember the scroll position and put it
            // back so the user stays where they were.
            var scrollable = page as ScrollableControl;
            Point savedScroll = scrollable != null ? scrollable.AutoScrollPosition : Point.Empty;

            // Freeze painting for the ENTIRE reposition + scroll-restore below so it commits
            // as one repaint — no momentary flash to the top before the remembered scroll is
            // put back. There are no early returns between here and the matching
            // SetRedraw(page, true) at the end of the method.
            //
            // CRITICAL: only ever toggle redraw on the page that is ACTUALLY ON SCREEN. On a
            // monitor wake / resolution change RecenterAllPages() calls this for EVERY page,
            // including the hidden (non-selected) ones. WM_SETREDRAW(TRUE) re-sets the
            // WS_VISIBLE style bit as a side effect, which natively UN-HIDES a hidden page —
            // and the top-most one in z-order (Clicker, added first) then paints over the
            // page you're actually on. That is the reported "after the 8-hour macro grind the
            // Macros tab was showing the Clicker page" bug. Hidden pages aren't painting, so
            // they don't need the freeze at all; skipping it leaves them correctly hidden.
            bool pageShown = Visible && _tabs != null && ReferenceEquals(page, _tabs.SelectedTab);

            // Re-entrancy guard: the moves below fire SizeChanged/ClientSizeChanged,
            // whose handlers call straight back in here. An interleaved pass toggled
            // WM_SETREDRAW out of order, which could leave the page frozen — never
            // repainting again, showing stale pixels of the OLD layout mixed with
            // moved controls (the reported "duplicated/overlapping cards" glitch).
            if (_centeringBusy)
            {
                return;
            }
            _centeringBusy = true;

            // Mark the page centred at this width ONLY now that we're committed to
            // actually doing it. This used to be recorded ~80 lines earlier, before the
            // re-entrancy bail above: a call that turned back here still left the cache
            // saying "already centred at this width", so the next non-forced pass hit the
            // skip-guard and the page kept a half-applied layout — cards left where an
            // interrupted pass had put them.
            _lastCenteredWidth[page] = available;
            if (pageShown)
            {
                SetRedraw(page, false);
            }
            // Did this pass actually change anything on screen? The thaw below used to
            // invalidate the page unconditionally, which quietly defeated the caller's
            // own "only repaint if the layout changed" check: the deferred post-switch
            // pass compares the layout before and after and skips its repaint when
            // nothing moved — but this method had already marked the whole page dirty,
            // so the repaint happened anyway on every single tab switch.
            bool changedSomething = false;
            Point scrollBefore = scrollable != null ? scrollable.AutoScrollPosition : Point.Empty;
            try
            {

            // Reposition children in UNSCROLLED coordinates. A child's Left/Top are in the
            // panel's scroll-offset client space, so setting Top to its base (unscrolled)
            // position while the page is scrolled shoves the content back to the top - which
            // is exactly what kept fighting the per-tab scroll restore. Reset the scroll to
            // 0 first, move everything, then re-apply the intended scroll below.
            if (scrollable != null && savedScroll != Point.Empty)
            {
                scrollable.AutoScrollPosition = Point.Empty;
            }

            // Batch the child moves so the page lays out and repaints once, not once per
            // control — that per-control repaint storm is what made re-centring flicker
            // (and momentarily blank) on resize / restore.
            page.SuspendLayout();
            foreach (Control c in page.Controls)
            {
                if (_autoFitBaseLeft.TryGetValue(c, out int baseLeft))
                {
                    int want = baseLeft + offset;
                    if (c.Left != want) { c.Left = want; changedSomething = true; }
                }
                if (_autoFitBaseTop.TryGetValue(c, out int baseTop))
                {
                    int want = baseTop + offsetY;
                    if (c.Top != want) { c.Top = want; changedSomething = true; }
                }
            }
            page.ResumeLayout(false);

            if (scrollable != null)
            {
                // Force the AutoScroll range to recompute against the new child
                // positions before we restore the scroll offset; without this the
                // page could keep a stale (too-small) scrollable range and refuse
                // to scroll to the bottom in full screen.
                scrollable.PerformLayout();

                // In full-screen, when a short page has been vertically centred the
                // pushed-down controls would extend the scroll range into empty space
                // below (you could scroll past the content into a void). Clamp the
                // minimum scroll size to the visible client size in that case so there
                // is nothing extra to scroll into. In every other case clear the clamp
                // and let AutoScroll measure normally.
                if (_isFullScreen && offsetY > 0)
                {
                    scrollable.AutoScrollMinSize = new Size(0, scrollable.ClientSize.Height);
                }
                else
                {
                    scrollable.AutoScrollMinSize = Size.Empty;
                }

                if (force && !_isFullScreen)
                {
                    // A forced re-centre happens on a tab switch: restore the scroll
                    // position this tab had last time (or top on first visit). The page
                    // has just been laid out above, so its scroll range is established and
                    // AutoScroll clamps the value - no "parked low with an empty band".
                    ApplyTabScroll(page);
                }
                else
                {
                    scrollable.AutoScrollPosition = new Point(-savedScroll.X, -savedScroll.Y);
                }

                // Diagnostic: log the layout numbers on a forced re-centre (a tab switch -
                // infrequent, so this won't spam) for EVERY tab, not just Statistics. If a
                // tab ever comes up blank or pushed down again, this records exactly what
                // the layout saw - which page, client size, full-screen flag, vertical
                // offset, the scroll it started from, and where the content ended up.
                if (force)
                {
                    try
                    {
                        int firstTop = -1, lastBottom = -1;
                        foreach (Control c in page.Controls)
                        {
                            if (_autoFitBaseTop.ContainsKey(c))
                            {
                                if (firstTop < 0) firstTop = c.Top;
                                if (c.Bottom > lastBottom) lastBottom = c.Bottom;
                            }
                        }
                        string tab = page is TabPage tp ? tp.Text : page.GetType().Name;
                        Rectangle disp = scrollable.DisplayRectangle;
                        Logger.Info("[layout] " + tab + " centre: client="
                            + page.ClientSize.Width + "x" + page.ClientSize.Height
                            + " fullscreen=" + _isFullScreen + " offsetY=" + offsetY
                            + " savedScroll=" + savedScroll
                            + " scrollNow=" + scrollable.AutoScrollPosition
                            + " firstCtrlTop=" + firstTop + " lastCtrlBottom=" + lastBottom
                            + " minSize=" + scrollable.AutoScrollMinSize
                            + " disp=" + disp.Y + ":" + disp.Height);
                    }
                    catch { }
                }
            }

            }
            finally
            {
                // Unfreeze and commit the whole reposition + scroll-restore in ONE
                // repaint. Only the on-screen page was frozen (see above), so only it
                // is thawed — a hidden page is never sent WM_SETREDRAW(TRUE) and so is
                // never un-hidden. In a FINALLY on purpose: an exception anywhere in
                // the moves above used to skip the thaw and freeze the page forever
                // (the stale-pixels glitch); now the page always paints again.
                if (pageShown)
                {
                    SetRedraw(page, true);
                    // Only when this pass moved something, or the scroll offset changed.
                    // Repainting a page that is already correct is the single most common
                    // outcome here (every tab switch runs a forced pass that usually finds
                    // nothing to fix), and with a wallpaper that repaint is the expensive
                    // whole-window kind.
                    if (changedSomething ||
                        (scrollable != null && scrollable.AutoScrollPosition != scrollBefore))
                    {
                        page.Invalidate(true);
                    }
                }
                _centeringBusy = false;
            }
        }

        /// <summary>
        /// Reads the active tab's scroll position (returns the value as the
        /// <see cref="ScrollableControl.AutoScrollPosition"/> getter reports it, i.e.
        /// with negative offsets when scrolled).
        /// </summary>
        /// <summary>Saves the currently-shown tab's scroll position into the memory.</summary>
        private void SaveActiveTabScroll()
        {
            if (_tabs?.SelectedTab is ScrollableControl sc)
            {
                _tabScroll[_tabs.SelectedTab] = sc.AutoScrollPosition;
            }
        }

        /// <summary>
        /// Restores a tab's remembered scroll position (or the top, on first visit).
        /// Must be called AFTER the page is laid out so its scroll range is established
        /// and the value is clamped to something valid. AutoScrollPosition is stored as
        /// WinForms reports it (negative offsets), so it's re-applied negated.
        /// </summary>
        private void ApplyTabScroll(TabPage page)
        {
            if (!(page is ScrollableControl sc))
            {
                return;
            }
            if (_tabScroll.TryGetValue(page, out Point saved))
            {
                sc.AutoScrollPosition = new Point(-saved.X, -saved.Y);
            }
            else
            {
                sc.AutoScrollPosition = new Point(0, 0);
            }
        }

        private Point CaptureActiveScroll()
        {
            var p = _tabs != null ? _tabs.SelectedTab as ScrollableControl : null;
            return p != null ? p.AutoScrollPosition : Point.Empty;
        }

        /// <summary>
        /// Restores a scroll position captured by <see cref="CaptureActiveScroll"/>.
        /// Live updates (status text, stats cards, etc.) can make a page jump to the
        /// top; re-asserting the position keeps the user where they were. It's a no-op
        /// when nothing actually moved.
        /// </summary>
        private void RestoreActiveScroll(Point saved)
        {
            var p = _tabs != null ? _tabs.SelectedTab as ScrollableControl : null;
            if (p != null)
            {
                p.AutoScrollPosition = new Point(-saved.X, -saved.Y);
            }
        }

        /// <summary>Re-centres every tab page (used after a full-screen toggle).</summary>
        // Tracks the last OS light/dark state we applied, so a UserPreferenceChanged
        // storm (Windows fires several) only re-themes when the value actually flips.
        private bool _lastOsLight;
        private bool _lastOsLightKnown;
        // Last Windows accent we applied, so an accent-only change (light/dark unmoved)
        // still re-themes when "Match Windows" is adopting the OS accent.
        private int _lastOsAccentArgb;

        /// <summary>
        /// Windows fired a preference change. When "Match Windows" is on and either the
        /// OS light/dark app mode OR the Windows accent colour has actually changed,
        /// re-theme Tempo live.
        /// </summary>
        private void OnUserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
        {
            try
            {
                if (_settings == null || !_settings.FollowSystemTheme)
                {
                    return;
                }
                if (e.Category != Microsoft.Win32.UserPreferenceCategory.General &&
                    e.Category != Microsoft.Win32.UserPreferenceCategory.VisualStyle &&
                    e.Category != Microsoft.Win32.UserPreferenceCategory.Color)
                {
                    return;
                }
                bool light = Utils.SystemTheme.IsWindowsLight();
                // Only track the accent when it can actually influence the look — a
                // neutral Light/Dark pick with no custom accent. That mirrors exactly
                // when BuildActiveTheme adopts the Windows accent, so a colourful theme
                // (whose accent never follows the OS) doesn't churn on every colour tick.
                bool accentMatters = !_settings.CustomAccentEnabled &&
                    (_settings.Theme == ThemeKind.Light || _settings.Theme == ThemeKind.Dark);
                int accent = accentMatters ? Utils.SystemTheme.CurrentAccentArgb() : 0;

                bool lightSame = _lastOsLightKnown && light == _lastOsLight;
                bool accentSame = accent == _lastOsAccentArgb;
                if (lightSame && accentSame)
                {
                    return;   // no real change
                }
                _lastOsLight = light;
                _lastOsLightKnown = true;
                _lastOsAccentArgb = accent;

                if (IsHandleCreated && !IsDisposed)
                {
                    BeginInvoke((Action)(() =>
                    {
                        try { ApplyThemeToEverything(); } catch { }
                    }));
                }
            }
            catch { }
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            // Marshal onto the UI thread and give Windows a moment to settle the new
            // resolution before re-laying-out (a game exiting can fire several of
            // these in quick succession).
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke((Action)(() =>
                {
                    try
                    {
                        // While minimised (e.g. the 8-hour AFK macro grind with the
                        // window auto-minimised), monitor sleep/wake fires this event
                        // repeatedly and the client size is bogus — don't lay out
                        // against it. The restore path (OnResize →
                        // RepairLayoutAfterRestore) re-centres everything anyway.
                        if (WindowState != FormWindowState.Minimized)
                        {
                            // A game exiting can leave a SMALLER desktop than the window
                            // was sized for; pull it back inside so the status bar and
                            // the sidebar's version stamp don't end up off-screen.
                            ClampToWorkArea();
                            RecenterAllPages();
                        }
                        // Caption overlays are manual top-most windows; nudge them back
                        // on-screen too in case the resolution shrank under them.
                        if (_captionOverlay != null && !_captionOverlay.IsDisposed && _captionOverlay.Visible)
                        {
                            _captionOverlay.EnsureOnScreen();
                        }
                        if (_captionHistoryForm != null && !_captionHistoryForm.IsDisposed && _captionHistoryForm.Visible)
                        {
                            _captionHistoryForm.EnsureOnScreen();
                        }
                    }
                    catch { }
                }));
            }
            catch { }
        }

        private void RecenterAllPages()
        {
            if (_tabs == null)
            {
                return;
            }
            // Clear the cached widths so every page is genuinely re-centred here
            // (this is called for real layout changes like a resolution change or
            // toggling full screen, not for scrolling).
            _lastCenteredWidth.Clear();
            foreach (TabPage page in _tabs.TabPages)
            {
                CenterPageContent(page);
            }
        }

        /// <summary>
        /// Repairs every page's layout after the window is restored from the tray/taskbar,
        /// but RETRIES until the window has actually finished restoring. A single deferred
        /// pass measured too early sometimes - while the client size was still 0 or the
        /// state still Minimized - so CenterPageContent hit its guard and bailed, leaving
        /// the page blank until the user nudged it again ("autofit didn't respond on
        /// return"). This waits for a real size, lays out, then does one confirmation pass.
        /// </summary>
        private void RepairLayoutAfterRestore()
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            // Fix it RIGHT NOW. When we get here from OnResize the restored client size
            // is almost always already valid, so re-centring + a forced repaint on the
            // spot means the page never sits blank waiting on a timer (the old code only
            // started repairing 70 ms later, after the user saw an empty screen).
            bool fixedNow = TryRepairLayoutNow();

            // A short backstop only for the rare case the size hasn't settled yet (a
            // confirmation pass also catches a size that nudges a touch after restore).
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer { Interval = 33 };
            int ticks = 0;
            int goodPasses = fixedNow ? 1 : 0;
            t.Tick += (s, e) =>
            {
                ticks++;
                if (TryRepairLayoutNow())
                {
                    goodPasses++;
                }
                if (goodPasses >= 2 || ticks >= 12)
                {
                    t.Stop();
                    t.Dispose();
                }
            };
            t.Start();
        }

        /// <summary>
        /// Re-centres every page and forces the active one to repaint immediately, so a
        /// restored window shows its content with no blank frame. Returns false (and does
        /// nothing) if the window size isn't valid yet, so the caller can retry.
        /// </summary>
        private bool TryRepairLayoutNow()
        {
            if (IsDisposed || WindowState == FormWindowState.Minimized ||
                ClientSize.Width <= 0 || ClientSize.Height <= 0)
            {
                return false;
            }
            try
            {
                // After hours minimised (monitor sleep/wake, DPI churn, session locks)
                // the NATIVE tab control can come back displaying a different page than
                // SelectedTab — the reported "wrong page (Clicker) shown after an
                // 8-hour macro grind". Exactly the selected page should be visible;
                // anything else is a desync. Repair it invisibly (painting frozen) by
                // forcing a selection round-trip, which makes WinForms re-hide every
                // other page and re-show the right one.
                ResyncTabSelection();

                RecenterAllPages(); // re-centres every page, preserving each one's scroll
                if (_tabs?.SelectedTab is ScrollableControl sc)
                {
                    sc.PerformLayout();
                    // Invalidate + Update paints synchronously, killing the "double-
                    // buffered AutoScroll page comes back blank" frame on restore. We do
                    // NOT reset the scroll here, so the page returns exactly where it was.
                    sc.Invalidate(true);
                    sc.Update();
                }
                _tabs?.Invalidate(true);
                // Re-assert the sidebar highlight so it can never sit on a different
                // tab than the one actually displayed after a restore.
                RefreshSidebarSelection();

                // Repaint the WHOLE window, not just the pages.
                //
                // Everything above only invalidates the tab control and its pages, which
                // left every other surface — the header, the sidebar, the footer GIF band
                // and the status strip — holding whatever Windows had in the buffer when
                // the window went down. Restoring then showed real corruption: a black
                // void over the bottom two-thirds of the window, the desktop icons behind
                // Tempo bleeding THROUGH the sidebar, and the header missing its logo,
                // profile caption and ─ □ ✕ entirely. (All three observed on restore.)
                //
                // These surfaces are transparency-backed — they paint the shared backdrop
                // themselves via PaintTransparentBackdrop — so a restore has to drop the
                // cached backdrop and redraw parent-then-child across the entire form,
                // not just the page that happens to be selected.
                InvalidateBackdropSurfaces();
                Invalidate(true);   // the form AND every child control
                Update();           // paint synchronously — never show the stale frame
            }
            catch
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Belt-and-suspenders repair for the native tab control showing a page other
        /// than <c>SelectedTab</c>: if a non-selected page has been natively un-hidden
        /// (e.g. an errant WM_SETREDRAW(TRUE) re-setting its WS_VISIBLE bit) it sits on
        /// top of the real page and shows the wrong tab. Hide any such page directly and
        /// make sure the selected one is on top. No-op when everything is consistent, so
        /// it's cheap to call on every restore. The true cause is fixed in
        /// CenterPageContent (which no longer freezes/thaws redraw on hidden pages);
        /// this guards against any other path that could leave a page mis-shown.
        /// </summary>
        private void ResyncTabSelection()
        {
            // Only meaningful while the form is actually visible: with the form hidden
            // (tray) every page reports not-visible and the check can't tell anything.
            if (!Visible || _tabs == null || _tabs.SelectedTab == null)
            {
                return;
            }

            TabPage sel = _tabs.SelectedTab;
            bool repaired = false;

            foreach (TabPage p in _tabs.TabPages)
            {
                if (ReferenceEquals(p, sel) || !p.IsHandleCreated)
                {
                    continue;
                }
                // A non-selected page that is natively visible is covering the real
                // page. Hide it. WinForms already considers it not-visible, so this only
                // re-syncs the native window state to what WinForms already believes.
                if (IsNativelyVisible(p))
                {
                    try { ShowWindow(p.Handle, SW_HIDE); repaired = true; } catch { }
                }
            }

            // Guarantee the page we should be on is actually shown (without stealing
            // activation from the form itself).
            if (sel.IsHandleCreated && !IsNativelyVisible(sel))
            {
                try { ShowWindow(sel.Handle, SW_SHOWNA); repaired = true; } catch { }
            }

            if (repaired)
            {
                try
                {
                    sel.BringToFront();
                    sel.Invalidate(true);
                }
                catch { }
                RefreshSidebarSelection();
            }
        }

        private bool _traySleepActive;

        /// <summary>
        /// Tray sleep: while the window is hidden in the tray AND nothing is
        /// running, global hotkeys and the cursor trail are paused so a forgotten
        /// Tempo can't start clicking invisibly hours later. The moment the window
        /// is shown again — or something starts from the tray menu — everything is
        /// re-registered. Never engages while clicking, playing or recording, so
        /// "hide to tray when clicking starts" keeps working exactly as before.
        /// </summary>
        private void UpdateTraySleepState()
        {
            if (_settings == null || _hotkeys == null || _shuttingDown)
            {
                return;
            }

            bool busy = (_engine != null && _engine.IsRunning) ||
                        (_player != null && _player.IsPlaying) ||
                        (_recorder != null && _recorder.IsRecording);
            bool shouldSleep = _settings.TraySleepEnabled && !Visible && !busy;
            if (shouldSleep == _traySleepActive)
            {
                return;
            }

            _traySleepActive = shouldSleep;
            try
            {
                if (shouldSleep)
                {
                    // Re-register the SAFE subset rather than dropping everything \u2014
                    // emergency stop and show/hide survive (see SurvivesTraySleep).
                    ApplyHotkeysFromSettings(sleepingInTray: true);
                    if (_cursorTrail != null)
                    {
                        _cursorTrail.Visible = false;
                    }
                    if (_trayIcon != null)
                    {
                        _trayIcon.Text = "Tempo \u2014 sleeping. Start/stop hotkeys are paused; " +
                                         "emergency stop and show/hide still work.";
                    }
                    Utils.Logger.Info("[Tray] sleep: start/playback hotkeys paused while hidden and idle " +
                                      "(emergency stop and show/hide stay bound).");
                }
                else
                {
                    ApplyHotkeysFromSettings();
                    ApplyCursorTrail(_settings.CursorTrailEnabled);
                    UpdateTrayTooltip();
                    Utils.Logger.Info("[Tray] sleep: woke up, hotkeys re-registered.");
                }
            }
            catch
            {
                // Never let the safety feature itself crash the app.
            }
        }

        private void UpdateGifAnimationState()
        {
            // One shared animator drives every backdrop surface. Pause it while the
            // window is hidden/minimised to save CPU; resume when it returns.
            bool active = WindowState != FormWindowState.Minimized && Visible;
            if (active)
            {
                StartSharedBgAnimation();
            }
            else
            {
                StopSharedBgAnimation();
            }
        }

        /// <summary>
        /// Activates the full-window backdrop on the currently visible page only and
        /// deactivates it on the rest, so a single animator runs at a time.
        /// </summary>
        private void UpdateBackdropActivePage()
        {
            if (_tabs == null)
            {
                return;
            }
            bool haveBg = _fullBgImage != null;

            // SelectedTab reads null until the tab control's HANDLE exists, and
            // ApplyBackgroundGif runs while the UI is still being built — so asking for
            // SelectedTab there matched no page at all and left every page inactive.
            // Nothing switched them on afterwards either (see RestoreLastTab), so the
            // page showing at startup painted the flat theme colour while the header,
            // sidebar and footer band all carried the wallpaper. Measured on the Clicker
            // tab: page RGB(40,42,54) — the bare Dracula background — against sidebar
            // RGB(83,35,43). Switching tabs once fixed it permanently, which is exactly
            // the "one region never shows the image" report.
            //
            // Fall back to the selected INDEX (then the first page) so the startup page
            // is resolved correctly no matter how early this runs.
            TabPage current = _tabs.SelectedTab;
            if (current == null && _tabs.TabPages.Count > 0)
            {
                int i = _tabs.SelectedIndex;
                current = i >= 0 && i < _tabs.TabPages.Count
                    ? _tabs.TabPages[i]
                    : _tabs.TabPages[0];
            }

            foreach (TabPage page in _tabs.TabPages)
            {
                if (page is BackdropTabPage bp)
                {
                    bp.SetActive(haveBg && ReferenceEquals(page, current));
                }
            }
        }

        private bool _bgAnimating;

        /// <summary>
        /// Loads ONE background image and shows it as a single seamless wallpaper across
        /// the whole window: the header, sidebar, every page and the footer band each
        /// paint their aligned slice of the same instance (see WindowBackdrop), and one
        /// shared animator drives them all so every region shows the same frame. The
        /// three legacy path slots collapse to one source (the full-window slot wins,
        /// then the older header/footer slots) so nothing a user already set disappears.
        /// </summary>
        private void ApplyBackgroundGif()
        {
            if (_header == null)
            {
                return;
            }

            string path = FirstNonEmpty(
                _settings?.FullBackgroundGifPath,
                _settings?.BackgroundGifPath,
                _settings?.BackgroundGifPath2);
            Image img = LoadGifImage(path);
            int dim = _settings != null ? _settings.BackgroundDim : 55;

            Image old = _fullBgImage;
            StopSharedBgAnimation();      // detaches the animator from the OLD image
            _fullBgImage = img;

            // Whole-window compositing is only worth its cost while a wallpaper is
            // actually showing (see ApplyCompositedForBackdrop). Match it to the image so
            // the common no-wallpaper case scrolls at full speed.
            // Record that a wallpaper exists, but DON'T switch compositing on here — it
            // is now armed per-scroll (NotifyBackdropScroll), because leaving it on cost
            // ~95% of a CPU core while the window just sat there. Clearing the wallpaper
            // drops it immediately.
            _wallpaperShowing = img != null;
            if (img == null)
            {
                ApplyCompositedForBackdrop(false);
            }

            // Hand the SAME instance to every surface — none of them own or dispose it.
            _header.SetSharedBackdrop(img, dim);
            if (_footerGif != null)
            {
                _footerGif.SetSharedBackdrop(img, dim);
                _footerGif.Visible = img != null;   // the bottom band only exists with a backdrop
            }
            _sidebar?.Invalidate();

            if (_tabs != null)
            {
                foreach (TabPage page in _tabs.TabPages)
                {
                    (page as BackdropTabPage)?.SetBackdrop(img, dim);
                }
                UpdateBackdropActivePage();

                // Page-level labels/checkboxes flip to a transparent background when a
                // wallpaper is present (so the image shows through them) and back to a
                // solid one when it's cleared.
                if (_theme != null)
                {
                    foreach (TabPage page in _tabs.TabPages)
                    {
                        ThemeManager.RefreshBackdropBackgrounds(page, _theme);
                    }
                }
            }

            StartSharedBgAnimation();     // one animator on the NEW image

            // Say so when a background is configured but its file is gone, instead of
            // leaving the user with a path in Settings and no wallpaper on screen.
            if (_bgGifNote != null && !_bgGifNote.IsDisposed)
            {
                bool configured = !string.IsNullOrWhiteSpace(path);
                if (configured && img == null)
                {
                    _bgGifNote.Text = Localization.T("file missing");
                    _bgGifNote.ForeColor = _theme != null ? _theme.Warning : _bgGifNote.ForeColor;
                }
                else
                {
                    _bgGifNote.Text = Localization.T("(experimental)");
                    _bgGifNote.ForeColor = _theme != null ? _theme.TextMuted : _bgGifNote.ForeColor;
                }
            }

            // The old image is now detached from every surface and the animator, so it
            // is safe to release. Without this, repeatedly changing the background would
            // leak GDI+ image handles.
            if (old != null && !ReferenceEquals(old, img))
            {
                try { old.Dispose(); } catch { }
            }
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values != null)
            {
                foreach (string v in values)
                {
                    if (!string.IsNullOrWhiteSpace(v)) { return v; }
                }
            }
            return null;
        }

        /// <summary>
        /// Starts the ONE animator that drives the shared window backdrop. A single
        /// animator (not one per surface) means every region advances to the same frame
        /// together — so the seam between header, page and footer stays invisible — and
        /// avoids the "one image, several animators → plays too fast" bug.
        /// </summary>
        private void StartSharedBgAnimation()
        {
            if (_bgAnimating || _fullBgImage == null)
            {
                return;
            }
            if (WindowState == FormWindowState.Minimized || !Visible)
            {
                return;   // don't spend CPU while hidden; UpdateGifAnimationState restarts it
            }
            try
            {
                if (System.Drawing.ImageAnimator.CanAnimate(_fullBgImage))
                {
                    System.Drawing.ImageAnimator.Animate(_fullBgImage, OnSharedBgFrame);
                    _bgAnimating = true;
                }
            }
            catch { }
        }

        private void StopSharedBgAnimation()
        {
            if (!_bgAnimating || _fullBgImage == null)
            {
                return;
            }
            try { System.Drawing.ImageAnimator.StopAnimate(_fullBgImage, OnSharedBgFrame); } catch { }
            _bgAnimating = false;
        }

        // Repaint ceiling for the animated backdrop. GIFs routinely carry 10–20 ms frame
        // delays (50–100 fps) and ImageAnimator fires at whatever the file asks for. Every
        // one of those frames repaints the header, the sidebar AND the whole active page —
        // and a wallpaper also switches the window into WS_EX_COMPOSITED, which measured
        // ~11 fps for a full-window repaint. So a fast GIF was asking for 100 repaints a
        // second on the slowest possible path, and the UI crawled while it played.
        // 30 fps looks identical for a backdrop and leaves the window responsive.
        private const int BackdropMaxFps = 30;
        private long _lastBackdropPaintTick;

        private void OnSharedBgFrame(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            // Nothing on screen to update — don't even queue the repaint.
            if (!Visible || WindowState == FormWindowState.Minimized)
            {
                return;
            }

            // Mid-drag: the animation is stopped in BeginMoveResizeLoop, but a frame
            // already queued can still arrive after it. Repainting every surface while
            // the window is being dragged is exactly the cost we just removed.
            if (_inMoveLoop)
            {
                return;
            }

            long now = Environment.TickCount64;
            if (now - _lastBackdropPaintTick < 1000 / BackdropMaxFps)
            {
                return;              // drop this frame; the next one repaints
            }
            _lastBackdropPaintTick = now;

            try { BeginInvoke((Action)InvalidateBackdropSurfaces); } catch { }
        }

        /// <summary>Repaints every surface that shows the shared backdrop, once per frame.</summary>
        private void InvalidateBackdropSurfaces()
        {
            // Drop the cached wallpaper slices too — an invalidate that reuses a stale
            // composite is exactly what "repaint the backdrop" is meant to prevent.
            WindowBackdrop.InvalidateCache();
            _header?.Invalidate();
            if (_footerGif != null && _footerGif.Visible) { _footerGif.Invalidate(); }
            _sidebar?.Invalidate();
            (_tabs?.SelectedTab as BackdropTabPage)?.InvalidateBackdrop();
        }

        private static Image LoadGifImage(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }
            if (!System.IO.File.Exists(path))
            {
                Utils.Logger.Warn("Background image not found (was it moved or deleted?): " + path);
                return null;
            }
            try
            {
                byte[] bytes = System.IO.File.ReadAllBytes(path);
                return Image.FromStream(new System.IO.MemoryStream(bytes));
            }
            catch (Exception ex)
            {
                Utils.Logger.Warn("Background image failed to load (" + ex.GetType().Name + "): " + path);
                return null;
            }
        }

        /// <summary>
        /// True if the file at <paramref name="path"/> loads as an image. Used by the
        /// Settings pickers so a corrupt or non-image file is rejected with a message
        /// instead of being saved and silently showing nothing.
        /// </summary>
        private static bool CanLoadImageFile(string path)
        {
            Image probe = LoadGifImage(path);
            if (probe == null)
            {
                return false;
            }
            probe.Dispose();
            return true;
        }

        private void LayoutHeader()
        {
            if (_header == null)
            {
                return;
            }

            // Scaled with the screen, like the header's own painting: at 150% the state
            // pill is 1.5x wider (it is a real control, so WinForms scaled it) and a
            // fixed 18 px gutter beside it no longer looks or behaves the same.
            float hdrScale = _header.DeviceDpi / 96f;
            int rightPad = (int)Math.Round(18 * hdrScale);
            int gap = (int)Math.Round(12 * hdrScale);

            // The header IS the title bar now, so the top-right strip belongs to the
            // caption buttons. Everything else on the right (the state pill, and the
            // profile caption that right-aligns to it) moves left to clear them —
            // otherwise "Profile • Default" and the IDLE pill sit underneath ─ □ ✕.
            int captionStrip = _header.CaptionStripWidth;

            _statePill.Top = (_header.Height - _statePill.Height) / 2;
            _statePill.Left = _header.ClientSize.Width - _statePill.Width - rightPad - captionStrip;

            // The header owner-draws the profile caption right-aligned to just
            // left of the state pill.
            _header.ProfileRightEdge = _statePill.Left - gap;
        }

        private void SetupTray()
        {
            _trayMenu = new ContextMenuStrip
            {
                // Tempo-themed instead of the stark white system menu: a branded
                // header, crisp vector icons painted per item (see
                // ThemedMenuRenderer), rounded accent selection. The icon column is
                // reserved for those glyphs; colours follow the active theme.
                ShowImageMargin = true,
                Font = new Font("Segoe UI", 9.75f),
                Padding = new Padding(3, 6, 3, 6)
            };
            ApplyTrayMenuTheme();
            // Fade + slide the menu into place instead of snapping it on screen.
            MenuOpenAnimation.Attach(_trayMenu);

            // Branded header banner — a non-interactive title row with the Tempo
            // wordmark and the running version, drawn by the renderer.
            var trayHeader = new ToolStripMenuItem("Tempo") { Enabled = false };
            trayHeader.Tag = new TrayItemStyle(TrayGlyph.Brand)
            {
                Header = true,
                Caption = VersionStamp()
            };
            _trayMenu.Items.Add(trayHeader);

            // Live status row — "what is Tempo doing right now" at a glance, with a
            // state dot (green while clicking, muted while idle). Refreshed every
            // time the menu opens.
            _trayStatusItem = new ToolStripMenuItem("Idle")
            { Enabled = false, Tag = new TrayItemStyle(TrayGlyph.None) { Status = true } };
            _trayMenu.Items.Add(_trayStatusItem);
            _trayMenu.Items.Add(new ToolStripSeparator());

            // Every label here goes through Localization.T. The tray menu was the one
            // part of Tempo still hard-coded to English: a user running the app in
            // Spanish, French, German, Italian or Portuguese got a fully translated
            // window and an English tray menu. "Always on top" and "Settings" already
            // HAD translations sitting unused because the menu never asked for them.
            _trayShowHideItem = new ToolStripMenuItem(Utils.Localization.T("Show / Hide"))
            { Tag = new TrayItemStyle(TrayGlyph.Window) };
            _trayShowHideItem.Click += (s, e) => TrayAction("Show/Hide", ToggleWindowVisibility);
            _trayMenu.Items.Add(_trayShowHideItem);

            _trayStartStopItem = new ToolStripMenuItem(Utils.Localization.T("Start / Stop"))
            { Tag = new TrayItemStyle(TrayGlyph.Play) };
            _trayStartStopItem.Click += (s, e) => TrayAction("Start/Stop", ToggleEngine);
            _trayMenu.Items.Add(_trayStartStopItem);

            // Fresh state every open: dynamic labels (Start vs Stop, Show vs Hide),
            // the bound hotkey as a right-aligned hint, and the status row's text.
            _trayMenu.Opening += (s, e) => UpdateTrayMenuDetails();

            _trayMenu.Items.Add(new ToolStripSeparator());

            _trayAlwaysOnTopItem = new ToolStripMenuItem(Utils.Localization.T("Always on top"))
            {
                CheckOnClick = true,
                Checked = _settings != null && _settings.AlwaysOnTop,
                Tag = new TrayItemStyle(TrayGlyph.Pin)
            };
            _trayAlwaysOnTopItem.Click += (s, e) =>
            {
                // Calling ToggleAlwaysOnTop after CheckOnClick already flipped the
                // menu item would double-flip; just sync the setting and re-assert.
                _settings.AlwaysOnTop = _trayAlwaysOnTopItem.Checked;
                if (_alwaysOnTopCheck != null)
                {
                    _alwaysOnTopCheck.Checked = _settings.AlwaysOnTop;
                }
                ReassertTopMost();
            };
            _trayMenu.Items.Add(_trayAlwaysOnTopItem);

            // Live captions on/off, right where the other caption items are. The
            // keybind has always toggled this; the tray had no way to do it, so a user
            // whose hotkey was taken by another app had to open the window.
            _trayCaptionsItem = new ToolStripMenuItem(Utils.Localization.T("Live captions"))
            {
                CheckOnClick = false,   // state is owned by the caption engine, not the item
                Tag = new TrayItemStyle(TrayGlyph.Speech)
            };
            _trayCaptionsItem.Click += (s, e) => TrayAction("Live captions", ToggleLiveCaptions);
            _trayMenu.Items.Add(_trayCaptionsItem);

            _trayCaptionHistoryItem = new ToolStripMenuItem(Utils.Localization.T("Caption history"))
            {
                CheckOnClick = false,
                Tag = new TrayItemStyle(TrayGlyph.Speech)
            };
            _trayCaptionHistoryItem.Click += (s, e) => TrayAction("Caption history", ToggleCaptionHistoryWindow);
            _trayMenu.Items.Add(_trayCaptionHistoryItem);

            _trayMoveCaptionsItem = new ToolStripMenuItem(Utils.Localization.T("Move captions (drag to reposition)"))
            {
                CheckOnClick = false,
                Tag = new TrayItemStyle(TrayGlyph.Move)
            };
            _trayMoveCaptionsItem.Click += (s, e) => TrayAction("Move captions", ToggleCaptionMoveMode);
            _trayMenu.Items.Add(_trayMoveCaptionsItem);

            _trayMenu.Items.Add(new ToolStripSeparator());

            // Notification switches. These were previously buried in Settings, which
            // meant silencing Tempo mid-game cost a window open; from the tray it's two
            // clicks. Both persist immediately, like the Settings toggles do.
            _trayNotifyItem = new ToolStripMenuItem(Utils.Localization.T("Pop-up notifications"))
            {
                CheckOnClick = true,
                Checked = _settings != null && _settings.CustomNotifications,
                Tag = new TrayItemStyle(TrayGlyph.Speech)
            };
            _trayNotifyItem.Click += (s, e) =>
            {
                if (_settings == null) { return; }
                _settings.CustomNotifications = _trayNotifyItem.Checked;
                if (_customNotifyCheck != null) { _customNotifyCheck.Checked = _settings.CustomNotifications; }
                ApplyNotificationSettings();
                ApplyClipboardImageWatcher();
                PersistNotificationSettings();
            };
            _trayMenu.Items.Add(_trayNotifyItem);

            _trayScreenshotItem = new ToolStripMenuItem(Utils.Localization.T("Screenshot alerts"))
            {
                CheckOnClick = true,
                Checked = _settings != null && _settings.NotifyOnClipboardImage,
                Tag = new TrayItemStyle(TrayGlyph.Window)
            };
            _trayScreenshotItem.Click += (s, e) =>
            {
                if (_settings == null) { return; }
                _settings.NotifyOnClipboardImage = _trayScreenshotItem.Checked;
                if (_notifyScreenshotCheck != null) { _notifyScreenshotCheck.Checked = _settings.NotifyOnClipboardImage; }
                ApplyClipboardImageWatcher();
                PersistNotificationSettings();
            };
            _trayMenu.Items.Add(_trayScreenshotItem);

            // Straight to Settings without hunting for the tab.
            var traySettings = new ToolStripMenuItem(Utils.Localization.T("Settings…"))
            { Tag = new TrayItemStyle(TrayGlyph.Window) };
            traySettings.Click += (s, e) =>
            {
                try
                {
                    ShowFromTrayAndActivate();
                    if (_tabs != null && _tabs.TabPages.Count > 0)
                    {
                        _tabs.SelectedIndex = _tabs.TabPages.Count - 1;   // Settings is last
                    }
                }
                catch (Exception ex) { Utils.Logger.Swallow("TraySettings", ex); }
            };
            _trayMenu.Items.Add(traySettings);

            // Check for updates without opening the window. Reuses the same handler as
            // the Settings button, so the result (dialog, "up to date" note, the cached
            // "last seen" value) behaves identically wherever it's run from.
            var trayUpdate = new ToolStripMenuItem(Utils.Localization.T("Check for updates…"))
            { Tag = new TrayItemStyle(TrayGlyph.Window) };
            trayUpdate.Click += (s, e) =>
            {
                try { OnCheckForUpdatesClicked(s, e); }
                catch (Exception ex) { Utils.Logger.Swallow("TrayUpdateCheck", ex); }
            };
            _trayMenu.Items.Add(trayUpdate);

            // Tempo is open source and people like knowing that — give the repository a
            // first-class way in rather than burying it in About.
            var traySource = new ToolStripMenuItem(Utils.Localization.T("View source on GitHub"))
            { Tag = new TrayItemStyle(TrayGlyph.Brand) };
            traySource.Click += (s, e) =>
            {
                try { Utils.AppActivator.OpenUrl(OfficialSourceForm.GitHubUrl); }
                catch (Exception ex) { Utils.Logger.Swallow("TraySource", ex); }
            };
            _trayMenu.Items.Add(traySource);

            _trayMenu.Items.Add(new ToolStripSeparator());
            var trayExit = new ToolStripMenuItem(Utils.Localization.T("Exit"))
            { Tag = new TrayItemStyle(TrayGlyph.Power) };
            trayExit.Click += (s, e) => ExitApplication();
            _trayMenu.Items.Add(trayExit);

            _trayIcon = new NotifyIcon
            {
                Icon = Utils.AppIcon.Get(),
                Text = "Tempo",
                Visible = true,
                ContextMenuStrip = _trayMenu
            };
            _trayIcon.DoubleClick += (s, e) => TrayAction("Show/Hide", ToggleWindowVisibility);
        }

        /// <summary>
        /// Puts Tempo's icon back in a notification area that has just been (re)created —
        /// at sign-in, or after Explorer restarted. See <see cref="WM_TASKBARCREATED"/>.
        ///
        /// The false/true toggle is deliberate and is what actually re-adds it: setting
        /// Visible to true when NotifyIcon already believes it is visible does nothing at
        /// all, and its belief is exactly what is wrong here — the icon it thinks it added
        /// belongs to a taskbar that no longer exists. Clearing first forces the
        /// NIM_DELETE/NIM_ADD pair that re-registers against the new one.
        ///
        /// The only time Tempo hides this icon on purpose is while shutting down, so
        /// _shuttingDown is the one thing that must veto it — otherwise a taskbar rebuild
        /// landing mid-exit would resurrect an icon for a process that is going away.
        /// </summary>
        private void ReassertTrayIcon()
        {
            if (_shuttingDown || _trayIcon == null)
            {
                return;
            }
            try
            {
                _trayIcon.Visible = false;
                _trayIcon.Visible = true;
                Utils.Logger.Info("[Tray] notification area (re)created — Tempo's icon re-registered.");
            }
            catch (Exception ex)
            {
                Utils.Logger.Warn("[Tray] could not re-register the tray icon: " + ex.Message);
            }
        }

        // ── Custom notifications ───────────────────────────────────────────────────

        /// <summary>
        /// Maps a tray balloon icon to the custom-card kind (accent + glyph).
        /// </summary>
        private static UI.ToastKind KindFor(ToolTipIcon icon)
        {
            switch (icon)
            {
                case ToolTipIcon.Warning: return UI.ToastKind.Warning;
                case ToolTipIcon.Error: return UI.ToastKind.Error;
                default: return UI.ToastKind.Info;
            }
        }

        /// <summary>
        /// The one place every Tempo notification goes. When the user keeps the custom
        /// pop-ups on, it shows an animated Tempo card; otherwise it falls back to the
        /// plain Windows balloon tip. Signature matches NotifyIcon.ShowBalloonTip so the
        /// old call sites route through here unchanged. Respects the master "show tray
        /// notifications" switch exactly as before.
        /// </summary>
        /// <summary>
        /// Shows one of Tempo's own notifications — and, unless <paramref name="always"/>
        /// is set, honours "Show tray notifications".
        ///
        /// This used to decide STYLE only and leave every call site to decide WHETHER,
        /// which put the user's choice at the mercy of 33 separate places remembering to
        /// check it. Eighteen of them did not, so turning notifications off still left
        /// routine notices — "macro finished", "transcript copied", "run finished" —
        /// popping up hours later. That is the reported bug, and patching eighteen call
        /// sites would only have lasted until the nineteenth was written.
        ///
        /// The default is now the safe one: forgetting the flag means the notification
        /// RESPECTS the setting. Only messages the user genuinely must see pass
        /// always:true — an action of theirs that failed, an explanation for why a feature
        /// they enabled cannot work, and the first-run "Tempo is still running in the
        /// tray" notice, without which closing the window looks like the app quit.
        /// </summary>
        private void TempoNotify(int timeoutMs, string title, string text, ToolTipIcon icon,
                                 bool always = false)
        {
            try
            {
                if (!always && (_settings == null || !_settings.ShowTrayNotifications))
                {
                    return;
                }

                // ONE choke point for translating notification text, the same trick that
                // makes UiFactory work: every label it builds is translated because it all
                // funnels through one method. Notifications had no such point — 34 call
                // sites passed raw English straight through to the card — so a Spanish
                // Tempo popped up "Running in the tray." while its own window was fully
                // translated.
                //
                // Safe to apply to text a caller ALREADY translated: T() returns its input
                // unchanged when there is no entry, so a second lookup on Spanish text is a
                // no-op. That matters because several call sites build their message from a
                // translated fragment plus a runtime value, and those keep working.
                title = Localization.T(title ?? "");
                text = Localization.T(text ?? "");

                // Beyond this point it is purely a STYLE choice — animated Tempo card vs.
                // the plain Windows balloon.
                if (_settings != null && _settings.CustomNotifications && _notifications != null)
                {
                    // "Tempo" as the app tag; title/body carry the message. Tempo's own
                    // strings are already short heading + detail, so pass them straight.
                    // Show Tempo's own icon so its cards read like a real app notification,
                    // and make the card open the Tempo window when clicked.
                    _notifications.Notify("Tempo", title, text, KindFor(icon),
                        TempoNotifyIcon(), null, ShowFromTrayAndActivate);
                    return;
                }

                // Fallback to the plain Windows balloon. (This line was accidentally
                // turned into a recursive TempoNotify call by an earlier bulk rename —
                // it must call the tray balloon, or the off-custom path stack-overflows.)
                _trayIcon?.ShowBalloonTip(timeoutMs, title, text, icon);
            }
            catch (Exception ex) { Utils.Logger.Swallow("TempoNotify", ex); }
        }

        /// <summary>A fresh Tempo-logo bitmap for a notification card (the card owns and
        /// disposes it). Null if the icon can't be produced.</summary>
        private System.Drawing.Image TempoNotifyIcon()
        {
            // GetBitmap, not Icon.ToBitmap: ToBitmap() would rasterise at the icon's own
            // 32 px and the card would then upscale it into its icon slot.
            try { return Utils.AppIcon.GetBitmap(48); }
            catch { return null; }
        }

        /// <summary>A large Tempo-logo bitmap for the notification's picture area (the
        /// card owns and disposes it). Used to show a photo on the test pop-up.</summary>
        private System.Drawing.Image TempoHeroImage()
        {
            try
            {
                return Utils.AppIcon.GetBitmap(256);
            }
            catch { return null; }
        }

        /// <summary>
        /// Applies the notification settings: (re)starts or stops the Windows-notification
        /// mirror to match <see cref="Models.AppSettings.MirrorWindowsNotifications"/>.
        /// Called on show and whenever settings are saved. Mirrored notifications from
        /// other apps are shown as Tempo cards tagged with the source app's name.
        /// </summary>
        private void ApplyNotificationSettings()
        {
            try
            {
                bool want = _settings != null && _settings.MirrorWindowsNotifications
                            && _settings.CustomNotifications;

                if (want)
                {
                    if (_notifyMirror == null)
                    {
                        _notifyMirror = new Utils.WindowsNotificationMirror(
                            (app, title, body, icon, aumid) =>
                            {
                                // ONE card per screenshot. Taking a shot fires both the
                                // clipboard watcher (instant, and it has the picture) and
                                // the capture app's own notification a moment later — two
                                // near-identical cards for one action. Fold this one into
                                // the card already waiting: hand over the app's name, icon
                                // and identity so the merged card wears them and can open
                                // the shot back in it. Only ever swallowed when a clipboard
                                // card really is pending/just shown, so a screenshot
                                // notification can never vanish silently.
                                if (_settings != null && _settings.NotifyOnClipboardImage &&
                                    LooksLikeScreenshotNotification(app, title, body) &&
                                    (ClipCardPending ||
                                     Environment.TickCount64 - _lastClipImageTick < 4000))
                                {
                                    _shotApp = string.IsNullOrWhiteSpace(app) ? "Screenshot" : app;
                                    _shotAumid = aumid;
                                    try { _shotIcon?.Dispose(); } catch { }
                                    _shotIcon = icon;          // reused by the merged card
                                    _shotTick = Environment.TickCount64;
                                    // This is the proof that a NEW capture happened (an
                                    // edit re-copy never posts one), so a held repeat card
                                    // is allowed through.
                                    _shotNotifyTick = _shotTick;
                                    // If the card is already up (it appears instantly now),
                                    // re-label it in place rather than losing the identity.
                                    if (UpgradeShotCard(_shotApp, aumid, icon))
                                    {
                                        _shotIcon = null;   // ownership passed to the card
                                        Utils.Logger.Info("[Notify] re-labelled the screenshot card as " + _shotApp + ".");
                                    }
                                    else
                                    {
                                        Utils.Logger.Info("[Notify] folded " + _shotApp +
                                            "'s screenshot notification into Tempo's card.");
                                    }
                                    return;
                                }

                                // Click the mirrored card → go where the notification
                                // points: a link in its text (a real redirect), else the
                                // source app — but NOT a blank browser tab when a web-push
                                // notification carries no link. Null = not clickable.
                                Action open = Utils.AppActivator.BuildNotificationClickAction(app, title, body, aumid);
                                _notifications?.Notify(
                                    string.IsNullOrWhiteSpace(app) ? "Windows" : app,
                                    title, body, UI.ToastKind.Mirror, icon, null, open);
                            },
                            () => _settings != null && _settings.MirrorClearFromActionCenter);
                    }
                    if (!_notifyMirror.Running)
                    {
                        // Start on a WORKER thread: RequestAccessAsync can pop a consent
                        // prompt and blocking it on the UI thread would freeze the window.
                        var mirror = _notifyMirror;
                        System.Threading.Tasks.Task.Run(() =>
                        {
                            bool ok = mirror.Start();
                            if (!ok)
                            {
                                Utils.Logger.Info("[Notify] mirror not active: " + mirror.StatusText);
                            }
                            try
                            {
                                if (!IsDisposed)
                                {
                                    BeginInvoke((Action)(() =>
                                    {
                                        // If the user turned mirroring off while we were
                                        // starting, stop the mirror we just started.
                                        if (_settings == null || !_settings.MirrorWindowsNotifications
                                            || !_settings.CustomNotifications)
                                        {
                                            mirror.Stop();
                                        }
                                        RefreshNotifyStatus();
                                    }));
                                }
                            }
                            catch { /* window tearing down */ }
                        });
                    }
                }
                else
                {
                    _notifyMirror?.Stop();
                }
            }
            catch (Exception ex) { Utils.Logger.Swallow("ApplyNotificationSettings", ex); }
        }

        /// <summary>
        /// Runs a tray-menu action so a failure inside it can never take Tempo down.
        ///
        /// A ToolStripItem.Click handler runs straight off the message loop with no
        /// framework guard: anything that escapes it is an unhandled exception and the
        /// process dies. Every other entry point in Tempo (hotkeys, timers, the engine
        /// callbacks) is wrapped — the tray items were the exception, so a fault in a
        /// caption toggle, a window that failed to open, or a settings read looked
        /// exactly like "clicking the tray menu crashes Tempo".
        ///
        /// Note this cannot save the app from a NATIVE crash (an access violation
        /// inside the speech engine is a corrupted-state exception that .NET refuses to
        /// deliver to managed code) — see <see cref="Utils.CaptionCrashGuard"/> for that.
        /// </summary>
        private void TrayAction(string what, Action action)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                Utils.Logger.Error("Tray menu action failed: " + what, ex);
                try
                {
                    TempoNotify(6000, "Tempo",
                        "\"" + what + "\" couldn't run: " + ex.Message,
                        ToolTipIcon.Warning, always: true);   // their action failed
                }
                catch { }
            }
        }

        /// <summary>
        /// Freshens the tray menu the moment it opens: state-aware labels + glyphs
        /// (Start ⟷ Stop, Show ⟷ Hide), the bound hotkey as a right-aligned hint,
        /// and the live status row (clicker · profile · captions).
        /// </summary>
        private void UpdateTrayMenuDetails()
        {
            try
            {
                bool running = _engine != null && _engine.IsRunning;

                // Keep the toggles honest every time the menu opens — captions can be
                // turned on by the hotkey or auto-start, and the notification switches
                // can be changed in Settings, so a stale tick would lie.
                if (_trayCaptionsItem != null)
                {
                    _trayCaptionsItem.Text = Utils.Localization.T("Live captions") + " — " +
                        (_captionsActive ? Utils.Localization.T("on") : Utils.Localization.T("off"));
                    _trayCaptionsItem.Checked = _captionsActive;
                }
                // Always-on-top was the one checkable tray item never refreshed here.
                // Every writer syncs it, but LoadSettingsIntoUi does not — it runs with
                // events suppressed — so importing settings or resetting to defaults left
                // the menu showing the old tick. These items use CheckOnClick, so they
                // flip from what is DISPLAYED: a stale tick makes the next click do the
                // opposite of what the user intends.
                if (_trayAlwaysOnTopItem != null && _settings != null)
                {
                    _trayAlwaysOnTopItem.Checked = _settings.AlwaysOnTop;
                }
                if (_trayNotifyItem != null && _settings != null)
                {
                    _trayNotifyItem.Checked = _settings.CustomNotifications;
                }
                if (_trayScreenshotItem != null && _settings != null)
                {
                    _trayScreenshotItem.Checked = _settings.NotifyOnClipboardImage;
                    // Screenshot alerts ride on the notification system.
                    _trayScreenshotItem.Enabled = _settings.CustomNotifications;
                }

                if (_trayStatusItem != null)
                {
                    string profile = _settings != null && !string.IsNullOrEmpty(_settings.LastProfileName)
                        ? _settings.LastProfileName : "Default";
                    string captions = Utils.Localization.T(_captionsActive ? "captions on" : "captions off");
                    _trayStatusItem.Text = Utils.Localization.T(running ? "Clicking" : "Idle") + " · " + profile + " · " + captions;
                    if (_trayStatusItem.Tag is TrayItemStyle st)
                    {
                        st.Dot = running
                            ? Color.FromArgb(52, 211, 153)
                            : (_captionsActive ? Color.FromArgb(167, 139, 250) : _theme.TextMuted);
                    }
                }

                if (_trayStartStopItem != null)
                {
                    _trayStartStopItem.Text = Utils.Localization.T(running ? "Stop clicking" : "Start clicking");
                    string hint = null;
                    try
                    {
                        var hk = _settings?.HotkeyFor(Models.HotkeyAction.ToggleStartStop);
                        if (hk != null && hk.IsValid) { hint = hk.ToDisplayString(); }
                    }
                    catch { }
                    _trayStartStopItem.Tag = new TrayItemStyle(running ? TrayGlyph.Stop : TrayGlyph.Play)
                    { Hint = hint };
                }

                if (_trayShowHideItem != null)
                {
                    bool visibleNow = Visible && WindowState != FormWindowState.Minimized;
                    _trayShowHideItem.Text = Utils.Localization.T(visibleNow ? "Hide window" : "Show window");
                    if (_trayShowHideItem.Tag is TrayItemStyle ws) { ws.Hint = "2× tray"; }
                }
            }
            catch { /* the menu must always open, even if a detail refresh fails */ }
        }

        /// <summary>
        /// (Re)applies Tempo's theme to the bottom status bar: the custom renderer
        /// (accent hairline + slim separators) and freshly-painted stat icons in
        /// theme hues. Called at build time and on every theme change. Disposes the
        /// previous icon bitmaps so repeated theme switches don't leak them.
        /// </summary>
        private void StyleStatusBar()
        {
            if (_statusStrip == null)
            {
                return;
            }
            try
            {
                _statusStrip.Renderer = new StatusStripRenderer(_theme);
                _statusStrip.BackColor = _theme.Surface;
                _statusStrip.ForeColor = _theme.Text;

                // Icons sit a touch brighter than muted text so they read as a set
                // without shouting; the live values stay in the normal text colour.
                Color hue = MixColor(_theme.TextMuted, _theme.Accent, 0.55);
                SetStatusIcon(_statusProfile, StatusIcons.Kind.Profile, hue);
                SetStatusIcon(_statusClicks, StatusIcons.Kind.Clicks, hue);
                SetStatusIcon(_statusCps, StatusIcons.Kind.Cps, hue);
                SetStatusIcon(_statusPeak, StatusIcons.Kind.Peak, hue);
                SetStatusIcon(_statusElapsed, StatusIcons.Kind.Time, hue);
                SetStatusIcon(_statusCpu, StatusIcons.Kind.Cpu, hue);
                SetStatusIcon(_statusRam, StatusIcons.Kind.Ram, hue);
                SetStatusIcon(_statusUptime, StatusIcons.Kind.Uptime, hue);
            }
            catch { }
        }

        private static Color MixColor(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        private static void SetStatusIcon(ToolStripStatusLabel label, StatusIcons.Kind kind, Color hue)
        {
            if (label == null) { return; }
            var old = label.Image;
            label.Image = StatusIcons.Make(kind, hue);
            label.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            label.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            try { old?.Dispose(); } catch { }
        }

        /// <summary>
        /// (Re)applies Tempo's theme to the tray menu — called at build time and
        /// whenever the theme changes, so the menu follows Dark/Light/Match Windows.
        /// </summary>
        private void ApplyTrayMenuTheme()
        {
            if (_trayMenu == null)
            {
                return;
            }
            try
            {
                // Release the previous renderer's animation timer. This runs on every
                // settings save, so without it each save left another live timer behind,
                // still ticking against a menu it no longer draws.
                (_trayMenu.Renderer as ThemedMenuRenderer)?.Dispose();
                _trayMenu.Renderer = new ThemedMenuRenderer(_theme);
                _trayMenu.BackColor = _theme != null ? _theme.Surface : Color.FromArgb(30, 30, 40);
                _trayMenu.ForeColor = _theme != null ? _theme.Text : Color.White;
            }
            catch { }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Engine event wiring (marshalled to the UI thread)
        // ─────────────────────────────────────────────────────────────────────

        private void WireEngineEvents()
        {
            _engine.StateChanged += (s, e) => UiInvoke(() => OnEngineStateChanged(e.NewState));
            _engine.RunCompleted += (s, e) => UiInvoke(OnEngineRunCompleted);
        }

        /// <summary>
        /// Two short notes marking a run starting or stopping — rising for start,
        /// falling for stop, so the two are told apart without looking.
        ///
        /// Off the UI thread deliberately: Console.Beep BLOCKS for its full duration,
        /// and this fires from the engine's state-changed handler. Beeping inline would
        /// stall the message pump for 160 ms at the exact moment the run begins, which
        /// is the worst possible time to freeze the window.
        /// </summary>
        private static void PlayRunTone(bool starting)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    if (starting) { Console.Beep(784, 70); Console.Beep(1047, 90); }
                    else { Console.Beep(1047, 70); Console.Beep(784, 90); }
                }
                catch { }   // no speaker, or a device that refuses the call
            });
        }

        private void OnEngineStateChanged(EngineState state)
        {
            Point keepScroll = CaptureActiveScroll();
            switch (state)
            {
                case EngineState.Running:
                    _bigStatusLabel.Text = Localization.T("RUNNING");
                    _bigStatusLabel.ForeColor = _theme.Success;
                    _statusState.Text = "\u25CF  " + Localization.T("Running");
                    _statusState.ForeColor = _theme.Success;
                    _stopBtn.Enabled = true;
                    ShowClickingIndicator(true);
                    if (_soundOnStartCheck != null && _soundOnStartCheck.Checked) { PlayRunTone(true); }
                    break;

                case EngineState.Idle:
                    _bigStatusLabel.Text = Localization.T("IDLE");
                    _bigStatusLabel.ForeColor = _theme.TextMuted;
                    _statusState.Text = "\u25CF  " + Localization.T("Idle");
                    _statusState.ForeColor = _theme.TextMuted;
                    _stopBtn.Enabled = false;
                    ShowClickingIndicator(false);
                    if (_soundOnStopCheck != null && _soundOnStopCheck.Checked) { PlayRunTone(false); }
                    break;

                case EngineState.Paused:
                    _bigStatusLabel.Text = Localization.T("PAUSED");
                    _bigStatusLabel.ForeColor = _theme.Warning;
                    _statusState.Text = "\u25CF  " + Localization.T("Paused");
                    _statusState.ForeColor = _theme.Warning;
                    _stopBtn.Enabled = true;
                    ShowClickingIndicator(false);
                    break;
            }

            UpdateStartButtonAppearance();
            RefreshStatePill();
            RefreshBusyLock();
            UpdateStatusHint();
            UpdateTraySleepState();
            UpdateTrayTooltip();
            UpdateTrayStartStopGlyph(state);
            RestoreActiveScroll(keepScroll);
        }

        /// <summary>
        /// Swaps the tray Start/Stop item's icon (and label) to match what the
        /// action will DO: a green play triangle while idle, a red stop square
        /// while running or paused — so the tray reads at a glance.
        /// </summary>
        private void UpdateTrayStartStopGlyph(EngineState state)
        {
            if (_trayStartStopItem?.Tag is TrayItemStyle st)
            {
                bool running = state != EngineState.Idle;
                st.Glyph = running ? TrayGlyph.Stop : TrayGlyph.Play;
                _trayStartStopItem.Text = running
                    ? Localization.T("Stop") : Localization.T("Start");
            }
        }

        /// <summary>
        /// Updates the system-tray icon tooltip to reflect what Tempo is doing right now
        /// (Running / Paused / Playing / Recording / Idle) and the active profile, so a
        /// glance at the tray tells you whether it's clicking without opening the window.
        /// The tray-sleep notice takes precedence while sleeping.
        /// </summary>
        private void UpdateTrayTooltip()
        {
            if (_trayIcon == null || _traySleepActive)
            {
                return;
            }

            string state;
            if (_recorder != null && _recorder.IsRecording) state = "Recording macro";
            else if (_player != null && _player.IsPlaying) state = "Playing macro";
            else if (_engine != null && _engine.IsPaused) state = "Paused";
            else if (_engine != null && _engine.IsRunning) state = "Running";
            else state = "Idle";

            string profile = !string.IsNullOrWhiteSpace(_currentProfileName)
                ? _currentProfileName
                : (_settings != null ? _settings.LastProfileName : null);

            string text = "Tempo — " + state;
            if (!string.IsNullOrWhiteSpace(profile))
            {
                text += " · " + profile;
            }
            // NotifyIcon tooltips are limited (historically 63 chars); keep it short.
            if (text.Length > 60)
            {
                text = text.Substring(0, 59) + "…";
            }

            try { _trayIcon.Text = text; } catch { }
        }

        /// <summary>
        /// Locks the UI so only one operation can run at a time and nothing
        /// conflicting can be triggered mid-run. While clicking, a macro playing, or
        /// recording is active, the other start triggers, profile management and the
        /// CPS test are disabled; only the matching Stop stays available (hotkeys and
        /// the emergency stop always work). Everything re-enables automatically when
        /// the operation ends. This prevents accidentally starting two things at once
        /// or changing profiles in the middle of a run.
        /// </summary>
        private void RefreshBusyLock()
        {
            bool clicking = _engine != null && (_engine.IsRunning || _engine.IsPaused);
            bool playing = _player != null && _player.IsPlaying;
            bool recording = _liveRecording;
            bool busy = clicking || playing || recording;

            void En(Control c, bool enabled) { if (c != null) c.Enabled = enabled; }

            // Profile management and the CPS test are never allowed mid-operation.
            En(_profileCombo, !busy);
            En(_newProfileBtn, !busy);
            En(_saveProfileBtn, !busy);
            En(_duplicateProfileBtn, !busy);
            En(_deleteProfileBtn, !busy);
            En(_cpsTestBtn, !busy);

            // Clicker: the primary button stays available while clicking so it can
            // pause/resume; it's only locked out when a macro is playing or recording
            // (you can't start a click run then). Stop is available while clicking.
            En(_startBtn, clicking || (!playing && !recording));
            En(_stopBtn, clicking);

            // Macros: can only record/play when nothing is running.
            En(_recordBtn, !busy);
            En(_playMacroBtn, !busy);
            En(_playOnceBtn, !busy);
            En(_stopPlayBtn, playing);     // Stop playback only while playing.
            En(_stopRecordBtn, recording); // Stop recording only while recording.

            // Keep the tray tooltip in step with what's happening (clicking, playing a
            // macro, recording, idle) since this runs on every such state change.
            UpdateTrayTooltip();
        }

        /// <summary>
        /// Shows or hides the small click-through "clicking" overlay. Honors the
        /// user's setting; never throws if the overlay can't be created.
        /// </summary>
        private void ShowClickingIndicator(bool show)
        {
            try
            {
                bool wanted = show && _settings != null && _settings.ShowClickingIndicator;

                if (!wanted)
                {
                    if (_clickingIndicator != null)
                    {
                        var ind = _clickingIndicator;
                        _clickingIndicator = null;
                        if (!ind.IsDisposed) ind.Close();
                    }
                    return;
                }

                if (_clickingIndicator == null || _clickingIndicator.IsDisposed)
                {
                    _clickingIndicator = new ClickingIndicatorForm(_theme);
                    ApplyOverlayConfig(_clickingIndicator);

                    // Show which hotkey stops clicking (Stop, else Start/Stop, else Emergency).
                    string stopHint = null;
                    var sc = _settings?.HotkeyFor(HotkeyAction.StopClicking);
                    if (sc != null && sc.IsValid)
                    {
                        stopHint = sc.ToDisplayString();
                    }
                    else
                    {
                        var es = _settings?.HotkeyFor(HotkeyAction.EmergencyStop);
                        if (es != null && es.IsValid) stopHint = es.ToDisplayString();
                    }
                    if (!string.IsNullOrEmpty(stopHint))
                    {
                        _clickingIndicator.SetHint("Press " + stopHint + " to stop");
                    }

                    _clickingIndicator.Show();
                }
                else
                {
                    _clickingIndicator.ApplyTheme(_theme);
                }
            }
            catch (Exception ex)
            {
                // The overlay is a nicety; never let it break start/stop — but do log
                // it, so "the badge never appears" is diagnosable instead of silent.
                Utils.Logger.Swallow("overlay", ex);
            }
        }

        /// <summary>Pushes the user's overlay preferences into a badge.</summary>
        /// <summary>
        /// The caption bar's screen rectangle while it is actually showing, or empty.
        /// Used so the running indicator can step out of its way.
        /// </summary>
        private Rectangle CaptionBarScreenRect()
        {
            try
            {
                if (_captionOverlay != null && !_captionOverlay.IsDisposed && _captionOverlay.Visible)
                {
                    return _captionOverlay.Bounds;
                }
            }
            catch { }
            return Rectangle.Empty;
        }

        private void ApplyOverlayConfig(ClickingIndicatorForm ind)
        {
            if (ind == null || _settings == null) { return; }
            try
            {
                // Hand it the caption bar's rectangle so the two overlays stop landing
                // on top of each other — both default toward the bottom of the screen
                // and neither knew the other existed.
                ind.AvoidRect = CaptionBarScreenRect();
                ind.Configure(_settings.OverlayCorner, _settings.OverlayOpacity,
                    _settings.OverlayShowClicks, _settings.OverlayShowCps, _settings.OverlayShowElapsed);
            }
            catch { }
        }

        /// <summary>Shows the "playing macro" overlay (honors the same overlay setting).</summary>
        private void ShowMacroIndicator(string macroName)
        {
            try
            {
                if (_settings == null || !_settings.ShowClickingIndicator) return;
                if (_macroIndicator == null || _macroIndicator.IsDisposed)
                {
                    _macroIndicator = new ClickingIndicatorForm(_theme, "Tempo \u2014 playing macro", _theme.Accent);

                    // Show which hotkey stops playback (Stop-macro, else Emergency-stop).
                    string stopHint = null;
                    var sm = _settings?.HotkeyFor(HotkeyAction.StopMacro);
                    if (sm != null && sm.IsValid)
                    {
                        stopHint = sm.ToDisplayString();
                    }
                    else
                    {
                        var es = _settings?.HotkeyFor(HotkeyAction.EmergencyStop);
                        if (es != null && es.IsValid) stopHint = es.ToDisplayString();
                    }
                    if (!string.IsNullOrEmpty(stopHint))
                    {
                        _macroIndicator.SetHint("Press " + stopHint + " to stop");
                    }

                    _macroIndicator.Show();
                }
                _macroIndicator.SetStatusText(string.IsNullOrEmpty(macroName) ? "playing\u2026" : macroName);
            }
            catch { }
        }

        private void UpdateMacroIndicator(string text)
        {
            if (_macroIndicator != null && !_macroIndicator.IsDisposed)
            {
                _macroIndicator.SetStatusText(text);
            }
        }

        private void HideMacroIndicator()
        {
            try
            {
                if (_macroIndicator != null && !_macroIndicator.IsDisposed)
                {
                    _macroIndicator.Close();
                }
            }
            catch { }
            _macroIndicator = null;
        }

        private void OnEngineRunCompleted()
        {
            // A run is recorded exactly once. On a normal stop this fires via the
            // engine's RunCompleted event; on app-close-while-running we call it
            // directly (see OnFormClosing) because the async event would be dropped
            // during shutdown. The guard stops the two paths double-counting.
            if (_runCompletedHandled)
            {
                return;
            }
            _runCompletedHandled = true;

            // Optional completion notice — only when a finite run (fixed count or
            // duration) ended on its own, never for a manual stop.
            if (_settings != null && _settings.NotifyOnRepeatFinish &&
                _lastRunWasFinite && _engine != null && _engine.LastRunCompletedNaturally)
            {
                try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
                try
                {
                    TempoNotify(2000, "Tempo",
                        "Fixed run finished (" + _statistics.SessionClicks.ToString("N0") + " session clicks).",
                        ToolTipIcon.Info);
                }
                catch { }
            }

            // The worker finished — either it was stopped or a fixed repeat count
            // was reached. Reflect the idle state in the UI and persist stats.
            _startBtn.Enabled = true;
            _stopBtn.Enabled = false;
            _bigStatusLabel.Text = Localization.T("IDLE");
            _bigStatusLabel.ForeColor = _theme.TextMuted;
            _statusState.Text = "\u25CF  " + Localization.T("Idle");
            _statusState.ForeColor = _theme.TextMuted;
            RefreshStatePill();

            // Privacy: when history recording is turned off, a finished run leaves no
            // trace — no history row and no change to the lifetime totals. We also pull
            // this run's clicks out of the persistence baseline so they can never be
            // folded into the lifetime count later, even if recording is re-enabled.
            if (!_settings.RecordSessionHistory)
            {
                _lifetimeBaseline -= (_statistics.TotalClicks - _runStartClicks);
                return;
            }

            // Roll this run's figures into the lifetime totals.
            if (_statistics.PeakClicksPerSecond > _settings.LifetimePeakCps)
            {
                _settings.LifetimePeakCps = _statistics.PeakClicksPerSecond;
            }

            double runSeconds = _statistics.GetElapsed().TotalSeconds;
            _settings.LifetimeRuntimeSeconds += (long)runSeconds;

            // Credit the run to the profile it ran under. ProfileManager has had
            // AddRuntime since it was written and nothing ever called it, so every
            // profile's TotalRuntimeSeconds was permanently zero and the Profiles
            // tab would have shown a column of noughts. Placed after the privacy
            // return above, so "don't record history" covers this too.
            if (_profiles != null && !string.IsNullOrEmpty(_currentProfileName))
            {
                _profiles.AddRuntime(_currentProfileName, (long)runSeconds);
                _profiles.Save();
                RefreshProfileGrid();
            }

            // Record this run in the session history (skip empty runs).
            long runClicks = _statistics.TotalClicks - _runStartClicks;
            if (runClicks > 0)
            {
                var record = new Models.SessionRecord
                {
                    WhenUtc = DateTime.UtcNow,
                    Clicks = runClicks,
                    DurationSeconds = runSeconds,
                    AverageCps = runSeconds > 0.01 ? runClicks / runSeconds : 0,
                    PeakCps = _statistics.PeakClicksPerSecond,
                    Profile = _currentProfileName ?? ""
                };
                _history.Add(record);
                // Fold into the rolling lifetime aggregates so the all-time insight cards
                // stay accurate after the history cap starts trimming old runs.
                AccumulateLifetimeAggregates(record.Clicks, record.WhenUtc, record.Profile);
                RefreshSessionHistory();

                if (runClicks > _settings.LifetimeMostClicksRun)
                {
                    _settings.LifetimeMostClicksRun = runClicks;
                }
                if ((long)runSeconds > _settings.LifetimeLongestRunSeconds)
                {
                    _settings.LifetimeLongestRunSeconds = (long)runSeconds;
                }
            }

            PersistLifetimeStats();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Hotkey wiring
        // ─────────────────────────────────────────────────────────────────────

        private void WireHotkeyEvents()
        {
            _hotkeys.HotkeyPressed += (s, e) => PostHotkey(e.Name);
        }

        /// <summary>
        /// Queues a hotkey action onto the message loop. This is deliberately
        /// asynchronous: mouse-button hotkeys are detected inside a low-level mouse
        /// hook callback, and running the action inline there (e.g. a modal
        /// countdown) would stall global input. BeginInvoke lets the hook return
        /// immediately and the action run a moment later on the UI thread.
        /// </summary>
        private void PostHotkey(string name)
        {
            if (IsDisposed)
            {
                return;
            }

            try
            {
                if (IsHandleCreated)
                {
                    BeginInvoke((Action)(() => OnHotkey(name)));
                }
            }
            catch (ObjectDisposedException) { /* shutting down */ }
            catch (InvalidOperationException) { /* handle gone */ }
        }

        private void OnHotkey(string name)
        {
            // The registration name is the HotkeyAction enum value's name.
            if (!Enum.TryParse(name, out HotkeyAction action))
            {
                return;
            }

            // Light the row up on the Keybinds tab. "Is my hotkey even reaching Tempo?"
            // was previously unanswerable without just trying the action and watching
            // for a side effect.
            FlashKeybind(action);

            DispatchAction(action);
        }

        /// <summary>Executes the handler for a bound action.</summary>
        private void DispatchAction(HotkeyAction action)
        {
            // If a start-delay count-in is on screen, the stop-family actions abort it
            // instead of doing their normal job — otherwise a stop hotkey pressed during
            // the countdown was ignored and you were stuck waiting for a run you'd
            // already changed your mind about.
            var countdown = _activeCountdown;
            if (countdown != null && !countdown.IsDisposed &&
                (action == HotkeyAction.ToggleStartStop || action == HotkeyAction.StopClicking ||
                 action == HotkeyAction.EmergencyStop))
            {
                countdown.CancelCountdown();
                return;
            }

            switch (action)
            {
                case HotkeyAction.ToggleStartStop:
                    ToggleEngine();
                    break;
                case HotkeyAction.StartClicking:
                    if (!_engine.IsRunning) BeginStartWithCountdown();
                    break;
                case HotkeyAction.StopClicking:
                    _engine.Stop();
                    break;
                case HotkeyAction.TogglePause:
                    _engine.TogglePause();
                    break;
                case HotkeyAction.PickPosition:
                    PickFixedPosition();
                    break;
                case HotkeyAction.EmergencyStop:
                    EmergencyStop();
                    break;
                case HotkeyAction.NextProfile:
                    CycleProfile(1);
                    break;
                case HotkeyAction.PreviousProfile:
                    CycleProfile(-1);
                    break;
                case HotkeyAction.IncreaseInterval:
                    NudgeInterval(_settings.IntervalStepMilliseconds);
                    break;
                case HotkeyAction.DecreaseInterval:
                    NudgeInterval(-_settings.IntervalStepMilliseconds);
                    break;
                case HotkeyAction.ToggleAlwaysOnTop:
                    ToggleAlwaysOnTop();
                    break;
                case HotkeyAction.ToggleRecordMacro:
                    ToggleMacroRecording();
                    break;
                case HotkeyAction.PlayMacro:
                    PlaySelectedMacroViaHotkey();
                    break;
                case HotkeyAction.StopMacro:
                    _player.Stop();
                    break;
                case HotkeyAction.ShowHideWindow:
                    ToggleWindowVisibility();
                    break;
                case HotkeyAction.ShowPointsOverlay:
                    OnShowPointsOverlay(this, EventArgs.Empty);
                    break;
                case HotkeyAction.ToggleAntiFreeze:
                    ToggleAntiFreezeProtection();
                    break;
                case HotkeyAction.AddPointAtCursor:
                    AddPointAtCursor();
                    break;
                case HotkeyAction.PlayMacro1:
                    PlayMacroSlot(1);
                    break;
                case HotkeyAction.PlayMacro2:
                    PlayMacroSlot(2);
                    break;
                case HotkeyAction.PlayMacro3:
                    PlayMacroSlot(3);
                    break;
                case HotkeyAction.ToggleLiveCaptions:
                    ToggleLiveCaptions();
                    break;
                case HotkeyAction.ToggleCameraMovement:
                    ToggleCameraMovement();
                    break;
                case HotkeyAction.RecenterCameraMovement:
                    RecenterCameraMovement();
                    break;
                case HotkeyAction.GrabSecondCursor:
                    _secondCursor?.StartPlacement();
                    break;
                case HotkeyAction.ToggleSecondCursorSpam:
                    _secondCursor?.ToggleSpam();
                    break;
            }
        }

        // Virtual-key codes for the Live Captions chord (Win + Ctrl + L).
        private const byte VK_LWIN = 0x5B;
        private const byte VK_CONTROL = 0x11;
        private const byte VK_L = 0x4C;
        private const uint KEYEVENTF_KEYUP_FLAG = 0x0002;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, System.UIntPtr dwExtraInfo);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int msg, bool wParam, int lParam);
        private const int WM_SETREDRAW = 0x000B;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_HIDE = 0;
        private const int SW_SHOWNA = 8; // show without changing activation

        /// <summary>
        /// True when the control's actual NATIVE window is visible. After handle
        /// churn (display sleep/wake while minimised) the managed Visible flag can
        /// disagree with the real window state — this reads the real one.
        /// </summary>
        private static bool IsNativelyVisible(Control c)
        {
            if (c == null || !c.IsHandleCreated)
            {
                return false;
            }
            try { return IsWindowVisible(c.Handle); } catch { return false; }
        }

        /// <summary>
        /// Freezes (or unfreezes) a control's painting so a batch of layout/scroll changes
        /// commits in a single repaint — no intermediate flash (e.g. a tab snapping to the
        /// top before its remembered scroll position is restored).
        /// </summary>
        // How many freezes are currently outstanding per control.
        //
        // WM_SETREDRAW is a plain on/off switch — Windows does NOT reference-count it —
        // and these freezes NEST. A tab switch freezes the page and then calls
        // CenterPageContent, which freezes and THAWS the same page inside that block. The
        // inner thaw (plus its Invalidate) re-enabled painting halfway through the
        // switch, so everything the outer freeze existed to hide — the child moves, the
        // PerformLayout, the scroll restore — painted anyway, one repaint at a time. The
        // switch was doing exactly the repeated repainting the freeze was added to
        // prevent, which is why a switch could still be seen assembling itself.
        //
        // Counting depth means only the OUTERMOST thaw actually re-enables painting, so
        // a nested helper can no longer cancel its caller's freeze.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, int[]> _redrawDepth =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Control, int[]>();

        private static void SetRedraw(Control c, bool on)
        {
            if (c == null || !c.IsHandleCreated)
            {
                return;
            }
            try
            {
                int[] depth = _redrawDepth.GetValue(c, _ => new int[1]);
                if (!on)
                {
                    // First freeze actually stops painting; deeper ones just count.
                    if (depth[0]++ == 0)
                    {
                        SendMessage(c.Handle, WM_SETREDRAW, false, 0);
                    }
                }
                else
                {
                    if (depth[0] > 0) { depth[0]--; }
                    // Only the outermost thaw lets painting resume.
                    if (depth[0] == 0)
                    {
                        SendMessage(c.Handle, WM_SETREDRAW, true, 0);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Toggles Windows Live Captions by synthesising its system shortcut,
        /// Win + Ctrl + L. Live Captions (Windows 11, version 22H2+) transcribes
        /// ALL audio playing on the PC in real time and floats a caption bar over
        /// any app, including fullscreen games — so it covers game voice chat,
        /// Discord and livestream audio. It is built in, free and runs offline.
        ///
        /// Tempo can't transcribe speech itself (that needs OS-level, GPU-backed
        /// speech models), but it can give you a single hotkey to flip the system
        /// captions on and off without leaving your game.
        /// </summary>
        /// <summary>
        /// Toggles Live Captions. Tempo shows its own transparent caption overlay
        /// <summary>
        /// Toggles Live Captions on/off. This respects the chosen caption SOURCE:
        /// if Tempo's own engine is selected it starts/stops that; if Windows is
        /// selected it drives Windows Live Captions. The hotkey and tray item both
        /// call this, so the keybind never forces Windows captions when you've picked
        /// Tempo's own engine. State is kept in _captionsActive.
        /// </summary>
        private void ToggleLiveCaptions()
        {
            // Ignore re-entrant presses (e.g. mashing the caption hotkey) while a
            // previous toggle is still being applied, so the on/off state can't get
            // out of sync or crash mid-transition.
            if (_captionToggleBusy)
            {
                return;
            }
            _captionToggleBusy = true;
            try
            {
                _captionsActive = !_captionsActive;
                SetCaptionsActive(_captionsActive);
            }
            finally
            {
                _captionToggleBusy = false;
            }
        }

        private void SetCaptionsActive(bool on)
        {
            _captionsActive = on;
            if (on)
            {
                // Fresh session: allow one auto-fallback from Tempo's engine to
                // Windows mirroring again, and restart speaker numbering at 1.
                _captionFellBackToWindows = false;
                _captionUiaFallbackDone = false;
                _speakerTurns.Reset();
                _lastCaptionTextUtc = DateTime.MinValue;
                _soundKindSinceUtc = DateTime.MinValue;
                _soundNoteShown = false;
                _lastVoiceSource = "";
                _tempoRollingLine = "";
                // Each caption session gets a fresh transcript.
                _captionHistory.Clear();
                _captionHistoryTimes.Clear();
                // The on-device mishear fixer (Windows' spell engine) — created once,
                // reused for the app's lifetime.
                if (_wordFixer == null)
                {
                    try { _wordFixer = new Utils.CaptionWordFixer(); } catch { }
                }

                // Voice matching for the speaker labels (best-effort; labels fall
                // back to pause-counted turns when audio capture isn't possible).
                if (_settings == null || _settings.CaptionSpeakerTurns)
                {
                    MaybeShowSpeakerNotice();
                    try
                    {
                        if (_voiceProfiler == null) { _voiceProfiler = new Utils.VoiceProfiler(); }
                        _voiceProfiler.Start();
                    }
                    catch { }
                    // Sight joins sound (opt-in): the OS face detector watches the
                    // foreground video and reports which face's mouth is moving.
                    if (_settings != null && _settings.CaptionFaceAnalysis)
                    {
                        try
                        {
                            if (_faceAnalyzer == null) { _faceAnalyzer = new Utils.FaceSpeakerAnalyzer(); }
                            // Read faces in whichever window is PLAYING the audio (any
                            // app or site, foreground or not); the analyzer falls back
                            // to the foreground window when that's unknown.
                            _faceAnalyzer.PreferredWindow =
                                () => _mediaDetector != null ? _mediaDetector.CurrentAudioWindow : IntPtr.Zero;
                            _faceAnalyzer.Start();
                        }
                        catch { }
                    }
                }
            }
            else
            {
                // Don't let the external watcher instantly re-activate while Tempo is
                // still busy turning the Windows bar off. The cooldown alone was never
                // enough — it only DELAYED the re-activation, because the watcher then
                // saw the (still open, or Tempo-toggled) Windows bar and read it as the
                // user turning captions on. Seeding "was present" means the Windows bar
                // has to genuinely disappear and come back before it counts as an
                // external turn-on, so an explicit off now stays off.
                _externalWatchCooldownUntil = DateTime.UtcNow.AddSeconds(10);
                _externalLcWasPresent = true;
                SaveTranscriptIfWanted();
                try { _voiceProfiler?.Stop(); } catch { }
                try { _faceAnalyzer?.Stop(); } catch { }
                // Captions were turned OFF — make that stick.
                //
                // This used to disarm auto-start only when the detector happened to read
                // "media active" at this exact instant, and any later silence re-armed
                // it. Both halves of that were wrong, and together they are the "I turn
                // it off and it just comes back" report:
                //   · Turning captions off during a PAUSED video (or a quiet passage, or
                //     between two tracks) left auto-start armed, so the moment sound
                //     resumed the captions switched themselves straight back on.
                //   · Even when it did disarm, a two-second gap between sentences was an
                //     inactive edge, which re-armed it — so captions returned part-way
                //     through the very same video the user had just silenced them for.
                // Disarm unconditionally, and remember WHAT was playing; re-arming is now
                // deliberate (see the StateChanged handler in StartMediaDetector).
                _mediaAutoArmed = false;
                _mediaInactiveSinceUtc = DateTime.MinValue;
                _autoStartOffSource = _mediaDetector != null
                    ? (_mediaDetector.CurrentAudioSource ?? string.Empty)
                    : string.Empty;
            }

            // Wrap the whole transition so an unexpected error while toggling captions
            // (Windows captions spawning/closing, UIA, the overlay, a timer, etc.) can
            // never crash Tempo - it's logged and the app stays up. This directly
            // guards the "stop kills the app" case.
            try
            {
            // Tempo's own overlay bar (shows the text from whichever engine).
            if (_settings == null || _settings.CaptionOverlayEnabled)
            {
                if (on)
                {
                    ShowCaptionOverlay();
                }
                else if (_captionOverlay != null)
                {
                    try { _captionOverlay.Hide(); } catch { }
                }
            }

            // FPS guard: while captions run, watch for a fullscreen game taking the
            // screen and switch the caption stack into low-impact mode (relaxed
            // engine pace + beam off, 1 fps face analysis, eased overlay animation)
            // — measured as the main causes of in-game frame drops. All of it
            // restores the moment the game closes; when captions are off, the
            // watcher doesn't run at all.
            if (on)
            {
                if (_gamePresence == null)
                {
                    _gamePresence = new Utils.GamePresence();
                    _gamePresence.FullscreenChanged += fullscreen =>
                    {
                        Utils.TempoTranscriber.LowImpactMode = fullscreen;
                        Utils.FaceSpeakerAnalyzer.LowImpactMode = fullscreen;
                        CaptionOverlayForm.LowImpactMode = fullscreen;
                        OverlayTopmost.LowImpactMode = fullscreen;
                        // The authoritative mode log — fires on BOTH caption paths
                        // (the engine adds its own beam/pace detail when it reacts).
                        Utils.Logger.Info(fullscreen
                            ? "[Audio] fullscreen app took the screen — caption stack easing off (game mode)."
                            : "[Audio] fullscreen app closed — caption stack back to full quality.");
                        if (fullscreen)
                        {
                            // Visual proof the bar CAN draw over this game: a brief
                            // status line the moment game mode starts. If the game is
                            // exclusive-fullscreen this is invisible — and the
                            // ExclusiveChanged handler below explains why.
                            UiInvoke(() =>
                            {
                                if (_captionsActive && _captionOverlay != null && !_captionOverlay.IsDisposed)
                                {
                                    _captionOverlay.SetCaption("🎮 " +
                                        Localization.T("Game mode — captions keep running at a lighter pace"));
                                }
                            });
                        }
                    };
                    _gamePresence.ExclusiveChanged += exclusive =>
                    {
                        if (!exclusive)
                        {
                            Utils.Logger.Info("[Captions] exclusive fullscreen ended — the caption bar can appear again.");
                            return;
                        }
                        // The one fullscreen mode NOTHING can draw over (without
                        // injecting into the game — an anti-cheat gamble Tempo will
                        // never take). Say why the bar is invisible, and how to fix
                        // it, exactly once per run.
                        Utils.Logger.Warn("[Captions] the game holds the display in EXCLUSIVE fullscreen — no app " +
                                          "can draw captions over it. In the game's video settings choose " +
                                          "'Borderless' / 'Windowed fullscreen' (looks identical) and captions will show.");
                        if (!_exclusiveTipShown)
                        {
                            _exclusiveTipShown = true;
                            UiInvoke(() =>
                            {
                                if (_trayIcon != null)
                                {
                                    TempoNotify(10000, "Tempo",
                                        Localization.T("Captions can't appear over this game: it uses Exclusive Fullscreen, which no app can draw over.") +
                                        " " + Localization.T("In the game's video settings choose 'Borderless' — it looks the same, and captions will show."),
                                        ToolTipIcon.Warning, always: true);   // explains a feature that cannot work
                                }
                            });
                        }
                    };
                }
                _gamePresence.Start();
            }
            else
            {
                _gamePresence?.Stop();
                Utils.TempoTranscriber.LowImpactMode = false;
                Utils.FaceSpeakerAnalyzer.LowImpactMode = false;
                CaptionOverlayForm.LowImpactMode = false;
                OverlayTopmost.LowImpactMode = false;
            }

            CaptionSource source = _settings != null ? _settings.CaptionSource : CaptionSource.Auto;

            // Auto and Tempo both START with Tempo's own offline engine. The engine
            // auto-falls-back to mirroring Windows if it can't run (see
            // FallBackToWindowsCaptions). Only an explicit Windows choice skips Tempo.
            // Before touching Windows' own caption switch, record how we found it — see
            // RememberWindowsCaptionState. Turning captions OFF puts it back that way.
            if (on) { RememberWindowsCaptionState(); }

            if (source == CaptionSource.Tempo || source == CaptionSource.Auto)
            {
                // Tempo's own offline engine. Make sure the Windows path is fully
                // stopped so the two never run at once.
                StopCaptionMirror();
                if (on)
                {
                    EnsureWindowsCaptions(false);
                    StartTempoCaptions();
                }
                else
                {
                    // NOT EnsureWindowsCaptions(false) here any more: forcing it off on
                    // the way out, then restoring below, sent two toggles for no reason —
                    // and when Tempo had fallen back to mirroring Windows, the old order
                    // left Windows Live Captions switched ON after Tempo's captions
                    // stopped, because nothing ever undid the fallback's own toggle.
                    StopTempoCaptions();
                }
            }
            else
            {
                // Windows 11 Live Captions, mirrored into Tempo's bar. Stop Tempo's
                // own engine first so they're mutually exclusive.
                StopTempoCaptions();
                if (on)
                {
                    EnsureWindowsCaptions(true);
                    StartCaptionMirror();
                }
                else
                {
                    StopCaptionMirror();
                }
            }

            if (!on) { RestoreWindowsCaptionState(); }

            if (_settings != null && _settings.ShowTrayNotifications && _trayIcon != null)
            {
                string msg;
                if (!on)
                {
                    msg = "Live Captions OFF.";
                }
                else if (source == CaptionSource.Tempo || source == CaptionSource.Auto)
                {
                    msg = "Tempo Live Captions ON (offline). Tempo is transcribing your PC audio itself.";
                }
                else
                {
                    msg = "Windows Live Captions ON. Showing in Tempo's bar; Windows is transcribing all PC audio.";
                }
                TempoNotify(2500, "Tempo", msg, ToolTipIcon.Info);
            }
            }
            catch (Exception ex)
            {
                Utils.Logger.Warn("SetCaptionsActive failed: " + ex.Message);
            }
        }

        /// <summary>Opens (or re-shows) the scrollable full caption history window.</summary>
        /// <summary>
        /// Toggles "move mode" for the caption overlays: they stop being
        /// click-through so you can drag them anywhere, and their new positions are
        /// remembered. Toggle off to lock them back to click-through. Shows the bars
        /// (with sample text if needed) so there's something to grab.
        /// </summary>
        private void ToggleCaptionMoveMode()
        {
            _captionMoveMode = !_captionMoveMode;
            if (_trayMoveCaptionsItem != null)
            {
                _trayMoveCaptionsItem.Checked = _captionMoveMode;
            }

            if (_captionMoveMode)
            {
                // Ensure both bars exist and are visible so they can be grabbed.
                ShowCaptionOverlay();
                if (_captionOverlay != null && !_captionOverlay.IsDisposed)
                {
                    _captionOverlay.SetCaption("Drag me \u2014 caption bar");
                }
                ShowCaptionHistoryWindow();
                if (_captionHistoryForm != null && !_captionHistoryForm.IsDisposed)
                {
                    var demo = new System.Collections.Generic.List<string>
                    { "Drag this history panel anywhere.", "Toggle move mode off in the tray to lock it." };
                    _captionHistoryForm.SetHistory(demo);
                }
                _captionOverlay?.SetMovable(true);
                _captionHistoryForm?.SetMovable(true);

                if (_trayIcon != null)
                {
                    TempoNotify(3000, "Tempo",
                        "Move mode ON: drag the caption bars where you want them, then turn move mode off in the tray to lock them.",
                        ToolTipIcon.Info);
                }
            }
            else
            {
                _captionOverlay?.SetMovable(false);
                _captionHistoryForm?.SetMovable(false);
                // Restore the live bar's real text (it was showing "Drag me …")
                // and the history's real content.
                if (_captionOverlay != null && !_captionOverlay.IsDisposed)
                {
                    _captionOverlay.SetCaption(
                        string.IsNullOrEmpty(_lastMirroredCaption)
                            ? (_captionsActive ? "Listening\u2026" : "")
                            : _lastMirroredCaption);
                }
                if (_captionHistoryForm != null && !_captionHistoryForm.IsDisposed)
                {
                    _captionHistoryForm.SetHistory(_captionHistory);
                }
            }
        }

        /// <summary>Tray entry: show the caption history overlay if hidden, hide it if shown.</summary>
        private void ToggleCaptionHistoryWindow()
        {
            if (_captionHistoryForm != null && !_captionHistoryForm.IsDisposed && _captionHistoryForm.Visible)
            {
                try { _captionHistoryForm.Hide(); } catch { }
                SyncCaptionHistoryMenu();
                return;
            }
            ShowCaptionHistoryWindow();
            SyncCaptionHistoryMenu();
        }

        /// <summary>Keeps the tray menu item's checkmark in step with the window.</summary>
        private void SyncCaptionHistoryMenu()
        {
            if (_trayCaptionHistoryItem != null)
            {
                _trayCaptionHistoryItem.Checked =
                    _captionHistoryForm != null && !_captionHistoryForm.IsDisposed && _captionHistoryForm.Visible;
            }
        }

        /// <summary>
        /// One-line state for a caption overlay window: hidden, or its position plus
        /// whether that position is actually covered by a display. The off-screen case
        /// is the one worth naming — it looks identical to "not working" from outside.
        /// </summary>
        private static string CaptionWindowState(Form w)
        {
            try
            {
                if (w == null || w.IsDisposed) { return "not created"; }
                if (!w.Visible) { return "hidden"; }
                var b = w.Bounds;
                bool onScreen = false;
                foreach (Screen s in Screen.AllScreens)
                {
                    if (s.WorkingArea.IntersectsWith(b)) { onScreen = true; break; }
                }
                return b.X + "," + b.Y + " " + b.Width + "x" + b.Height +
                       (onScreen ? " · on screen" : "  ⚠ OFF-SCREEN — nothing will be visible");
            }
            catch { return "?"; }
        }

        private void ShowCaptionHistoryWindow()
        {
            try
            {
                if (_captionHistoryForm == null || _captionHistoryForm.IsDisposed)
                {
                    _captionHistoryForm = new CaptionHistoryForm(_theme);
                    _captionHistoryForm.PositionChanged += p =>
                    {
                        if (_settings != null)
                        {
                            _settings.CaptionHistoryX = p.X;
                            _settings.CaptionHistoryY = p.Y;
                            try { SettingsManager.Save(_settings); } catch { }
                        }
                    };
                }
                if (_settings != null)
                {
                    _captionHistoryForm.SetFontSize(_settings.CaptionFontSize);
                    _captionHistoryForm.SetFontFamily(_settings.CaptionFontFamily);
                    _captionHistoryForm.SetTextColor(
                        System.Drawing.Color.FromArgb(_settings.CaptionColorArgb),
                        _settings.CaptionUseCustomColor);
                    _captionHistoryForm.SetTextOpacity(_settings.CaptionOpacity);
                }
                if (_settings != null && _settings.CaptionHistoryX >= 0 && _settings.CaptionHistoryY >= 0)
                {
                    _captionHistoryForm.MoveTo(new Point(_settings.CaptionHistoryX, _settings.CaptionHistoryY));
                }
                _captionHistoryForm.SetHistory(_captionHistory);
                if (!_captionHistoryForm.Visible)
                {
                    _captionHistoryForm.Show();
                }

                // Drag the panel back onto a real screen BEFORE handing it over.
                //
                // The saved position was restored verbatim with nothing checking it was
                // still visible. Move the panel onto a second monitor, then unplug that
                // monitor (or just boot with it off) and the window is restored into
                // coordinates no display covers any more: it opens, the tray menu ticks
                // "Caption history", and the user sees NOTHING. Same for a resolution
                // drop under a panel parked near the old right or bottom edge. That is
                // the "I turn it on and it never appears" report.
                //
                // EnsureOnScreen already existed for exactly this — it was only ever
                // called from the display-change event, which of course never fires when
                // the monitor was already gone at start-up.
                _captionHistoryForm.EnsureOnScreen();
                _captionHistoryForm.BringToFront();
                SyncCaptionHistoryMenu();
            }
            catch (Exception ex)
            {
                Utils.Logger.Warn("Could not show caption history: " + ex.Message);
            }
        }

        /// <summary>
        /// Pushes the current caption settings (size, font, colour, opacity,
        /// background) to any live overlay and history window. Called after Save so
        /// changes take effect immediately instead of only on the next toggle.
        /// </summary>
        private void ApplyCaptionSettingsToOverlays()
        {
            if (_settings == null) return;
            if (_captionOverlay != null && !_captionOverlay.IsDisposed)
            {
                _captionOverlay.SetFontSize(_settings.CaptionFontSize);
                _captionOverlay.SetMaxLines(_settings.CaptionMaxLines);
                _captionOverlay.SetOpacity(_settings.CaptionOpacity);
                _captionOverlay.SetFontFamily(_settings.CaptionFontFamily);
                _captionOverlay.SetTextColor(
                    System.Drawing.Color.FromArgb(_settings.CaptionColorArgb),
                    _settings.CaptionUseCustomColor);
                _captionOverlay.SetShowBackground(_settings.CaptionShowBackground);
            }
            if (_captionHistoryForm != null && !_captionHistoryForm.IsDisposed)
            {
                _captionHistoryForm.SetFontSize(_settings.CaptionFontSize);
                _captionHistoryForm.SetFontFamily(_settings.CaptionFontFamily);
                _captionHistoryForm.SetTextColor(
                    System.Drawing.Color.FromArgb(_settings.CaptionColorArgb),
                    _settings.CaptionUseCustomColor);
                _captionHistoryForm.SetTextOpacity(_settings.CaptionOpacity);
            }
        }

        private void ShowCaptionOverlay()
        {
            try
            {
                if (_captionOverlay == null || _captionOverlay.IsDisposed)
                {
                    _captionOverlay = new CaptionOverlayForm(_theme);
                    _captionOverlay.PositionChanged += p =>
                    {
                        if (_settings != null)
                        {
                            _settings.CaptionBarX = p.X;
                            _settings.CaptionBarY = p.Y;
                            try { SettingsManager.Save(_settings); } catch { }
                        }
                    };
                }
                if (_settings != null)
                {
                    _captionOverlay.SetFontSize(_settings.CaptionFontSize);
                    _captionOverlay.SetMaxLines(_settings.CaptionMaxLines);
                _captionOverlay.SetMaxLines(_settings.CaptionMaxLines);
                    _captionOverlay.SetOpacity(_settings.CaptionOpacity);
                    _captionOverlay.SetFontFamily(_settings.CaptionFontFamily);
                    _captionOverlay.SetTextColor(
                        System.Drawing.Color.FromArgb(_settings.CaptionColorArgb),
                        _settings.CaptionUseCustomColor);
                    _captionOverlay.SetShowBackground(_settings.CaptionShowBackground);
                    if (_settings.CaptionBarX >= 0 && _settings.CaptionBarY >= 0)
                    {
                        _captionOverlay.MoveTo(new Point(_settings.CaptionBarX, _settings.CaptionBarY));
                    }
                }
                // Show the slim "starting up" cue instead of a big empty panel with
                // a placeholder sentence, so there's no jarring empty caption box in
                // the gap before the first words arrive. The first real caption (from
                // whichever engine) clears it automatically.
                _captionOverlay.SetPending(true);
                if (!_captionOverlay.Visible)
                {
                    _captionOverlay.Show();
                }
                // Same off-screen restore as the history panel, and worse here: a bar
                // parked on a monitor that is no longer attached means captions never
                // appear AT ALL, with the engine running and the tray reporting them on.
                _captionOverlay.EnsureOnScreen();
                _captionOverlay.BringToFront();
            }
            catch (Exception ex)
            {
                Utils.Logger.Warn("Could not show caption overlay: " + ex.Message);
            }
        }

        /// <summary>
        /// Starts Tempo's own offline caption engine: it captures the PC's audio
        /// and transcribes it with Whisper, feeding text into the same overlay the
        /// Windows path uses. If no speech model is installed it tells the user how
        /// to add one and leaves the bar with a friendly note.
        /// </summary>
        // Set when the opted-in GPU engine proved unable to keep pace in THIS run.
        // Never saved: it dies with the process, so the next launch tries the GPU
        // again (a busy GPU is a temporary condition, not a broken one).
        private bool _gpuTooSlowThisSession;

        private void StartTempoCaptions()
        {
            _lastMirroredCaption = "";
            _captionHadAnyText = false;

            // The GPU already proved too slow this run. Whisper's native engine choice
            // is fixed for the process lifetime, so restarting the Tempo engine now
            // would just load the same starved GPU again — mirror Windows captions
            // until Tempo is restarted.
            if (_gpuTooSlowThisSession)
            {
                Utils.Logger.Info("[Captions] GPU was too slow earlier this session - staying on Windows captions.");
                FallBackToWindowsCaptions(
                    "The GPU speech engine couldn't keep up earlier in this session, so Tempo is mirroring Windows " +
                    "Live Captions. Restart Tempo to try its own engine on the GPU again.");
                return;
            }

            string wantedKey = _captionModelOverrideKey ??
                (_settings != null ? _settings.CaptionModelKey : "base");

            // If Tempo died inside this model last time, do NOT hand it to the native
            // engine again. Loading happens entirely in native code, so an access
            // violation there takes the whole process down and cannot be caught —
            // without this the user hits the identical crash every time they turn
            // captions on. Step down the ladder to the next installed model instead.
            string wantedFile = null;
            try
            {
                string probe = Utils.WhisperModelManager.ResolveInstalledPath(wantedKey);
                wantedFile = string.IsNullOrEmpty(probe) ? null : System.IO.Path.GetFileName(probe);
            }
            catch { }

            if (wantedFile != null && Utils.CaptionCrashGuard.IsQuarantined(wantedFile))
            {
                var safer = Utils.WhisperModelManager.SpeedOrder;
                string replacement = null;
                foreach (string key in safer)
                {
                    if (string.Equals(key, wantedKey, StringComparison.OrdinalIgnoreCase)) { continue; }
                    string candidate = Utils.WhisperModelManager.ResolveInstalledPath(key);
                    if (string.IsNullOrEmpty(candidate)) { continue; }
                    // Don't swap one crashing model for the same file under another key.
                    if (string.Equals(System.IO.Path.GetFileName(candidate), wantedFile,
                                      StringComparison.OrdinalIgnoreCase)) { continue; }
                    replacement = key;
                    break;
                }

                Utils.Logger.Warn("[Captions] '" + wantedKey + "' crashed Tempo on the previous run; " +
                                  (replacement != null ? "using '" + replacement + "' instead." : "no other model installed."));

                if (replacement != null)
                {
                    _captionModelOverrideKey = replacement;
                    wantedKey = replacement;
                    TempoNotify(9000, "Speech model changed",
                        "Tempo closed unexpectedly while loading the previous speech model, so it has switched to " +
                        "\"" + replacement + "\" for now. Pick a model again in Settings → Live Captions to retry the old one.",
                        ToolTipIcon.Warning, always: true);   // their model was changed for them
                }
            }

            // A model file the user pointed at explicitly outranks the built-in downloads.
            // Validated rather than trusted: the drive may be unplugged, the file moved or
            // deleted since it was chosen. Falling back to a built-in model with a clear
            // log line beats refusing to caption at all.
            string modelPath = null;
            string customPath = _settings != null ? (_settings.CaptionCustomModelPath ?? "") : "";
            if (customPath.Length > 0)
            {
                if (Utils.WhisperModelManager.LooksLikeModelFile(customPath))
                {
                    modelPath = customPath;
                }
                else
                {
                    Utils.Logger.Warn("[Captions] the chosen model file is missing or unreadable (" +
                                      customPath + ") — falling back to the built-in models.");
                }
            }

            if (modelPath == null)
            {
                modelPath = Utils.WhisperModelManager.ResolveInstalledPath(wantedKey);
            }

            // Remember which model actually loaded (Resolve may have fallen back to
            // any installed one), so the too-slow ladder starts from the right rung.
            //
            // An empty key means "not one of the known models" — a custom file, or one
            // discovered loose in the folder. That is NOT the same as "base", and the
            // difference matters: SpeedOrder runs slowest-first, so an unrecognised key
            // makes NextFasterInstalled start at index 0 and hand back large-v3, the
            // HEAVIEST model, as though it were a downgrade. See the ladder guard.
            _captionModelActiveKey = wantedKey;
            bool known = false;
            foreach (var m in Utils.WhisperModelManager.Available)
            {
                if (Utils.WhisperModelManager.PathFor(m) == modelPath)
                {
                    _captionModelActiveKey = m.Key;
                    known = true;
                    break;
                }
            }
            if (!known && modelPath != null)
            {
                _captionModelActiveKey = "";
                Utils.Logger.Info("[Captions] using a model Tempo doesn't ship: " +
                                  Utils.WhisperModelManager.DescribeModelFile(modelPath));
            }

            if (string.IsNullOrEmpty(modelPath))
            {
                // Tempo's own engine can't run without a model. Rather than just
                // sitting on an error, fall back to mirroring Windows Live Captions
                // so the user still gets captions. (Only auto-falls-back once per
                // toggle; guarded by _captionFellBackToWindows.)
                Utils.Logger.Info("[Captions] No Tempo speech model installed - falling back to Windows captions.");
                FallBackToWindowsCaptions(
                    "No offline speech model is installed yet, so Tempo is using Windows Live Captions for now. "
                    + "Add a model in Settings \u203a Behaviour \u203a Live Captions to caption fully offline.");
                return;
            }

            try
            {
                // Engine choice must be made before the first model load; the CPU
                // order is the default, GPU only when the user opted in.
                Utils.TempoTranscriber.ConfigureRuntime(_settings != null && _settings.CaptionTryGpu);
                if (_captionTranscriber == null)
                {
                    _captionTranscriber = new Utils.TempoTranscriber();
                    _captionTranscriber.TextRecognized += OnTempoCaptionText;
                    _captionTranscriber.Status += OnTempoCaptionStatus;
                    _captionTranscriber.RealTimeTooSlow += OnTempoModelTooSlow;
                    _captionTranscriber.RealTimeHeadroom += OnTempoEngineHeadroom;
                }
                _captionTranscriber.Mode = (Utils.CaptureMode)(_settings != null ? _settings.CaptionCaptureMode : 0);
                // Read at start time, so changing the language takes effect on the next
                // Start rather than needing a whole restart the way the GPU order does.
                _captionTranscriber.PreferredLanguage =
                    _settings != null ? (_settings.CaptionLanguage ?? "auto") : "auto";

                // Own-voice filtering (opt-in): only meaningful when captioning the
                // SPEAKERS — in microphone mode the user's voice IS the content.
                bool wantOwnVoice = _settings != null && _settings.CaptionFilterOwnVoice
                    && _settings.CaptionCaptureMode != 2;
                if (wantOwnVoice)
                {
                    if (_selfVoiceGuard == null) { _selfVoiceGuard = new Utils.SelfVoiceGuard(); }
                    _selfVoiceGuard.Start();          // best-effort; inactive without a mic
                    _captionTranscriber.OwnVoiceGuard = _selfVoiceGuard;
                }
                else
                {
                    _captionTranscriber.OwnVoiceGuard = null;
                    try { _selfVoiceGuard?.Stop(); } catch { }
                }

                if (_captionOverlay != null && !_captionOverlay.IsDisposed)
                {
                    _captionOverlay.SetPending(true);
                }
                _captionTranscriber.Start(modelPath);
            }
            catch (Exception ex)
            {
                Utils.Logger.Warn("Could not start Tempo captions: " + ex.Message);
                FallBackToWindowsCaptions(
                    "Tempo's own caption engine couldn't start, so it switched to Windows Live Captions.");
            }
        }

        /// <summary>
        /// The active model repeatedly took longer to transcribe a chunk than the
        /// chunk lasts — it can never keep up on this PC and captions would drift
        /// minutes behind. Drop to the next smaller INSTALLED model for this session
        /// (the saved setting is untouched) and restart the engine, telling the user
        /// what happened and why.
        /// </summary>
        private void OnTempoModelTooSlow()
        {
            UiInvoke(() =>
            {
                try
                {
                    if (!_captionsActive)
                    {
                        return;
                    }

                    // GPU guard: if the user's opted-in GPU engine is what can't keep
                    // pace, the model isn't the problem — so don't shrink models.
                    //
                    // This fallback is SESSION-ONLY and deliberately does NOT touch the
                    // saved setting. A GPU that's merely BUSY (a game running, as seen
                    // live: Large Turbo went from ~10x real time to 3x SLOWER than real
                    // time once Call of Duty had the card) is not a GPU that can't do
                    // the job. Persisting "GPU off" let one gaming session permanently
                    // disable GPU captions, and the user would never know why. The
                    // option stays on and the next launch tries the GPU again; a GPU
                    // that genuinely can't cope simply falls back again, costing a few
                    // seconds per launch instead of silently losing the feature.
                    if (_settings != null && _settings.CaptionTryGpu &&
                        _captionTranscriber != null &&
                        string.Equals(_captionTranscriber.RuntimeDescription, "Vulkan", StringComparison.OrdinalIgnoreCase))
                    {
                        Utils.Logger.Info("[Captions] GPU engine can't keep pace right now - falling back for THIS " +
                                          "session only; the GPU option stays on for the next start.");
                        // The native engine choice is fixed for the process lifetime, so
                        // the Tempo engine can't be re-run on the CPU here — remember the
                        // verdict and mirror Windows captions instead of restarting onto
                        // the same starved GPU every time captions are toggled.
                        _gpuTooSlowThisSession = true;
                        FallBackToWindowsCaptions(
                            "The GPU speech engine couldn't keep up with live audio right now — usually because a " +
                            "game or another app is using the graphics card. Tempo is mirroring Windows Live " +
                            "Captions for the rest of this session. The GPU option is left ON, so the next time you " +
                            "start Tempo it tries the GPU again.");
                        return;
                    }

                    // If we recently stepped BACK UP on measured headroom and the
                    // bigger model still couldn't hold pace, the machine has given
                    // its final answer — stay stepped down and stop trying, or the
                    // session bounces between models forever.
                    if (DateTime.UtcNow - _lastModelRecoveryUtc < TimeSpan.FromMinutes(10))
                    {
                        _modelRecoveryBlocked = true;
                        Utils.Logger.Info("[Captions] the bigger model still can't keep up — staying " +
                                          "stepped down for the rest of this session.");
                    }

                    // A model Tempo doesn't ship has no rung on the ladder, and must not be
                    // handed to it. SpeedOrder runs slowest-first, so an unrecognised key
                    // scores -1 and NextFasterInstalled starts at index 0 — returning
                    // large-v3, the heaviest model there is, as the "faster" replacement.
                    // It would also be silently discarding a file the user deliberately
                    // chose. Say it cannot keep up and leave their choice alone.
                    if (string.IsNullOrEmpty(_captionModelActiveKey))
                    {
                        if (_captionOverlay != null && !_captionOverlay.IsDisposed)
                        {
                            _captionOverlay.SetCaption(Localization.T(
                                "This speech model is too slow for live captions on this PC. Pick a smaller one."));
                        }
                        Utils.Logger.Info("[Captions] the chosen model file can't keep up, and Tempo can't " +
                                          "size an unknown model — leaving it in place.");
                        return;
                    }

                    // One shared ladder (WhisperModelManager.SpeedOrder), which also
                    // refuses to drop a multilingual model onto an English-only one.
                    string smaller = Utils.WhisperModelManager
                        .NextFasterInstalled(_captionModelActiveKey ?? "base");
                    if (smaller == null)
                    {
                        if (_captionOverlay != null && !_captionOverlay.IsDisposed)
                        {
                            _captionOverlay.SetCaption(Localization.T(
                                "This speech model is too slow for live captions on this PC and no smaller model is installed."));
                        }
                        return;
                    }

                    Utils.Logger.Info("[Captions] model '" + _captionModelActiveKey +
                        "' too slow for real time - switching to '" + smaller + "' for this session.");
                    _captionModelOverrideKey = smaller;
                    StopTempoCaptions();
                    StartTempoCaptions();
                    // Once per session, not once per downgrade. The ladder can step down,
                    // recover on sustained headroom, then step down again — and each pass
                    // used to raise the same toast, which is why this read as Tempo
                    // repeating something the user had already been told several times.
                    // The log still records every switch; only the interruption is capped.
                    if (_trayIcon != null && _settings != null && _settings.ShowTrayNotifications
                        && !_modelDowngradeNotified)
                    {
                        _modelDowngradeNotified = true;
                        var mi = Utils.WhisperModelManager.FindByKey(smaller);
                        TempoNotify(6000, "Tempo",
                            Localization.T("That speech model can't keep up with live audio on this PC, so Tempo switched to") +
                            " " + (mi != null ? mi.Label : smaller) + " " +
                            Localization.T("for now. Your saved model choice is unchanged."),
                            ToolTipIcon.Info);
                    }
                }
                catch (Exception ex)
                {
                    Utils.Logger.Warn("Model downgrade failed: " + ex.Message);
                }
            });
        }

        // Recovery state for the too-slow ladder: when the last up-step happened
        // (an immediate re-downgrade means the machine truly can't hold the bigger
        // model) and whether up-steps are done for this session.
        private DateTime _lastModelRecoveryUtc = DateTime.MinValue;
        private bool _modelRecoveryBlocked;
        // One "Tempo switched to a smaller model" toast per session, however many times
        // the ladder actually moves.
        private bool _modelDowngradeNotified;

        /// <summary>
        /// The other direction of the too-slow ladder. Fired by the engine after
        /// minutes of sustained headroom: whatever was hogging the machine (a game,
        /// a render, a huge install) has stopped, so a session-only downgrade can
        /// step back UP toward the model the user actually chose — one rung at a
        /// time, never during a game, and never again this session if the bigger
        /// model immediately proves too slow after all.
        /// </summary>
        private void OnTempoEngineHeadroom()
        {
            UiInvoke(() =>
            {
                try
                {
                    if (!_captionsActive || _modelRecoveryBlocked ||
                        string.IsNullOrEmpty(_captionModelOverrideKey) ||
                        Utils.TempoTranscriber.LowImpactMode)
                    {
                        return;
                    }

                    var ladder = Utils.WhisperModelManager.SpeedOrder;
                    string savedKey = _settings != null && !string.IsNullOrEmpty(_settings.CaptionModelKey)
                        ? _settings.CaptionModelKey : "base";
                    int userIdx = Utils.WhisperModelManager.IndexInSpeedOrder(savedKey);
                    int curIdx = Utils.WhisperModelManager.IndexInSpeedOrder(_captionModelOverrideKey);
                    if (userIdx < 0 || curIdx <= 0 || curIdx <= userIdx)
                    {
                        return;   // already at (or above) the user's choice
                    }

                    // One rung up toward the saved choice, skipping uninstalled rungs
                    // the same way the downgrade skipped them coming down.
                    string bigger = null;
                    for (int i = curIdx - 1; i >= userIdx; i--)
                    {
                        var m = Utils.WhisperModelManager.FindByKey(ladder[i]);
                        if (m != null && Utils.WhisperModelManager.IsInstalled(m))
                        {
                            bigger = ladder[i];
                            break;
                        }
                    }
                    if (bigger == null)
                    {
                        return;
                    }

                    _lastModelRecoveryUtc = DateTime.UtcNow;
                    Utils.Logger.Info("[Captions] engine has real headroom again — stepping back up to '" +
                                      bigger + "' (your chosen model: '" + savedKey + "').");
                    _captionModelOverrideKey = bigger == savedKey ? null : bigger;
                    StopTempoCaptions();
                    StartTempoCaptions();
                    if (_trayIcon != null && _settings != null && _settings.ShowTrayNotifications)
                    {
                        var mi = Utils.WhisperModelManager.FindByKey(bigger);
                        TempoNotify(6000, "Tempo",
                            Localization.T("Your PC has speed to spare again, so captions switched back up to") +
                            " " + (mi != null ? mi.Label : bigger) + ".",
                            ToolTipIcon.Info);
                    }
                }
                catch (Exception ex)
                {
                    Utils.Logger.Warn("Model recovery failed: " + ex.Message);
                }
            });
        }

        private void StopTempoCaptions()
        {
            try { _captionTranscriber?.Stop(); } catch { }
            // The mic monitor only needs to run while captions do — stopping it also
            // clears the mic-in-use indicator promptly.
            try { _selfVoiceGuard?.Stop(); } catch { }
        }

        /// <summary>
        /// Switches the live caption session from Tempo's own offline engine over to
        /// mirroring Windows 11 Live Captions. Called automatically when Tempo's own
        /// engine can't run (no model, missing engine files, no audio device) so the
        /// user still gets captions instead of an error. Runs at most once per
        /// caption session (guarded by <see cref="_captionFellBackToWindows"/>), only
        /// while captions are still on, and only if a Tempo source was selected -
        /// it never overrides a user who explicitly chose Windows. A short note tells
        /// the user it happened; the chosen source setting is NOT changed, so the
        /// next time they turn captions on Tempo tries its own engine again.
        /// </summary>
        private void FallBackToWindowsCaptions(string reason)
        {
            // Only fall back once per session, and only if captions are still on.
            if (_captionFellBackToWindows || !_captionsActive) return;
            _captionFellBackToWindows = true;

            try
            {
                // Make sure Tempo's own engine is fully stopped first.
                StopTempoCaptions();

                // Spin up the Windows mirror path (same as a Windows-source session).
                EnsureWindowsCaptions(true);
                StartCaptionMirror();

                if (_captionOverlay != null && !_captionOverlay.IsDisposed)
                {
                    // Stay in the slim "pending" cue until Windows produces text.
                    _captionOverlay.SetPending(true);
                }

                if (_trayIcon != null && _settings != null && _settings.ShowTrayNotifications
                    && !string.IsNullOrEmpty(reason))
                {
                    TempoNotify(5000, "Tempo Live Captions", reason, ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                Utils.Logger.Warn("Fallback to Windows captions failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Called when the Windows-mirror path detects that UI Automation is broken on
        /// this PC (the CacheRequest type-initializer failure), which makes reading the
        /// Windows caption text impossible. Stops the mirror and starts Tempo's own
        /// offline engine instead, which needs no UIA. The user is told once, clearly.
        /// </summary>
        private void SwitchToTempoEngineDueToBrokenUia()
        {
            if (!_captionsActive) return;
            try
            {
                Utils.Logger.Info("[Captions] UI Automation is broken on this PC; switching to Tempo's own engine.");
                StopCaptionMirror();
                EnsureWindowsCaptions(false);

                // Start Tempo's own engine. If no model is installed, StartTempoCaptions
                // shows the install guidance instead.
                StartTempoCaptions();

                if (_trayIcon != null && _settings != null && _settings.ShowTrayNotifications)
                {
                    TempoNotify(6000, "Tempo Live Captions",
                        "Windows Live Captions can't be read on this PC (its accessibility API "
                        + "isn't working), so Tempo switched to its own offline captions.",
                        ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                Utils.Logger.Warn("Switch to Tempo engine failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Applies the own-voice-filter setting to a RUNNING caption session, so the
        /// checkbox works immediately instead of only on the next caption start.
        /// </summary>
        private void ApplyOwnVoiceGuardLive()
        {
            try
            {
                if (_captionTranscriber == null || !_captionTranscriber.IsRunning)
                {
                    return;                     // next start picks the setting up
                }
                bool want = _settings != null && _settings.CaptionFilterOwnVoice
                    && _settings.CaptionCaptureMode != 2;
                if (want)
                {
                    if (_selfVoiceGuard == null) { _selfVoiceGuard = new Utils.SelfVoiceGuard(); }
                    _selfVoiceGuard.Start();
                    _captionTranscriber.OwnVoiceGuard = _selfVoiceGuard;
                }
                else
                {
                    _captionTranscriber.OwnVoiceGuard = null;
                    _selfVoiceGuard?.Stop();
                }
            }
            catch (Exception ex)
            {
                Utils.Logger.Warn("[OwnVoice] live apply failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Starts or stops the voice profiler and face analyzer to match the current
        /// "Label speakers" / "AI face analysis" settings on a RUNNING caption session —
        /// so toggling either in Settings takes effect immediately instead of silently
        /// leaving the degraded pause-count mode (or a pointless screen-capture) until
        /// the next caption toggle. Mirrors the start block in SetCaptionsActive(true).
        /// </summary>
        private void ApplySpeakerTurnsLive()
        {
            try
            {
                if (!_captionsActive) { return; }   // next start picks the settings up

                if (_settings != null && _settings.CaptionSpeakerTurns)
                {
                    MaybeShowSpeakerNotice();
                    // The notice can turn labels back off (the "no thanks" button).
                    if (_settings.CaptionSpeakerTurns)
                    {
                        try
                        {
                            if (_voiceProfiler == null) { _voiceProfiler = new Utils.VoiceProfiler(); }
                            _voiceProfiler.Start();
                        }
                        catch { }
                        if (_settings.CaptionFaceAnalysis)
                        {
                            try
                            {
                                if (_faceAnalyzer == null) { _faceAnalyzer = new Utils.FaceSpeakerAnalyzer(); }
                                _faceAnalyzer.PreferredWindow =
                                    () => _mediaDetector != null ? _mediaDetector.CurrentAudioWindow : IntPtr.Zero;
                                _faceAnalyzer.Start();
                            }
                            catch { }
                        }
                        else
                        {
                            try { _faceAnalyzer?.Stop(); } catch { }   // labels on, face off
                        }
                        _speakerTurns.Reset();   // numbering restarts cleanly at Speaker 1
                    }
                    else
                    {
                        try { _voiceProfiler?.Stop(); } catch { }
                        try { _faceAnalyzer?.Stop(); } catch { }
                    }
                }
                else
                {
                    try { _voiceProfiler?.Stop(); } catch { }
                    try { _faceAnalyzer?.Stop(); } catch { }
                }
            }
            catch (Exception ex)
            {
                Utils.Logger.Warn("[Captions] speaker-labels live apply failed: " + ex.Message);
            }
        }

        /// <summary>
        /// True when two or more apps are audible and none of them is loud enough to be
        /// "the" source — the state that produces spliced, half-sensible captions.
        ///
        /// WHY THIS EXISTS AS ONE METHOD: loopback capture is device-wide. Tempo receives
        /// the SAME single mixed stream Windows sends to the speakers, so when two videos
        /// play at once there is no second stream to separate — both voices arrive already
        /// summed into one waveform, and Whisper transcribes them as one speaker taking
        /// turns. The result reads like somebody said a sentence that nobody said. Tempo
        /// cannot un-mix that; what it CAN do is recognise the condition and say so, in
        /// every place the user might be looking. Those places used to test the thresholds
        /// separately, which is how surfaces drift apart and start contradicting one
        /// another. One method, one answer.
        /// </summary>
        private bool AudioSourcesAreMixed(out int apps, out int dominancePercent)
        {
            apps = 0;
            dominancePercent = 0;
            if (_mediaDetector == null) { return false; }

            apps = _mediaDetector.AudibleAppCount;
            float dom = _mediaDetector.SourceDominance;
            dominancePercent = (int)Math.Round(dom * 100);

            // Two audible apps is normal (a game plus a quiet music player) and captions
            // are fine as long as one clearly wins. It is the NEAR-TIE that garbles them.
            return apps > 1 && dom < MixedSourceDominance;
        }

        /// <summary>
        /// Below this dominance the top app is not carrying the mix and the engine is
        /// hearing a genuine blend. Deliberately the same constant everywhere.
        ///
        /// Dominance is 1 − (runner-up ÷ loudest) on smoothed amplitude, so the cutoff is
        /// really a signal-to-interference limit:
        ///     0.35 → warns only while the second app is within  −3.7 dB (a dead tie)
        ///     0.65 → warns while the second app is within       −9.1 dB
        /// Speech recognition falls apart once an interfering voice is within about 10 dB,
        /// and 0.35 sat nowhere near that. Measured against the reported case — two videos
        /// with the second at HALF volume, −6 dB — the old cutoff scored dominance 0.50 and
        /// stayed silent, while the captions were plainly transcribing both. That is the
        /// complaint this constant exists to catch, so it has to reach past −6 dB.
        /// </summary>
        private const float MixedSourceDominance = 0.65f;

        /// <summary>
        /// The "♪ …" label shown on the bar in front of the caption text.
        ///
        /// THE BUG THIS FIXES: the tag names whichever app is loudest, but the TEXT beside
        /// it came from the whole mixed device output. With two videos playing, the bar read
        /// "♪ YouTube · &lt;a sentence blended out of YouTube and Netflix&gt;" — it put one app's
        /// name in front of words that app never said, which is worse than no tag at all,
        /// because it invites the reader to believe a specific source said the combined
        /// sentence. When no app clearly leads, say so instead of picking a scapegoat.
        ///
        /// Kept SEPARATE from the value <see cref="UpdateCaptionSourceTag"/> returns on
        /// purpose: that string is the source IDENTITY, compared against _lastVoiceSource to
        /// decide whether to reset speaker numbering. Decorating it there would make the
        /// identity change every time a second app started or stopped, and churn the voice
        /// profiles that the 20 s reset guard above exists to protect.
        /// </summary>
        private string CaptionSourceTagText(string src)
        {
            if (string.IsNullOrEmpty(src)) { return src; }
            if (AudioSourcesAreMixed(out int apps, out int _) && apps > 1)
            {
                return src + " + " + (apps - 1) + " more";
            }
            return src;
        }

        /// <summary>
        /// How many lines of caption history the bar is keeping, clamped to the same
        /// range the overlay accepts. Used to size the rolling text buffer that feeds it.
        /// </summary>
        private int CaptionLineBudget()
        {
            int lines = _settings != null ? _settings.CaptionMaxLines : 6;
            return Math.Max(1, Math.Min(12, lines));
        }

        private void OnTempoCaptionText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            UiInvoke(() =>
            {
                if (!_captionsActive) return;
                string audioSource = UpdateCaptionSourceTag();
                _captionHadAnyText = true;

                // Whisper occasionally emits stray symbol tokens ("◆", box glyphs)
                // on hard audio — strip anything that isn't text or normal
                // punctuation so artifacts never reach the bar. The allowlist includes
                // CJK punctuation: the engine captions 90+ languages, and this filter
                // used to eat every 。、！？ so Japanese/Chinese lines lost all their
                // sentence marks.
                var sbClean = new System.Text.StringBuilder(text.Length);
                foreach (char c in text)
                {
                    if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) ||
                        ".,!?'’\"“”-—–:;()[]%&$€£+/。、，！？：；「」『』（）【】…・〜".IndexOf(c) >= 0)
                    {
                        sbClean.Append(c);
                    }
                }
                string clean = sbClean.ToString().Trim();
                if (clean.Length == 0) return;
                if (_wordFixer != null) { clean = _wordFixer.Fix(clean); }
                if (clean != _lastMirroredCaption)
                {
                    _lastMirroredCaption = clean;

                    // Build one long RUNNING line out of the engine's short chunks —
                    // the same reading experience as Windows Live Captions, instead of
                    // the bar being wiped every couple of seconds by the next fragment.
                    // The budget scales with how many lines the bar is set to keep.
                    // It was a flat ~360 chars, which is about three lines' worth — so
                    // even a taller bar had nothing to put in the extra rows, and text
                    // still vanished after a few seconds. ~120 chars per line keeps the
                    // buffer just ahead of what the bar can actually show.
                    int budget = 120 * CaptionLineBudget();
                    _tempoRollingLine = _tempoRollingLine.Length == 0
                        ? clean
                        : _tempoRollingLine + " " + clean;
                    if (_tempoRollingLine.Length > budget)
                    {
                        // Shed old words from the front — at a SENTENCE start when one
                        // exists near the cut (Latin OR CJK marks), so the line never
                        // begins with a dangling mid-sentence fragment ("..go that
                        // park."); a word boundary is the fallback when a monologue runs
                        // long without punctuation.
                        int from = _tempoRollingLine.Length - budget;
                        int cut = -1;
                        int searchEnd = Math.Min(_tempoRollingLine.Length - 40, from + 80);
                        for (int i = from; i < searchEnd; i++)
                        {
                            int r = Utils.SpeakerTurnLabeler.SentenceCutAfter(_tempoRollingLine, i);
                            if (r > 0) { cut = r; break; }   // resume index past the mark
                        }
                        int shed;
                        if (cut > 0 && cut < _tempoRollingLine.Length - 1)
                        {
                            shed = cut;
                        }
                        else
                        {
                            int sp = _tempoRollingLine.IndexOf(' ', from);
                            shed = sp > 0 && sp < _tempoRollingLine.Length - 1 ? sp + 1 : from;
                        }
                        _tempoRollingLine = _tempoRollingLine.Substring(shed);
                        // The engine appends AND front-trims in one atomic update, which
                        // the labeler's own roll detection can't see — tell it about the
                        // shed so its turn baseline stays aligned (else it gets stuck in
                        // the whole-line fallback, leaking prior turns under one label).
                        // Unconditional so state stays consistent if labels toggle.
                        _speakerTurns.NoteFrontShed(shed);
                    }

                    // The engine batches text every ~2.5 s even mid-sentence, so the
                    // turn threshold must exceed that or every batch becomes a "turn".
                    _speakerTurns.TurnGapSeconds = 4.0;
                    _speakerTurns.VoiceDriven = _voiceProfiler != null && _voiceProfiler.Running;
                    string shown = _settings != null && _settings.CaptionSpeakerTurns
                        ? _speakerTurns.Label(_tempoRollingLine, EffectiveSpeakerHint())
                        : _tempoRollingLine;
                    // The "♪ App ·" tag goes to the BAR only, never into history: it
                    // trades apps every second or two with dual audio, and any tag change
                    // broke the history dedup — re-adding the whole ~360-char line as a
                    // new entry and filling the transcript with duplicated paragraphs.
                    string tagged = (audioSource.Length > 0 &&
                                     (_settings == null || _settings.CaptionShowSourceTag))
                        ? "♪ " + CaptionSourceTagText(audioSource) + "  ·  " + shown
                        : shown;
                    if (_captionOverlay != null && !_captionOverlay.IsDisposed)
                    {
                        _captionOverlay.SetCaption(tagged);
                    }
                    AppendCaptionHistory(shown);
                }
            });
        }

        private void OnTempoCaptionStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status)) return;
            UiInvoke(() =>
            {
                Utils.Logger.Info("[Captions] " + status);
                if (!_captionsActive || _captionHadAnyText) return;
                if (_captionOverlay == null || _captionOverlay.IsDisposed) return;

                // A hard problem (missing engine files, can't load model, no audio
                // device) shouldn't dump a long technical sentence onto the caption
                // bar. Show a short, friendly line there and put the full detail in a
                // tray balloon + the log so the user still knows what to do.
                bool isProblem =
                    status.IndexOf("missing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    status.IndexOf("couldn't", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    status.IndexOf("could not", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    status.IndexOf("no speech model", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    status.IndexOf("no audio device", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    status.IndexOf("reinstall", StringComparison.OrdinalIgnoreCase) >= 0;

                if (isProblem)
                {
                    // Tempo's own engine hit a hard wall (missing engine files, can't
                    // load the model, no audio device). Don't leave the user stuck:
                    // automatically switch to mirroring Windows Live Captions, once.
                    Utils.Logger.Info("[Captions] Tempo engine problem - auto-falling back to Windows captions: " + status);
                    FallBackToWindowsCaptions(
                        "Tempo's own captions aren't available on this PC, so it switched to Windows Live Captions.");
                }
                else
                {
                    // Transient info (e.g. "no audio playing yet") is fine on the bar.
                    _captionOverlay.SetCaption(status);
                }
            });
        }

        /// <summary>
        /// Begins polling Windows Live Captions for its transcribed text and
        /// mirroring it into Tempo's overlay. Windows needs a moment to spawn its
        /// window after the Win+Ctrl+L toggle, so the first ticks just keep trying
        /// to locate it. Once text is flowing, the Windows bar is parked off-screen
        /// so only Tempo's clean bar shows.
        /// </summary>
        private void StartCaptionMirror()
        {
            _lastMirroredCaption = "";
            _captionMirrorMisses = 0;
            _captionHadAnyText = false;
            _captionDiagLogged = false;
            if (_captionReader == null)
            {
                _captionReader = new Utils.LiveCaptionReader();
            }
            _captionReader.Reset();

            // Poll on a background thread-pool timer (one-shot, re-armed each pass) so a
            // slow UI Automation read can never block Tempo's UI thread.
            _captionMirrorRunning = true;
            System.Threading.Interlocked.Exchange(ref _captionMirrorTickGuard, 0);
            if (_captionMirrorTimer == null)
            {
                _captionMirrorTimer = new System.Threading.Timer(
                    CaptionMirrorPoll, null, 250, System.Threading.Timeout.Infinite);
            }
            else
            {
                try { _captionMirrorTimer.Change(250, System.Threading.Timeout.Infinite); } catch { }
            }

            // Aggressively hide the Windows bar during the startup window, so it never
            // lingers visible in its "Ready to show live captions" state.
            StartCaptionHideEnforcer();
        }

        /// <summary>
        /// Runs a fast (120ms) UI-thread timer for ~6 seconds right after captions turn
        /// on, repeatedly shoving the Windows Live Captions bar off-screen. This closes
        /// the gap where Windows shows its "Ready to show live captions" bar before any
        /// text exists - the normal read-poll could be slow to win that race, leaving
        /// the bar visible. Stops itself after the window or once text is flowing.
        /// </summary>
        private void StartCaptionHideEnforcer()
        {
            _captionHideEnforcerTicks = 0;
            try { _captionHideEnforcer?.Stop(); _captionHideEnforcer?.Dispose(); } catch { }
            _captionHideEnforcer = new System.Windows.Forms.Timer { Interval = 120 };
            _captionHideEnforcer.Tick += (s, e) =>
            {
                _captionHideEnforcerTicks++;
                // Stop if captions were turned off, the source changed away from
                // Windows, or we've run for ~6s (50 * 120ms). The normal read-poll keeps
                // the bar hidden after that.
                if (!_captionsActive
                    || (_settings != null && _settings.CaptionSource != CaptionSource.Windows
                        && _settings.CaptionSource != CaptionSource.Auto)
                    || _captionHideEnforcerTicks > 50)
                {
                    StopCaptionHideEnforcer();
                    return;
                }
                try { _captionReader?.HideWindowsBar(); } catch { }
            };
            _captionHideEnforcer.Start();
        }

        private void StopCaptionHideEnforcer()
        {
            try { _captionHideEnforcer?.Stop(); _captionHideEnforcer?.Dispose(); } catch { }
            _captionHideEnforcer = null;
        }

        private void StopCaptionMirror()
        {
            _captionMirrorRunning = false;
            StopCaptionHideEnforcer();
            try { _captionMirrorTimer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite); } catch { }
            // Put the Windows bar back where the user had it.
            try { _captionReader?.RestoreWindowsBar(); } catch { }
            try { _captionReader?.Reset(); } catch { }
        }

        /// <summary>
        /// Background-thread poll: reads the Live Captions text via UI Automation (the
        /// part that can block), then marshals just the resulting string to the UI thread.
        /// Re-arms itself for the next pass so a slow read never overlaps the next one.
        /// </summary>
        private void CaptionMirrorPoll(object state)
        {
            if (!_captionMirrorRunning)
            {
                return;
            }
            // Skip this pass if the previous read is still running (UIA was slow).
            if (System.Threading.Interlocked.CompareExchange(ref _captionMirrorTickGuard, 1, 0) != 0)
            {
                return;
            }

            bool textChanged = false;
            try
            {
                string text = null;
                bool found = false;
                try { text = _captionReader?.ReadText(); } catch { }
                try { found = _captionReader != null && _captionReader.Found; } catch { }

                // Track whether Windows' text is actively MOVING — that decides how
                // hard the mirror polls (see the adaptive re-arm below).
                textChanged = !string.IsNullOrEmpty(text) &&
                              !string.Equals(text, _mirrorLastPolled, StringComparison.Ordinal);
                if (textChanged) { _mirrorLastPolled = text; _mirrorIdlePasses = 0; }
                else if (_mirrorIdlePasses < 1000) { _mirrorIdlePasses++; }

                // Park the Windows bar off-screen as soon as the window is FOUND, not
                // only once it has produced text. Waiting for text meant that on a
                // silent/blank video (Windows captioning the mic, or no speech yet) the
                // Windows bar sat visible forever - exactly the "it opened but never
                // hid" report. Reading the text keeps working while the window is parked
                // off-screen, so hiding early costs nothing. A safety net below restores
                // it if reading turns out to be impossible on this PC for a long stretch.
                if (found)
                {
                    // Hide right away. But if we've hidden it and STILL can't read any
                    // text after a long stretch, reading is broken on this PC - bring
                    // the Windows bar back so the user at least sees Windows' own
                    // captions rather than nothing at all.
                    if (!_captionHadAnyText && _captionMirrorMisses >= 60)
                    {
                        try { _captionReader?.RestoreWindowsBar(); } catch { }
                    }
                    else
                    {
                        try { _captionReader?.HideWindowsBar(); } catch { }
                    }
                }

                // If UI Automation itself is broken on this PC (the "CacheRequest type
                // initializer" failure), the Windows-mirror path can NEVER read text
                // here - no amount of retrying helps. Switch to Tempo's own offline
                // engine instead, which doesn't use UIA at all. Done once per session.
                bool uiaBroken = false;
                try { uiaBroken = _captionReader != null && _captionReader.UiaBroken; } catch { }
                if (uiaBroken && !_captionUiaFallbackDone)
                {
                    _captionUiaFallbackDone = true;
                    if (_captionMirrorRunning && IsHandleCreated && !IsDisposed)
                    {
                        try { BeginInvoke((Action)SwitchToTempoEngineDueToBrokenUia); } catch { }
                    }
                    return;
                }

                // If captions are on but the window still can't be found after a couple of
                // seconds, dump every visible/UIA window ONCE so its real title/class/process
                // can be matched. This is the data needed to finally pin the Win11 lookup;
                // the line lands in %LOCALAPPDATA%\AutoClicker\logs as "[caption-diag]".
                if (!found && !_captionDiagLogged && _captionMirrorMisses >= 13)
                {
                    _captionDiagLogged = true;
                    try
                    {
                        string dump = _captionReader?.DescribeCandidateWindows();
                        if (!string.IsNullOrEmpty(dump))
                        {
                            Logger.Info("[Captions] Windows Live Captions window not found; candidate windows:\n" + dump);
                        }
                    }
                    catch { }
                }

                if (_captionMirrorRunning && IsHandleCreated && !IsDisposed)
                {
                    try { BeginInvoke((Action<string, bool>)ApplyMirroredCaption, text, found); }
                    catch { /* form tearing down */ }
                }
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _captionMirrorTickGuard, 0);
                if (_captionMirrorRunning)
                {
                    // Adaptive cadence: poll HARD while Windows' text is moving (140 ms
                    // shaves ~100 ms average mirror latency vs the old fixed 250 ms),
                    // ease off during short gaps, and idle gently through silence so
                    // UI Automation isn't hammered for nothing.
                    int delay = textChanged ? 140
                              : _mirrorIdlePasses < 10 ? 200
                              : 350;
                    try { _captionMirrorTimer?.Change(delay, System.Threading.Timeout.Infinite); } catch { }
                }
            }
        }

        // Adaptive-mirror state: last text seen by the POLL thread and how many
        // consecutive passes it has been unchanged.
        private string _mirrorLastPolled = "";
        private int _mirrorIdlePasses;

        // Which app the captioned audio last came from (for the overlay tag and for
        // resetting speaker numbering when the app changes).
        private string _lastVoiceSource = "";
        // Last time the FACE source gave a confident verdict — used to hold the label's
        // number space steady across brief face-confidence dips (see EffectiveSpeakerHint).
        private DateTime _lastVisualHintUtc = DateTime.MinValue;
        // Running caption line built from Tempo's own engine's short chunks.
        private string _tempoRollingLine = "";

        /// <summary>
        /// The speaker number fed to the caption labeler. SIGHT beats SOUND when both
        /// are available: a face whose mouth is visibly moving is much stronger
        /// evidence than a pitch match — but the two use different number spaces
        /// (face slots vs voice profiles), so whichever source is currently
        /// confident owns the numbering rather than mixing them mid-conversation.
        /// </summary>
        private int EffectiveSpeakerHint()
        {
            try
            {
                int visual = _faceAnalyzer != null && _faceAnalyzer.Running ? _faceAnalyzer.CurrentVisualSpeaker : 0;
                if (visual > 0)
                {
                    _lastVisualHintUtc = DateTime.UtcNow;
                    return visual;
                }
                // CROSSTALK: two faces visibly talking at once. The voice guess is
                // meaningless during an overlap — the pitch tracker reads whichever
                // voice is momentarily louder, so following it flip-flops the label
                // mid-sentence. No hint = the labeler HOLDS the current speaker until
                // the overlap resolves, which is what a human captioner does too.
                if (_faceAnalyzer != null && _faceAnalyzer.Running && _faceAnalyzer.CrossTalk)
                {
                    return 0;
                }
                // The face verdict drops to 0 the instant mouth-motion confidence dips
                // (between phrases, a head turn) — but face slots and voice profiles are
                // DIFFERENT number spaces. Falling straight through to the voice number
                // then ping-pongs the label (face 2 → voice 1 → face 2) on every pause.
                // While faces are still on screen and the face source was confident very
                // recently, HOLD (return 0) instead of switching number spaces; the
                // labeler keeps the current number. Voice numbering resumes only once the
                // face source has been cold for the whole window (or faces are gone —
                // FaceCount hits 0 in game mode / true loss, releasing the hold).
                if (_faceAnalyzer != null && _faceAnalyzer.Running && _faceAnalyzer.FaceCount > 0
                    && (DateTime.UtcNow - _lastVisualHintUtc).TotalSeconds < 4.0)
                {
                    return 0;
                }
                return _voiceProfiler != null ? _voiceProfiler.CurrentSpeaker : 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// One-time, before speaker labels are first used: warn that the AI numbers
        /// make plenty of mistakes (similar voices, split speakers, music) so they're
        /// read as an aid, not identification. A dialog when the window is visible
        /// (auto-closes; offers one-click "turn labels off"); a tray balloon when
        /// captions were started from the tray/hotkey with the window hidden — a
        /// modal stealing focus mid-game would be worse than the warning is worth.
        /// </summary>
        private void MaybeShowSpeakerNotice()
        {
            try
            {
                if (_settings == null || _settings.SpeakerLabelsNoticeShown)
                {
                    return;
                }
                _settings.SpeakerLabelsNoticeShown = true;
                try { Persistence.SettingsManager.Save(_settings); } catch { }

                if (Visible && WindowState != FormWindowState.Minimized)
                {
                    using (var dlg = new SpeakerNoticeForm(_theme))
                    {
                        if (dlg.ShowDialog(this) == DialogResult.No)
                        {
                            _settings.CaptionSpeakerTurns = false;
                            if (_captionSpeakerCheck != null) { _captionSpeakerCheck.Checked = false; }
                            try { Persistence.SettingsManager.Save(_settings); } catch { }
                        }
                    }
                }
                else if (_trayIcon != null && _settings.ShowTrayNotifications)
                {
                    TempoNotify(8000, "Tempo",
                        "Heads-up: the \"Speaker 1/2\" caption labels are AI guesses from "
                        + "voice pitch and pauses — they make plenty of mistakes. Treat them "
                        + "as a reading aid; turn off in Settings → Live Captions.",
                        ToolTipIcon.Info);
                }
            }
            catch { }
        }

        /// <summary>
        /// Tracks which app the captioned audio comes from. Returns the current name
        /// ("YouTube", "Roblox", "VLC", ... or "" while quiet) for display, and — when
        /// the app making the sound CHANGES (a different game, another video site) —
        /// forgets the learned voices and restarts speaker numbering: those are
        /// different people, and matching them against the previous app's voices
        /// would give wrong numbers.
        /// </summary>
        // Last time the source-change RESET actually fired. With TWO apps audible at
        // once (game + video), the tag can legitimately trade back and forth — and
        // resetting the learned voices on every trade meant the voice profiles never
        // matured and the speaker numbering never settled (seen live: YouTube ↔
        // Call of Duty flipping for minutes, a reset every second or two).
        private DateTime _lastSpeakerResetUtc = DateTime.MinValue;

        private string UpdateCaptionSourceTag()
        {
            string src = "";
            try { src = _mediaDetector != null ? _mediaDetector.CurrentAudioSource : ""; } catch { }

            // Multi-monitor: put the caption bar on the monitor where the SOUND lives
            // (a game on the DisplayPort screen, a video on the other monitor) — but
            // only while the bar sits at its default spot. A user-dragged bar
            // (CaptionBarX/Y saved ≥ 0) is never moved.
            try
            {
                if (_settings != null && _settings.CaptionBarX < 0
                    && _captionOverlay != null && !_captionOverlay.IsDisposed && _captionOverlay.Visible
                    && _mediaDetector != null)
                {
                    _captionOverlay.MoveToScreenOf(_mediaDetector.CurrentAudioWindow);
                }
            }
            catch { }

            if (src.Length > 0)
            {
                if (_lastVoiceSource.Length > 0 && src != _lastVoiceSource)
                {
                    // Reset voices/numbering for a GENUINE app change — but at most
                    // once per 20 s. During dual-audio flapping the tag may keep
                    // trading; keeping the voices is far better than never letting
                    // them mature (the same people are still talking either way).
                    if ((DateTime.UtcNow - _lastSpeakerResetUtc).TotalSeconds >= 20)
                    {
                        _lastSpeakerResetUtc = DateTime.UtcNow;
                        try { _voiceProfiler?.ForgetVoices(); } catch { }
                        _speakerTurns.Reset();
                        Utils.Logger.Info("[Captions] audio source changed to " + src + " — speaker numbering reset.");
                    }
                    else
                    {
                        Utils.Logger.Info("[Captions] audio source tag → " + src + " (voices kept — recent reset).");
                    }
                }
                _lastVoiceSource = src;
            }
            return src;
        }

        /// <summary>Runs on the UI thread: pushes mirrored caption text into Tempo's bar.</summary>
        private void ApplyMirroredCaption(string text, bool windowFound)
        {
            if (!_captionsActive || _captionOverlay == null || _captionOverlay.IsDisposed)
            {
                return;
            }

            string audioSource = UpdateCaptionSourceTag();

            if (!string.IsNullOrEmpty(text))
            {
                // Repair clearly-misheard words before anything downstream sees the
                // text (deterministic + cached, so the rolling dedupe stays stable).
                if (_wordFixer != null) { text = _wordFixer.Fix(text); }
                _captionMirrorMisses = 0;
                _captionHadAnyText = true;

                if (text != _lastMirroredCaption)
                {
                    _lastMirroredCaption = text;
                    _lastCaptionTextUtc = DateTime.UtcNow;
                    _soundNoteShown = false;
                    // Label speaker turns on the way to the screen only — the raw text
                    // keeps feeding the dedupe above, so labelling can't break it.
                    _speakerTurns.TurnGapSeconds = 1.5;   // Windows streams every ~250 ms
                    _speakerTurns.VoiceDriven = _voiceProfiler != null && _voiceProfiler.Running;
                    string shown = _settings != null && _settings.CaptionSpeakerTurns
                        ? _speakerTurns.Label(text, EffectiveSpeakerHint())
                        : text;
                    // Show which app the audio comes from ("♪ YouTube · ...") on the BAR
                    // only — unless the user chose to hide the tag (info vs clutter). The
                    // tag never enters history: it trades apps with dual audio and any
                    // change re-added the whole line, duplicating the transcript.
                    string tagged = (audioSource.Length > 0 &&
                                     (_settings == null || _settings.CaptionShowSourceTag))
                        ? "♪ " + CaptionSourceTagText(audioSource) + "  ·  " + shown
                        : shown;
                    _captionOverlay.SetCaption(tagged);
                    AppendCaptionHistory(shown);
                }
                else
                {
                    // Same text as before — during long music/effects stretches Windows
                    // just keeps its last line on screen. Say what's happening instead.
                    MaybeShowSoundNote();
                }
            }
            else
            {
                // Windows Live Captions regularly returns an empty value between
                // phrases or during a brief UIA hiccup. If we already showed text,
                // DO NOT clear it - that was the "disappears mid-session" bug. Keep
                // the last line on screen and wait for the next words.
                _captionMirrorMisses++;
                if (!_captionHadAnyText &&
                    (_captionMirrorMisses == 1 || _captionMirrorMisses == 12))
                {
                    // No words yet - keep the slim "starting up" cue rather than a
                    // big empty panel with a placeholder sentence.
                    _captionOverlay.SetPending(true);
                }
                else if (_captionHadAnyText && !windowFound && _captionMirrorMisses == 25)
                {
                    // Distinct from an empty phrase (which keeps the text): the Live
                    // Captions WINDOW itself has been gone for a sustained run, so the
                    // line we're still showing is stale. Replace it with a neutral hint
                    // so it doesn't look like Tempo froze. windowFound==true with empty
                    // text is just a gap between phrases and is intentionally left alone,
                    // so this can't bring back the mid-session "captions vanish" bug.
                    _captionOverlay.SetCaption("Waiting for Live Captions\u2026");
                    _lastMirroredCaption = "";
                }
                else
                {
                    // Runs in the "listening…" state too: starting captions during
                    // pure music used to sit on the listening cue forever — now the
                    // ♪ note takes over once the audio is clearly non-speech.
                    MaybeShowSoundNote();
                }
            }
        }

        /// <summary>
        /// When audio keeps playing but nobody has SAID anything for a while (music,
        /// game sound effects, ambience), the bar would just hold the last stale
        /// sentence. Swap it for a "\u266a Music or sounds" note \u2014 the voice profiler can
        /// tell speech (stable human pitch) from other sound, which is what lets the
        /// captions describe non-speech audio instead of going quiet. The next real
        /// words replace the note immediately.
        /// </summary>
        private void MaybeShowSoundNote()
        {
            if (_voiceProfiler == null || !_voiceProfiler.Running)
            {
                return;
            }

            // Gate on the AUDIO having been continuously non-speech, not on the text
            // sitting still \u2014 Windows Live Captions keeps reflowing/rolling its line
            // even during pure music, so a "text unchanged for N seconds" clock kept
            // resetting and the note never appeared.
            if (_voiceProfiler.CurrentAudioKind != Utils.VoiceProfiler.AudioKind.Sound)
            {
                _soundKindSinceUtc = DateTime.MinValue;
                return;
            }
            if (_soundKindSinceUtc == DateTime.MinValue)
            {
                _soundKindSinceUtc = DateTime.UtcNow;
                return;
            }
            if (_soundNoteShown || (DateTime.UtcNow - _soundKindSinceUtc).TotalSeconds < 18)
            {
                return;
            }

            // NOTE: deliberately does NOT touch _lastMirroredCaption. Clearing it made
            // Windows' unchanged old line look "new" on the very next poll, which
            // overwrote this note 250 ms after it appeared (a note/stale-text flicker).
            // Left alone, the note stays until genuinely NEW words arrive \u2014 those
            // differ from _lastMirroredCaption and replace the note naturally.
            _soundNoteShown = true;
            try
            {
                // The app name obeys the same show/hide choice as the source tag.
                string src = _settings == null || _settings.CaptionShowSourceTag
                    ? _lastVoiceSource
                    : "";
                _captionOverlay.SetCaption(src.Length > 0
                    ? "\u266a " + src + "  \u00b7  " + Localization.T("Music or sounds playing \u2014 no speech")
                    : "\u266a " + Localization.T("Music or sounds playing \u2014 no speech"));
            }
            catch { }
        }

        /// <summary>Keeps the per-line timestamp list aligned with _captionHistory.</summary>
        private void SyncHistoryTime(bool added)
        {
            if (added)
            {
                _captionHistoryTimes.Add(DateTime.Now);
            }
            else if (_captionHistoryTimes.Count > 0)
            {
                _captionHistoryTimes[_captionHistoryTimes.Count - 1] = DateTime.Now;
            }
            // Self-heal any drift so the save never mispairs lines and times.
            while (_captionHistoryTimes.Count < _captionHistory.Count)
            {
                _captionHistoryTimes.Add(DateTime.Now);
            }
            while (_captionHistoryTimes.Count > _captionHistory.Count)
            {
                _captionHistoryTimes.RemoveAt(_captionHistoryTimes.Count - 1);
            }
        }

        /// <summary>
        /// Notices the user turning Windows Live Captions on THEMSELVES (Win+Ctrl+L)
        /// and brings Tempo's caption bar up automatically — that's what the "Show
        /// Tempo's caption overlay bar when Live Captions is on" setting promises.
        /// Polled cheaply (window handle lookup only) about every two seconds; a
        /// cooldown after Tempo's own off-toggle stops it re-triggering while the
        /// Windows bar is still being shut down.
        /// </summary>
        // Said once per session, and the last presence reading behind it.
        private bool _bothEnginesWarned;
        private bool _bothLcWasPresent;

        /// <summary>
        /// Notices Windows Live Captions being switched on WHILE Tempo's own engine is
        /// already running, and says so once.
        ///
        /// Tempo goes to some trouble to keep the two apart — the source switch stops one
        /// before starting the other, and starting Tempo's engine turns the Windows one
        /// off. None of that covers the user pressing Win+Ctrl+L afterwards. Nothing
        /// crashes: they are separate processes reading the same shared loopback. But two
        /// speech models then transcribe the same audio into two bars on screen, and on
        /// the caption-heavy setups this matters for — a big model, mid-game — that is a
        /// second engine's worth of CPU nobody asked for. Tempo could not see it happen.
        ///
        /// It TELLS rather than acts: Windows Live Captions is the user's own system
        /// feature, and having already switched it off once to take the audio, silently
        /// switching it off again while they are plainly trying to use it would be Tempo
        /// arguing with them.
        /// </summary>
        private void WarnIfWindowsCaptionsAlsoRunning()
        {
            if (_bothEnginesWarned || _settings == null) { return; }

            // Only meaningful while TEMPO'S OWN engine has the audio. When Tempo is
            // mirroring Windows — an explicit Windows source, or the automatic fallback —
            // the Windows bar being up is the entire point.
            if (_captionFellBackToWindows) { return; }
            if (_settings.CaptionSource == CaptionSource.Windows) { return; }
            if (_captionTranscriber == null || !_captionTranscriber.IsRunning) { return; }

            try
            {
                if (_captionReader == null) { _captionReader = new Utils.LiveCaptionReader(); }
                bool present = _captionReader.IsWindowPresentFast();
                bool justAppeared = present && !_bothLcWasPresent;
                // Track presence even while suppressed, so a transition that happens
                // DURING Tempo's own toggle isn't re-detected the moment it expires.
                _bothLcWasPresent = present;
                if (!justAppeared || DateTime.UtcNow < _externalWatchCooldownUntil) { return; }

                _bothEnginesWarned = true;
                Utils.Logger.Warn("[Captions] Windows Live Captions was switched on while Tempo's own " +
                    "engine is running: both are transcribing the same audio into their own bars.");
                TempoNotify(7000, "Two caption engines are running",
                    Localization.T("Windows Live Captions started while Tempo's own captions are on. " +
                        "Both are listening to the same audio, so you'll see two bars and pay for two " +
                        "engines. Press Win+Ctrl+L to turn the Windows one off."),
                    ToolTipIcon.Warning);
            }
            catch { }
        }

        private void WatchExternalLiveCaptions()
        {
            try
            {
                if (++_externalWatchTick % 8 != 0 || _settings == null)
                {
                    return;
                }
                // Captions already on: the question is no longer "should Tempo start?"
                // but "is something ELSE now transcribing the same audio?".
                if (_captionsActive)
                {
                    WarnIfWindowsCaptionsAlsoRunning();
                    return;
                }
                // Automatic starting is off: the user does not want captions coming on
                // by themselves — from a video, a game, OR the Windows bar appearing.
                // This path used to ignore that setting entirely, which is why captions
                // still switched themselves on after auto-start had been turned off.
                if (!_settings.CaptionOverlayEnabled || !_settings.CaptionAutoStart)
                {
                    return;
                }
                if (_captionReader == null)
                {
                    _captionReader = new Utils.LiveCaptionReader();
                }

                bool present = _captionReader.IsWindowPresentFast();
                bool justAppeared = present && !_externalLcWasPresent;
                _externalLcWasPresent = present;      // always track, even during cooldown

                // Only a fresh absent → present transition means "the user turned
                // Windows Live Captions on". A window that was ALREADY open (very often
                // one Tempo itself is still shutting down) must never re-arm captions.
                if (!justAppeared || DateTime.UtcNow < _externalWatchCooldownUntil)
                {
                    return;
                }

                Utils.Logger.Info("[Captions] Windows Live Captions was turned on externally — showing Tempo's bar.");
                SetCaptionsActive(true);
            }
            catch { }
        }

        /// <summary>
        /// Writes the finished session's transcript (with per-line timestamps) to
        /// %LOCALAPPDATA%\AutoClicker	ranscripts — opt-in via Settings, because it
        /// puts spoken content on disk.
        /// </summary>
        private void SaveTranscriptIfWanted()
        {
            try
            {
                if (_settings == null || !_settings.CaptionSaveTranscripts || _captionHistory.Count == 0)
                {
                    return;
                }
                string dir = System.IO.Path.Combine(
                    Persistence.SettingsManager.GetSettingsDirectory(), "transcripts");
                System.IO.Directory.CreateDirectory(dir);
                string file = System.IO.Path.Combine(dir,
                    "captions-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Tempo Live Captions transcript — " + DateTime.Now.ToString("f"));
                sb.AppendLine(new string('-', 60));
                for (int i = 0; i < _captionHistory.Count; i++)
                {
                    DateTime t = i < _captionHistoryTimes.Count ? _captionHistoryTimes[i] : DateTime.Now;
                    sb.AppendLine("[" + t.ToString("HH:mm:ss") + "]  " + _captionHistory[i]);
                }
                System.IO.File.WriteAllText(file, sb.ToString());
                Utils.Logger.Info("[Captions] transcript saved: " + file);
                if (_trayIcon != null && _settings.ShowTrayNotifications)
                {
                    TempoNotify(4000, "Tempo",
                        Localization.T("Caption transcript saved to") + " " + file, ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                Utils.Logger.Warn("Transcript save failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Accumulates caption text into the rolling history shown by the "Show
        /// full history" window. Live Captions re-sends a growing line as it
        /// refines a phrase, so we replace the in-progress tail rather than
        /// appending duplicates: if the new text extends the previous line we swap
        /// it; otherwise it is a new utterance and we add a line.
        /// </summary>
        private void AppendCaptionHistory(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            try
            {
                text = text.Trim();
                if (_captionHistory.Count > 0)
                {
                    string last = _captionHistory[_captionHistory.Count - 1];

                    // Windows Live Captions streams a single phrase by re-sending it
                    // as it grows and lightly revises wording/punctuation, and it
                    // also slides: the new text often begins where the previous line
                    // ended. Handle both so the panel shows continuous distinct
                    // speech instead of overlapping near-duplicates.
                    string merged = TryMergeOverlap(last, text);
                    if (merged != null)
                    {
                        _captionHistory[_captionHistory.Count - 1] = merged;
                        SyncHistoryTime(false);
                    }
                    else if (IsSameUtterance(last, text))
                    {
                        _captionHistory[_captionHistory.Count - 1] =
                            text.Length >= last.Length ? text : last;
                        SyncHistoryTime(false);
                    }
                    else
                    {
                        _captionHistory.Add(text);
                        SyncHistoryTime(true);
                    }
                }
                else
                {
                    _captionHistory.Add(text);
                    SyncHistoryTime(true);
                }

                const int MaxHistory = 500;
                if (_captionHistory.Count > MaxHistory)
                {
                    int drop = _captionHistory.Count - MaxHistory;
                    _captionHistory.RemoveRange(0, drop);
                    if (_captionHistoryTimes.Count >= drop)
                    {
                        _captionHistoryTimes.RemoveRange(0, drop);
                    }
                }

                if (_captionHistoryForm != null && !_captionHistoryForm.IsDisposed)
                {
                    _captionHistoryForm.SetHistory(_captionHistory);
                }
                // The Captions tab reads the same list. It no-ops unless that tab is the
                // one on screen, so this costs nothing during a long run on another tab.
                RefreshCaptionTranscript();
            }
            catch { }
        }

        /// <summary>
        /// If the end of <paramref name="prev"/> overlaps the start of
        /// <paramref name="next"/> (the sliding window Live Captions produces),
        /// returns the two stitched into one continuous line; otherwise null.
        /// Example: prev="...I seen it. I would have", next="I would have seen if
        /// y'all" → "...I seen it. I would have seen if y'all".
        /// </summary>
        private static string TryMergeOverlap(string prev, string next)
        {
            if (string.IsNullOrEmpty(prev) || string.IsNullOrEmpty(next)) return null;
            if (next.Length >= prev.Length &&
                next.StartsWith(prev, StringComparison.OrdinalIgnoreCase))
            {
                return next; // pure growth
            }

            // Find the largest k where prev's last k chars equal next's first k.
            int max = Math.Min(prev.Length, next.Length);
            for (int k = max; k >= 8; k--) // require a meaningful overlap (>=8 chars)
            {
                string tail = prev.Substring(prev.Length - k);
                string head = next.Substring(0, k);
                if (string.Equals(tail, head, StringComparison.OrdinalIgnoreCase))
                {
                    return prev + next.Substring(k);
                }
            }
            return null;
        }

        /// <summary>
        /// True when two caption strings are the same phrase being refined (so the
        /// history should replace, not append). Considers exact/contained matches
        /// and a long shared prefix, which is how Live Captions revises a line as
        /// it streams.
        /// </summary>
        private static bool IsSameUtterance(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            if (a == b) return true;
            if (a.StartsWith(b, StringComparison.OrdinalIgnoreCase) ||
                b.StartsWith(a, StringComparison.OrdinalIgnoreCase)) return true;

            // Length of the common leading run of characters.
            int n = Math.Min(a.Length, b.Length);
            int common = 0;
            for (int i = 0; i < n; i++)
            {
                if (char.ToLowerInvariant(a[i]) == char.ToLowerInvariant(b[i])) common++;
                else break;
            }
            // If they agree on most of the shorter string's length, it's the same
            // line mid-revision (e.g. a trailing word changed or punctuation moved).
            int shorter = Math.Min(a.Length, b.Length);
            return shorter > 0 && common >= (int)(shorter * 0.7);
        }

        /// <summary>
        /// Brings Windows Live Captions to the desired state (on/off) instead of
        /// blindly toggling. If captions are already in the wanted state, it does
        /// nothing; otherwise it sends Win+Ctrl+L. When turning on, it re-checks a
        /// few times and re-sends once if the window still hasn't appeared, since
        /// Windows can be slow to spawn it the first time.
        /// </summary>
        // How Windows' own Live Captions switch was found before Tempo touched it this
        // session. null = untouched. Restored when Tempo's captions are switched off.
        private bool? _windowsCaptionsWasOn;

        /// <summary>
        /// Records whether Windows Live Captions was already running, once per caption
        /// session, before Tempo changes it.
        ///
        /// Tempo drives that switch with Win+Ctrl+L — it turns Windows Live Captions OFF
        /// so its own engine has the audio to itself, and the fallback path turns it ON.
        /// Neither was ever undone: someone who runs Windows Live Captions for a second
        /// screen or another app found Tempo had silently switched their system feature
        /// off, and it stayed off. This is the same "put it back how we found it"
        /// contract LiveCaptionReader already honours for the window's POSITION.
        /// </summary>
        private void RememberWindowsCaptionState()
        {
            if (_windowsCaptionsWasOn.HasValue) { return; }   // already recorded this session
            try
            {
                if (_captionReader == null) { _captionReader = new Utils.LiveCaptionReader(); }
                // The VISIBLE-window check, not IsWindowPresent. That one falls back to
                // a UI Automation locate when it finds no window, and a warm
                // LiveCaptions.exe still answers it — so a bar that had just been closed
                // read as "on", Tempo recorded the wrong state to restore, and every
                // later toggle logged "already on; no toggle sent" and did nothing.
                _windowsCaptionsWasOn = _captionReader.IsWindowPresentFast();
                Utils.Logger.Info("[Captions] Windows Live Captions was " +
                    (_windowsCaptionsWasOn.Value ? "ON" : "off") +
                    " before Tempo started; it will be put back that way afterwards.");
            }
            catch { _windowsCaptionsWasOn = false; }
        }

        /// <summary>Puts Windows Live Captions back the way <see cref="RememberWindowsCaptionState"/> found it.</summary>
        private void RestoreWindowsCaptionState()
        {
            if (!_windowsCaptionsWasOn.HasValue) { return; }
            bool want = _windowsCaptionsWasOn.Value;
            _windowsCaptionsWasOn = null;
            try
            {
                EnsureWindowsCaptions(want);
            }
            catch (Exception ex)
            {
                Utils.Logger.Warn("Couldn't restore the Windows Live Captions state: " + ex.Message);
            }
        }

        private void EnsureWindowsCaptions(bool wantOn)
        {
            try
            {
                // Cancel any pending "did it appear?" verification from a previous
                // toggle. Without this, turning captions ON and then quickly OFF left
                // the old verify timer running, which would re-send Win+Ctrl+L and
                // flip Windows captions back on (or thrash the window) after you asked
                // for off. Always start from a clean slate.
                StopCaptionVerifyTimer();

                if (_captionReader == null)
                {
                    _captionReader = new Utils.LiveCaptionReader();
                }

                // On/off is decided by a window on screen — see RememberWindowsCaptionState.
                bool present = _captionReader.IsWindowPresentFast();
                if (present == wantOn)
                {
                    // Already in the desired state - don't toggle it back.
                    Utils.Logger.Info("[Captions] Windows Live Captions already " +
                        (wantOn ? "on" : "off") + "; no toggle sent.");
                    return;
                }

                if (!wantOn)
                {
                    // Turning it OFF closes the window rather than pressing Win+Ctrl+L.
                    // The shortcut stops responding after the first couple of presses
                    // (see LiveCaptionReader.CloseWindowsBar for the measurements), and
                    // when it does, Tempo's own engine ends up sharing the audio with a
                    // Windows bar it thinks it already switched off. Closing the window
                    // works every time. The shortcut stays as the fallback for the case
                    // where there is no window to close but Windows still thinks captions
                    // are on.
                    if (!_captionReader.CloseWindowsBar())
                    {
                        SendWindowsCaptionKey();
                    }
                    return;
                }

                SendWindowsCaptionKey();

                {
                    // Give Windows a beat, then verify; re-send if needed. The timer is a
                    // field so a later toggle can cancel it. We retry more than once over
                    // a longer window because on a fresh boot - or the very first time
                    // Live Captions is ever used - Windows can be slow to spawn the
                    // process (and may show a one-time setup prompt), so a single quick
                    // recheck was missing it and giving up too early.
                    _captionVerifyTimer = new System.Windows.Forms.Timer { Interval = 1000 };
                    int attempts = 0;
                    _captionVerifyTimer.Tick += (s, e) =>
                    {
                        attempts++;
                        // If captions are no longer wanted (user turned them off, or
                        // switched to Tempo's engine), abandon quietly.
                        if (!_captionsActive ||
                            (_settings != null && _settings.CaptionSource != CaptionSource.Windows
                                && _settings.CaptionSource != CaptionSource.Auto))
                        {
                            StopCaptionVerifyTimer();
                            return;
                        }
                        bool now = false;
                        try { now = _captionReader.IsWindowPresentFast(); } catch { }
                        if (now)
                        {
                            StopCaptionVerifyTimer();
                            return;
                        }
                        // Re-send the toggle on attempt 1 (give Windows time to react
                        // between presses). By attempt 2 the shortcut has plainly not
                        // taken, and pressing it a third time is not going to change
                        // that — LiveCaptions.exe is commonly already resident and simply
                        // ignoring it, which is the "it's in Task Manager but nothing
                        // appears" case. Launch the executable instead; measured, that
                        // brings the bar up when the shortcut no longer does.
                        if (attempts == 1)
                        {
                            SendWindowsCaptionKey();
                        }
                        else if (attempts == 2 || attempts == 4)
                        {
                            Utils.LiveCaptionReader.LaunchWindowsCaptions();
                        }
                        if (attempts >= 6)
                        {
                            StopCaptionVerifyTimer();
                            Utils.Logger.Warn("Windows Live Captions did not appear after toggling.");
                            if (_trayIcon != null && _settings != null && _settings.ShowTrayNotifications)
                            {
                                TempoNotify(4500, "Tempo",
                                    "Couldn't start Windows Live Captions automatically. Press "
                                    + "Win+Ctrl+L once, or turn it on in Settings > Accessibility > "
                                    + "Captions. The first time, Windows may ask to set it up.",
                                    ToolTipIcon.Warning);
                            }
                        }
                    };
                    _captionVerifyTimer.Start();
                }
            }
            catch (Exception ex)
            {
                Utils.Logger.Warn("EnsureWindowsCaptions failed: " + ex.Message);
            }
        }

        private void StopCaptionVerifyTimer()
        {
            try
            {
                if (_captionVerifyTimer != null)
                {
                    _captionVerifyTimer.Stop();
                    _captionVerifyTimer.Dispose();
                    _captionVerifyTimer = null;
                }
            }
            catch { }
        }

        private void SendWindowsCaptionKey()
        {
            try
            {
                // Press Win+Ctrl+L, then release in reverse order.
                keybd_event(VK_LWIN, 0, 0, System.UIntPtr.Zero);
                keybd_event(VK_CONTROL, 0, 0, System.UIntPtr.Zero);
                keybd_event(VK_L, 0, 0, System.UIntPtr.Zero);
                keybd_event(VK_L, 0, KEYEVENTF_KEYUP_FLAG, System.UIntPtr.Zero);
                keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP_FLAG, System.UIntPtr.Zero);
                keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP_FLAG, System.UIntPtr.Zero);
                Utils.Logger.Info("[Captions] sent Win+Ctrl+L to Windows Live Captions.");

                // Tempo just toggled that bar itself, and both watchers below read the
                // resulting appear/disappear as something the USER did. Measured: a
                // toggle-OFF produced an absent→present reading about 3.5 seconds later,
                // which the co-existence warning reported as "you switched Windows Live
                // Captions on" — Tempo reacting to its own keystroke. Hold both watchers
                // off across the transition.
                _externalWatchCooldownUntil = DateTime.UtcNow.AddSeconds(10);
            }
            catch (Exception ex)
            {
                Utils.Logger.Warn("Could not send Win+Ctrl+L: " + ex.Message);
            }
        }

        /// <summary>
        /// Registers all bound global hotkeys. The Toggle-Start/Stop hotkey is left
        /// unregistered while the active mode is hold-to-click, because that mode
        /// polls the key state directly instead.
        /// </summary>
        /// <summary>
        /// Actions that stay bound even while Tempo sleeps in the tray.
        ///
        /// Sleep exists so a forgotten Tempo cannot start clicking by itself hours
        /// later — and it achieved that by unregistering EVERY hotkey, including ones
        /// that cannot start anything. Emergency stop only ever STOPS input, so
        /// dropping it removed a safety net for no benefit; and with show/hide gone too,
        /// the only way to wake Tempo was to hunt for the tray icon, which is what made
        /// the sleeping state feel like the app had stopped working.
        ///
        /// Keeping just these two preserves the property that matters — nothing that can
        /// BEGIN clicking, playback or recording stays bound — while leaving the user a
        /// key to wake it and a working panic button.
        /// </summary>
        private static bool SurvivesTraySleep(HotkeyAction action)
        {
            return action == HotkeyAction.EmergencyStop
                || action == HotkeyAction.ShowHideWindow;
        }

        private void ApplyHotkeysFromSettings()
        {
            ApplyHotkeysFromSettings(false);
        }

        private void ApplyHotkeysFromSettings(bool sleepingInTray)
        {
            _hotkeys.UnregisterAll();

            bool holdMode = GetSelectedMode() == ClickMode.HoldToClick;
            _settings.EnsureBindings();

            foreach (var binding in _settings.Bindings)
            {
                if (binding == null || binding.Hotkey == null || !binding.Hotkey.IsValid)
                {
                    continue;
                }

                // Asleep: bind only what cannot start input.
                if (sleepingInTray && !SurvivesTraySleep(binding.Action))
                {
                    continue;
                }

                // Skip the toggle hotkey in hold mode so the poll timer owns it.
                if (holdMode && binding.Action == HotkeyAction.ToggleStartStop)
                {
                    continue;
                }

                if (binding.Hotkey.IsMouse)
                {
                    _hotkeys.RegisterMouse(binding.Action.ToString(), binding.Hotkey);
                }
                else
                {
                    // Pass the definition so the manager can fall back to a low-level
                    // keyboard hook if RegisterHotKey can't bind it on this keyboard.
                    _hotkeys.Register(binding.Action.ToString(), binding.Hotkey);
                }
            }

            // Enable hold polling only in hold mode.
            _holdPollTimer.Enabled = holdMode;

            // Tell the splash what actually got bound (the last startup stage).
            try
            {
                int bound = 0;
                foreach (var b in _settings.Bindings)
                {
                    if (b?.Hotkey != null && b.Hotkey.IsValid) { bound++; }
                }
                SplashForm.Report(4, bound + " " +
                    Localization.T(bound == 1 ? "hotkey" : "hotkeys"));
            }
            catch { }

            // Report anything Windows refused to reserve (Tempo is hook-driving it).
            RefreshKeybindRoutes();

            UpdateStartButtonHotkeyHint();
        }

        /// <summary>
        /// Shows the bound Start/Stop toggle hotkey on the Start button (e.g.
        /// "▶  Start · F6") so the shortcut is discoverable without opening the
        /// Keybinds tab. Falls back to a plain label when nothing is bound.
        /// </summary>
        private void UpdateStartButtonHotkeyHint()
        {
            if (_stopBtn != null)
            {
                HotkeyDefinition toggle = _settings?.HotkeyFor(HotkeyAction.ToggleStartStop);
                if (toggle != null && toggle.IsValid)
                {
                    string hk = toggle.ToDisplayString().Replace(" + ", "+");
                    _stopBtn.Text = "\u25A0 " + Utils.Localization.T("Stop") + " \u00b7 " + hk;
                }
                else
                {
                    _stopBtn.Text = "\u25A0 " + Utils.Localization.T("Stop");
                }
            }
            UpdateStartButtonAppearance();
        }

        /// <summary>
        /// Keeps the primary button in step with the engine: it reads "Start" when
        /// idle, "Pause" while clicking and "Resume" once paused, so pause/resume is
        /// reachable by mouse and not only via the hotkey. (Whether it's *enabled* is
        /// decided by RefreshBusyLock; this only sets the text and colour.)
        /// </summary>
        private void UpdateStartButtonAppearance()
        {
            if (_startBtn == null)
            {
                return;
            }

            bool paused = _engine != null && _engine.IsPaused;
            bool running = _engine != null && _engine.IsRunning && !paused;

            if (paused)
            {
                _startBtn.Text = "\u25B6 " + Utils.Localization.T("Resume");
                _startBtn.BackColor = _theme.Success;
            }
            else if (running)
            {
                _startBtn.Text = "\u2759\u2759 " + Utils.Localization.T("Pause");
                _startBtn.BackColor = _theme.Warning;
            }
            else
            {
                string baseText = "\u25B6 " + Utils.Localization.T("Start");
                HotkeyDefinition toggle = _settings?.HotkeyFor(HotkeyAction.ToggleStartStop);
                if (toggle != null && toggle.IsValid)
                {
                    _startBtn.Text = baseText + " \u00b7 " + toggle.ToDisplayString().Replace(" + ", "+");
                }
                else
                {
                    _startBtn.Text = baseText;
                }
                _startBtn.BackColor = _theme.Success;
            }
            _startBtn.ForeColor = Color.White;
        }

        /// <summary>
        /// Primary-button click: start when idle, pause while running, resume when
        /// paused. Pause/resume reuse the engine's existing, tested toggle.
        /// </summary>
        private void OnStartOrPauseClicked()
        {
            if (_engine == null)
            {
                return;
            }
            if (_engine.IsPaused)
            {
                _engine.TogglePause();   // resume
            }
            else if (_engine.IsRunning)
            {
                _engine.TogglePause();   // pause
            }
            else
            {
                BeginStartWithCountdown();
            }
        }

        private void PollHoldKey()
        {
            if (_traySleepActive)
            {
                return; // asleep in the tray — the hold trigger is paused too
            }
            HotkeyDefinition toggle = _settings.HotkeyFor(HotkeyAction.ToggleStartStop);
            if (toggle == null || !toggle.IsValid)
            {
                return;
            }

            // Resolve the virtual key to poll: a mouse button maps to its own VK
            // (GetAsyncKeyState works for mouse buttons too), otherwise the key.
            int vk = toggle.IsMouse ? MouseButtonVk(toggle.MouseButton) : (int)toggle.Key;
            if (vk == 0)
            {
                return;
            }

            bool down = (NativeMethods.GetAsyncKeyState(vk) & NativeMethods.KEY_PRESSED_MASK) != 0;

            // Require the configured modifiers too, so e.g. "Shift + X1 (hold)"
            // only engages while Shift is also held.
            if (down && !toggle.ModifiersMatch(v => (NativeMethods.GetAsyncKeyState(v) & NativeMethods.KEY_PRESSED_MASK) != 0))
            {
                down = false;
            }

            if (down && !_holdActive)
            {
                _holdActive = true;
                StartEngine();
            }
            else if (!down)
            {
                if (_holdActive)
                {
                    _holdActive = false;
                    _engine.Stop();
                }
                else if (_engine.IsRunning)
                {
                    // Safety net: in Hold mode the engine should never be running
                    // when the hold key is not physically held — even if something
                    // else (a bound "Start clicking" hotkey, say) started it.
                    _engine.Stop();
                }
            }
        }

        /// <summary>Maps a mouse-button trigger to its Win32 virtual-key code.</summary>
        private static int MouseButtonVk(HotkeyMouseButton button)
        {
            switch (button)
            {
                case HotkeyMouseButton.Left: return 0x01;   // VK_LBUTTON
                case HotkeyMouseButton.Right: return 0x02;  // VK_RBUTTON
                case HotkeyMouseButton.Middle: return 0x04; // VK_MBUTTON
                case HotkeyMouseButton.XButton1: return 0x05; // VK_XBUTTON1
                case HotkeyMouseButton.XButton2: return 0x06; // VK_XBUTTON2
                default: return 0;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Engine control helpers
        // ─────────────────────────────────────────────────────────────────────

        private void ToggleEngine()
        {
            if (_engine.IsRunning)
            {
                _engine.Stop();
            }
            else
            {
                BeginStartWithCountdown();
            }
        }

        /// <summary>
        /// Starts clicking, first showing a countdown overlay if the user has
        /// configured a start delay. Used by the explicit start paths (Start
        /// button, Start/Toggle hotkeys) but never by Hold mode, which must engage
        /// instantly.
        /// </summary>
        private void BeginStartWithCountdown()
        {
            if (_engine.IsRunning)
            {
                return;
            }

            // Validate up-front so an invalid profile fails immediately instead of
            // after sitting through the countdown.
            string error = BuildProfileFromUi().Validate();
            if (error != null)
            {
                ShowWarning(error);
                return;
            }

            int secs = _settings != null ? _settings.ClickerStartDelaySeconds : 0;
            if (secs > 0)
            {
                string subtitle = DescribePendingRun();
                bool beep = _settings != null && _settings.StartDelayBeep;
                using (var overlay = new CountdownOverlayForm(_theme, secs, subtitle, beep))
                {
                    // Track it so the stop hotkey can abort the count-in (see
                    // DispatchAction) — a fixed wait you can't cancel is worse than none.
                    _activeCountdown = overlay;
                    DialogResult r;
                    try { r = overlay.ShowDialog(); }
                    finally { _activeCountdown = null; }
                    if (r != DialogResult.OK)
                    {
                        return; // cancelled with Esc or the stop hotkey
                    }
                }
            }

            StartEngine();
        }

        /// <summary>One short line naming what the countdown is about to start.</summary>
        private string DescribePendingRun()
        {
            try
            {
                var p = BuildProfileFromUi();
                if (p.Target == ClickTarget.Key)
                {
                    return "Key press · " + (p.KeyVirtualKey != 0 ? "ready" : "no key set");
                }
                string btn = p.Button.ToString();
                string style = p.Style == ClickStyle.Single ? "" : " " + p.Style;
                double cps = p.GetBaseIntervalMilliseconds() > 0
                    ? 1000.0 / p.GetBaseIntervalMilliseconds() : 0;
                return "Starting: " + btn + style + " · " + cps.ToString("0.#") + " CPS";
            }
            catch
            {
                return "Starting…";
            }
        }

        /// <summary>Last off-screen warning shown, so an unchanged setup only says it once.</summary>
        private string _lastOffScreenWarning;

        /// <summary>
        /// Warns when a profile's saved coordinates no longer land on any monitor.
        ///
        /// WHY THIS IS WORTH A WARNING. InputSimulator clamps every coordinate into the
        /// virtual desktop before it clicks, so a point saved on a second screen that
        /// has since been unplugged does not fail — it quietly clicks the edge of the
        /// remaining monitor instead. That is the bad kind of wrong: the run looks
        /// healthy, the click counter goes up, and it has been hitting the wrong place
        /// for as long as it was left going.
        ///
        /// ScreenGeometry.IsOnScreen was written for precisely this check and had never
        /// been called from anywhere.
        ///
        /// It warns rather than refuses, because the run may well have been started by
        /// a hotkey with the window hidden, and a modal dialog there would strand it.
        /// The toast is non-blocking and the WARN line lands in Live Debug.
        /// </summary>
        private void WarnAboutOffScreenTargets(ClickProfile profile)
        {
            if (profile == null) { return; }

            var stray = new System.Collections.Generic.List<string>();
            try
            {
                if (profile.PositionMode == PositionMode.FixedPosition)
                {
                    if (!ScreenGeometry.IsOnScreen(profile.FixedX, profile.FixedY))
                    {
                        stray.Add(profile.FixedX + ", " + profile.FixedY);
                    }
                }
                else if (profile.PositionMode == PositionMode.MultiPoint && profile.Points != null)
                {
                    foreach (var pt in profile.Points)
                    {
                        // A disabled point is never visited, so it is not a problem.
                        if (pt == null || !pt.Enabled) { continue; }
                        if (!ScreenGeometry.IsOnScreen(pt.X, pt.Y))
                        {
                            string label = string.IsNullOrWhiteSpace(pt.Label) ? "?" : pt.Label;
                            stray.Add(label + " (" + pt.X + ", " + pt.Y + ")");
                        }
                    }
                }
            }
            catch (Exception ex) { Logger.Swallow("OffScreenCheck", ex); return; }

            if (stray.Count == 0)
            {
                _lastOffScreenWarning = null;
                return;
            }

            string list = string.Join(", ", stray.GetRange(0, Math.Min(3, stray.Count)));
            if (stray.Count > 3) { list += ", …"; }

            // Starting the same unchanged setup repeatedly should not nag on every run.
            string signature = stray.Count + "|" + list;
            if (string.Equals(signature, _lastOffScreenWarning, StringComparison.Ordinal)) { return; }
            _lastOffScreenWarning = signature;

            Logger.Warn("[clicker] " + stray.Count + " click target(s) lie outside every monitor and " +
                        "will be clamped to the screen edge: " + list);

            try
            {
                _notifications?.Notify("Tempo",
                    Localization.T("Some click points are off-screen"),
                    Localization.F(
                        "{0} — a monitor may have been unplugged since this profile was saved. " +
                        "Clicks there land on the edge of the screen instead.", list),
                    ToastKind.Warning);
            }
            catch { }
        }

        private void StartEngine()
        {
            bool wasRunning = _engine.IsRunning;

            // Apply anti-freeze settings to the engine before each start.
            ApplyAntiFreezeToEngine();

            ClickProfile profile = BuildProfileFromUi();
            WarnAboutOffScreenTargets(profile);
            _lastRunWasFinite = profile.RepeatMode != RepeatMode.UntilStopped;
            _lastRunDurationSeconds = profile.RepeatMode == RepeatMode.ForDuration
                ? profile.RepeatDurationSeconds : 0;
            _lastRunTargetClicks = profile.RepeatMode == RepeatMode.FixedCount
                ? profile.RepeatCount : 0;
            string error = _engine.Start(profile);
            if (error != null)
            {
                ShowWarning(error);
                return;
            }

            // Only bump the lifetime session counter on a real idle → running
            // transition, so the hold-mode poll timer cannot inflate it.
            if (!wasRunning && _engine.IsRunning)
            {
                _settings.LifetimeSessions++;
                _runStartClicks = _statistics.TotalClicks;
                _runCompletedHandled = false;
            }

            if (_settings.ShowTrayNotifications && !Visible)
            {
                TempoNotify(1500, "Tempo", "Clicking started.", ToolTipIcon.Info);
            }

            // Optionally tuck the window away to the tray once clicking begins. Animated,
            // like every other route to the tray — a bare Hide() here made the window
            // vanish the instant you pressed Start, which reads as a crash at exactly the
            // moment the user is looking for reassurance that the run began. It also
            // records the pre-tray state, so a maximised Tempo comes back maximised.
            if (!wasRunning && _engine.IsRunning && _settings.HideWhenClicking && Visible)
            {
                HideToTrayAnimated();
            }
        }

        /// <summary>Copies the user's anti-freeze settings onto the engine.</summary>
        private void ApplyAntiFreezeToEngine()
        {
            if (_engine == null || _settings == null)
            {
                return;
            }

            _engine.AntiFreezeEnabled = _settings.AntiFreezeEnabled;
            _engine.MaxClicksPerSecond = _settings.MaxClicksPerSecond < 1 ? 1 : _settings.MaxClicksPerSecond;
            _engine.CpuThresholdPercent = _settings.AntiFreezeCpuThreshold;
        }

        private void EmergencyStop()
        {
            _holdActive = false;
            _engine.Stop();
            _player.Stop();

            // The second-cursor spam runs on its own loop, independent of the click
            // engine — the panic key must stop it too, or "I can't force-stop it" is
            // true. (The cursor itself stays where it's parked; only the clicking
            // halts.) Also release any bound 2nd physical mouse so the panic key truly
            // hands the machine back — no more cursor snap-back.
            try { _secondCursor?.StopSpam(); } catch { }
            try { _secondCursor?.PanicReleaseSecondMouse(); } catch { }

            // Emergency stop means "give me my machine back" — and camera-relative
            // movement is the single most invasive thing Tempo does (it swallows
            // W/A/S/D and holds keys down). Disarming it releases every key it was
            // holding, so the panic key genuinely restores normal input.
            StopMovement();

            // Go through StopRecording so the record/stop buttons, status label,
            // and macro saving all behave the same way as a normal stop.
            if (_recorder.IsRecording)
            {
                StopRecording();
            }

            _statusState.Text = "\u25CF  " + Localization.T("Stopped (emergency)");
            _statusState.ForeColor = _theme.Danger;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Live display refresh
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Fills the middle of the status bar with something useful: the start/stop
        /// hotkey and what the clicker will do, or the macro that's playing.
        /// </summary>
        private void UpdateStatusHint()
        {
            if (_statusHint == null)
            {
                return;
            }

            string text;
            try
            {
                if (_recorder != null && _recorder.IsRecording)
                {
                    string rhk = "";
                    HotkeyDefinition rec = _settings != null ? _settings.HotkeyFor(HotkeyAction.ToggleRecordMacro) : null;
                    if (rec != null && rec.IsValid)
                    {
                        rhk = "  \u00b7  " + rec.ToDisplayString().Replace(" + ", "+") + " to stop";
                    }
                    text = "\u25CF Recording \u2014 " + _recordedStepCount + " steps" + rhk;
                }
                else if (_player != null && _player.IsPlaying)
                {
                    Macro m = SelectedMacro();
                    text = "Playing macro" + (m != null && !string.IsNullOrEmpty(m.Name) ? ": " + m.Name : "");
                }
                else
                {
                    string hk = "";
                    HotkeyDefinition toggle = _settings != null ? _settings.HotkeyFor(HotkeyAction.ToggleStartStop) : null;
                    if (toggle != null && toggle.IsValid)
                    {
                        hk = toggle.ToDisplayString().Replace(" + ", "+");
                    }

                    if (_engine != null && _engine.IsPaused)
                    {
                        text = hk.Length > 0 ? ("Paused \u2014 " + hk + " to resume") : "Paused";
                    }
                    else if (_engine != null && _engine.IsRunning)
                    {
                        ClickMode runMode = GetSelectedMode();
                        // Use the engine's real effective rate, not the slider value:
                        // the slider is capped at its maximum, so a fast interval typed
                        // straight into the ms box would otherwise under-report here.
                        string rate = "";
                        if (runMode == ClickMode.Interval && _engine != null)
                        {
                            double eff = _engine.EffectiveClicksPerSecond;
                            if (eff >= 1)
                            {
                                rate = " \u00b7 " + ((long)Math.Round(eff)).ToString("N0") + " CPS";
                            }
                            else if (eff > 0)
                            {
                                rate = " \u00b7 " + eff.ToString("0.0") + " CPS";
                            }
                        }
                        string timeLeft = "";
                        if (_lastRunDurationSeconds > 0)
                        {
                            // Engine active-time, so the countdown freezes while paused.
                            int leftS = _lastRunDurationSeconds - (int)(_engine.RunActiveMs / 1000);
                            if (leftS < 0) leftS = 0;
                            timeLeft = " \u00b7 " + leftS + " s left";
                        }
                        else if (_lastRunTargetClicks > 0)
                        {
                            timeLeft = " \u00b7 " + _engine.RunClicks.ToString("N0") +
                                       " / " + _lastRunTargetClicks.ToString("N0");
                        }
                        string verb = runMode == ClickMode.HoldToClick ? "Clicking (hold)" : "Clicking";
                        text = verb + rate + timeLeft + (hk.Length > 0 ? (" \u2014 " + hk + " to stop") : "");
                    }
                    else
                    {
                        string summary = BuildClickerSummary();
                        // In hold-to-click mode the toggle key doesn't toggle — it
                        // must be held — so say that instead of "to start".
                        string start = hk.Length > 0
                            ? (GetSelectedMode() == ClickMode.HoldToClick
                                ? "hold " + hk + " to click"
                                : hk + " to start")
                            : "Ready";
                        text = summary.Length > 0 ? (summary + "    \u00b7    " + start) : start;
                    }
                }
            }
            catch
            {
                text = "";
            }

            if (_statusHint.Text != text)
            {
                _statusHint.Text = text;
            }
        }

        /// <summary>
        /// Updates the target-run progress readout in the status bar: "1,250 / 5,000
        /// clicks" for a fixed-count run, or "remaining 0:42" for a duration run.
        /// Hidden when the run is open-ended (until stopped) or the engine is idle.
        /// </summary>
        /// <summary>
        /// Assigns a status-strip label's text only when it actually changed. WinForms
        /// does not check this for you: the setter relayouts and repaints regardless, so
        /// on a periodic tick an unchanged value is pure cost.
        /// </summary>
        private static void SetStatusText(ToolStripStatusLabel label, string text)
        {
            if (label == null) { return; }
            if (!string.Equals(label.Text, text, StringComparison.Ordinal))
            {
                label.Text = text;
            }
        }

        /// <summary>
        /// What Tempo is costing right now: its CPU share, its working set, and how long
        /// it has been running.
        ///
        /// Deliberately on a ONE-SECOND cadence of its own, not the caller's 5 Hz tick.
        /// The comment above the click counters records what that tick costs: assigning
        /// a ToolStripStatusLabel relayouts and repaints the strip, and with a wallpaper
        /// the window carries WS_EX_COMPOSITED, where any repaint redraws the WHOLE
        /// window. CPU% and free-running RAM change on almost every read, so at 5 Hz
        /// these three would repaint five times a second forever — reintroducing exactly
        /// the paint storm that was measured and fixed there. Once a second is as fast as
        /// a human can read a number anyway.
        ///
        /// Skipped entirely while the window is hidden or minimised: nobody can read a
        /// status bar that is not on screen, and sampling it keeps the process awake for
        /// nothing. That also means the CPU figure covers only the interval since the
        /// last VISIBLE sample, which is what the reader is looking at.
        /// </summary>
        private void UpdateResourceReadout(bool windowShowing)
        {
            if (_statusCpu == null || !windowShowing) { return; }
            try
            {
                DateTime now = DateTime.UtcNow;
                if (now < _resourceNextSampleUtc) { return; }
                bool first = _resourceNextSampleUtc == DateTime.MinValue;
                _resourceNextSampleUtc = now.AddSeconds(1);

                if (_uiCpuMonitor == null) { _uiCpuMonitor = new Utils.CpuMonitor(); }
                double cpu = _uiCpuMonitor.Sample();
                // The first sample spans from construction to now — an arbitrary window
                // that reads as a meaningless spike. Prime it and show it next second.
                if (first) { return; }

                // One decimal, not a whole number. CpuMonitor reports a share of the
                // WHOLE machine (all cores), same basis as Task Manager — and Tempo idle
                // is well under 1% of that, so "{0:0}" displayed a flat 0% almost all the
                // time and the readout told you nothing. 0.8% is both honest and useful,
                // and it still reads sensibly at the other end (23.4% mid click-run).
                SetStatusText(_statusCpu, Utils.Localization.F("CPU {0:0.0}%", cpu));
                // Working set: the figure Task Manager calls "Memory", so it matches
                // what a user checking up on Tempo would compare it against.
                SetStatusText(_statusRam,
                    Utils.Localization.F("RAM {0:N0} MB", Environment.WorkingSet / (1024 * 1024)));

                TimeSpan up = now - _appStartedUtc;
                SetStatusText(_statusUptime, Utils.Localization.T("Up") + " " +
                    ((int)up.TotalHours) + up.ToString("\\:mm\\:ss"));
            }
            catch (Exception ex) { Utils.Logger.Swallow("UpdateResourceReadout", ex); }
        }

        private void UpdateStatusProgress()
        {
            if (_statusProgress == null) return;

            try
            {
                bool running = _engine != null && _engine.IsRunning;
                if (!running)
                {
                    if (_statusProgress.Visible) _statusProgress.Visible = false;
                    return;
                }

                string text = null;
                if (_repeatCountRadio != null && _repeatCountRadio.Checked && _repeatCountNum != null)
                {
                    long target = (long)_repeatCountNum.Value;
                    long done = _engine.RunClicks;
                    if (target > 0)
                    {
                        if (done > target) done = target;
                        int pct = (int)Math.Round(100.0 * done / target);
                        text = $"\u25B8 {done:N0} / {target:N0}  ({pct}%)";
                        // Live estimate of time left, from how fast it's actually clicking.
                        long remaining = target - done;
                        double cps = _statistics.GetCurrentCps();
                        if (remaining > 0 && cps > 0.1)
                        {
                            text += "  \u00B7  ~" + FormatDuration(TimeSpan.FromSeconds(remaining / cps)) + " left";
                        }
                    }
                }
                else if (_repeatDurationRadio != null && _repeatDurationRadio.Checked && _repeatDurationNum != null)
                {
                    long targetMs = (long)_repeatDurationNum.Value * 1000L;
                    long elapsedMs = _engine.RunActiveMs;
                    long remainMs = Math.Max(0, targetMs - elapsedMs);
                    text = "\u25B8 remaining " + FormatDuration(TimeSpan.FromMilliseconds(remainMs));
                    if (targetMs > 0)
                    {
                        int pct = (int)Math.Round(100.0 * Math.Min(elapsedMs, targetMs) / targetMs);
                        text += $"  ({pct}%)";
                    }
                }

                if (text == null)
                {
                    if (_statusProgress.Visible) _statusProgress.Visible = false;
                }
                else
                {
                    _statusProgress.ForeColor = _theme.Accent;
                    if (!_statusProgress.Visible) _statusProgress.Visible = true;
                    if (_statusProgress.Text != text) _statusProgress.Text = text;
                }
            }
            catch
            {
                if (_statusProgress.Visible) _statusProgress.Visible = false;
            }
        }

        /// <summary>A short "Interval · 10 CPS · Left" summary of the current clicker setup.</summary>
        private string BuildClickerSummary()
        {
            try
            {
                ClickMode mode = GetSelectedMode();
                string modeName = mode == ClickMode.HoldToClick ? "Hold" : mode == ClickMode.Burst ? "Burst" : "Interval";
                string s = modeName;
                if (_speedTrack != null)
                {
                    s += " \u00b7 " + _speedTrack.Value + " CPS";
                }
                if (_buttonCombo != null && _buttonCombo.SelectedItem != null)
                {
                    s += " \u00b7 " + _buttonCombo.SelectedItem;
                }
                if (_repeatCountRadio != null && _repeatCountRadio.Checked && _repeatCountNum != null)
                {
                    s += " \u00b7 \u00d7" + ((int)_repeatCountNum.Value).ToString("N0");
                }
                else if (_repeatDurationRadio != null && _repeatDurationRadio.Checked && _repeatDurationNum != null)
                {
                    s += " \u00b7 " + (int)_repeatDurationNum.Value + " s";
                }
                if (_posFixedRadio != null && _posFixedRadio.Checked && _fixedXNum != null && _fixedYNum != null)
                {
                    s += " \u00b7 @(" + (int)_fixedXNum.Value + "," + (int)_fixedYNum.Value + ")";
                }
                else if (_posMultiRadio != null && _posMultiRadio.Checked)
                {
                    int pts = _pointsList != null ? _pointsList.Items.Count : 0;
                    s += " \u00b7 multi-point" + (pts > 0 ? " (" + pts + ")" : "");
                }
                return s;
            }
            catch
            {
                return "";
            }
        }

        private void UpdateLiveDisplays()
        {
            UpdateTraySleepState();

            // Keep the Captions tab honest from the tick rather than hooking every route
            // that can change caption state (button, hotkey, tray, overlay closing itself).
            // Gated on the tab being visible, so it costs nothing the rest of the time.
            if (IsCaptionsTabVisible()) { RefreshCaptionsTab(); }
            if (_traySleepActive)
            {
                return; // hidden and idle: no UI to update, nothing to burn CPU on
            }

            // The scroll-preserving capture/restore and the expensive dashboard rebuild
            // only make sense when the window is actually showing AND we're on the
            // Statistics tab. Reasons:
            //  - Only the Statistics dashboard rebuilds its child controls on a live
            //    update, and rebuilding controls is what snaps an AutoScroll page to the
            //    top - so only there is a scroll capture/restore needed. Doing it on every
            //    tab re-asserted the scroll five times a second and fought the user while
            //    they scrolled Settings ("I scroll down but it jumps / refreshes by itself").
            //  - Touching the scroll position against a minimised/hidden (collapsed) page
            //    is one of the ways a page came back mis-laid-out on restore, so we require
            //    a real, visible, non-minimised window.
            // The status bar, the clicking overlay and milestone checks below still run
            // regardless, because the overlay can be visible while the main window is in
            // the tray during a click run.
            bool windowShowing = Visible && WindowState != FormWindowState.Minimized;
            bool onStatsTab = windowShowing && _statsPage != null && _tabs != null && _tabs.SelectedTab == _statsPage;
            Point keepScroll = onStatsTab ? CaptureActiveScroll() : Point.Empty;

            // The status bar is visible on every tab, so always keep it current — but
            // only ASSIGN when the text actually differs.
            //
            // These four ran unconditionally five times a second. Setting .Text on a
            // ToolStripStatusLabel relayouts the strip and repaints it even when the
            // string is identical, and with a wallpaper the window carries
            // WS_EX_COMPOSITED, where any repaint redraws the WHOLE window — the ~90 ms
            // path this codebase already measured. So a completely idle Tempo showing
            // "Clicks: 0 · CPS: 0.0 · Peak 0.0 · Time: 00:00" was forcing full-window
            // composites 5x a second, for four values that had not changed. Measured at
            // idle: ~103% of one CPU core with a wallpaper against ~13% without, and
            // ~15% when minimised, which is what proved it was paint-driven. A saturated
            // UI thread is why dragging and the notification animation stuttered.
            SetStatusText(_statusClicks,
                Utils.Localization.T("Clicks:") + " " + _statistics.SessionClicks.ToString("N0"));
            SetStatusText(_statusCps,
                Utils.Localization.T("CPS:") + $" {_statistics.GetCurrentCps():0.0}");
            SetStatusText(_statusPeak, $"Peak {_statistics.PeakClicksPerSecond:0.0}");
            SetStatusText(_statusElapsed,
                Utils.Localization.T("Time:") + " " + FormatDuration(_statistics.GetElapsed()));

            UpdateResourceReadout(windowShowing);

            // Anti-freeze "throttling" flag, shown only while protection is actively
            // slowing the configured rate.
            if (_statusThrottle != null)
            {
                bool throttling = _engine != null && _engine.IsRunning && _engine.IsThrottling;
                if (_statusThrottle.Visible != throttling)
                {
                    _statusThrottle.Visible = throttling;
                }
                if (throttling)
                {
                    _statusThrottle.ForeColor = _theme.Warning;
                }
            }

            // Target-run progress: clicks-toward-count, or time-remaining for a
            // duration run. Hidden when the run is open-ended or idle.
            UpdateStatusProgress();

            // Prominent live rate next to the big status word, only while clicking.
            // Same rule: assigning the same string still repaints.
            if (_liveCpsLabel != null)
            {
                string live = (_engine != null && _engine.IsRunning && !_engine.IsPaused)
                    ? $"{_statistics.GetCurrentCps():0.0} CPS"
                    : string.Empty;
                if (!string.Equals(_liveCpsLabel.Text, live, StringComparison.Ordinal))
                {
                    _liveCpsLabel.Text = live;
                }
            }

            // Keep the on-screen running overlay (if shown) in step with the stats.
            if (_clickingIndicator != null && !_clickingIndicator.IsDisposed)
            {
                _clickingIndicator.SetStats(_statistics.SessionClicks, _statistics.GetCurrentCps(),
                    (long)_statistics.GetElapsed().TotalSeconds);
            }

            // The full statistics dashboard (cards, charts, insights) is expensive to
            // recompute, so only do it while the Statistics tab is actually showing.
            // It's also refreshed on tab-switch and whenever a session ends.
            if (onStatsTab)
            {
                UpdateStatisticsTab();
            }

            UpdateAntiFreezeStatus();
            UpdateMultiPointLive();
            CheckMilestoneCrossing();
            UpdateStatusHint();
            UpdateProfileDirty();

            if (onStatsTab)
            {
                RestoreActiveScroll(keepScroll);
            }
        }

        /// <summary>
        /// Refreshes the live anti-freeze "detection" readout in the Clicker tab:
        /// measured CPU, the actual rate being issued, and whether protection is
        /// currently throttling the clicker.
        /// </summary>
        private void UpdateAntiFreezeStatus()
        {
            if (_antiFreezeStatusLabel == null)
            {
                return;
            }

            // The "Enabled (prevents system freeze)" checkbox reads green while the
            // protection is on — matching the design mock — and normal text when off.
            // Re-asserted here (the UI tick) so a theme change can't wash it out.
            if (_antiFreezeCheck != null && _theme != null)
            {
                Color want = _antiFreezeCheck.Checked ? _theme.Success : _theme.Text;
                if (_antiFreezeCheck.ForeColor != want)
                {
                    _antiFreezeCheck.ForeColor = want;
                    _antiFreezeCheck.Invalidate();
                }
            }

            if (!_settings.AntiFreezeEnabled)
            {
                _antiFreezeStatusLabel.Text = Localization.T("Detection: off — no rate limit");
                _antiFreezeStatusLabel.ForeColor = _theme.TextMuted;
                return;
            }

            if (!_engine.IsRunning)
            {
                _antiFreezeStatusLabel.Text = Localization.F("Detection: idle  •  cap {0} CPS",
                    _settings.MaxClicksPerSecond);
                _antiFreezeStatusLabel.ForeColor = _theme.TextMuted;
                return;
            }

            double cpu = _engine.MeasuredCpuPercent;
            double cps = _engine.EffectiveClicksPerSecond;

            if (_engine.IsThrottling)
            {
                _antiFreezeStatusLabel.Text = Localization.F(
                    "⚠ Throttling — CPU {0:0}%  •  holding {1:0.0} CPS", cpu, cps);
                _antiFreezeStatusLabel.ForeColor = _theme.Warning;
            }
            else
            {
                _antiFreezeStatusLabel.Text = Localization.F(
                    "✓ Protected — CPU {0:0}%  •  {1:0.0} CPS", cpu, cps);
                _antiFreezeStatusLabel.ForeColor = _theme.Success;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Theming
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Builds the active theme, applying the custom accent if enabled.</summary>
        private Theme BuildActiveTheme()
        {
            // "Match Windows" follows the OS Light/Dark — but ONLY when the user's own
            // pick is the neutral Light or Dark. A deliberately colourful theme
            // (Synthwave, Ocean, Dracula, …) has no light/dark twin, so Match Windows
            // was silently erasing it — the app showed plain Dark instead of the theme
            // the user chose, and every themed surface (caption bar, notification cards)
            // mismatched their pick. Keep the colourful choice; only neutral picks track
            // the OS.
            // Only the neutral Light/Dark picks track the OS; a colourful theme keeps
            // its own identity (see above). We remember whether THIS build is a neutral
            // "look like Windows" one, because only then do we also adopt the Windows
            // accent colour — forcing a foreign accent onto Synthwave/Dracula/etc. would
            // clash with palettes that were designed around their own accent.
            bool neutralFollow = _settings.FollowSystemTheme &&
                (_settings.Theme == ThemeKind.Light || _settings.Theme == ThemeKind.Dark);

            ThemeKind kind = neutralFollow
                ? (Utils.SystemTheme.IsWindowsLight() ? ThemeKind.Light : ThemeKind.Dark)
                : _settings.Theme;

            Theme t = Theme.ForKind(kind);

            // Accent precedence: an explicit custom accent always wins (deliberate user
            // choice). Otherwise, a neutral "Match Windows" theme adopts the user's real
            // Windows accent so Tempo genuinely matches the OS, not just its light/dark.
            if (_settings.CustomAccentEnabled)
            {
                t = t.WithAccent(System.Drawing.Color.FromArgb(_settings.CustomAccentArgb));
            }
            else if (neutralFollow && Utils.SystemTheme.TryGetWindowsAccent(out var winAccent))
            {
                t = t.WithAccent(winAccent);
            }
            return t;
        }

        private void ApplyThemeToEverything()
        {
            _theme = BuildActiveTheme();
            // Hand the active theme to the message dialog. It is reached from static call
            // sites all over the app that have no way to pass one in, so it keeps its own
            // reference and picks up every theme change from here.
            TempoMessageForm.CurrentTheme = _theme;
            BackColor = _theme.Background;
            ForeColor = _theme.Text;
            ThemeManager.Apply(this, _theme);

            // Re-apply accent colours to the primary action buttons.
            if (_startBtn != null)
            {
                _startBtn.BackColor = _theme.Success;
                _startBtn.ForeColor = Color.White;
                _startBtn.FlatAppearance.BorderSize = 0;
            }

            if (_stopBtn != null)
            {
                _stopBtn.BackColor = _theme.Danger;
                _stopBtn.ForeColor = Color.White;
                _stopBtn.FlatAppearance.BorderSize = 0;
            }

            // Filled accent "primary" buttons also need their colour re-applied, since
            // ThemeManager just reset every Button to the neutral surface colour.
            StyleAccentButton(_newProfileBtn);
            StylePurpleButton(_humanizeBtn);
            UpdateHumanizeButton();   // restore the on/off look StylePurpleButton just overwrote
            StyleAccentButton(_exactCpsSetBtn);
            StyleDangerButton(_deleteProfileBtn);
            UpdateAudioDeviceStatusLabel();   // restore its muted/warning colour too
            StyleStatusBar();                 // status-bar renderer + stat icons follow the theme
            ApplyTrayMenuTheme();             // the tray menu follows the theme too
            if (_speedLabel != null)
            {
                _speedLabel.AccentColor = _theme.Accent;
                _speedLabel.MutedColor = _theme.TextMuted;
                _speedLabel.ForeColor = _theme.Text;
                _speedLabel.Invalidate();
            }
            if (_speedTrack != null)
            {
                HighlightActivePreset(_speedTrack.Value);
            }

            // Custom controls that don't go through ThemeManager.
            if (_tabs != null)
            {
                _tabs.ApplyTheme(_theme);
            }

            if (_sidebar != null)
            {
                _sidebar.BackColor = _theme.Background;
                _sidebar.Invalidate();
            }
            RefreshSidebarSelection();

            if (_header != null)
            {
                _header.ApplyTheme(_theme);
            }
            if (_footerGif != null)
            {
                _footerGif.ApplyTheme(_theme);
            }
            if (_clickingIndicator != null && !_clickingIndicator.IsDisposed)
            {
                _clickingIndicator.ApplyTheme(_theme);
            }
            if (_macroIndicator != null && !_macroIndicator.IsDisposed)
            {
                _macroIndicator.ApplyTheme(_theme);
            }
            if (_captionOverlay != null && !_captionOverlay.IsDisposed)
            {
                _captionOverlay.ApplyTheme(_theme);
            }
            if (_captionHistoryForm != null && !_captionHistoryForm.IsDisposed)
            {
                _captionHistoryForm.ApplyTheme(_theme);
            }
            // Live debug was the one long-lived window this list forgot, so changing
            // theme with it open left it in the previous theme's colours.
            if (_debugForm != null && !_debugForm.IsDisposed)
            {
                _debugForm.ApplyTheme(_theme);
            }
            // The header repaints the profile caption in the new theme's colours.
            _header?.Invalidate();

            // Pill colour is driven by current engine state; refresh it.
            RefreshStatePill();

            // Theme the statistics dashboard cards + graph.
            ApplyThemeToStatCards();

            // The profile library's cards are owner-drawn too, so they take their
            // colours the same way.
            ApplyThemeToProfileCards();

            // Keep the Settings live preview in sync.
            RefreshThemePreview();

            // Dark/light scroll bars + title bar to match the theme.
            ApplyNativeChrome();

            Invalidate(true);
        }

        /// <summary>
        /// Themes the OS-drawn chrome (scroll bars, title bar) to match the current theme:
        /// dark themes get Windows' thin dark scroll bars and a dark title bar instead of
        /// the chunky light-grey native ones. Best-effort and re-applied on theme change.
        /// </summary>
        private void ApplyNativeChrome()
        {
            if (_theme == null)
            {
                return;
            }
            bool dark = _theme.Background.GetBrightness() < 0.5f;
            NativeChrome.SetAppDarkMode(dark);
            if (IsHandleCreated)
            {
                NativeChrome.SetTitleBarDark(Handle, dark);
                // …then tint it to the actual theme, so switching to a coloured theme
                // repaints the title bar too instead of leaving a black bar on top.
                TintTitleBarToTheme();
            }
            if (_tabs != null)
            {
                foreach (TabPage page in _tabs.TabPages)
                {
                    NativeChrome.ApplyScrollbarTheme(page, dark);
                }
            }
            // Lists were the last native-light holdouts: theme every ListView (incl. its
            // column-header strip) and ListBox scrollbars to match. Controls on tabs that
            // haven't been visited yet don't have native handles — they get themed the
            // moment their handle is created instead.
            ApplyNativeListThemes(this);
        }

        /// <summary>
        /// Recursively applies the dark/light native theme to every ListView (with its
        /// header) and ListBox under <paramref name="root"/>. Handles created later (a
        /// control on an unvisited tab, or a handle recreation) are themed on creation.
        /// Subscriptions are detach-then-attach, so repeat calls never stack handlers.
        /// </summary>
        private void ApplyNativeListThemes(Control root)
        {
            if (root == null)
            {
                return;
            }
            foreach (Control c in root.Controls)
            {
                // TextBoxBase (TextBox AND RichTextBox) belongs here too. It was missing,
                // which is why the Live debug log's scroll bar stayed LIGHT while the
                // page scroll bars beside it were dark — the same app showing two
                // different scroll bar colours.
                if (c is ListView || c is ListBox || c is ComboBox || c is TextBoxBase)
                {
                    c.HandleCreated -= OnNativeListHandleCreated;
                    c.HandleCreated += OnNativeListHandleCreated;
                    if (c.IsHandleCreated)
                    {
                        ThemeNativeList(c);
                    }
                }
                ApplyNativeListThemes(c);
            }
        }

        private void OnNativeListHandleCreated(object sender, EventArgs e)
        {
            ThemeNativeList(sender as Control);
        }

        private void ThemeNativeList(Control c)
        {
            if (c == null || _theme == null)
            {
                return;
            }
            bool dark = _theme.Background.GetBrightness() < 0.5f;
            if (c is ListView lv)
            {
                NativeChrome.ApplyListViewTheme(lv, dark);
            }
            else if (c is ComboBox)
            {
                NativeChrome.ApplyComboListTheme(c, dark);
            }
            else
            {
                NativeChrome.ApplyScrollbarTheme(c, dark);
            }
        }

        private void RefreshStatePill()
        {
            if (_statePill == null || _theme == null)
            {
                return;
            }

            EngineState state = _engine != null ? _engine.State : EngineState.Idle;
            switch (state)
            {
                case EngineState.Running:
                    _statePill.PillColor = _theme.Success;
                    _statePill.Text = Localization.T("RUNNING");
                    break;
                case EngineState.Paused:
                    _statePill.PillColor = _theme.Warning;
                    _statePill.Text = Localization.T("PAUSED");
                    break;
                case EngineState.Idle:
                default:
                    _statePill.PillColor = _theme.TextMuted;
                    _statePill.Text = Localization.T("IDLE");
                    break;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Window / tray behaviour
        // ─────────────────────────────────────────────────────────────────────

        private void ApplyWindowPreferences()
        {
            ReassertTopMost();

            // Switch to manual positioning up front so the shell's CenterScreen
            // default doesn't fight the saved position. The actual bounds are applied
            // in OnLoad (RestoreWindowBounds), after the form has been DPI-scaled and
            // fully constructed — applying them here in the constructor is unreliable.
            if (_settings.RememberWindowPosition && HaveSavedWindowBounds())
            {
                StartPosition = FormStartPosition.Manual;
            }

            // NOTE: "Start minimised to tray" is intentionally handled in the
            // SetVisibleCore override below, not here. Calling BeginInvoke/Hide in
            // the constructor throws because the window handle does not exist yet.
        }

        protected override void OnLoad(EventArgs e)
        {
            // Runs after the handle exists, i.e. after WinForms applied DPI
            // autoscaling — so the centring system records the *scaled* base
            // positions (capturing them in the constructor would make every
            // re-centre snap controls back to their unscaled 96-DPI spots).
            EnableAutoFit();

            // Reconcile the Windows startup entry with the user's Tempo setting: heal a
            // vanished entry (cleaner/AV), refresh a moved path, and — crucially —
            // respect a disable made in Windows' own Startup list. If Windows overrode
            // the setting, mirror that back so Tempo's checkbox tells the truth.
            try
            {
                if (_settings != null)
                {
                    bool effective = Utils.StartupManager.Reconcile(_settings.LaunchAtStartup);
                    if (effective != _settings.LaunchAtStartup)
                    {
                        _settings.LaunchAtStartup = effective;
                        if (_launchStartupCheck != null) { _launchStartupCheck.Checked = effective; }
                        try { Persistence.SettingsManager.Save(_settings); } catch { }
                        Utils.Logger.Info("[startup] launch-at-login setting reconciled with Windows to: " + effective);
                    }
                }
            }
            catch { }

            // When a full-screen game (or anything) changes the screen resolution or
            // DPI and then exits, controls can end up mis-placed/overlapping until the
            // next manual resize. Re-centre every page whenever Windows reports a
            // display change so the layout repairs itself automatically.
HookSystemEvents();

            // MinimumSize is not covered by the one-time autoscale.
            float uiScale = CurrentAutoScaleDimensions.Width / 96f;
            MinimumSize = ScaledMinimumSize(uiScale);

            base.OnLoad(e);
            RestoreWindowBounds();
            ApplyDarkTitleBar();
            ResumeCaptionsAfterRestartIfAsked();

            // Notifications and the screenshot alert MUST be armed here, not only in
            // OnShown. With "Start minimised to tray" on, the window is never shown at
            // launch, so OnShown never fires — and Tempo sat there with its notification
            // mirror and clipboard watcher switched OFF until the user happened to open
            // the window. The log made it plain: the mirror only ever started minutes
            // after launch, exactly when the window was first opened, and on a launch
            // where it was never opened it never started at all. Both calls are
            // idempotent, so OnShown re-running them is harmless.
            ApplyNotificationSettings();
            ApplyClipboardImageWatcher();
            StartIntegrityCheck();

            // Start hidden so the window can fade in once it's shown, giving a smooth
            // launch (and a smooth hand-off when the app restarts itself).
            try { Opacity = 0; } catch { /* opacity unsupported — ignore */ }
        }

        // Guard so the integrity check runs once per launch however many entry points
        // call it — the same shape as HookSystemEvents, and for the same reason: a
        // start-minimised-to-tray launch never runs OnLoad or OnShown.
        private bool _integrityStarted;

        // Set by "Trust this copy" so the re-check it triggers records the verdict
        // without warning about it again. One shot, consumed on use.
        private bool _integritySuppressNextWarning;

        /// <summary>
        /// Verifies that Tempo.exe is still the file that was installed, on a worker
        /// thread, and tells the user when it is not.
        ///
        /// Deliberately AFTER the window is up. The check reads and hashes ~106 MB;
        /// that is ~120 ms of work which is invisible on a background thread and would
        /// be a visible stall on the startup path — and an integrity check nobody
        /// notices is the only kind that survives contact with users.
        /// </summary>
        private void StartIntegrityCheck()
        {
            if (_integrityStarted)
            {
                return;
            }
            _integrityStarted = true;

            try
            {
                Utils.IntegrityCheck.RunInBackground(_settings, verdict =>
                {
                    // Back to the UI thread before touching settings or showing a card.
                    try
                    {
                        if (IsDisposed || !IsHandleCreated) { return; }
                        BeginInvoke((Action)(() => OnIntegrityResult(verdict)));
                    }
                    catch (Exception ex) { Utils.Logger.Swallow("StartIntegrityCheck.post", ex); }
                });
            }
            catch (Exception ex) { Utils.Logger.Swallow("StartIntegrityCheck", ex); }
        }

        /// <summary>
        /// Acts on the verdict: saves a freshly recorded fingerprint, and warns once
        /// about a bad one.
        ///
        /// "Once" is per verdict AND per file, not per launch. Warning on every start
        /// about a condition the user has already seen is how a security warning gets
        /// trained into background noise — but if the file changes AGAIN, that is new
        /// information and it warns again. The Live Debug health panel shows the state
        /// unconditionally, so the status is never only in a card that was dismissed.
        /// </summary>
        private void OnIntegrityResult(Utils.IntegrityVerdict verdict)
        {
            try
            {
                if (_settings == null) { return; }

                RefreshIntegrityStatus();

                // Persist unconditionally. The check may have recorded a new fingerprint,
                // a GitHub confirmation, or both — and once the online layer can promote
                // a "Baselined" result to "Genuine", saving only on Baselined would have
                // thrown away the very baseline that had just been taken.
                try { Persistence.SettingsManager.Save(_settings); }
                catch (Exception ex) { Utils.Logger.Swallow("Integrity.save", ex); }

                if (!Utils.IntegrityCheck.IsProblem) { return; }

                string hash = Utils.IntegrityCheck.CurrentHash ?? "";
                string token = verdict + ":" + (hash.Length >= 12 ? hash.Substring(0, 12) : hash);

                // The user has just said, in a confirmation dialog, that they accept
                // this file. Popping the warning card at them a second later would be
                // the app arguing with an answer it asked for.
                if (_integritySuppressNextWarning)
                {
                    _integritySuppressNextWarning = false;
                    _settings.IntegrityLastWarned = token;
                    try { Persistence.SettingsManager.Save(_settings); }
                    catch (Exception ex) { Utils.Logger.Swallow("Integrity.saveTrusted", ex); }
                    return;
                }

                if (string.Equals(_settings.IntegrityLastWarned, token, StringComparison.Ordinal))
                {
                    return;                     // already told them about this exact file
                }
                _settings.IntegrityLastWarned = token;
                try { Persistence.SettingsManager.Save(_settings); }
                catch (Exception ex) { Utils.Logger.Swallow("Integrity.saveWarned", ex); }

                string title;
                string body;
                switch (verdict)
                {
                    case Utils.IntegrityVerdict.Damaged:
                        title = Localization.T("Tempo's program file is damaged");
                        body = Localization.T(
                            "Part of Tempo.exe is unreadable. This usually follows a crash or a power "
                            + "cut during an update. Reinstalling from the official download fixes it.");
                        break;
                    case Utils.IntegrityVerdict.Repackaged:
                        title = Localization.T("This is not an official Tempo build");
                        body = Localization.T(
                            "This copy was packaged by someone else. If you did not build it yourself, "
                            + "replace it with the official download.");
                        break;
                    case Utils.IntegrityVerdict.UnknownRelease:
                        title = Localization.T("This version was never published");
                        body = Localization.T(
                            "GitHub has no release matching this version number, so nothing outside "
                            + "this PC can confirm the file. That is normal for a build you made "
                            + "yourself — but if you downloaded it, get it from the official releases "
                            + "page instead.");
                        break;
                    default:
                        title = Localization.T("Tempo has been modified");
                        // Two different findings share this card, so the sentence has to
                        // fit both: the local fingerprint changed, or GitHub's copy of
                        // this exact version is a different file.
                        body = Utils.IntegrityCheck.ConfirmedByGitHub
                            ? Localization.T(
                                "Tempo.exe is not the file that was installed, and the version number has "
                                + "not changed. If you did not replace it yourself, reinstall from the "
                                + "official download.")
                            : Localization.T(
                                "This copy does not match the release published for its version number. "
                                + "Either it was altered after download, or it did not come from the "
                                + "official releases page. Reinstall from there to be sure.");
                        break;
                }

                if (_notifications != null)
                {
                    _notifications.Notify("Tempo", title, body, ToastKind.Warning);
                }
                else
                {
                    ShowWarning(title + "\n\n" + body);
                }
            }
            catch (Exception ex) { Utils.Logger.Swallow("OnIntegrityResult", ex); }
        }

        // Guard so the process-wide SystemEvents handlers are attached exactly once,
        // however many entry points call this.
        private bool _systemEventsHooked;

        /// <summary>
        /// Subscribes the process-wide Windows notifications Tempo depends on:
        ///   • DisplaySettingsChanged — re-lays-out pages when a game or a resolution
        ///     change resizes the desktop underneath the window.
        ///   • UserPreferenceChanged  — live "Match Windows" theme following.
        ///
        /// This used to live in OnLoad alone, which NEVER RUNS on a start-minimised-to-tray
        /// launch (the handle is created directly and the form is never shown — the same
        /// hole that left the notification mirror switched off). So every launch-at-sign-in
        /// silently lost live theme-following and display-change layout repair until the
        /// user happened to open the window. Called from both entry points now; the guard
        /// makes double-subscription (and the duplicate handler calls that would cause)
        /// impossible. Detached in CleanUp.
        /// </summary>
        private void HookSystemEvents()
        {
            if (_systemEventsHooked)
            {
                return;
            }
            _systemEventsHooked = true;
            try
            {
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
                Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            }
            catch (Exception ex) { Utils.Logger.Swallow("HookSystemEvents", ex); }
        }

        /// <summary>
        /// Turns captions back on after a restart that was performed to apply a caption
        /// setting (the CPU ⇄ GPU engine order, or the interface language).
        ///
        /// The flag is CLEARED FIRST and saved before anything is started. A restart is
        /// exactly the situation where the thing you are restarting into might not come
        /// up — a GPU engine that access-violates on load is the case this feature makes
        /// more likely, since enabling it is the main reason to restart at all. If the
        /// flag survived the attempt, every subsequent launch would retry the same crash
        /// and Tempo would be unopenable. Clearing it up front costs one lost resume in
        /// the failure case and cannot loop.
        ///
        /// Runs from OnLoad, not OnShown: with "Start minimised to tray" the window is
        /// never shown and OnShown never fires.
        /// </summary>
        private void ResumeCaptionsAfterRestartIfAsked()
        {
            if (_settings == null || !_settings.CaptionResumeAfterRestart) { return; }
            try
            {
                _settings.CaptionResumeAfterRestart = false;
                try { Persistence.SettingsManager.Save(_settings); } catch { }

                // Let the rest of startup finish first — the caption stack, the audio
                // watcher and the device lists are still being wired up at OnLoad, and
                // starting into a half-built pipeline is how the "engine started but
                // hears nothing" state happens.
                var t = new System.Windows.Forms.Timer { Interval = 2500 };
                t.Tick += (s, ev) =>
                {
                    t.Stop();
                    t.Dispose();
                    if (IsDisposed || _shuttingDown || _captionsActive) { return; }
                    try
                    {
                        Utils.Logger.Info("[Captions] resuming captions after the restart that applied a caption setting.");
                        SetCaptionsActive(true);
                    }
                    catch (Exception ex) { Utils.Logger.Warn("[Captions] resume after restart failed: " + ex.Message); }
                };
                t.Start();
            }
            catch (Exception ex) { Utils.Logger.Warn("[Captions] resume-after-restart setup failed: " + ex.Message); }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            // Now that the window and its tab-page handles exist, apply the dark/light
            // scroll bars and title bar (the construction-time pass ran before handles
            // were created, so the OS chrome couldn't be themed yet).
            ApplyNativeChrome();

            if (_settings != null && _settings.CursorTrailEnabled)
            {
                ApplyCursorTrail(true);
            }

            // Start the Windows-notification mirror if the user has opted in. Done here
            // (not during construction) because RequestAccessAsync wants a live window,
            // and the setting may enable it silently on a normal launch.
            ApplyNotificationSettings();
            ApplyClipboardImageWatcher();   // screenshot/clipboard-image alert

            // Tell the startup splash (running on its own thread) to fade out, then WAIT
            // for it to actually close before showing the welcome notice and fading this
            // window in. This window stays invisible (Opacity 0 from OnLoad) meanwhile, so
            // the splash is fully seen first - previously we fired the notice + fade-in
            // immediately, and on fast machines this window covered the splash (and the
            // notice could open behind the TopMost splash) before the loading effect was
            // ever visible. A hard timeout means startup proceeds even if the splash never
            // reported closed.
            try { SplashForm.RequestClose(); } catch { }
            var splashWait = new System.Windows.Forms.Timer { Interval = 40 };
            var waited = System.Diagnostics.Stopwatch.StartNew();
            splashWait.Tick += (s, ev) =>
            {
                if (SplashForm.IsClosed || waited.ElapsedMilliseconds >= 2500)
                {
                    splashWait.Stop();
                    splashWait.Dispose();
                    _splashGateOpen = true;
                    // Fade THIS window in first, alone, and only show the welcome notice
                    // once the fade has finished - otherwise the notice and the still-
                    // fading-in window appear together and read as a glitchy double-pop.
                    // The flag is honoured at the end of the fade in StartFadeIn().
                    _pendingWelcomeNotice = true;
                    StartFadeIn();
                }
            };
            splashWait.Start();
        }

        private bool _officialNoticeAttempted;
        /// <summary>The welcome notice is waiting for a fullscreen app to get out of the way.</summary>
        private bool _welcomeDeferred;

        // Stays closed during the brief startup window while the splash is on screen, so
        // the welcome notice isn't opened behind the (TopMost) splash. Opened once the
        // splash has been handled - by OnShown after a normal start, or immediately by
        // SetVisibleCore for a tray start (where the window is never shown).
        private bool _splashGateOpen;
        // True between the splash closing and the fade-in finishing, while a welcome notice
        // is queued to appear once the window has fully faded in (so it isn't shown mid-fade).
        private bool _pendingWelcomeNotice;

        /// <summary>
        /// First run only: a one-time note about where Tempo is officially
        /// published and how to verify a download. Deferred until the window is
        /// actually visible, so "start minimised to tray" users see it on their
        /// first restore instead of a dialog popping out of nowhere.
        /// </summary>
        private void MaybeShowOfficialSourceNotice()
        {
            // Shown once per launch (every run) now - users asked for the safety/official-
            // source notice to appear every time, not only on the very first run. The
            // per-session _officialNoticeAttempted guard still stops it opening twice in
            // one run (e.g. when retried after a restore-from-minimised).
            if (_officialNoticeAttempted || _settings == null ||
                !Visible || WindowState == FormWindowState.Minimized)
            {
                return;
            }

            // Not while a fullscreen app owns the screen.
            //
            // This is a MODAL dialog, so it takes the foreground — over a fullscreen
            // game that is a forced mode switch, and over a presentation it is a Tempo
            // dialog in front of the room. Windows suppresses its own toasts in both
            // cases and Tempo's notification cards now do too; a dialog that steals
            // focus deserves at least the same restraint.
            //
            // Deliberately does NOT set _officialNoticeAttempted, so this is a DEFERRAL
            // rather than a skip: the retry paths (restore from tray, the post-fade
            // hook) will show it once the screen is free.
            if (Utils.GamePresence.ShouldHoldNotifications(out string busyWhy))
            {
                // ShowQueuedWelcomeNotice clears _pendingWelcomeNotice BEFORE calling
                // here, so returning without arming a retry would drop the notice for
                // this whole run. _welcomeDeferred is what the UI tick watches.
                if (!_welcomeDeferred)
                {
                    _welcomeDeferred = true;
                    Utils.Logger.Info("[Welcome] holding the welcome notice — " + busyWhy +
                                      "; it will show once the screen is free.");
                }
                return;
            }
            _welcomeDeferred = false;

            _officialNoticeAttempted = true;
            try
            {
                using (var dlg = new OfficialSourceForm(_theme))
                {
                    dlg.ShowDialog(this);
                }
            }
            catch
            {
                // Cosmetic only — never block startup over it.
            }
        }

        /// <summary>Shows or hides the just-for-fun colourful cursor trail overlay.</summary>
        private void ApplyCursorTrail(bool on)
        {
            try
            {
                if (on)
                {
                    if (_cursorTrail == null || _cursorTrail.IsDisposed)
                    {
                        _cursorTrail = new CursorTrailForm();
                    }
                    _cursorTrail.Begin();
                }
                else if (_cursorTrail != null && !_cursorTrail.IsDisposed)
                {
                    _cursorTrail.End();
                }
            }
            catch
            {
                // Cosmetic only — never let it disrupt the app.
            }
        }

        private System.Windows.Forms.Timer _fadeTimer;

        /// <summary>Fades the window in from transparent to its configured opacity.</summary>
        private void StartFadeIn()
        {
            double target = 1.0;
            if (_settings != null)
            {
                target = _settings.WindowOpacity / 100.0;
            }
            if (target < 0.2) target = 0.2;   // never settle invisible
            if (target > 1.0) target = 1.0;

            // Reveal the window DIRECTLY at its final opacity — no multi-step Opacity fade.
            // A partially-transparent form is a LAYERED window (WS_EX_LAYERED); combined with
            // the WS_EX_COMPOSITED this form uses for the backdrop, the child labels and
            // buttons flashed their light default background while the layered fade ran —
            // the "light flash on every launch" most users were seeing. Snapping straight to
            // the final opacity skips the layered transition entirely, so the already-dark,
            // themed window simply appears cleanly. (The startup splash still provides the
            // smooth load-in beforehand.)
            _fadeTimer?.Stop();
            _fadeTimer?.Dispose();
            _fadeTimer = null;
            try { Opacity = target; } catch { }
            try { Invalidate(true); } catch { }
            ShowQueuedWelcomeNotice();
        }

        /// <summary>
        /// Shows the queued startup welcome/official-source notice exactly once, if
        /// one was pending. Centralised so every code path that finishes (or skips)
        /// the fade-in calls the same thing - previously a skipped fade could leave
        /// the notice queued forever, which is why "the welcome message stopped
        /// showing".
        /// </summary>
        private void ShowQueuedWelcomeNotice()
        {
            if (!_pendingWelcomeNotice) return;
            _pendingWelcomeNotice = false;
            try { BeginInvoke((Action)MaybeShowOfficialSourceNotice); }
            catch
            {
                // If marshalling fails for any reason, fall back to a direct call so
                // the notice still appears rather than being silently dropped.
                try { MaybeShowOfficialSourceNotice(); } catch { }
            }
        }

        /// <summary>
        /// Fades the window out and then restarts the app — used for the language
        /// change so the transition between the old and new instance feels smooth
        /// rather than an abrupt flash.
        /// </summary>
        /// <summary>
        /// Names whatever a restart would interrupt, or null when nothing would be lost.
        ///
        /// This exists because the restart path is a BLIND SPOT in the exit guards. Every
        /// other route out of Tempo honours "confirm before exit while running", but
        /// FadeOutThenRestart sets _reallyClosing before closing, and OnFormClosing's check
        /// reads `... && !_reallyClosing` — so a restart tore down a live click run or a
        /// playing macro without a word, for a user who had explicitly asked to be warned.
        /// The restart prompt is now the warning, which is why both prompts must ask.
        /// </summary>
        private string DescribeRestartInterruption()
        {
            try
            {
                if (_engine != null && _engine.IsRunning)
                {
                    return Localization.T("Clicking is running — restarting will stop it.");
                }
                if (_player != null && _player.IsPlaying)
                {
                    return Localization.T("A macro is playing — restarting will stop it.");
                }
                if (_recorder != null && _recorder.IsRecording)
                {
                    return Localization.T("A macro is being recorded — restarting will discard it.");
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Shows the themed restart prompt. Returns true when the user chose to restart.
        /// One method so the language and speech-engine prompts can never drift apart in
        /// wording, warnings or button behaviour.
        /// </summary>
        private bool AskToRestart(string headline, string saved, string why)
        {
            try
            {
                using (var dlg = new UI.RestartPromptForm(_theme, headline, saved, why,
                                                          DescribeRestartInterruption()))
                {
                    dlg.ShowDialog(this);
                    return dlg.RestartNow;
                }
            }
            catch (Exception ex)
            {
                // Never let a dialog failure strand the setting: fall back to the plain
                // prompt rather than silently doing nothing.
                Utils.Logger.Warn("[Restart] themed prompt failed: " + ex.Message);
                return MessageBox.Show(this, saved + "\n\n" + why, headline,
                                       MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            }
        }

        private void FadeOutThenRestart(string whatFailed = "the new language")
        {
            _reallyClosing = true;

            // Captions come back on the other side. Restarting is how a caption-engine
            // change (CPU ⇄ GPU) is applied at all, so landing in a fresh Tempo with
            // captions switched OFF meant the user had to notice and turn them back on
            // — after a restart they only performed to make captions better.
            try
            {
                if (_settings != null)
                {
                    _settings.CaptionResumeAfterRestart = _captionsActive;
                    Persistence.SettingsManager.Save(_settings);
                }
            }
            catch { }

            // Cover the window with a brief, centred "Restarting…" message so the
            // restart reads as a deliberate, polished transition rather than a flash.
            //
            // Held in a variable the timer below can reach. It used to be a local inside
            // this try block, which meant the failure path had no way to take it back off
            // — so when the relaunch did not start, the window came back to full opacity
            // still completely covered by "Restarting to apply changes…", for ever. That
            // is the reported hang: nothing was actually stuck, the app was live and
            // responsive underneath a panel that nothing ever removed.
            Panel overlay = null;
            try
            {
                overlay = new Panel
                {
                    Bounds = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height),
                    BackColor = _theme != null ? _theme.Background : BackColor,
                    Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
                };
                var label = new Label
                {
                    Text = Localization.T("Restarting to apply changes\u2026"),
                    ForeColor = _theme != null ? _theme.Text : ForeColor,
                    Font = new Font("Segoe UI", 13f, FontStyle.Regular),
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Dock = DockStyle.Fill
                };
                overlay.Controls.Add(label);
                Controls.Add(overlay);
                overlay.BringToFront();
                overlay.Refresh();
            }
            catch
            {
                // Non-fatal: if the overlay can't be shown we still fade and restart.
            }

            Utils.Logger.Info("[Restart] fading out to relaunch (" + whatFailed + ").");

            // Driven by ELAPSED TIME, not by reading Opacity back. The old loop computed
            // `Opacity - 0.08` and stopped when that reached zero, so its termination
            // depended on the property round-tripping through WinForms' layered-window
            // plumbing. It does round-trip here (measured, including with this window's
            // WS_EX_COMPOSITED and borderless chrome), but a fade that can only end by
            // observing a side effect is a poor way to reach something as important as
            // "restart the app": one environment where that assignment is ignored and the
            // restart silently never happens. A clock cannot fail that way.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            const int FadeMs = 220;
            var t = new System.Windows.Forms.Timer { Interval = 16 };
            t.Tick += (s, ev) =>
            {
                double progress = clock.Elapsed.TotalMilliseconds / FadeMs;
                if (progress < 1.0)
                {
                    try { Opacity = 1.0 - progress; } catch { }
                    return;
                }

                t.Stop();
                t.Dispose();
                try { Opacity = 0; } catch { }

                try
                {
                    AutoClicker.Program.RestartApp();
                    Utils.Logger.Info("[Restart] replacement launched; this instance is exiting.");
                }
                catch (Exception ex)
                {
                    // The relaunch did not start. Put the window back exactly as it was:
                    // opaque, WITHOUT the overlay, and no longer flagged as closing.
                    Utils.Logger.Warn("[Restart] relaunch failed: " + ex.Message);

                    try
                    {
                        if (overlay != null)
                        {
                            Controls.Remove(overlay);
                            overlay.Dispose();
                            overlay = null;
                        }
                    }
                    catch { }

                    try { Opacity = 1; } catch { }

                    // _reallyClosing was set on the way in to bypass the close guards. If
                    // it is left set, the next click on the window's ✕ quits Tempo outright
                    // instead of sending it to the tray — a second surprise caused by the
                    // first one.
                    _reallyClosing = false;

                    // Undo the resume flag: nothing restarted, so there is no next launch
                    // to hand it to, and leaving it set would start captions unbidden
                    // whenever Tempo is next opened.
                    try
                    {
                        if (_settings != null && _settings.CaptionResumeAfterRestart)
                        {
                            _settings.CaptionResumeAfterRestart = false;
                            Persistence.SettingsManager.Save(_settings);
                        }
                    }
                    catch { }

                    ShowInfo(Localization.F("Tempo couldn't restart automatically. Please reopen it to apply {0}.", whatFailed));
                }
            };
            t.Start();
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>
        /// Asks the desktop window manager to render this window's title bar (and its
        /// native minimize / maximize / close buttons) in dark mode, so the system
        /// chrome matches the dark app instead of showing a bright white bar on top.
        /// No-ops gracefully on Windows versions that don't support it.
        /// </summary>
        private void ApplyDarkTitleBar()
        {
            try
            {
                // Dark/light first — this is all Windows 10 can do, and it's the
                // fallback when the colour attributes below aren't supported.
                int on = _theme == null || _theme.Background.GetBrightness() < 0.5f ? 1 : 0;
                // DWMWA_USE_IMMERSIVE_DARK_MODE is 20 on current Windows 10/11 builds
                // and was 19 on early Windows 10 2004 builds — try both.
                if (DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(Handle, 19, ref on, sizeof(int));
                }

                // Then the real fix: colour the title bar in the THEME, not just dark.
                // "Dark" is still plain black above a Synthwave-purple header, which is
                // exactly the mismatch that stood out. The header's top edge paints
                // _theme.Surface, so using that makes the seam vanish.
                TintTitleBarToTheme();
            }
            catch { /* not supported on this OS — leave the default title bar */ }
        }

        /// <summary>
        /// Paints this window's title bar (and its border) in the active theme so the
        /// system chrome continues the app. Applied on load and on every theme change.
        /// </summary>
        private void TintTitleBarToTheme()
        {
            if (_theme == null || !IsHandleCreated)
            {
                return;
            }
            NativeChrome.TintTitleBar(Handle, _theme.Surface, _theme.Text, _theme.Border);
        }

        /// <summary>
        /// Applies the saved window size and position. Runs from OnLoad (after the
        /// form is constructed and DPI-scaled) so the values reliably "stick" — doing
        /// this in the constructor gets overridden by auto-scaling.
        /// </summary>
        /// <summary>
        /// True when a window position has actually been saved before.
        ///
        /// Answered by the SIZE, never by the sign of the coordinates. The old test was
        /// "WindowLeft >= 0 && WindowTop >= 0", meant to spot the -1 defaults — but a
        /// monitor placed left of or above the primary has NEGATIVE coordinates (this
        /// machine's second screen sits at -1920,-2). A window parked there saved a
        /// perfectly good position that the test then read as "never saved", so every
        /// launch re-centred on the primary. Remember-position looked like it did
        /// nothing whatsoever — but only for people with a monitor on the left or above.
        ///
        /// Width and height cannot collide with a legitimate value the way a coordinate
        /// can: they default to -1 and a real window is never zero wide. They are
        /// written in the same breath as the position, so they can answer for it.
        /// </summary>
        private bool HaveSavedWindowBounds()
        {
            return _settings != null && _settings.WindowWidth > 0 && _settings.WindowHeight > 0;
        }

        private void RestoreWindowBounds()
        {
            if (_settings == null || !_settings.RememberWindowPosition)
            {
                Utils.Logger.Info("[window] not restoring — settings" +
                    (_settings == null ? " NULL" : " loaded, remember=" + _settings.RememberWindowPosition +
                     ", saved " + _settings.WindowLeft + "," + _settings.WindowTop + " " +
                     _settings.WindowWidth + "x" + _settings.WindowHeight) + ".");
                return;
            }

            // Work out which screen the saved spot lands on, so the size can be clamped
            // to that monitor's WORKING AREA (screen minus taskbar). A settings file
            // already poisoned by an older build — where exiting full screen saved the
            // whole-screen rect as the normal size — heals itself here instead of opening
            // clipped under the taskbar on every launch, forever.
            Rectangle vs = SystemInformation.VirtualScreen;
            int wantLeft = Math.Min(Math.Max(_settings.WindowLeft, vs.Left), vs.Right - 100);
            int wantTop = Math.Min(Math.Max(_settings.WindowTop, vs.Top), vs.Bottom - 100);
            Rectangle wa = Screen.FromPoint(new Point(wantLeft, wantTop)).WorkingArea;

            // Restore the size first (clamped to the minimum so it can't be tiny, and to
            // the working area so the bottom is never left under the taskbar).
            if (_settings.WindowWidth >= MinimumSize.Width &&
                _settings.WindowHeight >= MinimumSize.Height)
            {
                Size = new Size(
                    Math.Max(MinimumSize.Width, Math.Min(_settings.WindowWidth, wa.Width)),
                    Math.Max(MinimumSize.Height, Math.Min(_settings.WindowHeight, wa.Height)));
            }

            // Then the position. If the user wants the window remembered, restore the
            // saved spot (clamped to a visible screen). Otherwise ALWAYS center on the
            // primary screen - so each launch opens centered instead of wherever it was
            // last, or at some stale/offscreen spot.
            if (_settings.RememberWindowPosition && HaveSavedWindowBounds())
            {
                StartPosition = FormStartPosition.Manual;
                // Keep the WHOLE window inside the work area, so the status strip and the
                // bottom card can never sit behind the taskbar.
                int left = Math.Max(wa.Left, Math.Min(wantLeft, wa.Right - Width));
                int top = Math.Max(wa.Top, Math.Min(wantTop, wa.Bottom - Height));
                Location = new Point(left, top);
                Utils.Logger.Info("[window] restored to " + left + "," + top + " " + Width + "x" + Height +
                                  " (saved " + _settings.WindowLeft + "," + _settings.WindowTop + " " +
                                  _settings.WindowWidth + "x" + _settings.WindowHeight +
                                  ", work area " + wa + ").");
            }
            else
            {
                // Center on the screen the window is currently on (primary by default).
                StartPosition = FormStartPosition.Manual;
                Rectangle here = Screen.FromControl(this).WorkingArea;
                int left = here.Left + (here.Width - Width) / 2;
                int top = here.Top + (here.Height - Height) / 2;
                Location = new Point(left, top);
                // Say WHY, so "it forgot my position again" has an answer in the log
                // instead of needing a debugger.
                Utils.Logger.Info("[window] centred at " + left + "," + top +
                                  " — remember=" + _settings.RememberWindowPosition +
                                  ", saved size " + _settings.WindowWidth + "x" + _settings.WindowHeight + ".");
            }
        }

        private bool _startMinimizedApplied;
        // True when Windows auto-started Tempo at sign-in (the startup entry passes
        // --startup). When so, Tempo starts in the tray instead of showing its window.
        private readonly bool _launchedAtStartup = Utils.StartupManager.LaunchedAtStartup();

        /// <summary>
        /// True when this process is the replacement half of Tempo's own restart, so the
        /// window must come back on screen rather than obey "start minimised to tray".
        /// </summary>
        private readonly bool _launchedForRestart = AutoClicker.Program.StartedForRestart();

        /// <summary>
        /// Honours the "start minimised to tray" option without a visible flash by
        /// suppressing the very first show. The handle is still created so timers
        /// and global hotkeys work while the window sits in the tray.
        /// </summary>
        protected override void SetVisibleCore(bool value)
        {
            // A RESTART is the one launch that must ignore "start minimised to tray".
            //
            // The user was looking at the window when they pressed Restart now — they
            // changed a setting and asked to see it applied. Honouring the tray preference
            // here made Tempo disappear instead: the window faded out, the replacement
            // started straight into the tray, and from the user's side the app simply quit
            // when they clicked a button labelled "Restart". The preference is about how
            // Tempo comes up at SIGN-IN, which this is not.
            if (!_startMinimizedApplied && value && !_launchedForRestart &&
                ((_settings != null && _settings.StartMinimizedToTray) || _launchedAtStartup))
            {
                _startMinimizedApplied = true;

                if (!IsHandleCreated)
                {
                    CreateHandle();
                }

                base.SetVisibleCore(false);

                // OnShown never fires for a suppressed tray start, so dismiss the splash
                // here (it would otherwise linger until its safety timeout) and open the
                // notice gate so the welcome notice shows when the user first restores.
                try { SplashForm.RequestClose(); } catch { }
                _splashGateOpen = true;

                // Arm the notification subsystem HERE too. Neither OnLoad nor OnShown
                // fires on a suppressed tray start (the handle is created directly and
                // the form is never shown), so a Tempo launched straight to the tray —
                // the normal case with "Start minimised to tray", and every launch-at-
                // sign-in — ran with its Windows-notification mirror and its clipboard
                // screenshot watcher switched OFF until the user happened to open the
                // window. The log showed the mirror starting minutes after launch, at
                // exactly the moment the window was first opened, and never at all on a
                // launch where it wasn't. Both calls are idempotent.
                try
                {
                    ApplyNotificationSettings();
                    ApplyClipboardImageWatcher();
                    // Same hole: OnLoad never runs on a tray start, so without this a
                    // sign-in launch had no live theme-following and no display-change
                    // layout repair either.
                    HookSystemEvents();
                    // And the integrity check, for the same reason. A launch-at-sign-in
                    // that goes straight to the tray is the launch most likely to follow
                    // an update or a swapped exe, so it is the last one that should skip
                    // the check.
                    StartIntegrityCheck();
                }
                catch (Exception nex) { Utils.Logger.Swallow("TrayStartNotify", nex); }

                if (_trayIcon != null)
                {
                    if (_settings != null && !_settings.HasShownTrayIntro)
                    {
                        // First time ever hiding to the tray: always explain where the
                        // window went and how to get it back, even if routine tray
                        // notifications are turned off - otherwise people think the app
                        // failed to open. Shown once, then remembered.
                        _settings.HasShownTrayIntro = true;
                        try { Persistence.SettingsManager.Save(_settings); } catch { }
                        string intro = _launchedAtStartup
                            ? "Tempo started with Windows and is running in the system tray (bottom-right, near the clock). Click its icon to open the window."
                            : "Tempo is still running in the system tray (bottom-right, near the clock). Click its icon to open the window. (Change this in Settings.)";
                        TempoNotify(7000, "Tempo is running in the tray", intro, ToolTipIcon.Info,
                            always: true);   // first run: closing would otherwise look like quitting
                    }
                    else if (_settings != null && _settings.ShowTrayNotifications)
                    {
                        string why = _launchedAtStartup
                            ? "Started with Windows \u2014 running in the tray."
                            : "Running in the tray.";
                        TempoNotify(1500, "Tempo", why, ToolTipIcon.Info);
                    }
                }

                return;
            }

            base.SetVisibleCore(value);
        }

        /// <summary>
        /// The Show/hide-window hotkey. Deliberately routed through the same two helpers
        /// the tray icon and the close button use, rather than its own Hide()/Show() pair:
        /// as a private copy it had drifted into a worse version of both halves.
        ///
        ///   • Hiding called bare Hide(), so the window blinked out of existence instead
        ///     of playing the minimise animation — the exact "looks like it crashed"
        ///     effect HideToTrayAnimated exists to avoid.
        ///   • Showing forced WindowState.Normal, so hiding a MAXIMISED Tempo and
        ///     bringing it back silently un-maximised it. The tray path has always
        ///     restored the previous state properly.
        ///
        /// A MINIMISED window also counts as "not on screen" here. Visible stays true
        /// while minimised, so the old check hid it to the tray — pressing "show/hide"
        /// on a window you cannot see made it disappear further, and took two presses to
        /// get it back. Anyone pressing this key when nothing is on screen wants it up.
        /// </summary>
        private void ToggleWindowVisibility()
        {
            if (Visible && WindowState != FormWindowState.Minimized)
            {
                HideToTrayAnimated();
            }
            else
            {
                ShowFromTrayAndActivate();
            }
        }

        private void ToggleAlwaysOnTop()
        {
            _settings.AlwaysOnTop = !_settings.AlwaysOnTop;
            ReassertTopMost();

            // Keep the Settings tab checkbox in sync if it exists.
            if (_alwaysOnTopCheck != null)
            {
                _alwaysOnTopCheck.Checked = _settings.AlwaysOnTop;
            }

            // Update the tray menu item's checked state too.
            if (_trayAlwaysOnTopItem != null)
            {
                _trayAlwaysOnTopItem.Checked = _settings.AlwaysOnTop;
            }

            if (_settings.ShowTrayNotifications)
            {
                TempoNotify(1000, "Tempo",
                    _settings.AlwaysOnTop ? "Always on top: ON" : "Always on top: OFF",
                    ToolTipIcon.Info);
            }
        }

        /// <summary>
        /// Forces the window's HWND_TOPMOST state to match
        /// <c>_settings.AlwaysOnTop</c>. Windows loses the topmost z-order across
        /// <c>Hide()</c> / <c>Show()</c> cycles, restore-from-minimised, and some
        /// focus changes; the .NET <see cref="Form.TopMost"/> setter no-ops when
        /// the cached value already matches, so we toggle through the opposite
        /// value first to guarantee SetWindowPos is called.
        /// </summary>
        private void ReassertTopMost()
        {
            if (_settings == null)
            {
                return;
            }

            bool desired = _settings.AlwaysOnTop;
            TopMost = !desired;
            TopMost = desired;
        }

        /// <summary>
        /// Makes sure the window is somewhere a human can actually see. Useful
        /// after restoring from the tray on a system where a monitor was
        /// disconnected since the window position was saved.
        /// </summary>
        private void EnsureOnScreen()
        {
            var virt = SystemInformation.VirtualScreen;
            int margin = 80;

            if (Left + Width < virt.Left + margin ||
                Top + Height < virt.Top + margin ||
                Left > virt.Right - margin ||
                Top > virt.Bottom - margin)
            {
                CenterToScreen();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Window-state overrides that re-assert TopMost
        // ─────────────────────────────────────────────────────────────────────

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            UpdateTraySleepState();
            if (Visible && _splashGateOpen && !_pendingWelcomeNotice)
            {
                MaybeShowOfficialSourceNotice();
            }

            // Every transition into visible re-asserts TOPMOST so the window
            // doesn't sink behind other windows after a tray-restore.
            if (Visible)
            {
                ReassertTopMost();
            }
            UpdateGifAnimationState();
        }

        /// <summary>
        /// Keeps a normal (non-maximised) window inside the monitor's WORK AREA, so its
        /// bottom edge can never sit under the taskbar. When it does, the first things
        /// to disappear are the status strip and the sidebar's version stamp — which is
        /// exactly the "I can't see the version unless I'm in full screen" report: full
        /// screen matches the screen exactly, so everything fits, while a too-tall normal
        /// window loses its bottom. Only shrinks a window that genuinely overflows.
        /// </summary>
        private void ClampToWorkArea()
        {
            if (IsDisposed || !IsHandleCreated || _isFullScreen ||
                WindowState != FormWindowState.Normal)
            {
                return;
            }
            try
            {
                Rectangle wa = Screen.FromControl(this).WorkingArea;
                int w = Math.Min(Width, wa.Width);
                int h = Math.Min(Height, wa.Height);
                int left = Math.Max(wa.Left, Math.Min(Left, wa.Right - w));
                int top = Math.Max(wa.Top, Math.Min(Top, wa.Bottom - h));
                if (w != Width || h != Height || left != Left || top != Top)
                {
                    // Say WHICH correction happened — the old wording claimed the window
                    // "overflowed" even when only its position was nudged, which read as
                    // a size bug in the log when nothing was oversized.
                    bool resized = w != Width || h != Height;
                    Utils.Logger.Info("[UI] window " + Width + "x" + Height + " @ (" + Left + "," + Top + ") "
                        + (resized ? "was larger than" : "sat outside") + " the "
                        + wa.Width + "x" + wa.Height + " work area — "
                        + (resized ? "resized" : "moved") + " to " + w + "x" + h + " @ (" + left + "," + top + ") "
                        + "so the status bar and version stamp stay on screen.");
                    Bounds = new Rectangle(left, top, w, h);
                }
            }
            catch (Exception ex) { Utils.Logger.Swallow("ClampToWorkArea", ex); }
        }

        /// <summary>
        /// The geometry the window last had while it was genuinely a normal, visible,
        /// on-screen window.
        ///
        /// THE BUG THIS FIXES: the remembered position was read from Bounds once, inside
        /// OnFormClosing, and skipped whenever WindowState wasn't Normal. By the time a
        /// tray user exits, it never is — HideToTrayAnimated sets Minimized before Hide()
        /// and deliberately never puts it back, so the state is still Minimized when Exit
        /// is chosen from the tray menu. The save returned without writing anything, and
        /// the stored position simply froze: on this machine it read 86,0 1020x824 in
        /// every launch across several days of use, no matter where the window was
        /// dragged. Restoring was never at fault — the logs show it reproducing the saved
        /// rect exactly, to the pixel. It was being handed a stale rect.
        ///
        /// Sampling continuously fixes it for every route out of the app — the tray, a
        /// minimised window, a maximised one, or a crash-adjacent teardown — because the
        /// value is already correct before the exit path starts.
        /// </summary>
        private Rectangle _lastNormalBounds = Rectangle.Empty;

        /// <summary>
        /// Records Bounds whenever the window is in a state where they MEAN something.
        /// Minimised bounds are meaningless, a maximised rect isn't what the user
        /// arranged, an invisible window is mid-teardown or parked in the tray, and full
        /// screen has its own remembered rect (_fsPrevBounds).
        /// </summary>
        private void TrackNormalBounds()
        {
            if (_shuttingDown) { return; }
            // _isFullScreen ONLY — never FormBorderStyle. This window is borderless at all
            // times (see _customChrome: Tempo draws its own ─ □ ✕), so testing the border
            // style would reject every ordinary window there is. That exact mistake is
            // what broke saving in the first place; see SaveWindowPosition.
            if (_isFullScreen) { return; }
            if (WindowState != FormWindowState.Normal || !Visible) { return; }

            Rectangle b = Bounds;
            if (b.Width > 0 && b.Height > 0)
            {
                _lastNormalBounds = b;
            }
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            // Every drag lands here. Storing a Rectangle per move message is far cheaper
            // than the alternative of being wrong about where the user left the window.
            TrackNormalBounds();
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            // After the user finishes dragging, not during — clamping mid-drag would
            // fight the resize.
            ClampToWorkArea();
            // Clamping can move the window, so sample AFTER it, or the remembered rect
            // would be the pre-clamp one Windows never actually showed.
            TrackNormalBounds();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            // Keep the maximise/restore glyph honest. OnResize fires for EVERY route
            // into and out of maximised — the caption button, a double-click on the
            // header, Aero Snap, Win+Up/Down, the taskbar, a restore from the tray —
            // whereas the button's own click handler (the only place this used to be
            // set) obviously fires for just one of them.
            if (WindowState != FormWindowState.Minimized)
            {
                // The last state the window was actually SHOWN in. Minimising discards
                // that information from WindowState, so anything that needs to restore
                // "the way it was" has to have remembered it beforehand.
                _lastNonMinimizedState = WindowState;
                if (_header != null)
                {
                    _header.WindowMaximized = WindowState == FormWindowState.Maximized;
                }
            }

            if (WindowState == FormWindowState.Minimized)
            {
                // Remember that we went down to the tray/taskbar so we can repair the
                // layout when we come back (see below).
                _wasMinimized = true;
            }
            else if (WindowState != FormWindowState.Minimized && Visible)
            {
                // Coming back from a minimised state is the other case where the
                // HWND topmost flag can quietly disappear. This covers BOTH a normal
                // and a maximised window - previously it only fired for Normal, so a
                // maximised Tempo came back from the tray with a stale, empty-looking
                // page because the repair below never ran.
                ReassertTopMost();

                // A welcome notice can be deferred when Tempo starts minimised to the
                // tray. OnVisibleChanged only retries on a visible transition; a plain
                // un-minimise (window was already visible) wouldn't trigger it, and a
                // restore caught mid-transition can hit the "still minimised" guard. Retry
                // here on any restore so tray-start users reliably see it once. (It
                // self-gates: a no-op once already shown, and held until the splash is done.)
                if (_splashGateOpen && !_pendingWelcomeNotice)
                {
                    try { BeginInvoke((Action)MaybeShowOfficialSourceNotice); } catch { }
                }

                // Restoring from minimised can leave AutoScroll pages with a stale
                // layout and scroll position - the bug where, after a macro recording
                // auto-minimised the window, the Statistics dashboard came back pushed
                // far down with a big empty gap above it. Re-lay-out every page from
                // scratch and snap the visible one back to the top. Deferred via
                // BeginInvoke so it runs once the restored client size has settled
                // (measuring too early would just re-introduce a bad layout).
                if (_wasMinimized)
                {
                    _wasMinimized = false;
                    RepairLayoutAfterRestore();
                }
            }

            UpdateGifAnimationState();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);

            // When a modal dialog closes (the update result, Merge, the macro editor,
            // a CPS test) or we Alt-Tab back, the current page just needs repainting -
            // and a plain (non-buffered) AutoScroll page repaints itself anyway. We add
            // only a gentle repaint of the visible tab.
            //
            // We deliberately do NOT rebuild the layout or touch the scroll position
            // here. Forcing a full re-centre on every activation is what kept leaving
            // Settings/Statistics shoved down with a big empty gap after a dialog closed
            // (most recently the "check for updates" result): re-centring repositions
            // every control and resets the scroll metrics, and doing that on the way
            // back from a modal is precisely when it misfired. A gentle full repaint
            // (Invalidate(true), children included) is enough to clear any double-buffer
            // artifact without disturbing layout or scroll.
            if (WindowState == FormWindowState.Minimized)
            {
                return;
            }
            try
            {
                BeginInvoke((Action)(() =>
                {
                    try
                    {
                        if (IsDisposed || _tabs == null || WindowState == FormWindowState.Minimized)
                        {
                            return;
                        }
                        TabPage page = _tabs.SelectedTab;
                        if (page != null)
                        {
                            page.Invalidate(true);
                        }
                    }
                    catch { /* best-effort repaint */ }
                }));
            }
            catch { /* handle not ready yet */ }
        }

        private void ExitApplication()
        {
            _reallyClosing = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Commit any pending "last tab" write NOW, before the window goes to the tray
            // or the process exits. Without this the debounce could still be counting down
            // when Windows reboots overnight, and the remembered tab would be lost — the
            // exact case this feature exists for.
            try
            {
                if (_lastTabSaveTimer != null && _lastTabSaveTimer.Enabled)
                {
                    _lastTabSaveTimer.Stop();
                    SaveLastTabNow();
                }
            }
            catch { }

            // Capture the window's geometry HERE, at the top, before any branch below can
            // return. It used to be recorded near the bottom of this method, which the
            // close-to-tray branch immediately below never reaches — it cancels the close
            // and returns. Combined with the tray hide leaving WindowState at Minimized,
            // that left NO route at all through this method that recorded a position for
            // anyone using "minimise to tray instead of closing": the close never got
            // here, and the later real exit arrived with a state the save refused. The
            // stored value simply stopped changing — 86,0 1020x824 on this machine, in
            // every launch for days, wherever the window was actually dragged.
            //
            // Cheap and safe to run on a cancelled close: it only updates _settings in
            // memory, and CleanUp() still performs the single write on the real exit.
            SaveWindowPosition();

            // Minimise to tray instead of closing, unless we are really exiting.
            if (!_reallyClosing && _settings.MinimizeToTrayOnClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTrayAnimated();
                if (_settings != null && !_settings.HasShownTrayIntro)
                {
                    // First time the window is closed to the tray: always explain it,
                    // even if routine notifications are off, so it isn't mistaken for
                    // the app having quit.
                    _settings.HasShownTrayIntro = true;
                    try { Persistence.SettingsManager.Save(_settings); } catch { }
                    TempoNotify(7000, "Tempo is still running",
                        "Closing the window keeps Tempo in the system tray (bottom-right, near the clock) so hotkeys keep working. Click its icon to reopen, or right-click it to Exit. You can change this in Settings.",
                        // Shown once, ever. Without it the very first close looks like the
                        // app quit — which is worse than one notification someone did not
                        // ask for, so it deliberately ignores the setting.
                        ToolTipIcon.Info, always: true);
                }
                else if (_settings != null && _settings.ShowTrayNotifications)
                {
                    TempoNotify(1500, "Tempo", "Minimised to tray.", ToolTipIcon.Info);
                }
                return;
            }

            if (_engine.IsRunning && _settings.ConfirmBeforeExitWhileRunning && !_reallyClosing)
            {
                var result = MessageBox.Show(
                    "Clicking is still running. Exit anyway?",
                    "Tempo",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.No)
                {
                    e.Cancel = true;
                    return;
                }
            }

            // ── Committed to exiting ──────────────────────────────────────────
            // Everything below tears the app down, and some of it has to block. Get the
            // window and the tray icon off the screen FIRST so that work happens behind
            // an already-gone UI: the user clicked Exit, so Tempo should look exited,
            // not sit there for a beat while its threads wind down. Purely cosmetic
            // ordering — the teardown itself is unchanged.
            _shuttingDown = true;
            _shutdownClock = System.Diagnostics.Stopwatch.StartNew();
            try { if (Visible) { Hide(); } } catch { }
            try { if (_trayIcon != null) { _trayIcon.Visible = false; } } catch { }

            // Stop a run in progress and record its session NOW, while the UI thread
            // is still pumping messages. During shutdown the engine's async
            // RunCompleted event would be dropped, losing the final session from the
            // history and lifetime totals.
            if (_engine != null && (_engine.IsRunning || _engine.IsPaused))
            {
                _engine.Stop();
                OnEngineRunCompleted();
            }

            // We're committed to closing now. Stop any active clicking or macro
            // playback cleanly while the process is still alive, so the worker thread
            // finishes its current click (releasing the mouse button) rather than being
            // killed mid-press on exit and leaving a button stuck down.
            bool wasActive = (_engine != null && _engine.IsRunning)
                             || (_player != null && _player.IsPlaying);
            try { _engine?.Stop(); } catch { }
            try { if (_player != null && _player.IsPlaying) _player.Stop(); } catch { }
            try { _cursorTrail?.End(); _cursorTrail?.Dispose(); } catch { }
            // Save the caption transcript BEFORE tearing the stack down. Exiting (tray
            // Exit or Windows shutdown) with captions running used to discard the whole
            // in-memory session because the save only ran when captions were toggled OFF.
            if (_captionsActive)
            {
                try { SaveTranscriptIfWanted(); } catch { }
            }
            // Stop the caption mirror and WAIT for any in-flight poll to finish before we
            // restore the Windows caption bar below. A plain Dispose() doesn't block for a
            // callback already running on the thread pool, so a late poll could re-hide the
            // bar AFTER RestoreWindowsBar, leaving the user's Live Captions window stuck
            // off-screen at -32000 after Tempo exits. Dispose(WaitHandle) blocks until the
            // running callback returns; the poll only uses BeginInvoke, so no UI deadlock.
            try
            {
                _captionMirrorRunning = false;
                var t = _captionMirrorTimer;
                _captionMirrorTimer = null;
                if (t != null)
                {
                    // Only pay for the blocking wait when there is actually something to
                    // protect: the stranded-bar case needs us to have parked Windows' own
                    // caption window off-screen in the first place. When we never moved it
                    // — which is every run with the caption source set to Tempo — a late
                    // poll has nothing to strand, so dispose without waiting and keep the
                    // exit instant. 500ms is plenty for a poll that only does UIA reads;
                    // the old 2000ms was a worst case nothing here reaches.
                    if (_captionReader != null && _captionReader.WindowsBarMovedOffscreen)
                    {
                        using (var done = new System.Threading.ManualResetEvent(false))
                        {
                            if (t.Dispose(done))
                            {
                                done.WaitOne(500);
                            }
                        }
                    }
                    else
                    {
                        t.Dispose();
                    }
                }
            }
            catch { }
            try { if (_clipboardListenerOn) { RemoveClipboardFormatListener(Handle); _clipboardListenerOn = false; } } catch { }
            ShutdownStep("notification mirror", () => _notifyMirror?.Dispose());
            ShutdownStep("notifications", () => _notifications?.Dispose());
            // The profile card menu is not in Controls, so nothing else would ever
            // dispose it — and its ThemedMenuRenderer owns a live animation timer.
            ShutdownStep("profile menu", () => _profileCardMenu?.Dispose());
            ShutdownStep("caption transcriber", () => _captionTranscriber?.Dispose());
            ShutdownStep("self-voice guard", () => _selfVoiceGuard?.Dispose());
            try { if (_captionHistoryForm != null) { _captionHistoryForm.Dispose(); } } catch { }
            try { _captionReader?.RestoreWindowsBar(); } catch { }
            try { _captionOverlay?.Dispose(); } catch { }
            if (wasActive)
            {
                // Safety net for hold-clicks / held playback: never exit with a button down.
                try
                {
                    InputSimulator.ButtonUp(MouseButtonType.Left);
                    InputSimulator.ButtonUp(MouseButtonType.Right);
                    InputSimulator.ButtonUp(MouseButtonType.Middle);
                }
                catch { }
            }

            // Persist any Settings-tab control changes the user made without pressing
            // "Save Settings", so a toggled checkbox isn't silently lost on close.
            // This only refreshes _settings from the live controls; CleanUp() does the
            // single on-disk write during OnFormClosed.
            try { CaptureSettingsFromUi(); } catch { /* controls may be partly torn down */ }

            // (Window geometry was captured at the TOP of this method, before the
            // close-to-tray branch that returns early — see the note there.)
            _settings.LifetimeClicks = _lifetimeBaseline + _statistics.TotalClicks;
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged; } catch { }
            try { Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged; } catch { }
            CleanUp();
            base.OnFormClosed(e);
        }

        private void SaveWindowPosition()
        {
            // Mutate settings in memory only — the single shutdown write happens
            // in CleanUp(). Saving here would be one of three redundant writes
            // during shutdown.
            if (_settings == null || !_settings.RememberWindowPosition)
            {
                return;
            }

            // Borderless full screen is STILL FormWindowState.Normal (ToggleFullScreen
            // forces Normal and only swaps the border), so a plain WindowState check
            // happily persisted the whole-screen rect as the remembered *normal* size.
            // One exit from full screen then made every later launch open a bordered,
            // screen-sized window at 0,0 with its bottom — status strip and the Macros
            // LIVE MONITOR card — clipped under the taskbar, permanently. Save the
            // geometry the window had BEFORE F11 instead.
            Rectangle rect;
            // THE ROOT CAUSE, and it hid in plain sight: this test used to read
            //     if (_isFullScreen || FormBorderStyle == FormBorderStyle.None)
            // to mean "borderless full screen". But Tempo's main window is borderless
            // ALWAYS — _customChrome sets FormBorderStyle.None at construction so the
            // header can draw its own ─ □ ✕ — so that condition was true for an ordinary
            // window sitting on the desktop. Every save took the full-screen path and bailed
            // out on the empty _fsPrevBounds, so the position was never written unless the
            // user happened to enter and leave F11 first. On this machine the stored value
            // stayed 86,0 1020x824 for days while the window was dragged all over the screen.
            //
            // _isFullScreen is the only honest test here. The rest of the file already knows
            // this — the window-state mismatch checks elsewhere guard themselves with
            // !_customChrome for exactly this reason.
            if (_isFullScreen)
            {
                // Full screen keeps its own pre-F11 rect; fall back to the tracked one
                // when that was never captured.
                rect = (_fsPrevState == FormWindowState.Normal &&
                        _fsPrevBounds.Width > 0 && _fsPrevBounds.Height > 0)
                    ? _fsPrevBounds
                    : _lastNormalBounds;
            }
            else if (WindowState == FormWindowState.Normal && Visible)
            {
                rect = Bounds;              // straightforward case: exiting a shown window
            }
            else
            {
                // Minimised, maximised, or already hidden into the tray. Bounds here are
                // not what the user arranged, and this is the ROUTINE case rather than an
                // edge one: with "minimise to tray on close" the window is always parked
                // and Minimized by the time Exit is chosen. Use the geometry sampled while
                // it was last genuinely on screen.
                rect = _lastNormalBounds;
            }

            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return;                     // never saw a normal window — keep what's saved
            }

            _settings.WindowLeft = rect.X;
            _settings.WindowTop = rect.Y;
            _settings.WindowWidth = rect.Width;
            _settings.WindowHeight = rect.Height;

            // Logged so "it didn't come back where I left it" has an answer in the log,
            // matching the [window] line the restore already writes.
            Utils.Logger.Info("[window] saving " + rect.X + "," + rect.Y + " " +
                              rect.Width + "x" + rect.Height +
                              " (state=" + WindowState + ", visible=" + Visible + ").");
        }

        private void PersistLifetimeStats()
        {
            // Recompute from the fixed baseline plus this session's in-memory total.
            // Idempotent: repeated calls do not double-count because the baseline
            // is captured once at startup and never folded back in.
            _settings.LifetimeClicks = _lifetimeBaseline + _statistics.TotalClicks;
            SettingsManager.Save(_settings);
        }

        /// <summary>
        /// Runs one teardown step, swallowing its failure the way the raw try/catch
        /// wrappers on this path always have, and logging it if it was slow enough for
        /// the user to feel. Quiet by default: only steps over the threshold say
        /// anything, so a healthy exit stays a single summary line.
        /// </summary>
        /// <summary>
        /// Times one startup phase and logs it if it was slow enough to be felt.
        ///
        /// Deliberately does NOT swallow exceptions, unlike its shutdown counterpart: a
        /// teardown step that fails costs nothing because the process is leaving anyway,
        /// but a build step that fails leaves a half-constructed window, and that belongs
        /// in the crash handler rather than quietly in a log line.
        /// </summary>
        private void StartupStep(string name, Action step)
        {
            if (step == null) { return; }
            long before = _startupClock?.ElapsedMilliseconds ?? 0;

            // Build with layout suspended. Each tab adds hundreds of controls, and every
            // single Controls.Add otherwise triggers its own layout pass over everything
            // already in the container — the same work repeated hundreds of times while
            // the user waits for the window. Suspending once around the whole step and
            // resuming after collapses that into one pass.
            //
            // ResumeLayout(false) on purpose: the layout that matters is performed when
            // the form is shown, and every control here is positioned by explicit
            // coordinates rather than by a layout engine, so forcing an immediate pass
            // here would only repeat work the show is going to do anyway.
            SuspendLayout();
            try
            {
                step();
            }
            finally
            {
                // In a finally so a throwing build step can't leave the form permanently
                // suspended (which would render it unable to lay out at all). The
                // exception still propagates to the crash handler, as before.
                ResumeLayout(false);
            }

            long took = (_startupClock?.ElapsedMilliseconds ?? 0) - before;
            if (took >= 40)
            {
                Logger.Info("[startup] " + name + " took " + took + " ms.");
            }
        }

        private void ShutdownStep(string name, Action step)
        {
            if (step == null) { return; }
            long before = _shutdownClock?.ElapsedMilliseconds ?? 0;
            try
            {
                step();
            }
            catch (Exception ex)
            {
                Logger.Warn("[shutdown] " + name + " failed: " + ex.Message);
            }
            long took = (_shutdownClock?.ElapsedMilliseconds ?? 0) - before;
            if (took >= 40)
            {
                Logger.Info("[shutdown] " + name + " took " + took + " ms.");
            }
        }

        private void CleanUp()
        {
            try
            {
                _uiTimer?.Stop();
                _uiTimer?.Dispose();
                _compositeOffTimer?.Stop();
                _compositeOffTimer?.Dispose();
                _holdPollTimer?.Stop();
                _holdPollTimer?.Dispose();
                _tips?.Dispose();
                // Before anything else on shutdown: release W/A/S/D. If Tempo exits
                // while the movement engine holds a key down, nothing is left to
                // release it — the character would run into the horizon and the user
                // would have no idea why. Dispose() joins the loop and lifts the keys.
                ShutdownStep("movement engine", () => _movement?.Dispose());
                _movement = null;
                ShutdownStep("media detector", () => _mediaDetector?.Dispose());
                ShutdownStep("audio watcher", () => _audioWatcher?.Dispose());
                ShutdownStep("game presence", () => _gamePresence?.Dispose());
                ShutdownStep("second cursor", () => _secondCursor?.Dispose());
                try { _miceRefreshTimer?.Dispose(); } catch { }
                // The "last tab" debounce. The index is already in _settings, and the
                // single shutdown write below persists it, so this only has to release
                // the timer rather than fire one more save.
                try
                {
                    _lastTabSaveTimer?.Stop();
                    _lastTabSaveTimer?.Dispose();
                    _lastTabSaveTimer = null;
                }
                catch { }
                // A screenshot card still waiting to merge, plus any capture-app icon we
                // took ownership of when swallowing its notification.
                try
                {
                    _clipMergeTimer?.Stop();
                    _clipMergeTimer?.Dispose();
                    _clipMergeTimer = null;
                    _pendingClipThumb?.Dispose();
                    _pendingClipThumb = null;
                    _shotIcon?.Dispose();
                    _shotIcon = null;
                }
                catch { }
                try { Utils.Logger.LineLogged -= OnLoggerLineForNotify; } catch { }
                ShutdownStep("voice profiler", () => _voiceProfiler?.Dispose());
                ShutdownStep("face analyzer", () => _faceAnalyzer?.Dispose());
                ShutdownStep("word fixer", () => _wordFixer?.Dispose());

                HideRecordingIndicator();

                try
                {
                    if (_clickingIndicator != null && !_clickingIndicator.IsDisposed)
                    {
                        _clickingIndicator.Close();
                    }
                }
                catch { }
                _clickingIndicator = null;

                try
                {
                    if (_macroIndicator != null && !_macroIndicator.IsDisposed)
                    {
                        _macroIndicator.Close();
                    }
                }
                catch { }
                _macroIndicator = null;

                ShutdownStep("click engine", () => _engine?.Dispose());
                ShutdownStep("macro player", () => _player?.Dispose());
                ShutdownStep("macro recorder", () => _recorder?.Dispose());
                ShutdownStep("hotkeys", () => _hotkeys?.Dispose());

                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                }

                ShutdownStep("save profiles", () => _profiles.Save());
                ShutdownStep("save macros", () => _macros.Save());
                ShutdownStep("save settings", () => SettingsManager.Save(_settings));
            }
            catch (Exception ex)
            {
                Logger.Error("Error during cleanup.", ex);
            }
            finally
            {
                if (_shutdownClock != null)
                {
                    _shutdownClock.Stop();
                    Logger.Info("[shutdown] teardown finished in " +
                                _shutdownClock.ElapsedMilliseconds + " ms.");
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Small shared helpers
        // ─────────────────────────────────────────────────────────────────────

        private void UiInvoke(Action action)
        {
            if (action == null || IsDisposed)
            {
                return;
            }

            try
            {
                if (InvokeRequired)
                {
                    // Only marshal once the handle exists. A cross-thread event that
                    // somehow arrives before the window is created is simply dropped,
                    // which is safe because there is no UI yet to update.
                    if (IsHandleCreated)
                    {
                        BeginInvoke(action);
                    }
                }
                else
                {
                    action();
                }
            }
            catch (ObjectDisposedException)
            {
                // The form is gone; ignore.
            }
            catch (InvalidOperationException)
            {
                // Handle not created / being destroyed; ignore.
            }
        }

        // These two translate their message, and that is load-bearing.
        //
        // They used to hand the string straight to MessageBox. Because they did NOT
        // translate, they were invisible to the choke-point audit — that scan looks for
        // methods which call Localization.T on a parameter, so a helper that translates
        // nothing is exactly the one it cannot flag. Thirty messages reached the user in
        // English in every language: "Select a macro to play first.", "Settings
        // exported.", "Statistics exported." and the rest.
        //
        // Translating here rather than at thirty call sites fixes them together AND
        // makes the pair a real choke point, so the existing audit now covers every
        // future call as well. Passing an already-translated string back through T() is
        // a no-op — a miss returns its input — so the sites that already localise are
        // unaffected.

        private void ShowWarning(string message)
        {
            MessageBox.Show(this, Localization.T(message), "Tempo",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void ShowInfo(string message)
        {
            MessageBox.Show(this, Localization.T(message), "Tempo",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
