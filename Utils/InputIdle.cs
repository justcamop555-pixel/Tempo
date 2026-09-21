using System;
using System.Runtime.InteropServices;

namespace AutoClicker.Utils
{
    /// <summary>
    /// How long the PC has been idle — no keyboard or mouse input, system-wide. This is the same
    /// signal a screen saver or lock screen uses, and it is the honest measure of "the user walked
    /// away": it keeps resetting while they are doing anything at all on the machine, in any app, and
    /// only climbs when the desk is genuinely empty. The vault's idle auto-lock is timed off it.
    ///
    /// Deliberately system idle, not "idle in Tempo": leaving the vault unlocked is only a risk once
    /// nobody is at the keyboard, and a per-window measure would lock the vault while the user was
    /// plainly still there in another window.
    /// </summary>
    public static class InputIdle
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;   // tick count (ms) of the last input event
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        /// <summary>
        /// Time since the last keyboard/mouse input anywhere on this session. <see cref="TimeSpan.Zero"/>
        /// if it cannot be read, so a caller treats "unknown" as "active" and never locks on a failure.
        /// </summary>
        public static TimeSpan Since()
        {
            try
            {
                var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
                if (!GetLastInputInfo(ref info)) { return TimeSpan.Zero; }

                // Both are the 32-bit tick count, which wraps every ~49.7 days. Unsigned subtraction
                // wraps the same way, so the delta stays correct across the rollover.
                uint now = (uint)Environment.TickCount;
                uint idleMs = now - info.dwTime;
                return TimeSpan.FromMilliseconds(idleMs);
            }
            catch
            {
                return TimeSpan.Zero;
            }
        }
    }
}
