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
        /// Hands our foreground right to the already-running Tempo.
        ///
        /// Windows refuses SetForegroundWindow to a process that does not own the
        /// foreground, and the running instance never does — it is in the background by
        /// definition when someone launches Tempo again. So it asked to come forward and
        /// Windows quietly flashed its taskbar button instead. Measured: with Notepad in
        /// front, launching Tempo a second time left the foreground on another window
        /// entirely, while the notice said "Its window has been brought to the front".
        ///
        /// THIS process is the one Windows will listen to — the user just launched it, so
        /// it holds the foreground right. AllowSetForegroundWindow passes that right to
        /// the target, which is the sanctioned way to do this rather than a trick.
        /// </summary>
        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);
        private const int ASFW_ANY = -1;

        /// <summary>
        /// Signals the already-running Tempo to surface its window (out of the tray and
        /// to the front). Broadcasts a registered message that the running instance's
        /// MainForm listens for; all top-level windows — including a tray-hidden one —
        /// receive it. Best-effort: if it doesn't land, the info dialog still guides the
        /// user to the tray.
        /// </summary>
        /// <summary>
        /// Passes this process's foreground right to the other Tempo.
        ///
        /// Named to that process rather than ASFW_ANY: the effect here is the same, but
        /// it does not leave every process on the desktop free to grab the foreground for
        /// the rest of this one's short life.
        /// </summary>
        private static void GrantForegroundToOtherTempo()
        {
            bool granted = false;
            try
            {
                int me = System.Diagnostics.Process.GetCurrentProcess().Id;
                // Under either name: the installed copy is TempoClicker.exe now, downloads are Tempo.exe
                // (see DeploymentInfo.InstalledExeName).
                foreach (string name in new[] { "TempoClicker", "Tempo" })
                {
                    foreach (var p in System.Diagnostics.Process.GetProcessesByName(name))
                    {
                        using (p)
                        {
                            if (p.Id != me && AllowSetForegroundWindow(p.Id)) { granted = true; }
                        }
                    }
                }
            }
            catch { }
            if (!granted) { try { AllowSetForegroundWindow(ASFW_ANY); } catch { } }
        }

        /// <summary>
        /// Asks the running Tempo to open its uninstall flow, and reports whether it actually
        /// received the request. A broadcast alone cannot say that — PostMessage succeeds whether
        /// or not anyone is listening (an older build has no handler) — so the running copy sets a
        /// named event when the message arrives, and this waits for it. The event is created BEFORE
        /// the broadcast, so an instance that answers instantly cannot signal into nothing.
        /// </summary>
        private static bool HandUninstallToRunningInstance()
        {
            try
            {
                using (var ack = new EventWaitHandle(false, EventResetMode.AutoReset, MainForm.UninstallAckEventName))
                {
                    // The uninstall confirmation opens in the other process and must be allowed to
                    // take the foreground, exactly like the "show me" hand-off.
                    GrantForegroundToOtherTempo();
                    int msg = RegisterWindowMessage(MainForm.UninstallRequestMessageName);
                    if (msg == 0) { return false; }
                    PostMessage(HWND_BROADCAST, msg, IntPtr.Zero, IntPtr.Zero);
                    return ack.WaitOne(TimeSpan.FromSeconds(4));
                }
            }
            catch (Exception ex)
            {
                Logger.Swallow("HandUninstallToRunningInstance", ex);
                return false;
            }
        }

        /// <summary>
        /// Asks the running Tempo to bring its window forward. With <paramref name="waitMs"/>, waits that
        /// long for it to say the window is on screen (MainForm.ShownAckEventName) and returns whether it did.
        /// </summary>
        private static bool TryActivateExistingInstance(int waitMs = 0)
        {
            try
            {
                GrantForegroundToOtherTempo();

                // Made before the request goes out, so the answer cannot arrive first.
                using (var shown = new EventWaitHandle(false, EventResetMode.AutoReset, MainForm.ShownAckEventName))
                {
                    int msg = RegisterWindowMessage(MainForm.ShowInstanceMessageName);
                    if (msg != 0)
                    {
                        PostMessage(HWND_BROADCAST, msg, IntPtr.Zero, IntPtr.Zero);
                    }
                    return waitMs > 0 && msg != 0 && shown.WaitOne(waitMs);
                }
            }
            catch { return false; }
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

                // Not for a single-file build — every release. Its native libraries are unpacked into TEMP
                // with the rest of it and loaded from there by full path; nothing of Tempo's ever sits in a
                // runtimes folder beside the exe. A folder of that name there was put there by someone else
                // (install.cmd used to copy one along from wherever the download was unzipped), and adding it
                // to Windows' DLL search path would let any library in it answer for one Tempo asks for.
                string baseDir = (AppContext.BaseDirectory ?? "").TrimEnd('\\', '/');
                if (baseDir.Length > 0 && !string.Equals(baseDir, dir.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
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

            // Launched by a restart or an install: say so at once, before waiting for the old copy to
            // let go. That copy is still on screen saying "Restarting…", and it now stays there until
            // this signal arrives instead of vanishing the moment it asked Windows to start us.
            if (StartedForRestart() || StartedForHandover())
            {
                AnnounceRestartStarted();
            }

            // Ensure only one copy of the application runs at a time.
            bool createdNew;
            _singleInstanceMutex = new Mutex(true, MutexName, out createdNew);
            if (!createdNew && (StartedForRestart() || StartedForHandover()) && WaitForRestartHandover())
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
            if (!createdNew && StartedForUninstall())
            {
                // Settings → Apps launched us to uninstall, but Tempo is already running — which it
                // nearly always is, because it starts with Windows and lives in the tray. This
                // process cannot do the uninstall itself (the running copy holds the mutex, the log
                // and the exe), and it used to fall straight through to the "already running"
                // notice below: clicking Uninstall in Settings → Apps told the user Tempo was running
                // and did nothing else. So hand the request to the running copy, and fall back to
                // that notice only when nothing acknowledges it — never a silent no-op.
                if (HandUninstallToRunningInstance())
                {
                    Logger.Info("[Uninstall] handed the Settings > Apps request to the running Tempo.");
                    return;
                }
                Logger.Warn("[Uninstall] the running Tempo did not acknowledge the uninstall request.");
            }
            if (!createdNew && Utils.StartupManager.LaunchedAtStartup())
            {
                // Tempo was already open when the start-with-Windows launch fired: the user got
                // there first, opening it themselves straight after signing in. This launch has
                // nothing left to do. It used to fall through to the block below, which pulled
                // the window forward and put "Tempo is already running" in front of whatever the
                // user was doing — a notice about something they did on purpose, from a launch
                // they never asked for. A startup launch never shows a window of its own, so it
                // shows none here either; it just leaves. A launch the USER makes while Tempo is
                // running still goes below and brings the window forward, as before.
                Logger.Info("[startup] Tempo is already running (opened before the sign-in launch fired) — this start-with-Windows launch exits quietly.");
                return;
            }
            if (!createdNew)
            {
                // Pull the running window forward so the user isn't left hunting the
                // tray — that's the actual thing they want when they double-click again.
                //
                // And when the running copy says its window is up, that is all: this launch leaves
                // without a word. "Tempo is already running" used to come up every time, and came
                // up FIRST — recorded (scratchpad/openframes), the notice was on screen a quarter of
                // a second before the window it said had been brought to the front, which is the
                // "some GUI appears before Tempo has loaded" people saw on opening it. It is kept for
                // when the running copy does not answer (busy, hung, or a version without the reply),
                // where saying where Tempo is still helps.
                if (TryActivateExistingInstance(3000))
                {
                    Logger.Info("[startup] Tempo is already running — its window is on screen now; this launch exits quietly.");
                    return;
                }
                Logger.Info("[startup] Tempo is already running but did not confirm its window within 3 s — showing the notice.");

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
                        // HDR-corrected like the running window, on the screen the notice opens on.
                        bool hdr = s.CompensateForHdr &&
                                   Utils.DisplayColorInfo.HdrOnFor(Screen.FromPoint(Control.MousePosition).DeviceName);
                        theme = UI.Theme.ForKind((Models.ThemeKind)s.Theme, hdr);
                    }
                }
                catch { /* fall back to the default dark theme and English */ }

                // This instance returns from here and never reaches the startup block
                // below, so nothing has prepared WinForms yet: no visual styles, and no
                // default window icon. That is why this notice came up with the stock
                // icon in its title bar, its taskbar button and Alt-Tab, while every
                // other Tempo window shows the app icon — or the custom logo, since
                // AppIcon.Get() prefers it. No form sets its own Icon; they all rely on
                // this one call, so the path that skips it is the path that loses it.
                //
                // Both Application calls must happen before the first window exists,
                // which here is still true.
                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Utils.AppIcon.SetAsWindowDefault();
                }
                catch (Exception ex)
                {
                    Utils.Logger.Warn("[startup] preparing the already-running notice: " + ex.Message);
                }

                try
                {
                    using (var dlg = new UI.AlreadyRunningForm(theme, version))
                    {
                        dlg.ShowDialog();
                    }

                    // Again, now the notice is gone. The first call restores the window
                    // while this dialog is still up, but the dialog is itself a
                    // foreground window in THIS process, and closing it hands the
                    // foreground to whatever Windows picks next — measured, that was the
                    // app the user came from, straight back over the window we had just
                    // raised. Asking once more after the notice closes is what makes the
                    // result stick, and it is the last thing this process does.
                    TryActivateExistingInstance();
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

            // One-time costs the UI thread would otherwise pay in the middle of start-up, paid here in
            // parallel instead (all profiled on the UI thread before this existed):
            //  • System.Text.Json describing each stored type and making its accessors, by reflection —
            //    66 of the 87 ms settings load, 26 of the 32 ms recycle-bin load;
            //  • the translation tables' static constructor (12 ms) and the globalization library
            //    behind the first culture-aware compare.
            // Two threads, so the settings the window needs first are not queued behind the rest.
            // Nothing is duplicated if the UI thread gets there first: a static constructor runs
            // once, and System.Text.Json configures a type once, the latecomer waiting. This
            // instance owns the mutex now, so a second launch that is about to exit starts neither.
            // Measured as installed, six interleaved starts each: median to the built window 591 ms
            // with no warm-up, 515 describing the types only, 503 with these two threads.
            new Thread(() =>
            {
                Persistence.SettingsManager.WarmUp();
                Persistence.ProfileManager.WarmUp();
                Persistence.MacroStore.WarmUp();
                Persistence.SessionHistoryStore.WarmUp();
            })
            { IsBackground = true, Name = "Tempo warm-up: data" }.Start();
            new Thread(() =>
            {
                try { Utils.Localization.T("OK"); } catch { }
                try { System.Globalization.CultureInfo.CurrentCulture.CompareInfo.Compare("a", "B"); } catch { }
            })
            { IsBackground = true, Name = "Tempo warm-up: text" }.Start();

            // Read the version rather than spelling it out: this line said "1.0.319" as a
            // literal, so every log from a later release would have kept claiming 1.0.319
            // until somebody noticed. The build ID rides along because the version alone
            // cannot distinguish a test build from the release it was cut from, and this
            // is the first line of every log attached to a bug report.
            Logger.Info("[startup] Tempo starting (version " + Utils.AppVersion.Text
                        + ", " + Utils.BuildInfo.Short + ").");

            // Discord showed Tempo users as playing the Steam game "Tempo": its detection list names
            // that game's program tempo.exe. An install still called Tempo.exe is renamed once, here, to
            // TempoClicker.exe (DeploymentInfo.RenameInstalledExe) — and this process hands over to the
            // renamed file, because to Discord it is still Tempo.exe: GetProcessImageFileName keeps
            // returning the name a process started under until it exits (measured). Never for an
            // uninstall launch: that must stay an uninstall.
            //
            // --handover, not --restart. Both wait for this process to let go of the single-instance
            // mutex, but --restart also forces the window on screen, and passing it here made the
            // FIRST sign-in after the update pop the window up for everyone who starts in the tray.
            // What this launch was asked to do travels with it instead: --startup for a sign-in
            // launch, --restart when it is itself a restart (the updater relaunches with it, and
            // "Update now" must bring the window back, not seem to quit).
            if (!StartedForUninstall() && Utils.DeploymentInfo.RenameInstalledExe(out string renamedExe))
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = renamedExe,
                        Arguments = "--handover"
                                    + (StartedForRestart() ? " --restart" : "")
                                    + (Utils.StartupManager.LaunchedAtStartup() ? " --startup" : ""),
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(renamedExe) ?? Environment.CurrentDirectory,
                    };
                    System.Diagnostics.Process.Start(psi);
                    Logger.Info("[startup] handed over to " + renamedExe + ".");
                    ReleaseMutex();
                    return;
                }
                catch (Exception ex)
                {
                    // The file is renamed either way; this copy simply carries on, and the next start
                    // is under the new name.
                    Logger.Warn("[startup] could not start the renamed copy (" + ex.Message + ") — carrying on.");
                }
            }

            Utils.SelfCheck.Run();   // does the single-file bundle actually work here?

            // Every build Tempo has ever run left ~263 MB of unpacked natives in TEMP and
            // nothing removed them. Sweep the stale ones on a background thread — after
            // SelfCheck, which is what establishes the folder we're running from and must
            // therefore be kept. Never blocks startup, never throws.
            Utils.BundleCleanup.SweepInBackground();

            // The user's data, before this build touches any of it: apply a restore that was scheduled
            // from Settings, then keep a restore point if this is not the build that ran last. Here and
            // not later — MainForm saves settings within moments of loading them, and only this instance
            // owns the mutex, so a second launch can never take a point of its own.
            Persistence.DataSnapshots.OnStartup();

            // Keep the version Windows shows in sync with the exe that is running.
            // Self-updates replace Tempo.exe without touching the registry, so the number
            // in Settings > Apps drifts further behind with every release. Off-thread and
            // best-effort: it is a cosmetic value and must never delay startup.
            System.Threading.ThreadPool.QueueUserWorkItem(
                _ => Utils.Uninstaller.RefreshRegisteredVersion());

            // The Uninstall button of a zip install runs the uninstall.cmd that was copied into the
            // install folder on day one, and an update only ever replaces Tempo.exe — so those users
            // never received a single fix to it. Bring that file up to the version this build carries.
            // Same slot and same rules as the line above: off-thread, best-effort, never delays
            // startup, and it only ever touches a script that is already there.
            System.Threading.ThreadPool.QueueUserWorkItem(
                _ => Utils.UninstallScriptRefresh.Run());

            // And what earlier installs left in the install folder that only misleads now (an out-of-date
            // setup note, an install stopped half-way). Same rules: off-thread, the installed copy only.
            System.Threading.ThreadPool.QueueUserWorkItem(
                _ => Utils.InstallFolderTidy.RunAtStartup());

            // Shortcuts, pins and the sign-in entry still aimed at the installed copy's old name (see the
            // rename above; install.cmd renames by installing the new name) move to this file.
            Utils.DeploymentInfo.RepointLeftoversOfOldNameInBackground();

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

            // No 1 ms timer resolution for the whole session any more: a run holds it while it runs
            // (Engine.RunQos, with full speed from Windows), and the clicker's and macro player's waits
            // are on the high-resolution kernel timer, which never needed it. Held for Tempo's whole
            // life it kept a laptop's timer firing a thousand times a second with Tempo idle in the tray.
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
                try { AutoClicker.Engine.RunQos.SetRunning(false); } catch { }
                Logger.Info("[shutdown] Tempo exiting.");
                ReleaseMutex();
            }

            // A helper is waiting for this PROCESS to end before it touches Tempo.exe (the updater's
            // swap, the unpacked-copy repair). The normal shutdown above has already saved and
            // released everything; this makes sure no straggling foreground thread can keep the
            // process — and the lock on the exe — alive after it.
            if (ExitProcessAfterShutdown)
            {
                Environment.Exit(0);
            }
        }

        /// <summary>
        /// Set when Tempo is closing so an external helper can replace or repair it. The close
        /// itself is the ordinary one — everything is saved and put back — and this only guarantees
        /// the process then actually ends. See <see cref="Main"/>.
        /// </summary>
        internal static volatile bool ExitProcessAfterShutdown;

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            Logger.Error("Unhandled UI thread exception.", e.Exception);
            ShowCrashReport(e.Exception, "UI thread", false);
        }

        private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            Logger.Error("Unhandled application domain exception.", ex);
            // A run's shortcut guard may have the Sticky Keys shortcut switched off: put it back before
            // the process goes, or it stays off until the user signs out.
            try { AutoClicker.Native.AccessibilityShortcuts.Restore(); } catch { }
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

        /// <summary>
        /// True when Windows launched us from the Uninstall button in Settings → Apps.
        ///
        /// The registry used to point that button at uninstall.cmd in the install folder,
        /// which install.cmd copies only IF it finds one to copy — but it wrote the
        /// registry value either way. On this machine the result was a dangling Uninstall
        /// button that had never worked. The exe cannot dangle: it is the thing being
        /// uninstalled, so it is present by definition.
        /// </summary>
        internal static bool StartedForUninstall()
        {
            try
            {
                foreach (string a in Environment.GetCommandLineArgs())
                {
                    if (string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(a, "/uninstall", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>True when this process was launched by Tempo's own restart flow.</summary>
        internal static bool StartedForRestart() => HasArg("restart");

        /// <summary>
        /// True when an older copy started this one to take over from it (the Discord rename) — it
        /// waits for the mutex like a restart, but comes up exactly as its own launch asked.
        /// </summary>
        internal static bool StartedForHandover() => HasArg("handover");

        /// <summary>
        /// True when a restart asked to come back in the tray: the tray menu's Restart, pressed while
        /// the window was hidden, should not bring the window up.
        /// </summary>
        internal static bool StartedToTray() => HasArg("tray");

        private static bool HasArg(string name)
        {
            try
            {
                foreach (string a in Environment.GetCommandLineArgs())
                {
                    if (string.Equals(a, "--" + name, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(a, "/" + name, StringComparison.OrdinalIgnoreCase))
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
        /// restart, then takes ownership. Returns true once we own it. If the old instance died
        /// without releasing, the mutex is abandoned — which still hands ownership to us.
        ///
        /// Twelve seconds, and then for as long as the outgoing Tempo is still shutting down (up to
        /// two minutes in all). Twelve seconds used to be the whole wait: a shutdown that took longer —
        /// a laptop saving everything while captions unloaded their model, an antivirus scanning
        /// the files being written — made this copy give up, show "Tempo is already running" about
        /// a Tempo that was closing, and exit; a moment later the old one finished, and the restart
        /// had ended with no Tempo at all.
        /// </summary>
        private static bool WaitForRestartHandover()
        {
            return WaitForHandover(_singleInstanceMutex, OtherTempoAlive, TimeSpan.FromSeconds(12), TimeSpan.FromMinutes(2));
        }

        /// <summary>The wait itself, and its test seam: any mutex, any "is the old copy still there?".</summary>
        internal static bool WaitForHandover(Mutex mutex, Func<bool> outgoingAlive, TimeSpan first, TimeSpan cap)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (mutex.WaitOne(first)) { return true; }
                bool logged = false;
                while (clock.Elapsed < cap)
                {
                    bool alive;
                    try { alive = outgoingAlive == null || outgoingAlive(); }
                    catch { alive = true; }
                    if (!alive)
                    {
                        // Gone without a word: one last short wait for the release it made on its way out.
                        return mutex.WaitOne(TimeSpan.FromSeconds(1));
                    }
                    if (!logged)
                    {
                        logged = true;
                        Logger.Info("[Restart] the old copy is still closing after " + (int)clock.Elapsed.TotalSeconds +
                                    " s — waiting for it.");
                    }
                    if (mutex.WaitOne(TimeSpan.FromSeconds(1))) { return true; }
                }
                Logger.Warn("[Restart] the old copy has not closed after " + (int)clock.Elapsed.TotalSeconds + " s.");
                return false;
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

        /// <summary>Whether another Tempo process — under either name — is still running.</summary>
        private static bool OtherTempoAlive()
        {
            int me = Environment.ProcessId;
            foreach (string name in new[] { "TempoClicker", "Tempo" })
            {
                System.Diagnostics.Process[] found;
                try { found = System.Diagnostics.Process.GetProcessesByName(name); }
                catch { continue; }
                bool other = false;
                foreach (var p in found)
                {
                    using (p)
                    {
                        if (p.Id != me) { other = true; }
                    }
                }
                if (other) { return true; }
            }
            return false;
        }

        /// <summary>
        /// Sets the event the outgoing instance waits on (<see cref="MainForm.RestartStartedEventName"/>).
        /// By the time Main runs, the slow part of a first start is over: a single-file build unpacks
        /// itself before any of Tempo's code runs, which for a freshly installed copy — a new file name,
        /// so a new unpack folder — is many seconds on a laptop with antivirus.
        /// </summary>
        private static void AnnounceRestartStarted()
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(MainForm.RestartStartedEventName, out EventWaitHandle started))
                {
                    using (started) { started.Set(); }
                }
            }
            catch { /* nobody is waiting: a hand-over from an older build */ }
        }

        /// <summary>
        /// Starts the copy of Tempo that replaces this one, with a --restart flag so it waits for the
        /// hand-over (<see cref="WaitForRestartHandover"/>) — the fix for the single-instance race
        /// <see cref="Application.Restart"/> had: it launched the new process before this one released
        /// the mutex, so the new instance saw "already running" and exited, leaving NO window.
        ///
        /// Only STARTS it. The caller exits once the new copy has announced itself (see
        /// <see cref="AnnounceRestartStarted"/>), and call it OFF the UI thread: ShellExecute has Windows
        /// look at the program first — the antivirus scan of a file that was only just written,
        /// SmartScreen for a downloaded one — and on the UI thread that wait froze the window. Throws if
        /// the relaunch can't start, so the caller can keep the app open and tell the user.
        /// </summary>
        internal static System.Diagnostics.Process StartReplacement(string relaunchExe = null, bool toTray = false)
        {
            // Normally the same file relaunches itself. Installing a portable copy relaunches the
            // INSTALLED copy instead, through the very same --restart hand-over, so the new
            // location takes over with none of the "already running" race this method exists for.
            string exe = relaunchExe;
            if (string.IsNullOrEmpty(exe)) { exe = Environment.ProcessPath; }
            if (string.IsNullOrEmpty(exe)) { exe = Application.ExecutablePath; }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                Arguments = "--restart" + (toTray ? " --tray" : ""),
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory
            };
            return System.Diagnostics.Process.Start(psi);   // throws → caller keeps app open
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
