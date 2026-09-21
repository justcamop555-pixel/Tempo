using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Installs the RUNNING portable copy of Tempo for this Windows account — what install.cmd does,
    /// from inside the app: into %LOCALAPPDATA%\Programs\TempoClicker, with a Start Menu shortcut and
    /// an entry in Settings › Apps. Per-user, so no administrator rights and nothing machine-wide.
    ///
    /// Why it exists: install.cmd was the ONLY thing that ever put Tempo into Settings › Apps and
    /// Control Panel, and it only runs if someone unzips the setup bundle and double-clicks it. A
    /// release also ships Tempo.exe on its own, and people who download that — or unzip the bundle
    /// and simply open Tempo.exe — never had an entry, a shortcut, or a normal way to uninstall.
    ///
    /// Settings, profiles and macros are not moved: they live in %LOCALAPPDATA%\AutoClicker whichever
    /// copy runs (see SettingsManager), so the installed copy picks up exactly where this one was.
    /// The portable exe is left where it is — it is running, and it is the user's download to keep.
    /// </summary>
    public static class SelfInstaller
    {
        /// <summary>What an install attempt did.</summary>
        public sealed class Result
        {
            public bool Ok { get; internal set; }
            public string InstalledExe { get; internal set; }
            public string Error { get; internal set; }
            public bool ShortcutCreated { get; internal set; }
            public bool Registered { get; internal set; }
            public bool StartupMoved { get; internal set; }
            public int ShortcutsRepointed { get; internal set; }
        }

        /// <summary>Where a shortcut that launches the portable copy is likely to live.</summary>
        private static IEnumerable<string> ShortcutFolders()
        {
            string desktop = "", pins = "", startMenu = "";
            try { desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); } catch { }
            try
            {
                pins = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                    "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");
            }
            catch { }
            try
            {
                startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                         "Microsoft", "Windows", "Start Menu", "Programs");
            }
            catch { }
            return new[] { desktop, pins, startMenu };
        }

        /// <summary>
        /// Points every shortcut that launches <paramref name="fromExe"/> at <paramref name="toExe"/> instead.
        ///
        /// Installing leaves the portable copy where it was, and a Desktop shortcut or taskbar pin made for
        /// it went on starting THAT copy — which keeps working, reads and writes the same data, and falls
        /// further behind with every update the installed copy gets. Only shortcuts whose target is exactly
        /// the old exe are touched; anything else, including a shortcut already pointing at the installed
        /// copy, is left as it is.
        /// </summary>
        internal static int RepointShortcuts(string fromExe, string toExe, IEnumerable<string> folders)
        {
            if (string.IsNullOrEmpty(fromExe) || string.IsNullOrEmpty(toExe) || folders == null) { return 0; }
            string from = FullPathOrEmpty(fromExe);
            string toDir = Path.GetDirectoryName(toExe) ?? "";
            int moved = 0;

            foreach (string folder in folders)
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) { continue; }
                string[] links;
                try { links = Directory.GetFiles(folder, "*.lnk", SearchOption.TopDirectoryOnly); }
                catch { continue; }

                foreach (string lnk in links)
                {
                    object link = null;
                    try
                    {
                        link = new CShellLink();
                        ((IPersistFile)link).Load(lnk, 0);
                        var shellLink = (IShellLinkW)link;
                        var target = new StringBuilder(1024);
                        shellLink.GetPath(target, target.Capacity, IntPtr.Zero, 0);
                        if (target.Length == 0 ||
                            !string.Equals(FullPathOrEmpty(target.ToString()), from, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        shellLink.SetPath(toExe);
                        shellLink.SetWorkingDirectory(toDir);
                        shellLink.SetIconLocation(toExe, 0);
                        ((IPersistFile)link).Save(lnk, true);
                        moved++;
                        Logger.Info("[Install] the shortcut " + lnk + " now opens the installed copy.");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn("[Install] could not update the shortcut " + lnk + ": " + ex.Message);
                    }
                    finally
                    {
                        if (link != null && Marshal.IsComObject(link))
                        {
                            try { Marshal.ReleaseComObject(link); } catch { }
                        }
                    }
                }
            }
            return moved;
        }

        private static string FullPathOrEmpty(string path)
        {
            try { return Path.GetFullPath(path.Trim()); }
            catch { return path ?? ""; }
        }

        /// <summary>The Start Menu shortcut install.cmd creates, and RemoveShellIntegration deletes.</summary>
        public static string StartMenuShortcutPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "Microsoft", "Windows", "Start Menu", "Programs", "Tempo.lnk");

        /// <summary>Installs the copy of Tempo that is running now.</summary>
        public static Result InstallRunningCopy()
        {
            Result r = Install(DeploymentInfo.ExecutablePath, DeploymentInfo.InstalledDirectory,
                               StartMenuShortcutPath, integrateWithWindows: true);
            if (r.Ok)
            {
                Logger.Info("[Install] installed this copy to " + r.InstalledExe
                            + " (shortcut " + (r.ShortcutCreated ? "created" : "NOT created")
                            + ", Settings > Apps " + (r.Registered ? "registered" : "NOT registered")
                            + (r.StartupMoved ? ", sign-in entry moved" : "") + ").");
            }
            else
            {
                Logger.Warn("[Install] installing this copy failed: " + r.Error);
            }
            return r;
        }

        /// <summary>
        /// The install itself. <paramref name="integrateWithWindows"/> is false only in tests: it is
        /// what writes the Settings › Apps entry and moves the sign-in entry, and a test must never
        /// touch the real ones of the Tempo installed on the machine it runs on.
        /// </summary>
        internal static Result Install(string sourceExe, string targetDir, string shortcutPath,
                                       bool integrateWithWindows)
        {
            var r = new Result();
            string staging = null;
            bool createdDir = false;
            try
            {
                if (string.IsNullOrEmpty(sourceExe) || !File.Exists(sourceExe))
                {
                    r.Error = "The running Tempo.exe could not be found.";
                    return r;
                }
                if (string.IsNullOrEmpty(targetDir))
                {
                    r.Error = "There is no install folder to copy Tempo into.";
                    return r;
                }

                string target = Path.Combine(targetDir, "Tempo.exe");
                if (string.Equals(Path.GetFullPath(sourceExe), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                {
                    r.Error = "This copy of Tempo is already the installed one.";
                    return r;
                }

                // Never put an older download over a newer install. The offer is not shown when an
                // installed copy exists at all; this is the same rule enforced where the copy happens.
                if (File.Exists(target) && IsOlder(sourceExe, target, out string installedVersion))
                {
                    // A fixed sentence so it can be translated; the version goes to the log instead.
                    Logger.Info("[Install] refused: the installed copy (" + installedVersion + ") is newer than this one.");
                    r.Error = "A newer Tempo is already installed on this PC. Open that one instead.";
                    return r;
                }

                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                    createdDir = true;
                }

                // Copy to a staging name and verify it BEFORE it takes the real name, so a copy that
                // failed half-way — a full disk, an antivirus snatching the file — can never leave a
                // truncated Tempo.exe where the Start Menu and Settings › Apps will point.
                staging = target + ".installing";
                File.Copy(sourceExe, staging, overwrite: true);
                if (!SameBytes(sourceExe, staging))
                {
                    r.Error = "The copied file did not match the original, so nothing was installed.";
                    return r;
                }
                File.Move(staging, target, overwrite: true);
                staging = null;

                string sha = sourceExe + ".sha256";
                if (File.Exists(sha))
                {
                    try { File.Copy(sha, Path.Combine(targetDir, "Tempo.exe.sha256"), overwrite: true); }
                    catch { /* the checksum is a convenience, not part of the install */ }
                }

                r.InstalledExe = target;

                // Everything past the copy is best-effort: a missing shortcut is not a failed install,
                // and an Apps entry that could not be written now is written by the installed copy
                // itself the first time it runs (Uninstaller.RefreshRegisteredVersion).
                if (!string.IsNullOrEmpty(shortcutPath))
                {
                    r.ShortcutCreated = CreateShortcut(shortcutPath, target, targetDir, "Tempo");
                }
                if (integrateWithWindows)
                {
                    string version = null;
                    try { version = FileVersionInfo.GetVersionInfo(target).FileVersion; } catch { }
                    r.Registered = Uninstaller.RegisterInstalledCopy(target, version);
                    r.StartupMoved = StartupManager.RepointTo(target);
                    r.ShortcutsRepointed = RepointShortcuts(sourceExe, target, ShortcutFolders());
                }

                r.Ok = true;
                return r;
            }
            catch (Exception ex)
            {
                r.Error = ex.Message;
                return r;
            }
            finally
            {
                if (staging != null)
                {
                    try { File.Delete(staging); } catch { }
                }
                // Undo only what this attempt created, and only if it is empty: a folder that already
                // held an install is never removed because a later copy failed.
                if (!r.Ok && createdDir)
                {
                    try
                    {
                        if (Directory.Exists(targetDir) && Directory.GetFileSystemEntries(targetDir).Length == 0)
                        {
                            Directory.Delete(targetDir);
                        }
                    }
                    catch { }
                }
            }
        }

        /// <summary>True when <paramref name="candidate"/> has a lower file version than <paramref name="installed"/>.</summary>
        private static bool IsOlder(string candidate, string installed, out string installedVersion)
        {
            installedVersion = "";
            try
            {
                string c = FileVersionInfo.GetVersionInfo(candidate).FileVersion;
                string i = FileVersionInfo.GetVersionInfo(installed).FileVersion;
                installedVersion = i ?? "";
                if (Version.TryParse(c, out Version cv) && Version.TryParse(i, out Version iv))
                {
                    return cv < iv;
                }
            }
            catch { }
            return false;   // unreadable versions: let the copy decide nothing on a guess
        }

        private static bool SameBytes(string a, string b)
        {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            if (!fa.Exists || !fb.Exists || fa.Length != fb.Length) { return false; }
            using (FileStream sa = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (FileStream sb = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                byte[] ha = SHA256.HashData(sa);
                byte[] hb = SHA256.HashData(sb);
                return CryptographicOperations.FixedTimeEquals(ha, hb);
            }
        }

        // ── Start Menu shortcut ───────────────────────────────────────────────────
        //
        // IShellLink straight from shell32 rather than WScript.Shell or PowerShell. install.cmd has
        // to fall back from PowerShell to VBScript because locked-down PCs block the first, and some
        // block the second; the shell's own link object is what both of them call in the end.

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class CShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010b-0000-0000-C000-000000000046")]
        private interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            [PreserveSig] int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }

        /// <summary>The path a shortcut opens, or null when it cannot be read.</summary>
        internal static string ReadShortcutTarget(string lnkPath)
        {
            object link = null;
            try
            {
                link = new CShellLink();
                ((IPersistFile)link).Load(lnkPath, 0);
                var target = new StringBuilder(1024);
                ((IShellLinkW)link).GetPath(target, target.Capacity, IntPtr.Zero, 0);
                return target.ToString();
            }
            catch
            {
                return null;
            }
            finally
            {
                if (link != null && Marshal.IsComObject(link))
                {
                    try { Marshal.ReleaseComObject(link); } catch { }
                }
            }
        }

        /// <summary>Creates (or replaces) a shortcut. Returns true only if the file really exists afterwards.</summary>
        internal static bool CreateShortcut(string lnkPath, string target, string workingDir, string description)
        {
            object link = null;
            try
            {
                string folder = Path.GetDirectoryName(lnkPath);
                if (!string.IsNullOrEmpty(folder)) { Directory.CreateDirectory(folder); }

                link = new CShellLink();
                var shellLink = (IShellLinkW)link;
                shellLink.SetPath(target);
                if (!string.IsNullOrEmpty(workingDir)) { shellLink.SetWorkingDirectory(workingDir); }
                shellLink.SetIconLocation(target, 0);
                shellLink.SetDescription(description ?? "Tempo");
                ((IPersistFile)link).Save(lnkPath, true);
                return File.Exists(lnkPath);
            }
            catch (Exception ex)
            {
                Logger.Warn("[Install] could not create the shortcut " + lnkPath + ": " + ex.Message);
                return false;
            }
            finally
            {
                if (link != null && Marshal.IsComObject(link))
                {
                    try { Marshal.ReleaseComObject(link); } catch { }
                }
            }
        }
    }
}
