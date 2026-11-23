using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Hears ONE app: what a process (and the processes it started) plays, and nothing any other app plays — Windows'
    /// process loopback (ActivateAudioInterfaceAsync on "VAD\Process_Loopback", Windows 10 build 20348 and later;
    /// Microsoft's ApplicationLoopback sample shows the same steps).
    ///
    /// Round 27 (2026-11-15). Captions listened to the speaker's whole mix, so every other app's sound was mixed into the
    /// words: the user's "my apps play random [sounds] that interrupt live caption — that's why it appears late and
    /// misspelled while the streamers say something" (Discord, Teams, Medal and Steam all running beside the stream).
    /// Listening to the app that plays the stream leaves them out before Whisper, the voice profiler or Silero hear them.
    ///
    /// The capture is not tied to an output: the app is heard on whichever speaker it plays to. When the app plays
    /// nothing, Windows delivers silence. Always 48 kHz stereo 32-bit float — the format a speaker's mix normally is —
    /// so the caption engine and the voice profiler treat it exactly like the loopback they had before (the profiler's
    /// voice gate needs a rate that is a whole multiple of 16 kHz). Windows converts the app's own format to it.
    /// </summary>
    public sealed class AppAudioCapture : IWaveIn
    {
        /// <summary>Process loopback exists from Windows 10 build 20348 (Windows 11 has it everywhere).</summary>
        public static bool Supported => Environment.OSVersion.Platform == PlatformID.Win32NT
                                        && Environment.OSVersion.Version.Build >= 20348;

        public const int Rate = 48000;
        public const int Channels = 2;

        /// <summary>The process whose audio (with its children's) is heard.</summary>
        public int ProcessId { get; }

        /// <summary>The executable's name, lower case ("msedge"), or "" when it could not be read.</summary>
        public string ProcessName { get; }

        public WaveFormat WaveFormat { get; set; }

        public event EventHandler<WaveInEventArgs> DataAvailable;
        public event EventHandler<StoppedEventArgs> RecordingStopped;

        /// <summary>Buffers delivered so far (for the harness and Live debug).</summary>
        public long Packets => Interlocked.Read(ref _packets);

        /// <summary>Times Windows reported a gap in the audio (it was not read in time).</summary>
        public int Discontinuities => _discontinuities;

        private AudioClient _client;
        private AudioCaptureClient _captureClient;
        private readonly AutoResetEvent _ready = new AutoResetEvent(false);
        private Thread _thread;
        private volatile bool _stopRequested;
        private volatile bool _disposed;
        private byte[] _buffer = new byte[Rate / 5 * Channels * 4];   // 200 ms to start; grows if a read needs more
        private readonly int _bytesPerFrame;
        private long _packets;
        private int _discontinuities;

        private AppAudioCapture(int pid, string name, AudioClient client, WaveFormat format)
        {
            ProcessId = pid;
            ProcessName = name ?? "";
            _client = client;
            WaveFormat = format;
            _bytesPerFrame = format.BlockAlign;
        }

        /// <summary>
        /// Opens a capture of <paramref name="pid"/>'s audio (not started). Null with the reason when Windows refuses:
        /// too old a Windows, a process that has gone, or the activation failing for any other reason — the caller then
        /// keeps hearing the speaker's whole mix.
        /// </summary>
        public static AppAudioCapture TryOpen(int pid, out string why)
        {
            // The activated client lives in the multithreaded apartment (the completion arrives on Windows' audio thread
            // pool) and IAudioClient has no proxy to reach it from a single-threaded one: opened from the WINDOW's thread
            // the cast below threw "Specified cast is not valid.", so the voice profiler — started from the UI — heard
            // every app's sound instead of the one playing the video (the user's log, round 30, 2026-11-16). Opened on a
            // pool thread, as the caption engine always was.
            if (OnUiApartment)
            {
                string poolWhy = null;
                var opened = System.Threading.Tasks.Task.Run(() => { var c = TryOpen(pid, out string w); poolWhy = w; return c; })
                    .GetAwaiter().GetResult();
                why = poolWhy;
                return opened;
            }
            why = null;
            if (!Supported) { why = "this Windows has no per-app capture (needs build 20348 or later)"; return null; }
            if (pid <= 0) { why = "no app"; return null; }
            string name = NameOf(pid);
            if (name == null) { why = "the app (process " + pid + ") is not running"; return null; }

            object iface;
            try
            {
                iface = Activate(pid, out why);
            }
            catch (Exception ex)
            {
                why = "activation failed: " + ex.Message;
                return null;
            }
            if (iface == null) { return null; }

            AudioClient client = null;
            try
            {
                client = new AudioClient((IAudioClient)iface);
                // Event-driven (Windows wakes the reader each period), with a 100 ms buffer as slack for a busy PC: in
                // event mode a bigger buffer adds no delay — every period's audio is read the moment it arrives — it only
                // keeps a late read from losing sound. Formats are tried from the one the engine and profiler like best.
                var flags = AudioClientStreamFlags.Loopback | AudioClientStreamFlags.EventCallback
                          | AudioClientStreamFlags.AutoConvertPcm | AudioClientStreamFlags.SrcDefaultQuality;
                WaveFormat[] formats =
                {
                    WaveFormat.CreateIeeeFloatWaveFormat(Rate, Channels),
                    new WaveFormat(Rate, 16, Channels),
                    new WaveFormat(44100, 16, Channels),   // the format Microsoft's sample uses
                };
                Exception last = null;
                foreach (WaveFormat f in formats)
                {
                    foreach (long buffer in new long[] { 1000000, 0 })
                    {
                        try
                        {
                            client.Initialize(AudioClientShareMode.Shared, flags, buffer, 0, f, Guid.Empty);
                            var cap = new AppAudioCapture(pid, name, client, f);
                            client = null;   // owned by the capture now
                            return cap;
                        }
                        catch (Exception ex)
                        {
                            last = ex;
                            // A failed Initialize leaves the client unusable: start again from a fresh activation.
                            try { client.Dispose(); } catch { }
                            client = null;
                            object again = Activate(pid, out why);
                            if (again == null) { return null; }
                            client = new AudioClient((IAudioClient)again);
                        }
                    }
                }
                why = "Windows refused every capture format (" + (last != null ? last.Message : "?") + ")";
                return null;
            }
            catch (Exception ex)
            {
                why = "setup failed: " + ex.Message;
                return null;
            }
            finally
            {
                try { client?.Dispose(); } catch { }
            }
        }

        /// <summary>Lower-case executable name of a running process, or null when it is not running.</summary>
        public static string NameOf(int pid)
        {
            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    if (p.HasExited) { return null; }
                    return p.ProcessName.ToLowerInvariant();
                }
            }
            catch (ArgumentException) { return null; }      // no such process
            catch { return ""; }                            // running, but its name is not ours to read
        }

        /// <summary>A single-threaded apartment (the window's thread), where the client cannot be used (see TryOpen).</summary>
        private static bool OnUiApartment => Thread.CurrentThread.GetApartmentState() == ApartmentState.STA;

        public void StartRecording()
        {
            if (_disposed) { throw new ObjectDisposedException(nameof(AppAudioCapture)); }
            if (_thread != null) { return; }
            if (OnUiApartment)
            {
                // Same reason as TryOpen: the client's calls go from a pool thread. Failures still surface here.
                System.Threading.Tasks.Task.Run(StartRecording).GetAwaiter().GetResult();
                return;
            }
            _stopRequested = false;
            _captureClient = _client.AudioCaptureClient;
            _client.SetEventHandle(_ready.SafeWaitHandle.DangerousGetHandle());
            _client.Start();
            _thread = new Thread(CaptureLoop)
            {
                IsBackground = true,
                Name = "Tempo app audio " + ProcessId,
                Priority = ThreadPriority.AboveNormal,
            };
            _thread.Start();
        }

        public void StopRecording()
        {
            _stopRequested = true;
            try { _ready.Set(); } catch { }
        }

        private void CaptureLoop()
        {
            Exception failure = null;
            try
            {
                while (!_stopRequested)
                {
                    // Woken each period; the timeout only guards against a missed signal.
                    _ready.WaitOne(100);
                    if (_stopRequested) { break; }
                    ReadAvailable();
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                try { _client?.Stop(); } catch { }
            }
            try { RecordingStopped?.Invoke(this, new StoppedEventArgs(failure)); } catch { }
        }

        private void ReadAvailable()
        {
            int filled = 0;
            int frames = _captureClient.GetNextPacketSize();
            while (frames > 0)
            {
                IntPtr data = _captureClient.GetBuffer(out int got, out AudioClientBufferFlags flags);
                int bytes = got * _bytesPerFrame;
                if (filled + bytes > _buffer.Length)
                {
                    Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, filled + bytes));
                }
                if ((flags & AudioClientBufferFlags.Silent) != 0 || data == IntPtr.Zero)
                {
                    Array.Clear(_buffer, filled, bytes);
                }
                else
                {
                    Marshal.Copy(data, _buffer, filled, bytes);
                }
                if ((flags & AudioClientBufferFlags.DataDiscontinuity) != 0 && Interlocked.Read(ref _packets) > 0)
                {
                    Interlocked.Increment(ref _discontinuities);
                }
                filled += bytes;
                _captureClient.ReleaseBuffer(got);
                frames = _captureClient.GetNextPacketSize();
            }
            if (filled > 0)
            {
                Interlocked.Increment(ref _packets);
                DataAvailable?.Invoke(this, new WaveInEventArgs(_buffer, filled));
            }
        }

        public void Dispose()
        {
            if (_disposed) { return; }
            _disposed = true;
            StopRecording();
            Thread t = _thread;
            // Joined unless Dispose is called FROM the capture thread (a handler reacting to RecordingStopped).
            if (t != null && t != Thread.CurrentThread)
            {
                try { t.Join(2000); } catch { }
            }
            _thread = null;
            _captureClient = null;   // the AudioClient disposes the capture client it handed out
            AudioClient client = _client;
            _client = null;
            // Released from a pool thread when called from the window's (see TryOpen).
            if (client != null)
            {
                if (OnUiApartment) { System.Threading.Tasks.Task.Run(() => { try { client.Dispose(); } catch { } }).Wait(2000); }
                else { try { client.Dispose(); } catch { } }
            }
            try { _ready.Dispose(); } catch { }
        }

        // ── Activation (mmdevapi) ─────────────────────────────────────────────────────────────────────────────────────

        private const string ProcessLoopbackDevice = @"VAD\Process_Loopback";   // VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK
        private const int ActivationTypeProcessLoopback = 1;                    // AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK
        private const int IncludeTargetProcessTree = 0;                         // PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE
        private const ushort VtBlob = 65;                                       // VT_BLOB
        private static readonly Guid IidAudioClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");

        [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IActivateAudioInterfaceAsyncOperation
        {
            void GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
        }

        [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IActivateAudioInterfaceCompletionHandler
        {
            void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
        }

        // Marks the handler free-threaded: Windows calls it back on one of its own threads.
        [ComImport, Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAgileObject { }

        [ClassInterface(ClassInterfaceType.None)]
        private sealed class Completion : IActivateAudioInterfaceCompletionHandler, IAgileObject
        {
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
            public int Result = unchecked((int)0x80004005);   // E_FAIL until Windows says otherwise
            public object Interface;

            public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation op)
            {
                try
                {
                    op.GetActivateResult(out int hr, out object iface);
                    Result = hr;
                    Interface = iface;
                }
                catch (Exception ex)
                {
                    Result = ex.HResult;
                }
                finally
                {
                    Done.Set();
                }
            }
        }

        [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = true)]
        private static extern int ActivateAudioInterfaceAsync(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
            [In] ref Guid riid,
            IntPtr activationParams,
            IActivateAudioInterfaceCompletionHandler completionHandler,
            out IActivateAudioInterfaceAsyncOperation activationOperation);

        /// <summary>An IAudioClient that hears <paramref name="pid"/>'s process tree, or null with the reason.</summary>
        private static object Activate(int pid, out string why)
        {
            why = null;
            // AUDIOCLIENT_ACTIVATION_PARAMS: { ActivationType; { TargetProcessId; ProcessLoopbackMode } } — 12 bytes.
            IntPtr parms = Marshal.AllocHGlobal(12);
            // PROPVARIANT holding a VT_BLOB: vt at 0, BLOB.cbSize at 8, BLOB.pBlobData at 8 + pointer alignment.
            IntPtr prop = Marshal.AllocHGlobal(32);
            // Freed once Windows is done with them: if it never answers, they are left alone (a few bytes) rather than
            // freed under a request that may still read them.
            bool release = true;
            try
            {
                Marshal.WriteInt32(parms, 0, ActivationTypeProcessLoopback);
                Marshal.WriteInt32(parms, 4, pid);
                Marshal.WriteInt32(parms, 8, IncludeTargetProcessTree);
                for (int i = 0; i < 32; i += 4) { Marshal.WriteInt32(prop, i, 0); }
                Marshal.WriteInt16(prop, 0, unchecked((short)VtBlob));
                Marshal.WriteInt32(prop, 8, 12);
                Marshal.WriteIntPtr(prop, IntPtr.Size == 8 ? 16 : 12, parms);

                var done = new Completion();
                Guid iid = IidAudioClient;
                int hr = ActivateAudioInterfaceAsync(ProcessLoopbackDevice, ref iid, prop, done, out IActivateAudioInterfaceAsyncOperation op);
                if (hr < 0)
                {
                    why = "Windows refused per-app capture (0x" + hr.ToString("X8") + ")";
                    return null;
                }
                if (!done.Done.Wait(5000))
                {
                    release = false;
                    why = "Windows did not answer the per-app capture request in 5 s";
                    return null;
                }
                GC.KeepAlive(op);
                if (done.Result < 0 || done.Interface == null)
                {
                    why = "per-app capture failed (0x" + done.Result.ToString("X8") + ")";
                    return null;
                }
                return done.Interface;
            }
            finally
            {
                if (release)
                {
                    Marshal.FreeHGlobal(prop);
                    Marshal.FreeHGlobal(parms);
                }
            }
        }
    }
}
