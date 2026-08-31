using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using AutoClicker.Native;
using AutoClicker.Utils;

namespace AutoClicker.Engine
{
    /// <summary>Which frame of reference the movement keys are interpreted in.</summary>
    public enum MovementFrame
    {
        /// <summary>
        /// The direction you press is held in WORLD space. Swing the camera and Tempo
        /// re-mixes W/A/S/D so the character keeps travelling the way you originally
        /// aimed it — you orbit/strafe around your heading instead of being dragged
        /// with the camera. THIS is the mode that does real work, because virtually
        /// every third-person game is already camera-relative on its own.
        /// </summary>
        WorldLocked = 0,

        /// <summary>
        /// Keys mean camera-space directions and are passed through unchanged. This is
        /// what a normal third-person game already does natively, so in such a game
        /// this mode is a deliberate NO-OP. It exists for games with fixed/detached
        /// cameras, and as an A/B baseline when calibrating.
        /// </summary>
        CameraRelative = 1
    }

    /// <summary>
    /// Every knob, in one place. Times are seconds, angles are degrees, and anything
    /// rate-like is per-second so the behaviour is identical at any update rate.
    /// </summary>
    public sealed class MovementTuning
    {
        // ── The one setting you MUST calibrate ───────────────────────────────
        /// <summary>
        /// How many degrees the in-game camera yaws per unit of raw mouse movement.
        /// This is the whole basis of the camera estimate, and it is specific to the
        /// game AND to its sensitivity slider. Use the built-in calibration helper
        /// (see <see cref="CameraRelativeMovement.CalibrateFromFullTurn"/>) rather
        /// than guessing.
        /// </summary>
        public double DegreesPerMouseCount = 0.06;

        /// <summary>Degrees per second of camera yaw at full right-stick deflection.</summary>
        public double GamepadYawDegreesPerSecond = 220.0;

        // ── Feel ─────────────────────────────────────────────────────────────
        /// <summary>
        /// Time constant for smoothing the commanded direction, in seconds. 0 = instant
        /// (zero added latency — what "responsive" means, and the default). Raising it
        /// softens direction changes at the cost of lag. Applied frame-rate
        /// independently, so the feel does not change with <see cref="UpdateHz"/>.
        /// </summary>
        public double TurnSmoothingSeconds = 0.0;

        /// <summary>Radial stick deadzone, 0..1.</summary>
        public double StickDeadzone = 0.20;

        /// <summary>
        /// WorldLocked mode, ANALOG input only: how far the stick must swing from the
        /// latched direction before a new world heading is taken, at FULL deflection.
        /// The engine widens this as the stick returns toward centre, where the same
        /// wobble means a much larger angle. Must sit above a stick's resting noise
        /// (a degree or two) and below a deliberate re-aim, or world-locking either
        /// chatters or stops responding. Keyboard input ignores this — W/A/S/D is exact.
        /// </summary>
        public double StickRelatchDegrees = 12.0;

        /// <summary>Which controller to read (0-3).</summary>
        public uint GamepadIndex = 0;

        // ── Jitter control ───────────────────────────────────────────────────
        /// <summary>
        /// Extra degrees the direction must travel PAST a sector boundary before the
        /// key combination flips. W/A/S/D can only express 8 directions, so a heading
        /// hovering exactly on a boundary would otherwise chatter between (say) W and
        /// W+D many times a second — audible, visible, and horrible. This hysteresis
        /// band is the single most important anti-jitter measure here.
        /// </summary>
        public double SectorHysteresisDegrees = 8.0;

        /// <summary>
        /// Minimum time a key combination must hold before another switch is allowed.
        /// Backstop for the hysteresis above: it bounds the WORST-CASE key-change rate
        /// no matter how the heading behaves.
        /// </summary>
        public double MinSectorDwellSeconds = 0.045;

        // ── Loop ─────────────────────────────────────────────────────────────
        /// <summary>
        /// Update rate. Everything is delta-time driven, so this changes only how
        /// finely the camera is tracked, never how fast the character moves or turns.
        /// </summary>
        public int UpdateHz = 120;

        // ── Keys (virtual-key codes) ─────────────────────────────────────────
        public int KeyForward = 0x57;   // W
        public int KeyLeft = 0x41;      // A
        public int KeyBack = 0x53;      // S
        public int KeyRight = 0x44;     // D

