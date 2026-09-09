using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using AutoClicker.Utils;

namespace AutoClicker.Persistence
{
    /// <summary>Thrown when the master password is wrong (the AES-GCM tag did not verify).</summary>
    public sealed class VaultWrongPasswordException : Exception
    {
        public VaultWrongPasswordException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Thrown when DPAPI refused to unprotect the blob — the vault was made by a different
    /// Windows user, or moved from another machine, or the file is damaged at the outer layer.
    /// </summary>
    public sealed class VaultForeignException : Exception
    {
        public VaultForeignException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>Thrown when the decrypted header is not a Tempo vault (wrong magic/version/shape).</summary>
    public sealed class VaultCorruptException : Exception
    {
        public VaultCorruptException(string message) : base(message) { }
    }

    /// <summary>
    /// The cryptography behind the account vault, kept as pure, side-effect-free functions
    /// so it can be exercised on its own (and it is — a standalone round-trip harness drives
    /// exactly these calls before any of it is wired to the UI).
    ///
    /// Two independent locks, both required to read a vault:
    ///
    ///   1. <b>Master password</b> → PBKDF2-HMAC-SHA256 (per-vault random salt, iteration
    ///      count stored in the file) → a 32-byte key → AES-256-GCM over the JSON. A wrong
    ///      password derives a wrong key, the GCM tag fails to verify, and decryption throws
    ///      — so the password is checked by the cipher itself, with no separate verifier blob
    ///      that could leak whether a guess was close.
    ///   2. <b>Windows account</b> → the whole thing above is then wrapped in DPAPI
    ///      (CurrentUser). The salt, nonce and ciphertext never touch the disk in the clear,
    ///      so an attacker who steals the file cannot even begin an offline password guess
    ///      without first being the signed-in Windows user on that machine.
    ///
    /// GCM's one hard rule — never reuse a (key, nonce) pair — is honoured by generating a
    /// fresh random 12-byte nonce on every single <see cref="Seal"/>, including re-saves that
    /// keep the same key. The layout is versioned so the format can change without stranding
    /// vaults already on disk.
    /// </summary>
    internal static class VaultCrypto
    {
        private static readonly byte[] Magic = { 0x54, 0x4D, 0x50, 0x56 }; // "TMPV"
        private const byte Version = 1;
        private const int SaltLen = 16;
        private const int NonceLen = 12;
        private const int TagLen = 16;
        private const int KeyLen = 32;   // AES-256
        private const int HeaderLen = 4 + 1 + 4 + SaltLen + NonceLen + TagLen;

        /// <summary>
        /// PBKDF2 work factor for new vaults. Tuned by the crypto harness (≈130 ms on a
        /// desktop with SHA hardware acceleration, where a lazier count came out at 34 ms and
        /// far too cheap to guess against) — well above OWASP's 600k baseline for
        /// PBKDF2-HMAC-SHA256, yet still a one-time cost paid only on unlock, not on every
        /// save (a re-save reuses the key already in memory). Old vaults are opened with
        /// whatever count is stamped in their own header, so this can rise over time without
        /// stranding anyone.
        /// </summary>
        public const int DefaultIterations = 1200000;

        // App-specific DPAPI entropy. Not a secret — it is compiled into the binary — but it
        // pins the outer layer to THIS feature, so an unrelated CryptUnprotectData running as
        // the same user cannot read the vault by chance.
        private static readonly byte[] DpapiEntropy = Encoding.UTF8.GetBytes("Tempo.AccountVault.v1");

        public static byte[] NewSalt()
        {
            var salt = new byte[SaltLen];
            RandomNumberGenerator.Fill(salt);
            return salt;
        }

        public static byte[] DeriveKey(string password, byte[] salt, int iterations)
        {
            return Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password ?? ""), salt, iterations,
                HashAlgorithmName.SHA256, KeyLen);
        }

