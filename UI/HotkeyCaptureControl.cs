using System;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Native;

namespace AutoClicker.UI
{
    /// <summary>
    /// A read-only text box that captures a key combination when focused.
    ///
    /// Capturing keys in a WinForms control is not simply "handle KeyDown". Several
    /// keys never reach KeyDown at all, because WinForms grabs them first for dialog
    /// navigation: <b>Tab</b> moves focus, Enter presses the accept button, Escape
    /// presses the cancel button, and the arrows move between controls. That is why
    /// Tab could not be bound — the control was never even told it had been pressed.
    /// <see cref="IsInputKey"/> below claims those keys so they arrive as ordinary
    /// key input instead.
    ///
    /// Because the field then swallows Tab, keyboard users need a way OUT of it, so
    /// <b>Escape leaves the field</b> rather than being captured. Backspace / Delete
    /// clear the binding — but only when pressed bare, so Ctrl+Delete and friends are
    /// still bindable.
    /// </summary>
    public sealed class HotkeyCaptureControl : TextBox
    {
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        /// <summary>
        /// Sent for the media and browser keys instead of a key message. A keyboard's
        /// Play/Pause, Volume and Browser keys do NOT arrive as WM_KEYDOWN — Windows
        /// turns them into an application command — so a control that only handles
        /// KeyDown is deaf to that whole row of the keyboard.
        /// </summary>
        private const int WM_APPCOMMAND = 0x0319;

        private HotkeyDefinition _hotkey = new HotkeyDefinition();

        public event EventHandler HotkeyChanged;

        public HotkeyCaptureControl()
        {
            ReadOnly = true;
            Cursor = Cursors.Hand;
            ShortcutsEnabled = false;
            Text = Utils.Localization.T("(click, then press keys / mouse buttons)");
        }

        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        /// <summary>
        /// THE capture path. Every key-down that reaches a focused capture field is
        /// taken here, before Windows Forms gets a chance to interpret it as anything
        /// else, and fed straight to <see cref="OnKeyDown"/>.
        ///
        /// This has to happen at ProcessCmdKey — not KeyDown, not IsInputKey — because
        /// two different layers were quietly eating keys before the control ever saw
        /// them:
        ///
        ///  • Dialog navigation: Tab moved focus, Enter hit the default button, the
        ///    arrows moved between controls. So Tab could not be bound at all.
        ///  • <see cref="TextBoxBase"/> with ShortcutsEnabled = false: it swallows a
        ///    FIXED list of editing shortcuts in its own ProcessCmdKey — Ctrl+Delete,
        ///    Shift+Delete, Ctrl+Backspace, Shift+Insert, and Ctrl+A/C/V/X/Z/Y/E/L/R.
        ///    Every one of those combos was silently unbindable, and no amount of
        ///    fixing OnKeyDown could help, because OnKeyDown was never called.
        ///
        /// Taking the key here fixes both classes at once. Two keys are deliberately
        /// let through: Escape (the way OUT of a field that now swallows Tab) and
        /// Alt+F4 (which must always close the window).
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (Focused && (msg.Msg == WM_KEYDOWN || msg.Msg == WM_SYSKEYDOWN))
            {
                Keys code = keyData & Keys.KeyCode;
                bool alt = (keyData & Keys.Alt) != 0;

                // Two keys are deliberately NOT captured, so they bubble up to the form:
                //  • Alt+F4 must always close the window.
                //  • Escape must leave the field (a keyboard user's only way out, now
                //    that Tab is captured as a binding). The form handles the actual
                //    focus move in its ProcessCmdKey — see MainForm — because that is
                //    the level the app reliably receives Escape at.
                if ((code == Keys.F4 && alt) || code == Keys.Escape)
                {
                    return base.ProcessCmdKey(ref msg, keyData);
                }

                OnKeyDown(new KeyEventArgs(keyData));
                return true;          // consumed: nothing else may reinterpret it
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>
        /// Catches the media and browser keys, which never arrive as key messages.
        ///
        /// Pressing Play/Pause or Volume Up sends WM_APPCOMMAND, not WM_KEYDOWN, so
        /// OnKeyDown never fires for them and that entire row of a modern keyboard was
        /// unbindable — even though RegisterHotKey accepts those virtual keys perfectly
        /// well and HotkeyDefinition was already happy to store them. The command is
        /// translated back to the virtual key it corresponds to and fed through the same
        /// path as any other key, so the rest of the class needs no special cases.
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_APPCOMMAND && Focused)
            {
                // The command sits in the high word of lParam, with device/key-state
                // flags in the top nibble that have to be masked off first.
                int cmd = ((int)(m.LParam.ToInt64() >> 16)) & 0x0FFF;
                Keys mapped = KeyForAppCommand(cmd);
                if (mapped != Keys.None)
                {
                    OnKeyDown(new KeyEventArgs(mapped | ModifierKeys));
                    // Returning "handled" (1) stops Windows ALSO acting on it, so
                    // binding Volume Up does not change the volume while you bind it.
                    m.Result = (IntPtr)1;
                    return;
                }
            }
            base.WndProc(ref m);
        }

