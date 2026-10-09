using System;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Reads the fixed parts of a line Tempo's logger wrote.
    ///
    /// Every line has the shape
    ///     2026-08-31 08:31:02.123 [INFO] [Captions] engine started
    /// so the level is the first bracketed token and the subsystem tag, when there is one,
    /// is the second. Both are read from where they actually are rather than searched for
    /// anywhere in the line — the distinction matters, because log lines quote text the user
    /// can influence (translated captions, window titles, transcribed speech), and a
    /// "does it contain the word…" test classifies those as whatever they happen to mention.
    /// </summary>
    public static class LogLine
    {
        /// <summary>"INFO", "WARN", "ERROR", … or null when the line has no level.</summary>
        public static string LevelOf(string line)
        {
            if (string.IsNullOrEmpty(line)) { return null; }
            int open = line.IndexOf('[');
            if (open < 0) { return null; }
            int close = line.IndexOf(']', open + 1);
            if (close <= open + 1) { return null; }
            return line.Substring(open + 1, close - open - 1);
        }

        /// <summary>
        /// The subsystem tag ("Captions", "2nd cursor", …), or null when the line has none.
        ///
        /// Deliberately NOT limited to [INFO]. The old reader anchored on the literal
        /// "[INFO] ", so every warning and every error carried no tag at all as far as the
        /// rest of the window was concerned — which is a strange blind spot for a diagnostic
        /// view, since those are the lines someone opened it to find.
        /// </summary>
        public static string TagOf(string line)
        {
            if (string.IsNullOrEmpty(line)) { return null; }
            int open = line.IndexOf('[');
            if (open < 0) { return null; }
            int close = line.IndexOf(']', open + 1);
            if (close <= open + 1) { return null; }

            // The tag is the NEXT bracketed token, and it has to start immediately after
            // the level's space — otherwise a message that merely mentions something in
            // brackets further along would be read as a tag.
            int at = close + 1;
            if (at >= line.Length || line[at] != ' ') { return null; }
            at++;
            if (at >= line.Length || line[at] != '[') { return null; }
            int tagClose = line.IndexOf(']', at + 1);
            if (tagClose <= at + 1) { return null; }
            return line.Substring(at + 1, tagClose - at - 1);
        }
    }
}
