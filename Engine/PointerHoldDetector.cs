using System;
using System.Runtime.InteropServices;

namespace AutoClicker.Engine
{
    /// <summary>
    /// Tells mouse movement that turns a game's camera from movement that only moves the pointer.
    ///
    /// Camera-relative movement estimates the camera by adding up raw mouse movement, and it used to
    /// add up ALL of it. Plenty of games turn the camera only some of the time. Roblox's own camera
    /// script turns it only while it locks the pointer: in place while the right (or middle) button is
    /// held, at the centre in first person and shift lock; otherwise the mouse just moves the pointer
    /// over the world and the menus. Moving the mouse to click something while running then counted
    /// as a turn, and W/A/S/D were re-mixed for a camera that never moved.
    ///
    /// A game that is turning its camera with the mouse does one of these to the pointer, and each can
    /// be seen from outside the game:
    ///  • hides it (GetCursorInfo: not showing, or no cursor shape);
    ///  • confines it to a few pixels (GetClipCursor);
    ///  • holds it in one place — it stays put, or keeps coming back to the same spot, while the mouse
    ///    reports movement.
    /// A pointer that moves with the mouse is not turning anything.
    ///
    /// One trap: a free pointer pushed against the edge of the screen also stays put. So "held in
    /// place" never counts on the edge of a monitor.
    /// </summary>
    internal sealed class PointerHoldDetector
    {
        /// <summary>What the pointer is doing right now, read with <see cref="Probe"/>.</summary>
        internal struct Sample
        {
            /// <summary>No pointer on screen: the game hid it.</summary>
            public bool Hidden;
            /// <summary>Confined to a few pixels (ClipCursor).</summary>
            public bool Confined;
            /// <summary>Where the pointer is, in screen coordinates.</summary>
            public int X, Y;
            /// <summary>On the edge of its monitor, where a free pointer also stops moving.</summary>
            public bool AtEdge;
        }

        internal enum Verdict
        {
            /// <summary>The pointer moves with the mouse: this movement turned no camera.</summary>
            NotTurning,
            /// <summary>The game is holding or hiding the pointer: this movement is a camera turn.</summary>
            Turning,
            /// <summary>The pointer has not moved yet, but too little movement has arrived to be sure.</summary>
            Undecided
        }

        /// <summary>
        /// Movement, in counts, the pointer must ignore before "it stayed put" means "the game holds it".
        /// A free pointer covers at least 3 pixels in 8 counts at any usual pointer speed.
        /// </summary>
        internal const int MinCounts = 8;

        /// <summary>
        /// A game that puts the pointer back every frame is sometimes caught between its frames, a few
        /// pixels off its spot. For this long after the pointer was last seen held, that still counts as
        /// held. Two frames of a 30 fps game.
        /// </summary>
        internal const long StickyMs = 70;

        private bool _hasAnchor;
        private int _anchorX, _anchorY;
        private long _countsSinceAnchor;
        private long _heldUntilMs = long.MinValue;

        /// <summary>Why the last verdict was what it was, for Live debug: "hidden", "held", "free", …</summary>
        public string Reason { get; private set; } = "still";

        /// <summary>The last verdict.</summary>
        public Verdict Last { get; private set; } = Verdict.Undecided;

        /// <summary>Forget everything (after a pause, a re-arm, or leaving the game).</summary>
        public void Reset()
        {
            _hasAnchor = false;
            _countsSinceAnchor = 0;
            _heldUntilMs = long.MinValue;
            Reason = "still";
            Last = Verdict.Undecided;
        }

        /// <summary>
        /// One tick: <paramref name="counts"/> is how far the mouse moved since the last call (|dx|+|dy|),
        /// <paramref name="s"/> what the pointer is doing now. Says whether that movement turned the camera.
        /// </summary>
        public Verdict Update(long counts, Sample s, long nowMs)
        {
            Last = Decide(counts, s, nowMs);
            return Last;
        }

