using System;
using System.Collections.Generic;
using System.Threading;
using Windows.Media.Control;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Notices a NEW VIDEO in the same app: the title Windows' media controls show for what an app is playing (the same
    /// title as the volume flyout's media card — Edge, Chrome and Firefox give it the video's own title and channel).
    ///
    /// Round 30 (2026-11-16): speakers — and the names the user gave them — were reset only when the sound moved to
    /// another APP. The user named a speaker "iShowSpeed's brother", then watched a video by another creator in the same
    /// Edge window, and the new man's words came out under the old name. The window title cannot tell a new video from a
    /// tab switch (the title follows the tab in front while another tab plays); the media title follows the playing tab.
    ///
    /// Read once every 2 s while captions run, kept in memory only: the title is compared, never written to the log or
    /// anywhere else.
    /// </summary>
    public sealed class MediaTitleWatcher : IDisposable
    {
        private const int LookMs = 2000;

        private readonly Timer _timer;
        private GlobalSystemMediaTransportControlsSessionManager _manager;
        private int _busy;
        private volatile bool _disposed;
        private volatile bool _failed;
        // App id (lower case) → "title|artist" of what it is playing now; replaced whole on every look.
        private volatile Dictionary<string, string> _playing = new Dictionary<string, string>(StringComparer.Ordinal);

        public MediaTitleWatcher()
        {
            _timer = new Timer(_ => Look(), null, 0, LookMs);
        }

        /// <summary>
        /// What <paramref name="processName"/> ("msedge", "chrome", "spotify") is playing as "title|artist", or null when
        /// Windows lists nothing playing for it. Matched by the media session's app id, which names the program
        /// ("MSEdge", "Chrome", "Spotify.exe"); when no id matches and only one session is playing, that one.
        /// </summary>
        public string PlayingKey(string processName)
        {
            var playing = _playing;
            if (playing.Count == 0) { return null; }
            string want = (processName ?? "").Trim().ToLowerInvariant();
            if (want.Length > 0)
            {
                foreach (var kv in playing)
                {
                    if (kv.Key.Contains(want) || (want.Length >= 4 && want.Contains(kv.Key))) { return kv.Value; }
                }
            }
            if (playing.Count == 1)
            {
                foreach (var kv in playing) { return kv.Value; }
            }
            return null;
        }

        /// <summary>
        /// Whether Windows' media controls list <paramref name="processName"/> ("msedge", "chrome", "spotify") as PLAYING
        /// right now — strictly by the session's app id, never the "only one session is playing" guess PlayingKey falls
        /// back on (that would make every program "the one playing"). Round 40: which app captions hear alone and name on
        /// the bar prefers the app Windows says is playing over whatever is loudest — a game or a Discord ping out-sounding
        /// the video took both ("hear only that's playing is sometimes not work", "prevent wrong source").
        /// </summary>
        public bool IsPlaying(string processName)
        {
            var playing = _playing;
            if (playing.Count == 0 || string.IsNullOrWhiteSpace(processName)) { return false; }
            string want = processName.Trim().ToLowerInvariant();
            foreach (var kv in playing)
            {
                string app = kv.Key;
                if (app.Length == 0) { continue; }
                if (app.Contains(want) || (want.Length >= 4 && want.Contains(app))) { return true; }
                // Firefox names its sessions by a fixed hash, not by its name.
                if (want == "firefox" && FirefoxAppIds.Contains(app)) { return true; }
            }
            return false;
        }

        // The app id Firefox's media sessions carry when it is installed in its usual folder (Firefox derives the id from its
        // install path, so a copy elsewhere has another and is simply not preferred).
        private static readonly HashSet<string> FirefoxAppIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "308046b0af4a39cb",
        };

        /// <summary>True once Windows' media controls could not be read (an old Windows, the service off): no keys ever.</summary>
        public bool Unavailable => _failed;

        private void Look()
        {
            if (_disposed || _failed || Interlocked.Exchange(ref _busy, 1) != 0) { return; }
            try
            {
                if (_manager == null)
                {
                    _manager = GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().GetAwaiter().GetResult();
                    if (_manager == null) { _failed = true; return; }
                }
                var now = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var session in _manager.GetSessions())
                {
                    try
                    {
                        var info = session.GetPlaybackInfo();
                        if (info == null || info.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) { continue; }
                        var props = session.TryGetMediaPropertiesAsync().AsTask().GetAwaiter().GetResult();
                        string title = props?.Title ?? "";
                        if (title.Length == 0) { continue; }
                        string app = (session.SourceAppUserModelId ?? "").Trim().ToLowerInvariant();
                        if (app.EndsWith(".exe", StringComparison.Ordinal)) { app = app.Substring(0, app.Length - 4); }
                        now[app] = title + "|" + (props.Artist ?? "");
                    }
                    catch { }
                }
                _playing = now;
            }
            catch (Exception ex)
            {
                // Once is enough: the media service is not there, or this Windows has no such controls.
                _failed = true;
                Logger.Info("[Captions] Windows' media controls can't be read (" + ex.GetType().Name
                            + ") — a new video in the same app is noticed by its sound changing app only.");
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        public void Dispose()
        {
            if (_disposed) { return; }
            _disposed = true;
            try { _timer.Dispose(); } catch { }
            _manager = null;
            _playing = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        /// <summary>What a look at the playing title means for the speakers.</summary>
        public enum Verdict { Same, NewVideo, BackToEarlier }

        /// <summary>
        /// Decides, look by look, when the playing title means a NEW video: a different title that holds for
        /// <see cref="SettleSec"/> (a few seconds of another title is not a video), and one not heard in the last
        /// <see cref="EarlierMinutes"/> minutes — going back to a video after an ad or from another tab is not new.
        /// Pure, so the harness can drive it on its own clock.
        /// </summary>
        public sealed class Judge
        {
            public const double SettleSec = 6;
            public const double EarlierMinutes = 15;

            private string _stable, _pending;
            private DateTime _pendingSince;
            private readonly Dictionary<string, DateTime> _seen = new Dictionary<string, DateTime>(StringComparer.Ordinal);

            public void Clear()
            {
                _stable = null;
                _pending = null;
                _seen.Clear();
            }

            /// <summary><paramref name="key"/>: the title now ("title|artist"), null when nothing is playing (no decision).</summary>
            public Verdict Look(string key, DateTime now)
            {
                if (key == null) { return Verdict.Same; }
                if (_stable == null || key == _stable)
                {
                    _stable = key;
                    _pending = null;
                    _seen[key] = now;
                    return Verdict.Same;
                }
                if (key != _pending)
                {
                    _pending = key;
                    _pendingSince = now;
                    return Verdict.Same;
                }
                if ((now - _pendingSince).TotalSeconds < SettleSec) { return Verdict.Same; }
                bool heardBefore = _seen.TryGetValue(key, out DateTime seen) && (now - seen).TotalMinutes < EarlierMinutes;
                _stable = key;
                _pending = null;
                _seen[key] = now;
                if (_seen.Count > 40)
                {
                    var old = new List<string>();
                    foreach (var kv in _seen) { if ((now - kv.Value).TotalMinutes > 2 * EarlierMinutes) { old.Add(kv.Key); } }
                    foreach (string k in old) { _seen.Remove(k); }
                }
                return heardBefore ? Verdict.BackToEarlier : Verdict.NewVideo;
            }
        }
    }
}
