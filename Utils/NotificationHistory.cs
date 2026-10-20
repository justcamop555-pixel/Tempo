using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Every notification Tempo raises, kept so it can be read back afterwards.
    ///
    /// WHY THIS EXISTS. A card is on screen for a few seconds and then it is gone, and
    /// the one case where that matters most is the case Tempo is built for: while a
    /// fullscreen game is running, NotificationCenter drops cards rather than painting
    /// over the game (deliberately — see ShowOrQueue). So the notifications most likely
    /// to be missed were also the ones no longer recorded anywhere except a count in
    /// Live debug. "A run finished two hours ago" is worth reading late; it is not worth
    /// nothing.
    ///
    /// Written through to disk on every entry, for the same reason the crash log is:
    /// the interesting history is the history that survives whatever happened next.
    /// One line per entry, appended, so a half-written tail costs one row rather than
    /// the file.
    /// </summary>
    public static class NotificationHistory
    {
        /// <summary>How a notification ended up.</summary>
        public enum Outcome
        {
            /// <summary>Shown on screen as a card.</summary>
            Shown = 0,
            /// <summary>Dropped without being shown — the reason says why.</summary>
            Missed = 1,
            /// <summary>Repeat of a card already on screen; counted on that card.</summary>
            Repeated = 2,
        }

        public sealed class Entry
        {
            public DateTime WhenUtc;
            public string Source;
            public string Title;
            public string Body;
            public string Kind;
            public Outcome How;
            /// <summary>Why it was missed, or "" when it was shown.</summary>
            public string Reason;
            /// <summary>
            /// How many times this message arrived — repeats are counted here, not given rows of
            /// their own (see <see cref="FindRowToCount"/>). 1 for a message that came once.
            /// </summary>
            public int Count = 1;
            /// <summary>When it last arrived; <see cref="WhenUtc"/> is the first time.</summary>
            public DateTime LastUtc;
            /// <summary>Who Windows named as the sender when it was not the app (an Edge web site), or "".</summary>
            public string Sender = "";
            /// <summary>The toast's attribution line ("via SMS"), or "".</summary>
            public string Attribution = "";
            /// <summary>
            /// The notification's picture, small, for THIS SESSION only — never written to disk: a history
            /// file full of other apps' pictures (screenshots, people's photos) is not something to leave
            /// lying around. Read it through <see cref="CopyPicture"/>; the history disposes its own.
            /// </summary>
            internal System.Drawing.Image Picture;

            public DateTime WhenLocal => WhenUtc.ToLocalTime();

            /// <summary>Who sent it, named the way its card named it ("www.kenherbert.dev · Microsoft Edge").</summary>
            public string SourceLine => NotificationExtras.SourceLine(string.IsNullOrEmpty(Source) ? "Tempo" : Source, Sender, Attribution);
        }

        /// <summary>
        /// How many entries are kept. 300 is a couple of weeks of ordinary use and about
        /// 60 KB on disk — small enough that trimming never has to be clever.
        /// </summary>
        private const int MaxEntries = 300;

        private static readonly object Lock = new object();
        private static readonly List<Entry> Items = new List<Entry>();
        private static bool _loaded;

        /// <summary>Raised after an entry is added, on the caller's thread.</summary>
        public static event Action Changed;

        /// <summary>For the tests: a scratch file instead of the user's history.</summary>
        internal static string PathForTest;

        /// <summary>
        /// In Tempo's data folder, with the settings — the same %LOCALAPPDATA%\AutoClicker as ever, but asked of
        /// SettingsManager rather than spelled out again. Spelled out, it was the one data file a copy of Tempo
        /// pointed at a scratch data folder still wrote to the user's own: test runs of the main window added
        /// their warnings to the real history.
        /// </summary>
        private static string PathOnDisk
        {
            get
            {
                if (PathForTest != null) { return PathForTest; }
                return Path.Combine(Persistence.SettingsManager.GetSettingsDirectory(), "notification-history.tsv");
            }
        }

        /// <summary>Newest first. A copy, so callers can hold it while more arrive.</summary>
        public static List<Entry> Recent()
        {
            lock (Lock)
            {
                EnsureLoaded();
                var copy = new List<Entry>(Items);
                copy.Reverse();
                return copy;
            }
        }

        /// <summary>How many were dropped without being seen, over the whole history (repeats included).</summary>
        public static int MissedCount()
        {
            lock (Lock)
            {
                EnsureLoaded();
                int n = 0;
                foreach (Entry e in Items)
                {
                    if (e.How == Outcome.Missed) { n += Math.Max(1, e.Count); }
                }
                return n;
            }
        }

        /// <summary>
        /// A repeat is counted on the row it repeats instead of becoming a row of its own: a card
        /// counting "×N" (Repeated → the row it was shown on), or the same message dropped again for
        /// the same reason (Missed — a game in front while an app kept notifying). One busy app or one
        /// spamming web page used to fill all 300 rows within minutes that way, pushing out everything
        /// else — the notifications the user had not seen yet included.
        /// </summary>
        private const int CoalesceLookBack = 20;
        private static readonly TimeSpan CoalesceWindow = TimeSpan.FromMinutes(10);

        private static Entry FindRowToCount(string source, string sender, string title, string body, Outcome how,
                                            string reason, DateTime now)
        {
            if (how == Outcome.Shown) { return null; }   // a card on screen again is a new event
            for (int i = Items.Count - 1, seen = 0; i >= 0 && seen < CoalesceLookBack; i--, seen++)
            {
                Entry e = Items[i];
                if (now - (e.LastUtc == default ? e.WhenUtc : e.LastUtc) > CoalesceWindow) { continue; }
                if (!string.Equals(e.Source, source, StringComparison.Ordinal)
                    || !string.Equals(e.Sender ?? "", sender, StringComparison.Ordinal)
                    || !string.Equals(e.Title, title, StringComparison.Ordinal)
                    || !string.Equals(e.Body, body, StringComparison.Ordinal))
                {
                    continue;
                }
                if (how == Outcome.Repeated && (e.How == Outcome.Shown || e.How == Outcome.Repeated)) { return e; }
                if (how == Outcome.Missed && e.How == Outcome.Missed
                    && string.Equals(e.Reason, reason, StringComparison.Ordinal))
                {
                    return e;
                }
                return null;   // its latest row is of another kind: don't merge across that
            }
            return null;
        }

        // A counted repeat changes a row already written, so the file is rewritten — at most once
        // per FlushDelayMs however fast the repeats come (a spam test must not become a disk loop).
        private const int FlushDelayMs = 1500;
        private static System.Threading.Timer _flushTimer;
        private static bool _dirty;
        private static bool _flushArmed;

        /// <summary>Writes any counted repeats not on disk yet. Called on a timer, and at shutdown.</summary>
        public static void Flush()
        {
            lock (Lock)
            {
                _flushArmed = false;
                if (!_dirty) { return; }
                _dirty = false;
                try { WriteAll(); }
                catch (Exception ex) { Logger.Swallow("NotificationHistory.Flush", ex); }
            }
        }

        private static void ScheduleFlush()
        {
            // The FIRST mark arms it; later ones ride along. Re-arming on every repeat would push the
            // write back for as long as the repeats kept coming.
            if (_flushArmed) { return; }
            _flushArmed = true;
            if (_flushTimer == null)
            {
                _flushTimer = new System.Threading.Timer(_ => Flush(), null,
                                                         System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            }
            _flushTimer.Change(FlushDelayMs, System.Threading.Timeout.Infinite);
        }

        /// <summary>
        /// Write one entry. <paramref name="whenUtc"/> exists for a card that WAITED before
        /// its fate was known: the outcome is decided when a slot frees (or when it is
        /// dropped), but the entry must still carry the moment Tempo had something to say,
        /// or a queued message reads as though it arrived a minute after the thing it was
        /// about. <paramref name="extras"/> names the sender a mirrored toast had (kept on
        /// disk); <paramref name="picture"/> is copied small and kept for this session only.
        /// </summary>
        public static void Add(string source, string title, string body, string kind,
                               Outcome how, string reason = "", DateTime? whenUtc = null,
                               NotificationExtras extras = null, System.Drawing.Image picture = null)
        {
            // Made outside the lock: scaling a picture is the one slow step here.
            System.Drawing.Image thumb = picture != null && how != Outcome.Repeated ? Thumbnail(picture) : null;
            Entry entry;
            lock (Lock)
            {
                EnsureLoaded();
                DateTime now = DateTime.UtcNow;
                string src = Clean(source), ttl = Clean(title), bdy = Clean(body), why = Clean(reason);
                string snd = Clean(extras?.Sender), att = Clean(extras?.Attribution);

                Entry counted = FindRowToCount(src, snd, ttl, bdy, how, why, now);
                if (counted != null)
                {
                    counted.Count = Math.Max(1, counted.Count) + 1;
                    counted.LastUtc = now;
                    if (counted.Picture == null && thumb != null) { counted.Picture = thumb; thumb = null; }
                    _dirty = true;
                    ScheduleFlush();
                }
                else
                {
                    entry = new Entry
                    {
                        WhenUtc = whenUtc ?? now,
                        Source = src,
                        Title = ttl,
                        Body = bdy,
                        Kind = Clean(kind),
                        How = how,
                        Reason = why,
                        Sender = snd,
                        Attribution = att,
                        Picture = thumb,
                    };
                    thumb = null;
                    entry.LastUtc = entry.WhenUtc;
                    Items.Add(entry);

                    bool trimmed = false;
                    while (Items.Count > MaxEntries)
                    {
                        DropPicture(Items[0]);
                        Items.RemoveAt(0);
                        trimmed = true;
                    }
                    KeepPicturesBounded();

                    try
                    {
                        // Rewrite the whole file after a trim (rare) or while a counted repeat is
                        // waiting to be written (the rewrite carries this row too); append otherwise.
                        if (trimmed || _dirty) { WriteAll(); _dirty = false; }
                        else { AppendOne(entry); }
                    }
                    catch (Exception ex) { Logger.Swallow("NotificationHistory.Add(write)", ex); }
                }
            }
            try { thumb?.Dispose(); } catch { }   // a repeat of a row that already has its picture

            try { Changed?.Invoke(); } catch { }
        }

        public static void Clear()
        {
            lock (Lock)
            {
                foreach (Entry e in Items) { DropPicture(e); }
                Items.Clear();
                _loaded = true;
                _dirty = false;
                try { File.Delete(PathOnDisk); }
                catch (Exception ex) { Logger.Swallow("NotificationHistory.Clear", ex); }
            }
            try { Changed?.Invoke(); } catch { }
        }

        // ── the session's pictures ─────────────────────────────────────────────

        /// <summary>
        /// How many entries keep a picture: the newest ones. Each is at most 320×180 (≈230 KB), so the
        /// whole set stays a few MB however long Tempo runs.
        /// </summary>
        private const int MaxPictures = 24;
        private const int ThumbW = 320, ThumbH = 180;

        /// <summary>A copy of an entry's picture for the caller to own (and dispose), or null when it has none.</summary>
        public static System.Drawing.Image CopyPicture(Entry e)
        {
            if (e == null) { return null; }
            lock (Lock)
            {
                try { return e.Picture == null ? null : new System.Drawing.Bitmap(e.Picture); }
                catch { return null; }
            }
        }

        private static System.Drawing.Image Thumbnail(System.Drawing.Image src)
        {
            try
            {
                if (src.Width <= 0 || src.Height <= 0) { return null; }
                double scale = Math.Min(1.0, Math.Min((double)ThumbW / src.Width, (double)ThumbH / src.Height));
                int w = Math.Max(1, (int)Math.Round(src.Width * scale));
                int h = Math.Max(1, (int)Math.Round(src.Height * scale));
                var copy = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (var g = System.Drawing.Graphics.FromImage(copy))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.DrawImage(src, 0, 0, w, h);
                }
                return copy;
            }
            catch (Exception ex)
            {
                Logger.Swallow("NotificationHistory.Thumbnail", ex);
                return null;
            }
        }

        private static void DropPicture(Entry e)
        {
            if (e?.Picture == null) { return; }
            try { e.Picture.Dispose(); } catch { }
            e.Picture = null;
        }

        /// <summary>Only the newest <see cref="MaxPictures"/> entries keep their picture.</summary>
        private static void KeepPicturesBounded()
        {
            int kept = 0;
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                if (Items[i].Picture == null) { continue; }
                if (++kept > MaxPictures) { DropPicture(Items[i]); }
            }
        }

        // ── disk ────────────────────────────────────────────────────────────────

        // Tab-separated, because the fields are free text a user wrote or an app sent:
        // a tab is the one character none of them contains after Clean(), while commas,
        // quotes and newlines are all routine in a notification body.
        private const char Sep = '\t';

        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) { return ""; }
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                sb.Append(c == '\t' || c == '\r' || c == '\n' ? ' ' : c);
            }
            return sb.ToString().Trim();
        }

        private static void EnsureLoaded()
        {
            if (_loaded) { return; }
            _loaded = true;                       // set first: a bad file must not retry forever
            try
            {
                string path = PathOnDisk;
                if (!File.Exists(path)) { return; }
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    Entry e = Parse(line);
                    if (e != null) { Items.Add(e); }
                }
                while (Items.Count > MaxEntries) { DropPicture(Items[0]); Items.RemoveAt(0); }
            }
            catch (Exception ex) { Logger.Swallow("NotificationHistory.Load", ex); }
        }

        private static Entry Parse(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) { return null; }
            string[] f = line.Split(Sep);
            if (f.Length < 6) { return null; }
            if (!DateTime.TryParse(f[0], CultureInfo.InvariantCulture,
                                   DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                                   out DateTime when))
            {
                return null;
            }
            int how;
            if (!int.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out how)) { how = 0; }
            // Columns 8 and 9 (the count and the last time) are newer: a file an older Tempo wrote
            // has neither, and an older Tempo reading this one ignores them.
            int count = 1;
            if (f.Length > 7 && int.TryParse(f[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out int c) && c > 1)
            {
                count = c;
            }
            DateTime last = when;
            if (f.Length > 8 && DateTime.TryParse(f[8], CultureInfo.InvariantCulture,
                                                  DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                                                  out DateTime l))
            {
                last = l;
            }
            // Columns 10 and 11 (the sender and the attribution) are newer still; the same rule holds.
            return new Entry
            {
                WhenUtc = when,
                Source = f[1],
                Title = f[2],
                Body = f[3],
                How = (Outcome)how,
                Kind = f[5],
                Reason = f.Length > 6 ? f[6] : "",
                Count = count,
                LastUtc = last,
                Sender = f.Length > 9 ? f[9] : "",
                Attribution = f.Length > 10 ? f[10] : "",
            };
        }

        private static string Format(Entry e)
        {
            DateTime last = e.LastUtc == default ? e.WhenUtc : e.LastUtc;
            return e.WhenUtc.ToString("o", CultureInfo.InvariantCulture) + Sep +
                   e.Source + Sep + e.Title + Sep + e.Body + Sep +
                   ((int)e.How).ToString(CultureInfo.InvariantCulture) + Sep +
                   e.Kind + Sep + e.Reason + Sep +
                   Math.Max(1, e.Count).ToString(CultureInfo.InvariantCulture) + Sep +
                   last.ToString("o", CultureInfo.InvariantCulture) + Sep +
                   (e.Sender ?? "") + Sep + (e.Attribution ?? "");
        }

        private static void AppendOne(Entry e)
        {
            string path = PathOnDisk;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.AppendAllText(path, Format(e) + Environment.NewLine, Encoding.UTF8);
        }

        private static void WriteAll()
        {
            string path = PathOnDisk;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var sb = new StringBuilder();
            foreach (Entry e in Items) { sb.AppendLine(Format(e)); }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }
    }
}
