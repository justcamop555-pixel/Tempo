using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using AutoClicker.Models;

namespace AutoClicker.UI
{
    /// <summary>
    /// Live debug window: a real-time view of what Tempo's caption stack is doing
    /// RIGHT NOW (engine, model, pace, backlog, devices, speaker verdicts) plus the
    /// live event stream every subsystem already reports (device changes, language
    /// locks, watchdog fires, GPU/CPU engine choice, auto-recoveries…).
    ///
    /// Everything stays on the PC: the view reads the in-process log ring; nothing
    /// is sent anywhere unless the user presses Copy/Save themselves. Built for
    /// "what is it doing?" moments and for attaching context to bug reports.
    /// </summary>
    public sealed class DebugForm : Form
    {
        private Theme _titleTheme;

        /// <summary>
        /// Themes the scroll bars once every child control actually exists.
        ///
        /// SetWindowTheme needs a real window handle, and a Form's own HandleCreated
        /// fires before its children have theirs — so doing this any earlier silently
        /// themed nothing. The stats and log panes here are RichTextBoxes, which the
        /// main form's theming pass never covered either, so this window showed LIGHT
        /// scroll bars right beside the main window's dark ones.
        /// </summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                bool dark = _titleTheme == null || _titleTheme.Background.GetBrightness() < 0.5f;
                NativeChrome.ApplyAllScrollbarThemes(this, dark);
            }
            catch { }
        }

        /// <summary>Paints this window's title bar in the theme (Windows 11+).</summary>
        private void ApplyTitleBarTheme()
        {
            if (_titleTheme == null || !IsHandleCreated)
            {
                return;
            }
            try
            {
                bool dark = _titleTheme.Background.GetBrightness() < 0.5f;
                NativeChrome.SetTitleBarDark(Handle, dark);
                NativeChrome.TintTitleBar(Handle, _titleTheme.Surface, _titleTheme.Text, _titleTheme.Border);
                // Scroll bars are themed in OnShown, NOT here: this runs on the FORM's
                // HandleCreated, when the child controls have no handles yet and
                // SetWindowTheme has nothing to act on.
            }
            catch { /* older Windows — the default bar stays */ }
        }

        private readonly Func<string> _statsProvider;
        private readonly RichTextBox _stats;
        private readonly RichTextBox _log;
        private readonly TextBox _filter;
        private readonly CheckBox _pause;
        private readonly CheckBox _problemsOnly;
        private readonly CheckBox _trace;
        private readonly CheckBox _moveTrace;
        private readonly CheckBox _onTop;
        private readonly System.Windows.Forms.Timer _statsTimer;
        private readonly Action<string> _onLine;
        private int _shownLines;
        private Color _infoColor;
        private Color _warnColor;
        private Color _errorColor;
        private Color _goodColor;
        private Color _statsLabelColor;

        // Distinct true-colour hues per subsystem, so the event stream can be scanned
        // at a glance: which lines are captions, which are movement, which are audio,
        // input, or the run itself. Errors and warnings still override everything.
        // TWO palettes: the pale set reads well on dark backgrounds but washes out
        // on white (Match-Windows light theme), so a saturated dark set is picked
        // when the theme background is light. System also moved off pale-cyan — it
        // was nearly the same hue as captions' sky blue.
        private Color CapColor;    // captions / speech — sky blue
        private Color MoveColor;   // camera movement — lavender
        private Color AudioColor;  // audio / voice / face — mint
        private Color InputColor;  // keyboard / hotkeys / hooks — gold
        private Color ClickColor;  // clicker engine / macros — soft orange
        private Color ScriptColor; // Python script steps — lime
        private Color SysColor;    // startup / settings / update — orchid
        private Color TraceColor;  // per-chunk traces — dim grey
        private readonly CheckBox _colourByKind;   // toggles category colouring on/off
        private RichTextBox _legend;

        /// <summary>A subsystem category a log line belongs to, for colouring.</summary>
        private enum LineKind { Info, Error, Warn, Captions, Movement, Audio, Input, Clicker, Script, System, Trace }
        // Last stats text rendered. The panel refreshes twice a second; rewriting a
        // RichTextBox that hasn't changed would throw away the user's selection (and
        // flicker) for nothing.
        private string _lastStats = "";

        private const int WM_SETREDRAW = 0x000B;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref Point lParam);

        // Rich-edit scroll position, so a refresh can put the view back where it was.
        private const int EM_GETSCROLLPOS = 0x04DD;   // WM_USER + 221
        private const int EM_SETSCROLLPOS = 0x04DE;   // WM_USER + 222

        /// <summary>
        /// Freezes/unfreezes painting on a control. The stats panel is rebuilt line by
        /// line with per-line colours; without this the user would watch it flicker
        /// twice a second.
        /// </summary>
        private static void SetRedraw(Control c, bool on)
        {
            try
            {
                if (c.IsHandleCreated)
                {
                    SendMessage(c.Handle, WM_SETREDRAW, (IntPtr)(on ? 1 : 0), IntPtr.Zero);
                    if (on) { c.Invalidate(); }
                }
            }
            catch { }
        }

        /// <summary>
        /// Picks the subsystem colour set for a theme's background.
        ///
        /// The pale hues read perfectly on dark themes but wash out on white (Match
        /// Windows → light), so a saturated dark-on-light set is used there. Same hue
        /// meanings either way. Extracted from the constructor so a LIVE theme change
        /// can recompute it — see <see cref="ApplyTheme"/>.
        /// </summary>
        private void ApplySemanticPalette(Theme theme)
        {
            if (theme.Background.GetBrightness() > 0.5f)
            {
                _warnColor = Color.FromArgb(178, 104, 0);
                _errorColor = Color.FromArgb(196, 32, 32);
                _goodColor = Color.FromArgb(22, 130, 70);
                CapColor = Color.FromArgb(0, 100, 190);     // captions — deep sky blue
                MoveColor = Color.FromArgb(112, 66, 200);   // movement — violet
                AudioColor = Color.FromArgb(12, 128, 84);   // audio — deep mint
                InputColor = Color.FromArgb(150, 108, 0);   // input — dark gold
                ClickColor = Color.FromArgb(180, 78, 22);   // clicker — burnt orange
                ScriptColor = Color.FromArgb(74, 130, 20);  // scripts — olive
                SysColor = Color.FromArgb(160, 44, 128);    // system — orchid
                TraceColor = Color.FromArgb(130, 136, 148); // traces — grey
            }
            else
            {
                _warnColor = Color.FromArgb(255, 190, 90);
                _errorColor = Color.FromArgb(255, 120, 120);
                _goodColor = Color.FromArgb(120, 230, 160);
                CapColor = Color.FromArgb(120, 200, 255);   // captions — sky blue
                MoveColor = Color.FromArgb(190, 165, 255);  // movement — lavender
                AudioColor = Color.FromArgb(120, 230, 175); // audio — mint
                InputColor = Color.FromArgb(240, 205, 120); // input — gold
                ClickColor = Color.FromArgb(255, 170, 120); // clicker — soft orange
                ScriptColor = Color.FromArgb(170, 230, 110); // scripts — lime. Sits in the
                                                            // one wide gap left in the hue
                                                            // circle (~88°): 45° off gold
                                                            // and 62° off the audio mint.
                SysColor = Color.FromArgb(235, 150, 210);   // system — orchid (was pale
                                                            // cyan, colliding with captions)
                TraceColor = Color.FromArgb(120, 128, 145); // traces — dim grey
            }
        }

        /// <summary>
        /// Re-themes this window in place.
        ///
        /// Live debug took its theme at construction and had no way to be told about a
        /// change, so switching theme with this window open left it in the OLD colours —
        /// including the light/dark semantic palette, which is picked from the background
        /// brightness and could end up pale-on-white and unreadable after a switch to a
        /// light theme. The main window re-themes every other long-lived window it owns;
        /// this one was simply missing from that list.
        /// </summary>
        public void ApplyTheme(Theme theme)
        {
            if (theme == null || IsDisposed) { return; }
            try
            {
                _titleTheme = theme;
                ApplySemanticPalette(theme);
                _infoColor = theme.Text;
                _statsLabelColor = theme.TextMuted;

                BackColor = theme.Background;
                ForeColor = theme.Text;
                ThemeManager.Apply(this, theme);

                // ThemeManager doesn't know about RichTextBox, and these two carry the
                // panes' own surface colour.
                if (_stats != null) { _stats.BackColor = theme.Surface; _stats.ForeColor = theme.Text; }
                if (_log != null) { _log.BackColor = theme.Surface; _log.ForeColor = theme.Text; }
                if (_legend != null) { _legend.BackColor = theme.Surface; _legend.ForeColor = theme.TextMuted; }

                ApplyTitleBarTheme();
                try { NativeChrome.ApplyAllScrollbarThemes(this, theme.Background.GetBrightness() < 0.5f); }
                catch { }

                // Both panes hold text already coloured with the OLD palette, so repaint
                // their contents rather than leaving stale hues behind.
                BuildLegend();
                _lastStats = "";      // force the next tick to redraw with the new colours
                ReloadFromRing();
                RefreshStats();
                Invalidate(true);
            }
            catch (Exception ex) { Utils.Logger.Swallow("DebugForm.ApplyTheme", ex); }
        }

        public DebugForm(Theme theme, Func<string> statsProvider)
        {
            _statsProvider = statsProvider;
            theme = theme ?? Theme.ForKind(ThemeKind.Dark);

            ApplySemanticPalette(theme);

            Text = Utils.Localization.T("Tempo — Live debug");
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(880, 640);
            MinimumSize = new Size(700, 420);
            BackColor = theme.Background;
            ForeColor = theme.Text;
            Font = new Font("Segoe UI", 9f);

            // Match the main window: paint the title bar in the theme rather than
            // leaving a black system bar above a themed window.
            _titleTheme = theme;
            HandleCreated += (s, e) => ApplyTitleBarTheme();

            _statsLabelColor = theme.TextMuted;
            // A RichTextBox, not a Label: the header now carries warnings that need to
            // stand out in colour, and — the reason users kept asking — its text can be
            // SELECTED and copied. A Label's text cannot.
            _stats = new RichTextBox
            {
                Dock = DockStyle.Top,
                Height = 196,
                ReadOnly = true,
                WordWrap = false,
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 9.5f),
                ForeColor = theme.Text,
                BackColor = theme.Surface,
                Text = "…"
            };
            Controls.Add(_stats);

            // Colour legend for the event stream below. A RichTextBox so each category
            // word can be shown in its own colour — a plain Label can't do multi-colour.
            _legend = new RichTextBox
            {
                Dock = DockStyle.Top,
                Height = 22,
                ReadOnly = true,
                WordWrap = false,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 8.25f),
                BackColor = theme.Surface,
                ForeColor = theme.TextMuted,
                ScrollBars = RichTextBoxScrollBars.None
            };
            Controls.Add(_legend);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 72, BackColor = theme.Surface };
            Controls.Add(bottom);

            _filter = new TextBox
            {
                Left = 10, Top = 8, Width = 220,
                BackColor = theme.InputBackground, ForeColor = theme.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            _filterDebounce = new System.Windows.Forms.Timer { Interval = 180 };
            _filterDebounce.Tick += Safe("filterDebounce", () =>
            {
                _filterDebounce.Stop();
                ReloadFromRing();
            });
            _filter.TextChanged += Safe("filter", () =>
            {
                _filterDebounce.Stop();      // restart the countdown on each keystroke
                _filterDebounce.Start();
            });
            bottom.Controls.Add(_filter);
            var filterHint = new Label
            {
                Left = 236, Top = 12, AutoSize = true,
                Text = Utils.Localization.T("filter"),
                ForeColor = theme.TextMuted, BackColor = Color.Transparent
            };
            bottom.Controls.Add(filterHint);

            _pause = new CheckBox
            {
                Left = 280, Top = 9, AutoSize = true, Text = Utils.Localization.T("Pause"),
                ForeColor = theme.Text, BackColor = Color.Transparent
            };
            // Catching up on UNPAUSE is the whole point, and it was missing: FlushPending
            // drains the queue before it checks Pause, so everything logged while paused
            // was thrown away, and with no handler here nothing ever rebuilt the view.
            // Un-pausing left a silent, permanent hole in the stream — the events you
            // paused to go and read were the ones you lost. The ring still has them.
            _pause.CheckedChanged += Safe("pause", () =>
            {
                if (!_pause.Checked) { ReloadFromRing(); }
            });
            bottom.Controls.Add(_pause);

            _problemsOnly = new CheckBox
            {
                Left = 348, Top = 9, AutoSize = true, Text = Utils.Localization.T("Problems only"),
                ForeColor = theme.Text, BackColor = Color.Transparent
            };
            _problemsOnly.CheckedChanged += Safe("problemsOnly", ReloadFromRing);
            bottom.Controls.Add(_problemsOnly);

            // Per-chunk pipeline trace: one [Trace] line per transcription pass
            // (chunk → inference time, real-time factor, backlog, gain, the text
            // shown). Lives in the transcriber as a static flag so no plumbing is
            // needed; switched off again when this window closes so the log ring
            // isn't quietly flooded afterwards.
            _trace = new CheckBox
            {
                Left = 466, Top = 9, AutoSize = true, Text = Utils.Localization.T("Caption trace"),
                ForeColor = theme.Text, BackColor = Color.Transparent
            };
            _trace.CheckedChanged += Safe("captionTrace",
                () => Utils.TempoTranscriber.VerboseTrace = _trace.Checked);
            bottom.Controls.Add(_trace);

            // The movement equivalent: a [Move] line on every key change plus a 1 Hz
            // heartbeat, showing yaw, heading, and "you press W → Tempo sends A". This
            // is the only practical way to see WHY the character went the wrong way.
            _moveTrace = new CheckBox
            {
                Left = 576, Top = 9, AutoSize = true, Text = Utils.Localization.T("Movement trace"),
                ForeColor = theme.Text, BackColor = Color.Transparent
            };
            _moveTrace.CheckedChanged += Safe("movementTrace",
                () => Engine.CameraRelativeMovement.VerboseTrace = _moveTrace.Checked);
            bottom.Controls.Add(_moveTrace);

            // Without this the window sits BEHIND the game you are trying to debug,
            // which made it useless for exactly the case it is most needed in.
            _onTop = new CheckBox
            {
                Left = 706, Top = 9, AutoSize = true, Text = Utils.Localization.T("Always on top"),
                ForeColor = theme.Text, BackColor = Color.Transparent
            };
            _onTop.CheckedChanged += Safe("alwaysOnTop", () => TopMost = _onTop.Checked);
            bottom.Controls.Add(_onTop);

            var copyBtn = MakeButton(theme, "Copy", 10);
            copyBtn.Top = 40;
            copyBtn.Click += Safe("copy", () =>
            {
                // The clipboard genuinely refuses sometimes — another app can hold it
                // open — and this used to swallow that silently: you pressed Copy,
                // nothing happened, and nothing said so. On a window whose whole purpose
                // is getting this text OUT to a bug report, that is the worst place to
                // fail quietly. Save… still works when the clipboard will not.
                try
                {
                    Clipboard.SetText(BuildExportText());
                    FlashButton(copyBtn, Utils.Localization.T("Copied  ✓"));
                }
                catch (Exception ex)
                {
                    Utils.Logger.Warn("[Debug] clipboard copy failed: " + ex.Message);
                    MessageBox.Show(this,
                        Utils.Localization.F("Couldn't copy to the clipboard — another app may be holding it.\n\n"
                            + "Use Save… instead.\n\n{0}", ex.Message),
                        "Tempo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            });
            bottom.Controls.Add(copyBtn);

            var saveBtn = MakeButton(theme, "Save…", 96);
            saveBtn.Top = 40;
            saveBtn.Click += Safe("save", SaveToFile);
            bottom.Controls.Add(saveBtn);

            var clearBtn = MakeButton(theme, "Clear view", 182);
            clearBtn.Top = 40;
            clearBtn.Click += Safe("clear", () => { _log.Clear(); _shownLines = 0; });
            bottom.Controls.Add(clearBtn);

            var hint = new Label
            {
                Left = 272, Top = 45, AutoSize = true,
                Text = Utils.Localization.T("Copy/Save include the stats above + the recent events."),
                ForeColor = theme.TextMuted, BackColor = Color.Transparent
            };
            bottom.Controls.Add(hint);

            // Colour each event by which subsystem it came from (captions, movement,
            // audio, input, …) so the stream can be read at a glance. On by default;
            // untick for the plain severity-only colouring. The legend strip above the
            // log shows what each colour means.
            _colourByKind = new CheckBox
            {
                Left = 640, Top = 42, AutoSize = true,
                Text = Utils.Localization.T("Colour by category"), Checked = true,
                ForeColor = theme.Text, BackColor = Color.Transparent
            };
            _colourByKind.CheckedChanged += Safe("colourByKind", () =>
            {
                BuildLegend();
                ReloadFromRing();
            });
            bottom.Controls.Add(_colourByKind);

            _infoColor = theme.Text;
            _log = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                WordWrap = false,
                Font = new Font("Consolas", 9f),
                BackColor = theme.InputBackground,
                ForeColor = theme.Text,
                BorderStyle = BorderStyle.None,
                HideSelection = false
            };
            Controls.Add(_log);
            _log.BringToFront();

            // 2 Hz: fast enough that the level meter and backlog feel live, far too
            // slow to cost anything.
            _statsTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _statsTimer.Tick += Safe("statsTick", RefreshStats);
            _statsTimer.Start();

            // Live tail: every logger line lands here from any thread.
            //
            // Lines are QUEUED and flushed in batches rather than each one marshalling
            // its own BeginInvoke and doing its own RichTextBox append. Per line that was
            // a cross-thread post plus a SelectionStart/SelectionColor/AppendText round
            // trip — the slowest way to put text in a RichTextBox — so a burst (turning
            // on a verbose trace, the caption engine warming up, a macro replaying) sent
            // hundreds of those a second at the UI thread and the whole app hitched while
            // the debug window was open. Draining on a timer collapses a burst into one
            // append with painting frozen, and costs nothing when the log is quiet.
            _onLine = line =>
            {
                if (line == null) { return; }
                lock (_pendingLock)
                {
                    // Bound the queue: if the UI can't keep up, the ring buffer is still
                    // the source of truth and ReloadFromRing will catch us up.
                    if (_pending.Count < 4000) { _pending.Add(line); }
                    else { _pendingOverflow = true; }
                }
            };
            Utils.Logger.LineLogged += _onLine;

            _flushTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _flushTimer.Tick += Safe("flushTick", FlushPending);
            _flushTimer.Start();

            Load += Safe("load", () => { BuildLegend(); ReloadFromRing(); RefreshStats(); });
            FormClosed += (s, e) =>
            {
                try { Utils.Logger.LineLogged -= _onLine; } catch { }
                try { _statsTimer.Stop(); _statsTimer.Dispose(); } catch { }
                try { _flushTimer?.Stop(); _flushTimer?.Dispose(); _flushTimer = null; } catch { }
                try { _filterDebounce?.Stop(); _filterDebounce?.Dispose(); _filterDebounce = null; } catch { }
                lock (_pendingLock) { _pending.Clear(); _pendingOverflow = false; }
                // Nobody is watching any more — stop both trace streams, or they would
                // quietly flood the log ring for the rest of the session.
                Utils.TempoTranscriber.VerboseTrace = false;
                Engine.CameraRelativeMovement.VerboseTrace = false;
            };
        }

        /// <summary>
        /// Paints the colour legend for the event stream. Each category name is drawn
        /// in the exact colour its log lines use, so the colours are self-explanatory.
        /// When category colouring is off, it shows only the severity key.
        /// </summary>
        private void BuildLegend()
        {
            if (_legend == null) { return; }
            SetRedraw(_legend, false);
            try
            {
                _legend.Clear();
                AppendChip("errors", _errorColor);
                AppendChip("warnings", _warnColor);
                if (_colourByKind == null || _colourByKind.Checked)
                {
                    AppendChip("captions", CapColor);
                    AppendChip("movement", MoveColor);
                    AppendChip("audio", AudioColor);
                    AppendChip("input", InputColor);
                    AppendChip("clicker", ClickColor);
                    AppendChip("scripts", ScriptColor);
                    AppendChip("system", SysColor);
                    AppendChip("trace", TraceColor);
                }
            }
            finally
            {
                SetRedraw(_legend, true);
            }
        }

        private void AppendChip(string label, Color colour)
        {
            _legend.SelectionStart = _legend.TextLength;
            _legend.SelectionLength = 0;
            _legend.SelectionColor = colour;
            // Translated here rather than at each call site, so the legend cannot drift
            // out of the language the rest of the window is in.
            _legend.AppendText("● " + Utils.Localization.T(label) + "   ");   // ● swatch + name
        }

        private static Button MakeButton(Theme theme, string text, int left)
        {
            var b = new Button
            {
                Left = left, Top = 6, Width = 80, Height = 26,
                Text = Utils.Localization.T(text),
                FlatStyle = FlatStyle.Flat,
                BackColor = theme.Surface2, ForeColor = theme.Text
            };
            b.FlatAppearance.BorderColor = theme.Border;
            return b;
        }

        /// <summary>
        /// Wraps an event handler so a throw inside it cannot take Tempo down.
        ///
        /// WinForms invokes these on the UI thread, where an escaping exception ends the
        /// process — and this is the DIAGNOSTIC window: it is opened precisely when
        /// something is already misbehaving, and it reads live state from every
        /// subsystem while that state is churning. A window whose job is to explain a
        /// problem must not become a second one.
        /// </summary>
        private static EventHandler Safe(string where, Action body)
        {
            return (s, e) =>
            {
                try { body(); }
                catch (Exception ex) { Utils.Logger.Swallow("DebugForm." + where, ex); }
            };
        }

        /// <summary>
        /// Briefly confirms on the button itself that something happened, then restores
        /// its caption. Copy has no other visible result — the text goes somewhere the
        /// user cannot see — so without this a successful copy and a silently failed one
        /// looked exactly alike.
        /// </summary>
        private void FlashButton(Button b, string confirmation)
        {
            if (b == null) { return; }
            string original = b.Text;
            b.Text = confirmation;
            var t = new System.Windows.Forms.Timer { Interval = 1400 };
            t.Tick += (s, e) =>
            {
                t.Stop();
                t.Dispose();
                try { if (!IsDisposed && !b.IsDisposed) { b.Text = original; } } catch { }
            };
            // Stop the timer if the window closes first, so it cannot fire against a
            // disposed button (and so it is not left running for its full interval).
            FormClosed += (s, e) => { try { t.Stop(); t.Dispose(); } catch { } };
            t.Start();
        }

        /// <summary>
        /// Rebuilds the stats header, colouring each line by what it MEANS: warnings
        /// amber, hard failures red, healthy/armed states green. The plain-Label version
        /// rendered a wall of identical grey text in which a "⚠ RESTART TEMPO" line was
        /// no more visible than the frame rate.
        /// </summary>
        private void RefreshStats()
        {
            string text;
            try
            {
                text = _statsProvider != null ? _statsProvider() : "";
            }
            catch (Exception ex)
            {
                text = "(stats error: " + ex.Message + ")";
            }

            if (text == _lastStats)
            {
                return;                     // unchanged — don't destroy the user's selection
            }
            _lastStats = text;

            // Remember where the user had scrolled to. Rebuilding the panel resets the
            // view to the top, and because parts of these stats change every half second
            // (times, counters) that happened CONSTANTLY — anything below the fold was
            // impossible to read, because it jumped back the moment you scrolled to it.
            // The event log below already preserved its position; the stats never did.
            var scroll = Point.Empty;
            bool captured = false;
            try
            {
                if (_stats.IsHandleCreated)
                {
                    SendMessage(_stats.Handle, EM_GETSCROLLPOS, IntPtr.Zero, ref scroll);
                    captured = true;
                }
            }
            catch { }

            SetRedraw(_stats, false);
            try
            {
                _stats.Clear();
                foreach (string line in text.Split('\n'))
                {
                    string l = line.TrimEnd('\r');
                    _stats.SelectionStart = _stats.TextLength;
                    _stats.SelectionLength = 0;
                    _stats.SelectionColor = StatsColorFor(l);
                    _stats.AppendText(l + Environment.NewLine);
                }
            }
            finally
            {
                SetRedraw(_stats, true);
            }

            try
            {
                if (captured)
                {
                    SendMessage(_stats.Handle, EM_SETSCROLLPOS, IntPtr.Zero, ref scroll);
                }
            }
            catch { }
        }

        private Color StatsColorFor(string line)
        {
            // Health markers win: ✗ = a hard error (red), ✓ = all-clear (green).
            if (line.StartsWith("✗", StringComparison.Ordinal)) { return _errorColor; }
            if (line.StartsWith("Health: ✓", StringComparison.Ordinal) ||
                line.StartsWith("✓", StringComparison.Ordinal)) { return _goodColor; }
            if (line.StartsWith("⚠", StringComparison.Ordinal) ||
                line.StartsWith("Health: ⚠", StringComparison.Ordinal)) { return _warnColor; }
            if (line.IndexOf("DROPPED", StringComparison.Ordinal) >= 0 ||
                line.IndexOf("too slow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("Couldn't", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return _errorColor;
            }
            if (line.IndexOf("ARMED", StringComparison.Ordinal) >= 0 ||
                line.IndexOf("[ACTING]", StringComparison.Ordinal) >= 0)
            {
                return _goodColor;
            }
            // Indented continuation lines are detail — mute them so the section
            // headings carry the eye down the panel.
            if (line.StartsWith("  ", StringComparison.Ordinal)) { return _statsLabelColor; }
            return _infoColor;
        }

        private bool PassesFilter(string line)
        {
            if (_problemsOnly.Checked &&
                line.IndexOf("[WARN]", StringComparison.Ordinal) < 0 &&
                line.IndexOf("[ERROR]", StringComparison.Ordinal) < 0)
            {
                return false;
            }
            string f = _filter.Text;
            return string.IsNullOrEmpty(f) ||
                   line.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Every line Logger writes has the shape
        //     2026-08-31 08:31:02.123 [INFO] [Captions] engine started
        // so the subsystem tag, when there is one, is always the FIRST bracketed token
        // after the level. This is the anchor the classifier reads.
        private const string InfoLevel = "[INFO] ";

        /// <summary>
        /// The subsystem tag a line carries ("Captions", "2nd cursor", …), or null when
        /// it has none.
        ///
        /// Parsed from a fixed position rather than searched for anywhere in the line.
        /// That distinction is the whole point: the old classifier asked "does this line
        /// CONTAIN the word 'Engine '/'update'/'profile'?", and log lines quote text the
        /// user can influence — translated UI captions, window titles, transcribed
        /// speech. In the shipped log, all 30 lines that matched the clicker's
        /// <c>"Engine "</c> test were German layout warnings quoting the caption
        /// setting "GPU-Engine ausprobieren". Not one was about the clicker. Reading the
        /// tag from where the tag actually is cannot be fooled that way.
        /// </summary>
        private static string TagOf(string line)
        {
            int at = line.IndexOf(InfoLevel, StringComparison.Ordinal);
            if (at < 0) { return null; }
            int open = at + InfoLevel.Length;
            if (open >= line.Length || line[open] != '[') { return null; }
            int close = line.IndexOf(']', open + 1);
            if (close <= open + 1) { return null; }
            return line.Substring(open + 1, close - open - 1);
        }

        /// <summary>
        /// Which subsystem each tag belongs to.
        ///
        /// This table is the contract between the app's logging and this window's
        /// colours, and it is deliberately EXHAUSTIVE over the tags Tempo emits. Half of
        /// them had no entry before and rendered in undifferentiated white — including
        /// [Python], [2nd cursor], [2nd mouse], [Model], [Overlay], [OwnVoice],
        /// [WordFix], [window] and [Tray] — while the legend advertised a "clicker"
        /// colour that nothing in the entire codebase could ever produce, because no line
        /// anywhere was tagged [Clicker].
        ///
        /// Ordinal-ignore-case, so [ui] and [UI] cannot drift apart. Indexer syntax
        /// rather than <c>{ "k", v }</c> pairs on purpose: a duplicate key here would
        /// throw inside a static constructor, i.e. crash the diagnostic window on open.
        /// Duplicates are caught by the source probe instead, where the cost of being
        /// wrong is a failed check rather than a failed launch.
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<string, LineKind> TagKinds =
            new System.Collections.Generic.Dictionary<string, LineKind>(StringComparer.OrdinalIgnoreCase)
            {
                // Speech and captions, end to end: the engine, the model manager, the
                // word fixer and the on-screen bar.
                ["Captions"] = LineKind.Captions,
                ["Captions tab"] = LineKind.Captions,
                ["TempoTranscriber"] = LineKind.Captions,
                ["Model"] = LineKind.Captions,
                ["WordFix"] = LineKind.Captions,
                ["Overlay"] = LineKind.Captions,

                ["Movement"] = LineKind.Movement,

                // Sound and who is making it.
                ["Audio"] = LineKind.Audio,
                ["Voice"] = LineKind.Audio,
                ["OwnVoice"] = LineKind.Audio,
                ["Face"] = LineKind.Audio,

                // Input Tempo READS: devices, the global hotkeys, the low-level hooks,
                // and the second physical mouse.
                ["Keyboard"] = LineKind.Input,
                ["Devices"] = LineKind.Input,
                ["Hotkeys"] = LineKind.Input,
                ["Hooks"] = LineKind.Input,
                ["2nd mouse"] = LineKind.Input,

                // Input Tempo WRITES: the click engine, macro record/playback, and the
                // second cursor, which is a clicking automaton of its own.
                ["Clicker"] = LineKind.Clicker,
                ["Macro"] = LineKind.Clicker,
                ["2nd cursor"] = LineKind.Clicker,

                // Script steps run a whole external process, with failure modes nothing
                // else here has — no interpreter, a timeout, a non-zero exit.
                ["Python"] = LineKind.Script,

                // Lifecycle and housekeeping.
                ["startup"] = LineKind.System,
                ["shutdown"] = LineKind.System,
                ["cleanup"] = LineKind.System,
                ["Restart"] = LineKind.System,
                ["SelfCheck"] = LineKind.System,
                ["Icon"] = LineKind.System,
                ["Notify"] = LineKind.System,
                ["Tray"] = LineKind.System,
                ["Update"] = LineKind.System,
                ["Uninstall"] = LineKind.System,

                // Trust and storage. Both were falling through to the uncategorised
                // safety net: [Integrity] is the tamper check's verdict — the one
                // subsystem a user is most likely to open this window to read — and
                // [Store] is every atomic file write, which is what you want when a
                // setting or a profile did not survive a restart.
                ["Integrity"] = LineKind.System,
                ["Store"] = LineKind.System,
                ["Welcome"] = LineKind.System,
                ["Settings"] = LineKind.System,
                ["Profiles"] = LineKind.System,
                ["UI"] = LineKind.System,
                ["window"] = LineKind.System,
                ["perf"] = LineKind.System,
                ["Debug"] = LineKind.System,

                // High-frequency chatter, dimmed so the events above stand out.
                ["Trace"] = LineKind.Trace,
                ["Move"] = LineKind.Trace,
                ["layout"] = LineKind.Trace,
            };

        /// <summary>
        /// Classifies a log line into a subsystem category. Severity wins first (an
        /// ERROR is red whatever it is about), then the subsystem tag the line carries.
        /// </summary>
        private static LineKind KindOf(string line)
        {
            if (line.IndexOf("[ERROR]", StringComparison.Ordinal) >= 0) { return LineKind.Error; }
            if (line.IndexOf("[WARN]", StringComparison.Ordinal) >= 0) { return LineKind.Warn; }

            string tag = TagOf(line);
            if (tag != null && TagKinds.TryGetValue(tag, out LineKind kind))
            {
                return kind;
            }

            // No tag, or one added since this table was last reviewed. Every
            // Logger.Info call site in Tempo is tagged, so nothing routine lands here —
            // it is the safety net for a new tag and for text pasted in from elsewhere,
            // and it stays plain rather than guessing a subsystem from prose.
            return LineKind.Info;
        }

        private Color ColorForKind(LineKind kind)
        {
            switch (kind)
            {
                case LineKind.Error: return _errorColor;
                case LineKind.Warn: return _warnColor;
                case LineKind.Captions: return CapColor;
                case LineKind.Movement: return MoveColor;
                case LineKind.Audio: return AudioColor;
                case LineKind.Input: return InputColor;
                case LineKind.Clicker: return ClickColor;
                case LineKind.Script: return ScriptColor;
                case LineKind.System: return SysColor;
                case LineKind.Trace: return TraceColor;
                default: return _infoColor;
            }
        }

        private Color ColorFor(string line)
        {
            LineKind kind = KindOf(line);
            // Errors/warnings always keep their severity colour. The per-subsystem hues
            // are optional — the checkbox lets the user fall back to the plain
            // severity-only scheme.
            if (kind == LineKind.Error) { return _errorColor; }
            if (kind == LineKind.Warn) { return _warnColor; }
            if (_colourByKind != null && !_colourByKind.Checked) { return _infoColor; }
            return ColorForKind(kind);
        }

        private void AppendColored(string line)
        {
            _log.SelectionStart = _log.TextLength;
            _log.SelectionLength = 0;
            _log.SelectionColor = ColorFor(line);
            _log.AppendText(line + Environment.NewLine);
        }

        /// <summary>
        /// True when the view is scrolled to (or near) the newest lines. Reading
        /// OLDER lines means the user scrolled UP — new lines must not yank the
        /// view back down (the reported "it forces me to the start" while reading).
        /// </summary>
        private bool FollowingTail()
        {
            try
            {
                // The character under the bottom edge of the viewport: if it's within
                // a couple of lines of the end, the user is at the tail.
                int lastVisible = _log.GetCharIndexFromPosition(
                    new Point(4, _log.ClientSize.Height - 4));
                return lastVisible >= _log.TextLength - 220;
            }
            catch
            {
                return true;
            }
        }

        private readonly System.Collections.Generic.List<string> _pending =
            new System.Collections.Generic.List<string>();
        private readonly object _pendingLock = new object();
        private bool _pendingOverflow;
        private System.Windows.Forms.Timer _flushTimer;

        // Rebuilding the view costs a full re-colour of the ring (up to 1500 lines), and
        // TextChanged fires per KEYSTROKE — so typing a six-letter filter used to do six
        // of them, back to back, on the UI thread. One rebuild once typing settles.
        private System.Windows.Forms.Timer _filterDebounce;

        /// <summary>
        /// Appends everything queued since the last tick in one pass, with the control's
        /// painting frozen so a burst costs a single repaint instead of one per line.
        /// </summary>
        private void FlushPending()
        {
            string[] batch;
            bool overflowed;
            lock (_pendingLock)
            {
                if (_pending.Count == 0 && !_pendingOverflow) { return; }
                batch = _pending.ToArray();
                _pending.Clear();
                overflowed = _pendingOverflow;
                _pendingOverflow = false;
            }

            if (IsDisposed || !IsHandleCreated || _pause.Checked) { return; }

            // Dropped lines mean the view is behind the ring — rebuild from the ring,
            // which is authoritative, rather than showing a gap with no explanation.
            if (overflowed)
            {
                ReloadFromRing();
                return;
            }

            bool follow = FollowingTail();
            int keepPos = _log.SelectionStart;

            SetRedraw(false);
            try
            {
                foreach (string line in batch)
                {
                    if (!PassesFilter(line)) { continue; }
                    if (_shownLines > 2200)
                    {
                        SetRedraw(true);
                        ReloadFromRing();
                        return;
                    }
                    _shownLines++;
                    AppendColored(line);
                }
            }
            finally
            {
                SetRedraw(true);
            }

            if (follow)
            {
                _log.SelectionStart = _log.TextLength;
                _log.SelectionLength = 0;
                _log.ScrollToCaret();
            }
            else
            {
                try { _log.SelectionStart = keepPos; _log.SelectionLength = 0; } catch { }
            }
            _log.Invalidate();
        }

        // SendMessage is already declared once for this form (the scroll-position
        // preservation around the stats rebuild) — reuse it.

        /// <summary>Freezes/thaws the log control's painting around a batch append.</summary>
        private void SetRedraw(bool on)
        {
            try
            {
                const int WM_SETREDRAW = 0x000B;
                SendMessage(_log.Handle, WM_SETREDRAW, (IntPtr)(on ? 1 : 0), IntPtr.Zero);
            }
            catch { }
        }

        // AppendLine used to live here: the pre-batching, one-line-at-a-time appender.
        // FlushPending replaced it and nothing has called it since, so it was dead code
        // carrying a SECOND copy of the follow-the-tail and 2200-line-cap rules — the
        // kind that gets faithfully updated alongside the real one for years without
        // ever running, or worse, drifts from it and misleads the next reader.

        private void ReloadFromRing()
        {
            try
            {
                _log.SuspendLayout();
                _log.Clear();
                int n = 0;
                foreach (string line in Utils.Logger.Snapshot())
                {
                    if (PassesFilter(line))
                    {
                        AppendColored(line);
                        n++;
                    }
                }
                _shownLines = n;
                _log.SelectionStart = _log.TextLength;
                _log.ScrollToCaret();
                _log.ResumeLayout();
            }
            catch { }
        }

        private string BuildExportText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("== Tempo live debug export · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ==");
            // Export the stats we last RENDERED, not _stats.Text: the RichTextBox
            // normalises newlines, and _lastStats is the exact provider output.
            sb.AppendLine(_lastStats);
            sb.AppendLine("== recent events ==");
            foreach (string line in Utils.Logger.Snapshot())
            {
                sb.AppendLine(line);
            }
            return sb.ToString();
        }

        private void SaveToFile()
        {
            try
            {
                using (var dlg = new SaveFileDialog
                {
                    FileName = "tempo-debug-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log",
                    Title = Utils.Localization.T("Save the live debug report"),
                    Filter = Utils.Localization.T("Log files (*.log)|*.log|All files (*.*)|*.*")
                })
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        System.IO.File.WriteAllText(dlg.FileName, BuildExportText());
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Utils.Localization.F("Couldn't save: {0}", ex.Message), "Tempo",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
