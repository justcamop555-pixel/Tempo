using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using AutoClicker.UI;
using AutoClicker.Utils;

namespace AutoClicker
{
    /// <summary>
    /// Application entry point. Sets up single-instance behaviour, global exception
    /// handlers and visual styles before launching the main window.
    /// </summary>
    internal static class Program
    {
        private static Mutex _singleInstanceMutex;
        private const string MutexName = "AutoClicker.Pro.SingleInstance.Mutex.{B3F1A7C2-2E55-4A1D-9C44-77E0F1A2B3C4}";

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string lpString);
        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
        private static readonly IntPtr HWND_BROADCAST = (IntPtr)0xFFFF;

        /// <summary>
        /// Signals the already-running Tempo to surface its window (out of the tray and
        /// to the front). Broadcasts a registered message that the running instance's
        /// MainForm listens for; all top-level windows — including a tray-hidden one —
        /// receive it. Best-effort: if it doesn't land, the info dialog still guides the
        /// user to the tray.
        /// </summary>
        private static void TryActivateExistingInstance()
        {
            try
            {
                int msg = RegisterWindowMessage(MainForm.ShowInstanceMessageName);
                if (msg != 0)
                {
                    PostMessage(HWND_BROADCAST, msg, IntPtr.Zero, IntPtr.Zero);
                }
            }
            catch { }
        }

        /// <summary>
        /// Adds runtimes\&lt;rid&gt;\native (next to Tempo.exe) to the native DLL search
        /// path, so the Whisper speech-engine libraries load from that tidy subfolder
        /// rather than having to sit loose beside the exe. Best-effort and never throws;
        /// the exe's own folder is always searched too, so this can't make things worse.
        /// </summary>
        private static void RegisterNativeLibrarySearchPath()
        {
            try
            {
                string exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe))
                {
                    exe = Application.ExecutablePath;
                }
                string dir = Path.GetDirectoryName(exe);
                if (string.IsNullOrEmpty(dir))
                {
                    return;
                }

