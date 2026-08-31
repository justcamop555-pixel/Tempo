using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using AutoClicker.Models;

namespace AutoClicker.Engine
{
    /// <summary>What a finding does to the macro if it is applied.</summary>
    public enum MacroFixKind
    {
        ReleaseStuckInputs,
        ClampOffScreen,
        RemoveRedundantMoves,
        RemoveEmptyDelays,
        HumaniseTiming,
        HumanisePositions,
        SplitMarathonDelay
    }

    /// <summary>How much the user should care.</summary>
    public enum MacroFindingLevel
    {
        /// <summary>The macro is broken or will misbehave — fixing is recommended.</summary>
        Problem,
        /// <summary>The macro works; this makes it better or less detectable.</summary>
        Suggestion
    }

    /// <summary>One diagnosed issue plus the repair for it.</summary>
    public sealed class MacroFinding
    {
        public MacroFixKind Kind { get; set; }
        public MacroFindingLevel Level { get; set; }

        /// <summary>Short headline, e.g. "2 inputs are never released".</summary>
        public string Title { get; set; }

        /// <summary>Why it matters and what applying the fix will do.</summary>
        public string Detail { get; set; }

        /// <summary>How many steps the repair touches.</summary>
        public int AffectedSteps { get; set; }

        public override string ToString() => Title;
    }

    /// <summary>
    /// Inspects a recorded macro for the faults that recording actually produces, and
    /// repairs them.
    ///
    /// Recording captures whatever happened, including the mistakes: a key that was
    /// still held when recording stopped, coordinates from a monitor layout that no
    /// longer exists, a stray double-move, the thirty seconds you spent reading the
    /// screen. Played back later those are not harmless — a macro that never releases
    /// Left leaves the button stuck down after playback ends, and one recorded on a
    /// second monitor clicks into empty space.
    ///
    /// Every check here is conservative: it only reports something it can also repair,
    /// the repair is described before it runs, and nothing is applied without being
    /// asked for. <see cref="Apply"/> works on a macro the caller already cloned, so
    /// the original is never modified in place by accident.
    /// </summary>
    public static class MacroDoctor
    {
        // Two mouse moves closer together than this are treated as the same place:
        // recording samples the pointer far faster than anything reacts to it.
        private const int RedundantMovePixels = 2;

        // A single gap longer than this is almost always the operator thinking, not
        // part of the task being automated.
        private const int MarathonDelayMs = 30_000;

        // Humanising jitter. Small enough to keep the macro doing its job.
        private const double TimingJitterFraction = 0.08;   // ±8% of each delay
        private const int PositionJitterPixels = 2;         // ±2 px per click

