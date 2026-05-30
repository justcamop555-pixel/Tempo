using System;

namespace AutoClicker.Models
{
    /// <summary>
    /// A single target coordinate used in multi-point clicking. Each point can
    /// override the global button/style and define its own dwell delay before the
    /// engine advances to the next point.
    /// </summary>
    public sealed class ClickPoint
    {
        /// <summary>Optional friendly label shown in the UI list.</summary>
        public string Label { get; set; } = "Point";

        /// <summary>Screen X coordinate.</summary>
        public int X { get; set; }

        /// <summary>Screen Y coordinate.</summary>
        public int Y { get; set; }

        /// <summary>Mouse button used at this point.</summary>
        public MouseButtonType Button { get; set; } = MouseButtonType.Left;

        /// <summary>Single / double / triple click at this point.</summary>
        public ClickStyle Style { get; set; } = ClickStyle.Single;

        /// <summary>
        /// Milliseconds to wait after clicking this point before moving on. When
        /// zero the profile's global interval is used instead.
        /// </summary>
        public int DwellMilliseconds { get; set; } = 0;

        /// <summary>
        /// How many times to click this point before advancing to the next one.
        /// Minimum 1.
        /// </summary>
        public int Repeat { get; set; } = 1;

        /// <summary>Whether this point participates in the cycle.</summary>
        public bool Enabled { get; set; } = true;

        public ClickPoint()
        {
        }

        public ClickPoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public ClickPoint Clone()
        {
            return new ClickPoint
            {
                Label = Label,
                X = X,
                Y = Y,
                Button = Button,
                Style = Style,
                DwellMilliseconds = DwellMilliseconds,
                Repeat = Repeat,
                Enabled = Enabled
            };
        }

        public override string ToString()
        {
            return $"{Label} ({X}, {Y}) - {Button}/{Style}";
        }
    }
}
