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
                double speed = speedMultiplier <= 0 ? 1.0 : speedMultiplier;
                int delay = loopDelayMs < 0 ? 0 : loopDelayMs;
                _stopSignal.Reset();
                _playing = true;

                _worker = new Thread(() => PlaybackLoop(snapshot, loopCount, speed, delay))
                {
                    IsBackground = true,
                    Name = "AutoClicker.MacroPlayer"
                };
                _worker.Start();
            }

            PlaybackStarted?.Invoke(this, EventArgs.Empty);
            Logger.Info($"Macro playback started ('{macro.Name}', loops={loopCount}).");
        }

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

            Logger.Info("Macro playback stopped.");
        }

        private void PlaybackLoop(Macro macro, int loopCount, double speed, int loopDelayMs)
        {
            try
            {
                _pressedButtons.Clear();
                _pressedKeys.Clear();

                int loopsDone = 0;
                bool infinite = loopCount <= 0;

                while (_playing && (infinite || loopsDone < loopCount))
                {
                    LoopChanged?.Invoke(this, loopsDone + 1);

                    for (int i = 0; i < macro.Actions.Count && _playing; i++)
                    {
                        ExecuteAction(macro.Actions[i], speed);
                        StepExecuted?.Invoke(this, i);
                    }

                    loopsDone++;

                    // Wait between loops (but not after the final one). The wait is
                    // interruptible so Stop() takes effect promptly.
                    bool moreLoops = _playing && (infinite || loopsDone < loopCount);
                    if (moreLoops && loopDelayMs > 0)
                    {
                        _stopSignal.Wait(loopDelayMs);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Exception during macro playback.", ex);
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

        private void ExecuteAction(MacroAction action, double speed)
        {
            switch (action.Type)
            {
                case MacroActionType.Delay:
                    int ms = (int)Math.Max(0, Math.Round(action.DelayMilliseconds / speed));
                    WaitInterruptible(ms);
                    break;

                case MacroActionType.MouseMove:
                    InputSimulator.MoveTo(action.X, action.Y);
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
            }
        }

        private bool WaitInterruptible(int ms)
        {
            if (ms <= 0)
            {
                return !_playing;
            }

            if (PreciseWait.Wait(ms, _stopSignal))
            {
                return true;
            }

            return !_playing;
        }

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
