using System;
using System.Collections.Generic;
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
                  .Append(" · ")
                  .Append(string.IsNullOrEmpty(_speakerHintWhy) ? "waiting for the first caption" : _speakerHintWhy);
                if (_speakerHintSpaceSwitches > 0)
                {
                    // Face slots and voice profiles are different number spaces, so each
                    // hand-over is a moment the same number can start meaning someone else.
                    sb.Append(" · numbering moved between faces and voices ")
                      .Append(_speakerHintSpaceSwitches).Append('×');
                }
                sb.AppendLine();

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
                      .Append(vp.CurrentSpeaker > 0 ? "voice " + vp.CurrentSpeaker : "no voice");
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
                            // A face's slot number IS its speaker number, and an expired slot
                            // goes to the next new face — so this counts the moments a new
                            // person may have inherited someone else's label.
                            sb.Append(" · ").Append(fa.SlotRecycles)
                              .Append(fa.SlotRecycles == 1 ? " slot" : " slots")
                              .Append(" reused — a face arriving after another left takes its number");
                        }
                        sb.AppendLine();
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
