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

        // ── Script step (Type == Script) ────────────────────────────────────────
        //
        // The path is stored as the user chose it, NOT resolved or copied. A macro is a
        // portable JSON file, so a script step travels as a reference to a file on the
        // machine that made it — which is exactly why importing a macro containing one
        // asks before it will run (see MainForm.Macros ImportMacro).

        /// <summary>Full path to the .py file (used when Type == Script).</summary>
        public string ScriptPath { get; set; } = "";

        /// <summary>
        /// How long the script may run before it is killed. Every script gets a limit:
        /// an unbounded one turns "stop the macro" into "end the process", because the
        /// step would never hand control back.
        /// </summary>
        public int ScriptTimeoutMs { get; set; } = 5000;

        /// <summary>What playback does if the script fails, times out, or is missing.</summary>
        public ScriptFailureAction ScriptOnFailure { get; set; } = ScriptFailureAction.StopMacro;

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
                VirtualKey = VirtualKey,
                ScriptPath = ScriptPath,
                ScriptTimeoutMs = ScriptTimeoutMs,
                ScriptOnFailure = ScriptOnFailure
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
                case MacroActionType.Script:
                    return $"Run script {ScriptFileName()}";
                default:
                    return $"{Type} at ({X}, {Y})";
            }
        }

        /// <summary>
        /// Just the file name for the step list — a full path is far too wide for the
        /// column, and the step dialog shows the whole thing anyway.
        /// </summary>
        public string ScriptFileName()
        {
            if (string.IsNullOrWhiteSpace(ScriptPath)) { return "(none)"; }
            try { return System.IO.Path.GetFileName(ScriptPath); }
            catch { return ScriptPath; }
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

        /// <summary>
        /// A friendly, human-readable name for an action type, for the Live Monitor and
        /// step editor. Avoids showing raw enum identifiers like "LeftDown"/"KeyUp".
        /// </summary>
        public static string FriendlyType(MacroActionType t)
        {
            switch (t)
            {
                case MacroActionType.LeftDown: return "Left ↓";
                case MacroActionType.LeftUp: return "Left ↑";
                case MacroActionType.RightDown: return "Right ↓";
                case MacroActionType.RightUp: return "Right ↑";
                case MacroActionType.MiddleDown: return "Middle ↓";
                case MacroActionType.MiddleUp: return "Middle ↑";
                case MacroActionType.MouseMove: return "Move";
                case MacroActionType.Wheel: return "Scroll";
                case MacroActionType.KeyDown: return "Key ↓";
                case MacroActionType.KeyUp: return "Key ↑";
                case MacroActionType.Delay: return "Delay";
                case MacroActionType.Script: return "Script";
                default: return t.ToString();
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

        /// <summary>Interpolate mouse movement during playback for natural motion.</summary>
        public bool SmoothMovement { get; set; } = false;

        /// <summary>
        /// When true, the speed multiplier (0.5x / 2x / 4x …) only speeds up the GAPS
        /// between actions — the time a key or mouse button is physically held down is
        /// played back at its real recorded length. Without this, a 2x/4x preset shrinks a
        /// recorded WASD hold into a tap, so the character barely moves. Default false =
        /// original behaviour (everything scales).
        /// </summary>
        public bool PreserveKeyHolds { get; set; } = false;

        /// <summary>Pinned macros sort to the top of the list and show a star.</summary>
        public bool IsFavorite { get; set; } = false;

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
                    else if (a.Type == MacroActionType.Script)
                    {
                        // A script's real run time is unknowable from here, so its timeout
                        // stands in — the one bound that is guaranteed. Counting it as
                        // zero (which is what summing only delays did) made a macro built
                        // around a 30-second script read as "≈0.2s", and the playback ETA
                        // beside it was wrong for the entire run.
                        total += a.ScriptTimeoutMs;
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
                SmoothMovement = SmoothMovement,
                PreserveKeyHolds = PreserveKeyHolds,
                IsFavorite = IsFavorite,
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

            return $"{(IsFavorite ? "★ " : "")}{Name}  •  {StepCount} steps  •  {duration}";
        }
    }
}
