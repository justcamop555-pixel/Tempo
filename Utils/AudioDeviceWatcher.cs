using System;
using NAudio.CoreAudioApi;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Watches whether this PC currently HAS a speaker (default playback device)
    /// and a microphone, by name, and says so the moment that changes — headphones
    /// plugged in, a USB speaker removed, Bluetooth connecting, etc.
    ///
    /// Why it matters: Tempo's captions hear "what the PC is playing" through the
    /// speaker's loopback. No speaker → nothing to capture. This watcher (a) lets
    /// the Settings page show plainly which devices Tempo sees right now, and
    /// (b) lets captions AUTO-RECOVER when a speaker appears or returns, instead
    /// of needing Live Captions toggled off and on by hand.
    ///
    /// Polls every 3 s with a cheap default-endpoint query; events fire on a
    /// worker thread — UI subscribers must marshal.
    /// </summary>
    public sealed class AudioDeviceWatcher : IDisposable
    {
        private readonly System.Threading.Timer _timer;
        private MMDeviceEnumerator _enum;
        private volatile bool _disposed;
        private int _guard;

        private string _speaker;          // friendly name, null = none
        private bool _speakerKnown;       // first tick fires events for initial state
        private string _micName;          // friendly name of the default mic, null = none
        private bool _micKnown;
        // Debounce for "speaker gone": device queries can fail TRANSIENTLY during a
        // device change, and one bad read must not flash "⚠ No speaker" or trigger
        // recovery churn. Gone is only reported after two null reads in a row.
        private int _nullReads;

        /// <summary>Friendly name of the default speaker, or null when the PC has none.</summary>
        public string SpeakerName => _speaker;
        public bool HasSpeaker => _speaker != null;
        /// <summary>Friendly name of the default microphone, or null when the PC has none.</summary>
        public string MicrophoneName => _micName;
        public bool HasMicrophone => _micName != null;

        /// <summary>Fired when the default speaker changes or (dis)appears. Arg = name or null.</summary>
        public event Action<string> SpeakerChanged;
        /// <summary>Fired when the default microphone changes or (dis)appears. Arg = name or null.</summary>
        public event Action<string> MicrophoneChanged;

        // ── Full device lists (for the "choose your device" pickers) ────────
        private readonly object _listLock = new object();
        private System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>
            _speakerList = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>();
        private System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>
            _micList = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>();

        /// <summary>All active speakers as (endpoint id, model name), refreshed ~2 s.</summary>
        public System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>> Speakers
        {
            get { lock (_listLock) { return new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>(_speakerList); } }
        }

        /// <summary>All active microphones as (endpoint id, model name), refreshed ~2 s.</summary>
        public System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>> Microphones
        {
            get { lock (_listLock) { return new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>(_micList); } }
        }

        /// <summary>Fired (worker thread) when the set of devices changes at all.</summary>
        public event Action DeviceListChanged;

        public AudioDeviceWatcher()
        {
            try { _enum = new MMDeviceEnumerator(); } catch { _enum = null; }
            // 2 s poll: quick enough that plugging in a microphone or speaker gets
            // noticed (and acted on) promptly, still a trivial enumeration cost.
            _timer = new System.Threading.Timer(Tick, null, 250, 2000);
        }

        private void Tick(object state)
        {
            if (_disposed || System.Threading.Interlocked.CompareExchange(ref _guard, 1, 0) != 0)
            {
                return;
            }
            try
            {
                // The enumerator itself can fail to construct (early COM state);
                // keep retrying so the watcher heals instead of staying dead.
                if (_enum == null)
                {
                    try { _enum = new MMDeviceEnumerator(); } catch { _enum = null; }
                }

                string speaker = QuerySpeakerName();
                string mic = QueryMicName();

                // Transient-null debounce: keep the last known speaker through one
                // failed read; only two consecutive nulls mean it's really gone.
                if (speaker == null && _speaker != null && _nullReads == 0)
                {
                    _nullReads = 1;
                    return;
                }
                _nullReads = 0;

                bool speakerChanged = !_speakerKnown || !string.Equals(speaker, _speaker, StringComparison.Ordinal);
                bool micChanged = !_micKnown || !string.Equals(mic, _micName, StringComparison.Ordinal);
                _speaker = speaker;
                _speakerKnown = true;
                _micName = mic;
                _micKnown = true;

                if (speakerChanged)
                {
                    try { SpeakerChanged?.Invoke(speaker); } catch { }
                }
                if (micChanged)
                {
                    try { MicrophoneChanged?.Invoke(mic); } catch { }
                }

                RefreshDeviceLists();
            }
            catch { /* device queries must never destabilise the app */ }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _guard, 0);
            }
        }

        private string QuerySpeakerName()
        {
            try
            {
                if (_enum == null) { return null; }
                using (MMDevice dev = _enum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                {
                    string name = dev?.FriendlyName;
                    return string.IsNullOrWhiteSpace(name) ? "Speaker" : name;
                }
            }
            catch
            {
                return null;                       // no active playback device
            }
        }

        /// <summary>
        /// Re-enumerates every active device of both classes, with honest names for
        /// devices whose model can't be read. Fires DeviceListChanged on membership
        /// changes so the Settings pickers stay current as devices come and go.
        /// </summary>
        private void RefreshDeviceLists()
        {
            try
            {
                if (_enum == null) { return; }
                var speakers = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>();
                var mics = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>();
                foreach (DataFlow flow in new[] { DataFlow.Render, DataFlow.Capture })
                {
                    var target = flow == DataFlow.Render ? speakers : mics;
                    foreach (MMDevice dev in _enum.EnumerateAudioEndPoints(flow, DeviceState.Active))
                    {
                        using (dev)
                        {
                            string id = "";
                            try { id = dev.ID; } catch { }
                            if (id.Length == 0) { continue; }
                            target.Add(new System.Collections.Generic.KeyValuePair<string, string>(
                                id, AudioDeviceSelection.NameOf(dev, flow)));
                        }
                    }
                }

                bool changed;
                lock (_listLock)
                {
                    changed = !SameList(_speakerList, speakers) || !SameList(_micList, mics);
                    _speakerList = speakers;
                    _micList = mics;
                }
                if (changed)
                {
                    try { DeviceListChanged?.Invoke(); } catch { }
                }
            }
            catch { }
        }

        private static bool SameList(
            System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>> a,
            System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>> b)
        {
            if (a.Count != b.Count) { return false; }
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Key != b[i].Key || a[i].Value != b[i].Value) { return false; }
            }
            return true;
        }

        private string QueryMicName()
        {
            try
            {
                if (_enum == null) { return null; }
                using (MMDevice dev = _enum.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia))
                {
                    string name = dev?.FriendlyName;
                    return string.IsNullOrWhiteSpace(name) ? "Microphone" : name;
                }
            }
            catch
            {
                return null;                       // no active capture device
            }
        }

        public void Dispose()
        {
            _disposed = true;
            try { _timer.Dispose(); } catch { }
            try { _enum?.Dispose(); } catch { }
            _enum = null;
        }
    }
}
