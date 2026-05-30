using System;
using System.Diagnostics;
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
    /// </summary>
    public sealed class MacroRecorder : IDisposable
    {
        private readonly LowLevelMouseHook _hook = new LowLevelMouseHook();
        private readonly LowLevelKeyboardHook _keyHook = new LowLevelKeyboardHook();
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private Macro _macro;
        private long _lastEventMs;
        private bool _recording;
        private bool _disposed;
        private readonly object _appendLock = new object();

        // Track which keys are currently held so auto-repeat key-down messages are
        // collapsed into a single down/up pair in the recording.
        private readonly System.Collections.Generic.HashSet<int> _keysDown
            = new System.Collections.Generic.HashSet<int>();

        // Track which mouse buttons the recorder has seen go down, so we can
        // synthesize the matching up event at Stop() if a recording ends while a
        // button is still held. Without this, playback would leave the button
        // pressed.
        private readonly System.Collections.Generic.HashSet<MacroActionType> _buttonsDown
            = new System.Collections.Generic.HashSet<MacroActionType>();

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

        public bool RecordMovements { get; set; } = true;

        /// <summary>When true, key presses are captured in addition to the mouse.</summary>
        public bool RecordKeyboard { get; set; } = true;

        /// <summary>
        /// Virtual-key codes to ignore while recording (typically the keys used
        /// to toggle recording and to emergency-stop). Default empty.
        /// </summary>
        public System.Collections.Generic.HashSet<int> ExcludedVirtualKeys { get; }
            = new System.Collections.Generic.HashSet<int>();

        public bool IsRecording => _recording;

        public event EventHandler<MacroAction> ActionRecorded;

        public MacroRecorder()
        {
            _hook.MouseEvent += OnMouseEvent;
            _keyHook.KeyEvent += OnKeyEvent;
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
            _keysDown.Clear();
            _buttonsDown.Clear();
            _firstEventCaptured = false;

            if (!_hook.Start())
            {
                Logger.Error("Macro recorder could not install the mouse hook.");
                return false;
            }

            if (RecordKeyboard)
            {
                if (!_keyHook.Start())
                {
                    // Non-fatal: continue recording mouse only.
                    Logger.Warn("Keyboard hook failed; recording mouse only.");
                }
            }

            _stopwatch.Restart();
            _recording = true;
            Logger.Info("Macro recording started.");
            return true;
        }

        /// <summary>Stops recording and returns the captured macro.</summary>
        public Macro Stop()
        {
            if (!_recording)
            {
                return _macro;
            }

            _recording = false;

            // Synthesize Up events for any keys or mouse buttons still tracked as
            // held, so a played-back macro ends with no stuck inputs in the
            // target application.
            long now = _stopwatch.ElapsedMilliseconds;
            lock (_appendLock)
            {
                foreach (int vk in _keysDown)
                {
                    AppendDelay(now);
                    Append(new MacroAction(MacroActionType.KeyUp) { VirtualKey = vk });
                }
                _keysDown.Clear();

                foreach (MacroActionType downType in _buttonsDown)
                {
                    MacroActionType upType = PairedUp(downType);
                    AppendDelay(now);
                    // X/Y reuse the most recent recorded location.
                    Append(new MacroAction(upType) { X = _lastMoveX, Y = _lastMoveY });
                }
                _buttonsDown.Clear();
            }

            _hook.Stop();
            _keyHook.Stop();
            _stopwatch.Stop();
            Logger.Info($"Macro recording stopped ({(_macro?.StepCount ?? 0)} steps).");
            return _macro;
        }

        private void OnKeyEvent(object sender, KeyboardHookEventArgs e)
        {
            if (!_recording || _macro == null || !RecordKeyboard)
            {
                return;
            }

            // Skip keys reserved for control (e.g. the toggle-record and
            // emergency-stop hotkeys, plus their modifiers).
            if (ExcludedVirtualKeys.Contains(e.VirtualKey))
            {
                return;
            }

            long now = _stopwatch.ElapsedMilliseconds;

            lock (_appendLock)
            {
                if (e.IsKeyDown)
                {
                    // Collapse OS auto-repeat into a single down event.
                    if (_keysDown.Contains(e.VirtualKey))
                    {
                        return;
                    }
                    _keysDown.Add(e.VirtualKey);

                    AppendDelay(now);
                    Append(new MacroAction(MacroActionType.KeyDown) { VirtualKey = e.VirtualKey });
                }
                else
                {
                    if (!_keysDown.Remove(e.VirtualKey))
                    {
                        // A key-up with no matching down (pressed before recording);
                        // record it anyway so held keys are released on playback.
                    }

                    AppendDelay(now);
                    Append(new MacroAction(MacroActionType.KeyUp) { VirtualKey = e.VirtualKey });
                }
            }
        }

        private void OnMouseEvent(object sender, MouseHookEventArgs e)
        {
            if (!_recording || _macro == null)
            {
                return;
            }

            long now = _stopwatch.ElapsedMilliseconds;

            if (e.Type == MouseHookEventType.Move)
            {
                if (!RecordMovements)
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
                    return;
                }

                _lastMoveX = e.X;
                _lastMoveY = e.Y;
                _lastMoveMs = now;

                AppendDelay(now);
                Append(new MacroAction(MacroActionType.MouseMove) { X = e.X, Y = e.Y });
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
                _ => (MacroActionType?)null
            };

            if (type == null)
            {
                return;
            }

            AppendDelay(now);

            // Track button-down / button-up transitions so a recording that ends
            // while a button is held is repaired in Stop().
            switch (type.Value)
            {
                case MacroActionType.LeftDown:
                case MacroActionType.RightDown:
                case MacroActionType.MiddleDown:
                    _buttonsDown.Add(type.Value);
                    break;
                case MacroActionType.LeftUp:
                    _buttonsDown.Remove(MacroActionType.LeftDown);
                    break;
                case MacroActionType.RightUp:
                    _buttonsDown.Remove(MacroActionType.RightDown);
                    break;
                case MacroActionType.MiddleUp:
                    _buttonsDown.Remove(MacroActionType.MiddleDown);
                    break;
            }

            var action = new MacroAction(type.Value) { X = e.X, Y = e.Y };
            if (type.Value == MacroActionType.Wheel)
            {
                action.WheelDelta = e.WheelDelta;
            }

            Append(action);
        }

        private void AppendDelay(long now)
        {
            lock (_appendLock)
            {
                long delta = now - _lastEventMs;

                // Skip the gap between Start() and the first real event entirely.
                // Without this every macro begins with a noticeable pause equal to
                // however long the user took to make their first move.
                if (!_firstEventCaptured)
                {
                    _lastEventMs = now;
                    _firstEventCaptured = true;
                    return;
                }

                if (delta > 0)
                {
                    Append(new MacroAction(MacroActionType.Delay) { DelayMilliseconds = (int)Math.Min(delta, int.MaxValue) });
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

        /// <summary>
        /// Returns the Up action type that pairs with a Down action type. Used by
        /// Stop() to synthesize releases for buttons that were still held when the
        /// recording ended.
        /// </summary>
        private static MacroActionType PairedUp(MacroActionType downType)
        {
            switch (downType)
            {
                case MacroActionType.LeftDown: return MacroActionType.LeftUp;
                case MacroActionType.RightDown: return MacroActionType.RightUp;
                case MacroActionType.MiddleDown: return MacroActionType.MiddleUp;
                default: return MacroActionType.LeftUp;
            }
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
