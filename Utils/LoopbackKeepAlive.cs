using System;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Keeps WASAPI loopback capture awake by playing continuous, perfectly
    /// inaudible silence to the default speaker.
    ///
    /// Why this exists: Windows' loopback capture ("record what the PC is playing")
    /// only delivers audio while some app is actually rendering sound. The moment
    /// everything goes quiet, the capture stream simply stops producing buffers -
    /// and when sound returns (a video starts, a game plays a button click), the
    /// stream has to spin back up, which on many drivers delays or even drops the
    /// opening moments. That is exactly the "captions sometimes not responding /
    /// delayed at the start of a video or a short sound" symptom. Playing silence
    /// keeps the audio engine pumping 24/7, so both Tempo's caption engine and the
    /// speaker-voice profiler hear EVERYTHING from its very first millisecond.
    ///
    /// Ref-counted: the transcriber and the voice profiler each Acquire() while
    /// they capture loopback and Release() when they stop; silence plays while at
    /// least one holder remains. Playing zeros costs no audible output and no
    /// meaningful CPU. Everything is best-effort - a PC with no speaker just runs
    /// without the keep-alive, exactly as before.
    /// </summary>
    public static class LoopbackKeepAlive
    {
        private static readonly object _lock = new object();
        private static int _refs;
        private static MMDeviceEnumerator _enumerator;
        private static MMDevice _device;
        private static WasapiOut _out;
        private static int _restartAttempts;

        /// <summary>True while the silence stream is actually playing (Live Debug).</summary>
        public static bool IsActive
        {
            get { lock (_lock) { return _out != null; } }
        }

        /// <summary>A component that captures loopback audio has started.</summary>
        public static void Acquire()
        {
            lock (_lock)
            {
                _refs++;
                if (_refs == 1)
                {
                    _restartAttempts = 0;
                    StartLocked();
                }
            }
        }

        /// <summary>A component that captures loopback audio has stopped.</summary>
        public static void Release()
        {
            lock (_lock)
            {
                if (_refs > 0) _refs--;
                if (_refs == 0)
                {
                    StopLocked();
                }
            }
        }

        /// <summary>
        /// Re-opens the silence stream on the CURRENT default speaker. Called by the
        /// capture components when they recover from a device change, so the
        /// keep-alive follows them to the new device instead of pumping the old one.
        /// </summary>
        public static void Poke()
        {
            lock (_lock)
            {
                if (_refs > 0)
                {
                    StopLocked();
                    _restartAttempts = 0;
                    StartLocked();
                }
            }
        }

        private static void StartLocked()
        {
            try
            {
                _enumerator = new MMDeviceEnumerator();
                // Keep-alive must pump the device the captures LISTEN to — the
                // user's chosen speaker when one is picked, the default otherwise.
                _device = AudioDeviceSelection.Resolve(_enumerator, DataFlow.Render, out _);
                if (_device == null)
                {
                    throw new InvalidOperationException("no playback device");
                }
                // Use the device's own mix format so no resampling is involved -
                // the render path just copies zeros.
                WaveFormat mix = _device.AudioClient.MixFormat;
                _out = new WasapiOut(_device, AudioClientShareMode.Shared, true, 200);
                _out.PlaybackStopped += OnPlaybackStopped;
                _out.Init(new SilenceProvider(mix));
                _out.Play();
                Logger.Info("[Audio] loopback keep-alive playing (device stays awake for captions).");
            }
            catch (Exception ex)
            {
                // No speaker, exclusive-mode app, etc. - captions still work as before.
                Logger.Warn("[Audio] loopback keep-alive unavailable: " + ex.Message);
                StopLocked();
            }
        }

        private static void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            if (e?.Exception == null)
            {
                return;                                  // our own StopLocked()
            }
            // The device died or changed under us; follow it to the new default.
            Logger.Warn("[Audio] keep-alive playback stopped: " + e.Exception.Message);
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    await System.Threading.Tasks.Task.Delay(1500);
                    lock (_lock)
                    {
                        if (_refs > 0 && _restartAttempts < 5)
                        {
                            _restartAttempts++;
                            StopLocked();
                            StartLocked();
                        }
                    }
                }
                catch { }
            });
        }

        private static void StopLocked()
        {
            try
            {
                if (_out != null)
                {
                    _out.PlaybackStopped -= OnPlaybackStopped;
                    _out.Stop();
                    _out.Dispose();
                }
            }
            catch { }
            _out = null;
            try { _device?.Dispose(); } catch { }
            _device = null;
            try { _enumerator?.Dispose(); } catch { }
            _enumerator = null;
        }
    }
}
