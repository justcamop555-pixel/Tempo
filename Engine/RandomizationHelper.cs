using System;

namespace AutoClicker.Engine
{
    /// <summary>
    /// The randomness behind the clicker's anti-pattern options: how long to wait between clicks, where each
    /// click lands, how long it is held. One per RUN, used only by that run's worker — System.Random is not safe
    /// across threads, and a stopped run's worker can still be finishing its last click while the next run's
    /// begins. It was one per engine, shared by both; corrupted, a Random returns the same draw for good, and
    /// every "random" offset becomes one fixed offset — the most regular pattern there is.
    /// </summary>
    public sealed class RandomizationHelper
    {
        private readonly Random _random;

        public RandomizationHelper()
        {
            _random = new Random();
        }

        public RandomizationHelper(int seed)
        {
            _random = new Random(seed);
        }

        /// <summary>
        /// How far the interval may really move: the jitter asked for, but never so far below the interval that
        /// it would cross <paramref name="floorMs"/> — the least the engine waits (half a millisecond, or the
        /// Anti-Freeze cap's gap while it is on). Symmetric, so the AVERAGE stays the interval set.
        ///
        /// It used to be the jitter as typed, with every result below 1 ms simply raised to 1 ms: ±50 ms on a
        /// 10 ms interval made a third of the clicks exactly 1 ms apart — a spike any detector sees — and moved
        /// the average away from the speed on screen.
        /// </summary>
        public static double EffectiveJitter(double baseMs, double jitterMs, double floorMs)
        {
            if (jitterMs <= 0 || baseMs <= floorMs) { return 0; }
            return Math.Min(jitterMs, baseMs - floorMs);
        }

        /// <summary>
        /// <paramref name="baseMs"/> moved by a random amount within ± <see cref="EffectiveJitter"/>, most often by
        /// a little (a triangular spread: the sum of two uniform draws), never below <paramref name="floorMs"/>.
        /// </summary>
        public double JitterInterval(double baseMs, double jitterMs, double floorMs = 0.5)
        {
            double j = EffectiveJitter(baseMs, jitterMs, floorMs);
            if (j <= 0) { return Math.Max(baseMs, floorMs); }
            double t = _random.NextDouble() + _random.NextDouble() - 1.0;   // triangular on (-1, 1), peak at 0
            return Math.Max(floorMs, baseMs + j * t);
        }

        /// <summary>
        /// A point within <paramref name="radius"/> pixels of (<paramref name="x"/>, <paramref name="y"/>), the way a
        /// hand misses its aim: most often near the centre, in every direction alike (a normal spread, σ = radius / 2,
        /// redrawn outside the radius).
        ///
        /// X and Y used to be jittered independently, which spread the clicks over a SQUARE: "±5 px" reached 7 px
        /// along the diagonals, and the box showed in a plot of the clicks.
        /// </summary>
        public void JitterPosition(ref int x, ref int y, int radius)
        {
            if (radius <= 0) { return; }
            OffsetWithin(radius, out int dx, out int dy);
            x += dx;
            y += dy;
        }

        private void OffsetWithin(int radius, out int dx, out int dy)
        {
            double sigma = radius / 2.0;
            long r2 = (long)radius * radius;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                // Box–Muller: two independent normal draws.
                double u1 = 1.0 - _random.NextDouble();   // (0, 1]: no log of zero
                double u2 = _random.NextDouble();
                double m = Math.Sqrt(-2.0 * Math.Log(u1)) * sigma;
                int ox = (int)Math.Round(m * Math.Cos(2 * Math.PI * u2));
                int oy = (int)Math.Round(m * Math.Sin(2 * Math.PI * u2));
                if ((long)ox * ox + (long)oy * oy <= r2)
                {
                    dx = ox;
                    dy = oy;
                    return;
                }
            }
            dx = 0;
            dy = 0;
        }

        /// <summary>
        /// <see cref="JitterPosition"/> that stays on the window the point itself is on — the same top-level window
        /// (<paramref name="windowAt"/>) and on the screen. A point near a window's edge sent some of its clicks to
        /// whatever lay beyond the edge: the desktop, another program — and with "click without moving my cursor"
        /// the click was POSTED to that other program. A draw that leaves the window is drawn again; after a few
        /// misses the point itself is clicked. Returns false when the point itself was used.
        /// </summary>
        public bool JitterWithin(ref int x, ref int y, int radius, Func<int, int, IntPtr> windowAt)
        {
            if (radius <= 0) { return false; }
            IntPtr home = IntPtr.Zero;
            try { home = windowAt != null ? windowAt(x, y) : IntPtr.Zero; } catch { home = IntPtr.Zero; }
            for (int attempt = 0; attempt < 6; attempt++)
            {
                OffsetWithin(radius, out int dx, out int dy);
                int nx = x + dx, ny = y + dy;
                if (!Utils.ScreenGeometry.IsOnScreen(nx, ny)) { continue; }
                if (home != IntPtr.Zero)
                {
                    IntPtr there;
                    try { there = windowAt(nx, ny); } catch { there = IntPtr.Zero; }
                    if (there != home) { continue; }
                }
                x = nx;
                y = ny;
                return true;
            }
            return false;
        }

        /// <summary>Returns a random integer in [minInclusive, maxExclusive).</summary>
        public int Next(int minInclusive, int maxExclusive)
        {
            return _random.Next(minInclusive, maxExclusive);
        }
    }

    /// <summary>
    /// Where "click where the cursor is" aims while its position is randomized: the spot the USER put the cursor,
    /// not where the last click left it.
    ///
    /// Each click moves the real cursor to its jittered point, and the next click read the cursor from there and
    /// jittered again — so the clicks walked away from the target, a few pixels a click in a random direction:
    /// about 40 px after 1,000 clicks at ±3, 120 px after 10,000, until they missed the button altogether. Now the
    /// spot is kept while the cursor still sits where the last click put it (give or take a pixel of rounding in
    /// Windows' absolute moves), and taken afresh the moment the user moves the mouse.
    /// </summary>
    public sealed class CursorAnchor
    {
        /// <summary>How far (px) from the last click the cursor can be and still count as not moved.</summary>
        public const int Tolerance = 2;

        private bool _have, _clicked;
        private int _ax, _ay, _lastX, _lastY;

        /// <summary>The point to aim around, the cursor being at (<paramref name="cursorX"/>, <paramref name="cursorY"/>).</summary>
        public void Aim(int cursorX, int cursorY, out int x, out int y)
        {
            if (!_have || !_clicked || !Near(cursorX, cursorY))
            {
                _ax = cursorX;
                _ay = cursorY;
                _have = true;
            }
            x = _ax;
            y = _ay;
        }

        /// <summary>The click just went to (<paramref name="x"/>, <paramref name="y"/>).</summary>
        public void Clicked(int x, int y)
        {
            _lastX = x;
            _lastY = y;
            _clicked = true;
        }

        /// <summary>
        /// Where to put the cursor back when the run ends: the spot it was aimed around, as long as the cursor is
        /// still where the last click left it (the user has not taken the mouse since).
        /// </summary>
        public bool TryHome(int cursorX, int cursorY, out int x, out int y)
        {
            x = _ax;
            y = _ay;
            return _have && _clicked && Near(cursorX, cursorY) && (cursorX != _ax || cursorY != _ay);
        }

        private bool Near(int cx, int cy) => Math.Abs(cx - _lastX) <= Tolerance && Math.Abs(cy - _lastY) <= Tolerance;
    }
}
