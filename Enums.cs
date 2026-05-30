namespace AutoClicker.Models
{
    /// <summary>Which physical mouse button to actuate.</summary>
    public enum MouseButtonType
    {
        Left = 0,
        Right = 1,
        Middle = 2
    }

    /// <summary>How many clicks make up a single "actuation".</summary>
    public enum ClickStyle
    {
        Single = 0,
        Double = 1,
        Triple = 2
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
        FixedCount = 1
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
        KeyUp = 10
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
        Dracula = 9
    }
}
