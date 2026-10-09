using System;
using System.Collections.Generic;
using System.Text;

namespace AutoClicker.Utils
{
    /// <summary>
    /// One speaker identity out of what the caption speakers are SEEN and HEARD doing: the face
    /// analyzer's talking face and the voice profiler's voice become the same "Speaker N".
    ///
    /// Before this, the two were separate number spaces. A face slot number named the label while a
    /// mouth was moving, a voice profile number named it the rest of the time, and nothing connected
    /// them — so "Speaker 2" from a face and "Speaker 2" from a voice could be two different people,
    /// the label jumped between the spaces at every pause, and a face slot handed to a newcomer gave
    /// them the previous person's number.
    ///
    /// Now each face on screen is LINKED to a voice by what happens together: while a face's mouth is
    /// visibly moving and the voice profiler names the voice speaking at that moment, that pairing
    /// gains weight. Once a face is linked:
    ///   • it is labelled with its voice's speaker — the same label whether the evidence of the moment
    ///     is sight or sound, so there is one numbering, not two;
    ///   • sight overrides a wrong voice guess: two similar voices the profiler mixes up still get the
    ///     right label while their faces are visible;
    ///   • a voice the profiler splits in two (one person heard as a "new" voice) is re-joined, because
    ///     the new voice appears while the person's linked face is visibly talking;
    ///   • and two different faces that share one voice — two people the profiler cannot tell apart —
    ///     are told apart: the face linked second gets a speaker of its own.
    /// Faces are identified by the analyzer's occupant ids, never slot numbers, so a person arriving in
    /// a departed person's slot starts unlinked.
    ///
    /// It also keeps a timeline of who was talking, sampled every ~100 ms, so a caption can be labelled
    /// by who spoke while its words were being SAID rather than who happens to be talking when the text
    /// arrives, which is ~1.5–3 s later (see <see cref="SpanVerdict"/>).
    ///
    /// Thread-safe: sampled from a timer thread, read from the UI thread.
    /// </summary>
    public sealed class SpeakerLinker
    {
        /// <summary>The highest label number (the caption labeller's own ceiling).</summary>
        public const int MaxLabels = 9;

        /// <summary>What the two analyzers say at one moment.</summary>
        public struct Evidence
        {
            /// <summary>The voice profiler is listening to the audio being captioned.</summary>
            public bool VoiceRunning;
            /// <summary>The voice named for the speech happening right now; 0 when none.</summary>
            public int ActiveVoice;
            /// <summary>The voice named last, held through pauses; 0 when none yet.</summary>
            public int HeldVoice;
            /// <summary>The face analyzer is watching.</summary>
            public bool FaceRunning;
            /// <summary>Occupant id of the face visibly talking; 0 when none is.</summary>
            public long TalkingFace;
            /// <summary>Faces on screen.</summary>
            public int FacesOnScreen;
            /// <summary>Two or more faces talking at once.</summary>
            public bool CrossTalk;
            /// <summary>Occupant ids of the living faces on screen.</summary>
            public long[] VisibleFaces;
        }

        /// <summary>One speaker as the Captions tab shows it.</summary>
        public sealed class SpeakerInfo
        {
            public int Label;
            public bool Talking;
            public bool HasVoice;
            public bool HasFace;
            /// <summary>The most recent face linked to this speaker (for its picture), or 0.</summary>
            public long Face;
            /// <summary>
            /// Every face linked to this speaker lately, most recent first (at most <see cref="FacesKept"/>).
            /// One person is often two faces to the analyzer — a close-up and a small face in the wide
            /// shots — and the card shows the better picture of the two, not merely the latest.
            /// </summary>
            public List<long> Faces = new List<long>();
            public DateTime LastHeardUtc;

            internal const int FacesKept = 4;

            internal void SawFace(long face)
            {
                Face = face;
                Faces.Remove(face);
                Faces.Insert(0, face);
                if (Faces.Count > FacesKept) { Faces.RemoveAt(Faces.Count - 1); }
            }
        }