        /// <summary>Every problem and suggestion found in <paramref name="macro"/>.</summary>
        public static List<MacroFinding> Diagnose(Macro macro)
        {
            var found = new List<MacroFinding>();
            if (macro?.Actions == null || macro.Actions.Count == 0)
            {
                return found;
            }

            List<MacroAction> a = macro.Actions;

            // ── 1. Inputs that are pressed and never released ────────────────────
            // The worst recording fault there is: playback ends with the button or key
            // still down, so the mouse "sticks" or the character keeps walking.
            int stuck = CountUnreleased(a);
            if (stuck > 0)
            {
                found.Add(new MacroFinding
                {
                    Kind = MacroFixKind.ReleaseStuckInputs,
                    Level = MacroFindingLevel.Problem,
                    AffectedSteps = stuck,
                    Title = Utils.Localization.F(
                        stuck == 1 ? "1 input is never released" : "{0} inputs are never released", stuck),
                    Detail = Utils.Localization.T(
                        "The macro presses a button or key and playback ends without letting go, " +
                        "so it stays held afterwards. The fix appends the missing releases at the end.")
                });
            }

            // ── 2. Coordinates that land off every monitor ───────────────────────
            Rectangle desktop = VirtualDesktop();
            int offScreen = a.Count(s => UsesPosition(s.Type) && !desktop.Contains(s.X, s.Y));
            if (offScreen > 0)
            {
                found.Add(new MacroFinding
                {
                    Kind = MacroFixKind.ClampOffScreen,
                    Level = MacroFindingLevel.Problem,
                    AffectedSteps = offScreen,
                    Title = Utils.Localization.F(
                        offScreen == 1 ? "1 step clicks off-screen" : "{0} steps click off-screen", offScreen),
                    Detail = Utils.Localization.T(
                        "These coordinates are outside every monitor you have now — usually a macro " +
                        "recorded on a different display setup. The fix pulls them back onto the " +
                        "nearest screen so they hit something instead of nothing.")
                });
            }

            // ── 3. Redundant consecutive moves ───────────────────────────────────
            int redundant = CountRedundantMoves(a);
            if (redundant > 0)
            {
                found.Add(new MacroFinding
                {
                    Kind = MacroFixKind.RemoveRedundantMoves,
                    Level = MacroFindingLevel.Suggestion,
                    AffectedSteps = redundant,
                    Title = Utils.Localization.F(
                        redundant == 1 ? "1 duplicate move" : "{0} duplicate moves", redundant),
                    Detail = Utils.Localization.F(
                        "Consecutive moves to the same spot (within {0} px). " +
                        "Recording samples the pointer far faster than anything reacts to it. " +
                        "Removing them shortens the macro without changing what it does.",
                        RedundantMovePixels)
                });
            }

            // ── 4. Delays that wait for nothing ──────────────────────────────────
            int empties = a.Count(s => s.Type == MacroActionType.Delay && s.DelayMilliseconds <= 0);
            if (empties > 0)
            {
                found.Add(new MacroFinding
                {
                    Kind = MacroFixKind.RemoveEmptyDelays,
                    Level = MacroFindingLevel.Suggestion,
                    AffectedSteps = empties,
                    Title = Utils.Localization.F(
                        empties == 1 ? "1 empty delay" : "{0} empty delays", empties),
                    Detail = Utils.Localization.T(
                        "Wait steps of zero milliseconds. They cost a step each and wait for nothing.")
                });
            }

            // ── 5. Machine-perfect timing ────────────────────────────────────────
            // Looks for a DOMINANT repeated gap rather than demanding every gap match:
            // one long pause in the middle of an otherwise metronomic macro should not
            // hide the fact that the rest is machine-perfect.
            var delays = a.Where(s => s.Type == MacroActionType.Delay && s.DelayMilliseconds > 0)
                          .Select(s => s.DelayMilliseconds).ToList();
            if (delays.Count >= 4)
            {
                var group = delays.GroupBy(d => d).OrderByDescending(g => g.Count()).First();
                if (group.Count() >= 4 && group.Count() >= delays.Count * 0.6)
                {
                    found.Add(new MacroFinding
                    {
                        Kind = MacroFixKind.HumaniseTiming,
                        Level = MacroFindingLevel.Suggestion,
                        AffectedSteps = group.Count(),
                        Title = Utils.Localization.F("{0} of {1} gaps are exactly {2} ms",
                            group.Count(), delays.Count, group.Key),
                        Detail = Utils.Localization.F(
                            "Identical timing to the millisecond is something no hand produces. " +
                            "The fix varies each gap by up to ±{0}%, keeping the overall pace " +
                            "but removing the metronome.",
                            (int)(TimingJitterFraction * 100))
                    });
                }
            }

            // ── 6. Machine-perfect aim ───────────────────────────────────────────
            // Same reasoning as the timing check: find the most-repeated pixel instead of
            // insisting every click shares it, so one stray click elsewhere in the macro
            // doesn't mask a hundred landing on an identical spot.
            var clicks = a.Where(s => IsPress(s.Type)).ToList();
            if (clicks.Count >= 4)
            {
                var hotspot = clicks.GroupBy(s => (s.X, s.Y)).OrderByDescending(g => g.Count()).First();
                if (hotspot.Count() >= 4)
                {
                    found.Add(new MacroFinding
                    {
                        Kind = MacroFixKind.HumanisePositions,
                        Level = MacroFindingLevel.Suggestion,
                        AffectedSteps = hotspot.Count(),
                        Title = Utils.Localization.F("{0} clicks hit the exact same pixel ({1}, {2})",
                            hotspot.Count(), hotspot.Key.X, hotspot.Key.Y),
                        Detail = Utils.Localization.F(
                            "A real hand never lands twice on the same pixel. The fix scatters them " +
                            "by up to ±{0} px, which stays well inside any button.",
                            PositionJitterPixels)
                    });
                }
            }

            // ── 7. A single enormous pause ───────────────────────────────────────
            // Strictly GREATER than the cap: the repair sets the delay TO the cap, so a
            // ">=" test here would keep re-reporting a pause it had already fixed.
            int marathon = a.Count(s => s.Type == MacroActionType.Delay && s.DelayMilliseconds > MarathonDelayMs);
            if (marathon > 0)
            {
                int longest = a.Where(s => s.Type == MacroActionType.Delay).Max(s => s.DelayMilliseconds);
                found.Add(new MacroFinding
                {
                    Kind = MacroFixKind.SplitMarathonDelay,
                    Level = MacroFindingLevel.Suggestion,
                    AffectedSteps = marathon,
                    Title = Utils.Localization.F(
                        marathon == 1 ? "1 very long pause (longest {1} s)"
                                      : "{0} very long pauses (longest {1} s)",
                        marathon, longest / 1000),
                    Detail = Utils.Localization.F(
                        "Gaps this long are usually you reading the screen while recording, not part " +
                        "of the task. The fix caps them at {0} s so the macro doesn't idle.",
                        MarathonDelayMs / 1000)
                });
            }

            return found;
        }

