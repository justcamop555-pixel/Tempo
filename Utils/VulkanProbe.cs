using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Answers "can the GPU speech engine actually work on this PC?" BEFORE Tempo
    /// commits to it.
    ///
    /// The Vulkan engine is chosen once per process and cannot be swapped afterwards,
    /// so a machine with no Vulkan driver used to enable the setting, restart, quietly
    /// load the CPU engine instead, and leave the user believing the GPU was doing the
    /// work. This asks Vulkan directly: is the runtime installed, and is there a real
    /// device behind it? The answer names the GPU, so the caption settings and Live
    /// debug can say what will happen instead of guessing.
    ///
    /// Everything is wrapped: a missing or broken driver must never do more than
    /// report "no usable GPU".
    /// </summary>
    public static class VulkanProbe
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibraryW(string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr module);

        [DllImport("vulkan-1.dll", EntryPoint = "vkCreateInstance")]
        private static extern int VkCreateInstance(ref VkInstanceCreateInfo info, IntPtr alloc, out IntPtr instance);

        [DllImport("vulkan-1.dll", EntryPoint = "vkDestroyInstance")]
        private static extern void VkDestroyInstance(IntPtr instance, IntPtr alloc);

        [DllImport("vulkan-1.dll", EntryPoint = "vkEnumeratePhysicalDevices")]
        private static extern int VkEnumeratePhysicalDevices(IntPtr instance, ref uint count, IntPtr[] devices);

        [DllImport("vulkan-1.dll", EntryPoint = "vkGetPhysicalDeviceProperties")]
        private static extern void VkGetPhysicalDeviceProperties(IntPtr device, IntPtr properties);

        [StructLayout(LayoutKind.Sequential)]
        private struct VkInstanceCreateInfo
        {
            public int SType;                 // VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO = 1
            public IntPtr Next;
            public uint Flags;
            public IntPtr ApplicationInfo;
            public uint EnabledLayerCount;
            public IntPtr EnabledLayerNames;
            public uint EnabledExtensionCount;
            public IntPtr EnabledExtensionNames;
        }

        private static bool _probed;
        private static bool _runtimePresent;
        private static bool _hasDevice;
        private static bool _hasDiscrete;
        private static string _summary;

        // VkPhysicalDeviceType.
        private const int TypeOther = 0, TypeIntegrated = 1, TypeDiscrete = 2, TypeVirtual = 3, TypeCpu = 4;

        /// <summary>True when the Vulkan runtime (vulkan-1.dll) is installed at all.</summary>
        public static bool RuntimePresent { get { Probe(); return _runtimePresent; } }

        /// <summary>
        /// True when Vulkan reports a graphics device that is a real GPU: integrated, discrete or virtual. Not a Vulkan
        /// driver that draws on the processor (device type "CPU": SwiftShader, lavapipe — what VMs and remote sessions
        /// often have): the GPU engine there is the CPU engine, slower. It used to count, as the first device whatever
        /// it was.
        /// </summary>
        public static bool HasUsableDevice { get { Probe(); return _hasDevice; } }

        /// <summary>
        /// True when one of the devices is a discrete graphics card — what captions use on their own, without being asked
        /// (MainForm.WantGpuCaptionEngine). An integrated GPU is used only when the user asks for the GPU engine.
        /// </summary>
        public static bool HasDiscreteDevice { get { Probe(); return _hasDiscrete; } }

        /// <summary>Whether the probe has run: <see cref="HasUsableDevice"/> then answers at once instead of loading
        /// Vulkan — what a caller on the UI thread asks first.</summary>
        public static bool Probed => _probed;

        /// <summary>
        /// One line for the UI: the GPU name and Vulkan version, or the reason the GPU
        /// engine cannot be used here.
        /// </summary>
        public static string Summary { get { Probe(); return _summary; } }

        private static string TypeName(int type) =>
            type == TypeDiscrete ? "discrete" : type == TypeIntegrated ? "integrated" : type == TypeVirtual ? "virtual"
            : type == TypeCpu ? "software" : "other";

        private static readonly object _probeLock = new object();

        /// <summary>
        /// Runs the probe once, whichever thread asks first; a second caller waits for its answer. "Probed" is set only
        /// once the answer is in: it used to be set FIRST, so a caller racing the start-up probe (captions starting on the
        /// UI thread while a background probe ran) read "no GPU" off a probe still under way.
        /// </summary>
        private static void Probe()
        {
            if (System.Threading.Volatile.Read(ref _probed)) { return; }
            lock (_probeLock)
            {
                if (_probed) { return; }
                try { ProbeCore(); }
                finally { System.Threading.Volatile.Write(ref _probed, true); }
            }
        }

        private static void ProbeCore()
        {
            _summary = "GPU engine unavailable — Vulkan could not be queried.";

            // 1. Is the loader even installed? Probing this first means a machine with
            //    no Vulkan at all never pays for a failed P/Invoke stack unwind.
            IntPtr module = IntPtr.Zero;
            try
            {
                module = LoadLibraryW("vulkan-1.dll");
            }
            catch { }

            if (module == IntPtr.Zero)
            {
                _runtimePresent = false;
                _summary = "no Vulkan runtime (vulkan-1.dll) — update your graphics driver to use the GPU engine";
                Logger.Info("[Captions] Vulkan probe: runtime not installed.");
                return;
            }

            _runtimePresent = true;
            try { FreeLibrary(module); } catch { }

            // 2. A runtime with no device behind it is common on remote desktop and in
            //    VMs, and is exactly the case that used to fall back silently.
            IntPtr instance = IntPtr.Zero;
            try
            {
                var info = new VkInstanceCreateInfo { SType = 1 };
                int rc = VkCreateInstance(ref info, IntPtr.Zero, out instance);
                if (rc != 0 || instance == IntPtr.Zero)
                {
                    _summary = "Vulkan is installed but refused to start (error " + rc +
                               ") — the CPU engine will be used";
                    Logger.Info("[Captions] Vulkan probe: vkCreateInstance failed (" + rc + ").");
                    return;
                }

                uint count = 0;
                VkEnumeratePhysicalDevices(instance, ref count, null);
                if (count == 0)
                {
                    _summary = "Vulkan is installed but reports no GPU — the CPU engine will be used";
                    Logger.Info("[Captions] Vulkan probe: no physical devices.");
                    return;
                }

                var handles = new IntPtr[count];
                VkEnumeratePhysicalDevices(instance, ref count, handles);

                // VkPhysicalDeviceProperties: apiVersion(4) driverVersion(4) vendorID(4)
                // deviceID(4) deviceType(4) deviceName[256]… — well under 2 KB in total,
                // and only the leading fields are read. Every device, not only the first: the
                // first was taken whatever it was, a software renderer included.
                IntPtr props = Marshal.AllocHGlobal(2048);
                try
                {
                    string bestName = null, firstName = null;
                    uint bestApi = 0;
                    int bestRank = -1, usable = 0;
                    var kinds = new System.Collections.Generic.List<string>();
                    foreach (IntPtr h in handles)
                    {
                        VkGetPhysicalDeviceProperties(h, props);
                        uint api = (uint)Marshal.ReadInt32(props, 0);
                        int type = Marshal.ReadInt32(props, 16);
                        string name = (Marshal.PtrToStringAnsi(props + 20) ?? "GPU").Trim();
                        if (firstName == null) { firstName = name; }
                        kinds.Add(name + " (" + TypeName(type) + ")");
                        // Discrete, then integrated, then virtual; a CPU renderer or an unknown kind is never a GPU.
                        int rank = type == TypeDiscrete ? 3 : type == TypeIntegrated ? 2 : type == TypeVirtual ? 1 : -1;
                        if (rank < 0) { continue; }
                        usable++;
                        if (type == TypeDiscrete) { _hasDiscrete = true; }
                        if (rank > bestRank) { bestRank = rank; bestName = name; bestApi = api; }
                    }
                    if (bestName == null)
                    {
                        _summary = "Vulkan reports only a software renderer (" + firstName + ") — the CPU engine will be used";
                        Logger.Info("[Captions] Vulkan probe: no real GPU — " + string.Join(", ", kinds) + ".");
                        return;
                    }
                    _hasDevice = true;
                    _summary = bestName + " · Vulkan " +
                               ((bestApi >> 22) & 0x7F) + "." + ((bestApi >> 12) & 0x3FF) + "." + (bestApi & 0xFFF) +
                               (usable > 1 ? " (+" + (usable - 1) + " more)" : "");
                    Logger.Info("[Captions] Vulkan probe: " + _summary + (count > 1 ? " — devices: " + string.Join(", ", kinds) : "") + ".");
                }
                finally
                {
                    Marshal.FreeHGlobal(props);
                }
            }
            catch (Exception ex)
            {
                _summary = "GPU engine unavailable — " + ex.GetType().Name + " while querying Vulkan";
                Logger.Info("[Captions] Vulkan probe failed: " + ex.Message);
            }
            finally
            {
                if (instance != IntPtr.Zero)
                {
                    try { VkDestroyInstance(instance, IntPtr.Zero); } catch { }
                }
            }
        }
    }
}
