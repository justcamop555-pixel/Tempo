using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace AutoClicker.Utils
{
    /// <summary>How trustworthy the installed Roblox client looks.</summary>
    public enum RobloxAuthenticity
    {
        NotInstalled,      // no client found
        Unsigned,          // the exe carries no Authenticode signature
        Modified,          // it is signed but the signature does not verify (tampered / untrusted)
        WrongPublisher,    // it verifies, but the signer is not Roblox Corporation
        Official           // verifies AND is signed by Roblox Corporation
    }

    /// <summary>
    /// Finds the locally installed Roblox client and judges whether it is the genuine, unmodified
    /// article. This is a safety check for the account manager: a launch hands a live session to
    /// whatever RobloxPlayerBeta.exe is on disk, so if that file has been tampered with (a patched
    /// or exploit-injected client), the user should be told before their account touches it.
    ///
    /// "Official" means two things together: the Authenticode signature verifies via WinVerifyTrust
    /// (so the bytes are unmodified since signing), AND the signer is Roblox Corporation. A
    /// third-party BOOTSTRAPPER (Bloxstrap, Fishstrap, …) still launches this same signed exe, so it
    /// reads as Official — only the client binary itself being altered flips it to Modified.
    ///
    /// Pure and dependency-free (no Logger) so it can be exercised on its own against a real install.
    /// </summary>
    public static class RobloxInstall
    {
        /// <summary>Newest <c>RobloxPlayerBeta.exe</c> on disk, or null if none is found.</summary>
        public static string LocatePlayer()
        {
            foreach (string root in PlayerRoots())
            {
                string found = NewestPlayerUnder(root);
                if (found != null) { return found; }
            }
            // Last resort: the roblox-player protocol handler's command.
            string fromProto = LocateFromProtocol();
            return fromProto;
        }

        public static bool IsInstalled()
        {
            return LocatePlayer() != null;
        }

        /// <summary>The version folder name (e.g. "version-abc123") for a player path, or "".</summary>
        public static string VersionOf(string playerPath)
        {
            try
            {
                if (string.IsNullOrEmpty(playerPath)) { return ""; }
                string dir = Path.GetFileName(Path.GetDirectoryName(playerPath) ?? "");
                return dir ?? "";
            }
            catch { return ""; }
        }

        /// <summary>
        /// Verify the client's signature. <paramref name="signer"/> receives the signing
        /// certificate's subject (or an empty string) for display.
        /// </summary>
        public static RobloxAuthenticity CheckAuthenticity(string playerPath, out string signer)
        {
            signer = "";
            if (string.IsNullOrEmpty(playerPath) || !File.Exists(playerPath))
            {
                return RobloxAuthenticity.NotInstalled;
            }

            // Read the signer subject first (also tells us "unsigned" cheaply).
            bool hasCert = false;
            try
            {
                using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(playerPath));
                signer = cert.Subject ?? "";
                hasCert = true;
            }
            catch
            {
                // No embedded signature at all.
                return RobloxAuthenticity.Unsigned;
            }

            uint trust = WinVerifyTrust(playerPath);
            if (trust == TRUST_E_NOSIGNATURE || !hasCert)
            {
                return RobloxAuthenticity.Unsigned;
            }
            if (trust != 0)
            {
                // Signature present but does not verify: tampered, revoked, or untrusted root.
                return RobloxAuthenticity.Modified;
            }

            bool isRoblox = signer.IndexOf("Roblox Corporation", StringComparison.OrdinalIgnoreCase) >= 0;
            return isRoblox ? RobloxAuthenticity.Official : RobloxAuthenticity.WrongPublisher;
        }

        /// <summary>The official download page — opened in the browser so the user installs it themselves.</summary>
        public static void OpenDownloadPage()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "https://www.roblox.com/download") { UseShellExecute = true });
            }
            catch { }
        }

        // ── locating ─────────────────────────────────────────────────────────────

        private static System.Collections.Generic.IEnumerable<string> PlayerRoots()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            // The usual per-user install, and the bootstrappers that mirror its layout.
            yield return Path.Combine(local, "Roblox", "Versions");
            yield return Path.Combine(local, "Bloxstrap", "Versions");
            yield return Path.Combine(local, "Fishstrap", "Versions");
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(pf86)) { yield return Path.Combine(pf86, "Roblox", "Versions"); }
        }

        private static string NewestPlayerUnder(string versionsRoot)
        {
            try
            {
                if (!Directory.Exists(versionsRoot)) { return null; }
                return Directory.GetDirectories(versionsRoot)
                    .Select(d => Path.Combine(d, "RobloxPlayerBeta.exe"))
                    .Where(File.Exists)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
            }
            catch { return null; }
        }

        private static string LocateFromProtocol()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.ClassesRoot
                    .OpenSubKey(@"roblox-player\shell\open\command");
                string cmd = key?.GetValue(null) as string;
                if (string.IsNullOrEmpty(cmd)) { return null; }
                // The command is like: "C:\...\RobloxPlayerBeta.exe" "%1"
                int q1 = cmd.IndexOf('"');
                int q2 = q1 >= 0 ? cmd.IndexOf('"', q1 + 1) : -1;
                string path = q2 > q1 ? cmd.Substring(q1 + 1, q2 - q1 - 1) : cmd.Split(' ')[0];
                return File.Exists(path) ? path : null;
            }
            catch { return null; }
        }

        // ── WinVerifyTrust ─────────────────────────────────────────────────────────

        private const uint TRUST_E_NOSIGNATURE = 0x800B0100;

        private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 =
            new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            [MarshalAs(UnmanagedType.LPWStr)] public string pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
        private static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, IntPtr pWVTData);

        private const uint WTD_UI_NONE = 2;
        private const uint WTD_REVOKE_NONE = 0;
        private const uint WTD_CHOICE_FILE = 1;
        private const uint WTD_STATEACTION_VERIFY = 1;
        private const uint WTD_STATEACTION_CLOSE = 2;
        private const uint WTD_SAFER_FLAG = 0x100;

        /// <summary>0 when the file's signature is present and trusted; a nonzero status otherwise.</summary>
        private static uint WinVerifyTrust(string path)
        {
            var fileInfo = new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
                pcwszFilePath = path,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero
            };
            IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
            IntPtr pData = IntPtr.Zero;
            try
            {
                Marshal.StructureToPtr(fileInfo, pFile, false);
                var data = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                    dwUIChoice = WTD_UI_NONE,
                    fdwRevocationChecks = WTD_REVOKE_NONE,
                    dwUnionChoice = WTD_CHOICE_FILE,
                    pFile = pFile,
                    dwStateAction = WTD_STATEACTION_VERIFY,
                    dwProvFlags = WTD_SAFER_FLAG
                };
                pData = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_DATA>());
                Marshal.StructureToPtr(data, pData, false);

                uint result = WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, pData);

                // Always close the state to free provider resources.
                data.dwStateAction = WTD_STATEACTION_CLOSE;
                Marshal.StructureToPtr(data, pData, true);
                WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, pData);
                return result;
            }
            catch
            {
                return 0xFFFFFFFF;
            }
            finally
            {
                if (pFile != IntPtr.Zero) { Marshal.FreeHGlobal(pFile); }
                if (pData != IntPtr.Zero) { Marshal.FreeHGlobal(pData); }
            }
        }
    }
}
