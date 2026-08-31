namespace AutoClicker.Models
{
    /// <summary>Which physical mouse button to actuate.</summary>
    public enum MouseButtonType
    {
        Left = 0,
        Right = 1,
        Middle = 2
    }

    /// <summary>Whether an actuation is a mouse click or a keyboard key press.</summary>
    public enum ClickTarget
    {
        Mouse = 0,
        Key = 1
    }

    /// <summary>How many clicks make up a single "actuation".</summary>
    public enum ClickStyle
    {
        Single = 0,
        Double = 1,
        Triple = 2,
        Quadruple = 3
    }

    /// <summary>Where the clicks land.</summary>
    public enum PositionMode
    {
        /// <summary>Click wherever the cursor currently is.</summary>
        CurrentPosition = 0,

        /// <summary>Click at a single fixed coordinate.</summary>
        FixedPosition = 1,

        /// <summary>Cycle through a list of coordinates.</summary>
        MultiPoint = 2
    }

    /// <summary>Overall behaviour of the clicking engine.</summary>
    public enum ClickMode
    {
        /// <summary>Repeat clicks on a timer until stopped.</summary>
        Interval = 0,

        /// <summary>Click only while the trigger key is held down.</summary>
        HoldToClick = 1,

        /// <summary>Click in bursts of N, pausing between bursts.</summary>
        Burst = 2
    }

    /// <summary>How long a run continues.</summary>
    public enum RepeatMode
    {
        UntilStopped = 0,
        FixedCount = 1,
        ForDuration = 2
    }

    /// <summary>Order in which the engine visits multi-point targets.</summary>
    public enum MultiPointOrder
    {
        /// <summary>1 → 2 → 3 → 1 …</summary>
        Sequential = 0,
        /// <summary>3 → 2 → 1 → 3 …</summary>
        Reverse = 1,
        /// <summary>A random enabled point each time.</summary>
        Random = 2,
        /// <summary>1 → 2 → 3 → 2 → 1 → 2 … (bounce).</summary>
        PingPong = 3
    }

    /// <summary>State of the click engine, surfaced to the UI.</summary>
    public enum EngineState
    {
        Idle = 0,
        Running = 1,
        Paused = 2
    }

    /// <summary>Kinds of action stored inside a recorded macro.</summary>
    public enum MacroActionType
    {
        MouseMove = 0,
        LeftDown = 1,
        LeftUp = 2,
        RightDown = 3,
        RightUp = 4,
        MiddleDown = 5,
        MiddleUp = 6,
        Delay = 7,
        Wheel = 8,
        KeyDown = 9,
        KeyUp = 10,

        /// <summary>
        /// Runs a Python script and waits for it. Numbered explicitly, like every value
        /// above: macros are serialised to JSON by this number, so a saved macro from an
        /// older Tempo has to keep meaning what it meant.
        /// </summary>
        Script = 11
    }

    /// <summary>What playback does when a Script step fails.</summary>
    public enum ScriptFailureAction
    {
        /// <summary>Stop the macro. The default — a failed step usually invalidates the rest.</summary>
        StopMacro = 0,
        /// <summary>Log it and play on, for a script whose result is advisory.</summary>
        Continue = 1
    }

    /// <summary>Visual theme selection.</summary>
    public enum ThemeKind
    {
        Dark = 0,
        Light = 1,
        Midnight = 2,
        Ocean = 3,
        Forest = 4,
        Crimson = 5,
        Solarized = 6,
        Amoled = 7,
        Nord = 8,
        Dracula = 9,
        Monokai = 10,
        Gruvbox = 11,
        Synthwave = 12,
        Coffee = 13,
        Cosmos = 14,
        Rose = 15,
        Slate = 16,
        Sunset = 17,
        Mint = 18,
        Sand = 19,
        Lavender = 20,
        Sakura = 21,
        Emerald = 22,
        Steel = 23,
        Grape = 24,
        Arctic = 25,
        Indigo = 26,
        Teal = 27,
        Tangerine = 28,
        Bubblegum = 29,
        Carbon = 30,
        Honey = 31,
        Sapphire = 32,
        Olive = 33,
        Cyan = 34,
        Peach = 35,
        Wine = 36,
        Magenta = 37
    }

    /// <summary>Which engine produces Live Caption text.</summary>
    public enum CaptionSource
    {
        /// <summary>Windows 11 Live Captions, mirrored into Tempo's overlay.</summary>
        Windows = 0,

        /// <summary>Tempo's own offline Whisper engine (captures system audio).</summary>
        Tempo = 1,

        /// <summary>
        /// Recommended: try Tempo's own offline engine first, and if it can't run on
        /// this PC (no speech model, missing engine files, no audio device) fall back
        /// automatically to mirroring Windows 11 Live Captions. Best of both - fully
        /// offline when possible, always captions something when not.
        /// </summary>
        Auto = 2
    }

    public enum Language
    {
        English = 0,
        Spanish = 1,
        French = 2,
        German = 3,
        Italian = 4,
        Portuguese = 5
    }
}
