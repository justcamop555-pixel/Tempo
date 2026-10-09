using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// The Captions tab's row of speakers: each one's face — when the face analyzer has linked a face
    /// on screen to their voice — their name (or "Speaker N") in the colour it has on the caption bar,
    /// and whether they are talking right now. Hovering a speaker shows their face large; clicking one
    /// raises <see cref="ChipClicked"/> so they can be given a name. Owner-drawn, so the faces can be
    /// round and the row costs one control.
    ///
    /// Speakers used to exist only as text in Live debug; nothing on the tab said who was talking or
    /// which face a number belonged to.
    /// </summary>
    public sealed class SpeakerStrip : Control
    {
        /// <summary>One speaker as the strip draws it.</summary>
        public sealed class Chip
        {
            public int Label;
            /// <summary>What to call them: the name they were given, or "Speaker N".</summary>
            public string Name;
            public Color Color;
            /// <summary>Their face, or null. The strip owns it once handed over.</summary>
            public Bitmap Face;
            public bool Talking;
            public string Detail;
        }

        /// <summary>A speaker was clicked (the argument is their label).</summary>
        public event Action<int> ChipClicked;

        private List<Chip> _chips = new List<Chip>();
        private readonly List<Rectangle> _chipBounds = new List<Rectangle>();
        private string _message = "";
        private string _status = "";
        private Theme _theme;
        private int _hover = -1;                 // index into _chips of the chip under the mouse
        private readonly ToolTip _preview;
        private Chip _previewChip;               // what the preview is drawing (a copy of the fields; see ShowPreview)
        private Bitmap _previewFace;             // the preview's own copy of the face

        // Bigger than the first version's 34 px: at that size a face was a blob of skin colour.
        private const int FaceSize = 44;
        private const int MinChipWidth = 112;
        private const int MaxChipWidth = 196;
        private const int PreviewFace = 128;

        public SpeakerStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false;
            _preview = new ToolTip { OwnerDraw = true, UseAnimation = false, UseFading = false, ShowAlways = true };
            _preview.Popup += OnPreviewPopup;
            _preview.Draw += OnPreviewDraw;
        }

        public void SetTheme(Theme theme)
        {
            _theme = theme;
            Invalidate();
        }

        /// <summary>A line of text in place of the chips (labels off, captions off, nobody yet).</summary>
        public void SetMessage(string message, string status)
        {
            bool had = _chips.Count > 0;
            ReplaceChips(new List<Chip>());
            if (!had && _message == message && _status == status) { return; }
            _message = message ?? "";
            _status = status ?? "";
            Invalidate();
        }

        /// <summary>The speakers to draw; <paramref name="status"/> is the muted text on the right.</summary>
        public void SetChips(List<Chip> chips, string status)
        {
            ReplaceChips(chips ?? new List<Chip>());
            _message = "";
            _status = status ?? "";
            Invalidate();
        }

        /// <summary>The speakers the strip was last given, left to right (for tests).</summary>
        internal IReadOnlyList<Chip> Chips => _chips;

        /// <summary>How many of them are drawn after the last paint; the rest are the "+N" (for tests).</summary>
        internal int ChipsDrawn => _chipBounds.Count;

        private void ReplaceChips(List<Chip> chips)
        {
            foreach (Chip c in _chips)
            {
                if (c.Face != null && !chips.Exists(n => ReferenceEquals(n.Face, c.Face))) { try { c.Face.Dispose(); } catch { } }
            }
            _chips = chips;
            // The speaker under the mouse may be gone; the preview follows on the next move.
            if (_hover >= _chips.Count) { _hover = -1; HidePreview(); }
        }

        private static TextFormatFlags LeftFlags =>
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;

        private string LineFor(Chip c) => c.Talking ? "● " + Utils.Localization.T("talking") : (c.Detail ?? "");

        private static string NameFor(Chip c) =>
            string.IsNullOrWhiteSpace(c.Name) ? Utils.SpeakerTurnLabeler.SpeakerWord + " " + c.Label : c.Name.Trim();

        /// <summary>
        /// Where each chip goes: as wide as its name needs (a name like "Grandma" and a "Speaker 12"
        /// cost different room), and as many as fit beside the status — the rest summarised as "+N".
        /// </summary>
        private void Measure(Graphics g, Font bold, int statusLeft)
        {
            _chipBounds.Clear();
            int x = 0;
            int limit = Math.Max(0, statusLeft - 12);
            const int moreWidth = 40;              // room kept for "+N" when not everyone fits
            for (int i = 0; i < _chips.Count; i++)
            {
                Chip c = _chips[i];
                int text = Math.Max(
                    TextRenderer.MeasureText(g, NameFor(c), bold, new Size(int.MaxValue, Height), LeftFlags).Width,
                    TextRenderer.MeasureText(g, LineFor(c), Font, new Size(int.MaxValue, Height), LeftFlags).Width);
                // The talking line swaps in and out; measure that too so the chip does not jump.
                text = Math.Max(text, TextRenderer.MeasureText(g, "● " + Utils.Localization.T("talking"), Font,
                                                              new Size(int.MaxValue, Height), LeftFlags).Width);
                int w = Math.Max(MinChipWidth, Math.Min(MaxChipWidth, FaceSize + 10 + text + 16));
                bool last = i == _chips.Count - 1;
                if (x + w > limit - (last ? 0 : moreWidth))
                {
                    // Squeeze the final chip into what is left rather than drop it for "+1".
                    if (last && limit - x >= MinChipWidth) { w = limit - x; }
                    else { break; }
                }
                _chipBounds.Add(new Rectangle(x, 0, w, Height));
                x += w;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color back = Parent != null ? Parent.BackColor : BackColor;
            g.Clear(back);
            Color text = _theme != null ? _theme.Text : ForeColor;
            Color muted = _theme != null ? _theme.TextMuted : Color.Gray;
            Color talking = _theme != null ? _theme.SuccessText : Color.LimeGreen;
            // A wash of the text colour, not a theme surface: some themes paint their cards in
            // exactly Surface2, and the hover would vanish.
            Color hoverBack = Blend(back, text, 0.16);

            // The status (faces on screen, or why there are none) on the right, muted. Measured and
            // drawn with the SAME flags: it was measured without TextRenderer's side padding and drawn
            // with it, so its first letter was cut off ("aces on screen: 2").
            int statusLeft = Width;
            if (!string.IsNullOrEmpty(_status))
            {
                const TextFormatFlags statusFlags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
                Size ss = TextRenderer.MeasureText(g, _status, Font, new Size(int.MaxValue, Height), statusFlags);
                statusLeft = Math.Max(0, Width - ss.Width - 2);
                TextRenderer.DrawText(g, _status, Font, new Rectangle(statusLeft, 0, Width - statusLeft, Height), muted,
                                      statusFlags | TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }

            if (_chips.Count == 0)
            {
                _chipBounds.Clear();
                TextRenderer.DrawText(g, _message, Font, new Rectangle(0, 0, Math.Max(0, statusLeft - 12), Height), muted,
                                      LeftFlags & ~TextFormatFlags.NoPadding);
                return;
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using (var bold = new Font(Font, FontStyle.Bold))
            {
                Measure(g, bold, statusLeft);
                for (int i = 0; i < _chipBounds.Count; i++)
                {
                    Chip c = _chips[i];
                    Rectangle r = _chipBounds[i];
                    if (i == _hover)
                    {
                        // Says "this is clickable" before anyone reads the hint.
                        using (var path = RoundRect(new Rectangle(r.X, r.Y + 1, r.Width - 6, r.Height - 2), 10))
                        using (var fill = new SolidBrush(hoverBack))
                        {
                            g.FillPath(fill, path);
                        }
                    }
                    var face = new Rectangle(r.X + 4, r.Y + (r.Height - FaceSize) / 2, FaceSize, FaceSize);
                    Color ground = i == _hover ? hoverBack : back;
                    DrawFace(g, c, face, bold, ground);

                    int tx = face.Right + 10;
                    int tw = Math.Max(0, r.Right - 8 - tx);
                    int half = r.Height / 2;
                    // The speaker colours were picked for the caption bar's black; as TEXT on a light
                    // theme's card several of them fade out, so the words are nudged to a readable
                    // shade of the same colour (the ring keeps the bar's exact colour).
                    TextRenderer.DrawText(g, NameFor(c), bold, new Rectangle(tx, r.Y + 3, tw, half - 3),
                                          Theme.Readable(c.Color, ground), LeftFlags);
                    TextRenderer.DrawText(g, LineFor(c), Font, new Rectangle(tx, r.Y + half, tw, half - 3),
                                          c.Talking ? talking : muted, LeftFlags);
                }
            }
            if (_chipBounds.Count < _chips.Count)
            {
                int x = _chipBounds.Count > 0 ? _chipBounds[_chipBounds.Count - 1].Right : 0;
                TextRenderer.DrawText(g, "+" + (_chips.Count - _chipBounds.Count), Font,
                                      new Rectangle(x, 0, Math.Max(0, statusLeft - x - 8), Height), muted,
                                      LeftFlags & ~TextFormatFlags.NoPadding);
            }
        }

        /// <summary>A round face with a ring in the speaker's colour (thicker while they talk), or — with
        /// no face linked — the colour with their number in it.</summary>
        private static void DrawFace(Graphics g, Chip c, Rectangle face, Font bold, Color ground)
        {
            if (c.Face != null)
            {
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(face);
                    Region old = g.Clip;
                    g.SetClip(path, CombineMode.Intersect);
                    g.DrawImage(c.Face, face);
                    g.Clip = old;
                }
            }
            else
            {
                using (var fill = new SolidBrush(Color.FromArgb(70, c.Color)))
                {
                    g.FillEllipse(fill, face);
                }
                // On the tint it actually sits on: the fill is the speaker colour at 70/255 over the ground.
                TextRenderer.DrawText(g, c.Label.ToString(), bold, face, Theme.Readable(c.Color, Blend(ground, c.Color, 70 / 255.0)),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            using (var ring = new Pen(c.Color, c.Talking ? 3f : 1.5f))
            {
                g.DrawEllipse(ring, face);
            }
        }

        private static Color Blend(Color under, Color over, double amount)
        {
            return Color.FromArgb(
                (int)Math.Round(under.R + (over.R - under.R) * amount),
                (int)Math.Round(under.G + (over.G - under.G) * amount),
                (int)Math.Round(under.B + (over.B - under.B) * amount));
        }

        private static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private int ChipAt(Point p)
        {
            for (int i = 0; i < _chipBounds.Count && i < _chips.Count; i++)
            {
                if (_chipBounds[i].Contains(p)) { return i; }
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int at = ChipAt(e.Location);
            if (at == _hover) { return; }
            _hover = at;
            Cursor = at >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
            if (at >= 0) { ShowPreview(at); } else { HidePreview(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1) { _hover = -1; Invalidate(); }
            Cursor = Cursors.Default;
            HidePreview();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) { return; }
            int at = ChipAt(e.Location);
            if (at < 0 || at >= _chips.Count) { return; }
            HidePreview();
            ChipClicked?.Invoke(_chips[at].Label);
        }

        // ── The hover preview ───────────────────────────────────────────────
        // The chip's face is 44 px; the analyzer keeps a 96 px picture, which is what you need to
        // actually recognise someone before naming them. Drawn in a tooltip so it floats over the
        // transcript card without moving anything.

        private void ShowPreview(int index)
        {
            if (index < 0 || index >= _chips.Count || index >= _chipBounds.Count) { HidePreview(); return; }
            Chip c = _chips[index];
            // The preview keeps its own copy: the refresh timer replaces the strip's chips (and
            // disposes their faces) while the preview is up.
            DisposePreviewFace();
            try { _previewFace = c.Face != null ? new Bitmap(c.Face) : null; } catch { _previewFace = null; }
            _previewChip = new Chip { Label = c.Label, Name = c.Name, Color = c.Color, Talking = c.Talking, Detail = c.Detail };
            Rectangle r = _chipBounds[index];
            try
            {
                _preview.Hide(this);
                // Any non-empty text; the tooltip is owner-drawn (OnPreviewDraw) and sized in OnPreviewPopup.
                _preview.Show(NameFor(c), this, new Point(r.X, Height + 4));
            }
            catch { }
        }

        private void HidePreview()
        {
            try { _preview.Hide(this); } catch { }
            _previewChip = null;
            DisposePreviewFace();
        }

        private void DisposePreviewFace()
        {
            Bitmap f = _previewFace;
            _previewFace = null;
            try { f?.Dispose(); } catch { }
        }

        private string PreviewHint => Utils.Localization.T("Click to give them a name.");
        private string PreviewPrivacy => Utils.Localization.T("The picture stays in memory and is never saved.");

        private void OnPreviewPopup(object sender, PopupEventArgs e)
        {
            Chip c = _previewChip;
            if (c == null) { e.Cancel = true; return; }
            using (var bold = new Font(Font.FontFamily, Font.Size + 1.5f, FontStyle.Bold))
            {
                int text = Math.Max(
                    TextRenderer.MeasureText(NameFor(c), bold).Width,
                    Math.Max(TextRenderer.MeasureText(PreviewHint, Font).Width,
                             _previewFace != null ? TextRenderer.MeasureText(PreviewPrivacy, Font).Width : 0));
                int width = Math.Max(PreviewFace, Math.Min(300, text)) + 24;
                int height = 12 + PreviewFace + 8 + bold.Height + 4 + Font.Height * (_previewFace != null ? 2 : 1) + 12;
                e.ToolTipSize = new Size(width, height);
            }
        }

        private void OnPreviewDraw(object sender, DrawToolTipEventArgs e)
        {
            Graphics g = e.Graphics;
            Chip c = _previewChip;
            Color back = _theme != null ? _theme.Surface : SystemColors.Info;
            Color border = _theme != null ? _theme.Border : SystemColors.WindowFrame;
            Color muted = _theme != null ? _theme.TextMuted : SystemColors.GrayText;
            Color text = _theme != null ? _theme.Text : SystemColors.InfoText;
            using (var b = new SolidBrush(back)) { g.FillRectangle(b, e.Bounds); }
            using (var p = new Pen(border)) { g.DrawRectangle(p, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1); }
            if (c == null) { return; }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            var face = new Rectangle((e.Bounds.Width - PreviewFace) / 2, 12, PreviewFace, PreviewFace);
            using (var bold = new Font(Font.FontFamily, Font.Size + 1.5f, FontStyle.Bold))
            {
                if (_previewFace != null)
                {
                    using (var path = RoundRect(face, 14))
                    {
                        Region old = g.Clip;
                        g.SetClip(path, CombineMode.Intersect);
                        g.DrawImage(_previewFace, face);
                        g.Clip = old;
                        using (var ring = new Pen(c.Color, 2f)) { g.DrawPath(ring, path); }
                    }
                }
                else
                {
                    using (var fill = new SolidBrush(Color.FromArgb(70, c.Color))) { g.FillEllipse(fill, face); }
                    using (var big = new Font(Font.FontFamily, 30f, FontStyle.Bold))
                    {
                        // Large text: 3:1 is the bar for it.
                        TextRenderer.DrawText(g, c.Label.ToString(), big, face, Theme.Readable(c.Color, Blend(back, c.Color, 70 / 255.0), 3.0),
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    }
                    using (var ring = new Pen(c.Color, 2f)) { g.DrawEllipse(ring, face); }
                }
                const TextFormatFlags centre = TextFormatFlags.HorizontalCenter | TextFormatFlags.Top |
                                               TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
                int y = face.Bottom + 8;
                TextRenderer.DrawText(g, NameFor(c), bold, new Rectangle(8, y, e.Bounds.Width - 16, bold.Height),
                                      Theme.Readable(c.Color, back), centre);
                y += bold.Height + 4;
                TextRenderer.DrawText(g, PreviewHint, Font, new Rectangle(8, y, e.Bounds.Width - 16, Font.Height), text, centre);
                if (_previewFace != null)
                {
                    y += Font.Height;
                    TextRenderer.DrawText(g, PreviewPrivacy, Font, new Rectangle(8, y, e.Bounds.Width - 16, Font.Height), muted, centre);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                HidePreview();
                try { _preview.Dispose(); } catch { }
                ReplaceChips(new List<Chip>());
            }
            base.Dispose(disposing);
        }
    }
}
