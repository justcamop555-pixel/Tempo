using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Native;
using AutoClicker.UI;
using AutoClicker.Utils;

namespace AutoClicker.Engine
{
    /// <summary>
    /// Screen point where the user asked for the second cursor's menu.</summary>
    public sealed class SecondCursorMenuEventArgs : EventArgs
    {
        public int X { get; }
        public int Y { get; }
        public SecondCursorMenuEventArgs(int x, int y) { X = x; Y = y; }
    }

    /// <summary>
    /// Drives the "second mouse": a visible second cursor (see
    /// <see cref="SecondCursorOverlay"/>) the user interacts with DIRECTLY.
    ///
    ///  • Right-click the second cursor → a menu (Grab &amp; place, Spam-click,
    ///    Change colour, Change size). The menu itself is built by the main form
    ///    (themed); this controller just detects the right-click on the cursor and
    ///    raises <see cref="MenuRequested"/>.
    ///  • "Grab &amp; place" → the next left-click ANYWHERE (either monitor) drops the
    ///    second cursor there. While placing, it follows the mouse so you can see
    ///    where it will land. One simple click to place — no hotkeys needed.
    ///  • Spam-click → auto-clicks at the parked spot (posted to the window under it,
    ///    so your real mouse stays free).
    ///
    /// A single PASSIVE low-level mouse hook (never blocks input) watches for the
    /// right-click-on-cursor and the place-click. The overlay is click-through, so it
    /// never blocks anything and the spam clicks reach the window beneath it.
    /// </summary>
    public sealed class SecondCursorController : IDisposable
    {
        private readonly SecondCursorOverlay _overlay = new SecondCursorOverlay();
        private readonly System.Windows.Forms.Timer _followTimer;
        private LowLevelMouseHook _hook;

        private volatile bool _enabled;
        private volatile bool _placing;
        private volatile int _x = 400;
        private volatile int _y = 400;
        private int _markerRadius = 26;   // how close a click must be to count as "on" the cursor

        private Thread _spamThread;
        private volatile bool _spamming;
        private MouseButtonType _spamButton = MouseButtonType.Left;
        private ClickStyle _spamStyle = ClickStyle.Single;
        private volatile int _spamIntervalMs = 100;

        // ── second PHYSICAL mouse (optional) ─────────────────────────────────
        // When the user plugs in a second real mouse, it can drive this cursor
        // directly while the main mouse keeps the normal Windows cursor. We tell the
        // mice apart by their Raw-Input device handle. See SecondMouseListener.
        private SecondMouseListener _mouseListener;
        private volatile bool _usePhysicalMouse;
        private IntPtr _secondMouseHandle = IntPtr.Zero;
        private volatile bool _assigning;
        private int _assignDeadlineTick;
        private int _lastMainX, _lastMainY;          // where the MAIN mouse parked the system cursor
        private IntPtr _lastInputDevice = IntPtr.Zero;
        private int _lastInputTick;
        private volatile int _suppressMainUntilTick;   // ignore "main mouse" updates right after our own click
        private IntPtr _lastMovedDevice = IntPtr.Zero;  // which mouse physically MOVED most recently
        private volatile int _lastMovedTick;
        // While the second mouse has moved within this window, the low-level hook BLOCKS
        // cursor movement so the real pointer stays perfectly still (no snap-back jitter).
        // It auto-releases this long after the second mouse stops, so the main mouse is
        // never left locked.
        private const int CursorLockMs = 260;
        private volatile int _secondMouseSensPercent = 100;   // 100 = 1 raw count : 1 px
        private string _secondMouseName = "";                 // friendly (product) name of the bound mouse
        private string _secondMouseDeviceName = "";           // raw device path of the bound mouse
        private string _desiredDeviceName = "";               // the mouse the user chose (persisted by the UI)
        private long _secondMouseMoves;              // Interlocked — Live-debug counters
        private long _secondMouseClicks;
        private volatile int _lastSecondActivityTick;
        private System.Windows.Forms.Timer _deviceWatch;      // notices the 2nd mouse being plugged/unplugged

        /// <summary>Raised (UI thread) when the set of connected mice, or the binding, changes — the UI refreshes its list.</summary>
        public event EventHandler MiceChanged;
        /// <summary>Raised (UI thread) when a mouse becomes bound — the UI persists which one.</summary>
        public event EventHandler SecondMouseBoundChanged;
        // During "wiggle to assign", movement is summed PER DEVICE; the first device to
        // cross this threshold wins — so a deliberate wiggle beats an accidental nudge of
        // the other mouse (much more reliable than "first device to twitch").
        private readonly Dictionary<IntPtr, int> _assignAccum = new Dictionary<IntPtr, int>();
        private const int AssignThresholdCounts = 40;

        // Per-device live activity, for the "both mice are being read" Live-debug readout.
        // AbsMoves counts reports that carried a screen COORDINATE rather than a delta.
        // A device with AbsMoves rising and Moves stuck at zero is a tablet / touchscreen /
        // remote-desktop pointer: Windows lists it as a mouse, so it arms the mode and
        // shows up in the picker, but it can never drive the second cursor.
        private sealed class DevStat { public int LastTick; public long Moves; public long Clicks; public long AbsMoves; }
        private readonly Dictionary<IntPtr, DevStat> _devStats = new Dictionary<IntPtr, DevStat>();
        // Per-button (L=0, R=1, M=2, wheel=3) tick of the last time the SECOND mouse
        // pressed/released that button (or rolled its wheel) — lets the low-level hook
        // swallow the matching stray system event even when the raw event and the hook
        // fire in an unpredictable order.
        private readonly int[] _secondBtnTick = new int[4];
        // Which second-mouse buttons are currently held down (bit 0 = left, 1 = right), so
        // hold-to-drag works: the real button stays down and movement drags until release.
        private volatile int _secondHeldButtons;
        private const int HeldLeft = 1, HeldRight = 2;

        /// <summary>Raised (UI thread) when the user right-clicks the second cursor.</summary>
        public event EventHandler<SecondCursorMenuEventArgs> MenuRequested;

        public bool Enabled => _enabled;
        public bool Placing => _placing;
        public bool Spamming => _spamming;
        public int X => _x;
        public int Y => _y;

