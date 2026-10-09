using System;
using System.Collections.Generic;
using AutoClicker.Models;
using AutoClicker.Native;
using AutoClicker.Utils;

namespace AutoClicker.Engine
{
    /// <summary>
    /// Records raw mouse activity into a <see cref="Macro"/>. Movements are
    /// throttled so the resulting macro stays compact, and the elapsed time
    /// between events is captured as explicit <see cref="MacroActionType.Delay"/>
    /// steps so playback preserves the original rhythm.
    ///
    /// A take is only the user's own input, from their first action to their last.
    /// Everything that is really Tempo's own controls stays out of it: the Record /
    /// Stop hotkey's modifiers, keys still held from before recording began, and a
    /// click on Tempo's own window — the Stop button, above all, which every take
    /// stopped by button used to end with (playback then moved to where that button
    /// had been and clicked whatever was there now).
    /// </summary>
    public sealed class MacroRecorder : IDisposable
    {
        private readonly LowLevelMouseHook _hook = new LowLevelMouseHook();
        private readonly LowLevelKeyboardHook _keyHook = new LowLevelKeyboardHook();
        private Macro _macro;
        // Event timeline is the OS GetTickCount (ms) clock carried on each hook event, so
        // it isn't skewed by hook-callback dispatch latency. _lastEventMs holds the last
        // event's tick value.
        private long _lastEventMs;
        private bool _recording;
        private bool _disposed;
        private readonly object _appendLock = new object();

        // Keys currently held, each with the KeyDown step that pressed it. Auto-repeat
        // key-downs collapse into that one step, and Stop() can find the press again
        // (see DropStopChord).
        private readonly Dictionary<int, MacroAction> _keysDown = new Dictionary<int, MacroAction>();

        // Modifier keys that were already down when recording started — the Ctrl/Alt/Shift
        // of the hotkey that started it, in practice. Their auto-repeats and their release
        // belong to that hotkey, not to the take; the first release lets the key be
        // recorded normally again.
        private readonly HashSet<int> _heldAtStart = new HashSet<int>();

        // Track which mouse buttons the recorder has seen go down, so we can
        // synthesize the matching up event at Stop() if a recording ends while a
        // button is still held. Without this, playback would leave the button
        // pressed.
        private readonly HashSet<MacroActionType> _buttonsDown = new HashSet<MacroActionType>();

        // True once the first real event has been captured. Used to skip the
        // "leading silence" between Start() being called and the user's first
        // action — without this the macro begins playback with a long pause.
        private bool _firstEventCaptured;

        // Only store a move if the cursor travelled at least this many pixels or
        // this many milliseconds have elapsed. Tighter values yield smoother
        // playback paths at the cost of a slightly larger macro file.
        private const int MoveDistanceThreshold = 3;
        private const long MoveTimeThresholdMs = 16;
        private int _lastMoveX = int.MinValue;
        private int _lastMoveY = int.MinValue;
        private long _lastMoveMs;

        // Where PLAYBACK will have put the pointer once the steps recorded so far have run:
        // the last recorded move or press. int.MinValue until something positional exists.
        private int _macroPointerX = int.MinValue;
        private int _macroPointerY = int.MinValue;

        // The newest position the hook has reported, recorded or not.
        private bool _havePointer;
        private int _pointerX;
        private int _pointerY;

        // The newest move the throttle skipped. The throttle keeps a take compact, but the
        // sample it drops last is where the pointer came to REST: hover over something and
        // press E, and playback used to press E up to 16 ms of movement short of it. It is
        // written out before the next key press and at the end (see FlushPendingMove).
        private bool _pendingMove;
        private int _pendingX;
        private int _pendingY;
        private long _pendingMs;

        // The first and last steps that DID something — a press, a release, a scroll. Moves,
        // delays and modifier presses don't count: what comes before the first and after the
        // last of these is what Stop() may tidy away.
        private MacroAction _firstRealStep;
        private MacroAction _lastRealStep;

        // The pointer was over Tempo's own window when recording began — the Record button was
        // pressed — so the take opens with the pointer's trip from there to the app.
        private bool _startedOnTempo;

        // When the newest click, scroll or key press on Tempo's own window happened, if one has
        // since _lastRealStep (-1 = none). One just before Stop() means the take ended with the
        // user reaching for Tempo — its Stop button — so the pointer's trip there is trimmed.
        private long _ownClickMs = -1;

        // "Just before": Stop() runs as the Stop button's click is handled, milliseconds after
        // the press. A Tempo click longer ago than this (a checkbox, then back to the game and
        // stop by hotkey) did not end the take.
        private const uint OwnClickEndsTakeMs = 1500;

        private static readonly uint OwnProcessId = (uint)Environment.ProcessId;