        /// <summary>
        /// Applies the given fixes to <paramref name="macro"/> IN PLACE and returns how
        /// many steps changed. Callers pass a clone when they want to keep the original.
        /// </summary>
        public static int Apply(Macro macro, IEnumerable<MacroFinding> fixes)
        {
            if (macro?.Actions == null || fixes == null)
            {
                return 0;
            }

            int changed = 0;
            var rng = new Random();

            foreach (MacroFinding f in fixes)
            {
                switch (f.Kind)
                {
                    case MacroFixKind.ReleaseStuckInputs:
                        changed += ReleaseStuck(macro.Actions);
                        break;

                    case MacroFixKind.ClampOffScreen:
                    {
                        Rectangle desktop = VirtualDesktop();
                        foreach (MacroAction s in macro.Actions)
                        {
                            if (!UsesPosition(s.Type) || desktop.Contains(s.X, s.Y))
                            {
                                continue;
                            }
                            Rectangle near = NearestScreen(s.X, s.Y);
                            s.X = Math.Min(Math.Max(s.X, near.Left), near.Right - 1);
                            s.Y = Math.Min(Math.Max(s.Y, near.Top), near.Bottom - 1);
                            changed++;
                        }
                        break;
                    }

                    case MacroFixKind.RemoveRedundantMoves:
                    {
                        int before = macro.Actions.Count;
                        var kept = new List<MacroAction>(macro.Actions.Count);
                        MacroAction lastMove = null;
                        foreach (MacroAction s in macro.Actions)
                        {
                            if (s.Type == MacroActionType.MouseMove && lastMove != null &&
                                Math.Abs(s.X - lastMove.X) <= RedundantMovePixels &&
                                Math.Abs(s.Y - lastMove.Y) <= RedundantMovePixels)
                            {
                                continue;   // same place as the previous move
                            }
                            lastMove = s.Type == MacroActionType.MouseMove ? s : lastMove;
                            kept.Add(s);
                        }
                        macro.Actions = kept;
                        changed += before - kept.Count;
                        break;
                    }

                    case MacroFixKind.RemoveEmptyDelays:
                    {
                        int before = macro.Actions.Count;
                        macro.Actions = macro.Actions
                            .Where(s => !(s.Type == MacroActionType.Delay && s.DelayMilliseconds <= 0))
                            .ToList();
                        changed += before - macro.Actions.Count;
                        break;
                    }

                    case MacroFixKind.HumaniseTiming:
                        foreach (MacroAction s in macro.Actions)
                        {
                            if (s.Type != MacroActionType.Delay || s.DelayMilliseconds <= 0)
                            {
                                continue;
                            }
                            double jitter = (rng.NextDouble() * 2.0 - 1.0) * TimingJitterFraction;
                            int next = (int)Math.Round(s.DelayMilliseconds * (1.0 + jitter));
                            s.DelayMilliseconds = Math.Max(1, next);
                            changed++;
                        }
                        break;

                    case MacroFixKind.HumanisePositions:
                    {
                        Rectangle desktop = VirtualDesktop();
                        foreach (MacroAction s in macro.Actions)
                        {
                            if (!IsPress(s.Type))
                            {
                                continue;
                            }
                            int nx = s.X + rng.Next(-PositionJitterPixels, PositionJitterPixels + 1);
                            int ny = s.Y + rng.Next(-PositionJitterPixels, PositionJitterPixels + 1);
                            // Never let humanising push a good click off the desktop.
                            if (desktop.Contains(nx, ny))
                            {
                                s.X = nx;
                                s.Y = ny;
                                changed++;
                            }
                        }
                        break;
                    }

                    case MacroFixKind.SplitMarathonDelay:
                        foreach (MacroAction s in macro.Actions)
                        {
                            if (s.Type == MacroActionType.Delay && s.DelayMilliseconds > MarathonDelayMs)
                            {
                                s.DelayMilliseconds = MarathonDelayMs;
                                changed++;
                            }
                        }
                        break;
                }
            }

            return changed;
        }

        // ── helpers ─────────────────────────────────────────────────────────────

        private static bool UsesPosition(MacroActionType t)
        {
            return t == MacroActionType.MouseMove || IsPress(t) || IsRelease(t) ||
                   t == MacroActionType.Wheel;
        }

