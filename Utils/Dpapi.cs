using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Windows DPAPI (Data Protection API), CurrentUser scope, via a direct P/Invoke of
    /// <c>crypt32.dll</c> rather than <c>System.Security.Cryptography.ProtectedData</c>.
    ///
    /// Why P/Invoke and not ProtectedData: Tempo publishes as a single-file, self-contained
    /// executable, and every avoidable assembly is weight in that bundle. crypt32 is part of
    /// Windows and always present, so calling it directly keeps the publish lean and the
    /// dependency surface flat — the same reason the rest of Tempo P/Invokes user32 rather
    /// than dragging in wrappers.
    ///
    /// CurrentUser scope binds the ciphertext to the signed-in Windows account: only that
    /// user, on a machine that still holds their DPAPI master key, can unprotect it. Copy
    /// the file to another PC, or sign in as someone else, and it will not decrypt — which
    /// for a vault of logins is exactly the behaviour we want. It is one of the two locks;
    /// the master password is the other (see <see cref="AutoClicker.Persistence.VaultCrypto"/>).
    /// </summary>
    internal static class Dpapi
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct DATA_BLOB
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(
            ref DATA_BLOB pDataIn, string szDataDescr,
            ref DATA_BLOB pOptionalEntropy, IntPtr pvReserved,
            IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(
            ref DATA_BLOB pDataIn, IntPtr ppszDataDescr,
            ref DATA_BLOB pOptionalEntropy, IntPtr pvReserved,
            IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        // Never let DPAPI pop UI (a credential prompt) on a background/tray thread.
        private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

        /// <summary>Protect <paramref name="data"/> for the current Windows user.</summary>
        public static byte[] Protect(byte[] data, byte[] entropy)
        {
            if (data == null) { throw new ArgumentNullException(nameof(data)); }
            return Run(CryptProtect, data, entropy);
        }

        /// <summary>
        /// Unprotect data produced by <see cref="Protect"/>. Throws
        /// <see cref="Win32Exception"/> when the caller is not the user (or machine) that
        /// protected it, which the vault turns into a clear "belongs to another account".
        /// </summary>
        public static byte[] Unprotect(byte[] data, byte[] entropy)
        {
            if (data == null) { throw new ArgumentNullException(nameof(data)); }
            return Run(CryptUnprotect, data, entropy);
        }

        private static bool CryptProtect(ref DATA_BLOB inBlob, ref DATA_BLOB entBlob, ref DATA_BLOB outBlob)
        {
            return CryptProtectData(ref inBlob, null, ref entBlob, IntPtr.Zero, IntPtr.Zero,
                CRYPTPROTECT_UI_FORBIDDEN, ref outBlob);
        }

        private static bool CryptUnprotect(ref DATA_BLOB inBlob, ref DATA_BLOB entBlob, ref DATA_BLOB outBlob)
        {
            return CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entBlob, IntPtr.Zero, IntPtr.Zero,
                CRYPTPROTECT_UI_FORBIDDEN, ref outBlob);
        }

        private delegate bool BlobOp(ref DATA_BLOB inBlob, ref DATA_BLOB entBlob, ref DATA_BLOB outBlob);

        private static byte[] Run(BlobOp op, byte[] data, byte[] entropy)
        {
            var inBlob = new DATA_BLOB();
            var entBlob = new DATA_BLOB();
            var outBlob = new DATA_BLOB();
            try
            {
                Init(ref inBlob, data);
                Init(ref entBlob, entropy ?? Array.Empty<byte>());
                if (!op(ref inBlob, ref entBlob, ref outBlob))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                return ReadBlob(outBlob);
            }
            finally
            {
                Free(ref inBlob);
                Free(ref entBlob);
                if (outBlob.pbData != IntPtr.Zero) { LocalFree(outBlob.pbData); }
            }
        }

        private static void Init(ref DATA_BLOB blob, byte[] data)
        {
            blob.cbData = data.Length;
            // AllocHGlobal(0) is legal but returns a pointer we would still have to free;
            // ask for at least one byte so pbData is always a real, freeable block.
            blob.pbData = Marshal.AllocHGlobal(Math.Max(1, data.Length));
            if (data.Length > 0) { Marshal.Copy(data, 0, blob.pbData, data.Length); }
        }

        private static byte[] ReadBlob(DATA_BLOB blob)
        {
            var outBytes = new byte[blob.cbData];
            if (blob.cbData > 0) { Marshal.Copy(blob.pbData, outBytes, 0, blob.cbData); }
            return outBytes;
        }

        private static void Free(ref DATA_BLOB blob)
        {
            if (blob.pbData != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(blob.pbData);
                blob.pbData = IntPtr.Zero;
            }
        }
    }
}
