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

        /// <summary>The custom-logo file <see cref="_cached"/> was built from, or null.</summary>
        private static string _cachedFrom;

        /// <summary>
        /// Throws away the cached icon so the next Get() rebuilds it.
        ///
        /// Needed because a custom logo can be set, replaced or cleared while Tempo is
        /// running, and everything that shows an icon — the window, the taskbar, the
        /// tray, every dialog's title bar — pulls from this one cache.
        ///
        /// The old Icon is deliberately NOT disposed. Dialogs that are already open still
        /// hold it (their title bar, Alt-Tab entry and Form.Icon property), and one of
        /// them may be the very dialog the logo was changed from, so tearing the handle
        /// out from under it is a crash waiting to happen. Every icon this class caches
        /// owns its own handle and releases it on finalisation, so letting go of the
        /// reference is enough — and a logo change is a rare, user-driven event, not a
        /// loop that could outrun the GC.
        /// </summary>
        public static void Reset()
        {
            _cached = null;
            _cachedFrom = null;
        }

        /// <summary>The sizes baked into a custom logo's icon, smallest first.</summary>
        private static readonly int[] IconFrameSizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

        /// <summary>
        /// The part of <paramref name="src"/> that actually has ink in it, or the whole
        /// image when it has no alpha channel or is blank.
        ///
        /// Logos are usually PNGs with generous transparent padding — art on a square
        /// canvas, the mark filling perhaps half of it. Fitted whole, that padding is
        /// scaled down with everything else, and at 16px in a title bar the visible mark
        /// ends up a handful of pixels across. Trimming first means the icon is the LOGO,
        /// not the logo plus its margins.
        ///
        /// Alpha only: a mark on an opaque white card is a deliberate design, and
        /// guessing a background colour to trim would be a good way to eat a real border.
        /// </summary>
        private static Rectangle InkedBounds(Bitmap src)
        {
            var whole = new Rectangle(0, 0, src.Width, src.Height);
            if (!Image.IsAlphaPixelFormat(src.PixelFormat)) { return whole; }

            System.Drawing.Imaging.BitmapData data = null;
            try
            {
                data = src.LockBits(whole, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                int minX = src.Width, minY = src.Height, maxX = -1, maxY = -1;

                // Copied a row at a time through Marshal rather than read through a
                // pointer: this is the only place in Tempo that would want /unsafe, and
                // turning it on for the whole assembly to save a per-row memcpy on an
                // image the user picks once is a poor trade.
                var row = new byte[data.Stride];
                for (int y = 0; y < src.Height; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(
                        data.Scan0 + y * data.Stride, row, 0, data.Stride);

                    for (int x = 0; x < src.Width; x++)
                    {
                        // Alpha is the 4th byte of each BGRA pixel. 8/255 rather than 0,
                        // so a faint anti-aliased halo does not count as ink.
                        if (row[x * 4 + 3] > 8)
                        {
                            if (x < minX) { minX = x; }
                            if (x > maxX) { maxX = x; }
                            if (y < minY) { minY = y; }
                            if (y > maxY) { maxY = y; }
                        }
                    }
                }
                if (maxX < minX || maxY < minY) { return whole; }   // fully transparent
                return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
            }
            catch { return whole; }
            finally
            {
                if (data != null) { try { src.UnlockBits(data); } catch { } }
            }
        }

        /// <summary>
        /// Draws <paramref name="src"/> centred on a square transparent canvas of
        /// <paramref name="size"/> px, keeping its aspect ratio so a wide or tall picture
        /// is letterboxed rather than stretched into the box. Transparent margins are
        /// trimmed first, so the mark fills the icon.
        /// </summary>
        private static Bitmap RenderSquare(Image src, int size)
        {
            // Fit the INKED part, not the file. See InkedBounds.
            Rectangle ink = src is Bitmap bmp
                ? InkedBounds(bmp)
                : new Rectangle(0, 0, src.Width, src.Height);
            if (ink.Width <= 0 || ink.Height <= 0)
            {
                ink = new Rectangle(0, 0, src.Width, src.Height);
            }

            var square = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(square))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                double scale = Math.Min((double)size / ink.Width, (double)size / ink.Height);
                int w = Math.Max(1, (int)Math.Round(ink.Width * scale));
                int h = Math.Max(1, (int)Math.Round(ink.Height * scale));
                g.DrawImage(src,
                    new Rectangle((size - w) / 2, (size - h) / 2, w, h),
                    ink,
                    GraphicsUnit.Pixel);
            }
            return square;
        }

        /// <summary>
        /// Packs <paramref name="src"/> into a real multi-resolution .ico, one PNG frame
        /// per entry in <see cref="IconFrameSizes"/>.
        ///
        /// A single 256px frame is not good enough. Windows picks the nearest frame and
        /// scales it in ONE step, so a 256px logo asked for a 16px title bar goes through
        /// a 16:1 reduction with no filtering worth the name — visibly mushy next to the
        /// built-in mark, which ships proper small frames. Resampling each size ourselves
        /// with HighQualityBicubic is what makes a custom logo look native.
        /// </summary>
        private static byte[] BuildIcoBytes(Image src)
        {
            var frames = new byte[IconFrameSizes.Length][];
            for (int i = 0; i < IconFrameSizes.Length; i++)
            {
                using (var bmp = RenderSquare(src, IconFrameSizes[i]))
                using (var ms = new System.IO.MemoryStream())
                {
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    frames[i] = ms.ToArray();
                }
            }

            // ICONDIR: reserved(2) type(2) count(2), then count × ICONDIRENTRY(16).
            int offset = 6 + frames.Length * 16;
            int total = offset;
            foreach (byte[] f in frames) { total += f.Length; }

            var ico = new byte[total];
            ico[2] = 1;                                        // type 1 = icon
            BitConverter.GetBytes((ushort)frames.Length).CopyTo(ico, 4);

            for (int i = 0; i < frames.Length; i++)
            {
                int e = 6 + i * 16;
                int size = IconFrameSizes[i];
                ico[e] = (byte)(size >= 256 ? 0 : size);       // 0 means 256 in the ICO format
                ico[e + 1] = (byte)(size >= 256 ? 0 : size);
                ico[e + 2] = 0;                                // palette entries (0 = truecolour)
                ico[e + 3] = 0;                                // reserved
                BitConverter.GetBytes((ushort)1).CopyTo(ico, e + 4);    // colour planes
                BitConverter.GetBytes((ushort)32).CopyTo(ico, e + 6);   // bits per pixel
                BitConverter.GetBytes(frames[i].Length).CopyTo(ico, e + 8);
                BitConverter.GetBytes(offset).CopyTo(ico, e + 12);

                Buffer.BlockCopy(frames[i], 0, ico, offset, frames[i].Length);
                offset += frames[i].Length;
            }
            return ico;
        }

        /// <summary>
        /// Builds an Icon from the user's custom logo, or null if there isn't one (or it
        /// won't decode).
        /// </summary>
        private static Icon TryCustomLogo(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) { return null; }

                byte[] bytes;
                using (var src = Image.FromFile(path))
                {
                    bytes = BuildIcoBytes(src);
                }

                // new Icon(stream) copies what it needs and owns the resulting handle, so
                // unlike Icon.FromHandle there is nothing for us to destroy by hand.
                using (var ms = new System.IO.MemoryStream(bytes))
                {
                    return new Icon(ms);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("[Icon] the custom logo could not be used as a window icon: " + ex.Message);
                return null;
            }
        }

        public static Icon Get()
        {
            // A custom logo is the app's face everywhere, not just in the header. If the
            // one on disk is not the one this cache was built from — set, replaced or
            // cleared — start again.
            string custom = null;
            try { custom = CustomLogo.GetPath(); } catch { }

            if (!string.Equals(custom ?? "", _cachedFrom ?? "", StringComparison.OrdinalIgnoreCase))
            {
                Reset();
            }

            if (_cached != null)
            {
                return _cached;
            }

            if (!string.IsNullOrEmpty(custom))
            {
                Icon fromLogo = TryCustomLogo(custom);
                if (fromLogo != null)
                {
                    _cachedFrom = custom;
                    _cached = fromLogo;
                    return _cached;
                }
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

        /// <summary>
        /// Tempo's logo as a bitmap at (or above) <paramref name="size"/> pixels — the
        /// caller owns and must dispose the result.
        ///
        /// This exists because the Icon class does not reliably give you the size you ask
        /// for. <see cref="Icon.ToBitmap"/> rasterises at the Icon object's OWN size — 32px
        /// for an icon loaded from a stream — regardless of what the caller wants, and
        /// <c>new Icon(ico, 256, 256)</c> hands back tempo.ico's 128px frame even though
        /// the file genuinely contains a 256px one. Both cases end in an upscale of a
        /// smaller frame, which is exactly the soft, mushy logo we were shipping.
        /// Measured on tempo.ico at a requested 256px: the Icon route yields 128x128
        /// (mean neighbour delta 24/765), this route yields a true 256x256 (17/765 — more
        /// real detail, not interpolation).
        ///
        /// So the wanted frame is located in the .ico directory and decoded directly.
        /// </summary>
        public static Image GetBitmap(int size)
        {
            // A custom logo wins here too — this is what the tray icon, the toast
            // thumbnails and the header all draw, so honouring it in Get() alone would
            // have left half the app on the built-in mark.
            try
            {
                string custom = CustomLogo.GetPath();
                if (!string.IsNullOrEmpty(custom) && System.IO.File.Exists(custom))
                {
                    using (var src = Image.FromFile(custom))
                    {
                        return RenderSquare(src, size);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Swallow("AppIcon.GetBitmap(custom)", ex);
            }

            try
            {
                byte[] raw = RawIconBytes();
                if (raw != null)
                {
                    Image img = DecodeBestFrame(raw, size);
                    if (img != null) { return img; }
                }
            }
            catch { /* fall through to the handle-based route below */ }

            // Fallback: let Windows rasterise the icon and copy the pixels back out.
            // GDI+ handles an HICON correctly — it's only the file-frame path that breaks.
            try
            {
                Icon ico = Get();
                if (ico != null)
                {
                    using (var sized = new Icon(ico, size, size))
                    {
                        return Bitmap.FromHicon(sized.Handle);
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>The bytes of the embedded tempo.ico, or null. Cached (read once).</summary>
        private static byte[] RawIconBytes()
        {
            if (_rawTried) { return _raw; }
            _rawTried = true;
            try
            {
                Assembly asm = typeof(AppIcon).Assembly;
                foreach (string name in asm.GetManifestResourceNames())
                {
                    if (name.EndsWith("tempo.ico", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var s = asm.GetManifestResourceStream(name))
                        {
                            if (s == null) { continue; }
                            using (var ms = new System.IO.MemoryStream())
                            {
                                s.CopyTo(ms);
                                _raw = ms.ToArray();
                                return _raw;
                            }
                        }
                    }
                }
            }
            catch { }
            return _raw;
        }

        private static byte[] _raw;
        private static bool _rawTried;

        /// <summary>
        /// Picks the smallest frame at least <paramref name="want"/> px (else the largest
        /// available) out of an .ico and decodes it. Handles both PNG-compressed and
        /// classic DIB frames. Returns null if the bytes aren't a usable icon.
        /// </summary>
        private static Image DecodeBestFrame(byte[] b, int want)
        {
            // ICONDIR: reserved(2) type(2) count(2), then count × ICONDIRENTRY(16).
            if (b == null || b.Length < 6) { return null; }
            if (BitConverter.ToUInt16(b, 0) != 0 || BitConverter.ToUInt16(b, 2) != 1) { return null; }
            int count = BitConverter.ToUInt16(b, 4);
            if (count <= 0 || b.Length < 6 + count * 16) { return null; }

            int bestOff = -1, bestLen = 0, bestDim = 0;
            for (int i = 0; i < count; i++)
            {
                int e = 6 + i * 16;
                int w = b[e] == 0 ? 256 : b[e];              // 0 means 256 in the ICO format
                int len = (int)BitConverter.ToUInt32(b, e + 8);
                int off = (int)BitConverter.ToUInt32(b, e + 12);
                if (len <= 0 || off < 0 || off + len > b.Length) { continue; }

                // Prefer the smallest frame that still meets the requested size; if none
                // does, take the biggest one there is and let the caller downscale.
                bool better = bestOff < 0
                    || (bestDim < want ? w > bestDim : (w >= want && w < bestDim));
                if (better) { bestOff = off; bestLen = len; bestDim = w; }
            }
            if (bestOff < 0) { return null; }

            using (var ms = new System.IO.MemoryStream(b, bestOff, bestLen))
            {
                bool isPng = bestLen > 8 && b[bestOff] == 0x89 && b[bestOff + 1] == 0x50
                             && b[bestOff + 2] == 0x4E && b[bestOff + 3] == 0x47;
                if (isPng)
                {
                    // Copy into a standalone bitmap so nothing keeps the stream alive.
                    using (var decoded = Image.FromStream(ms))
                    {
                        return new Bitmap(decoded);
                    }
                }

                // Classic DIB frame: rebuild a one-frame .ico around it and let GDI+ read
                // it, which is the path ToBitmap() is actually correct for.
                var single = new byte[6 + 16 + bestLen];
                Buffer.BlockCopy(b, 0, single, 0, 6);
                single[4] = 1; single[5] = 0;                             // count = 1
                Buffer.BlockCopy(b, 6 + IndexOfEntry(b, count, bestOff) * 16, single, 6, 16);
                BitConverter.GetBytes(6 + 16).CopyTo(single, 6 + 12);     // new offset
                Buffer.BlockCopy(b, bestOff, single, 6 + 16, bestLen);
                using (var ims = new System.IO.MemoryStream(single))
                using (var ico = new Icon(ims))
                {
                    return Bitmap.FromHicon(ico.Handle);
                }
            }
        }

        /// <summary>Index of the directory entry whose image lives at <paramref name="off"/>.</summary>
        private static int IndexOfEntry(byte[] b, int count, int off)
        {
            for (int i = 0; i < count; i++)
            {
                if ((int)BitConverter.ToUInt32(b, 6 + i * 16 + 12) == off) { return i; }
            }
            return 0;
        }

        /// <summary>
        /// Makes Tempo's icon the DEFAULT for every WinForms window in this process, so
        /// all the dialogs (Live debug, About, Calibrate, Overlay customise, update
        /// prompts, …) show it in their title bar and in Alt-Tab — without each form
        /// having to set it, and without missing any future one.
        ///
        /// WinForms hands any form that doesn't set its own icon a private static
        /// default (Form.DefaultIcon, backed by a static field). Pre-seeding that field
        /// before the first window is created replaces the generic default everywhere.
        /// The field name is version-specific, so this is wrapped and simply no-ops if
        /// it ever changes — the app still runs, dialogs just keep the stock icon.
        ///
        /// Call once at startup, after EnableVisualStyles and before any form is shown.
        /// </summary>
        public static void SetAsWindowDefault()
        {
            try
            {
                Icon ico = Get();
                if (ico == null)
                {
                    return;
                }

                const System.Reflection.BindingFlags Flags =
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;

                // .NET Core/8 uses "s_defaultIcon"; older/full framework used "defaultIcon".
                foreach (string name in new[] { "s_defaultIcon", "defaultIcon" })
                {
                    var field = typeof(Form).GetField(name, Flags);
                    if (field != null && field.FieldType == typeof(Icon))
                    {
                        field.SetValue(null, ico);
                        Logger.Info("[Icon] Tempo icon set as the default for all windows.");
                        return;
                    }
                }
                Logger.Warn("[Icon] couldn't find Form.DefaultIcon backing field; dialogs use the stock icon.");
            }
            catch (Exception ex)
            {
                Logger.Warn("[Icon] setting the default window icon failed: " + ex.Message);
            }
        }
    }
}