        // ── Camera turns ────────────────────────────────────────────────────────
        // Raw reports come far faster than a macro needs (a gaming mouse sends up to 1000+ a
        // second), so they are summed into one camera step per clock tick, and at least this
        // far apart. Summing loses nothing: the step carries every count of its interval.
        private const uint CameraSampleMs = 8;
        private readonly Action<RawMouseEvent> _onRawMouse;
        private bool _camera;
        private bool _camHave;
        private int _camX;
        private int _camY;
        private long _camFirstMs;
        private long _camLastMs;

        public bool RecordMovements { get; set; } = true;

        /// <summary>
        /// Record mouse movement as CAMERA TURNS: how far the mouse moved (raw counts, from
        /// <see cref="RawMouseHub"/>) instead of where the pointer was. For games with
        /// mouse-look — first person, shift lock, turning while holding right-click — where
        /// the pointer is locked in place: the hook then reports it sitting still (or snapping
        /// back to the middle) however far the mouse travels, so a recording of positions held
        /// almost none of the turn. Only with <see cref="RecordMovements"/>.
        /// </summary>
        public bool RecordCamera { get; set; }

        /// <summary>True while THIS take records camera turns (raw input really started).</summary>
        public bool RecordingCamera => _camera;

        /// <summary>When true, key presses are captured in addition to the mouse.</summary>
        public bool RecordKeyboard { get; set; } = true;

        /// <summary>
        /// Virtual-key codes to ignore while recording (typically the keys used
        /// to toggle recording and to emergency-stop). Default empty.
        /// </summary>
        public HashSet<int> ExcludedVirtualKeys { get; } = new HashSet<int>();

        /// <summary>
        /// Mouse-button hotkeys — side buttons, middle, modified left/right — whatever action
        /// they are bound to. A press matching one (that button, with exactly its modifiers
        /// held) is swallowed by the hotkey hook and never reaches the app, so it is no part of
        /// the take either. It used to be recorded (this hook runs before the hotkey hook), so
        /// a take stopped with a middle-click ended with a middle-click.
        /// </summary>
        public List<HotkeyDefinition> ExcludedMouseHotkeys { get; } = new List<HotkeyDefinition>();

        public bool IsRecording => _recording;

        public event EventHandler<MacroAction> ActionRecorded;

        public MacroRecorder()
        {
            _hook.MouseEvent += OnMouseEvent;
            _keyHook.KeyEvent += OnKeyEvent;
            _onRawMouse = OnRawMouse;
        }

        /// <summary>Begins a new recording, discarding any previous capture.</summary>
        public bool Start(string macroName)
        {
            ThrowIfDisposed();

            if (_recording)
            {
                return true;
            }

            _macro = new Macro(string.IsNullOrWhiteSpace(macroName) ? "Recorded Macro" : macroName);
            _lastEventMs = 0;
            _lastMoveX = int.MinValue;
            _lastMoveY = int.MinValue;
            _lastMoveMs = 0;
            _macroPointerX = int.MinValue;
            _macroPointerY = int.MinValue;
            _havePointer = false;
            _pendingMove = false;
            _firstRealStep = null;
            _lastRealStep = null;
            _ownClickMs = -1;
            _keysDown.Clear();
            _buttonsDown.Clear();
            _firstEventCaptured = false;
            NoteModifiersHeldAtStart();
            _startedOnTempo = ScreenGeometry.TryGetCursorPosition(out int startX, out int startY) &&
                              IsOwnWindowAt(startX, startY);

            if (!_hook.Start())
            {
                Logger.Error("[Macro] recorder could not install the mouse hook.");
                return false;
            }

            if (RecordKeyboard)
            {
                if (!_keyHook.Start())
                {
                    // Non-fatal: continue recording mouse only.
                    Logger.Warn("[Macro] keyboard hook failed; recording mouse only.");
                }
            }

            _camera = false;
            _camHave = false;
            _camX = 0;
            _camY = 0;
            if (RecordCamera && RecordMovements)
            {
                // Before _recording is set, so the take never runs without its movement source.
                _camera = RawMouseHub.Subscribe(_onRawMouse);
                if (!_camera)
                {
                    Logger.Warn("[Macro] raw mouse input would not start; recording screen positions instead of camera turns.");
                }
            }

            _recording = true;
            Logger.Info(_camera ? "[Macro] recording started (mouse as camera turns)." : "[Macro] recording started.");
            return true;
        }