                Architecture arch = RuntimeInformation.ProcessArchitecture;
                string rid = arch == Architecture.X86 ? "win-x86"
                           : arch == Architecture.Arm64 ? "win-arm64"
                           : "win-x64";
                string nativeDir = Path.Combine(dir, "runtimes", rid, "native");
                if (Directory.Exists(nativeDir))
                {
                    SetDllDirectory(nativeDir);
                    Logger.Info("[startup] native library search path added: " + nativeDir);
                }
            }
            catch (Exception ex)
            {
                try { Logger.Error("Could not register native library search path.", ex); } catch { }
            }
        }

        [STAThread]
        private static void Main()
        {
            // Tempo's native speech-engine libraries (Whisper) now live in the tidy
            // runtimes\<rid>\native subfolder instead of loose next to the exe. Whisper.net
            // already searches that standard layout; this also adds it to the OS DLL search
            // path as a safety net. The exe's own folder is always searched too, so any
            // libs left beside the exe still load. Done first, before anything triggers a
            // native load.
            RegisterNativeLibrarySearchPath();

            // Ensure only one copy of the application runs at a time.
            bool createdNew;
            _singleInstanceMutex = new Mutex(true, MutexName, out createdNew);
            if (!createdNew && StartedForRestart() && WaitForRestartHandover())
            {
                // A deliberate restart (language or GPU change) relaunches Tempo with
                // --restart WHILE the old instance is still shutting down, so the mutex
                // is momentarily still held. Rather than giving up with a misleading
                // "already running" and no window (the reported bug), we waited for the
                // outgoing instance to release the mutex and now own it — carry on as
                // the fresh instance.
                createdNew = true;
                Logger.Info("[Restart] hand-over complete; continuing as the new instance.");
            }
            if (!createdNew)
            {
                // Pull the running window forward so the user isn't left hunting the
                // tray — that's the actual thing they want when they double-click again.
                TryActivateExistingInstance();

                // Show the version so support/bug reports always name the exact build.
                // Read from the assembly so this can never drift from the real number.
                string version;
                try
                {
                    Version v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                    version = v != null ? v.Major + "." + v.Minor + "." + v.Build : "";
                }
                catch { version = ""; }

                // Read the user's theme and language so this looks and reads like Tempo.
                // This instance is about to exit and only ever READS the file, so it
                // cannot race the running copy's settings.
                UI.Theme theme = null;
                try
                {
                    Models.AppSettings s = Persistence.SettingsManager.Load();
                    if (s != null)
                    {
                        Utils.Localization.Current = s.Language;
                        theme = UI.Theme.ForKind((Models.ThemeKind)s.Theme);
                    }
                }
                catch { /* fall back to the default dark theme and English */ }

                try
                {
                    using (var dlg = new UI.AlreadyRunningForm(theme, version))
                    {
                        dlg.ShowDialog();
                    }
                }
                catch (Exception ex)
                {
                    // Never let a cosmetic dialog stop the app from telling the user what
                    // happened — fall back to the plain box it replaced.
                    Utils.Logger.Warn("[startup] themed already-running notice failed: " + ex.Message);
                    MessageBox.Show(
                        "Tempo" + (version.Length > 0 ? " " + version : "") + " is already running.\n\n" +
                        "Look for its icon in the system tray (bottom-right of the taskbar, " +
                        "you may need to click the ^ arrow). Its window has been brought to the front.",
                        "Tempo" + (version.Length > 0 ? " " + version : ""),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                return;
            }

            // Route unhandled exceptions to the logger so the app fails gracefully.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainException;
            // Background Task exceptions that nobody observed would otherwise be
            // re-thrown by the finalizer and can crash the app. Log and mark them
            // observed so a stray failed Task (e.g. a network or audio task) never
            // takes Tempo down.
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                // Log every INNER exception, not the AggregateException wrapper. The
                // wrapper's own StackTrace is null, so logging it said only "a Task
                // failed" and named neither the fault nor the code that raised it —
                // which is exactly what made a live "Collection was modified" error
                // unattributable. The inners carry the real type, message and stack.
                var agg = e.Exception;
                if (agg != null)
                {
                    foreach (Exception inner in agg.Flatten().InnerExceptions)
                    {
                        Logger.Error("Unobserved background task exception (handled).", inner);
                    }
                }
                else
                {
                    Logger.Error("Unobserved background task exception (handled, no detail).");
                }
                e.SetObserved();
            };

            Logger.Info("[startup] Tempo starting (version 1.0.319).");
            Utils.SelfCheck.Run();   // does the single-file bundle actually work here?

            // Every build Tempo has ever run left ~263 MB of unpacked natives in TEMP and
            // nothing removed them. Sweep the stale ones on a background thread — after
            // SelfCheck, which is what establishes the folder we're running from and must
            // therefore be kept. Never blocks startup, never throws.
            Utils.BundleCleanup.SweepInBackground();

            // Prime WinForms and put the splash on screen FIRST, before any other
            // startup work, so there's instant visible feedback. Everything below this
            // point (monitor enumeration, prerequisite check, the heavy MainForm
            // construction) then happens *behind* a window that's already up — which is
            // what closes the "sometimes it takes a moment to open" gap.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // One call gives every Tempo window (Live debug, About, Calibrate, update
            // prompts, …) the app icon in its title bar and in Alt-Tab. Must run after
            // EnableVisualStyles and before the first form is created.
            Utils.AppIcon.SetAsWindowDefault();

            // Show a loading splash the instant we start, on its OWN UI thread, so its
            // animation keeps running while the (heavier) main window is constructed on
            // this thread. MainForm calls SplashForm.RequestClose() once it's on screen;
            // the splash then fades out and closes itself. Background + self-timeout, so
            // it can never hold the process open or linger if startup goes wrong.
            try
            {
                Thread splashThread = new Thread(() =>
                {
                    try { Application.Run(new SplashForm()); }
                    catch { /* splash is best-effort */ }
                    finally { SplashForm.MarkClosed(); } // never let the main window wait forever
                });
                splashThread.SetApartmentState(ApartmentState.STA);
                splashThread.IsBackground = true;
                splashThread.Start();
            }
            catch { /* if the splash can't start, the app still launches normally */ }

            // Environment details are for the log only — nothing downstream waits on
            // them — so gather them off the startup path instead of stalling the open.
            System.Threading.Tasks.Task.Run(() =>
            {
                try { EnvironmentInfo.LogSummary(); }
                catch (Exception ex) { Logger.Warn("[Startup] environment logging failed: " + ex.Message); }
            });

            // Verify the host meets Tempo's requirements. If not, explain what to
            // install by hand and exit cleanly rather than failing later.
            PrerequisiteChecker.Result prereq = PrerequisiteChecker.Check();
            if (!prereq.Satisfied)
            {
                SplashForm.RequestClose(); // don't leave the splash floating over the advice dialog
                PrerequisiteChecker.ReportAndAdvise(prereq);
                Logger.Info("[shutdown] exiting: prerequisites not satisfied.");
                ReleaseMutex();
                return;
            }

            // Hold a fine system timer resolution for the whole session so timed
            // waits in the click engine and macro player are accurate. Restored
            // automatically on exit.
            using (new AutoClicker.Engine.TimerResolution(1))
            {
                try
                {
                    using (var form = new MainForm())
                    {
                        Application.Run(form);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Fatal error while running the application.", ex);
                    ShowCrashReport(ex, "main loop / fatal", true);
                }
                finally
                {
                    Logger.Info("[shutdown] Tempo exiting.");
                    ReleaseMutex();
                }
            }
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            Logger.Error("Unhandled UI thread exception.", e.Exception);
            ShowCrashReport(e.Exception, "UI thread", false);
        }

        private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            Logger.Error("Unhandled application domain exception.", ex);
            // The process is usually terminating here, so don't try to show UI —
            // just preserve the report on disk for later.
            try
            {
                CrashReporter.WriteReportFile(CrashReporter.BuildReport(ex, "background thread"));
            }
            catch
            {
                // Nothing more we can safely do during a hard shutdown.
            }
        }

        /// <summary>Saves a crash report and shows the reporting dialog.</summary>
        private static void ShowCrashReport(Exception ex, string context, bool fatal)
        {
            try
            {
                string report = CrashReporter.BuildReport(ex, context);
                string path = CrashReporter.WriteReportFile(report);
                using (var form = new CrashReportForm(ex, context, report, path, fatal))
                {
                    form.ShowDialog();
                    if (form.UserChoseQuit)
                    {
                        Application.Exit();
                    }
                }
            }
            catch (Exception reportingError)
            {
                Logger.Error("The crash reporter itself failed.", reportingError);
                MessageBox.Show(
                    Utils.Localization.F("An unexpected error occurred:\n\n{0}", ex == null ? "(unknown)" : ex.Message),
                    "Tempo - Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        /// <summary>True when this process was launched by Tempo's own restart flow.</summary>
        internal static bool StartedForRestart()
        {
            try
            {
                foreach (string a in Environment.GetCommandLineArgs())
                {
                    if (string.Equals(a, "--restart", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(a, "/restart", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Waits for the outgoing instance to release the single-instance mutex during a
        /// restart, then takes ownership. Returns true once we own it. A generous timeout
        /// covers a slow shutdown; if the old instance died without releasing, the mutex
        /// is abandoned — which still hands ownership to us.
        /// </summary>
        private static bool WaitForRestartHandover()
        {
            try
            {
                return _singleInstanceMutex.WaitOne(TimeSpan.FromSeconds(12));
            }
            catch (AbandonedMutexException)
            {
                return true;   // previous owner exited abruptly; ownership is now ours
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Restarts Tempo cleanly, avoiding the single-instance race that
        /// <see cref="Application.Restart"/> caused: it launched the new process before
        /// this one released the mutex, so the new instance saw "already running" and
        /// exited — leaving NO window. This launches the replacement with a --restart
        /// flag (so it waits for the hand-over, see <see cref="WaitForRestartHandover"/>)
        /// and only then exits the current instance to release the mutex.
        /// Throws if the relaunch can't start, so the caller can keep the app open and
        /// tell the user; in that case the current instance is NOT exited.
        /// </summary>
        internal static void RestartApp()
        {
            string exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) { exe = Application.ExecutablePath; }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                Arguments = "--restart",
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory
            };
            System.Diagnostics.Process.Start(psi);   // throws → caller keeps app open

            // Relaunch is on its way and will wait for the mutex; now let this one go.
            Application.Exit();
        }

        private static void ReleaseMutex()
        {
            try
            {
                if (_singleInstanceMutex != null)
                {
                    _singleInstanceMutex.ReleaseMutex();
                    _singleInstanceMutex.Dispose();
                    _singleInstanceMutex = null;
                }
            }
            catch (ApplicationException)
            {
                // The mutex was not owned by this thread; safe to ignore on shutdown.
            }
        }
    }
}
