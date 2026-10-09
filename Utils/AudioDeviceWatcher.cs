using System;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

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
    /// Windows says when something changes (IMMNotificationClient), and a change is looked at
    /// ~150 ms later. A 2 s poll stays as the safety net: a notification can be missed (the audio
    /// service restarting, a registration that failed), and the null-read debounce below needs a
    /// second look anyway. Events fire on a worker thread — UI subscribers must marshal.
    ///
    /// COST. Every poll used to read every device's NAME, and the name is the expensive part of an
    /// endpoint: NAudio opens the device's whole property store and walks every key to find it.
    /// Measured on this project's PC (two speakers, one microphone): 194 ms per poll, 96 ms of CPU
    /// every second — a tenth of a core for as long as Tempo ran, hidden in the tray with captions
    /// off included, which was nearly all of what an idle Tempo cost. A name is now read once per
    /// endpoint and kept (<see cref="FriendlyNameOf"/>); a poll reads only ids and states, and a
    /// rename is caught by its notification.
    /// </summary>
    public sealed class AudioDeviceWatcher : IDisposable
    {
        private const int PollMs = 2000;

        /// <summary>
        /// How long after a notification to look. Plugging in a headset raises several at once
        /// (added, state, default, properties); each one pushes this back, so the burst is read once.
        /// </summary>
        private const int PokeDelayMs = 150;

        private readonly System.Threading.Timer _timer;
        private MMDeviceEnumerator _enum;
        private volatile bool _disposed;
        private int _guard;

        private DeviceNotifications _notifications;
        private MMDeviceEnumerator _notificationsOn;   // the enumerator they are registered with

        /// <summary>Friendly names by endpoint id — see <see cref="FriendlyNameOf"/>.</summary>
        private readonly System.Collections.Generic.Dictionary<string, string> _names =
            new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
        private readonly object _namesLock = new object();

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
        private System.Collections.Generic.List<AudioEndpointInfo>
            _speakerList = new System.Collections.Generic.List<AudioEndpointInfo>();
        private System.Collections.Generic.List<AudioEndpointInfo>
            _micList = new System.Collections.Generic.List<AudioEndpointInfo>();

        /// <summary>Every speaker Windows knows about, usable or not, refreshed on a change.</summary>
        public System.Collections.Generic.List<AudioEndpointInfo> Speakers
        {
            get { lock (_listLock) { return new System.Collections.Generic.List<AudioEndpointInfo>(_speakerList); } }
        }

        /// <summary>Every microphone Windows knows about, usable or not, refreshed on a change.</summary>
        public System.Collections.Generic.List<AudioEndpointInfo> Microphones
        {
            get { lock (_listLock) { return new System.Collections.Generic.List<AudioEndpointInfo>(_micList); } }
        }

        /// <summary>Fired (worker thread) when the set of devices changes at all.</summary>
        public event Action DeviceListChanged;

        public AudioDeviceWatcher()
        {
            try { _enum = new MMDeviceEnumerator(); } catch { _enum = null; }
            _timer = new System.Threading.Timer(Tick, null, 250, PollMs);
            EnsureNotifications();
        }

        /// <summary>
        /// Asks Windows to say when a device changes. Best-effort: without it the poll still sees
        /// everything, only later — and a rename not at all, until Tempo restarts.
        /// </summary>
        private void EnsureNotifications()
        {
            try
            {
                if (_disposed || _enum == null || ReferenceEquals(_notificationsOn, _enum)) { return; }
                var client = _notifications ?? new DeviceNotifications(this);
                if (_enum.RegisterEndpointNotificationCallback(client) == 0)
                {
                    _notifications = client;
                    _notificationsOn = _enum;
                }
            }
            catch { }
        }

        /// <summary>
        /// Look soon rather than at the next poll. Runs on Windows' notification thread, which must
        /// not be kept waiting, so this only moves the timer.
        /// </summary>
        private void Poke()
        {
            if (_disposed) { return; }
            try { _timer.Change(PokeDelayMs, PollMs); } catch { }   // disposed under us
        }

        /// <summary>A renamed device: read its name again the next time it is asked for.</summary>
        private void ForgetName(string id)
        {
            if (string.IsNullOrEmpty(id)) { return; }
            lock (_namesLock) { _names.Remove(id); }
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
                    EnsureNotifications();
                }

                string speaker = QueryDefaultName(DataFlow.Render, "Speaker");
                string mic = QueryDefaultName(DataFlow.Capture, "Microphone");

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

        /// <summary>
        /// The default device's name for a flow, <paramref name="unnamed"/> when it reports none, or
        /// null when there is no default device (or it could not be read — see the debounce).
        /// </summary>
        private string QueryDefaultName(DataFlow flow, string unnamed)
        {
            try
            {
                if (_enum == null) { return null; }
                using (MMDevice dev = _enum.GetDefaultAudioEndpoint(flow, Role.Multimedia))
                {
                    string name = dev == null ? null : FriendlyNameOf(dev, dev.ID);
                    return string.IsNullOrWhiteSpace(name) ? unnamed : name;
                }
            }
            catch
            {
                return null;                       // no active device for this flow
            }
        }

        /// <summary>
        /// An endpoint's friendly name, read once per id and kept. Throws when it cannot be read, and
        /// then nothing is kept, so the next look tries again: queries fail transiently mid-change.
        /// </summary>
        private string FriendlyNameOf(MMDevice dev, string id)
        {
            lock (_namesLock)
            {
                if (_names.TryGetValue(id, out string known)) { return known; }
            }
            string name = dev.FriendlyName;
            lock (_namesLock) { _names[id] = name; }
            return name;
        }

        /// <summary>
        /// Devices Windows will let Tempo open, plus the ones it knows about and will
        /// not: unplugged, disabled, or missing a driver.
        ///
        /// Enumerating only Active was a detection gap rather than a safety measure. A
        /// headset sitting in Windows' own sound panel as "Unplugged", or an input the
        /// user disabled months ago, simply did not appear — which from the outside looks
        /// exactly like Tempo failing to detect hardware that is plainly there. Listing
        /// them with the reason turns "Tempo can't see my headset" into "Windows has your
        /// headset unplugged", which the user can act on.
        ///
        /// NotPresent is deliberately EXCLUDED, and that was measured rather than
        /// reasoned: including it turned this machine's two real speakers into a list of
        /// twelve, ten of them ghost endpoints Windows keeps for hardware that is not
        /// there — four identical "NVIDIA Output", an onboard "Speakers" and "Headphones"
        /// with no driver. Burying the two usable devices among them is worse than the
        /// original problem. Unplugged and Disabled are different: those are devices the
        /// user can plug in or re-enable, so naming them is actionable.
        /// </summary>
        private void RefreshDeviceLists()
        {
            try
            {
                if (_enum == null) { return; }
                const DeviceState Known = DeviceState.Active | DeviceState.Unplugged
                                        | DeviceState.Disabled;

                var speakers = new System.Collections.Generic.List<AudioEndpointInfo>();
                var mics = new System.Collections.Generic.List<AudioEndpointInfo>();
                foreach (DataFlow flow in new[] { DataFlow.Render, DataFlow.Capture })
                {
                    var target = flow == DataFlow.Render ? speakers : mics;
                    foreach (MMDevice dev in _enum.EnumerateAudioEndPoints(flow, Known))
                    {
                        using (dev)
                        {
                            string id = "";
                            try { id = dev.ID; } catch { }
                            if (id.Length == 0) { continue; }
                            DeviceState state;
                            try { state = dev.State; } catch { continue; }
                            string name = null;
                            try { name = FriendlyNameOf(dev, id); } catch { }
                            target.Add(new AudioEndpointInfo
                            {
                                Id = id,
                                Name = AudioDeviceSelection.NameOrUnknown(name, flow),
                                State = state,
                            });
                        }
                    }

                    // Usable first, then by name: enumeration order is not promised to be
                    // stable, and an unstable order both reshuffles the picker and makes
                    // every poll look like a device change.
                    target.Sort((a, b) =>
                    {
                        if (a.Usable != b.Usable) { return a.Usable ? -1 : 1; }
                        return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
                    });
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

        /// <summary>
        /// Compares by content — id, name AND state — so that plugging a listed device
        /// in, or disabling it, counts as a change even though the membership is the
        /// same. Both lists arrive sorted, so index-wise comparison is order-stable.
        /// </summary>
        private static bool SameList(
            System.Collections.Generic.List<AudioEndpointInfo> a,
            System.Collections.Generic.List<AudioEndpointInfo> b)
        {
            if (a.Count != b.Count) { return false; }
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Id != b[i].Id || a[i].Name != b[i].Name || a[i].State != b[i].State)
                {
                    return false;
                }
            }
            return true;
        }

        public void Dispose()
        {
            _disposed = true;
            try
            {
                if (_notifications != null && _notificationsOn != null)
                {
                    _notificationsOn.UnregisterEndpointNotificationCallback(_notifications);
                }
            }
            catch { }
            _notificationsOn = null;
            try { _timer.Dispose(); } catch { }
            try { _enum?.Dispose(); } catch { }
            _enum = null;
        }

        /// <summary>
        /// Windows' own word that a device changed. Every callback only schedules a look: they run
        /// on the audio service's notification thread, which must not be kept waiting, and calling
        /// back into the enumerator from inside one is not allowed.
        /// </summary>
        private sealed class DeviceNotifications : IMMNotificationClient
        {
            private readonly AudioDeviceWatcher _owner;

            public DeviceNotifications(AudioDeviceWatcher owner) { _owner = owner; }

            public void OnDeviceStateChanged(string deviceId, DeviceState newState) => _owner.Poke();
            public void OnDeviceAdded(string pwstrDeviceId) => _owner.Poke();
            public void OnDeviceRemoved(string deviceId) => _owner.Poke();
            public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => _owner.Poke();

            public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
            {
                // Endpoints raise this for many properties, some of them all the time (formats,
                // levels). Only the ones a device's name is made of matter here.
                if (key.formatId != PropertyKeys.PKEY_Device_FriendlyName.formatId &&
                    key.formatId != PropertyKeys.PKEY_DeviceInterface_FriendlyName.formatId)
                {
                    return;
                }
                _owner.ForgetName(pwstrDeviceId);
                _owner.Poke();
            }
        }
    }
}