        /// <summary>Stops recording and returns the captured macro.</summary>
        public Macro Stop()
        {
            if (!_recording)
            {
                return _macro;
            }

            // Use the same GetTickCount clock as the hook events so the trailing delay is
            // measured on one consistent timeline.
            long now = (uint)Environment.TickCount;

            // 0. The last of the camera turn — reports still queued, and the part summed but
            //    not yet written — while the take still counts as recording, or OnRawMouse
            //    would turn the queued reports away.
            if (_camera)
            {
                lock (_appendLock)
                {
                    RawMouseHub.DrainPending((uint)now);
                    AppendCameraStep();
                }
            }

            _recording = false;

            int chord = 0;
            int trimmed = 0;
            lock (_appendLock)
            {
                // 1. The modifiers of the hotkey that is stopping this recording.
                chord = DropStopChord();

                // 2. The end of the take. If it ended with a click on Tempo's own window, the
                //    pointer's trip there is not part of it. Otherwise the pointer's final
                //    resting place is, even if the throttle had skipped it.
                long carryMs = 0;
                if (EndedOnTempo(now))
                {
                    int before = _macro.Actions.Count;
                    carryMs += TrimPointerTail();
                    trimmed = before - _macro.Actions.Count;
                    _pendingMove = false;
                }
                else
                {
                    FlushPendingMove();
                }

                // 3. The start of the take, when the Record button began it: the pointer's trip
                //    from that button to the app. Every loop used to open with it.
                if (_startedOnTempo)
                {
                    trimmed += TrimPointerLead();
                }

                // Removing steps can leave two waits side by side, or a wait with nothing
                // after it. A take ends on its last action; the time is kept (carryMs) only
                // for anything released below, which really was held until now.
                carryMs += MergeDelaysAndTrimTrailing();

                // A camera take opens with a camera step, even one of no movement: playback
                // takes its mode from the latest movement step, and a take that opened with a
                // click would otherwise have that click jump to its stored position — in a game
                // with mouse-look, a screen coordinate it can read as a huge turn.
                if (_camera && _macro.Actions.Count > 0 &&
                    _macro.Actions[0].Type != MacroActionType.MouseMoveRelative)
                {
                    _macro.Actions.Insert(0, new MacroAction(MacroActionType.MouseMoveRelative));
                }

                // 4. Synthesize Up events for any keys or mouse buttons still held, so a
                //    played-back macro ends with no stuck inputs in the target application.
                if (_keysDown.Count > 0 || _buttonsDown.Count > 0)
                {
                    if (carryMs > 0)
                    {
                        Append(new MacroAction(MacroActionType.Delay) { DelayMilliseconds = ClampMs(carryMs) });
                    }
                    AppendDelay(now);

                    foreach (int vk in _keysDown.Keys)
                    {
                        Append(new MacroAction(MacroActionType.KeyUp) { VirtualKey = vk });
                    }

                    // The position is informational (playback releases wherever the pointer
                    // is); the newest known one keeps the step list and the macro doctor honest.
                    int rx = _havePointer ? _pointerX : (_macroPointerX != int.MinValue ? _macroPointerX : 0);
                    int ry = _havePointer ? _pointerY : (_macroPointerY != int.MinValue ? _macroPointerY : 0);
                    foreach (MacroActionType downType in _buttonsDown)
                    {
                        Append(new MacroAction(MacroAction.ReleaseOf(downType)) { X = rx, Y = ry });
                    }
                }
                _keysDown.Clear();
                _buttonsDown.Clear();
                _heldAtStart.Clear();
            }

            if (_camera)
            {
                // Only this listener leaves: camera-relative movement or the second cursor
                // sharing the registration keep theirs.
                RawMouseHub.Unsubscribe(_onRawMouse);
            }
            _hook.Stop();
            _keyHook.Stop();
            Logger.Info($"[Macro] recording stopped ({(_macro?.StepCount ?? 0)} steps" +
                        (chord > 0 ? $"; left out the stop hotkey's {chord} modifier key(s)" : "") +
                        (trimmed > 0 ? $"; trimmed {trimmed} step(s) of the pointer travelling to or from Tempo's own window" : "") +
                        ").");
            return _macro;
        }