        /// <summary>How the pressed keys are interpreted.</summary>
        public MovementFrame Frame = MovementFrame.WorldLocked;
    }

    /// <summary>
    /// Camera-relative movement for an EXTERNAL tool.
    ///
    /// ───────────────────────────────────────────────────────────────────────────
    ///  HOW THIS CAN WORK AT ALL (and where it can't)
    /// ───────────────────────────────────────────────────────────────────────────
    /// Tempo cannot read the game's camera — it lives in another process. What it CAN
    /// do is DEAD-RECKON it: in a mouse-look game the camera's yaw is driven almost
    /// entirely by horizontal mouse movement, so integrating raw mouse dx (and the
    /// gamepad's right stick) yields a running estimate of how far the camera has
    /// turned since we started. Every decision here is made against that estimate.
    ///
    /// Consequences you should understand, because they are inherent, not bugs:
    ///
    ///  • The estimate DRIFTS whenever the camera turns without mouse input — a
    ///    cutscene, a vehicle, aim-snap, a camera spring, hitting a wall. Nothing
    ///    external can observe that. Press the recentre hotkey (<see cref="ResetYaw"/>)
    ///    while facing your heading to re-zero it.
    ///  • In-game mouse ACCELERATION breaks the linear mouse→degrees relationship the
    ///    estimate depends on. Turn it off in the game, or the estimate will wander.
    ///  • W/A/S/D is 8 directions. A digital keyboard physically cannot express an
    ///    arbitrary heading, so the emitted direction is quantised — worst case 22.5°
    ///    off true. (A virtual analog gamepad would fix this; that is a bigger job.)
    ///
    /// The character is never "rotated" by this code — no external tool can rotate a
    /// character. It only ever decides WHICH MOVEMENT KEYS TO HOLD. The smooth,
    /// lerped, frame-rate-independent part is the HEADING this class steers; the game
    /// then turns the character to match, as it always does.
    ///
    /// Thread model: raw mouse deltas accumulate on the UI thread (lock-free), the
    /// keyboard hook records physical key state, and one dedicated worker thread runs
    /// the fixed-timestep loop. Keys are diffed, so SendInput is called ONLY when the
    /// combination actually changes — not every tick.
    /// </summary>
    public sealed class CameraRelativeMovement : IDisposable
    {
        private readonly MovementTuning _cfg;
        private readonly RawMouseInput _mouse = new RawMouseInput();
        private readonly LowLevelKeyboardHook _keys = new LowLevelKeyboardHook();

        private Thread _worker;
        private ManualResetEventSlim _stop;
        private volatile bool _running;

        // Physical key state, written by the hook thread, read by the worker.
        private volatile bool _physForward, _physBack, _physLeft, _physRight;

        // Dead-reckoned camera yaw in degrees (0 = wherever we started / last reset).
        // Only the worker thread touches this.
        private double _yawDeg;

        // The heading we are steering, in the active frame. Smoothed if configured.
        private double _commandedDeg;
        private bool _hasCommand;

        // World-space heading latched when the pressed direction last changed.
        private double _worldHeadingDeg;
        private double _lastInputDirDeg = double.NaN;

        // The 8-way sector currently being emitted, and when it last changed.
        private int _sector = -1;
        private double _sectorAgeSec;

        // Which keys WE are currently holding down, so we can diff and release cleanly.
        private bool _outForward, _outBack, _outLeft, _outRight;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        /// <summary>
        /// The window this system is allowed to act on — captured when it was armed.
        /// IntPtr.Zero means "act everywhere".
        ///
        /// This matters more than it looks. Suppressing W/A/S/D is GLOBAL: without this
        /// guard, arming the system and then alt-tabbing to a browser or Discord would
        /// silently eat every 'w', 'a', 's' and 'd' you typed. Because you arm it with
        /// a hotkey while the game is in front, the game is exactly what gets captured
        /// here — so your keys pass through untouched everywhere else, and interception
        /// resumes by itself when you switch back.
        /// </summary>
        private IntPtr _targetWindow;

        /// <summary>True when the armed target window (if any) is the foreground one.</summary>
        private bool TargetActive()
        {
            return _targetWindow == IntPtr.Zero || GetForegroundWindow() == _targetWindow;
        }