        private static bool IsPress(MacroActionType t)
        {
            return t == MacroActionType.LeftDown || t == MacroActionType.RightDown ||
                   t == MacroActionType.MiddleDown;
        }

        private static bool IsRelease(MacroActionType t)
        {
            return t == MacroActionType.LeftUp || t == MacroActionType.RightUp ||
                   t == MacroActionType.MiddleUp;
        }

        private static MacroActionType ReleaseFor(MacroActionType down)
        {
            switch (down)
            {
                case MacroActionType.LeftDown: return MacroActionType.LeftUp;
                case MacroActionType.RightDown: return MacroActionType.RightUp;
                case MacroActionType.MiddleDown: return MacroActionType.MiddleUp;
                default: return down;
            }
        }

        /// <summary>
        /// Walks the macro tracking what is currently held, and reports how many inputs
        /// are still down at the end. Counting per key/button rather than in total means
        /// a legitimate press-move-release pattern is never mistaken for a fault.
        /// </summary>
        private static int CountUnreleased(List<MacroAction> actions)
        {
            var buttons = new HashSet<MacroActionType>();
            var keys = new HashSet<int>();

            foreach (MacroAction s in actions)
            {
                if (IsPress(s.Type)) { buttons.Add(s.Type); }
                else if (IsRelease(s.Type))
                {
                    buttons.Remove(s.Type == MacroActionType.LeftUp ? MacroActionType.LeftDown
                                 : s.Type == MacroActionType.RightUp ? MacroActionType.RightDown
                                 : MacroActionType.MiddleDown);
                }
                else if (s.Type == MacroActionType.KeyDown) { keys.Add(s.VirtualKey); }
                else if (s.Type == MacroActionType.KeyUp) { keys.Remove(s.VirtualKey); }
            }

            return buttons.Count + keys.Count;
        }

        private static int ReleaseStuck(List<MacroAction> actions)
        {
            var buttons = new HashSet<MacroActionType>();
            var keys = new HashSet<int>();
            int lastX = 0, lastY = 0;

            foreach (MacroAction s in actions)
            {
                if (UsesPosition(s.Type)) { lastX = s.X; lastY = s.Y; }

                if (IsPress(s.Type)) { buttons.Add(s.Type); }
                else if (IsRelease(s.Type))
                {
                    buttons.Remove(s.Type == MacroActionType.LeftUp ? MacroActionType.LeftDown
                                 : s.Type == MacroActionType.RightUp ? MacroActionType.RightDown
                                 : MacroActionType.MiddleDown);
                }
                else if (s.Type == MacroActionType.KeyDown) { keys.Add(s.VirtualKey); }
                else if (s.Type == MacroActionType.KeyUp) { keys.Remove(s.VirtualKey); }
            }

            int added = 0;
            foreach (MacroActionType down in buttons)
            {
                actions.Add(new MacroAction(ReleaseFor(down)) { X = lastX, Y = lastY });
                added++;
            }
            foreach (int vk in keys)
            {
                actions.Add(new MacroAction(MacroActionType.KeyUp) { VirtualKey = vk });
                added++;
            }
            return added;
        }

        private static int CountRedundantMoves(List<MacroAction> actions)
        {
            int count = 0;
            MacroAction lastMove = null;
            foreach (MacroAction s in actions)
            {
                if (s.Type != MacroActionType.MouseMove)
                {
                    continue;
                }
                if (lastMove != null &&
                    Math.Abs(s.X - lastMove.X) <= RedundantMovePixels &&
                    Math.Abs(s.Y - lastMove.Y) <= RedundantMovePixels)
                {
                    count++;
                }
                else
                {
                    lastMove = s;
                }
            }
            return count;
        }

        private static Rectangle VirtualDesktop()
        {
            try
            {
                Rectangle r = Screen.AllScreens[0].Bounds;
                foreach (Screen s in Screen.AllScreens)
                {
                    r = Rectangle.Union(r, s.Bounds);
                }
                return r;
            }
            catch
            {
                return new Rectangle(0, 0, 1920, 1080);
            }
        }

        private static Rectangle NearestScreen(int x, int y)
        {
            try
            {
                Screen best = Screen.AllScreens[0];
                long bestDist = long.MaxValue;
                foreach (Screen s in Screen.AllScreens)
                {
                    int cx = s.Bounds.Left + s.Bounds.Width / 2;
                    int cy = s.Bounds.Top + s.Bounds.Height / 2;
                    long d = (long)(cx - x) * (cx - x) + (long)(cy - y) * (cy - y);
                    if (d < bestDist) { bestDist = d; best = s; }
                }
                return best.Bounds;
            }
            catch
            {
                return VirtualDesktop();
            }
        }
    }
}
