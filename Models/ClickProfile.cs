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
        /// <summary>Fields written by a newer Tempo, kept so saving here cannot erase them. See AppSettings.UnknownFields.</summary>
        [System.Text.Json.Serialization.JsonExtensionData]
        public System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement> UnknownFields { get; set; }

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

        /// <summary>When a run last started with this profile.</summary>
        public DateTime LastUsedUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// How many runs have started with this profile. It counted switches made from the
        /// Profiles tab and the tray — clicking the card already active counted again — while
        /// switching with the Clicker dropdown or the profile hotkeys never counted. A run is
        /// the same thing whichever way you got to the profile.
        /// </summary>
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

        /// <summary>
        /// Takes over everything the Clicker tab does NOT edit — the library entry (description,
        /// category, icon, colour tag, favourite star), the history (created, last used, use
        /// count, run time), the keybind and appearance snapshots, and any fields a newer Tempo
        /// wrote — from the stored copy this profile is about to replace.
        ///
        /// WHY: the Clicker tab's Save builds a fresh profile from its controls and replaces the
        /// stored one with it. Those controls carry only the clicking setup, so every press of
        /// Save (or of the Multi-Point tab's Save) wiped the rest back to defaults — the icon
        /// went back to 🎯, the star went out, "used 48× · 6h" became "never used", and a
        /// profile's saved keybinds were thrown away, silently.
        /// </summary>
        public void KeepLibraryDataOf(ClickProfile stored)
        {
            if (stored == null || ReferenceEquals(stored, this)) { return; }
            Description = stored.Description;
            Category = stored.Category;
            Icon = stored.Icon;
            ColorTagArgb = stored.ColorTagArgb;
            Favorite = stored.Favorite;
            CreatedUtc = stored.CreatedUtc;
            LastUsedUtc = stored.LastUsedUtc;
            TimesUsed = stored.TimesUsed;
            TotalRuntimeSeconds = stored.TotalRuntimeSeconds;
            Keybinds = stored.Keybinds;
            AppSettings = stored.AppSettings;
            UnknownFields = stored.UnknownFields;
        }

        /// <summary>
        /// A deep copy. EVERY property belongs in here: the click engine runs a Clone of the
        /// profile it is given, so a property this leaves out is silently switched off inside
        /// the engine. That is exactly what happened to BackgroundClick — "Click without moving
        /// my cursor" saved, loaded and showed its tick correctly, and the engine never saw it,
        /// so every run moved the real pointer. scratchpad/clonecheck sets every property and
        /// checks the copy.
        /// </summary>
        public ClickProfile Clone()
        {
            var copy = new ClickProfile
            {
                // Fields a newer Tempo wrote: kept on the copy too, as on the original.
                UnknownFields = UnknownFields != null
                    ? new Dictionary<string, System.Text.Json.JsonElement>(UnknownFields)
                    : null,
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
                BackgroundClick = BackgroundClick,
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

        /// <summary>Longest name the editors accept.</summary>
        public const int MaxNameLength = 60;

        /// <summary>Longest description the details dialog accepts.</summary>
        public const int MaxDescriptionLength = 120;

        /// <summary>
        /// Puts every value back inside what the editors can show, and returns true when
        /// anything had to change. For data that did NOT come through the editors: a
        /// hand-edited or damaged profiles.json, a file written by a newer Tempo, an import.
        ///
        /// An enum value this build does not have (a newer click style, say) set a combo's
        /// SelectedIndex out of range, and loading the profile threw half-way through — the
        /// Clicker tab was left showing a mix of two profiles. A null point crashed the load
        /// the same way. Numbers out of range (a negative interval part, a zero repeat count)
        /// never reached the clicker — it runs from the Clicker tab's controls, which clamp
        /// them on load — but the file kept them, and everything else that reads a stored
        /// profile saw them. The names are not touched here (they are identities; see
        /// ProfileManager for how clashes are resolved), nor is anything the user could have
        /// set on purpose.
        ///
        /// <paramref name="imported"/>: false when loading this PC's own file. An enum value this
        /// build does not know most likely came from a NEWER Tempo, and rewriting it would
        /// erase that build's choice for good; the Clicker tab shows the nearest thing it has
        /// instead (LoadProfileIntoUi), and only an explicit Save replaces it. A long
        /// description is the user's own text and stays whole. An import is someone else's
        /// file, so it gets the full treatment.
        /// </summary>
        public bool Sanitize(bool imported = true)
        {
            bool changed = false;
            int Fix(int v, int min, int max) { int r = v < min ? min : v > max ? max : v; if (r != v) { changed = true; } return r; }
            long FixL(long v, long min, long max) { long r = v < min ? min : v > max ? max : v; if (r != v) { changed = true; } return r; }
            TEnum FixE<TEnum>(TEnum v, TEnum fallback) where TEnum : struct, Enum
            {
                if (!imported || Enum.IsDefined(typeof(TEnum), v)) { return v; }
                changed = true;
                return fallback;
            }

            if (Description == null) { Description = string.Empty; changed = true; }
            if (imported && Description.Length > MaxDescriptionLength)
            {
                Description = Description.Substring(0, MaxDescriptionLength);
                changed = true;
            }
            // An icon is one glyph (an emoji can be a few UTF-16 units); anything longer is not one.
            if (string.IsNullOrEmpty(Icon) || Icon.Length > 16) { Icon = "🎯"; changed = true; }

            Category = FixE(Category, ProfileCategory.Custom);
            Button = FixE(Button, MouseButtonType.Left);
            Style = FixE(Style, ClickStyle.Single);
            Mode = FixE(Mode, ClickMode.Interval);
            Target = FixE(Target, ClickTarget.Mouse);
            PositionMode = FixE(PositionMode, PositionMode.CurrentPosition);
            PointOrder = FixE(PointOrder, MultiPointOrder.Sequential);
            RepeatMode = FixE(RepeatMode, RepeatMode.UntilStopped);

            IntervalHours = Fix(IntervalHours, 0, 999);
            IntervalMinutes = Fix(IntervalMinutes, 0, 59);
            IntervalSeconds = Fix(IntervalSeconds, 0, 59);
            IntervalMilliseconds = Fix(IntervalMilliseconds, 0, 999);
            if (double.IsNaN(ManualPreciseIntervalMs) || double.IsInfinity(ManualPreciseIntervalMs) || ManualPreciseIntervalMs < 0)
            {
                ManualPreciseIntervalMs = 0;
                changed = true;
            }
            KeyVirtualKey = Fix(KeyVirtualKey, 0, 255);
            FixedX = Fix(FixedX, -100000, 100000);
            FixedY = Fix(FixedY, -100000, 100000);
            RepeatCount = FixL(RepeatCount, 1, 100000000);
            RepeatDurationSeconds = Fix(RepeatDurationSeconds, 1, 86400);
            BurstSize = Fix(BurstSize, 1, 100000);
            BurstPauseMilliseconds = Fix(BurstPauseMilliseconds, 0, 3600000);
            IntervalJitterMilliseconds = Fix(IntervalJitterMilliseconds, 0, 100000);
            PositionJitterPixels = Fix(PositionJitterPixels, 0, 1000);
            ClickHoldMilliseconds = Fix(ClickHoldMilliseconds, 0, 5000);
            TimesUsed = Fix(TimesUsed, 0, int.MaxValue);
            TotalRuntimeSeconds = FixL(TotalRuntimeSeconds, 0, long.MaxValue);

            if (Points == null) { Points = new List<ClickPoint>(); changed = true; }
            for (int i = Points.Count - 1; i >= 0; i--)
            {
                ClickPoint pt = Points[i];
                if (pt == null) { Points.RemoveAt(i); changed = true; continue; }
                pt.X = Fix(pt.X, -100000, 100000);
                pt.Y = Fix(pt.Y, -100000, 100000);
                pt.Button = FixE(pt.Button, MouseButtonType.Left);
                pt.Style = FixE(pt.Style, ClickStyle.Single);
                pt.DwellMilliseconds = Fix(pt.DwellMilliseconds, 0, 3600000);
                pt.Repeat = Fix(pt.Repeat, 1, 100000);
                if (pt.Label == null) { pt.Label = "Point"; changed = true; }
            }
            if (Keybinds != null && Keybinds.Bindings == null) { Keybinds.Bindings = new List<HotkeyBinding>(); changed = true; }
            return changed;
        }

        /// <summary>
        /// Clears the usage history, for a profile that is new to this library: a copy, or an
        /// import. It is the NEW profile's history that starts now — an import used to arrive
        /// "used 48× · 6h", counted on somebody else's PC, and sorted to the top as recently used.
        /// </summary>
        public void StartHistoryNow()
        {
            CreatedUtc = DateTime.UtcNow;
            LastUsedUtc = DateTime.MinValue;
            TimesUsed = 0;
            TotalRuntimeSeconds = 0;
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