        // ── Live-debug telemetry (read by the Live Debug window, ~2 Hz) ──────
        // Every one of these is a plain volatile read: the debug window must never
        // be able to stall, block or perturb the movement loop just by looking at it.
        private volatile int _yawX10;          // yaw, tenths of a degree
        private volatile int _headingX10;      // commanded heading, tenths of a degree
        private volatile int _mouseRate;       // raw counts/second (how hard you're turning)
        private volatile bool _padConnected;
        private volatile bool _targetActive;
        private int _rateAccum;
        private double _rateWindowSec;

        /// <summary>
        /// When true, every sector change (and a 1 Hz heartbeat while moving) logs a
        /// [Move] line: yaw, heading, the keys being held, and whether the armed window
        /// is in front. This is the only practical way to see WHY the character went
        /// the wrong way, so it is the first thing to turn on when calibrating.
        /// </summary>
        public static volatile bool VerboseTrace;

        /// <summary>Live camera-yaw estimate in degrees (for a HUD/debug readout).</summary>
        public double EstimatedYawDegrees => _yawX10 / 10.0;

        /// <summary>The heading currently being steered, in camera space, degrees.</summary>
        public double CommandedHeadingDegrees => _headingX10 / 10.0;

        /// <summary>Raw mouse counts per second — how hard the camera is being swung.</summary>
        public int MouseCountsPerSecond => _mouseRate;

        /// <summary>Whether a gamepad answered the last poll.</summary>
        public bool GamepadConnected => _padConnected;

        /// <summary>Whether the armed window is the foreground one (i.e. we are acting).</summary>
        public bool TargetIsForeground => _targetActive;

        /// <summary>Which frame the engine is running in.</summary>
        public MovementFrame Frame => _cfg.Frame;

        /// <summary>The keys Tempo is holding down right now, e.g. "W+D"; "-" when none.</summary>
        public string HeldKeys => Describe(_outForward, _outBack, _outLeft, _outRight);

        /// <summary>The keys the PLAYER is physically holding, e.g. "W"; "-" when none.</summary>
        public string PhysicalKeys => Describe(_physForward, _physBack, _physLeft, _physRight);

        private static string Describe(bool f, bool b, bool l, bool r)
        {
            string s = "";
            if (f) { s += "W"; }
            if (l) { s += (s.Length > 0 ? "+" : "") + "A"; }
            if (b) { s += (s.Length > 0 ? "+" : "") + "S"; }
            if (r) { s += (s.Length > 0 ? "+" : "") + "D"; }
            return s.Length == 0 ? "-" : s;
        }

        /// <summary>The window movement is confined to, or IntPtr.Zero for "everywhere".</summary>
        public IntPtr TargetWindow => _targetWindow;

        /// <summary>True while the movement system is driving the keyboard.</summary>
        public bool IsRunning => _running;

        public MovementTuning Tuning => _cfg;

        public CameraRelativeMovement(MovementTuning tuning = null)
        {
            _cfg = tuning ?? new MovementTuning();
        }

        /// <summary>
        /// Starts the system. Call from the UI thread — raw input needs a message pump.
        ///
        /// Whatever window is in the FOREGROUND right now becomes the only window this
        /// system acts on (see <see cref="_targetWindow"/>) — which is the game, because
        /// you arm this with a hotkey while playing. Pass Tempo's own window handle as
        /// <paramref name="excludeWindow"/> so that arming it from the Settings tab does
        /// not lock the system to Tempo itself; in that case it acts everywhere, which
        /// is the honest fallback and is what the on-screen warning describes.
        /// </summary>
        public bool Start(IntPtr excludeWindow = default)
        {
            if (_running) { return true; }

            if (!_mouse.Start())
            {
                Logger.Error("[Movement] cannot start: raw mouse input unavailable.");
                return false;
            }
            _keys.KeyEvent += OnKeyEvent;
            if (!_keys.Start())
            {
                _keys.KeyEvent -= OnKeyEvent;
                _mouse.Stop();
                Logger.Error("[Movement] cannot start: keyboard hook unavailable.");
                return false;
            }

            _yawDeg = 0;
            _commandedDeg = 0;
            _hasCommand = false;
            _lastInputDirDeg = double.NaN;
            _sector = -1;
            _sectorAgeSec = 0;
            _physForward = _physBack = _physLeft = _physRight = false;

            IntPtr fg = GetForegroundWindow();
            _targetWindow = (fg != IntPtr.Zero && fg != excludeWindow) ? fg : IntPtr.Zero;

            _stop = new ManualResetEventSlim(false);
            _running = true;
            _worker = new Thread(Loop)
            {
                IsBackground = true,
                Name = "Tempo camera-relative movement",
                // Above normal: a late tick means the character keeps running the wrong
                // way for that long. It is a tiny, mostly-sleeping loop.
                Priority = ThreadPriority.AboveNormal
            };
            _worker.Start();

            Logger.Info("[Movement] camera-relative movement started · frame=" + _cfg.Frame +
                        " · " + _cfg.DegreesPerMouseCount.ToString("0.####") + " deg/count · " +
                        _cfg.UpdateHz + " Hz · " +
                        (_targetWindow == IntPtr.Zero
                            ? "active in EVERY window (armed from Tempo — W/A/S/D are captured globally)"
                            : "confined to the window that was in front when armed"));
            return true;
        }

