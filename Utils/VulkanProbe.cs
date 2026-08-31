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
        private static string _summary;

        /// <summary>True when the Vulkan runtime (vulkan-1.dll) is installed at all.</summary>
        public static bool RuntimePresent { get { Probe(); return _runtimePresent; } }

        /// <summary>True when Vulkan reports at least one physical device.</summary>
        public static bool HasUsableDevice { get { Probe(); return _hasDevice; } }

        /// <summary>
        /// One line for the UI: the GPU name and Vulkan version, or the reason the GPU
        /// engine cannot be used here.
        /// </summary>
        public static string Summary { get { Probe(); return _summary; } }

        private static void Probe()
        {
            if (_probed)
            {
                return;
            }
            _probed = true;
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
                // and only the leading fields are read.
                IntPtr props = Marshal.AllocHGlobal(2048);
                try
                {
                    VkGetPhysicalDeviceProperties(handles[0], props);
                    uint api = (uint)Marshal.ReadInt32(props, 0);
                    string name = Marshal.PtrToStringAnsi(props + 20) ?? "GPU";
                    _hasDevice = true;
                    _summary = name.Trim() + " · Vulkan " +
                               ((api >> 22) & 0x7F) + "." + ((api >> 12) & 0x3FF) + "." + (api & 0xFFF) +
                               (count > 1 ? " (+" + (count - 1) + " more)" : "");
                    Logger.Info("[Captions] Vulkan probe: " + _summary);
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