        // ── Tunables ─────────────────────────────────────────────────────────────
        // A face is linked to a voice once this much weight (≈ samples) backs the pairing and that
        // voice holds this share of the face's evidence.
        private const double LinkWeight = 5.0;
        private const double LinkShare = 0.65;
        // Older evidence fades, so a wrong early pairing can be outvoted: every new sample for a face
        // multiplies its existing weights by this (≈ a 3 s memory at 10 samples a second of talking).
        private const double VoteDecay = 0.97;
        // A brand-new voice heard while a STRONGLY linked face talks is taken to be that same person.
        private const double StrongWeight = 10.0;
        private const double StrongShare = 0.8;
        // The face that was talking a moment ago: while it is still on screen and linked to someone
        // else, a voice guess that disagrees is held rather than followed — for this long.
        private const double FaceHoldSeconds = 1.5;
        // The timeline.
        private const int TimelineCapacity = 600;               // 60 s at 100 ms
        /// <summary>
        /// How far the verdicts trail the audio: a voice is named a few hundred ms into its speech and
        /// a talking face is recognised through a smoothing filter. A caption's spoken span is read
        /// this much later on the timeline.
        /// </summary>
        public static readonly TimeSpan EvidenceLag = TimeSpan.FromMilliseconds(500);

        private readonly object _gate = new object();
        private readonly Dictionary<int, int> _voiceLabel = new Dictionary<int, int>();
        private readonly Dictionary<long, int> _faceLabel = new Dictionary<long, int>();       // split / face-only
        private readonly Dictionary<long, Dictionary<int, double>> _votes = new Dictionary<long, Dictionary<int, double>>();
        private readonly Dictionary<long, DateTime> _linkedSince = new Dictionary<long, DateTime>();
        private readonly Dictionary<int, SpeakerInfo> _speakers = new Dictionary<int, SpeakerInfo>();
        private readonly DateTime[] _tlTime = new DateTime[TimelineCapacity];
        private readonly int[] _tlLabel = new int[TimelineCapacity];
        private int _tlHead, _tlCount;
        private long _lastTalkingFace;
        private DateTime _lastTalkingFaceUtc = DateTime.MinValue;
        private int _current;
        private string _why = "";
        private int _splits, _rejoins, _overrides;
        // Bumped each time a label is handed to someone: a fresh speaker, or one recycled from a
        // speaker not heard for longest. Whatever is attached to a label — a name the user gave it —
        // belongs to one incarnation, so it can't follow the number onto somebody else.
        private readonly Dictionary<int, int> _incarnation = new Dictionary<int, int>();

        /// <summary>Which incarnation of <paramref name="label"/> is current (0 = never assigned).</summary>
        public int Incarnation(int label)
        {
            lock (_gate) { return _incarnation.TryGetValue(label, out int n) ? n : 0; }
        }

        /// <summary>The speaker named at the last sample; 0 while holding or before anyone spoke.</summary>
        public int Current { get { lock (_gate) { return _current; } } }

        /// <summary>Why the last sample answered as it did, for Live debug.</summary>
        public string Why { get { lock (_gate) { return _why; } } }

        /// <summary>Faces given a speaker of their own because another face shared their voice.</summary>
        public int Splits { get { lock (_gate) { return _splits; } } }

        /// <summary>Voices the profiler learned twice for one person, joined back by their face.</summary>
        public int Rejoins { get { lock (_gate) { return _rejoins; } } }

        /// <summary>Samples where a linked face overruled the voice guess.</summary>
        public int Overrides { get { lock (_gate) { return _overrides; } } }

        /// <summary>Forgets everything: a new caption session, or new audio (new people).</summary>
        public void Reset()
        {
            lock (_gate)
            {
                _voiceLabel.Clear();
                _faceLabel.Clear();
                _votes.Clear();
                _linkedSince.Clear();
                _speakers.Clear();
                _tlHead = 0;
                _tlCount = 0;
                _lastTalkingFace = 0;
                _lastTalkingFaceUtc = DateTime.MinValue;
                _current = 0;
                _why = "";
                _splits = _rejoins = _overrides = 0;
                // Deliberately NOT cleared: incarnations keep counting across resets, so a name
                // given in an earlier session can never match a label handed out in this one.
                foreach (int l in new List<int>(_incarnation.Keys)) { _incarnation[l]++; }
            }
        }

