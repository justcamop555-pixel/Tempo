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

        // Keyboard fallback: on some keyboards (and for keys another app has grabbed)
        // RegisterHotKey simply won't bind. For those we detect the key combo with a
        // low-level keyboard hook instead, so the shortcut still works. The hook is
        // created lazily and only when a RegisterHotKey call has actually failed, so
        // keyboards where the normal path works pay nothing and can't double-fire.
        private LowLevelKeyboardHook _keyHook;
        private readonly Dictionary<string, HotkeyDefinition> _keyboardFallbacks =
            new Dictionary<string, HotkeyDefinition>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _comboHeld =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

            // MOD_NOREPEAT: one WM_HOTKEY per physical press, not one per keyboard
            // auto-repeat. Without it, holding a hotkey down past the repeat delay sends
            // ~15 messages a second, and nothing downstream debounces — so holding F6 for
            // a second toggled the clicker on and off a dozen times and landed on whichever
            // state the last message happened to produce.
            //
            // It also makes the two delivery routes agree. The keyboard-hook fallback has
            // always fired once per press (see _comboHeld), so the SAME binding behaved
            // differently depending on whether Windows accepted the registration — and the
            // accepted path, the normal one, was the misbehaving one.
            //
            // The cost is that a held hotkey no longer repeats, which an interval-nudge
            // binding could in principle have wanted. Nobody could rely on that: it never
            // worked on the fallback route. Firing once is the behaviour worth having
            // consistently, and it is the one that cannot wreck a running click session.
            //
            // Kept out of HotkeyDefinition.GetModifierFlags deliberately: this is a
            // registration detail, not part of the combination, and Registration.Modifiers
            // below is bookkeeping that should keep describing the combination itself.
            bool ok = NativeMethods.RegisterHotKey(
                _window.Handle, id, modifiers | NativeMethods.MOD_NOREPEAT, virtualKey);

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
                Logger.Warn($"[Hotkeys] failed to register '{name}' (mods={modifiers}, vk={virtualKey}). It may be in use by another application.");
            }
            else
            {
                Logger.Info($"[Hotkeys] registered '{name}' (mods={modifiers}, vk={virtualKey}).");
            }

            return ok;
        }

        /// <summary>
        /// Registers a keyboard hotkey from a definition. Tries RegisterHotKey first;
        /// if that fails (the usual reason a shortcut "doesn't work on some keyboards"),
        /// it transparently falls back to a low-level keyboard hook so the combo still
        /// fires. Returns true if the hotkey is usable by either route.
        /// </summary>
        public bool Register(string name, HotkeyDefinition def)
        {
            ThrowIfDisposed();

            if (def == null || !def.IsValid || def.IsMouse)
            {
                return false;
            }

            // Clear any stale fallback for this name before re-evaluating.
            _keyboardFallbacks.Remove(name);
            _comboHeld.Remove(name);
            _selfCollisions.Remove(name);

            uint mods = def.GetModifierFlags();
            uint vk = def.GetVirtualKey();

            // Is the combination one WE already hold? RegisterHotKey fails with the same
            // ERROR_HOTKEY_ALREADY_REGISTERED whether the owner is another program or
            // Tempo itself, and every message downstream assumed the former — so two
            // Tempo actions sharing a key produced "another program already owns it"
            // about a key Tempo had reserved a moment earlier. Worse, the fallback hook
            // still sees the keystroke WM_HOTKEY consumed, so BOTH actions fired.
            // Detect it here, where the answer is knowable, and refuse the fallback.
            string heldBy = null;
            foreach (var kv in _registrations)
            {
                Registration r = kv.Value;
                if (r != null && r.Active && r.Modifiers == mods && r.VirtualKey == vk &&
                    !string.Equals(r.Name, name, StringComparison.Ordinal))
                {
                    heldBy = r.Name;
                    break;
                }
            }

            bool ok = Register(name, mods, vk);
            if (!ok && heldBy != null)
            {
                // Deliberately no hook fallback: adding one is what made both actions
                // fire. Left unbound and reported, so the Keybinds tab can say the truth.
                _selfCollisions[name] = heldBy;
                Logger.Warn($"[Hotkeys] '{name}' collides with Tempo's own '{heldBy}' on the same " +
                            "combination; left unbound rather than doubling up on one key.");
                return false;
            }

            if (!ok)
            {
                // The fallback is only real if the hook actually installed. It returns a
                // bool and that used to be discarded, so a hook blocked by policy or a
                // full desktop hook table still reported a working hotkey — and the tab
                // told the user "Tempo still catches it with a keyboard hook, so the
                // action works" about a binding with no delivery mechanism at all.
                if (!EnsureKeyHook())
                {
                    Logger.Warn($"[Hotkeys] '{name}' could not bind: Windows refused it and the " +
                                "keyboard hook would not install. This hotkey is dead.");
                    return false;
                }

                _keyboardFallbacks[name] = def.Clone();
                Logger.Info($"[Hotkeys] '{name}' couldn't bind via RegisterHotKey; using low-level keyboard fallback.");
                ok = true;
            }
            return ok;
        }

        /// <summary>Names left unbound because another Tempo action already holds the combo.</summary>
        private readonly Dictionary<string, string> _selfCollisions =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// The Tempo action already holding this one's combination, or null.
        /// Lets the Keybinds tab blame the right owner instead of "another program".
        /// </summary>
        public string SelfCollisionOwner(string name)
        {
            if (name == null) { return null; }
            return _selfCollisions.TryGetValue(name, out string owner) ? owner : null;
        }

        /// <summary>True if the hook is installed and running.</summary>
        private bool EnsureKeyHook()
        {
            if (_keyHook == null)
            {
                _keyHook = new LowLevelKeyboardHook();
                _keyHook.KeyEvent += OnKeyHookEvent;
            }
            if (!_keyHook.IsRunning)
            {
                return _keyHook.Start();
            }
            return true;
        }

        private void OnKeyHookEvent(object sender, KeyboardHookEventArgs e)
        {
            // Runs for every key, so keep it cheap and bail fast.
            if (_keyboardFallbacks.Count == 0 || DeliveryPaused)
            {
                return;
            }

            foreach (var pair in _keyboardFallbacks)
            {
                HotkeyDefinition def = pair.Value;
                if (e.VirtualKey != (int)def.GetVirtualKey())
                {
                    continue;
                }

                string nm = pair.Key;
                if (e.IsKeyDown)
                {
                    // Fire once per press: ignore auto-repeat while the key is held.
                    // ModifiersMatch is exact, so at most one binding on a given key
                    // fires for the current modifier state.
                    if (!_comboHeld.Contains(nm) && def.ModifiersMatch(IsKeyDown))
                    {
                        _comboHeld.Add(nm);
                        HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs(-1, nm));
                    }
                }
                else
                {
                    _comboHeld.Remove(nm);
                }
                // Keep checking: another binding may use this same key with a
                // different modifier combination.
            }
        }
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

            // Same trap as the keyboard hook: Start() reports whether the hook actually
            // installed, and discarding that meant a mouse hotkey with no delivery
            // mechanism still logged "registered" and reported BindRoute.Mouse. The log
            // could carry "failed to install the mouse-hotkey hook" and "registered mouse
            // hotkey" on consecutive lines and nothing reconciled them.
            if (!_mouseHook.IsRunning && !_mouseHook.Start())
            {
                _mouseBindings.Remove(name);
                Logger.Warn($"[Hotkeys] mouse hotkey '{name}' could not bind: the mouse hook " +
                            "would not install. This hotkey is dead.");
                return false;
            }

            Logger.Info($"[Hotkeys] registered mouse hotkey '{name}' ({def.ToDisplayString()}).");
            return true;
        }

        /// <summary>
        /// While true, bound hotkeys are not delivered. Set by the Keybinds tab for as
        /// long as a capture field has focus.
        ///
        /// Without it a mouse button could be bound exactly once and never changed: click
        /// into the field to rebind X2, and the hook — which runs before the click ever
        /// reaches the field — matched the EXISTING X2 binding, suppressed the click and
        /// fired the old action instead. The rebind was unreachable by the only gesture
        /// that performs it, and the action fired every time you tried.
        /// </summary>
        public bool DeliveryPaused { get; set; }

        private void OnMouseHotkey(object sender, MouseHotkeyEventArgs e)
        {
            // Never let the auto-clicker's own synthetic clicks trigger a hotkey.
            if (e.Injected)
            {
                return;
            }

            if (DeliveryPaused)
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

            // Also drop any keyboard-hook fallback registered under this name.
            _keyboardFallbacks.Remove(name);
            _comboHeld.Remove(name);
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

        /// <summary>How a bound hotkey is actually being delivered.</summary>
        public enum BindRoute
        {
            /// <summary>Nothing is bound under this name.</summary>
            None = 0,
            /// <summary>Windows accepted it (RegisterHotKey). The key is reserved for Tempo.</summary>
            Registered = 1,
            /// <summary>
            /// Windows REFUSED it — almost always because another program already owns
            /// the combination — so Tempo is watching for it with a keyboard hook
            /// instead. The action still fires, but the key is NOT reserved: it also
            /// keeps doing whatever it does in the app you're using. That difference is
            /// invisible today, and it's exactly the thing people mean when they say a
            /// hotkey "half works".
            /// </summary>
            HookFallback = 2,
            /// <summary>A mouse-button binding (always hook-driven; that is normal).</summary>
            Mouse = 3
        }

        /// <summary>
        /// One-line tally of how the bound hotkeys are being delivered, for Live debug.
        /// Names the hook-fallback ones explicitly: those are the "my hotkey half works"
        /// cases — the action fires, but the key is not reserved, so it ALSO keeps doing
        /// its normal job in whatever app is in front.
        /// </summary>
        public string DebugSummary()
        {
            try
            {
                int reserved = 0, hooked = 0, mouse = 0;
                var hookedNames = new List<string>();
                foreach (string name in _nameToId.Keys)
                {
                    switch (RouteOf(name))
                    {
                        case BindRoute.Registered: reserved++; break;
                        case BindRoute.HookFallback: hooked++; hookedNames.Add(name); break;
                        case BindRoute.Mouse: mouse++; break;
                    }
                }
                foreach (string name in _mouseBindings.Keys)
                {
                    if (!_nameToId.ContainsKey(name)) { mouse++; }
                }

                var sb = new System.Text.StringBuilder();
                sb.Append(reserved).Append(" reserved");
                if (hooked > 0)
                {
                    sb.Append(", ").Append(hooked).Append(" via keyboard hook (")
                      .Append(string.Join(", ", hookedNames))
                      .Append(" — key not reserved, another app owns it)");
                }
                if (mouse > 0) { sb.Append(", ").Append(mouse).Append(" mouse-button"); }
                return sb.ToString();
            }
            catch { return ""; }
        }

        /// <summary>How the named hotkey is bound, for the Keybinds tab to report.</summary>
        public BindRoute RouteOf(string name)
        {
            if (name == null)
            {
                return BindRoute.None;
            }
            if (_mouseBindings.ContainsKey(name))
            {
                return BindRoute.Mouse;
            }
            if (_keyboardFallbacks.ContainsKey(name))
            {
                return BindRoute.HookFallback;
            }
            return IsActive(name) ? BindRoute.Registered : BindRoute.None;
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
            _selfCollisions.Clear();

            // Tear down mouse bindings and stop the hook so it adds no overhead
            // while no mouse hotkeys are configured.
            _mouseBindings.Clear();
            _mouseHook?.Stop();

            // Same for keyboard fallbacks and their hook.
            _keyboardFallbacks.Clear();
            _comboHeld.Clear();
            _keyHook?.Stop();
        }

        private void OnHotkeyMessage(int id)
        {
            // Same reason as the mouse path: while a Keybinds field is capturing, pressing
            // an already-bound key was rebinding it AND firing its action, so trying to
            // change the Start/Stop key started the clicker every time.
            if (DeliveryPaused)
            {
                return;
            }

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

            if (_keyHook != null)
            {
                _keyHook.KeyEvent -= OnKeyHookEvent;
                _keyHook.Dispose();
                _keyHook = null;
            }

            try
            {
                _window.HotkeyMessage -= OnHotkeyMessage;
                _window.DestroyHandle();
            }
            catch (Exception ex)
            {
                Logger.Warn("[Hotkeys] error while disposing the hotkey window: " + ex.Message);
            }

            _disposed = true;
        }
    }
}
