using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// A <see cref="TabControl"/> used purely as a page host. Its native tab header
    /// is hidden — navigation happens through the left sidebar — so each page fills
    /// the whole control. Subclassing TabControl (rather than reimplementing it)
    /// keeps every existing <c>_tabs.TabPages.Add(page)</c> and
    /// <c>_tabs.SelectedIndex</c> call working unchanged.
    /// </summary>
    public sealed class ModernTabControl : TabControl
    {
        // TCM_ADJUSTRECT: when intercepted and answered with 1, the control stops
        // reserving space for the tab-header strip, so the page fills the whole
        // control and the header tabs end up covered by the (opaque) page.
        private const int TCM_ADJUSTRECT = 0x1328;

        public ModernTabControl()
        {
            SizeMode = TabSizeMode.Fixed;
            Appearance = TabAppearance.Normal;
            ItemSize = new Size(0, 1);
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer,
                true);
        }

        /// <summary>Applies the supplied theme's colours to the page host.</summary>
        public void ApplyTheme(Theme theme)
        {
            if (theme == null)
            {
                return;
            }

            BackColor = theme.Background;
            ForeColor = theme.Text;

            foreach (TabPage page in TabPages)
            {
                page.BackColor = theme.Background;
                page.ForeColor = theme.Text;
            }

            Invalidate();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == TCM_ADJUSTRECT && !DesignMode)
            {
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
        }
    }
}
