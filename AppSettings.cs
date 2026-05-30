using System;

namespace AutoClicker.Models
{
    /// <summary>
    /// Global application settings, persisted to disk between runs. Distinct from
    /// <see cref="ClickProfile"/>, which holds the clicking configuration.
    /// </summary>
    public sealed class AppSettings
    {
        // ── Appearance ────────────────────────────────────────────────────────
        public ThemeKind Theme { get; set; } = ThemeKind.Dark;
        public bool StartMinimizedToTray { get; set; } = false;
        public bool MinimizeToTrayOnClose { get; set; } = true;
        public bool ShowTrayNotifications { get; set; } = true;
        public bool AlwaysOnTop { get; set; } = false;

        /// <summary>Register the app to launch when the user signs in to Windows.</summary>
        public bool LaunchAtStartup { get; set; } = false;

        /// <summary>Automatically hide the window to the tray when clicking starts.</summary>
        public bool HideWhenClicking { get; set; } = false;

        /// <summary>Check for a newer Tempo version when the app starts.</summary>
        public bool CheckForUpdatesOnLaunch { get; set; } = true;

        // ── Hotkeys ───────────────────────────────────────────────────────────
        // Legacy single hotkeys, kept only so older settings files can be migrated
        // into the unified Bindings list below. New code reads Bindings.
        public HotkeyDefinition StartStopHotkey { get; set; } =
            new HotkeyDefinition(System.Windows.Forms.Keys.F6);

        public HotkeyDefinition PickPositionHotkey { get; set; } =
            new HotkeyDefinition(System.Windows.Forms.Keys.F7);

        public HotkeyDefinition EmergencyStopHotkey { get; set; } =
            new HotkeyDefinition(System.Windows.Forms.Keys.F8);

        /// <summary>The full set of action-to-hotkey bindings.</summary>
        public System.Collections.Generic.List<HotkeyBinding> Bindings { get; set; }
            = new System.Collections.Generic.List<HotkeyBinding>();

        /// <summary>
        /// Amount (milliseconds) added or removed by the increase/decrease-interval
        /// hotkeys.
        /// </summary>
        public int IntervalStepMilliseconds { get; set; } = 10;

        /// <summary>
        /// Seconds of countdown shown before clicking begins from the Start button
        /// or the Start/Toggle hotkey (not Hold mode). Zero starts immediately.
        /// </summary>
        public int ClickerStartDelaySeconds { get; set; } = 0;

        // ── Anti-freeze protection ────────────────────────────────────────────
        /// <summary>
        /// When enabled, the engine never exceeds <see cref="MaxClicksPerSecond"/>
        /// and adaptively backs off if this process's CPU usage climbs past
        /// <see cref="AntiFreezeCpuThreshold"/>, preventing the system from being
        /// frozen by an insane spam rate.
        /// </summary>
        public bool AntiFreezeEnabled { get; set; } = true;

        /// <summary>Hard ceiling on clicks per second (the anti-freeze cap).</summary>
        public int MaxClicksPerSecond { get; set; } = 200;

        /// <summary>
        /// Process CPU usage (percent of the whole machine) above which the engine
        /// throttles itself to keep the system responsive.
        /// </summary>
        public int AntiFreezeCpuThreshold { get; set; } = 80;

        // ── Behaviour ─────────────────────────────────────────────────────────
        public string LastProfileName { get; set; } = string.Empty;
        public bool RememberWindowPosition { get; set; } = true;
        public int WindowLeft { get; set; } = -1;
        public int WindowTop { get; set; } = -1;
        public bool ConfirmBeforeExitWhileRunning { get; set; } = true;
        public bool SafetyStopOnEscape { get; set; } = true;

        // ── Statistics ────────────────────────────────────────────────────────
        public long LifetimeClicks { get; set; } = 0;
        public long LifetimeSessions { get; set; } = 0;

        /// <summary>Highest clicks-per-second ever observed across all sessions.</summary>
        public double LifetimePeakCps { get; set; } = 0;

