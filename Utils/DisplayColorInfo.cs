using System;
using System.Runtime.InteropServices;

namespace AutoClicker.Utils
{
    /// <summary>
    /// What the display is actually capable of showing, and whether Windows is altering
    /// Tempo's colours on the way to it.
    ///
    /// This exists because "the theme doesn't look like the colours you picked" has real
    /// causes that live entirely outside the app, and Tempo had no way to see any of them:
    ///
    ///  • HDR / Advanced Color turned ON. Tempo is an SDR application, so Windows
    ///    tone-maps everything it draws into the HDR signal. Dark themes come out milky
    ///    and greys lift; the app is painting exactly the colour it was told to and the
    ///    compositor is changing it afterwards. This is by far the most common cause, and
    ///    it is invisible from inside the process — GetPixel would still read the colour
    ///    we drew.
    ///  • A desktop below 32-bit colour. At 16-bit, every gradient and every blended
    ///    surface banding-steps, and near-identical greys collapse into one another.
    ///  • Wide-gamut panels running without colour management, where an sRGB value is
    ///    displayed against a much larger gamut and comes out oversaturated.
    ///
    /// Everything here is read-only and best-effort: it explains what is happening rather
    /// than trying to correct it, because an SDR app cannot undo the compositor's
    /// tone-mapping from inside itself, and guessing a correction would make the colours
    /// wrong in a second, less predictable way.
    /// </summary>
    public static class DisplayColorInfo
    {
        // ── Colour depth (already available; nothing was reading it) ─────────

        private const int ENUM_CURRENT_SETTINGS = -1;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2;
            public int dmPanningWidth, dmPanningHeight;
        }