        private void OnKeyEvent(object sender, KeyboardHookEventArgs e)
        {
            if (!_recording || _macro == null || !RecordKeyboard)
            {
                return;
            }

            // Never record synthesised input — otherwise Tempo's own playback or the
            // auto-clicker running during a recording would be captured back into it.
            if (e.Injected)
            {
                return;
            }

            // Skip keys reserved for control (e.g. the toggle-record and
            // emergency-stop hotkeys, plus their modifiers).
            if (ExcludedVirtualKeys.Contains(e.VirtualKey))
            {
                return;
            }

            // Timestamp from the OS event time (when the key physically changed), NOT
            // from when this hook callback happens to run — the callback can be dispatched
            // late while the UI thread is busy, and that jitter used to be folded straight
            // into the recorded key-hold length, so a WASD hold played back the wrong
            // duration. data.time is a GetTickCount (ms) value.
            long now = e.TimeStamp;

            lock (_appendLock)
            {
                // A modifier already down when recording began: part of the hotkey that
                // started it. Its auto-repeats used to be recorded as a fresh press and its
                // release as a stray Up, so a take started with Ctrl+F9 began with a Ctrl tap.
                if (_heldAtStart.Contains(e.VirtualKey))
                {
                    if (!e.IsKeyDown)
                    {
                        _heldAtStart.Remove(e.VirtualKey);
                    }
                    return;
                }

                if (e.IsKeyDown)
                {
                    // Collapse OS auto-repeat into a single down event.
                    if (_keysDown.ContainsKey(e.VirtualKey))
                    {
                        return;
                    }

                    // Typed into Tempo itself — Space or Enter on its focused Stop button, say.
                    // The key went to Tempo, not to the app being recorded, and played back it
                    // would go to whatever has focus then. Its release is dropped with it.
                    if (IsOwnWindowForeground())
                    {
                        _ownClickMs = now;
                        return;
                    }

                    FlushCamera(now);
                    FlushPendingMove();
                    AppendDelay(now);
                    var press = new MacroAction(MacroActionType.KeyDown) { VirtualKey = e.VirtualKey };
                    _keysDown[e.VirtualKey] = press;
                    Append(press);
                    if (!IsModifier(e.VirtualKey))
                    {
                        NoteRealStep(press);
                    }
                }
                else
                {
                    // A release with no press in this take — the key went down before recording
                    // started. It used to be recorded "so held keys are released on playback",
                    // but playback never pressed it: all it could release is a key the USER is
                    // holding while the macro runs.
                    if (!_keysDown.Remove(e.VirtualKey))
                    {
                        return;
                    }

                    FlushCamera(now);
                    FlushPendingMove();
                    AppendDelay(now);
                    var release = new MacroAction(MacroActionType.KeyUp) { VirtualKey = e.VirtualKey };
                    Append(release);
                    NoteRealStep(release);
                }
            }
        }

        private void OnMouseEvent(object sender, MouseHookEventArgs e)
        {
            if (!_recording || _macro == null)
            {
                return;
            }

            // Ignore Tempo's own synthesised clicks/moves (see OnKeyEvent).
            if (e.Injected)
            {
                return;
            }

            // OS event time, not callback-dispatch time (see OnKeyEvent).
            long now = e.TimeStamp;

            lock (_appendLock)
            {
                _havePointer = true;
                _pointerX = e.X;
                _pointerY = e.Y;

                if (e.Type == MouseHookEventType.Move)
                {
                    // In camera mode the movement comes from raw input (OnRawMouse); the
                    // hook's positions are where a locked pointer sits, not how far it turned.
                    if (!RecordMovements || _camera)
                    {
                        return;
                    }

                    bool farEnough =
                        _lastMoveX == int.MinValue ||
                        Math.Abs(e.X - _lastMoveX) >= MoveDistanceThreshold ||
                        Math.Abs(e.Y - _lastMoveY) >= MoveDistanceThreshold;

                    bool longEnough = (now - _lastMoveMs) >= MoveTimeThresholdMs;

                    if (!(farEnough && longEnough))
                    {
                        _pendingMove = true;
                        _pendingX = e.X;
                        _pendingY = e.Y;
                        _pendingMs = now;
                        return;
                    }

                    AppendMove(e.X, e.Y, now);
                    return;
                }

                MacroActionType? type = e.Type switch
                {
                    MouseHookEventType.LeftDown => MacroActionType.LeftDown,
                    MouseHookEventType.LeftUp => MacroActionType.LeftUp,
                    MouseHookEventType.RightDown => MacroActionType.RightDown,
                    MouseHookEventType.RightUp => MacroActionType.RightUp,
                    MouseHookEventType.MiddleDown => MacroActionType.MiddleDown,
                    MouseHookEventType.MiddleUp => MacroActionType.MiddleUp,
                    MouseHookEventType.Wheel => MacroActionType.Wheel,
                    MouseHookEventType.XButton1Down => MacroActionType.X1Down,
                    MouseHookEventType.XButton1Up => MacroActionType.X1Up,
                    MouseHookEventType.XButton2Down => MacroActionType.X2Down,
                    MouseHookEventType.XButton2Up => MacroActionType.X2Up,
                    _ => (MacroActionType?)null
                };

                if (type == null)
                {
                    return;
                }

                // Presses and releases are told apart by kind (MacroAction.IsButtonPress), not
                // by a list of button names here: a button missing from such a list fell
                // through to the scroll branch at the bottom.
                if (MacroAction.IsButtonPress(type.Value))
                {
                    // A press on Tempo's own window is the user working Tempo — its Stop
                    // button, at the end of nearly every take. Played back it can only miss:
                    // Tempo's own window ignores the macro's clicks while it runs, and
                    // anywhere else the click lands on whatever sits there now. Its release
                    // then has no press in the take and is dropped with it.
                    if (IsOwnWindowAt(e.X, e.Y))
                    {
                        _ownClickMs = now;
                        return;
                    }
                    if (IsExcludedMouseHotkey(type.Value))
                    {
                        return;
                    }

                    FlushCamera(now);
                    _pendingMove = false;   // the press carries its own position
                    AppendDelay(now);
                    // Track button-down / button-up transitions so a recording that ends
                    // while a button is held is repaired in Stop().
                    _buttonsDown.Add(type.Value);
                    var press = new MacroAction(type.Value) { X = e.X, Y = e.Y };
                    Append(press);
                    _macroPointerX = e.X;
                    _macroPointerY = e.Y;
                    NoteRealStep(press);
                    return;
                }

                if (MacroAction.IsButtonRelease(type.Value))
                {
                    // No press in this take: pressed before recording started, or pressed on
                    // Tempo's own window (above).
                    if (!_buttonsDown.Remove(MacroAction.PressOf(type.Value)))
                    {
                        return;
                    }

                    FlushCamera(now);
                    _pendingMove = false;
                    AppendDelay(now);
                    // Playback releases wherever the pointer is. When the button went up
                    // somewhere else — the end of a drag, which the move throttle may have
                    // cut short and which a take without mouse movement never had — the
                    // pointer is taken there first, so a drag-and-drop replays as one
                    // instead of as a click on the spot it started from.
                    MoveIfElsewhere(e.X, e.Y);
                    var release = new MacroAction(type.Value) { X = e.X, Y = e.Y };
                    Append(release);
                    NoteRealStep(release);
                    return;
                }

                // The wheel.
                if (IsOwnWindowAt(e.X, e.Y))
                {
                    _ownClickMs = now;
                    return;
                }

                FlushCamera(now);
                _pendingMove = false;
                AppendDelay(now);
                // Windows scrolls whatever is under the pointer, so the scroll goes where
                // it happened — not wherever the last click left the pointer.
                MoveIfElsewhere(e.X, e.Y);
                var scroll = new MacroAction(MacroActionType.Wheel)
                {
                    X = e.X,
                    Y = e.Y,
                    WheelDelta = e.WheelDelta
                };
                Append(scroll);
                NoteRealStep(scroll);
            }
        }

