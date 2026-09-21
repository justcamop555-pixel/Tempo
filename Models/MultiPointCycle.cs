using System.Collections.Generic;

namespace AutoClicker.Models
{
    /// <summary>
    /// What one pass over a multi-point list actually costs — the numbers the Multi-Point tab
    /// reports above the list.
    ///
    /// It lives here, away from the tab, because every part of it was wrong in a way a Form cannot
    /// be tested for:
    ///  • a "cycle" is only n visits in Sequential and Reverse. Ping-Pong bounces, so a full cycle
    ///    is 2n-2 visits (1,2,3,2 then back to 1), and Random has no cycle at all — the old line
    ///    reported n for all four orders.
    ///  • "clicks" counted one per visit, while the engine issues <see cref="ClicksForStyle"/> per
    ///    visit — so three double-click points read "3 clicks" and the Statistics tab then recorded 6.
    ///  • the time estimate used the configured interval, which the engine will not honour when
    ///    Anti-Freeze caps the rate (ClickEngine takes max(interval, 1000/cap, 0.5)), and it ignored
    ///    the per-click hold, which is a real Thread.Sleep inside every actuation.
    /// </summary>
    public static class MultiPointCycle
    {
        /// <summary>
        /// Real mouse clicks one visit of this style issues. Mirrors ClickEngine.StyleToClicks —
        /// the engine's count is what Statistics records, so the estimate must use the same mapping.
        /// </summary>
        public static int ClicksForStyle(ClickStyle style)
        {
            switch (style)
            {
                case ClickStyle.Single: return 1;
                case ClickStyle.Double: return 2;
                case ClickStyle.Triple: return 3;
                case ClickStyle.Quadruple: return 4;
                default: return 1;
            }
        }

        /// <summary>
        /// How many point VISITS one full cycle makes, given the order. Ping-Pong is 2n-2 because the
        /// two ends are not revisited on the way back; Random is reported as one pass over n.
        /// </summary>
        public static int VisitsPerCycle(MultiPointOrder order, int enabledPoints)
        {
            if (enabledPoints <= 0) { return 0; }
            if (order == MultiPointOrder.PingPong && enabledPoints > 2) { return (enabledPoints - 1) * 2; }
            return enabledPoints;
        }

        /// <summary>True when "per cycle" is a real, repeating quantity for this order.</summary>
        public static bool HasFixedCycle(MultiPointOrder order) => order != MultiPointOrder.Random;

        /// <summary>
        /// The smallest gap the engine will actually use between actuations, in milliseconds:
        /// the configured interval, floored by the Anti-Freeze cap and the engine's own 0.5 ms
        /// limit. <paramref name="capCps"/> is int.MaxValue when Anti-Freeze is off.
        /// </summary>
        public static double EffectiveIntervalMs(double configuredIntervalMs, int capCps)
        {
            double min = capCps >= int.MaxValue || capCps < 1 ? 0.5 : 1000.0 / capCps;
            if (min < 0.5) { min = 0.5; }
            return configuredIntervalMs > min ? configuredIntervalMs : min;
        }

        /// <summary>One pass's totals: visits, real clicks, and elapsed milliseconds.</summary>
        public struct Estimate
        {
            public int Visits;
            public long Clicks;
            public double Milliseconds;
            /// <summary>True when the rate asked for is being held back by the Anti-Freeze cap.</summary>
            public bool Capped;
        }

        /// <summary>
        /// Totals one cycle over the ENABLED points. Every visit costs its dwell (or the effective
        /// interval when it has none) plus the hold each of its clicks is held down for.
        ///
        /// Ping-Pong's extra visits are the interior points, so they are counted by walking the
        /// bounce rather than multiplying — a 3-point list with different dwells is not 2x the
        /// one-way total.
        /// </summary>
        public static Estimate Measure(IList<ClickPoint> points, MultiPointOrder order,
                                       double configuredIntervalMs, int capCps, int holdMs)
        {
            var est = new Estimate();
            if (points == null) { return est; }

            var enabled = new List<ClickPoint>();
            foreach (ClickPoint p in points)
            {
                if (p != null && p.Enabled) { enabled.Add(p); }
            }
            if (enabled.Count == 0) { return est; }

            double interval = EffectiveIntervalMs(configuredIntervalMs, capCps);
            est.Capped = interval > configuredIntervalMs + 0.0001;

            // The visit order of one cycle, so uneven dwells are counted where they fall.
            var visits = new List<ClickPoint>(enabled);
            if (order == MultiPointOrder.PingPong && enabled.Count > 2)
            {
                for (int i = enabled.Count - 2; i >= 1; i--) { visits.Add(enabled[i]); }
            }

            if (holdMs < 0) { holdMs = 0; }
            foreach (ClickPoint p in visits)
            {
                int repeat = p.Repeat < 1 ? 1 : p.Repeat;
                int perVisit = ClicksForStyle(p.Style);
                est.Visits += repeat;
                est.Clicks += (long)repeat * perVisit;
                double gap = p.DwellMilliseconds > 0 ? p.DwellMilliseconds : interval;
                est.Milliseconds += repeat * (gap + (double)holdMs * perVisit);
            }
            return est;
        }
    }
}
