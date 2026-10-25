using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// The Live Captions tab.
    ///
    /// Captions had no home of their own: the controls lived in one crowded card at the
    /// bottom of Settings, and the two things you actually LOOK at — the caption bar and
    /// the history — were separate always-on-top windows that open somewhere over the
    /// desktop. Users reported not being able to find the history and assuming captions
    /// were broken, which is the complaint this tab exists to answer.
    ///
    /// It is deliberately the OPERATIONAL view, not a second settings page: start/stop,
    /// what the engine is doing right now, the running transcript, and the actions you
    /// take while it runs. Every persisted preference stays in Settings, so there is
    /// exactly one control per setting and nothing to keep in sync — this codebase has
    /// been bitten enough times by two widgets owning one value.
    /// </summary>
    public partial class MainForm
    {
        private Label _capStatePill;
        private Button _capStartStopBtn;
        private Label _capEngineLine;
        private Label _capSourceLine;
        private Label _capDelayLine;
        private Button _capFasterModelBtn;
        private ThemedRichTextBox _capTranscript;
        private Button _capCopyBtn;            // relabels "Copy all" → "Copy shown" while filtering
        private Button _capSaveBtn;
        private ToolTip _capTip;               // the filter syntax, and what Save will write
        private TextBox _capSearchBox;
        private System.Windows.Forms.Timer _capSearchDebounce;
        private Label _capCountLabel;
        private Button _capOverlayBtn;
        private Button _capHistoryBtn;
        private Button _capMoveBtn;
        private CaptionLevelMeter _capLevel;
        private Label _capQualityLine;
        // The Captions tab's copy of the language picker. Its twin on the Settings page
        // is _captionLangCombo; both go through ApplyCaptionLanguage so they cannot
        // disagree with each other or with what is on disk.
        private ComboBox _capLangCombo;
        private Label _capLangLabel;
        // Who is talking: each speaker with their face (see SpeakerStrip / Utils.SpeakerLinker).
        private SpeakerStrip _capSpeakerStrip;
        private Button _capSpeakerActionBtn;     // "Turn on speaker labels" / "Use faces too", when either is off

        // What the transcript box is currently showing, so the (potentially 500-line)
        // rebuild is skipped when nothing changed. Starts as null rather than "" so the
        // FIRST pass always renders — otherwise an empty transcript compared equal to its
        // own initial value and returned early, and the "nothing transcribed yet" hint
        // under the box was never filled in.
        private string _capRenderedText;
        private string _capFilter = "";

        private void BuildCaptionsTab()
        {
            var page = new BackdropTabPage(Localization.T("Captions")) { AutoScroll = true };
            page.Name = "captions";   // stable key for LastTabKey

            // ── Status + start/stop ───────────────────────────────────────────
            var status = UiFactory.Group(Localization.T("Live Captions"), 12, 12, 696, 186, CardIcon.Caption);

            _capStatePill = UiFactory.Label("Off", 16, 30, FontStyle.Bold, 15f);
            // Every state it can say (RefreshCaptionsTab), in 15 pt bold beside the Start button.
            LayoutFitter.DeclareCaptions(_capStatePill, Localization.T("Off"), Localization.T("⚠  Not hearing anything"),
                Localization.T("◌  Loading the speech model…"), Localization.T("◌  Waiting for Windows captions…"),
                Localization.T("●  Listening…"));
            status.Controls.Add(_capStatePill);

            _capEngineLine = UiFactory.Caption("", 16, 60);
            CapStatusLine(_capEngineLine);
            status.Controls.Add(_capEngineLine);
            _capSourceLine = UiFactory.Caption("", 16, 80);
            CapStatusLine(_capSourceLine);
            status.Controls.Add(_capSourceLine);

            // The delay line. "The video says something, then the caption catches up
            // later" is the complaint this answers — in seconds, with the reason.
            _capDelayLine = UiFactory.Caption("", 16, 100);
            // The button column starts at 520; cap the label short of it so a longer
            // number can never grow underneath the button (it wraps instead).
            _capDelayLine.MaximumSize = new Size(492, 0);
            status.Controls.Add(_capDelayLine);

            _capStartStopBtn = UiFactory.PrimaryButton("Start captions", 520, 26, 156, 34, _theme);
            // Every caption these buttons switch between, so the layout checks measure each one.
            LayoutFitter.DeclareCaptions(_capStartStopBtn, Localization.T("Start captions"), Localization.T("Stop captions"));
            _capStartStopBtn.Click += (s, e) =>
            {
                // ToggleLiveCaptions, not a private copy of its body. It carries a
                // re-entrancy guard that exists so the on/off state cannot desync or
                // crash mid-transition — rolling our own here bypassed that guard, so
                // this button and the hotkey/tray could interleave.
                try { ToggleLiveCaptions(); }
                catch (Exception ex) { Logger.Warn("[Captions tab] start/stop failed: " + ex.Message); }
                RefreshCaptionsTab();
            };
            status.Controls.Add(_capStartStopBtn);

            var settingsLink = UiFactory.Button("All caption settings…", 520, 66, 156, 28);
            settingsLink.Click += (s, e) =>
            {
                // Settings is deliberately still the only home for the preferences.
                // Located by key rather than "the last tab" so adding another tab later
                // cannot quietly send this button somewhere else.
                try
                {
                    int idx = IndexOfTabKey("settings");
                    if (idx >= 0) { _tabs.SelectedIndex = idx; }
                    // And to the Live Captions card itself: it is far down a long page, and the button said
                    // "caption settings" but left you wherever Settings had last been scrolled to. After the
                    // switch has laid the page out, or that pass would put the scroll back.
                    var card = _captionOverlayCheck?.Parent as GroupBox;
                    if (card != null) { BeginInvoke((Action)(() => ScrollSettingsTo(card))); }
                }
                catch { }
            };
            status.Controls.Add(settingsLink);

            // Only appears when the pace figure says the model genuinely cannot keep up.
            // Third in the right-hand button column, NOT beside the delay text: that
            // text is AutoSize and grows with the numbers in it, so a button next to it
            // ended up underneath the words.
            _capFasterModelBtn = UiFactory.Button("Use a faster model", 520, 102, 156, 28);
            _capFasterModelBtn.Visible = false;
            _capFasterModelBtn.Click += (s2, e2) =>
            {
                if (_capOfferGpuEngine) { EnableGpuCaptionEngine(); } else { UseFasterCaptionModel(); }
            };
            status.Controls.Add(_capFasterModelBtn);

            // Live input level. For a feature built for people who cannot hear the audio,
            // "is it even receiving sound?" was unanswerable from the UI: the engine
            // recomputes LevelDb ~25 times a second and it only ever reached Live debug.
            // Silence and a broken capture looked identical — both produce no captions.
            var inputLabel = UiFactory.Caption("Input:", 16, 126);
            status.Controls.Add(inputLabel);
            // After the label, not at a fixed x=60: "Entrada:", "Eingang:" and "Ingresso:" ran
            // 6–10 px under the meter, which painted over the colon and the last letter. The
            // meter keeps its right edge.
            int meterLeft = Math.Max(60, LayoutRight(inputLabel) + 6);   // the label sizes itself in screen pixels
            _capLevel = new CaptionLevelMeter { Left = meterLeft, Top = 126, Width = 360 - meterLeft, Height = 14 };
            status.Controls.Add(_capLevel);

            // Confidence / clipping / language lock — all computed by the engine already.
            _capQualityLine = UiFactory.Caption("", 16, 148);
            _capQualityLine.MaximumSize = new Size(660, 0);
            status.Controls.Add(_capQualityLine);

            // ── Speakers ──────────────────────────────────────────────────────
            // Who is talking, with their faces. The speaker labels, the voice matching and the face
            // & mouth analysis only ever showed up as text in Live debug; nothing here said who
            // "Speaker 2" was. The transcript card gave up the height (the tab still fits its
            // 689 px without scrolling, so the language picker stays in view). Click a speaker to give
            // them a name; hover one to see their face large.
            var speakers = UiFactory.Group(Localization.T("Speakers"), 12, 210, 696, 88, CardIcon.People);
            _capSpeakerStrip = new SpeakerStrip
            {
                Left = 16,
                Top = 28,
                Width = 660,
                Height = 52,
                Font = new Font("Segoe UI", 9f),
            };
            _capSpeakerStrip.SetTheme(_theme);
            _capSpeakerStrip.ChipClicked += RenameSpeaker;
            speakers.Controls.Add(_capSpeakerStrip);
            // Names reach every surface through these: the transcript's lines (and so the history
            // window, the saved file and the subtitles) and the caption bar's labels.
            _transcript.SpeakerName = SpeakerDisplayName;
            _transcript.SpeakerIdentity = _speakerLinker.Incarnation;
            // A long session's oldest lines go to its transcript file as they leave the 500 kept here.
            _transcript.Spilling = SpillTranscriptLines;
            CaptionOverlayForm.SpeakerNameFor = SpeakerDisplayName;
            _capSpeakerActionBtn = UiFactory.Button("Turn on speaker labels", 476, 40, 200, 28);
            _capSpeakerActionBtn.Visible = false;
            _capSpeakerActionBtn.Click += (s, e) => OnCaptionSpeakerAction();
            speakers.Controls.Add(_capSpeakerActionBtn);
            LayoutFitter.DeclareOverlay(_capSpeakerActionBtn,
                "sits over the right end of the speaker strip, which leaves that end empty while it is showing");

            // ── The running transcript ────────────────────────────────────────
            var live = UiFactory.Group(Localization.T("Transcript"), 12, 310, 696, 248, CardIcon.Caption);

            live.Controls.Add(UiFactory.Label("Find:", 16, 32));
            // x=96, not 60: "Find:" fits in 44px but "Rechercher :" needs 66, and the
            // label was overprinting this box in four of the five languages.
            // Its width is settled below, once the buttons to its right have measured
            // their own captions: the box takes what they leave (200 px in English).
            _capSearchBox = new TextBox
            {
                Left = 96,
                Top = 28,
                Width = 200,
                PlaceholderText = Localization.T("filter the transcript…")
            };

            // Debounced, like Live debug's filter and for the same measured reason: each
            // pass walks the whole history (up to 500 lines) and rebuilds the box, and
            // TextChanged fires per KEYSTROKE — so typing a six-letter word did six full
            // rebuilds back-to-back on the UI thread.
            _capSearchDebounce = new System.Windows.Forms.Timer { Interval = 180 };
            _capSearchDebounce.Tick += (s, e) =>
            {
                try
                {
                    _capSearchDebounce.Stop();
                    // Trimmed: a trailing space is invisible in the box but matched
                    // literally, so typing "hello " quietly found nothing in a transcript
                    // full of "hello". (The parser splits on whitespace anyway now, but the
                    // trimmed text is still what the "Nothing matches …" message quotes.)
                    _capFilter = (_capSearchBox.Text ?? "").Trim();
                    // Text-only: tag: and level: are log fields and mean nothing here, so
                    // they stay ordinary words rather than becoming a term that never matches.
                    _capParsedFilter = Utils.LogFilter.ParseTextOnly(_capFilter);
                    RefreshTranscriptButtonLabels();
                    _capRenderedText = null;   // force a re-render through the filter
                    RefreshCaptionTranscript();
                }
                catch (Exception ex) { Logger.Swallow("caption filter tick", ex); }
            };
            _capSearchBox.TextChanged += (s, e) =>
            {
                try { _capSearchDebounce.Stop(); _capSearchDebounce.Start(); }
                catch (Exception ex) { Logger.Swallow("caption filter", ex); }
            };
            // Escape clears the filter — the fastest way back to the whole transcript,
            // and the one people try first.
            _capSearchBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Escape || _capSearchBox.TextLength == 0) { return; }
                e.Handled = true;
                e.SuppressKeyPress = true;      // no ding, and Escape stays ours here
                _capSearchBox.Clear();
            };
            live.Controls.Add(_capSearchBox);

            // What the box understands, where someone will actually see it. It is a small
            // query language now, shared with Live debug's filter.
            _capTip = new ToolTip { AutoPopDelay = 20000, InitialDelay = 350, ReshowDelay = 120 };
            TipStyle.Set(_capTip, _capSearchBox, Localization.T(
                "two words = both  ·  \"a phrase\"  ·  -hide a word"));
            // No tooltip on the speaker strip: it shows its own preview of whoever is under the
            // mouse (face, name, how to rename, that the picture is never saved), and a second
            // tooltip there would pop up on top of it.

            _capCopyBtn = UiFactory.Button("Copy all", 312, 26, 104, 28);
            LayoutFitter.DeclareCaptions(_capCopyBtn, Localization.T("Copy all"), Localization.T("Copy shown"));
            _capCopyBtn.Click += (s, e) => CopyTranscript();
            live.Controls.Add(_capCopyBtn);

            _capSaveBtn = UiFactory.Button("Save…", 424, 26, 96, 28);
            _capSaveBtn.Click += (s, e) => SaveTranscript();
            live.Controls.Add(_capSaveBtn);
            RefreshTranscriptButtonLabels();

            var srtBtn = UiFactory.Button("Subtitles…", 524, 26, 100, 28);
            srtBtn.Click += (s, e) => ExportTranscriptSubtitles();
            live.Controls.Add(srtBtn);

            var clearBtn = UiFactory.Button("Clear", 630, 26, 46, 28);
            clearBtn.Click += (s, e) => ClearCaptionTranscript();
            live.Controls.Add(clearBtn);

            // The row, packed from the right edge: each button as wide as its own caption
            // needs (never narrower than its English design width, so English is unchanged),
            // and the search box takes whatever is left.
            //
            // Copy is sized for the WIDER of its two captions. It turns into "Copy shown" the
            // moment a filter is typed, and "Angezeigtes kopieren" (120 px of text), "Copiar lo
            // mostrado" (109) and "Copiar o mostrado" (106) did not fit its 104 px — cut off
            // at runtime, long after the start-up layout check had passed the row as clean.
            // In 100% units (FitToText96): this row is built before the window scales, and measured in
            // screen pixels it was scaled twice — every button 1.5× too wide on a 150% laptop, the row
            // packed leftwards from the card's edge into the transcript's filter box. "Copy all" is
            // sized for both its captions (declared where it is built).
            const int capPad = 12;
            UiFactory.FitToText96(_capCopyBtn, 104, capPad);
            UiFactory.FitToText96(_capSaveBtn, 96, capPad);
            UiFactory.FitToText96(srtBtn, 100, capPad);
            UiFactory.FitToText96(clearBtn, 46, capPad);
            clearBtn.Left = 676 - clearBtn.Width;
            srtBtn.Left = clearBtn.Left - 6 - srtBtn.Width;
            _capSaveBtn.Left = srtBtn.Left - 4 - _capSaveBtn.Width;
            _capCopyBtn.Left = _capSaveBtn.Left - 8 - _capCopyBtn.Width;
            _capSearchBox.Width = Math.Max(120, _capCopyBtn.Left - 16 - _capSearchBox.Left);

            // A RichTextBox, not a TextBox: the timestamps read as timestamps (muted) and a
            // filter MARKS what it matched, the way Live debug's does — in a plain box the
            // words you searched for looked exactly like every other word on the line.
            // The font is set explicitly to what it inherited before (the tab control's), so
            // the transcript looks as it always has. Themed, because Windows drew its border as a
            // grey-and-white sunken edge on every theme.
            _capTranscript = new ThemedRichTextBox
            {
                Left = 16,
                Top = 62,
                Width = 660,
                Height = 158,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                WordWrap = true,
                BorderStyle = BorderStyle.FixedSingle,
                TabStop = false,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                DetectUrls = false,
                BackColor = _theme.InputBackground,
                ForeColor = _theme.Text,
            };
            _capTranscript.ApplyTheme(_theme);
            live.Controls.Add(_capTranscript);

            // Just under the transcript box (62+158=220), inside the card (248 tall). It once sat
            // at y=352 in a 348-tall card — below its own client area, clipped away — which is why
            // none of the three messages this label carries had ever been seen: the empty-state
            // hint ("Nothing transcribed yet — start captions and play something with speech."),
            // the line counter, and the "Nothing matches … press Esc to clear the filter." that
            // explains an empty box during a search.
            _capCountLabel = UiFactory.Caption("", 16, 226);
            _capCountLabel.AutoSize = false;
            _capCountLabel.Size = new Size(660, 16);
            // Doubles as the way back down while it is showing "N new below".
            _capCountLabel.Click += (s, e) => { if (_capNewBelow > 0) { JumpTranscriptToNewest(); } };
            live.Controls.Add(_capCountLabel);

            // ── Actions ───────────────────────────────────────────────────────
            // Stays 104 tall. A taller card pushed the page past the 689px client area
            // and put the language picker below the fold, which is a poor home for a
            // control this tab exists to expose — so the row shares line 2 instead.
            var actions = UiFactory.Group(Localization.T("Windows & model"), 12, 570, 696, 104, CardIcon.Gear);

            _capOverlayBtn = UiFactory.Button("Show caption bar", 16, 30, 172, 30);
            LayoutFitter.DeclareCaptions(_capOverlayBtn, Localization.T("Show caption bar"), Localization.T("Hide caption bar"));
            _capOverlayBtn.Click += (s, e) => ToggleCaptionOverlayBar();
            actions.Controls.Add(_capOverlayBtn);

            _capHistoryBtn = UiFactory.Button("Show history window", 198, 30, 190, 30);
            LayoutFitter.DeclareCaptions(_capHistoryBtn, Localization.T("Show history window"), Localization.T("Hide history window"));
            _capHistoryBtn.Click += (s, e) =>
            {
                try { ToggleCaptionHistoryWindow(); } catch (Exception ex) { Logger.Warn("[Captions tab] history: " + ex.Message); }
                RefreshCaptionsTab();
            };
            actions.Controls.Add(_capHistoryBtn);

            // Move mode, beside the two windows it moves. It lived only in the tray, where the only
            // way to know it was on was that the windows had stopped letting clicks through.
            // "Download model…" moved into the Models… menu to make room: it is a one-time job,
            // and Settings keeps its own button for it.
            _capMoveBtn = UiFactory.Button("Move captions", 398, 30, 160, 30);
            LayoutFitter.DeclareCaptions(_capMoveBtn, Localization.T("Move captions"), Localization.T("Done moving"));
            _capMoveBtn.Click += (s, e) =>
            {
                try { ToggleCaptionMoveMode(); } catch (Exception ex) { Logger.Warn("[Captions tab] move: " + ex.Message); }
                RefreshCaptionsTab();
            };
            actions.Controls.Add(_capMoveBtn);

            // A menu rather than a plain button, because BOTH rows of this card are full and
            // the card is pinned at 104 tall on purpose (a taller one pushes the language
            // picker below the 689 px fold — see the note above). The menu adds the
            // "use a model from anywhere" option without costing a single pixel.
            var modelsBtn = UiFactory.Button("Models…", 568, 30, 110, 30);
            modelsBtn.Click += (s, e) => ShowModelSourceMenu(modelsBtn, withDownload: true);
            actions.Controls.Add(modelsBtn);

            // Each toggle as wide as the WIDER of its two captions — they change at run time, long
            // after the start-up layout check has passed the row — packed into the card's width.
            const int actPad = 16;
            PackButtonRow(16, 680,
                new Control[] { _capOverlayBtn, _capHistoryBtn, _capMoveBtn, modelsBtn },
                new[] { 172, 190, 160, 110 },
                new[]
                {
                    WidestCaption(_capOverlayBtn, actPad, "Show caption bar", "Hide caption bar"),
                    WidestCaption(_capHistoryBtn, actPad, "Show history window", "Hide history window"),
                    WidestCaption(_capMoveBtn, actPad, "Move captions", "Done moving"),
                    WidestCaption(modelsBtn, actPad, "Models…")
                });

            actions.Controls.Add(UiFactory.Caption(
                "Captions run entirely on this PC — audio never leaves it.", 16, 70));

            // The same picker as Settings → Live Captions, on the tab where captions are
            // actually being watched. Both write through ApplyCaptionLanguage.
            _capLangLabel = UiFactory.Label("Spoken language:", 330, 68);
            actions.Controls.Add(_capLangLabel);
            _capLangCombo = UiFactory.Combo(446, 65, 232, CaptionLanguageLabels());
            // Seed it from settings HERE. Without this the combo sat at SelectedIndex −1
            // showing nothing until some other caption setting happened to fire the sync
            // funnel, and the first arrow-key press on a blank combo selected "Auto-detect"
            // as if the user had chosen it.
            _capLangCombo.SelectedIndex = _settings != null
                ? CaptionLanguageIndexFromCode(_settings.CaptionLanguage)
                : 0;
            _capLangCombo.SelectedIndexChanged += (s, e) =>
            {
                if (_suppressSettingsEvents || _settings == null) { return; }
                ApplyCaptionLanguage(CaptionLanguageCodeFromIndex(_capLangCombo.SelectedIndex));
            };
            actions.Controls.Add(_capLangCombo);
            // No room for the "why pin it" hint on this row — the engine line above
            // already reports what the language is doing, and Settings carries the hint.

            page.Controls.Add(status);
            page.Controls.Add(speakers);
            page.Controls.Add(live);
            page.Controls.Add(actions);
            _tabs.TabPages.Add(page);

            // The transcript takes the window's spare height (FitCaptionsTranscriptToPage). Hooked here,
            // before EnableAutoFit hooks the page's centring, so on a resize it runs first.
            _capLiveCard = live;
            _capActionsCard = actions;
            page.SizeChanged += (s, e) => FitCaptionsTranscriptToPage();
            page.ClientSizeChanged += (s, e) => FitCaptionsTranscriptToPage();
        }

        // The transcript card and the card under it; and their design measurements in the window's units,
        // taken the first time the fit runs (after the window has scaled).
        private GroupBox _capLiveCard, _capActionsCard;
        private int _capLiveH0 = -1, _capBoxH0, _capCountTop0, _capCardGap0;
        private bool _capFitBusy;

        /// <summary>
        /// The transcript takes the window's spare height. It was a fixed 158 px box — five lines of what was
        /// said — while a large or maximised window left a band of empty page under the last card, and the
        /// transcript is what this tab is watched for. Never shorter than designed, so a small window (a
        /// laptop) keeps the layout as it was; the Windows &amp; model card moves down with it, and the place
        /// the page centring notes for that card (CenterPageContent puts every control back there) follows.
        /// Only while the tab is on screen: a page that is not shown can report a size it does not have.
        /// </summary>
        private void FitCaptionsTranscriptToPage()
        {
            if (_capFitBusy || _capLiveCard == null || _capActionsCard == null || _capTranscript == null) { return; }
            var page = _capLiveCard.Parent as ScrollableControl;
            if (page == null || WindowState == FormWindowState.Minimized || page.ClientSize.Height <= 0) { return; }
            if (!IsCaptionsTabVisible()) { return; }
            if (!_autoFitBaseTop.TryGetValue(_capLiveCard, out int liveBase)) { return; }   // not laid out yet
            _capFitBusy = true;
            try
            {
                if (_capLiveH0 < 0)
                {
                    _capLiveH0 = _capLiveCard.Height;
                    _capBoxH0 = _capTranscript.Height;
                    _capCountTop0 = _capCountLabel.Top;
                    _capCardGap0 = _capActionsCard.Top - _capLiveCard.Bottom;
                }
                // Where the page would end at the design height, in the noted (unscrolled, uncentred) places
                // — which neither the scroll nor full-screen centring moves.
                int designBottom = liveBase + _capLiveH0 + _capCardGap0 + _capActionsCard.Height;
                int extra = Math.Max(0, page.ClientSize.Height - L(12) - designBottom);
                int liveH = _capLiveH0 + extra;
                if (_capLiveCard.Height == liveH) { return; }

                bool followTail = IsTranscriptScrolledToBottom() && _capTranscript.SelectionLength == 0;
                _capLiveCard.Height = liveH;
                _capTranscript.Height = _capBoxH0 + extra;
                _capCountLabel.Top = _capCountTop0 + extra;
                if (_autoFitBaseTop.ContainsKey(_capActionsCard)) { _autoFitBaseTop[_capActionsCard] = liveBase + liveH + _capCardGap0; }
                _capActionsCard.Top = _capLiveCard.Bottom + _capCardGap0;
                if (followTail && _capTranscript.TextLength > 0)
                {
                    // A taller box with the newest line still at its foot, not stranded mid-box.
                    _capTranscript.SelectionStart = _capTranscript.TextLength;
                    _capTranscript.ScrollToCaret();
                }
            }
            catch (Exception ex) { Logger.Swallow("FitCaptionsTranscriptToPage", ex); }
            finally { _capFitBusy = false; }
        }

        /// <summary>
        /// The engine the captions run on, for the engine line: while Tempo's engine runs, the one that
        /// actually loaded (whisper says which); otherwise the one that will be tried. It read the setting
        /// — "GPU (if available)" — even while the run was on the CPU, because the CPU/GPU choice only
        /// takes effect after a Tempo restart or because this PC has no usable GPU at all.
        /// </summary>
        private string CaptionEngineText(TempoTranscriber tr)
        {
            if (_settings == null) { return "CPU"; }
            string runtime = tr != null && (tr.IsRunning || tr.IsStarting) ? tr.RuntimeDescription : null;
            bool gpuWanted = _settings.CaptionTryGpu;
            if (gpuWanted && TempoTranscriber.GpuUnavailableReason != null)
            {
                // Asked for, and declined at load: restarting would not change that.
                return Localization.T("CPU (no usable GPU)");
            }
            string text = string.IsNullOrEmpty(runtime)
                ? (gpuWanted ? Localization.T("GPU (if available)") : "CPU")
                : runtime.StartsWith("Cpu", StringComparison.OrdinalIgnoreCase) ? "CPU" : "GPU (" + runtime + ")";
            if (GpuSettingNeedsRestart()) { text += " " + Localization.T("(restart Tempo to apply)"); }
            return text;
        }

        /// <summary>
        /// One status line that stops short of the button column (x=520). The engine and source lines
        /// grew with their contents — the model, the language read-out, "running behind…", the face
        /// analysis note — and in French the source line ran under "All caption settings…". Past its
        /// width a line ends in "…", and the whole of it is on hover.
        /// </summary>
        private static void CapStatusLine(Label line)
        {
            line.AutoSize = false;
            line.Size = new Size(492, 16);
            line.AutoEllipsis = true;
            TipStyle.FollowText(line);
        }

        /// <summary>
        /// The Speakers card: who has spoken this session, their faces, who is talking now — or why
        /// there is nothing to show, with the one switch that would change that.
        /// </summary>
        private void RefreshCaptionSpeakers()
        {
            if (_capSpeakerStrip == null || _capSpeakerStrip.IsDisposed) { return; }
            try
            {
                _capSpeakerStrip.SetTheme(_theme);
                bool labels = _settings != null && _settings.CaptionSpeakerTurns;
                bool faces = _settings != null && _settings.CaptionFaceAnalysis;
                var fa = _faceAnalyzer;

                // One button, whichever switch is off — labels first, since faces need them.
                string action = !labels ? "Turn on speaker labels" : !faces ? "Use faces too" : null;
                // At the window's scale: this runs again whenever the card refreshes, long after the
                // window has scaled, and its 100% numbers then put the button a third of the way short
                // of the card's edge and cut the face strip to two-thirds of its width on a 150% laptop.
                float k = AppliedWindowScale;
                if (action != null)
                {
                    _capSpeakerActionBtn.Text = Localization.T(action);
                    FitButtonToText(_capSpeakerActionBtn, 120, 12);
                    int right = _capSpeakerActionBtn.Parent != null
                        ? _capSpeakerActionBtn.Parent.ClientSize.Width - (int)Math.Round(20 * k)
                        : 676;
                    _capSpeakerActionBtn.Left = right - _capSpeakerActionBtn.Width;
                }
                _capSpeakerActionBtn.Visible = action != null;
                // The strip keeps clear of the button while it shows.
                _capSpeakerStrip.Width = action != null
                    ? Math.Max((int)Math.Round(200 * k), _capSpeakerActionBtn.Left - (int)Math.Round(12 * k) - _capSpeakerStrip.Left)
                    : (int)Math.Round(660 * k);

                if (!labels)
                {
                    _capSpeakerStrip.SetMessage(Localization.T("Speaker labels are off."), "");
                    return;
                }
                if (!_captionsActive)
                {
                    _capSpeakerStrip.SetMessage(Localization.T("Start captions to see who is talking."), "");
                    return;
                }

                string status;
                if (!faces)
                {
                    status = "";
                }
                else if (fa == null || !fa.Running)
                {
                    status = Localization.T("faces: not watching");
                }
                else
                {
                    status = Localization.F("faces on screen: {0}", fa.FaceCount);
                }

                var list = _speakerLinker.Snapshot();
                if (list.Count == 0)
                {
                    _capSpeakerStrip.SetMessage(Localization.T("No one has spoken yet."), status);
                    return;
                }
                var chips = new System.Collections.Generic.List<SpeakerStrip.Chip>();
                DateTime now = DateTime.UtcNow;
                foreach (Utils.SpeakerLinker.SpeakerInfo s in list)
                {
                    chips.Add(new SpeakerStrip.Chip
                    {
                        Label = s.Label,
                        Name = SpeakerDisplayName(s.Label),
                        Color = CaptionOverlayForm.SpeakerColor(s.Label),
                        // The best of their faces: one person is often a close-up AND a small face in
                        // the wide shots, and the latest is not always the one worth showing.
                        Face = faces && fa != null && s.Faces.Count > 0 ? fa.GetBestFaceThumbnail(s.Faces) : null,
                        Talking = s.Talking && (now - s.LastHeardUtc).TotalSeconds < 1.0,
                        Detail = s.HasVoice && s.HasFace ? Localization.T("voice + face")
                               : s.HasFace ? Localization.T("face")
                               : Localization.T("voice"),
                    });
                }
                _capSpeakerStrip.SetChips(chips, status);
            }
            catch (Exception ex) { Logger.Swallow("RefreshCaptionSpeakers", ex); }
        }

        /// <summary>The Speakers card's button: turns on speaker labels, or face &amp; mouth analysis.</summary>
        private void OnCaptionSpeakerAction()
        {
            if (_settings == null) { return; }
            try
            {
                if (!_settings.CaptionSpeakerTurns)
                {
                    _settings.CaptionSpeakerTurns = true;
                    // Settings shows the same switch; keep it truthful.
                    if (_captionSpeakerCheck != null) { _captionSpeakerCheck.Checked = true; }
                }
                else if (!_settings.CaptionFaceAnalysis)
                {
                    _settings.CaptionFaceAnalysis = true;
                    if (_captionFaceCheck != null) { _captionFaceCheck.Checked = true; }
                }
                Persistence.SettingsManager.Save(_settings);
                // A running session picks it up now (and shows the one-time note about labels).
                ApplySpeakerTurnsLive();
            }
            catch (Exception ex) { Logger.Warn("[Captions tab] speaker switch failed: " + ex.Message); }
            RefreshCaptionsTab();
        }

        /// <summary>
        /// Refreshes the whole tab (state pill, engine line, buttons, transcript).
        /// Cheap and safe to call often; the transcript half short-circuits when nothing
        /// changed and skips entirely while the tab isn't the one on screen.
        /// </summary>
        private void RefreshCaptionsTab()
        {
            if (_capStatePill == null) { return; }
            try
            {
                // The tab may have come on screen at a size it has not been fitted to (a resize while
                // another tab showed): no-op when the transcript is already the right height.
                FitCaptionsTranscriptToPage();

                bool on = _captionsActive;
                var tr = _captionTranscriber;

                // Keep the tab's language combo honest even when the change came from
                // somewhere that doesn't run the settings funnel (a settings file edit,
                // a profile load). Cheap: it no-ops unless the index actually differs.
                SyncCaptionLanguageCombosFromSettings();

                // The pill told one of three lies before. During the model load — up to
                // ~20 s on the large model — it said "Listening…" while nothing was being
                // transcribed. When the capture device was lost with no replacement it
                // ALSO said "Listening…", because IsRunning deliberately stays true, so a
                // stone-deaf engine looked healthy indefinitely. Both states now say so.
                string pill;
                Color pillColor;
                if (!on)
                {
                    pill = "Off";
                    pillColor = _theme.TextMuted;
                }
                else if (tr != null && tr.CaptureLost)
                {
                    pill = "⚠  Not hearing anything";
                    pillColor = _theme.DangerText;
                }
                else if (tr != null && tr.IsStarting)
                {
                    pill = "◌  Loading the speech model…";
                    pillColor = _theme.TextMuted;
                }
                else if (_captionMirrorRunning && !(_captionReader != null && _captionReader.Found))
                {
                    // Mirroring Windows Live Captions (chosen, or Tempo's engine fell back to it), and its
                    // window is not up: nothing is being heard yet, whatever "Listening…" claimed.
                    pill = "◌  Waiting for Windows captions…";
                    pillColor = _theme.TextMuted;
                }
                else
                {
                    pill = "●  Listening…";
                    pillColor = _theme.SuccessText;
                }
                // Translated here: the label is CREATED translated, and this line used to put the
                // English back on the first refresh — "Off" in every language, found by the layout
                // suite once its check ran after that refresh.
                _capStatePill.Text = Localization.T(pill);
                _capStatePill.ForeColor = pillColor;

                if (_capStartStopBtn != null)
                {
                    _capStartStopBtn.Text = Localization.T(on ? "Stop captions" : "Start captions");
                }

                if (_capEngineLine != null && _settings != null)
                {
                    var sb = new StringBuilder();
                    // Only claim a Tempo model when Tempo's engine is the one producing
                    // the text. On the Windows source — or after a fallback to it — the
                    // Whisper model and CPU/GPU choice are not in play at all, and naming
                    // them here told the user their large model was running when it
                    // wasn't, which is the opposite of a diagnosis.
                    bool tempoEngine = _settings.CaptionSource != Models.CaptionSource.Windows
                                       && !_captionFellBackToWindows;
                    // Translated piece by piece. It was assembled in English and shown as it was, so
                    // this line said "Model: base · Engine: CPU · Language: auto-detect" in every
                    // language, under a translated heading and above translated lines.
                    if (!tempoEngine)
                    {
                        sb.Append(Localization.T("Using Windows Live Captions"));
                        if (_captionFellBackToWindows)
                        {
                            sb.Append("   ·   ").Append(Localization.T("Tempo's engine fell back to it this session"));
                        }
                    }
                    else
                    {
                        sb.Append(Localization.F("Model: {0}", string.IsNullOrEmpty(_captionModelActiveKey)
                            ? (string.IsNullOrEmpty(_settings.CaptionModelKey) ? "base" : _settings.CaptionModelKey)
                            : _captionModelActiveKey));
                        sb.Append("   ·   ").Append(Localization.F("Engine: {0}", CaptionEngineText(tr)));
                        // What language the text is being decoded as. Pinned shows the
                        // language; auto shows what detection has settled on, or that it
                        // is still deciding — which is the state game audio tends to sit
                        // in, and which used to be invisible from here.
                        string language;
                        string pinned = _settings.CaptionLanguage;
                        string state = _captionsActive && _captionTranscriber != null &&
                                       (_captionTranscriber.IsRunning || _captionTranscriber.IsStarting)
                            ? _captionTranscriber.LanguageState : null;
                        if (!string.IsNullOrEmpty(pinned) &&
                            !pinned.Equals("auto", StringComparison.OrdinalIgnoreCase))
                        {
                            // Translated, like the pickers' own list: it said "Spanish" on a German screen.
                            language = CaptionLanguageName(pinned);
                            // Compared code to code. Finding the pinned code ANYWHERE in the engine's state
                            // text matched "de" inside "detecting", so pinning German mid-run never said it
                            // needed a restart; and an English-only model was told to restart for a
                            // language it can never write.
                            if (state == "English model" && !pinned.Equals("en", StringComparison.OrdinalIgnoreCase))
                            {
                                language += " " + Localization.T("(this model is English-only)");
                            }
                            // A running engine switches at its next step (ApplyCaptionLanguage). Only a choice it
                            // still has not taken a few seconds on — its rebuild failed — needs a restart.
                            else if (state != null &&
                                     !string.Equals(ActiveCaptionLanguageCode(state), pinned, StringComparison.OrdinalIgnoreCase) &&
                                     (DateTime.UtcNow - _capLanguageChangedUtc).TotalSeconds > 6)
                            {
                                language += " " + Localization.T("(restart captions to apply)");
                            }
                        }
                        else
                        {
                            language = !string.IsNullOrEmpty(state)
                                ? CaptionLanguageStateText(state)
                                : Localization.T("auto-detect");
                        }
                        sb.Append("   ·   ").Append(Localization.F("Language: {0}", language));
                        if (TempoTranscriber.GpuWouldHelp && !_settings.CaptionTryGpu)
                        {
                            sb.Append("  — ").Append(Localization.T("running behind; the GPU engine would help"));
                        }
                    }
                    _capEngineLine.Text = sb.ToString();
                }

                if (_capSourceLine != null && _settings != null)
                {
                    // What is actually being HEARD, not which engine was picked. This
                    // used to print CaptionSource — Windows/Tempo/Auto — under the word
                    // "Source", which is the engine choice and says nothing about the
                    // audio. Auto silently resolves to the microphone when there is no
                    // speaker, and an explicit "system audio" request falls back the same
                    // way, so a user could be captioning their room while this line
                    // cheerfully read "Source: Tempo".
                    //
                    // "&&", not "&": a Label treats a single ampersand as a mnemonic
                    // prefix and swallows it.
                    // While running, this says what is actually being HEARD. While stopped
                    // there is nothing being heard, so it names the engine that WOULD run
                    // instead — "Hearing: engine: Tempo" was the clumsy result of forcing
                    // one prefix onto both cases.
                    // Whole sentences per case rather than gluing a translated noun onto a
                    // translated prefix — word order round "microphone" is not universal.
                    string line;
                    if (tr != null && (tr.IsRunning || tr.IsStarting))
                    {
                        bool mic = tr.ActiveMode == CaptureMode.Microphone;
                        bool fellBack = _settings.CaptionCaptureMode == (int)CaptureMode.SystemAudio && mic;
                        line = fellBack
                            ? Localization.T("Hearing: microphone (no speaker found — fell back)")
                            : mic
                                ? Localization.T("Hearing: microphone")
                                : Localization.T("Hearing: system audio");
                        // And WHICH one. "System audio" says what kind of sound, not which speaker — the very
                        // question when captions hear nothing because the video plays on another output. A
                        // device name is never translated: it is what Windows calls the hardware.
                        string device = tr.OpenedDeviceName;
                        if (!string.IsNullOrWhiteSpace(device)) { line += "   ·   " + device.Replace("&", "&&"); }
                    }
                    else
                    {
                        // A whole sentence per choice. "Will use: {0} engine" was handed the setting's
                        // raw value — "Auto", in English, in every language.
                        switch (_settings.CaptionSource)
                        {
                            case Models.CaptionSource.Windows:
                                line = Localization.T("Will use: Windows Live Captions");
                                break;
                            case Models.CaptionSource.Tempo:
                                line = Localization.T("Will use: Tempo's offline engine");
                                break;
                            default:
                                line = Localization.T("Will use: Tempo's engine, Windows if needed");
                                break;
                        }
                    }
                    _capSourceLine.Text = line
                        + (_settings.CaptionFaceAnalysis
                            ? Localization.T("   ·   face && mouth analysis on") : "");
                }

                RefreshCaptionDelayLine();
                RefreshCaptionQuality(tr);
                RefreshCaptionSpeakers();

                if (_capOverlayBtn != null)
                {
                    bool barUp = _captionOverlay != null && !_captionOverlay.IsDisposed && _captionOverlay.Visible;
                    // It's a toggle now, so the label has to name the ACTION. "Caption bar
                    // is showing" described the state and left you guessing what a click
                    // would do.
                    _capOverlayBtn.Text = Localization.T(barUp ? "Hide caption bar" : "Show caption bar");
                }
                if (_capHistoryBtn != null)
                {
                    bool histUp = _captionHistoryForm != null && !_captionHistoryForm.IsDisposed && _captionHistoryForm.Visible;
                    _capHistoryBtn.Text = Localization.T(histUp ? "Hide history window" : "Show history window");
                }
                if (_capMoveBtn != null)
                {
                    _capMoveBtn.Text = Localization.T(_captionMoveMode ? "Done moving" : "Move captions");
                }

                RefreshCaptionTranscript();
            }
            catch (Exception ex) { Logger.Swallow("RefreshCaptionsTab", ex); }
        }

        /// <summary>
        /// Re-renders the transcript box from <c>_captionHistory</c>.
        ///
        /// Two guards keep this off the hot path: it does nothing unless the Captions tab
        /// is the visible one (captions can run for hours with the user on another tab),
        /// and nothing unless the rendered text actually differs — the last line is
        /// revised in place as the sliding window grows, so an append-only fast path
        /// wouldn't be correct.
        /// </summary>
        private void RefreshCaptionTranscript()
        {
            if (_capTranscript == null || _capTranscript.IsDisposed) { return; }
            try
            {
                if (!IsCaptionsTabVisible()) { return; }

                bool filtering = !_capParsedFilter.IsEmpty;
                string text = BuildTranscriptText(true, out int shown);

                // The count label is updated BEFORE the early-out below, not after it.
                //
                // It used to sit behind "has the transcript text changed?", which is the
                // wrong question: this label reports the history COUNT and the FILTER,
                // and both can change while the rendered text does not — most obviously
                // when there is no transcript at all, which is the one moment the
                // empty-state hint exists for. On a fresh launch the box sat blank with
                // nothing under it saying why.
                if (_capCountLabel != null)
                {
                    // Every branch goes through the translator. This expression used to
                    // mix one T() call with raw English on the other branches, which is
                    // exactly the shape the literal audit skipped — it saw the T() and
                    // treated the whole assignment as handled, so "12 of 300 lines match"
                    // stayed English in all five languages.
                    string caption;
                    if (_captionHistory.Count == 0)
                    {
                        caption = Localization.T(
                            "Nothing transcribed yet — start captions and play something with speech.");
                    }
                    else if (filtering)
                    {
                        // A filter that matches nothing says so, rather than leaving an
                        // empty box and "0 of 300" to be worked out.
                        caption = shown == 0
                            ? Localization.F("Nothing matches “{0}” — press Esc to clear the filter.", _capFilter)
                            : Localization.F("{0} of {1} lines match", shown, _captionHistory.Count);
                    }
                    else
                    {
                        caption = _captionHistory.Count == 1
                            ? Localization.F("{0} line · newest at the bottom", _captionHistory.Count)
                            : Localization.F("{0} lines · newest at the bottom", _captionHistory.Count);
                    }

                    // Assigned only when it differs. This now runs on every tick rather
                    // than only when the transcript text changed, and setting .Text on a
                    // Label repaints it whether or not the string is new. The "N new below"
                    // suffix is added by UpdateTranscriptNewBelow, so the base text is kept
                    // apart from it — otherwise the two would compound on every tick.
                    _capCountBaseText = caption;
                    UpdateTranscriptNewBelow();
                }

                // NOW the early-out, and only for the expensive half. Rewriting the
                // transcript box throws away the selection and scroll position, so it is
                // still skipped whenever the text is unchanged — which is most ticks
                // during a long run.
                if (text == _capRenderedText) { return; }
                int wasShowing = CountRenderedLines(_capRenderedText);
                _capRenderedText = text;

                // Only follow the tail when the user is already at the bottom, so reading
                // back through the transcript isn't yanked away every time a line lands.
                bool atBottom = IsTranscriptScrolledToBottom();
                // A selection — a phrase being copied — survives the rewrite. Replacing the box's text
                // dropped it, so while captions ran nothing stayed selected for longer than the couple of
                // seconds until the next caption. While something is selected the box also stays put.
                int selStart = _capTranscript.SelectionStart;
                string selText = _capTranscript.SelectionLength > 0 ? _capTranscript.SelectedText : null;
                int firstRow = selText != null ? FirstVisibleTranscriptRow() : 0;
                _capTranscript.Rtf = BuildTranscriptRtf();
                if (selText != null)
                {
                    RestoreTranscriptSelection(selStart, selText, firstRow);
                    atBottom = false;
                }
                if (atBottom)
                {
                    _capTranscript.SelectionStart = _capTranscript.TextLength;
                    _capTranscript.ScrollToCaret();
                    _capNewBelow = 0;
                }
                else
                {
                    // Reading back through the transcript: say how much has landed since,
                    // rather than leaving new speech to be discovered by scrolling down.
                    int now = CountRenderedLines(text);
                    if (now > wasShowing) { _capNewBelow += now - wasShowing; }
                }
                UpdateTranscriptNewBelow();
            }
            catch (Exception ex) { Logger.Swallow("RefreshCaptionTranscript", ex); }
        }

        /// <summary>
        /// The width <paramref name="b"/> needs for the widest of its translated captions, plus
        /// <paramref name="pad"/>: for a button whose caption flips at run time.
        /// </summary>
        private int WidestCaption(Control b, int pad, params string[] keys)
        {
            // In the window's units (MeasureLayout): the row is packed while the window is built.
            int w = 0;
            foreach (string k in keys)
            {
                w = Math.Max(w, MeasureLayout(Localization.T(k), b.Font, 0, TextFormatFlags.Default).Width);
            }
            return w + pad;
        }

        /// <summary>
        /// Lays <paramref name="row"/> out left to right between <paramref name="left"/> and
        /// <paramref name="right"/>: each button at its design width, or wider when its text needs it.
        /// When that overflows, the gaps close from 10 to 6 px and buttons wider than their text
        /// give back the difference, most spare first — so English keeps its design exactly and a
        /// long language fits without any button's font being shrunk. Only a row that cannot fit
        /// even then is left to the layout checker.
        /// </summary>
        private static void PackButtonRow(int left, int right, Control[] row, int[] design, int[] needed)
        {
            int n = row.Length;
            var w = new int[n];
            for (int i = 0; i < n; i++) { w[i] = Math.Max(design[i], needed[i]); }
            int gap = 10;
            int avail = right - left;
            int Total() { int s = 0; foreach (int x in w) { s += x; } return s + gap * (n - 1); }
            if (Total() > avail) { gap = 6; }
            while (Total() > avail)
            {
                int best = -1, spare = 0;
                for (int i = 0; i < n; i++)
                {
                    int s = w[i] - needed[i];
                    if (s > spare) { spare = s; best = i; }
                }
                if (best < 0) { break; }
                w[best] -= Math.Min(spare, Total() - avail);
            }
            int x0 = left;
            for (int i = 0; i < n; i++)
            {
                row[i].Left = x0;
                row[i].Width = w[i];
                x0 += w[i] + gap;
            }
        }

        /// <summary>
        /// Shows or hides the on-screen caption bar by driving the SETTING that owns it,
        /// not by poking the window directly.
        ///
        /// SetCaptionsActive only shows/hides the bar when CaptionOverlayEnabled is true,
        /// so a tab button that called ShowCaptionOverlay() straight out could put a bar
        /// on screen that the Settings checkbox said should not exist — and nothing would
        /// ever take it down again, because that branch is skipped entirely while the
        /// setting is off. Toggling the setting keeps both surfaces telling the same story.
        /// </summary>
        private void ToggleCaptionOverlayBar()
        {
            try
            {
                bool showing = _captionOverlay != null && !_captionOverlay.IsDisposed && _captionOverlay.Visible;
                ApplyCaptionOverlayPreference(!showing);
            }
            catch (Exception ex) { Logger.Warn("[Captions tab] overlay toggle: " + ex.Message); }
            RefreshCaptionsTab();
        }

        /// <summary>
        /// Where a speech model may come from: the folder Tempo downloads into, a file
        /// anywhere on disk, or one of the loose files already sitting in that folder.
        ///
        /// Whisper models are large — large-v3 is roughly 3 GB — and anyone already running
        /// whisper.cpp, Subtitle Edit, Buzz or their own fine-tune has one on a drive
        /// somewhere. Requiring a second copy purely because it must live in Tempo's folder
        /// is a cost with nothing behind it, so Tempo can read one where it already is.
        /// </summary>
        private void ShowModelSourceMenu(Control anchor, bool withDownload = false)
        {
            if (anchor == null || anchor.IsDisposed) { return; }

            var menu = new ContextMenuStrip { ShowImageMargin = false };
            try
            {
                menu.Closed += (s, e) => menu.BeginInvoke((Action)(() => { try { menu.Dispose(); } catch { } }));
                menu.Renderer = new ThemedMenuRenderer(_theme);
                menu.BackColor = _theme.Surface;
                menu.ForeColor = _theme.Text;
            }
            catch { }

            // The tab's own "Download model…" button gave its place to "Move captions"; the job it
            // did (the model chosen in Settings, with the same confirmation) is first in here.
            // Settings has its button still, so its copy of this menu leaves it out.
            if (withDownload)
            {
                menu.Items.Add(Utils.Localization.T("Download model…"), null, (s, e) => OnDownloadCaptionModel(s, e));
                menu.Items.Add(new ToolStripSeparator());
            }

            menu.Items.Add(Utils.Localization.T("Open the models folder"), null, (s, e) =>
            {
                try
                {
                    string dir = WhisperModelManager.GetModelsDirectory();
                    System.IO.Directory.CreateDirectory(dir);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true });
                }
                catch (Exception ex) { Logger.Warn("[Captions] models folder: " + ex.Message); }
            });

            menu.Items.Add(Utils.Localization.T("Get more models from the official site…"), null, (s, e) => OpenOfficialModelsPage());

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Utils.Localization.T("Use a model file from anywhere…"), null, (s, e) => BrowseForCaptionModel());

            // Models already on this PC, put there by another app or left in Downloads.
            var elsewhere = WhisperModelManager.FindModelsElsewhere();
            if (elsewhere.Count > 0)
            {
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(new ToolStripMenuItem(Utils.Localization.T("Already on this PC")) { Enabled = false });
                foreach (string path in elsewhere)
                {
                    string p = path;
                    // The model's real identity, read from its header — not the filename,
                    // which is whatever the person who saved it happened to type.
                    var pf = WhisperModelManager.ReadFacts(p);
                    var it = new ToolStripMenuItem(pf.Headline.Replace("&", "&&"))
                    {
                        ToolTipText = TipStyle.Wrap(pf.FileName + "\n" + p),
                        Checked = string.Equals(_settings?.CaptionCustomModelPath, p, StringComparison.OrdinalIgnoreCase),
                        CheckOnClick = false
                    };
                    it.Click += (s, e) => UseCaptionModelFile(p);
                    menu.Items.Add(it);
                }
            }

            // Loose files already in the folder — one click each, no file dialog needed.
            var loose = WhisperModelManager.DiscoverExtraModelFiles();
            if (loose.Count > 0)
            {
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(new ToolStripMenuItem(Utils.Localization.T("Found in the models folder")) { Enabled = false });
                foreach (string path in loose)
                {
                    string p = path;
                    // Same rule as the "already on this PC" list below: identify the model
                    // by what its header says it is, not by the name someone typed.
                    var lf = WhisperModelManager.ReadFacts(p);
                    var it = new ToolStripMenuItem(lf.Headline.Replace("&", "&&"))
                    {
                        ToolTipText = TipStyle.Wrap(lf.FileName + "\n" + p),
                        Checked = string.Equals(_settings?.CaptionCustomModelPath, p, StringComparison.OrdinalIgnoreCase),
                        CheckOnClick = false
                    };
                    it.Click += (s, e) => UseCaptionModelFile(p);
                    menu.Items.Add(it);
                }
            }

            string current = _settings != null ? (_settings.CaptionCustomModelPath ?? "") : "";
            if (current.Length > 0)
            {
                menu.Items.Add(new ToolStripSeparator());
                var cf = WhisperModelManager.ReadFacts(current);
                // Through F(): glued together from English pieces, this line read "Using: …" in every
                // language although "Using: {0}" has been translated all along.
                menu.Items.Add(new ToolStripMenuItem(
                    (cf.Valid ? Utils.Localization.F("Using: {0}", cf.Headline)
                              : Utils.Localization.F("Using a file that is missing: {0}", cf.FileName))
                        .Replace("&", "&&"))
                { Enabled = false });
                menu.Items.Add(Utils.Localization.T("Show details of this model…"), null, (s, e) =>
                {
                    using (var dlg = new ModelDetailsForm(_theme, WhisperModelManager.ReadFacts(current)))
                    {
                        dlg.ShowDialog(this);
                    }
                });
                menu.Items.Add(Utils.Localization.T("Go back to Tempo's own models"), null, (s, e) => UseCaptionModelFile(""));
            }

            try { menu.Show(anchor, new System.Drawing.Point(0, anchor.Height)); }
            catch (Exception ex) { Logger.Warn("[Captions] model menu: " + ex.Message); }
        }

        /// <summary>
        /// Sends the user to the official model repository — but explains it FIRST.
        ///
        /// That page is a bare listing of a hundred-odd files with names like
        /// "ggml-large-v3-turbo-q5_0.bin", no descriptions and no sizes above the fold.
        /// Dropping someone there because they said "I don't know where to get one" just
        /// relocates the confusion: the whole difficulty is knowing WHICH file and what to
        /// do with it afterwards. So the three things they actually need — which names are
        /// models, which one to pick, and where to put it — are said before the browser
        /// opens, and the folder is offered at the same time.
        ///
        /// Nothing is opened without the user choosing to: this runs from a menu item they
        /// clicked, and the dialog can still be cancelled.
        /// </summary>
        private void OpenOfficialModelsPage()
        {
            string url = WhisperModelManager.OfficialModelsPageUrl;
            var answer = MessageBox.Show(this,
                Localization.T("Tempo's speech models come from the whisper.cpp project on Hugging Face — the same " +
                "place the \"Download model\" button uses. You only need this page for something " +
                "Tempo doesn't list, such as a language-specific or fine-tuned model.\n\n" +
                "On that page:\n" +
                "   •  Download any file named ggml-….bin  (ignore everything else)\n" +
                "   •  \"q5\" / \"q8\" in the name means a smaller, quantised build — same hearing, " +
                "about a third of the size and CPU\n" +
                "   •  \".en\" means English-only; without it, the model handles any language\n" +
                "   •  Bigger is more accurate but slower — large is roughly 3 GB\n\n" +
                "Then save it into Tempo's models folder and it will be picked up automatically. " +
                "You can also keep it anywhere you like and use \"Use a model file from anywhere\".\n\n" +
                "Open the page in your browser now?"),
                Localization.T("Where to get speech models"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Information);

            if (answer != DialogResult.Yes) { return; }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                Logger.Info("[Captions] opened the official models page: " + url);

                // Open the destination folder too. Downloading the file is only half of it;
                // the next question is always "where do I put this?", and answering it while
                // the download runs beats answering it afterwards.
                string dir = WhisperModelManager.GetModelsDirectory();
                System.IO.Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.Warn("[Captions] could not open the models page: " + ex.Message);
                MessageBox.Show(this,
                    Localization.F("Couldn't open your browser. The address is:\n\n{0}", url),
                    "Tempo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>File-dialog path to <see cref="UseCaptionModelFile"/>.</summary>
        private void BrowseForCaptionModel()
        {
            try
            {
                using (var dlg = new OpenFileDialog
                {
                    Title = Localization.T("Choose a Whisper speech model"),
                    Filter = Localization.T("Whisper model (*.bin)|*.bin|All files (*.*)|*.*"),
                    CheckFileExists = true
                })
                {
                    string cur = _settings != null ? (_settings.CaptionCustomModelPath ?? "") : "";
                    try
                    {
                        dlg.InitialDirectory = cur.Length > 0 && System.IO.File.Exists(cur)
                            ? System.IO.Path.GetDirectoryName(cur)
                            : WhisperModelManager.GetModelsDirectory();
                    }
                    catch { }

                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        UseCaptionModelFile(dlg.FileName);
                    }
                }
            }
            catch (Exception ex) { Logger.Warn("[Captions] model browse: " + ex.Message); }
        }

        /// <summary>
        /// Points captions at a model file (or "" to go back to the built-in downloads),
        /// then restarts the engine if it is running so the change takes effect now rather
        /// than at some unexplained later moment.
        ///
        /// The file is CHECKED before it is accepted. A bad pick handed to the native
        /// whisper library is an access violation that takes the whole process down — not
        /// an exception anything upstream could catch — so a wrong file has to be refused
        /// here, while there is still a dialog to explain it in.
        /// </summary>
        private void UseCaptionModelFile(string path)
        {
            if (_settings == null) { return; }
            path = path ?? "";

            // Read the file and SHOW what it is before committing captions to it. A file
            // from outside Tempo comes with nothing but a name somebody else chose, so
            // this is the only point at which the user can find out they are about to
            // switch to an English-only model, or a 3 GB Large this PC can't run live.
            // The same dialog explains the refusal when the file isn't usable, which is
            // strictly more useful than the flat "not a model" message it replaces.
            if (path.Length > 0)
            {
                var facts = WhisperModelManager.ReadFacts(path);
                using (var dlg = new ModelDetailsForm(_theme, facts))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) { return; }
                }
                if (!facts.Valid) { return; }
            }

            _settings.CaptionCustomModelPath = path;
            try { Persistence.SettingsManager.Save(_settings); } catch { }
            Logger.Info(path.Length > 0
                ? "[Captions] speech model set to " + path
                : "[Captions] speech model back to Tempo's own downloads.");

            // An explicit model choice beats the session's too-slow override, as one made in Settings does.
            _captionModelOverrideKey = null;
            _modelRecoveryBlocked = false;

            // In place, and only when Tempo's engine is the source. This called StartTempoCaptions
            // whatever the source was, so with "Windows 11 Live Captions" chosen it started Tempo's
            // engine BESIDE the Windows captions being mirrored — two engines writing to one bar.
            if (_captionsActive && _settings.CaptionSource != Models.CaptionSource.Windows)
            {
                RestartCaptionEngine(path.Length > 0 ? "a model file was chosen" : "back to Tempo's own models");
            }
            RefreshCaptionsTab();
        }

        /// <summary>
        /// Fills the input meter and the quality line from signals the engine already
        /// produces. Every one of these existed only in Live debug (or, for confidence,
        /// nowhere at all), while the tab that calls itself the operational view showed
        /// none of them.
        /// </summary>
        private void RefreshCaptionQuality(TempoTranscriber tr)
        {
            bool live = _captionsActive && tr != null && (tr.IsRunning || tr.IsStarting);

            if (_capLevel != null)
            {
                _capLevel.Live = live;
                // The PEAK since the last refresh, not the instantaneous value —
                // the meter ticks five times slower than the audio arrives.
                _capLevel.LevelDb = live ? tr.TakeLevelPeakDb() : -60;
                _capLevel.Clipping = live && tr.InputClipping;
                _capLevel.ApplyTheme(_theme);
            }

            if (_capQualityLine == null) { return; }
            if (!live) { _capQualityLine.Text = ""; return; }

            var sb = new StringBuilder();

            // Clipping first: it is the one fault here the user can actually fix, and the
            // remedy is the opposite of what the delay line's button suggests — turn the
            // volume DOWN, not shrink the model.
            // Set alongside the text so the colour below is decided once, not re-derived.
            bool warn = tr.InputClipping;

            // Every piece through the translator: this line was assembled from English and shown as it
            // was, so it read in English on the tab's otherwise translated status card.
            //
            // First, sound that cannot reach the engine at all: a muted speaker, the sound going to another
            // output, a slider near zero. Each looks exactly like a quiet room everywhere else here, and the
            // engine says so on the bar only until the first words — someone who mutes mid-video was told
            // nothing. (Loopback only: a microphone is not behind the speaker's volume.)
            bool loopback = tr.ActiveMode == CaptureMode.SystemAudio;
            string elsewhere = loopback ? tr.AudioElsewhereOn : null;
            if (loopback && tr.SystemMuted)
            {
                warn = true;
                sb.Append(Localization.T("⚠ The speaker is muted — there is no sound to caption. Unmute it and captions carry on."));
            }
            else if (!string.IsNullOrEmpty(elsewhere))
            {
                warn = true;
                // One device name: the line above already names the one being heard, and two long names
                // ran this to a third line, past the bottom of the card.
                sb.Append(Localization.F("⚠ Sound is playing on “{0}”, not on the device captions hear — pick that one under Settings › Live Captions.",
                    elsewhere.Replace("&", "&&")));
            }
            else if (loopback && tr.SystemVolume >= 0 && tr.SystemVolume < 0.08)
            {
                warn = true;
                sb.Append(Localization.F("⚠ Speaker volume is {0}% — too faint to caption reliably.", Math.Round(tr.SystemVolume * 100)));
            }
            else if (tr.InputClipping)
            {
                sb.Append(Localization.T("⚠ Audio is clipping — turn the app or system volume down; a smaller model will not help."));
            }
            // Two videos at once is the most confusing failure this feature has, because it
            // does not LOOK like a failure: the text keeps flowing, it stays grammatical,
            // and it is a blend of two things nobody said together. It has to be called out
            // where captions are being READ, not only in Health — someone who can't hear the
            // audio has no other way to know the engine is listening to a mix. This outranks
            // the confidence read-out below, which is a symptom of the same cause.
            else if (AudioSourcesAreMixed(out int mixedApps, out int _))
            {
                warn = true;
                sb.Append(Localization.F("⚠ {0} apps are playing sound at once — captions are a blend of all of them, "
                    + "so sentences may combine things different sources said. Mute the ones you don't need captioned.",
                    mixedApps));
            }
            else
            {
                double conf = tr.LastConfidence;
                if (conf >= 0)
                {
                    int percent = (int)Math.Round(conf * 100);
                    sb.Append(conf < 0.45
                        ? Localization.F("Confidence {0}% — low; the audio may be quiet, noisy or not speech", percent)
                        : Localization.F("Confidence {0}%", percent));
                }

                // In words, not the engine's log text ("detecting: en (2/4)").
                string lang = CaptionLanguageStateText(tr.LanguageState);
                if (!string.IsNullOrEmpty(lang))
                {
                    if (sb.Length > 0) { sb.Append("   ·   "); }
                    sb.Append(Localization.F("Language: {0}", lang));
                }

                // A caption engine that has gone quiet is indistinguishable from a quiet
                // room on every other surface. This is the cheapest detector there is and
                // nothing was consulting it.
                double quiet = tr.SecondsSinceLastCaption;
                if (quiet >= 20)
                {
                    if (sb.Length > 0) { sb.Append("   ·   "); }
                    sb.Append(Localization.F("⚠ nothing transcribed for {0} s", (int)quiet));
                }
            }

            _capQualityLine.Text = sb.ToString();
            _capQualityLine.ForeColor = warn ? _theme.DangerText : _theme.TextMuted;
        }

        /// <summary>
        /// Shows how far behind the captions are, and — when the model cannot hold
        /// real-time pace — says so in words rather than leaving the user to wonder why
        /// the video is ahead of the text.
        ///
        /// The distinction that matters: a steady delay is just the pipeline's latency
        /// (a window has to finish arriving before it can be decoded). A GROWING delay
        /// means decode is slower than audio arrives, so it drifts further behind for as
        /// long as you keep playing, and eventually audio is dropped outright.
        /// </summary>
        private void RefreshCaptionDelayLine()
        {
            if (_capDelayLine == null) { return; }
            var t = _captionTranscriber;
            bool live = _captionsActive && t != null && t.IsRunning;
            if (!live)
            {
                _capDelayLine.Text = "";
                if (_capFasterModelBtn != null) { _capFasterModelBtn.Visible = false; }
                return;
            }

            double rtf = t.RealTimeFactor;
            // MEASURED once a caption has come out: the engine's share (words heard → text out)
            // plus the bar's (text in → word on screen). The estimate it used to show instead
            // counted the same wait twice and read ~4.3 s while captions were ~2 s behind.
            double engineDelay = t.MeasuredDelaySeconds;
            double delay = engineDelay >= 0 ? engineDelay + CaptionBarLagSeconds() : t.EstimatedDelaySeconds;
            // Translated piece by piece, like the engine line (it was English in every language).
            var sb = new StringBuilder();
            sb.Append(Localization.F("Delay ~{0} s behind", delay.ToString("0.0")));
            if (t.CurrentDecodeSeconds >= 4)
            {
                sb.Append("  ·  ").Append(Localization.F("frozen {0} s in one decode", (int)t.CurrentDecodeSeconds));
            }
            if (rtf > 0)
            {
                // Two decimals: at one, 0.96 printed as "1.0× real time" in the calm colour while
                // 1.00 printed the same "1.0×" in the warning colour.
                sb.Append("  ·  ").Append(Localization.F("{0}× real time", rtf.ToString("0.00")));
            }
            if (t.BacklogDroppedSeconds > 0.5)
            {
                sb.Append("  ·  ").Append(Localization.F("{0} s skipped", t.BacklogDroppedSeconds.ToString("0")));
            }
            _capDelayLine.Text = sb.ToString();

            bool cannotKeepUp = rtf >= 1.0 || TempoTranscriber.GpuWouldHelp;
            _capDelayLine.ForeColor = cannotKeepUp ? _theme.DangerText : _theme.TextMuted;

            // Which remedy to offer depends on the machine. Telling someone with an idle
            // discrete GPU to shrink their model is the wrong advice: Tempo already probes
            // Vulkan and, on this kind of PC, logged "turning on the GPU engine would run
            // this model far faster" — into the log file, where nobody looks. Offer that
            // first, and only fall back to a smaller model when there is no usable GPU.
            if (_capFasterModelBtn != null)
            {
                bool gpuAvailable = false;
                try { gpuAvailable = VulkanProbe.HasUsableDevice; } catch { }
                bool offerGpu = gpuAvailable && _settings != null && !_settings.CaptionTryGpu;

                _capFasterModelBtn.Visible = cannotKeepUp;
                _capFasterModelBtn.Text = Localization.T(offerGpu
                    ? "Enable GPU engine" : "Use a faster model");
                _capOfferGpuEngine = offerGpu;
            }
        }

        // Whether the remedy button currently offers the GPU engine (rather than a
        // smaller model), decided in RefreshCaptionDelayLine from what this PC has.
        private bool _capOfferGpuEngine;

        /// <summary>
        /// The caption bar's share of caption delay, in seconds: how long words wait on the bar
        /// before they appear. 0 while the bar is hidden (nothing waits on a bar nobody can see)
        /// or before it has revealed a word.
        /// </summary>
        private double CaptionBarLagSeconds()
        {
            var bar = _captionOverlay;
            if (bar == null || bar.IsDisposed || !bar.Visible) { return 0; }
            int lag = bar.RevealLagMs;
            return lag < 0 ? 0 : lag / 1000.0;
        }

        /// <summary>
        /// Live debug's caption SYNC block: how far behind the speech captions really are, split into
        /// the engine's share and the bar's, and what each is made of right now.
        ///
        /// It replaces a "delay ~4.3 s (window 2.8 s + backlog 2.1 s + decode 1.2 s)" line whose parts
        /// did not add up to its total and whose total was about double the truth: the engine now
        /// measures every caption against the audio it came from, and the bar times every word from
        /// arriving to appearing. A decode that is running long — the freeze — is named while it is
        /// happening instead of only afterwards, as a drop.
        /// </summary>
        private void AppendCaptionSyncStats(StringBuilder sb, TempoTranscriber t)
        {
            double engine = t.MeasuredDelaySeconds;
            double bar = CaptionBarLagSeconds();
            if (engine < 0)
            {
                sb.Append("  Caption sync: no caption yet (estimate ~")
                  .Append(t.EstimatedDelaySeconds.ToString("0.0")).Append(" s once words arrive)").AppendLine();
            }
            else
            {
                sb.Append("  Caption sync: words reach the bar ~").Append((engine + bar).ToString("0.0"))
                  .Append(" s after they are spoken (engine ").Append(engine.ToString("0.0")).Append(" s");
                if (bar > 0) { sb.Append(" + bar reveal ").Append(bar.ToString("0.0")).Append(" s"); }
                sb.Append(')').AppendLine();
                sb.Append("    last caption: newest words ").Append(t.LastCaptionNewestDelaySeconds.ToString("0.0"))
                  .Append(" s, oldest ").Append(t.LastCaptionOldestDelaySeconds.ToString("0.0")).Append(" s");
                double worst = t.WorstRecentDelaySeconds;
                if (worst >= 0) { sb.Append(" · longest wait in the last 30 s ").Append(worst.ToString("0.0")).Append(" s"); }
                sb.AppendLine();
            }

            double rtf = t.RealTimeFactor;
            sb.Append("    engine: backlog ").Append(t.BacklogSeconds.ToString("0.0")).Append(" s")
              .Append(" · decode avg ").Append((t.AverageInferenceMs / 1000.0).ToString("0.00"))
              .Append(" s, slowest ").Append((t.WorstRecentInferenceMs / 1000.0).ToString("0.0")).Append(" s in the last minute")
              .Append(" · pace ").Append(rtf <= 0 ? "—" : rtf.ToString("0.00") + "× real time");
            if (rtf >= 1.0) { sb.Append("  ⚠ SLOWER THAN REAL TIME"); }
            sb.AppendLine();

            double decoding = t.CurrentDecodeSeconds;
            if (decoding >= 2)
            {
                sb.Append("    ⚠ one decode has been running for ").Append(decoding.ToString("0.0"))
                  .Append(" s — nothing reaches the bar until it returns").AppendLine();
            }

            var overlay = _captionOverlay;
            if (overlay != null && !overlay.IsDisposed && overlay.Visible)
            {
                int queued = Math.Max(0, overlay.RevealTotalWords - overlay.RevealShownWords);
                int waiting = overlay.RevealWaitingMs;
                sb.Append("    bar: ").Append(queued).Append(queued == 1 ? " word waiting" : " words waiting");
                if (waiting > 0) { sb.Append(", the oldest for ").Append((waiting / 1000.0).ToString("0.0")).Append(" s"); }
                sb.Append(" · revealing at ").Append(overlay.RevealPaceMs).Append(" ms a word").AppendLine();
            }

            double sinceDrop = t.SecondsSinceLastDrop;
            if (sinceDrop >= 0)
            {
                sb.Append("    ").Append(sinceDrop < 120 ? "⚠ " : "").Append("last skipped audio: ")
                  .Append(t.LastDropSeconds.ToString("0.0")).Append(" s, ").Append(FormatAgo(sinceDrop)).Append(" ago · ")
                  .Append(t.BacklogDroppedSeconds.ToString("0.0")).Append(" s this session").AppendLine();
            }
            if (rtf >= 1.0)
            {
                sb.Append("    → '").Append(_captionModelActiveKey ?? "?")
                  .Append("' cannot hold pace on this PC. A smaller model (small/base) or the GPU engine fixes the drift.")
                  .AppendLine();
            }
        }

        /// <summary>
        /// Switches captions to the GPU engine. It only takes effect on restart — the
        /// runtime is fixed for the life of the process — so say that plainly rather than
        /// letting the user watch for a change that cannot happen yet.
        /// </summary>
        private void EnableGpuCaptionEngine()
        {
            if (_settings == null) { return; }
            try
            {
                string gpu = "";
                try { gpu = VulkanProbe.Summary ?? ""; } catch { }
                Logger.Info("[Captions] GPU engine enabled from the Captions tab" +
                    (gpu.Length > 0 ? " (" + gpu + ")" : "") + " — takes effect on restart.");

                // Drive the Settings checkbox rather than writing the setting here. Its
                // CheckedChanged is the owner: it stores the value AND runs the restart
                // prompt. Doing both meant two messages about the same thing — my own
                // notification and that prompt — and two places that could drift apart.
                if (_captionGpuCheck != null)
                {
                    _captionGpuCheck.Checked = true;      // fires the owning handler
                }
                else
                {
                    _settings.CaptionTryGpu = true;       // no Settings page yet — write directly
                }
                // Persist now: the checkbox handler only updates the in-memory setting,
                // and this choice should survive even if Save Settings is never pressed.
                try { Persistence.SettingsManager.Save(_settings); } catch { }
                RefreshCaptionsTab();
            }
            catch (Exception ex) { Logger.Warn("[Captions tab] enabling the GPU engine failed: " + ex.Message); }
        }

        /// <summary>
        /// Drops to the next model down and restarts captions, because "your model is too
        /// slow" is only useful advice if acting on it does not mean hunting through
        /// Settings for a dropdown whose options mean nothing to most people.
        /// </summary>
        private void UseFasterCaptionModel()
        {
            if (_settings == null) { return; }
            try
            {
                // A model file the user chose outranks the built-in models (StartTempoCaptions), so
                // stepping the built-in choice down changed nothing that ran — while the notice below
                // said the captions had switched.
                if (!string.IsNullOrEmpty(_settings.CaptionCustomModelPath))
                {
                    TempoNotify(5000, "Tempo",
                        Localization.T("Captions use a model file you chose, which Tempo can't size down. Pick a smaller one with Models… on the Captions tab."),
                        ToolTipIcon.Info, always: true);
                    return;
                }

                string current = !string.IsNullOrEmpty(_captionModelActiveKey)
                    ? _captionModelActiveKey : (_settings.CaptionModelKey ?? "base");
                // The next smaller model that is DOWNLOADED. The next rung of the ladder may not be, and
                // the engine then loaded whatever was installed instead — often the same slow model.
                // (Not NextFasterInstalled for a key it doesn't know: it would start from the heaviest.)
                string next = Utils.WhisperModelManager.IndexInSpeedOrder(current) >= 0
                    ? Utils.WhisperModelManager.NextFasterInstalled(current)
                    : InstalledModelKeyOrNull(NextFasterModelKey(current));
                if (next == null)
                {
                    // An answer to a button press: always shown.
                    TempoNotify(5000, "Tempo", NextFasterModelKey(current) == null
                        ? Localization.T("Already on the fastest model. Turning on the GPU engine in Settings is the next step.")
                        : Localization.T("No faster model is installed. Download a smaller one with Models… on the Captions tab, or turn on the GPU engine in Settings."),
                        ToolTipIcon.Info, always: true);
                    return;
                }

                // An explicit pick beats the session's too-slow override, as a model chosen in Settings
                // does (OnSaveSettings). Left in place, the restart loaded the overridden model again.
                _captionModelOverrideKey = null;
                _modelRecoveryBlocked = false;

                // Through the funnel: it persists and repaints BOTH surfaces, so the
                // Settings combo cannot be left showing the old model (Save Settings
                // writes CaptionModelKey from that combo and would have reverted this).
                ChangeCaptionSetting(() => _settings.CaptionModelKey = next,
                    "model switched to '" + next + "' to keep pace");

                // In place: switching captions off and on to do this started a new session, which
                // cleared the transcript this tab is showing.
                RestartCaptionEngine("model switched to '" + next + "' to keep pace");

                Logger.Info("[Captions] switched to the '" + next + "' model to keep pace (was '" + current + "').");
                TempoNotify(4000, "Tempo",
                    Localization.F("Captions switched to the ‘{0}’ model so they keep up.", next),
                    ToolTipIcon.Info);
                RefreshCaptionsTab();
            }
            catch (Exception ex) { Logger.Warn("[Captions tab] faster model failed: " + ex.Message); }
        }


        /// <summary>
        /// The caption languages offered on BOTH surfaces — {Whisper code, label}.
        /// One table so the Settings combo and the Captions tab combo can never drift
        /// apart in contents or order, the way two hand-written lists would.
        ///
        /// "Auto-detect" stays first and remains the default, but it is genuinely worse
        /// on a noisy mix: detection re-runs per chunk, may never settle, and can put a
        /// stretch of speech through the wrong language. Anyone who watches or plays in
        /// one language is better off pinning it.
        /// </summary>
        internal static readonly string[][] CaptionLanguageChoices =
        {
            new[] { "auto", "Auto-detect" },
            new[] { "en",   "English" },
            new[] { "es",   "Spanish" },
            new[] { "fr",   "French" },
            new[] { "de",   "German" },
            new[] { "it",   "Italian" },
            new[] { "pt",   "Portuguese" },
            new[] { "nl",   "Dutch" },
            new[] { "pl",   "Polish" },
            new[] { "ru",   "Russian" },
            new[] { "tr",   "Turkish" },
            new[] { "ar",   "Arabic" },
            new[] { "hi",   "Hindi" },
            new[] { "ja",   "Japanese" },
            new[] { "ko",   "Korean" },
            new[] { "zh",   "Chinese" },
        };

        /// <summary>The labels only, in table order — what both combos are filled with.</summary>
        internal static string[] CaptionLanguageLabels()
        {
            // Translated: these are SPOKEN-language names shown to the reader, so a
            // Spanish UI should offer "Francés", not "French". Unlike the interface
            // language picker — which lists endonyms (Español, Français) and must keep
            // them — nothing here depends on the text: the combo is read and set purely
            // by index, and what gets stored is the code ("auto", "en", "es").
            var labels = new string[CaptionLanguageChoices.Length];
            for (int i = 0; i < CaptionLanguageChoices.Length; i++)
            {
                labels[i] = Localization.T(CaptionLanguageChoices[i][1]);
            }
            return labels;
        }

        /// <summary>Combo index for a stored code; 0 (Auto-detect) when unrecognised.</summary>
        internal static int CaptionLanguageIndexFromCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) { return 0; }
            string want = code.Trim().ToLowerInvariant();
            for (int i = 0; i < CaptionLanguageChoices.Length; i++)
            {
                if (CaptionLanguageChoices[i][0] == want) { return i; }
            }
            return 0;
        }

        /// <summary>Stored code for a combo index; "auto" when out of range.</summary>
        internal static string CaptionLanguageCodeFromIndex(int index)
        {
            return (index >= 0 && index < CaptionLanguageChoices.Length)
                ? CaptionLanguageChoices[index][0]
                : "auto";
        }

        /// <summary>The human label for a stored code, for status lines.</summary>
        internal static string CaptionLanguageLabel(string code)
        {
            return CaptionLanguageChoices[CaptionLanguageIndexFromCode(code)][1];
        }

        /// <summary>
        /// A language code's name in the reader's language when the pickers list it, otherwise the code
        /// itself: detection can settle on any of Whisper's ~100 languages, and the list names fifteen —
        /// <see cref="CaptionLanguageLabel"/> would call an unlisted one "Auto-detect".
        /// </summary>
        internal static string CaptionLanguageName(string code)
        {
            string want = (code ?? "").Trim();
            for (int i = 1; i < CaptionLanguageChoices.Length; i++)
            {
                if (string.Equals(CaptionLanguageChoices[i][0], want, StringComparison.OrdinalIgnoreCase))
                {
                    return Localization.T(CaptionLanguageChoices[i][1]);
                }
            }
            return want;
        }

        /// <summary>
        /// The engine's language state in the reader's words. The engine keeps it as log text —
        /// "auto-detect", "detecting: en (2/4)", "locked: en", "pinned: en", "English model" — and the
        /// Captions tab showed it as it was, in English and with bare codes, in every language.
        /// </summary>
        internal static string CaptionLanguageStateText(string state)
        {
            if (string.IsNullOrEmpty(state)) { return ""; }
            if (state == "auto-detect") { return Localization.T("auto-detect"); }
            if (state == "English model") { return Localization.T("English-only model"); }
            if (state.StartsWith("pinned: ", StringComparison.Ordinal))
            {
                return CaptionLanguageName(state.Substring(8));
            }
            if (state.StartsWith("locked: ", StringComparison.Ordinal))
            {
                return Localization.F("{0} (detected)", CaptionLanguageName(state.Substring(8)));
            }
            if (state.StartsWith("detecting: ", StringComparison.Ordinal))
            {
                // "detecting: en (2/4)" — the language detection leans towards, and how sure it is.
                string rest = state.Substring(11);
                int open = rest.IndexOf(" (", StringComparison.Ordinal);
                string code = open > 0 ? rest.Substring(0, open) : rest;
                string score = open > 0 ? rest.Substring(open + 2).TrimEnd(')') : "";
                return score.Length > 0
                    ? Localization.F("detecting… {0} ({1})", CaptionLanguageName(code), score)
                    : Localization.F("detecting… {0}", CaptionLanguageName(code));
            }
            return state;
        }

        /// <summary>The code the running engine decodes as, from its state; null while it is still detecting.</summary>
        internal static string ActiveCaptionLanguageCode(string state)
        {
            if (string.IsNullOrEmpty(state)) { return null; }
            if (state == "English model") { return "en"; }
            foreach (string prefix in new[] { "pinned: ", "locked: " })
            {
                if (state.StartsWith(prefix, StringComparison.Ordinal)) { return state.Substring(prefix.Length).Trim(); }
            }
            return null;
        }

        /// <summary>
        /// Points BOTH language combos at whatever CaptionLanguage now says, without
        /// re-entering their change handlers — the same job SyncCaptionModelComboFromSettings
        /// does for the model, and for the same reason: either surface can change it.
        /// </summary>
        private void SyncCaptionLanguageCombosFromSettings()
        {
            if (_settings == null) { return; }
            try
            {
                int idx = CaptionLanguageIndexFromCode(_settings.CaptionLanguage);
                bool prev = _suppressSettingsEvents;
                _suppressSettingsEvents = true;
                try
                {
                    if (_captionLangCombo != null && _captionLangCombo.SelectedIndex != idx &&
                        idx < _captionLangCombo.Items.Count)
                    {
                        _captionLangCombo.SelectedIndex = idx;
                    }
                    if (_capLangCombo != null && _capLangCombo.SelectedIndex != idx &&
                        idx < _capLangCombo.Items.Count)
                    {
                        _capLangCombo.SelectedIndex = idx;
                    }
                }
                finally { _suppressSettingsEvents = prev; }
            }
            catch (Exception ex) { Logger.Swallow("SyncCaptionLanguageCombos", ex); }
        }

        /// <summary>
        /// The one implementation of "the caption language changed", used by both combos.
        /// Writes through the shared funnel so the setting persists and the other surface
        /// follows, and tells the user when the change only lands on the next start.
        /// </summary>
        private void ApplyCaptionLanguage(string code)
        {
            if (_settings == null) { return; }
            string want = string.IsNullOrWhiteSpace(code) ? "auto" : code.Trim().ToLowerInvariant();
            if (string.Equals(_settings.CaptionLanguage, want, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            ChangeCaptionSetting(() => _settings.CaptionLanguage = want,
                want == "auto" ? "caption language: auto-detect"
                               : "caption language: " + CaptionLanguageLabel(want));
            // A running engine takes it at its next step: it re-reads the choice between steps and
            // rebuilds its processor from the model it already has, so the session — and the transcript
            // on this tab — carries on. The engine line used to say "restart captions to apply", and
            // restarting them started a new session, which cleared the transcript.
            _capLanguageChangedUtc = DateTime.UtcNow;
            var tr = _captionTranscriber;
            if (tr != null) { tr.PreferredLanguage = want; }
        }

        /// <summary>When a language picker last changed the language — the engine gets a few seconds to apply it.</summary>
        private DateTime _capLanguageChangedUtc = DateTime.MinValue;

        /// <summary>
        /// Points the Settings model combo at whatever CaptionModelKey now says, without
        /// re-entering its change handler. Needed because the Captions tab can change the
        /// model, and Settings both DISPLAYS that value and writes it back on save.
        /// </summary>
        private void SyncCaptionModelComboFromSettings()
        {
            if (_captionModelCombo == null || _settings == null) { return; }
            try
            {
                int idx = WhisperModelIndexFromKey(_settings.CaptionModelKey);
                if (idx < 0 || idx >= _captionModelCombo.Items.Count) { return; }
                if (_captionModelCombo.SelectedIndex == idx) { return; }
                bool prev = _suppressSettingsEvents;
                _suppressSettingsEvents = true;
                try { _captionModelCombo.SelectedIndex = idx; }
                finally { _suppressSettingsEvents = prev; }
            }
            catch (Exception ex) { Logger.Swallow("SyncCaptionModelCombo", ex); }
        }


        /// <summary>
        /// The one implementation of "should there be a caption bar", used by BOTH the
        /// Captions tab's toggle and the Settings checkbox.
        ///
        /// They used to disagree about what clicking meant. The tab wrote the setting,
        /// persisted it and showed/hid the bar; the Settings checkbox only re-evaluated
        /// which controls were greyed out — it did not write the setting, did not persist,
        /// and did not touch the bar. So ticking the box left the Settings page showing
        /// one thing while the tab, the file and the bar on screen all still said another,
        /// until Save Settings was pressed — and if Tempo was killed rather than closed,
        /// the change was simply lost.
        /// </summary>
        private void ApplyCaptionOverlayPreference(bool want)
        {
            ChangeCaptionSetting(() => _settings.CaptionOverlayEnabled = want,
                want ? "caption bar enabled" : "caption bar disabled");

            if (want)
            {
                // Show the bar even when captions are off. An earlier version of this
                // gated on _captionsActive, which made the button lie: it read "Show
                // caption bar", and clicking it with captions stopped did nothing
                // visible. An empty bar is also how you drag it where you want it before
                // starting, so there is a real reason to put one up. (With captions off it now
                // says so, instead of "Captions on · listening…".)
                ShowCaptionOverlay();
                // Asked for on purpose: ending move mode must not take it away again.
                _moveModeShowedBar = false;
            }
            else if (_captionOverlay != null && !_captionOverlay.IsDisposed)
            {
                try { _captionOverlay.Hide(); } catch { }
            }
            SyncCaptionMoveUi();
        }

        /// <summary>
        /// Writes the transcript as SubRip (.srt) or WebVTT (.vtt).
        ///
        /// The per-line times were already being kept — and were only ever used to prefix
        /// a plain .txt. Subtitle formats are what a deaf user can actually DO something
        /// with: load them next to a recording, hand them to someone else, or check what
        /// was said at a given moment.
        ///
        /// Cue times: a cue starts when its line's first words were SPOKEN and ends a moment
        /// after its last words (or where the next cue begins, if that is sooner). Lines used
        /// to carry only the time they were last UPDATED — the moment a long line's final
        /// words arrived, seconds after they were said — so every cue started late, often
        /// after its speech had finished. Tempo's engine measures when each caption's words
        /// were heard; the Windows mirror can't, so its cues start when the words arrived.
        /// Still approximate at the word level, and the file says so where the format allows.
        /// </summary>
        private void ExportTranscriptSubtitles()
        {
            try
            {
                if (_captionHistory.Count == 0)
                {
                    // always: the answer to a button press. With "Show notifications" off it was
                    // dropped, and the button looked broken.
                    TempoNotify(2500, "Tempo", Localization.T("Nothing to export yet."), ToolTipIcon.Info, always: true);
                    return;
                }
                using (var dlg = new SaveFileDialog
                {
                    Title = Localization.T("Export captions as subtitles"),
                    Filter = Localization.T("SubRip subtitles (*.srt)|*.srt|WebVTT subtitles (*.vtt)|*.vtt"),
                    FileName = "Tempo-captions-" + DateTime.Now.ToString("yyyy-MM-dd-HHmm") + ".srt"
                })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) { return; }
                    bool vtt = dlg.FileName.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase);

                    // Cue times are relative to the FIRST line, so the file starts at zero
                    // like a subtitle track should, rather than at a wall-clock time.
                    DateTime origin = _captionHistoryTimes.Count > 0 ? _captionHistoryTimes[0] : DateTime.Now;

                    var sb = new StringBuilder();
                    if (vtt)
                    {
                        sb.AppendLine("WEBVTT");
                        sb.AppendLine("NOTE Times are approximate - each cue starts when its first words were spoken and ends shortly after its last.");
                        sb.AppendLine();
                    }

                    int cue = 0;
                    for (int i = 0; i < _captionHistory.Count; i++)
                    {
                        string line = _captionHistory[i];
                        if (string.IsNullOrWhiteSpace(line)) { continue; }
                        cue++;

                        TimeSpan start = (i < _captionHistoryTimes.Count ? _captionHistoryTimes[i] : origin) - origin;
                        // Ends a moment after its last words were spoken, or where the next cue
                        // begins if that is sooner — not held on screen through a silence.
                        TimeSpan end = (i < _transcript.EndTimes.Count ? _transcript.EndTimes[i] : origin)
                                       - origin + TimeSpan.FromSeconds(1.5);
                        if (i + 1 < _captionHistoryTimes.Count)
                        {
                            TimeSpan next = _captionHistoryTimes[i + 1] - origin;
                            if (next < end) { end = next; }
                        }
                        if (start < TimeSpan.Zero) { start = TimeSpan.Zero; }
                        if (end <= start) { end = start + TimeSpan.FromSeconds(1); }

                        sb.Append(cue).AppendLine();
                        sb.Append(CueTime(start, vtt)).Append(" --> ").Append(CueTime(end, vtt)).AppendLine();
                        sb.AppendLine(CueText(line, vtt));
                        sb.AppendLine();
                    }

                    System.IO.File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                    Logger.Info("[Captions] exported " + cue + " subtitle cues to " + dlg.FileName);
                    TempoNotify(3500, "Tempo",
                        Localization.F("Exported {0} subtitle lines.", cue)
                        + Environment.NewLine + dlg.FileName, ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                // A file that couldn't be written (read-only folder, open in another program, a
                // disconnected drive) was only logged: the export seemed to work and left no file.
                Logger.Warn("[Captions tab] subtitle export failed: " + ex.Message);
                ShowWarning(Localization.F("Couldn't save: {0}", ex.Message));
            }
        }

        /// <summary>
        /// One cue's text. A blank line ends a cue in both formats, so a line break inside a
        /// caption becomes a space. WebVTT reads its text as markup: "&lt;" opened a tag that
        /// swallowed the rest of the line ("x &lt; 5 so…" showed as "x"), "&amp;" started an
        /// entity, and "-->" is not allowed in a cue at all — they are escaped there. SubRip
        /// has no escapes, so its text is left as spoken.
        /// </summary>
        private static string CueText(string line, bool vtt)
        {
            string text = (line ?? "").Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
            if (vtt)
            {
                text = text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            }
            return text;
        }

        /// <summary>SubRip uses a comma before the milliseconds; WebVTT uses a dot.</summary>
        private static string CueTime(TimeSpan t, bool vtt)
        {
            return string.Format("{0:00}:{1:00}:{2:00}{3}{4:000}",
                (int)t.TotalHours, t.Minutes, t.Seconds, vtt ? "." : ",", t.Milliseconds);
        }

        /// <summary>
        /// The single path for changing a caption setting from anywhere.
        ///
        /// Both surfaces show the same values, and the Settings page additionally writes
        /// its CONTROLS back into _settings whenever Save Settings is pressed. That makes
        /// any write which skips the Settings control a silent revert waiting to happen,
        /// and every fix so far has been per-control whack-a-mole. This funnels the whole
        /// class: mutate, persist once, then push the settings back out to BOTH surfaces
        /// so nothing can be left showing a stale value.
        /// </summary>
        private void ChangeCaptionSetting(Action mutate, string what)
        {
            if (_settings == null || mutate == null) { return; }
            try
            {
                mutate();
                try { Persistence.SettingsManager.Save(_settings); } catch { }
                PushCaptionSettingsToAllSurfaces();
                if (!string.IsNullOrEmpty(what))
                {
                    Logger.Info("[Captions] " + what + " (both surfaces refreshed).");
                }
            }
            catch (Exception ex) { Logger.Warn("[Captions] setting change failed: " + ex.Message); }
        }

        /// <summary>
        /// Re-reads _settings into every caption control on BOTH the Settings page and the
        /// Captions tab, with the Settings-page change handlers suppressed so this cannot
        /// re-enter them. Adding a caption control in future means adding one line here —
        /// which is the point: there is now a single place that can be wrong, instead of
        /// one per control.
        /// </summary>
        private void PushCaptionSettingsToAllSurfaces()
        {
            if (_settings == null) { return; }
            bool prev = _suppressSettingsEvents;
            _suppressSettingsEvents = true;
            try
            {
                // --- Settings page ---
                if (_captionOverlayCheck != null) { _captionOverlayCheck.Checked = _settings.CaptionOverlayEnabled; }
                if (_captionGpuCheck != null) { _captionGpuCheck.Checked = _settings.CaptionTryGpu; }
                if (_captionAutoStartCheck != null) { _captionAutoStartCheck.Checked = _settings.CaptionAutoStart; }
                if (_captionFaceCheck != null) { _captionFaceCheck.Checked = _settings.CaptionFaceAnalysis; }
                if (_captionSpeakerCheck != null) { _captionSpeakerCheck.Checked = _settings.CaptionSpeakerTurns; }
                if (_captionTranscriptCheck != null) { _captionTranscriptCheck.Checked = _settings.CaptionSaveTranscripts; }
                if (_captionSourceTagCheck != null) { _captionSourceTagCheck.Checked = _settings.CaptionShowSourceTag; }
                if (_captionBackgroundCheck != null) { _captionBackgroundCheck.Checked = _settings.CaptionShowBackground; }
                if (_captionSourceCombo != null)
                {
                    int si = (int)_settings.CaptionSource;
                    if (si >= 0 && si < _captionSourceCombo.Items.Count) { _captionSourceCombo.SelectedIndex = si; }
                }
                if (_captionCaptureCombo != null)
                {
                    int ci = _settings.CaptionCaptureMode;
                    if (ci >= 0 && ci < _captionCaptureCombo.Items.Count) { _captionCaptureCombo.SelectedIndex = ci; }
                }
                SyncCaptionModelComboFromSettings();
                SyncCaptionLanguageCombosFromSettings();
            }
            catch (Exception ex) { Logger.Swallow("PushCaptionSettings", ex); }
            finally { _suppressSettingsEvents = prev; }

            // --- Captions tab --- (reads _settings live; this just repaints it now
            // rather than waiting up to 200 ms for the next UI tick)
            try { if (IsCaptionsTabVisible()) { RefreshCaptionsTab(); } }
            catch { }
        }

        /// <summary><paramref name="key"/> when that model is downloaded, otherwise null.</summary>
        private static string InstalledModelKeyOrNull(string key)
        {
            if (string.IsNullOrEmpty(key)) { return null; }
            var m = Utils.WhisperModelManager.FindByKey(key);
            return m != null && Utils.WhisperModelManager.IsInstalled(m) ? key : null;
        }

        /// <summary>The next model down in speed order, or null at the bottom.</summary>
        private static string NextFasterModelKey(string current)
        {
            // Slowest/most accurate first. NOTE the ladder deliberately steps
            // large -> large-q5 and skips 'medium'.
            //
            // By size: tiny 75 MB, base 140, small 460, large-q5 575, medium 1.5 GB,
            // large 1.6 GB. So 'medium' is barely cheaper than 'large' AND it is
            // English-only, while large/large-q5 handle 90+ languages — stepping from
            // large to medium would cost the user their language support to buy almost
            // no speed. large-q5 is the compressed build of the same model and is what
            // the engine's own auto-downgrade picks when large cannot hold pace.
            // The hand-written order above skipped 'medium' purely because it was
            // English-only and stepping onto it cost the user their language support.
            // Every size now has a multilingual build, so the shared ladder can carry
            // the rule properly instead: it walks WhisperModelManager.SpeedOrder and
            // skips English-only rungs whenever the current model understands more.
            var order = Utils.WhisperModelManager.SpeedOrder;
            bool keepMultilingual = current != null && !Utils.WhisperModelManager.IsEnglishOnly(current);
            int i = Utils.WhisperModelManager.IndexInSpeedOrder(current);
            if (i >= 0)
            {
                for (int n = i + 1; n < order.Count; n++)
                {
                    if (keepMultilingual && Utils.WhisperModelManager.IsEnglishOnly(order[n])) { continue; }
                    return order[n];
                }
                return null;
            }
            // Unknown or a variant key (e.g. "small.en") — step to small unless we are
            // clearly already below it.
            return current != null && current.StartsWith("tiny", StringComparison.OrdinalIgnoreCase)
                ? null : "small";
        }

        /// <summary>The transcript filter, parsed once per settled query rather than per line.</summary>
        private Utils.LogFilter _capParsedFilter = Utils.LogFilter.ParseTextOnly("");

        /// <summary>
        /// The transcript as text, timestamped, optionally through the filter.
        ///
        /// One builder for the box, Copy and Save. They used to be three: the box filtered,
        /// Copy took whatever the box happened to have rendered, and Save walked the raw
        /// history and ignored the filter completely — so with a filter active, the button
        /// labelled "Copy all" gave you the filtered lines and "Save…" gave you everything.
        /// Whichever of those a user wanted, one of the two buttons lied to them.
        /// </summary>
        private string BuildTranscriptText(bool honourFilter, out int shown)
        {
            var sb = new StringBuilder();
            shown = 0;
            bool filtering = honourFilter && !_capParsedFilter.IsEmpty;
            for (int i = 0; i < _captionHistory.Count; i++)
            {
                string line = _captionHistory[i];
                if (string.IsNullOrEmpty(line)) { continue; }
                if (filtering && !_capParsedFilter.IsMatch(line)) { continue; }
                string stamp = i < _captionHistoryTimes.Count
                    ? _captionHistoryTimes[i].ToString("HH:mm:ss") + "  "
                    : "";
                sb.Append(stamp).AppendLine(line);
                shown++;
            }
            return sb.ToString();
        }

        /// <summary>
        /// The same transcript, as RTF: each timestamp muted, each line in the text colour,
        /// and every filter match marked. Built and handed over in one assignment — see
        /// UI/RtfBuilder.cs for what filling a RichTextBox a piece at a time costs.
        /// </summary>
        private string BuildTranscriptRtf()
        {
            Color text = _theme != null ? _theme.Text : Color.White;
            Color muted = _theme != null ? _theme.TextMuted : Color.Gray;
            bool light = _theme != null && _theme.InputBackground.GetBrightness() > 0.5f;
            Color hiBack = light ? Color.FromArgb(255, 226, 120) : Color.FromArgb(120, 96, 0);
            Color hiFore = light ? Color.FromArgb(28, 26, 12) : Color.FromArgb(255, 244, 205);

            var rtf = new RtfBuilder(_capTranscript.Font, Math.Max(1024, _captionHistory.Count * 64));
            bool filtering = !_capParsedFilter.IsEmpty;
            for (int i = 0; i < _captionHistory.Count; i++)
            {
                string line = _captionHistory[i];
                if (string.IsNullOrEmpty(line)) { continue; }
                if (filtering && !_capParsedFilter.IsMatch(line)) { continue; }

                if (i < _captionHistoryTimes.Count)
                {
                    rtf.Run(_captionHistoryTimes[i].ToString("HH:mm:ss") + "  ", muted);
                }
                // Each speaker's label in that speaker's colour, as the caption bar, the history window and
                // the Speakers card show it (readable on this box's ground) — here every line was one colour,
                // so who said what had to be read off the words "Speaker 2:".
                int labelLen = Math.Min(line.Length, _transcript.LabelLengthOf(i));
                Color labelColor = labelLen > 0 ? SpeakerInk(_transcript.SpeakerOf(i)) : text;
                void Plain(int from, int to)
                {
                    if (from < labelLen && to > from)
                    {
                        int mid = Math.Min(to, labelLen);
                        rtf.Run(line.Substring(from, mid - from), labelColor);
                        from = mid;
                    }
                    if (to > from) { rtf.Run(line.Substring(from, to - from), text); }
                }
                int at = 0;
                if (filtering && _capParsedFilter.HasHighlightableText)
                {
                    foreach (var span in _capParsedFilter.Highlights(line))
                    {
                        int s = Math.Max(at, Math.Min(span.Key, line.Length));
                        int e = Math.Min(line.Length, span.Key + span.Value);
                        if (e <= s) { continue; }
                        Plain(at, s);
                        rtf.Run(line.Substring(s, e - s), hiFore, hiBack);
                        at = e;
                    }
                }
                Plain(at, line.Length);
                rtf.EndLine();
            }
            return rtf.ToRtf();
        }

        /// <summary>How many lines a rendered transcript string holds.</summary>
        private static int CountRenderedLines(string rendered)
        {
            if (string.IsNullOrEmpty(rendered)) { return 0; }
            int n = 0;
            foreach (char c in rendered) { if (c == '\n') { n++; } }
            return n;
        }

        /// <summary>Lines that have landed while the reader is scrolled away from the newest.</summary>
        private int _capNewBelow;

        /// <summary>
        /// Adds "· N new below" to the transcript's count line while the view is scrolled up,
        /// and makes the line a way back down. Without it, speech that arrives while you are
        /// reading back is silent — the box does not move, by design, and nothing says why.
        /// </summary>
        private void UpdateTranscriptNewBelow()
        {
            if (_capCountLabel == null) { return; }
            bool show = _capNewBelow > 0;
            string suffix = show ? "   ·   " + Localization.F("{0} new below ↓", _capNewBelow) : "";
            string baseText = _capCountBaseText ?? _capCountLabel.Text ?? "";
            string wanted = baseText + suffix;
            if (!string.Equals(_capCountLabel.Text, wanted, StringComparison.Ordinal))
            {
                _capCountLabel.Text = wanted;
            }
            _capCountLabel.Cursor = show ? Cursors.Hand : Cursors.Default;
            if (_theme != null)
            {
                _capCountLabel.ForeColor = show ? _theme.Accent : _theme.TextMuted;
            }
        }

        /// <summary>The count line without the "N new below" suffix, so the two never compound.</summary>
        private string _capCountBaseText;

        /// <summary>
        /// The transcript card's Clear. It is one small button beside Subtitles…, and what it threw
        /// away — for someone who can't hear the audio, the only record of what was said — could not be
        /// brought back, with no question asked. Now: with "Save transcripts to disk" on, the lines are
        /// written to a transcript file first (as the end of a session would) and nothing needs asking;
        /// otherwise it asks. It also left "N new below" standing under the empty box.
        /// </summary>
        private void ClearCaptionTranscript()
        {
            try
            {
                if (_captionHistory.Count > 0)
                {
                    if (_settings != null && _settings.CaptionSaveTranscripts)
                    {
                        SaveTranscriptIfWanted();
                    }
                    else if (MessageBox.Show(this,
                                 Localization.T("Clear the transcript? It isn't saved anywhere, so it can't be brought back."),
                                 "Tempo", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    {
                        return;
                    }
                    _transcript.Clear();
                    try { _captionHistoryForm?.SetHistory(_transcript); } catch { }
                }
                _capNewBelow = 0;
                _capRenderedText = null;
                RefreshCaptionTranscript();
            }
            catch (Exception ex) { Logger.Warn("[Captions tab] clear failed: " + ex.Message); }
        }

        /// <summary>Jumps the transcript to the newest line and clears the "new below" note.</summary>
        private void JumpTranscriptToNewest()
        {
            if (_capTranscript == null || _capTranscript.IsDisposed) { return; }
            try
            {
                _capTranscript.SelectionStart = _capTranscript.TextLength;
                _capTranscript.SelectionLength = 0;
                _capTranscript.ScrollToCaret();
            }
            catch { }
            _capNewBelow = 0;
            UpdateTranscriptNewBelow();
        }

        /// <summary>
        /// Keeps the Copy/Save captions honest about what they will actually produce, so the
        /// buttons cannot promise "all" while a filter is hiding most of it.
        /// </summary>
        private void RefreshTranscriptButtonLabels()
        {
            bool filtering = _capParsedFilter != null && !_capParsedFilter.IsEmpty;
            if (_capCopyBtn != null)
            {
                string label = filtering ? Localization.T("Copy shown") : Localization.T("Copy all");
                // Saying "Copied ✓" right now (ConfirmOnButton): it is the caption to come BACK to that changes.
                if (ReferenceEquals(_saveConfirmTarget, _capCopyBtn)) { _saveBtnText = label; }
                else { _capCopyBtn.Text = label; }
            }
            if (_capSaveBtn != null && _capTip != null)
            {
                TipStyle.Set(_capTip, _capSaveBtn, filtering
                    ? Localization.T("Saves only the lines matching the filter.")
                    : Localization.T("Saves the whole transcript."));
            }
        }

        /// <summary>
        /// True when the Captions tab is the one on screen. Found by walking up from the
        /// transcript box to its owning TabPage rather than by index, so inserting or
        /// reordering tabs later can't quietly turn this into the wrong answer.
        /// </summary>
        private bool IsCaptionsTabVisible()
        {
            try
            {
                if (_capTranscript == null || _tabs == null || _tabs.SelectedTab == null)
                {
                    return false;
                }
                for (Control c = _capTranscript; c != null; c = c.Parent)
                {
                    if (c is TabPage page) { return ReferenceEquals(page, _tabs.SelectedTab); }
                }
                return false;
            }
            catch { return false; }
        }

        /// <summary>
        /// True when the newest lines are already on screen, so a new caption may follow the
        /// tail without dragging the user away from what they were reading.
        ///
        /// It compared two different kinds of "line". GetLineFromCharIndex counts PHYSICAL
        /// lines — it asks the control, and the control counts every wrapped row — while
        /// <c>Lines.Length</c> counts LOGICAL ones, splitting only on newlines. The transcript
        /// has WordWrap on and caption lines are long, so one logical line is routinely three
        /// physical ones: the logical total was far smaller than the physical position, the
        /// test read as "at the bottom" from about a third of the way down, and reading back
        /// through a transcript kept snapping to the end whenever anyone spoke.
        ///
        /// Asking the control for the last physical line keeps both sides in the same unit.
        /// </summary>
        private bool IsTranscriptScrolledToBottom()
        {
            try
            {
                int visibleLines = Math.Max(1, _capTranscript.ClientSize.Height / Math.Max(1, _capTranscript.Font.Height));
                int firstVisible = _capTranscript.GetLineFromCharIndex(_capTranscript.GetCharIndexFromPosition(new Point(1, 1)));
                int lastLine = _capTranscript.GetLineFromCharIndex(Math.Max(0, _capTranscript.TextLength - 1));
                return firstVisible + visibleLines >= lastLine;
            }
            catch { return true; }
        }

        /// <summary>A speaker's colour as text in the transcript box: the bar's colour, made readable on the box.</summary>
        private Color SpeakerInk(int label)
        {
            Color c = CaptionOverlayForm.SpeakerColor(label);
            return _theme != null ? Theme.Readable(c, _theme.InputBackground) : c;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW")]
        private static extern IntPtr SendMessagePtr(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        private const int EM_GETFIRSTVISIBLELINE = 0x00CE, EM_LINESCROLL = 0x00B6;

        /// <summary>The transcript box's top row on screen (wrapped rows count).</summary>
        private int FirstVisibleTranscriptRow()
        {
            try { return (int)SendMessagePtr(_capTranscript.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero); }
            catch { return 0; }
        }

        /// <summary>
        /// Puts back a selection the rewrite of the transcript box dropped, and the rows the reader had on
        /// screen. By position when the text there is unchanged; otherwise — the oldest lines left the
        /// 500 kept, and everything moved up — the nearest place that text is found. Never a selection of
        /// different words.
        /// </summary>
        private void RestoreTranscriptSelection(int start, string selected, int firstRow)
        {
            try
            {
                string all = _capTranscript.Text;
                int at = -1;
                if (start >= 0 && start + selected.Length <= all.Length &&
                    string.CompareOrdinal(all, start, selected, 0, selected.Length) == 0)
                {
                    at = start;
                }
                else
                {
                    int from = Math.Max(0, Math.Min(start, all.Length - 1));
                    // Backwards from where the selection ENDED, so text that moved up by less than its own
                    // length is still found.
                    int backFrom = Math.Min(all.Length - 1, start + selected.Length - 1);
                    int back = backFrom >= 0 ? all.LastIndexOf(selected, backFrom, StringComparison.Ordinal) : -1;
                    int fwd = all.IndexOf(selected, from, StringComparison.Ordinal);
                    at = back < 0 ? fwd : fwd < 0 ? back : (start - back <= fwd - start ? back : fwd);
                }
                if (at >= 0) { _capTranscript.Select(at, selected.Length); }
                // Selecting scrolls the selection into view; the reader's rows go back where they were.
                int now = FirstVisibleTranscriptRow();
                if (now != firstRow)
                {
                    SendMessagePtr(_capTranscript.Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)(firstRow - now));
                }
            }
            catch (Exception ex) { Logger.Swallow("RestoreTranscriptSelection", ex); }
        }

        private void CopyTranscript()
        {
            try
            {
                // Built from the history, not from _capRenderedText. That field is a RENDER
                // CACHE: it is only written while the tab is on screen and the text changed,
                // so copying through it meant copying whatever happened to have been drawn.
                string text = BuildTranscriptText(true, out int shown);
                if (text.Length == 0)
                {
                    // Silence here read as "the button is broken". Say which of the two it is —
                    // always, or "Show notifications" off brought the silence back.
                    TempoNotify(2200, "Tempo", _captionHistory.Count == 0
                        ? Localization.T("There is nothing transcribed to copy yet.")
                        : Localization.T("No lines match the filter, so nothing was copied."),
                        ToolTipIcon.Info, always: true);
                    return;
                }
                Clipboard.SetText(text);
                // On the button itself: with "Show notifications" off, the card below never comes, and a copy
                // that leaves no trace reads as a button that did nothing.
                ConfirmOnButton(_capCopyBtn, Localization.T("Copied ✓"));
                TempoNotify(1800, "Tempo", _capParsedFilter.IsEmpty
                    ? Localization.T("Transcript copied to the clipboard.")
                    : Localization.F("{0} matching line(s) copied to the clipboard.", shown),
                    ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                // Another program holding the clipboard (SetText already retries for a second) was
                // only logged, so Copy seemed to work and pasting gave the old clipboard.
                Logger.Warn("[Captions tab] copy failed: " + ex.Message);
                TempoNotify(3000, "Tempo", Localization.T("Couldn't reach the clipboard — try again."),
                    ToolTipIcon.Warning, always: true);
            }
        }

        private void SaveTranscript()
        {
            try
            {
                // Like Copy: say why nothing happens. Save returned silently with nothing heard yet,
                // and with a filter that matched nothing it wrote an empty file.
                if (_captionHistory.Count == 0 || BuildTranscriptText(true, out _).Length == 0)
                {
                    TempoNotify(2200, "Tempo", _captionHistory.Count == 0
                        ? Localization.T("There is nothing transcribed to save yet.")
                        : Localization.T("No lines match the filter, so nothing was saved."),
                        ToolTipIcon.Info, always: true);
                    return;
                }
                using (var dlg = new SaveFileDialog
                {
                    Title = Localization.T("Save transcript"),
                    Filter = Localization.T("Text file (*.txt)|*.txt"),
                    FileName = "Tempo-transcript-" + DateTime.Now.ToString("yyyy-MM-dd-HHmm") + ".txt"
                })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) { return; }
                    // Through the same builder as Copy and the box, so all three agree about
                    // what "the transcript" is while a filter is on.
                    string text = BuildTranscriptText(true, out int shown);
                    System.IO.File.WriteAllText(dlg.FileName, text, Encoding.UTF8);
                    TempoNotify(2500, "Tempo",
                        (_capParsedFilter.IsEmpty
                            ? Localization.T("Transcript saved.")
                            : Localization.F("Saved {0} of {1} lines (the filter is on).", shown, _captionHistory.Count))
                        + Environment.NewLine + dlg.FileName, ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                // Only logged before: the save seemed to work and there was no file.
                Logger.Warn("[Captions tab] save failed: " + ex.Message);
                ShowWarning(Localization.F("Couldn't save: {0}", ex.Message));
            }
        }
    }
}