        /// <summary>
        /// Raw mouse movement, for a camera take. Runs on the UI thread, from the shared raw
        /// input (or from <see cref="RawMouseHub.DrainPending"/> inside a hook callback).
        /// </summary>
        private void OnRawMouse(RawMouseEvent e)
        {
            if (!_recording || !_camera || _macro == null)
            {
                return;
            }
            // Injected movement (Tempo's own playback, any other injector) has no device;
            // the hook path skips injected input the same way. A tablet or remote-desktop
            // pointer reports positions, not movement.
            if (e.Device == IntPtr.Zero || e.Absolute || (e.Dx == 0 && e.Dy == 0))
            {
                return;
            }

            lock (_appendLock)
            {
                if (_camHave && TickGap(_camFirstMs, e.Time) >= CameraSampleMs)
                {
                    AppendCameraStep();
                }
                if (!_camHave)
                {
                    _camHave = true;
                    _camFirstMs = e.Time;
                }
                _camX += e.Dx;
                _camY += e.Dy;
                _camLastMs = e.Time;
            }
        }

        /// <summary>Writes the movement summed so far as one camera step (under the lock).</summary>
        private void AppendCameraStep()
        {
            if (!_camHave)
            {
                return;
            }
            _camHave = false;
            int dx = _camX, dy = _camY;
            _camX = 0;
            _camY = 0;
            if (dx == 0 && dy == 0)
            {
                return;   // there and back within one sample: nothing to turn
            }
            AppendDelay(_camLastMs);
            Append(new MacroAction(MacroActionType.MouseMoveRelative) { X = dx, Y = dy });
        }

        /// <summary>
        /// Before a key, click or scroll in a camera take: the camera turn that came before
        /// it is written first — including raw reports still queued behind this hook
        /// callback, which runs AHEAD of them (see <see cref="RawMouseHub.DrainPending"/>).
        /// So "turn, then shoot" is recorded, and replayed, in that order.
        /// </summary>
        private void FlushCamera(long now)
        {
            if (!_camera)
            {
                return;
            }
            RawMouseHub.DrainPending((uint)now);
            AppendCameraStep();
        }

        /// <summary>Milliseconds from <paramref name="from"/> to <paramref name="to"/> on the tick clock; 0 if out of order.</summary>
        private static uint TickGap(long from, long to)
        {
            uint gap = unchecked((uint)to - (uint)from);
            return gap > int.MaxValue ? 0 : gap;
        }

