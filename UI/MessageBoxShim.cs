using System.Windows.Forms;
using WinForms = System.Windows.Forms;

namespace AutoClicker
{
    /// <summary>
    /// Tempo's MessageBox. Every <c>MessageBox.Show(...)</c> in this project resolves to
    /// THIS type, not <see cref="System.Windows.Forms.MessageBox"/>, and shows the themed,
    /// translated <see cref="UI.TempoMessageForm"/> instead of a system box.
    ///
    /// WHY IT IS DONE THIS WAY. There were 109 places that pop a message — 41 direct calls
    /// plus 68 through ShowInfo/ShowWarning — and every one produced a system-grey box in
    /// an app that draws its own chrome, always in English because a MessageBox cannot be
    /// translated. Editing 109 call sites would have churned a dozen files and left the
    /// next new call site to make the same mistake again. One shim fixes all of them at
    /// once, and keeps fixing them.
    ///
    /// HOW IT WORKS, because it is not obvious: C# resolves a type in an ENCLOSING
    /// NAMESPACE before one pulled in by a `using`. This class sits in the root
    /// <c>AutoClicker</c> namespace, so from <c>AutoClicker.UI</c>, <c>AutoClicker.Utils</c>
    /// and the rest it wins over the <c>System.Windows.Forms</c> import — without any file
    /// needing to change.
    ///
    /// TO GET THE REAL ONE, say so explicitly: <c>System.Windows.Forms.MessageBox.Show(…)</c>.
    /// TempoMessageForm itself does exactly that as its last-resort fallback, which is why
    /// a failure inside the themed dialog can still show the user their message.
    /// </summary>
    internal static class MessageBox
    {
        internal static DialogResult Show(IWin32Window owner, string text, string caption,
            MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return UI.TempoMessageForm.Show(owner, text, caption, buttons, icon,
                                            MessageBoxDefaultButton.Button1);
        }

        internal static DialogResult Show(IWin32Window owner, string text, string caption,
            MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
        {
            return UI.TempoMessageForm.Show(owner, text, caption, buttons, icon, defaultButton);
        }

        internal static DialogResult Show(string text, string caption,
            MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return UI.TempoMessageForm.Show(null, text, caption, buttons, icon,
                                            MessageBoxDefaultButton.Button1);
        }

        internal static DialogResult Show(string text, string caption,
            MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
        {
            return UI.TempoMessageForm.Show(null, text, caption, buttons, icon, defaultButton);
        }

        internal static DialogResult Show(string text, string caption, MessageBoxButtons buttons)
        {
            return UI.TempoMessageForm.Show(null, text, caption, buttons,
                                            MessageBoxIcon.None, MessageBoxDefaultButton.Button1);
        }

        internal static DialogResult Show(string text, string caption)
        {
            return UI.TempoMessageForm.Show(null, text, caption, MessageBoxButtons.OK,
                                            MessageBoxIcon.None, MessageBoxDefaultButton.Button1);
        }

        internal static DialogResult Show(string text)
        {
            return UI.TempoMessageForm.Show(null, text, "Tempo", MessageBoxButtons.OK,
                                            MessageBoxIcon.None, MessageBoxDefaultButton.Button1);
        }
    }
}
