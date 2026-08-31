using System;
using System.Threading;
using AutoClicker.Models;
using AutoClicker.Utils;

namespace AutoClicker.Engine
{
    /// <summary>
    /// Plays back a recorded <see cref="Macro"/>, optionally looping. Runs on a
    /// background thread and can be stopped responsively.
    /// </summary>
    public sealed class MacroPlayer : IDisposable
    {
        private readonly object _sync = new object();
        private Thread _worker;
        private ManualResetEventSlim _stopSignal = new ManualResetEventSlim(false);
        private volatile bool _playing;
        private bool _disposed;
        private bool _smooth;
        private bool _preserveHolds;

        // Tracks anything pressed by the currently playing macro. If playback is
        // stopped before a matching Up step is reached, the worker's finally
        // releases everything in these sets so no input stays stuck.
        private readonly System.Collections.Generic.HashSet<MouseButtonType> _pressedButtons
            = new System.Collections.Generic.HashSet<MouseButtonType>();
        private readonly System.Collections.Generic.HashSet<int> _pressedKeys
            = new System.Collections.Generic.HashSet<int>();

        public event EventHandler PlaybackStarted;
        public event EventHandler PlaybackFinished;
        public event EventHandler<int> StepExecuted;

        /// <summary>Raised at the start of each loop with the 1-based loop number.</summary>
        public event EventHandler<int> LoopChanged;

        public bool IsPlaying => _playing;

        /// <summary>
        /// Begins playback. <paramref name="loopCount"/> of 0 means loop forever
        /// until <see cref="Stop"/> is called.
        /// </summary>
        public void Play(Macro macro, int loopCount, double speedMultiplier, int loopDelayMs = 0)
        {
            ThrowIfDisposed();

            if (macro == null || macro.Actions.Count == 0)
            {
                return;
            }

            lock (_sync)
            {
                if (_playing)
                {
                    return;
                }

                Macro snapshot = macro.Clone();
                _smooth = snapshot.SmoothMovement;
                _preserveHolds = snapshot.PreserveKeyHolds;
                double speed = speedMultiplier <= 0 ? 1.0 : speedMultiplier;
                int delay = loopDelayMs < 0 ? 0 : loopDelayMs;
                _stopSignal.Reset();
                _playing = true;
                LastRunCompletedNaturally = true;   // Stop() flips this off.

                _worker = new Thread(() => PlaybackLoop(snapshot, loopCount, speed, delay))
                {
                    IsBackground = true,
                    Name = "AutoClicker.MacroPlayer"
                };
                _worker.Start();
            }

            PlaybackStarted?.Invoke(this, EventArgs.Empty);
            Logger.Info($"[Macro] playback started ('{macro.Name}', loops={loopCount}).");
        }

        /// <summary>
        /// True when the most recent playback ran to the end of its loops rather
        /// than being stopped via <see cref="Stop"/> — lets the UI chime only for
        /// real completions, mirroring the click engine.
        /// </summary>
        public bool LastRunCompletedNaturally { get; private set; }

        public void Stop()
        {
            Thread toJoin = null;
            lock (_sync)
            {
                if (!_playing)
                {
                    return;
                }

                _playing = false;
                LastRunCompletedNaturally = false;
                _stopSignal.Set();
                toJoin = _worker;
            }

            if (toJoin != null && toJoin.IsAlive)
            {
                toJoin.Join(TimeSpan.FromSeconds(2));
            }

            lock (_sync)
            {
                _worker = null;
            }

            Logger.Info("[Macro] playback stopped.");
        }

