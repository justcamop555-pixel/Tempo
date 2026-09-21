using System;
using System.Collections.Generic;

namespace AutoClicker.Utils
{
    /// <summary>
    /// The running caption transcript: what the Captions tab, the history window, the saved .txt
    /// and the subtitle export all read.
    ///
    /// It used to be built from the caption BAR's text. That text is made for a screen, not a
    /// record: with speaker labels on it is trimmed to the newest ~140 characters of the turn and
    /// re-shows up to three earlier turns in front of it, so every time the trim moved the whole
    /// visible text failed to match the stored line and was stored again. Measured by
    /// scratchpad/transcripttest on 149 spoken words: 481 words kept from Tempo's engine and 945
    /// from the Windows Live Captions mirror, most of them repeats — and a conversation's turns
    /// ended up inside one line.
    ///
    /// Now each surface is fed what was actually SAID:
    ///   • Tempo's engine hands over each chunk of new words once (<see cref="AppendChunk"/>).
    ///   • The Windows mirror hands over the current turn's whole, uncapped text, which is merged
    ///     as Windows grows, reflows and rolls its line (<see cref="AppendRolling"/>).
    /// A line is one speaker turn (a long one breaks at a sentence end), labelled once, and stamped
    /// with when its first words were SPOKEN rather than when the text happened to arrive or last
    /// changed — so exported subtitles line up with the recording they describe.
    /// </summary>
    public sealed class CaptionTranscript
    {
        /// <summary>Lines kept; the oldest go first.</summary>
        public const int MaxLines = 500;

        // A turn that runs on is split at its next sentence end past this length, so a monologue
        // becomes readable paragraphs (and subtitle cues of a sensible size) instead of one line.
        private const int SplitAfterChars = 320;

        // Without speaker labels a pause this long starts a new line: the only turn signal there is.
        private const double PauseSeconds = 4.0;

        /// <summary>The lines, oldest first. Labelled "Speaker N: …" when labels are on.</summary>
        public List<string> Lines { get; } = new List<string>();

        /// <summary>When each line's first words were spoken (local time), index for index.</summary>
        public List<DateTime> Times { get; } = new List<DateTime>();

        /// <summary>When each line's latest words were spoken (local time), index for index.</summary>
        public List<DateTime> EndTimes { get; } = new List<DateTime>();

        private bool _open;
        private int _speaker;
        private DateTime _turn = DateTime.MinValue;
        private string _text = string.Empty;

        /// <summary>Empties the transcript (a new caption session, or the Clear button).</summary>
        public void Clear()
        {
            Lines.Clear();
            Times.Clear();
            EndTimes.Clear();
            _open = false;
            _speaker = 0;
            _turn = DateTime.MinValue;
            _text = string.Empty;
        }

        /// <summary>
        /// Tempo's own engine: one chunk of NEW words, already de-duplicated against the previous
        /// chunk by the engine. <paramref name="speaker"/> is 0 and <paramref name="turnStarted"/>
        /// DateTime.MinValue when speaker labels are off.
        /// </summary>
        public void AppendChunk(string words, int speaker, DateTime turnStarted, DateTime spokenStart, DateTime spokenEnd)
        {
            words = (words ?? string.Empty).Trim();
            if (words.Length == 0) { return; }
            if (spokenEnd < spokenStart) { spokenEnd = spokenStart; }

            if (_open && turnStarted == _turn)
            {
                // The same turn. A number the labeller corrected in place relabels the line: the
                // words were that speaker's all along.
                _speaker = speaker;
                bool paused = speaker == 0 && EndTimes.Count > 0
                              && (spokenStart - EndTimes[EndTimes.Count - 1]).TotalSeconds >= PauseSeconds;
                bool longEnough = _text.Length >= SplitAfterChars && EndsSentence(_text);
                if (!paused && !longEnough)
                {
                    Update(_text + " " + words, spokenEnd);
                    return;
                }
            }
            Start(words, speaker, turnStarted, spokenStart, spokenEnd);
        }

        /// <summary>
        /// Takes the last <paramref name="count"/> words off the open line: Tempo's engine heard them
        /// better in the chunk about to be appended (TempoTranscriber.JoinSeam). Left alone when the
        /// line holds no more words than that — the better hearing then simply follows.
        /// </summary>
        public void RetractWords(int count)
        {
            if (count <= 0 || !_open || Lines.Count == 0) { return; }
            string[] words = _text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= count) { return; }
            Update(string.Join(" ", words, 0, words.Length - count),
                   EndTimes.Count > 0 ? EndTimes[EndTimes.Count - 1] : DateTime.Now);
        }

