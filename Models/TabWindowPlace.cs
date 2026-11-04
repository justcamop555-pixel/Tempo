namespace AutoClicker.Models
{
    /// <summary>
    /// Where one tab's own window was last, in screen pixels, and whether it was kept on top.
    /// -1 means never placed: the window opens centred on the main window's screen.
    /// </summary>
    public sealed class TabWindowPlace
    {
        /// <summary>Fields a newer Tempo wrote, kept and written back untouched (see AppSettings.UnknownFields).</summary>
        [System.Text.Json.Serialization.JsonExtensionData]
        public System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement> UnknownFields { get; set; }

        public int Left { get; set; } = -1;
        public int Top { get; set; } = -1;
        public int Width { get; set; } = -1;
        public int Height { get; set; } = -1;

        /// <summary>The window's own "Always on top" pin.</summary>
        public bool OnTop { get; set; }

        public TabWindowPlace Clone()
        {
            return new TabWindowPlace
            {
                UnknownFields = UnknownFields,
                Left = Left,
                Top = Top,
                Width = Width,
                Height = Height,
                OnTop = OnTop
            };
        }
    }
}
