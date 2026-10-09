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
    ///    converts everything it draws into the HDR signal — and it decodes SDR with the
    ///    piecewise sRGB curve, where an SDR monitor uses gamma 2.2. The two agree on
    ///    white and mid-greys but not in the shadows: dark themes come out milky and
    ///    near-blacks lift to charcoal. The app is painting exactly the colour it was told
    ///    to and the compositor changes it afterwards, so GetPixel would still read the
    ///    colour we drew. Theme.ForHdr pre-compensates for exactly that curve difference.
    ///  • A desktop below 32-bit colour. At 16-bit, every gradient and every blended
    ///    surface banding-steps, and near-identical greys collapse into one another.
    ///  • Wide-gamut panels running without colour management, where an sRGB value is
    ///    displayed against a much larger gamut and comes out oversaturated.
    ///
    /// Everything here is read-only and best-effort: it reports what Windows is doing,
    /// per screen, and the palette decides what to do about it.
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

        /// <summary>How bright Windows makes SDR white on an HDR output (the "SDR content brightness" slider).</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_SDR_WHITE_LEVEL
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            /// <summary>In thousandths of 80 nits: 1000 = 80 nits.</summary>
            public uint SDRWhiteLevel;
        }

        /// <summary>The GDI name (\\.\DISPLAY1) of a path's SOURCE — what Screen.DeviceName reports.</summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
        }

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SDR_WHITE_LEVEL requestPacket);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

        private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
        private const int ERROR_INSUFFICIENT_BUFFER = 122;
        private const uint DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
        private const uint DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9;
        private const uint DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL = 11;

        /// <summary>One active output: which screen it drives and what Windows does to SDR colour on it.</summary>
        public struct Output
        {
            /// <summary>\\.\DISPLAY1 and so on — the same string as Screen.DeviceName; null if unknown.</summary>
            public string GdiName;
            public bool HdrSupported;
            public bool HdrOn;
            public bool WideGamut;
            public uint BitsPerChannel;

            /// <summary>SDR white in nits while HDR is on (80 at the slider's lowest, 480 at its highest); 0 if unknown.</summary>
            public double SdrWhiteNits;
        }

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

            /// <summary>Each output that answered, in Windows' path order.</summary>
            public Output[] PerOutput;

            /// <summary>The dimmest SDR white among the HDR outputs, in nits; 0 when none is known.</summary>
            public double LowestSdrWhiteNits;
        }

        /// <summary>
        /// A boxed <see cref="AdvancedColor"/>, or null. Boxed so it is read and replaced as ONE
        /// reference: the splash thread asks too, and a nullable struct of this size can be read
        /// half-written.
        /// </summary>
        private static volatile object _cached;

        /// <summary>
        /// Advanced-colour state of every active output. Cached: it is a display-config query plus
        /// three calls per output, and it only changes with the display configuration. MainForm
        /// calls <see cref="Invalidate"/> when Windows reports a display change — it used to be
        /// read ONCE, so switching HDR on or off with Tempo open left the colour correction, the
        /// Settings note and Live debug on the old answer until Tempo was restarted.
        /// </summary>
        public static AdvancedColor Advanced()
        {
            object hit = _cached;
            if (hit != null) { return (AdvancedColor)hit; }
            var result = new AdvancedColor();
            try
            {
                // The configuration can change between sizing the buffers and filling them —
                // most likely at the very moment this is asked, straight after Windows reports a
                // display change. Windows says so with ERROR_INSUFFICIENT_BUFFER and the answer
                // is to ask again. A query that still fails is NOT cached: cached, "no HDR
                // anywhere" stood until the next display change, with HDR on.
                uint pathCount = 0, modeCount = 0;
                DISPLAYCONFIG_PATH_INFO[] paths = null;
                int rc = ERROR_INSUFFICIENT_BUFFER;
                for (int attempt = 0; attempt < 3 && rc == ERROR_INSUFFICIENT_BUFFER; attempt++)
                {
                    if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out pathCount, out modeCount) != 0
                        || pathCount == 0)
                    {
                        return result;
                    }
                    paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
                    var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
                    rc = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths,
                                            ref modeCount, modes, IntPtr.Zero);
                }
                if (rc != 0) { return result; }

                // EVERY active output, not just the first. Outputs genuinely differ — the
                // machine this was written on has HDR on across two panels running at 8
                // and 10 bits per channel — and Tempo's window can be dragged onto any of
                // them, so the correction follows the window from one to the next (HdrOnFor).
                // Mixed setups are reported as mixed rather than averaged into a fiction.
                var outputs = new System.Collections.Generic.List<Output>();
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

                    var o = new Output
                    {
                        HdrSupported = (req.value & 0x1) != 0,
                        HdrOn = enabled,
                        WideGamut = (req.value & 0x4) != 0,
                        BitsPerChannel = req.bitsPerColorChannel,
                    };

                    // Which screen this is, so the palette can follow the window: a SOURCE property.
                    var name = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                    name.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
                    name.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME));
                    name.header.adapterId = paths[i].sourceInfo.adapterId;
                    name.header.id = paths[i].sourceInfo.id;
                    if (DisplayConfigGetDeviceInfo(ref name) == 0) { o.GdiName = name.viewGdiDeviceName; }

                    if (enabled)
                    {
                        var white = new DISPLAYCONFIG_SDR_WHITE_LEVEL();
                        white.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL;
                        white.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_SDR_WHITE_LEVEL));
                        white.header.adapterId = paths[i].targetInfo.adapterId;
                        white.header.id = paths[i].targetInfo.id;
                        if (DisplayConfigGetDeviceInfo(ref white) == 0 && white.SDRWhiteLevel > 0)
                        {
                            o.SdrWhiteNits = white.SDRWhiteLevel / 1000.0 * 80.0;
                            if (result.LowestSdrWhiteNits <= 0 || o.SdrWhiteNits < result.LowestSdrWhiteNits)
                            {
                                result.LowestSdrWhiteNits = o.SdrWhiteNits;
                            }
                        }
                    }
                    outputs.Add(o);
                }
                result.PerOutput = outputs.ToArray();
            }
            catch (Exception ex) { Logger.Swallow("DisplayColorInfo.Advanced", ex); }

            _cached = result;
            return result;
        }

        /// <summary>Forgets the cached answer, for when the display configuration changes.</summary>
        public static void Invalidate() { _cached = null; }

        /// <summary>
        /// Whether Windows converts SDR colour on the screen named <paramref name="gdiName"/>
        /// (Screen.DeviceName, e.g. \\.\DISPLAY2). With HDR on one screen and not another, the
        /// correction has to follow the window: applied on the SDR screen it would crush shadows
        /// that Windows had not lifted. Falls back to "any output" when the screen can't be matched.
        /// </summary>
        public static bool HdrOnFor(string gdiName)
        {
            AdvancedColor a = Advanced();
            if (!string.IsNullOrEmpty(gdiName) && a.PerOutput != null)
            {
                foreach (Output o in a.PerOutput)
                {
                    if (string.Equals(o.GdiName, gdiName, StringComparison.OrdinalIgnoreCase)) { return o.HdrOn; }
                }
            }
            return a.Enabled;
        }

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
        /// One line for Live debug: what the display is doing to the colours.
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
                        if (a.LowestSdrWhiteNits > 0) { sb.Append(" · SDR white ").Append(a.LowestSdrWhiteNits.ToString("0")).Append(" nits"); }
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
    }
}
