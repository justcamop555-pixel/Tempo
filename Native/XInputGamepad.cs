using System;
using System.Runtime.InteropServices;
using AutoClicker.Utils;

namespace AutoClicker.Native
{
    /// <summary>One poll of a gamepad's sticks, already deadzoned and normalised.</summary>
    public struct GamepadState
    {
        /// <summary>True when a controller is actually plugged in.</summary>
        public bool Connected;

        /// <summary>Left stick, -1..+1. X = right, Y = forward (Y is already un-inverted).</summary>
        public double LeftX;
        public double LeftY;

        /// <summary>Right stick, -1..+1. X = look right (this is what turns the camera).</summary>
        public double RightX;
        public double RightY;
    }

    /// <summary>
    /// Reads Xbox-compatible controllers through XInput. No NuGet package and no
    /// extra files: XInput ships with Windows, so this is a pure P/Invoke.
    ///
    /// Sticks are radially deadzoned — the deadzone is applied to the stick's
    /// MAGNITUDE, not to each axis separately. Per-axis deadzones are the classic
    /// bug that makes diagonals feel wrong (a stick pushed to a perfect 45° has both
    /// axes at ~0.707, so a naive per-axis cut clips them unevenly and the direction
    /// bends toward the cardinals). Doing it radially keeps every angle honest, which
    /// is exactly what a direction-driven movement system needs.
    /// </summary>
    public static class XInputGamepad
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        private const uint ERROR_SUCCESS = 0;

        // xinput1_4 ships with Windows 8+. xinput9_1_0 is the ancient always-present
        // fallback, so a missing 1_4 (or an odd Windows install) degrades instead of
        // throwing DllNotFoundException on the polling thread.
        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState_14(uint index, ref XINPUT_STATE state);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState_91(uint index, ref XINPUT_STATE state);

        // 0 = untried, 1 = use 1_4, 2 = use 9_1_0, 3 = no XInput at all.
        private static int _binding;

        private static uint GetState(uint index, ref XINPUT_STATE state)
        {
            if (_binding == 0)
            {
                try
                {
                    XINPUT_STATE probe = default;
                    XInputGetState_14(0, ref probe);
                    _binding = 1;
                }
                catch (DllNotFoundException)
                {
                    try
                    {
                        XINPUT_STATE probe = default;
                        XInputGetState_91(0, ref probe);
                        _binding = 2;
                    }
                    catch (DllNotFoundException)
                    {
                        _binding = 3;
                        Logger.Info("[Movement] no XInput on this PC — gamepad support is off.");
                    }
                    catch (EntryPointNotFoundException) { _binding = 3; }
                }
                catch (EntryPointNotFoundException) { _binding = 3; }
            }

            switch (_binding)
            {
                case 1: return XInputGetState_14(index, ref state);
                case 2: return XInputGetState_91(index, ref state);
                default: return 1;                       // not ERROR_SUCCESS
            }
        }

        /// <summary>
        /// Polls controller <paramref name="index"/> (0-3). Never throws: an
        /// unplugged or unsupported controller simply reports Connected = false.
        /// </summary>
        public static GamepadState Poll(uint index, double deadzone)
        {
            var result = new GamepadState();
            try
            {
                XINPUT_STATE state = default;
                if (GetState(index, ref state) != ERROR_SUCCESS)
                {
                    return result;                       // not connected
                }
                result.Connected = true;

                ApplyRadialDeadzone(state.Gamepad.sThumbLX, state.Gamepad.sThumbLY, deadzone,
                    out result.LeftX, out result.LeftY);
                ApplyRadialDeadzone(state.Gamepad.sThumbRX, state.Gamepad.sThumbRY, deadzone,
                    out result.RightX, out result.RightY);
            }
            catch
            {
                // Polling must never destabilise the movement loop.
                result.Connected = false;
            }
            return result;
        }

        /// <summary>
        /// Normalises a raw stick pair to the unit disc and removes the deadzone
        /// RADIALLY, rescaling the remainder to 0..1 so there is no dead step the
        /// instant the stick leaves the deadzone — the direction is preserved exactly.
        /// </summary>
        private static void ApplyRadialDeadzone(short rawX, short rawY, double deadzone,
            out double x, out double y)
        {
            // 32768 (not 32767) keeps the negative extreme inside the unit disc.
            double nx = rawX / 32768.0;
            double ny = rawY / 32768.0;

            double mag = Math.Sqrt(nx * nx + ny * ny);
            if (mag <= deadzone || mag <= 1e-6)
            {
                x = 0; y = 0;
                return;
            }
            if (deadzone >= 1.0) { deadzone = 0.99; }

            // Rescale so magnitude runs 0..1 across the LIVE part of the stick's travel.
            double scaled = Math.Min(1.0, (mag - deadzone) / (1.0 - deadzone));
            double ux = nx / mag, uy = ny / mag;         // unit direction, angle intact
            x = ux * scaled;
            y = uy * scaled;
        }
    }
}
