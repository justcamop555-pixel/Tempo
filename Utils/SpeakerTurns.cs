using System;
using System.Collections.Generic;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Adds "Speaker 1:", "Speaker 2:" ... labels to caption lines by detecting
    /// speaker TURNS from the rhythm of the transcript, and shows only the current
    /// turn's words after the label so the label always stays on screen.
    ///
    /// Honest scope: neither Windows Live Captions nor Tempo's offline engine says
    /// WHO is talking — there is no voice identification in the pipeline. The one
    /// thing we can detect reliably is a turn boundary: speech paused, then new
    /// words arrived. That silence-then-new-words pattern is how broadcast captions
    /// mark a speaker change (">>"); each turn advances the label. It cannot tell
    /// two people apart by voice, and one person pausing a long time then continuing
    /// counts as a new turn — same limitation every caption system has without a
    /// microphone array.
    ///
    /// The label word is translated, and BOTH the writer here and the reader in
    /// CaptionOverlayForm go through <see cref="SpeakerWord"/> to get it. They have to:
    /// the overlay colours a turn by finding the label in the finished caption text,
    /// so if the two ever disagreed about the word, every speaker turn would quietly
    /// lose its colour — a failure with no error and no log line, visible only as
    /// captions that stopped being colour-coded.
    ///
    /// Two things make Windows Live Captions awkward to read this way, both handled:
    ///  • It REFLOWS its line — re-capitalising and re-punctuating earlier words as
    ///    it gains confidence, even while no one is speaking. So "the text changed"
    ///    is NOT a reliable silence signal. We instead time the gaps between moments
    ///    the line GROWS (gets longer): reflow barely changes length, real speech
    ///    extends the tail. Silence = a long gap between growths.
    ///  • It keeps one long running line rather than tidy per-utterance lines. When a
    ///    turn begins we snapshot the line as a "baseline" and display only the text
    ///    past the longest common prefix with it — the words added this turn.
    /// </summary>
    public sealed class SpeakerTurnLabeler
    {
        /// <summary>
        /// New words at least this long after the previous words = the next turn.
        /// This must be tuned to the SOURCE's delivery cadence: Windows Live Captions
        /// streams updates every ~250 ms, so 1.5 s of quiet is a real pause; Tempo's
        /// own engine delivers text in ~2.5 s batches even during continuous speech,
        /// so its turn gap must sit ABOVE that cadence or every batch looks like a
        /// new speaker. The caller sets this per source (default suits Windows).
        /// </summary>
        public double TurnGapSeconds { get; set; } = 1.5;
        // With VOICE evidence of a different speaker, a much shorter pause already
        // counts as a turn (quick back-and-forth conversation).
        private const double VoiceTurnGapSeconds = 0.5;
        // A gap this long = a new conversation; numbering restarts at 1.
        private const double ResetGapSeconds = 30.0;
        // After this many speakers, wrap back to 1 (labels stay short and readable).
        private const int MaxSpeakers = 9;

        private string _lastRaw = string.Empty;
        private string _baseline = string.Empty;   // line snapshot when this turn began
        private DateTime _lastGrowUtc = DateTime.MinValue;
        private DateTime _turnStartUtc = DateTime.MinValue;
        private int _speaker;
        private bool _primeNeeded = true;           // exclude text already on screen at start

        // The voice profiler needs ~a second of audio before it can say WHOSE voice a
        // new turn is, but the first words usually reach the screen sooner — so a turn
        // may start under the previous speaker's number. While the turn is younger
        // than this, a differing voice verdict corrects the number in place.
        private const double VoiceAdoptWindowSeconds = 4.0;

        // ── Conversation continuity ──────────────────────────────────────────
        // When the speaker CHANGES, the finished turn no longer vanishes from the
        // bar — it stays in front of the new one ("Speaker 1: … park.  Speaker 2:
        // I'll see you later."), sliding off only when space runs out. That reads
        // like a conversation instead of the bar wiping on every hand-over (the
        // overlay colours every "Speaker N:" it finds, so each kept turn keeps its
        // speaker colour too). Finished turns are trimmed to their newest words at
        // push time so history stays compact.
        private readonly List<KeyValuePair<int, string>> _history = new List<KeyValuePair<int, string>>();
        private const int HistoryKeep = 3;          // finished turns retained at most
        private const int HistoryTurnChars = 90;    // each kept turn's max length
        private const int TotalBudgetChars = 220;   // whole assembled line budget
        private string _lastShown = string.Empty;   // current turn's latest shown text

        /// <summary>
        /// The word that prefixes a speaker turn, in the reader's language.
        ///
        /// The single source of truth for it. The caption bar re-reads finished lines
        /// to colour each turn, so the word MUST be identical on both sides — and it
        /// must be ONE word, because the bar treats "&lt;label&gt; N:" as a two-token
        /// unit so a label never wraps away from its number.
        /// </summary>
        public static string SpeakerWord
        {
            get { return Localization.T("Speaker"); }
        }

        /// <summary>"Speaker 3: " — the full prefix for a turn.</summary>
        public static string Prefix(int speaker)
        {
            return SpeakerWord + " " + speaker + ": ";
        }

        // When a turn ends but the speaker NUMBER can't change yet (voice-driven mode
        // holds it until the profiler names the new voice ~a second later), the finished
        // turn's text is stashed here so the delayed voice verdict can still file it into
        // history — otherwise _lastShown was already overwritten and the previous
        // speaker's words vanished from the bar instead of staying in front.
        private string _pendingHandoverText;
        private int _pendingHandoverSpeaker;

        /// <summary>
        /// True while a live VOICE verdict source (the voice profiler) is running.
        /// Changes what a silent pause means: with voices in play, a pause alone no
        /// longer mints a NEW speaker number — the voice verdict is what changes it.
        /// Without voices, the pause count-up heuristic remains the only signal.
        /// </summary>
        public bool VoiceDriven { get; set; }

        /// <summary>Forget everything — the next new words become "Speaker 1:".</summary>
        public void Reset()
        {
            _lastRaw = string.Empty;
            _baseline = string.Empty;
            _lastGrowUtc = DateTime.MinValue;
            _turnStartUtc = DateTime.MinValue;
            _speaker = 0;
            _primeNeeded = true;
            _history.Clear();
            _lastShown = string.Empty;
            _pendingHandoverText = null;
        }

        /// <summary>
        /// Tells the labeler that the caller shed <paramref name="chars"/> characters
        /// off the FRONT of the rolling line BEFORE this Label() call. The Tempo engine
        /// appends AND front-trims in one atomic update, so Label()'s own net-shrink roll
        /// detection can't see the shed — left uncorrected, the turn baseline's front
        /// stays misaligned, FuzzyCutIndex mismatches at index 0, and the labeler is
        /// permanently stuck in the whole-line fallback (leaking prior turns' words under
        /// the current speaker). Shaving baseline+lastRaw by the same amount keeps them
        /// aligned. Mirrors the Windows-mirror path's roll rebase.
        /// </summary>
        public void NoteFrontShed(int chars)
        {
            if (chars <= 0) { return; }
            if (_lastRaw.Length > 0)
            {
                _lastRaw = chars >= _lastRaw.Length ? string.Empty : _lastRaw.Substring(chars);
            }
            if (_baseline.Length > 0)
            {
                _baseline = chars >= _baseline.Length ? string.Empty : _baseline.Substring(chars);
            }
        }

        /// <summary>
        /// Files the just-finished turn into the on-bar history (called at a speaker
        /// hand-over). The turn keeps only its newest words — sentence-preferred cut,
        /// ellipsis when mid-sentence — so history entries stay compact.
        /// </summary>
        private void PushTurnToHistory(int speaker)
        {
            PushTurnToHistory(speaker, _lastShown);
        }

        private void PushTurnToHistory(int speaker, string text)
        {
            if (speaker <= 0 || string.IsNullOrWhiteSpace(text))
            {
                return;
            }
            string t = text;
            if (t.Length > HistoryTurnChars)
            {
                int from = t.Length - HistoryTurnChars;
                int cut = -1;
                int searchEnd = Math.Min(t.Length - 25, from + 50);
                for (int i = from; i < searchEnd; i++)
                {
                    int r = SentenceCutAfter(t, i);
                    if (r > 0) { cut = r; break; }
                }
                if (cut > 0)
                {
                    t = t.Substring(cut);
                }
                else
                {
                    int sp = t.IndexOf(' ', from);
                    t = "… " + (sp >= 0 && sp < t.Length - 15 ? t.Substring(sp + 1) : t.Substring(from));
                }
            }
            _history.Add(new KeyValuePair<int, string>(speaker, t));
            while (_history.Count > HistoryKeep)
            {
                _history.RemoveAt(0);
            }
        }

        /// <summary>
        /// Labels one caption update. Call with each raw text change, in order;
        /// returns the current turn's text with its speaker prefix.
        /// <paramref name="voiceSpeaker"/> is the voice-profiler's verdict on whose
        /// VOICE is currently talking (1-based; 0 = no voice matching available).
        /// When present it names the turn — so a returning speaker gets their old
        /// number back — and a changed voice can even split a turn that a short
        /// pause alone wouldn't have.
        /// </summary>
        public string Label(string raw, int voiceSpeaker = 0)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return raw;
            }

            DateTime now = DateTime.UtcNow;

            // Whatever text is already on screen the moment captions turn on is not
            // part of any turn we should show — baseline it out so the first label
            // covers only words spoken from here on.
            if (_primeNeeded)
            {
                _primeNeeded = false;
                _baseline = raw;
                _lastRaw = raw;
                _lastGrowUtc = now;
                _turnStartUtc = now;
                _speaker = voiceSpeaker > 0 ? voiceSpeaker : 1;
                _lastShown = LastSentence(raw);
                _pendingHandoverText = null;
                return Prefix(_speaker) + _lastShown;
            }

            bool grew = raw.Length > _lastRaw.Length;
            if (grew)
            {
                double gap = _lastGrowUtc == DateTime.MinValue
                    ? double.MaxValue
                    : (now - _lastGrowUtc).TotalSeconds;

                int prevSpeaker = _speaker;
                if (_speaker == 0)
                {
                    _speaker = voiceSpeaker > 0 ? voiceSpeaker : 1;  // first real words
                    _baseline = _lastRaw;                            // exclude primed text
                    _turnStartUtc = now;
                    _pendingHandoverText = null;
                }
                else if (gap >= ResetGapSeconds)
                {
                    _speaker = voiceSpeaker > 0 ? voiceSpeaker : 1;  // new conversation
                    _baseline = _lastRaw;
                    _turnStartUtc = now;
                    _history.Clear();                                // old conversation is over
                    _pendingHandoverText = null;
                }
                else if (gap >= TurnGapSeconds)
                {
                    // Turn boundary: the voice verdict names the speaker when we have
                    // one (returning voices keep their number). With NO verdict yet:
                    //  · voice-driven mode HOLDS the current number — a lone pause is
                    //    not evidence of a new person (one narrator pausing >4 s was
                    //    getting renumbered 1→2→3 mid-video); if it really is someone
                    //    new, the voice verdict lands within ~a second and the
                    //    adopt-window below corrects the number in place.
                    //  · without voices (profiler off / no device) the old count-up
                    //    heuristic is still the only signal there is.
                    _speaker = voiceSpeaker > 0
                        ? voiceSpeaker
                        : VoiceDriven
                            ? _speaker
                            : (_speaker >= MaxSpeakers ? 1 : _speaker + 1);
                    // The previous speaker's words stay on the bar (conversation
                    // continuity) — but only at a real HAND-OVER; the same speaker
                    // pausing keeps one running turn, no duplicate label.
                    if (_speaker != prevSpeaker)
                    {
                        PushTurnToHistory(prevSpeaker);
                        _pendingHandoverText = null;
                    }
                    else
                    {
                        // Number held (voice-driven, verdict not in yet). Stash the
                        // finished turn so the delayed verdict can still file it —
                        // _lastShown is about to be overwritten by the new turn.
                        _pendingHandoverText = _lastShown;
                        _pendingHandoverSpeaker = prevSpeaker;
                    }
                    _baseline = _lastRaw;                            // words before this turn
                    _turnStartUtc = now;
                }
                else if (_pendingHandoverText != null && voiceSpeaker > 0 &&
                         voiceSpeaker != _speaker &&
                         _turnStartUtc != DateTime.MinValue &&
                         (now - _turnStartUtc).TotalSeconds <= VoiceAdoptWindowSeconds)
                {
                    // The turn-boundary above held the number (no verdict yet) and
                    // stashed the finished turn. The verdict is in now and the turn is
                    // still young: file the stashed turn into history under its speaker
                    // and adopt the new number IN PLACE — same turn, right speaker, and
                    // the previous speaker's words stay on the bar as intended.
                    PushTurnToHistory(_pendingHandoverSpeaker, _pendingHandoverText);
                    _pendingHandoverText = null;
                    _speaker = voiceSpeaker;
                }
                else if (voiceSpeaker > 0 && voiceSpeaker != _speaker && gap >= VoiceTurnGapSeconds)
                {
                    // The VOICE changed even though the pause was short (fast
                    // back-and-forth). Trust the voice: new turn, their number.
                    _speaker = voiceSpeaker;
                    PushTurnToHistory(prevSpeaker);
                    _pendingHandoverText = null;
                    _baseline = _lastRaw;
                    _turnStartUtc = now;
                }
                else if (voiceSpeaker > 0 && voiceSpeaker != _speaker &&
                         _turnStartUtc != DateTime.MinValue &&
                         (now - _turnStartUtc).TotalSeconds <= VoiceAdoptWindowSeconds)
                {
                    // The turn started before the voice profiler had heard enough of
                    // the new voice to name it (it needs ~a second of audio), so the
                    // first words went out under the PREVIOUS speaker's number. The
                    // verdict is in now and the turn is still young: correct the
                    // number in place — same turn, same words, right speaker.
                    _speaker = voiceSpeaker;
                }

                _lastGrowUtc = now;
            }
            else if (raw.Length < _lastRaw.Length - 10)
            {
                // The line ROLLED — dropped a chunk of old words from the FRONT to
                // make room. That only happens while recognition is actively running,
                // so it counts as speech activity: refresh the clock, or the roll
                // would stall the growth timer mid-sentence and the next words would
                // measure a bogus "pause" and get labelled as a brand-new turn.
                _lastGrowUtc = now;

                // The roll also ate the front of THIS TURN'S BASELINE. Left alone,
                // FuzzyCutIndex(raw, baseline) then mismatches at the very first
                // letter, returns 0, and the WHOLE rolled line — previous turns
                // included — leaks back out under the current speaker's label
                // ("Speaker 1: ..go that park. I'll see you later."). Rebase by
                // shedding from the baseline's front what the line itself lost.
                int lost = _lastRaw.Length - raw.Length;
                if (_baseline.Length > 0 && lost > 0)
                {
                    _baseline = lost >= _baseline.Length
                        ? string.Empty
                        : _baseline.Substring(lost);
                }
            }
            // Any other non-growing change is a reflow of existing words — same turn.

            _lastRaw = raw;

            if (_speaker == 0)
            {
                _speaker = voiceSpeaker > 0 ? voiceSpeaker : 1;      // grew==false first pass
            }

            // Show the words added since this turn began. The cut point is found by a
            // punctuation/case-INSENSITIVE walk over the baseline: Windows loves to go
            // back and re-punctuate or re-capitalise the older words (an exact prefix
            // comparison then "diverges" inside the previous turn and old words leak
            // back into view); comparing only letters and digits survives that.
            int cut = FuzzyCutIndex(raw, _baseline);
            // Baseline unrecognisable (cut found nothing despite a baseline existing:
            // a reflow beyond the fuzzy walk, or a roll the rebase above couldn't
            // fully absorb) — fall back to the last sentence, NEVER the whole line;
            // dumping everything is how old turns' words end up under the wrong label.
            string shown = cut == 0 && _baseline.Length > 0
                ? LastSentence(raw)
                : raw.Substring(Math.Min(cut, raw.Length)).TrimStart(' ', '.', ',', '!', '?');
            if (shown.Length == 0)
            {
                shown = LastSentence(raw);   // bounded fallback — never dump the whole line
            }

            // One speaker talking for a while builds a long turn; the overlay trims
            // overflowing text from the LEFT, which would cut the label off first.
            // Cap the turn text to its newest words so "Speaker N:" always survives.
            // Sized for the three-line bar (~180 chars) minus the label and the
            // "♪ AppName ·" source prefix.
            const int MaxShownChars = 140;
            if (shown.Length > MaxShownChars)
            {
                int from = shown.Length - MaxShownChars;
                // Prefer starting at a SENTENCE boundary just past the cut — "… I'll
                // see you later." reads clean, while a word-boundary cut mid-sentence
                // ("… go to that park.") is the dangling fragment users read as a
                // glitch. Only when no sentence starts nearby, fall back to the word
                // boundary and say so with the ellipsis.
                int sentence = -1;
                int searchEnd = Math.Min(shown.Length - 20, from + 60);
                for (int i = from; i < searchEnd; i++)
                {
                    int r = SentenceCutAfter(shown, i);
                    if (r > 0) { sentence = r; break; }
                }
                if (sentence > 0)
                {
                    shown = shown.Substring(sentence);          // clean sentence start
                }
                else
                {
                    int space = shown.IndexOf(' ', from);
                    if (space >= 0 && space < shown.Length - 20)
                    {
                        from = space + 1;    // start at a word boundary
                    }
                    shown = "… " + shown.Substring(from);
                }
            }

            _lastShown = shown;

            // Assemble: kept previous turns in front of the live one, newest-first
            // fitting into the bar budget — older turns slide off as space runs out,
            // exactly like conversation captions on TV. Each keeps its own label (and
            // therefore its own colour on the bar).
            string current = Prefix(_speaker) + shown;
            if (_history.Count == 0)
            {
                return current;
            }
            int budget = TotalBudgetChars - current.Length;
            int start = _history.Count;
            while (start > 0)
            {
                // Measure the label instead of assuming 12 characters: that constant
                // was the width of "Speaker N: " plus the two-space separator, and it
                // silently under- or over-counts once the word is translated
                // (German "Sprecher", Italian "Parlante"), which would let the bar
                // overrun its budget or drop a turn it had room for.
                int cost = _history[start - 1].Value.Length
                         + Prefix(_history[start - 1].Key).Length + 2;
                if (budget - cost < 0) { break; }
                budget -= cost;
                start--;
            }
            var sbOut = new System.Text.StringBuilder();
            for (int i = start; i < _history.Count; i++)
            {
                sbOut.Append(Prefix(_history[i].Key))
                     .Append(_history[i].Value).Append("  ");
            }
            sbOut.Append(current);
            return sbOut.ToString();
        }

        /// <summary>
        /// If a sentence ends at index <paramref name="i"/> of <paramref name="s"/>,
        /// returns the index where the NEXT sentence resumes; otherwise -1. Handles
        /// Latin marks (need a following space) and CJK full-stop marks 。！？ (no space
        /// follows in Chinese/Japanese, so these were previously never matched — the
        /// labeler then fell back to dumping the whole line under one speaker).
        /// </summary>
        internal static int SentenceCutAfter(string s, int i)
        {
            if (i < 0 || i >= s.Length) { return -1; }
            char c = s[i];
            if ((c == '.' || c == '?' || c == '!' || c == '…') && i + 1 < s.Length && s[i + 1] == ' ')
            {
                return i + 2;
            }
            if (c == '。' || c == '！' || c == '？')
            {
                return i + 1;
            }
            return -1;
        }

        /// <summary>The text after the last sentence-ending mark (bounds fallback length).</summary>
        private static string LastSentence(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return s;
            }
            int best = -1;
            for (int i = 0; i < s.Length; i++)
            {
                int r = SentenceCutAfter(s, i);
                if (r > 0) { best = r; }
            }
            string tail = best >= 0 ? s.Substring(Math.Min(best, s.Length)).TrimStart(' ', '.', ',', '!', '?') : s;
            return tail.Length == 0 ? s : tail;
        }

        /// <summary>
        /// Index in <paramref name="raw"/> where <paramref name="baseline"/>'s content
        /// ends, comparing only letters/digits case-insensitively (punctuation, spaces
        /// and casing may differ — Windows reflows those freely).
        /// </summary>
        private static int FuzzyCutIndex(string raw, string baseline)
        {
            if (string.IsNullOrEmpty(baseline) || string.IsNullOrEmpty(raw))
            {
                return 0;
            }
            int i = 0, j = 0;
            while (i < raw.Length && j < baseline.Length)
            {
                char a = raw[i];
                char b = baseline[j];
                if (!char.IsLetterOrDigit(a)) { i++; continue; }
                if (!char.IsLetterOrDigit(b)) { j++; continue; }
                if (char.ToLowerInvariant(a) != char.ToLowerInvariant(b))
                {
                    break;   // genuinely different words from here on
                }
                i++; j++;
            }
            return i;
        }
    }
}
