using System;
using System.Runtime.InteropServices;
using AutoClicker.Models;
using AutoClicker.Native;
using AutoClicker.Utils;

namespace AutoClicker.Engine
{
    /// <summary>
    /// Performs the actual mouse and keyboard actuation.
    ///
    /// The hot path is built around a few rules optimised for both speed and
    /// compatibility:
    ///
    ///   1. Every actuation goes through <c>SendInput</c>. SendInput delivers real
    ///      hardware-style input events, so games and apps that use raw input or
    ///      DirectInput see the click — unlike a bare <c>SetCursorPos</c> call.
    ///
    ///   2. Multi-event actuations (button-down + button-up, double/triple clicks,
    ///      and move + click) are submitted as a single batched <c>SendInput</c>
    ///      call rather than several. There is no blocking <c>Sleep</c> between the
    ///      events. The OS preserves the relative order while compressing the gap
    ///      between them to microseconds, which is what makes very high click
    ///      rates possible.
    ///
    ///   3. Movement uses absolute coordinates normalised across the entire virtual
    ///      desktop (<c>MOUSEEVENTF_VIRTUALDESK | MOUSEEVENTF_ABSOLUTE</c>). This
    ///      is the most reliable form of synthetic movement and works correctly on
    ///      multi-monitor setups with mixed DPI scaling.
    ///   4. If SendInput ever returns zero (blocked or filtered), the call falls
    ///      back to the legacy <c>mouse_event</c> API or <c>SetCursorPos</c>.
    /// </summary>
    public static class InputSimulator
    {
        private static readonly int InputStructSize = Marshal.SizeOf(typeof(NativeMethods.INPUT));

        // Pre-cached extra info pointer so the hot path does not P/Invoke twice.
        private static readonly IntPtr ExtraInfo = NativeMethods.GetMessageExtraInfo();

        // ─────────────────────────────────────────────────────────────────────
        //  Movement
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Moves the cursor to an absolute screen coordinate. Primary path uses
        /// <c>SendInput</c> with absolute virtual-desktop coordinates so games
        /// using raw input still see the move; if that ever fails, falls back to
        /// <c>SetCursorPos</c>.
        /// </summary>
        public static void MoveTo(int x, int y)
        {
            ScreenGeometry.Clamp(ref x, ref y);

            if (TrySendAbsoluteMove(x, y))
            {
                return;
            }

            // Fallback: bare cursor position update.
            if (!NativeMethods.SetCursorPos(x, y))
            {
                Logger.Warn($"All movement APIs failed for ({x}, {y}).");
            }
        }

