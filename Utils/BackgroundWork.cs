using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Runs a start-up chore in Windows' background processing mode — lowest CPU, disk and memory
    /// priority together — so it gives way to whatever the user is doing instead of competing with it.
    ///
    /// Written for the two chores that read or delete hundreds of megabytes on every start: the
    /// integrity check hashes all 266 unpacked files (270 MB, ~1.1–1.4 s on this project's SSD), and
    /// after an update the bundle clean-up deletes the previous build's 270 MB from TEMP. Both ran at
    /// normal priority the moment Tempo started, and a start-with-Windows launch starts at the busiest
    /// moment the disk has all day. Neither result is urgent: the check's verdict is evidence shown in
    /// Settings and Live debug, and the clean-up only reclaims space.
    /// </summary>
    internal static class BackgroundWork
    {
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentThread();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetThreadPriority(IntPtr thread, int priority);

        private const int THREAD_MODE_BACKGROUND_BEGIN = 0x00010000;
        private const int THREAD_MODE_BACKGROUND_END = 0x00020000;

        /// <summary>
        /// How long a start-with-Windows launch waits before its chores. Sign-in is when every other
        /// startup app, Windows' own services and the user's first clicks all want the disk at once.
        /// </summary>
        public const int SignInDelayMs = 45000;

        /// <summary>
        /// Runs <paramref name="work"/> on a thread of its own, after <paramref name="delayMs"/>, in
        /// background mode. A thread of its own rather than the pool: background mode belongs to the
        /// thread, and a pool thread would carry it into whatever work it picked up next. Never throws;
        /// a failure is logged under <paramref name="name"/>.
        /// </summary>
        public static void Start(string name, int delayMs, Action work)
        {
            try
            {
                var t = new Thread(() =>
                {
                    try
                    {
                        if (delayMs > 0) { Thread.Sleep(delayMs); }
                        bool background = SetThreadPriority(GetCurrentThread(), THREAD_MODE_BACKGROUND_BEGIN);
                        try { work(); }
                        finally
                        {
                            if (background) { SetThreadPriority(GetCurrentThread(), THREAD_MODE_BACKGROUND_END); }
                        }
                    }
                    catch (Exception ex) { Logger.Swallow("BackgroundWork." + name, ex); }
                })
                {
                    IsBackground = true,
                    Name = "Tempo " + name,
                };
                t.Start();
            }
            catch (Exception ex) { Logger.Swallow("BackgroundWork.Start." + name, ex); }
        }
    }
}
