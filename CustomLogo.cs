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

        public static void Clear()
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
                Clear();
                File.WriteAllBytes(Path.Combine(Dir, BaseName + Sniff(data)), data);
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
                request.UserAgent = "Tempo";
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
