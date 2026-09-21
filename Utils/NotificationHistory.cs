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

            public DateTime WhenLocal => WhenUtc.ToLocalTime();
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

        private static string PathOnDisk
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AutoClicker", "notification-history.tsv");
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

        /// <summary>How many were dropped without being seen, over the whole history.</summary>
        public static int MissedCount()
        {
            lock (Lock)
            {
                EnsureLoaded();
                int n = 0;
                foreach (Entry e in Items)
                {
                    if (e.How == Outcome.Missed) { n++; }
                }
                return n;
            }
        }

        /// <summary>
        /// Write one entry. <paramref name="whenUtc"/> exists for a card that WAITED before
        /// its fate was known: the outcome is decided when a slot frees (or when it is
        /// dropped), but the entry must still carry the moment Tempo had something to say,
        /// or a queued message reads as though it arrived a minute after the thing it was
        /// about.
        /// </summary>
        public static void Add(string source, string title, string body, string kind,
                               Outcome how, string reason = "", DateTime? whenUtc = null)
        {
            Entry entry;
            lock (Lock)
            {
                EnsureLoaded();
                entry = new Entry
                {
                    WhenUtc = whenUtc ?? DateTime.UtcNow,
                    Source = Clean(source),
                    Title = Clean(title),
                    Body = Clean(body),
                    Kind = Clean(kind),
                    How = how,
                    Reason = Clean(reason),
                };
                Items.Add(entry);

                bool trimmed = false;
                while (Items.Count > MaxEntries)
                {
                    Items.RemoveAt(0);
                    trimmed = true;
                }

                try
                {
                    // Rewrite the whole file after a trim (rare), append otherwise.
                    if (trimmed) { WriteAll(); }
                    else { AppendOne(entry); }
                }
                catch (Exception ex) { Logger.Swallow("NotificationHistory.Add(write)", ex); }
            }

            try { Changed?.Invoke(); } catch { }
        }

        public static void Clear()
        {
            lock (Lock)
            {
                Items.Clear();
                _loaded = true;
                try { File.Delete(PathOnDisk); }
                catch (Exception ex) { Logger.Swallow("NotificationHistory.Clear", ex); }
            }
            try { Changed?.Invoke(); } catch { }
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
                while (Items.Count > MaxEntries) { Items.RemoveAt(0); }
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
            return new Entry
            {
                WhenUtc = when,
                Source = f[1],
                Title = f[2],
                Body = f[3],
                How = (Outcome)how,
                Kind = f[5],
                Reason = f.Length > 6 ? f[6] : "",
            };
        }

        private static string Format(Entry e)
        {
            return e.WhenUtc.ToString("o", CultureInfo.InvariantCulture) + Sep +
                   e.Source + Sep + e.Title + Sep + e.Body + Sep +
                   ((int)e.How).ToString(CultureInfo.InvariantCulture) + Sep +
                   e.Kind + Sep + e.Reason;
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
