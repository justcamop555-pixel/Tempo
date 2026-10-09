using System;
using System.Collections.Generic;
using System.Text;

namespace AutoClicker.Utils
{
    /// <summary>
    /// The Live-debug filter box, parsed.
    ///
    /// It used to be one case-insensitive substring test over the whole line, which is the
    /// wrong tool for this log for the same reason the old colour classifier was: log lines
    /// quote text the user controls. Typing <c>engine</c> to find the click engine matched
    /// thirty German layout warnings quoting the caption setting "GPU-Engine ausprobieren"
    /// and nothing else — and there was no way to say "not those".
    ///
    /// The syntax is the one people already expect from a search box:
    ///   <code>
    ///   captions overlay      both words, anywhere in the line (AND)
    ///   "engine started"      an exact phrase
    ///   -timer                lines containing this are hidden
    ///   tag:captions          only lines the logger tagged [Captions]
    ///   level:warn            only warnings (info / warn / error)
    ///   -tag:audio            everything except that subsystem
    ///   </code>
    /// Anything unrecognised is an ordinary word, so an old habit of typing plain text keeps
    /// working exactly as it did.
    /// </summary>
    public sealed class LogFilter
    {
        private sealed class Term
        {
            public string Text;        // what to look for, lower-cased
            public bool Exclude;       // "-foo"
            public Kind What;
        }

        private enum Kind { Text, Tag, Level }

        private readonly List<Term> _terms = new List<Term>();

        /// <summary>The query exactly as it was typed, for round-tripping and tooltips.</summary>
        public string Query { get; private set; } = "";

        /// <summary>True when nothing was typed, so every line passes.</summary>
        public bool IsEmpty => _terms.Count == 0;

        /// <summary>True when at least one term can be highlighted in a matching line.</summary>
        public bool HasHighlightableText
        {
            get
            {
                foreach (Term t in _terms)
                {
                    if (!t.Exclude && t.What == Kind.Text && t.Text.Length > 0) { return true; }
                }
                return false;
            }
        }

        public static LogFilter Parse(string query) => Parse(query, allowFields: true);

        /// <summary>
        /// The same syntax minus <c>tag:</c> and <c>level:</c>, for text that is not a log —
        /// the caption transcript, where those fields mean nothing. They stay ORDINARY WORDS
        /// there rather than becoming a field that can never match, so searching a transcript
        /// for "tag:" finds the places somebody said it.
        /// </summary>
        public static LogFilter ParseTextOnly(string query) => Parse(query, allowFields: false);

        private static LogFilter Parse(string query, bool allowFields)
        {
            var f = new LogFilter { Query = query ?? "" };
            foreach (string token in Tokenize(f.Query))
            {
                string s = token;
                bool exclude = false;
                if (s.Length > 1 && s[0] == '-') { exclude = true; s = s.Substring(1); }

                // A lone "-" is someone mid-word, not an empty exclusion that hides everything.
                if (s.Length == 0) { continue; }

                Kind what = Kind.Text;
                if (allowFields)
                {
                    if (StartsWith(s, "tag:")) { what = Kind.Tag; s = s.Substring(4); }
                    else if (StartsWith(s, "level:")) { what = Kind.Level; s = s.Substring(6); }
                    if (s.Length == 0) { continue; }
                }

                f._terms.Add(new Term { Text = s.ToLowerInvariant(), Exclude = exclude, What = what });
            }
            return f;
        }

        private static bool StartsWith(string s, string prefix) =>
            s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Splits on whitespace, but keeps a "quoted phrase" together — including one the
        /// user is still typing, where the closing quote has not been pressed yet. Without
        /// that, every keystroke inside a phrase would filter on a different broken query
        /// and the list would thrash.
        /// </summary>
        private static IEnumerable<string> Tokenize(string q)
        {
            var sb = new StringBuilder();
            bool inQuote = false;
            bool leadingDash = false;

            for (int i = 0; i < q.Length; i++)
            {
                char c = q[i];
                if (c == '"')
                {
                    inQuote = !inQuote;
                    continue;
                }
                if (!inQuote && char.IsWhiteSpace(c))
                {
                    if (sb.Length > 0) { yield return (leadingDash ? "-" : "") + sb.ToString(); sb.Clear(); }
                    leadingDash = false;
                    continue;
                }
                // A '-' immediately before an opening quote negates the whole phrase:
                //   -"engine started"
                if (c == '-' && sb.Length == 0 && !inQuote) { leadingDash = true; continue; }
                sb.Append(c);
            }
            if (sb.Length > 0) { yield return (leadingDash ? "-" : "") + sb.ToString(); }
        }

