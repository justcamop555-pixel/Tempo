using System;
using System.IO;
using System.Runtime.InteropServices;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Verifies that the SINGLE-FILE bundle actually delivered everything Tempo needs at
    /// run time, and says so out loud.
    ///
    /// Why this exists: Tempo ships as one self-contained Tempo.exe with the .NET runtime
    /// AND the native speech libraries (whisper / ggml / NAudio) packed inside. At start-up
    /// the single-file host extracts those natives to a temp folder and adds it to the
    /// native search path. That normally just works — but on a real user's machine it can
    /// fail quietly:
    ///
    ///   • antivirus or an enterprise policy blocks writing/executing from %TEMP%;
    ///   • a "temp cleaner" deletes the extraction folder while Tempo is running;
    ///   • %TEMP% is full, redirected, or on a read-only/roaming location;
    ///   • the app is launched from a path the extractor can't stage.
    ///
    /// When that happens the app still opens and the clicker still works, so it looks
    /// installed and healthy — but Live Captions silently never start. That is exactly the
    /// "I installed Tempo and some features don't work" report, and previously nothing
    /// anywhere said why. This turns it into a named, visible condition: logged at
    /// start-up, shown in Live Debug, and raised in the Health section.
    /// </summary>
    public static class SelfCheck
    {
        /// <summary>Human-readable result, shown in Live Debug. Set by <see cref="Run"/>.</summary>
        public static string Summary { get; private set; } = "not run";

        /// <summary>True when a required native library could not be loaded.</summary>
        public static bool NativesMissing { get; private set; }

        /// <summary>Where the single-file host staged the bundled native libraries.</summary>
        public static string ExtractionDir { get; private set; }

        /// <summary>True when this process is running from a single-file bundle.</summary>
        public static bool IsSingleFile { get; private set; }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string name);

        /// <summary>
        /// Probes the bundle. Cheap (a few library loads) and safe to call once at start-up.
        /// Never throws — a self-check that crashes the app would be worse than the fault
        /// it is looking for.
        /// </summary>
        public static void Run()
        {
            try
            {
                // In a single-file build AppContext.BaseDirectory is the EXTRACTION folder,
                // which differs from the directory the .exe actually sits in. That
                // difference is how we know extraction happened at all.
                string baseDir = AppContext.BaseDirectory ?? "";
                string exeDir = "";
                try
                {
                    string exe = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exe)) { exeDir = Path.GetDirectoryName(exe) ?? ""; }
                }
                catch { }

                IsSingleFile = !string.IsNullOrEmpty(baseDir) && !string.IsNullOrEmpty(exeDir) &&
                               !string.Equals(baseDir.TrimEnd('\\'), exeDir.TrimEnd('\\'),
                                              StringComparison.OrdinalIgnoreCase);
                ExtractionDir = IsSingleFile ? baseDir : exeDir;

                // The caption engine's natives. Loading by bare name goes through the same
                // search path the real engine uses, so this proves the actual thing rather
                // than merely checking a file exists.
                // Probe ONLY the engine's entry library. Its ggml dependencies are named
                // ggml-*-whisper.dll and are loaded BY whisper.dll — if any of them were
                // missing, whisper itself would fail to load, so checking it alone is both
                // sufficient and safe. (An earlier version guessed "ggml-base", which is
                // not a real file name here — that reported a broken install on a machine
                // where captions worked perfectly.)
                string[] needed = { "whisper" };
                var missing = new System.Collections.Generic.List<string>();
                foreach (string lib in needed)
                {
                    if (LoadLibraryW(lib) == IntPtr.Zero && !ProbeOnDisk(ExtractionDir, lib))
                    {
                        missing.Add(lib);
                    }
                }

                NativesMissing = missing.Count > 0;
                if (NativesMissing)
                {
                    Summary = "⚠ bundled speech libraries did not load (" + string.Join(", ", missing) +
                              ") — Live Captions will not start. Antivirus or a cleaner may be blocking " +
                              "Tempo's temp folder: " + ExtractionDir;
                    Logger.Warn("[SelfCheck] " + Summary);
                }
                else
                {
                    Summary = IsSingleFile
                        ? "single-file bundle OK · natives staged in " + Shorten(ExtractionDir)
                        : "loose build OK · running from " + Shorten(ExtractionDir);
                    Logger.Info("[SelfCheck] " + Summary);
                }
            }
            catch (Exception ex)
            {
                Summary = "could not run (" + ex.Message + ")";
                Logger.Swallow("SelfCheck", ex);
            }
        }

        /// <summary>Fallback: the library may be present but not loadable by bare name.</summary>
        private static bool ProbeOnDisk(string dir, string libName)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) { return false; }
                // Recursive: the single-file host stages natives in per-runtime
                // SUB-folders, so a top-level-only search always came up empty.
                foreach (string f in Directory.EnumerateFiles(dir, libName + "*.dll",
                                                              SearchOption.AllDirectories))
                {
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static string Shorten(string path)
        {
            if (string.IsNullOrEmpty(path)) { return "(unknown)"; }
            return path.Length <= 48 ? path : "…" + path.Substring(path.Length - 46);
        }
    }
}
