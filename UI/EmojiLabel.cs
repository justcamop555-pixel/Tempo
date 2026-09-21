using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A Label that keeps an emoji's colours. Windows' own label painting draws emoji as flat
    /// silhouettes in the text colour — black on a light theme — so a label whose text holds
    /// one is drawn through <see cref="ColorEmoji"/> instead. Plain text, an image, right-to-left
    /// text, or no colour renderer: painted exactly as a Label always was.
    /// </summary>
    public class EmojiLabel : Label
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            string text = Text;
            if (Image != null || RightToLeft == RightToLeft.Yes || !ColorEmoji.Contains(text) || !ColorEmoji.Available)
            {
                base.OnPaint(e);
                return;
            }

            var face = new Rectangle(Padding.Left, Padding.Top,
                Math.Max(0, ClientSize.Width - Padding.Horizontal),
                Math.Max(0, ClientSize.Height - Padding.Vertical));

            // The same flags a Label hands TextRenderer, so layout and wrapping decisions match.
            TextFormatFlags flags = AlignmentFlags(TextAlign) | TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl;
            if (AutoEllipsis) { flags |= TextFormatFlags.EndEllipsis; }
            if (!UseMnemonic) { flags |= TextFormatFlags.NoPrefix; }
            else if (!ShowKeyboardCues) { flags |= TextFormatFlags.HidePrefix; }

            ColorEmoji.DrawText(e.Graphics, text, Font, face, Enabled ? ForeColor : SystemColors.GrayText, flags);
        }

        private static TextFormatFlags AlignmentFlags(ContentAlignment align)
        {
            switch (align)
            {
                case ContentAlignment.TopCenter: return TextFormatFlags.Top | TextFormatFlags.HorizontalCenter;
                case ContentAlignment.TopRight: return TextFormatFlags.Top | TextFormatFlags.Right;
                case ContentAlignment.MiddleLeft: return TextFormatFlags.VerticalCenter | TextFormatFlags.Left;
                case ContentAlignment.MiddleCenter: return TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter;
                case ContentAlignment.MiddleRight: return TextFormatFlags.VerticalCenter | TextFormatFlags.Right;
                case ContentAlignment.BottomLeft: return TextFormatFlags.Bottom | TextFormatFlags.Left;
                case ContentAlignment.BottomCenter: return TextFormatFlags.Bottom | TextFormatFlags.HorizontalCenter;
                case ContentAlignment.BottomRight: return TextFormatFlags.Bottom | TextFormatFlags.Right;
                default: return TextFormatFlags.Top | TextFormatFlags.Left;
            }
        }
    }
}
