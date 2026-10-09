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

        // Whether the latest movement step was a camera turn (MouseMoveRelative) rather than a
        // screen position. While it is, presses happen where the pointer already is: in a game
        // with mouse-look the pointer is locked, and a jump to a stored position would reach
        // the game as a screen coordinate it can read as a huge turn. Playback thread only.
        private bool _relativeMode;

        // Tracks anything pressed by the currently playing macro. If playback is
        // stopped before a matching Up step is reached, the worker's finally
        // releases everything in these sets so no input stays stuck.
        private readonly System.Collections.Generic.HashSet<MouseButtonType> _pressedButtons
            = new System.Collections.Generic.HashSet<MouseButtonType>();
        private readonly System.Collections.Generic.HashSet<int> _pressedKeys
            = new System.Collections.Generic.HashSet<int>();
        // Side buttons held down: 1 = X1 (Back), 2 = X2 (Forward).
        private readonly System.Collections.Generic.HashSet<int> _pressedXButtons
            = new System.Collections.Generic.HashSet<int>();

        public event EventHandler PlaybackStarted;
        public event EventHandler PlaybackFinished;
        public event EventHandler<int> StepExecuted;

        /// <summary>Raised at the start of each loop with the 1-based loop number.</summary>
        public event EventHandler<int> LoopChanged;

        public bool IsPlaying => _playing;

        /// <summary>
        /// True while a run is playing OR a stopped run's thread has not yet returned from
        /// the step it was in. <see cref="Stop"/> gives the worker 2 s and then returns
        /// regardless, so <see cref="IsPlaying"/> can read false while the old thread is still
        /// alive. Starting a new run in that window used to put two workers on the same
        /// fields: <see cref="Play"/> reset the shared stop signal and set the shared playing
        /// flag, so the OLD worker carried on with its own macro beside the new one, both
        /// pressing keys from one unsynchronised set. Callers check this before they start
        /// anything; <see cref="Play"/> refuses on its own as well.
        /// </summary>
        public bool IsBusy
        {
            get
            {
                if (_playing) { return true; }
                Thread w = _worker;
                return w != null && w.IsAlive;
            }
        }

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

                if (_worker != null && _worker.IsAlive)
                {
                    Logger.Warn("[Macro] the previous playback is still finishing its last step; '" +
                                macro.Name + "' was not started on top of it.");
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

            bool exited = true;
            if (toJoin != null && toJoin.IsAlive && !ReferenceEquals(toJoin, Thread.CurrentThread))
            {
                exited = toJoin.Join(TimeSpan.FromSeconds(2));
            }

            lock (_sync)
            {
                // Forget the worker only once it has really gone. Clearing it after a timed-out
                // join is what let the next Play start a second worker beside it (see IsBusy).
                if (exited && ReferenceEquals(_worker, toJoin))
                {
                    _worker = null;
                }
            }

            if (exited)
            {
                Logger.Info("[Macro] playback stopped.");
            }
            else
            {
                Logger.Warn("[Macro] playback was told to stop, but its current step has not returned " +
                            "after 2 s; it stops as soon as that step does, and nothing new plays until then.");
            }
        }

        private void PlaybackLoop(Macro macro, int loopCount, double speed, int loopDelayMs)
        {
            try
            {
                _pressedButtons.Clear();
                _pressedKeys.Clear();
                _pressedXButtons.Clear();
                _relativeMode = false;

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

                _scriptMacroName = macro.Name ?? "";
                System.Collections.Generic.List<MacroAction> actions = macro.Actions;

                // Whether the wait just before the step about to run has already glided the
                // pointer to it (see WaitGliding). A Move with no such wait in front of it —
                // none at all, or one too short to animate — decides for itself.
                bool arrived = false;

                while (_playing && (infinite || loopsDone < loopCount))
                {
                    LoopChanged?.Invoke(this, loopsDone + 1);
                    _scriptLoop = loopsDone + 1;

                    // Another loop follows with no pause between, so the step after the last
                    // one is the first one again — a trailing Delay glides towards it.
                    bool wrapsStraightOn = loopDelayMs <= 0 && (infinite || loopsDone + 1 < loopCount);

                    for (int i = 0; i < actions.Count && _playing; i++)
                    {
                        _scriptStep = i + 1;
                        MacroAction action = actions[i];
                        MacroAction next = i + 1 < actions.Count ? actions[i + 1]
                                         : wrapsStraightOn ? actions[0] : null;
                        arrived = ExecuteAction(action, next, arrived, speed, clock, ref intendedMs);
                        StepExecuted?.Invoke(this, i);
                    }

                    loopsDone++;

                    // Wait between loops (but not after the final one), on the same
                    // timeline so the loop cadence stays exact too.
                    bool moreLoops = _playing && (infinite || loopsDone < loopCount);
                    if (moreLoops && loopDelayMs > 0)
                    {
                        intendedMs += loopDelayMs;
                        arrived = false;
                        // The pause between loops is a wait like any other.
                        bool stopped = actions.Count > 0
                            ? WaitBefore(actions[0], clock, ref intendedMs, out arrived)
                            : WaitUntil(clock, ref intendedMs);
                        if (stopped)
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

            foreach (int which in _pressedXButtons)
            {
                try { InputSimulator.XButtonUp(which); } catch { /* best effort */ }
            }
            _pressedXButtons.Clear();
        }

        // ── Smooth movement ─────────────────────────────────────────────────────
        //
        // A glide used to be something a Move step DID: 70–650 ms of animation added on
        // top of the macro. Fine for a hand-built "move here, then there" macro; ruinous for
        // a recording, whose path is already a Move every ~16 ms with a Delay between each —
        // every one of those became its own 70 ms glide, so a one-second sweep played for
        // four and dragged everything after it late. And a macro recorded WITHOUT movement
        // has no Move steps at all, so "Smooth mouse movement" did nothing for it: every
        // click still teleported.
        //
        // Now the glide happens INSIDE the wait in front of a Move or a press: the pointer
        // idles, then travels so that it arrives exactly when that step is due. Recorded
        // timing is kept, recorded paths still come out as recorded, and click-only macros
        // glide between their clicks. Only a Move with no wait in front of it still takes
        // time of its own, which is the hand-built case the feature started with.

        /// <summary>~140 pointer updates a second while gliding.</summary>
        private const double GlideFrameMs = 7.0;

        /// <summary>A wait too short for two frames is not worth animating.</summary>
        private const double MinGlideMs = 2 * GlideFrameMs;

        /// <summary>
        /// A Move with no wait in front of it glides only when it is a real jump. Shorter
        /// hops are a recording's own samples — a move logged in the same clock tick as a
        /// click, say — and giving each of those its own 70 ms glide is what made smooth
        /// playback of recorded paths crawl.
        /// </summary>
        private const double MinInsertedGlidePixels = 40;

        /// <summary>
        /// How long a glide over <paramref name="distance"/> pixels takes: ~1.1 ms per pixel,
        /// so the pointer moves at a roughly constant, natural speed, kept within 70–650 ms.
        /// </summary>
        private static double GlideDurationMs(double distance)
        {
            double ms = distance * 1.1;
            if (ms < 70) ms = 70;
            if (ms > 650) ms = 650;
            return ms;
        }

        /// <summary>Cosine ease-in-out: gentle acceleration and deceleration.</summary>
        private static double Ease(double t)
        {
            return 0.5 - 0.5 * Math.Cos(t * Math.PI);
        }

        /// <summary>
        /// The steps whose position playback actually moves the pointer to: moves, and
        /// presses — unless a camera turn came last, when a press stays where the pointer is.
        /// </summary>
        private bool MovesPointer(MacroActionType t)
        {
            return t == MacroActionType.MouseMove || (MacroAction.IsButtonPress(t) && !_relativeMode);
        }

        /// <summary>
        /// A wait before <paramref name="next"/>: spreads a camera turn over it, glides towards
        /// a position when smooth movement is on, or just waits. <paramref name="done"/> says the
        /// next step's movement has already happened inside the wait.
        /// </summary>
        private bool WaitBefore(MacroAction next, System.Diagnostics.Stopwatch clock, ref double intendedMs, out bool done)
        {
            done = false;
            if (next != null && next.Type == MacroActionType.MouseMoveRelative)
            {
                return WaitSpreading(next, clock, ref intendedMs, out done);
            }
            if (_smooth)
            {
                return WaitGliding(next, clock, ref intendedMs, out done);
            }
            return WaitUntil(clock, ref intendedMs);
        }

        // ── Camera turns ────────────────────────────────────────────────────────
        //
        // A recording samples the mouse about once a clock tick (~16 ms) and each camera step
        // carries everything the mouse moved in that time. Sent as one jump, playback would
        // turn the camera in 60 steps a second — visibly jerky at a game's frame rate. The
        // movement really happened across the wait in front of the step, so that is where it
        // is sent: in slices every few milliseconds, the last one landing exactly when the
        // step is due. Always, not only with smooth movement: it is how the turn was made,
        // not a flourish, and the slices add up to exactly the recorded counts.

        /// <summary>One slice of a camera turn every ~4 ms (~250 a second).</summary>
        private const double SpreadSliceMs = 4.0;

        private bool WaitSpreading(MacroAction next, System.Diagnostics.Stopwatch clock, ref double intendedMs, out bool sent)
        {
            sent = false;
            int totalX = next.X, totalY = next.Y;
            double start = clock.Elapsed.TotalMilliseconds;
            double budget = intendedMs - start;
            if ((totalX == 0 && totalY == 0) || budget < 2 * SpreadSliceMs)
            {
                return WaitUntil(clock, ref intendedMs);   // nothing to spread, or no time: the step sends it
            }

            int slices = Math.Max(2, (int)Math.Round(budget / SpreadSliceMs));
            int sentX = 0, sentY = 0;
            for (int k = 1; k <= slices; k++)
            {
                if (WaitUntilTime(clock, start + budget * k / slices))
                {
                    return true;
                }
                // Rounded against the running total, never slice by slice, so the slices add
                // up to exactly the recorded counts.
                int wantX = (int)Math.Round((double)totalX * k / slices);
                int wantY = (int)Math.Round((double)totalY * k / slices);
                InputSimulator.MoveRelative(wantX - sentX, wantY - sentY);
                sentX = wantX;
                sentY = wantY;
            }
            sent = true;
            return WaitUntil(clock, ref intendedMs);
        }

        /// <summary>
        /// Moves the pointer from wherever it is to (<paramref name="tx"/>, <paramref name="ty"/>)
        /// over <paramref name="durationMs"/>, starting at <paramref name="startMs"/> on the
        /// playback clock. Frames are placed on that clock rather than slept one after
        /// another, so the glide lands on time. Returns true if playback was stopped.
        /// </summary>
        private bool Glide(int tx, int ty, double startMs, double durationMs, System.Diagnostics.Stopwatch clock)
        {
            // Read the start position only now: after a long idle the pointer may not be
            // where it was when the glide was planned, and gliding from a stale point would
            // open with a jump.
            if (!ScreenGeometry.TryGetCursorPosition(out int sx, out int sy))
            {
                InputSimulator.MoveTo(tx, ty);
                return !_playing;
            }

            double dx = tx - sx;
            double dy = ty - sy;
            int frames = Math.Max(2, (int)Math.Round(durationMs / GlideFrameMs));
            for (int k = 1; k <= frames; k++)
            {
                if (WaitUntilTime(clock, startMs + durationMs * k / frames))
                {
                    return true;
                }
                double eased = Ease((double)k / frames);
                InputSimulator.MoveTo((int)Math.Round(sx + dx * eased), (int)Math.Round(sy + dy * eased));
            }
            return !_playing;
        }

        /// <summary>
        /// A Move with no wait in front of it: glides there in time of its own, as the
        /// feature always did for hand-built macros. Hops shorter than
        /// <see cref="MinInsertedGlidePixels"/> just go. Returns true when it glided.
        /// </summary>
        private bool SmoothMoveTo(int x, int y, System.Diagnostics.Stopwatch clock)
        {
            ScreenGeometry.Clamp(ref x, ref y);
            if (!ScreenGeometry.TryGetCursorPosition(out int startX, out int startY))
            {
                InputSimulator.MoveTo(x, y);
                return false;
            }

            double dx = x - startX;
            double dy = y - startY;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < MinInsertedGlidePixels)
            {
                InputSimulator.MoveTo(x, y);
                return false;
            }

            if (!Glide(x, y, clock.Elapsed.TotalMilliseconds, GlideDurationMs(distance), clock))
            {
                InputSimulator.MoveTo(x, y);   // land exactly on target
            }
            return true;
        }

        /// <summary>
        /// Waits until <paramref name="intendedMs"/> like <see cref="WaitUntil"/>, but when the
        /// step after the wait moves the pointer, spends the END of the wait gliding there so
        /// it arrives exactly when that step is due. <paramref name="arrived"/> says whether the
        /// pointer is now at that step's position. Returns true if playback was stopped.
        /// </summary>
        private bool WaitGliding(MacroAction next, System.Diagnostics.Stopwatch clock, ref double intendedMs,
                                 out bool arrived)
        {
            arrived = false;
            if (next == null || !MovesPointer(next.Type))
            {
                return WaitUntil(clock, ref intendedMs);
            }

            // Less than two frames left (a zero delay, a recording's densest samples, or a
            // playback running late): nothing to glide in, so the step decides for itself.
            double budget = intendedMs - clock.Elapsed.TotalMilliseconds;
            if (budget < MinGlideMs ||
                !ScreenGeometry.TryGetCursorPosition(out int sx, out int sy))
            {
                return WaitUntil(clock, ref intendedMs);
            }

            int tx = next.X, ty = next.Y;
            ScreenGeometry.Clamp(ref tx, ref ty);
            double distance = Math.Sqrt((double)(tx - sx) * (tx - sx) + (double)(ty - sy) * (ty - sy));
            if (distance < 3)
            {
                arrived = true;   // already there
                return WaitUntil(clock, ref intendedMs);
            }

            double duration = Math.Min(GlideDurationMs(distance), budget);
            double start = intendedMs - duration;
            if (WaitUntilTime(clock, start) || Glide(tx, ty, start, duration, clock))
            {
                return true;
            }
            arrived = true;
            return WaitUntil(clock, ref intendedMs);
        }

        /// <summary>
        /// Waits (interruptibly) until the playback clock reaches <paramref name="targetMs"/>,
        /// without the re-anchoring <see cref="WaitUntil"/> does — for points INSIDE a wait,
        /// whose end is still the real deadline. Returns true if playback was stopped.
        /// </summary>
        private bool WaitUntilTime(System.Diagnostics.Stopwatch clock, double targetMs)
        {
            double remaining = targetMs - clock.Elapsed.TotalMilliseconds;
            if (remaining <= 0)
            {
                return !_playing;
            }
            return PreciseWait.Wait(remaining, _stopSignal) || !_playing;
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

        /// <param name="next">The step that runs after this one (null at the very end) — a
        /// Delay glides towards it when smooth movement is on.</param>
        /// <param name="arrived">True when the wait just before this step has already glided
        /// the pointer here.</param>
        /// <returns>True when this step was a wait that left the pointer where
        /// <paramref name="next"/> wants it.</returns>
        private bool ExecuteAction(MacroAction action, MacroAction next, bool arrived, double speed,
                                   System.Diagnostics.Stopwatch clock, ref double intendedMs)
        {
            bool pointerReady = false;
            switch (action.Type)
            {
                case MacroActionType.Delay:
                    // Normally every delay scales with the speed multiplier. But when
                    // "preserve key holds" is on and something is currently held down, this
                    // delay IS the hold duration (recorded as KeyDown → Delay → KeyUp), so
                    // play it at its real length — otherwise 2x/4x turns a movement hold into
                    // a tap. Gaps with nothing held still scale.
                    double add = action.DelayMilliseconds;
                    bool holding = _pressedKeys.Count > 0 || _pressedButtons.Count > 0 || _pressedXButtons.Count > 0;
                    if (!(_preserveHolds && holding))
                    {
                        add /= speed;
                    }
                    intendedMs += Math.Max(0, add);
                    WaitBefore(next, clock, ref intendedMs, out pointerReady);
                    break;

                case MacroActionType.MouseMoveRelative:
                    _relativeMode = true;
                    if (!arrived)   // not already sent in slices during the wait before it
                    {
                        InputSimulator.MoveRelative(action.X, action.Y);
                    }
                    break;

                case MacroActionType.MouseMove:
                    _relativeMode = false;
                    if (_smooth && !arrived)
                    {
                        if (SmoothMoveTo(action.X, action.Y, clock))
                        {
                            // A glide with no wait to happen in takes time the macro never had,
                            // so the timeline moves on by it. Left behind, the wait after it was
                            // eaten instead: "move, wait 400 ms, click" lost its wait to the move
                            // and then had none left to glide to the click in.
                            intendedMs = Math.Max(intendedMs, clock.Elapsed.TotalMilliseconds);
                        }
                    }
                    else
                    {
                        InputSimulator.MoveTo(action.X, action.Y);
                    }
                    break;

                case MacroActionType.LeftDown:
                    MoveToPress(action);
                    InputSimulator.ButtonDown(MouseButtonType.Left);
                    _pressedButtons.Add(MouseButtonType.Left);
                    break;
                case MacroActionType.LeftUp:
                    InputSimulator.ButtonUp(MouseButtonType.Left);
                    _pressedButtons.Remove(MouseButtonType.Left);
                    break;

                case MacroActionType.RightDown:
                    MoveToPress(action);
                    InputSimulator.ButtonDown(MouseButtonType.Right);
                    _pressedButtons.Add(MouseButtonType.Right);
                    break;
                case MacroActionType.RightUp:
                    InputSimulator.ButtonUp(MouseButtonType.Right);
                    _pressedButtons.Remove(MouseButtonType.Right);
                    break;

                case MacroActionType.MiddleDown:
                    MoveToPress(action);
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

                // Side buttons act on the window under the pointer, like any other button
                // (X1 over a browser is Back), so the press goes where it was recorded.
                case MacroActionType.X1Down:
                case MacroActionType.X2Down:
                {
                    int which = action.Type == MacroActionType.X2Down ? 2 : 1;
                    MoveToPress(action);
                    InputSimulator.XButtonDown(which);
                    _pressedXButtons.Add(which);
                    break;
                }
                case MacroActionType.X1Up:
                case MacroActionType.X2Up:
                {
                    int which = action.Type == MacroActionType.X2Up ? 2 : 1;
                    InputSimulator.XButtonUp(which);
                    _pressedXButtons.Remove(which);
                    break;
                }

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
            return pointerReady;
        }

        /// <summary>
        /// Takes the pointer to a press's recorded position — except after a camera turn,
        /// when the press happens where the pointer is (see <see cref="_relativeMode"/>).
        /// </summary>
        private void MoveToPress(MacroAction press)
        {
            if (!_relativeMode)
            {
                InputSimulator.MoveTo(press.X, press.Y);
            }
        }

        // Where playback is, for the Script step's environment (TEMPO_MACRO, TEMPO_STEP,
        // TEMPO_LOOP). Written only by the playback thread, read only by it.
        private string _scriptMacroName = "";
        private int _scriptStep;
        private int _scriptLoop;

        /// <summary>
        /// How many lines of one script's output are written to the log AS THEY ARRIVE.
        /// Enough to follow a script's progress in Live debug; a chatty loop printing every
        /// frame must not push everything else out of a 1,500-line ring. The full output (up
        /// to its own cap) still comes back in the result for the failure report.
        /// </summary>
        private const int ScriptLinesLoggedLive = 200;

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

            string name = action.ScriptFileName();
            int logged = 0;
            var options = new Utils.PythonRunner.RunOptions
            {
                // Live, so a long script shows its progress while it runs instead of one
                // block of text after it has finished.
                OnLine = line =>
                {
                    int n = System.Threading.Interlocked.Increment(ref logged);
                    if (n <= ScriptLinesLoggedLive)
                    {
                        Utils.Logger.Info("[Python] " + name + ": " + line);
                    }
                    else if (n == ScriptLinesLoggedLive + 1)
                    {
                        Utils.Logger.Info("[Python] " + name + ": … further output is not logged line by line.");
                    }
                },
                Environment = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["TEMPO_MACRO"] = _scriptMacroName ?? "",
                    ["TEMPO_STEP"] = _scriptStep.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["TEMPO_LOOP"] = _scriptLoop.ToString(System.Globalization.CultureInfo.InvariantCulture),
                }
            };

            Utils.PythonRunner.Result r;
            try
            {
                r = Utils.PythonRunner.Run(action.ScriptPath, action.ScriptTimeoutMs, _stopSignal, options);
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

            // The output has already been logged line by line as it arrived; logging the
            // whole block again here would print every line twice.

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
