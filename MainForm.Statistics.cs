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
            var page = new TabPage("Statistics") { AutoScroll = true };

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

            // ── Row 2: rate + timing ──────────────────────────────────────────
            int row2 = graphTop + _cpsSparkline.Height + CardGap;
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

            // ── Charts: per-session (left) and last-7-days (right) ────────────
            int chartY = row5 + CardH + 14;
            int halfW = (CardW * 2) + CardGap;
            _sessionBarChart = new MiniBarChart
            {
                Title = "Clicks per recent session",
                Left = c0,
                Top = chartY,
                Width = halfW,
                Height = 132
            };
            page.Controls.Add(_sessionBarChart);

            _dailyBarChart = new MiniBarChart
            {
                Title = "Clicks — last 7 days",
                Left = c2,
                Top = chartY,
                Width = halfW,
                Height = 132
            };
            page.Controls.Add(_dailyBarChart);

            // ── Recent sessions ───────────────────────────────────────────────
            int histTitleY = chartY + _sessionBarChart.Height + 14;
            page.Controls.Add(UiFactory.Label("Recent sessions (double-click for details, right-click for options)", 16, histTitleY, FontStyle.Bold, 12f));

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

            // ── Action buttons ────────────────────────────────────────────────
            int btnY = histY + _sessionHistoryList.Height + 14;
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

            _tabs.TabPages.Add(page);
            RefreshSessionHistory();
        }

        private StatCard MakeCard(string caption, int x, int y, bool accent)
        {
            return new StatCard
            {
                Caption = caption,
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
                _cardAvgPerSession, _cardAvgRunLength
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

            foreach (var chart in new[] { _sessionBarChart, _dailyBarChart })
            {
                if (chart == null) continue;
                chart.CardColor = _theme.Surface;
                chart.BarColor = _theme.Accent;
                chart.TextColor = _theme.Text;
                chart.MutedColor = _theme.TextMuted;
                chart.Invalidate();
            }

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

            _cardSessionClicks.Value = _statistics.SessionClicks.ToString("N0");
            _cardTotalClicks.Value = _statistics.TotalClicks.ToString("N0");
            _cardCurrentCps.Value = cps.ToString("0.0");
            _cardPeakCps.Value = _statistics.PeakClicksPerSecond.ToString("0.0");

            TimeSpan elapsed = _statistics.GetElapsed();
            _cardElapsed.Value = FormatDuration(elapsed);

            _cardAvgCps.Value = _statistics.GetAverageCps().ToString("0.0");
            _cardClicksPerMin.Value = _statistics.GetClicksPerMinute().ToString("N0");
            _cardLeft.Value = _statistics.LeftClicks.ToString("N0");
            _cardRight.Value = _statistics.RightClicks.ToString("N0");
            _cardMiddle.Value = _statistics.MiddleClicks.ToString("N0");

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

            // Feed the live graph. Only sample while running so the line returns to
            // zero and flattens when idle rather than freezing on the last value.
            _cpsSparkline.Push(_engine.IsRunning ? cps : 0);
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
            _statistics.ResetAll();
            _cpsSparkline?.Clear();
            _distBar?.SetValues(0, 0, 0);
            UpdateStatisticsTab();
        }

        /// <summary>Rebuilds the recent-sessions list from the history store.</summary>
        private void RefreshSessionHistory()
        {
            if (_sessionHistoryList == null)
            {
                return;
            }

            _sessionHistoryList.BeginUpdate();
            _sessionHistoryList.Items.Clear();

            foreach (SessionRecord r in _history.Records)
            {
                DateTime local = r.WhenUtc.ToLocalTime();
                var item = new ListViewItem(local.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture));
                item.SubItems.Add(string.IsNullOrEmpty(r.Profile) ? "—" : r.Profile);
                item.SubItems.Add(r.Clicks.ToString("N0"));
                item.SubItems.Add(FormatDuration(TimeSpan.FromSeconds(r.DurationSeconds)));
                item.SubItems.Add(r.AverageCps.ToString("0.0"));
                item.SubItems.Add(r.PeakCps.ToString("0.0"));
                item.Tag = r;
                _sessionHistoryList.Items.Add(item);
            }

            _sessionHistoryList.EndUpdate();
            ApplyThemeToSessionList();

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
            for (int i = 0; i < 7; i++)
            {
                vals.Add(totals[i]);
                DateTime d = today.AddDays(-(6 - i));
                labels.Add(d.ToString("ddd", CultureInfo.CurrentCulture));
            }
            _dailyBarChart.SetValues(vals, labels);
        }

        /// <summary>Sum of clicks from completed runs dated today, plus the live run.</summary>
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