        /// <summary>
        /// The Windows Live Captions mirror: the current turn's text as Windows shows it right now —
        /// grown, lightly revised, or with its oldest words rolled off the front. With labels off it
        /// is Windows' whole line and the turn never changes.
        /// </summary>
        public void AppendRolling(string turnText, int speaker, DateTime turnStarted, DateTime now)
        {
            turnText = (turnText ?? string.Empty).Trim();
            if (turnText.Length == 0) { return; }

            if (_open && turnStarted == _turn)
            {
                _speaker = speaker;
                string merged = TryMergeOverlap(_text, turnText);
                if (merged != null)
                {
                    Update(merged, now);
                    return;
                }
                if (IsSameUtterance(_text, turnText))
                {
                    Update(turnText.Length >= _text.Length ? turnText : _text, now);
                    return;
                }
            }
            Start(turnText, speaker, turnStarted, now, now);
        }

        private void Start(string text, int speaker, DateTime turn, DateTime start, DateTime end)
        {
            _open = true;
            _speaker = speaker;
            _turn = turn;
            _text = text;
            Lines.Add(Prefix(speaker) + text);
            Times.Add(start);
            EndTimes.Add(end);
            if (Lines.Count > MaxLines)
            {
                int drop = Lines.Count - MaxLines;
                Lines.RemoveRange(0, drop);
                Times.RemoveRange(0, Math.Min(drop, Times.Count));
                EndTimes.RemoveRange(0, Math.Min(drop, EndTimes.Count));
            }
        }

        private void Update(string text, DateTime end)
        {
            if (Lines.Count == 0) { Start(text, _speaker, _turn, end, end); return; }
            _text = text;
            Lines[Lines.Count - 1] = Prefix(_speaker) + text;
            if (EndTimes.Count > 0) { EndTimes[EndTimes.Count - 1] = end; }
        }

        private static string Prefix(int speaker)
        {
            return speaker > 0 ? SpeakerTurnLabeler.Prefix(speaker) : string.Empty;
        }

        private static bool EndsSentence(string text)
        {
            string t = text.TrimEnd(' ', '"', '\'', '”', '’', ')');
            if (t.Length == 0) { return false; }
            char c = t[t.Length - 1];
            return c == '.' || c == '!' || c == '?' || c == '…' || c == '。' || c == '！' || c == '？';
        }

        /// <summary>
        /// If the end of <paramref name="prev"/> overlaps the start of <paramref name="next"/> (the
        /// sliding window Live Captions produces), returns the two stitched into one continuous line;
        /// otherwise null. Example: prev="...I seen it. I would have", next="I would have seen if
        /// y'all" → "...I seen it. I would have seen if y'all". (Moved here from MainForm unchanged.)
        /// </summary>
        internal static string TryMergeOverlap(string prev, string next)
        {
            if (string.IsNullOrEmpty(prev) || string.IsNullOrEmpty(next)) return null;
            if (next.Length >= prev.Length &&
                next.StartsWith(prev, StringComparison.OrdinalIgnoreCase))
            {
                return next; // pure growth
            }

            // Find the largest k where prev's last k chars equal next's first k.
            int max = Math.Min(prev.Length, next.Length);
            for (int k = max; k >= 8; k--) // require a meaningful overlap (>=8 chars)
            {
                string tail = prev.Substring(prev.Length - k);
                string head = next.Substring(0, k);
                if (string.Equals(tail, head, StringComparison.OrdinalIgnoreCase))
                {
                    return prev + next.Substring(k);
                }
            }
            return null;
        }

        /// <summary>
        /// True when two caption strings are the same phrase being refined (so the transcript should
        /// replace, not append): exact or contained matches, or a long shared prefix — how Live
        /// Captions revises a line as it streams. (Moved here from MainForm unchanged.)
        /// </summary>
        internal static bool IsSameUtterance(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            if (a == b) return true;
            if (a.StartsWith(b, StringComparison.OrdinalIgnoreCase) ||
                b.StartsWith(a, StringComparison.OrdinalIgnoreCase)) return true;

            // Length of the common leading run of characters.
            int n = Math.Min(a.Length, b.Length);
            int common = 0;
            for (int i = 0; i < n; i++)
            {
                if (char.ToLowerInvariant(a[i]) == char.ToLowerInvariant(b[i])) common++;
                else break;
            }
            // If they agree on most of the shorter string's length, it's the same
            // line mid-revision (e.g. a trailing word changed or punctuation moved).
            int shorter = Math.Min(a.Length, b.Length);
            return shorter > 0 && common >= (int)(shorter * 0.7);
        }
    }
}