        /// <summary>True while waiting for the user to wiggle the mouse they want as the 2nd mouse.</summary>
        public bool Assigning => _assigning;
        /// <summary>True once a second physical mouse is bound to this cursor.</summary>
        public bool SecondMouseBound => _usePhysicalMouse && _secondMouseHandle != IntPtr.Zero;
        /// <summary>True when "a 2nd real mouse drives this cursor" is switched on (bound or not).</summary>
        public bool UsePhysicalMouse => _usePhysicalMouse;
        /// <summary>Friendly name (product / VID-PID) of the bound second mouse, or "".</summary>
        public string SecondMouseName => _secondMouseName ?? "";
        /// <summary>Raw device path of the bound second mouse, or "" — persisted so it re-binds after a reconnect.</summary>
        public string SecondMouseDeviceName => _secondMouseDeviceName ?? "";
        /// <summary>Second-mouse movement sensitivity, percent (100 = 1:1).</summary>
        public int SecondMouseSensitivityPercent => _secondMouseSensPercent;
        /// <summary>Total moves / clicks routed from the second mouse this session (Live debug).</summary>
        public long SecondMouseMoveCount => Interlocked.Read(ref _secondMouseMoves);
        public long SecondMouseClickCount => Interlocked.Read(ref _secondMouseClicks);
        /// <summary>Milliseconds since the bound second mouse last did anything, or -1 if never.</summary>
        public int SecondMouseIdleMs => _lastSecondActivityTick == 0
            ? -1 : unchecked(Environment.TickCount - _lastSecondActivityTick);
        /// <summary>True while a second-mouse button is held down (a click/hold/drag is in progress).</summary>
        public bool SecondMouseButtonHeld => _secondHeldButtons != 0;
        /// <summary>Which second-mouse buttons are held right now — "L", "R", "L+R" or "" (Live debug).</summary>
        public string HeldButtonsText
        {
            get
            {
                int h = _secondHeldButtons;
                if (h == (HeldLeft | HeldRight)) { return "L+R"; }
                if ((h & HeldLeft) != 0) { return "L"; }
                if ((h & HeldRight) != 0) { return "R"; }
                return "";
            }
        }
        /// <summary>The mouse the user chose (raw path), even before it's connected/bound — for Live debug.</summary>
        public string PreferredDeviceName => _desiredDeviceName ?? "";
        /// <summary>Where the MAIN pointer is parked while the 2nd mouse drives (Live debug).</summary>
        public int ParkedX => _lastMainX;
        public int ParkedY => _lastMainY;
        /// <summary>How many real mice Windows currently reports (cached-free; call sparingly).</summary>
        public static int DetectedMouseCount()
        {
            try { return Native.SecondMouseListener.EnumerateRealMice().Count; }
            catch { return 0; }
        }
        /// <summary>Short "2 mice: Mouse VID.../…" summary for the UI / Live debug.</summary>
        public static string DetectedMouseSummary()
        {
            try
            {
                var mice = Native.SecondMouseListener.EnumerateRealMice();
                if (mice.Count == 0) { return "no mice detected"; }
                var names = new System.Text.StringBuilder();
                for (int i = 0; i < mice.Count && i < 4; i++)
                {
                    if (i > 0) { names.Append(", "); }
                    names.Append(mice[i].FriendlyName);
                }
                return mice.Count + (mice.Count == 1 ? " mouse" : " mice") + ": " + names;
            }
            catch { return "unknown"; }
        }

