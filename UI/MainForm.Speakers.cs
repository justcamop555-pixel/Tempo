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
                });
            }
            catch (Exception ex) { Utils.Logger.Swallow("SampleSpeakerEvidence", ex); }
            finally { System.Threading.Interlocked.Exchange(ref _speakerSampleBusy, 0); }
        }

        /// <summary>A new caption session or new audio: forget who everyone was.</summary>
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
