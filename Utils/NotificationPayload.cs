using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Everything a mirrored notification's toast says that the listener API does not hand out. That API
    /// gives Tempo the TEXT only — so a notification Windows showed with a picture, under a web site's own
    /// name and icon, with a progress bar, or as a reminder that stays until it is dismissed, became a
    /// plain text card from "Microsoft Edge" that timed out like any other.
    ///
    /// Windows keeps each notification's whole toast XML in its own store,
    /// %LOCALAPPDATA%\Microsoft\Windows\Notifications\wpndatabase.db, keyed by the same id the listener
    /// reports — beside the values an app filled in afterwards (table NotificationData: a progress bar's
    /// "{progressValue}") and what Windows knows about the SENDER (table HandlerAssets: Edge registers
    /// each web site as a sender with its own DisplayName and IconUri, which is what Windows' pop-up
    /// wears). This reads exactly that one notification's rows through Windows' own SQLite
    /// (winsqlite3.dll), and loads the pictures they name.
    ///
    /// A progress bar is read as it ARRIVED: an app's later updates to it (ToastNotifier.Update) never
    /// reach the store — measured unchanged six seconds after two updates — and the listener reports
    /// no updates at all, so the card shows the bar the notification came with.
    ///
    /// The rules, because this reads a system store:
    ///   * NEVER writes. The connection is opened read-write ONLY because a read-only one cannot see rows
    ///     still in the write-ahead log (measured: it saw nothing newer than the last checkpoint, while a
    ///     read-write one found the new row 11 ms after the listener saw it). Writes are refused
    ///     (PRAGMA query_only) and the close never checkpoints (SQLITE_DBCONFIG_NO_CKPT_ON_CLOSE).
    ///   * Local pictures only. A web address is NOT fetched (nothing goes online for the mirror), and a
    ///     network path is refused outright: opening \\server\share\x.png would hand that server the
    ///     user's Windows sign-in hash — a toast from any app could name one.
    ///   * Best-effort and self-limiting: any failure means a card without the extras, and repeated
    ///     failures (a Windows build with another layout, a missing DLL) switch this off for the session.
    /// </summary>
    public static class NotificationPayload
    {
        public sealed class Details
        {
            /// <summary>The picture under the text (hero or inline image), or null.</summary>
            public Image Picture;
            /// <summary>The picture beside the text (appLogoOverride: a contact's photo, a site's notification icon), or null.</summary>
            public Image Logo;
            /// <summary>The sender's own icon, as Windows' pop-up header shows it (an Edge web site's), or null.</summary>
            public Image SenderIcon;
            /// <summary>Everything else the toast said. Never null.</summary>
            public NotificationExtras Extras = new NotificationExtras();

            public void DisposeImages()
            {
                try { Picture?.Dispose(); } catch { }
                try { Logo?.Dispose(); } catch { }
                try { SenderIcon?.Dispose(); } catch { }
                Picture = Logo = SenderIcon = null;
            }
        }

        /// <summary>For the tests: a database file instead of Windows' own.</summary>
        internal static string DatabasePathForTest;

        private const int MaxPictureBytes = 8 * 1024 * 1024;
        private const int HeroMaxWidth = 720, HeroMaxHeight = 400;   // twice the card's picture area — sharp, bounded
        private const int LogoMax = 96;                               // drawn at 44 px beside the text
        private const int SenderIconMax = 64;                         // drawn at 26 px in the header
        private const int MaxLaunchChars = 8192;                      // launch arguments kept for a click (Chromium's are ~120)
        private const int FailuresBeforeOff = 5;

        private static int _lookups, _read, _withPicture, _withSender, _kept, _withProgress, _rowMissing, _notLocal,
                           _failures, _consecutiveFailures;
        private static volatile bool _off;
        private static volatile string _lastError;

        /// <summary>One line for Live debug.</summary>
        public static string Describe()
        {
            if (Volatile.Read(ref _lookups) == 0 && !_off) { return "toast details: none read yet"; }
            var sb = new StringBuilder("toast details: ");
            sb.Append(_read).Append(" of ").Append(_lookups).Append(" read from Windows' store");
            sb.Append(" · ").Append(_withPicture).Append(" with a picture");
            if (_withSender > 0) { sb.Append(" · ").Append(_withSender).Append(" from a sender of their own (a web site)"); }
            if (_kept > 0) { sb.Append(" · ").Append(_kept).Append(" kept up until dismissed (reminder/alarm/call)"); }
            if (_withProgress > 0) { sb.Append(" · ").Append(_withProgress).Append(" with a progress bar"); }
            if (_notLocal > 0) { sb.Append(" · ").Append(_notLocal).Append(" online/packaged picture(s) not fetched"); }
            if (_rowMissing > 0) { sb.Append(" · ").Append(_rowMissing).Append(" not in Windows' store yet"); }
            if (_failures > 0) { sb.Append(" · ").Append(_failures).Append(" error(s)"); }
            if (_off) { sb.Append(" · ⚠ OFF for this session (").Append(_lastError ?? "repeated errors").Append(')'); }
            else if (_lastError != null) { sb.Append(" · last error: ").Append(_lastError); }
            return sb.ToString();
        }

        /// <summary>
        /// What Windows' store holds about listener notification <paramref name="id"/>, or null when it could
        /// not be read (not there, or the reader is off). <paramref name="aumid"/> resolves ms-appdata
        /// addresses into the sender's package folders. Never throws; the caller owns (and disposes) the
        /// pictures it gets.
        /// </summary>
        public static Details TryGet(uint id, string aumid)
        {
            if (_off || id == 0) { return null; }
            Interlocked.Increment(ref _lookups);
            Row row;
            try
            {
                row = ReadRow(id);
                if (row == null)
                {
                    // Not there yet (the store is written as the notification arrives, but give it a moment
                    // once) — or gone already.
                    Thread.Sleep(60);
                    row = ReadRow(id);
                }
                Interlocked.Exchange(ref _consecutiveFailures, 0);
            }
            catch (Exception ex)
            {
                Fail(ex is DllNotFoundException || ex is EntryPointNotFoundException
                     ? "Windows' SQLite is not available" : ex.Message);
                return null;
            }
            if (row == null) { Interlocked.Increment(ref _rowMissing); return null; }
            Interlocked.Increment(ref _read);

            try
            {
                Details d = Parse(row, aumid);
                if (d.Picture != null || d.Logo != null) { Interlocked.Increment(ref _withPicture); }
                if (d.Extras.Sender.Length > 0) { Interlocked.Increment(ref _withSender); }
                if (d.Extras.StaysUntilDismissed) { Interlocked.Increment(ref _kept); }
                if (d.Extras.Progress != null) { Interlocked.Increment(ref _withProgress); }
                return d;
            }
            catch (Exception ex)
            {
                Logger.Swallow("NotificationPayload.Parse", ex);
                return null;
            }
        }

        private static void Fail(string why)
        {
            Interlocked.Increment(ref _failures);
            _lastError = why;
            if (Interlocked.Increment(ref _consecutiveFailures) >= FailuresBeforeOff && !_off)
            {
                _off = true;
                Logger.Warn("[Notify] notification details switched off for this session: " + why);
            }
        }

        // ── the store ──────────────────────────────────────────────────────────────────

        private const int SQLITE_OK = 0, SQLITE_ROW = 100;
        private const int SQLITE_OPEN_READWRITE = 0x00000002, SQLITE_OPEN_NOMUTEX = 0x00008000;
        private const int SQLITE_DBCONFIG_NO_CKPT_ON_CLOSE = 1006;

        [DllImport("winsqlite3.dll")] private static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
        [DllImport("winsqlite3.dll")] private static extern int sqlite3_close(IntPtr db);
        [DllImport("winsqlite3.dll")] private static extern int sqlite3_db_config(IntPtr db, int op, int value, IntPtr result);
        [DllImport("winsqlite3.dll")] private static extern int sqlite3_busy_timeout(IntPtr db, int ms);
        [DllImport("winsqlite3.dll")] private static extern int sqlite3_exec(IntPtr db, byte[] sql, IntPtr cb, IntPtr arg, IntPtr err);
        [DllImport("winsqlite3.dll")] private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int n, out IntPtr stmt, IntPtr tail);
        [DllImport("winsqlite3.dll")] private static extern int sqlite3_bind_int64(IntPtr stmt, int i, long v);
        [DllImport("winsqlite3.dll")] private static extern int sqlite3_step(IntPtr stmt);
        [DllImport("winsqlite3.dll")] private static extern IntPtr sqlite3_column_blob(IntPtr stmt, int col);
        [DllImport("winsqlite3.dll")] private static extern int sqlite3_column_bytes(IntPtr stmt, int col);
        [DllImport("winsqlite3.dll")] private static extern IntPtr sqlite3_column_text(IntPtr stmt, int col);
        [DllImport("winsqlite3.dll")] private static extern long sqlite3_column_int64(IntPtr stmt, int col);
        [DllImport("winsqlite3.dll")] private static extern int sqlite3_finalize(IntPtr stmt);
        [DllImport("winsqlite3.dll")] private static extern IntPtr sqlite3_errmsg(IntPtr db);

        private static byte[] U8(string s) => Encoding.UTF8.GetBytes(s + "\0");

        private static string DatabasePath =>
            DatabasePathForTest ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                                @"Microsoft\Windows\Notifications\wpndatabase.db");

        /// <summary>One notification's rows: its toast, the values filled in for it, and its sender's name and icon.</summary>
        private sealed class Row
        {
            public string Xml;
            public readonly Dictionary<string, string> Data = new Dictionary<string, string>(StringComparer.Ordinal);
            public string SenderName;
            public string SenderIcon;
        }

        /// <summary>Notification <paramref name="id"/>'s rows, or null when there is no such notification. Throws on errors.</summary>
        private static Row ReadRow(uint id)
        {
            string path = DatabasePath;
            if (!File.Exists(path)) { throw new FileNotFoundException("Windows' notification store is not there"); }
            // No SQLITE_OPEN_CREATE: a missing file is an error, never a new database.
            int rc = sqlite3_open_v2(U8(path), out IntPtr db, SQLITE_OPEN_READWRITE | SQLITE_OPEN_NOMUTEX, IntPtr.Zero);
            try
            {
                if (rc != SQLITE_OK) { throw new IOException("open: " + Marshal.PtrToStringUTF8(sqlite3_errmsg(db))); }
                sqlite3_db_config(db, SQLITE_DBCONFIG_NO_CKPT_ON_CLOSE, 1, IntPtr.Zero);
                sqlite3_busy_timeout(db, 150);
                sqlite3_exec(db, U8("PRAGMA query_only = 1"), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

                var row = new Row();
                long handler = -1;
                Query(db, "SELECT Payload, HandlerId FROM Notification WHERE Id = ?1", id, true, st =>
                {
                    int n = sqlite3_column_bytes(st, 0);
                    if (n <= 0 || n > 1024 * 1024) { return; }
                    var bytes = new byte[n];
                    Marshal.Copy(sqlite3_column_blob(st, 0), bytes, 0, n);
                    row.Xml = Encoding.UTF8.GetString(bytes);
                    handler = sqlite3_column_int64(st, 1);
                });
                if (row.Xml == null) { return null; }

                // The two below only add to it: a Windows build without these tables still gets the toast.
                Query(db, "SELECT Key, Value FROM NotificationData WHERE NotificationId = ?1", id, false, st =>
                {
                    string k = Text(st, 0);
                    if (!string.IsNullOrEmpty(k) && row.Data.Count < 64) { row.Data[k] = Text(st, 1) ?? ""; }
                });
                if (handler >= 0)
                {
                    Query(db, "SELECT AssetKey, AssetValue FROM HandlerAssets WHERE HandlerId = ?1", handler, false, st =>
                    {
                        string k = Text(st, 0);
                        if (string.Equals(k, "DisplayName", StringComparison.OrdinalIgnoreCase)) { row.SenderName = Text(st, 1); }
                        else if (string.Equals(k, "IconUri", StringComparison.OrdinalIgnoreCase)) { row.SenderIcon = Text(st, 1); }
                    });
                }
                return row;
            }
            finally { sqlite3_close(db); }
        }

        private static string Text(IntPtr st, int col)
        {
            IntPtr p = sqlite3_column_text(st, col);
            return p == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(p);
        }

        /// <summary>Runs one query with one number bound to ?1. A <paramref name="required"/> query that will not prepare is an error; another is skipped.</summary>
        private static void Query(IntPtr db, string sql, long arg, bool required, Action<IntPtr> onRow)
        {
            if (sqlite3_prepare_v2(db, U8(sql), -1, out IntPtr st, IntPtr.Zero) != SQLITE_OK)
            {
                if (required) { throw new IOException("query: " + Marshal.PtrToStringUTF8(sqlite3_errmsg(db))); }
                return;
            }
            try
            {
                sqlite3_bind_int64(st, 1, arg);
                for (int rows = 0; rows < 256 && sqlite3_step(st) == SQLITE_ROW; rows++) { onRow(st); }
            }
            finally { sqlite3_finalize(st); }
        }

        // ── the XML and the files ─────────────────────────────────────────────────────

        /// <summary>For the tests: what a toast, its filled-in values and its sender's assets come to.</summary>
        internal static Details ParseForTest(string xml, string aumid, IDictionary<string, string> data = null,
                                             string senderName = null, string senderIcon = null)
        {
            var row = new Row { Xml = xml, SenderName = senderName, SenderIcon = senderIcon };
            if (data != null) { foreach (var kv in data) { row.Data[kv.Key] = kv.Value; } }
            return Parse(row, aumid);
        }

        private static Details Parse(Row row, string aumid)
        {
            var doc = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(new StringReader(row.Xml),
                       new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
            {
                doc.Load(reader);
            }
            var d = new Details();
            NotificationExtras x = d.Extras;
            XmlElement toast = doc.DocumentElement;
            if (toast == null) { return d; }

            // An attribute may be "{key}", filled in from the values the app supplied (or updated) for it.
            string Get(XmlElement e, string attr) => Resolve(e.GetAttribute(attr), row.Data);

            x.Scenario = Get(toast, "scenario").Trim().ToLowerInvariant();
            x.LongDuration = Get(toast, "duration").Trim().Equals("long", StringComparison.OrdinalIgnoreCase);
            string stamp = Get(toast, "displayTimestamp").Trim();
            if (stamp.Length > 0 && DateTime.TryParse(stamp, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime sent))
            {
                x.SentUtc = sent;
            }
            // What Windows does with a click: hands these launch arguments to the app (its activator, see
            // ToastActivator), or — a protocol launch — opens them as an address. LaunchUrl is that address when it
            // is a web page.
            x.ActivationType = Get(toast, "activationType").Trim().ToLowerInvariant();
            string launchArgs = Get(toast, "launch");
            x.Launch = launchArgs.Length <= MaxLaunchChars ? launchArgs : "";
            if (x.ActivationType == "protocol")
            {
                Uri launch = NotificationExtras.WebAddress(launchArgs);
                if (launch != null) { x.LaunchUrl = launch.AbsoluteUri; }
            }

            foreach (XmlElement t in toast.GetElementsByTagName("text"))
            {
                if (t.GetAttribute("placement").Equals("attribution", StringComparison.OrdinalIgnoreCase))
                {
                    x.Attribution = Short(Resolve(t.InnerText, row.Data), 120);
                    break;
                }
            }

            string hero = null, inline = null, logo = null;
            foreach (XmlElement img in toast.GetElementsByTagName("image"))
            {
                string src = Get(img, "src");
                string placement = img.GetAttribute("placement");
                if (string.IsNullOrWhiteSpace(src)) { continue; }
                if (placement.Equals("hero", StringComparison.OrdinalIgnoreCase)) { hero ??= src; }
                else if (placement.Equals("appLogoOverride", StringComparison.OrdinalIgnoreCase))
                {
                    if (logo == null)
                    {
                        logo = src;
                        x.LogoCircle = img.GetAttribute("hint-crop").Trim().Equals("circle", StringComparison.OrdinalIgnoreCase);
                    }
                }
                else if (placement.Length == 0 || placement.Equals("inline", StringComparison.OrdinalIgnoreCase)) { inline ??= src; }
            }

            foreach (XmlElement p in toast.GetElementsByTagName("progress"))
            {
                var bar = new NotificationExtras.ProgressBar
                {
                    Title = Short(Get(p, "title"), 120),
                    Status = Short(Get(p, "status"), 120),
                    ValueText = Short(Get(p, "valueStringOverride"), 60),
                };
                string v = Get(p, "value").Trim();
                if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double f) && !double.IsNaN(f))
                {
                    bar.Value = Math.Max(0, Math.Min(1, f));
                }
                // A bar whose every value is still an unfilled {binding} says nothing: no bar.
                if (bar.Value >= 0 || v.Equals("indeterminate", StringComparison.OrdinalIgnoreCase)
                    || bar.Status.Length > 0 || bar.Title.Length > 0 || bar.ValueText.Length > 0)
                {
                    x.Progress = bar;
                }
                break;
            }

            // Windows keeps a reminder or an alarm up until it is dismissed only when it carries at least
            // one button (without one it is "treated as a normal toast"), and an incoming call always.
            bool button = false;
            foreach (XmlElement a in toast.GetElementsByTagName("action"))
            {
                if (a.GetAttribute("placement").Equals("contextMenu", StringComparison.OrdinalIgnoreCase)) { continue; }
                button = true;

                // The buttons the card can press as Windows would: not one that sends what was typed into a reply
                // box (the card has no box), not Windows' snooze (it needs the toast's own time picker).
                string text = Short(Get(a, "content"), 40);
                string type = Get(a, "activationType").Trim().ToLowerInvariant();
                string args = Get(a, "arguments");
                if (text.Length == 0 || x.Buttons.Count >= 3 || args.Length > MaxLaunchChars) { continue; }
                if (a.GetAttribute("hint-inputId").Trim().Length > 0) { continue; }
                if (type == "system")
                {
                    if (!args.Trim().Equals("dismiss", StringComparison.OrdinalIgnoreCase)) { continue; }
                    type = "dismiss";
                }
                x.Buttons.Add(new NotificationExtras.ToastButton { Text = text, Arguments = args, ActivationType = type });
            }
            x.StaysUntilDismissed = x.Scenario == "incomingcall"
                                    || ((x.Scenario == "reminder" || x.Scenario == "alarm") && button);

            x.Sender = Short(row.SenderName, 80);

            // Pictures last: everything above is cheap, these read files.
            d.Picture = Load(hero ?? inline, aumid, HeroMaxWidth, HeroMaxHeight);
            d.Logo = Load(logo, aumid, LogoMax, LogoMax);
            d.SenderIcon = Load(row.SenderIcon, aumid, SenderIconMax, SenderIconMax);
            return d;
        }

        /// <summary>"{key}" → the value the app supplied for it ("" when it supplied none); anything else as it is.</summary>
        private static string Resolve(string v, Dictionary<string, string> data)
        {
            if (string.IsNullOrEmpty(v)) { return ""; }
            string t = v.Trim();
            if (t.Length >= 2 && t[0] == '{' && t[t.Length - 1] == '}')
            {
                return data != null && data.TryGetValue(t.Substring(1, t.Length - 2), out string val) ? val ?? "" : "";
            }
            return v;
        }

        private static string Short(string s, int max)
        {
            s = (s ?? "").Trim();
            return s.Length <= max ? s : s.Substring(0, max - 1).TrimEnd() + "…";
        }

        private static Image Load(string src, string aumid, int maxW, int maxH)
        {
            if (string.IsNullOrWhiteSpace(src)) { return null; }
            string path = LocalPathFor(src, aumid);
            if (path == null) { Interlocked.Increment(ref _notLocal); return null; }
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length <= 0 || fi.Length > MaxPictureBytes) { return null; }
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var raw = Image.FromStream(fs, false, true))
                {
                    double scale = Math.Min(1.0, Math.Min((double)maxW / raw.Width, (double)maxH / raw.Height));
                    int w = Math.Max(1, (int)Math.Round(raw.Width * scale));
                    int h = Math.Max(1, (int)Math.Round(raw.Height * scale));
                    var copy = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    using (var g = Graphics.FromImage(copy))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(raw, 0, 0, w, h);
                    }
                    return copy;   // independent of the file, which its app may delete at any moment
                }
            }
            catch (Exception ex)
            {
                Logger.Swallow("NotificationPayload.Load", ex);
                return null;
            }
        }

        /// <summary>
        /// The local file a toast's image address names, or null for anything that is not a local file
        /// Tempo will open: web addresses (not fetched), network paths (refused — see the class summary),
        /// ms-appx (inside an app package) and anything unparseable.
        /// </summary>
        internal static string LocalPathFor(string src, string aumid)
        {
            if (string.IsNullOrWhiteSpace(src)) { return null; }
            src = src.Trim();
            string path = null;
            if (src.StartsWith("ms-appdata:///", StringComparison.OrdinalIgnoreCase))
            {
                path = AppDataPath(src.Substring("ms-appdata:///".Length), aumid);
            }
            else if (src.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                if (!Uri.TryCreate(src, UriKind.Absolute, out Uri u) || !u.IsFile || u.IsUnc) { return null; }
                path = u.LocalPath;
            }
            else if (src.IndexOf("://", StringComparison.Ordinal) < 0 && src.Length > 2 && src[1] == ':' && char.IsLetter(src[0]))
            {
                path = src;   // a plain drive path
            }
            if (path == null) { return null; }
            try { path = Path.GetFullPath(path); }
            catch { return null; }
            // Never a network location, however it is spelt (\\server, \\?\UNC\…, a mapped-looking form).
            if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.Length < 3 || path[1] != ':') { return null; }
            return path;
        }

        /// <summary>ms-appdata:///local|temp|roaming/… → the sender package's LocalState / TempState / RoamingState.</summary>
        private static string AppDataPath(string rest, string aumid)
        {
            if (string.IsNullOrEmpty(aumid)) { return null; }
            int bang = aumid.IndexOf('!');
            string family = bang > 0 ? aumid.Substring(0, bang) : null;
            // A package family name is "<name>_<13-char publisher id>"; anything else is not a package.
            if (family == null || family.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || family.IndexOf('_') < 0) { return null; }
            int slash = rest.IndexOf('/');
            if (slash <= 0) { return null; }
            string kind = rest.Substring(0, slash).ToLowerInvariant();
            string sub = kind == "local" ? "LocalState" : kind == "temp" ? "TempState" : kind == "roaming" ? "RoamingState" : null;
            if (sub == null) { return null; }
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", family, sub);
            string rel = Uri.UnescapeDataString(rest.Substring(slash + 1)).Replace('/', '\\');
            string full;
            try { full = Path.GetFullPath(Path.Combine(root, rel)); }
            catch { return null; }
            // Stay inside that package's folder: "..\..\" must not walk out of it.
            return full.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase) ? full : null;
        }

        /// <summary>For the tests.</summary>
        internal static void ResetForTest()
        {
            _lookups = _read = _withPicture = _withSender = _kept = _withProgress = _rowMissing = _notLocal = _failures = _consecutiveFailures = 0;
            _off = false;
            _lastError = null;
        }
    }
}
