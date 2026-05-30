using System;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Native;

namespace AutoClicker.UI
{
    /// <summary>
    /// A read-only text box that captures a key combination when focused. Pressing
    /// Backspace or Delete clears the binding.
    /// </summary>
    public sealed class HotkeyCaptureControl : TextBox
    {
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        private HotkeyDefinition _hotkey = new HotkeyDefinition();

        public event EventHandler HotkeyChanged;

        public HotkeyCaptureControl()
        {
            ReadOnly = true;
            Cursor = Cursors.Hand;
            ShortcutsEnabled = false;
            Text = "(click, then press keys / mouse buttons)";
        }

        public HotkeyDefinition Hotkey
        {
            get => _hotkey;
            set
            {
                _hotkey = value ?? new HotkeyDefinition();
                UpdateText();
            }
        }

        protected override void OnEnter(EventArgs e)
        {
            base.OnEnter(e);
            if (!_hotkey.IsValid)
            {
                Text = "press keys, or middle / side mouse button…";
            }
        }

        protected override void OnLeave(EventArgs e)
        {
            base.OnLeave(e);
            UpdateText();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            // Left click is reserved for focusing the box, so it is never captured
            // as a hotkey unless a modifier is held. The middle and X buttons can
            // be captured bare. Right is capturable too (with or without modifier).
            HotkeyMouseButton button = HotkeyMouseButton.None;
            switch (e.Button)
            {
                case MouseButtons.Middle: button = HotkeyMouseButton.Middle; break;
                case MouseButtons.XButton1: button = HotkeyMouseButton.XButton1; break;
                case MouseButtons.XButton2: button = HotkeyMouseButton.XButton2; break;
                case MouseButtons.Right: button = HotkeyMouseButton.Right; break;
                case MouseButtons.Left:
                    // Only capture a modified left click; a bare left click just
                    // focuses the control.
                    if (ModifierKeys != Keys.None)
                    {
                        button = HotkeyMouseButton.Left;
                    }
                    break;
            }

            if (button == HotkeyMouseButton.None)
            {
                return;
            }

            // A bare Left/Right would consume normal clicking, so require a
            // modifier for those two.
            bool bareLeftRight =
                (button == HotkeyMouseButton.Left || button == HotkeyMouseButton.Right)
                && ModifierKeys == Keys.None;
            if (bareLeftRight)
            {
                return;
            }

            bool winDown =
                (NativeMethods.GetAsyncKeyState(VK_LWIN) & NativeMethods.KEY_PRESSED_MASK) != 0 ||
                (NativeMethods.GetAsyncKeyState(VK_RWIN) & NativeMethods.KEY_PRESSED_MASK) != 0;

            _hotkey = new HotkeyDefinition
            {
                Control = (ModifierKeys & Keys.Control) != 0,
                Alt = (ModifierKeys & Keys.Alt) != 0,
                Shift = (ModifierKeys & Keys.Shift) != 0,
                Win = winDown,
                Key = Keys.None,
                MouseButton = button
            };

            UpdateText();
            HotkeyChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;

            Keys keyCode = e.KeyCode;

            // Ignore standalone modifier presses.
            if (keyCode == Keys.ControlKey || keyCode == Keys.ShiftKey ||
                keyCode == Keys.Menu || keyCode == Keys.LWin || keyCode == Keys.RWin)
            {
                return;
            }

            // Clear the binding.
            if (keyCode == Keys.Back || keyCode == Keys.Delete)
            {
                _hotkey = new HotkeyDefinition();
                UpdateText();
                HotkeyChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            bool winDown =
                (NativeMethods.GetAsyncKeyState(VK_LWIN) & NativeMethods.KEY_PRESSED_MASK) != 0 ||
                (NativeMethods.GetAsyncKeyState(VK_RWIN) & NativeMethods.KEY_PRESSED_MASK) != 0;

            _hotkey = new HotkeyDefinition
            {
                Control = e.Control,
                Alt = e.Alt,
                Shift = e.Shift,
                Win = winDown,
                Key = keyCode
            };

            UpdateText();
            HotkeyChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateText()
        {
            Text = _hotkey.IsValid ? _hotkey.ToDisplayString() : "(none)";
        }
    }
}