        /// <summary>
        /// The virtual key an APPCOMMAND_* corresponds to, or <see cref="Keys.None"/>
        /// for commands with no key of their own (which are left to Windows).
        /// </summary>
        private static Keys KeyForAppCommand(int cmd)
        {
            switch (cmd)
            {
                case 1:  return Keys.BrowserBack;
                case 2:  return Keys.BrowserForward;
                case 3:  return Keys.BrowserRefresh;
                case 4:  return Keys.BrowserStop;
                case 5:  return Keys.BrowserSearch;
                case 6:  return Keys.BrowserFavorites;
                case 7:  return Keys.BrowserHome;
                case 8:  return Keys.VolumeMute;
                case 9:  return Keys.VolumeDown;
                case 10: return Keys.VolumeUp;
                case 11: return Keys.MediaNextTrack;
                case 12: return Keys.MediaPreviousTrack;
                case 13: return Keys.MediaStop;
                case 14: return Keys.MediaPlayPause;
                case 15: return Keys.LaunchMail;
                case 16: return Keys.SelectMedia;
                case 17: return Keys.LaunchApplication1;
                case 18: return Keys.LaunchApplication2;
                default: return Keys.None;
            }
        }

        /// <summary>Moves focus off this field and restores its resting text.</summary>
        public void ReleaseFocusToParent()
        {
            UpdateText();
            if (Parent != null && Parent.SelectNextControl(this, true, true, true, true))
            {
                return;
            }
            Form form = FindForm();
            if (form != null)
            {
                form.ActiveControl = null;
            }
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
            ShowListening();
        }

        protected override void OnLeave(EventArgs e)
        {
            base.OnLeave(e);
            UpdateText();
        }

        /// <summary>The idle prompt shown while the field is focused and waiting.</summary>
        private void ShowListening()
        {
            Text = _hotkey.IsValid
                ? Utils.Localization.F("{0}   ▸ press a new combo", _hotkey.ToDisplayString())
                : Utils.Localization.T("▸ listening… press keys / mouse button (Esc to leave)");
        }

        /// <summary>
        /// Live preview WHILE the combo is being formed. Hold Ctrl+Shift and the field
        /// reads "Ctrl + Shift + …" before you have chosen the final key — so you can see
        /// exactly which modifiers Tempo has detected, and catch a Ctrl that didn't
        /// register or a Shift you didn't mean to be holding, rather than committing a
        /// combo and wondering why it came out wrong.
        /// </summary>
        private void ShowPendingModifiers()
        {
            bool ctrl = (ModifierKeys & Keys.Control) != 0;
            bool alt = (ModifierKeys & Keys.Alt) != 0;
            bool shift = (ModifierKeys & Keys.Shift) != 0;
            bool win = WinKeyDown();

            if (!ctrl && !alt && !shift && !win)
            {
                ShowListening();
                return;
            }

            var sb = new System.Text.StringBuilder();
            if (ctrl) { sb.Append("Ctrl + "); }
            if (alt) { sb.Append("Alt + "); }
            if (shift) { sb.Append("Shift + "); }
            if (win) { sb.Append("Win + "); }
            sb.Append('…');
            Text = sb.ToString();
        }

