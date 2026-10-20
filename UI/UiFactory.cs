using System;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// Factory helpers that create consistently styled WinForms controls so the
    /// large form-building code stays readable.
    /// </summary>
    public static class UiFactory
    {
        public static readonly Font BodyFont = new Font("Segoe UI", 9f);
        public static readonly Font BoldFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        public static readonly Font TitleFont = new Font("Segoe UI", 16f, FontStyle.Bold);
        public static readonly Font SubtitleFont = new Font("Segoe UI", 8.5f);
        public static readonly Font ButtonFont = new Font("Segoe UI", 10f, FontStyle.Bold);

        /// <summary>
        /// Segoe UI fonts by (size, style), created once and shared.
        ///
        /// Every label used to allocate its own Font. A Font is a GDI+ handle, and the
        /// tabs build hundreds of labels between them at a handful of distinct sizes, so
        /// nearly all of those allocations were duplicates of one another — paid on the
        /// startup path, then held for the life of the process.
        ///
        /// Sharing one instance per distinct (size, style) is safe here because these are
        /// never disposed and never mutated: a Font is immutable, and assigning the same
        /// instance to many controls is explicitly supported by WinForms (controls do not
        /// dispose a Font they were merely given). That is already how BodyFont and the
        /// other statics above are used throughout.
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<(float, FontStyle), Font> FontCache =
            new System.Collections.Generic.Dictionary<(float, FontStyle), Font>();

        /// <summary>Shared Segoe UI font for this size/style — see <see cref="FontCache"/>.</summary>
        public static Font FontFor(float size, FontStyle style)
        {
            // The UI is built on one thread, but lock anyway: a stray call from a worker
            // would otherwise corrupt the dictionary, and this is far off any hot path.
            lock (FontCache)
            {
                if (!FontCache.TryGetValue((size, style), out Font f))
                {
                    f = new Font("Segoe UI", size, style);
                    FontCache[(size, style)] = f;
                }
                return f;
            }
        }

        public static Label Label(string text, int x, int y, FontStyle style = FontStyle.Regular, float size = 9f)
        {
            text = Localization.T(text);
            // EmojiLabel paints plain text exactly like a Label, and keeps an emoji (🔒, 🔊, 🎙) in colour.
            return new EmojiLabel
            {
                Text = text,
                Left = x,
                Top = y,
                AutoSize = true,
                Font = FontFor(size, style)
                // BackColor set by ThemeManager (solid, parent-matched) - not transparent.
            };
        }

        public static Label Caption(string text, int x, int y)
        {
            var lbl = Label(text, x, y, FontStyle.Regular, 8.25f);
            return lbl;
        }

        public static NumericUpDown Numeric(int x, int y, int width, decimal min, decimal max, decimal value)
        {
            var nud = new FlatNumericUpDown
            {
                Left = x,
                Top = y,
                Width = width,
                Minimum = min,
                Maximum = max,
                Font = BodyFont,
                BorderStyle = BorderStyle.FixedSingle
            };

            // Clamp the initial value into range to avoid an exception.
            if (value < min) value = min;
            if (value > max) value = max;
            nud.Value = value;
            return nud;
        }

        public static ComboBox Combo(int x, int y, int width, params string[] items)
        {
            var combo = new FlatComboBox
            {
                Left = x,
                Top = y,
                Width = width,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Font = BodyFont
            };

            if (items != null && items.Length > 0)
            {
                combo.Items.AddRange(items);
                combo.SelectedIndex = 0;
            }

            return combo;
        }

        public static CheckBox Check(string text, int x, int y, bool isChecked = false)
        {
            text = Localization.T(text);
            return new ModernCheckBox
            {
                Text = text,
                Left = x,
                Top = y,
                AutoSize = true,
                Checked = isChecked,
                Font = BodyFont
                // BackColor is set by ThemeManager to a concrete card/page colour, NOT
                // Color.Transparent - transparent checkboxes ghosted/left stale text over
                // the scrolling, custom-painted settings panel.
            };
        }

        public static CheckBox Toggle(string text, int x, int y, bool isChecked = false)
        {
            text = Localization.T(text);
            return new ToggleSwitch
            {
                Text = text,
                Left = x,
                Top = y,
                AutoSize = true,
                Checked = isChecked,
                Font = BodyFont
            };
        }

        public static RadioButton Radio(string text, int x, int y, bool isChecked = false)
        {
            text = Localization.T(text);
            return new ModernRadioButton
            {
                Text = text,
                Left = x,
                Top = y,
                AutoSize = true,
                Checked = isChecked,
                Font = BodyFont
                // BackColor set by ThemeManager (solid, parent-matched) - not transparent.
            };
        }

        public static Button Button(string text, int x, int y, int width, int height)
        {
            text = Localization.T(text);
            var b = new RoundedButton
            {
                Text = text,
                Left = x,
                Top = y,
                Width = width,
                Height = height,
                FlatStyle = FlatStyle.Flat,
                Font = BodyFont,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 1;
            return b;
        }

        public static Button PrimaryButton(string text, int x, int y, int width, int height, Theme theme)
        {
            var b = Button(text, x, y, width, height);
            b.Font = ButtonFont;
            // Not hardcoded white: a pale custom accent made every primary button's label
            // vanish. See Theme.OnAccent. Marked, so the theme pass a window runs after
            // building it keeps the accent instead of greying it like any other button.
            ThemeManager.MarkPrimary(b);
            ThemeManager.StylePrimary(b, theme);
            return b;
        }

        /// <summary>
        /// Widen a button until its own text fits, keeping at least <paramref name="minWidth"/>.
        ///
        /// A width that fits the English word is a width that clips the German one — "Unmute"
        /// becomes "Stummschaltung aufheben", "Re-check" becomes "Volver a comprobar". Measuring
        /// the text that is actually on the button is the only version of this that survives a
        /// translation, so it belongs here rather than being re-derived at each call site.
        /// </summary>
        public static Button FitToText(Button b, int minWidth = 0, int padding = 26)
        {
            if (b == null) { return null; }
            try
            {
                // Every caption it can show (LayoutFitter.DeclareCaptions), not only the one it has
                // now: sized for "Testing…" as well as "Test", or it clipped the moment its caption
                // changed. Measured as drawn — a RoundedButton shows an "&" as written.
                TextFormatFlags flags = b is RoundedButton || !b.UseMnemonic ? TextFormatFlags.NoPrefix : TextFormatFlags.Default;
                int needed = padding;
                foreach (string caption in LayoutFitter.CaptionsOf(b))
                {
                    needed = Math.Max(needed, TextRenderer.MeasureText(caption, b.Font, new Size(int.MaxValue, int.MaxValue), flags).Width + padding);
                }
                b.Width = Math.Max(minWidth, needed);
            }
            catch { }
            return b;
        }

        /// <summary>
        /// <see cref="FitToText"/> for a window that AutoScaleMode.Dpi scales AFTER this runs — a dialog's
        /// constructor, or an account screen built before it is brought to the window's scale. Such a
        /// layout is written in 100% (96-dpi) units, so the text is measured in them too.
        ///
        /// THE CONFLICT THIS FIXES. FitToText measures in the screen's pixels. On a 150% laptop that is
        /// 1.5× the units the dialog is written in, and the window then scales every size by 1.5 again:
        /// each button sized this way came out about 1.5× too wide for its caption, and the rows packed
        /// from both ends (OK / Cancel against Test / Copy test URL) closed in on each other. At 100% the
        /// two are identical.
        /// </summary>
        public static Button FitToText96(Button b, int minWidth = 0, int padding = 26)
        {
            if (b == null) { return null; }
            try
            {
                TextFormatFlags flags = b is RoundedButton || !b.UseMnemonic ? TextFormatFlags.NoPrefix : TextFormatFlags.Default;
                int needed = padding;
                foreach (string caption in LayoutFitter.CaptionsOf(b))
                {
                    needed = Math.Max(needed, Measure96(caption, b.Font, 0, flags).Width + padding);
                }
                b.Width = Math.Max(minWidth, needed);
            }
            catch { }
            return b;
        }

        /// <summary>
        /// Text's size in 100% (96-dpi) units, for a layout AutoScaleMode.Dpi scales after it is built: the
        /// screen-pixel measurement divided by the screen's scale (see <see cref="FitToText96"/>).
        /// <paramref name="width96"/> above 0 wraps the words at that width; 0 measures one line.
        /// </summary>
        internal static Size Measure96(string text, Font font, int width96 = 0, TextFormatFlags flags = TextFormatFlags.NoPrefix)
        {
            return MeasureIn(1f / BuildScale, text, font, width96, flags);
        }

        /// <summary>
        /// Text's size in a layout's own units, where one screen pixel is <paramref name="unitsPerPixel"/>
        /// of them: 1 / 1.5 while a window that will be scaled ×1.5 is still being built (its numbers are
        /// 100% ones), 1 once it has been scaled. <paramref name="width"/> above 0 wraps the words at that
        /// width, in the same units; 0 measures one line.
        /// </summary>
        internal static Size MeasureIn(float unitsPerPixel, string text, Font font, int width = 0, TextFormatFlags flags = TextFormatFlags.NoPrefix)
        {
            float u = unitsPerPixel > 0.05f ? unitsPerPixel : 1f;
            Size proposed = new Size(int.MaxValue, int.MaxValue);
            if (width > 0)
            {
                flags |= TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl;
                proposed = new Size(Math.Max(1, (int)Math.Round(width / u)), int.MaxValue);
            }
            Size real = TextRenderer.MeasureText(text ?? string.Empty, font, proposed, flags);
            if (u == 1f) { return real; }
            return new Size((int)Math.Ceiling(real.Width * u), (int)Math.Ceiling(real.Height * u));
        }

        /// <summary>
        /// The screen's scale while windows are built: 1 at 100%, 1.5 at 150%. Tempo is System-DPI-aware,
        /// so this is one number for the life of the process.
        /// </summary>
        internal static float BuildScale
        {
            get
            {
                if (_buildScale <= 0f)
                {
                    try
                    {
                        using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) { _buildScale = Math.Max(1f, g.DpiX / 96f); }
                    }
                    catch { _buildScale = 1f; }
                }
                return _buildScale;
            }
        }

        private static float _buildScale;

        /// <summary>
        /// Scales a window built in 100% numbers to the screen — everything in it, once, now. Call it at the
        /// END of the constructor, after the last control is added, and never inside SuspendLayout (the
        /// scaling would wait for the ResumeLayout).
        ///
        /// WinForms scales an AutoScaleMode.Dpi window when its AutoScaleDimensions are declared — at once,
        /// or at the ResumeLayout that ends a suspended block — and only the controls the window holds AT
        /// THAT MOMENT: nothing added afterwards is ever scaled. Every Tempo window declared them in its
        /// first lines and built its controls after, so on a 125% or 150% screen none was scaled at all —
        /// its text grew (fonts are points) and every box, row and button kept its 100% size. The main
        /// window declared them inside its first build step, which scaled the frame and the sidebar and
        /// none of the nine tabs. A 100% screen cannot show that, and the ×1.5 test could not either: it
        /// declared 64 dpi AFTER the constructor, which scales everything. Telling WinForms the screen is
        /// 144 dpi from the start shows it (scratchpad mainscan / dialogscan "fake150").
        ///
        /// Give such a window its CLIENT size, not its window size. The frame is not scaled with the rest
        /// — it is 16×39 px at 100% and 22×56 at 150% — so a window size written for 100% leaves 1.5 ×
        /// (160 − 56) = 156 px of room on a 150% screen where 1.5 × (160 − 39) = 181 was meant, and the
        /// buttons along the bottom of the key-capture dialog were cut off.
        /// </summary>
        internal static void ScaleToScreen(ContainerControl window)
        {
            if (window == null || window.IsDisposed) { return; }
            // Once: declared a second time, the window would be scaled a second time.
            if (ScaledWindows.TryGetValue(window, out _)) { return; }
            window.AutoScaleMode = AutoScaleMode.Dpi;
            window.AutoScaleDimensions = new SizeF(96F, 96F);
            ScaledWindows.AddOrUpdate(window, null);
            FitAutoSizedTicks(window);
        }

        /// <summary>
        /// Whether <paramref name="window"/> has been through <see cref="ScaleToScreen"/>: its numbers are the
        /// screen's pixels from then on, 100% ones before. Its window handle says nothing about it — the
        /// scaling now happens in the constructor, before there is one.
        /// </summary>
        internal static bool IsScaledToScreen(Control window) => window != null && ScaledWindows.TryGetValue(window, out _);

        /// <summary>
        /// What one of <paramref name="window"/>'s own 100% numbers is in its units of the moment: 1 while it
        /// is built, the screen's scale once <see cref="ScaleToScreen"/> has run — for code that lays it out
        /// again later (a re-fit on a resize, a section shown or hidden).
        /// </summary>
        internal static float ScaleOf(Control window) => IsScaledToScreen(window) ? BuildScale : 1f;

        /// <summary>How many of <paramref name="window"/>'s units one screen pixel of text is (for <see cref="MeasureIn"/>).</summary>
        internal static float UnitsPerPixel(Control window) => ScaleOf(window) / BuildScale;

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object> ScaledWindows =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Control, object>();

        /// <summary>
        /// Sets each auto-sized native tick box and option button to its own size again. The scaling sets
        /// every control to 1.5× its bounds, and one that had measured itself keeps that until its container
        /// happens to be laid out again: a box half as wide again as its words, its caption lower than its
        /// row. (Labels refuse any other size by themselves, and so do Tempo's own tick boxes — SelfSize.)
        /// </summary>
        private static void FitAutoSizedTicks(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if ((c is CheckBox || c is RadioButton) && SelfSize.Applies(c))
                {
                    try
                    {
                        Size p = c.GetPreferredSize(Size.Empty);
                        bool fromRight = (c.Anchor & (AnchorStyles.Left | AnchorStyles.Right)) == AnchorStyles.Right;
                        c.SetBounds(fromRight ? c.Right - p.Width : c.Left, c.Top, p.Width, p.Height);
                    }
                    catch { }
                }
                if (c.HasChildren) { FitAutoSizedTicks(c); }
            }
        }

        /// <summary>Right-align an auto-sized control so its right edge lands on <paramref name="rightEdge"/>.</summary>
        public static T RightAlign<T>(T c, int rightEdge) where T : Control
        {
            if (c != null) { c.Left = Math.Max(0, rightEdge - c.PreferredSize.Width); }
            return c;
        }

        public static GroupBox Group(string title, int x, int y, int width, int height)
        {
            return Group(title, x, y, width, height, CardIcon.None);
        }

        public static GroupBox Group(string title, int x, int y, int width, int height, CardIcon icon)
        {
            // Escape ampersands so they don't render as a mnemonic (which would
            // hide the '&' and underline/eat the next character in the caption).
            // CardGroupBox undoes this when it custom-draws the title.
            title = Localization.T(title).Replace("&", "&&");
            return new CardGroupBox
            {
                Text = title,
                Left = x,
                Top = y,
                Width = width,
                Height = height,
                Font = BoldFont,
                Icon = icon
            };
        }

        public static TextBox Text(int x, int y, int width, string value = "")
        {
            return new FlatTextBox
            {
                Left = x,
                Top = y,
                Width = width,
                Text = value ?? string.Empty,
                Font = BodyFont,
                BorderStyle = BorderStyle.FixedSingle
            };
        }

        public static Panel Surface(int x, int y, int width, int height, string tag = "surface2")
        {
            return new Panel
            {
                Left = x,
                Top = y,
                Width = width,
                Height = height,
                Tag = tag
            };
        }
    }
}