        public void Stop()
        {
            if (!_running) { return; }
            _running = false;

            try { _stop?.Set(); } catch { }
            try { _worker?.Join(500); } catch { }
            _worker = null;

            try { _keys.KeyEvent -= OnKeyEvent; } catch { }
            try { _keys.Stop(); } catch { }
            try { _mouse.Stop(); } catch { }

            // CRITICAL: never leave a synthesised key stuck down. If we exit while
            // holding W, the character runs into the horizon forever and the user has
            // no idea why — the physical key is already up, so nothing releases it.
            ReleaseAllOutputs();

            try { _stop?.Dispose(); } catch { }
            _stop = null;

            Logger.Info("[Movement] camera-relative movement stopped.");
        }

        /// <summary>
        /// Re-zeroes the camera estimate. Bind this to a hotkey: it is the cure for the
        /// drift that any dead-reckoned camera eventually accumulates. Press it while
        /// the camera faces the direction you consider "forward".
        /// </summary>
        public void ResetYaw()
        {
            _yawDeg = 0;
            _worldHeadingDeg = 0;
            _lastInputDirDeg = double.NaN;   // force a re-latch on the next tick
            Logger.Info("[Movement] camera estimate re-zeroed.");
        }

        /// <summary>
        /// Calibration helper. Sweep the mouse so the game's camera turns exactly one
        /// full circle, pass the raw count that took, and this solves for the only
        /// value that matters. Far more reliable than guessing at a sensitivity slider.
        /// </summary>
        public static double CalibrateFromFullTurn(long rawCountsForFullTurn)
        {
            if (rawCountsForFullTurn == 0) { return 0.06; }
            return 360.0 / Math.Abs(rawCountsForFullTurn);
        }

        // ── Input capture ────────────────────────────────────────────────────

        private void OnKeyEvent(object sender, KeyboardHookEventArgs e)
        {
            // Our OWN injected keys must pass through untouched — suppressing them
            // would be a perfect feedback loop that eats the very input we just sent.
            if (e.Injected || !_running)
            {
                return;
            }

            bool relevant = true;
            if (e.VirtualKey == _cfg.KeyForward) { _physForward = e.IsKeyDown; }
            else if (e.VirtualKey == _cfg.KeyBack) { _physBack = e.IsKeyDown; }
            else if (e.VirtualKey == _cfg.KeyLeft) { _physLeft = e.IsKeyDown; }
            else if (e.VirtualKey == _cfg.KeyRight) { _physRight = e.IsKeyDown; }
            else { relevant = false; }

            // Swallow the physical movement key. The game must NOT see it: we are about
            // to send a re-mixed combination, and if the original leaked through too the
            // game would add both and move somewhere neither of us asked for.
            //
            // In CameraRelative (pass-through) mode we are not re-mixing anything, so we
            // let the real key go straight to the game — lower latency, and nothing to
            // gain by round-tripping it through us.
            //
            // And never swallow anything while a DIFFERENT window is in front: outside
            // the armed game these are just ordinary letters someone is typing.
            if (relevant && _cfg.Frame == MovementFrame.WorldLocked && TargetActive())
            {
                e.Suppress = true;
            }
        }

        // ── The loop ─────────────────────────────────────────────────────────

