using System;
using System.Reflection;

namespace AutoClicker.Utils
{
    /// <summary>
    /// The running assembly's version, e.g. "1.0.320".
    ///
    /// Exists because the startup log spelled the version out as a literal —
    /// "Tempo starting (version 1.0.319)" — which would have gone on claiming that
    /// out of every release after it, in the one line most likely to be quoted in a bug
    /// report. Read it, never type it.
    ///
    /// See <see cref="BuildInfo"/> for which BUILD this is; the version cannot tell you
    /// that on its own.
    /// </summary>
    public static class AppVersion
    {
        private static string _text;

        /// <summary>The version, or "" if it can't be read.</summary>
        public static string Text
        {
            get
            {
                if (_text != null) { return _text; }
                try
                {
                    Version v = Assembly.GetExecutingAssembly().GetName().Version;
                    _text = v != null ? v.Major + "." + v.Minor + "." + v.Build : "";
                }
                catch { _text = ""; }
                return _text;
            }
        }

        /// <summary>The version with a leading "v" — the form the UI stamps use.</summary>
        public static string Stamp
        {
            get { return Text.Length == 0 ? "" : "v" + Text; }
        }
    }
}
