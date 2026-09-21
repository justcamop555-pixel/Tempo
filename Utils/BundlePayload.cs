using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Checks that the code Tempo actually RUNS is the code inside Tempo.exe.
    ///
    /// Tempo is a single-file bundle built with IncludeAllContentForSelfExtract, so on start the .NET
    /// host unpacks every file it carries — Tempo.dll itself, the framework, the speech natives —
    /// into %TEMP%\.net\Tempo\&lt;bundle id&gt;\ and loads them from THERE. When that folder already
    /// exists the host reuses it and only restores files that are missing; it never looks at what
    /// is inside them. So a file swapped in that folder is loaded on the next start while Tempo.exe
    /// stays byte-identical — and the integrity check, which hashed only Tempo.exe (and had GitHub
    /// confirm that hash), went on reporting a genuine copy while patched code ran. The same folder
    /// is on the native DLL search path, so a planted DLL named like a system library is loaded too.
    ///
    /// This reads the bundle's own manifest out of the exe and compares every unpacked file with
    /// the bytes the exe carries for it, and lists executable files the bundle never contained.
    /// Like the rest of the check it is tamper-EVIDENT: something able to rewrite Tempo.exe can
    /// rewrite this too. What it removes is the easy route — changing Tempo without touching the
    /// one file that is verified.
    ///
    /// No dependencies (no Logger, no settings), so it can be exercised on its own against a real
    /// install and a copied, deliberately damaged unpack folder.
    /// </summary>
    public static class BundlePayload
    {
        /// <summary>
        /// The marker the bundler finds in the app host — SHA-256 of ".net core bundle". The 8 bytes
        /// in front of it hold the offset of the bundle header.
        /// </summary>
        internal static readonly byte[] Signature =
        {
            0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38, 0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32,
            0x13, 0xf5, 0xb9, 0xe6, 0xef, 0xae, 0x33, 0x18, 0xee, 0x3b, 0x2d, 0xce, 0x24, 0xb3, 0x6a, 0xae
        };

        public enum EntryType : byte
        {
            Unknown = 0,
            Assembly = 1,
            NativeBinary = 2,
            DepsJson = 3,
            RuntimeConfigJson = 4,
            Symbols = 5
        }

        public sealed class Entry
        {
            public long Offset;
            public long Size;
            public long CompressedSize;      // 0 = stored uncompressed
            public EntryType Type;
            public string RelativePath;
        }

        public sealed class Manifest
        {
            public uint MajorVersion;
            public uint MinorVersion;
            public string BundleId;
            public ulong Flags;
            public long HeaderOffset;
            public readonly List<Entry> Entries = new List<Entry>();

            /// <summary>
            /// Bundle flag 1 (NetcoreApp3CompatMode, set by IncludeAllContentForSelfExtract): assemblies
            /// are unpacked to disk as well, instead of being loaded from inside the exe.
            /// </summary>
            public bool UnpacksAssemblies => (Flags & 1) != 0;

            /// <summary>Whether the host writes this entry to the unpack folder (mirrors the host's rule).</summary>
            public bool IsUnpacked(Entry e)
            {
                switch (e.Type)
                {
                    case EntryType.DepsJson:
                    case EntryType.RuntimeConfigJson:
                        return false;
                    case EntryType.Assembly:
                        return UnpacksAssemblies;
                    default:
                        return true;
                }
            }
        }

        public sealed class Report
        {
            /// <summary>Unpacked files compared byte-for-byte (by SHA-256) with the exe's copy.</summary>
            public int Checked;
            public long Bytes;
            public long Milliseconds;
            /// <summary>Present, but not what the exe carries.</summary>
            public readonly List<string> Changed = new List<string>();
            /// <summary>Not on disk. Not tampering: the host restores missing files on the next start.</summary>
            public readonly List<string> Missing = new List<string>();
            /// <summary>Executable files in the folder that the bundle never contained.</summary>
            public readonly List<string> Unexpected = new List<string>();
            /// <summary>Why the comparison could not be made, or null.</summary>
            public string Error;

            public bool Tampered => Changed.Count > 0 || Unexpected.Count > 0;
        }

        // Extensions Windows will load or run as code. A stray .txt or .log in the folder is not a threat.
        private static readonly HashSet<string> CodeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".dll", ".exe", ".ocx", ".cpl", ".drv", ".sys", ".com", ".scr", ".winmd"
        };

        /// <summary>
        /// Finds and parses the bundle manifest, or returns null when the file is not a bundle this
        /// understands. <paramref name="signatureOffset"/> saves a second search when the caller has
        /// already found the marker while reading the file.
        /// </summary>
        public static Manifest Read(string exePath, long signatureOffset = -1)
        {
            using (var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                long sig = signatureOffset >= 0 ? signatureOffset : FindSignature(fs);
                if (sig < 8) { return null; }

                var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);
                fs.Position = sig - 8;
                long headerOffset = br.ReadInt64();
                if (headerOffset <= 0 || headerOffset >= fs.Length) { return null; }   // a plain, un-bundled host

                fs.Position = headerOffset;
                var m = new Manifest { HeaderOffset = headerOffset };
                m.MajorVersion = br.ReadUInt32();
                m.MinorVersion = br.ReadUInt32();
                int count = br.ReadInt32();
                if (m.MajorVersion < 1 || m.MajorVersion > 64 || count < 0 || count > 100000) { return null; }
                m.BundleId = br.ReadString();
                if (m.MajorVersion >= 2)
                {
                    br.ReadInt64(); br.ReadInt64();      // deps.json offset, size
                    br.ReadInt64(); br.ReadInt64();      // runtimeconfig.json offset, size
                    m.Flags = br.ReadUInt64();
                }
                for (int i = 0; i < count; i++)
                {
                    var e = new Entry { Offset = br.ReadInt64(), Size = br.ReadInt64() };
                    if (m.MajorVersion >= 6) { e.CompressedSize = br.ReadInt64(); }   // compression arrived in 6.0
                    e.Type = (EntryType)br.ReadByte();
                    e.RelativePath = br.ReadString();
                    long stored = e.CompressedSize > 0 ? e.CompressedSize : e.Size;
                    if (e.Offset < 0 || e.Size < 0 || stored < 0 || e.Offset + stored > fs.Length ||
                        string.IsNullOrEmpty(e.RelativePath))
                    {
                        return null;                         // not a manifest we understand; claim nothing
                    }
                    m.Entries.Add(e);
                }
                return m;
            }
        }

        /// <summary>
        /// Compares every file the host unpacked with the copy inside the exe, and lists executable
        /// files in the folder the bundle never contained. Never throws.
        /// </summary>
        public static Report Verify(string exePath, Manifest manifest, string unpackDir)
        {
            var report = new Report();
            var clock = Stopwatch.StartNew();
            try
            {
                if (manifest == null) { report.Error = "no bundle manifest"; return report; }
                if (string.IsNullOrEmpty(unpackDir) || !Directory.Exists(unpackDir))
                {
                    report.Error = "the unpack folder does not exist";
                    return report;
                }

                string root = Path.GetFullPath(unpackDir);
                var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var exe = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    foreach (Entry e in manifest.Entries)
                    {
                        if (!manifest.IsUnpacked(e)) { continue; }
                        string path = Path.GetFullPath(Path.Combine(root, e.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
                        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) { continue; }   // never outside the folder
                        known.Add(path);

                        var file = new FileInfo(path);
                        if (!file.Exists)
                        {
                            report.Missing.Add(e.RelativePath);
                            continue;
                        }
                        report.Checked++;
                        report.Bytes += e.Size;
                        if (file.Length != e.Size)
                        {
                            report.Changed.Add(e.RelativePath);
                            continue;
                        }
                        if (!CryptographicOperations.FixedTimeEquals(HashEmbedded(exe, e), HashFile(path)))
                        {
                            report.Changed.Add(e.RelativePath);
                        }
                    }
                }

                foreach (string f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    if (!CodeExtensions.Contains(Path.GetExtension(f))) { continue; }
                    if (!known.Contains(Path.GetFullPath(f)))
                    {
                        report.Unexpected.Add(Path.GetRelativePath(root, f));
                    }
                }
            }
            catch (Exception ex)
            {
                report.Error = ex.GetType().Name + ": " + ex.Message;
            }
            finally
            {
                report.Milliseconds = clock.ElapsedMilliseconds;
            }
            return report;
        }

        /// <summary>Position of the bundle marker in the file, or -1.</summary>
        public static long FindSignature(Stream stream)
        {
            stream.Position = 0;
            byte[] buffer = new byte[1 << 20];
            int keep = Signature.Length - 1;
            long bufferStart = 0;
            int filled = 0;
            while (true)
            {
                int read = stream.Read(buffer, filled, buffer.Length - filled);
                if (read <= 0) { return -1; }
                filled += read;
                int at = IndexOf(buffer, filled, Signature);
                if (at >= 0) { return bufferStart + at; }
                // Keep the tail, so a marker split across two reads is still found.
                int shift = filled - keep;
                if (shift <= 0) { continue; }
                Buffer.BlockCopy(buffer, shift, buffer, 0, keep);
                bufferStart += shift;
                filled = keep;
            }
        }

        internal static int IndexOf(byte[] haystack, int length, byte[] needle)
        {
            int last = length - needle.Length;
            for (int i = 0; i <= last; i++)
            {
                if (haystack[i] != needle[0]) { continue; }
                int j = 1;
                while (j < needle.Length && haystack[i + j] == needle[j]) { j++; }
                if (j == needle.Length) { return i; }
            }
            return -1;
        }

        private static byte[] HashEmbedded(FileStream exe, Entry e)
        {
            long stored = e.CompressedSize > 0 ? e.CompressedSize : e.Size;
            using (var slice = new Slice(exe, e.Offset, stored))
            {
                if (e.CompressedSize > 0)
                {
                    using (var inflate = new DeflateStream(slice, CompressionMode.Decompress, leaveOpen: true))
                    {
                        return SHA256.HashData(inflate);
                    }
                }
                return SHA256.HashData(slice);
            }
        }

        private static byte[] HashFile(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                                           1 << 16, FileOptions.SequentialScan))
            {
                return SHA256.HashData(fs);
            }
        }

        /// <summary>A read-only window onto part of the exe, so the inflater can't read past its entry.</summary>
        private sealed class Slice : Stream
        {
            private readonly Stream _inner;
            private readonly long _start;
            private readonly long _length;
            private long _position;

            public Slice(Stream inner, long start, long length)
            {
                _inner = inner;
                _start = start;
                _length = length;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _length;
            public override long Position { get => _position; set => throw new NotSupportedException(); }

            public override int Read(byte[] buffer, int offset, int count)
            {
                long left = _length - _position;
                if (left <= 0) { return 0; }
                if (count > left) { count = (int)left; }
                _inner.Position = _start + _position;
                int read = _inner.Read(buffer, offset, count);
                _position += read;
                return read;
            }

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