        /// <summary>
        /// Submits a single batched SendInput containing an absolute move followed
        /// by all the down/up events that make up the requested click style. The
        /// whole actuation reaches the OS as one ordered burst, so there is no
        /// inter-event scheduler latency.
        /// </summary>
        public static void MoveAndClick(int x, int y, MouseButtonType button, ClickStyle style)
        {
            ScreenGeometry.Clamp(ref x, ref y);

            int clickCount = ClickCount(style);
            int total = 1 + 2 * clickCount; // 1 move + (down + up) per click

            var inputs = new NativeMethods.INPUT[total];
            int idx = 0;

            if (TryBuildAbsoluteMove(x, y, out NativeMethods.INPUT moveInput))
            {
                inputs[idx++] = moveInput;
            }
            else
            {
                // SetCursorPos cannot be batched into SendInput, so use it outside.
                NativeMethods.SetCursorPos(x, y);

                // Resize the array so the call below matches its element count.
                Array.Resize(ref inputs, total - 1);
            }

            uint downFlag = ButtonFlag(button, down: true);
            uint upFlag = ButtonFlag(button, down: false);

            for (int i = 0; i < clickCount; i++)
            {
                inputs[idx++] = MakeButton(downFlag);
                inputs[idx++] = MakeButton(upFlag);
            }

            uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, InputStructSize);
            if (sent == 0)
            {
                // Legacy fallback: emit one (down, up) pair per click in the style.
                for (int i = 0; i < clickCount; i++)
                {
                    NativeMethods.mouse_event(downFlag, 0, 0, 0, IntPtr.Zero);
                    NativeMethods.mouse_event(upFlag, 0, 0, 0, IntPtr.Zero);
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Clicking (batched, no inter-event Sleep)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Performs a complete down + up click as a single batched SendInput call.
        /// No blocking sleep is inserted, so this is suitable for very high click
        /// rates. Apps still register the click because the OS preserves the event
        /// order within the batch.
        /// </summary>
        public static void Click(MouseButtonType button)
        {
            uint downFlag = ButtonFlag(button, down: true);
            uint upFlag = ButtonFlag(button, down: false);

            var inputs = new NativeMethods.INPUT[2];
            inputs[0] = MakeButton(downFlag);
            inputs[1] = MakeButton(upFlag);

            uint sent = NativeMethods.SendInput(2, inputs, InputStructSize);
            if (sent == 0)
            {
                NativeMethods.mouse_event(downFlag, 0, 0, 0, IntPtr.Zero);
                NativeMethods.mouse_event(upFlag, 0, 0, 0, IntPtr.Zero);
            }
        }

        /// <summary>Presses (and holds) a button.</summary>
        public static void ButtonDown(MouseButtonType button)
        {
            SendButton(button, down: true);
        }

        /// <summary>Releases a previously pressed button.</summary>
        public static void ButtonUp(MouseButtonType button)
        {
            SendButton(button, down: false);
        }

        /// <summary>
        /// Performs a click of the requested style (single, double or triple) in a
        /// single batched SendInput call.
        /// </summary>
        public static void ClickStyled(MouseButtonType button, ClickStyle style)
        {
            int count = ClickCount(style);
            uint downFlag = ButtonFlag(button, down: true);
            uint upFlag = ButtonFlag(button, down: false);

            var inputs = new NativeMethods.INPUT[count * 2];
            for (int i = 0; i < count; i++)
            {
                inputs[i * 2] = MakeButton(downFlag);
                inputs[i * 2 + 1] = MakeButton(upFlag);
            }

            uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, InputStructSize);
            if (sent == 0)
            {
                // Legacy fallback: walk the events manually.
                for (int i = 0; i < count; i++)
                {
                    NativeMethods.mouse_event(downFlag, 0, 0, 0, IntPtr.Zero);
                    NativeMethods.mouse_event(upFlag, 0, 0, 0, IntPtr.Zero);
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Wheel
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Scrolls the mouse wheel by the given delta.</summary>
        public static void Wheel(int delta)
        {
            var inputs = new NativeMethods.INPUT[1];
            inputs[0].type = NativeMethods.INPUT_MOUSE;
            inputs[0].U.mi = new NativeMethods.MOUSEINPUT
            {
                dx = 0,
                dy = 0,
                mouseData = unchecked((uint)delta),
                dwFlags = NativeMethods.MOUSEEVENTF_WHEEL,
                time = 0,
                dwExtraInfo = ExtraInfo
            };

            uint sent = NativeMethods.SendInput(1, inputs, InputStructSize);
            if (sent == 0)
            {
                NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)delta), IntPtr.Zero);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Keyboard
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Presses a key down (does not release it).</summary>
        public static void KeyDown(int virtualKey)
        {
            SendKey((ushort)virtualKey, keyUp: false);
        }

        /// <summary>Releases a key.</summary>
        public static void KeyUp(int virtualKey)
        {
            SendKey((ushort)virtualKey, keyUp: true);
        }

        /// <summary>
        /// Presses and releases a key as a single batched SendInput call (no
        /// blocking sleep between events).
        /// </summary>
        public static void KeyPress(int virtualKey)
        {
            ushort vk = (ushort)virtualKey;

            var inputs = new NativeMethods.INPUT[2];
            inputs[0].type = NativeMethods.INPUT_KEYBOARD;
            inputs[0].U.ki = new NativeMethods.KEYBDINPUT
            {
                wVk = vk,
                wScan = 0,
                dwFlags = 0,
                time = 0,
                dwExtraInfo = ExtraInfo
            };
            inputs[1].type = NativeMethods.INPUT_KEYBOARD;
            inputs[1].U.ki = new NativeMethods.KEYBDINPUT
            {
                wVk = vk,
                wScan = 0,
                dwFlags = NativeMethods.KEYEVENTF_KEYUP,
                time = 0,
                dwExtraInfo = ExtraInfo
            };

            NativeMethods.SendInput(2, inputs, InputStructSize);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Internals
        // ─────────────────────────────────────────────────────────────────────

        private static int ClickCount(ClickStyle style)
        {
            switch (style)
            {
                case ClickStyle.Single: return 1;
                case ClickStyle.Double: return 2;
                case ClickStyle.Triple: return 3;
                default: return 1;
            }
        }

        /// <summary>
        /// Attempts an absolute SendInput move. Returns true on success.
        /// </summary>
        private static bool TrySendAbsoluteMove(int x, int y)
        {
            if (!TryBuildAbsoluteMove(x, y, out NativeMethods.INPUT input))
            {
                return false;
            }

            var inputs = new NativeMethods.INPUT[] { input };
            uint sent = NativeMethods.SendInput(1, inputs, InputStructSize);
            return sent != 0;
        }

        /// <summary>
        /// Builds the INPUT record for an absolute move across the virtual desktop
        /// at the given screen coordinate. Returns false only when the virtual
        /// desktop is degenerate.
        /// </summary>
        private static bool TryBuildAbsoluteMove(int x, int y, out NativeMethods.INPUT input)
        {
            int vWidth = ScreenGeometry.VirtualWidth;
            int vHeight = ScreenGeometry.VirtualHeight;

            if (vWidth < 2 || vHeight < 2)
            {
                input = default;
                return false;
            }

            // Normalise (left/top, right/bottom) → (0, 0)..(65535, 65535).
            long nx = ((long)(x - ScreenGeometry.VirtualLeft) * 65535L) / (vWidth - 1);
            long ny = ((long)(y - ScreenGeometry.VirtualTop) * 65535L) / (vHeight - 1);

            if (nx < 0) nx = 0;
            if (ny < 0) ny = 0;
            if (nx > 65535) nx = 65535;
            if (ny > 65535) ny = 65535;

            input = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                U = new NativeMethods.InputUnion
                {
                    mi = new NativeMethods.MOUSEINPUT
                    {
                        dx = (int)nx,
                        dy = (int)ny,
                        mouseData = 0,
                        dwFlags = NativeMethods.MOUSEEVENTF_MOVE
                                | NativeMethods.MOUSEEVENTF_ABSOLUTE
                                | NativeMethods.MOUSEEVENTF_VIRTUALDESK,
                        time = 0,
                        dwExtraInfo = ExtraInfo
                    }
                }
            };

            return true;
        }

        private static NativeMethods.INPUT MakeButton(uint flag)
        {
            return new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                U = new NativeMethods.InputUnion
                {
                    mi = new NativeMethods.MOUSEINPUT
                    {
                        dx = 0,
                        dy = 0,
                        mouseData = 0,
                        dwFlags = flag,
                        time = 0,
                        dwExtraInfo = ExtraInfo
                    }
                }
            };
        }

        private static void SendButton(MouseButtonType button, bool down)
        {
            uint flag = ButtonFlag(button, down);

            var inputs = new NativeMethods.INPUT[] { MakeButton(flag) };
            uint sent = NativeMethods.SendInput(1, inputs, InputStructSize);
            if (sent == 0)
            {
                NativeMethods.mouse_event(flag, 0, 0, 0, IntPtr.Zero);
            }
        }

        private static void SendKey(ushort virtualKey, bool keyUp)
        {
            var inputs = new NativeMethods.INPUT[1];
            inputs[0].type = NativeMethods.INPUT_KEYBOARD;
            inputs[0].U.ki = new NativeMethods.KEYBDINPUT
            {
                wVk = virtualKey,
                wScan = 0,
                dwFlags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0u,
                time = 0,
                dwExtraInfo = ExtraInfo
            };

            NativeMethods.SendInput(1, inputs, InputStructSize);
        }

        private static uint ButtonFlag(MouseButtonType button, bool down)
        {
            switch (button)
            {
                case MouseButtonType.Left:
                    return down ? NativeMethods.MOUSEEVENTF_LEFTDOWN : NativeMethods.MOUSEEVENTF_LEFTUP;
                case MouseButtonType.Right:
                    return down ? NativeMethods.MOUSEEVENTF_RIGHTDOWN : NativeMethods.MOUSEEVENTF_RIGHTUP;
                case MouseButtonType.Middle:
                    return down ? NativeMethods.MOUSEEVENTF_MIDDLEDOWN : NativeMethods.MOUSEEVENTF_MIDDLEUP;
                default:
                    return down ? NativeMethods.MOUSEEVENTF_LEFTDOWN : NativeMethods.MOUSEEVENTF_LEFTUP;
            }
        }
    }
}
