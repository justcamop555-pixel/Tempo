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

        // ── Presentation / library metadata (Profiles tab) ───────────────────
        /// <summary>Category bucket: Gaming, Work, Productivity, or Custom.</summary>
        public ProfileCategory Category { get; set; } = ProfileCategory.Custom;

        /// <summary>A short emoji/glyph shown on the profile card (e.g. "🎮").</summary>
        public string Icon { get; set; } = "🎯";

        /// <summary>Colour tag as an ARGB int; 0 means "use the theme accent".</summary>
        public int ColorTagArgb { get; set; } = 0;

        /// <summary>Whether the user has starred this profile.</summary>
        public bool Favorite { get; set; } = false;

        // ── Library timestamps &amp; usage stats ───────────────────────────────
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime LastUsedUtc { get; set; } = DateTime.MinValue;

        /// <summary>How many times this profile has been set active.</summary>
        public int TimesUsed { get; set; } = 0;

        /// <summary>Cumulative clicking runtime under this profile, in seconds.</summary>
        public long TotalRuntimeSeconds { get; set; } = 0;

        // ── Captured cross-feature settings (Phase 1) ─────────────────────────
        /// <summary>Keybinds saved with this profile (null = don't apply on activate).</summary>
        public ProfileKeybinds Keybinds { get; set; } = null;

        /// <summary>App/overlay/theme settings saved with this profile (null = don't apply).</summary>
        public ProfileAppSettings AppSettings { get; set; } = null;

        // ── Interval (broken into components for the UI) ──────────────────────
        public int IntervalHours { get; set; } = 0;
        public int IntervalMinutes { get; set; } = 0;
        public int IntervalSeconds { get; set; } = 0;
        public int IntervalMilliseconds { get; set; } = 100;

        /// <summary>
        /// Optional sub-millisecond interval, in milliseconds (e.g. 0.5 for
        /// 2000 CPS), used by the manual speed slider above 1000 CPS where an
        /// integer-millisecond interval can't express the rate. 0 means "ignore;
        /// use the normal whole-millisecond interval above".
        /// </summary>
        public double ManualPreciseIntervalMs { get; set; }

        // ── Click definition ──────────────────────────────────────────────────
        public MouseButtonType Button { get; set; } = MouseButtonType.Left;
        public ClickStyle Style { get; set; } = ClickStyle.Single;
        public ClickMode Mode { get; set; } = ClickMode.Interval;

        /// <summary>Whether each actuation is a mouse click or a keyboard key press.</summary>
        public ClickTarget Target { get; set; } = ClickTarget.Mouse;

        /// <summary>The virtual-key code pressed when <see cref="Target"/> is Key (0 = unset).</summary>
        public int KeyVirtualKey { get; set; } = 0;

        // ── Position ──────────────────────────────────────────────────────────
        public PositionMode PositionMode { get; set; } = PositionMode.CurrentPosition;
        public int FixedX { get; set; } = 0;
        public int FixedY { get; set; } = 0;
        public List<ClickPoint> Points { get; set; } = new List<ClickPoint>();

        /// <summary>
        /// When true and a target point is set (fixed position or multi-point), Tempo
        /// clicks by posting the click to the window under that point instead of
        /// moving the real cursor there — a "second mouse" that leaves your real mouse
        /// free (e.g. AFK-grind a game on monitor 2 while you work on monitor 1). Only
        /// works for windows that read standard mouse messages; raw-input/DirectInput
        /// games ignore posted clicks (see InputSimulator.BackgroundClick).
        /// </summary>
        public bool BackgroundClick { get; set; } = false;

        /// <summary>Order in which multi-point targets are visited.</summary>
        public MultiPointOrder PointOrder { get; set; } = MultiPointOrder.Sequential;

        // ── Repeat ────────────────────────────────────────────────────────────
        public RepeatMode RepeatMode { get; set; } = RepeatMode.UntilStopped;
        public long RepeatCount { get; set; } = 100;
        public int RepeatDurationSeconds { get; set; } = 60;

        // ── Burst mode ────────────────────────────────────────────────────────
        public int BurstSize { get; set; } = 10;
        public int BurstPauseMilliseconds { get; set; } = 1000;

        // ── Randomization ─────────────────────────────────────────────────────
        public bool RandomizeInterval { get; set; } = false;

        /// <summary>+/- jitter applied to the interval, in milliseconds.</summary>
        public int IntervalJitterMilliseconds { get; set; } = 0;

        public bool RandomizePosition { get; set; } = false;

        /// <summary>
        /// When the run stops, move the cursor back to where it was before the run
        /// started (only meaningful for Fixed and Multi-Point position modes).
        /// </summary>
        public bool RestoreCursorOnStop { get; set; } = false;

        /// <summary>+/- jitter applied to X and Y, in pixels.</summary>
        public int PositionJitterPixels { get; set; } = 0;

        /// <summary>How long to hold each click's button down (ms). 0 = instant click.</summary>
        public int ClickHoldMilliseconds { get; set; } = 0;

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

            if (Target == ClickTarget.Key && KeyVirtualKey == 0)
            {
                return "Choose a key to auto-press first (Click Options → Set key).";
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

            if (RepeatMode == RepeatMode.ForDuration && RepeatDurationSeconds < 1)
            {
                return "Run duration must be at least 1 second.";
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

            if (ClickHoldMilliseconds < 0)
            {
                return "Click hold time cannot be negative.";
            }

            return null;
        }

        public ClickProfile Clone()
        {
            var copy = new ClickProfile
            {
                Name = Name,
                Description = Description,
                Category = Category,
                Icon = Icon,
                ColorTagArgb = ColorTagArgb,
                Favorite = Favorite,
                CreatedUtc = CreatedUtc,
                LastUsedUtc = LastUsedUtc,
                TimesUsed = TimesUsed,
                TotalRuntimeSeconds = TotalRuntimeSeconds,
                Keybinds = Keybinds?.Clone(),
                AppSettings = AppSettings?.Clone(),
                IntervalHours = IntervalHours,
                IntervalMinutes = IntervalMinutes,
                IntervalSeconds = IntervalSeconds,
                IntervalMilliseconds = IntervalMilliseconds,
                ManualPreciseIntervalMs = ManualPreciseIntervalMs,
                Button = Button,
                Style = Style,
                Mode = Mode,
                Target = Target,
                KeyVirtualKey = KeyVirtualKey,
                PositionMode = PositionMode,
                FixedX = FixedX,
                FixedY = FixedY,
                RepeatMode = RepeatMode,
                RepeatCount = RepeatCount,
                RepeatDurationSeconds = RepeatDurationSeconds,
                BurstSize = BurstSize,
                BurstPauseMilliseconds = BurstPauseMilliseconds,
                RandomizeInterval = RandomizeInterval,
                IntervalJitterMilliseconds = IntervalJitterMilliseconds,
                RandomizePosition = RandomizePosition,
                RestoreCursorOnStop = RestoreCursorOnStop,
                PositionJitterPixels = PositionJitterPixels,
                ClickHoldMilliseconds = ClickHoldMilliseconds,
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