        private Verdict Decide(long counts, Sample s, long nowMs)
        {
            if (s.Hidden || s.Confined)
            {
                Reason = s.Hidden ? "hidden" : "confined";
                Anchor(s);
                // No grace period after this one: a pointer that shows again (the right button let go
                // in Roblox) is free from that moment, and hiding is a state, not a position that can
                // be caught between frames.
                _heldUntilMs = long.MinValue;
                return Verdict.Turning;
            }

            if (!_hasAnchor)
            {
                Anchor(s);
                _countsSinceAnchor = counts;
                Reason = "still";
                return Verdict.Undecided;
            }

            bool atAnchor = Math.Abs(s.X - _anchorX) <= 1 && Math.Abs(s.Y - _anchorY) <= 1;
            if (atAnchor)
            {
                _countsSinceAnchor += counts;
                if (s.AtEdge)
                {
                    // A free pointer pushed into the edge stays put too. Only a hidden or confined
                    // pointer counts here.
                    Reason = "edge";
                    return nowMs <= _heldUntilMs ? Verdict.Turning : Verdict.NotTurning;
                }
                if (_countsSinceAnchor >= MinCounts)
                {
                    Reason = "held";
                    _heldUntilMs = nowMs + StickyMs;
                    return Verdict.Turning;
                }
                if (nowMs <= _heldUntilMs)
                {
                    Reason = "held";
                    return Verdict.Turning;
                }
                Reason = "still";
                return Verdict.Undecided;
            }

            // Away from where it rested.
            if (nowMs <= _heldUntilMs)
            {
                // Just seen held: a game that puts the pointer back each frame, caught between frames.
                // The spot stays where it was; the game is about to put the pointer back there.
                Reason = "held";
                return Verdict.Turning;
            }
            // It moved with the mouse: a free pointer. It rests here now.
            Reason = "free";
            Anchor(s);
            _countsSinceAnchor = 0;
            return Verdict.NotTurning;
        }

        private void Anchor(Sample s)
        {
            _hasAnchor = true;
            _anchorX = s.X;
            _anchorY = s.Y;
            _countsSinceAnchor = 0;
        }

        // ── Reading the real pointer ─────────────────────────────────────────────────────────────

        /// <summary>Reads what the pointer is doing now. A few microseconds; any thread.</summary>
        internal static Sample Probe()
        {
            var s = new Sample();
            try
            {
                var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
                if (GetCursorInfo(ref ci))
                {
                    s.Hidden = (ci.flags & CURSOR_SHOWING) == 0 || ci.hCursor == IntPtr.Zero;
                    s.X = ci.ptScreenPos.X;
                    s.Y = ci.ptScreenPos.Y;
                }
                else if (GetCursorPos(out POINT p))
                {
                    s.X = p.X;
                    s.Y = p.Y;
                }

                if (GetClipCursor(out RECT clip))
                {
                    // Clipped to the whole desktop (no clip) or to a window is not holding anything; a
                    // box a few pixels across is the pointer locked in place.
                    s.Confined = clip.Right - clip.Left <= 8 && clip.Bottom - clip.Top <= 8;
                }

                IntPtr monitor = MonitorFromPoint(new POINT { X = s.X, Y = s.Y }, MONITOR_DEFAULTTONEAREST);
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref mi))
                {
                    s.AtEdge = s.X <= mi.rcMonitor.Left || s.X >= mi.rcMonitor.Right - 1
                            || s.Y <= mi.rcMonitor.Top || s.Y >= mi.rcMonitor.Bottom - 1;
                }
            }
            catch
            {
                // Reading the pointer must never stop the movement loop: a blank sample is "free".
            }
            return s;
        }

        private const int CURSOR_SHOWING = 0x00000001;
        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct CURSORINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hCursor;
            public POINT ptScreenPos;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetCursorInfo(ref CURSORINFO pci);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool GetClipCursor(out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    }
}