        private void Loop()
        {
            double tickMs = 1000.0 / Math.Max(1, _cfg.UpdateHz);
            var clock = Stopwatch.StartNew();
            double lastSec = 0;

            try
            {
                while (_running && !_stop.IsSet)
                {
                    double nowSec = clock.Elapsed.TotalSeconds;
                    double dt = nowSec - lastSec;
                    lastSec = nowSec;

                    // A huge dt means we were descheduled (a stall, a breakpoint, the
                    // machine sleeping). Integrating it would teleport the estimate, so
                    // clamp it: better to under-rotate for one tick than to lurch.
                    if (dt > 0.25) { dt = 0.25; }
                    if (dt < 0) { dt = 0; }

                    try { Tick(dt); }
                    catch (Exception ex)
                    {
                        Logger.Warn("[Movement] tick skipped: " + ex.Message);
                    }

                    PreciseWait.Wait(tickMs, _stop);
                }
            }
            finally
            {
                // Whatever happened — stop, crash, unhandled exit — do not strand keys.
                ReleaseAllOutputs();
            }
        }

        private void Tick(double dt)
        {
            // 0. Not the armed window? Then we are a bystander: hold no keys, and do not
            //    integrate the mouse — movement out here (aiming at a browser, dragging
            //    a window) has nothing to do with the game's camera, and folding it into
            //    the estimate is precisely how the heading would come back corrupted.
            bool active = TargetActive();
            _targetActive = active;
            if (!active)
            {
                _mouse.Drain(out _, out _);        // discard, don't accumulate
                ReleaseAllOutputs();
                return;
            }

            // 1. Advance the camera estimate from everything that can turn the camera.
            _mouse.Drain(out int dx, out _);
            _yawDeg += dx * _cfg.DegreesPerMouseCount;

            // Mouse-rate readout: how hard the camera is being swung, in raw counts per
            // second. Averaged over a rolling second so the number is readable rather
            // than a blur, and it is what tells a bad calibration from a dead mouse.
            _rateAccum += Math.Abs(dx);
            _rateWindowSec += dt;
            if (_rateWindowSec >= 1.0)
            {
                _mouseRate = (int)(_rateAccum / _rateWindowSec);
                _rateAccum = 0;
                _rateWindowSec = 0;
            }

            GamepadState pad = XInputGamepad.Poll(_cfg.GamepadIndex, _cfg.StickDeadzone);
            _padConnected = pad.Connected;
            if (pad.Connected && pad.RightX != 0)
            {
                // Rate, not position: multiply by dt so the turn speed is identical at
                // any update rate.
                _yawDeg += pad.RightX * _cfg.GamepadYawDegreesPerSecond * dt;
            }
            _yawDeg = Wrap180(_yawDeg);
            _yawX10 = (int)Math.Round(_yawDeg * 10);

            // 2. What direction is the player ASKING for, in the frame they think in?
            //    Gamepad wins when the stick is live (it is analog and unambiguous),
            //    otherwise fall back to the keys.
            double inX, inY;
            bool analogInput;
            if (pad.Connected && (pad.LeftX != 0 || pad.LeftY != 0))
            {
                inX = pad.LeftX;
                inY = pad.LeftY;
                analogInput = true;
            }
            else
            {
                inX = (_physRight ? 1 : 0) - (_physLeft ? 1 : 0);
                inY = (_physForward ? 1 : 0) - (_physBack ? 1 : 0);
                analogInput = false;
            }

            if (inX == 0 && inY == 0)
            {
                // Nothing held: release everything and forget the latch, so the next
                // press starts a fresh heading rather than resuming a stale one.
                ReleaseAllOutputs();
                _hasCommand = false;
                _lastInputDirDeg = double.NaN;
                _sector = -1;
                _traceHeartbeatSec = 0;
                return;
            }

            // Angle convention: 0 = forward (into the screen), +90 = right. Note the
            // (x, y) argument order — atan2(x, y), not the usual atan2(y, x) — which is
            // what puts 0° on the forward axis instead of the right axis.
            double inputDirDeg = Wrap180(Math.Atan2(inX, inY) * 180.0 / Math.PI);

            // 3. Resolve the target heading in CAMERA space (which is the only space
            //    W/A/S/D can actually address).
            double targetDeg;
            if (_cfg.Frame == MovementFrame.WorldLocked)
            {
                // Latch a world heading whenever the pressed direction changes; while it
                // is held, the world heading stays put and the camera moves underneath
                // it. Converting back to camera space is what re-mixes the keys.
                double relatchDeg = RelatchThresholdFor(analogInput, Math.Sqrt(inX * inX + inY * inY));
                if (double.IsNaN(_lastInputDirDeg) ||
                    Math.Abs(AngleDelta(inputDirDeg, _lastInputDirDeg)) > relatchDeg)
                {
                    _lastInputDirDeg = inputDirDeg;
                    _worldHeadingDeg = Wrap180(inputDirDeg + _yawDeg);
                }
                targetDeg = Wrap180(_worldHeadingDeg - _yawDeg);
            }
            else
            {
                targetDeg = inputDirDeg;      // pass-through
            }

            // 4. Optional smoothing, done the frame-rate-independent way.
            //    The naive `a += (b - a) * k` is NOT frame-rate independent — it
            //    converges faster the more often you call it, so the feel would change
            //    with UpdateHz and stutter under load. The exponential form below
            //    depends only on elapsed TIME, so 60 Hz and 240 Hz feel identical.
            //    Interpolating the SHORTEST way round also stops a heading crossing
            //    ±180° from spinning the long way ("gimbal pop").
            if (!_hasCommand)
            {
                _commandedDeg = targetDeg;    // first frame: snap, never sweep in
                _hasCommand = true;
            }
            else if (_cfg.TurnSmoothingSeconds > 1e-4)
            {
                double alpha = 1.0 - Math.Exp(-dt / _cfg.TurnSmoothingSeconds);
                _commandedDeg = Wrap180(_commandedDeg + AngleDelta(targetDeg, _commandedDeg) * alpha);
            }
            else
            {
                _commandedDeg = targetDeg;    // zero latency
            }

            _headingX10 = (int)Math.Round(_commandedDeg * 10);

            // 5. Quantise to the 8 directions a keyboard can express, with hysteresis.
            _sectorAgeSec += dt;
            int previous = _sector;
            int wanted = SectorFor(_commandedDeg, _sector);
            if (wanted != _sector && _sectorAgeSec >= _cfg.MinSectorDwellSeconds)
            {
                _sector = wanted;
                _sectorAgeSec = 0;
            }
            if (_sector < 0)
            {
                _sector = SectorFor(_commandedDeg, -1);   // first commit is immediate
            }

            EmitSector(_sector);

            // Trace: on every key change, plus a 1 Hz heartbeat while moving. The
            // heartbeat is what makes calibration debuggable — you can watch the yaw
            // estimate track (or fail to track) the camera you can see on screen.
            if (VerboseTrace)
            {
                _traceHeartbeatSec += dt;
                bool changed = _sector != previous;
                if (changed || _traceHeartbeatSec >= 1.0)
                {
                    if (!changed) { _traceHeartbeatSec = 0; }
                    else { _traceHeartbeatSec = 0; }
                    Logger.Info("[Move] yaw " + _yawDeg.ToString("0.0") +
                                "° · heading " + _commandedDeg.ToString("0.0") +
                                "° · you press " + PhysicalKeys +
                                " → Tempo sends " + HeldKeys +
                                (changed ? "  (key change)" : ""));
                }
            }
        }