        private void PlaybackLoop(Macro macro, int loopCount, double speed, int loopDelayMs)
        {
            try
            {
                _pressedButtons.Clear();
                _pressedKeys.Clear();

                int loopsDone = 0;
                bool infinite = loopCount <= 0;

                // ABSOLUTE-TIMELINE scheduling. One monotonic clock runs for the whole
                // playback and "intendedMs" tracks where on that timeline we should be.
                // Each Delay advances intendedMs; we then wait until the real clock
                // catches up to it. The crucial property: the wall-clock cost of every
                // OTHER step (the SendInput syscall, the cross-thread StepExecuted
                // marshal, loop overhead) is absorbed by the FOLLOWING wait — the next
                // wait is simply shorter — instead of being added on top of every delay.
                // Under the old relative scheme that per-step overhead accumulated, so a
                // recorded key-hold (and the whole pace) ran progressively LONGER than
                // recorded — the "WASD movement isn't the right length in-game" report.
                // A double timeline also keeps sub-millisecond accuracy without the old
                // fractional-carry bookkeeping.
                var clock = System.Diagnostics.Stopwatch.StartNew();
                double intendedMs = 0;

                while (_playing && (infinite || loopsDone < loopCount))
                {
                    LoopChanged?.Invoke(this, loopsDone + 1);

                    for (int i = 0; i < macro.Actions.Count && _playing; i++)
                    {
                        ExecuteAction(macro.Actions[i], speed, clock, ref intendedMs);
                        StepExecuted?.Invoke(this, i);
                    }

                    loopsDone++;

                    // Wait between loops (but not after the final one), on the same
                    // timeline so the loop cadence stays exact too.
                    bool moreLoops = _playing && (infinite || loopsDone < loopCount);
                    if (moreLoops && loopDelayMs > 0)
                    {
                        intendedMs += loopDelayMs;
                        if (WaitUntil(clock, ref intendedMs))
                        {
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("[Macro] exception during playback.", ex);
            }
            finally
            {
                // Release anything the macro pressed but did not release —
                // typically because the user stopped playback mid-way through.
                ReleaseHeldInputs();

                _playing = false;
                PlaybackFinished?.Invoke(this, EventArgs.Empty);
            }
        }

        private void ReleaseHeldInputs()
        {
            foreach (MouseButtonType b in _pressedButtons)
            {
                try { InputSimulator.ButtonUp(b); } catch { /* best effort */ }
            }
            _pressedButtons.Clear();

            foreach (int vk in _pressedKeys)
            {
                try { InputSimulator.KeyUp(vk); } catch { /* best effort */ }
            }
            _pressedKeys.Clear();
        }

        /// <summary>
        /// Moves the cursor to (x, y) in small interpolated steps for natural,
        /// human-like motion. Honors the stop signal so playback stays responsive.
        /// </summary>
        private void SmoothMoveTo(int x, int y)
        {
            // Always start from where the real cursor is right now, so the glide
            // continues naturally from the user's actual pointer position.
            int startX, startY;
            if (!ScreenGeometry.TryGetCursorPosition(out startX, out startY))
            {
                InputSimulator.MoveTo(x, y);
                return;
            }

            double dx = x - startX;
            double dy = y - startY;
            double distance = Math.Sqrt(dx * dx + dy * dy);

            // Very short hops aren't worth animating.
            if (distance < 3)
            {
                InputSimulator.MoveTo(x, y);
                return;
            }

            // Time-based motion: glide duration scales with distance so the cursor
            // travels at a roughly constant, natural speed regardless of how far it
            // has to go. ~1.1 ms per pixel, clamped to a comfortable range.
            double durationMs = distance * 1.1;
            if (durationMs < 70) durationMs = 70;
            if (durationMs > 650) durationMs = 650;

            // ~7 ms per frame gives a fluid ~140 fps update without busy-spinning.
            const int frameMs = 7;
            int steps = (int)Math.Round(durationMs / frameMs);
            if (steps < 3) steps = 3;

            for (int i = 1; i <= steps; i++)
            {
                if (_stopSignal.IsSet)
                {
                    break;
                }

                // Cosine ease-in-out: gentle acceleration and deceleration.
                double t = (double)i / steps;
                double eased = 0.5 - 0.5 * Math.Cos(t * Math.PI);
                int ix = (int)Math.Round(startX + dx * eased);
                int iy = (int)Math.Round(startY + dy * eased);
                InputSimulator.MoveTo(ix, iy);
                _stopSignal.Wait(frameMs);
            }

            if (!_stopSignal.IsSet)
            {
                InputSimulator.MoveTo(x, y); // land exactly on target
            }
        }

        // How far the real clock may fall behind the intended timeline before we drop the
        // backlog instead of trying to catch up. Without this, a GC / scheduler hitch
        // would make the next several waits compute as "already due" and fire a burst of
        // inputs back-to-back (e.g. a KeyUp immediately after its KeyDown = no movement).
        // 250 ms is far beyond any normal hitch yet small enough to keep playback in sync.
        private const double CatchUpCapMs = 250.0;

        /// <summary>
        /// Waits (interruptibly) until the monotonic <paramref name="clock"/> reaches
        /// <paramref name="intendedMs"/> on the absolute playback timeline. Returns true
        /// if the stop signal fired. If we're already past the target only slightly, it
        /// returns immediately (the next wait naturally absorbs the small overshoot); if
        /// we're past it by more than <see cref="CatchUpCapMs"/>, the timeline is
        /// re-anchored to "now" so a hitch can't trigger a burst of catch-up inputs.
        /// </summary>
        private bool WaitUntil(System.Diagnostics.Stopwatch clock, ref double intendedMs)
        {
            double now = clock.Elapsed.TotalMilliseconds;
            double remaining = intendedMs - now;
            if (remaining <= 0)
            {
                if (now - intendedMs > CatchUpCapMs)
                {
                    intendedMs = now;
                }
                return !_playing;
            }
            return PreciseWait.Wait(remaining, _stopSignal);
        }

        private void ExecuteAction(MacroAction action, double speed, System.Diagnostics.Stopwatch clock, ref double intendedMs)
        {
            switch (action.Type)
            {
                case MacroActionType.Delay:
                    // Normally every delay scales with the speed multiplier. But when
                    // "preserve key holds" is on and something is currently held down, this
                    // delay IS the hold duration (recorded as KeyDown → Delay → KeyUp), so
                    // play it at its real length — otherwise 2x/4x turns a movement hold into
                    // a tap. Gaps with nothing held still scale.
                    double add = action.DelayMilliseconds;
                    bool holding = _pressedKeys.Count > 0 || _pressedButtons.Count > 0;
                    if (!(_preserveHolds && holding))
                    {
                        add /= speed;
                    }
                    intendedMs += Math.Max(0, add);
                    WaitUntil(clock, ref intendedMs);
                    break;

                case MacroActionType.MouseMove:
                    if (_smooth)
                    {
                        SmoothMoveTo(action.X, action.Y);
                    }
                    else
                    {
                        InputSimulator.MoveTo(action.X, action.Y);
                    }
                    break;

                case MacroActionType.LeftDown:
                    InputSimulator.MoveTo(action.X, action.Y);
                    InputSimulator.ButtonDown(MouseButtonType.Left);
                    _pressedButtons.Add(MouseButtonType.Left);
                    break;
                case MacroActionType.LeftUp:
                    InputSimulator.ButtonUp(MouseButtonType.Left);
                    _pressedButtons.Remove(MouseButtonType.Left);
                    break;

                case MacroActionType.RightDown:
                    InputSimulator.MoveTo(action.X, action.Y);
                    InputSimulator.ButtonDown(MouseButtonType.Right);
                    _pressedButtons.Add(MouseButtonType.Right);
                    break;
                case MacroActionType.RightUp:
                    InputSimulator.ButtonUp(MouseButtonType.Right);
                    _pressedButtons.Remove(MouseButtonType.Right);
                    break;

                case MacroActionType.MiddleDown:
                    InputSimulator.MoveTo(action.X, action.Y);
                    InputSimulator.ButtonDown(MouseButtonType.Middle);
                    _pressedButtons.Add(MouseButtonType.Middle);
                    break;
                case MacroActionType.MiddleUp:
                    InputSimulator.ButtonUp(MouseButtonType.Middle);
                    _pressedButtons.Remove(MouseButtonType.Middle);
                    break;

                case MacroActionType.Wheel:
                    InputSimulator.Wheel(action.WheelDelta);
                    break;

                case MacroActionType.KeyDown:
                    InputSimulator.KeyDown(action.VirtualKey);
                    _pressedKeys.Add(action.VirtualKey);
                    break;

                case MacroActionType.KeyUp:
                    InputSimulator.KeyUp(action.VirtualKey);
                    _pressedKeys.Remove(action.VirtualKey);
                    break;

                case MacroActionType.Script:
                    RunScriptStep(action, clock, ref intendedMs);
                    break;
            }
        }

        /// <summary>
        /// Runs a Script step, then puts the playback timeline back where the script left
        /// the clock.
        ///
        /// THE TIMELINE MATTERS. Every other step is effectively instant, so playback
        /// keeps an intended-time cursor and waits until the clock catches up. A script is
        /// the one step that takes real, unpredictable time — so afterwards the cursor is
        /// re-anchored to now. Without that, a 3-second script leaves every subsequent
        /// step "already due" and WaitUntil fires the whole rest of the macro back-to-back
        /// as fast as the input layer accepts it.
        ///
        /// KEYS ARE RELEASED FIRST. A script can take seconds and can steal focus; leaving
        /// W held down across it means the game keeps walking the entire time, and if the
        /// script fails and playback stops, the key is never released at all.
        /// </summary>
        private void RunScriptStep(MacroAction action, System.Diagnostics.Stopwatch clock, ref double intendedMs)
        {
            ReleaseHeldInputs();

            Utils.PythonRunner.Result r;
            try
            {
                r = Utils.PythonRunner.Run(action.ScriptPath, action.ScriptTimeoutMs, _stopSignal);
            }
            catch (Exception ex)
            {
                // PythonRunner is written not to throw; this is the belt to its braces,
                // because an exception escaping here kills the playback thread.
                Utils.Logger.Swallow("MacroPlayer.RunScriptStep", ex);
                intendedMs = clock.Elapsed.TotalMilliseconds;
                return;
            }

            intendedMs = clock.Elapsed.TotalMilliseconds;

            string name = action.ScriptFileName();
            if (!string.IsNullOrEmpty(r.Output))
            {
                Utils.Logger.Info("[Python] " + name + " output:" + Environment.NewLine + r.Output);
            }

            if (r.Succeeded)
            {
                Utils.Logger.Info("[Python] " + name + " finished in " + r.ElapsedMs + " ms.");
                return;
            }

            // A stop is not a failure — the user asked for it, and OnFailure must not
            // turn "I pressed Stop" into "your script is broken".
            if (r.Outcome == Utils.PythonRunner.Outcome.Stopped)
            {
                Utils.Logger.Info("[Python] " + name + " was stopped with the macro.");
                return;
            }

            Utils.Logger.Warn("[Python] " + name + " failed (" + r.Outcome + "): " + r.Detail);
            ScriptFailed?.Invoke(this, new ScriptFailure(action, r));

            if (action.ScriptOnFailure == ScriptFailureAction.StopMacro)
            {
                _playing = false;
                _stopSignal.Set();
            }
        }

        /// <summary>
        /// Details of a Script step that did not succeed, for the UI to report. Internal
        /// rather than public, to match PythonRunner.Result — this is Tempo talking to
        /// itself, not an API surface.
        /// </summary>
        internal sealed class ScriptFailure : EventArgs
        {
            internal ScriptFailure(MacroAction action, Utils.PythonRunner.Result result)
            {
                Action = action;
                Result = result;
            }

            internal MacroAction Action { get; }
            internal Utils.PythonRunner.Result Result { get; }
        }

        /// <summary>
        /// Raised when a Script step fails. The player only decides whether to keep
        /// playing; TELLING the user is the UI's job, and it happens on a background
        /// thread, so the handler must marshal.
        /// </summary>
        internal event EventHandler<ScriptFailure> ScriptFailed;

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MacroPlayer));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Stop();
            _stopSignal.Dispose();
            _disposed = true;
        }
    }
}