        /// <summary>Records a movement sample (under the lock).</summary>
        private void AppendMove(int x, int y, long at)
        {
            _lastMoveX = x;
            _lastMoveY = y;
            _lastMoveMs = at;
            _pendingMove = false;

            AppendDelay(at);
            Append(new MacroAction(MacroActionType.MouseMove) { X = x, Y = y });
            _macroPointerX = x;
            _macroPointerY = y;
        }

        /// <summary>
        /// Writes out the move sample the throttle skipped, when it leaves the pointer
        /// somewhere other than where playback will have it — the spot the pointer came to
        /// rest on before a key press or the end of the take.
        /// </summary>
        private void FlushPendingMove()
        {
            if (!_pendingMove)
            {
                return;
            }
            _pendingMove = false;
            if (!RecordMovements || IsNearMacroPointer(_pendingX, _pendingY))
            {
                return;
            }
            AppendMove(_pendingX, _pendingY, _pendingMs);
        }

        /// <summary>
        /// Adds a move to (<paramref name="x"/>, <paramref name="y"/>), at the current point of
        /// the timeline, unless playback's pointer will already be there.
        /// </summary>
        private void MoveIfElsewhere(int x, int y)
        {
            // Never in a camera take: a jump to a screen position is exactly what it avoids.
            if (_camera || IsNearMacroPointer(x, y))
            {
                return;
            }
            Append(new MacroAction(MacroActionType.MouseMove) { X = x, Y = y });
            _macroPointerX = x;
            _macroPointerY = y;
            _lastMoveX = x;
            _lastMoveY = y;
        }

        private bool IsNearMacroPointer(int x, int y)
        {
            return _macroPointerX != int.MinValue &&
                   Math.Abs(x - _macroPointerX) < MoveDistanceThreshold &&
                   Math.Abs(y - _macroPointerY) < MoveDistanceThreshold;
        }

        private void NoteRealStep(MacroAction step)
        {
            if (_firstRealStep == null)
            {
                _firstRealStep = step;
            }
            _lastRealStep = step;
            _ownClickMs = -1;
        }

        /// <summary>
        /// True when the take ended on Tempo's own window: a click, scroll or key press there
        /// after the last real step, and just before <paramref name="now"/>.
        /// </summary>
        private bool EndedOnTempo(long now)
        {
            if (_ownClickMs < 0)
            {
                return false;
            }
            uint since = unchecked((uint)now - (uint)_ownClickMs);
            // A stamp a hair AFTER now (the two clocks tick together, but are read apart)
            // reads as a huge unsigned value: that is "just now" too.
            return since <= OwnClickEndsTakeMs || since > int.MaxValue;
        }

        /// <summary>
        /// True when the window under the point belongs to Tempo — its main window, a
        /// dialog, the tray menu, an overlay. Asked only for presses and scrolls, never for
        /// movement, so it costs nothing while the pointer is merely moving.
        /// </summary>
        private static bool IsOwnWindowAt(int x, int y)
        {
            try
            {
                IntPtr hwnd = NativeMethods.WindowFromPoint(new NativeMethods.POINT(x, y));
                if (hwnd == IntPtr.Zero)
                {
                    return false;
                }
                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                return pid == OwnProcessId;
            }
            catch
            {
                return false;   // unknown: record it, as before
            }
        }

