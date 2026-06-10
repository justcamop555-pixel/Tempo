using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Loads Tempo's application icon (embedded as Assets\tempo.ico). Falls back to
    /// the executable's own icon, then the system default, so the UI always has one.
    /// </summary>
    public static class AppIcon
    {
        private static Icon _cached;

        public static Icon Get()
        {
            if (_cached != null)
            {
                return _cached;
            }

            // Preferred: the icon embedded in the assembly.
            try
            {
                Assembly asm = typeof(AppIcon).Assembly;
                foreach (string name in asm.GetManifestResourceNames())
                {
                    if (name.EndsWith("tempo.ico", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var stream = asm.GetManifestResourceStream(name))
                        {
                            if (stream != null)
                            {
                                _cached = new Icon(stream);
                                return _cached;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fall through to the alternatives below.
            }

            // Next best: the icon baked into the .exe via <ApplicationIcon>.
            try
            {
                _cached = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (_cached != null)
                {
                    return _cached;
                }
            }
            catch
            {
                // Ignore and use the system default.
            }

            _cached = SystemIcons.Application;
            return _cached;
        }
    }
}