        /// <summary>True when <paramref name="line"/> should be shown.</summary>
        public bool IsMatch(string line)
        {
            if (_terms.Count == 0) { return true; }
            if (line == null) { line = ""; }

            string lower = line.ToLowerInvariant();
            string tag = null, level = null;
            bool looked = false;

            foreach (Term t in _terms)
            {
                bool hit;
                switch (t.What)
                {
                    case Kind.Tag:
                        if (!looked) { tag = LogLine.TagOf(line); level = LogLine.LevelOf(line); looked = true; }
                        hit = tag != null && tag.ToLowerInvariant().Contains(t.Text);
                        break;
                    case Kind.Level:
                        if (!looked) { tag = LogLine.TagOf(line); level = LogLine.LevelOf(line); looked = true; }
                        hit = level != null && level.ToLowerInvariant().StartsWith(t.Text, StringComparison.Ordinal);
                        break;
                    default:
                        hit = lower.Contains(t.Text);
                        break;
                }
                if (t.Exclude && hit) { return false; }
                if (!t.Exclude && !hit) { return false; }
            }
            return true;
        }

        /// <summary>
        /// Where each plain-text term appears in <paramref name="line"/>, so the view can
        /// mark WHY a line matched. Overlapping and touching ranges are merged, and the
        /// result is ordered, so a caller can paint them in one pass.
        /// </summary>
        public List<KeyValuePair<int, int>> Highlights(string line)
        {
            var spans = new List<KeyValuePair<int, int>>();
            if (string.IsNullOrEmpty(line)) { return spans; }

            foreach (Term t in _terms)
            {
                if (t.Exclude || t.What != Kind.Text || t.Text.Length == 0) { continue; }
                int from = 0;
                while (from <= line.Length - t.Text.Length)
                {
                    int at = line.IndexOf(t.Text, from, StringComparison.OrdinalIgnoreCase);
                    if (at < 0) { break; }
                    spans.Add(new KeyValuePair<int, int>(at, t.Text.Length));
                    from = at + t.Text.Length;
                }
            }
            if (spans.Count < 2) { return spans; }

            spans.Sort((a, b) => a.Key.CompareTo(b.Key));
            var merged = new List<KeyValuePair<int, int>>();
            int start = spans[0].Key, end = spans[0].Key + spans[0].Value;
            for (int i = 1; i < spans.Count; i++)
            {
                int s = spans[i].Key, e = s + spans[i].Value;
                if (s <= end) { if (e > end) { end = e; } }
                else { merged.Add(new KeyValuePair<int, int>(start, end - start)); start = s; end = e; }
            }
            merged.Add(new KeyValuePair<int, int>(start, end - start));
            return merged;
        }

        /// <summary>A plain-English reading of the query, for the filter box's tooltip.</summary>
        public string Describe()
        {
            if (_terms.Count == 0) { return Localization.T("Showing every line."); }

            var want = new List<string>();
            var not = new List<string>();
            foreach (Term t in _terms)
            {
                string piece = t.What == Kind.Tag ? Localization.F("tagged [{0}]", t.Text)
                             : t.What == Kind.Level ? Localization.F("level {0}", t.Text.ToUpperInvariant())
                             : "\"" + t.Text + "\"";
                (t.Exclude ? not : want).Add(piece);
            }

            var sb = new StringBuilder();
            if (want.Count > 0)
            {
                sb.Append(Localization.T("Showing lines that match")).Append(' ').Append(string.Join(" + ", want));
            }
            else
            {
                sb.Append(Localization.T("Showing every line"));
            }
            if (not.Count > 0)
            {
                sb.Append(", ").Append(Localization.T("except")).Append(' ').Append(string.Join(" / ", not));
            }
            sb.Append('.');
            return sb.ToString();
        }
    }
}
