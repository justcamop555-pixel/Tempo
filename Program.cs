using System;
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

        [STAThread]
        private static void Main()
        {
            // Ensure only one copy of the application runs at a time.
            bool createdNew;
            _singleInstanceMutex = new Mutex(true, MutexName, out createdNew);
            if (!createdNew)
            {
                MessageBox.Show(
                    "AutoClicker is already running.\n\nCheck the system tray for the existing window.",
                    "Tempo",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            // Route unhandled exceptions to the logger so the app fails gracefully.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainException;

            Logger.Info("Application starting (version 1.0.86).");
            EnvironmentInfo.LogSummary();

            // Verify the host meets Tempo's requirements. If not, explain what to
            // install by hand and exit cleanly rather than failing later.
            PrerequisiteChecker.Result prereq = PrerequisiteChecker.Check();
            if (!prereq.Satisfied)
            {
                PrerequisiteChecker.ReportAndAdvise(prereq);
                Logger.Info("Exiting: prerequisites not satisfied.");
                ReleaseMutex();
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

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
                    Logger.Info("Application exiting.");
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
                    "An unexpected error occurred:\n\n" + (ex == null ? "(unknown)" : ex.Message),
                    "Tempo - Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
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
