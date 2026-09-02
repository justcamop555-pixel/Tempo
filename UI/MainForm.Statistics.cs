using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Persistence;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    public partial class MainForm
    {
        // Layout constants for the dashboard grid.
        private const int CardW = 172;
        private const int CardH = 78;
        private const int CardGap = 12;

        private void BuildStatisticsTab()
        {
            var page = new BackdropTabPage(Utils.Localization.T("Statistics")) { AutoScroll = true };
            page.Name = "statistics";   // stable key for LastTabKey
            _statsPage = page;

            var title = UiFactory.Label(Utils.Localization.T("Live Dashboard"), 16, 14, FontStyle.Bold, 13f);
            page.Controls.Add(title);

            // Column x positions for a 4-wide card grid.
            int c0 = 16;
            int c1 = c0 + CardW + CardGap;
            int c2 = c1 + CardW + CardGap;
            int c3 = c2 + CardW + CardGap;

            // ── Row 1: session headline cards ─────────────────────────────────
            int row1 = 46;
            _cardSessionClicks = MakeCard("Session Clicks", c0, row1, true);
            _cardTotalClicks = MakeCard("Total (Launch)", c1, row1, true);
            _cardCurrentCps = MakeCard("Current CPS", c2, row1, true);
            _cardPeakCps = MakeCard("Peak CPS", c3, row1, true);
            page.Controls.Add(_cardSessionClicks);
            page.Controls.Add(_cardTotalClicks);
            page.Controls.Add(_cardCurrentCps);
            page.Controls.Add(_cardPeakCps);

            // ── Live CPS graph ────────────────────────────────────────────────
            int graphTop = row1 + CardH + CardGap;
            _cpsSparkline = new SparklineControl
            {
                Left = c0,
                Top = graphTop,
                Width = (CardW * 4) + (CardGap * 3),
                Height = 168
            };
            page.Controls.Add(_cpsSparkline);

            // ── Session goal ──────────────────────────────────────────────────
            int fullW = (CardW * 4) + (CardGap * 3);
            int rightEdge = c0 + fullW;
            int goalY = graphTop + _cpsSparkline.Height + CardGap;

            page.Controls.Add(UiFactory.Label(Utils.Localization.T("Session goal (clicks, 0 = off):"), 16, goalY + 2, FontStyle.Bold, 9.5f));
            _sessionGoalNum = UiFactory.Numeric(220, goalY, 110, 0, 1000000000, 0);
            _suppressGoalEvent = true;
            try
            {
                long g = _settings != null ? _settings.SessionGoalClicks : 0;
                if (g < 0) g = 0;
                if (g > 1000000000) g = 1000000000;
                _sessionGoalNum.Value = g;
            }
            finally { _suppressGoalEvent = false; }
            _sessionGoalNum.ValueChanged += OnSessionGoalChanged;
            page.Controls.Add(_sessionGoalNum);

            _goalProgressBar = new ThemedProgressBar
            {
                Left = 348,
                Top = goalY + 2,
                Width = rightEdge - 348 - 172,
                Height = 18,
                Maximum = 100,
                Value = 0
            };
            page.Controls.Add(_goalProgressBar);

            _goalProgressLabel = UiFactory.Label("—", rightEdge - 166, goalY + 2, FontStyle.Bold, 9.5f);
            _goalProgressLabel.AutoSize = false;
            _goalProgressLabel.Width = 166;
            _goalProgressLabel.Height = 18;
            _goalProgressLabel.TextAlign = ContentAlignment.MiddleRight;
            page.Controls.Add(_goalProgressLabel);

            // Quick-set buttons so a goal is one tap, not a typed number — the same
            // idea as the Clicker's CPS presets. Each just drives the numeric above,
            // which already persists the goal and refreshes the bar.
            page.Controls.Add(UiFactory.Caption(Utils.Localization.T("Quick goal:"), 16, goalY + 36));
            int[] goalPresets = { 1000, 10000, 100000 };
            string[] goalPresetLabels = { "1K", "10K", "100K" };
            int gx = 92;
            for (int gi = 0; gi < goalPresets.Length; gi++)
            {
                int gp = goalPresets[gi];
                var gb = UiFactory.Button(goalPresetLabels[gi], gx, goalY + 32, 58, 26);
                gb.Click += (s, e) => { if (_sessionGoalNum != null) _sessionGoalNum.Value = gp; };
                page.Controls.Add(gb);
                gx += 64;
            }

            // ── Row 2: rate + timing ──────────────────────────────────────────
            int row2 = goalY + 64;
            _cardAvgCps = MakeCard("Average CPS", c0, row2, false);
            _cardClicksPerMin = MakeCard("Clicks / Min", c1, row2, false);
            _cardElapsed = MakeCard("Elapsed", c2, row2, false);
            _cardToday = MakeCard("Today", c3, row2, true);
            page.Controls.Add(_cardAvgCps);
            page.Controls.Add(_cardClicksPerMin);
            page.Controls.Add(_cardElapsed);
            page.Controls.Add(_cardToday);

            // ── Button breakdown ──────────────────────────────────────────────
            int byBtnLabelY = row2 + CardH + 14;
            var byBtnLabel = UiFactory.Label(Utils.Localization.T("Clicks by button"), 16, byBtnLabelY, FontStyle.Bold, 10f);
            page.Controls.Add(byBtnLabel);

            int row3 = byBtnLabelY + 26;
            _cardLeft = MakeCard("Left", c0, row3, false);
            _cardRight = MakeCard("Right", c1, row3, false);
            _cardMiddle = MakeCard("Middle", c2, row3, false);
            page.Controls.Add(_cardLeft);
            page.Controls.Add(_cardRight);
            page.Controls.Add(_cardMiddle);

            // Distribution bar visualising the split.
            int distY = row3 + CardH + 10;
            _distBar = new DistributionBar
            {
                Left = c0,
                Top = distY,
                Width = (CardW * 4) + (CardGap * 3),
                Height = 64
            };
            page.Controls.Add(_distBar);

            // ── Lifetime section ──────────────────────────────────────────────
            int lifeTitleY = distY + _distBar.Height + 14;
            var lifeTitle = UiFactory.Label(Utils.Localization.T("Lifetime"), 16, lifeTitleY, FontStyle.Bold, 12f);
            page.Controls.Add(lifeTitle);

            int row4 = lifeTitleY + 30;
            _cardLifeClicks = MakeCard("Lifetime Clicks", c0, row4, false);
            _cardLifeSessions = MakeCard("Sessions", c1, row4, false);
            _cardLifePeak = MakeCard("Best CPS Ever", c2, row4, false);
            _cardLifeRuntime = MakeCard("Total Runtime", c3, row4, false);
            page.Controls.Add(_cardLifeClicks);
            page.Controls.Add(_cardLifeSessions);
            page.Controls.Add(_cardLifePeak);
            page.Controls.Add(_cardLifeRuntime);

            // ── Records ───────────────────────────────────────────────────────
            int recTitleY = row4 + CardH + 16;
            page.Controls.Add(UiFactory.Label(Utils.Localization.T("Records"), 16, recTitleY, FontStyle.Bold, 12f));

            int row5 = recTitleY + 30;
            _cardMostClicks = MakeCard("Most Clicks / Run", c0, row5, false);
            _cardLongestRun = MakeCard("Longest Run", c1, row5, false);
            _cardAvgPerSession = MakeCard("Avg Clicks / Session", c2, row5, false);
            _cardAvgRunLength = MakeCard("Avg Run Length", c3, row5, false);
            page.Controls.Add(_cardMostClicks);
            page.Controls.Add(_cardLongestRun);
            page.Controls.Add(_cardAvgPerSession);
            page.Controls.Add(_cardAvgRunLength);

            // ── Insights (derived from session history) ───────────────────────
            int insTitleY = row5 + CardH + 16;
            page.Controls.Add(UiFactory.Label(Utils.Localization.T("Insights"), 16, insTitleY, FontStyle.Bold, 12f));

            int rowI1 = insTitleY + 30;
            _cardThisWeek = MakeCard("This Week", c0, rowI1, true);
            _cardThisMonth = MakeCard("This Month", c1, rowI1, true);
            _cardLifeAvgCps = MakeCard("Lifetime Avg CPS", c2, rowI1, false);
            _cardActiveDays = MakeCard("Active Days", c3, rowI1, false);
            page.Controls.Add(_cardThisWeek);
            page.Controls.Add(_cardThisMonth);
            page.Controls.Add(_cardLifeAvgCps);
            page.Controls.Add(_cardActiveDays);

            int rowI2 = rowI1 + CardH + CardGap;
            _cardBestDay = MakeCard("Best Day", c0, rowI2, false);
            _cardBusiestWeekday = MakeCard("Busiest Weekday", c1, rowI2, false);
            _cardBusiestHour = MakeCard("Busiest Hour", c2, rowI2, false);
            _cardTopProfile = MakeCard("Top Profile", c3, rowI2, false);
            page.Controls.Add(_cardBestDay);
            page.Controls.Add(_cardBusiestWeekday);
            page.Controls.Add(_cardBusiestHour);
            page.Controls.Add(_cardTopProfile);

            int rowI3 = rowI2 + CardH + CardGap;
            _cardStreak = MakeCard("Current Streak", c0, rowI3, true);
            _cardLongestStreak = MakeCard("Longest Streak", c1, rowI3, false);
            _cardDailyAvg = MakeCard("Daily Average", c2, rowI3, false);
            _cardThisYear = MakeCard("This Year", c3, rowI3, false);
            page.Controls.Add(_cardStreak);
            page.Controls.Add(_cardLongestStreak);
            page.Controls.Add(_cardDailyAvg);
            page.Controls.Add(_cardThisYear);

            // ── Charts: per-session (left) and last-7-days (right) ────────────
            int chartY = rowI3 + CardH + 14;
            int halfW = (CardW * 2) + CardGap;
            _sessionBarChart = new MiniBarChart
            {
                Title = Utils.Localization.T("Clicks per recent session"),
                Left = c0,
                Top = chartY,
                Width = halfW,
                Height = 132
            };
            page.Controls.Add(_sessionBarChart);

            _dailyBarChart = new MiniBarChart
            {
                Title = Utils.Localization.T("Clicks — last 7 days"),
                Left = c2,
                Top = chartY,
                Width = halfW,
                Height = 132
            };
            page.Controls.Add(_dailyBarChart);

            // ── Clicks by hour of day (full width) ────────────────────────────
            int hourChartY = chartY + _sessionBarChart.Height + 14;
            _hourChart = new MiniBarChart
            {
                Title = Utils.Localization.T("Clicks by hour of day"),
                Left = c0,
                Top = hourChartY,
                Width = (CardW * 4) + (CardGap * 3),
                Height = 132
            };
            page.Controls.Add(_hourChart);

            // ── Clicks by day of week (full width) ────────────────────────────
            int weekdayChartY = hourChartY + _hourChart.Height + 14;
            _weekdayChart = new MiniBarChart
            {
                Title = Utils.Localization.T("Clicks by day of week"),
                Left = c0,
                Top = weekdayChartY,
                Width = (CardW * 4) + (CardGap * 3),
                Height = 132
            };
            page.Controls.Add(_weekdayChart);

            // ── Recent sessions ───────────────────────────────────────────────
            int histTitleY = weekdayChartY + _weekdayChart.Height + 14;
            page.Controls.Add(UiFactory.Label(Utils.Localization.T("Recent sessions"), 16, histTitleY, FontStyle.Bold, 12f));

            // Search + filter by profile.
            page.Controls.Add(UiFactory.Caption("Find:", rightEdge - 448, histTitleY + 2));
            // Starts 40px further right (and gives that back off its own width, so the
            // profile filter beside it does not move): the label had 38px, which "Find:"
            // fits and every translation of it does not.
            _historySearchBox = UiFactory.Text(rightEdge - 370, histTitleY - 2, 130);
            _historySearchBox.TextChanged += (s, e) => { _historySearchText = _historySearchBox.Text; RefreshSessionHistory(); };
            page.Controls.Add(_historySearchBox);

            // Safe to translate: the filter keys off SelectedIndex > 0, not off this
            // string, so item 0 means "everything" whatever it says.
            _historyProfileFilter = UiFactory.Combo(rightEdge - 230, histTitleY - 2, 170,
                Utils.Localization.T("All profiles"));
            _historyProfileFilter.DropDownStyle = ComboBoxStyle.DropDownList;
            _historyProfileFilter.SelectedIndex = 0;
            _historyProfileFilter.SelectedIndexChanged += OnHistoryFilterChanged;
            page.Controls.Add(_historyProfileFilter);

            int histY = histTitleY + 28;
            _sessionHistoryList = new SessionHistoryListView
            {
                Left = c0,
                Top = histY,
                Width = (CardW * 4) + (CardGap * 3),
                Height = 180
            };
            _sessionHistoryList.Columns.Add(Utils.Localization.T("When"), 150);
            _sessionHistoryList.Columns.Add(Utils.Localization.T("Profile"), 130);
            _sessionHistoryList.Columns.Add(Utils.Localization.T("Clicks"), 90);
            _sessionHistoryList.Columns.Add(Utils.Localization.T("Duration"), 100);
            _sessionHistoryList.Columns.Add(Utils.Localization.T("Avg CPS"), 80);
            _sessionHistoryList.Columns.Add(Utils.Localization.T("Peak CPS"), 80);
            // 94px of the control was left unused: the automatic fit in
            // ThemedListView.OnSizeChanged runs when Width is set above, before any
            // column exists, so it has nothing to fit.
            _sessionHistoryList.FitLastColumn();
            _sessionHistoryList.DoubleClick += OnSessionHistoryDetails;
            _sessionHistoryList.ColumnClick += OnSessionHistoryColumnClick;

            var histMenu = new ContextMenuStrip();
            histMenu.Items.Add(Utils.Localization.T("View details"), null, (s, e) => OnSessionHistoryDetails(s, e));
            histMenu.Items.Add(Utils.Localization.T("Copy row"), null, OnCopyHistoryRow);
            histMenu.Items.Add(new ToolStripSeparator());
            histMenu.Items.Add(Utils.Localization.T("Delete entry"), null, OnDeleteHistoryEntry);
            _sessionHistoryList.ContextMenuStrip = histMenu;
            page.Controls.Add(_sessionHistoryList);

            _historySummaryLabel = UiFactory.Label("", 16, histY + _sessionHistoryList.Height + 4, FontStyle.Italic, 9f);
            _historySummaryLabel.AutoSize = false;
            _historySummaryLabel.Width = fullW;
            _historySummaryLabel.Height = 16;
            _historySummaryLabel.ForeColor = _theme.TextMuted;
            page.Controls.Add(_historySummaryLabel);

            // ── Action buttons ────────────────────────────────────────────────
            int btnY = histY + _sessionHistoryList.Height + 26;
            _resetStatsBtn = UiFactory.Button(Utils.Localization.T("Reset session"), c0, btnY, 150, 32);
            _resetStatsBtn.Click += OnResetStats;
            page.Controls.Add(_resetStatsBtn);

            _resetLifetimeBtn = UiFactory.Button(Utils.Localization.T("Reset lifetime"), c1, btnY, 150, 32);
            _resetLifetimeBtn.Click += OnResetLifetime;
            page.Controls.Add(_resetLifetimeBtn);

            var exportCsvBtn = UiFactory.Button(Utils.Localization.T("Export CSV…"), c2, btnY, 150, 32);
            exportCsvBtn.Click += OnExportStatsCsv;
            page.Controls.Add(exportCsvBtn);

            var clearHistBtn = UiFactory.Button(Utils.Localization.T("Clear history"), c3, btnY, 150, 32);
            clearHistBtn.Click += OnClearHistory;
            page.Controls.Add(clearHistBtn);

            var copySummaryBtn = UiFactory.Button("Copy summary", c0, btnY + 40, 150, 32);
            copySummaryBtn.Click += OnCopyStatsSummary;
            page.Controls.Add(copySummaryBtn);

            var exportJsonBtn = UiFactory.Button(Utils.Localization.T("Export JSON…"), c1, btnY + 40, 150, 32);
            exportJsonBtn.Click += OnExportStatsJson;
            page.Controls.Add(exportJsonBtn);

            // ── Milestones ────────────────────────────────────────────────────
            int fw = (CardW * 4) + (CardGap * 3);
            int msTitleY = btnY + 96;
            page.Controls.Add(UiFactory.Label("Milestones", 16, msTitleY, FontStyle.Bold, 12f));

            _milestoneLabel = UiFactory.Label("", 16, msTitleY + 26, FontStyle.Regular, 9.5f);
            _milestoneLabel.AutoSize = false;
            _milestoneLabel.Width = fw;
            _milestoneLabel.Height = 18;
            page.Controls.Add(_milestoneLabel);

            _milestoneBar = new ThemedProgressBar
            {
                Left = c0,
                Top = msTitleY + 48,
                Width = fw,
                Height = 18,
                Maximum = 100,
                Value = 0
            };
            _milestoneBar.ApplyTheme(_theme);
            page.Controls.Add(_milestoneBar);

            _milestoneBadges = UiFactory.Label("", 16, msTitleY + 74, FontStyle.Regular, 9.5f);
            _milestoneBadges.AutoSize = false;
            _milestoneBadges.Width = fw;
            _milestoneBadges.Height = 20;
            _milestoneBadges.ForeColor = _theme.TextMuted;
            page.Controls.Add(_milestoneBadges);

            _tabs.TabPages.Add(page);
            RefreshSessionHistory();
        }

        /// <summary>
        /// Detects when lifetime clicks cross a milestone tier (regardless of which
        /// tab is open) and shows a one-off celebratory tray notification. Runs from
        /// the main UI tick. The first call only records the current level so already
        /// reached milestones don't fire a toast on launch.
        /// </summary>
        private void CheckMilestoneCrossing()
        {
            long lifetime = _lifetimeBaseline + _statistics.TotalClicks;

            long highest = 0;
            foreach (long tier in MilestoneTiers)
            {
                if (lifetime >= tier) highest = tier;
            }

            if (_lastMilestoneClicks < 0)
            {
                _lastMilestoneClicks = highest; // initialise silently
                return;
            }

            if (highest > _lastMilestoneClicks)
            {
                _lastMilestoneClicks = highest;
                ShowMilestoneToast(highest);
            }
        }

        private void ShowMilestoneToast(long tier)
        {
            try
            {
                if (_trayIcon != null && _settings != null && _settings.ShowTrayNotifications)
                {
                    TempoNotify(
                        3000,
                        "Tempo milestone reached!",
                        "You've passed " + FormatMilestone(tier) + " lifetime clicks. Nice work!",
                        ToolTipIcon.Info);
                }
            }
            catch { /* notifications are best-effort */ }
        }

        // Lifetime click milestones (gamified achievements).
        private static readonly long[] MilestoneTiers =
            { 1000, 5000, 10000, 50000, 100000, 250000, 500000, 1000000, 5000000, 10000000 };

        private static string FormatMilestone(long n)
        {
            if (n >= 1000000) return (n / 1000000.0).ToString("0.#") + "M";
            if (n >= 1000) return (n / 1000.0).ToString("0.#") + "K";
            return n.ToString("N0");
        }

        /// <summary>Updates the Milestones progress bar and badges from lifetime clicks.</summary>
        private void UpdateMilestones(long lifetimeClicks)
        {
            if (_milestoneBar == null || _milestoneLabel == null || _milestoneBadges == null)
            {
                return;
            }

            // Find the next tier not yet reached, and the one just below it.
            long next = 0;
            long prev = 0;
            foreach (long tier in MilestoneTiers)
            {
                if (lifetimeClicks < tier) { next = tier; break; }
                prev = tier;
            }

            if (next == 0)
            {
                // Every milestone reached.
                _milestoneBar.Value = 100;
                _milestoneLabel.Text = Utils.Localization.F(
                    "All milestones reached — {0:N0} lifetime clicks. Incredible!", lifetimeClicks);
            }
            else
            {
                long span = next - prev;
                int pct = span > 0
                    ? (int)Math.Round((lifetimeClicks - prev) / (double)span * 100.0)
                    : 0;
                if (pct < 0) pct = 0;
                if (pct > 100) pct = 100;
                _milestoneBar.Value = pct;

                long remaining = next - lifetimeClicks;
                _milestoneLabel.Text = Utils.Localization.F(
                    "{0:N0} / {1:N0} lifetime clicks  —  {2:N0} to go until the {3} milestone ({4}%)",
                    lifetimeClicks, next, remaining, FormatMilestone(next), pct);
            }

            // Badge row: filled for reached, hollow for locked.
            var sb = new System.Text.StringBuilder();
            foreach (long tier in MilestoneTiers)
            {
                bool reached = lifetimeClicks >= tier;
                sb.Append(reached ? "\u2605 " : "\u2606 ");
                sb.Append(FormatMilestone(tier));
                sb.Append("    ");
            }
            _milestoneBadges.Text = sb.ToString().TrimEnd();
        }

        private StatCard MakeCard(string caption, int x, int y, bool accent)
        {
            return new StatCard
            {
                Caption = Utils.Localization.T(caption),
                Value = "0",
                Left = x,
                Top = y,
                Width = CardW,
                Height = CardH,
                ShowAccent = accent
            };
        }

        private void ApplyThemeToStatCards()
        {
            if (_cpsSparkline == null)
            {
                return;
            }

            foreach (var card in new[]
            {
                _cardSessionClicks, _cardTotalClicks, _cardCurrentCps, _cardPeakCps,
                _cardAvgCps, _cardClicksPerMin, _cardElapsed,
                _cardLeft, _cardRight, _cardMiddle,
                _cardLifeClicks, _cardLifeSessions, _cardLifePeak, _cardLifeRuntime,
                _cardMostClicks, _cardLongestRun, _cardToday,
                _cardAvgPerSession, _cardAvgRunLength,
                _cardThisWeek, _cardThisMonth, _cardLifeAvgCps, _cardActiveDays,
                _cardBestDay, _cardBusiestWeekday, _cardBusiestHour, _cardTopProfile,
                _cardStreak, _cardLongestStreak, _cardDailyAvg, _cardThisYear
            })
            {
                if (card == null) continue;
                card.CardColor = _theme.Surface;
                card.CaptionColor = _theme.TextMuted;
                card.ValueColor = _theme.Text;
                card.AccentBar = _theme.Accent;
                card.Invalidate();
            }

            // Headline cards get coloured values for quick reading.
            if (_cardCurrentCps != null) _cardCurrentCps.ValueColor = _theme.Accent;
            if (_cardPeakCps != null) _cardPeakCps.ValueColor = _theme.Success;
            if (_cardToday != null) _cardToday.ValueColor = _theme.Accent;
            if (_cardThisWeek != null) _cardThisWeek.ValueColor = _theme.Accent;
            if (_cardThisMonth != null) _cardThisMonth.ValueColor = _theme.Accent;

            foreach (var chart in new[] { _sessionBarChart, _dailyBarChart, _hourChart, _weekdayChart })
            {
                if (chart == null) continue;
                chart.CardColor = _theme.Surface;
                chart.BarColor = _theme.Accent;
                chart.TextColor = _theme.Text;
                chart.MutedColor = _theme.TextMuted;
                chart.Invalidate();
            }

            if (_historySummaryLabel != null) _historySummaryLabel.ForeColor = _theme.TextMuted;
            _goalProgressBar?.ApplyTheme(_theme);
            _milestoneBar?.ApplyTheme(_theme);
            if (_milestoneBadges != null) _milestoneBadges.ForeColor = _theme.TextMuted;
            UpdateGoalProgress();

            if (_distBar != null)
            {
                _distBar.TrackColor = _theme.Surface2;
                _distBar.LeftColor = _theme.Accent;
                _distBar.RightColor = _theme.Success;
                _distBar.MiddleColor = _theme.Warning;
                _distBar.TextColor = _theme.Text;
                _distBar.MutedColor = _theme.TextMuted;
                _distBar.Invalidate();
            }

            ApplyThemeToSessionList();

            _cpsSparkline.CardColor = _theme.Surface;
            _cpsSparkline.LineColor = _theme.Accent;
            _cpsSparkline.FillColor = Color.FromArgb(60, _theme.Accent);
            _cpsSparkline.GridColor = _theme.Border;
            _cpsSparkline.TextColor = _theme.Text;
            _cpsSparkline.MutedColor = _theme.TextMuted;
            _cpsSparkline.Invalidate();
        }

        private void UpdateStatisticsTab()
        {
            if (_cardSessionClicks == null)
            {
                return;
            }

            double cps = _statistics.GetCurrentCps();

            // Smooth the displayed rate so the number eases to its target instead
            // of jittering frame to frame (exponential moving average).
            double targetCps = _engine.IsRunning ? cps : 0;
            _displayCps += (targetCps - _displayCps) * 0.35;
            if (_displayCps < 0.05) _displayCps = targetCps <= 0 ? 0 : _displayCps;

            _cardSessionClicks.Value = _statistics.SessionClicks.ToString("N0");
            _cardTotalClicks.Value = _statistics.TotalClicks.ToString("N0");
            _cardCurrentCps.Value = _displayCps.ToString("0.0");
            _cardPeakCps.Value = _statistics.PeakClicksPerSecond.ToString("0.0");

            TimeSpan elapsed = _statistics.GetElapsed();
            _cardElapsed.Value = FormatDuration(elapsed);

            _cardAvgCps.Value = _statistics.GetAverageCps().ToString("0.0");
            _cardClicksPerMin.Value = _statistics.GetClicksPerMinute().ToString("N0");
            _cardLeft.Value = _statistics.LeftClicks.ToString("N0");
            _cardRight.Value = _statistics.RightClicks.ToString("N0");
            _cardMiddle.Value = _statistics.MiddleClicks.ToString("N0");
            long btnTotal = _statistics.LeftClicks + _statistics.RightClicks + _statistics.MiddleClicks;
            if (btnTotal > 0)
            {
                _cardLeft.Sub = (_statistics.LeftClicks * 100.0 / btnTotal).ToString("0") + "%";
                _cardRight.Sub = (_statistics.RightClicks * 100.0 / btnTotal).ToString("0") + "%";
                _cardMiddle.Sub = (_statistics.MiddleClicks * 100.0 / btnTotal).ToString("0") + "%";
            }
            else
            {
                _cardLeft.Sub = _cardRight.Sub = _cardMiddle.Sub = string.Empty;
            }

            long lifetimeClicks = _lifetimeBaseline + _statistics.TotalClicks;
            double lifetimePeak = Math.Max(_settings.LifetimePeakCps, _statistics.PeakClicksPerSecond);
            long lifetimeRuntime = _settings.LifetimeRuntimeSeconds +
                                   (_engine.IsRunning ? (long)elapsed.TotalSeconds : 0);

            _cardLifeClicks.Value = lifetimeClicks.ToString("N0");
            _cardLifeSessions.Value = _settings.LifetimeSessions.ToString("N0");
            _cardLifePeak.Value = lifetimePeak.ToString("0.0");
            _cardLifeRuntime.Value = FormatDuration(TimeSpan.FromSeconds(lifetimeRuntime));

            // Derived lifetime averages.
            long sessions = _settings.LifetimeSessions;
            if (_cardAvgPerSession != null)
            {
                _cardAvgPerSession.Value = sessions > 0
                    ? (lifetimeClicks / sessions).ToString("N0")
                    : "0";
            }
            if (_cardAvgRunLength != null)
            {
                _cardAvgRunLength.Value = sessions > 0
                    ? FormatDuration(TimeSpan.FromSeconds(lifetimeRuntime / (double)sessions))
                    : "0s";
            }

            // Records — include the current run live so a new best shows immediately.
            long runClicks = _statistics.TotalClicks - _runStartClicks;
            long bestRun = Math.Max(_settings.LifetimeMostClicksRun, _engine.IsRunning ? runClicks : 0);
            long longestRun = Math.Max(_settings.LifetimeLongestRunSeconds,
                                       _engine.IsRunning ? (long)elapsed.TotalSeconds : 0);
            if (_cardMostClicks != null) _cardMostClicks.Value = bestRun.ToString("N0");
            if (_cardLongestRun != null) _cardLongestRun.Value = FormatDuration(TimeSpan.FromSeconds(longestRun));

            // Per-button distribution bar.
            _distBar?.SetValues(_statistics.LeftClicks, _statistics.RightClicks, _statistics.MiddleClicks);

            // Today's clicks: completed runs dated today + the current run.
            if (_cardToday != null)
            {
                _cardToday.Value = ComputeTodayClicks().ToString("N0");
            }

            ComputeInsights(lifetimeClicks, lifetimeRuntime);

            UpdateGoalProgress();
            UpdateMilestones(lifetimeClicks);

            // Feed the live graph. Only sample while running so the line returns to
            // zero and flattens when idle rather than freezing on the last value.
            _cpsSparkline.Push(_engine.IsRunning ? _displayCps : 0);
        }

        private static string FormatDuration(TimeSpan t)
        {
            if (t.TotalHours >= 1)
            {
                return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
            }
            return $"{t.Minutes:00}:{t.Seconds:00}";
        }

        private void OnResetStats(object sender, EventArgs e)
        {
            // Resetting clears the whole session (clicks, peak CPS, the live charts) and
            // can't be undone, so confirm first. Lifetime totals and history are kept.
            if (_statistics.SessionClicks > 0 || _statistics.TotalClicks > 0)
            {
                var confirm = MessageBox.Show(this,
                    "Reset the current session's statistics?\n\n" +
                    "This clears the session counters, peak CPS and the live charts. " +
                    "Your lifetime totals and saved history are not affected.",
                    "Reset session",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);

                if (confirm != DialogResult.Yes)
                {
                    return;
                }
            }

            _statistics.ResetAll();
            _cpsSparkline?.Clear();
            _distBar?.SetValues(0, 0, 0);
            _goalReachedNotified = false;
            UpdateStatisticsTab();
        }

        private void OnSessionGoalChanged(object sender, EventArgs e)
        {
            if (_suppressGoalEvent)
            {
                return;
            }

            _settings.SessionGoalClicks = (long)_sessionGoalNum.Value;
            _goalReachedNotified = false;
            SettingsManager.Save(_settings);
            UpdateGoalProgress();
        }

        /// <summary>Updates the session-goal progress bar and notifies once on reach.</summary>
        private void UpdateGoalProgress()
        {
            if (_goalProgressBar == null || _goalProgressLabel == null)
            {
                return;
            }

            long goal = _settings.SessionGoalClicks;
            long done = _statistics.SessionClicks;

            if (goal <= 0)
            {
                _goalProgressBar.Value = 0;
                _goalProgressLabel.Text = Utils.Localization.T("off");
                _goalProgressLabel.ForeColor = _theme.TextMuted;
                return;
            }

            double frac = done / (double)goal;
            int pct = (int)Math.Round(frac * 100);
            if (pct < 0) pct = 0;
            if (pct > 100) pct = 100;

            _goalProgressBar.Value = pct;

            bool reached = done >= goal;
            _goalProgressLabel.ForeColor = reached ? _theme.Success : _theme.Text;

            if (reached)
            {
                _goalProgressLabel.Text = pct + "%  ✓";
            }
            else
            {
                // Show an ETA at the current click rate while running.
                double cps = _statistics.GetCurrentCps();
                if (_engine.IsRunning && cps > 0.01)
                {
                    double etaSec = (goal - done) / cps;
                    _goalProgressLabel.Text = Utils.Localization.F("{0}%  •  ~{1} left",
                        pct, FormatDuration(TimeSpan.FromSeconds(etaSec)));
                }
                else
                {
                    _goalProgressLabel.Text = pct + "%";
                }
            }

            if (reached && !_goalReachedNotified)
            {
                _goalReachedNotified = true;
                _statusState.Text = Utils.Localization.T("Session goal reached");
                if (_settings.ShowTrayNotifications && _trayIcon != null)
                {
                    TempoNotify(2500, "Tempo",
                        $"Session goal reached: {goal:N0} clicks.", ToolTipIcon.Info);
                }
            }
        }

        /// <summary>Rebuilds the recent-sessions list from the history store.</summary>
        private void RefreshSessionHistory()
        {
            if (_sessionHistoryList == null)
            {
                return;
            }

            SyncProfileFilterItems();
            string filter = _historyProfileFilter != null && _historyProfileFilter.SelectedIndex > 0
                ? _historyProfileFilter.SelectedItem as string
                : null;

            _sessionHistoryList.BeginUpdate();
            _sessionHistoryList.Items.Clear();

            long shownCount = 0;
            long shownClicks = 0;

            string search = (_historySearchText ?? "").Trim();

            foreach (SessionRecord r in _history.Records)
            {
                string profile = string.IsNullOrEmpty(r.Profile) ? "—" : r.Profile;
                if (filter != null && !string.Equals(profile, filter, StringComparison.Ordinal))
                {
                    continue;
                }

                DateTime local = r.WhenUtc.ToLocalTime();

                // Free-text search over date and profile.
                if (search.Length > 0)
                {
                    string haystack = local.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture) + " " + profile;
                    if (haystack.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                }

                var item = new ListViewItem(local.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture));
                item.SubItems.Add(profile);
                item.SubItems.Add(r.Clicks.ToString("N0"));
                item.SubItems.Add(FormatDuration(TimeSpan.FromSeconds(r.DurationSeconds)));
                item.SubItems.Add(r.AverageCps.ToString("0.0"));
                item.SubItems.Add(r.PeakCps.ToString("0.0"));
                item.Tag = r;
                _sessionHistoryList.Items.Add(item);

                shownCount++;
                shownClicks += r.Clicks;
            }

            _sessionHistoryList.EndUpdate();
            ApplyThemeToSessionList();
            // After the rows exist, so the column fit accounts for the scrollbar.
            _sessionHistoryList.RefreshLayout();

            if (_historySummaryLabel != null)
            {
                string summary = shownCount == 0
                    ? Utils.Localization.T("No sessions to show.")
                    : Utils.Localization.F("{0:N0} session(s) shown  •  {1:N0} clicks total",
                        shownCount, shownClicks);

                // SAY WHEN THE LIST IS ABOUT TO START FORGETTING. The store keeps the
                // newest 200 runs and silently drops the rest — the lifetime totals
                // survive, but individual runs do not, and nothing anywhere said so.
                // Someone sitting on 196 rows is four runs from losing the oldest with
                // no warning at all, and Export CSV (right below this line) is the
                // thing that would have kept them.
                int kept = _history != null ? _history.Records.Count : 0;
                int cap = Persistence.SessionHistoryStore.Capacity;
                if (kept >= cap)
                {
                    summary += "  •  " + Utils.Localization.F(
                        "keeping the newest {0:N0} — older runs drop off as new ones arrive; Export CSV keeps them",
                        cap);
                }
                else if (kept >= cap - 10)
                {
                    summary += "  •  " + Utils.Localization.F(
                        "{0:N0} of {1:N0} kept — the oldest start dropping off after that",
                        kept, cap);
                }

                _historySummaryLabel.Text = summary;
            }

            // Feed the recent-session bar chart (last 24, oldest → newest).
            if (_sessionBarChart != null)
            {
                var vals = new System.Collections.Generic.List<long>();
                int take = Math.Min(24, _history.Records.Count);
                for (int i = take - 1; i >= 0; i--)
                {
                    vals.Add(_history.Records[i].Clicks);
                }
                _sessionBarChart.SetValues(vals);
            }

            // Feed the last-7-days chart.
            RefreshDailyChart();
        }

        /// <summary>Keeps the profile-filter dropdown in sync with the profiles in history.</summary>
        private void SyncProfileFilterItems()
        {
            if (_historyProfileFilter == null)
            {
                return;
            }

            var profiles = new System.Collections.Generic.List<string>();
            foreach (SessionRecord r in _history.Records)
            {
                string profile = string.IsNullOrEmpty(r.Profile) ? "—" : r.Profile;
                if (!profiles.Contains(profile))
                {
                    profiles.Add(profile);
                }
            }
            profiles.Sort(StringComparer.OrdinalIgnoreCase);

            // Only rebuild if the set changed, to preserve the current selection.
            bool changed = _historyProfileFilter.Items.Count != profiles.Count + 1;
            if (!changed)
            {
                for (int i = 0; i < profiles.Count; i++)
                {
                    if (!string.Equals(_historyProfileFilter.Items[i + 1] as string, profiles[i], StringComparison.Ordinal))
                    {
                        changed = true;
                        break;
                    }
                }
            }

            if (!changed)
            {
                return;
            }

            string current = _historyProfileFilter.SelectedItem as string;
            _suppressHistoryFilterEvent = true;
            _historyProfileFilter.BeginUpdate();
            _historyProfileFilter.Items.Clear();
            _historyProfileFilter.Items.Add(Utils.Localization.T("All profiles"));
            foreach (string p in profiles)
            {
                _historyProfileFilter.Items.Add(p);
            }

            int idx = current != null ? _historyProfileFilter.Items.IndexOf(current) : 0;
            _historyProfileFilter.SelectedIndex = idx >= 0 ? idx : 0;
            _historyProfileFilter.EndUpdate();
            _suppressHistoryFilterEvent = false;
        }

        private void OnHistoryFilterChanged(object sender, EventArgs e)
        {
            if (_suppressHistoryFilterEvent)
            {
                return;
            }
            RefreshSessionHistory();
        }

        /// <summary>Aggregates the history into clicks-per-day for the last 7 days.</summary>
        private void RefreshDailyChart()
        {
            if (_dailyBarChart == null)
            {
                return;
            }

            var totals = new long[7];
            DateTime today = DateTime.Now.Date;

            foreach (SessionRecord r in _history.Records)
            {
                DateTime day = r.WhenUtc.ToLocalTime().Date;
                int ago = (int)(today - day).TotalDays;
                if (ago >= 0 && ago < 7)
                {
                    totals[6 - ago] += r.Clicks; // index 6 = today, 0 = six days ago
                }
            }

            // Include the live run in today's bar.
            if (_engine.IsRunning)
            {
                long runClicks = _statistics.TotalClicks - _runStartClicks;
                if (runClicks > 0) totals[6] += runClicks;
            }

            var vals = new System.Collections.Generic.List<long>();
            var labels = new System.Collections.Generic.List<string>();
            long weekTotal = 0;
            long peakDay = 0;
            for (int i = 0; i < 7; i++)
            {
                vals.Add(totals[i]);
                weekTotal += totals[i];
                if (totals[i] > peakDay) peakDay = totals[i];
                DateTime d = today.AddDays(-(6 - i));
                labels.Add(d.ToString("ddd", CultureInfo.CurrentCulture));
            }
            _dailyBarChart.SetValues(vals, labels);
            // F(), not string.Format(T(...)): F falls back to the English frame if a
            // translation ever loses a placeholder, instead of throwing FormatException.
            _dailyBarChart.Title = weekTotal > 0
                ? Utils.Localization.F("Last 7 days · {0} clicks · peak {1}",
                    weekTotal.ToString("N0"), peakDay.ToString("N0"))
                : Utils.Localization.T("Clicks — last 7 days");
            _dailyBarChart.Invalidate();
        }

        /// <summary>Sum of clicks from completed runs dated today, plus the live run.</summary>
        /// <summary>Populates the Insights cards and the hour-of-day chart from history.</summary>
        private void ComputeInsights(long lifetimeClicks, long lifetimeRuntimeSeconds)
        {
            DateTime now = DateTime.Now;
            DateTime today = now.Date;
            DateTime weekAgo = today.AddDays(-6); // rolling 7-day window incl. today

            long weekClicks = 0, monthClicks = 0, yearClicks = 0;
            var byDay = new System.Collections.Generic.Dictionary<DateTime, long>();
            var byWeekday = new long[7];
            var byHour = new long[24];
            var byProfile = new System.Collections.Generic.Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            foreach (SessionRecord r in _history.Records)
            {
                DateTime local = r.WhenUtc.ToLocalTime();
                DateTime day = local.Date;

                if (day >= weekAgo) weekClicks += r.Clicks;
                if (day.Year == now.Year && day.Month == now.Month) monthClicks += r.Clicks;
                if (day.Year == now.Year) yearClicks += r.Clicks;

                byDay.TryGetValue(day, out long dc);
                byDay[day] = dc + r.Clicks;
                byWeekday[(int)local.DayOfWeek] += r.Clicks;
                byHour[local.Hour] += r.Clicks;

                string prof = string.IsNullOrWhiteSpace(r.Profile) ? "(unnamed)" : r.Profile;
                byProfile.TryGetValue(prof, out long pc);
                byProfile[prof] = pc + r.Clicks;
            }

            if (_cardThisWeek != null) _cardThisWeek.Value = weekClicks.ToString("N0");
            if (_cardThisMonth != null) _cardThisMonth.Value = monthClicks.ToString("N0");

            // Today card: add a signed "vs yesterday" trend in the sublabel (reusing the
            // per-day totals already built above). Today includes the live run so it matches
            // the number shown as the card's Value.
            if (_cardToday != null)
            {
                // Deliberately the SAME source as the card's Value directly above, not a
                // second computation from byDay. ComputeTodayClicks already reconciles the
                // capped history against the rolling aggregate and folds in the live run;
                // recomputing here would make the delta disagree with the number printed
                // beside it as soon as the 200-entry cap starts trimming today's runs.
                long todayTotal = ComputeTodayClicks();
                byDay.TryGetValue(today.AddDays(-1), out long yesterday);
                long delta = todayTotal - yesterday;
                // Only show once there is a yesterday baseline, so a fresh install doesn't
                // read a noisy "+0 vs yesterday".
                _cardToday.Sub = yesterday > 0
                    ? (delta >= 0 ? "+" : "") + delta.ToString("N0") + " " + Utils.Localization.T("vs yesterday")
                    : string.Empty;
            }

            // Lifetime average CPS = total clicks / total active seconds.
            if (_cardLifeAvgCps != null)
            {
                _cardLifeAvgCps.Value = lifetimeRuntimeSeconds > 0
                    ? ((double)lifetimeClicks / lifetimeRuntimeSeconds).ToString("0.0")
                    : "0.0";
            }

            // All-time insight cards read from the rolling lifetime aggregates (persisted
            // per run) rather than the recent-history window, so they stay accurate once
            // the 200-entry history cap starts trimming old runs. The CHARTS below stay on
            // recent history — they're meant to show a recent trend.
            var lba = _settings;
            if (_cardActiveDays != null) _cardActiveDays.Value = lba.LifetimeActiveDays.ToString("N0");

            // Best single day.
            if (_cardBestDay != null)
            {
                long bestDayClicks = lba.LifetimeBestDayClicks;
                _cardBestDay.Value = bestDayClicks <= 0 ? "—" : $"{bestDayClicks:N0}";
                string cap = Utils.Localization.T("Best Day");
                if (bestDayClicks > 0 && DateTime.TryParseExact(lba.LifetimeBestDay ?? "", "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out DateTime bd))
                {
                    cap += " · " + bd.ToString("d MMM", Utils.Localization.DateCulture);
                }
                _cardBestDay.Caption = cap;
            }

            // Busiest weekday.
            if (_cardBusiestWeekday != null)
            {
                int bestWd = -1; long bestWdClicks = -1;
                long[] wd = lba.LifetimeByWeekday;
                if (wd != null)
                    for (int i = 0; i < 7 && i < wd.Length; i++)
                        if (wd[i] > bestWdClicks) { bestWdClicks = wd[i]; bestWd = i; }
                _cardBusiestWeekday.Value = bestWdClicks <= 0
                    ? "—"
                    : Utils.Localization.DateCulture.DateTimeFormat
                        .GetAbbreviatedDayName((DayOfWeek)bestWd);
            }

            // Busiest hour.
            if (_cardBusiestHour != null)
            {
                int bestHr = -1; long bestHrClicks = -1;
                long[] hr = lba.LifetimeByHour;
                if (hr != null)
                    for (int i = 0; i < 24 && i < hr.Length; i++)
                        if (hr[i] > bestHrClicks) { bestHrClicks = hr[i]; bestHr = i; }
                _cardBusiestHour.Value = bestHrClicks <= 0 ? "—" : FormatHour(bestHr);
            }

            // Top profile by clicks.
            if (_cardTopProfile != null)
            {
                string topProfile = "—"; long topClicks = -1;
                if (lba.LifetimeByProfile != null)
                    foreach (var kv in lba.LifetimeByProfile)
                        if (kv.Value > topClicks) { topClicks = kv.Value; topProfile = kv.Key; }
                _cardTopProfile.Value = topClicks <= 0 ? "—" : topProfile;
            }

            // Hour-of-day chart (00..23).
            if (_hourChart != null)
            {
                var vals = new System.Collections.Generic.List<long>(24);
                var labels = new System.Collections.Generic.List<string>(24);
                for (int i = 0; i < 24; i++)
                {
                    vals.Add(byHour[i]);
                    labels.Add(i % 6 == 0 ? i.ToString("00") : "");
                }
                _hourChart.SetValues(vals, labels);
            }

            // Day-of-week chart (Sun..Sat), reusing the weekday tally.
            if (_weekdayChart != null)
            {
                var vals = new System.Collections.Generic.List<long>(7);
                var labels = new System.Collections.Generic.List<string>(7);
                var dtf = Utils.Localization.DateCulture.DateTimeFormat;
                for (int i = 0; i < 7; i++)
                {
                    vals.Add(byWeekday[i]);
                    labels.Add(dtf.GetAbbreviatedDayName((DayOfWeek)i));
                }
                _weekdayChart.SetValues(vals, labels);
            }

            // This year (all-time aggregate; 0 if the counter is for a previous year and
            // nothing has been clicked yet this year).
            if (_cardThisYear != null)
            {
                long yr = lba.LifetimeYearOf == today.Year ? lba.LifetimeYearClicks : 0;
                _cardThisYear.Value = yr.ToString("N0");
            }

            // Daily average = all-time clicks / all-time active days.
            if (_cardDailyAvg != null)
            {
                _cardDailyAvg.Value = lba.LifetimeActiveDays > 0
                    ? ((double)lifetimeClicks / lba.LifetimeActiveDays).ToString("N0")
                    : "0";
            }

            // Streaks from the aggregates. The current streak only counts as "alive" when
            // the last active day is today or yesterday; otherwise it's been broken.
            if (_cardStreak != null || _cardLongestStreak != null)
            {
                int current = 0;
                if (DateTime.TryParseExact(lba.LifetimeLastActiveDay ?? "", "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out DateTime lastDay))
                {
                    if (lastDay.Date == today || lastDay.Date == today.AddDays(-1))
                    {
                        current = lba.LifetimeCurrentStreak;
                    }
                }

                if (_cardStreak != null) _cardStreak.Value = current.ToString("N0");
                if (_cardLongestStreak != null) _cardLongestStreak.Value = lba.LifetimeLongestStreak.ToString("N0");
            }
        }

        private static string FormatHour(int hour24)
        {
            if (hour24 == 0) return "12 AM";
            if (hour24 == 12) return "12 PM";
            return hour24 < 12 ? hour24 + " AM" : (hour24 - 12) + " PM";
        }

        private void EnsureLifetimeAggregateArrays()
        {
            if (_settings == null) return;
            if (_settings.LifetimeByHour == null || _settings.LifetimeByHour.Length != 24)
                _settings.LifetimeByHour = new long[24];
            if (_settings.LifetimeByWeekday == null || _settings.LifetimeByWeekday.Length != 7)
                _settings.LifetimeByWeekday = new long[7];
            if (_settings.LifetimeByProfile == null)
                _settings.LifetimeByProfile = new System.Collections.Generic.Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Folds one completed run into the rolling lifetime aggregates that feed the
        /// all-time insight cards, so those cards survive the 200-entry history cap. Uses
        /// local time (the cards are shown in local time). Callers persist settings.
        /// </summary>
        private void AccumulateLifetimeAggregates(long clicks, DateTime whenUtc, string profile)
        {
            if (_settings == null || clicks <= 0) return;
            EnsureLifetimeAggregateArrays();

            DateTime local = whenUtc.ToLocalTime();
            DateTime day = local.Date;
            string dayStr = day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

            _settings.LifetimeByHour[local.Hour] += clicks;
            _settings.LifetimeByWeekday[(int)local.DayOfWeek] += clicks;
            string prof = string.IsNullOrWhiteSpace(profile) ? "(unnamed)" : profile;
            _settings.LifetimeByProfile.TryGetValue(prof, out long pc);
            _settings.LifetimeByProfile[prof] = pc + clicks;

            if (_settings.LifetimeYearOf != local.Year)
            {
                _settings.LifetimeYearOf = local.Year;
                _settings.LifetimeYearClicks = 0;
            }
            _settings.LifetimeYearClicks += clicks;

            if (dayStr == _settings.LifetimeLastActiveDay)
            {
                _settings.LifetimeCurrentDayClicks += clicks;
            }
            else
            {
                int newStreak = 1;
                if (!string.IsNullOrEmpty(_settings.LifetimeLastActiveDay)
                    && DateTime.TryParseExact(_settings.LifetimeLastActiveDay, "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out DateTime prev)
                    && day == prev.Date.AddDays(1))
                {
                    newStreak = _settings.LifetimeCurrentStreak + 1;
                }
                _settings.LifetimeCurrentStreak = newStreak;
                if (newStreak > _settings.LifetimeLongestStreak)
                {
                    _settings.LifetimeLongestStreak = newStreak;
                }
                _settings.LifetimeActiveDays++;
                _settings.LifetimeLastActiveDay = dayStr;
                _settings.LifetimeCurrentDayClicks = clicks;
            }

            if (_settings.LifetimeCurrentDayClicks > _settings.LifetimeBestDayClicks)
            {
                _settings.LifetimeBestDayClicks = _settings.LifetimeCurrentDayClicks;
                _settings.LifetimeBestDay = dayStr;
            }
        }

        /// <summary>
        /// One-time seed of the lifetime aggregates from the existing session history, so a
        /// user upgrading with hundreds of past runs doesn't see the all-time cards reset to
        /// near-zero. Records are replayed oldest-first so the streak logic is correct.
        /// </summary>
        private void SeedLifetimeAggregatesIfNeeded()
        {
            if (_settings == null || _settings.LifetimeAggregatesSeeded) return;
            try
            {
                _settings.LifetimeByHour = new long[24];
                _settings.LifetimeByWeekday = new long[7];
                _settings.LifetimeByProfile = new System.Collections.Generic.Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                _settings.LifetimeActiveDays = 0;
                _settings.LifetimeLastActiveDay = "";
                _settings.LifetimeCurrentDayClicks = 0;
                _settings.LifetimeBestDayClicks = 0;
                _settings.LifetimeBestDay = "";
                _settings.LifetimeCurrentStreak = 0;
                _settings.LifetimeLongestStreak = 0;
                _settings.LifetimeYearClicks = 0;
                _settings.LifetimeYearOf = 0;

                if (_history != null)
                {
                    var ordered = new System.Collections.Generic.List<Models.SessionRecord>(_history.Records);
                    ordered.Sort((a, b) => a.WhenUtc.CompareTo(b.WhenUtc));
                    foreach (var r in ordered)
                    {
                        AccumulateLifetimeAggregates(r.Clicks, r.WhenUtc, r.Profile);
                    }
                }
            }
            catch { /* best effort — a malformed record must not block startup */ }
            _settings.LifetimeAggregatesSeeded = true;
            try { Persistence.SettingsManager.Save(_settings); } catch { }
        }

        private long ComputeTodayClicks()
        {
            DateTime today = DateTime.Now.Date;

            long scanned = 0;
            foreach (SessionRecord r in _history.Records)
            {
                if (r.WhenUtc.ToLocalTime().Date == today)
                {
                    scanned += r.Clicks;
                }
            }

            // Two things already know today's total, and they disagree in one case.
            // SessionHistoryStore keeps only the newest MaxEntries (200) runs, so scanning
            // it silently loses today's earliest runs once that cap starts trimming —
            // "Today" would then go DOWN over a long session of short runs, which is the
            // one thing a daily counter must never do. LifetimeCurrentDayClicks is the
            // rolling aggregate kept precisely so the cap can't erase history, and it is
            // fed by the same records.
            //
            // Take the larger of the two rather than replacing one with the other: the
            // aggregate is only meaningful while its last-active day IS today, and on a
            // profile upgraded from a build that never wrote aggregates it starts at zero,
            // where the scan is the only source. Whichever is populated wins, and they
            // agree exactly in the ordinary case, so this can neither under-report nor
            // double-count.
            long aggregate = 0;
            if (_settings != null &&
                string.Equals(_settings.LifetimeLastActiveDay,
                              today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                              StringComparison.Ordinal))
            {
                aggregate = _settings.LifetimeCurrentDayClicks;
            }

            long sum = Math.Max(scanned, aggregate);

            if (_engine.IsRunning)
            {
                long runClicks = _statistics.TotalClicks - _runStartClicks;
                if (runClicks > 0) sum += runClicks;
            }

            return sum;
        }

        private SessionRecord SelectedHistoryRecord()
        {
            if (_sessionHistoryList.SelectedItems.Count == 0)
            {
                return null;
            }
            return _sessionHistoryList.SelectedItems[0].Tag as SessionRecord;
        }

        private void OnSessionHistoryDetails(object sender, EventArgs e)
        {
            SessionRecord r = SelectedHistoryRecord();
            if (r == null)
            {
                return;
            }

            DateTime local = r.WhenUtc.ToLocalTime();
            var ts = TimeSpan.FromSeconds(r.DurationSeconds);

            string details =
                $"When:        {local:dddd, dd MMM yyyy  HH:mm:ss}\n" +
                $"Profile:     {(string.IsNullOrEmpty(r.Profile) ? "—" : r.Profile)}\n" +
                $"Clicks:      {r.Clicks:N0}\n" +
                $"Duration:    {FormatDuration(ts)}  ({r.DurationSeconds:0.0} s)\n" +
                $"Average CPS: {r.AverageCps:0.0}\n" +
                $"Peak CPS:    {r.PeakCps:0.0}";

            MessageBox.Show(this, details, "Session details",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnCopyHistoryRow(object sender, EventArgs e)
        {
            SessionRecord r = SelectedHistoryRecord();
            if (r == null)
            {
                return;
            }

            DateTime local = r.WhenUtc.ToLocalTime();
            string line = string.Join("\t",
                local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                string.IsNullOrEmpty(r.Profile) ? "-" : r.Profile,
                r.Clicks.ToString(CultureInfo.InvariantCulture),
                r.DurationSeconds.ToString("0.0", CultureInfo.InvariantCulture),
                r.AverageCps.ToString("0.0", CultureInfo.InvariantCulture),
                r.PeakCps.ToString("0.0", CultureInfo.InvariantCulture));

            try { Clipboard.SetText(line); } catch { /* clipboard may be busy */ }
        }

        private void OnDeleteHistoryEntry(object sender, EventArgs e)
        {
            SessionRecord r = SelectedHistoryRecord();
            if (r == null)
            {
                return;
            }

            // Map the record back to its store index (records are reference-equal).
            int idx = -1;
            for (int i = 0; i < _history.Records.Count; i++)
            {
                if (ReferenceEquals(_history.Records[i], r)) { idx = i; break; }
            }

            if (idx >= 0)
            {
                _history.RemoveAt(idx);
                RefreshSessionHistory();
            }
        }

        /// <summary>Sorts the history list by the clicked column (toggles direction).</summary>
        private void OnSessionHistoryColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (e.Column == _histSortColumn)
            {
                _histSortAsc = !_histSortAsc;
            }
            else
            {
                _histSortColumn = e.Column;
                _histSortAsc = true;
            }

            _sessionHistoryList.ListViewItemSorter =
                new HistoryColumnComparer(e.Column, _histSortAsc);
            _sessionHistoryList.Sort();

            // Show which column drives the order, and which way.
            string[] baseTitles = { "When", "Profile", "Clicks", "Duration", "Avg CPS", "Peak CPS" };
            for (int i = 0; i < _sessionHistoryList.Columns.Count && i < baseTitles.Length; i++)
            {
                string t = baseTitles[i];
                if (i == _histSortColumn)
                {
                    t += _histSortAsc ? " \u25B2" : " \u25BC";
                }
                _sessionHistoryList.Columns[i].Text = t;
            }
        }

        private void ApplyThemeToSessionList()
        {
            if (_sessionHistoryList == null)
            {
                return;
            }

            // Goes through the control so the header, stripes and profile chips are
            // repainted too — setting Back/ForeColor alone left the system-drawn header
            // as unreadable dark-on-dark text.
            _sessionHistoryList.ApplyTheme(_theme);
        }

        private void OnCopyStatsSummary(object sender, EventArgs e)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Tempo — statistics summary");
            sb.AppendLine("Session clicks : " + _statistics.SessionClicks.ToString("N0"));
            sb.AppendLine("Total clicks   : " + _statistics.TotalClicks.ToString("N0"));
            sb.AppendLine("Current CPS    : " + _displayCps.ToString("0.0"));
            sb.AppendLine("Peak CPS       : " + _statistics.PeakClicksPerSecond.ToString("0.0"));
            sb.AppendLine("Average CPS    : " + _statistics.GetAverageCps().ToString("0.0"));
            sb.AppendLine("Elapsed        : " + FormatDuration(_statistics.GetElapsed()));
            sb.AppendLine("Sessions logged: " + _history.Records.Count.ToString("N0"));
            if (_cardToday != null) sb.AppendLine("Today          : " + _cardToday.Value);
            if (_cardLifeClicks != null) sb.AppendLine("Lifetime clicks: " + _cardLifeClicks.Value);
            if (_cardLifePeak != null) sb.AppendLine("Best CPS ever  : " + _cardLifePeak.Value);
            if (_cardThisWeek != null) sb.AppendLine("This week      : " + _cardThisWeek.Value);
            if (_cardThisMonth != null) sb.AppendLine("This month     : " + _cardThisMonth.Value);
            if (_cardThisYear != null) sb.AppendLine("This year      : " + _cardThisYear.Value);
            if (_cardActiveDays != null) sb.AppendLine("Active days    : " + _cardActiveDays.Value);
            if (_cardDailyAvg != null) sb.AppendLine("Daily average  : " + _cardDailyAvg.Value);
            if (_cardBestDay != null) sb.AppendLine("Best day       : " + _cardBestDay.Value);
            if (_cardStreak != null) sb.AppendLine("Current streak : " + _cardStreak.Value + " day(s)");
            if (_cardLongestStreak != null) sb.AppendLine("Longest streak : " + _cardLongestStreak.Value + " day(s)");
            if (_cardBusiestWeekday != null) sb.AppendLine("Busiest day    : " + _cardBusiestWeekday.Value);
            if (_cardBusiestHour != null) sb.AppendLine("Busiest hour   : " + _cardBusiestHour.Value);
            if (_cardTopProfile != null) sb.AppendLine("Top profile    : " + _cardTopProfile.Value);
            if (_milestoneLabel != null && !string.IsNullOrEmpty(_milestoneLabel.Text))
                sb.AppendLine("Milestone      : " + _milestoneLabel.Text);

            try
            {
                Clipboard.SetText(sb.ToString());
                ShowInfo("Statistics summary copied to the clipboard.");
            }
            catch
            {
                ShowWarning("Couldn't copy to the clipboard — it may be in use by another app.");
            }
        }

        private void OnClearHistory(object sender, EventArgs e)
        {
            if (_history.Records.Count == 0)
            {
                return;
            }

            var confirm = MessageBox.Show(this,
                "Clear the recent-sessions history?",
                "Tempo", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes)
            {
                return;
            }

            _history.Clear();
            RefreshSessionHistory();
        }

        private void OnExportStatsCsv(object sender, EventArgs e)
        {
            using (var dlg = new SaveFileDialog
            {
                Title = Utils.Localization.T("Export statistics"),
                Filter = Utils.Localization.T("CSV file (*.csv)|*.csv|All files (*.*)|*.*"),
                FileName = "tempo-stats.csv"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    var sb = new StringBuilder();
                    var inv = CultureInfo.InvariantCulture;
                    // The insight cards are formatted for display with thousands separators
                    // (12,345); reusing those strings verbatim splits a value across CSV
                    // columns. Strip the current culture's group separator when reusing them.
                    string grp = CultureInfo.CurrentCulture.NumberFormat.NumberGroupSeparator;
                    string Card(StatCard c) => (c?.Value ?? "").Replace(grp, "");
                    string Dec(double v) => v.ToString("0.0", inv);

                    // Summary block. All numbers invariant so decimals use a period and
                    // never contain the CSV delimiter (a comma) in decimal-comma locales.
                    sb.AppendLine("Metric,Value");
                    sb.AppendLine($"Session clicks,{_statistics.SessionClicks}");
                    sb.AppendLine($"Total clicks (launch),{_statistics.TotalClicks}");
                    sb.AppendLine($"Left clicks,{_statistics.LeftClicks}");
                    sb.AppendLine($"Right clicks,{_statistics.RightClicks}");
                    sb.AppendLine($"Middle clicks,{_statistics.MiddleClicks}");
                    sb.AppendLine($"Peak CPS,{Dec(_statistics.PeakClicksPerSecond)}");
                    sb.AppendLine($"Average CPS,{Dec(_statistics.GetAverageCps())}");
                    sb.AppendLine($"Lifetime clicks,{_lifetimeBaseline + _statistics.TotalClicks}");
                    sb.AppendLine($"Lifetime sessions,{_settings.LifetimeSessions}");
                    sb.AppendLine($"Best CPS ever,{Dec(_settings.LifetimePeakCps)}");
                    sb.AppendLine($"Total runtime (s),{_settings.LifetimeRuntimeSeconds}");
                    sb.AppendLine($"Most clicks per run,{_settings.LifetimeMostClicksRun}");
                    sb.AppendLine($"Longest run (s),{_settings.LifetimeLongestRunSeconds}");

                    // Insights (mirrors the cards on the dashboard).
                    if (_cardThisWeek != null) sb.AppendLine($"This week,{Card(_cardThisWeek)}");
                    if (_cardThisMonth != null) sb.AppendLine($"This month,{Card(_cardThisMonth)}");
                    if (_cardThisYear != null) sb.AppendLine($"This year,{Card(_cardThisYear)}");
                    if (_cardActiveDays != null) sb.AppendLine($"Active days,{Card(_cardActiveDays)}");
                    if (_cardDailyAvg != null) sb.AppendLine($"Daily average,{Card(_cardDailyAvg)}");
                    if (_cardBestDay != null) sb.AppendLine($"Best day,{Card(_cardBestDay)}");
                    if (_cardStreak != null) sb.AppendLine($"Current streak (days),{Card(_cardStreak)}");
                    if (_cardLongestStreak != null) sb.AppendLine($"Longest streak (days),{Card(_cardLongestStreak)}");
                    if (_cardBusiestWeekday != null) sb.AppendLine($"Busiest weekday,{(_cardBusiestWeekday.Value ?? "").Replace(",", " ")}");
                    if (_cardBusiestHour != null) sb.AppendLine($"Busiest hour,{(_cardBusiestHour.Value ?? "").Replace(",", " ")}");
                    if (_cardTopProfile != null) sb.AppendLine($"Top profile,{(_cardTopProfile.Value ?? "").Replace(",", " ")}");
                    sb.AppendLine();

                    // Session history block.
                    sb.AppendLine("When,Profile,Clicks,Duration (s),Average CPS,Peak CPS");
                    foreach (SessionRecord r in _history.Records)
                    {
                        string when = r.WhenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", inv);
                        string profile = (r.Profile ?? "").Replace(",", " ");
                        sb.AppendLine($"{when},{profile},{r.Clicks},{Dec(r.DurationSeconds)},{Dec(r.AverageCps)},{Dec(r.PeakCps)}");
                    }

                    File.WriteAllText(dlg.FileName, sb.ToString());
                    ShowInfo("Statistics exported.");
                }
                catch (Exception ex)
                {
                    ShowWarning(Localization.F("Could not export statistics: {0}", ex.Message));
                }
            }
        }

        private void OnExportStatsJson(object sender, EventArgs e)
        {
            using (var dlg = new SaveFileDialog
            {
                Title = Utils.Localization.T("Export statistics (JSON)"),
                Filter = Utils.Localization.T("JSON file (*.json)|*.json|All files (*.*)|*.*"),
                FileName = "tempo-stats.json"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    // Structured export straight from the numeric sources — no locale /
                    // delimiter hazards, and trivial to re-import or process. Sessions come
                    // from the same records the history store serialises.
                    var dto = new
                    {
                        summary = new
                        {
                            _statistics.SessionClicks,
                            _statistics.TotalClicks,
                            _statistics.LeftClicks,
                            _statistics.RightClicks,
                            _statistics.MiddleClicks,
                            PeakCps = _statistics.PeakClicksPerSecond,
                            AverageCps = _statistics.GetAverageCps(),
                            LifetimeClicks = _lifetimeBaseline + _statistics.TotalClicks,
                            _settings.LifetimeSessions,
                            BestCpsEver = _settings.LifetimePeakCps,
                            _settings.LifetimeRuntimeSeconds,
                            _settings.LifetimeMostClicksRun,
                            _settings.LifetimeLongestRunSeconds
                        },
                        sessions = _history.Records
                    };

                    string json = System.Text.Json.JsonSerializer.Serialize(
                        dto, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(dlg.FileName, json);
                    ShowInfo("Statistics exported.");
                }
                catch (Exception ex)
                {
                    ShowWarning(Localization.F("Could not export statistics: {0}", ex.Message));
                }
            }
        }

        private void OnResetLifetime(object sender, EventArgs e)
        {
            var confirm = MessageBox.Show(this,
                "Reset all lifetime totals (clicks, sessions, best CPS, runtime)?\n" +
                "This cannot be undone.",
                "Tempo",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes)
            {
                return;
            }

            _settings.LifetimeClicks = 0;
            _settings.LifetimeSessions = 0;
            _settings.LifetimePeakCps = 0;
            _settings.LifetimeRuntimeSeconds = 0;
            _settings.LifetimeMostClicksRun = 0;
            _settings.LifetimeLongestRunSeconds = 0;
            // Also clear the rolling insight aggregates (kept seeded so a later run rebuilds
            // them from scratch rather than re-seeding from the old history).
            _settings.LifetimeByHour = new long[24];
            _settings.LifetimeByWeekday = new long[7];
            _settings.LifetimeByProfile = new System.Collections.Generic.Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            _settings.LifetimeActiveDays = 0;
            _settings.LifetimeLastActiveDay = "";
            _settings.LifetimeCurrentDayClicks = 0;
            _settings.LifetimeBestDayClicks = 0;
            _settings.LifetimeBestDay = "";
            _settings.LifetimeCurrentStreak = 0;
            _settings.LifetimeLongestStreak = 0;
            _settings.LifetimeYearClicks = 0;
            _settings.LifetimeYearOf = 0;
            _settings.LifetimeAggregatesSeeded = true;
            _lifetimeBaseline = 0;
            _statistics.ResetAll();
            SettingsManager.Save(_settings);
            _cpsSparkline?.Clear();
            UpdateStatisticsTab();
        }
    }
}