        /// <summary>
        /// Encrypt <paramref name="plaintext"/> under <paramref name="key"/>, wrap the header,
        /// and DPAPI-protect the result. A fresh random nonce is generated here every call.
        /// </summary>
        public static byte[] Seal(byte[] plaintext, byte[] key, byte[] salt, int iterations)
        {
            if (plaintext == null) { throw new ArgumentNullException(nameof(plaintext)); }
            if (key == null || key.Length != KeyLen) { throw new ArgumentException("key length", nameof(key)); }
            if (salt == null || salt.Length != SaltLen) { throw new ArgumentException("salt length", nameof(salt)); }

            var nonce = new byte[NonceLen];
            RandomNumberGenerator.Fill(nonce);
            var cipher = new byte[plaintext.Length];
            var tag = new byte[TagLen];
            using (var aes = new AesGcm(key, TagLen))
            {
                aes.Encrypt(nonce, plaintext, cipher, tag);
            }

            var inner = new byte[HeaderLen + cipher.Length];
            int o = 0;
            Buffer.BlockCopy(Magic, 0, inner, o, 4); o += 4;
            inner[o++] = Version;
            BinaryPrimitives.WriteInt32BigEndian(inner.AsSpan(o), iterations); o += 4;
            Buffer.BlockCopy(salt, 0, inner, o, SaltLen); o += SaltLen;
            Buffer.BlockCopy(nonce, 0, inner, o, NonceLen); o += NonceLen;
            Buffer.BlockCopy(tag, 0, inner, o, TagLen); o += TagLen;
            Buffer.BlockCopy(cipher, 0, inner, o, cipher.Length);

            byte[] disk = Dpapi.Protect(inner, DpapiEntropy);
            CryptographicOperations.ZeroMemory(inner); // held the plaintext ciphertext + header; scrub it
            return disk;
        }

        /// <summary>What a successful <see cref="Open"/> hands back.</summary>
        public sealed class Opened
        {
            public byte[] Plaintext;
            public byte[] Key;         // kept by the vault so re-saves need no re-prompt; zeroed on lock
            public byte[] Salt;
            public int Iterations;
        }

        /// <summary>
        /// Reverse of <see cref="Seal"/>: DPAPI-unprotect, parse, derive the key from the
        /// password, AES-GCM-decrypt. Throws <see cref="VaultForeignException"/> if the outer
        /// DPAPI layer refuses, <see cref="VaultCorruptException"/> if the header is not ours,
        /// and <see cref="VaultWrongPasswordException"/> if the password is wrong.
        /// </summary>
        public static Opened Open(byte[] disk, string password)
        {
            if (disk == null) { throw new ArgumentNullException(nameof(disk)); }

            byte[] inner;
            try
            {
                inner = Dpapi.Unprotect(disk, DpapiEntropy);
            }
            catch (Exception ex)
            {
                throw new VaultForeignException("The vault could not be unprotected for this Windows account.", ex);
            }

            try
            {
                if (inner.Length < HeaderLen) { throw new VaultCorruptException("vault is too short"); }
                int o = 0;
                for (int i = 0; i < 4; i++)
                {
                    if (inner[o++] != Magic[i]) { throw new VaultCorruptException("not a Tempo vault"); }
                }
                byte ver = inner[o++];
                if (ver != Version) { throw new VaultCorruptException("unsupported vault version " + ver); }
                int iterations = BinaryPrimitives.ReadInt32BigEndian(inner.AsSpan(o)); o += 4;
                if (iterations < 10000 || iterations > 20000000) { throw new VaultCorruptException("iteration count out of range"); }

                var salt = new byte[SaltLen]; Buffer.BlockCopy(inner, o, salt, 0, SaltLen); o += SaltLen;
                var nonce = new byte[NonceLen]; Buffer.BlockCopy(inner, o, nonce, 0, NonceLen); o += NonceLen;
                var tag = new byte[TagLen]; Buffer.BlockCopy(inner, o, tag, 0, TagLen); o += TagLen;
                var cipher = new byte[inner.Length - o]; Buffer.BlockCopy(inner, o, cipher, 0, cipher.Length);

                byte[] key = DeriveKey(password, salt, iterations);
                var plain = new byte[cipher.Length];
                try
                {
                    using (var aes = new AesGcm(key, TagLen))
                    {
                        aes.Decrypt(nonce, cipher, tag, plain);
                    }
                }
                catch (CryptographicException ex)
                {
                    CryptographicOperations.ZeroMemory(key);
                    throw new VaultWrongPasswordException("The master password is incorrect.", ex);
                }

                return new Opened { Plaintext = plain, Key = key, Salt = salt, Iterations = iterations };
            }
            finally
            {
                CryptographicOperations.ZeroMemory(inner);
            }
        }
    }
}
