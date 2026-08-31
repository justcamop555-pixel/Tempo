using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AutoClicker.UI
{
    /// <summary>
    /// Keeps Tempo's borderless overlay windows (the clicking badge, the countdown,
    /// the caption bar, the recording/points markers) on top — INCLUDING over
    /// borderless-fullscreen games and fullscreen browser video, which is where they
    /// used to vanish.
    ///
    /// Why a shared keeper instead of each form doing its own thing:
    ///
    ///  • The moment a game or a fullscreen YouTube tab becomes the foreground window,
    ///    Windows re-orders the z-order and our top-most windows get pushed underneath
    ///    it. A slow per-window timer only fixes this up to a tick later — long enough
    ///    to look broken. So this hooks EVENT_SYSTEM_FOREGROUND and re-asserts every
    ///    overlay the instant the foreground app changes, which is the exact moment
    ///    they'd otherwise be buried.
    ///  • A short backstop timer also re-tops periodically, for the apps that keep
    ///    re-topping themselves.
    ///
    /// The honest limit: a program using TRUE EXCLUSIVE fullscreen (old-style DirectX
    /// exclusive mode) owns the whole display and NO ordinary window — Tempo's, or any
    /// other overlay tool's — can draw over it. The fix there is to run the game in
    /// "borderless" / "windowed fullscreen" mode, which this keeper then covers.
    ///
    /// SetWindowPos with SWP_NOACTIVATE is safe to call for a UI-thread window from the
    /// timer's pool thread — it only changes z-order and never touches managed state.
    /// </summary>
    internal static class OverlayTopmost
    {
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOOWNERZORDER = 0x0200;
        private const uint SWP_NOSENDCHANGING = 0x0400;

        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after,
            int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd,
            int idObject, int idChild, uint thread, uint time);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module,
            WinEventProc proc, uint pid, uint tid, uint flags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hook);

        /// <summary>
        /// True while a fullscreen game owns the screen. The 250 ms backstop then
        /// only re-tops every 6th tick (~1.5 s): repeated z-order churn above a
        /// fullscreen game bounces DWM in and out of its fast presentation path,
        /// which shows up as stutter — and was part of the Rainbow Six freeze
        /// report. The EVENT_SYSTEM_FOREGROUND re-top stays INSTANT either way,
        /// and that hook is what actually keeps captions visible when a game
        /// takes the screen; the timer is only a backstop for self-re-topping
        /// apps, so it can afford to be lazy during play.
        /// </summary>
        public static volatile bool LowImpactMode;
        private static int _lazyTick;

        private static readonly object _lock = new object();
        private static readonly HashSet<IntPtr> _handles = new HashSet<IntPtr>();
        private static System.Threading.Timer _timer;
        private static IntPtr _hook;
        // The delegate MUST be held in a field, or the GC collects it and the native
        // hook calls into freed memory.
        private static WinEventProc _proc;

        /// <summary>
        /// Starts keeping <paramref name="hwnd"/> on top. Call once the window handle
        /// exists (OnHandleCreated / OnShown). Idempotent.
        /// </summary>
        public static void Register(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) { return; }
            lock (_lock)
            {
                _handles.Add(hwnd);
                EnsureRunning();
            }
            Reassert(hwnd);
        }

        /// <summary>Stops keeping <paramref name="hwnd"/> on top (call on close).</summary>
        public static void Unregister(IntPtr hwnd)
        {
            lock (_lock)
            {
                _handles.Remove(hwnd);
                if (_handles.Count == 0)
                {
                    StopRunning();
                }
            }
        }

        // ── internals ────────────────────────────────────────────────────────────

        private static void EnsureRunning()
        {
            // Backstop re-top a few times a second — cheap, and covers apps that keep
            // re-asserting their own top-most state after the foreground event.
            if (_timer == null)
            {
                _timer = new System.Threading.Timer(_ => BackstopTick(), null, 250, 250);
            }
            // Instant re-top the moment any other app takes the foreground (the exact
            // instant a game / fullscreen video would otherwise bury us). Skipping our
            // own process avoids needless churn when Tempo itself gains focus.
            if (_hook == IntPtr.Zero)
            {
                try
                {
                    _proc = OnForeground;
                    _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
                        IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
                }
                catch { _hook = IntPtr.Zero; }
            }
        }

        private static void StopRunning()
        {
            try { _timer?.Dispose(); } catch { }
            _timer = null;
            if (_hook != IntPtr.Zero)
            {
                try { UnhookWinEvent(_hook); } catch { }
                _hook = IntPtr.Zero;
                _proc = null;
            }
        }

        private static void OnForeground(IntPtr hook, uint ev, IntPtr hwnd,
            int idObject, int idChild, uint thread, uint time)
        {
            ReassertAll();
        }

        private static void BackstopTick()
        {
            // Game mode: only every 6th backstop tick actually re-tops (see
            // LowImpactMode). The counter free-runs; exactness doesn't matter.
            if (LowImpactMode && (++_lazyTick % 6) != 0) { return; }
            ReassertAll();
        }

        private static void ReassertAll()
        {
            IntPtr[] snapshot;
            lock (_lock)
            {
                if (_handles.Count == 0) { return; }
                snapshot = new IntPtr[_handles.Count];
                _handles.CopyTo(snapshot);
            }

            List<IntPtr> dead = null;
            foreach (IntPtr h in snapshot)
            {
                if (!IsWindow(h))
                {
                    (dead ??= new List<IntPtr>()).Add(h);   // a window closed without unregistering
                    continue;
                }
                Reassert(h);
            }
            if (dead != null)
            {
                lock (_lock)
                {
                    foreach (IntPtr h in dead) { _handles.Remove(h); }
                    if (_handles.Count == 0) { StopRunning(); }
                }
            }
        }

        private static void Reassert(IntPtr h)
        {
            try
            {
                SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOSENDCHANGING);
            }
            catch { }
        }
    }
}