        private double _traceHeartbeatSec;

        /// <summary>
        /// How far the requested direction must move before the WORLD heading is
        /// re-latched (WorldLocked mode only).
        ///
        /// THE BUG THIS FIXES: this test used a flat 0.5° for every input. That is right
        /// for a keyboard — W/A/S/D can only produce multiples of 45°, so a real change is
        /// enormous and anything smaller is impossible. It is badly wrong for a stick.
        /// A real thumbstick jitters by a degree or two even when held still, which is
        /// more than 0.5°, so the world heading was re-latched on EVERY tick: it was
        /// pinned to wherever the stick pointed right now, the camera could never move out
        /// from under it, and WorldLocked silently degraded into the pass-through mode it
        /// exists to be an alternative to. Proven in a harness — with ±1.5° of stick noise
        /// and the camera swung a full 90°, the heading never left ±1.4° of the stick.
        /// Gamepad users had the headline feature quietly doing nothing.
        ///
        /// The widening matters as much as the number. A stick's ANGLE gets less
        /// trustworthy the less it is deflected: the same physical wobble is a fraction of
        /// a degree at full throw and wild just past the deadzone, where the vector is
        /// short and atan2 amplifies it. So the deadband grows as the stick returns toward
        /// centre — capped, so a deliberate change still registers.
        /// </summary>
        private double RelatchThresholdFor(bool analog, double magnitude)
        {
            if (!analog) { return 0.5; }
            double m = Math.Max(0.3, Math.Min(1.0, magnitude));
            return Math.Min(30.0, _cfg.StickRelatchDegrees / m);
        }