        /// <summary>
        /// Takes one moment's evidence (call ~10 times a second), updates the links, records the
        /// timeline, and returns the speaker label for that moment — 0 when the honest answer is
        /// "hold what is showing".
        /// </summary>
        public int Sample(DateTime utc, Evidence e)
        {
            lock (_gate)
            {
                int label;
                string why;
                if (e.FaceRunning && e.CrossTalk)
                {
                    label = 0;
                    why = "HOLDING — two or more faces are talking at once";
                }
                else if (e.FaceRunning && e.TalkingFace > 0)
                {
                    label = FromTalkingFace(utc, e, out why);
                }
                else
                {
                    label = FromVoice(utc, e, out why);
                }

                _current = label;
                _why = why;
                foreach (SpeakerInfo s in _speakers.Values) { s.Talking = false; }
                if (label > 0)
                {
                    SpeakerInfo s = Info(label, utc);
                    s.Talking = e.ActiveVoice > 0 || (e.FaceRunning && e.TalkingFace > 0);
                    if (s.Talking) { s.LastHeardUtc = utc; }
                }

                _tlTime[_tlHead] = utc;
                _tlLabel[_tlHead] = label;
                _tlHead = (_tlHead + 1) % TimelineCapacity;
                if (_tlCount < TimelineCapacity) { _tlCount++; }
                return label;
            }
        }

        private int FromTalkingFace(DateTime utc, Evidence e, out string why)
        {
            long face = e.TalkingFace;
            _lastTalkingFace = face;
            _lastTalkingFaceUtc = utc;
            if (e.ActiveVoice > 0) { Vote(face, e.ActiveVoice, utc); }

            int linked = LinkedLabel(face, e, utc);
            if (linked > 0)
            {
                Info(linked, utc).SawFace(face);
                if (e.ActiveVoice > 0)
                {
                    if (!_voiceLabel.TryGetValue(e.ActiveVoice, out int voiceLabel))
                    {
                        // A voice the profiler has not placed yet, heard while this face talks. With a
                        // strong link it is the same person heard as "new" — join it to them.
                        if (IsStrong(face))
                        {
                            _voiceLabel[e.ActiveVoice] = linked;
                            Info(linked, utc).HasVoice = true;
                            _rejoins++;
                        }
                    }
                    else if (voiceLabel != linked)
                    {
                        _overrides++;
                        why = "face → Speaker " + linked + " (sight overrides voice " + e.ActiveVoice + ")";
                        return linked;
                    }
                    else
                    {
                        why = "face and voice agree → Speaker " + linked;
                        return linked;
                    }
                }
                why = "face → Speaker " + linked + " (linked to its voice)";
                return linked;
            }

            int voice = e.ActiveVoice > 0 ? e.ActiveVoice : e.HeldVoice;
            if (e.VoiceRunning && voice > 0)
            {
                why = "voice " + voice + " → Speaker " + LabelForVoice(voice, utc) + " (the talking face is still being linked)";
                return LabelForVoice(voice, utc);
            }
            if (!e.VoiceRunning)
            {
                // No voices to link to: the face is the only evidence there is.
                if (!_faceLabel.TryGetValue(face, out int own))
                {
                    own = NewLabel(utc);
                    _faceLabel[face] = own;
                }
                Info(own, utc).HasFace = true;
                Info(own, utc).SawFace(face);
                why = "face → Speaker " + own + " (voice matching is off)";
                return own;
            }
            why = "a face is talking — waiting for its voice";
            return 0;
        }

