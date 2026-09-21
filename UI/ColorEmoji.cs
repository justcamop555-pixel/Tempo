using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// Draws emoji in their real colours.
    ///
    /// Tempo draws its text with GDI (TextRenderer), and GDI cannot paint a colour font:
    /// Segoe UI Emoji's colour layers are ignored and every emoji comes out as a flat
    /// silhouette in the text colour — black on a light theme, which is how the profile
    /// icons, the account icons and the 🔒 marks all looked. Only DirectWrite draws the
    /// colour layers, so each emoji is rendered once through Direct2D into a small
    /// transparent bitmap (cached) and composited where the glyph belongs. The text around
    /// it is still drawn by TextRenderer, at the positions TextRenderer itself gives it, so
    /// nothing else about a label changes.
    ///
    /// Plain COM through vtable slots — no SharpDX/Vortice package, the single-file exe
    /// stays lean. If Direct2D can't start (a stripped-down Windows, a broken graphics
    /// stack), <see cref="DrawText"/> falls back to TextRenderer: the old flat emoji, never
    /// a missing one. Shared state is guarded by one lock, so any UI thread may call in.
    /// </summary>
    public static class ColorEmoji
    {
        private readonly struct Run
        {
            public readonly string Text;
            public readonly bool Emoji;
            public Run(string text, bool emoji) { Text = text; Emoji = emoji; }
        }

        /// <summary>Where each piece of one string sits, measured once per text, font and flags.</summary>
        private sealed class Layout
        {
            public Run[] Runs;
            public int[] Ends;           // x just after each run, from the first glyph
            public int Left, Right;      // TextRenderer's side margins for these flags
            public int LineHeight;
        }

        private static readonly object Gate = new object();
        private static int _state;                       // 0 not started · 1 ready · -1 unavailable
        private static string _status = "not used yet";
        private static int _rendered;
        private static IntPtr _d2dFactory, _dwFactory, _target, _brush;
        private static readonly Dictionary<int, IntPtr> Formats = new Dictionary<int, IntPtr>();
        private static readonly Dictionary<string, Bitmap> Cache = new Dictionary<string, Bitmap>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Layout> Layouts = new Dictionary<string, Layout>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> LeftPads = new Dictionary<string, int>(StringComparer.Ordinal);
        private const int CacheLimit = 512;

        /// <summary>True when colour emoji can be drawn. Starts the renderer on first use.</summary>
        public static bool Available
        {
            get { lock (Gate) { return EnsureReadyLocked(); } }
        }

        /// <summary>One line for Live debug.</summary>
        public static string Status
        {
            get
            {
                lock (Gate)
                {
                    return _state == 1
                        ? "colour (Direct2D) · " + _rendered + " glyph(s) rendered, " + Cache.Count + " cached"
                        : _status;
                }
            }
        }

        /// <summary>True when the text holds at least one emoji that should be drawn in colour.</summary>
        public static bool Contains(string text)
        {
            if (string.IsNullOrEmpty(text)) { return false; }
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c < 0x2300 && c != '#' && c != '*' && (c < '0' || c > '9')) { continue; }
                if (EmojiEnd(text, i) > i) { return true; }
                if (char.IsHighSurrogate(c)) { i++; }
            }
            return false;
        }

        /// <summary>
        /// Drop-in for <see cref="TextRenderer.DrawText(IDeviceContext, string, Font, Rectangle, Color, TextFormatFlags)"/>
        /// that keeps emoji in colour. Text without emoji — and anything this can't lay out the
        /// same way Windows would (several lines, wrapping, right-to-left) — goes straight to
        /// TextRenderer, so it looks exactly as it did.
        /// </summary>
        public static void DrawText(Graphics g, string text, Font font, Rectangle bounds, Color color, TextFormatFlags flags)
        {
            if (g == null || font == null) { return; }
            if (!Contains(text) || text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0
                || (flags & (TextFormatFlags.RightToLeft | TextFormatFlags.PrefixOnly)) != 0
                || !Available || HasTransformTextIgnores(g, flags))
            {
                TextRenderer.DrawText(g, text, font, bounds, color, flags);
                return;
            }

            Layout layout = LayoutFor(g, text, font, flags);
            float emPx = font.SizeInPoints * g.DpiY / 72f;
            var glyphs = new Bitmap[layout.Runs.Length];
            for (int i = 0; i < layout.Runs.Length; i++)
            {
                if (!layout.Runs[i].Emoji) { continue; }
                glyphs[i] = Glyph(layout.Runs[i].Text, emPx, color);
                if (glyphs[i] == null)
                {
                    TextRenderer.DrawText(g, text, font, bounds, color, flags);   // renderer failed — the old way
                    return;
                }
            }

            int total = layout.Ends[layout.Ends.Length - 1];
            int full = total + layout.Left + layout.Right;
            bool ellipsis = (flags & (TextFormatFlags.EndEllipsis | TextFormatFlags.WordEllipsis | TextFormatFlags.PathEllipsis)) != 0;
            if (full > bounds.Width + 2 && (flags & TextFormatFlags.WordBreak) != 0 && !ellipsis)
            {
                TextRenderer.DrawText(g, text, font, bounds, color, flags);   // it would wrap — keep Windows' wrapping
                return;
            }

            int origin;
            if (full > bounds.Width) { origin = bounds.X + layout.Left; }
            else if ((flags & TextFormatFlags.HorizontalCenter) != 0) { origin = bounds.X + layout.Left + (bounds.Width - full) / 2; }
            else if ((flags & TextFormatFlags.Right) != 0) { origin = bounds.Right - layout.Right - total; }
            else { origin = bounds.X + layout.Left; }

            int lineTop = (flags & TextFormatFlags.VerticalCenter) != 0 ? bounds.Y + (bounds.Height - layout.LineHeight) / 2
                        : (flags & TextFormatFlags.Bottom) != 0 ? bounds.Bottom - layout.LineHeight
                        : bounds.Y;

            // Each text piece is drawn unclipped in its own box (a glyph such as "J" reaches left of
            // its origin; clipping it there nudged the whole word a pixel) and clipped by the
            // Graphics instead, which holds the caller's bounds.
            TextFormatFlags pieceFlags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.Left
                | TextFormatFlags.NoClipping | TextFormatFlags.PreserveGraphicsClipping
                | (flags & (TextFormatFlags.NoPrefix | TextFormatFlags.HidePrefix | TextFormatFlags.ExpandTabs
                            | TextFormatFlags.VerticalCenter | TextFormatFlags.Bottom));

            bool clip = (flags & TextFormatFlags.NoClipping) == 0;
            Region savedClip = null;
            if (clip)
            {
                savedClip = g.Clip;
                g.SetClip(bounds, CombineMode.Intersect);
            }
            try
            {
                int start = origin;
                for (int i = 0; i < layout.Runs.Length; i++)
                {
                    int end = origin + layout.Ends[i];
                    int width = end - start;
                    bool overflows = end > bounds.Right;
                    if (layout.Runs[i].Emoji)
                    {
                        if (overflows && ellipsis) { break; }   // no room for the picture
                        Bitmap b = glyphs[i];
                        var dest = new Rectangle(start + (width - b.Width) / 2, lineTop + (layout.LineHeight - b.Height) / 2,
                                                 b.Width, b.Height);
                        g.DrawImage(b, dest, 0, 0, b.Width, b.Height, GraphicsUnit.Pixel);
                    }
                    else if (overflows && ellipsis)
                    {
                        // The piece that doesn't fit ends in "…" against the room that is really left.
                        TextRenderer.DrawText(g, layout.Runs[i].Text, font,
                            new Rectangle(start, bounds.Y, Math.Max(0, bounds.Right - start), bounds.Height), color,
                            (pieceFlags & ~TextFormatFlags.NoClipping) | TextFormatFlags.EndEllipsis);
                        break;
                    }
                    else
                    {
                        TextRenderer.DrawText(g, layout.Runs[i].Text, font,
                            new Rectangle(start, bounds.Y, Math.Max(1, width), bounds.Height), color, pieceFlags);
                    }
                    start = end;
                }
            }
            finally
            {
                if (clip)
                {
                    g.Clip = savedClip;
                    savedClip.Dispose();
                }
            }
        }

        // ── layout ───────────────────────────────────────────────────────────────

        private static Layout LayoutFor(Graphics g, string text, Font font, TextFormatFlags flags)
        {
            string key = text + "" + FontKey(font) + "" + (int)flags + "" + (int)g.DpiY;
            lock (Gate)
            {
                if (Layouts.TryGetValue(key, out Layout known)) { return known; }
            }

            List<Run> runs = Split(text);
            var huge = new Size(int.MaxValue, int.MaxValue);
            TextFormatFlags measure = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine
                | (flags & (TextFormatFlags.NoPrefix | TextFormatFlags.HidePrefix | TextFormatFlags.ExpandTabs));
            var layout = new Layout { Runs = runs.ToArray(), Ends = new int[runs.Count] };

            // Each piece ends where the text up to it ends, so every piece lands exactly where
            // TextRenderer puts it in the whole string. A trailing "x" is measured with the prefix and
            // taken off again, so spaces at the end of a prefix still count.
            int x = TextRenderer.MeasureText(g, "x", font, huge, measure).Width;
            int consumed = 0, previous = 0;
            for (int i = 0; i < runs.Count; i++)
            {
                consumed += runs[i].Text.Length;
                int end = TextRenderer.MeasureText(g, text.Substring(0, consumed) + "x", font, huge, measure).Width - x;
                previous = Math.Max(previous, end);
                layout.Ends[i] = previous;
            }

            bool padded = (flags & TextFormatFlags.NoPadding) == 0;
            layout.Left = padded ? LeftPaddingFor(g, font, flags) : 0;
            layout.Right = padded ? Math.Max(0, PaddingFor(g, font, flags) - layout.Left) : 0;
            layout.LineHeight = TextRenderer.MeasureText(g, "Ag", font, huge, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Height;

            lock (Gate)
            {
                if (Layouts.Count >= CacheLimit) { Layouts.Clear(); }
                Layouts[key] = layout;
            }
            return layout;
        }

        private static string FontKey(Font font) =>
            font.Name + "|" + (int)Math.Round(font.SizeInPoints * 100) + "|" + (int)font.Style;

        /// <summary>
        /// TextRenderer ignores a Graphics transform unless asked not to, while an image honours
        /// it; with a transform in play the two halves would drift apart, so draw the old way.
        /// </summary>
        private static bool HasTransformTextIgnores(Graphics g, TextFormatFlags flags)
        {
            if ((flags & TextFormatFlags.PreserveGraphicsTranslateTransform) != 0) { return false; }
            using (Matrix m = g.Transform)
            {
                // Judge the numbers, not Matrix.IsIdentity: a WinForms paint hands over a Graphics whose
                // matrix is exactly 1,0,0,1,0,0 yet reports IsIdentity=false (measured in a real window).
                // Trusting the flag sent every on-screen paint down the flat fallback — the colour only
                // ever showed on bitmaps.
                float[] e = m.Elements;
                return Math.Abs(e[0] - 1f) > 0.0001f || Math.Abs(e[1]) > 0.0001f || Math.Abs(e[2]) > 0.0001f
                    || Math.Abs(e[3] - 1f) > 0.0001f || Math.Abs(e[4]) > 0.0001f || Math.Abs(e[5]) > 0.0001f;
            }
        }

        /// <summary>Both side margins TextRenderer adds for these flags together, found by measuring.</summary>
        private static int PaddingFor(Graphics g, Font font, TextFormatFlags flags)
        {
            var huge = new Size(int.MaxValue, int.MaxValue);
            int padded = TextRenderer.MeasureText(g, "x", font, huge,
                (flags & TextFormatFlags.LeftAndRightPadding) | TextFormatFlags.SingleLine).Width;
            int bare = TextRenderer.MeasureText(g, "x", font, huge, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            return Math.Max(0, padded - bare);
        }

        /// <summary>
        /// How far TextRenderer insets the first glyph. The margins don't split evenly out of a
        /// measurement (7px in all for Segoe UI 9pt, not 3.5 a side), so this is found once per font
        /// by drawing a bar with and without padding and comparing where the ink starts.
        /// </summary>
        private static int LeftPaddingFor(Graphics g, Font font, TextFormatFlags flags)
        {
            TextFormatFlags mode = flags & TextFormatFlags.LeftAndRightPadding;
            string key = FontKey(font) + "|" + (int)g.DpiY + "|" + (int)mode;
            lock (Gate)
            {
                if (LeftPads.TryGetValue(key, out int known)) { return known; }
            }
            int pad = 0;
            try
            {
                using (var probe = new Bitmap(96, 64, PixelFormat.Format24bppRgb))
                using (Graphics pg = Graphics.FromImage(probe))
                {
                    int withPadding = InkStart(probe, pg, font, mode);
                    int without = InkStart(probe, pg, font, TextFormatFlags.NoPadding);
                    if (withPadding >= 0 && without >= 0) { pad = Math.Max(0, withPadding - without); }
                }
            }
            catch { pad = 0; }
            lock (Gate) { LeftPads[key] = pad; }
            return pad;
        }

        private static int InkStart(Bitmap probe, Graphics pg, Font font, TextFormatFlags padding)
        {
            pg.Clear(Color.White);
            TextRenderer.DrawText(pg, "|", font, new Rectangle(16, 0, 64, 64), Color.Black,
                padding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            for (int x = 0; x < probe.Width; x++)
            {
                for (int y = 0; y < probe.Height; y++)
                {
                    if (probe.GetPixel(x, y).R < 128) { return x; }
                }
            }
            return -1;
        }

        // ── finding emoji ────────────────────────────────────────────────────────

        private static List<Run> Split(string text)
        {
            var runs = new List<Run>();
            int textStart = 0, i = 0;
            while (i < text.Length)
            {
                int end = EmojiEnd(text, i);
                if (end > i)
                {
                    if (i > textStart) { runs.Add(new Run(text.Substring(textStart, i - textStart), false)); }
                    runs.Add(new Run(text.Substring(i, end - i), true));
                    i = end;
                    textStart = end;
                }
                else
                {
                    i += char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? 2 : 1;
                }
            }
            if (textStart < text.Length) { runs.Add(new Run(text.Substring(textStart), false)); }
            return runs;
        }

        private static int CodePointAt(string s, int i, out int length)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                length = 2;
                return char.ConvertToUtf32(s[i], s[i + 1]);
            }
            length = 1;
            return s[i];
        }

        private static bool IsRegionalIndicator(int cp) => cp >= 0x1F1E6 && cp <= 0x1F1FF;

        /// <summary>
        /// Where the emoji starting at <paramref name="i"/> ends (index after it), or -1 when no
        /// emoji starts there. An emoji is a pictograph from the emoji planes or a character that
        /// shows as emoji by default, or anything followed by U+FE0F (which asks for emoji
        /// presentation), plus what joins it: skin tones, ZWJ sequences, keycaps, tag sequences
        /// and the second half of a flag. U+FE0E (text presentation) opts a character out, so
        /// symbols Tempo tints on purpose (⚠ ✓ ✕ ★ ●) are left to TextRenderer.
        /// </summary>
        private static int EmojiEnd(string s, int i)
        {
            int cp = CodePointAt(s, i, out int len);
            if (cp == 0xFE0F || cp == 0xFE0E || cp == 0x200D || cp == 0x20E3) { return -1; }
            int next = i + len < s.Length ? s[i + len] : 0;
            if (next == 0xFE0E) { return -1; }
            bool isEmoji = next == 0xFE0F
                || (cp >= 0x1F000 && cp <= 0x1FAFF)
                || (cp < 0x10000 && IsDefaultEmoji(cp));
            if (!isEmoji) { return -1; }

            int j = i + len;
            if (IsRegionalIndicator(cp) && j < s.Length && IsRegionalIndicator(CodePointAt(s, j, out int riLen)))
            {
                return j + riLen;   // two regional indicators make one flag
            }
            while (j < s.Length)
            {
                int c = CodePointAt(s, j, out int l);
                if (c == 0xFE0F || c == 0x20E3 || (c >= 0x1F3FB && c <= 0x1F3FF) || (c >= 0xE0020 && c <= 0xE007F))
                {
                    j += l;
                    continue;
                }
                if (c == 0x200D && j + l < s.Length)
                {
                    CodePointAt(s, j + l, out int joined);
                    j += l + joined;
                    continue;
                }
                break;
            }
            return j;
        }

        /// <summary>Characters below the emoji planes that are emoji by default (Emoji_Presentation=Yes).</summary>
        private static bool IsDefaultEmoji(int cp)
        {
            switch (cp)
            {
                case 0x231A: case 0x231B: case 0x23F0: case 0x23F3: case 0x25FD: case 0x25FE:
                case 0x2614: case 0x2615: case 0x267F: case 0x2693: case 0x26A1: case 0x26AA:
                case 0x26AB: case 0x26BD: case 0x26BE: case 0x26C4: case 0x26C5: case 0x26CE:
                case 0x26D4: case 0x26EA: case 0x26F2: case 0x26F3: case 0x26F5: case 0x26FA:
                case 0x26FD: case 0x2705: case 0x270A: case 0x270B: case 0x2728: case 0x274C:
                case 0x274E: case 0x2757: case 0x27B0: case 0x27BF: case 0x2B1B: case 0x2B1C:
                case 0x2B50: case 0x2B55:
                    return true;
            }
            return (cp >= 0x23E9 && cp <= 0x23EC) || (cp >= 0x2648 && cp <= 0x2653)
                || (cp >= 0x2753 && cp <= 0x2755) || (cp >= 0x2795 && cp <= 0x2797);
        }

        // ── rendering ────────────────────────────────────────────────────────────

        private static Bitmap Glyph(string cluster, float emPx, Color color)
        {
            int sizeQ = Math.Max(4, (int)Math.Round(emPx * 4));    // quarter-pixel sizes share a bitmap
            string key = cluster + "" + sizeQ + "" + color.ToArgb();
            lock (Gate)
            {
                if (Cache.TryGetValue(key, out Bitmap hit)) { return hit; }
                if (!EnsureReadyLocked()) { return null; }
                Bitmap made = RenderLocked(cluster, sizeQ / 4f, color);
                if (made == null) { return null; }
                // Dropped, not disposed: a bitmap handed out this paint may still be on its way to
                // the screen. They are tiny, and the collector frees them.
                if (Cache.Count >= CacheLimit) { Cache.Clear(); }
                Cache[key] = made;
                return made;
            }
        }

        private static Bitmap RenderLocked(string cluster, float emPx, Color color)
        {
            IntPtr format = FormatForLocked(emPx);
            if (format == IntPtr.Zero) { return null; }

            int side = (int)Math.Ceiling(emPx * 1.5f) + 2;   // room for the ink of any emoji at this size
            IntPtr memDc = CreateCompatibleDC(IntPtr.Zero);
            if (memDc == IntPtr.Zero) { return null; }
            IntPtr dib = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                var header = new BITMAPINFOHEADER
                {
                    biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = side,
                    biHeight = -side,                // top-down rows
                    biPlanes = 1,
                    biBitCount = 32
                };
                dib = CreateDIBSection(memDc, ref header, 0, out IntPtr bits, IntPtr.Zero, 0);
                if (dib == IntPtr.Zero || bits == IntPtr.Zero) { return null; }
                old = SelectObject(memDc, dib);

                var area = new RECT { Right = side, Bottom = side };
                if (_bindDC(_target, memDc, ref area) < 0) { return null; }
                _setTextAntialiasMode(_target, D2D1_TEXT_ANTIALIAS_MODE_GRAYSCALE);   // ClearType needs an opaque target
                _beginDraw(_target);
                var transparent = new D2D1_COLOR_F();
                _clear(_target, ref transparent);
                var ink = new D2D1_COLOR_F { r = color.R / 255f, g = color.G / 255f, b = color.B / 255f, a = 1f };
                _setColor(_brush, ref ink);          // only for a glyph the font has no colour for
                var layoutRect = new D2D1_RECT_F { right = side, bottom = side };
                _drawText(_target, cluster, cluster.Length, format, ref layoutRect, _brush,
                          D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT, DWRITE_MEASURING_MODE_NATURAL);
                int hr = _endDraw(_target, IntPtr.Zero, IntPtr.Zero);
                if (hr < 0)
                {
                    _status = "flat (Windows' text drawing) — a colour draw failed: 0x" + hr.ToString("X8");
                    if (hr == D2DERR_RECREATE_TARGET) { ReleaseTargetLocked(); _state = 0; }   // rebuilt on the next draw
                    return null;
                }

                // Direct2D wrote premultiplied BGRA, which is exactly GDI+'s PArgb layout.
                var bmp = new Bitmap(side, side, PixelFormat.Format32bppPArgb);
                BitmapData data = bmp.LockBits(new Rectangle(0, 0, side, side), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    int rowBytes = side * 4;
                    var row = new byte[rowBytes];
                    for (int y = 0; y < side; y++)
                    {
                        Marshal.Copy(bits + y * rowBytes, row, 0, rowBytes);
                        Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, rowBytes);
                    }
                }
                finally
                {
                    bmp.UnlockBits(data);
                }
                _rendered++;
                return bmp;
            }
            finally
            {
                if (old != IntPtr.Zero) { SelectObject(memDc, old); }
                if (dib != IntPtr.Zero) { DeleteObject(dib); }
                DeleteDC(memDc);
            }
        }

        private static IntPtr FormatForLocked(float emPx)
        {
            int key = (int)Math.Round(emPx * 4);
            if (Formats.TryGetValue(key, out IntPtr cached)) { return cached; }
            int hr = _createTextFormat(_dwFactory, "Segoe UI Emoji", IntPtr.Zero, DWRITE_FONT_WEIGHT_NORMAL,
                                       DWRITE_FONT_STYLE_NORMAL, DWRITE_FONT_STRETCH_NORMAL, key / 4f, "en-us", out IntPtr format);
            if (hr < 0 || format == IntPtr.Zero)
            {
                _status = "flat (Windows' text drawing) — no text format: 0x" + hr.ToString("X8");
                return IntPtr.Zero;
            }
            Slot<SetEnumFn>(format, 3)(format, DWRITE_TEXT_ALIGNMENT_CENTER);
            Slot<SetEnumFn>(format, 4)(format, DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
            Slot<SetEnumFn>(format, 5)(format, DWRITE_WORD_WRAPPING_NO_WRAP);
            Formats[key] = format;
            return format;
        }

        private static bool EnsureReadyLocked()
        {
            if (_state == 1) { return true; }
            if (_state == -1) { return false; }
            try
            {
                int hr;
                if (_d2dFactory == IntPtr.Zero)
                {
                    Guid iid = IID_ID2D1Factory;
                    hr = D2D1CreateFactory(D2D1_FACTORY_TYPE_MULTI_THREADED, ref iid, IntPtr.Zero, out _d2dFactory);
                    if (hr < 0 || _d2dFactory == IntPtr.Zero) { return Fail("D2D1CreateFactory 0x" + hr.ToString("X8")); }
                }
                if (_dwFactory == IntPtr.Zero)
                {
                    Guid iid = IID_IDWriteFactory;
                    hr = DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, ref iid, out _dwFactory);
                    if (hr < 0 || _dwFactory == IntPtr.Zero) { return Fail("DWriteCreateFactory 0x" + hr.ToString("X8")); }
                    _createTextFormat = Slot<CreateTextFormatFn>(_dwFactory, 15);
                }

                var props = new D2D1_RENDER_TARGET_PROPERTIES
                {
                    type = D2D1_RENDER_TARGET_TYPE_SOFTWARE,
                    format = DXGI_FORMAT_B8G8R8A8_UNORM,
                    alphaMode = D2D1_ALPHA_MODE_PREMULTIPLIED,
                    dpiX = 96f,
                    dpiY = 96f                        // 1 DIP = 1 pixel
                };
                hr = Slot<CreateDCRenderTargetFn>(_d2dFactory, 16)(_d2dFactory, ref props, out _target);
                if (hr < 0 || _target == IntPtr.Zero) { return Fail("CreateDCRenderTarget 0x" + hr.ToString("X8")); }

                var black = new D2D1_COLOR_F { a = 1f };
                hr = Slot<CreateSolidColorBrushFn>(_target, 8)(_target, ref black, IntPtr.Zero, out _brush);
                if (hr < 0 || _brush == IntPtr.Zero) { return Fail("CreateSolidColorBrush 0x" + hr.ToString("X8")); }

                _bindDC = Slot<BindDCFn>(_target, 57);
                _setTextAntialiasMode = Slot<SetModeFn>(_target, 34);
                _beginDraw = Slot<BeginDrawFn>(_target, 48);
                _clear = Slot<ClearFn>(_target, 47);
                _drawText = Slot<DrawTextFn>(_target, 27);
                _endDraw = Slot<EndDrawFn>(_target, 49);
                _setColor = Slot<SetColorFn>(_brush, 8);
                _state = 1;
                return true;
            }
            catch (Exception ex)   // DllNotFound / EntryPointNotFound on a stripped-down Windows
            {
                return Fail(ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static bool Fail(string what)
        {
            _state = -1;
            _status = "flat (Windows' text drawing) — colour renderer unavailable: " + what;
            ReleaseTargetLocked();
            foreach (IntPtr f in Formats.Values) { ReleaseCom(f); }
            Formats.Clear();
            ReleaseCom(_dwFactory);
            ReleaseCom(_d2dFactory);
            _dwFactory = _d2dFactory = IntPtr.Zero;
            return false;
        }

        private static void ReleaseTargetLocked()
        {
            ReleaseCom(_brush);
            ReleaseCom(_target);
            _brush = _target = IntPtr.Zero;
        }

        private static void ReleaseCom(IntPtr com)
        {
            if (com == IntPtr.Zero) { return; }
            try { Slot<ReleaseFn>(com, 2)(com); } catch { }
        }

        /// <summary>The method in vtable slot <paramref name="index"/> of a COM object.</summary>
        private static T Slot<T>(IntPtr com, int index) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(com), index * IntPtr.Size));

        // ── test hooks ───────────────────────────────────────────────────────────

        internal static Bitmap RenderForTest(string cluster, float emPx, Color color)
        {
            lock (Gate) { return EnsureReadyLocked() ? RenderLocked(cluster, emPx, color) : null; }
        }

        internal static Bitmap GlyphForTest(string cluster, float emPx, Color color) => Glyph(cluster, emPx, color);

        internal static int LeftPaddingForTest(Graphics g, Font font, TextFormatFlags flags) => LeftPaddingFor(g, font, flags);

        internal static string RunsForTest(string text)
        {
            var parts = new List<string>();
            foreach (Run r in Split(text)) { parts.Add(r.Emoji ? "[" + r.Text + "]" : r.Text); }
            return string.Join("|", parts);
        }

        // ── Direct2D / DirectWrite ───────────────────────────────────────────────
        // Slots: IUnknown 0–2 · ID2D1Factory CreateDCRenderTarget 16 · ID2D1RenderTarget
        // CreateSolidColorBrush 8, DrawText 27, SetTextAntialiasMode 34, Clear 47, BeginDraw 48,
        // EndDraw 49 · ID2D1DCRenderTarget BindDC 57 · ID2D1SolidColorBrush SetColor 8 ·
        // IDWriteFactory CreateTextFormat 15 · IDWriteTextFormat SetTextAlignment 3,
        // SetParagraphAlignment 4, SetWordWrapping 5.

        private static readonly Guid IID_ID2D1Factory = new Guid("06152247-6f50-465a-9245-118bfd3b6007");
        private static readonly Guid IID_IDWriteFactory = new Guid("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");

        private const int D2D1_FACTORY_TYPE_MULTI_THREADED = 1;
        private const int DWRITE_FACTORY_TYPE_SHARED = 0;
        private const int D2D1_RENDER_TARGET_TYPE_SOFTWARE = 1;
        private const int DXGI_FORMAT_B8G8R8A8_UNORM = 87;
        private const int D2D1_ALPHA_MODE_PREMULTIPLIED = 1;
        private const int D2D1_TEXT_ANTIALIAS_MODE_GRAYSCALE = 2;
        private const int D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT = 4;
        private const int DWRITE_MEASURING_MODE_NATURAL = 0;
        private const int DWRITE_FONT_WEIGHT_NORMAL = 400;
        private const int DWRITE_FONT_STYLE_NORMAL = 0;
        private const int DWRITE_FONT_STRETCH_NORMAL = 5;
        private const int DWRITE_TEXT_ALIGNMENT_CENTER = 2;
        private const int DWRITE_PARAGRAPH_ALIGNMENT_CENTER = 2;
        private const int DWRITE_WORD_WRAPPING_NO_WRAP = 1;
        private const int D2DERR_RECREATE_TARGET = unchecked((int)0x8899000C);

        [StructLayout(LayoutKind.Sequential)]
        private struct D2D1_COLOR_F { public float r, g, b, a; }

        [StructLayout(LayoutKind.Sequential)]
        private struct D2D1_RECT_F { public float left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct D2D1_RENDER_TARGET_PROPERTIES
        {
            public int type;
            public int format;
            public int alphaMode;
            public float dpiX;
            public float dpiY;
            public int usage;
            public int minLevel;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint ReleaseFn(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateDCRenderTargetFn(IntPtr self, ref D2D1_RENDER_TARGET_PROPERTIES props, out IntPtr target);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        private delegate int CreateTextFormatFn(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string family, IntPtr collection,
                                                int weight, int style, int stretch, float size,
                                                [MarshalAs(UnmanagedType.LPWStr)] string locale, out IntPtr format);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetEnumFn(IntPtr self, int value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateSolidColorBrushFn(IntPtr self, ref D2D1_COLOR_F color, IntPtr properties, out IntPtr brush);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void SetColorFn(IntPtr self, ref D2D1_COLOR_F color);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int BindDCFn(IntPtr self, IntPtr hdc, ref RECT rect);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void SetModeFn(IntPtr self, int mode);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void BeginDrawFn(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void ClearFn(IntPtr self, ref D2D1_COLOR_F color);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        private delegate void DrawTextFn(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string text, int length, IntPtr format,
                                         ref D2D1_RECT_F rect, IntPtr brush, int options, int measuringMode);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int EndDrawFn(IntPtr self, IntPtr tag1, IntPtr tag2);

        private static CreateTextFormatFn _createTextFormat;
        private static BindDCFn _bindDC;
        private static SetModeFn _setTextAntialiasMode;
        private static BeginDrawFn _beginDraw;
        private static ClearFn _clear;
        private static DrawTextFn _drawText;
        private static EndDrawFn _endDraw;
        private static SetColorFn _setColor;

        [DllImport("d2d1.dll", ExactSpelling = true)]
        private static extern int D2D1CreateFactory(int factoryType, ref Guid riid, IntPtr factoryOptions, out IntPtr factory);

        [DllImport("dwrite.dll", ExactSpelling = true)]
        private static extern int DWriteCreateFactory(int factoryType, ref Guid iid, out IntPtr factory);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER header, uint usage,
                                                      out IntPtr bits, IntPtr section, uint offset);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr gdiObject);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        private static extern bool DeleteObject(IntPtr gdiObject);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        private static extern bool DeleteDC(IntPtr hdc);
    }
}
