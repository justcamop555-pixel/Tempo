using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// Gives a caption speaker a name ("Speaker 2" → "Mia"), opened by clicking them on the Captions
    /// tab's Speakers card. Shows their face large — the whole point is knowing WHO you are naming —
    /// or their colour and number when no face is linked. The name then replaces "Speaker 2" on the
    /// caption bar and in the transcript, including the lines they already spoke.
    /// </summary>
    public sealed class SpeakerNameForm : Form
    {
        /// <summary>Longest name kept: it has to fit the caption bar's label and a chip on the card.</summary>
        public const int MaxNameLength = 24;

        private readonly TextBox _input;
        private readonly Label _prompt;
        private readonly Control _picture;
        private readonly Bitmap _face;           // the form's own copy

        /// <summary>The name to use; "" to go back to the number. Only meaningful after OK.</summary>
        public string Value { get; private set; } = "";

        public SpeakerNameForm(Theme theme, int label, string currentName, Bitmap face, Color color)
        {
            // Built in 100% units; scaled with the screen at the end (UiFactory.ScaleToScreen).
            theme = theme ?? Theme.ForKind(Models.ThemeKind.Dark);
            try { _face = face != null ? new Bitmap(face) : null; } catch { _face = null; }

            Text = Utils.Localization.T("Name this speaker");
            ClientSize = new Size(440, 190);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = theme.Background;
            ForeColor = theme.Text;
            Font = UiFactory.BodyFont;

            var picture = new FacePicture(_face, color, label) { Left = 18, Top = 18, Width = 96, Height = 96 };
            _picture = picture;

            string shownAs = string.IsNullOrWhiteSpace(currentName)
                ? Utils.SpeakerTurnLabeler.SpeakerWord + " " + label
                : currentName.Trim();
            // Built with F, not handed to UiFactory.Label as a key: the speaker's name is in it.
            var prompt = UiFactory.Label("", 132, 18);
            prompt.Text = Utils.Localization.F("What should {0} be called?", shownAs);
            prompt.AutoSize = true;
            prompt.MaximumSize = new Size(290, 0);
            _prompt = prompt;

            _input = UiFactory.Text(132, 52, 290, currentName ?? string.Empty);
            _input.MaxLength = MaxNameLength;
            _input.SelectAll();

            var hint = UiFactory.Caption("The name shows on the caption bar and in the transcript.", 132, 84);
            hint.AutoSize = true;
            hint.MaximumSize = new Size(290, 0);

            var cancel = UiFactory.Button("Cancel", 0, 144, 90, 30);
            UiFactory.FitToText96(cancel, 90, 16);
            cancel.Left = 422 - cancel.Width;
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            var save = UiFactory.PrimaryButton("Save", 0, 144, 90, 30, theme);
            UiFactory.FitToText96(save, 90, 16);
            save.Left = cancel.Left - 8 - save.Width;
            save.Click += (s, e) =>
            {
                Value = Clean(_input.Text);
                DialogResult = DialogResult.OK;
                Close();
            };

            Controls.Add(picture);
            Controls.Add(prompt);
            Controls.Add(_input);
            Controls.Add(hint);
            Controls.Add(save);
            Controls.Add(cancel);

            // Only when there is a name to take away.
            if (!string.IsNullOrWhiteSpace(currentName))
            {
                var clear = UiFactory.Button("Use the number", 18, 144, 130, 30);
                UiFactory.FitToText96(clear, 130, 16);
                clear.Click += (s, e) =>
                {
                    Value = "";
                    DialogResult = DialogResult.OK;
                    Close();
                };
                Controls.Add(clear);
            }

            AcceptButton = save;
            CancelButton = cancel;
            ThemeManager.Apply(this, theme);
            UiFactory.ScaleToScreen(this);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // The question holds the speaker's current name, and 24 wide letters wrap it onto a third
            // line. Move everything under it down rather than let the question run into the box.
            try
            {
                int overlap = _prompt.Bottom + LogicalToDeviceUnits(6) - _input.Top;
                if (overlap > 0)
                {
                    int from = _input.Top;
                    foreach (Control c in Controls)
                    {
                        if (c != _prompt && c != _picture && c.Top >= from) { c.Top += overlap; }
                    }
                    ClientSize = new Size(ClientSize.Width, ClientSize.Height + overlap);
                }
            }
            catch { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try { _input.Focus(); _input.SelectAll(); } catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { try { _face?.Dispose(); } catch { } }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Asks for a name. Returns null when cancelled, "" to go back to "Speaker N", otherwise the
        /// cleaned name.
        /// </summary>
        public static string Ask(IWin32Window owner, Theme theme, int label, string currentName, Bitmap face, Color color)
        {
            using (var form = new SpeakerNameForm(theme, label, currentName, face, color))
            {
                return form.ShowDialog(owner) == DialogResult.OK ? form.Value : null;
            }
        }

        /// <summary>
        /// A name as it can be shown: one line, no colon (the label is "Name: words", so a colon inside
        /// the name reads as the end of it), runs of spaces folded, at most <see cref="MaxNameLength"/>
        /// characters without splitting an emoji in half.
        /// </summary>
        internal static string Clean(string raw)
        {
            if (string.IsNullOrEmpty(raw)) { return ""; }
            var sb = new StringBuilder(raw.Length);
            bool pendingSpace = false;
            foreach (char c in raw)
            {
                bool gap = char.IsWhiteSpace(c) || char.IsControl(c) || c == ':' || c == '：';
                if (gap)
                {
                    if (sb.Length > 0) { pendingSpace = true; }
                    continue;
                }
                if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
                sb.Append(c);
            }
            string s = sb.ToString();
            if (s.Length > MaxNameLength)
            {
                int cut = MaxNameLength;
                if (char.IsHighSurrogate(s[cut - 1])) { cut--; }
                s = s.Substring(0, cut).TrimEnd();
            }
            return s;
        }

        /// <summary>The speaker's face, rounded, with a ring in their colour — or the colour and number.</summary>
        private sealed class FacePicture : Control
        {
            private readonly Bitmap _face;
            private readonly Color _color;
            private readonly int _label;

            public FacePicture(Bitmap face, Color color, int label)
            {
                _face = face;
                _color = color;
                _label = label;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                TabStop = false;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                Color back = Parent != null ? Parent.BackColor : BackColor;
                g.Clear(back);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                var r = new Rectangle(1, 1, Width - 3, Height - 3);
                if (_face != null)
                {
                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(r);
                        Region old = g.Clip;
                        g.SetClip(path, CombineMode.Intersect);
                        g.DrawImage(_face, r);
                        g.Clip = old;
                    }
                }
                else
                {
                    using (var fill = new SolidBrush(Color.FromArgb(70, _color))) { g.FillEllipse(fill, r); }
                    // On the tint it sits on (the colour at 70/255 over the window); large text, so 3:1.
                    Color tint = Color.FromArgb(
                        back.R + (_color.R - back.R) * 70 / 255,
                        back.G + (_color.G - back.G) * 70 / 255,
                        back.B + (_color.B - back.B) * 70 / 255);
                    using (var big = new Font("Segoe UI", 26f, FontStyle.Bold))
                    {
                        TextRenderer.DrawText(g, _label.ToString(), big, r, Theme.Readable(_color, tint, 3.0),
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    }
                }
                using (var ring = new Pen(_color, 2.5f)) { g.DrawEllipse(ring, r); }
            }
        }
    }
}