        /// <summary>
        /// Maps a heading to one of 8 key directions. With a <paramref name="current"/>
        /// sector, the heading must leave that sector's 45° slice by a further
        /// <see cref="MovementTuning.SectorHysteresisDegrees"/> before we switch — the
        /// deadband that stops a heading resting on a boundary from rattling the keys.
        /// </summary>
        private int SectorFor(double deg, int current)
        {
            int nearest = ((int)Math.Round(Wrap180(deg) / 45.0) % 8 + 8) % 8;
            if (current < 0 || current == nearest)
            {
                return nearest;
            }
            // Still inside the current sector's slice plus the hysteresis band? Stay.
            double offCentre = Math.Abs(AngleDelta(deg, current * 45.0));
            return offCentre <= 22.5 + _cfg.SectorHysteresisDegrees ? current : nearest;
        }

        /// <summary>
        /// The 8-way key table. Sector 0 = forward, advancing clockwise in 45° steps,
        /// so 1 = forward-right, 2 = right, 3 = back-right, and so on. Each diagonal
        /// holds two keys at once — which is exactly how a game produces a diagonal, so
        /// diagonal movement needs no special case anywhere else in this class.
        ///
        /// Pure and static so it can be tested without touching the keyboard.
        /// </summary>
        internal static void KeysForSector(int sector, out bool f, out bool b, out bool l, out bool r)
        {
            f = sector == 7 || sector == 0 || sector == 1;
            r = sector == 1 || sector == 2 || sector == 3;
            b = sector == 3 || sector == 4 || sector == 5;
            l = sector == 5 || sector == 6 || sector == 7;
        }

        /// <summary>Holds exactly the keys for a sector, touching only what changed.</summary>
        private void EmitSector(int sector)
        {
            KeysForSector(sector, out bool f, out bool b, out bool l, out bool r);
            SetOutputs(f, b, l, r);
        }

        /// <summary>
        /// Diffs the wanted key set against what we hold and sends ONLY the changes.
        /// Re-sending a key that is already down every tick would flood SendInput, and
        /// games that watch for key-repeat would see a stutter.
        /// </summary>
        private void SetOutputs(bool f, bool b, bool l, bool r)
        {
            if (f != _outForward) { Key(_cfg.KeyForward, f); _outForward = f; }
            if (b != _outBack) { Key(_cfg.KeyBack, b); _outBack = b; }
            if (l != _outLeft) { Key(_cfg.KeyLeft, l); _outLeft = l; }
            if (r != _outRight) { Key(_cfg.KeyRight, r); _outRight = r; }
        }

        private void ReleaseAllOutputs()
        {
            SetOutputs(false, false, false, false);
        }

        private static void Key(int vk, bool down)
        {
            try
            {
                if (down) { InputSimulator.KeyDown(vk); }
                else { InputSimulator.KeyUp(vk); }
            }
            catch (Exception ex)
            {
                Logger.Warn("[Movement] key " + (down ? "down" : "up") + " failed: " + ex.Message);
            }
        }

        // ── Angle maths ──────────────────────────────────────────────────────

        /// <summary>Normalises to (-180, 180].</summary>
        private static double Wrap180(double deg)
        {
            deg %= 360.0;
            if (deg > 180.0) { deg -= 360.0; }
            else if (deg <= -180.0) { deg += 360.0; }
            return deg;
        }

        /// <summary>
        /// Signed shortest angular distance from <paramref name="from"/> to
        /// <paramref name="to"/>. Using this everywhere (instead of plain subtraction)
        /// is what makes the ±180° seam a non-event.
        /// </summary>
        private static double AngleDelta(double to, double from)
        {
            return Wrap180(to - from);
        }

        public void Dispose()
        {
            Stop();
            try { _mouse.Dispose(); } catch { }
            try { _keys.Dispose(); } catch { }
        }
    }
}
