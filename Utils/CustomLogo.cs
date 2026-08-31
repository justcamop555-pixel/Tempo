using System;
using System.Drawing;
using System.IO;
using System.Net;
using AutoClicker.Persistence;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Stores an optional user-supplied logo (e.g. an animated GIF) that overrides
    /// the built-in About logo. The image can come from a dropped file or a dropped
    /// image URL (dragged from a web browser). Saved in the app data folder so it
    /// persists across runs.
    /// </summary>
    public static class CustomLogo
    {
        private const string BaseName = "customlogo";
        private const long MaxBytes = 15 * 1024 * 1024; // 15 MB cap

        private static string Dir => SettingsManager.GetSettingsDirectory();

        /// <summary>Path to the saved custom logo, or null if none is set.</summary>
        public static string GetPath()
        {
            try
            {
                if (!Directory.Exists(Dir)) return null;
                foreach (string f in Directory.GetFiles(Dir, BaseName + ".*"))
                {
                    return f;
                }
            }
            catch
            {
                // fall through
            }
            return null;
        }

        public static bool Exists() => GetPath() != null;

        /// <summary>
        /// Raised after the custom logo is set or cleared, so anything already showing it
        /// can pick the new one up without a restart. The header caches its logo bitmap,
        /// and About only ever refreshed its own preview.
        /// </summary>
        public static event Action Changed;

        private static void RaiseChanged()
        {
            try { Changed?.Invoke(); }
            catch (Exception ex) { Logger.Swallow("CustomLogo.Changed", ex); }
        }

        public static void Clear()
        {
            ClearQuietly();
            RaiseChanged();
        }

        /// <summary>
        /// Deletes the logo WITHOUT announcing it. Used by <see cref="SaveBytes"/>, which
        /// clears before writing the replacement — announcing there would tell listeners
        /// "no logo" for the moment between the delete and the write, so they'd refresh
        /// twice and briefly show the fallback.
        /// </summary>
        private static void ClearQuietly()
        {
            try
            {
                string p = GetPath();
                if (p != null) File.Delete(p);
            }
            catch
            {
                // ignore
            }
        }

        /// <summary>Saves image bytes as the custom logo, validating that they decode.</summary>
        public static bool SaveBytes(byte[] data, out string error)
        {
            error = null;
            if (data == null || data.Length == 0)
            {
                error = "The image was empty.";
                return false;
            }
            if (data.Length > MaxBytes)
            {
                error = "That image is too large (over 15 MB).";
                return false;
            }

            // Validate it really is an image before keeping it.
            try
            {
                using (var ms = new MemoryStream(data))
                using (Image.FromStream(ms))
                {
                    // decoded fine
                }
            }
            catch
            {
                error = "That file doesn't look like a supported image.";
                return false;
            }

            try
            {
                Directory.CreateDirectory(Dir);
                ClearQuietly();
                File.WriteAllBytes(Path.Combine(Dir, BaseName + Sniff(data)), data);
                RaiseChanged();
                return true;
            }
            catch (Exception ex)
            {
                error = "Couldn't save the image: " + ex.Message;
                return false;
            }
        }

        public static bool SaveFromFile(string path, out string error)
        {
            error = null;
            try
            {
                if (!File.Exists(path))
                {
                    error = "The file no longer exists.";
                    return false;
                }
                var info = new FileInfo(path);
                if (info.Length > MaxBytes)
                {
                    error = "That image is too large (over 15 MB).";
                    return false;
                }
                return SaveBytes(File.ReadAllBytes(path), out error);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Saves an inline "data:image/…;base64,…" image. Canvases, inline SVGs and
        /// lazy-loading galleries hand one of these to a drag instead of an address,
        /// and there is nothing to download — the bytes are already in the string.
        /// </summary>
        public static bool SaveFromDataUri(string uri, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(uri) ||
                !uri.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                error = "That doesn't look like an image.";
                return false;
            }
            try
            {
                int comma = uri.IndexOf(',');
                if (comma < 0)
                {
                    error = "That image link is incomplete.";
                    return false;
                }
                string meta = uri.Substring(0, comma);
                string payload = uri.Substring(comma + 1);
                if (meta.IndexOf("base64", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    // The percent-encoded form is rare and is usually an SVG, which
                    // System.Drawing cannot load anyway — say so rather than fail oddly.
                    error = "That image is in a format Tempo can't read.";
                    return false;
                }
                byte[] bytes = Convert.FromBase64String(payload.Trim());
                if (bytes.Length > MaxBytes)
                {
                    error = "That image is too large (over 15 MB).";
                    return false;
                }
                return SaveBytes(bytes, out error);
            }
            catch (Exception ex)
            {
                error = "Couldn't read that image: " + ex.Message;
                return false;
            }
        }

        /// <summary>Downloads an image from a URL (e.g. dragged from a browser) and saves it.</summary>
        public static bool SaveFromUrl(string url, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(url) ||
                !(url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                  url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                error = "That doesn't look like an image link.";
                return false;
            }

            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                // A browser-shaped User-Agent, because "Tempo" was getting 403ed. Plenty
                // of image hosts and CDNs reject unrecognised agents outright, so an
                // image dragged from a page that displayed it perfectly well would fail
                // to download with a bare "(403) Forbidden".
                request.UserAgent =
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                    "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";
                request.Accept = "image/avif,image/webp,image/apng,image/*,*/*;q=0.8";
                // Hotlink protection: many hosts serve the image only when the request
                // looks like it came from their own page. The image's own origin is the
                // best guess available from a drag and satisfies the common cases.
                try
                {
                    var origin = new Uri(url);
                    request.Referer = origin.GetLeftPart(UriPartial.Authority) + "/";
                }
                catch { }
                request.Timeout = 20000;
                request.AllowAutoRedirect = true;

                using (var response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (var ms = new MemoryStream())
                {
                    if (stream == null)
                    {
                        error = "No data was returned by the link.";
                        return false;
                    }

                    var buffer = new byte[8192];
                    int read;
                    long total = 0;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        total += read;
                        if (total > MaxBytes)
                        {
                            error = "That image is too large (over 15 MB).";
                            return false;
                        }
                        ms.Write(buffer, 0, read);
                    }

                    return SaveBytes(ms.ToArray(), out error);
                }
            }
            catch (Exception ex)
            {
                error = "Couldn't download that image: " + ex.Message;
                return false;
            }
        }

        /// <summary>Picks a file extension from the image's magic bytes.</summary>
        private static string Sniff(byte[] d)
        {
            if (d.Length >= 6 && d[0] == 'G' && d[1] == 'I' && d[2] == 'F') return ".gif";
            if (d.Length >= 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47) return ".png";
            if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF) return ".jpg";
            if (d.Length >= 2 && d[0] == 'B' && d[1] == 'M') return ".bmp";
            return ".img";
        }
    }
}