        private static bool WinKeyDown()
        {
            return (NativeMethods.GetAsyncKeyState(VK_LWIN) & NativeMethods.KEY_PRESSED_MASK) != 0 ||
                   (NativeMethods.GetAsyncKeyState(VK_RWIN) & NativeMethods.KEY_PRESSED_MASK) != 0;
        }

        /// <summary>
        /// Releasing a modifier without ever pressing a real key must return the field to
        /// its resting state — otherwise it would sit there forever showing a phantom
        /// "Ctrl + …" for a combo the user abandoned.
        /// </summary>
        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (!Focused) { return; }

            // Print Screen is captured HERE, not in OnKeyDown, because Windows does not
            // send a key-DOWN for it to an ordinary window — only the key-up. Handling
            // just KeyDown therefore made it the one labelled key on the keyboard that
            // could never be bound, despite HotkeyDefinition already having a display
            // name ready for it.
            if (e.KeyCode == Keys.PrintScreen)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                CaptureKey(Keys.PrintScreen, e.Control, e.Alt, e.Shift, WinKeyDown());
                return;
            }

            ShowPendingModifiers();
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

            _hotkey = new HotkeyDefinition
            {
                Control = (ModifierKeys & Keys.Control) != 0,
                Alt = (ModifierKeys & Keys.Alt) != 0,
                Shift = (ModifierKeys & Keys.Shift) != 0,
                Win = WinKeyDown(),
                Key = Keys.None,
                MouseButton = button
            };

            UpdateText();
            HotkeyChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            Keys keyCode = e.KeyCode;

            // Alt+F4 must always close the window. Capturing it would bind a hotkey the
            // user almost certainly did not intend AND leave them unable to close Tempo
            // with it — so let this one through untouched.
            if (keyCode == Keys.F4 && e.Alt)
            {
                base.OnKeyDown(e);
                return;
            }

            e.SuppressKeyPress = true;
            e.Handled = true;

            // A standalone modifier is not a binding on its own — but it IS worth
            // showing. Echo it live ("Ctrl + Shift + …") so the modifiers Tempo has
            // actually detected are visible before the final key commits the combo.
            if (keyCode == Keys.ControlKey || keyCode == Keys.ShiftKey ||
                keyCode == Keys.Menu || keyCode == Keys.LWin || keyCode == Keys.RWin)
            {
                ShowPendingModifiers();
                return;
            }

            bool anyModifier = e.Control || e.Alt || e.Shift;
            bool winDown = WinKeyDown();

            // Clear the binding — but only on a BARE Backspace/Delete. Previously any
            // Backspace or Delete cleared the field, which quietly made Ctrl+Delete,
            // Shift+Backspace and every other modified variant impossible to bind: the
            // clear branch ran before the capture branch could ever see them.
            if ((keyCode == Keys.Back || keyCode == Keys.Delete) && !anyModifier && !winDown)
            {
                _hotkey = new HotkeyDefinition();
                UpdateText();
                HotkeyChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            CaptureKey(keyCode, e.Control, e.Alt, e.Shift, winDown);
        }

        /// <summary>
        /// Commits a binding. Shared by every capture route — the ordinary key-down
        /// path, Print Screen (key-up only), and the media keys (WM_APPCOMMAND) — so
        /// all three produce an identical HotkeyDefinition and raise one change event.
        /// </summary>
        private void CaptureKey(Keys keyCode, bool ctrl, bool alt, bool shift, bool win)
        {
            _hotkey = new HotkeyDefinition
            {
                Control = ctrl,
                Alt = alt,
                Shift = shift,
                Win = win,
                Key = keyCode
            };

            UpdateText();
            HotkeyChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateText()
        {
            Text = _hotkey.IsValid ? _hotkey.ToDisplayString() : Utils.Localization.T("(none)");
        }
    }
}