        private int FromVoice(DateTime utc, Evidence e, out string why)
        {
            int voice = e.ActiveVoice > 0 ? e.ActiveVoice : e.HeldVoice;
            if (!e.VoiceRunning || voice <= 0)
            {
                why = e.VoiceRunning ? "no verdict yet — voice matching needs about half a second of speech"
                                     : "counting pauses — voice matching isn't running";
                return 0;
            }
            int label = LabelForVoice(voice, utc);

            // The face that was talking a moment ago is still on screen and linked to someone else:
            // its mouth pausing between phrases is not evidence of a hand-over, so hold briefly.
            if (e.FaceRunning && _lastTalkingFace > 0 && e.FacesOnScreen > 0
                && (utc - _lastTalkingFaceUtc).TotalSeconds < FaceHoldSeconds)
            {
                int faceLabel = LinkedLabel(_lastTalkingFace, e, utc);
                if (faceLabel > 0 && faceLabel != label)
                {
                    why = "HOLDING — Speaker " + faceLabel + "'s face paused " +
                          (utc - _lastTalkingFaceUtc).TotalSeconds.ToString("0.0") + " s ago; the voice says " + label;
                    return 0;
                }
            }
            why = "voice " + voice + " → Speaker " + label;
            return label;
        }

        /// <summary>The speaker a face is linked to, or 0 when it isn't (yet).</summary>
        private int LinkedLabel(long face, Evidence e, DateTime utc)
        {
            if (_faceLabel.TryGetValue(face, out int own)) { return own; }
            if (!TopVoice(face, out int voice, out double weight, out double share)
                || weight < LinkWeight || share < LinkShare)
            {
                return 0;
            }
            if (!_linkedSince.ContainsKey(face)) { _linkedSince[face] = utc; }

            // Another face on screen RIGHT NOW linked to the same voice, and linked first: two people
            // the voice profiler hears as one. This face gets a speaker of its own.
            if (e.VisibleFaces != null)
            {
                foreach (long other in e.VisibleFaces)
                {
                    if (other == face || _faceLabel.ContainsKey(other)) { continue; }
                    if (TopVoice(other, out int ov, out double ow, out double os)
                        && ov == voice && ow >= LinkWeight && os >= LinkShare
                        && _linkedSince.TryGetValue(other, out DateTime otherSince)
                        && otherSince < _linkedSince[face])
                    {
                        int mine = NewLabel(utc);
                        _faceLabel[face] = mine;
                        Info(mine, utc).HasFace = true;
                        _splits++;
                        return mine;
                    }
                }
            }
            int label = LabelForVoice(voice, utc);
            Info(label, utc).HasFace = true;
            return label;
        }

        private void Vote(long face, int voice, DateTime utc)
        {
            if (!_votes.TryGetValue(face, out Dictionary<int, double> w))
            {
                w = new Dictionary<int, double>();
                _votes[face] = w;
                // Faces come and go with scene cuts; keep the table from growing for ever.
                if (_votes.Count > 64) { ForgetOldestFace(face); }
            }
            var keys = new List<int>(w.Keys);
            foreach (int k in keys) { w[k] *= VoteDecay; }
            w[voice] = (w.TryGetValue(voice, out double cur) ? cur : 0) + 1.0;
        }

        private void ForgetOldestFace(long keep)
        {
            long oldest = 0;
            foreach (long f in _votes.Keys) { if (f != keep && (oldest == 0 || f < oldest)) { oldest = f; } }
            if (oldest != 0) { _votes.Remove(oldest); _linkedSince.Remove(oldest); _faceLabel.Remove(oldest); }
        }

        private bool TopVoice(long face, out int voice, out double weight, out double share)
        {
            voice = 0; weight = 0; share = 0;
            if (!_votes.TryGetValue(face, out Dictionary<int, double> w) || w.Count == 0) { return false; }
            double total = 0;
            foreach (KeyValuePair<int, double> kv in w)
            {
                total += kv.Value;
                if (kv.Value > weight) { weight = kv.Value; voice = kv.Key; }
            }
            share = total > 0 ? weight / total : 0;
            return voice > 0;
        }

        private bool IsStrong(long face)
        {
            return TopVoice(face, out _, out double weight, out double share)
                   && weight >= StrongWeight && share >= StrongShare;
        }

        private int LabelForVoice(int voice, DateTime utc)
        {
            if (!_voiceLabel.TryGetValue(voice, out int label))
            {
                label = NewLabel(utc);
                _voiceLabel[voice] = label;
            }
            Info(label, utc).HasVoice = true;
            return label;
        }

