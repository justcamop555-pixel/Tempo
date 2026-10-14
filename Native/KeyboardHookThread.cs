using System;
using System.Runtime.InteropServices;
using System.Threading;
using AutoClicker.Utils;

namespace AutoClicker.Native
{
    /// <summary>
    /// A low-level keyboard hook on a thread of its own, one that does nothing but receive the hook's
    /// callbacks.
    ///
    /// Every key pressed anywhere waits for each low-level hook's callback before it reaches the app
    /// in front, and Windows delivers a callback on the thread that installed the hook, inside its
    /// message loop. On the UI thread that wait includes whatever the window is busy with (a layout,
    /// a paint, a slow dialog), so every key in the game lags behind it; and when the wait passes
    /// <c>LowLevelHooksTimeout</c> Windows removes the hook without a word (see HookHealth). Here the
    /// wait is only the listener's few comparisons. Same design as <see cref="ShortcutGuard"/>.
    ///
    /// Listeners run on the hook thread: keep them short, and marshal before touching UI.
    /// </summary>
    public sealed class KeyboardHookThread : IDisposable
    {
        private readonly LowLevelKeyboardHook _hook = new LowLevelKeyboardHook();
        private readonly string _name;
        private Thread _thread;
        private uint _threadId;
        private volatile bool _installed;
        private bool _disposed;

        public KeyboardHookThread(string name, bool quiet = false)
        {
            _name = name ?? "Tempo keyboard hook";
            _hook.Quiet = quiet;
        }

        /// <summary>Raised on the hook's own thread for every key. Set <see cref="KeyboardHookEventArgs.Suppress"/> to swallow it.</summary>
        public event EventHandler<KeyboardHookEventArgs> KeyEvent
        {
            add { _hook.KeyEvent += value; }
            remove { _hook.KeyEvent -= value; }
        }

        /// <summary>True while the hook is installed and its thread is listening.</summary>
        public bool IsRunning => _installed && _thread != null && _thread.IsAlive;

        /// <summary>The managed id of the hook's thread (Live debug, the tests); 0 when it is not running.</summary>
        public int ManagedThreadId { get; private set; }

        /// <summary>Installs the hook on a new thread. False when Windows refused the hook.</summary>
        public bool Start()
        {
            if (_disposed) { throw new ObjectDisposedException(nameof(KeyboardHookThread)); }
            if (IsRunning) { return true; }

            var ready = new ManualResetEventSlim(false);
            bool installed = false;
            var thread = new Thread(() =>
            {
                _threadId = NativeMethods.GetCurrentThreadId();
                // A message queue before anyone is told the thread is up: WM_QUIT posted to a thread
                // without one is lost, and the thread would never end.
                PeekMessage(out MSG peek, IntPtr.Zero, 0, 0, PM_NOREMOVE);
                installed = _hook.Start();
                _installed = installed;
                ready.Set();
                if (!installed) { return; }
                try
                {
                    // The callbacks are delivered inside GetMessage. Nothing is ever posted here but the
                    // WM_QUIT that ends it.
                    while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0) { }
                }
                finally
                {
                    _installed = false;
                    _hook.Stop();
                }
            })
            {
                IsBackground = true,
                Name = _name,
                // Every key waits for this thread, so it must not queue behind a busy game's threads.
                Priority = ThreadPriority.AboveNormal
            };
            thread.Start();
            _thread = thread;   // even if it is slow to start: Stop must still be able to end it
            ManagedThreadId = thread.ManagedThreadId;
            bool ok = ready.Wait(3000) && installed;
            ready.Dispose();
            if (!ok)
            {
                Logger.Warn("[Hooks] " + _name + ": the keyboard hook would not install on its own thread.");
            }
            return ok;
        }

        /// <summary>Removes the hook and ends its thread.</summary>
        public void Stop()
        {
            Thread thread = _thread;
            _thread = null;
            ManagedThreadId = 0;
            if (thread == null || !thread.IsAlive) { return; }
            PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            if (!thread.Join(2000))
            {
                Logger.Warn("[Hooks] " + _name + ": the hook thread did not stop in time.");
            }
        }

        public void Dispose()
        {
            if (_disposed) { return; }
            Stop();
            _hook.Dispose();
            _disposed = true;
        }

        // ── The hook thread's message loop ──────────────────────────────────────────────────────
        private const uint WM_QUIT = 0x0012, PM_NOREMOVE = 0x0000;

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG msg, IntPtr hWnd, uint filterMin, uint filterMax);

        [DllImport("user32.dll")]
        private static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint filterMin, uint filterMax, uint remove);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
    }
}
