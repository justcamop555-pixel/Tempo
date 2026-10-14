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
        private Color AcctColor;   // Roblox accounts, vault, launches, the local API — teal
        private Color TraceColor;  // per-chunk traces — dim grey
        private readonly CheckBox _colourByKind;   // toggles category colouring on/off
        private RichTextBox _legend;
        private Label _matchCount;                 // "142 of 2,310" beside the filter box

        /// <summary>
        /// What "Check layout" scans. Set by the main window, which knows about screens that
        /// exist but are not on screen (the detached Accounts panels); without it, only the
        /// window that owns this one is checked.
        /// </summary>
        public Func<System.Collections.Generic.IEnumerable<Control>> LayoutRoots { get; set; }
        private ToolTip _filterTip;                // the filter syntax, and what the query means
        private string _filterTipText;             // what _filterTip currently says
        private Color _matchBackColor;             // highlight behind a matched word
        private Color _matchForeColor;

        /// <summary>A subsystem category a log line belongs to, for colouring.</summary>
        private enum LineKind { Info, Error, Warn, Captions, Movement, Audio, Input, Clicker, Script, System, Accounts, Trace }
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
                AcctColor = Color.FromArgb(0, 124, 124);    // accounts — deep teal
                TraceColor = Color.FromArgb(130, 136, 148); // traces — grey
                // The search highlight. A fixed pair rather than a theme colour: it has to
                // stay readable over EVERY category hue a matched line might already be
                // wearing, and the accent of a red or green theme would not.
                _matchBackColor = Color.FromArgb(255, 226, 120);
                _matchForeColor = Color.FromArgb(28, 26, 12);
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
                AcctColor = Color.FromArgb(64, 214, 200);   // accounts — teal: saturated and
                                                            // green-leaning, so it reads apart
                                                            // from the pale sky-blue captions
                TraceColor = Color.FromArgb(120, 128, 145); // traces — dim grey
                _matchBackColor = Color.FromArgb(120, 96, 0);
                _matchForeColor = Color.FromArgb(255, 244, 205);
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
                if (_splitter != null) { _splitter.BackColor = theme.Border; }
                if (_sectionPicker != null) { _sectionPicker.BackColor = theme.InputBackground; _sectionPicker.ForeColor = theme.Text; }

                ApplyTitleBarTheme();
                try { NativeChrome.ApplyAllScrollbarThemes(this, theme.Background.GetBrightness() < 0.5f); }
                catch { }

                // Both panes hold text already coloured with the OLD palette, so repaint
                // their contents rather than leaving stale hues behind.
                BuildLegend();
                _lastStats = "";      // force the next tick to redraw with the new colours
                ReloadFromRing(keepPlace: true);
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
            _baseTitle = Text;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(880, 640);
            MinimumSize = new Size(700, 420);
            // 640 tall is more than a 1366x768 laptop at 125% has room for, and this window is only
            // themed by ApplyTheme (on a theme change), not by the chrome call that fits the others.
            ScreenFit.Attach(this);
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

            // Drag to give the stats more room (or less). The report runs to some 35 sections, and a
            // fixed 196 px showed about twelve lines of it. Remembered while Tempo runs.
            _splitter = new Splitter
            {
                Dock = DockStyle.Top,
                Height = 5,
                MinSize = StatsLeastHeight,
                MinExtra = LogLeastHeight,
                BackColor = theme.Border,
            };
            _splitter.SplitterMoved += (s, e) =>
            {
                try
                {
                    _userStatsHeight = _stats.Height;
                    s_rememberedStatsHeight = _stats.Height;
                }
                catch (Exception ex) { Utils.Logger.Swallow("DebugForm.statsSplitter", ex); }
            };
            Controls.Add(_splitter);
            _userStatsHeight = s_rememberedStatsHeight;

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

            // The control strip FLOWS and wraps instead of pinning each control at a fixed x. Pinned,
            // the longer languages ran them into each other ("Problèmes uniquement" over "Trace des
            // sous-titres", the hint under "Colorear por categoría"), "Toujours au premier plan" hung
            // off the right edge, and at the window's minimum width three controls sat outside it in
            // English too — 27 faults across six languages, measured by scratchpad/debugfit. The
            // strip's height follows whatever the wrap needs (FitControlStrip).
            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 72,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Padding = new Padding(6, 3, 6, 3),
                BackColor = theme.Surface
            };
            _controlStrip = bottom;
            bottom.Layout += (s, e) => FitControlStrip();
            // A height-only resize doesn't lay the strip out again, and the stats/log share still has to follow.
            Resize += (s, e) => FitControlStrip();
            Controls.Add(bottom);

            _filter = new TextBox
            {
                Width = 220,
                Margin = new Padding(4, 6, 4, 4),
                BackColor = theme.InputBackground, ForeColor = theme.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            _filterDebounce = new System.Windows.Forms.Timer { Interval = 180 };
            _filterDebounce.Tick += Safe("filterDebounce", () =>
            {
                _filterDebounce.Stop();
                // Parsed ONCE per settled query, not once per line: a reload asks the filter
                // about 1500 lines, and re-parsing inside that loop is work for nothing.
                _parsedFilter = Utils.LogFilter.Parse(_filter.Text);
                ReloadFromRing();
            });
            _filter.TextChanged += Safe("filter", () =>
            {
                _filterDebounce.Stop();      // restart the countdown on each keystroke
                _filterDebounce.Start();
            });
            // Enter jumps to the first match instead of doing nothing (and instead of the
            // form's default button eating the key).
            _filter.KeyDown += (s, e) =>
            {
                // Escape clears the filter, as it does in the caption transcript's filter box (the same
                // query language) — it did nothing here.
                if (e.KeyCode == Keys.Escape && _filter.TextLength > 0)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    _filter.Clear();
                    return;
                }
                if (e.KeyCode != Keys.Enter) { return; }
                e.Handled = true;
                e.SuppressKeyPress = true;
                try
                {
                    // Enter again on the same query: the next match (Shift+Enter the previous), as in a
                    // browser's find box, with the caret staying here. It is also the way through the
                    // matches when F3 is bound as a Tempo hotkey: a key Windows reserves for a hotkey
                    // never reaches this window, and with Shift/Ctrl versions on, Shift+F3 goes too.
                    if (_enterJumped && !_filterDebounce.Enabled
                        && string.Equals(_filter.Text, _parsedFilter.Query, StringComparison.Ordinal))
                    {
                        JumpToMatch(!e.Shift, focusLog: false);
                        return;
                    }
                    _filterDebounce.Stop();
                    _parsedFilter = Utils.LogFilter.Parse(_filter.Text);
                    ReloadFromRing();
                    _log.SelectionStart = 0;
                    JumpToMatch(true, focusLog: false);
                    _enterJumped = true;
                }
                catch { }
            };
            _filter.TextChanged += (s, e) => _enterJumped = false;
            bottom.Controls.Add(_filter);

            // What the box understands. It is a real query language now, and a syntax nobody
            // can see is a syntax nobody uses.
            _filterTip = new ToolTip { AutoPopDelay = 20000, InitialDelay = 350, ReshowDelay = 120 };
            TipStyle.Set(_filterTip, _filter, FilterHelp);

            var filterHint = new Label
            {
                AutoSize = true,
                Margin = new Padding(0, 9, 8, 4),
                Text = Utils.Localization.T("filter"),
                ForeColor = theme.TextMuted, BackColor = Color.Transparent
            };
            bottom.Controls.Add(filterHint);

            _matchCount = new Label
            {
                AutoSize = true,
                Margin = new Padding(0, 9, 12, 4),
                Text = "",
                ForeColor = theme.TextMuted, BackColor = Color.Transparent
            };
            bottom.Controls.Add(_matchCount);

            _pause = new CheckBox
            {
                AutoSize = true, Margin = StripCheckMargin, Text = Utils.Localization.T("Pause"),
                ForeColor = theme.Text, BackColor = Color.Transparent
            };
            // Catching up on UNPAUSE is the whole point, and it was missing: FlushPending
            // drains the queue before it checks Pause, so everything logged while paused
            // was thrown away, and with no handler here nothing ever rebuilt the view.
            // Un-pausing left a silent, permanent hole in the stream — the events you
            // paused to go and read were the ones you lost. The ring still has them.
            // Pause holds the STATS still too: they changed twice a second under the reader.
            _pause.CheckedChanged += Safe("pause", () =>
            {
                if (!_pause.Checked) { ReloadFromRing(keepPlace: true); }
                RefreshStats();
            });
            bottom.Controls.Add(_pause);
            TipStyle.Set(_tips, _pause, Utils.Localization.T(
                "Holds the stats and the event log still so you can read them. Selecting text in the stats holds them too."));

            _problemsOnly = new CheckBox
            {
                AutoSize = true, Margin = StripCheckMargin, Text = Utils.Localization.T("Problems only"),
                ForeColor = theme.Text, BackColor = Color.Transparent
            };
            _problemsOnly.CheckedChanged += Safe("problemsOnly", () => ReloadFromRing());
            bottom.Controls.Add(_problemsOnly);

            // Per-chunk pipeline trace: one [Trace] line per transcription pass
            // (chunk → inference time, real-time factor, backlog, gain, the text
            // shown). Lives in the transcriber as a static flag so no plumbing is
            // needed; switched off again when this window closes so the log ring
            // isn't quietly flooded afterwards.
            _trace = new CheckBox
            {
                AutoSize = true, Margin = StripCheckMargin, Text = Utils.Localization.T("Caption trace"),
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
                AutoSize = true, Margin = StripCheckMargin, Text = Utils.Localization.T("Movement trace"),
                ForeColor = theme.Text, BackColor = Color.Transparent
            };
            _moveTrace.CheckedChanged += Safe("movementTrace",
                () => Engine.CameraRelativeMovement.VerboseTrace = _moveTrace.Checked);
            bottom.Controls.Add(_moveTrace);

            // Without this the window sits BEHIND the game you are trying to debug,
            // which made it useless for exactly the case it is most needed in.
            _onTop = new CheckBox
            {
                AutoSize = true, Margin = StripCheckMargin, Text = Utils.Localization.T("Always on top"),
                ForeColor = theme.Text, BackColor = Color.Transparent
            };
            _onTop.CheckedChanged += Safe("alwaysOnTop", () => TopMost = _onTop.Checked);
            bottom.Controls.Add(_onTop);

            // Colour each event by which subsystem it came from (captions, movement,
            // audio, input, …) so the stream can be read at a glance. On by default;
            // untick for the plain severity-only colouring. The legend strip above the
            // log shows what each colour means. It sits with the other view options now,
            // and the buttons start a row of their own after it.
            _colourByKind = new CheckBox
            {
                AutoSize = true, Margin = StripCheckMargin,
                Text = Utils.Localization.T("Colour by category"), Checked = true,
                ForeColor = theme.Text, BackColor = Color.Transparent
            };
            _colourByKind.CheckedChanged += Safe("colourByKind", () =>
            {
                BuildLegend();
                ReloadFromRing(keepPlace: true);
            });
            bottom.Controls.Add(_colourByKind);

            // Jump the stats to a section: the headings of the report as it stands ("Health",
            // "Captions", "Speakers", "Accounts", …), refreshed as sections come and go.
            _sectionPicker = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Width = 170,
                Margin = new Padding(4, 6, 10, 4),
                BackColor = theme.InputBackground, ForeColor = theme.Text,
            };
            _sectionPicker.Items.Add(Utils.Localization.T("Go to section…"));
            _sectionPicker.SelectedIndex = 0;
            // This window is not scaled by WinForms (its strips flow), so its numbers are the screen's
            // pixels: a fixed 170 cut "Aller à la section…" on a 150% screen.
            _sectionPicker.Width = _sectionPicker.LogicalToDeviceUnits(170);
            _sectionPicker.SelectedIndexChanged += Safe("sectionPicker", () =>
            {
                if (_sectionPicker.SelectedIndex <= 0) { return; }
                ScrollStatsTo(_sectionPicker.SelectedItem as string);
                // Back to the prompt, so choosing the same section again works too.
                BeginInvoke(new Action(() => { try { _sectionPicker.SelectedIndex = 0; } catch { } }));
            });
            bottom.Controls.Add(_sectionPicker);

            var copyBtn = MakeButton(theme, "Copy");
            LayoutFitter.DeclareCaptions(copyBtn, Utils.Localization.T("Copy"), Utils.Localization.T("Copied  ✓"));
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

            var saveBtn = MakeButton(theme, "Save…");
            // Confirmed on the button, as Copy is: once the dialog closed, nothing said the file was written.
            LayoutFitter.DeclareCaptions(saveBtn, Utils.Localization.T("Save…"), Utils.Localization.T("Saved  ✓"));
            saveBtn.Click += Safe("save", () =>
            {
                if (SaveToFile()) { FlashButton(saveBtn, Utils.Localization.T("Saved  ✓")); }
            });
            bottom.Controls.Add(saveBtn);

            var clearBtn = MakeButton(theme, "Clear view");
            clearBtn.Click += Safe("clear", ClearView);
            bottom.Controls.Add(clearBtn);

            // The layout checker, on demand. At start-up it runs once, over the main window,
            // before anything has been used; this runs the same four checks over every
            // Tempo window that is open now, laid out as it really is, and filters the log
            // to what they found.
            var layoutBtn = MakeButton(theme, "Check layout");
            // What it flashes after a run: measured with the caption it has.
            LayoutFitter.DeclareCaptions(layoutBtn, Utils.Localization.T("Check layout"), Utils.Localization.T("No problems  ✓"),
                Utils.Localization.F("{0} found", 88));
            layoutBtn.Click += Safe("checkLayout", () =>
            {
                System.Collections.Generic.IEnumerable<Control> roots = LayoutRoots != null
                    ? LayoutRoots()
                    : new Control[] { Owner ?? (Control)this };
                LayoutFitter.Report rep = LayoutFitter.CheckNow(roots);

                // Show what it found where the eye already is: the log, narrowed to the
                // layout lines, with the faults highlighted.
                _filter.Text = "tag:layout";
                // Setting the text started the filter's countdown, which would rebuild the log a second
                // time 180 ms later; this rebuild already does it.
                _filterDebounce.Stop();
                _parsedFilter = Utils.LogFilter.Parse(_filter.Text);
                ReloadFromRing();
                FlashButton(layoutBtn, rep.Faults == 0
                    ? Utils.Localization.T("No problems  ✓")
                    : Utils.Localization.F("{0} found", rep.Faults));
            });
            bottom.Controls.Add(layoutBtn);
            TipStyle.Set(_tips, layoutBtn, Utils.Localization.T(
                "Checks every open Tempo window for text that overlaps, buttons that cut off their label, "
                + "and controls that stick out of their card."));

            // What Copy and Save put in the report, on the buttons themselves. It was a line of text
            // beside them, which in French alone needed 434 px — a whole extra row of the strip,
            // taken from the event log.
            string exportHint = Utils.Localization.T("Copy/Save include the stats above + the recent events.");
            TipStyle.Set(_tips, copyBtn, exportHint);
            TipStyle.Set(_tips, saveBtn, exportHint);

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

            // Top to bottom: stats, the splitter that resizes them, the colour legend, the event
            // log, the control strip. Docking goes by z-order, back to front, and the legend — the
            // key to the EVENT LOG's colours — used to dock first: it sat at the very top, above the
            // stats, 196 px away from the lines it explains.
            Controls.SetChildIndex(bottom, 4);
            Controls.SetChildIndex(_stats, 3);
            Controls.SetChildIndex(_splitter, 2);
            Controls.SetChildIndex(_legend, 1);
            Controls.SetChildIndex(_log, 0);

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
                try { _tips.Dispose(); } catch { }
                try { _filterTip?.Dispose(); _filterTip = null; } catch { }
                foreach (System.Windows.Forms.Timer flash in _flashTimers.Values)
                {
                    try { flash.Stop(); flash.Dispose(); } catch { }
                }
                _flashTimers.Clear();
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
                    AppendChip("accounts", AcctColor);
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

        private static Button MakeButton(Theme theme, string text)
        {
            var b = new Button
            {
                // Sized to its caption, never narrower than 80. A fixed 80 cut off "Limpiar la
                // vista", "Ansicht leeren", "Svuota la vista", "Limpar a vista" and "Effacer la vue".
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(80, 26),
                Padding = new Padding(6, 0, 6, 0),
                Margin = new Padding(4, 4, 4, 4),
                Text = Utils.Localization.T(text),
                FlatStyle = FlatStyle.Flat,
                BackColor = theme.Surface2, ForeColor = theme.Text
            };
            b.FlatAppearance.BorderColor = theme.Border;
            return b;
        }

        // The view options' spacing in the control strip: a gap after each, nudged down to sit
        // level with the filter box.
        private static readonly Padding StripCheckMargin = new Padding(4, 8, 10, 4);

        private FlowLayoutPanel _controlStrip;
        private bool _fittingStrip;
        private readonly ToolTip _tips = new ToolTip();

        // The stats pane's full height, and the least it gives up to on a short window.
        private const int StatsFullHeight = 196;
        private const int StatsLeastHeight = 96;
        // The least the event log keeps when the stats are dragged taller.
        private const int LogLeastHeight = 100;

        private readonly Splitter _splitter;
        // A height the user chose by dragging the splitter (0 = the automatic share), and the same
        // for the next Live debug opened this session.
        private int _userStatsHeight;
        private static int s_rememberedStatsHeight;

        private readonly ComboBox _sectionPicker;
        private string _sectionKey = "";          // the headings the picker lists, joined

        /// <summary>
        /// Gives the control strip the height its wrapped rows need at the window's current width,
        /// then shares what is left between the stats pane and the event log.
        ///
        /// A docked FlowLayoutPanel does not size itself to its own wrap — AutoSize measures the
        /// controls as one unbroken row — so the height is asked for at the real width and applied
        /// here, from the strip's Layout (a resize, a longer caption such as "Copied ✓"). The stats
        /// pane used to keep a fixed 196 px however short the window, which at the minimum size left
        /// the event log 91 px; now it gives way until the two are even.
        /// </summary>
        private void FitControlStrip()
        {
            var strip = _controlStrip;
            if (strip == null || strip.IsDisposed || _fittingStrip) { return; }
            try
            {
                _fittingStrip = true;
                int width = strip.ClientSize.Width;
                if (width <= 0) { return; }
                int want = strip.GetPreferredSize(new Size(width, 0)).Height;
                if (want > 0 && want != strip.Height) { strip.Height = want; }

                if (_stats != null && !_stats.IsDisposed)
                {
                    int legend = _legend != null ? _legend.Height : 0;
                    int splitter = _splitter != null ? _splitter.Height : 0;
                    int shared = ClientSize.Height - legend - splitter - strip.Height;
                    // The user's own split when they dragged one (the log always keeps its least);
                    // otherwise half, up to the full 196.
                    int stats = _userStatsHeight > 0
                        ? Math.Max(StatsLeastHeight, Math.Min(_userStatsHeight, shared - LogLeastHeight))
                        : Math.Max(StatsLeastHeight, Math.Min(StatsFullHeight, shared / 2));
                    if (shared > 0 && stats != _stats.Height) { _stats.Height = stats; }
                }
            }
            catch (Exception ex) { Utils.Logger.Swallow("DebugForm.FitControlStrip", ex); }
            finally { _fittingStrip = false; }
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

            // A second press while the first confirmation is still up must restore the REAL
            // caption. Reading b.Text here used to capture the confirmation itself, so a
            // double-click on Copy left the button reading "Copied ✓" for good.
            if (!_flashOriginal.TryGetValue(b, out string original))
            {
                original = b.Text;
                _flashOriginal[b] = original;
            }
            if (_flashTimers.TryGetValue(b, out System.Windows.Forms.Timer running))
            {
                try { running.Stop(); running.Dispose(); } catch { }
            }

            b.Text = confirmation;
            var t = new System.Windows.Forms.Timer { Interval = 1400 };
            _flashTimers[b] = t;
            t.Tick += (s, e) =>
            {
                t.Stop();
                t.Dispose();
                _flashTimers.Remove(b);
                _flashOriginal.Remove(b);
                try { if (!IsDisposed && !b.IsDisposed) { b.Text = original; } } catch { }
            };
            t.Start();
        }

        // The caption each flashing button goes back to, and the timer that will put it back.
        // Stopped when the window closes (see the FormClosed handler), so no timer fires
        // against a disposed button.
        private readonly System.Collections.Generic.Dictionary<Button, string> _flashOriginal =
            new System.Collections.Generic.Dictionary<Button, string>();
        private readonly System.Collections.Generic.Dictionary<Button, System.Windows.Forms.Timer> _flashTimers =
            new System.Collections.Generic.Dictionary<Button, System.Windows.Forms.Timer>();

        /// <summary>
        /// Rebuilds the stats header, colouring each line by what it MEANS: warnings
        /// amber, hard failures red, healthy/armed states green. The plain-Label version
        /// rendered a wall of identical grey text in which a "⚠ RESTART TEMPO" line was
        /// no more visible than the frame rate.
        /// </summary>
        private void RefreshStats()
        {
            // Minimised, nobody can read them: the report is not built (it asks every subsystem) nor
            // repainted. Twice a second for as long as the window sat minimised, it was. They are rebuilt
            // within a tick of the restore (see FlushPending).
            if (WindowState == FormWindowState.Minimized)
            {
                _statsStale = true;
                return;
            }

            // Held still while paused, or while text in the stats is selected or being selected.
            // They are rebuilt from scratch twice a second and something in them changes every
            // time, so a selection was wiped within half a second: copying one line out of the
            // stats was a race nobody won. The title says when they are held.
            bool hold = (_pause != null && _pause.Checked) || StatsSelectionInProgress();
            SetStatsHeldTitle(hold);
            if (hold) { return; }

            string text;
            try
            {
                text = _statsProvider != null ? _statsProvider() : "";
            }
            catch (Exception ex)
            {
                text = "(stats error: " + ex.Message + ")";
            }

            // Say so when a tag has been seen that the colour table has no entry for.
            // Those lines render as ordinary Info and look like everything else, so the
            // table falling behind the code is otherwise invisible — which is exactly how
            // [Macros], [history] and [sidebar] went uncoloured without anyone noticing.
            string[] unknown = UncategorisedTags();
            if (unknown.Length > 0)
            {
                text += Environment.NewLine + "⚠ uncategorised log tags (shown without a colour): ["
                        + string.Join("], [", unknown) + "]";
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
                var lines = new System.Collections.Generic.List<string>();
                foreach (string line in text.Split('\n')) { lines.Add(line.TrimEnd('\r')); }
                UpdateSectionPicker(lines);
                bool done = false;
                try
                {
                    // One assignment, not one append per line — ~2.5 ms instead of ~47 ms,
                    // twice a second (see BuildLinesRtf).
                    _stats.Rtf = BuildLinesRtf(lines, StatsColorFor, _stats.Font, false);
                    done = true;
                }
                catch (Exception ex) { Utils.Logger.Swallow("DebugForm stats", ex); }

                if (!done)
                {
                    _stats.Clear();
                    foreach (string l in lines)
                    {
                        _stats.SelectionStart = _stats.TextLength;
                        _stats.SelectionLength = 0;
                        _stats.SelectionColor = StatsColorFor(l);
                        _stats.AppendText(l + Environment.NewLine);
                    }
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

        /// <summary>True while text in the stats is selected (with the pane focused) or a selection is being dragged.</summary>
        private bool StatsSelectionInProgress()
        {
            try
            {
                if (_stats == null || _stats.IsDisposed || !_stats.IsHandleCreated) { return false; }
                if (_stats.ContainsFocus && _stats.SelectionLength > 0) { return true; }
                // Mid-drag the selection can still be empty: the button is down over the pane.
                return (Control.MouseButtons & MouseButtons.Left) != 0
                       && _stats.ClientRectangle.Contains(_stats.PointToClient(Control.MousePosition));
            }
            catch { return false; }
        }

        private string _baseTitle;

        private void SetStatsHeldTitle(bool held)
        {
            if (_baseTitle == null) { return; }
            string want = held ? _baseTitle + "  ·  " + Utils.Localization.T("stats held") : _baseTitle;
            if (Text != want) { Text = want; }
        }

        // A section heading: a line that starts, unindented, with a short name and a colon —
        // "Health: …", "Local API: …", "2nd cursor: …".
        private static readonly System.Text.RegularExpressions.Regex HeadingRx =
            new System.Text.RegularExpressions.Regex(@"^([A-Z0-9][A-Za-z0-9 /&.\-]{1,22}):\s",
                                                     System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>Lists the report's section headings in the picker, when they change.</summary>
        private void UpdateSectionPicker(System.Collections.Generic.List<string> lines)
        {
            if (_sectionPicker == null || _sectionPicker.IsDisposed || _sectionPicker.DroppedDown) { return; }
            var headings = new System.Collections.Generic.List<string>();
            foreach (string l in lines)
            {
                var m = HeadingRx.Match(l);
                if (m.Success && !headings.Contains(m.Groups[1].Value)) { headings.Add(m.Groups[1].Value); }
            }
            string key = string.Join("|", headings);
            if (key == _sectionKey) { return; }
            _sectionKey = key;
            _sectionPicker.BeginUpdate();
            try
            {
                while (_sectionPicker.Items.Count > 1) { _sectionPicker.Items.RemoveAt(1); }
                foreach (string h in headings) { _sectionPicker.Items.Add(h); }
            }
            finally { _sectionPicker.EndUpdate(); }
        }

        private const int EM_GETFIRSTVISIBLELINE = 0x00CE;
        private const int EM_LINESCROLL = 0x00B6;

        /// <summary>
        /// Scrolls the stats so <paramref name="heading"/>'s line is at the top. The next rebuild keeps
        /// the view where it is (see RefreshStats), so the jump sticks.
        /// </summary>
        private void ScrollStatsTo(string heading)
        {
            if (string.IsNullOrEmpty(heading) || _stats == null || !_stats.IsHandleCreated) { return; }
            string[] lines = _stats.Lines;
            int target = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith(heading + ":", StringComparison.Ordinal)) { target = i; break; }
            }
            if (target < 0) { return; }
            int first = (int)SendMessage(_stats.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
            SendMessage(_stats.Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)(target - first));
        }

        private Color StatsColorFor(string line)
        {
            // Health markers win: ✗ = a hard error (red), ✓ = all-clear (green).
            if (line.StartsWith("✗", StringComparison.Ordinal)) { return _errorColor; }
            if (line.StartsWith("Health: ✓", StringComparison.Ordinal) ||
                line.StartsWith("✓", StringComparison.Ordinal)) { return _goodColor; }
            // Indented warnings count too. Detail lines such as "  ⚠ the watched window
            // returns a BLANK image" used to fall through to the indentation rule below
            // and render MUTED — the one kind of line meant to stand out came out
            // quieter than the heading above it.
            string lead = line.TrimStart(' ');
            if (lead.StartsWith("⚠", StringComparison.Ordinal) ||
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

        /// <summary>
        /// The parsed filter box. Rebuilt when the text settles rather than per line: a
        /// reload asks this question 1500 times, and re-parsing the query each time was pure
        /// waste on the UI thread.
        /// </summary>
        private Utils.LogFilter _parsedFilter = Utils.LogFilter.Parse("");

        private bool PassesFilter(string line)
        {
            if (_problemsOnly.Checked && !IsProblem(line))
            {
                return false;
            }
            return _parsedFilter.IsMatch(line);
        }

        /// <summary>
        /// A warning or an error, by the LEVEL the logger wrote — read from where it is, as the filter's
        /// level: reads it. Searching for "[WARN]" / "[ERROR]" anywhere in the line counted an INFO line
        /// that merely quoted one (a script's output, a window title) as a problem: Problems only listed
        /// it, the colours made it red, and level:error — the same question — said no.
        /// </summary>
        private static bool IsProblem(string line)
        {
            string level = Utils.LogLine.LevelOf(line);
            return level == "WARN" || level == "ERROR";
        }

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
        // One reader for the whole app, in Utils/LogLine.cs. It also fixed a blind spot this
        // copy had: anchoring on the literal "[INFO] " meant no WARNING and no ERROR line
        // carried a tag, so "which subsystem is complaining" — the question the window exists
        // to answer — had no answer for exactly those lines.
        private static string TagOf(string line) => Utils.LogLine.TagOf(line);

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
                // [Guard] is the shortcut guard (Native/ShortcutGuard): Windows' own shortcuts held back
                // while a run lasts, and the hook it does that with.
                ["Guard"] = LineKind.Input,
                // [RawInput] is the one shared raw-mouse registration (Native/RawMouseHub) the second mouse,
                // the macro recorder and the raw mouse readout listen through: registered, released, refused.
                ["RawInput"] = LineKind.Input,
                ["2nd mouse"] = LineKind.Input,

                // Input Tempo WRITES: the click engine, macro record/playback, and the
                // second cursor, which is a clicking automaton of its own.
                ["Clicker"] = LineKind.Clicker,
                ["Macro"] = LineKind.Clicker,
                // [Macros] as well as [Macro]: the ENGINE tags its lines with the
                // singular (MacroPlayer, MacroRecorder) and the TAB with the plural
                // (quick-play slots, delete, the recycle bin). Only the singular was
                // mapped, so every line about deleting or restoring a macro — exactly
                // what you open this window to check — rendered uncoloured.
                ["Macros"] = LineKind.Clicker,
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
                // [Logo] is the animated custom logo: how many frames it decoded, how
                // long the loop is, or why it is showing still. It sits beside [Icon] for
                // the same reason — "my logo isn't doing what I set" is one question, and
                // splitting its answer across a coloured tag and an uncoloured one would
                // hide half of it.
                ["Logo"] = LineKind.System,
                // [Install] is the Settings > Apps entry being corrected — the version
                // Windows shows, and a repaired Uninstall button. Rare, and exactly the
                // kind of line you go looking for when Control Panel disagrees with the
                // app.
                ["Install"] = LineKind.System,
                // [OS] names the Windows this is running on and says whether it is older
                // than Tempo supports. First line of any bug report.
                ["OS"] = LineKind.System,
                ["Notify"] = LineKind.System,
                ["Tray"] = LineKind.System,
                ["Update"] = LineKind.System,
                // [GitHub] is every request to GitHub's API (the update check, the release check behind
                // the Official ID): the answer, the requests left this hour, a rate-limit pause.
                ["GitHub"] = LineKind.System,
                ["Uninstall"] = LineKind.System,

                // Trust and storage. Both were falling through to the uncategorised
                // safety net: [Integrity] is the tamper check's verdict — the one
                // subsystem a user is most likely to open this window to read — and
                // [Store] is every atomic file write, which is what you want when a
                // setting or a profile did not survive a restart.
                ["Integrity"] = LineKind.System,
                ["Store"] = LineKind.System,
                // [history] is the session-history store trimming or failing to write —
                // the other half of "where did my data go", alongside [Store].
                ["history"] = LineKind.System,
                ["Welcome"] = LineKind.System,
                ["Settings"] = LineKind.System,
                ["Profiles"] = LineKind.System,
                ["UI"] = LineKind.System,
                // [Overlays] is the running badges (clicking, playing, recording): moved, faded, put back.
                // Not [Overlay], which is the caption bar.
                ["Overlays"] = LineKind.System,
                // [Display] is the HDR colour correction switching on or off, as the window
                // moves between an HDR and an SDR screen or HDR is toggled in Windows.
                ["Display"] = LineKind.System,
                // [sidebar] is the nav-rail table check. It only ever warns, so severity
                // colours it anyway — but relying on that would leave it uncoloured the
                // day someone adds an Info line under the same tag.
                ["sidebar"] = LineKind.System,
                ["window"] = LineKind.System,
                ["perf"] = LineKind.System,
                // [stall] is the window-freeze watch (UI/UiStallWatch): "the window did not respond for 2.4 s",
                // with what was running. It sits with [perf]: the same question, how long things take.
                ["stall"] = LineKind.System,
                ["Debug"] = LineKind.System,
                // [Data] is restore points: the copy taken when a new build first starts, and a
                // scheduled restore. It belongs with [Store] — "where did my data go" again.
                ["Data"] = LineKind.System,
                // [WebUI] is the experimental HTML Settings page; [Drag] is the window move loop
                // (the drag-lag work); [crash] is the crash-count warning at startup.
                ["WebUI"] = LineKind.System,
                ["Drag"] = LineKind.System,
                ["crash"] = LineKind.System,

                // The Roblox account manager, which arrived with ninety-odd log lines and no entry
                // here, so every one of them rendered uncoloured and the stats header listed five
                // "uncategorised" tags: the vault, sign-in through a real browser, launching and the
                // multi-instance lock closer, and the local API. It is the subsystem people now run
                // live beside a game, so it gets its own colour rather than a share of System's.
                ["Accounts"] = LineKind.Accounts,
                ["Vault"] = LineKind.Accounts,
                ["Roblox"] = LineKind.Accounts,
                ["RobloxLogin"] = LineKind.Accounts,
                ["AccountApi"] = LineKind.Accounts,

                // High-frequency chatter, dimmed so the events above stand out.
                ["Trace"] = LineKind.Trace,
                ["Move"] = LineKind.Trace,
                ["layout"] = LineKind.Trace,
            };

        /// <summary>
        /// Classifies a log line into a subsystem category. Severity wins first (an
        /// ERROR is red whatever it is about), then the subsystem tag the line carries.
        /// </summary>
        /// <summary>
        /// Tags seen in the log that TagKinds has no entry for.
        ///
        /// The table above claims to be exhaustive, and it was not: [Macros], [history]
        /// and [sidebar] were all being written and none of them had a colour. Nothing
        /// announced that — an uncategorised line looks like an ordinary one, so the only
        /// way to notice was to go looking. This records them instead, and the stats
        /// header says so, which turns "the table quietly fell behind the code" into
        /// something the window reports about itself.
        ///
        /// Deliberately not logged: this runs from the classifier while rendering the
        /// log, and writing a log line from there would feed the thing it is reading.
        /// </summary>
        private static readonly System.Collections.Generic.SortedSet<string> UnknownTags =
            new System.Collections.Generic.SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object UnknownTagsSync = new object();

        internal static string[] UncategorisedTags()
        {
            lock (UnknownTagsSync)
            {
                var a = new string[UnknownTags.Count];
                UnknownTags.CopyTo(a);
                return a;
            }
        }

        private static LineKind KindOf(string line)
        {
            // By the level the logger wrote (see IsProblem), not by "[ERROR]" anywhere in the text.
            string level = Utils.LogLine.LevelOf(line);
            if (level == "ERROR") { return LineKind.Error; }
            if (level == "WARN") { return LineKind.Warn; }

            string tag = TagOf(line);
            if (tag != null && TagKinds.TryGetValue(tag, out LineKind kind))
            {
                return kind;
            }

            if (tag != null)
            {
                lock (UnknownTagsSync)
                {
                    // Capped: Logger.Swallow builds its tag from a method name, so a
                    // pathological run could otherwise add one entry per call site.
                    if (UnknownTags.Count < 40) { UnknownTags.Add(tag); }
                }
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
                case LineKind.Accounts: return AcctColor;
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

        // ── Drawing many lines at once ─────────────────────────────────────────────
        //
        // BOTH panes used to be filled a line at a time: move the caret to the end, set a
        // colour, AppendText, then select each filter match and set its highlight. Every one
        // of those is a separate message to the RichEdit control, which re-lays-out after
        // each. Measured with the real window (scratchpad/debugcost):
        //   · the stats pane, rebuilt every 500 ms: ~47 ms a time, up to 105 ms — about a
        //     tenth of the UI thread, the whole time Live debug was open;
        //   · the event log's full reload, which OPENING the window and every pause in the
        //     filter box trigger: 2.6 s for a 1,500-line ring, 4.4 s when every line matched
        //     the filter — Tempo frozen for all of it.
        // Built as one RTF string and handed over in ONE assignment, the same content —
        // identical text, identical colours, highlights included — takes ~2.5 ms for the
        // stats pane. The per-line path stays as the fallback if RTF is ever refused.

        /// <summary>
        /// RTF for <paramref name="lines"/>: each line in the colour <paramref name="colourOf"/>
        /// gives it, with the filter's matches highlighted when <paramref name="highlight"/> is
        /// set, in <paramref name="font"/>. Each line ends with a paragraph mark, exactly as
        /// AppendText(line + NewLine) left it.
        /// </summary>
        private string BuildLinesRtf(System.Collections.Generic.IList<string> lines, Func<string, Color> colourOf,
                                     Font font, bool highlight)
        {
            var rtf = new RtfBuilder(font, lines.Count * 96);
            foreach (string raw in lines)
            {
                string line = raw ?? "";
                Color fore = colourOf(line);
                if (highlight)
                {
                    int at = 0;
                    foreach (var span in _parsedFilter.Highlights(line))
                    {
                        int s = Math.Max(at, Math.Min(span.Key, line.Length));
                        int e = Math.Min(line.Length, span.Key + span.Value);
                        if (e <= s) { continue; }
                        rtf.Run(line.Substring(at, s - at), fore);
                        rtf.Run(line.Substring(s, e - s), _matchForeColor, _matchBackColor);
                        at = e;
                    }
                    rtf.Run(line.Substring(at), fore);
                }
                else
                {
                    rtf.Run(line, fore);
                }
                rtf.EndLine();
            }
            return rtf.ToRtf();
        }

        private void AppendColored(string line)
        {
            int start = _log.TextLength;
            _log.SelectionStart = start;
            _log.SelectionLength = 0;
            _log.SelectionColor = ColorFor(line);
            _log.AppendText(line + Environment.NewLine);

            // Mark WHY this line is here. On a filtered view of a busy log, a line can match
            // on a word buried 200 characters in, and picking it out by eye is the slow part
            // of reading the thing. Only the words actually searched for are marked — an
            // excluded term has, by definition, no hit to point at.
            if (!_parsedFilter.HasHighlightableText) { return; }
            foreach (var span in _parsedFilter.Highlights(line))
            {
                try
                {
                    _log.SelectionStart = start + span.Key;
                    _log.SelectionLength = span.Value;
                    _log.SelectionBackColor = _matchBackColor;
                    _log.SelectionColor = _matchForeColor;
                }
                catch { break; }
            }
            // Leave the caret past the end with default colours, or the NEXT append inherits
            // the highlight and paints an unmatched line as if it had matched.
            _log.SelectionStart = _log.TextLength;
            _log.SelectionLength = 0;
            _log.SelectionBackColor = _log.BackColor;
        }

        /// <summary>
        /// Moves the view to the next (or previous) highlighted match and selects it.
        /// Wraps around, because a search that silently stops at the end of a 1500-line log
        /// reads as "no more matches" when there are twenty above you.
        /// </summary>
        // Enter in the filter box has already jumped once on the current query (see its KeyDown).
        private bool _enterJumped;

        private void JumpToMatch(bool forward, bool focusLog = true)
        {
            if (_parsedFilter == null || !_parsedFilter.HasHighlightableText) { return; }
            string text = _log.Text;
            if (string.IsNullOrEmpty(text)) { return; }

            var hits = _parsedFilter.Highlights(text);
            if (hits.Count == 0) { return; }

            int caret = _log.SelectionStart;
            int target = -1;
            if (forward)
            {
                foreach (var h in hits) { if (h.Key > caret) { target = h.Key; break; } }
                if (target < 0) { target = hits[0].Key; }                 // wrap to the first
            }
            else
            {
                for (int i = hits.Count - 1; i >= 0; i--)
                {
                    if (hits[i].Key < caret) { target = hits[i].Key; break; }
                }
                if (target < 0) { target = hits[hits.Count - 1].Key; }    // wrap to the last
            }

            int len = 0;
            foreach (var h in hits) { if (h.Key == target) { len = h.Value; break; } }
            try
            {
                _log.SelectionStart = target;
                _log.SelectionLength = len;
                _log.ScrollToCaret();
                // The log keeps showing the selection without focus (HideSelection is off), so Enter in
                // the filter box can leave the caret where it is and be pressed again.
                if (focusLog) { _log.Focus(); }
            }
            catch { }
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
            // Not while a selection is being dragged in the log: lines inserted under the mouse move the
            // text out from under it. They wait in the queue (bounded — the ring is the backstop).
            if (LogSelectionDragInProgress()) { return; }

            // Back from minimised: catch the stats and the log up now. Checked here, on this 100 ms tick,
            // rather than in a Resize handler — WindowState can still read Minimized while the restore's
            // own Resize is raised, and a missed restore left the log without what arrived meanwhile.
            if (IsHandleCreated && WindowState != FormWindowState.Minimized)
            {
                if (_statsStale) { _statsStale = false; RefreshStats(); }
                if (_reloadOnRestore && !_pause.Checked)
                {
                    _reloadOnRestore = false;
                    ReloadFromRing(keepPlace: true);   // it takes over the queue too
                    return;
                }
            }

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

            // Minimised, nobody is reading it: catch up once, from the ring, when it comes back.
            if (WindowState == FormWindowState.Minimized)
            {
                _reloadOnRestore = true;
                return;
            }

            // Dropped lines mean the view is behind the ring — rebuild from the ring,
            // which is authoritative, rather than showing a gap with no explanation.
            if (overflowed)
            {
                ReloadFromRing(keepPlace: true);
                return;
            }

            var passing = new System.Collections.Generic.List<string>(batch.Length);
            foreach (string line in batch)
            {
                if (PassesFilter(line)) { passing.Add(line); }
            }
            if (passing.Count == 0) { return; }

            // Past the cap, start again from the ring (which trims itself) rather than growing —
            // keeping the reader on the line they were reading.
            if (_shownLines + passing.Count > 2200)
            {
                ReloadFromRing(keepPlace: true);
                return;
            }

            // The reader's place: the selection AND the scroll position, kept apart. Scrolling with the
            // wheel leaves the caret where it was (at the end, after a reload), so putting the caret back
            // dragged the view down to it — new lines pulled you away from the older ones you had
            // scrolled up to read — and SelectionLength = 0 wiped any text selected to copy. A
            // selection also stops the view following new lines, or they would carry it away.
            int selStart = _log.SelectionStart, selLen = _log.SelectionLength;
            bool follow = selLen == 0 && FollowingTail();
            bool haveScroll = TryGetScrollPos(_log, out Point scroll);

            SetRedraw(false);
            try
            {
                // An assigned .Rtf ends without a paragraph mark after its last line — that mark becomes
                // the document's own end — so text inserted at the end continued that line: the first
                // event after every reload (opening the window, a filter) was glued onto the one before.
                EnsureLogEndsWithBreak();
                int before = _log.TextLength;
                bool done = false;
                try
                {
                    // The whole batch as ONE insertion at the end (see BuildLinesRtf).
                    _log.Select(before, 0);
                    _log.SelectedRtf = BuildLinesRtf(passing, ColorFor, _log.Font, _parsedFilter.HasHighlightableText);
                    done = true;
                }
                catch (Exception ex)
                {
                    Utils.Logger.Swallow("DebugForm batch append", ex);
                    // A refused insert can leave the part before the fault behind; take it out first.
                    try
                    {
                        if (_log.TextLength > before)
                        {
                            _log.Select(before, _log.TextLength - before);
                            _log.SelectedText = "";
                        }
                    }
                    catch { }
                }

                if (!done)
                {
                    foreach (string line in passing) { AppendColored(line); }
                }
                _shownLines += passing.Count;
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
                try { _log.Select(selStart, selLen); } catch { }
                if (haveScroll) { SetScrollPos(_log, scroll); }
            }
            _log.Invalidate();
            UpdateMatchCount(_shownLines, LinesSinceClear());
        }

        // Work left while the window was minimised — lines that arrived, a stats rebuild — done on restore
        // (see FlushPending: the 100 ms tick notices, whatever order the restore's own events came in).
        private bool _reloadOnRestore;
        private bool _statsStale;

        private static bool TryGetScrollPos(RichTextBox box, out Point pos)
        {
            pos = Point.Empty;
            try
            {
                if (box == null || !box.IsHandleCreated) { return false; }
                SendMessage(box.Handle, EM_GETSCROLLPOS, IntPtr.Zero, ref pos);
                return true;
            }
            catch { return false; }
        }

        private static void SetScrollPos(RichTextBox box, Point pos)
        {
            try
            {
                if (box != null && box.IsHandleCreated) { SendMessage(box.Handle, EM_SETSCROLLPOS, IntPtr.Zero, ref pos); }
            }
            catch { }
        }

        /// <summary>Gives the log a paragraph mark after its last line, when it has none (see FlushPending).</summary>
        private void EnsureLogEndsWithBreak()
        {
            int end = _log.TextLength;
            if (end == 0) { return; }
            // WordWrap is off, so the line holding the end is the last line of text: with content on it
            // there is no break after it yet.
            int last = _log.GetLineFromCharIndex(end);
            if (_log.GetFirstCharIndexFromLine(last) < end)
            {
                _log.Select(end, 0);
                _log.SelectedText = Environment.NewLine;
            }
        }

        /// <summary>
        /// True while the left button is down over the log — a selection being dragged, or its scroll bar
        /// (the bounds, not the client area, so the scroll thumb counts too).
        /// </summary>
        private bool LogSelectionDragInProgress()
        {
            try
            {
                if (_log == null || _log.IsDisposed || !_log.IsHandleCreated || _log.Parent == null) { return false; }
                return (Control.MouseButtons & MouseButtons.Left) != 0
                       && _log.Parent.RectangleToScreen(_log.Bounds).Contains(Control.MousePosition);
            }
            catch { return false; }
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

        /// <summary>
        /// Rebuilds the log from the ring. <paramref name="keepPlace"/>: a rebuild the reader did not ask
        /// for — the trim at 2,200 lines, a burst, unpausing, a theme or colour change — leaves them on the
        /// line they were reading, when they had scrolled up from the newest. It threw them to the bottom,
        /// and with a trace on the trim came round every minute or so.
        /// </summary>
        private void ReloadFromRing(bool keepPlace = false)
        {
            try
            {
                string anchor = null;
                int anchorOffset = 0;
                if (keepPlace && _log.TextLength > 0 && !FollowingTail())
                {
                    anchor = TopEntryLine(out anchorOffset);
                }

                // The ring already holds everything still waiting for the 100 ms append, so the queue is
                // emptied first: those lines were shown by this rebuild AND appended again a moment later —
                // every filter change, Problems only or unpause doubled the newest lines (with a trace on,
                // dozens of them).
                lock (_pendingLock) { _pending.Clear(); _pendingOverflow = false; }

                var shown = new System.Collections.Generic.List<string>();
                int total = 0;
                foreach (string line in SnapshotSinceClear())
                {
                    total++;
                    if (PassesFilter(line)) { shown.Add(line); }
                }

                SetRedraw(false);
                try
                {
                    bool done = false;
                    try
                    {
                        // One assignment for the whole view — see BuildLinesRtf for what the
                        // line-at-a-time version cost (seconds, on every filter change).
                        _log.Rtf = BuildLinesRtf(shown, ColorFor, _log.Font, _parsedFilter.HasHighlightableText);
                        done = true;
                    }
                    catch (Exception ex) { Utils.Logger.Swallow("DebugForm reload", ex); }

                    if (!done)
                    {
                        _log.Clear();
                        foreach (string line in shown) { AppendColored(line); }
                    }
                }
                finally
                {
                    SetRedraw(true);
                }

                _shownLines = shown.Count;
                _log.SelectionStart = _log.TextLength;
                if (anchor == null || !ScrollLogTo(anchor, anchorOffset))
                {
                    _log.ScrollToCaret();
                }
                _log.Invalidate();
                UpdateMatchCount(shown.Count, total);
            }
            catch { }
        }

        /// <summary>
        /// The first line of the entry at the top of the view, and how many lines below it the view starts
        /// (an error's stack trace runs over several lines, and "   at …" repeats from entry to entry, so the
        /// entry's own first line — it begins with its time — is what is looked for after a rebuild).
        /// </summary>
        private string TopEntryLine(out int offset)
        {
            offset = 0;
            try
            {
                string[] lines = _log.Lines;
                int first = (int)SendMessage(_log.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
                for (int i = Math.Min(first, lines.Length - 1); i >= 0; i--)
                {
                    if (StartsWithTime(lines[i])) { offset = first - i; return lines[i]; }
                }
            }
            catch { }
            return null;
        }

        /// <summary>"2026-10-07 21:06:21.900 [" — a logger line's own first line.</summary>
        private static bool StartsWithTime(string line)
        {
            return line != null && line.Length > 25 && char.IsDigit(line[0]) && line[4] == '-' && line[10] == ' '
                   && line[19] == '.' && line[23] == ' ' && line[24] == '[';
        }

        /// <summary>Scrolls the log so <paramref name="entry"/>'s line, plus <paramref name="offset"/>, is at the top.</summary>
        private bool ScrollLogTo(string entry, int offset)
        {
            try
            {
                int at = Array.LastIndexOf(_log.Lines, entry);
                if (at < 0) { return false; }
                int first = (int)SendMessage(_log.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
                SendMessage(_log.Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)(at + offset - first));
                return true;
            }
            catch { return false; }
        }

        // ── Clear view ───────────────────────────────────────────────────────────────
        //
        // It emptied the box and nothing else, so every rebuild — a filter, Problems only, a theme change,
        // the trim at 2,200 lines — read the whole ring again and put the cleared lines straight back. It
        // now remembers WHERE it cleared (the logger numbers its lines), and the rebuilds, the count beside
        // the filter, Copy and Save all start from there.
        private long _clearedBefore;
        private DateTime _clearedAt;

        /// <summary>The ring, without what Clear view put away.</summary>
        private string[] SnapshotSinceClear()
        {
            string[] ring = Utils.Logger.Snapshot(out long firstNumber);
            if (_clearedBefore <= firstNumber) { return ring; }
            int skip = (int)Math.Min(ring.Length, _clearedBefore - firstNumber);
            var kept = new string[ring.Length - skip];
            Array.Copy(ring, skip, kept, 0, kept.Length);
            return kept;
        }

        /// <summary>How many lines the ring holds since Clear view (all of them when it wasn't used).</summary>
        private int LinesSinceClear()
        {
            int ring = Utils.Logger.BufferedCount;
            if (_clearedBefore <= 0) { return ring; }
            long since = Utils.Logger.WrittenCount - _clearedBefore;
            return (int)Math.Max(0, Math.Min(ring, since));
        }

        private void ClearView()
        {
            lock (_pendingLock) { _pending.Clear(); _pendingOverflow = false; }
            _clearedBefore = Utils.Logger.WrittenCount;
            _clearedAt = DateTime.Now;
            _log.Clear();
            _shownLines = 0;
            UpdateMatchCount(0, 0);
        }

        /// <summary>
        /// "142 of 2,310 lines" beside the filter box.
        ///
        /// Without it a filter that matches nothing looks identical to a log that has gone
        /// quiet, and a typo in the query is indistinguishable from "the thing you are
        /// looking for never happened" — which is the wrong conclusion to hand someone
        /// debugging. Zero matches says so in the warning colour.
        /// </summary>
        private void UpdateMatchCount(int shown, int total)
        {
            if (_matchCount == null) { return; }
            bool filtering = !_parsedFilter.IsEmpty || _problemsOnly.Checked;
            string text;
            if (!filtering)
            {
                text = Utils.Localization.F("{0} lines", total.ToString("N0"));
                _matchCount.ForeColor = _statsLabelColor;
            }
            else
            {
                text = Utils.Localization.F("{0} of {1}", shown.ToString("N0"), total.ToString("N0"));
                _matchCount.ForeColor = shown == 0 ? _warnColor : _statsLabelColor;
            }
            // Say that older lines were put away, and when — or a cleared log reads as a quiet one.
            if (_clearedBefore > 0)
            {
                text += "  ·  " + Utils.Localization.F("cleared at {0}", _clearedAt.ToString("HH:mm:ss"));
            }
            if (_matchCount.Text != text) { _matchCount.Text = text; }
            // Only when the query changed. This runs after every batch of new lines, and
            // re-setting a tooltip that is on screen makes it flicker while the log streams.
            string tip = _parsedFilter.Describe() + Environment.NewLine + FilterHelp;
            if (!string.Equals(tip, _filterTipText, StringComparison.Ordinal))
            {
                _filterTipText = tip;
                try { TipStyle.Set(_filterTip, _filter, tip); }
                catch { }
            }
        }

        /// <summary>What the filter box understands, shown on hover — the syntax is no use unseen.</summary>
        private static string FilterHelp =>
            Utils.Localization.T("two words = both  ·  \"a phrase\"  ·  -hide  ·  tag:captions  ·  level:warn")
            + Environment.NewLine
            + Utils.Localization.T("F3 jumps to the next match, Shift+F3 the previous one.")
            + Environment.NewLine
            + Utils.Localization.T("Esc clears the filter.");

        /// <summary>
        /// F3 / Shift+F3 step through the matches, the way every other search box works.
        /// ProcessCmdKey rather than a KeyDown handler so it fires wherever focus is —
        /// the filter box, the log, or a checkbox in the strip.
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if ((keyData & Keys.KeyCode) == Keys.F3)
            {
                JumpToMatch((keyData & Keys.Shift) != Keys.Shift);
                return true;
            }
            // Ctrl+F puts the caret in the filter box and selects what is there, so a new
            // query replaces the old one with one keystroke instead of needing it cleared.
            if (keyData == (Keys.Control | Keys.F))
            {
                try { _filter.Focus(); _filter.SelectAll(); } catch { }
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private string BuildExportText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("== Tempo live debug export · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ==");
            // Export the stats we last RENDERED, not _stats.Text: the RichTextBox
            // normalises newlines, and _lastStats is the exact provider output.
            sb.AppendLine(_lastStats);

            // Honour the filter. Someone who narrowed 1500 lines down to the eleven that
            // matter and then pressed Copy was handed all 1500 again — the filtering, which
            // is the work, was thrown away at exactly the moment they went to share it.
            // Clear view the same way: what was cleared to watch what happens next stays out.
            string[] ring = SnapshotSinceClear();
            string cleared = _clearedBefore > 0
                ? "lines before " + _clearedAt.ToString("HH:mm:ss") + " were cleared from the view and are not included."
                : null;
            bool filtering = !_parsedFilter.IsEmpty || _problemsOnly.Checked;
            if (filtering)
            {
                int shown = 0, total = 0;
                var kept = new StringBuilder();
                foreach (string line in ring)
                {
                    total++;
                    if (!PassesFilter(line)) { continue; }
                    shown++;
                    kept.AppendLine(line);
                }
                sb.AppendLine("== recent events · filtered ==");
                sb.AppendLine("filter: " + (_parsedFilter.IsEmpty ? "(none)" : _parsedFilter.Query)
                              + (_problemsOnly.Checked ? "   + problems only" : ""));
                sb.AppendLine("showing " + shown + " of " + total + " line(s); "
                              + (total - shown) + " hidden by the filter.");
                if (cleared != null) { sb.AppendLine(cleared); }
                sb.Append(kept);
                return sb.ToString();
            }

            sb.AppendLine("== recent events ==");
            if (cleared != null) { sb.AppendLine(cleared); }
            foreach (string line in ring)
            {
                sb.AppendLine(line);
            }
            return sb.ToString();
        }

        /// <summary>Saves the report where the user picks; true when it was written.</summary>
        private bool SaveToFile()
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
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Utils.Localization.F("Couldn't save: {0}", ex.Message), "Tempo",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return false;
        }
    }
}
