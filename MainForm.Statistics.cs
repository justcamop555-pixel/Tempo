using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Persistence;

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
            _statsPage = page;

            var title = UiFactory.Label("Live Dashboard", 16, 14, FontStyle.Bold, 13f);
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

            page.Controls.Add(UiFactory.Label("Session goal (clicks, 0 = off):", 16, goalY + 2, FontStyle.Bold, 9.5f));
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

            // ── Row 2: rate + timing ──────────────────────────────────────────
            int row2 = goalY + 34;
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
            var byBtnLabel = UiFactory.Label("Clicks by button", 16, byBtnLabelY, FontStyle.Bold, 10f);
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
            var lifeTitle = UiFactory.Label("Lifetime", 16, lifeTitleY, FontStyle.Bold, 12f);
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
            page.Controls.Add(UiFactory.Label("Records", 16, recTitleY, FontStyle.Bold, 12f));

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
            page.Controls.Add(UiFactory.Label("Insights", 16, insTitleY, FontStyle.Bold, 12f));

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
            page.Controls.Add(UiFactory.Label("Recent sessions (double-click for details, right-click for options)", 16, histTitleY, FontStyle.Bold, 12f));

            // Search + filter by profile.
            page.Controls.Add(UiFactory.Caption("Find:", rightEdge - 448, histTitleY + 2));
            _historySearchBox = UiFactory.Text(rightEdge - 410, histTitleY - 2, 170);
            _historySearchBox.TextChanged += (s, e) => { _historySearchText = _historySearchBox.Text; RefreshSessionHistory(); };
            page.Controls.Add(_historySearchBox);

            _historyProfileFilter = UiFactory.Combo(rightEdge - 230, histTitleY - 2, 170, "All profiles");
            _historyProfileFilter.DropDownStyle = ComboBoxStyle.DropDownList;
            _historyProfileFilter.SelectedIndex = 0;
            _historyProfileFilter.SelectedIndexChanged += OnHistoryFilterChanged;
            page.Controls.Add(_historyProfileFilter);

            int histY = histTitleY + 28;
            _sessionHistoryList = new ListView
            {
                Left = c0,
                Top = histY,
                Width = (CardW * 4) + (CardGap * 3),
                Height = 180,
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                HideSelection = true,
                MultiSelect = false
            };
            _sessionHistoryList.Columns.Add("When", 150);
            _sessionHistoryList.Columns.Add("Profile", 130);
            _sessionHistoryList.Columns.Add("Clicks", 90);
            _sessionHistoryList.Columns.Add("Duration", 100);
            _sessionHistoryList.Columns.Add("Avg CPS", 80);
            _sessionHistoryList.Columns.Add("Peak CPS", 80);
            _sessionHistoryList.DoubleClick += OnSessionHistoryDetails;
            _sessionHistoryList.ColumnClick += OnSessionHistoryColumnClick;

            var histMenu = new ContextMenuStrip();
            histMenu.Items.Add("View details", null, (s, e) => OnSessionHistoryDetails(s, e));
            histMenu.Items.Add("Copy row", null, OnCopyHistoryRow);
            histMenu.Items.Add(new ToolStripSeparator());
            histMenu.Items.Add("Delete entry", null, OnDeleteHistoryEntry);
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
            _resetStatsBtn = UiFactory.Button("Reset session", c0, btnY, 150, 32);
            _resetStatsBtn.Click += OnResetStats;
            page.Controls.Add(_resetStatsBtn);

            _resetLifetimeBtn = UiFactory.Button("Reset lifetime", c1, btnY, 150, 32);
            _resetLifetimeBtn.Click += OnResetLifetime;
            page.Controls.Add(_resetLifetimeBtn);

            var exportCsvBtn = UiFactory.Button("Export CSV…", c2, btnY, 150, 32);
            exportCsvBtn.Click += OnExportStatsCsv;
            page.Controls.Add(exportCsvBtn);

            var clearHistBtn = UiFactory.Button("Clear history", c3, btnY, 150, 32);
            clearHistBtn.Click += OnClearHistory;
            page.Controls.Add(clearHistBtn);

            var copySummaryBtn = UiFactory.Button("Copy summary", c0, btnY + 40, 150, 32);
            copySummaryBtn.Click += OnCopyStatsSummary;
            page.Controls.Add(copySummaryBtn);

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
                    _trayIcon.ShowBalloonTip(
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
                _milestoneLabel.Text =
                    $"All milestones reached — {lifetimeClicks:N0} lifetime clicks. Incredible!";
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
                _milestoneLabel.Text =
                    $"{lifetimeClicks:N0} / {next:N0} lifetime clicks  —  " +
                    $"{remaining:N0} to go until the {FormatMilestone(next)} milestone ({pct}%)";
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
                _goalProgressLabel.Text = "off";
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
                    _goalProgressLabel.Text = $"{pct}%  •  ~{FormatDuration(TimeSpan.FromSeconds(etaSec))} left";
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
                    _trayIcon.ShowBalloonTip(2500, "Tempo",
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

            if (_historySummaryLabel != null)
            {
                _historySummaryLabel.Text = shownCount == 0
                    ? "No sessions to show."
                    : $"{shownCount:N0} session(s) shown  •  {shownClicks:N0} clicks total";
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
            _historyProfileFilter.Items.Add("All profiles");
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
            _dailyBarChart.Title = weekTotal > 0
                ? string.Format(Utils.Localization.T("Last 7 days · {0} clicks · peak {1}"), weekTotal.ToString("N0"), peakDay.ToString("N0"))
                : "Clicks — last 7 days";
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

            // Lifetime average CPS = total clicks / total active seconds.
            if (_cardLifeAvgCps != null)
            {
                _cardLifeAvgCps.Value = lifetimeRuntimeSeconds > 0
                    ? ((double)lifetimeClicks / lifetimeRuntimeSeconds).ToString("0.0")
                    : "0.0";
            }

            if (_cardActiveDays != null) _cardActiveDays.Value = byDay.Count.ToString("N0");

            // Best single day.
            if (_cardBestDay != null)
            {
                DateTime bestDay = default;
                long bestDayClicks = -1;
                foreach (var kv in byDay)
                {
                    if (kv.Value > bestDayClicks) { bestDayClicks = kv.Value; bestDay = kv.Key; }
                }
                _cardBestDay.Value = bestDayClicks <= 0
                    ? "—"
                    : $"{bestDayClicks:N0}";
                _cardBestDay.Caption = bestDayClicks <= 0
                    ? Utils.Localization.T("Best Day")
                    : Utils.Localization.T("Best Day") + " · " + bestDay.ToString("d MMM", Utils.Localization.DateCulture);
            }

            // Busiest weekday.
            if (_cardBusiestWeekday != null)
            {
                int bestWd = -1; long bestWdClicks = -1;
                for (int i = 0; i < 7; i++)
                    if (byWeekday[i] > bestWdClicks) { bestWdClicks = byWeekday[i]; bestWd = i; }
                _cardBusiestWeekday.Value = bestWdClicks <= 0
                    ? "—"
                    : Utils.Localization.DateCulture.DateTimeFormat
                        .GetAbbreviatedDayName((DayOfWeek)bestWd);
            }

            // Busiest hour.
            if (_cardBusiestHour != null)
            {
                int bestHr = -1; long bestHrClicks = -1;
                for (int i = 0; i < 24; i++)
                    if (byHour[i] > bestHrClicks) { bestHrClicks = byHour[i]; bestHr = i; }
                _cardBusiestHour.Value = bestHrClicks <= 0
                    ? "—"
                    : FormatHour(bestHr);
            }

            // Top profile by clicks.
            if (_cardTopProfile != null)
            {
                string topProfile = "—"; long topClicks = -1;
                foreach (var kv in byProfile)
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

            // This year.
            if (_cardThisYear != null) _cardThisYear.Value = yearClicks.ToString("N0");

            // Daily average across days that had any activity.
            if (_cardDailyAvg != null)
            {
                long totalRecorded = 0;
                foreach (var kv in byDay) totalRecorded += kv.Value;
                _cardDailyAvg.Value = byDay.Count > 0
                    ? (totalRecorded / (double)byDay.Count).ToString("N0")
                    : "0";
            }

            // Streaks: current run of consecutive active days ending today or
            // yesterday, plus the longest such run ever recorded.
            if (_cardStreak != null || _cardLongestStreak != null)
            {
                int current = 0, longest = 0;
                if (byDay.Count > 0)
                {
                    // Current streak.
                    DateTime cursor = today;
                    if (!byDay.ContainsKey(cursor)) cursor = today.AddDays(-1);
                    while (byDay.ContainsKey(cursor))
                    {
                        current++;
                        cursor = cursor.AddDays(-1);
                    }

                    // Longest streak over the full set of active days.
                    var days = new System.Collections.Generic.List<DateTime>(byDay.Keys);
                    days.Sort();
                    int run = 1;
                    longest = 1;
                    for (int i = 1; i < days.Count; i++)
                    {
                        run = (days[i] - days[i - 1]).Days == 1 ? run + 1 : 1;
                        if (run > longest) longest = run;
                    }
                }

                if (_cardStreak != null)
                {
                    _cardStreak.Value = current.ToString("N0");
                    _cardStreak.Caption = Utils.Localization.T("Current Streak") + " · " + current + " " +
                        Utils.Localization.T(current == 1 ? "day" : "days");
                }
                if (_cardLongestStreak != null)
                {
                    _cardLongestStreak.Value = longest.ToString("N0");
                    _cardLongestStreak.Caption = Utils.Localization.T("Longest Streak") + " · " + longest + " " +
                        Utils.Localization.T(longest == 1 ? "day" : "days");
                }
            }
        }

        private static string FormatHour(int hour24)
        {
            if (hour24 == 0) return "12 AM";
            if (hour24 == 12) return "12 PM";
            return hour24 < 12 ? hour24 + " AM" : (hour24 - 12) + " PM";
        }

        private long ComputeTodayClicks()
        {
            DateTime today = DateTime.Now.Date;
            long sum = 0;
            foreach (SessionRecord r in _history.Records)
            {
                if (r.WhenUtc.ToLocalTime().Date == today)
                {
                    sum += r.Clicks;
                }
            }

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
        }

        private void ApplyThemeToSessionList()
        {
            if (_sessionHistoryList == null)
            {
                return;
            }

            _sessionHistoryList.BackColor = _theme.Surface;
            _sessionHistoryList.ForeColor = _theme.Text;
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
            if (_cardThisWeek != null) sb.AppendLine("This week      : " + _cardThisWeek.Value);
            if (_cardThisMonth != null) sb.AppendLine("This month     : " + _cardThisMonth.Value);
            if (_cardThisYear != null) sb.AppendLine("This year      : " + _cardThisYear.Value);
            if (_cardDailyAvg != null) sb.AppendLine("Daily average  : " + _cardDailyAvg.Value);
            if (_cardStreak != null) sb.AppendLine("Current streak : " + _cardStreak.Value + " day(s)");
            if (_cardLongestStreak != null) sb.AppendLine("Longest streak : " + _cardLongestStreak.Value + " day(s)");
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
                Title = "Export statistics",
                Filter = "CSV file (*.csv)|*.csv|All files (*.*)|*.*",
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

                    // Summary block.
                    sb.AppendLine("Metric,Value");
                    sb.AppendLine($"Session clicks,{_statistics.SessionClicks}");
                    sb.AppendLine($"Total clicks (launch),{_statistics.TotalClicks}");
                    sb.AppendLine($"Left clicks,{_statistics.LeftClicks}");
                    sb.AppendLine($"Right clicks,{_statistics.RightClicks}");
                    sb.AppendLine($"Middle clicks,{_statistics.MiddleClicks}");
                    sb.AppendLine($"Peak CPS,{_statistics.PeakClicksPerSecond:0.0}");
                    sb.AppendLine($"Average CPS,{_statistics.GetAverageCps():0.0}");
                    sb.AppendLine($"Lifetime clicks,{_lifetimeBaseline + _statistics.TotalClicks}");
                    sb.AppendLine($"Lifetime sessions,{_settings.LifetimeSessions}");
                    sb.AppendLine($"Best CPS ever,{_settings.LifetimePeakCps:0.0}");
                    sb.AppendLine($"Total runtime (s),{_settings.LifetimeRuntimeSeconds}");
                    sb.AppendLine($"Most clicks per run,{_settings.LifetimeMostClicksRun}");
                    sb.AppendLine($"Longest run (s),{_settings.LifetimeLongestRunSeconds}");

                    // Insights (mirrors the cards on the dashboard).
                    if (_cardThisWeek != null) sb.AppendLine($"This week,{_cardThisWeek.Value}");
                    if (_cardThisMonth != null) sb.AppendLine($"This month,{_cardThisMonth.Value}");
                    if (_cardThisYear != null) sb.AppendLine($"This year,{_cardThisYear.Value}");
                    if (_cardActiveDays != null) sb.AppendLine($"Active days,{_cardActiveDays.Value}");
                    if (_cardDailyAvg != null) sb.AppendLine($"Daily average,{_cardDailyAvg.Value}");
                    if (_cardStreak != null) sb.AppendLine($"Current streak (days),{_cardStreak.Value}");
                    if (_cardLongestStreak != null) sb.AppendLine($"Longest streak (days),{_cardLongestStreak.Value}");
                    if (_cardBusiestWeekday != null) sb.AppendLine($"Busiest weekday,{_cardBusiestWeekday.Value}");
                    if (_cardBusiestHour != null) sb.AppendLine($"Busiest hour,{_cardBusiestHour.Value}");
                    if (_cardTopProfile != null) sb.AppendLine($"Top profile,{(_cardTopProfile.Value ?? "").Replace(",", " ")}");
                    sb.AppendLine();

                    // Session history block.
                    sb.AppendLine("When,Profile,Clicks,Duration (s),Average CPS,Peak CPS");
                    foreach (SessionRecord r in _history.Records)
                    {
                        string when = r.WhenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                        string profile = (r.Profile ?? "").Replace(",", " ");
                        sb.AppendLine($"{when},{profile},{r.Clicks},{r.DurationSeconds:0.0},{r.AverageCps:0.0},{r.PeakCps:0.0}");
                    }

                    File.WriteAllText(dlg.FileName, sb.ToString());
                    ShowInfo("Statistics exported.");
                }
                catch (Exception ex)
                {
                    ShowWarning("Could not export statistics: " + ex.Message);
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
            _lifetimeBaseline = 0;
            _statistics.ResetAll();
            SettingsManager.Save(_settings);
            _cpsSparkline?.Clear();
            UpdateStatisticsTab();
        }
    }
}
