using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace AutoClicker.Models
{
    /// <summary>
    /// Every action in the application that can be triggered by a global hotkey.
    /// The enum name is used as the stable registration key, so values must not be
    /// renamed without a migration.
    /// </summary>
    public enum HotkeyAction
    {
        ToggleStartStop = 0,
        StartClicking = 1,
        StopClicking = 2,
        TogglePause = 3,
        PickPosition = 4,
        EmergencyStop = 5,
        NextProfile = 6,
        PreviousProfile = 7,
        IncreaseInterval = 8,
        DecreaseInterval = 9,
        ToggleAlwaysOnTop = 10,
        ToggleRecordMacro = 11,
        PlayMacro = 12,
        StopMacro = 13,
        ShowHideWindow = 14,
        ShowPointsOverlay = 15,
        ToggleAntiFreeze = 16,
        AddPointAtCursor = 17,
        PlayMacro1 = 18,
        PlayMacro2 = 19,
        PlayMacro3 = 20
    }

    /// <summary>
    /// Associates a <see cref="HotkeyAction"/> with the key combination bound to it.
    /// Serialized as part of <see cref="AppSettings"/>.
    /// </summary>
    public sealed class HotkeyBinding
    {
        public HotkeyAction Action { get; set; }
        public HotkeyDefinition Hotkey { get; set; } = new HotkeyDefinition();

        public HotkeyBinding()
        {
        }

        public HotkeyBinding(HotkeyAction action, HotkeyDefinition hotkey)
        {
            Action = action;
            Hotkey = hotkey ?? new HotkeyDefinition();
        }

        public HotkeyBinding Clone()
        {
            return new HotkeyBinding(Action, Hotkey?.Clone());
        }
    }

    /// <summary>
    /// Static metadata for each bindable action: a friendly label, a short
    /// description and the factory default hotkey. Also produces the full default
    /// binding set used when seeding or resetting.
    /// </summary>
    public static class HotkeyActions
    {
        /// <summary>Describes a single action for display in the Keybinds tab.</summary>
        public sealed class Info
        {
            public HotkeyAction Action { get; }
            public string Label { get; }
            public string Description { get; }
            public HotkeyDefinition Default { get; }

            public Info(HotkeyAction action, string label, string description, HotkeyDefinition def)
            {
                Action = action;
                Label = label;
                Description = description;
                Default = def ?? new HotkeyDefinition();
            }
        }

        // Ordered for presentation. Only the most common actions get a default key
        // so that fresh installs do not collide on obscure combinations.
        private static readonly List<Info> _all = new List<Info>
        {
            new Info(HotkeyAction.ToggleStartStop,   "Start / Stop (toggle)",   "Toggle clicking on and off.",                 new HotkeyDefinition(Keys.F6)),
            new Info(HotkeyAction.StartClicking,     "Start clicking",          "Begin clicking (no effect if already running).", new HotkeyDefinition()),
            new Info(HotkeyAction.StopClicking,      "Stop clicking",           "Stop clicking (no effect if already idle).",  new HotkeyDefinition()),
            new Info(HotkeyAction.TogglePause,       "Pause / Resume",          "Pause or resume the current run.",            new HotkeyDefinition(Keys.F9)),
            new Info(HotkeyAction.PickPosition,      "Pick position",           "Open the on-screen coordinate picker.",       new HotkeyDefinition(Keys.F7)),
            new Info(HotkeyAction.EmergencyStop,     "Emergency stop",          "Immediately stop clicking, playback and recording.", new HotkeyDefinition(Keys.F8)),
            new Info(HotkeyAction.NextProfile,       "Next profile",            "Switch to the next saved profile.",           new HotkeyDefinition()),
            new Info(HotkeyAction.PreviousProfile,   "Previous profile",        "Switch to the previous saved profile.",       new HotkeyDefinition()),
            new Info(HotkeyAction.IncreaseInterval,  "Increase interval",       "Add to the click interval on the fly.",       new HotkeyDefinition()),
            new Info(HotkeyAction.DecreaseInterval,  "Decrease interval",       "Subtract from the click interval on the fly.", new HotkeyDefinition()),
            new Info(HotkeyAction.ToggleAlwaysOnTop, "Toggle always on top",    "Pin or unpin the window.",                    new HotkeyDefinition()),
            new Info(HotkeyAction.ToggleRecordMacro, "Record macro (toggle)",   "Start or stop macro recording.",              new HotkeyDefinition()),
            new Info(HotkeyAction.PlayMacro,         "Play selected macro",     "Play the macro selected on the Macros tab.",  new HotkeyDefinition()),
            new Info(HotkeyAction.StopMacro,         "Stop macro playback",     "Stop the currently playing macro.",           new HotkeyDefinition()),
            new Info(HotkeyAction.ShowHideWindow,    "Show / Hide window",      "Toggle the main window visibility.",          new HotkeyDefinition()),
            new Info(HotkeyAction.ShowPointsOverlay, "Show points on screen",   "Flash numbered markers at the multi-point targets.", new HotkeyDefinition()),
            new Info(HotkeyAction.ToggleAntiFreeze,  "Toggle anti-freeze",      "Enable or disable anti-freeze protection.",   new HotkeyDefinition()),
            new Info(HotkeyAction.AddPointAtCursor,  "Add point at cursor",     "Append a multi-point target at the current cursor position.", new HotkeyDefinition()),
            new Info(HotkeyAction.PlayMacro1,        "Play macro #1",           "Play the 1st macro in the list.",             new HotkeyDefinition()),
            new Info(HotkeyAction.PlayMacro2,        "Play macro #2",           "Play the 2nd macro in the list.",             new HotkeyDefinition()),
            new Info(HotkeyAction.PlayMacro3,        "Play macro #3",           "Play the 3rd macro in the list.",             new HotkeyDefinition())
        };

        public static IReadOnlyList<Info> All => _all;

        public static Info Get(HotkeyAction action)
        {
            foreach (var info in _all)
            {
                if (info.Action == action)
                {
                    return info;
                }
            }

            return null;
        }

        public static string LabelFor(HotkeyAction action)
        {
            Info info = Get(action);
            return info != null ? info.Label : action.ToString();
        }

        /// <summary>Builds the factory default set of bindings.</summary>
        public static List<HotkeyBinding> DefaultBindings()
        {
            var list = new List<HotkeyBinding>();
            foreach (var info in _all)
            {
                list.Add(new HotkeyBinding(info.Action, info.Default.Clone()));
            }
            return list;
        }
    }
}
