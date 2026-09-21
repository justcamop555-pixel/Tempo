using System;
using System.IO;
using System.Reflection;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Keeps the uninstall.cmd sitting in the install folder in step with the one this build ships.
    ///
    /// WHY THIS EXISTS: install.cmd copies uninstall.cmd next to Tempo.exe and points BOTH
    /// UninstallString and QuietUninstallString at that copy, and <see cref="Uninstaller"/>'s repair
    /// keeps preferring a script beside the exe whenever one is there. But an update only ever
    /// replaces Tempo.exe — <see cref="UpdateInstaller"/> pulls that single file out of the setup zip
    /// — so a zip-installed user's Uninstall button kept running the script they first installed
    /// with, and every later fix to it (the start-with-Windows entry, the live browser profiles, a
    /// OneDrive-moved Desktop, SHIFT /1, full System32 paths) never reached them. Carrying the script
    /// inside the exe makes a normal update deliver it.
    ///
    /// TWO RULES THIS MUST KEEP:
    ///  • It NEVER creates a uninstall.cmd where there isn't one. A copy that was installed by
    ///    Tempo itself has no script and its Apps entry runs "Tempo.exe --uninstall"; writing a
    ///    script there would silently switch those users onto the .cmd route, because RepairEntry
    ///    prefers a script when it finds one. Refresh only what is already there.
    ///  • It writes the shipped bytes EXACTLY. uninstall.cmd is LF-only on purpose
    ///    (AutoClicker/.gitattributes explains why), so this copies bytes and never text.
    /// </summary>
    public static class UninstallScriptRefresh
    {
        /// <summary>The name the csproj gives the embedded copy.</summary>
        private const string ResourceName = "Tempo.uninstall.cmd";

        /// <summary>What one refresh did, so a harness can assert on it.</summary>
        public enum Outcome
        {
            NotInstalled,     // a portable copy — the install folder is not ours to touch
            NoScriptThere,    // installed, but this copy uninstalls through Tempo.exe --uninstall
            AlreadyCurrent,   // the script beside the exe is already these bytes
            Updated,          // replaced with the version this build ships
            NoShippedCopy,    // the embedded resource is missing (a build mistake)
            Failed            // could not be written; the old script is left alone
        }

        /// <summary>
        /// Refreshes the installed copy's uninstall.cmd when this build ships a different one.
        /// Best-effort and silent: a failure leaves the existing script in place.
        /// </summary>
        public static Outcome Run()
        {
            Outcome result;
            try
            {
                result = Refresh(DeploymentInfo.IsInstalled
                                     ? Path.GetDirectoryName(DeploymentInfo.ExecutablePath)
                                     : null,
                                 ShippedBytes(),
                                 DeploymentInfo.IsInstalled);
            }
            catch (Exception ex)
            {
                Logger.Swallow("UninstallScriptRefresh.Run", ex);
                result = Outcome.Failed;
            }
            _lastOutcome = (int)result;
            return result;
        }

        // Written once by the startup thread-pool item, read by Live debug on the UI thread; an int
        // so the read can't be torn. -1 until this start's refresh has run.
        private static volatile int _lastOutcome = -1;

        /// <summary>What this start's refresh did, or null while it has not run yet (it runs off-thread).</summary>
        public static Outcome? LastOutcome => _lastOutcome < 0 ? (Outcome?)null : (Outcome)_lastOutcome;

        /// <summary>The uninstall.cmd carried inside this exe, or null when it isn't there.</summary>
        public static byte[] ShippedBytes()
        {
            try
            {
                Assembly asm = typeof(UninstallScriptRefresh).Assembly;
                using (Stream s = asm.GetManifestResourceStream(ResourceName))
                {
                    if (s == null)
                    {
                        Logger.Warn("[Uninstall] this build carries no embedded uninstall.cmd ("
                            + ResourceName + ") — installed copies cannot be refreshed.");
                        return null;
                    }
                    using (var ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        return ms.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Swallow("UninstallScriptRefresh.ShippedBytes", ex);
                return null;
            }
        }

        /// <summary>
        /// Test seam: the same decision against any folder. <paramref name="installDir"/> is the
        /// folder holding the installed Tempo.exe; nothing outside it is ever read or written.
        /// </summary>
        internal static Outcome Refresh(string installDir, byte[] shipped, bool isInstalled)
        {
            if (!isInstalled || string.IsNullOrEmpty(installDir)) { return Outcome.NotInstalled; }
            if (shipped == null || shipped.Length == 0) { return Outcome.NoShippedCopy; }

            string script = Path.Combine(installDir, "uninstall.cmd");
            if (!File.Exists(script)) { return Outcome.NoScriptThere; }

            try
            {
                if (SameBytes(File.ReadAllBytes(script), shipped)) { return Outcome.AlreadyCurrent; }
            }
            catch (Exception ex)
            {
                // Unreadable is not a reason to overwrite it — that is how a half-written script
                // would replace a working one.
                Logger.Swallow("UninstallScriptRefresh.read", ex);
                return Outcome.Failed;
            }

            // Write beside it and move into place, so a failure halfway through can never leave a
            // truncated uninstaller behind.
            string staging = script + ".new";
            try
            {
                File.WriteAllBytes(staging, shipped);
                File.Copy(staging, script, true);
                File.Delete(staging);
                Logger.Info("[Uninstall] refreshed the installed uninstall.cmd to this build's version.");
                return Outcome.Updated;
            }
            catch (Exception ex)
            {
                Logger.Warn("[Uninstall] could not refresh the installed uninstall.cmd: " + ex.Message);
                try { if (File.Exists(staging)) { File.Delete(staging); } } catch { }
                return Outcome.Failed;
            }
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) { return false; }
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) { return false; }
            }
            return true;
        }
    }
}