        /// <summary>Total accumulated active clicking time across all runs, in seconds.</summary>
        public long LifetimeRuntimeSeconds { get; set; } = 0;

        /// <summary>Most clicks performed in a single run.</summary>
        public long LifetimeMostClicksRun { get; set; } = 0;

        /// <summary>Longest single run, in seconds.</summary>
        public long LifetimeLongestRunSeconds { get; set; } = 0;

        public AppSettings()
        {
        }

        /// <summary>Returns a fresh settings object with default values.</summary>
        public static AppSettings CreateDefault()
        {
            return new AppSettings();
        }

        /// <summary>
        /// Guards against missing nested objects after deserialization of an older
        /// or partial settings file.
        /// </summary>
        public void EnsureConsistency()
        {
            if (StartStopHotkey == null)
            {
                StartStopHotkey = new HotkeyDefinition(System.Windows.Forms.Keys.F6);
            }

            if (PickPositionHotkey == null)
            {
                PickPositionHotkey = new HotkeyDefinition(System.Windows.Forms.Keys.F7);
            }

            if (EmergencyStopHotkey == null)
            {
                EmergencyStopHotkey = new HotkeyDefinition(System.Windows.Forms.Keys.F8);
            }

            if (LastProfileName == null)
            {
                LastProfileName = string.Empty;
            }

            EnsureBindings();
        }

        /// <summary>
        /// Makes sure <see cref="Bindings"/> is populated. If it is empty (a fresh
        /// install or an older settings file), it is seeded from the factory
        /// defaults and then overridden with any legacy single-hotkey values so the
        /// user's previous Start/Pick/Emergency keys carry over. Any actions missing
        /// from a partial list are added with their default binding.
        /// </summary>
        public void EnsureBindings()
        {
            if (Bindings == null)
            {
                Bindings = new System.Collections.Generic.List<HotkeyBinding>();
            }

            bool freshlySeeded = Bindings.Count == 0;
            if (freshlySeeded)
            {
                Bindings = HotkeyActions.DefaultBindings();

                // Carry over legacy values from pre-Bindings settings files.
                ApplyLegacy(HotkeyAction.ToggleStartStop, StartStopHotkey);
                ApplyLegacy(HotkeyAction.PickPosition, PickPositionHotkey);
                ApplyLegacy(HotkeyAction.EmergencyStop, EmergencyStopHotkey);
            }

            // Add any actions that are not present yet (e.g. new actions added in a
            // later version) using their defaults, and drop null entries.
            Bindings.RemoveAll(b => b == null);
            foreach (var info in HotkeyActions.All)
            {
                if (GetBinding(info.Action) == null)
                {
                    Bindings.Add(new HotkeyBinding(info.Action, info.Default.Clone()));
                }
            }

            foreach (var b in Bindings)
            {
                if (b.Hotkey == null)
                {
                    b.Hotkey = new HotkeyDefinition();
                }
            }
        }

        private void ApplyLegacy(HotkeyAction action, HotkeyDefinition legacy)
        {
            if (legacy == null || !legacy.IsValid)
            {
                return;
            }

            HotkeyBinding binding = GetBinding(action);
            if (binding != null)
            {
                binding.Hotkey = legacy.Clone();
            }
        }

        /// <summary>Returns the binding for an action, or null if absent.</summary>
        public HotkeyBinding GetBinding(HotkeyAction action)
        {
            if (Bindings == null)
            {
                return null;
            }

            foreach (var b in Bindings)
            {
                if (b != null && b.Action == action)
                {
                    return b;
                }
            }

            return null;
        }

        /// <summary>Returns the hotkey for an action, or an empty (unbound) one.</summary>
        public HotkeyDefinition HotkeyFor(HotkeyAction action)
        {
            HotkeyBinding b = GetBinding(action);
            return b != null && b.Hotkey != null ? b.Hotkey : new HotkeyDefinition();
        }
    }
}
