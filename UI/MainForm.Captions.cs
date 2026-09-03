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
        private TextBox _capTranscript;
        private TextBox _capSearchBox;
        private System.Windows.Forms.Timer _capSearchDebounce;
        private Label _capCountLabel;
        private Button _capOverlayBtn;
        private Button _capHistoryBtn;
        private CaptionLevelMeter _capLevel;
        private Label _capQualityLine;
        // The Captions tab's copy of the language picker. Its twin on the Settings page
        // is _captionLangCombo; both go through ApplyCaptionLanguage so they cannot
        // disagree with each other or with what is on disk.
        private ComboBox _capLangCombo;
        private Label _capLangLabel;

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
            status.Controls.Add(_capStatePill);

            _capEngineLine = UiFactory.Caption("", 16, 60);
            status.Controls.Add(_capEngineLine);
            _capSourceLine = UiFactory.Caption("", 16, 80);
            status.Controls.Add(_capSourceLine);

            // The delay line. "The video says something, then the caption catches up
            // later" is the complaint this answers — in seconds, with the reason.
            _capDelayLine = UiFactory.Caption("", 16, 100);
            // The button column starts at 520; cap the label short of it so a longer
            // number can never grow underneath the button (it wraps instead).
            _capDelayLine.MaximumSize = new Size(492, 0);
            status.Controls.Add(_capDelayLine);

            _capStartStopBtn = UiFactory.PrimaryButton("Start captions", 520, 26, 156, 34, _theme);
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
            status.Controls.Add(UiFactory.Caption("Input:", 16, 126));
            _capLevel = new CaptionLevelMeter { Left = 60, Top = 126, Width = 300, Height = 14 };
            status.Controls.Add(_capLevel);

            // Confidence / clipping / language lock — all computed by the engine already.
            _capQualityLine = UiFactory.Caption("", 16, 148);
            _capQualityLine.MaximumSize = new Size(660, 0);
            status.Controls.Add(_capQualityLine);

            // ── The running transcript ────────────────────────────────────────
            var live = UiFactory.Group(Localization.T("Transcript"), 12, 210, 696, 348, CardIcon.Caption);

            live.Controls.Add(UiFactory.Label("Find:", 16, 32));
            // x=96, not 60: "Find:" fits in 44px but "Rechercher :" needs 66, and the
            // label was overprinting this box in four of the five languages.
            // Width 200, not 240: "Copy all" sits at x=312, so the box has to give back
            // what it took from the label rather than grow into the button.
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
                    // full of "hello".
                    _capFilter = (_capSearchBox.Text ?? "").Trim();
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

            var copyBtn = UiFactory.Button("Copy all", 312, 26, 104, 28);
            copyBtn.Click += (s, e) => CopyTranscript();
            live.Controls.Add(copyBtn);

            var saveBtn = UiFactory.Button("Save…", 424, 26, 96, 28);
            saveBtn.Click += (s, e) => SaveTranscript();
            live.Controls.Add(saveBtn);

            var srtBtn = UiFactory.Button("Subtitles…", 524, 26, 100, 28);
            srtBtn.Click += (s, e) => ExportTranscriptSubtitles();
            live.Controls.Add(srtBtn);

            var clearBtn = UiFactory.Button("Clear", 630, 26, 46, 28);
            clearBtn.Click += (s, e) =>
            {
                _captionHistory.Clear();
                _captionHistoryTimes.Clear();
                try { _captionHistoryForm?.SetHistory(_captionHistory); } catch { }
                _capRenderedText = null;
                RefreshCaptionTranscript();
            };
            live.Controls.Add(clearBtn);

            _capTranscript = new TextBox
            {
                Left = 16,
                Top = 62,
                Width = 660,
                Height = 258,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                BorderStyle = BorderStyle.FixedSingle,
                TabStop = false
            };
            live.Controls.Add(_capTranscript);

            // y=326, not 352. The card is 348 tall, so a child at 352 sits BELOW its own
            // client area and is clipped away entirely — which is why none of the three
            // messages this label carries has ever been seen: the empty-state hint
            // ("Nothing transcribed yet — start captions and play something with
            // speech."), the line counter, and the "Nothing matches … press Esc to clear
            // the filter." that explains an empty box during a search. The transcript
            // ends at 62+258=320, so this sits just under it with room to spare.
            _capCountLabel = UiFactory.Caption("", 16, 326);
            _capCountLabel.AutoSize = false;
            _capCountLabel.Size = new Size(660, 16);
            live.Controls.Add(_capCountLabel);

            // ── Actions ───────────────────────────────────────────────────────
            // Stays 104 tall. A taller card pushed the page past the 689px client area
            // and put the language picker below the fold, which is a poor home for a
            // control this tab exists to expose — so the row shares line 2 instead.
            var actions = UiFactory.Group(Localization.T("Windows & model"), 12, 570, 696, 104, CardIcon.Gear);

            _capOverlayBtn = UiFactory.Button("Show caption bar", 16, 30, 172, 30);
            _capOverlayBtn.Click += (s, e) => ToggleCaptionOverlayBar();
            actions.Controls.Add(_capOverlayBtn);

            _capHistoryBtn = UiFactory.Button("Show history window", 198, 30, 190, 30);
            _capHistoryBtn.Click += (s, e) =>
            {
                try { ToggleCaptionHistoryWindow(); } catch (Exception ex) { Logger.Warn("[Captions tab] history: " + ex.Message); }
                RefreshCaptionsTab();
            };
            actions.Controls.Add(_capHistoryBtn);

            var dlBtn = UiFactory.Button("Download model…", 398, 30, 160, 30);
            dlBtn.Click += OnDownloadCaptionModel;      // the same handler Settings uses
            actions.Controls.Add(dlBtn);

            // A menu rather than a plain button, because BOTH rows of this card are full and
            // the card is pinned at 104 tall on purpose (a taller one pushes the language
            // picker below the 689 px fold — see the note above). The menu adds the
            // "use a model from anywhere" option without costing a single pixel.
            var modelsBtn = UiFactory.Button("Models…", 568, 30, 110, 30);
            modelsBtn.Click += (s, e) => ShowModelSourceMenu(modelsBtn);
            actions.Controls.Add(modelsBtn);

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
            page.Controls.Add(live);
            page.Controls.Add(actions);
            _tabs.TabPages.Add(page);
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
                    pillColor = _theme.Danger;
                }
                else if (tr != null && tr.IsStarting)
                {
                    pill = "◌  Loading the speech model…";
                    pillColor = _theme.TextMuted;
                }
                else
                {
                    pill = "●  Listening…";
                    pillColor = _theme.Success;
                }
                _capStatePill.Text = pill;
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
                    if (!tempoEngine)
                    {
                        sb.Append("Using Windows Live Captions");
                        if (_captionFellBackToWindows)
                        {
                            sb.Append("   ·   Tempo's engine fell back to it this session");
                        }
                    }
                    else
                    {
                        sb.Append("Model: ").Append(string.IsNullOrEmpty(_captionModelActiveKey)
                            ? (string.IsNullOrEmpty(_settings.CaptionModelKey) ? "base" : _settings.CaptionModelKey)
                            : _captionModelActiveKey);
                        sb.Append("   ·   Engine: ").Append(_settings.CaptionTryGpu ? "GPU (if available)" : "CPU");
                        // What language the text is being decoded as. Pinned shows the
                        // language; auto shows what detection has settled on, or that it
                        // is still deciding — which is the state game audio tends to sit
                        // in, and which used to be invisible from here.
                        sb.Append("   ·   Language: ");
                        string pinned = _settings.CaptionLanguage;
                        if (!string.IsNullOrEmpty(pinned) &&
                            !pinned.Equals("auto", StringComparison.OrdinalIgnoreCase))
                        {
                            sb.Append(CaptionLanguageLabel(pinned));
                            if (_captionsActive && _captionTranscriber != null &&
                                !string.IsNullOrEmpty(_captionTranscriber.LanguageState) &&
                                _captionTranscriber.LanguageState.IndexOf(pinned,
                                    StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                sb.Append(" (restart captions to apply)");
                            }
                        }
                        else
                        {
                            sb.Append(_captionsActive && _captionTranscriber != null &&
                                      !string.IsNullOrEmpty(_captionTranscriber.LanguageState)
                                ? _captionTranscriber.LanguageState
                                : "auto-detect");
                        }
                        if (TempoTranscriber.GpuWouldHelp && !_settings.CaptionTryGpu)
                        {
                            sb.Append("  — running behind; the GPU engine would help");
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
                    }
                    else
                    {
                        line = Localization.F("Will use: {0} engine", _settings.CaptionSource);
                    }
                    _capSourceLine.Text = line
                        + (_settings.CaptionFaceAnalysis
                            ? Localization.T("   ·   face && mouth analysis on") : "");
                }

                RefreshCaptionDelayLine();
                RefreshCaptionQuality(tr);

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

                var sb = new StringBuilder();
                int shown = 0;
                bool filtering = !string.IsNullOrWhiteSpace(_capFilter);
                for (int i = 0; i < _captionHistory.Count; i++)
                {
                    string line = _captionHistory[i];
                    if (string.IsNullOrEmpty(line)) { continue; }
                    if (filtering && line.IndexOf(_capFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                    string stamp = i < _captionHistoryTimes.Count
                        ? _captionHistoryTimes[i].ToString("HH:mm:ss") + "  "
                        : "";
                    sb.Append(stamp).AppendLine(line);
                    shown++;
                }

                string text = sb.ToString();

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
                    // Label repaints it whether or not the string is new.
                    if (!string.Equals(_capCountLabel.Text, caption, StringComparison.Ordinal))
                    {
                        _capCountLabel.Text = caption;
                    }
                }

                // NOW the early-out, and only for the expensive half. Rewriting the
                // transcript box throws away the selection and scroll position, so it is
                // still skipped whenever the text is unchanged — which is most ticks
                // during a long run.
                if (text == _capRenderedText) { return; }
                _capRenderedText = text;

                // Only follow the tail when the user is already at the bottom, so reading
                // back through the transcript isn't yanked away every time a line lands.
                bool atBottom = IsTranscriptScrolledToBottom();
                _capTranscript.Text = text;
                if (atBottom)
                {
                    _capTranscript.SelectionStart = _capTranscript.TextLength;
                    _capTranscript.ScrollToCaret();
                }
            }
            catch (Exception ex) { Logger.Swallow("RefreshCaptionTranscript", ex); }
        }

        /// <summary>
        /// True when the Captions tab is the one on screen. Found by walking up from the
        /// transcript box to its owning TabPage rather than by index, so inserting or
        /// reordering tabs later can't quietly turn this into the wrong answer.
        /// </summary>

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
        /// Fills the input meter and the quality line from signals the engine already
        /// produces. Every one of these existed only in Live debug (or, for confidence,
        /// nowhere at all), while the tab that calls itself the operational view showed
        /// none of them.
        /// </summary>
        /// <summary>
        /// Where a speech model may come from: the folder Tempo downloads into, a file
        /// anywhere on disk, or one of the loose files already sitting in that folder.
        ///
        /// Whisper models are large — large-v3 is roughly 3 GB — and anyone already running
        /// whisper.cpp, Subtitle Edit, Buzz or their own fine-tune has one on a drive
        /// somewhere. Requiring a second copy purely because it must live in Tempo's folder
        /// is a cost with nothing behind it, so Tempo can read one where it already is.
        /// </summary>
        private void ShowModelSourceMenu(Control anchor)
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
                        ToolTipText = pf.FileName + "\n" + p,
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
                        ToolTipText = lf.FileName + "\n" + p,
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
                menu.Items.Add(new ToolStripMenuItem(
                    (cf.Valid ? "Using: " + cf.Headline : "Using a file that is missing: " + cf.FileName)
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
                "Tempo's speech models come from the whisper.cpp project on Hugging Face — the same " +
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
                "Open the page in your browser now?",
                "Where to get speech models",
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

            if (_captionsActive)
            {
                try { StopTempoCaptions(); StartTempoCaptions(); }
                catch (Exception ex) { Logger.Warn("[Captions] restart after model change: " + ex.Message); }
            }
            RefreshCaptionsTab();
        }

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

            if (tr.InputClipping)
            {
                sb.Append("⚠ Audio is clipping — turn the app or system volume down; a smaller model will not help.");
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
                sb.Append("⚠ ").Append(mixedApps)
                  .Append(" apps are playing sound at once — captions are a blend of all of them, so ")
                  .Append("sentences may combine things different sources said. Mute the ones you don't ")
                  .Append("need captioned.");
            }
            else
            {
                double conf = tr.LastConfidence;
                if (conf >= 0)
                {
                    sb.Append("Confidence ").Append(Math.Round(conf * 100)).Append('%');
                    if (conf < 0.45) { sb.Append(" — low; the audio may be quiet, noisy or not speech"); }
                }

                string lang = tr.LanguageState;
                if (!string.IsNullOrEmpty(lang))
                {
                    if (sb.Length > 0) { sb.Append("   ·   "); }
                    sb.Append("Language ").Append(lang);
                }

                // A caption engine that has gone quiet is indistinguishable from a quiet
                // room on every other surface. This is the cheapest detector there is and
                // nothing was consulting it.
                double quiet = tr.SecondsSinceLastCaption;
                if (quiet >= 20)
                {
                    if (sb.Length > 0) { sb.Append("   ·   "); }
                    sb.Append("⚠ nothing transcribed for ").Append((int)quiet).Append(" s");
                }
            }

            _capQualityLine.Text = sb.ToString();
            _capQualityLine.ForeColor = warn ? _theme.Danger : _theme.TextMuted;
        }

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
            double delay = t.EstimatedDelaySeconds;
            var sb = new StringBuilder();
            sb.Append("Delay ~").Append(delay.ToString("0.0")).Append(" s behind");
            if (rtf > 0)
            {
                sb.Append("  ·  ").Append(rtf.ToString("0.0")).Append("× real time");
            }
            if (t.BacklogDroppedSeconds > 0.5)
            {
                sb.Append("  ·  ").Append(t.BacklogDroppedSeconds.ToString("0")).Append(" s skipped");
            }
            _capDelayLine.Text = sb.ToString();

            bool cannotKeepUp = rtf >= 1.0 || TempoTranscriber.GpuWouldHelp;
            _capDelayLine.ForeColor = cannotKeepUp ? _theme.Danger : _theme.TextMuted;

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
                string current = _captionModelActiveKey ?? _settings.CaptionModelKey ?? "base";
                string next = NextFasterModelKey(current);
                if (next == null)
                {
                    TempoNotify(4000, "Tempo",
                        Localization.T("Already on the fastest model. Turning on the GPU engine in Settings is the next step."),
                        ToolTipIcon.Info);
                    return;
                }

                // Through the funnel: it persists and repaints BOTH surfaces, so the
                // Settings combo cannot be left showing the old model (Save Settings
                // writes CaptionModelKey from that combo and would have reverted this).
                ChangeCaptionSetting(() => _settings.CaptionModelKey = next,
                    "model switched to '" + next + "' to keep pace");

                bool wasOn = _captionsActive;
                if (wasOn) { SetCaptionsActive(false); }
                if (wasOn) { _captionsActive = true; SetCaptionsActive(true); }

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
            // The engine reads the language when a run STARTS, so a change mid-run only
            // lands on the next start — not on a Tempo restart the way the GPU order
            // does. RefreshCaptionsTab spells that out on the engine line rather than
            // firing a notification, because these are exactly the routine messages the
            // user asked to stop being told about.
        }

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
                // starting, so there is a real reason to put one up.
                ShowCaptionOverlay();
            }
            else if (_captionOverlay != null && !_captionOverlay.IsDisposed)
            {
                try { _captionOverlay.Hide(); } catch { }
            }
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

        /// <summary>
        /// Writes the transcript as SubRip (.srt) or WebVTT (.vtt).
        ///
        /// The per-line times were already being kept — and were only ever used to prefix
        /// a plain .txt. Subtitle formats are what a deaf user can actually DO something
        /// with: load them next to a recording, hand them to someone else, or check what
        /// was said at a given moment.
        ///
        /// Honest limitation: only one timestamp is stored per line (when that line was
        /// last updated), so a cue ends where the next one begins, and the final cue gets
        /// a nominal three seconds. Cue timings are therefore approximate, and the file
        /// says so in a header comment where the format allows one.
        /// </summary>
        private void ExportTranscriptSubtitles()
        {
            try
            {
                if (_captionHistory.Count == 0)
                {
                    TempoNotify(2500, "Tempo", Localization.T("Nothing to export yet."), ToolTipIcon.Info);
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
                        sb.AppendLine("NOTE Times are approximate - Tempo records one timestamp per caption line.");
                        sb.AppendLine();
                    }

                    int cue = 0;
                    for (int i = 0; i < _captionHistory.Count; i++)
                    {
                        string line = _captionHistory[i];
                        if (string.IsNullOrWhiteSpace(line)) { continue; }
                        cue++;

                        TimeSpan start = (i < _captionHistoryTimes.Count ? _captionHistoryTimes[i] : origin) - origin;
                        TimeSpan end = i + 1 < _captionHistoryTimes.Count
                            ? _captionHistoryTimes[i + 1] - origin
                            : start + TimeSpan.FromSeconds(3);
                        if (start < TimeSpan.Zero) { start = TimeSpan.Zero; }
                        if (end <= start) { end = start + TimeSpan.FromSeconds(1); }

                        sb.Append(cue).AppendLine();
                        sb.Append(CueTime(start, vtt)).Append(" --> ").Append(CueTime(end, vtt)).AppendLine();
                        sb.AppendLine(line.Trim());
                        sb.AppendLine();
                    }

                    System.IO.File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                    Logger.Info("[Captions] exported " + cue + " subtitle cues to " + dlg.FileName);
                    TempoNotify(3500, "Tempo",
                        Localization.F("Exported {0} subtitle lines.", cue)
                        + Environment.NewLine + dlg.FileName, ToolTipIcon.Info);
                }
            }
            catch (Exception ex) { Logger.Warn("[Captions tab] subtitle export failed: " + ex.Message); }
        }

        /// <summary>SubRip uses a comma before the milliseconds; WebVTT uses a dot.</summary>
        private static string CueTime(TimeSpan t, bool vtt)
        {
            return string.Format("{0:00}:{1:00}:{2:00}{3}{4:000}",
                (int)t.TotalHours, t.Minutes, t.Seconds, vtt ? "." : ",", t.Milliseconds);
        }

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

        private bool IsTranscriptScrolledToBottom()
        {
            try
            {
                int visibleLines = Math.Max(1, _capTranscript.ClientSize.Height / Math.Max(1, _capTranscript.Font.Height));
                int firstVisible = _capTranscript.GetLineFromCharIndex(_capTranscript.GetCharIndexFromPosition(new Point(1, 1)));
                return firstVisible + visibleLines >= _capTranscript.Lines.Length - 1;
            }
            catch { return true; }
        }

        private void CopyTranscript()
        {
            try
            {
                string text = _capRenderedText ?? "";
                if (text.Length == 0) { return; }
                Clipboard.SetText(text);
                TempoNotify(1800, "Tempo", Localization.T("Transcript copied to the clipboard."), ToolTipIcon.Info);
            }
            catch (Exception ex) { Logger.Warn("[Captions tab] copy failed: " + ex.Message); }
        }

        private void SaveTranscript()
        {
            try
            {
                if (_captionHistory.Count == 0) { return; }
                using (var dlg = new SaveFileDialog
                {
                    Title = Localization.T("Save transcript"),
                    Filter = Localization.T("Text file (*.txt)|*.txt"),
                    FileName = "Tempo-transcript-" + DateTime.Now.ToString("yyyy-MM-dd-HHmm") + ".txt"
                })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) { return; }
                    var sb = new StringBuilder();
                    for (int i = 0; i < _captionHistory.Count; i++)
                    {
                        string stamp = i < _captionHistoryTimes.Count
                            ? _captionHistoryTimes[i].ToString("HH:mm:ss") + "  "
                            : "";
                        sb.Append(stamp).AppendLine(_captionHistory[i]);
                    }
                    System.IO.File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                    TempoNotify(2500, "Tempo",
                        Localization.T("Transcript saved.") + Environment.NewLine + dlg.FileName, ToolTipIcon.Info);
                }
            }
            catch (Exception ex) { Logger.Warn("[Captions tab] save failed: " + ex.Message); }
        }
    }
}
