using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// The startup safety note, as part of the main window (2026-11-08, the user: "it need into main form. not need second
    /// form"). It was a dialog of its own (OfficialSourceForm): a second window, modal, that held the whole of Tempo for up
    /// to ten seconds at every launch, opened over the window's own way-in animation, and cut its last line ("⚠ This copy
    /// is a test build…") behind its buttons. It is a card on the window:
    ///   • in the middle of it, over a frosted copy of the window (BlurBackdrop — 2026-11-12, the user: "it need center
    ///     screen and background blur"); the title bar above the frost stays live;
    ///   • the keyboard is its own while it is up (Got it has the focus once the window is active; Tab goes round its own
    ///     links and buttons) — the page under the frost takes no keys and no clicks;
    ///   • the ten seconds count only while the card is in view (the window up, nothing of another program's over it — the
    ///     window need not be the active one: a restart's often isn't) and the pointer is not on it, so it never runs out
    ///     unread behind another window, or while someone is reading it;
    ///   • Got it, the ✕, Esc or the countdown closes it; opening a link stops the countdown;
    ///   • it fades in and out with the window's cross-fade (MainForm.FadeChange).
    /// The words are the dialog's, so every translation is kept.
    /// </summary>
    public sealed class SafetyNoteCard : Panel
    {
        public const string WebsiteUrl = "https://justcamop555-pixel.github.io/Tempo/";
        public static string GitHubUrl => "https://github.com/" + UpdateChecker.Repository;

        private const int CountdownSeconds = 10;

        private Theme _theme;
        private readonly Label _title, _intro, _body, _verdict;
        private readonly LinkLabel _ghLink, _siteLink;
        private readonly RoundedButton _close;
        private readonly Button _ghButton, _siteButton, _ok;
        private readonly Timer _countdown;
        private int _secondsLeft = CountdownSeconds;
        private bool _dismissed;

        /// <summary>Raised once, as the card is closed: how ("Got it", "✕", "Esc", "after 10 s").</summary>
        public event EventHandler<string> Dismissed;

        /// <summary>Seconds the countdown still has, or 0 once stopped (a link was opened).</summary>
        public int SecondsLeft => _secondsLeft;

        private float Scale => DeviceDpi / 96f;
        private int Px(float v) => (int)Math.Round(v * Scale, MidpointRounding.AwayFromZero);

        public SafetyNoteCard(Theme theme)
        {
            _theme = theme ?? Theme.ForKind(ThemeKind.Dark);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw, true);
            Name = "safetyNote";
            AccessibleName = Localization.T("Quick safety note");
            AccessibleRole = AccessibleRole.Alert;
            Width = Px(392);

            _title = new Label { AutoSize = false, UseMnemonic = false, Font = new Font("Segoe UI", 13f, FontStyle.Bold), Text = Localization.T("Quick safety note") };
            _intro = new Label { AutoSize = false, UseMnemonic = false, Font = UiFactory.BodyFont, Text = Localization.T("Tempo is free and only officially published in two places:") };
            // The addresses themselves: never translated, set apart so they read as data, and clickable.
            _ghLink = MakeLink(GitHubUrl);
            _siteLink = MakeLink(WebsiteUrl);
            _body = new Label
            {
                AutoSize = false,
                UseMnemonic = false,
                Font = UiFactory.BodyFont,
                Text = Localization.T("If you downloaded this copy anywhere else, it may have been "
                       + "modified by someone other than the developer. Tempo checks itself — open "
                       + "Settings to see whether this copy matches the file published on GitHub.")
            };
            _verdict = new Label { AutoSize = false, UseMnemonic = false, Font = UiFactory.BoldFont };

            _close = new RoundedButton
            {
                Text = "✕",
                FlatStyle = FlatStyle.Flat,
                CornerRadius = 6,
                TabStop = false,
                Cursor = Cursors.Hand,
                AccessibleName = Localization.T("Close")
            };
            _close.FlatAppearance.BorderSize = 0;
            _close.Click += (s, e) => Dismiss("✕");

            _ghButton = UiFactory.Button("Open GitHub page", 0, 0, 160, 32);
            _siteButton = UiFactory.Button("Open website", 0, 0, 120, 32);
            _ghButton.Click += (s, e) => OpenLink(GitHubUrl);
            _siteButton.Click += (s, e) => OpenLink(WebsiteUrl);
            _ok = UiFactory.PrimaryButton(CountdownText(_secondsLeft), 0, 0, 200, 36, _theme);
            _ok.Click += (s, e) => Dismiss("Got it");

            Controls.AddRange(new Control[] { _title, _close, _intro, _ghLink, _siteLink, _body, _verdict, _ghButton, _siteButton, _ok });

            _countdown = new Timer { Interval = 1000 };
            _countdown.Tick += (s, e) => CountdownTick();

            ApplyTheme(_theme);
            LayoutCard();
        }

        private LinkLabel MakeLink(string url)
        {
            var link = new LinkLabel
            {
                AutoSize = false,
                UseMnemonic = false,
                Font = UiFactory.BodyFont,
                Text = "•  " + url,
                LinkBehavior = LinkBehavior.HoverUnderline,
                TabStop = true,
                AccessibleName = url
            };
            link.Links.Clear();
            link.Links.Add(3, url.Length, url);
            link.LinkClicked += (s, e) => OpenLink(url);
            return link;
        }

        /// <summary>The main button's caption, with the seconds left — through T() each time, so it stays translated.</summary>
        private static string CountdownText(int secondsLeft)
        {
            string got = Localization.T("Got it");
            return secondsLeft > 0 ? got + "  (" + secondsLeft + ")" : got;
        }

        /// <summary>Starts the countdown (called once the card is on the window).</summary>
        public void StartCountdown()
        {
            if (_dismissed || _secondsLeft <= 0) { return; }
            _countdown.Start();
        }

        private void CountdownTick()
        {
            if (_dismissed || IsDisposed) { _countdown.Stop(); return; }
            // Only while it can be read and isn't being read: on the screen, the pointer elsewhere.
            //
            // "On the screen" was "Tempo's window is the active one", and a Tempo started by a restart, an install or the
            // sign-in entry is very often NOT active: Windows shows the new window on top but keeps the focus where the
            // user was (the foreground lock — it stops new windows stealing typing). The card was in plain view and sat
            // at "Got it (10)" for good (the user's screenshot, 2026-11-11). It counts now while it is actually visible:
            // the window up and not minimised, and nothing of another program's over the card.
            string held = HeldBecause();
            if (held != null)
            {
                _heldTicks++;
                _heldWhy = held;
                return;
            }
            _secondsLeft--;
            if (_secondsLeft <= 0)
            {
                _countdown.Stop();
                Dismiss("after " + CountdownSeconds + " s");
                return;
            }
            _ok.Text = CountdownText(_secondsLeft);
        }

        /// <summary>Seconds the countdown has been held so far, and the latest reason (for the log and Live debug).</summary>
        public int HeldSeconds => _heldTicks;
        public string HeldWhy => _heldWhy;
        private int _heldTicks;
        private string _heldWhy;

        /// <summary>Why the countdown can't run this second — null when the card is in view and not being read.</summary>
        private string HeldBecause()
        {
            Form host = FindForm();
            if (host == null || !host.Visible || !IsHandleCreated || !Visible) { return "the window is not shown"; }
            if (host.WindowState == FormWindowState.Minimized) { return "the window is minimised"; }
            try
            {
                if (ClientRectangle.Contains(PointToClient(Cursor.Position))) { return "the pointer is on the card (being read)"; }
            }
            catch { }
            // Active or not, it counts when the card itself is on the screen: what lies at its middle and at its title
            // belongs to this window. Being the active window says nothing about that (an always-on-top window can lie
            // over the card of an active Tempo); and a cloaked window (still being revealed) is not hit, nor one behind.
            try
            {
                foreach (Point local in new[] { new Point(Width / 2, Height / 2), new Point(Width / 2, Px(22)) })
                {
                    Point screen = PointToScreen(local);
                    IntPtr at = WindowFromPoint(new POINT { X = screen.X, Y = screen.Y });
                    if (at == IntPtr.Zero || GetAncestor(at, GA_ROOT) != host.Handle) { return "another window is over it"; }
                }
                return null;
            }
            catch { return "its place on the screen can't be read"; }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT p);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        private const uint GA_ROOT = 2;

        private void OpenLink(string url)
        {
            // Reading it, clearly: the countdown stops, and Got it is left to the reader.
            _countdown.Stop();
            _secondsLeft = 0;
            _ok.Text = CountdownText(0);
            try { AppActivator.OpenUrl(url); }
            catch (Exception ex) { Logger.Warn("[Welcome] could not open " + url + ": " + ex.Message); }
        }

        /// <summary>Closes the card (once): the owner takes it off the window.</summary>
        public void Dismiss(string how)
        {
            if (_dismissed) { return; }
            _dismissed = true;
            _countdown.Stop();
            try { Dismissed?.Invoke(this, how); }
            catch (Exception ex) { Logger.Swallow("SafetyNoteCard.Dismissed", ex); }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Dismiss("Esc"); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>Got it takes the focus (the window gives the card the keyboard while it is up).</summary>
        public void FocusMain()
        {
            try
            {
                if (!_dismissed && !IsDisposed && _ok.CanFocus) { _ok.Focus(); }
            }
            catch (Exception ex) { Logger.Swallow("SafetyNoteCard.FocusMain", ex); }
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            // Tab and Shift+Tab go round the card's own links and buttons: past the last one the focus would go on into the
            // page hidden under the frost, where a key would act on something nobody can see.
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.Tab && (keyData & (Keys.Control | Keys.Alt)) == 0)
            {
                bool forward = (keyData & Keys.Shift) == 0;
                Control at = null;
                foreach (Control c in Controls) { if (c.ContainsFocus) { at = c; break; } }
                if (SelectNextControl(at, forward, tabStopOnly: true, nested: true, wrap: true)) { return true; }
            }
            return base.ProcessDialogKey(keyData);
        }

        private const int WM_MOUSEWHEEL = 0x020A, WM_MOUSEHWHEEL = 0x020E;

        protected override void WndProc(ref Message m)
        {
            // Turned over the card or anything on it (each passes the turn up to the card): nothing scrolls — the page lies
            // frosted under the note, and is not handed the turn (nor is the window, which would pass it on).
            if (m.Msg == WM_MOUSEWHEEL || m.Msg == WM_MOUSEHWHEEL)
            {
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        /// <summary>The colours of <paramref name="theme"/>, for the card and everything on it.</summary>
        public void ApplyTheme(Theme theme)
        {
            if (theme == null) { return; }
            _theme = theme;
            BackColor = theme.Surface;
            ForeColor = theme.Text;
            ThemeManager.Apply(this, theme);
            BackColor = theme.Surface;
            foreach (Control c in Controls)
            {
                if (c is Label && !(c is LinkLabel)) { c.BackColor = theme.Surface; }
            }
            _title.ForeColor = theme.AccentText;
            _intro.ForeColor = theme.Text;
            _body.ForeColor = theme.TextMuted;
            foreach (LinkLabel link in new[] { _ghLink, _siteLink })
            {
                link.BackColor = theme.Surface;
                link.LinkColor = theme.AccentText;
                link.ActiveLinkColor = theme.AccentText;
                link.VisitedLinkColor = theme.AccentText;
                link.ForeColor = theme.TextMuted;
            }
            string mine = ThisCopyLine(out Color mineColour, theme);
            _verdict.Text = mine ?? "";
            _verdict.ForeColor = mineColour;
            _verdict.Visible = mine != null;
            _close.BackColor = theme.Surface;
            _close.ForeColor = theme.TextMuted;
            _close.FlatAppearance.BorderSize = 0;   // the theme pass gives every button a border; the ✕ has none
            ThemeManager.MarkPrimary(_ok);
            ThemeManager.StylePrimary(_ok, theme);
            if (IsHandleCreated) { LayoutCard(); }
            Invalidate(true);
        }

        /// <summary>
        /// The "this copy …" line again, once the integrity check has an answer: it runs in the background at start-up, so
        /// the card usually opens before it knows. The dialog never updated, and being modal it was gone by then anyway.
        /// </summary>
        public void RefreshVerdict()
        {
            if (IsDisposed || _dismissed) { return; }
            string mine = ThisCopyLine(out Color mineColour, _theme);
            if (string.Equals(mine ?? "", _verdict.Text, StringComparison.Ordinal) && _verdict.Visible == (mine != null)) { return; }
            _verdict.Text = mine ?? "";
            _verdict.ForeColor = mineColour;
            _verdict.Visible = mine != null;
            LayoutCard();
            Invalidate(true);
        }

        /// <summary>Everything placed and the card sized to its words, at this window's scale.</summary>
        private void LayoutCard()
        {
            int pad = Px(18), inner = Width - pad * 2;
            int closeSize = Px(26);
            _close.SetBounds(Width - closeSize - Px(10), Px(12), closeSize, closeSize);

            int y = Px(16);
            y = Place(_title, pad, y, inner - closeSize - Px(6)) + Px(6);
            y = Place(_intro, pad, y, inner) + Px(4);
            y = Place(_ghLink, pad, y, inner) + Px(1);
            y = Place(_siteLink, pad, y, inner) + Px(8);
            y = Place(_body, pad, y, inner) + Px(8);
            if (_verdict.Visible) { y = Place(_verdict, pad, y, inner) + Px(10); }
            else { y += Px(4); }

            // The two links as buttons, each as wide as its own translated caption.
            int bh = Px(32);
            _ghButton.SetBounds(pad, y, Math.Min(inner, ButtonWidth(_ghButton)), bh);
            int siteW = Math.Min(inner, ButtonWidth(_siteButton));
            if (_ghButton.Right + Px(8) + siteW <= pad + inner)
            {
                _siteButton.SetBounds(_ghButton.Right + Px(8), y, siteW, bh);
                y += bh + Px(10);
            }
            else
            {
                // A long translation: one under the other rather than cut.
                y += bh + Px(6);
                _siteButton.SetBounds(pad, y, siteW, bh);
                y += bh + Px(10);
            }
            _ok.SetBounds(pad, y, inner, Px(36));
            y += Px(36) + Px(16);
            Height = y;
            UpdateRegion();
        }

        private int ButtonWidth(Button b)
        {
            Size need = TextRenderer.MeasureText(b.Text, b.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix);
            return Math.Max(Px(96), need.Width + Px(28));
        }

        /// <summary>Places a wrapped text control at its own height for <paramref name="width"/>; returns the y below it.</summary>
        private static int Place(Control c, int x, int y, int width)
        {
            Size need = TextRenderer.MeasureText(c.Text ?? "", c.Font, new Size(Math.Max(1, width), int.MaxValue),
                                                 TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);
            c.SetBounds(x, y, width, need.Height + 2);
            return y + need.Height + 2;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            LayoutCard();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateRegion();
        }

        // Rounded, so the page shows past its corners.
        private void UpdateRegion()
        {
            if (Width < 4 || Height < 4) { return; }
            using (GraphicsPath path = Rounded(new Rectangle(0, 0, Width, Height), Px(12)))
            {
                Region = new Region(path);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var fill = new SolidBrush(_theme.Surface)) { g.FillRectangle(fill, ClientRectangle); }
            // The accent along the top, as the dialog's strip was.
            using (var accent = new SolidBrush(_theme.Accent))
            {
                g.FillRectangle(accent, new Rectangle(0, 0, Width, Px(4)));
            }
            using (GraphicsPath path = Rounded(r, Px(12)))
            using (var pen = new Pen(Theme.Blend(_theme.Surface, _theme.Accent, 0.55), Math.Max(1f, Scale)))
            {
                g.DrawPath(pen, path);
            }
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Max(2, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _countdown.Stop();
                _countdown.Dispose();
                _title.Font.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// One line about the copy the user is actually running, or null when nothing is known yet. The integrity check runs
        /// in the background at start-up, so Unknown is a real and common answer — and inventing reassurance for a file
        /// nothing has checked would be worse than saying nothing.
        /// </summary>
        internal static string ThisCopyLine(out Color colour, Theme theme)
        {
            colour = theme.TextMuted;
            try
            {
                switch (IntegrityCheck.Verdict)
                {
                    case IntegrityVerdict.Genuine:
                        colour = theme.SuccessText;
                        return IntegrityCheck.OfficialId != null
                            ? Localization.F("✓ This copy matches GitHub's {0} release — Official ID {1}.",
                                             IntegrityCheck.OfficialTag, IntegrityCheck.OfficialId)
                            : Localization.T("✓ This copy matches the release published on GitHub.");
                    case IntegrityVerdict.TestBuild:
                        colour = theme.WarningText;
                        return Localization.F("⚠ This copy is a test build ({0}) — it did not come from either of those.", BuildInfo.Id);
                    case IntegrityVerdict.UnpackedModified:
                        colour = theme.DangerText;
                        return Localization.T("✗ The copy of Tempo running from your TEMP folder was changed. "
                                              + "Open Settings and press Repair.");
                    case IntegrityVerdict.ForeignCode:
                        colour = theme.DangerText;
                        return Localization.T("✗ Program files that are not Tempo's were loaded into it from its folder. "
                                              + "Open Settings › Updates & privacy › Details to see them.");
                    case IntegrityVerdict.Modified:
                    case IntegrityVerdict.Repackaged:
                    case IntegrityVerdict.UnknownRelease:
                        colour = theme.DangerText;
                        return Localization.T("✗ This copy does not match the official release. "
                                              + "Replace it with one of the downloads above.");
                    default:
                        return null;    // Unknown / Ok / Baselined / Damaged — say nothing here
                }
            }
            catch { return null; }
        }
    }
}