        /// <summary>
        /// The lowest free label. When all are taken, the one heard longest ago is recycled — its
        /// voices and faces are forgotten — rather than numbering past what the bar can colour.
        /// </summary>
        private int NewLabel(DateTime utc)
        {
            var used = new HashSet<int>(_voiceLabel.Values);
            foreach (int l in _faceLabel.Values) { used.Add(l); }
            for (int l = 1; l <= MaxLabels; l++)
            {
                if (!used.Contains(l))
                {
                    _speakers.Remove(l);
                    _incarnation[l] = (_incarnation.TryGetValue(l, out int n) ? n : 0) + 1;
                    return l;
                }
            }
            int stale = 1;
            DateTime staleAt = DateTime.MaxValue;
            foreach (KeyValuePair<int, SpeakerInfo> kv in _speakers)
            {
                if (kv.Value.LastHeardUtc < staleAt) { staleAt = kv.Value.LastHeardUtc; stale = kv.Key; }
            }
            foreach (int v in new List<int>(_voiceLabel.Keys)) { if (_voiceLabel[v] == stale) { _voiceLabel.Remove(v); } }
            foreach (long f in new List<long>(_faceLabel.Keys)) { if (_faceLabel[f] == stale) { _faceLabel.Remove(f); } }
            _speakers.Remove(stale);
            _incarnation[stale] = (_incarnation.TryGetValue(stale, out int m) ? m : 0) + 1;
            return stale;
        }

        private SpeakerInfo Info(int label, DateTime utc)
        {
            if (!_speakers.TryGetValue(label, out SpeakerInfo s))
            {
                s = new SpeakerInfo { Label = label, LastHeardUtc = utc };
                _speakers[label] = s;
            }
            return s;
        }

        /// <summary>
        /// Who spoke while a caption's words were being said, from <paramref name="fromUtc"/> to
        /// <paramref name="toUtc"/> (audio time; the timeline is read <see cref="EvidenceLag"/> later).
        /// Returns false when the timeline has nothing to say about that span. Otherwise
        /// <paramref name="first"/> is who spoke it; when the span holds a clear hand-over,
        /// <paramref name="second"/> is who took over and <paramref name="splitFraction"/> how far into
        /// the span (0..1) it happened — else <paramref name="second"/> is 0.
        /// </summary>
        public bool SpanVerdict(DateTime fromUtc, DateTime toUtc, out int first, out int second, out double splitFraction)
        {
            first = 0; second = 0; splitFraction = 1;
            if (toUtc < fromUtc) { toUtc = fromUtc; }
            DateTime a = fromUtc + EvidenceLag, b = toUtc + EvidenceLag;
            var times = new List<DateTime>();
            var labels = new List<int>();
            lock (_gate)
            {
                for (int k = 0; k < _tlCount; k++)
                {
                    int i = (_tlHead - _tlCount + k + TimelineCapacity) % TimelineCapacity;
                    if (_tlTime[i] < a || _tlTime[i] > b || _tlLabel[i] <= 0) { continue; }
                    times.Add(_tlTime[i]);
                    labels.Add(_tlLabel[i]);
                }
            }
            return Decide(times, labels, a, b, out first, out second, out splitFraction);
        }

        /// <summary>One piece of a caption and who said it.</summary>
        public struct ChunkPart
        {
            public string Text;
            /// <summary>The speaker label; 0 = no verdict, let the caption labeller hold.</summary>
            public int Speaker;
            /// <summary>This piece starts a new speaker's turn exactly here.</summary>
            public bool HandOver;
            public DateTime StartUtc, EndUtc;
        }

