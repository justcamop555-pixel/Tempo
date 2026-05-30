using System;
using System.Collections.Generic;

namespace AutoClicker.Models
{
    /// <summary>
    /// A complete, named clicking configuration. Profiles are saved to disk so the
    /// user can build a library of setups and switch between them instantly.
    /// </summary>
    public sealed class ClickProfile
    {
        // ── Identity ──────────────────────────────────────────────────────────
        public string Name { get; set; } = "New Profile";
        public string Description { get; set; } = string.Empty;

        // ── Interval (broken into components for the UI) ──────────────────────
        public int IntervalHours { get; set; } = 0;
        public int IntervalMinutes { get; set; } = 0;
        public int IntervalSeconds { get; set; } = 0;
        public int IntervalMilliseconds { get; set; } = 100;

        // ── Click definition ──────────────────────────────────────────────────
        public MouseButtonType Button { get; set; } = MouseButtonType.Left;
        public ClickStyle Style { get; set; } = ClickStyle.Single;
        public ClickMode Mode { get; set; } = ClickMode.Interval;

        // ── Position ──────────────────────────────────────────────────────────
        public PositionMode PositionMode { get; set; } = PositionMode.CurrentPosition;
        public int FixedX { get; set; } = 0;
        public int FixedY { get; set; } = 0;
        public List<ClickPoint> Points { get; set; } = new List<ClickPoint>();

        /// <summary>Order in which multi-point targets are visited.</summary>
        public MultiPointOrder PointOrder { get; set; } = MultiPointOrder.Sequential;

        // ── Repeat ────────────────────────────────────────────────────────────
        public RepeatMode RepeatMode { get; set; } = RepeatMode.UntilStopped;
        public long RepeatCount { get; set; } = 100;

        // ── Burst mode ────────────────────────────────────────────────────────
        public int BurstSize { get; set; } = 10;
        public int BurstPauseMilliseconds { get; set; } = 1000;

        // ── Randomization ─────────────────────────────────────────────────────
        public bool RandomizeInterval { get; set; } = false;

        /// <summary>+/- jitter applied to the interval, in milliseconds.</summary>
        public int IntervalJitterMilliseconds { get; set; } = 0;

        public bool RandomizePosition { get; set; } = false;

        /// <summary>+/- jitter applied to X and Y, in pixels.</summary>
        public int PositionJitterPixels { get; set; } = 0;

        // ── Misc ──────────────────────────────────────────────────────────────
        public bool PlaySoundOnStart { get; set; } = false;
        public bool PlaySoundOnStop { get; set; } = false;

        public ClickProfile()
        {
        }

        public ClickProfile(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Computes the base interval (before any jitter) in milliseconds. Always
        /// returns at least 1 to avoid a zero-delay busy loop.
        /// </summary>
        public long GetBaseIntervalMilliseconds()
        {
            long ms =
                (long)IntervalHours * 3_600_000L +
                (long)IntervalMinutes * 60_000L +
                (long)IntervalSeconds * 1_000L +
                IntervalMilliseconds;

            return ms < 1 ? 1 : ms;
        }

        /// <summary>
        /// Validates the profile and returns a human readable error, or null when
        /// the profile is usable.
        /// </summary>
        public string Validate()
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                return "Profile name cannot be empty.";
            }

            if (GetBaseIntervalMilliseconds() < 1)
            {
                return "Interval must be at least 1 millisecond.";
            }

            if (Mode == ClickMode.Burst)
            {
                if (BurstSize < 1)
                {
                    return "Burst size must be at least 1.";
                }

                if (BurstPauseMilliseconds < 0)
                {
                    return "Burst pause cannot be negative.";
                }
            }

            if (RepeatMode == RepeatMode.FixedCount && RepeatCount < 1)
            {
                return "Repeat count must be at least 1.";
            }

            if (PositionMode == PositionMode.MultiPoint)
            {
                int enabled = 0;
                foreach (var p in Points)
                {
                    if (p.Enabled)
                    {
                        enabled++;
                    }
                }

                if (enabled == 0)
                {
                    return "Multi-point mode needs at least one enabled point.";
                }
            }

            if (RandomizeInterval && IntervalJitterMilliseconds < 0)
            {
                return "Interval jitter cannot be negative.";
            }

            if (RandomizePosition && PositionJitterPixels < 0)
            {
                return "Position jitter cannot be negative.";
            }

            return null;
        }

        public ClickProfile Clone()
        {
            var copy = new ClickProfile
            {
                Name = Name,
                Description = Description,
                IntervalHours = IntervalHours,
                IntervalMinutes = IntervalMinutes,
                IntervalSeconds = IntervalSeconds,
                IntervalMilliseconds = IntervalMilliseconds,
                Button = Button,
                Style = Style,
                Mode = Mode,
                PositionMode = PositionMode,
                FixedX = FixedX,
                FixedY = FixedY,
                RepeatMode = RepeatMode,
                RepeatCount = RepeatCount,
                BurstSize = BurstSize,
                BurstPauseMilliseconds = BurstPauseMilliseconds,
                RandomizeInterval = RandomizeInterval,
                IntervalJitterMilliseconds = IntervalJitterMilliseconds,
                RandomizePosition = RandomizePosition,
                PositionJitterPixels = PositionJitterPixels,
                PlaySoundOnStart = PlaySoundOnStart,
                PlaySoundOnStop = PlaySoundOnStop,
                PointOrder = PointOrder,
                Points = new List<ClickPoint>()
            };

            foreach (var p in Points)
            {
                copy.Points.Add(p.Clone());
            }

            return copy;
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
