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

        // The Shift / Ctrl / Shift+Ctrl versions of reserved bindings (RegisterShiftCtrlVariants), by
        // registration id. Name is the binding they fire. Kept apart from _registrations so a binding's
        // route, the conflict report and the self-collision check all keep seeing one entry per action.
        private readonly Dictionary<int, Registration> _variants = new Dictionary<int, Registration>();

        /// <summary>
        /// Bindings that tolerate it (HotkeyDefinition.ToleratesExtraShiftCtrl) also fire with Shift
        /// and/or Ctrl held on top of their own modifiers — in the keyboard hook, the mouse hook and
        /// for watched combinations. A reserved binding needs <see cref="RegisterShiftCtrlVariants"/>
        /// as well, because Windows only ever delivers the exact combination it was given. A
        /// combination bound exactly to something always goes to that.
        /// </summary>
        public bool ExtraShiftCtrl { get; set; }

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

        // Watched, not bound: combinations that are NOT delivered and NOT reserved — the key keeps
        // reaching whatever app is in front — but whose press is still noticed. Used while Tempo sleeps in
        // the tray: its start hotkeys are given back to other programs, and a press of one used to vanish
        // without a trace ("my keybind sometimes doesn't work"). Now it wakes Tempo up and says so.
        private readonly Dictionary<string, HotkeyDefinition> _watched =
            new Dictionary<string, HotkeyDefinition>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _watchHeld = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>A watched combination was pressed (see <see cref="Watch"/>). Raised on the hook's thread.</summary>
        public event EventHandler<HotkeyPressedEventArgs> WatchedHotkeyPressed;

        /// <summary>
        /// Notice presses of <paramref name="def"/> without delivering or reserving it: the key passes
        /// through untouched and <see cref="WatchedHotkeyPressed"/> fires once per press. Needs a low-level
        /// hook, installed only while something is watched and removed by <see cref="UnregisterAll"/>.
        /// </summary>
        public bool Watch(string name, HotkeyDefinition def)
        {
            ThrowIfDisposed();
            if (name == null || def == null || !def.IsValid) { return false; }
            _watched[name] = def.Clone();
            _watchHeld.Remove(name);
            if (def.IsMouse)
            {
                if (_mouseHook == null)
                {
                    _mouseHook = new MouseHotkeyHook();
                    _mouseHook.ButtonDown += OnMouseHotkey;
                }
                if (_mouseHook.IsRunning || _mouseHook.Start()) { return true; }
            }
            else if (EnsureKeyHook())
            {
                return true;
            }
            _watched.Remove(name);
            return false;
        }

        /// <summary>How many combinations are being watched (for Live debug and the tests).</summary>
        public int WatchedCount => _watched.Count;

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
            bool ok = Reserve(id, modifiers, virtualKey);

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

            // The same question for a combination another Tempo action already catches with the
            // keyboard HOOK (Windows refused it to both — another program owns it). The check above
            // only saw reserved ones, so the second action got a hook route of its own and one press
            // ran BOTH: the very outcome the check exists to prevent, on the route where the user
            // already has one surprise (the key also works in the other program).
            if (heldBy == null)
            {
                foreach (var kv in _keyboardFallbacks)
                {
                    if (!string.Equals(kv.Key, name, StringComparison.Ordinal) &&
                        kv.Value.GetModifierFlags() == mods && kv.Value.GetVirtualKey() == vk)
                    {
                        Unregister(name);
                        _selfCollisions[name] = kv.Key;
                        Logger.Warn($"[Hotkeys] '{name}' collides with Tempo's own '{kv.Key}' on the same " +
                                    "combination (caught by the keyboard hook); left unbound rather than doubling up on one key.");
                        return false;
                    }
                }
            }

            if (heldBy != null)
            {
                // Deliberately no hook fallback: adding one is what made both actions
                // fire. Left unbound and reported, so the Keybinds tab can say the truth.
                // Decided here, not by asking Windows: while a Keybinds field listens the
                // reservations are lifted (DeliveryPaused), and Windows would hand the same
                // combination out a second time.
                Unregister(name);
                _selfCollisions[name] = heldBy;
                Logger.Warn($"[Hotkeys] '{name}' collides with Tempo's own '{heldBy}' on the same " +
                            "combination; left unbound rather than doubling up on one key.");
                return false;
            }

            bool ok = Register(name, mods, vk);

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

        /// <summary>
        /// Reserves a binding's Shift / Ctrl / Shift+Ctrl versions for it as well, so F6 fires while
        /// Shift is held for sprinting. Skips every combination in <paramref name="taken"/> — bound
        /// exactly to some action, or already given to a more specific binding — and adds the ones it
        /// takes, so callers should go from the most specific binding to the least. Only for a binding
        /// Windows reserved; hooked and mouse bindings match loosely in their hooks instead. Best
        /// effort: a version another program owns is left to that program. Returns how many it took.
        /// </summary>
        public int RegisterShiftCtrlVariants(string name, HotkeyDefinition def, ISet<string> taken)
        {
            ThrowIfDisposed();
            if (!ExtraShiftCtrl || name == null || def == null || def.IsMouse) { return 0; }
            if (RouteOf(name) != BindRoute.Registered) { return 0; }

            int took = 0;
            foreach (HotkeyDefinition v in def.ExtraShiftCtrlVariants())
            {
                string identity = v.ToIdentityString();
                if (taken != null && taken.Contains(identity)) { continue; }
                uint mods = v.GetModifierFlags();
                uint vk = v.GetVirtualKey();
                int id = _nextId++;
                if (Reserve(id, mods, vk))
                {
                    _variants[id] = new Registration { Id = id, Name = name, Modifiers = mods, VirtualKey = vk, Active = true };
                    taken?.Add(identity);
                    took++;
                }
            }
            if (took > 0)
            {
                Logger.Info($"[Hotkeys] '{name}' also fires with Shift/Ctrl held ({took} more combination(s) reserved).");
            }
            return took;
        }

        /// <summary>How many Shift/Ctrl versions are reserved (Live debug and the tests).</summary>
        public int VariantCount => _variants.Count;

        /// <summary>
        /// True when pressing <paramref name="trigger"/>'s key or button with the modifiers held NOW is
        /// exactly one of Tempo's combinations — reserved, a Shift/Ctrl version, hooked or watched.
        /// Hold mode asks before engaging with extra Shift/Ctrl held, so one press never runs two things.
        /// </summary>
        public bool IsExactlyBoundNow(HotkeyDefinition trigger)
        {
            if (trigger == null || !trigger.IsValid) { return false; }
            if (trigger.IsMouse)
            {
                foreach (var set in new[] { _mouseBindings, _watched })
                {
                    foreach (HotkeyDefinition d in set.Values)
                    {
                        if (d.IsMouse && d.MouseButton == trigger.MouseButton && d.ModifiersMatch(IsKeyDown)) { return true; }
                    }
                }
                return false;
            }
            return ExactlyBound(CurrentModifiers(), trigger.GetVirtualKey());
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
            if (_keyboardFallbacks.Count == 0 && _watched.Count == 0)
            {
                return;
            }

            // Paused (a Keybinds field is listening): nothing fires — but a RELEASE still ends the press
            // it belongs to. Ignoring releases as well left a key that went down before the pause marked
            // as held, so the first press after the field let go was taken for auto-repeat and did
            // nothing. (Showing Tempo with its hotkey can land focus straight in a capture field.)
            if (DeliveryPaused || !e.IsKeyDown)
            {
                if (!e.IsKeyDown) { ForgetHeld(e.VirtualKey); }
                return;
            }

            // Watched combinations: noticed, never swallowed — by the same rules a bound hotkey
            // follows, so what wakes Tempo is exactly what would have fired. Once per press.
            string watched = BestKeyMatch(_watched, e.VirtualKey);
            if (watched != null && _watchHeld.Add(watched))
            {
                WatchedHotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs(-1, watched));
            }

            // Hooked bindings: once per press — auto-repeat finds the name still held.
            string bound = BestKeyMatch(_keyboardFallbacks, e.VirtualKey);
            if (bound != null && _comboHeld.Add(bound))
            {
                HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs(-1, bound));
            }
        }

        /// <summary>
        /// Which of <paramref name="set"/> a press of <paramref name="virtualKey"/> fires now, or null.
        /// The binding whose modifiers are EXACTLY the ones held wins outright. Failing that, with
        /// <see cref="ExtraShiftCtrl"/> on, the most specific binding that tolerates Shift/Ctrl on
        /// top — but only when no Tempo combination is exactly this press: a reserved one is being
        /// delivered through WM_HOTKEY at this moment, and one press must not run two actions.
        /// </summary>
        private string BestKeyMatch(Dictionary<string, HotkeyDefinition> set, int virtualKey)
        {
            string loose = null;
            int looseRank = -1;
            foreach (var pair in set)
            {
                HotkeyDefinition def = pair.Value;
                if (def.IsMouse || (int)def.GetVirtualKey() != virtualKey) { continue; }
                if (def.ModifiersMatch(IsKeyDown)) { return pair.Key; }
                if (ExtraShiftCtrl && def.ModifierCount() > looseRank && def.ModifiersMatch(IsKeyDown, true))
                {
                    loose = pair.Key;
                    looseRank = def.ModifierCount();
                }
            }
            if (loose != null && ExactlyBound(CurrentModifiers(), (uint)virtualKey)) { return null; }
            return loose;
        }

        /// <summary>True when any Tempo combination — reserved, a Shift/Ctrl version, hooked or watched — is exactly this.</summary>
        private bool ExactlyBound(uint mods, uint virtualKey)
        {
            foreach (Registration r in _registrations.Values)
            {
                if (r.Active && r.Modifiers == mods && r.VirtualKey == virtualKey) { return true; }
            }
            foreach (Registration r in _variants.Values)
            {
                if (r.Modifiers == mods && r.VirtualKey == virtualKey) { return true; }
            }
            foreach (HotkeyDefinition d in _keyboardFallbacks.Values)
            {
                if (d.GetModifierFlags() == mods && d.GetVirtualKey() == virtualKey) { return true; }
            }
            foreach (HotkeyDefinition d in _watched.Values)
            {
                if (!d.IsMouse && d.GetModifierFlags() == mods && d.GetVirtualKey() == virtualKey) { return true; }
            }
            return false;
        }

        /// <summary>The modifiers held now, as RegisterHotKey flags.</summary>
        private static uint CurrentModifiers()
        {
            uint mods = 0;
            if (IsKeyDown(0x11)) { mods |= 0x0002; }                      // MOD_CONTROL
            if (IsKeyDown(0x12)) { mods |= 0x0001; }                      // MOD_ALT
            if (IsKeyDown(0x10)) { mods |= 0x0004; }                      // MOD_SHIFT
            if (IsKeyDown(0x5B) || IsKeyDown(0x5C)) { mods |= 0x0008; }   // MOD_WIN
            return mods;
        }

        /// <summary>Ends the "held" state of every hooked or watched combination on this key.</summary>
        private void ForgetHeld(int virtualKey)
        {
            foreach (var pair in _keyboardFallbacks)
            {
                if ((int)pair.Value.GetVirtualKey() == virtualKey) { _comboHeld.Remove(pair.Key); }
            }
            foreach (var pair in _watched)
            {
                if (!pair.Value.IsMouse && (int)pair.Value.GetVirtualKey() == virtualKey) { _watchHeld.Remove(pair.Key); }
            }
        }
        public bool RegisterMouse(string name, HotkeyDefinition def)
        {
            ThrowIfDisposed();

            if (def == null || !def.IsMouse)
            {
                return false;
            }

            // One button combination, one action. A press only ever ran the FIRST binding on it (see
            // OnMouseHotkey), so a second one was silently dead while it reported a working route and
            // the Keybinds tab said nothing. Refused and reported the way a keyboard clash is.
            _selfCollisions.Remove(name);
            foreach (var kv in _mouseBindings)
            {
                if (!string.Equals(kv.Key, name, StringComparison.Ordinal) && kv.Value.SameCombination(def))
                {
                    _mouseBindings.Remove(name);
                    _selfCollisions[name] = kv.Key;
                    Logger.Warn($"[Hotkeys] mouse hotkey '{name}' collides with Tempo's own '{kv.Key}' on " +
                                $"{def.ToDisplayString()}; left unbound rather than doubling up on one button.");
                    return false;
                }
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
        /// While true, bound hotkeys are not delivered — and Windows' reservations are lifted, so
        /// the keys reach the field. Set by the Keybinds tab for as long as a capture field has focus.
        ///
        /// Without it a mouse button could be bound exactly once and never changed: click
        /// into the field to rebind X2, and the hook — which runs before the click ever
        /// reaches the field — matched the EXISTING X2 binding, suppressed the click and
        /// fired the old action instead. The rebind was unreachable by the only gesture
        /// that performs it, and the action fired every time you tried.
        ///
        /// The keyboard had the same trap one layer down. A combination reserved with RegisterHotKey
        /// is taken by Windows before ANY window sees it — Tempo's own included (measured with
        /// scratchpad/hkprobe: WM_HOTKEY 1, the focused text box's KeyDown 0). Not delivering
        /// WM_HOTKEY was not enough: pressing a bound key in a field produced nothing at all, so a
        /// saved key could not be moved to another action by pressing it there, and "that key was
        /// taken from another action" could never happen for one. The reservations are released
        /// while a field listens and taken back when it stops.
        /// </summary>
        public bool DeliveryPaused
        {
            get => _paused;
            set
            {
                if (_paused == value) { return; }
                _paused = value;
                if (_disposed) { return; }
                if (value) { LiftReservations(); } else { RestoreReservations(); }
            }
        }
        private bool _paused;

        private void LiftReservations()
        {
            foreach (Registration r in _registrations.Values)
            {
                if (r.Active) { NativeMethods.UnregisterHotKey(_window.Handle, r.Id); }
            }
            foreach (Registration r in _variants.Values)
            {
                NativeMethods.UnregisterHotKey(_window.Handle, r.Id);
            }
        }

        private void RestoreReservations()
        {
            foreach (Registration r in _registrations.Values)
            {
                if (!r.Active || NativeMethods.RegisterHotKey(_window.Handle, r.Id, r.Modifiers | NativeMethods.MOD_NOREPEAT, r.VirtualKey))
                {
                    continue;
                }
                // Another program took the combination in the seconds a field was listening. Catch
                // it with the keyboard hook instead — what Register would have done in the first place.
                r.Active = false;
                if (EnsureKeyHook())
                {
                    _keyboardFallbacks[r.Name] = DefinitionOf(r.Modifiers, r.VirtualKey);
                    Logger.Warn($"[Hotkeys] '{r.Name}' was taken by another program while a Keybinds field listened; using the keyboard hook.");
                }
                else
                {
                    Logger.Warn($"[Hotkeys] '{r.Name}' was taken by another program while a Keybinds field listened, and the hook would not install.");
                }
            }
            var lost = new List<int>();
            foreach (var kv in _variants)
            {
                Registration r = kv.Value;
                if (!NativeMethods.RegisterHotKey(_window.Handle, r.Id, r.Modifiers | NativeMethods.MOD_NOREPEAT, r.VirtualKey)) { lost.Add(kv.Key); }
            }
            foreach (int id in lost) { _variants.Remove(id); }   // a Shift/Ctrl version is best effort
        }

        /// <summary>Registers with Windows — and lets go again at once while a Keybinds field is listening.</summary>
        private bool Reserve(int id, uint modifiers, uint virtualKey)
        {
            bool ok = NativeMethods.RegisterHotKey(_window.Handle, id, modifiers | NativeMethods.MOD_NOREPEAT, virtualKey);
            if (ok && _paused) { NativeMethods.UnregisterHotKey(_window.Handle, id); }   // RestoreReservations takes it
            return ok;
        }

        private static HotkeyDefinition DefinitionOf(uint modifiers, uint virtualKey) =>
            new HotkeyDefinition((Keys)virtualKey, control: (modifiers & 0x0002) != 0, alt: (modifiers & 0x0001) != 0,
                                 shift: (modifiers & 0x0004) != 0, win: (modifiers & 0x0008) != 0);

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

            // Watched mouse combinations: noticed, and the click is left alone.
            string watched = BestMouseMatch(_watched, e.Button);
            if (watched != null)
            {
                WatchedHotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs(-1, watched));
                return;
            }

            string bound = BestMouseMatch(_mouseBindings, e.Button);
            if (bound == null)
            {
                return;
            }
            HotkeyDefinition def = _mouseBindings[bound];

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

            HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs(-1, bound));
        }

        /// <summary>
        /// The mouse twin of <see cref="BestKeyMatch"/>: an exact match wins; otherwise, with
        /// <see cref="ExtraShiftCtrl"/>, the most specific binding on this button that tolerates
        /// Shift/Ctrl on top — unless some mouse combination of Tempo's is exactly this press.
        /// </summary>
        private string BestMouseMatch(Dictionary<string, HotkeyDefinition> set, HotkeyMouseButton button)
        {
            string loose = null;
            int looseRank = -1;
            foreach (var pair in set)
            {
                HotkeyDefinition def = pair.Value;
                if (!def.IsMouse || def.MouseButton != button) { continue; }
                if (def.ModifiersMatch(IsKeyDown)) { return pair.Key; }
                if (ExtraShiftCtrl && def.ModifierCount() > looseRank && def.ModifiersMatch(IsKeyDown, true))
                {
                    loose = pair.Key;
                    looseRank = def.ModifierCount();
                }
            }
            if (loose == null) { return null; }
            foreach (var other in new[] { _mouseBindings, _watched })
            {
                foreach (HotkeyDefinition d in other.Values)
                {
                    if (d.IsMouse && d.MouseButton == button && d.ModifiersMatch(IsKeyDown)) { return null; }
                }
            }
            return loose;
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

            // And its Shift/Ctrl versions: left behind, they would keep firing an action that has
            // been rebound — or, worse, hold a combination its new binding needs.
            var versions = new List<int>();
            foreach (var kv in _variants)
            {
                if (string.Equals(kv.Value.Name, name, StringComparison.OrdinalIgnoreCase)) { versions.Add(kv.Key); }
            }
            foreach (int vid in versions)
            {
                NativeMethods.UnregisterHotKey(_window.Handle, vid);
                _variants.Remove(vid);
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
                if (_variants.Count > 0) { sb.Append(", ").Append(_variants.Count).Append(" Shift/Ctrl version(s) reserved"); }
                if (ExtraShiftCtrl) { sb.Append(" · extra Shift/Ctrl allowed"); }
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
            foreach (var reg in _variants.Values)
            {
                NativeMethods.UnregisterHotKey(_window.Handle, reg.Id);
            }

            _registrations.Clear();
            _variants.Clear();
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

            // And the watched ones — the hooks above are down, so nothing can notice them any more.
            _watched.Clear();
            _watchHeld.Clear();
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
            else if (_variants.TryGetValue(id, out var version))
            {
                // A Shift/Ctrl version: it fires the binding it belongs to.
                HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs(version.Id, version.Name));
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
