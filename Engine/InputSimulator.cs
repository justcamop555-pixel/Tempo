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

        /// <summary>
        /// Stamped into every event Tempo injects, so Tempo can recognise its OWN
        /// clicks when they come back round the input queue.
        ///
        /// Windows delivers injected input to whatever window is under the pointer —
        /// including Tempo's own. Leave the clicker running with the cursor over the
        /// Tempo window and it starts clicking its own interface: toggling checkboxes,
        /// switching tabs, and eventually landing on something destructive like Delete
        /// profile. A macro replaying recorded coordinates does the same if the window
        /// has moved since. Nothing distinguished those clicks from the user's, because
        /// this used to be GetMessageExtraInfo() — whatever value the thread happened to
        /// have last seen, i.e. no signature at all.
        ///
        /// The message filter in MainForm drops mouse messages carrying this value while
        /// a run is in progress. REAL clicks never carry it, so the Stop button, the tray
        /// and everything else keep working — which matters, because the user has to be
        /// able to stop a run that is misbehaving.
        /// </summary>
        internal static readonly IntPtr ExtraInfo = new IntPtr(0x54454D50);   // 'TEMP'

        /// <summary>
        /// The signature for input Tempo injects ON BEHALF OF A PHYSICAL BUTTON PRESS —
        /// today, the second-cursor relay: the user really did press a button on their
        /// second mouse, and Tempo only re-emits it at the second cursor's position.
        ///
        /// It has to be distinguishable from <see cref="ExtraInfo"/> because SelfClickGuard
        /// swallows THAT while a run is in progress. Without a separate tag, aiming the
        /// second cursor at Tempo's own window and clicking Stop mid-run did nothing —
        /// the guard could not tell a relayed human click from the automation it exists
        /// to block. Second-cursor SPAM deliberately keeps the automation tag: once it is
        /// auto-repeating it is exactly the runaway-clicks case the guard is for.
        /// </summary>
        internal static readonly IntPtr UserRelayExtraInfo = new IntPtr(0x54454D55);   // 'TEMU'

        // Depth rather than a bool so nested relay calls (MoveTo + ButtonDown inside one
        // press) can't have the inner scope clear the outer one. ThreadStatic because the
        // relay runs on the UI thread while the click engine injects from its own thread —
        // a shared flag would leak the user tag onto automation running concurrently.
        [ThreadStatic] private static int _relayDepth;

        private static IntPtr CurrentExtraInfo => _relayDepth > 0 ? UserRelayExtraInfo : ExtraInfo;

        /// <summary>
        /// Marks everything injected inside the scope as a relayed physical action rather
        /// than automation. Use with <c>using</c>.
        /// </summary>
        internal static IDisposable RelayingUserInput()
        {
            _relayDepth++;
            return new RelayScope();
        }

        private sealed class RelayScope : IDisposable
        {
            private bool _done;
            public void Dispose()
            {
                if (_done) { return; }
                _done = true;
                if (_relayDepth > 0) { _relayDepth--; }
            }
        }

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
                Logger.Warn($"[Clicker] all movement APIs failed for ({x}, {y}).");
            }
        }

        /// <summary>
        /// Moves the cursor by a RELATIVE delta via SendInput. Unlike SetCursorPos this
        /// also feeds raw-input games the movement, which is what makes drag-to-pan /
        /// hold-to-look work when a second-mouse button is held. Deltas are in mouse
        /// counts (subject to the OS pointer-speed curve).
        /// </summary>
        public static void MoveRelative(int dx, int dy)
        {
            if (dx == 0 && dy == 0) { return; }
            var input = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                U = new NativeMethods.InputUnion
                {
                    mi = new NativeMethods.MOUSEINPUT
                    {
                        dx = dx,
                        dy = dy,
                        mouseData = 0,
                        dwFlags = NativeMethods.MOUSEEVENTF_MOVE,
                        time = 0,
                        dwExtraInfo = CurrentExtraInfo
                    }
                }
            };
            var inputs = new NativeMethods.INPUT[] { input };
            uint sent = NativeMethods.SendInput(1, inputs, InputStructSize);
            if (sent == 0)
            {
                NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_MOVE, unchecked((uint)dx), unchecked((uint)dy), 0, CurrentExtraInfo);
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
                    NativeMethods.mouse_event(downFlag, 0, 0, 0, CurrentExtraInfo);
                    NativeMethods.mouse_event(upFlag, 0, 0, 0, CurrentExtraInfo);
                }
            }
        }

        /// <summary>
        /// A REAL wheel notch at a screen point: briefly warps the pointer there, sends
        /// a genuine SendInput wheel (Windows routes wheel input to the window under the
        /// pointer — with the OS's default "scroll inactive windows" this scrolls the
        /// target without focusing it), then restores the pointer. Used by the second
        /// cursor so its mouse's wheel scrolls what's under the SECOND cursor, not
        /// whatever sits under the parked real pointer.
        /// </summary>
        public static void RealWheelAtAndRestore(int x, int y, int delta, int restoreX, int restoreY)
        {
            ScreenGeometry.Clamp(ref x, ref y);
            MoveTo(x, y);
            Wheel(delta);
            ScreenGeometry.Clamp(ref restoreX, ref restoreY);
            NativeMethods.SetCursorPos(restoreX, restoreY);
        }

        /// <summary>
        /// A REAL click at a screen point that then puts the cursor back where it was.
        /// The move + down + up go out as one batched SendInput (so the click lands at
        /// the target), then the cursor is restored to <paramref name="restoreX"/>/
        /// <paramref name="restoreY"/>. Unlike posted messages, a real click actually
        /// opens apps, hits desktop icons and any control — used by the second cursor so
        /// a second mouse can genuinely click things while the main pointer stays parked.
        /// The cursor only visits the target for the instant of the click.
        /// </summary>
        public static void RealClickAtAndRestore(int x, int y, MouseButtonType button, int restoreX, int restoreY)
        {
            ScreenGeometry.Clamp(ref x, ref y);
            // Absolute SendInput move so raw-input games see the cursor at the target,
            // then a real press/release with a short dwell — many games only register a
            // click if the button is actually held for a frame or two, not an instant
            // down+up. Finally, restore the pointer to where the main mouse left it.
            MoveTo(x, y);
            ButtonDown(button);
            System.Threading.Thread.Sleep(24);
            ButtonUp(button);
            System.Threading.Thread.Sleep(8);
            ScreenGeometry.Clamp(ref restoreX, ref restoreY);
            NativeMethods.SetCursorPos(restoreX, restoreY);
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
                NativeMethods.mouse_event(downFlag, 0, 0, 0, CurrentExtraInfo);
                NativeMethods.mouse_event(upFlag, 0, 0, 0, CurrentExtraInfo);
            }
        }

        /// <summary>Presses (and holds) a button.</summary>
        /// <summary>
        /// Like <see cref="ClickStyled"/>, but holds each click's button down for
        /// <paramref name="holdMilliseconds"/> before releasing. Used for the optional
        /// "hold each click" feature; when hold is 0 the engine uses the faster
        /// batched <see cref="ClickStyled"/> instead, so normal clicking is unchanged.
        /// </summary>
        public static void ClickStyledHeld(MouseButtonType button, ClickStyle style, int holdMilliseconds)
        {
            if (holdMilliseconds <= 0)
            {
                ClickStyled(button, style);
                return;
            }

            int count = ClickCount(style);
            for (int i = 0; i < count; i++)
            {
                ButtonDown(button);
                System.Threading.Thread.Sleep(holdMilliseconds);
                ButtonUp(button);
            }
        }

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
                    NativeMethods.mouse_event(downFlag, 0, 0, 0, CurrentExtraInfo);
                    NativeMethods.mouse_event(upFlag, 0, 0, 0, CurrentExtraInfo);
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Background clicking (no cursor movement)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Clicks a screen point by POSTING the click straight to the window under
        /// that point, WITHOUT moving the real mouse cursor. This is the "second
        /// mouse" / AFK-on-another-monitor feature: Tempo clicks the target (e.g. a
        /// game on monitor 2) while you keep using your real mouse freely on monitor
        /// 1.
        ///
        /// Honest limitation: posted messages reach apps and many games that read the
        /// standard Windows mouse messages (windowed/borderless MMOs, idle/2D/browser
        /// games, ordinary desktop apps). Games that read the mouse through raw input
        /// or DirectInput — most competitive 3D/FPS titles — ignore posted clicks and
        /// only respond to real hardware input (which does move the cursor). There is
        /// no way around that from outside the game; for those, use the normal
        /// (cursor-moving) fixed-position mode instead.
        ///
        /// Returns false when there is no window at the point (caller can log it).
        /// </summary>
        public static bool BackgroundClick(int x, int y, MouseButtonType button, ClickStyle style)
        {
            return BackgroundClickHeld(x, y, button, style, 0);
        }

        /// <summary>
        /// Like <see cref="BackgroundClick"/>, but holds each click's button down for
        /// <paramref name="holdMilliseconds"/> before releasing.
        /// </summary>
        public static bool BackgroundClickHeld(int x, int y, MouseButtonType button, ClickStyle style, int holdMilliseconds)
        {
            IntPtr hwnd = ResolveWindowAt(x, y);
            if (hwnd == IntPtr.Zero)
            {
                WarnBackgroundNoWindow(x, y);
                return false;
            }

            // Convert the screen point to the target window's client coordinates —
            // that's what the mouse messages carry.
            var pt = new NativeMethods.POINT(x, y);
            NativeMethods.ScreenToClient(hwnd, ref pt);
            IntPtr lParam = MakeLParam(pt.X, pt.Y);

            uint downMsg, upMsg;
            int mk;
            ButtonMessages(button, out downMsg, out upMsg, out mk);

            int count = ClickCount(style);
            for (int i = 0; i < count; i++)
            {
                // A move first so hover-aware controls register the pointer, then the
                // down/up pair. wParam on the down carries the "button held" flag.
                NativeMethods.PostMessage(hwnd, (uint)NativeMethods.WM_MOUSEMOVE, IntPtr.Zero, lParam);
                NativeMethods.PostMessage(hwnd, downMsg, (IntPtr)mk, lParam);
                if (holdMilliseconds > 0)
                {
                    System.Threading.Thread.Sleep(holdMilliseconds);
                }
                NativeMethods.PostMessage(hwnd, upMsg, IntPtr.Zero, lParam);
            }
            return true;
        }

        /// <summary>
        /// Posts a mouse-wheel notch to the window under a screen point, without moving
        /// the real cursor — used by the second cursor so a second mouse's wheel scrolls
        /// the app beneath it. <paramref name="delta"/> is in WHEEL_DELTA units (120 per
        /// notch, positive = away/up). Best-effort: some apps only scroll the focused
        /// window, which a posted message can't change.
        /// </summary>
        public static bool BackgroundWheel(int x, int y, int delta)
        {
            if (delta == 0) { return false; }
            IntPtr hwnd = ResolveWindowAt(x, y);
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }
            // Unlike the button messages, WM_MOUSEWHEEL carries SCREEN coordinates in
            // lParam (not client), and the wheel delta in the high word of wParam.
            IntPtr lParam = MakeLParam(x, y);
            IntPtr wParam = (IntPtr)((delta & 0xFFFF) << 16);
            NativeMethods.PostMessage(hwnd, (uint)NativeMethods.WM_MOUSEWHEEL, wParam, lParam);
            return true;
        }

        /// <summary>
        /// Finds the window that should receive a click at a screen point: the
        /// top-level window under it, then the deepest visible, non-transparent child
        /// at that spot (so a click lands on the actual control, not just the frame).
        /// </summary>
        private static IntPtr ResolveWindowAt(int x, int y)
        {
            var pt = new NativeMethods.POINT(x, y);
            IntPtr hwnd = NativeMethods.WindowFromPoint(pt);
            if (hwnd == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            // Descend into child controls at the point (a few levels is plenty; guards
            // against a pathological cycle). WindowFromPoint already returns a fairly
            // deep window, but this reaches controls it can skip past.
            for (int depth = 0; depth < 8; depth++)
            {
                var cpt = new NativeMethods.POINT(x, y);
                NativeMethods.ScreenToClient(hwnd, ref cpt);
                IntPtr child = NativeMethods.ChildWindowFromPointEx(hwnd, cpt,
                    NativeMethods.CWP_SKIPINVISIBLE | NativeMethods.CWP_SKIPTRANSPARENT);
                if (child == IntPtr.Zero || child == hwnd)
                {
                    break;
                }
                hwnd = child;
            }
            return hwnd;
        }

        private static void ButtonMessages(MouseButtonType button, out uint down, out uint up, out int mk)
        {
            switch (button)
            {
                case MouseButtonType.Right:
                    down = (uint)NativeMethods.WM_RBUTTONDOWN; up = (uint)NativeMethods.WM_RBUTTONUP; mk = NativeMethods.MK_RBUTTON; break;
                case MouseButtonType.Middle:
                    down = (uint)NativeMethods.WM_MBUTTONDOWN; up = (uint)NativeMethods.WM_MBUTTONUP; mk = NativeMethods.MK_MBUTTON; break;
                default:
                    down = (uint)NativeMethods.WM_LBUTTONDOWN; up = (uint)NativeMethods.WM_LBUTTONUP; mk = NativeMethods.MK_LBUTTON; break;
            }
        }

        private static IntPtr MakeLParam(int loWord, int hiWord)
        {
            return (IntPtr)((hiWord << 16) | (loWord & 0xFFFF));
        }

        private static bool _bgNoWindowWarned;

        private static void WarnBackgroundNoWindow(int x, int y)
        {
            if (_bgNoWindowWarned)
            {
                return;
            }
            _bgNoWindowWarned = true;
            try
            {
                Logger.Warn("[Clicker] background click found no window at (" + x + ", " + y + "). Pick the spot again "
                    + "over the target window, or turn off \"don't move my mouse\" to use a normal click.");
            }
            catch { }
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
                dwExtraInfo = CurrentExtraInfo
            };

            uint sent = NativeMethods.SendInput(1, inputs, InputStructSize);
            if (sent == 0)
            {
                NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)delta), CurrentExtraInfo);
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
            inputs[0].U.ki = BuildKeyInput(vk, keyUp: false);
            inputs[1].type = NativeMethods.INPUT_KEYBOARD;
            inputs[1].U.ki = BuildKeyInput(vk, keyUp: true);

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
                case ClickStyle.Quadruple: return 4;
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
                        dwExtraInfo = CurrentExtraInfo
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
                        dwExtraInfo = CurrentExtraInfo
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
                NativeMethods.mouse_event(flag, 0, 0, 0, CurrentExtraInfo);
            }
        }

        private static void SendKey(ushort virtualKey, bool keyUp)
        {
            var inputs = new NativeMethods.INPUT[1];
            inputs[0].type = NativeMethods.INPUT_KEYBOARD;
            inputs[0].U.ki = BuildKeyInput(virtualKey, keyUp);

            uint sent = NativeMethods.SendInput(1, inputs, InputStructSize);
            if (sent == 0)
            {
                WarnKeyboardBlocked();
            }
        }

        private static bool _keyBlockedWarned;

        /// <summary>
        /// Logs, once per session, when a keyboard SendInput is rejected (returns 0).
        /// The usual cause is UI Privilege Isolation: an elevated / anti-cheat-protected
        /// game only accepts synthetic input from an equally-elevated process, so keys
        /// from a normal-user Tempo are dropped. Surfacing it makes the "macro does
        /// nothing in this game" case diagnosable from the log instead of a silent no-op.
        /// </summary>
        private static void WarnKeyboardBlocked()
        {
            if (_keyBlockedWarned)
            {
                return;
            }
            _keyBlockedWarned = true;
            try
            {
                Logger.Warn("[Clicker] keyboard SendInput was blocked (returned 0). The target window is "
                    + "likely elevated/anti-cheat protected — run Tempo as administrator so its "
                    + "integrity level matches the game, or use borderless-windowed mode.");
            }
            catch { /* logging is best effort */ }
        }

        /// <summary>
        /// Builds a KEYBDINPUT for one key transition, sent as a hardware SCAN CODE so
        /// games that read the keyboard through DirectInput / Raw Input (which look at
        /// scan codes, not virtual keys) actually receive WASD, arrows and every other
        /// movement key. The virtual key is mapped to its scan code with MapVirtualKey;
        /// extended keys (arrows, right-Ctrl/Alt, nav cluster, etc.) still get
        /// KEYEVENTF_EXTENDEDKEY so the OS prefixes the 0xE0 byte and they don't replay
        /// as the numpad equivalent. If a key has no scan code (some media / browser
        /// virtual keys) it falls back to sending the virtual key directly, which is
        /// how it worked before — those keys aren't used for game movement anyway.
        /// </summary>
        private static NativeMethods.KEYBDINPUT BuildKeyInput(ushort virtualKey, bool keyUp)
        {
            uint flags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0u;
            if (IsExtendedKey(virtualKey))
            {
                flags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;
            }

            ushort scan = (ushort)NativeMethods.MapVirtualKey(virtualKey, NativeMethods.MAPVK_VK_TO_VSC);
            if (scan != 0)
            {
                // Scan-code path: wVk is ignored by the OS when KEYEVENTF_SCANCODE is set,
                // so send 0 for it — this is the "real hardware key" form games respond to.
                flags |= NativeMethods.KEYEVENTF_SCANCODE;
                return new NativeMethods.KEYBDINPUT
                {
                    wVk = 0,
                    wScan = scan,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = CurrentExtraInfo
                };
            }

            // Fallback: no scan code available — send the virtual key as before.
            return new NativeMethods.KEYBDINPUT
            {
                wVk = virtualKey,
                wScan = 0,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = CurrentExtraInfo
            };
        }

        // Keys that live in the "extended" block of the keyboard. They need the
        // KEYEVENTF_EXTENDEDKEY flag or they replay as the wrong key (e.g. an arrow
        // turns into a numpad digit, or right-Ctrl behaves like left-Ctrl).
        private static bool IsExtendedKey(ushort vk)
        {
            switch (vk)
            {
                case 0xA3: // VK_RCONTROL
                case 0xA5: // VK_RMENU (right Alt / AltGr)
                case 0x21: // VK_PRIOR (Page Up)
                case 0x22: // VK_NEXT (Page Down)
                case 0x23: // VK_END
                case 0x24: // VK_HOME
                case 0x25: // VK_LEFT
                case 0x26: // VK_UP
                case 0x27: // VK_RIGHT
                case 0x28: // VK_DOWN
                case 0x2D: // VK_INSERT
                case 0x2E: // VK_DELETE
                case 0x2C: // VK_SNAPSHOT (Print Screen)
                case 0x90: // VK_NUMLOCK
                case 0x6F: // VK_DIVIDE (numpad /)
                case 0x5B: // VK_LWIN
                case 0x5C: // VK_RWIN
                case 0x5D: // VK_APPS (menu key)
                    return true;
                default:
                    return false;
            }
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