        /// <summary>Desktop colour depth in bits per pixel, or 0 when it cannot be read.</summary>
        public static int BitsPerPixel
        {
            get
            {
                try
                {
                    var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
                    if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm))
                    {
                        return dm.dmBitsPerPel;
                    }
                }
                catch { }
                return 0;
            }
        }

        // ── HDR / Advanced Color ─────────────────────────────────────────────

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID { public uint LowPart; public int HighPart; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
        {
            public uint type; public uint size; public LUID adapterId; public uint id;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            /// <summary>Bit 0 supported, bit 1 enabled, bit 2 wideColorEnforced, bit 3 forceDisabled.</summary>
            public uint value;
            public uint colorEncoding;
            public uint bitsPerColorChannel;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_RATIONAL { public uint Numerator, Denominator; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_SOURCE_INFO
        {
            public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_TARGET_INFO
        {
            public LUID adapterId; public uint id; public uint modeInfoIdx;
            public uint outputTechnology, rotation, scaling;
            public DISPLAYCONFIG_RATIONAL refreshRate;
            public uint scanLineOrdering;
            [MarshalAs(UnmanagedType.Bool)] public bool targetAvailable;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_INFO
        {
            public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
            public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
            public uint flags;
        }

        /// <summary>Never inspected — only its SIZE matters, so QueryDisplayConfig can fill it.</summary>
        [StructLayout(LayoutKind.Sequential, Size = 64)]
        private struct DISPLAYCONFIG_MODE_INFO { }

        [DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements,
                                                              out uint numModeInfoArrayElements);

        [DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements,
            [Out] DISPLAYCONFIG_PATH_INFO[] pathArray, ref uint numModeInfoArrayElements,
            [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO requestPacket);

        private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
        private const uint DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9;

        /// <summary>The panel's advanced-colour state, as Windows reports it.</summary>
        public struct AdvancedColor
        {
            public bool Queried;        // the API answered at all
            public bool Supported;      // some panel can do HDR
            public bool Enabled;        // HDR is ON somewhere — SDR apps are tone-mapped
            public bool WideGamut;      // wide colour enforced
            public uint BitsPerChannel; // of the first output that answered

            /// <summary>Active outputs the query answered for.</summary>
            public int Outputs;

            /// <summary>How many of them have HDR switched on.</summary>
            public int OutputsHdrOn;

            /// <summary>True when the outputs disagree — one HDR, one not.</summary>
            public bool Mixed => Outputs > 1 && OutputsHdrOn > 0 && OutputsHdrOn < Outputs;
        }

        private static AdvancedColor? _cached;

        /// <summary>
        /// Advanced-colour state for the primary output. Cached: it needs two P/Invokes
        /// and a display-config query, and it can only change when the user changes a
        /// display setting — which Tempo already handles by restarting its theme pass.
        /// </summary>
        public static AdvancedColor Advanced()
        {
            if (_cached.HasValue) { return _cached.Value; }
            var result = new AdvancedColor();
            try
            {
                if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount,
                                                out uint modeCount) != 0 || pathCount == 0)
                {
                    _cached = result; return result;
                }

                var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
                var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
                if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths,
                                       ref modeCount, modes, IntPtr.Zero) != 0)
                {
                    _cached = result; return result;
                }

                // EVERY active output, not just the first. Outputs genuinely differ — the
                // machine this was written on has HDR on across two panels running at 8
                // and 10 bits per channel — and Tempo's window can be dragged onto any of
                // them, so "is anything tone-mapping us?" is the question that matters.
                // Mixed setups are reported as mixed rather than averaged into a fiction.
                for (int i = 0; i < pathCount; i++)
                {
                    var req = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO();
                    req.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO;
                    req.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO));
                    req.header.adapterId = paths[i].targetInfo.adapterId;
                    req.header.id = paths[i].targetInfo.id;

                    if (DisplayConfigGetDeviceInfo(ref req) != 0) { continue; }

                    bool enabled = (req.value & 0x2) != 0;
                    if (!result.Queried)
                    {
                        result.Queried = true;
                        result.BitsPerChannel = req.bitsPerColorChannel;
                    }
                    result.Outputs++;
                    if (enabled) { result.OutputsHdrOn++; }
                    result.Supported |= (req.value & 0x1) != 0;
                    result.Enabled |= enabled;
                    result.WideGamut |= (req.value & 0x4) != 0;
                }
            }
            catch (Exception ex) { Logger.Swallow("DisplayColorInfo.Advanced", ex); }

            _cached = result;
            return result;
        }

        /// <summary>Forgets the cached answer, for when the display configuration changes.</summary>
        public static void Invalidate() { _cached = null; }

        /// <summary>
        /// True when Windows is altering Tempo's colours before they reach the panel.
        /// This is the answer to "the theme looks washed out / not true colour".
        /// </summary>
        public static bool SdrIsToneMapped => Advanced().Enabled;

        /// <summary>True when the desktop is below 32-bit and gradients will band.</summary>
        public static bool ReducedColourDepth
        {
            get { int bpp = BitsPerPixel; return bpp > 0 && bpp < 32; }
        }

        /// <summary>
        /// One line for About and Live debug: what the display is doing to the colours.
        /// </summary>
        public static string Describe()
        {
            try
            {
                var a = Advanced();
                int bpp = BitsPerPixel;

                var sb = new System.Text.StringBuilder();
                sb.Append(bpp > 0 ? bpp + "-bit desktop" : "colour depth unknown");
                if (bpp == 32) { sb.Append(" (true colour)"); }

                if (a.Queried)
                {
                    if (a.BitsPerChannel > 0) { sb.Append(" · ").Append(a.BitsPerChannel).Append(" bits/channel"); }
                    if (a.Enabled)
                    {
                        sb.Append(" · HDR ON");
                        if (a.Outputs > 1) { sb.Append(" (").Append(a.OutputsHdrOn).Append('/').Append(a.Outputs).Append(" outputs)"); }
                    }
                    else
                    {
                        sb.Append(a.Supported ? " · HDR available, off" : " · SDR panel");
                    }
                    if (a.WideGamut) { sb.Append(" · wide gamut"); }
                }
                return sb.ToString();
            }
            catch { return "display colour info unavailable"; }
        }

        /// <summary>
        /// The plain-English consequence, or empty when nothing is interfering. Kept
        /// separate from <see cref="Describe"/> so the UI can show the facts always and
        /// the warning only when there is something to warn about.
        /// </summary>
        public static string ColourAccuracyNote()
        {
            var a = Advanced();
            if (a.Enabled)
            {
                return "Windows HDR is on. Tempo draws in SDR, so Windows tone-maps its "
                     + "colours into the HDR signal — dark themes look milky and greys lift. "
                     + "The theme is painting the right colours; the compositor changes them "
                     + "afterwards. Turning HDR off (or using Windows' SDR brightness slider) "
                     + "restores them.";
            }
            if (ReducedColourDepth)
            {
                return "The desktop is running at " + BitsPerPixel + "-bit colour, so gradients "
                     + "band and near-identical shades collapse together. Set the display to "
                     + "32-bit (True Color) in Windows display settings.";
            }
            return "";
        }
    }
}
