using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace AutoClicker.UI
{
    /// <summary>
    /// Speaker identification in Live debug and the Health panel: the label actually on
    /// the caption bar, which piece of evidence named it (or why it is holding), and
    /// whether that evidence is even listening to the right audio.
    /// </summary>
    partial class MainForm
    {
        // How long the voice matcher and the caption engine must disagree about the
        // speaker device before Health calls it an issue. Both re-point within a moment
        // of a device change, and a warning that flickers for that moment would only
        // teach people to ignore it.
        private const double SpeakerDeviceMismatchGraceSeconds = 5.0;
        private DateTime _speakerDeviceMismatchSinceUtc = DateTime.MinValue;

        // One speaker identity out of what the face analyzer SEES and the voice profiler HEARS, and the
        // timeline captions are labelled from (see Utils.SpeakerLinker).
        private readonly Utils.SpeakerLinker _speakerLinker = new Utils.SpeakerLinker();
        private System.Threading.Timer _speakerSampleTimer;
        private int _speakerSampleBusy;
        private int _keepPicturesTick;
        private int _speakerSplitCaptions;     // captions cut in two at a hand-over, this session

        /// <summary>Starts feeding the linker ~10 samples a second. Idempotent.</summary>
        private void StartSpeakerSampling()
        {
            if (_speakerSampleTimer != null) { return; }
            try { _speakerSampleTimer = new System.Threading.Timer(_ => SampleSpeakerEvidence(), null, 100, 100); }
            catch (Exception ex) { Utils.Logger.Swallow("StartSpeakerSampling", ex); }
        }

        private void StopSpeakerSampling()
        {
            try { _speakerSampleTimer?.Dispose(); } catch { }
            _speakerSampleTimer = null;
        }

        /// <summary>
        /// One evidence sample into the linker. Runs on a timer thread, and on its own schedule rather
        /// than the UI tick's: that tick stops while Tempo sleeps in the tray, and captions keep running
        /// then. Reads only values the two analyzers publish for other threads.
        /// </summary>
        private void SampleSpeakerEvidence()
        {
            if (System.Threading.Interlocked.Exchange(ref _speakerSampleBusy, 1) != 0) { return; }
            try
            {
                var vp = _voiceProfiler;
                var fa = _faceAnalyzer;
                var tr = _captionTranscriber;
                // Microphone captions: the profiler listens to the speakers and the analyzer watches
                // the window playing sound — neither describes the room the microphone hears.
                bool mic = tr != null && tr.IsRunning && !_captionFellBackToWindows
                           && tr.ActiveMode == Utils.CaptureMode.Microphone;
                bool voice = !mic && vp != null && vp.Running;
                bool face = !mic && fa != null && fa.Running && _settings != null && _settings.CaptionFaceAnalysis;
                // Whether two faces are one person, from the face analyzer's fingerprints (round 26) — whichever analyzer is
                // current when asked (0 = cannot say: no analyzer, no prints).
                if (_speakerLinker.SamePerson == null)
                {
                    _speakerLinker.SamePerson = (a, b) => _faceAnalyzer != null ? _faceAnalyzer.SamePerson(a, b) : 0;
                }
                _speakerLinker.Sample(DateTime.UtcNow, new Utils.SpeakerLinker.Evidence
                {
                    VoiceRunning = voice,
                    ActiveVoice = voice ? vp.ActiveSegmentSpeaker : 0,
                    HeldVoice = voice ? vp.CurrentSpeaker : 0,
                    FaceRunning = face,
                    TalkingFace = face ? fa.CurrentSpeakerOccupant : 0,
                    FacesOnScreen = face ? fa.FaceCount : 0,
                    CrossTalk = face && fa.CrossTalk,
                    VisibleFaces = face ? fa.VisibleOccupants : null,
                    DetectedFaces = face ? fa.DetectedOccupants : null,
                });
                // About once a second: the faces speakers are linked to keep their pictures while passers-by's go first
                // (round 27 — FaceSpeakerAnalyzer.TrimPictures). Outside both locks: the linker's copy, then a set swap.
                if (face && ++_keepPicturesTick % 10 == 0)
                {
                    var keep = new System.Collections.Generic.List<long>();
                    foreach (Utils.SpeakerLinker.SpeakerInfo s in _speakerLinker.Snapshot()) { keep.AddRange(s.Faces); }
                    fa.KeepPicturesOf(keep);
                }
            }
            catch (Exception ex) { Utils.Logger.Swallow("SampleSpeakerEvidence", ex); }
            finally { System.Threading.Interlocked.Exchange(ref _speakerSampleBusy, 0); }
        }

        /// <summary>A new caption session or new audio: forget who everyone was.</summary>
        // ── A new video in the same app (round 30) ───────────────────────────────────────────────────────────────────────
        // Speakers were reset only when the sound moved to another APP: the user named a speaker "iShowSpeed's brother",
        // watched a video by another creator in the same Edge window, and the new man was shown under that name. The title
        // Windows' media controls show for the playing app (MediaTitleWatcher) changes with the video; kept in memory only.
        private Utils.MediaTitleWatcher _mediaTitles;
        private readonly Utils.MediaTitleWatcher.Judge _newVideoJudge = new Utils.MediaTitleWatcher.Judge();
        private int _mediaKeyPid = -1;
        private string _mediaKeyProc;

        // ── Which window the face reader looks at when a BROWSER plays (round 32) ─────────────────────────────────────────
        // The user's 32.png: the video played in a background tab while a page of haircut photos was in front; Windows draws
        // no background tab, so the face reader read the wrong page ("faces on screen: 0" at best). With the playing title
        // (MediaTitleWatcher) the browser's windows can be told apart: the one whose tab in front IS the video (a tab dragged
        // into its own window), a picture-in-picture window, or — for sites whose tab title is the channel ("Streamer -
        // Twitch") — the one naming the channel. None of them: nothing to look at, and the Speakers card says so.
        // (GetWindowThreadProcessId, GetWindowText and IsWindowVisible are declared in MainForm.Clicker.cs / MainForm.cs.)
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }

        private volatile IntPtr _faceWindow;
        private volatile string _faceSkip;
        private long _faceWindowTick;
        private readonly object _faceWindowLock = new object();
        private readonly Dictionary<uint, string> _pidNames = new Dictionary<uint, string>();

        /// <summary>The face reader's window: the browser window showing the playing video, else the audio app's (any thread).</summary>
        private IntPtr FaceReaderWindow()
        {
            ResolveFaceWindow();
            IntPtr w = _faceWindow;
            if (w != IntPtr.Zero) { return w; }
            try { return _mediaDetector != null ? _mediaDetector.CurrentAudioWindow : IntPtr.Zero; } catch { return IntPtr.Zero; }
        }

        /// <summary>Why the face reader should not look now, or null (any thread).</summary>
        private string FaceReaderSkip()
        {
            ResolveFaceWindow();
            string why = _faceSkip;
            return why == null ? null : Utils.Localization.T(why);
        }

        private void ResolveFaceWindow()
        {
            long now = Environment.TickCount64;
            lock (_faceWindowLock)
            {
                if (now - _faceWindowTick < 1000) { return; }
                _faceWindowTick = now;
                _faceWindow = IntPtr.Zero;
                _faceSkip = null;
                _faceWindowRule = null;
                var watcher = _mediaTitles;
                string proc = _mediaKeyProc;
                if (watcher == null || string.IsNullOrEmpty(proc) || !Utils.MediaDetector.IsBrowserProc(proc)) { return; }
                string key = watcher.PlayingKey(proc);
                if (key == null) { return; }   // nothing reported as playing: as before
                int sep = key.LastIndexOf('|');
                string title = (sep >= 0 ? key.Substring(0, sep) : key).Trim();
                string artist = (sep >= 0 ? key.Substring(sep + 1) : "").Trim();
                if (title.Length < 3) { return; }

                IntPtr byTitle = IntPtr.Zero, pip = IntPtr.Zero, byArtist = IntPtr.Zero;
                long byTitleArea = 0;
                var sb = new System.Text.StringBuilder(512);
                EnumWindows((h, l) =>
                {
                    try
                    {
                        if (!IsWindowVisible(h) || IsIconic(h) || !GetWindowRect(h, out NativeRect r)) { return true; }
                        long area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
                        if (r.Right - r.Left < 160 || r.Bottom - r.Top < 120) { return true; }
                        GetWindowThreadProcessId(h, out uint pid);
                        if (!_pidNames.TryGetValue(pid, out string name))
                        {
                            name = Utils.AppAudioCapture.NameOf((int)pid) ?? "";
                            if (_pidNames.Count > 256) { _pidNames.Clear(); }
                            _pidNames[pid] = name;
                        }
                        if (!string.Equals(name, proc, StringComparison.OrdinalIgnoreCase)) { return true; }
                        sb.Clear();
                        GetWindowText(h, sb, sb.Capacity);
                        string wt = sb.ToString();
                        if (wt.Length == 0) { return true; }
                        if (wt.IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            if (area > byTitleArea) { byTitleArea = area; byTitle = h; }
                        }
                        else if (wt.StartsWith("Picture in picture", StringComparison.OrdinalIgnoreCase)
                                 || wt.StartsWith("Picture-in-Picture", StringComparison.OrdinalIgnoreCase))
                        {
                            pip = h;
                        }
                        else if (artist.Length >= 3 && byArtist == IntPtr.Zero && wt.IndexOf(artist, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            byArtist = h;
                        }
                    }
                    catch { }
                    return true;
                }, IntPtr.Zero);

                if (byTitle != IntPtr.Zero)
                {
                    _faceWindow = byTitle;
                    // This site puts the video's title in its tab title (YouTube does; Twitch and Netflix do not): from now
                    // on, no window carrying it means the video's tab is not the one in front.
                    _titleInTabSeen = true;
                    _faceWindowRule = "the browser window whose tab in front is the video";
                }
                else if (pip != IntPtr.Zero) { _faceWindow = pip; _faceWindowRule = "the picture-in-picture window"; }
                else if (byArtist != IntPtr.Zero) { _faceWindow = byArtist; _faceWindowRule = "the browser window naming the channel"; }
                // Only once the title has been seen in a tab: for a site whose tab title never carries it, "no match" says
                // nothing, and faces are read as before.
                else if (_titleInTabSeen) { _faceSkip = "faces: video not on screen"; _faceWindowRule = "none: the video's tab is not in front in any window"; }
            }
        }

        // For Live debug: which of ResolveFaceWindow's rules chose the face reader's window last (null: the audio app's own
        // window, as before round 32), and how the media titles have been judged this session. Never a title.
        private volatile string _faceWindowRule;
        private int _newVideosReset, _newVideosKept, _videosReturnedTo;
        private DateTime _lastNewVideoUtc;

        /// <summary>Live debug's line about the playing app's media title and the face reader's window (no title in it).</summary>
        private void AppendVideoStats(StringBuilder sb)
        {
            var watcher = _mediaTitles;
            sb.Append("  videos: ");
            if (watcher == null) { sb.Append("not watched (captions off)"); }
            else if (watcher.Unavailable) { sb.Append("Windows' media controls can't be read here — a new video is only noticed by a change of app"); }
            else
            {
                string proc = _mediaKeyProc;
                bool playing = !string.IsNullOrEmpty(proc) && watcher.PlayingKey(proc) != null;
                sb.Append(string.IsNullOrEmpty(proc) ? "no app playing yet"
                          : playing ? proc + " has a title playing" : proc + " shows no title (paused, or not a video)");
                sb.Append(" · ").Append(_newVideosReset).Append(" new video(s) → speakers reset");
                if (_newVideosKept > 0) { sb.Append(", ").Append(_newVideosKept).Append(" kept (a reset moments before)"); }
                if (_videosReturnedTo > 0) { sb.Append(" · ").Append(_videosReturnedTo).Append(" return(s) to an earlier video, speakers kept"); }
                if (_newVideosReset > 0) { sb.Append(" · last ").Append(AgoText(DateTime.UtcNow - _lastNewVideoUtc)); }
            }
            sb.AppendLine();
            if (_settings != null && _settings.CaptionFaceAnalysis)
            {
                string rule = _faceWindowRule;
                bool browser = !string.IsNullOrEmpty(_mediaKeyProc) && Utils.MediaDetector.IsBrowserProc(_mediaKeyProc);
                sb.Append("  face reader looks at: ")
                  .Append(rule ?? (browser ? "the browser's own window (the video's tab title has not been seen in any window yet)"
                                           : "the playing app's window"))
                  .AppendLine();
            }
        }

        private static string AgoText(TimeSpan ago)
        {
            if (ago.TotalSeconds < 90) { return (int)Math.Max(0, ago.TotalSeconds) + " s ago"; }
            if (ago.TotalMinutes < 90) { return (int)ago.TotalMinutes + " min ago"; }
            return ago.TotalHours.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " h ago";
        }

        // The playing title has been found in a browser window's title this session (see ResolveFaceWindow).
        private volatile bool _titleInTabSeen;

        private void StartMediaTitleWatch()
        {
            if (_mediaTitles != null) { return; }
            try { _mediaTitles = new Utils.MediaTitleWatcher(); } catch { _mediaTitles = null; }
            _newVideoJudge.Clear();
            _mediaKeyPid = -1;
            _titleInTabSeen = false;
            _faceWindowRule = null;
            _newVideosReset = 0;
            _newVideosKept = 0;
            _videosReturnedTo = 0;
        }

        private void StopMediaTitleWatch()
        {
            try { _mediaTitles?.Dispose(); } catch { }
            _mediaTitles = null;
            _newVideoJudge.Clear();
        }

        /// <summary>
        /// UI thread, with each caption: when the playing app's media title has changed and held a few seconds, to a title
        /// not heard in the last 15 minutes (MediaTitleWatcher.Judge), a new video started — its people are not the last
        /// one's, so the voices, faces, numbering and the NAMES given to them are reset (the same reset as a change of app),
        /// and the spoken language is checked again.
        /// </summary>
        private void CheckForNewVideo(string src)
        {
            var watcher = _mediaTitles;
            if (watcher == null || _mediaDetector == null) { return; }
            int pid = 0;
            try { pid = _mediaDetector.CurrentAudioPid; } catch { }
            if (pid != _mediaKeyPid)
            {
                _mediaKeyPid = pid;
                _mediaKeyProc = pid > 0 ? Utils.AppAudioCapture.NameOf(pid) : null;
                _titleInTabSeen = false;   // another app: its tab titles are its own (ResolveFaceWindow)
            }
            // Null while paused, or for an app with no media title (a game, a call): nothing to judge.
            var verdict = _newVideoJudge.Look(watcher.PlayingKey(_mediaKeyProc), DateTime.UtcNow);
            if (verdict == Utils.MediaTitleWatcher.Verdict.Same) { return; }
            if (verdict == Utils.MediaTitleWatcher.Verdict.NewVideo) { _titleInTabSeen = false; }   // maybe another site
            DateTime now = DateTime.UtcNow;
            if (verdict == Utils.MediaTitleWatcher.Verdict.BackToEarlier)
            {
                _videosReturnedTo++;
                Utils.Logger.Info("[Captions] back to a video that played a moment ago in " + src + " — speakers kept.");
                return;
            }
            if ((now - _lastSpeakerResetUtc).TotalSeconds < 20)
            {
                _newVideosKept++;
                Utils.Logger.Info("[Captions] a new video started in " + src + " — speakers kept (they were reset moments ago).");
                return;
            }
            _lastSpeakerResetUtc = now;
            _newVideosReset++;
            _lastNewVideoUtc = now;
            try { _voiceProfiler?.ForgetVoices(); } catch { }
            _speakerTurns.Reset();
            ResetSpeakerIdentities();
            try { _captionTranscriber?.RecheckLanguage("a new video started"); } catch { }
            // The title itself stays out of the log.
            Utils.Logger.Info("[Captions] a new video started in " + src + " (the title in Windows' media controls changed) — "
                              + "speakers, their names and numbering reset, language to be checked.");
            try { RefreshCaptionsTab(); } catch { }
        }

        private void ResetSpeakerIdentities()
        {
            _speakerLinker.Reset();
            _speakerSplitCaptions = 0;
            // Their names too: the next "Speaker 1" is whoever talks first in the new audio.
            _speakerNames.Clear();
            // And the faces the analyzer would recognise on their return: different audio, different people.
            try { _faceAnalyzer?.ForgetFaces(); } catch { }
        }

        // The names given to speakers this session, by label — with the incarnation of that label
        // they were given to (SpeakerLinker.Incarnation), so a number handed to someone new does not
        // arrive wearing the previous person's name. UI thread only. Never saved: the people behind
        // the numbers are only known for as long as the audio runs.
        private readonly Dictionary<int, KeyValuePair<int, string>> _speakerNames =
            new Dictionary<int, KeyValuePair<int, string>>();

        /// <summary>
        /// The name <paramref name="label"/> was given, or null for "Speaker N". The caption bar, the
        /// transcript and the Speakers card all ask here.
        /// </summary>
        private string SpeakerDisplayName(int label)
        {
            if (label <= 0 || !_speakerNames.TryGetValue(label, out KeyValuePair<int, string> named)) { return null; }
            return named.Key == _speakerLinker.Incarnation(label) ? named.Value : null;
        }

        /// <summary>A speaker on the Speakers card was clicked: ask what to call them.</summary>
        private void RenameSpeaker(int label)
        {
            if (label <= 0) { return; }
            try
            {
                var fa = _faceAnalyzer;
                Utils.SpeakerLinker.SpeakerInfo info = _speakerLinker.Snapshot().Find(s => s.Label == label);
                Bitmap face = null;
                try
                {
                    face = info != null && info.Faces.Count > 0 && fa != null && _settings != null && _settings.CaptionFaceAnalysis
                        ? fa.GetBestFaceThumbnail(info.Faces) : null;
                    string name = SpeakerNameForm.Ask(this, _theme, label, SpeakerDisplayName(label), face,
                                                      CaptionOverlayForm.SpeakerColor(label));
                    if (name != null) { SetSpeakerName(label, name); }
                }
                finally { face?.Dispose(); }
            }
            catch (Exception ex) { Utils.Logger.Warn("[Captions tab] naming a speaker failed: " + ex.Message); }
        }

        /// <summary>
        /// Names <paramref name="label"/> (or, with an empty name, takes the name away) and shows it
        /// everywhere at once: the lines they already spoke, the caption bar, the history window and the card.
        /// </summary>
        internal void SetSpeakerName(int label, string name)
        {
            if (label <= 0) { return; }
            name = SpeakerNameForm.Clean(name);
            if (name.Length == 0) { _speakerNames.Remove(label); }
            else { _speakerNames[label] = new KeyValuePair<int, string>(_speakerLinker.Incarnation(label), name); }
            // What went into the log is the fact, not the name: names are the user's business.
            Utils.Logger.Info("[captions] speaker " + label + (name.Length == 0 ? " back to its number" : " given a name"));

            int changed = _transcript.Relabel(label);
            if (changed > 0)
            {
                _capRenderedText = null;          // the tab's copy is text-compared; force it
                OnTranscriptChanged();
            }
            try { _captionOverlay?.RefreshSpeakerNames(); } catch { }
            RefreshCaptionSpeakers();
        }

        /// <summary>
        /// The speaker block of Live debug.
        ///
        /// It used to be one fragment on the end of the audio-source line — "speaker:
        /// voice 1 / face 2" — printing numbers from two different number spaces side by
        /// side. It never said which of them named the caption, that the labeler was
        /// deliberately HOLDING, that the voice matcher was listening to a different
        /// device from the captions, or that its voice table had filled up and started
        /// merging people.
        /// </summary>
        private void AppendSpeakerStats(StringBuilder sb)
        {
            try
            {
                if (_settings == null || !_settings.CaptionSpeakerTurns)
                {
                    sb.Append("Speakers: labels off").AppendLine();
                    return;
                }
                if (!_captionsActive)
                {
                    sb.Append("Speakers: labels on · captions are off").AppendLine();
                    return;
                }

                int onBar = _speakerTurns.CurrentSpeaker;
                sb.Append("Speakers: bar shows ")
                  .Append(onBar > 0 ? "Speaker " + onBar : "no label yet")
                  // Whether they were named, not the name: Live debug gets pasted into bug reports.
                  .Append(onBar > 0 && SpeakerDisplayName(onBar) != null ? " (named)" : "")
                  .Append(" · now: ")
                  .Append(string.IsNullOrEmpty(_speakerLinker.Why) ? "waiting for the first sample" : _speakerLinker.Why);
                if (_speakerSplitCaptions > 0)
                {
                    sb.Append(" · ").Append(_speakerSplitCaptions)
                      .Append(_speakerSplitCaptions == 1 ? " caption" : " captions").Append(" split at a hand-over");
                }
                int named = 0;
                foreach (int label in _speakerNames.Keys) { if (SpeakerDisplayName(label) != null) { named++; } }
                if (named > 0)
                {
                    sb.Append(" · ").Append(named).Append(named == 1 ? " speaker named" : " speakers named");
                }
                sb.AppendLine();
                // Who is who: each speaker's voices and face, and each face's link evidence.
                string links = _speakerLinker.DebugDetail();
                if (!string.IsNullOrEmpty(links)) { sb.Append(links); }

                var vp = _voiceProfiler;
                if (vp != null && vp.Running)
                {
                    sb.Append("  voice matching: listening to ").Append(vp.DeviceName ?? "the default output");
                    if (vp.UsedFallback)
                    {
                        sb.Append(" (the chosen speaker is unavailable — fell back to the default)");
                    }
                    sb.Append(" · ").Append(vp.ProfileCount).Append(" of ").Append(vp.ProfileCapacity)
                      .Append(" voices learned · now ")
                      .Append(vp.ActiveSegmentSpeaker > 0 ? "voice " + vp.ActiveSegmentSpeaker + " speaking"
                              : vp.CurrentSpeaker > 0 ? "voice " + vp.CurrentSpeaker + " (last heard)" : "no voice");
                    if (vp.MidSpeechChanges > 0)
                    {
                        sb.Append(" · ").Append(vp.MidSpeechChanges).Append(" hand-over(s) caught without a pause");
                    }
                    if (vp.CaptureOpens > 1)
                    {
                        sb.Append(" · capture reopened ").Append(vp.CaptureOpens - 1).Append('×');
                    }
                    sb.AppendLine();

                    if (SpeakerDevicesDiffer(out string captureName))
                    {
                        sb.Append("  ⚠ voice matching hears \"").Append(vp.DeviceName)
                          .Append("\" but captions capture \"").Append(captureName)
                          .Append("\" — labels are being judged from different audio").AppendLine();
                    }
                    if (vp.ForcedMerges > 0)
                    {
                        sb.Append("  ⚠ voice table full (").Append(vp.ProfileCount).Append('/')
                          .Append(vp.ProfileCapacity).Append(") — ").Append(vp.ForcedMerges)
                          .Append(vp.ForcedMerges == 1 ? " newer voice was" : " newer voices were")
                          .Append(" merged into the closest match, so two people can share a number")
                          .AppendLine();
                    }

                    // Per-voice detail: each learned voice's fingerprint (pitch, brightness,
                    // intonation) and evidence — why "Speaker N" is who it is. Kept here,
                    // under the matcher it belongs to, rather than below the face detail.
                    string vd = vp.DebugDetail();
                    if (!string.IsNullOrEmpty(vd)) { sb.Append(vd); }
                    // The voice-fingerprint model on its way (round 34): how much of it has come.
                    if (Utils.VoicePrint.Downloading)
                    {
                        sb.Append("    voice-fingerprint model: downloading — ").Append(Utils.VoicePrint.DownloadedBytes / 1048576)
                          .Append(" of ").Append(Utils.VoicePrint.ModelBytes / 1048576).Append(" MB").AppendLine();
                    }
                }
                else
                {
                    sb.Append("  voice matching: not running — turns are counted from pauses").AppendLine();
                }

                if (_settings.CaptionFaceAnalysis)
                {
                    var fa = _faceAnalyzer;
                    if (fa != null && fa.Running)
                    {
                        sb.Append("  face analysis: ").Append(fa.FaceCount)
                          .Append(fa.FaceCount == 1 ? " face" : " faces")
                          .Append(" · ").Append(fa.TalkingFaceCount).Append(" talking · verdict ")
                          .Append(fa.CurrentVisualSpeaker > 0 ? "face " + fa.CurrentVisualSpeaker : "none");
                        if (fa.SlotRecycles > 0)
                        {
                            // Speakers are linked to the person in a slot, not the slot, so a newcomer
                            // taking a departed face's slot starts unlinked rather than inheriting.
                            sb.Append(" · ").Append(fa.SlotRecycles)
                              .Append(fa.SlotRecycles == 1 ? " slot" : " slots")
                              .Append(" reused (each newcomer is linked afresh)");
                        }
                        // Round 32: not looking at all while the video's tab is not on screen — speakers by voice alone.
                        string skipped = fa.SkippedBecause;
                        if (!string.IsNullOrEmpty(skipped)) { sb.Append(" · NOT LOOKING (").Append(skipped).Append(") — speakers by voice alone"); }
                        sb.AppendLine();
                        // Recognising returning faces (Utils/FaceAppearance.cs): who came back and was
                        // known again, and who turned up where someone else had been.
                        sb.Append("  face recognition: ").Append(fa.Reidentified)
                          .Append(fa.Reidentified == 1 ? " face" : " faces").Append(" known again on return · ")
                          .Append(fa.ChangedAtCut).Append(" new person caught where another had been · ")
                          .Append(fa.KnownFaceCount).Append(" off screen, remembered").AppendLine();
                    }
                    else
                    {
                        sb.Append("  face analysis: on in Settings, but not running").AppendLine();
                    }
                }
                AppendVideoStats(sb);
            }
            catch (Exception ex)
            {
                sb.Append("Speakers: (probe error: ").Append(ex.Message).Append(')').AppendLine();
            }
        }

        /// <summary>
        /// True when the voice matcher and Tempo's caption engine are both listening to
        /// speakers, but to DIFFERENT outputs. They run separate captures, so this can
        /// happen — and then every speaker label describes audio other than the words
        /// being captioned. Microphone captions are excluded: those hear a different
        /// device by design, and <see cref="EffectiveSpeakerHint"/> ignores the matcher then.
        /// </summary>
        private bool SpeakerDevicesDiffer(out string captureDeviceName)
        {
            captureDeviceName = null;
            var vp = _voiceProfiler;
            var tr = _captionTranscriber;
            if (vp == null || !vp.Running || tr == null || !tr.IsRunning || _captionFellBackToWindows)
            {
                return false;
            }
            if (tr.ActiveMode != Utils.CaptureMode.SystemAudio)
            {
                return false;
            }
            string voiceId = vp.DeviceId;
            string captureId = tr.CaptureDeviceId;
            if (string.IsNullOrEmpty(voiceId) || string.IsNullOrEmpty(captureId)
                || string.Equals(voiceId, captureId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            captureDeviceName = tr.CaptureDeviceName;
            return true;
        }

        /// <summary>Speaker checks for the Health panel; called from DetectIssues.</summary>
        private void AddSpeakerIssues(List<string> issues)
        {
            try
            {
                if (_settings == null || !_settings.CaptionSpeakerTurns || !_captionsActive
                    || !SpeakerDevicesDiffer(out string captureName))
                {
                    _speakerDeviceMismatchSinceUtc = DateTime.MinValue;
                    return;
                }
                DateTime now = DateTime.UtcNow;
                if (_speakerDeviceMismatchSinceUtc == DateTime.MinValue)
                {
                    _speakerDeviceMismatchSinceUtc = now;
                }
                if ((now - _speakerDeviceMismatchSinceUtc).TotalSeconds >= SpeakerDeviceMismatchGraceSeconds)
                {
                    issues.Add("⚠ Speaker labels are being judged from \"" + _voiceProfiler.DeviceName +
                               "\" while captions capture \"" + captureName + "\", so the labels follow " +
                               "different audio. Turn captions off and on to put both on the same speaker.");
                }
            }
            catch { }
        }

        /// <summary>
        /// Names the device captions are set to use, for the Devices line.
        ///
        /// That line printed the WINDOWS DEFAULT's name and tagged it "[chosen]" whenever
        /// any device had been picked at all — so with a headset chosen and the monitor
        /// speakers as the default, it named the monitor as the user's choice.
        /// </summary>
        private static string DescribeChosenDevice(
            List<Utils.AudioEndpointInfo> list, string chosenId, string defaultName)
        {
            string def = defaultName ?? "none";
            if (string.IsNullOrEmpty(chosenId))
            {
                return def + " [default]";
            }
            Utils.AudioEndpointInfo d = FindDevice(list, chosenId);
            if (d == null)
            {
                return "⚠ the chosen device is gone — using " + def + " [default]";
            }
            if (!d.Usable)
            {
                return "⚠ " + d.Name + " [chosen, " + d.State.ToString().ToLowerInvariant() +
                       "] — using " + def + " [default]";
            }
            return string.Equals(d.Name, defaultName, StringComparison.Ordinal)
                ? d.Name + " [chosen · also the Windows default]"
                : d.Name + " [chosen] (Windows default: " + def + ")";
        }
    }
}
