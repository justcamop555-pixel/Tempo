using System;
using System.Reflection;

namespace AutoClicker.Utils
{
    /// <summary>
    /// The running assembly's version as "1.0.319".
    ///
    /// Exists because the startup log spelled the version out as a literal —
    /// "Tempo starting (version 1.0.319)" — which would have gone on claiming 1.0.319
    /// out of every release after it, in the one line most likely to be quoted in a bug
    /// report. Read it, never type it.
    ///
    /// See <see cref="BuildInfo"/> for which BUILD this is; the version cannot tell you
    /// that on its own.
    /// </summary>
    public static class AppVersion
    {
        private static string _text;

        /// <summary>"1.0.319", or "" if the version can't be read.</summary>
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

        /// <summary>"v1.0.319" — the form the UI stamps use.</summary>
        public static string Stamp
        {
            get { return Text.Length == 0 ? "" : "v" + Text; }
        }
    }
}
