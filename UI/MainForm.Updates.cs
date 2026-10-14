using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using AutoClicker.Persistence;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// Updates: asking GitHub, offering what it finds, and downloading and installing it.
    ///
    /// Gathered here from three files, because the pieces had drifted apart in ways that showed:
    ///  · Every trigger ran its OWN check — the launch check, the Settings button and the tray
    ///    item — so two at once produced two update dialogs, one on top of the other.
    ///  · The launch check was the only automatic one. If it failed (the network is often not up
    ///    yet at sign-in) nothing tried again, and Tempo — which lives in the tray for days —
    ///    never looked again until it was restarted.
    ///  · "Update now" left through Environment.Exit, which skips the normal shutdown: the run in
    ///    progress was never recorded, a held movement key stayed held, the caption transcript was
    ///    thrown away and Windows' own caption bar could be left off-screen.
    ///  · A failed install was silent, and threw the download away.
    /// </summary>
    public partial class MainForm
    {
        // ── State ─────────────────────────────────────────────────────────────

        /// <summary>The ONE check in flight, whoever asked for it; null when none is running.</summary>
        private Task<UpdateChecker.UpdateResult> _updateCheckTask;

        /// <summary>Everyone waiting for <see cref="_updateCheckTask"/>, answered together when it ends.</summary>
        private List<Action<UpdateChecker.UpdateResult>> _updateCheckWaiters;

        /// <summary>Someone pressed a button for the check in flight — their answer is the one shown.</summary>
        private bool _updateCheckUserAsked;

        /// <summary>
        /// Set while the waiters of a finished check run: true when a person asked for it. The
        /// automatic waiter then leaves the prompt to them — the check is shared, and without this a
        /// click during the automatic check opened the dialog twice, one after the other.
        /// </summary>
        private bool _updateCheckAnsweredUser;

        /// <summary>The newest successful answer this session — enough to reopen the prompt without asking GitHub again.</summary>
        private UpdateChecker.UpdateResult _lastUpdateResult;
        private DateTime _lastUpdateResultUtc;

        /// <summary>When the automatic check may run next. MaxValue = not armed (off, or a check is running).</summary>
        private DateTime _nextAutoCheckUtc = DateTime.MaxValue;
        private int _autoCheckFailures;

        /// <summary>The version the prompt was shown for this session: "Later" means later, not in an hour.</summary>
        private string _promptedVersionThisSession;

        /// <summary>The version a notification already announced this session.</summary>
        private string _announcedVersionThisSession;

        /// <summary>An automatic find held back until showing it would not interrupt anything.</summary>
        private UpdateChecker.UpdateResult _pendingUpdate;
        private bool _presentingUpdate;

        /// <summary>A download or install is under way — nothing else update-related may start.</summary>
        private bool _updateInProgress;

        private Button _checkUpdatesBtn;
        private ToolStripMenuItem _trayUpdateItem;

        /// <summary>Where the sidebar last drew its "v… available" note, for clicks on it.</summary>
        private Rectangle _sidebarUpdateNoteRect;

        /// <summary>What the update before this launch did, reported once the window is up.</summary>
        private UpdateHandoff.Report _updateHandoffReport;

        private readonly DateTime _updateSessionStartedUtc = DateTime.UtcNow;

        /// <summary>How long a result may be reused to reopen the prompt before GitHub is asked again.</summary>
        private static readonly TimeSpan UpdateResultReuse = TimeSpan.FromHours(6);

        /// <summary>Waits after a failed automatic check: the network is usually back within minutes.</summary>
        private static readonly int[] AutoCheckRetryMinutes = { 5, 15, 60, 180 };

        // ── Scheduling ────────────────────────────────────────────────────────

        /// <summary>
        /// The hours between automatic checks. "Every launch" means every launch — and, since
        /// Tempo can stay open for days, twice a day while it does.
        /// </summary>
        private double AutoCheckIntervalHours(bool atLaunch)
        {
            switch (_settings?.UpdateCheckFrequency ?? 1)
            {
                case 0: return atLaunch ? 0 : 12;   // every launch
                case 2: return 24 * 7;               // weekly
                default: return 20;                  // daily (also the legacy behaviour)
            }
        }

        /// <summary>
        /// Arms the first automatic check. Called once while the window is being built; the check
        /// itself starts from the UI tick, so it never runs before there is a window to answer to.
        /// </summary>
        private void ScheduleUpdateChecks()
        {
            if (_settings == null || !_settings.CheckForUpdatesOnLaunch)
            {
                _nextAutoCheckUtc = DateTime.MaxValue;
                return;
            }

            // When Windows launched us at sign-in, hold the network check back by the user's
            // startup delay so it doesn't compete with everything else booting.
            int startupDelayMs = (StartupManager.LaunchedAtStartup() && _settings.StartupDelaySeconds > 0)
                ? _settings.StartupDelaySeconds * 1000 : 0;
            // Small delay so the window settles before any dialog could appear.
            DateTime soonest = DateTime.UtcNow.AddMilliseconds(2500 + startupDelayMs);

            _nextAutoCheckUtc = NextAutomaticCheck(atLaunch: true);
            if (_nextAutoCheckUtc < soonest) { _nextAutoCheckUtc = soonest; }
            if (_nextAutoCheckUtc > soonest)
            {
                Logger.Info("[Update] last checked " + FormatAgo((DateTime.UtcNow - _settings.LastUpdateCheckUtc.Value).TotalSeconds)
                            + " ago; the next automatic check is due " + _nextAutoCheckUtc.ToLocalTime().ToString("d MMM HH:mm") + ".");
            }
        }

        /// <summary>When the next automatic check is due, from the last one that reached GitHub.</summary>
        private DateTime NextAutomaticCheck(bool atLaunch)
        {
            double hours = AutoCheckIntervalHours(atLaunch);
            DateTime? last = _settings?.LastUpdateCheckUtc;
            if (hours <= 0 || last == null) { return DateTime.UtcNow; }
            DateTime due = last.Value.AddHours(hours);
            // A clock that has jumped backwards must not park the check for weeks.
            if (due > DateTime.UtcNow.AddHours(hours)) { due = DateTime.UtcNow; }
            return due;
        }

        /// <summary>From the 200 ms UI tick: runs the automatic check when it is due.</summary>
        private void MaybeRunScheduledUpdateCheck()
        {
            if (_settings == null || !_settings.CheckForUpdatesOnLaunch || _shuttingDown || _updateInProgress)
            {
                return;
            }
            if (_updateCheckTask != null) { return; }   // it re-arms the schedule when it ends

            // Switched on after launch (the launch schedule found it off): arm it now.
            if (_nextAutoCheckUtc == DateTime.MaxValue)
            {
                _nextAutoCheckUtc = NextAutomaticCheck(atLaunch: false);
                return;
            }
            if (DateTime.UtcNow < _nextAutoCheckUtc) { return; }

            _nextAutoCheckUtc = DateTime.MaxValue;
            RunUpdateCheck("automatic", OnAutomaticCheckFinished, userAsked: false);
        }

        private void OnAutomaticCheckFinished(UpdateChecker.UpdateResult result)
        {
            if (_shuttingDown) { return; }
            if (result == null || !result.Success)
            {
                // Quiet, as before — but no longer the end of it. The launch check at sign-in is
                // the one most likely to fail (the network is still coming up), and it used to be
                // the only check until Tempo was next restarted.
                int minutes = AutoCheckRetryMinutes[Math.Min(_autoCheckFailures, AutoCheckRetryMinutes.Length - 1)];
                _autoCheckFailures++;
                _nextAutoCheckUtc = DateTime.UtcNow.AddMinutes(minutes);
                // Refused for GitHub's rate limit: not before it resets. The 5-minute retry used to fire
                // straight into the same refusal — and spend one more of the address's 60 an hour.
                DateTime? retryAt = result?.RetryAtUtc;
                if (retryAt.HasValue && retryAt.Value.AddSeconds(30) > _nextAutoCheckUtc)
                {
                    _nextAutoCheckUtc = retryAt.Value.AddSeconds(30);
                    Logger.Info("[Update] the automatic check was refused for GitHub's rate limit; trying again after "
                                + _nextAutoCheckUtc.ToLocalTime().ToString("HH:mm") + ".");
                    return;
                }
                Logger.Info("[Update] the automatic check didn't reach GitHub; trying again in " + minutes + " min.");
                return;
            }

            // A person asked for this same check: their answer shows the prompt, not this one.
            if (result.UpdateAvailable && !IsSkipped(result) && !_updateCheckAnsweredUser)
            {
                string latest = UpdateChecker.Display(result.LatestVersion);
                if (UpdateChecker.IsSameVersion(_promptedVersionThisSession, latest))
                {
                    return;   // already offered this session; the sidebar and tray still say so
                }
                // NOT necessarily right now — see PresentOrDeferUpdate.
                PresentOrDeferUpdate(result);
            }
        }

        // ── The check ─────────────────────────────────────────────────────────

        /// <summary>
        /// Starts a check, or joins the one already running. Every trigger comes through here — the
        /// schedule, the Settings button, the tray, the sidebar note — so there is never more than
        /// one, and <paramref name="then"/> runs on the UI thread with its answer (null if it failed
        /// to finish at all).
        /// </summary>
        private void RunUpdateCheck(string why, Action<UpdateChecker.UpdateResult> then, bool userAsked)
        {
            if (_updateCheckWaiters == null) { _updateCheckWaiters = new List<Action<UpdateChecker.UpdateResult>>(); }
            if (then != null) { _updateCheckWaiters.Add(then); }
            if (userAsked) { _updateCheckUserAsked = true; }

            if (_updateCheckTask != null)
            {
                Logger.Info("[Update] a check is already running (" + why + " joins it).");
                return;
            }

            Logger.Info("[Update] checking for updates (" + why + ").");
            Task<UpdateChecker.UpdateResult> task = Task.Run(() => UpdateChecker.Check());
            _updateCheckTask = task;
            RefreshUpdateUi();

            task.ContinueWith(t =>
            {
                UpdateChecker.UpdateResult r = t.Status == TaskStatus.RanToCompletion ? t.Result : null;
                UiInvoke(() => FinishUpdateCheck(task, r, timedOut: false));
            }, TaskScheduler.Default);

            // Safety net: nothing waits on a check for ever. Sized from the check's own worst case —
            // the old 22 s net was shorter than the check it guarded, so a slow but healthy answer
            // was reported as "took too long" and then thrown away.
            var net = new Timer { Interval = (int)UpdateChecker.WorstCaseCheckTime.TotalMilliseconds + 5000 };
            net.Tick += (s, e) =>
            {
                net.Stop();
                net.Dispose();
                if (ReferenceEquals(_updateCheckTask, task)) { FinishUpdateCheck(task, null, timedOut: true); }
            };
            net.Start();
        }

        private void FinishUpdateCheck(Task<UpdateChecker.UpdateResult> task, UpdateChecker.UpdateResult result, bool timedOut)
        {
            if (!ReferenceEquals(_updateCheckTask, task)) { return; }   // already answered (timed out, then finished)
            _updateCheckTask = null;

            if (timedOut)
            {
                Logger.Warn("[Update] the check did not finish in " + (int)UpdateChecker.WorstCaseCheckTime.TotalSeconds + " s; stopped waiting for it.");
                result = new UpdateChecker.UpdateResult
                {
                    Error = Localization.T("The update check took too long and was cancelled. Please check your connection and try again.")
                };
            }

            RecordUpdateResult(result);

            var waiters = _updateCheckWaiters ?? new List<Action<UpdateChecker.UpdateResult>>();
            _updateCheckWaiters = new List<Action<UpdateChecker.UpdateResult>>();
            bool userAsked = _updateCheckUserAsked;
            _updateCheckUserAsked = false;
            RefreshUpdateUi();

            _updateCheckAnsweredUser = userAsked;
            try
            {
                foreach (var waiter in waiters)
                {
                    try { waiter(result); }
                    catch (Exception ex) { Logger.Swallow("UpdateCheckWaiter", ex); }
                }
            }
            finally { _updateCheckAnsweredUser = false; }
        }

        /// <summary>
        /// Stores what a check learned, for every surface that shows it. One place for all
        /// triggers: the manual check never cleared the "last check failed" flag after a failed
        /// launch check, so Live debug kept calling a fresh answer a stale cache.
        /// </summary>
        private void RecordUpdateResult(UpdateChecker.UpdateResult result)
        {
            if (_settings == null) { return; }
            try
            {
                if (result == null || !result.Success)
                {
                    // The cached version stays, but is now marked as not what was just confirmed.
                    _settings.LastUpdateCheckFailed = true;
                }
                else
                {
                    _lastUpdateResult = result;
                    _lastUpdateResultUtc = DateTime.UtcNow;
                    _settings.LastUpdateCheckUtc = DateTime.UtcNow;
                    _settings.LastUpdateCheckFailed = false;   // this value is now trustworthy
                    _settings.LastKnownLatestVersion = UpdateChecker.Display(result.LatestVersion);
                    _settings.LastCheckFoundUpdate = result.UpdateAvailable;

                    // Any successful check resets the automatic schedule from now.
                    _autoCheckFailures = 0;
                    if (_settings.CheckForUpdatesOnLaunch)
                    {
                        _nextAutoCheckUtc = DateTime.UtcNow.AddHours(AutoCheckIntervalHours(atLaunch: false));
                    }

                    if (!result.UpdateAvailable)
                    {
                        // Nothing newer exists, so nothing half-downloaded is worth keeping.
                        Task.Run(() => UpdateInstaller.CleanupStaleDownloads());
                    }
                }
                SettingsManager.Save(_settings);
            }
            catch (Exception ex) { Logger.Swallow("RecordUpdateResult", ex); }
        }

        /// <summary>True when the version the check found is the one the user chose to skip.</summary>
        private bool IsSkipped(UpdateChecker.UpdateResult result)
        {
            return result != null && _settings != null
                   && !string.IsNullOrWhiteSpace(_settings.SkippedUpdateVersion)
                   && UpdateChecker.IsSameVersion(_settings.SkippedUpdateVersion, UpdateChecker.Display(result.LatestVersion));
        }

        /// <summary>The newer version the last check found, if it is still newer than this build.</summary>
        private bool KnownUpdateVersion(out string latest)
        {
            latest = null;
            if (_settings == null || !_settings.LastCheckFoundUpdate) { return false; }
            if (!UpdateChecker.IsNewerThanCurrent(_settings.LastKnownLatestVersion)) { return false; }
            latest = UpdateChecker.Display(_settings.LastKnownLatestVersion);
            return true;
        }

        /// <summary>
        /// Clears state saved BEFORE the user updated: an "update available" flag naming the version
        /// now running, and a skipped version that is no longer ahead of this build.
        ///
        /// The flag and the version it names are both persisted, and only a check that actually
        /// reaches GitHub rewrites them — which is throttled (daily by default), can be switched off
        /// entirely, and leaves them alone when it fails. So after updating, Tempo went on
        /// advertising the very version it was now running until some later check happened to
        /// succeed. The skipped version had no repair at all: Settings said "Skipping version
        /// 1.0.320" to someone on 1.0.322, for good.
        /// </summary>
        private void ClearStaleUpdateFlag()
        {
            try
            {
                if (_settings == null) { return; }
                bool changed = false;

                if (_settings.LastCheckFoundUpdate && !UpdateChecker.IsNewerThanCurrent(_settings.LastKnownLatestVersion))
                {
                    _settings.LastCheckFoundUpdate = false;
                    changed = true;
                    Logger.Info("[Update] cleared a stale 'update available' flag — v"
                                + (_settings.LastKnownLatestVersion ?? "(none)")
                                + " is not newer than " + UpdateChecker.CurrentVersion + ".");
                }

                if (!string.IsNullOrWhiteSpace(_settings.SkippedUpdateVersion)
                    && !UpdateChecker.IsNewerThanCurrent(_settings.SkippedUpdateVersion))
                {
                    Logger.Info("[Update] forgot the skipped version " + _settings.SkippedUpdateVersion
                                + " — this build is already that version or newer.");
                    _settings.SkippedUpdateVersion = "";
                    changed = true;
                }

                if (changed)
                {
                    try { SettingsManager.Save(_settings); } catch { }
                }
            }
            catch (Exception ex) { Logger.Swallow("ClearStaleUpdateFlag", ex); }
        }

        // ── Offering an update ────────────────────────────────────────────────

        /// <summary>
        /// True when a modal dialog would land on top of something the user is in the
        /// middle of — or on top of nothing at all, because Tempo is in the tray.
        /// </summary>
        private bool BadMomentForUpdatePrompt()
        {
            if (_updateInProgress || _shuttingDown) { return true; }
            if (!Visible || WindowState == FormWindowState.Minimized) { return true; }
            if (_engine != null && _engine.IsRunning) { return true; }
            if (_recorder != null && _recorder.IsRecording) { return true; }
            if (_player != null && _player.IsPlaying) { return true; }

            // Never open ON TOP of another dialog.
            //
            // This deferral is released from the 200 ms UI tick, and a modal dialog
            // keeps pumping messages — so the tick goes on firing while one is open. On
            // a first launch that had an update waiting, the welcome notice appeared and
            // the update prompt then opened straight over it: two stacked modals before
            // the user had touched anything. The original conditions all looked fine
            // (window visible, nothing running), because none of them knew a dialog was
            // already up.
            try
            {
                foreach (Form f in Application.OpenForms)
                {
                    if (f != this && f.Visible && f.Modal) { return true; }
                }
            }
            catch { /* collection changed under us — just try again next tick */ }

            // Only while Tempo is the window being used. The automatic check also runs while Tempo
            // sits open behind other work now, and a dialog should not rise over someone's game or
            // document from a window they are not looking at — it waits until they come back.
            if (Form.ActiveForm != this) { return true; }

            // A fullscreen app owning the screen is the same "don't interrupt" case the
            // notification cards already respect.
            if (GamePresence.ShouldHoldNotifications(out _)) { return true; }

            return false;
        }

        /// <summary>
        /// Shows the update prompt, or holds it until showing it is not an interruption.
        ///
        /// The automatic check used to open a MODAL dialog unconditionally. Two ways that goes
        /// wrong, and both are the normal case rather than the edge case:
        ///
        ///  · Tempo starts in the tray for anyone with "Start minimised to tray" on, and
        ///    for EVERY launch-at-sign-in. A modal dialog then appears over whatever the
        ///    machine is doing at sign-in, from an app the user deliberately keeps out of
        ///    the way — with no window on screen to explain where it came from.
        ///
        ///  · Tempo is an auto-clicker. If a click run or a macro is going, a dialog
        ///    steals focus mid-automation: the run's synthetic clicks land on the dialog
        ///    instead of the target, which is precisely the failure the whole app exists
        ///    to avoid. Someone clicking in a game gets pulled out of it.
        ///
        /// While it is held, the sidebar, the Settings line and the tray menu all say an update is
        /// waiting, and a notification says so once — except in the first minutes after a sign-in
        /// launch, which is exactly the moment the note above is about.
        ///
        /// A user who presses "Check for updates" themselves still gets an immediate
        /// answer — that path calls PresentUpdateResult directly, because they asked.
        /// </summary>
        private void PresentOrDeferUpdate(UpdateChecker.UpdateResult result)
        {
            if (result == null || _shuttingDown) { return; }
            if (BadMomentForUpdatePrompt())
            {
                _pendingUpdate = result;
                string latest = UpdateChecker.Display(result.LatestVersion);
                Logger.Info("[Update] " + latest + " is available — holding the prompt until Tempo is on screen and idle.");

                bool signInQuiet = _launchedAtStartup && DateTime.UtcNow - _updateSessionStartedUtc < TimeSpan.FromMinutes(10);
                if (!signInQuiet && !UpdateChecker.IsSameVersion(_announcedVersionThisSession, latest))
                {
                    _announcedVersionThisSession = latest;
                    NotifyUpdate(Localization.T("Update available"),
                                 Localization.F("Tempo {0} is ready to install. Click to see what's new.", latest),
                                 ToastKind.Info,
                                 () => OpenKnownUpdate("notification"));
                }
                return;
            }
            // Through ShowUpdatePrompt, so the tray item or a notification clicked while this
            // dialog is open cannot stack a second one on top of it.
            ShowUpdatePrompt(result);
        }

        /// <summary>
        /// Called from the periodic UI tick: releases a held update prompt once the
        /// window is up and nothing is running.
        /// </summary>
        private void MaybeShowDeferredUpdate()
        {
            if (_pendingUpdate == null || _presentingUpdate) { return; }
            if (BadMomentForUpdatePrompt()) { return; }

            var result = _pendingUpdate;
            _pendingUpdate = null;
            _presentingUpdate = true;      // ShowDialog pumps messages — don't re-enter
            try
            {
                Logger.Info("[Update] showing the held prompt now.");
                PresentUpdateResult(result);
            }
            catch (Exception ex) { Logger.Swallow("DeferredUpdate", ex); }
            finally { _presentingUpdate = false; }
        }

        /// <summary>
        /// "Show me that update" — from the sidebar note, the Settings line, the tray item or a
        /// notification. Reuses this session's answer when it is recent; otherwise (the update is
        /// only known from an earlier session, without its notes or download) asks GitHub again.
        /// </summary>
        private void OpenKnownUpdate(string why)
        {
            if (_updateInProgress || _presentingUpdate) { return; }
            var cached = _lastUpdateResult;
            if (cached != null && cached.Success && cached.UpdateAvailable
                && DateTime.UtcNow - _lastUpdateResultUtc < UpdateResultReuse
                && UpdateChecker.IsNewerThanCurrent(UpdateChecker.Display(cached.LatestVersion)))
            {
                ShowUpdatePrompt(cached);
                return;
            }
            RunUpdateCheck(why, r => PresentRequestedCheck(r, quietly: !IsWindowUp()), userAsked: true);
        }

        private bool IsWindowUp()
        {
            return Visible && WindowState != FormWindowState.Minimized;
        }

        /// <summary>The Settings button and the tray item: a check the user asked for, answered either way.</summary>
        private void OnCheckForUpdatesClicked(object sender, EventArgs e)
        {
            if (_updateInProgress) { return; }
            // From the tray with the window hidden, "you're up to date" is a notification rather
            // than a message box: a modal answer to a menu click, with nothing of Tempo on screen,
            // came up wherever Windows put it — the corner of the screen, owned by a hidden window.
            bool fromTray = sender is ToolStripItem;
            RunUpdateCheck(fromTray ? "tray" : "requested", r => PresentRequestedCheck(r, quietly: fromTray && !IsWindowUp()),
                           userAsked: true);
        }

        /// <summary>The tray item: the update itself when one is known, otherwise a check.</summary>
        private void OnTrayUpdateClicked(object sender, EventArgs e)
        {
            if (KnownUpdateVersion(out _)) { OpenKnownUpdate("tray"); }
            else { OnCheckForUpdatesClicked(sender, e); }
        }

        /// <summary>Answers a check the user asked for: the prompt, "up to date", or why it failed.</summary>
        private void PresentRequestedCheck(UpdateChecker.UpdateResult result, bool quietly)
        {
            if (_shuttingDown) { return; }
            if (result == null || !result.Success)
            {
                string why = result?.Error ?? Localization.T("The update check failed.");
                if (quietly) { NotifyUpdate(Localization.T("The update check failed."), why, ToastKind.Warning, null); }
                else { ShowWarning(why); }
                return;
            }

            if (!result.UpdateAvailable)
            {
                string current = UpdateChecker.Display(UpdateChecker.CurrentVersion);
                if (quietly)
                {
                    NotifyUpdate(Localization.T("You're up to date"),
                                 Localization.F("Tempo {0} is the latest version.", current), ToastKind.Success, null);
                }
                else
                {
                    ShowInfo(Localization.F("You're up to date.\n\nTempo {0} is the latest version.", current));
                }
                return;
            }

            ShowUpdatePrompt(result);
        }

        private void ShowUpdatePrompt(UpdateChecker.UpdateResult result)
        {
            if (_presentingUpdate) { return; }
            _presentingUpdate = true;
            try { PresentUpdateResult(result); }
            finally { _presentingUpdate = false; }
        }

        /// <summary>The update dialog, and whatever the user chose in it.</summary>
        private void PresentUpdateResult(UpdateChecker.UpdateResult result)
        {
            if (result == null || !result.Success || !result.UpdateAvailable || _updateInProgress) { return; }

            string latest = UpdateChecker.Display(result.LatestVersion);
            _promptedVersionThisSession = latest;
            if (_pendingUpdate != null && UpdateChecker.IsSameVersion(UpdateChecker.Display(_pendingUpdate.LatestVersion), latest))
            {
                _pendingUpdate = null;   // answered now — the held copy must not come back after "Later"
            }

            string blocked = OneClickBlockedReason(result);
            var details = new UpdatePromptForm.Details
            {
                CurrentVersion = UpdateChecker.Display(UpdateChecker.CurrentVersion),
                LatestVersion = latest,
                Notes = result.Notes,
                ReleaseDate = result.ReleaseDate,
                DownloadSize = result.DownloadSize,
                CanOneClick = blocked == null,
                OneClickBlockedReason = blocked,
                BusyWarning = DescribeUpdateInterruption()
            };

            UpdatePromptForm.UpdateChoice choice;
            using (var dlg = new UpdatePromptForm(_theme, details))
            {
                choice = ShowUpdateDialog(dlg) == DialogResult.OK ? dlg.Choice : UpdatePromptForm.UpdateChoice.Later;
            }
            Logger.Info("[Update] prompt for " + latest + ": " + choice + ".");

            switch (choice)
            {
                case UpdatePromptForm.UpdateChoice.UpdateNow:
                    RunSelfUpdate(result);
                    break;

                case UpdatePromptForm.UpdateChoice.OpenPage:
                    OpenDownloadPage(result.ReleasePageUrl);
                    break;

                case UpdatePromptForm.UpdateChoice.Skip:
                    _settings.SkippedUpdateVersion = latest;
                    try { SettingsManager.Save(_settings); } catch { }
                    RefreshUpdateUi();
                    break;

                // Later: nothing — the sidebar, Settings and the tray keep saying it is there.
            }
        }

        /// <summary>
        /// Shows an update dialog over the window when it is on screen, and centred on the screen
        /// when Tempo is in the tray — a hidden, minimised owner put it in the top-left corner.
        /// </summary>
        private DialogResult ShowUpdateDialog(Form dlg)
        {
            if (IsWindowUp())
            {
                dlg.ShowInTaskbar = false;
                return dlg.ShowDialog(this);
            }
            dlg.StartPosition = FormStartPosition.CenterScreen;
            dlg.ShowInTaskbar = true;    // the only sign of Tempo on the taskbar while it waits
            return dlg.ShowDialog();
        }

        /// <summary>Why this copy cannot replace itself, or null when it can.</summary>
        private static string OneClickBlockedReason(UpdateChecker.UpdateResult result)
        {
            string url = result?.DownloadUrl;
            if (string.IsNullOrWhiteSpace(url) ||
                !(url.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
            {
                return Localization.T("This release has no file Tempo can install by itself, so it has to be downloaded from GitHub.");
            }
            // Running out of a zip: Explorer's scratch copy in TEMP, which Windows deletes. Updating
            // it would update nothing anyone opens again.
            if (DeploymentInfo.Kind == DeploymentKind.Transient)
            {
                return Localization.T("This copy of Tempo is running from inside a zip file, so it can't update itself. Download the new version from GitHub instead.");
            }
            if (!UpdateInstaller.IsExeDirWritable())
            {
                return Localization.T("Tempo can't update itself here because its folder can't be written to. Download the new version from GitHub instead.");
            }
            return null;
        }

        /// <summary>What updating now would interrupt, for the dialog to say before it happens.</summary>
        private string DescribeUpdateInterruption()
        {
            try
            {
                if (_engine != null && _engine.IsRunning)
                {
                    return Localization.T("Clicking is running — updating will stop it.");
                }
                if (_player != null && _player.IsPlaying)
                {
                    return Localization.T("A macro is playing — updating will stop it.");
                }
                if (_recorder != null && _recorder.IsRecording)
                {
                    return Localization.T("A macro is being recorded — updating will discard it.");
                }
            }
            catch { }
            return null;
        }

        // ── Installing ────────────────────────────────────────────────────────

        /// <summary>
        /// Downloads the new build, puts a verified copy beside Tempo.exe, then closes Tempo the
        /// ordinary way for the helper that swaps it in and starts Tempo again.
        /// </summary>
        private void RunSelfUpdate(UpdateChecker.UpdateResult result)
        {
            if (_updateInProgress || result == null) { return; }

            // A macro being RECORDED only exists in memory until it is stopped and saved, and
            // updating restarts Tempo. Asked BEFORE the download now, not after it.
            if (!ConfirmDiscardRecordingForUpdate()) { return; }

            // "Update now" stops what is running (the dialog said it would). Left going, the
            // clicker kept clicking wherever the cursor was — on the download window's Cancel
            // button, as likely as anywhere.
            try
            {
                if (_engine != null && (_engine.IsRunning || _engine.IsPaused)) { _engine.Stop(); }
                if (_player != null && _player.IsPlaying) { _player.Stop(); }
            }
            catch (Exception ex) { Logger.Swallow("UpdateStopRuns", ex); }

            _updateInProgress = true;
            bool handedOver = false;
            try
            {
                bool isZip = result.DownloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
                string dest = UpdateInstaller.GetDownloadTargetPath(result.LatestVersion, isZip, result.DownloadSha256);

                DialogResult dr;
                string staged;
                string error;
                using (var dlg = new UpdateDownloadForm(_theme, result.DownloadUrl, dest, result.LatestVersion,
                                                        result.Sha256Url, result.DownloadSha256))
                {
                    dr = ShowUpdateDialog(dlg);
                    staged = dlg.StagedPath;
                    error = dlg.Error;
                }

                if (dr == DialogResult.Cancel)
                {
                    Logger.Info("[Update] download cancelled by the user.");
                    return;
                }

                if (dr != DialogResult.OK || string.IsNullOrEmpty(staged))
                {
                    Logger.Warn("[Update] the update did not download: " + (error ?? "(no reason given)"));
                    DialogResult fallback = MessageBox.Show(this,
                        Localization.T(error ?? "The download failed.") + "\n\n" + Localization.T("Open the download page instead?"),
                        "Tempo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (fallback == DialogResult.Yes)
                    {
                        OpenDownloadPage(result.ReleasePageUrl);
                    }
                    return;
                }

                // Hotkeys work while the download window is up, so a recording can have started in
                // the meantime. Same question, same answer.
                if (!ConfirmDiscardRecordingForUpdate())
                {
                    UpdateInstaller.DiscardStagedUpdate(staged);
                    return;
                }

                UpdateHandoff.Record(UpdateChecker.CurrentVersion, result.LatestVersion, result.ReleasePageUrl);
                if (!UpdateInstaller.LaunchSwapAndExitHelper(staged, out string err))
                {
                    UpdateHandoff.Forget();
                    UpdateInstaller.DiscardStagedUpdate(staged);
                    ShowWarning(err ?? Localization.T("Couldn't start the updater."));
                    return;
                }

                handedOver = true;
                Logger.Info("[Update] installing " + UpdateChecker.Display(result.LatestVersion)
                            + " — closing Tempo so the updater can swap Tempo.exe.");
                CloseForHelper("the updater can replace Tempo.exe");
            }
            finally
            {
                if (!handedOver) { _updateInProgress = false; }
            }
        }

        /// <summary>
        /// Asks before an update throws away a macro recording in progress. True = go ahead
        /// (nothing was recording, or the user chose to discard it).
        /// </summary>
        private bool ConfirmDiscardRecordingForUpdate()
        {
            try
            {
                if (_recorder == null || !_recorder.IsRecording) { return true; }
                DialogResult keep = MessageBox.Show(this,
                    Localization.T("A macro is still being recorded.\n\n" +
                    "Updating restarts Tempo, which discards the recording in progress.\n\n" +
                    "Yes — discard it and update now\n" +
                    "No — cancel the update so you can finish and save it"),
                    "Tempo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (keep != DialogResult.Yes)
                {
                    Logger.Info("[Update] cancelled — a macro recording was in progress.");
                    return false;
                }
                try { _recorder.Stop(); } catch { }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Swallow("ConfirmDiscardRecordingForUpdate", ex);
                return false;
            }
        }

        /// <summary>
        /// Closes Tempo the ORDINARY way — so a run in progress is recorded, captions are saved and
        /// put back, held keys and buttons are released, and profiles, macros and settings are
        /// written — for a helper that is waiting for this process to end before it touches
        /// Tempo.exe or its unpacked copy.
        ///
        /// Both helpers used to be preceded by Environment.Exit, which runs none of that. The
        /// helper waits for the process, not for a time, so a proper close costs nothing.
        /// </summary>
        private void CloseForHelper(string why)
        {
            try
            {
                // Captions come back on the other side, exactly as after a restart.
                if (_settings != null) { _settings.CaptionResumeAfterRestart = _captionsActive; }
            }
            catch { }

            _reallyClosing = true;
            // The update dialog already said what this interrupts, so the exit confirmation —
            // which exists for a quit the user may not have meant — does not ask again.
            _restartingApp = true;
            AutoClicker.Program.ExitProcessAfterShutdown = true;
            Logger.Info("[shutdown] closing so " + why + ".");

            // Belt and braces: if something keeps the close from completing, the helper would wait
            // on a Tempo that never ends. The normal close takes well under a second.
            var fallback = new System.Threading.Timer(_ =>
            {
                Logger.Warn("[shutdown] the close for the helper did not finish in 15 s — ending the process.");
                Environment.Exit(0);
            }, null, 15000, System.Threading.Timeout.Infinite);
            GC.KeepAlive(fallback);
            _closeForHelperFallback = fallback;

            Application.Exit();
        }

        /// <summary>Held so the fallback timer is not collected before it can fire.</summary>
        private System.Threading.Timer _closeForHelperFallback;

        private void OpenDownloadPage(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                url = UpdateChecker.ReleasesPageUrl;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ShowWarning(Localization.F("Couldn't open the download page: {0}", ex.Message));
            }
        }

        // ── After the restart ─────────────────────────────────────────────────

        /// <summary>Reads what the update before this launch did. Runs while the window is built.</summary>
        private void CollectUpdateHandoff()
        {
            try
            {
                _updateHandoffReport = UpdateHandoff.Collect(UpdateChecker.CurrentVersion, Environment.ProcessPath);
                if (_updateHandoffReport == null || _updateHandoffReport.Outcome == UpdateHandoff.Outcome.None) { return; }

                // Said once the window (or the tray) is up, not in the middle of building it.
                var t = new Timer { Interval = 2500 };
                t.Tick += (s, e) =>
                {
                    t.Stop();
                    t.Dispose();
                    ReportUpdateOutcome();
                };
                t.Start();
            }
            catch (Exception ex) { Logger.Swallow("CollectUpdateHandoff", ex); }
        }

        private void ReportUpdateOutcome()
        {
            var report = _updateHandoffReport;
            _updateHandoffReport = null;
            if (report == null || _shuttingDown) { return; }

            string current = UpdateChecker.Display(UpdateChecker.CurrentVersion);
            if (report.Outcome == UpdateHandoff.Outcome.Updated)
            {
                string page = report.ReleasePageUrl;
                NotifyUpdate(Localization.T("Tempo updated"),
                             Localization.F("You're now on Tempo {0}. Click to see what changed.", current),
                             ToastKind.Success,
                             () => OpenDownloadPage(page));
            }
            else if (report.Outcome == UpdateHandoff.Outcome.NotInstalled)
            {
                NotifyUpdate(Localization.T("The update didn't install"),
                             Localization.F("Tempo.exe couldn't be replaced, so this is still Tempo {0}. Click to try again.", current),
                             ToastKind.Warning,
                             () => OpenKnownUpdate("retry after a failed install"));
            }
        }

        /// <summary>
        /// A notification about updates. Shown whatever "Show tray notifications" says — like the
        /// crash and integrity cards, it is not routine chatter — as Tempo's card, or the plain
        /// Windows balloon when the cards are switched off.
        /// </summary>
        private void NotifyUpdate(string title, string body, ToastKind kind, Action onClick)
        {
            try
            {
                if (_settings != null && _settings.CustomNotifications && _notifications != null)
                {
                    _notifications.Notify("Tempo", title, body, kind, TempoNotifyIcon(), null,
                                          onClick ?? ShowFromTrayAndActivate);
                    return;
                }
                // The plain balloon, for anyone who switched Tempo's cards off. It carries the same
                // click: these notices all say "click to …", and a balloon that did nothing when
                // clicked made that a sentence with nothing behind it.
                _balloonClickAction = onClick ?? ShowFromTrayAndActivate;
                _trayIcon?.ShowBalloonTip(8000, title, body,
                                          kind == ToastKind.Warning ? ToolTipIcon.Warning : ToolTipIcon.Info);
                Logger.Info("[Update] notice shown as a Windows notification: " + title);
            }
            catch (Exception ex) { Logger.Swallow("NotifyUpdate", ex); }
        }

        /// <summary>
        /// What clicking the tray balloon on screen does, or null. Set only by a balloon that has an
        /// action; any other balloon clears it (TempoNotify), and it is cleared when the balloon closes
        /// — so a later, unrelated balloon can never inherit an update notice's click.
        /// </summary>
        private Action _balloonClickAction;

        private void OnTrayBalloonClicked(object sender, EventArgs e)
        {
            Action act = _balloonClickAction;
            _balloonClickAction = null;
            if (act != null) { TrayAction("Balloon click", act); }
        }

        // ── Everywhere the state shows ────────────────────────────────────────

        /// <summary>The Settings line, the Settings button, the tray item and the sidebar note, all from the same state.</summary>
        private void RefreshUpdateUi()
        {
            try
            {
                bool checking = _updateCheckTask != null;
                if (_checkUpdatesBtn != null && !_checkUpdatesBtn.IsDisposed)
                {
                    _checkUpdatesBtn.Enabled = !checking && !_updateInProgress;
                    _checkUpdatesBtn.Text = Localization.T(checking ? "Checking…" : "Check for updates");
                }
                UpdateLastCheckedLabel();
                RefreshTrayUpdateItem();
                _sidebar?.Invalidate();
            }
            catch (Exception ex) { Logger.Swallow("RefreshUpdateUi", ex); }
        }

        private void UpdateLastCheckedLabel()
        {
            if (_lastCheckedLabel == null || _lastCheckedLabel.IsDisposed)
            {
                return;
            }

            var parts = new List<string>();
            bool checking = _updateCheckTask != null;
            bool available = KnownUpdateVersion(out string latest);
            bool skippedLatest = available && _settings != null
                                 && UpdateChecker.IsSameVersion(_settings.SkippedUpdateVersion, latest);

            if (checking)
            {
                parts.Add(Localization.T("Checking for updates…"));
            }
            else if (_settings == null || _settings.LastUpdateCheckUtc == null)
            {
                parts.Add(Localization.T("Updates haven't been checked yet."));
            }
            else
            {
                parts.Add(Localization.F("Last checked for updates: {0}",
                    _settings.LastUpdateCheckUtc.Value.ToLocalTime().ToString("d MMM yyyy, HH:mm", Localization.DateCulture)));

                // Inline result of that last check, so the state is visible without opening the
                // update dialog. The saved flag alone is not enough: it survives the update that
                // resolves it, so the version itself has to be newer (see UpdateChecker.Normalize).
                if (skippedLatest)
                {
                    parts.Add(Localization.F("Skipping version {0}", latest));
                }
                else if (available)
                {
                    parts.Add(Localization.F("Update available: v{0}", latest));
                }
                else if (!_settings.LastUpdateCheckFailed)
                {
                    parts.Add(Localization.T("You're up to date."));
                }

                if (_settings.LastUpdateCheckFailed)
                {
                    parts.Add(Localization.T("The last check couldn't reach GitHub."));
                }
            }

            _lastCheckedLabel.Text = string.Join("   ·   ", parts);
            // Clickable while there is an update to open.
            _lastCheckedLabel.Cursor = available && !checking ? Cursors.Hand : Cursors.Default;
        }

        private void OnLastCheckedLabelClick(object sender, EventArgs e)
        {
            if (_updateCheckTask == null && KnownUpdateVersion(out _)) { OpenKnownUpdate("settings"); }
        }

        /// <summary>
        /// The tray item says what clicking it will do: "Update to v1.0.323…" once an update is
        /// known, "Checking for updates…" while a check runs, "Check for updates…" otherwise.
        /// </summary>
        private void RefreshTrayUpdateItem()
        {
            if (_trayUpdateItem == null) { return; }
            try
            {
                bool checking = _updateCheckTask != null;
                // A skipped version is not advertised here, the same as in the sidebar: the user
                // answered. "Check for updates…" still offers it again if they change their mind.
                bool available = KnownUpdateVersion(out string latest)
                                 && !UpdateChecker.IsSameVersion(_settings?.SkippedUpdateVersion, latest);
                var style = _trayUpdateItem.Tag as TrayItemStyle ?? new TrayItemStyle(TrayGlyph.Refresh);
                if (checking)
                {
                    _trayUpdateItem.Text = Localization.T("Checking for updates…");
                    _trayUpdateItem.Enabled = false;
                    style.Glyph = TrayGlyph.Refresh;
                }
                else if (available && !_updateInProgress)
                {
                    _trayUpdateItem.Text = MenuText(Localization.F("Update to v{0}…", latest));
                    _trayUpdateItem.Enabled = true;
                    style.Glyph = TrayGlyph.Download;
                }
                else
                {
                    _trayUpdateItem.Text = Localization.T("Check for updates…");
                    _trayUpdateItem.Enabled = !_updateInProgress;
                    style.Glyph = TrayGlyph.Refresh;
                }
                _trayUpdateItem.Tag = style;
            }
            catch (Exception ex) { Logger.Swallow("RefreshTrayUpdateItem", ex); }
        }

        private void OnSidebarMouseMoveForUpdate(object sender, MouseEventArgs e)
        {
            bool over = !_sidebarUpdateNoteRect.IsEmpty && _sidebarUpdateNoteRect.Contains(e.Location);
            Cursor want = over ? Cursors.Hand : Cursors.Default;
            if (_sidebar.Cursor != want) { _sidebar.Cursor = want; }
        }

        private void OnSidebarMouseClickForUpdate(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && !_sidebarUpdateNoteRect.IsEmpty
                && _sidebarUpdateNoteRect.Contains(e.Location))
            {
                OpenKnownUpdate("sidebar");
            }
        }

        /// <summary>Live debug's view of the schedule: what runs next, and when.</summary>
        private string DescribeUpdateScheduleForDebug()
        {
            if (_updateInProgress) { return "installing an update"; }
            if (_updateCheckTask != null) { return "checking now"; }
            if (_settings == null || !_settings.CheckForUpdatesOnLaunch) { return "automatic checks off"; }
            if (_nextAutoCheckUtc == DateTime.MaxValue) { return "next automatic check not scheduled yet"; }
            double secs = (_nextAutoCheckUtc - DateTime.UtcNow).TotalSeconds;
            string when = secs <= 0 ? "due now" : "in " + FormatAgo(secs);
            return "next automatic check " + when + (_autoCheckFailures > 0 ? " (after " + _autoCheckFailures + " failed)" : "");
        }
    }
}