        /// <summary>True when a press of this button is one of <see cref="ExcludedMouseHotkeys"/>.</summary>
        private bool IsExcludedMouseHotkey(MacroActionType press)
        {
            if (ExcludedMouseHotkeys.Count == 0)
            {
                return false;
            }

            HotkeyMouseButton button =
                press == MacroActionType.LeftDown ? HotkeyMouseButton.Left :
                press == MacroActionType.RightDown ? HotkeyMouseButton.Right :
                press == MacroActionType.X1Down ? HotkeyMouseButton.XButton1 :
                press == MacroActionType.X2Down ? HotkeyMouseButton.XButton2 :
                HotkeyMouseButton.Middle;
            try
            {
                foreach (HotkeyDefinition hk in ExcludedMouseHotkeys)
                {
                    if (hk != null && hk.MouseButton == button &&
                        hk.ModifiersMatch(vk => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Unknown: record it, as before.
            }
            return false;
        }

        /// <summary>True when Tempo's own window has keyboard focus.</summary>
        private static bool IsOwnWindowForeground()
        {
            try
            {
                IntPtr hwnd = NativeMethods.GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                {
                    return false;
                }
                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                return pid == OwnProcessId;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsModifier(int vk)
        {
            switch (vk)
            {
                case 0x10: // VK_SHIFT
                case 0x11: // VK_CONTROL
                case 0x12: // VK_MENU (Alt)
                case 0xA0: // VK_LSHIFT
                case 0xA1: // VK_RSHIFT
                case 0xA2: // VK_LCONTROL
                case 0xA3: // VK_RCONTROL
                case 0xA4: // VK_LMENU
                case 0xA5: // VK_RMENU
                case 0x5B: // VK_LWIN
                case 0x5C: // VK_RWIN
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Remembers which modifier keys are physically down as recording begins.</summary>
        private void NoteModifiersHeldAtStart()
        {
            _heldAtStart.Clear();
            try
            {
                int[] modifiers = { 0x10, 0x11, 0x12, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C };
                foreach (int vk in modifiers)
                {
                    if ((NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0)
                    {
                        _heldAtStart.Add(vk);
                    }
                }
            }
            catch
            {
                // Best effort: without it a held modifier is recorded exactly as before.
            }
        }

        /// <summary>
        /// Removes the presses of modifier keys that are still held and came after the last
        /// step that did anything: the Ctrl / Shift / Alt / Win of the hotkey that is stopping
        /// this recording. Only a hotkey's MAIN key is excluded from a take (its modifiers are
        /// recorded like any other key, so Ctrl+C still records as Ctrl+C), so stopping with
        /// Ctrl+F9 ended every take with a Ctrl press that playback turned into a stray Ctrl
        /// tap at the end of every loop. A modifier pressed before the last real step and still
        /// held is left alone — that one is part of the take, and is released at the end.
        /// Returns how many presses were removed. Under the lock.
        /// </summary>
        private int DropStopChord()
        {
            if (_keysDown.Count == 0)
            {
                return 0;
            }

            int lastReal = _lastRealStep == null ? -1 : LastIndexOfStep(_lastRealStep);
            List<int> dropped = null;
            foreach (KeyValuePair<int, MacroAction> held in _keysDown)
            {
                if (!IsModifier(held.Key))
                {
                    continue;
                }
                int at = LastIndexOfStep(held.Value);
                if (at <= lastReal)
                {
                    continue;
                }
                _macro.Actions.RemoveAt(at);
                (dropped ??= new List<int>()).Add(held.Key);
            }

            if (dropped == null)
            {
                return 0;
            }
            foreach (int vk in dropped)
            {
                _keysDown.Remove(vk);
            }
            return dropped.Count;
        }

        // The reach for Tempo's Stop button is one continuous movement ending on the click.
        // The pointer standing still at least this long marks where it began...
        private const int ReachRestMs = 250;
        // ...and no reach for a button takes longer than this, so movement older than it is
        // kept even when it never paused (a take of mouse circles, say, stopped by button).
        private const long ReachMaxMs = 2000;

        /// <summary>
        /// Removes the pointer's trip to Tempo's Stop button: the unbroken movement at the end
        /// of the take, back to where the pointer last stood still (or at most
        /// <see cref="ReachMaxMs"/> of it), and never past the last real step. Returns the
        /// milliseconds of waiting removed. Under the lock.
        /// </summary>
        private long TrimPointerTail()
        {
            List<MacroAction> a = _macro.Actions;
            int keepThrough = _lastRealStep == null ? -1 : LastIndexOfStep(_lastRealStep);

            // Steps [cut, end) are the reach. Only a move moves the cut: a wait joins the reach
            // once a move before it does.
            int cut = a.Count;
            long motionMs = 0;
            for (int i = a.Count - 1; i > keepThrough; i--)
            {
                MacroAction s = a[i];
                if (IsMovement(s.Type))
                {
                    cut = i;
                    continue;
                }
                if (s.Type != MacroActionType.Delay || s.DelayMilliseconds >= ReachRestMs)
                {
                    break;   // part of the take, or the pointer at rest before the reach
                }
                motionMs += s.DelayMilliseconds;
                if (motionMs > ReachMaxMs)
                {
                    break;
                }
            }

            long removedMs = 0;
            for (int i = a.Count - 1; i >= cut; i--)
            {
                if (a[i].Type == MacroActionType.Delay)
                {
                    removedMs += a[i].DelayMilliseconds;
                }
                a.RemoveAt(i);
            }
            return removedMs;
        }

        /// <summary>
        /// Removes the pointer's trip from Tempo's Record button to the app: the unbroken
        /// movement at the start of the take, up to where the pointer first stood still (or at
        /// most <see cref="ReachMaxMs"/> of it), and never past the first real step — plus the
        /// wait after it, since a take starts on its first action. Returns the steps removed.
        /// Under the lock.
        /// </summary>
        private int TrimPointerLead()
        {
            List<MacroAction> a = _macro.Actions;
            int firstReal = _firstRealStep == null ? a.Count : LastIndexOfStep(_firstRealStep);
            if (firstReal < 0)
            {
                firstReal = a.Count;
            }

            // Steps [0, cut) are the trip. Only a move moves the cut, as in TrimPointerTail.
            int cut = 0;
            long motionMs = 0;
            for (int i = 0; i < firstReal; i++)
            {
                MacroAction s = a[i];
                if (IsMovement(s.Type))
                {
                    cut = i + 1;
                    continue;
                }
                if (s.Type != MacroActionType.Delay || s.DelayMilliseconds >= ReachRestMs)
                {
                    break;
                }
                motionMs += s.DelayMilliseconds;
                if (motionMs > ReachMaxMs)
                {
                    break;
                }
            }

            if (cut == 0)
            {
                return 0;
            }
            while (cut < a.Count && a[cut].Type == MacroActionType.Delay)
            {
                cut++;
            }
            a.RemoveRange(0, cut);
            return cut;
        }

        /// <summary>
        /// Joins waits left side by side by a removal, and takes the waits off the end.
        /// Returns the milliseconds taken off the end. Under the lock.
        /// </summary>
        private long MergeDelaysAndTrimTrailing()
        {
            List<MacroAction> a = _macro.Actions;
            for (int i = a.Count - 1; i > 0; i--)
            {
                if (a[i].Type == MacroActionType.Delay && a[i - 1].Type == MacroActionType.Delay)
                {
                    a[i - 1].DelayMilliseconds = ClampMs((long)a[i - 1].DelayMilliseconds + a[i].DelayMilliseconds);
                    a.RemoveAt(i);
                }
            }

            long trailing = 0;
            while (a.Count > 0 && a[a.Count - 1].Type == MacroActionType.Delay)
            {
                trailing += a[a.Count - 1].DelayMilliseconds;
                a.RemoveAt(a.Count - 1);
            }
            return trailing;
        }

        /// <summary>A movement step: a position (MouseMove) or a camera turn (MouseMoveRelative).</summary>
        private static bool IsMovement(MacroActionType t)
        {
            return t == MacroActionType.MouseMove || t == MacroActionType.MouseMoveRelative;
        }

        /// <summary>Index of this exact step object, searching from the end; -1 if absent.</summary>
        private int LastIndexOfStep(MacroAction step)
        {
            List<MacroAction> a = _macro.Actions;
            for (int i = a.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(a[i], step))
                {
                    return i;
                }
            }
            return -1;
        }

        private static int ClampMs(long ms)
        {
            return (int)Math.Min(Math.Max(ms, 0), int.MaxValue);
        }

        private void AppendDelay(long now)
        {
            lock (_appendLock)
            {
                // Skip the gap between Start() and the first real event entirely.
                // Without this every macro begins with a noticeable pause equal to
                // however long the user took to make their first move.
                if (!_firstEventCaptured)
                {
                    _lastEventMs = now;
                    _firstEventCaptured = true;
                    return;
                }

                // GetTickCount (uint ms) wraps every ~49.7 days; the unsigned difference is
                // still the true small gap across a wrap. An event stamped EARLIER than the
                // one before it — the keyboard and mouse hooks are two separate streams —
                // reads as a huge unsigned value: that is no gap at all, and the timeline
                // stays at the later stamp. It used to be "corrected" by adding 2^32, which
                // put a 49-day wait into the macro.
                uint gap = unchecked((uint)now - (uint)_lastEventMs);
                if (gap > int.MaxValue)
                {
                    return;
                }

                if (gap > 0)
                {
                    Append(new MacroAction(MacroActionType.Delay) { DelayMilliseconds = (int)gap });
                }

                _lastEventMs = now;
            }
        }

        private void Append(MacroAction action)
        {
            lock (_appendLock)
            {
                // Coalesce consecutive delays so the macro never contains two
                // back-to-back Delay steps (which would inflate the step count and
                // confuse the editor's cumulative-time column).
                if (action.Type == MacroActionType.Delay
                    && _macro.Actions.Count > 0
                    && _macro.Actions[_macro.Actions.Count - 1].Type == MacroActionType.Delay)
                {
                    var last = _macro.Actions[_macro.Actions.Count - 1];
                    long combined = (long)last.DelayMilliseconds + action.DelayMilliseconds;
                    last.DelayMilliseconds = (int)Math.Min(combined, int.MaxValue);
                }
                else
                {
                    _macro.Actions.Add(action);
                }
            }

            ActionRecorded?.Invoke(this, action);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MacroRecorder));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (_recording)
            {
                Stop();
            }

            _hook.MouseEvent -= OnMouseEvent;
            _hook.Dispose();
            _keyHook.KeyEvent -= OnKeyEvent;
            _keyHook.Dispose();
            _disposed = true;
        }
    }
}
