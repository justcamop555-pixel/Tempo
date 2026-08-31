using System;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// Stops a running clicker or macro from operating Tempo's own interface.
    ///
    /// Injected input goes to whatever window is under the pointer, and Tempo is just
    /// another window. Start a run with the cursor over Tempo — or leave it there after
    /// pressing Start, or bring the window up mid-run to check progress — and Tempo
    /// begins clicking itself: ticking checkboxes, dragging the speed slider, switching
    /// tabs, and at 60+ clicks a second it reaches something that matters very quickly.
    /// RefreshBusyLock already disables the worst of it (profile delete, the CPS test),
    /// but that is a short list of named buttons — "Reset to defaults", "Uninstall
    /// Tempo…", the theme and language pickers and every checkbox on the Settings page
    /// stay live. A macro is worse still: it replays absolute coordinates recorded
    /// somewhere else entirely, so anything that has since moved under those points
    /// gets clicked.
    ///
    /// This is the third and broadest of Tempo's three overlapping protections:
    /// RefreshBusyLock disables specific controls, MinimizeWhileRecording hides the
    /// window (macros only, and only when a stop hotkey is bound), and this catches
    /// everything else — including the clicker, which never minimises. Users can turn
    /// it off with AppSettings.IgnoreOwnWindowWhileRunning.
    ///
    /// Filtering at the APPLICATION level rather than in MainForm.WndProc is deliberate:
    /// every button, checkbox and combo is its own window with its own WndProc, so the
    /// form never sees those clicks. IMessageFilter runs before the message is
    /// dispatched to any of them.
    ///
    /// Only Tempo's OWN injected events are dropped — they carry
    /// <see cref="Engine.InputSimulator.ExtraInfo"/>, which real input never has. That
    /// distinction is the whole point: a user watching their clicker do something wrong
    /// must still be able to hit Stop, and mouse-button HOTKEYS must still fire.
    /// </summary>
    internal sealed class SelfClickGuard : IMessageFilter
    {
        private readonly Func<bool> _isRunning;
        private long _blocked;

        /// <summary>How many self-inflicted input messages have been dropped this session.</summary>
        public long BlockedCount => System.Threading.Interlocked.Read(ref _blocked);

        /// <summary>When the most recent one was dropped, for Live debug.</summary>
        public DateTime LastBlockedUtc { get; private set; } = DateTime.MinValue;

        public SelfClickGuard(Func<bool> isRunning)
        {
            _isRunning = isRunning ?? (() => false);
        }

        // Mouse messages that can ACT on a control. Movement and hover are left alone:
        // dropping those would leave the UI with a stale hover state, and they cannot
        // change anything.
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_RBUTTONDBLCLK = 0x0206;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;
        private const int WM_MBUTTONDBLCLK = 0x0209;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_XBUTTONDOWN = 0x020B;
        private const int WM_XBUTTONUP = 0x020C;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int WM_NCLBUTTONUP = 0x00A2;
        private const int WM_NCLBUTTONDBLCLK = 0x00A3;

        // Keyboard matters just as much for MACROS. A recorded macro replays keystrokes,
        // and keystrokes go to whatever has FOCUS rather than whatever is under the
        // pointer — so a macro replaying "type the password, press Enter" with Tempo
        // focused types it into whichever Tempo field has the caret, and Space or Enter
        // on a focused button presses it. Minimise during playback is only a default;
        // users turn it off, and the clicker never minimises at all.
        //
        // Blocking the key-DOWN is what does the work: a filtered message is never
        // passed to TranslateMessage, so no WM_CHAR is ever synthesised from it. The
        // char messages below are belt-and-braces.
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_CHAR = 0x0102;
        private const int WM_DEADCHAR = 0x0103;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int WM_SYSCHAR = 0x0106;
        private const int WM_SYSDEADCHAR = 0x0107;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetMessageExtraInfo();

        public bool PreFilterMessage(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_LBUTTONDOWN:
                case WM_LBUTTONUP:
                case WM_LBUTTONDBLCLK:
                case WM_RBUTTONDOWN:
                case WM_RBUTTONUP:
                case WM_RBUTTONDBLCLK:
                case WM_MBUTTONDOWN:
                case WM_MBUTTONUP:
                case WM_MBUTTONDBLCLK:
                case WM_MOUSEWHEEL:
                case WM_XBUTTONDOWN:
                case WM_XBUTTONUP:
                case WM_NCLBUTTONDOWN:
                case WM_NCLBUTTONUP:
                case WM_NCLBUTTONDBLCLK:
                case WM_KEYDOWN:
                case WM_KEYUP:
                case WM_CHAR:
                case WM_DEADCHAR:
                case WM_SYSKEYDOWN:
                case WM_SYSKEYUP:
                case WM_SYSCHAR:
                case WM_SYSDEADCHAR:
                    break;
                default:
                    return false;       // not input that can act — nothing to do
            }

            bool running;
            try { running = _isRunning(); }
            catch { return false; }
            if (!running) { return false; }

            try
            {
                // GetMessageExtraInfo returns the value attached to the message being
                // processed right now, which is exactly what SendInput stamped on it.
                if (GetMessageExtraInfo() != Engine.InputSimulator.ExtraInfo)
                {
                    return false;       // a real click from the user — always let it through
                }
            }
            catch { return false; }

            System.Threading.Interlocked.Increment(ref _blocked);
            LastBlockedUtc = DateTime.UtcNow;
            return true;                // swallow: Tempo must not click itself
        }
    }
}
