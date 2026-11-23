using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Tempo's logo where WINDOWS draws it — the taskbar button, the Start menu, a notification's header (round 39:
    /// "Windows Notification shows old gif or picture … it also need on Windows Taskbar").
    ///
    /// None of those come from Tempo's window. Measured on Windows 11 26H2 (scratchpad/traysnap): the taskbar button of
    /// a program that has a Start-menu shortcut wears the SHORTCUT's icon and passes the window's own (WM_SETICON) over.
    /// A test window without a shortcut showed every icon it set; the same window with one showed only the shortcut's,
    /// and followed it the moment it changed. A toast's header is that shortcut's icon and name too. So a custom logo
    /// played in Tempo's title bar, tray and dialogs while the taskbar kept the picture built into TempoClicker.exe —
    /// and the notifications showed something else again (see <see cref="WindowsToast"/>).
    ///
    /// What this does: the logo's still icon (its first frame — Windows has no moving icons in those places) as a file
    /// of its own, under a new name for every logo so no cache can hand the old picture back, and every shortcut that
    /// opens THIS exe — Start menu, taskbar pins, Desktop — pointed at it. Only a shortcut still wearing the exe's own
    /// icon or a logo Tempo gave it before: an icon the user picked for a shortcut is theirs. No custom logo: back to
    /// the exe's icon. Shortcuts that open another copy of Tempo are left alone. Runs off the UI thread.
    ///
    /// The taskbar button itself (round 39b, "gif instead of one frame") no longer comes from here unless Tempo is pinned:
    /// <see cref="TaskbarIdentity"/> gives Tempo's windows an identity of their own, and the button then shows the window's
    /// icon — every frame of an animated logo. A pinned button is drawn from its pin, which this keeps on the still logo.
    /// </summary>
    public static class ShellLogo
    {
        private const string FilePrefix = "tempo-logo-";

        /// <summary>
        /// Where the logo's icon and picture are kept: beside the settings, in a folder of their own — never named
        /// customlogo.* (CustomLogo.GetPath takes any file by that name as the logo).
        /// </summary>
        public static string Folder => Path.Combine(Persistence.SettingsManager.GetSettingsDirectory(), "shell-icon");

        private static readonly object Gate = new object();
        private static int _queued;
        private static Timer _later;

        private static volatile string _toastAppId;   // the AppID Windows knows Tempo by (a Start-menu shortcut opens it), or null
        private static volatile string _picture;      // the logo as a PNG, for a toast's picture, or null
        private static volatile string _status = "not checked yet";
        private static string _lastLogged;

        /// <summary>The AppID a toast must carry to be Tempo's (the exe's path), or null when no Start-menu shortcut opens this copy.</summary>
        public static string ToastAppId => _toastAppId;

        /// <summary>The current logo as a 96 px PNG file, or null.</summary>
        public static string Picture => _picture;

        /// <summary>One line for Live debug: what the last sync found.</summary>
        public static string Status => _status;

        /// <summary>For the harnesses: other shortcut folders (the first stands for the Start menu) and another exe than this one.</summary>
        public static Func<string[]> FoldersForTest;
        public static string ExeForTest;
        public static string FolderForTest;

        /// <summary>A sync on the thread pool; a request while one is waiting to start is folded into it.</summary>
        public static void SyncSoon(string why)
        {
            if (Interlocked.Exchange(ref _queued, 1) == 1) { return; }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Interlocked.Exchange(ref _queued, 0);
                Sync(why);
            });
        }

        /// <summary>A sync after <paramref name="delayMs"/>: start-up's, out of the way of what the window does first.</summary>
        public static void SyncLater(int delayMs, string why)
        {
            lock (Gate)
            {
                _later?.Dispose();
                _later = new Timer(_ => SyncSoon(why), null, delayMs, Timeout.Infinite);
            }
        }

        /// <summary>Brings the shortcuts and the toast picture to the logo on disk now. One at a time; never throws.</summary>
        public static void Sync(string why)
        {
            lock (Gate)
            {
                try { SyncCore(why); }
                catch (Exception ex) { Logger.Swallow("ShellLogo.Sync", ex); }
            }
        }

        private static void SyncCore(string why)
        {
            string exe = ExeForTest ?? Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) { return; }
            string folder = FolderForTest ?? Folder;
            string[] folders = FoldersForTest != null ? FoldersForTest() : SelfInstaller.ShortcutFolders().ToArray();
            // ShortcutFolders: Desktop, taskbar pins, Start menu — the Start menu is what Windows knows the app by.
            string startMenu = FoldersForTest != null ? folders.FirstOrDefault() : folders.LastOrDefault();

            // The icon the shortcuts should wear: the custom logo's, or the exe's own.
            string logo = null;
            try { logo = CustomLogo.GetPath(); } catch { }
            byte[] ico = logo != null ? AppIcon.CustomLogoIcoBytes(logo) : null;
            string iconFile = ico != null ? Keep(folder, ico, ".ico") : null;

            // The picture a toast carries: the logo, custom or built in.
            string pngFile = null;
            try
            {
                using (System.Drawing.Image img = AppIcon.GetBitmap(96))
                using (var ms = new MemoryStream())
                {
                    if (img != null)
                    {
                        img.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        pngFile = Keep(folder, ms.ToArray(), ".png");
                    }
                }
            }
            catch (Exception ex) { Logger.Swallow("ShellLogo.Picture", ex); }

            string wantPath = iconFile ?? exe;
            List<SelfInstaller.ShortcutIcon> found = SelfInstaller.SetShortcutIcons(
                exe, wantPath, 0, folders, (path, index) => Replaceable(path, exe, folder));

            int changed = 0;
            foreach (SelfInstaller.ShortcutIcon s in found)
            {
                if (!s.Changed) { continue; }
                changed++;
                // Explorer, the Start menu and the taskbar re-read the shortcut (measured: the taskbar button and the next
                // toast's header changed within the second).
                try { SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW, s.Link, IntPtr.Zero); } catch { }
            }

            // A toast needs a Start-menu shortcut that opens this exe: that is the entry Windows lists the app under
            // (Get-StartApps: AppID = the exe's path). Without one, a toast is dropped without a word.
            bool inStartMenu = !string.IsNullOrEmpty(startMenu) && found.Any(s =>
                s.Error == null && string.Equals(Path.GetDirectoryName(s.Link), startMenu.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            _toastAppId = inStartMenu ? FullPath(exe) : null;
            _picture = pngFile;

            // Older pictures go once every shortcut that wore one wears the new one (a copy with no shortcut of its own
            // cleans nothing: the files may be another copy's).
            bool settled = found.Count > 0 && found.All(s => s.Error == null
                                                             && (s.Changed || SamePath(s.IconPath, wantPath) || !Replaceable(s.IconPath, exe, folder)));
            if (settled) { DeleteOthers(folder, iconFile, pngFile); }

            var mine = found.Where(s => s.Error == null).ToList();
            int wearing = mine.Count(s => s.Changed || SamePath(s.IconPath, wantPath));
            var theirs = mine.Where(s => !s.Changed && !SamePath(s.IconPath, wantPath)).ToList();
            var failed = found.Where(s => s.Error != null).ToList();
            _status = (iconFile != null ? "custom logo" : "Tempo's own icon") + " on " + wearing + " of " + found.Count + " shortcut(s)"
                      + (theirs.Count > 0 ? " · " + theirs.Count + " with an icon of their own" : "")
                      + (failed.Count > 0 ? " · " + failed.Count + " could not be changed" : "")
                      + " · notifications " + (inStartMenu ? "as Windows toasts" : "as tray balloons (no Start-menu shortcut opens this copy)");

            string line = "[Icon] Windows' own places (Start menu, notifications, taskbar pins) — " + _status + " (" + why + ")"
                          + (changed > 0 ? "; " + changed + " shortcut(s) given " + (iconFile != null ? "the logo: " : "Tempo's own icon back: ")
                                           + string.Join(", ", found.Where(s => s.Changed).Select(s => Path.GetFileName(s.Link)))
                                         : "")
                          + string.Concat(theirs.Select(s => "; left alone: " + Path.GetFileName(s.Link) + " has an icon the user chose (" + Path.GetFileName(s.IconPath) + ")"))
                          + string.Concat(failed.Select(s => "; " + Path.GetFileName(s.Link) + " could not be changed: " + s.Error))
                          + ".";
            // Once per distinct outcome: a start-up that changes nothing says nothing new.
            string key = _status + "|" + changed;
            if (changed > 0 || failed.Count > 0 || !string.Equals(key, _lastLogged, StringComparison.Ordinal))
            {
                _lastLogged = key;
                Logger.Info(line);
            }
        }

        /// <summary>
        /// Whether a shortcut's icon is Tempo's to change: none of its own (the target's icon), the exe's, or a logo Tempo
        /// put there before. Anything else the user picked.
        /// </summary>
        internal static bool Replaceable(string iconPath, string exe, string folder)
        {
            if (string.IsNullOrWhiteSpace(iconPath)) { return true; }
            return SamePath(iconPath, exe) || IsOurs(iconPath, folder);
        }

        private static bool IsOurs(string iconPath, string folder)
        {
            if (string.IsNullOrWhiteSpace(iconPath)) { return false; }
            string p = FullPath(iconPath), f = FullPath(folder).TrimEnd('\\') + "\\";
            return p.StartsWith(f, StringComparison.OrdinalIgnoreCase)
                   && Path.GetFileName(p).StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool SamePath(string a, string b) =>
            !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
            && string.Equals(FullPath(a), FullPath(b), StringComparison.OrdinalIgnoreCase);

        private static string FullPath(string p)
        {
            try { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(p.Trim().Trim('"'))); }
            catch { return p ?? ""; }
        }

        /// <summary>The bytes as a file named after them (so a new picture always has a new name), written once.</summary>
        private static string Keep(string folder, byte[] bytes, string ext)
        {
            try
            {
                Directory.CreateDirectory(folder);
                string name = FilePrefix + Convert.ToHexString(SHA256.HashData(bytes), 0, 6).ToLowerInvariant() + ext;
                string path = Path.Combine(folder, name);
                if (!File.Exists(path) || new FileInfo(path).Length != bytes.Length)
                {
                    string tmp = path + ".tmp";
                    File.WriteAllBytes(tmp, bytes);
                    File.Move(tmp, path, true);
                }
                return path;
            }
            catch (Exception ex)
            {
                Logger.Swallow("ShellLogo.Keep", ex);
                return null;
            }
        }

        private static void DeleteOthers(string folder, params string[] keep)
        {
            try
            {
                if (!Directory.Exists(folder)) { return; }
                foreach (string f in Directory.GetFiles(folder, FilePrefix + "*"))
                {
                    if (keep.Any(k => k != null && SamePath(k, f))) { continue; }
                    try { File.Delete(f); } catch { }
                }
            }
            catch (Exception ex) { Logger.Swallow("ShellLogo.DeleteOthers", ex); }
        }

        private const int SHCNE_UPDATEITEM = 0x00002000;
        private const uint SHCNF_PATHW = 0x0005;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(int wEventId, uint uFlags, string dwItem1, IntPtr dwItem2);
    }
}
