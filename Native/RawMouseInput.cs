using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using AutoClicker.Utils;

namespace AutoClicker.Native
{
    /// <summary>
    /// Reports RAW, RELATIVE mouse movement (counts, not pixels) using the Win32 Raw
    /// Input API.
    ///
    /// Why not the low-level mouse hook? WH_MOUSE_LL reports the CURSOR POSITION, and
    /// the moment a game takes mouse-look it hides the cursor, clips it to the window
    /// centre, or reads raw input itself — at which point the cursor stops moving and
    /// the hook reports deltas of zero even though the player is swinging the mouse
    /// hard. Camera tracking built on the hook would simply go blind exactly when it
    /// matters. Raw Input reports the physical movement the mouse reported to the OS,
    /// independent of where (or whether) a cursor exists.
    ///
    /// RIDEV_INPUTSINK means we keep receiving movement even though our window is not
    /// the foreground one — essential, since the game is what has focus.
    ///
    /// Deltas are accumulated with Interlocked and drained by the consumer thread, so
    /// the (UI-thread) message handler stays allocation-free and lock-free.
    /// </summary>
    public sealed class RawMouseInput : IDisposable
    {
        /// <summary>Accumulated raw counts since the last drain. Interlocked only.</summary>
        private long _accumX;
        private long _accumY;

        // One delegate instance, so the hub can find this listener again to remove it.
        private readonly Action<RawMouseEvent> _onInput;
        private bool _disposed;

        public RawMouseInput()
        {
            _onInput = OnRawInput;
        }

        /// <summary>True once the OS accepted our raw-input registration.</summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// Starts listening. MUST be called from a thread with a message pump (the UI
        /// thread) — WM_INPUT is delivered to a window, and without a pump no movement
        /// would ever arrive. The registration itself is shared (see <see cref="RawMouseHub"/>).
        /// </summary>
        public bool Start()
        {
            if (_disposed) { throw new ObjectDisposedException(nameof(RawMouseInput)); }
            if (IsRunning) { return true; }

            if (!RawMouseHub.Subscribe(_onInput))
            {
                Logger.Error("[Movement] raw mouse input could not start.");
                return false;
            }

            IsRunning = true;
            Logger.Info("[Movement] raw mouse input active (relative deltas, survives cursor capture).");
            return true;
        }

        public void Stop()
        {
            if (!IsRunning) { return; }
            IsRunning = false;
            // Only this listener leaves: the second cursor or a macro recording sharing the
            // registration keep theirs.
            RawMouseHub.Unsubscribe(_onInput);
            Interlocked.Exchange(ref _accumX, 0);
            Interlocked.Exchange(ref _accumY, 0);
            Logger.Info("[Movement] raw mouse input stopped.");
        }

        /// <summary>
        /// Takes everything accumulated since the previous call and resets the
        /// counters, so no movement is ever counted twice or silently dropped.
        /// </summary>
        public void Drain(out int dx, out int dy)
        {
            dx = (int)Interlocked.Exchange(ref _accumX, 0);
            dy = (int)Interlocked.Exchange(ref _accumY, 0);
        }

        private void OnRawInput(RawMouseEvent e)
        {
            // Absolute-positioning devices (tablet, RDP, some VMs) report a screen
            // coordinate here, not a delta. Integrating that as if it were movement
            // would send the camera estimate flying, so ignore it outright.
            if (e.Absolute)
            {
                return;
            }
            if (e.Dx != 0) { Interlocked.Add(ref _accumX, e.Dx); }
            if (e.Dy != 0) { Interlocked.Add(ref _accumY, e.Dy); }
        }

        public void Dispose()
        {
            if (_disposed) { return; }
            Stop();
            _disposed = true;
        }
    }
}
