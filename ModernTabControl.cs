using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A minimalist replacement skin for the standard <see cref="TabControl"/>:
    /// flat tabs in a single horizontal strip, no rounded "raised" look, and the
    /// selected tab is indicated by an accent-coloured underline rather than a
    /// raised bevel. Colours are driven by a <see cref="UI.Theme"/> applied with
    /// <see cref="ApplyTheme"/>.
    ///
    /// The control subclasses <see cref="TabControl"/> rather than reimplements
    /// it, so every existing <c>_tabs.TabPages.Add(page)</c> call continues to
    /// work unchanged.
    /// </summary>
    public sealed class ModernTabControl : TabControl
    {
        private Color _stripBackground = Color.FromArgb(18, 18, 24);
        private Color _tabBackground = Color.FromArgb(28, 28, 38);
        private Color _selectedTabBackground = Color.FromArgb(38, 38, 52);
        private Color _accent = Color.FromArgb(99, 102, 241);
        private Color _text = Color.FromArgb(226, 232, 240);
        private Color _textMuted = Color.FromArgb(120, 132, 152);

        public ModernTabControl()
        {
            DrawMode = TabDrawMode.OwnerDrawFixed;
            SizeMode = TabSizeMode.Fixed;
            ItemSize = new Size(130, 38);
            Alignment = TabAlignment.Top;
            Appearance = TabAppearance.Normal;
            Padding = new Point(0, 0);
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);

            // Reduce flicker on the bits we own.
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
        }

        /// <summary>Applies the supplied theme's colours to the tab strip.</summary>
        public void ApplyTheme(Theme theme)
        {
            if (theme == null)
            {
                return;
            }

            _stripBackground = theme.Background;
            _tabBackground = theme.Surface;
            _selectedTabBackground = theme.Surface2;
            _accent = theme.Accent;
            _text = theme.Text;
            _textMuted = theme.TextMuted;

            BackColor = theme.Background;
            ForeColor = theme.Text;

            foreach (TabPage page in TabPages)
            {
                page.BackColor = theme.Background;
                page.ForeColor = theme.Text;
            }

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;

            // Paint the whole strip in the background colour first so the gaps
            // between tabs and the area to the right are filled cleanly.
            using (var fill = new SolidBrush(_stripBackground))
            {
                g.FillRectangle(fill, ClientRectangle);
            }

            // Draw each tab.
            for (int i = 0; i < TabCount; i++)
            {
                DrawTab(g, i);
            }
        }

        // OnDrawItem is still fired by the runtime in OwnerDrawFixed mode, but we
        // do the drawing in OnPaint above so the inter-tab background is also
        // covered. This handler is left empty.
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            // Intentionally blank — see OnPaint.
        }

        private void DrawTab(Graphics g, int index)
        {
            if (index < 0 || index >= TabPages.Count)
            {
                return;
            }

            Rectangle bounds = GetTabRect(index);
            bool selected = index == SelectedIndex;

            using (var fill = new SolidBrush(selected ? _selectedTabBackground : _tabBackground))
            {
                g.FillRectangle(fill, bounds);
            }

            // Accent underline for the selected tab.
            if (selected)
            {
                int barHeight = 3;
                Rectangle bar = new Rectangle(
                    bounds.Left + 6,
                    bounds.Bottom - barHeight,
                    bounds.Width - 12,
                    barHeight);

                using (var accent = new SolidBrush(_accent))
                {
                    g.FillRectangle(accent, bar);
                }
            }

            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (var brush = new SolidBrush(selected ? _text : _textMuted))
            using (var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap,
                Trimming = StringTrimming.EllipsisCharacter
            })
            {
                g.DrawString(TabPages[index].Text, Font, brush, bounds, format);
            }
        }
    }
}