        /// <summary>
        /// How to label one caption from Tempo's engine: who said its words, going by the timeline
        /// over the span they were SPOKEN — and, when someone else took over part-way through, where to
        /// cut the text. <paramref name="fallback"/> is used when the timeline has nothing to say.
        ///
        /// A caption covers ~2.8 s of audio and used to get ONE label, read the moment it arrived,
        /// ~1.5–3 s after its words were said. So at a hand-over the new speaker's label landed on the
        /// old speaker's last words, or the old speaker's on the new speaker's first ones.
        /// </summary>
        public List<ChunkPart> PlanChunk(string text, DateTime startUtc, DateTime endUtc, int fallback)
        {
            var parts = new List<ChunkPart>();
            text = (text ?? string.Empty).Trim();
            if (text.Length == 0) { return parts; }
            if (endUtc < startUtc) { endUtc = startUtc; }

            if (SpanVerdict(startUtc, endUtc, out int first, out int second, out double frac) && first > 0)
            {
                if (second > 0 && second != first)
                {
                    int cut = HandOverCut(text, frac);
                    if (cut > 0 && cut < text.Length)
                    {
                        string a = text.Substring(0, cut).Trim(), b = text.Substring(cut).Trim();
                        if (a.Length > 0 && b.Length > 0)
                        {
                            DateTime at = startUtc + TimeSpan.FromTicks((long)((endUtc - startUtc).Ticks * frac));
                            parts.Add(new ChunkPart { Text = a, Speaker = first, StartUtc = startUtc, EndUtc = at });
                            parts.Add(new ChunkPart { Text = b, Speaker = second, HandOver = true, StartUtc = at, EndUtc = endUtc });
                            return parts;
                        }
                    }
                    // Nowhere sensible to cut: the one who said most of it.
                    if (frac < 0.5) { first = second; }
                }
                parts.Add(new ChunkPart { Text = text, Speaker = first, StartUtc = startUtc, EndUtc = endUtc });
                return parts;
            }
            parts.Add(new ChunkPart { Text = text, Speaker = fallback, StartUtc = startUtc, EndUtc = endUtc });
            return parts;
        }

        /// <summary>
        /// Where to cut a caption so its second part starts with the new speaker's words, given how far
        /// through its time the hand-over happened. Words are assumed evenly spread in time, so the
        /// fraction becomes a position — then moved to the nearest sentence end (people hand over at
        /// the end of a sentence), else a comma, else a space. -1 when there is no word boundary.
        /// </summary>
        internal static int HandOverCut(string text, double fraction)
        {
            if (string.IsNullOrEmpty(text)) { return -1; }
            int target = (int)Math.Round(text.Length * Math.Max(0, Math.Min(1, fraction)));
            int window = Math.Max(12, text.Length / 4);
            int lo = Math.Max(1, target - window), hi = Math.Min(text.Length - 1, target + window);

            int best = -1, bestDist = int.MaxValue;
            for (int i = lo - 1; i < hi; i++)
            {
                int r = SpeakerTurnLabeler.SentenceCutAfter(text, i);
                if (r > 0 && r < text.Length && Math.Abs(r - target) < bestDist) { bestDist = Math.Abs(r - target); best = r; }
            }
            if (best > 0) { return best; }
            for (int i = lo; i < hi; i++)
            {
                if ((text[i] == ',' || text[i] == ';') && i + 1 < text.Length && text[i + 1] == ' '
                    && Math.Abs(i + 2 - target) < bestDist)
                {
                    bestDist = Math.Abs(i + 2 - target);
                    best = i + 2;
                }
            }
            if (best > 0) { return best; }
            for (int i = lo; i < hi; i++)
            {
                if (text[i] == ' ' && Math.Abs(i + 1 - target) < bestDist) { bestDist = Math.Abs(i + 1 - target); best = i + 1; }
            }
            return best;
        }