        /// <summary>
        /// One line per connected mouse for Live debug: which one drives the second
        /// cursor, and each one's live movement/click activity — so you can wiggle each
        /// mouse and confirm Tempo is reading BOTH real mice independently.
        /// </summary>
        public string DevicesDebug()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                var mice = Native.SecondMouseListener.EnumerateRealMice();
                int now = Environment.TickCount;
                foreach (var m in mice)
                {
                    bool bound = m.Handle == _secondMouseHandle && _secondMouseHandle != IntPtr.Zero;
                    _devStats.TryGetValue(m.Handle, out DevStat st);
                    int idle = (st != null && st.LastTick != 0) ? unchecked(now - st.LastTick) : -1;
                    bool active = idle >= 0 && idle < 700;
                    sb.Append("    ").Append(bound ? "▶ " : "   ").Append(m.FriendlyName)
                      .Append(bound ? "  → SECOND cursor" : "  → main pointer");
                    if (st != null)
                    {
                        sb.Append("  · moves ").Append(st.Moves).Append(", clicks ").Append(st.Clicks);
                        if (st.AbsMoves > 0)
                        {
                            sb.Append(", absolute ").Append(st.AbsMoves);
                            if (st.Moves == 0) { sb.Append("  ⚠ ABSOLUTE-ONLY — cannot drive the 2nd cursor"); }
                        }
                        sb.Append(active ? "  ● MOVING NOW" : (idle >= 0 ? "  (" + idle + "ms ago)" : ""));
                    }
                    else
                    {
                        sb.Append("  · (no input read yet — wiggle it)");
                    }
                    sb.AppendLine();
                }
                return sb.ToString();
            }
            catch { return ""; }
        }

        public SecondCursorController()
        {
            _followTimer = new System.Windows.Forms.Timer { Interval = 15 };
            _followTimer.Tick += FollowTick;
            // Keep the marker visible: other apps (games, launchers) assert topmost
            // AFTER the overlay and bury it — reported as "the second cursor vanished".
            // A gentle 2 s re-assert keeps it above them without the z-order churn that
            // would bother a fullscreen game's presentation.
            _topKeeper = new System.Windows.Forms.Timer { Interval = 2000 };
            _topKeeper.Tick += (s, e) => { if (_enabled) { _overlay.KeepOnTop(); } };
        }

        private readonly System.Windows.Forms.Timer _topKeeper;

        // ── enable / appearance ───────────────────────────────────────────────

        public void SetEnabled(bool on)
        {
            _enabled = on;
            if (on)
            {
                _overlay.SetHotspot(_x, _y);
                if (!_overlay.Visible) { _overlay.Show(); }
                _overlay.KeepOnTop();
                _topKeeper.Start();
                StartHook();
            }
            else
            {
                _topKeeper.Stop();
                CancelPlacement();
                StopSpam();
                StopHook();
                // Also release any bound second physical mouse (its snap-back would be
                // pointless with the cursor hidden).
                ReleaseAllHeldButtons();
                _usePhysicalMouse = false;
                _assigning = false;
                _secondMouseHandle = IntPtr.Zero;
                _secondMouseName = "";
                _secondMouseDeviceName = "";
                StopListener();
                if (_overlay.Visible) { _overlay.Hide(); }
            }
        }

        public void SetAppearance(SecondCursorShape shape, Color color, int scalePercent)
        {
            _overlay.SetAppearance(shape, color, scalePercent);
            _markerRadius = 18 + scalePercent / 6;   // bigger cursor → bigger click target
            _overlay.SetHotspot(_x, _y);
        }

        public void SetSpamSettings(MouseButtonType button, ClickStyle style, int intervalMs)
        {
            _spamButton = button;
            _spamStyle = style;
            _spamIntervalMs = Math.Max(1, intervalMs);
        }

        public void SetPosition(int x, int y)
        {
            _x = x;
            _y = y;
            if (_enabled) { _overlay.SetHotspot(x, y); }
        }

        // ── second PHYSICAL mouse: bind a real 2nd mouse to this cursor ────────

        /// <summary>
        /// Turns on/off "a second real mouse drives this cursor". Needs the cursor to be
        /// showing AND at least two mice connected (otherwise the machine would have no
        /// usable pointer). On enable, we listen and the mouse the user WIGGLES becomes
        /// the second mouse.
        /// </summary>
        public void SetUsePhysicalMouse(bool on)
        {
            if (on)
            {
                if (!_enabled)
                {
                    _usePhysicalMouse = false;
                    return;
                }
                int mice = DetectedMouseCount();
                if (mice < 2)
                {
                    // Don't hard-fail: let the mode arm and wait — the moment a 2nd mouse
                    // is plugged in, the device-change watch binds it. (Nothing snaps the
                    // cursor until something is actually bound.)
                    _usePhysicalMouse = true;
                    EnsureListener();
                    Logger.Warn("[2nd mouse] only " + mice + " mouse detected — waiting for a 2nd one to be plugged in.");
                    RaiseMiceChanged();
                    return;
                }
                _usePhysicalMouse = true;
                EnsureListener();
                // Already bound to the chosen mouse? Leave it alone — every settings
                // change funnels through here, and re-binding each time spammed the log
                // and reset the parked-spot bookkeeping mid-use.
                if (_secondMouseHandle != IntPtr.Zero &&
                    (_desiredDeviceName.Length == 0 ||
                     string.Equals(_secondMouseDeviceName, _desiredDeviceName, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }
                // If the user already chose a specific mouse (or one was remembered) and
                // it's connected, bind it straight away — no wiggle needed. Otherwise fall
                // back to "wiggle the one you want".
                if (!TryBindByName(_desiredDeviceName))
                {
                    BeginAssign();
                }
            }
            else
            {
                ReleaseAllHeldButtons();
                _usePhysicalMouse = false;
                _assigning = false;
                _secondMouseHandle = IntPtr.Zero;
                _secondMouseName = "";
                _secondMouseDeviceName = "";
                StopListener();
                if (_enabled) { _overlay.SetActiveLook(_placing || _spamming); }
                RaiseMiceChanged();
            }
        }

        /// <summary>Movement gain for the second mouse (percent; 100 = 1 raw count : 1 px).</summary>
        public void SetSecondMouseSensitivity(int percent)
        {
            _secondMouseSensPercent = Math.Max(10, Math.Min(400, percent));
        }

        /// <summary>
        /// The mouse the user picked to drive the second cursor (its raw device path).
        /// Pushed in from the saved settings and from the on-screen picker; if that mouse
        /// is connected we bind it immediately, otherwise we remember it and bind it as
        /// soon as it appears.
        /// </summary>
        public void SetPreferredDevice(string rawDeviceName)
        {
            _desiredDeviceName = rawDeviceName ?? "";
            if (!_usePhysicalMouse || !_enabled)
            {
                return;
            }
            EnsureListener();
            if (_desiredDeviceName.Length == 0)
            {
                return;
            }
            // No-op when that exact mouse is already the bound one (settings re-apply).
            if (_secondMouseHandle != IntPtr.Zero &&
                string.Equals(_secondMouseDeviceName, _desiredDeviceName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            if (!TryBindByName(_desiredDeviceName))
            {
                // Chosen mouse isn't here yet — wait for the device-change watch to bind it.
                Logger.Info("[2nd mouse] chosen mouse isn't connected yet — it will bind when plugged in.");
            }
        }

        /// <summary>Stop the second-mouse binding at once (panic key) — hands the machine fully back.</summary>
        public void PanicReleaseSecondMouse()
        {
            if (!_usePhysicalMouse && _secondMouseHandle == IntPtr.Zero) { return; }
            ReleaseAllHeldButtons();
            _usePhysicalMouse = false;
            _assigning = false;
            _secondMouseHandle = IntPtr.Zero;
            _secondMouseName = "";
            _secondMouseDeviceName = "";
            StopListener();
            Logger.Info("[2nd mouse] released by Emergency-Stop — the second mouse no longer moves the cursor.");
        }

        /// <summary>Re-run the wiggle-to-assign step (e.g. the wrong mouse got picked).</summary>
        public void RepickSecondMouse()
        {
            if (!_usePhysicalMouse || !_enabled) { return; }
            EnsureListener();
            BeginAssign();
        }

        private void EnsureListener()
        {
            if (_mouseListener == null)
            {
                _mouseListener = new SecondMouseListener();
                _mouseListener.Input += OnRawDeviceInput;
                _mouseListener.DevicesChanged += OnDevicesChanged;
            }
            _mouseListener.Start();
            if (_deviceWatch == null)
            {
                _deviceWatch = new System.Windows.Forms.Timer { Interval = 1500 };
                _deviceWatch.Tick += DeviceWatchTick;
            }
            _deviceWatch.Start();
        }

        private void StopListener()
        {
            try { _deviceWatch?.Stop(); } catch { }
            if (_mouseListener == null)
            {
                return;
            }
            try
            {
                _mouseListener.Input -= OnRawDeviceInput;
                _mouseListener.DevicesChanged -= OnDevicesChanged;
                _mouseListener.Dispose();
            }
            catch { }
            _mouseListener = null;
        }

        private void BeginAssign()
        {
            // Re-picking while a second-mouse button is held: that button's release
            // event can no longer route back to us (the binding is being cleared), so
            // release anything held NOW or the real button would stay stuck down.
            ReleaseAllHeldButtons();
            _secondMouseHandle = IntPtr.Zero;
            _secondMouseName = "";
            _secondMouseDeviceName = "";
            _assignAccum.Clear();
            _assigning = true;
            _assignDeadlineTick = Environment.TickCount + 12000;
            if (NativeMethods.GetCursorPos(out NativeMethods.POINT p)) { _lastMainX = p.X; _lastMainY = p.Y; }
            if (_enabled) { _overlay.SetActiveLook(true); }
            RaiseMiceChanged();
            Logger.Info("[2nd mouse] move the mouse you want to use as the SECOND mouse now…");
        }

        /// <summary>Bind the mouse with this raw device path if it's currently connected.</summary>
        private bool TryBindByName(string rawDeviceName)
        {
            if (string.IsNullOrEmpty(rawDeviceName))
            {
                return false;
            }
            var info = FindMouse(rawDeviceName);
            if (info == null)
            {
                return false;
            }
            BindSecondMouse(info);
            return true;
        }

        private static MouseDeviceInfo FindMouse(string rawDeviceName)
        {
            try
            {
                foreach (var m in SecondMouseListener.EnumerateRealMice())
                {
                    if (string.Equals(m.DeviceName, rawDeviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        return m;
                    }
                }
            }
            catch { }
            return null;
        }

        private static MouseDeviceInfo FindMouse(IntPtr hDevice)
        {
            try
            {
                foreach (var m in SecondMouseListener.EnumerateRealMice())
                {
                    if (m.Handle == hDevice) { return m; }
                }
            }
            catch { }
            return null;
        }

        private void BindSecondMouse(IntPtr hDevice)
        {
            BindSecondMouse(FindMouse(hDevice) ?? new MouseDeviceInfo(hDevice, "", ""));
        }

        private void BindSecondMouse(MouseDeviceInfo info)
        {
            // Switching to a DIFFERENT mouse mid-hold: the old mouse's release would
            // now be routed as "main mouse" and never lift the real button — release
            // everything held before the handle changes.
            if (_secondMouseHandle != IntPtr.Zero && _secondMouseHandle != info.Handle)
            {
                ReleaseAllHeldButtons();
            }
            _secondMouseHandle = info.Handle;
            _assigning = false;
            _assignAccum.Clear();
            _secondMouseName = info.FriendlyName ?? "";
            _secondMouseDeviceName = info.DeviceName ?? "";
            _desiredDeviceName = _secondMouseDeviceName;   // remember this choice
            if (NativeMethods.GetCursorPos(out NativeMethods.POINT mp)) { _lastMainX = mp.X; _lastMainY = mp.Y; }
            if (_enabled) { _overlay.SetActiveLook(_spamming); }
            Logger.Info("[2nd mouse] bound " + (_secondMouseName.Length > 0 ? _secondMouseName : "a mouse")
                + " — it now drives the second cursor.");
            try { SecondMouseBoundChanged?.Invoke(this, EventArgs.Empty); } catch { }
            RaiseMiceChanged();
        }

        private void RaiseMiceChanged()
        {
            try { MiceChanged?.Invoke(this, EventArgs.Empty); } catch { }
        }

        // Devices already called out as absolute-only, so the log gets one line per device
        // rather than one per report (a tablet streams these continuously while in range).
        private readonly HashSet<IntPtr> _absoluteWarned = new HashSet<IntPtr>();

        /// <summary>
        /// Says, once per device, that a "mouse" is sending screen coordinates instead of
        /// deltas — so it can never move the second cursor no matter how much it is
        /// wiggled. Before this the device simply produced nothing: the wiggle step would
        /// time out with no explanation, and it still counted towards the two-mice check
        /// that arms the mode.
        /// </summary>
        private void WarnAbsoluteOnce(IntPtr hDevice, DevStat st)
        {
            if (st.Moves != 0 || st.AbsMoves < 8) { return; }   // give it a moment to prove otherwise
            if (!_absoluteWarned.Add(hDevice)) { return; }
            string name = "a pointing device";
            try
            {
                var info = FindMouse(hDevice);
                if (info != null && !string.IsNullOrEmpty(info.FriendlyName)) { name = info.FriendlyName; }
            }
            catch { }
            Logger.Warn("[2nd mouse] " + name + " reports absolute screen positions (a drawing tablet, "
                + "touchscreen or remote-desktop pointer), not movement deltas, so it cannot drive the "
                + "second cursor. Use a mouse that reports relative movement.");
        }

        /// <summary>Instant notification that a mouse was plugged in / unplugged.</summary>
        private void OnDevicesChanged()
        {
            ReconcileSecondMouse();
            RaiseMiceChanged();
        }

        /// <summary>Slower backstop poll in case a device-change message is missed.</summary>
        private void DeviceWatchTick(object sender, EventArgs e)
        {
            // Auto-cancel a stuck "wiggle to assign" even if NO mouse ever moves — the raw
            // handler only checks the deadline when input arrives, so without this the
            // overlay could sit in its pulsing "assign" look forever.
            if (_assigning && unchecked(Environment.TickCount - _assignDeadlineTick) > 0)
            {
                _assigning = false;
                _assignAccum.Clear();
                if (_enabled) { _overlay.SetActiveLook(_placing || _spamming); }
                Logger.Info("[2nd mouse] second-mouse assignment timed out (no movement).");
                RaiseMiceChanged();
            }
            PruneDeviceStats();
            ReconcileSecondMouse();
        }

        /// <summary>Drops per-device activity entries for mice that are no longer connected,
        /// so the dictionary can't grow without bound across reconnects.</summary>
        private void PruneDeviceStats()
        {
            if (_devStats.Count <= 8) { return; }
            try
            {
                var connected = new HashSet<IntPtr>();
                foreach (var m in SecondMouseListener.EnumerateRealMice()) { connected.Add(m.Handle); }
                var stale = new List<IntPtr>();
                foreach (var k in _devStats.Keys) { if (!connected.Contains(k)) { stale.Add(k); } }
                foreach (var k in stale) { _devStats.Remove(k); }
            }
            catch { }
        }

        /// <summary>
        /// Keeps the binding in sync with what's actually plugged in: drops a mouse that
        /// vanished, and (re)binds the user's chosen mouse the moment it appears.
        /// </summary>
        private void ReconcileSecondMouse()
        {
            if (!_usePhysicalMouse || !_enabled)
            {
                return;
            }
            try
            {
                var mice = SecondMouseListener.EnumerateRealMice();

                // 1) Is the currently-bound mouse still present?
                if (_secondMouseHandle != IntPtr.Zero)
                {
                    bool present = false;
                    foreach (var m in mice) { if (m.Handle == _secondMouseHandle) { present = true; break; } }
                    if (present)
                    {
                        return;
                    }
                    // Unplugged mid-hold? Release any held button so it can't stick down
                    // (the UP report can never come from a mouse that's gone).
                    ReleaseAllHeldButtons();
                    _secondMouseHandle = IntPtr.Zero;
                    _secondMouseName = "";
                    Logger.Info("[2nd mouse] the bound mouse was unplugged.");
                    RaiseMiceChanged();
                }

                if (_assigning)
                {
                    return;
                }

                // 2) (Re)bind the chosen mouse as soon as it's connected.
                if (_desiredDeviceName.Length > 0 && TryBindByName(_desiredDeviceName))
                {
                    return;
                }

                // 3) No choice yet but two mice are present → ask the user to wiggle one.
                if (_desiredDeviceName.Length == 0 && mice.Count >= 2)
                {
                    BeginAssign();
                }
            }
            catch { }
        }

        /// <summary>
        /// Every raw mouse report, tagged with WHICH physical mouse produced it. The
        /// second mouse drives this cursor (move + left-click + wheel + right-click-to-
        /// spam) while the main mouse keeps the real Windows cursor — the two are told
        /// apart purely by the device handle.
        /// </summary>
        private void OnRawDeviceInput(IntPtr hDevice, int dx, int dy, ushort buttons, int wheel, bool absolute)
        {
            if (!_usePhysicalMouse || !_enabled)
            {
                return;
            }
            // Ignore synthetic/injected events. Our own real clicks go out via SendInput,
            // which Raw Input reports with a NULL device handle; if we treated those as
            // the "main mouse" we'd record the click spot as the parked spot and then keep
            // snapping the real cursor onto the second cursor. Dropping them keeps the
            // parked spot honest so the real pointer stays put while you move the 2nd mouse.
            if (hDevice == IntPtr.Zero)
            {
                return;
            }
            int now = Environment.TickCount;

            // Record activity for EVERY real mouse (bound or not) so Live debug can show
            // that both mice are genuinely being read and which one drives the cursor.
            if (!_devStats.TryGetValue(hDevice, out DevStat st)) { st = new DevStat(); _devStats[hDevice] = st; }
            if (dx != 0 || dy != 0)
            {
                st.Moves++; st.LastTick = now;
                _lastMovedDevice = hDevice; _lastMovedTick = now;
            }
            else if (absolute)
            {
                // Counted but never treated as movement — see DevStat.
                st.AbsMoves++; st.LastTick = now;
                WarnAbsoluteOnce(hDevice, st);
            }
            if ((buttons & 0x03FF) != 0) { st.Clicks++; st.LastTick = now; }

            if (_assigning)
            {
                // Sum movement per device; the first to cross the threshold wins, so a
                // deliberate wiggle beats a stray nudge of the other mouse.
                if (dx != 0 || dy != 0)
                {
                    _assignAccum.TryGetValue(hDevice, out int acc);
                    acc += Math.Abs(dx) + Math.Abs(dy);
                    _assignAccum[hDevice] = acc;
                    if (acc >= AssignThresholdCounts)
                    {
                        BindSecondMouse(hDevice);
                    }
                }
                // unchecked subtraction, not a direct >: Environment.TickCount is a signed
                // int that wraps, and every other deadline in this file (including the
                // backstop for THIS one in DeviceWatchTick) already compares that way.
                if (_assigning && unchecked(now - _assignDeadlineTick) > 0)
                {
                    _assigning = false;
                    _assignAccum.Clear();
                    _overlay.SetActiveLook(_spamming);
                    Logger.Info("[2nd mouse] no clear movement seen — second-mouse assignment timed out.");
                }
                return;
            }

            if (_secondMouseHandle == IntPtr.Zero)
            {
                return;
            }

            // Remember the most-recent device: the low-level hook can't tell mice apart,
            // so it swallows the second mouse's stray click by trusting this.
            _lastInputDevice = hDevice;
            _lastInputTick = now;

            if (hDevice != _secondMouseHandle)
            {
                // Main mouse moved/clicked — record where it left the real cursor so a
                // following second-mouse move can snap the real cursor straight back.
                // (Skip right after our own click, while the cursor is briefly warped to
                // the second cursor, so we never adopt that as the parked spot.)
                if (unchecked(now - _suppressMainUntilTick) >= 0
                    && NativeMethods.GetCursorPos(out NativeMethods.POINT mp))
                {
                    _lastMainX = mp.X;
                    _lastMainY = mp.Y;
                }
                return;
            }

            // ── the SECOND mouse ──
            // While grab & place is active the MAIN mouse owns the second cursor (the
            // follow timer tracks it). Second-mouse input during placement would fight
            // that follow for _x/_y and could fire real clicks mid-aim — ignore it
            // until the drop. (Its stray system clicks still get swallowed by the hook.)
            if (_placing)
            {
                return;
            }
            bool activity = false;

            // Remember which of the second mouse's buttons just fired (down OR up) so the
            // low-level hook can eat the matching stray system click. (Do this BEFORE the
            // movement so a same-report press+move is attributed correctly.)
            if ((buttons & (SecondMouseListener.RI_MOUSE_LEFT_BUTTON_DOWN | SecondMouseListener.RI_MOUSE_LEFT_BUTTON_UP)) != 0) { _secondBtnTick[0] = now; }
            if ((buttons & (SecondMouseListener.RI_MOUSE_RIGHT_BUTTON_DOWN | SecondMouseListener.RI_MOUSE_RIGHT_BUTTON_UP)) != 0) { _secondBtnTick[1] = now; }
            if ((buttons & (SecondMouseListener.RI_MOUSE_MIDDLE_BUTTON_DOWN | SecondMouseListener.RI_MOUSE_MIDDLE_BUTTON_UP)) != 0) { _secondBtnTick[2] = now; }
            if (wheel != 0) { _secondBtnTick[3] = now; }

            // Press-and-HOLD, so click, hold and drag all work. Left/Right press the real
            // button at the second cursor and keep it down until you release it (below).
            // Middle = spam toggle. Do the press BEFORE handling this report's movement so
            // a press-then-drag in one report drags with the button already down.
            if ((buttons & SecondMouseListener.RI_MOUSE_LEFT_BUTTON_DOWN) != 0) { PressSecondButton(MouseButtonType.Left, HeldLeft); activity = true; }
            if ((buttons & SecondMouseListener.RI_MOUSE_RIGHT_BUTTON_DOWN) != 0) { PressSecondButton(MouseButtonType.Right, HeldRight); activity = true; }
            if ((buttons & SecondMouseListener.RI_MOUSE_MIDDLE_BUTTON_DOWN) != 0) { ToggleSpam(); activity = true; }

            if (dx != 0 || dy != 0)
            {
                double sens = _secondMouseSensPercent / 100.0;
                int nx = _x + (int)Math.Round(dx * sens);
                int ny = _y + (int)Math.Round(dy * sens);
                var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
                nx = Math.Max(vs.Left, Math.Min(vs.Right - 1, nx));
                ny = Math.Max(vs.Top, Math.Min(vs.Bottom - 1, ny));
                _x = nx;
                _y = ny;
                _overlay.SetHotspot(nx, ny);

                if (_secondHeldButtons != 0)
                {
                    // A button is HELD (clicking / holding / dragging). The real pointer
                    // has to be AT the second cursor for that press, drag and release to
                    // land there — a Windows click happens wherever the pointer is — so it
                    // FOLLOWS the second cursor while held, then is handed back to the
                    // parked spot on release. Without this, a click that moved even a pixel
                    // put its down at the second cursor and its up back at the parked spot
                    // (a broken click / accidental drag).
                    NativeMethods.SetCursorPos(_x, _y);
                    _suppressMainUntilTick = now + 200;
                }
                else
                {
                    // AIMING (no button held): the real pointer stays PARKED. Windows only
                    // has one cursor, so this is the whole trick — you move the second
                    // cursor to line up your shot while your main pointer never budges
                    // (also blocked in the hook so there's not even a flicker).
                    NativeMethods.SetCursorPos(_lastMainX, _lastMainY);
                }
                Interlocked.Increment(ref _secondMouseMoves);
                activity = true;
            }

            // Release AFTER movement, so a drag's final motion is applied before the up.
            if ((buttons & SecondMouseListener.RI_MOUSE_LEFT_BUTTON_UP) != 0) { ReleaseSecondButton(MouseButtonType.Left, HeldLeft); activity = true; }
            if ((buttons & SecondMouseListener.RI_MOUSE_RIGHT_BUTTON_UP) != 0) { ReleaseSecondButton(MouseButtonType.Right, HeldRight); activity = true; }

            if (wheel != 0)
            {
                // A REAL wheel at the second cursor (borrow the pointer, scroll, hand it
                // back) — Windows scrolls the window under the pointer, so this scrolls
                // the second cursor's target. The physical wheel event at the PARKED
                // spot is swallowed by the hook (it used to scroll the wrong window).
                int wx = _x, wy = _y, wd = wheel, rx = _lastMainX, ry = _lastMainY;
                _suppressMainUntilTick = now + 120;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        using (InputSimulator.RelayingUserInput())
                        {
                            InputSimulator.RealWheelAtAndRestore(wx, wy, wd, rx, ry);
                        }
                    }
                    catch { }
                });
                activity = true;
            }

            if (activity) { _lastSecondActivityTick = now; }
        }

        /// <summary>
        /// Press a real mouse button AT the second cursor and keep it held. On the first
        /// button we borrow the real pointer (warp it onto the second cursor); it's handed
        /// back on release. This is what lets you click, hold, and drag with the 2nd mouse.
        /// </summary>
        private void PressSecondButton(MouseButtonType button, int bit)
        {
            bool wasIdle = _secondHeldButtons == 0;
            _secondHeldButtons |= bit;
            Interlocked.Increment(ref _secondMouseClicks);
            _suppressMainUntilTick = Environment.TickCount + 250;
            try
            {
                // Relayed, not automated: the user physically pressed this button. The
                // tag keeps SelfClickGuard from swallowing it when it lands on Tempo's own
                // window mid-run — reaching over with the second mouse to hit Stop has to
                // work, and that is exactly when someone would do it.
                using (InputSimulator.RelayingUserInput())
                {
                    if (wasIdle) { InputSimulator.MoveTo(_x, _y); }   // borrow the pointer
                    InputSimulator.ButtonDown(button);
                }
            }
            catch { }
        }

        /// <summary>Releases any second-cursor buttons still held — so a button never sticks
        /// down if the mode is turned off (or panic-stopped) mid-hold.</summary>
        private void ReleaseAllHeldButtons()
        {
            int held = _secondHeldButtons;
            _secondHeldButtons = 0;
            if (held == 0) { return; }
            try
            {
                using (InputSimulator.RelayingUserInput())
                {
                    if ((held & HeldLeft) != 0) { InputSimulator.ButtonUp(MouseButtonType.Left); }
                    if ((held & HeldRight) != 0) { InputSimulator.ButtonUp(MouseButtonType.Right); }
                }
            }
            catch { }
        }

        /// <summary>Release a held second-cursor button; hand the real pointer back when none remain.</summary>
        private void ReleaseSecondButton(MouseButtonType button, int bit)
        {
            if ((_secondHeldButtons & bit) == 0)
            {
                return;   // we never pressed it (e.g. mode armed mid-hold)
            }
            _secondHeldButtons &= ~bit;
            try
            {
                using (InputSimulator.RelayingUserInput())
                {
                    InputSimulator.ButtonUp(button);
                    if (_secondHeldButtons == 0)
                    {
                        InputSimulator.MoveTo(_lastMainX, _lastMainY);   // give the pointer back
                        _suppressMainUntilTick = Environment.TickCount + 120;
                    }
                }
            }
            catch { }
        }

        // ── grab & place (one simple click) ───────────────────────────────────

        /// <summary>Enter placement: the second cursor follows the mouse; the next click drops it.</summary>
        public void StartPlacement()
        {
            if (!_enabled)
            {
                return;
            }
            _placing = true;
            _overlay.SetActiveLook(true);
            if (!_overlay.Visible) { _overlay.Show(); }
            _followTimer.Start();
            Logger.Info("[2nd cursor] grab & place — move the mouse and click where you want the second cursor.");
        }

        private void CancelPlacement()
        {
            if (!_placing)
            {
                return;
            }
            _placing = false;
            _followTimer.Stop();
            _overlay.SetActiveLook(false);
        }

        private void FollowTick(object sender, EventArgs e)
        {
            if (NativeMethods.GetCursorPos(out NativeMethods.POINT p))
            {
                _x = p.X;
                _y = p.Y;
                _overlay.SetHotspot(p.X, p.Y);
            }
        }

        // ── spam clicking at the parked spot ──────────────────────────────────

        public void ToggleSpam()
        {
            if (_spamming) { StopSpam(); }
            else { StartSpam(); }
        }

        public void StartSpam()
        {
            if (!_enabled || _spamming)
            {
                return;
            }
            // Ensure any previous loop (e.g. stopped by a right-click) has fully exited
            // before starting a fresh one.
            try { _spamThread?.Join(200); } catch { }

            // Whatever is under the cursor is what you meant to click — but read it in the
            // loop, not here, where the menu that started this is still covering the point.
            ArmSpamLatch(_x, _y);
            _spamPaused = "";

            _spamming = true;
            _spamThread = new Thread(SpamLoop) { IsBackground = true, Name = "SecondCursorSpam" };
            _spamThread.Start();
            Logger.Info("[2nd cursor] spam-clicking at (" + _x + ", " + _y + ") every " + _spamIntervalMs
                + " ms → target: " + DescribeTarget());
        }

        /// <summary>
        /// Describes the window currently under the second cursor — what a spam click
        /// will actually hit. The overlay reports HTTRANSPARENT, so it's skipped here
        /// (we see the game/app beneath). Shown in Live debug.
        /// </summary>
        public string DescribeTarget()
        {
            try
            {
                IntPtr h = NativeMethods.WindowFromPoint(new NativeMethods.POINT(_x, _y));
                if (h == IntPtr.Zero)
                {
                    return "(nothing under the cursor)";
                }
                var title = new System.Text.StringBuilder(160);
                NativeMethods.GetWindowText(h, title, title.Capacity);
                var cls = new System.Text.StringBuilder(96);
                NativeMethods.GetClassName(h, cls, cls.Capacity);
                string t = title.ToString().Trim();
                return (t.Length > 0 ? "\"" + t + "\"" : "(untitled)") + " [" + cls + "]";
            }
            catch { return "(unknown)"; }
        }

        private int _spamOverSelfWarnTick;

        // ── what the spam was AIMED at ────────────────────────────────────────
        // The parked spot is a fixed SCREEN point, and the window under a screen point is
        // not a fixed thing. Nothing remembered what the user actually aimed at, so the
        // loop re-resolved a target every tick and clicked whatever it found. Park the
        // cursor on a game, then drag another window over that spot, minimise the game,
        // close it, or let it move itself — and the clicks carry on at the same rate into
        // whatever is there now. That is the sharp edge of this feature: background clicks
        // are invisible, so a stream of them landing in the wrong application produces no
        // signal at all until something has already been clicked.
        //
        // Compared by PROCESS, not window handle, and that is deliberate. Handle equality
        // sounds stricter but is wrong in practice: an app that opens its own dropdown,
        // popup or tooltip over the spot, or rebuilds its window on a resolution change,
        // would read as "target lost" and stop clicking for no good reason. The process is
        // what the user means by "that app", and it still catches every case that matters —
        // another application on top, the window gone, or the bare desktop underneath.
        private IntPtr _spamTargetRoot = IntPtr.Zero;
        private uint _spamTargetPid;
        private string _spamTargetLabel = "";
        private int _spamTargetWarnTick;
        private volatile int _spamLatchX;
        private volatile int _spamLatchY;
        private volatile bool _spamNeedsLatch;
        private volatile string _spamPaused = "";

        /// <summary>
        /// Why spam-clicking is currently posting nothing, or "" when it is clicking
        /// normally. A paused spam is otherwise completely invisible — the clicks were
        /// never visible in the first place, so "not clicking" and "clicking" look
        /// identical from outside. Live debug reads this.
        /// </summary>
        public string SpamPausedReason => _spamming ? _spamPaused : "";

        /// <summary>What spam was aimed at when it started; "" if it isn't running.</summary>
        public string SpamAimedAt => _spamming ? _spamTargetLabel : "";

        /// <summary>
        /// The TOP-LEVEL window under a screen point. WindowFromPoint returns whichever
        /// child happens to be there, which changes as an app re-lays-out its own
        /// controls; GA_ROOT is the part that identifies the window you aimed at.
        /// </summary>
        private static IntPtr TopLevelAt(int x, int y)
        {
            try
            {
                IntPtr h = NativeMethods.WindowFromPoint(new NativeMethods.POINT(x, y));
                if (h == IntPtr.Zero) { return IntPtr.Zero; }
                IntPtr root = NativeMethods.GetAncestor(h, NativeMethods.GA_ROOT);
                return root != IntPtr.Zero ? root : h;
            }
            catch { return IntPtr.Zero; }
        }

        /// <summary>
        /// Marks the target as needing to be resolved, WITHOUT resolving it here.
        ///
        /// Resolving at this moment is a trap, and it broke the feature's main path once:
        /// the second cursor's own right-click menu is shown with its corner exactly on the
        /// cursor point, and "Spam-click here" runs its Click handler while that menu is
        /// still on screen. Reading the window under the cursor right then returns the MENU
        /// — a Tempo window — so the target latched as Tempo, and from the next tick onward
        /// the real app underneath looked like a stranger covering the target and the spam
        /// paused for ever. The loop resolves it instead, on the first tick where the point
        /// is not over one of Tempo's own windows, by which time the menu is long gone.
        /// </summary>
        private void ArmSpamLatch(int x, int y)
        {
            _spamLatchX = x;
            _spamLatchY = y;
            _spamTargetRoot = IntPtr.Zero;
            _spamTargetPid = 0;
            _spamTargetLabel = "";
            _spamTargetWarnTick = 0;
            _spamNeedsLatch = true;
        }

        /// <summary>Records what is under the cursor now as the thing spam is aimed at.</summary>
        private void LatchSpamTarget(int x, int y)
        {
            _spamLatchX = x;
            _spamLatchY = y;
            _spamNeedsLatch = false;
            _spamTargetRoot = TopLevelAt(x, y);
            _spamTargetPid = 0;
            if (_spamTargetRoot != IntPtr.Zero)
            {
                try { NativeMethods.GetWindowThreadProcessId(_spamTargetRoot, out _spamTargetPid); } catch { }
            }
            _spamTargetLabel = DescribeTarget();
            _spamTargetWarnTick = 0;
        }

        /// <summary>
        /// True while the app the user aimed at is still the one under the cursor. Returns
        /// true when nothing was ever latched, so a failure to identify the target can
        /// never silently disable the feature.
        /// </summary>
        private bool SpamTargetStillThere(int x, int y)
        {
            if (_spamTargetPid == 0) { return true; }
            IntPtr now = TopLevelAt(x, y);
            if (now == IntPtr.Zero) { return false; }
            try
            {
                NativeMethods.GetWindowThreadProcessId(now, out uint pid);
                return pid == _spamTargetPid;
            }
            catch { return true; }
        }

        /// <summary>Says the aimed-at window is no longer there, at most once every 3 s.</summary>
        private void WarnSpamTargetChangedOnce()
        {
            int now = Environment.TickCount;
            if (_spamTargetWarnTick != 0 && unchecked(now - _spamTargetWarnTick) < 3000) { return; }
            _spamTargetWarnTick = now;

            bool closed = _spamTargetRoot != IntPtr.Zero && !NativeMethods.IsWindow(_spamTargetRoot);
            string why = closed
                ? "the window it was aimed at " + _spamTargetLabel + " has closed."
                : "something else is under the second cursor now (aimed at " + _spamTargetLabel
                  + ", currently " + DescribeTarget() + ").";
            Logger.Info("[2nd cursor] spam paused — " + why
                + " Bring that window back, or move the cursor to re-aim.");
        }

        /// <summary>
        /// True when the window under this screen point belongs to Tempo. Compared by
        /// PROCESS id rather than a list of known handles, so it covers the main window,
        /// every dialog, and the overlays without anything needing to register itself.
        /// </summary>
        private static bool TargetIsOwnWindow(int x, int y)
        {
            try
            {
                IntPtr hwnd = NativeMethods.WindowFromPoint(new NativeMethods.POINT(x, y));
                if (hwnd == IntPtr.Zero) { return false; }
                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                return pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            }
            catch { return false; }
        }

        /// <summary>Says why spam went quiet, at most once every 3 s.</summary>
        private void WarnSpamOverSelfOnce()
        {
            int now = Environment.TickCount;
            if (_spamOverSelfWarnTick != 0 && unchecked(now - _spamOverSelfWarnTick) < 3000) { return; }
            _spamOverSelfWarnTick = now;
            Logger.Info("[2nd cursor] spam paused — the second cursor is over Tempo's own window. "
                + "Move it onto the app you want to click.");
        }

        public void StopSpam()
        {
            if (!_spamming)
            {
                return;
            }
            _spamming = false;
            try { _spamThread?.Join(400); } catch { }
            _spamThread = null;
            Logger.Info("[2nd cursor] spam-clicking stopped.");
        }

        private void SpamLoop()
        {
            while (_spamming)
            {
                int x = _x, y = _y;
                // Never spam Tempo's own interface. Park the second cursor over the Tempo
                // window, middle-click, and this loop was posting clicks straight into the
                // controls underneath it — ticking boxes and pressing buttons several times
                // a second. SelfClickGuard cannot catch this: these are PostMessage'd, so
                // they carry none of the dwExtraInfo it matches on. Skipping the tick (and
                // not stopping the spam) means moving the cursor back off Tempo resumes it.
                // The cursor MOVED, so the user re-aimed: whatever is under it now is the
                // new target. Detecting it here covers every way the cursor can be moved —
                // the menu's "grab & place", a bound second physical mouse, or a caller
                // setting the position — without each of those having to remember to
                // re-latch. Only a cursor that is standing still can be pointing at a
                // window that changed underneath it.
                if (x != _spamLatchX || y != _spamLatchY)
                {
                    ArmSpamLatch(x, y);
                }

                if (TargetIsOwnWindow(x, y))
                {
                    // Note this runs BEFORE the latch below, which is what keeps Tempo's
                    // own menu (or window) from ever being recorded as the target.
                    _spamPaused = "the second cursor is over Tempo's own window";
                    WarnSpamOverSelfOnce();
                }
                else if (_spamNeedsLatch)
                {
                    LatchSpamTarget(x, y);
                    _spamPaused = "";
                    try { InputSimulator.BackgroundClick(x, y, _spamButton, _spamStyle); }
                    catch { }
                }
                else if (!SpamTargetStillThere(x, y))
                {
                    // Pause rather than stop, exactly like the over-Tempo guard above: the
                    // window may well come back (an alt-tab, a dialog dismissed), and
                    // silently ending the run would be its own surprise.
                    _spamPaused = (_spamTargetRoot != IntPtr.Zero && !NativeMethods.IsWindow(_spamTargetRoot))
                        ? "the window it was aimed at has closed"
                        : "another window is covering the target";
                    WarnSpamTargetChangedOnce();
                }
                else
                {
                    _spamPaused = "";
                    try { InputSimulator.BackgroundClick(x, y, _spamButton, _spamStyle); }
                    catch { }
                }
                int wait = _spamIntervalMs;
                while (wait > 0 && _spamming)
                {
                    int slice = Math.Min(20, wait);
                    Thread.Sleep(slice);
                    wait -= slice;
                }
            }
        }

        // ── input hook: right-click the cursor / place-click ──────────────────

        private void StartHook()
        {
            if (_hook != null)
            {
                return;
            }
            _hook = new LowLevelMouseHook();
            _hook.MouseEvent += OnHookMouse;
            _hook.Start();
        }

        private void StopHook()
        {
            if (_hook == null)
            {
                return;
            }
            try { _hook.MouseEvent -= OnHookMouse; _hook.Dispose(); } catch { }
            _hook = null;
        }

        private void OnHookMouse(object sender, MouseHookEventArgs e)
        {
            // ── keep the REAL cursor perfectly still while the 2nd mouse is moving ──
            // The clean way to stop the real pointer moving is not to snap it back after
            // the fact (that jitters) but to BLOCK the movement here before the cursor
            // ever moves. We block movement whenever the second mouse was the last device
            // to physically move (raw input tells us which). It auto-releases shortly
            // after the second mouse stops, and the moment the MAIN mouse moves it becomes
            // the last-moved device, so the main pointer is never left stuck.
            if (e.Type == MouseHookEventType.Move && !_placing && !e.Injected
                && _usePhysicalMouse && _secondMouseHandle != IntPtr.Zero && CursorHeldStill())
            {
                e.Handled = true;
                return;
            }

            // Note: Tempo's own spam is posted with PostMessage, which never reaches a
            // low-level hook, so there's nothing of ours to filter out here — and we
            // must NOT skip injected events, or the feature would be untestable and
            // some remapper-driven mice (whose input is injected) couldn't use it.

            // PLACING comes FIRST. This used to sit below the second-mouse swallow,
            // which meant a place-click made within ~150 ms of second-mouse activity
            // was eaten by that swallow instead of dropping the cursor — an
            // intermittent "grab & place doesn't work" whenever both mice were in use.
            if (_placing && IsButtonEvent(e.Type))
            {
                // While grabbing/placing with the real mouse, swallow EVERY button (down
                // and up, left/right/middle) so nothing under the pointer is accidentally
                // clicked. Left drops the second cursor here; right cancels the grab.
                e.Handled = true;
                if (e.Type == MouseHookEventType.LeftDown)
                {
                    _x = e.X;
                    _y = e.Y;
                    MarshalToUi(() =>
                    {
                        _placing = false;
                        _followTimer.Stop();
                        _overlay.SetActiveLook(false);
                        _overlay.SetHotspot(_x, _y);
                        Logger.Info("[2nd cursor] placed at (" + _x + ", " + _y + ").");
                    });
                }
                else if (e.Type == MouseHookEventType.RightDown)
                {
                    MarshalToUi(() =>
                    {
                        _placing = false;
                        _followTimer.Stop();
                        _overlay.SetActiveLook(_spamming);
                        Logger.Info("[2nd cursor] grab cancelled.");
                    });
                }
                return;
            }

            // Second physical mouse: its button press still fires at the (parked) system
            // cursor. Swallow that stray PHYSICAL click — the real click already happened
            // AT the second cursor via our own SendInput (which is injected, so the
            // !Injected guard lets it through while eating the stray). The hook can't see
            // devices, so we swallow when EITHER the second mouse just pressed this exact
            // button (matched per-button, order-independent) OR it was the most recent
            // device. The main mouse's own clicks match neither and pass through.
            if (_usePhysicalMouse && _secondMouseHandle != IntPtr.Zero && !e.Injected
                && (IsButtonEvent(e.Type) || e.Type == MouseHookEventType.Wheel))
            {
                int nowTick = Environment.TickCount;
                int bi = ButtonIndex(e.Type);
                bool secondPressedThisButton = bi >= 0 && unchecked(nowTick - _secondBtnTick[bi]) < 90;
                // Fallback for the raw-vs-hook ordering race. Kept SHORT (was 350 ms):
                // that window also eats the MAIN mouse's clicks right after second-mouse
                // activity — reported as "click not work" — so it covers just the race,
                // not a third of a second of the main mouse's input.
                bool secondIsActive = _lastInputDevice == _secondMouseHandle
                    && unchecked(nowTick - _lastInputTick) < 150;
                if (secondPressedThisButton || secondIsActive)
                {
                    e.Handled = true;
                    return;
                }
            }

            // A right-click ON the marker opens its menu — but ONLY from the real MAIN
            // mouse. Injected right-clicks are excluded: the second mouse's own right-click
            // is performed via SendInput (injected) and briefly warps the real cursor onto
            // the marker, which would otherwise pop this menu instead of doing a plain
            // right-click. So the menu is a main-cursor-only affair. SWALLOWED (down + up)
            // so it doesn't ALSO open the window's context menu. Shown on the UP.
            if (!_placing && !e.Injected
                && (e.Type == MouseHookEventType.RightDown || e.Type == MouseHookEventType.RightUp)
                && IsOnCursor(e.X, e.Y))
            {
                e.Handled = true;
                if (e.Type == MouseHookEventType.RightUp)
                {
                    int mx = e.X, my = e.Y;
                    MarshalToUi(() =>
                    {
                        try { MenuRequested?.Invoke(this, new SecondCursorMenuEventArgs(mx, my)); }
                        catch (Exception ex) { Logger.Warn("[2nd cursor] menu failed: " + ex.Message); }
                    });
                }
            }
        }

        private bool IsOnCursor(int x, int y)
        {
            long dx = x - _x, dy = y - _y;
            return dx * dx + dy * dy <= (long)_markerRadius * _markerRadius;
        }

        /// <summary>
        /// True while the real cursor is being pinned in place because the second mouse
        /// is the one currently moving. Drives the movement-block in the hook and the
        /// Live-debug status line.
        /// </summary>
        public bool CursorHeldStill()
        {
            return _usePhysicalMouse && _secondMouseHandle != IntPtr.Zero
                && _lastMovedDevice == _secondMouseHandle
                && unchecked(Environment.TickCount - _lastMovedTick) < CursorLockMs;
        }

        private static bool IsButtonEvent(MouseHookEventType t)
        {
            return t == MouseHookEventType.LeftDown || t == MouseHookEventType.LeftUp
                || t == MouseHookEventType.RightDown || t == MouseHookEventType.RightUp
                || t == MouseHookEventType.MiddleDown || t == MouseHookEventType.MiddleUp;
        }

        /// <summary>Maps a hook event to the L=0 / R=1 / M=2 / wheel=3 index (-1 otherwise).</summary>
        private static int ButtonIndex(MouseHookEventType t)
        {
            switch (t)
            {
                case MouseHookEventType.LeftDown:
                case MouseHookEventType.LeftUp: return 0;
                case MouseHookEventType.RightDown:
                case MouseHookEventType.RightUp: return 1;
                case MouseHookEventType.MiddleDown:
                case MouseHookEventType.MiddleUp: return 2;
                case MouseHookEventType.Wheel: return 3;
                default: return -1;
            }
        }

        private void MarshalToUi(Action action)
        {
            try
            {
                if (_overlay.IsHandleCreated)
                {
                    _overlay.BeginInvoke(action);
                }
                else
                {
                    action();
                }
            }
            catch { }
        }

        public void Dispose()
        {
            StopSpam();
            StopHook();
            StopListener();
            try { _topKeeper?.Dispose(); } catch { }
            try { _deviceWatch?.Dispose(); } catch { }
            try { _followTimer?.Dispose(); } catch { }
            try { _overlay?.Dispose(); } catch { }
        }
    }
}
