using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// Notices when the window stops answering and writes it down: for how long, and what had just been
    /// clicked or pressed — so a "Tempo is not responding when I …" report arrives with its answer in the
    /// log instead of needing the person's PC to reproduce it.
    ///
    /// Why it exists: "not responding for some users, the first time they click …" could not be seen on
    /// this machine. The causes found by reading and measuring — the install's copy, the vault's password
    /// hashing, the Roblox client check, ShellExecute's antivirus/SmartScreen wait — were each slow only on
    /// someone else's PC (a slower disk, a CPU without SHA extensions, a cold cache). The next one will be
    /// too; this is how it gets found.
    ///
    /// How: a background thread posts a no-op to the window every half second and waits for it. An answer
    /// that takes longer than <see cref="StallMs"/> is a stall, logged once the window answers. A message
    /// filter notes the last click or key press — its target's caption or name only, never what was typed
    /// (a text box is named, not read: it may be the master password). It costs one posted message per
    /// half second and a compare per message.
    /// </summary>
    internal static class UiStallWatch
    {
        private const int CheckEveryMs = 500;

        /// <summary>A pause a person notices. Windows calls a window "Not responding" after 5 s.</summary>
        private const int StallMs = 1500;

        private static Thread _thread;
        private static Control _owner;
        private static volatile string _lastInput = "";
        private static long _lastInputAtMs;
        private static readonly Stopwatch Clock = Stopwatch.StartNew();

        // ONE event for every ping, set by the posted no-op. Each BeginInvoke result's own
        // AsyncWaitHandle would be a new kernel event every half second, left to the finalizer.
        private static readonly AutoResetEvent Answered = new AutoResetEvent(false);
        private static readonly Action Ping = () => Answered.Set();

        /// <summary>Starts watching <paramref name="owner"/> (the main window). Idempotent.</summary>
        internal static void Start(Control owner)
        {
            if (_thread != null || owner == null) { return; }
            try
            {
                _owner = owner;
                Application.AddMessageFilter(new InputNoter());
                _thread = new Thread(Loop) { IsBackground = true, Name = "UiStallWatch", Priority = ThreadPriority.BelowNormal };
                _thread.Start();
            }
            catch (Exception ex) { Logger.Swallow("UiStallWatch.Start", ex); }
        }

        private static void Loop()
        {
            while (true)
            {
                Thread.Sleep(CheckEveryMs);
                Control owner = _owner;
                if (owner == null || owner.IsDisposed) { return; }
                if (!owner.IsHandleCreated) { continue; }
                try
                {
                    long sent = Clock.ElapsedMilliseconds;
                    owner.BeginInvoke(Ping);
                    if (Answered.WaitOne(StallMs)) { continue; }

                    // Stalled. Note what came just before it, then wait for the window to come back.
                    // Every ping is answered before the next is posted, so no late answer is left over.
                    string cause = _lastInput;
                    long causeAge = sent - Interlocked.Read(ref _lastInputAtMs);
                    Answered.WaitOne();
                    long ms = Clock.ElapsedMilliseconds - sent;
                    // Minutes on end is a PC that slept or a debugger, not something anyone sat through.
                    if (ms > 10 * 60 * 1000) { continue; }
                    Logger.Warn("[stall] the window did not respond for " + (ms / 1000.0).ToString("0.0") + " s"
                                + (cause.Length > 0 && causeAge >= 0 && causeAge < 10000
                                    ? " — just after " + cause + "." : " (no click or key press just before it)."));
                }
                catch (ObjectDisposedException) { return; }
                catch (InvalidOperationException) { /* the window is being made or closed */ }
                catch (Exception ex)
                {
                    Logger.Swallow("UiStallWatch", ex);
                    return;
                }
            }
        }

        /// <summary>Remembers the last click or key press and what it landed on. Never filters anything.</summary>
        private sealed class InputNoter : IMessageFilter
        {
            private const int WM_KEYDOWN = 0x0100, WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205;

            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != WM_LBUTTONUP && m.Msg != WM_RBUTTONUP && m.Msg != WM_KEYDOWN) { return false; }
                try
                {
                    Control c = Control.FromHandle(m.HWnd);
                    string what = m.Msg == WM_KEYDOWN ? KeyName((Keys)(int)m.WParam) : "a click";
                    _lastInput = what + " on " + Describe(c);
                    Interlocked.Exchange(ref _lastInputAtMs, Clock.ElapsedMilliseconds);
                }
                catch { }
                return false;
            }

            // Only keys that ACT (Enter, Esc, F-keys…): a letter or digit may be part of a password.
            private static string KeyName(Keys k)
            {
                switch (k)
                {
                    case Keys.Enter: return "Enter";
                    case Keys.Escape: return "Esc";
                    case Keys.Space: return "Space";
                    case Keys.Delete: return "Delete";
                    case Keys.Tab: return "Tab";
                    default: return k >= Keys.F1 && k <= Keys.F24 ? k.ToString() : "a key";
                }
            }

            private static string Describe(Control c)
            {
                if (c == null) { return "something outside Tempo's controls"; }
                string where = "";
                Form f = c.FindForm();
                if (f != null && !ReferenceEquals(f, c)) { where = " in \"" + Short(f.Text) + "\""; }
                // A text box's Text is what was typed into it — never logged.
                if (c is TextBoxBase || c is ComboBox)
                {
                    return "a text field" + (string.IsNullOrEmpty(c.AccessibleName) ? "" : " (" + Short(c.AccessibleName) + ")") + where;
                }
                string text = Short(c.Text);
                if (text.Length > 0) { return "\"" + text + "\"" + where; }
                return c.GetType().Name + (string.IsNullOrEmpty(c.Name) ? "" : " " + c.Name) + where;
            }

            private static string Short(string s)
            {
                if (string.IsNullOrEmpty(s)) { return ""; }
                s = s.Replace("\r", " ").Replace("\n", " ").Trim();
                return s.Length <= 48 ? s : s.Substring(0, 46) + "…";
            }
        }
    }
}