        /// <summary>The pure half of <see cref="SpanVerdict"/> (a harness feeds it directly).</summary>
        internal static bool Decide(List<DateTime> times, List<int> labels, DateTime a, DateTime b,
                                    out int first, out int second, out double splitFraction)
        {
            first = 0; second = 0; splitFraction = 1;
            int n = labels.Count;
            if (n < 3) { return false; }

            var counts = new Dictionary<int, int>();
            foreach (int l in labels) { counts[l] = counts.TryGetValue(l, out int c) ? c + 1 : 1; }
            int dominant = 0, most = 0;
            foreach (KeyValuePair<int, int> kv in counts) { if (kv.Value > most) { most = kv.Value; dominant = kv.Key; } }
            first = dominant;
            if (most >= n * 0.8) { return true; }

            // A hand-over inside the span: the cut that best explains it as "one speaker, then
            // another", each side at least a quarter of it and at least 70% one speaker.
            int bestCut = -1, bestScore = -1, bestA = 0, bestB = 0;
            for (int cut = 1; cut < n; cut++)
            {
                if (cut < n / 4 || n - cut < n / 4) { continue; }
                int la = Mode(labels, 0, cut, out int ca);
                int lb = Mode(labels, cut, n, out int cb);
                if (la == lb || ca < cut * 0.7 || cb < (n - cut) * 0.7) { continue; }
                if (ca + cb > bestScore) { bestScore = ca + cb; bestCut = cut; bestA = la; bestB = lb; }
            }
            if (bestCut < 0) { return true; }                      // mixed, no clean cut: the majority
            first = bestA;
            second = bestB;
            double span = (b - a).TotalSeconds;
            splitFraction = span > 0 ? Math.Max(0, Math.Min(1, (times[bestCut] - a).TotalSeconds / span)) : 0.5;
            return true;
        }

        private static int Mode(List<int> labels, int from, int to, out int count)
        {
            var c = new Dictionary<int, int>();
            int best = 0; count = 0;
            for (int i = from; i < to; i++)
            {
                int l = labels[i];
                int v = c.TryGetValue(l, out int x) ? x + 1 : 1;
                c[l] = v;
                if (v > count) { count = v; best = l; }
            }
            return best;
        }

        /// <summary>The speakers so far, lowest label first, for the Captions tab.</summary>
        public List<SpeakerInfo> Snapshot()
        {
            lock (_gate)
            {
                var list = new List<SpeakerInfo>();
                foreach (SpeakerInfo s in _speakers.Values)
                {
                    list.Add(new SpeakerInfo
                    {
                        Label = s.Label, Talking = s.Talking, HasVoice = s.HasVoice, HasFace = s.HasFace,
                        Face = s.Face, Faces = new List<long>(s.Faces), LastHeardUtc = s.LastHeardUtc,
                    });
                }
                list.Sort((x, y) => x.Label.CompareTo(y.Label));
                return list;
            }
        }

        /// <summary>Live debug: each speaker's voices and faces, and each face's link evidence.</summary>
        public string DebugDetail()
        {
            lock (_gate)
            {
                var sb = new StringBuilder();
                var labels = new List<int>(_speakers.Keys);
                labels.Sort();
                foreach (int l in labels)
                {
                    SpeakerInfo s = _speakers[l];
                    var voices = new List<int>();
                    foreach (KeyValuePair<int, int> kv in _voiceLabel) { if (kv.Value == l) { voices.Add(kv.Key); } }
                    voices.Sort();
                    sb.Append("    Speaker ").Append(l).Append(s.Talking ? " ● talking" : "          ")
                      .Append(" · voice ").Append(voices.Count > 0 ? string.Join("+", voices) : "—")
                      .Append(" · face ").Append(s.Faces.Count > 0 ? "#" + string.Join(" #", s.Faces) : "—")
                      .Append(" · heard ").Append(((int)(DateTime.UtcNow - s.LastHeardUtc).TotalSeconds).ToString())
                      .Append(" s ago").AppendLine();
                }
                foreach (KeyValuePair<long, Dictionary<int, double>> kv in _votes)
                {
                    if (!TopVoice(kv.Key, out int v, out double w, out double share)) { continue; }
                    sb.Append("    face #").Append(kv.Key).Append(" → voice ").Append(v)
                      .Append(" (weight ").Append(w.ToString("0.0")).Append(", ").Append((share * 100).ToString("0")).Append("%)")
                      .Append(_faceLabel.TryGetValue(kv.Key, out int own) ? " · own Speaker " + own : "")
                      .Append(w >= LinkWeight && share >= LinkShare ? " · linked" : " · not linked yet")
                      .AppendLine();
                }
                if (_splits + _rejoins + _overrides > 0)
                {
                    sb.Append("    faces told apart ").Append(_splits).Append(" · voices re-joined ").Append(_rejoins)
                      .Append(" · sight overruled the voice ").Append(_overrides).Append('×').AppendLine();
                }
                return sb.ToString();
            }
        }
    }
}
