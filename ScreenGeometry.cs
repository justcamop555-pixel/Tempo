using System;
using System.Windows.Forms;
using AutoClicker.Native;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Helpers for validating and clamping screen coordinates against the bounds
    /// of the virtual desktop (all monitors combined).
    /// </summary>
    public static class ScreenGeometry
    {
        public static int VirtualLeft => SystemInformation.VirtualScreen.Left;
        public static int VirtualTop => SystemInformation.VirtualScreen.Top;
        public static int VirtualRight => SystemInformation.VirtualScreen.Right;
        public static int VirtualBottom => SystemInformation.VirtualScreen.Bottom;
        public static int VirtualWidth => SystemInformation.VirtualScreen.Width;
        public static int VirtualHeight => SystemInformation.VirtualScreen.Height;

        /// <summary>True if the supplied coordinate lies on some monitor.</summary>
        public static bool IsOnScreen(int x, int y)
        {
            return x >= VirtualLeft && x < VirtualRight
                && y >= VirtualTop && y < VirtualBottom;
        }

        /// <summary>Clamps a coordinate so it stays within the virtual desktop.</summary>
        public static void Clamp(ref int x, ref int y)
        {
            if (x < VirtualLeft) x = VirtualLeft;
            if (y < VirtualTop) y = VirtualTop;
            if (x > VirtualRight - 1) x = VirtualRight - 1;
            if (y > VirtualBottom - 1) y = VirtualBottom - 1;
        }

        /// <summary>Reads the current cursor position via the Win32 API.</summary>
        public static bool TryGetCursorPosition(out int x, out int y)
        {
            if (NativeMethods.GetCursorPos(out NativeMethods.POINT p))
            {
                x = p.X;
                y = p.Y;
                return true;
            }

            x = 0;
            y = 0;
            return false;
        }
    }
}
