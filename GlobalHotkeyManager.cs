using System;
using System.Collections.Generic;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Native;
using AutoClicker.Utils;

namespace AutoClicker.Native
{
    /// <summary>
    /// Arguments raised when a registered global hotkey fires.
    /// </summary>
    public sealed class HotkeyPressedEventArgs : EventArgs
    {
        public int Id { get; }
        public string Name { get; }

        public HotkeyPressedEventArgs(int id, string name)
        {
            Id = id;
            Name = name;
        }
    }

    /// <summary>
    /// Manages registration of system wide hotkeys via a private message-only
    /// window. Hotkeys keep working even when the main window is minimised to the
    /// tray or another application has focus.
    /// </summary>
    public sealed class GlobalHotkeyManager : IDisposable
    {
        private sealed class MessageWindow : NativeWindow
        {
            public event Action<int> HotkeyMessage;

            public MessageWindow()
            {
                CreateHandle(new CreateParams());
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == NativeMethods.WM_HOTKEY)
                {
                    int id = m.WParam.ToInt32();
                    HotkeyMessage?.Invoke(id);
                }

                base.WndProc(ref m);
            }
        }

        private sealed class Registration
        {
            public int Id;
            public string Name;
            public uint Modifiers;
            public uint VirtualKey;
            public bool Active;
        }

        private readonly MessageWindow _window;
        private readonly Dictionary<int, Registration> _registrations = new Dictionary<int, Registration>();
        private readonly Dictionary<string, int> _nameToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private int _nextId = 0xA000;
        private bool _disposed;

        // Mouse-button hotkeys are handled with a low-level hook rather than
        // RegisterHotKey (which only supports keyboard keys). The hook is created
        // lazily the first time a mouse binding is registered.
        private MouseHotkeyHook _mouseHook;
        private readonly Dictionary<string, HotkeyDefinition> _mouseBindings =
            new Dictionary<string, HotkeyDefinition>(StringComparer.OrdinalIgnoreCase);

        public event EventHandler<HotkeyPressedEventArgs> HotkeyPressed;

        public GlobalHotkeyManager()
        {
            _window = new MessageWindow();
            _window.HotkeyMessage += OnHotkeyMessage;
        }

        /// <summary>
        /// Registers a hotkey under a friendly name. If a hotkey with the same
        /// name already exists it is replaced.
        /// </summary>
        public bool Register(string name, uint modifiers, uint virtualKey)
        {
            ThrowIfDisposed();

            // Remove any previous registration sharing this name.
            Unregister(name);

            int id = _nextId++;
            bool ok = NativeMethods.RegisterHotKey(_window.Handle, id, modifiers, virtualKey);

            var reg = new Registration
            {
                Id = id,
                Name = name,
                Modifiers = modifiers,
                VirtualKey = virtualKey,
                Active = ok
            };

            _registrations[id] = reg;
            _nameToId[name] = id;

            if (!ok)
            {
                Logger.Warn($"Failed to register hotkey '{name}' (mods={modifiers}, vk={virtualKey}). It may be in use by another application.");
            }
            else
            {
                Logger.Info($"Registered hotkey '{name}' (mods={modifiers}, vk={virtualKey}).");
            }

            return ok;
        }

        /// <summary>
        /// Registers a mouse-button hotkey under a friendly name. Uses a low-level
        /// mouse hook. Replaces any prior mouse binding with the same name.
        /// </summary>
        public bool RegisterMouse(string name, HotkeyDefinition def)
        {
            ThrowIfDisposed();

            if (def == null || !def.IsMouse)
            {
                return false;
            }

            _mouseBindings[name] = def.Clone();

            if (_mouseHook == null)
            {
                _mouseHook = new MouseHotkeyHook();
                _mouseHook.ButtonDown += OnMouseHotkey;
            }

            if (!_mouseHook.IsRunning)
            {
                _mouseHook.Start();
            }

            Logger.Info($"Registered mouse hotkey '{name}' ({def.ToDisplayString()}).");
            return true;
        }

        private void OnMouseHotkey(object sender, MouseHotkeyEventArgs e)
        {
            // Never let the auto-clicker's own synthetic clicks trigger a hotkey.
            if (e.Injected)
            {
                return;
            }

            foreach (var pair in _mouseBindings)
            {
                HotkeyDefinition def = pair.Value;
                if (def.MouseButton != e.Button)
                {
                    continue;
                }

                if (!def.ModifiersMatch(IsKeyDown))
                {
                    continue;
                }

                // Consume side/middle buttons (and any modified left/right) so the
                // bound button does not also do its normal job. A bare left/right
                // is left to pass through as a safety measure.
                bool bareLeftRight =
                    (def.MouseButton == HotkeyMouseButton.Left || def.MouseButton == HotkeyMouseButton.Right)
                    && !def.Control && !def.Alt && !def.Shift && !def.Win;

                if (!bareLeftRight)
                {
                    e.Suppress = true;
                }

                HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs(-1, pair.Key));
                return;
            }
        }

        private static bool IsKeyDown(int vk)
        {
            return (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        /// <summary>
        /// Removes a previously registered hotkey by name. Safe to call when no
        /// such hotkey exists.
        /// </summary>
        public void Unregister(string name)
        {
            if (_disposed)
            {
                return;
            }

            if (_nameToId.TryGetValue(name, out int id))
            {
                if (_registrations.TryGetValue(id, out var reg) && reg.Active)
                {
                    NativeMethods.UnregisterHotKey(_window.Handle, id);
                }

                _registrations.Remove(id);
                _nameToId.Remove(name);
            }
        }

        /// <summary>
        /// Returns true if the named hotkey is currently active.
        /// </summary>
        public bool IsActive(string name)
        {
            return _nameToId.TryGetValue(name, out int id)
                && _registrations.TryGetValue(id, out var reg)
                && reg.Active;
        }

        public void UnregisterAll()
        {
            if (_disposed)
            {
                return;
            }

            foreach (var reg in _registrations.Values)
            {
                if (reg.Active)
                {
                    NativeMethods.UnregisterHotKey(_window.Handle, reg.Id);
                }
            }

            _registrations.Clear();
            _nameToId.Clear();

            // Tear down mouse bindings and stop the hook so it adds no overhead
            // while no mouse hotkeys are configured.
            _mouseBindings.Clear();
            _mouseHook?.Stop();
        }

        private void OnHotkeyMessage(int id)
        {
            if (_registrations.TryGetValue(id, out var reg))
            {
                HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs(reg.Id, reg.Name));
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(GlobalHotkeyManager));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            UnregisterAll();

            if (_mouseHook != null)
            {
                _mouseHook.ButtonDown -= OnMouseHotkey;
                _mouseHook.Dispose();
                _mouseHook = null;
            }

            try
            {
                _window.HotkeyMessage -= OnHotkeyMessage;
                _window.DestroyHandle();
            }
            catch (Exception ex)
            {
                Logger.Warn("Error while disposing hotkey window: " + ex.Message);
            }

            _disposed = true;
        }
    }
}
