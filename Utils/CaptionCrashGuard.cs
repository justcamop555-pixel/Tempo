using System;
using System.IO;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Stops a speech model that kills the process from killing it again, for ever.
    ///
    /// Loading and warming a Whisper model runs entirely inside the bundled NATIVE
    /// engine (`ggml-cpu-whisper.dll`). When that faults it raises an access violation
    /// (0xC0000005), which is a corrupted-state exception: .NET Core deliberately does
    /// NOT let managed code catch it, so no try/catch anywhere in Tempo can save the
    /// process. Tempo simply disappears. Observed with `large-v3-turbo` on this machine
    /// — a byte-for-byte intact 1.6 GB model, 11 GB of RAM free — while `base` loads
    /// and warms fine, so it is a fault in the native build, not a bad download.
    ///
    /// Since the crash can't be caught, it is instead REMEMBERED. A marker naming the
    /// model is written immediately before the native call and deleted the moment the
    /// engine survives warm-up. If Tempo starts and finds a marker still sitting there,
    /// the only way that happens is that the process died inside that model — so the
    /// model is quarantined and captions fall back to one that works, with an
    /// explanation, instead of the user hitting the same crash every single time.
    /// </summary>
    public static class CaptionCrashGuard
    {
        /// <summary>
        /// In Tempo's data folder (SettingsManager's — the same %LOCALAPPDATA%\AutoClicker), so a copy of Tempo
        /// run against a scratch data folder cannot leave a marker that quarantines a model in the real one.
        /// </summary>
        private static string MarkerPath =>
            Path.Combine(Persistence.SettingsManager.GetSettingsDirectory(), "caption-loading.marker");

        /// <summary>
        /// Records that <paramref name="modelKey"/> is about to be handed to the native
        /// engine. Call IMMEDIATELY before the load — the whole point is that the file
        /// survives an uncatchable crash.
        /// </summary>
        public static void MarkLoading(string modelKey, bool onGpu = false)
        {
            try
            {
                string path = MarkerPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, (modelKey ?? "?") + "\n" + DateTime.UtcNow.ToString("o") + (onGpu ? "\ngpu" : ""));
            }
            catch { /* diagnostics must never break captions */ }
        }

        /// <summary>
        /// True when the previous run died starting the GPU engine (the marker says "gpu"). The GPU path is what failed
        /// then — a graphics driver, not the model — so the model is NOT quarantined; captions start on the CPU engine
        /// instead (MainForm.WantGpuCaptionEngine), and a second such crash turns the automatic GPU choice off.
        /// </summary>
        public static bool CrashedOnGpu
        {
            get { TakeCrashedModel(); return _crashedOnGpu; }
        }

        private static bool _crashedOnGpu;

        /// <summary>Clears the marker — the model loaded and warmed without crashing.</summary>
        public static void MarkLoaded()
        {
            try
            {
                string path = MarkerPath;
                if (File.Exists(path)) { File.Delete(path); }
            }
            catch { }
        }

        /// <summary>
        /// The model Tempo died inside on the previous run, or null. Reads ONCE per
        /// process and clears the marker, so the quarantine applies to this session and
        /// the user can deliberately try the model again next launch.
        /// </summary>
        public static string TakeCrashedModel()
        {
            if (_checked)
            {
                return _crashed;
            }
            _checked = true;

            try
            {
                string path = MarkerPath;
                if (File.Exists(path))
                {
                    string[] lines = File.ReadAllLines(path);
                    _crashed = lines.Length > 0 ? lines[0].Trim() : null;
                    _crashedOnGpu = lines.Length > 2 && string.Equals(lines[2].Trim(), "gpu", StringComparison.OrdinalIgnoreCase);
                    File.Delete(path);
                    if (_crashedOnGpu)
                    {
                        Logger.Warn("[Captions] previous run died starting the GPU speech engine (with the '" + _crashed +
                                    "' model) — captions use the CPU engine this session; the model is not blamed.");
                        _crashed = null;
                    }
                    else if (!string.IsNullOrEmpty(_crashed))
                    {
                        Logger.Warn("[Captions] previous run died loading the '" + _crashed +
                                    "' speech model — quarantining it for this session.");
                    }
                }
            }
            catch { }

            return _crashed;
        }

        private static bool _checked;
        private static string _crashed;

        /// <summary>True if this model is the one that killed the previous run.</summary>
        public static bool IsQuarantined(string modelKey)
        {
            string bad = TakeCrashedModel();
            return !string.IsNullOrEmpty(bad) && !string.IsNullOrEmpty(modelKey) &&
                   string.Equals(bad, modelKey, StringComparison.OrdinalIgnoreCase);
        }
    }
}
