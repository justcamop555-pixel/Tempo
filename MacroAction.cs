using System;
using System.Collections.Generic;

namespace AutoClicker.Models
{
    /// <summary>
    /// One step in a recorded macro. Depending on <see cref="Type"/> the X/Y or
    /// the delay/wheel fields are meaningful.
    /// </summary>
    public sealed class MacroAction
    {
        public MacroActionType Type { get; set; }
        public int X { get; set; }
        public int Y { get; set; }

        /// <summary>Delay in milliseconds (used when Type == Delay).</summary>
        public int DelayMilliseconds { get; set; }

        /// <summary>Wheel delta (used when Type == Wheel).</summary>
        public int WheelDelta { get; set; }

        /// <summary>Virtual-key code (used when Type == KeyDown / KeyUp).</summary>
        public int VirtualKey { get; set; }

        public MacroAction()
        {
        }

        public MacroAction(MacroActionType type)
        {
            Type = type;
        }

        public MacroAction Clone()
        {
            return new MacroAction
            {
                Type = Type,
                X = X,
                Y = Y,
                DelayMilliseconds = DelayMilliseconds,
                WheelDelta = WheelDelta,
                VirtualKey = VirtualKey
            };
        }

        public override string ToString()
        {
            switch (Type)
            {
                case MacroActionType.Delay:
                    return $"Wait {DelayMilliseconds} ms";
                case MacroActionType.MouseMove:
                    return $"Move to ({X}, {Y})";
                case MacroActionType.Wheel:
                    return $"Wheel {WheelDelta}";
                case MacroActionType.KeyDown:
                    return $"Key down {KeyName(VirtualKey)}";
                case MacroActionType.KeyUp:
                    return $"Key up {KeyName(VirtualKey)}";
                default:
                    return $"{Type} at ({X}, {Y})";
            }
        }

        /// <summary>Best-effort friendly name for a virtual-key code.</summary>
        public static string KeyName(int vk)
        {
            try
            {
                var key = (System.Windows.Forms.Keys)vk;
                return key.ToString();
            }
            catch
            {
                return "0x" + vk.ToString("X2");
            }
        }
    }

    /// <summary>
    /// A named sequence of <see cref="MacroAction"/> steps.
    /// </summary>
    public sealed class Macro
    {
        /// <summary>
        /// Schema version of the macro file. Incremented whenever the macro file
        /// format changes in a backwards-incompatible way so older recordings can
        /// be migrated cleanly.
        /// </summary>
        public int Version { get; set; } = 1;

        public string Name { get; set; } = "Recorded Macro";
        public List<MacroAction> Actions { get; set; } = new List<MacroAction>();
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Default loop count for this macro (0 = infinite). Loaded into the
        /// playback UI whenever the macro is selected, and written back when the
        /// user changes it, so each macro remembers its own preferred settings.
        /// </summary>
        public int DefaultLoops { get; set; } = 1;

        /// <summary>
        /// Default playback speed multiplier stored as an integer in the same
        /// 1..100 range the UI shows (10 = 1.0x). Per-macro so different macros
        /// can have different natural pacing.
        /// </summary>
        public int DefaultSpeed { get; set; } = 10;

        /// <summary>
        /// Seconds of "3, 2, 1, GO" countdown shown before playback actually
        /// starts. Zero disables the countdown. Useful when you need to switch
        /// windows after pressing Play.
        /// </summary>
        public int PreplayCountdownSeconds { get; set; } = 0;

        /// <summary>How many times this macro has been played.</summary>
        public int TimesPlayed { get; set; }

        /// <summary>Milliseconds to wait between loops during playback (0 = none).</summary>
        public int LoopDelayMs { get; set; } = 0;

        /// <summary>Optional free-text note/description for this macro.</summary>
        public string Notes { get; set; } = "";

        /// <summary>When the macro was last played (UTC), or null if never.</summary>
        public DateTime? LastPlayedUtc { get; set; }

        public Macro()
        {
        }

        public Macro(string name)
        {
            Name = name;
        }

        public int StepCount => Actions.Count;

        /// <summary>Total estimated runtime in milliseconds (sum of delays).</summary>
        public long EstimatedDurationMs
        {
            get
            {
                long total = 0;
                foreach (var a in Actions)
                {
                    if (a.Type == MacroActionType.Delay)
                    {
                        total += a.DelayMilliseconds;
                    }
                }
                return total;
            }
        }

        public Macro Clone()
        {
            var copy = new Macro
            {
                Version = Version,
                Name = Name,
                CreatedUtc = CreatedUtc,
                DefaultLoops = DefaultLoops,
                DefaultSpeed = DefaultSpeed,
                PreplayCountdownSeconds = PreplayCountdownSeconds,
                TimesPlayed = TimesPlayed,
                LastPlayedUtc = LastPlayedUtc,
                LoopDelayMs = LoopDelayMs,
                Notes = Notes,
                Actions = new List<MacroAction>()
            };

            foreach (var a in Actions)
            {
                copy.Actions.Add(a.Clone());
            }

            return copy;
        }

        public override string ToString()
        {
            // Show step count and approximate duration alongside the name in the
            // saved-macros list — much more useful than just "Name (N steps)".
            long ms = EstimatedDurationMs;
            string duration;
            if (ms >= 60_000)
            {
                duration = $"{ms / 60_000.0:0.0} min";
            }
            else if (ms >= 1_000)
            {
                duration = $"{ms / 1_000.0:0.0} s";
            }
            else
            {
                duration = $"{ms} ms";
            }

            return $"{Name}  •  {StepCount} steps  •  {duration}";
        }
    }
}
