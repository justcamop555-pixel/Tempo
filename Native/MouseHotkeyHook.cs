using System;
using System.Runtime.InteropServices;
using AutoClicker.Models;
using AutoClicker.Native;
using AutoClicker.Utils;

namespace AutoClicker.Native
{
    /// <summary>
    /// Event raised when a mouse button is pressed while the hotkey hook is
    /// active. Set <see cref="Suppress"/> to true to stop the click reaching other
    /// applications (used so a bound side button doesn't also trigger browser
    /// back/forward, etc.).
    /// </summary>
    public sealed class MouseHotkeyEventArgs : EventArgs
    {
        public HotkeyMouseButton Button { get; }
        public bool Injected { get; }
        public bool Suppress { get; set; }

        public MouseHotkeyEventArgs(HotkeyMouseButton button, bool injected)
        {
            Button = button;
            Injected = injected;
        }
    }

    /// <summary>
    /// A low-level mouse hook (WH_MOUSE_LL) used purely for hotkey detection. It
    /// only reports button-down transitions, flags injected events (so the
    /// auto-clicker's own clicks can be filtered by listeners), and lets the
    /// listener suppress the event by returning 1 from the hook — and then
    /// suppresses that press's release as well, so the button does nothing at all.
    ///
    /// This is separate from <see cref="LowLevelMouseHook"/> (used by the macro
    /// recorder) because the hotkey use-case needs the injected flag and the
    /// ability to swallow the event.
    /// </summary>
    public sealed class MouseHotkeyHook : IDisposable
    {
        private readonly NativeMethods.LowLevelProc _proc;
        private IntPtr _hookHandle = IntPtr.Zero;
        private bool _disposed;

        // Buttons whose PRESS was swallowed, indexed by HotkeyMouseButton, so the matching RELEASE is
        // swallowed too. Only the press used to be: the bound side button's release still reached the
        // window in front, and Windows turns an X-button RELEASE into the Back/Forward command
        // (DefWindowProc makes WM_APPCOMMAND from WM_XBUTTONUP). So X1 bound to a Tempo action still
        // sent the browser Back on every press, and a bound middle button left a release with no press.
        // Kept across Stop/Start: re-applying the hotkeys stops and restarts this hook, often while the
        // button that triggered it is still held (Next profile re-binds the keys of the new profile).
        private readonly bool[] _pressSwallowed = new bool[6];

        public event EventHandler<MouseHotkeyEventArgs> ButtonDown;

        public bool IsRunning => _hookHandle != IntPtr.Zero;

        public MouseHotkeyHook()
        {
            _proc = HookCallback;
        }

        public bool Start()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MouseHotkeyHook));
            }

            if (IsRunning)
            {
                return true;
            }

            IntPtr moduleHandle = NativeMethods.GetModuleHandle(null);
            _hookHandle = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_MOUSE_LL, _proc, moduleHandle, 0);

            if (_hookHandle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                Logger.Error($"[Hooks] failed to install the mouse-hotkey hook. Win32 error {error}.");
                return false;
            }

            // A swallowed press whose button went up while the hook was off has had its release
            // already (unseen): forget it, or the NEXT release of that button would be eaten after a
            // press that reached the app — a button the app would then think is still held.
            for (int b = 1; b < _pressSwallowed.Length; b++)
            {
                if (_pressSwallowed[b] && !IsPhysicallyDown((HotkeyMouseButton)b))
                {
                    _pressSwallowed[b] = false;
                }
            }

            Logger.Info("[Hooks] mouse-hotkey hook installed.");
            return true;
        }

        private static bool IsPhysicallyDown(HotkeyMouseButton button)
        {
            int vk;
            switch (button)
            {
                case HotkeyMouseButton.Left: vk = 0x01; break;      // VK_LBUTTON
                case HotkeyMouseButton.Right: vk = 0x02; break;     // VK_RBUTTON
                case HotkeyMouseButton.Middle: vk = 0x04; break;    // VK_MBUTTON
                case HotkeyMouseButton.XButton1: vk = 0x05; break;  // VK_XBUTTON1
                case HotkeyMouseButton.XButton2: vk = 0x06; break;  // VK_XBUTTON2
                default: return false;
            }
            return (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        public void Stop()
        {
            if (_hookHandle != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
                Logger.Info("[Hooks] mouse-hotkey hook removed.");
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    int message = wParam.ToInt32();
                    HotkeyMouseButton button = HotkeyMouseButton.None;
                    HotkeyMouseButton released = HotkeyMouseButton.None;

                    var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                    bool injected = (data.flags & NativeMethods.LLMHF_INJECTED) != 0;

                    switch (message)
                    {
                        case NativeMethods.WM_LBUTTONDOWN:
                            button = HotkeyMouseButton.Left;
                            break;
                        case NativeMethods.WM_RBUTTONDOWN:
                            button = HotkeyMouseButton.Right;
                            break;
                        case NativeMethods.WM_MBUTTONDOWN:
                            button = HotkeyMouseButton.Middle;
                            break;
                        case NativeMethods.WM_XBUTTONDOWN:
                            button = WhichXButton(data.mouseData);
                            break;
                        case NativeMethods.WM_LBUTTONUP:
                            released = HotkeyMouseButton.Left;
                            break;
                        case NativeMethods.WM_RBUTTONUP:
                            released = HotkeyMouseButton.Right;
                            break;
                        case NativeMethods.WM_MBUTTONUP:
                            released = HotkeyMouseButton.Middle;
                            break;
                        case NativeMethods.WM_XBUTTONUP:
                            released = WhichXButton(data.mouseData);
                            break;
                    }

                    if (released != HotkeyMouseButton.None)
                    {
                        // The release of a press that was swallowed: swallow it too. Only a real
                        // one — a swallowed press is never injected (injected presses are not
                        // hotkeys), so an injected release of the same button belongs to someone else.
                        if (!injected && _pressSwallowed[(int)released])
                        {
                            _pressSwallowed[(int)released] = false;
                            return (IntPtr)1;
                        }
                    }
                    else if (button != HotkeyMouseButton.None)
                    {
                        // A new press pairs afresh: whatever happened to the last one's release,
                        // this press decides what happens to the next.
                        if (!injected) { _pressSwallowed[(int)button] = false; }

                        if (ButtonDown != null)
                        {
                            var args = new MouseHotkeyEventArgs(button, injected);
                            // Timed: this one runs the hotkey ACTIONS, so it is the likeliest of the
                            // three to grow slow as the app gains features — see HookHealth.
                            long started = System.Diagnostics.Stopwatch.GetTimestamp();
                            ButtonDown.Invoke(this, args);
                            HookHealth.NoteCallback("mouse-hotkey", started);

                            if (args.Suppress)
                            {
                                // Returning a non-zero value swallows the event so it
                                // never reaches the focused application.
                                if (!injected) { _pressSwallowed[(int)button] = true; }
                                return (IntPtr)1;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("[Hooks] exception inside the mouse-hotkey hook callback.", ex);
                }
            }

            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        /// <summary>The high word of an X-button message's mouseData says which one.</summary>
        private static HotkeyMouseButton WhichXButton(uint mouseData)
        {
            uint which = (mouseData >> 16) & 0xFFFF;
            return which == NativeMethods.XBUTTON2 ? HotkeyMouseButton.XButton2 : HotkeyMouseButton.XButton1;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Stop();
            _disposed = true;
        }
    }
}
