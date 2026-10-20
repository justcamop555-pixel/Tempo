using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// Settings › Data &amp; Backup › Details: everything the tamper check found, layer by layer — the file, its
    /// SHA-256 and Official ID, where it came from, and what each of the six checks concluded and why.
    ///
    /// Why it exists: the Settings line is one sentence. That is the right answer to "is this Tempo all right?"
    /// and no answer at all to "what exactly did it check?" or "why does it say that?" — a user told their
    /// copy failed had nothing to show anyone, and one told it passed could not see what passing meant.
    /// Every fact comes from one run's snapshot (<see cref="IntegrityCheck.LastReport"/>), so the window never
    /// mixes two runs.
    ///
    /// Built in 100% units and scaled once at the end (UiFactory.ScaleToScreen), text measured in the same
    /// units. "Check again" closes the window and Settings opens it afresh when the check is done: re-laying
    /// out a scaled window by hand is how the other dialogs grew their 150% bugs.
    /// </summary>
    public sealed class IntegrityDetailsForm : Form
    {
        /// <summary>What the user asked for when the window closed.</summary>
        public enum Outcome { Closed, CheckAgain, ShowFiles }

        public Outcome Result { get; private set; } = Outcome.Closed;

        private const int ClientW = 640;
        private const int Pad = 22;
        private const int GlyphW = 20;

        private readonly Theme _t;
        private readonly Panel _body;
        private readonly Font _valueFont = new Font("Segoe UI", 9f);
        private readonly Font _nameFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        private readonly Font _smallFont = new Font("Segoe UI", 8.25f);
        private readonly Font _headFont = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        private readonly Font _sectionFont = new Font("Segoe UI", 8.25f, FontStyle.Bold);
        private readonly Font _glyphFont = new Font("Segoe UI Symbol", 9.75f, FontStyle.Bold);
        private readonly Font _hashFont = new Font("Consolas", 9f);
        private int _nameW = 96;

        public IntegrityDetailsForm(Theme theme, IntegrityReport report, string headline, Color headlineColour,
                                    bool checkEnabled)
        {
            _t = theme ?? Theme.ForKind(Models.ThemeKind.Dark);
            Text = Localization.T("Tamper check details");
            FormBorderStyle = FormBorderStyle.FixedSingle;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;
            BackColor = _t.Background;
            ForeColor = _t.Text;
            Font = _valueFont;
            ThemeManager.ApplyWindowChrome(this, _t);

            _body = new Panel { Left = 0, Top = 0, Width = ClientW, BackColor = _t.Background };
            Controls.Add(_body);

            // The name column as wide as its longest name in this language.
            foreach (string n in new[] { "File", "Size", "SHA-256", "Official ID", "Version", "Signature",
                                         "Downloaded", "Identity", "Program file", "Unpacked copy",
                                         "Beside the exe", "This PC's record", "GitHub" })
            {
                _nameW = Math.Max(_nameW, UiFactory.Measure96(Localization.T(n), _nameFont).Width + 4);
            }
            _nameW = Math.Min(_nameW, 190);

            int y = 18;
            y = AddText(headline, Pad, y, ClientW - 2 * Pad, _headFont, headlineColour) + 2;
            y = AddText(WhenLine(report), Pad, y, ClientW - 2 * Pad, _smallFont, _t.TextMuted) + 12;

            if (report == null || !checkEnabled)
            {
                y = AddText(Localization.T(checkEnabled
                        ? "The check has not run yet. Press Check again to run it now."
                        : "The tamper check is turned off, so nothing was checked. Turn it on in Settings › Data & Backup."),
                    Pad, y, ClientW - 2 * Pad, _valueFont, _t.Text) + 10;
            }
            else
            {
                y = AddSection("This file", y);
                y = AddRow(null, "File", report.ExePath ?? "—", y);
                y = AddRow(null, "Size", SizeLine(report), y);
                y = AddRow(null, "SHA-256", SpacedHash(report.Sha256), y, _hashFont);
                y = AddRow(null, "Official ID", OfficialIdLine(report), y);
                y = AddRow(null, "Version", VersionLine(report), y);
                y = AddRow(report.HasSignature ? IntegrityLayerState.Warning : (IntegrityLayerState?)null,
                           "Signature", SignatureLine(report), y);
                y = AddRow(report.DownloadedFrom != null && !report.DownloadedFromOfficial ? IntegrityLayerState.Warning : (IntegrityLayerState?)null,
                           "Downloaded", DownloadLine(report), y);

                y = AddSection("Checks", y + 6);
                y = AddRow(report.Identity, "Identity", IdentityLine(report), y);
                y = AddRow(report.Structure, "Program file", StructureLine(report), y);
                y = AddRow(report.Unpacked, "Unpacked copy", UnpackedLine(report), y);
                y = AddRow(report.Beside, "Beside the exe", BesideLine(report), y);
                y = AddRow(report.Local, "This PC's record", LocalLine(report), y);
                y = AddRow(report.Online, "GitHub", OnlineLine(report), y);
            }

            // As tall as the content, unless that is taller than the screen: then the rows scroll and the
            // buttons stay put. A 1366×768 laptop at 125% has about 580 of these units of desk.
            int bodyH = y + 6;
            int maxBody = 560;
            try
            {
                Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
                maxBody = Math.Max(240, (int)(wa.Height / UiFactory.BuildScale) - 60 - 90);
            }
            catch { }
            if (bodyH > maxBody)
            {
                _body.AutoScroll = true;
                _body.Height = maxBody;
            }
            else
            {
                _body.Height = bodyH;
            }

            // ── buttons ──
            int by = _body.Bottom + 12;
            var close = UiFactory.PrimaryButton("Close", 0, by, 96, 30, _t);
            UiFactory.FitToText96(close, 96);
            close.Left = ClientW - Pad - close.Width;
            close.Click += (s, e) => { Result = Outcome.Closed; Close(); };
            Controls.Add(close);
            AcceptButton = close;
            CancelButton = close;

            int x = Pad;
            Button copy = SecondaryButton("Copy details", x, by);
            string copyText = Localization.T("Copy details");
            var copiedTimer = new Timer { Interval = 1500 };
            copiedTimer.Tick += (s, e) => { copiedTimer.Stop(); copy.Text = copyText; };
            Disposed += (s, e) => copiedTimer.Dispose();
            copy.Click += (s, e) =>
            {
                try { Clipboard.SetText(BuildReportText(report)); } catch { return; }   // the clipboard may be held by another app
                copy.Text = Localization.T("Copied ✓");
                copiedTimer.Stop();
                copiedTimer.Start();
            };
            x = copy.Right + 8;

            Button again = SecondaryButton("Check again", x, by);
            again.Enabled = checkEnabled;
            again.Click += (s, e) => { Result = Outcome.CheckAgain; Close(); };
            x = again.Right + 8;

            string releaseUrl = IntegrityCheck.OfficialReleaseUrl;
            if (report != null && report.Verdict == IntegrityVerdict.Genuine && !string.IsNullOrEmpty(releaseUrl))
            {
                Button page = SecondaryButton("Open release page", x, by);
                page.Click += (s, e) =>
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = releaseUrl, UseShellExecute = true }); }
                    catch (Exception ex) { Logger.Warn("[Integrity] could not open the release page: " + ex.Message); }
                };
                x = page.Right + 8;
            }
            if (report != null && report.LoadedBeside.Count > 0)
            {
                Button files = SecondaryButton("Show files", x, by);
                files.Click += (s, e) => { Result = Outcome.ShowFiles; Close(); };
                x = files.Right + 8;
            }

            ClientSize = new Size(ClientW, by + 30 + 16);
            UiFactory.ScaleToScreen(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _valueFont.Dispose();
                _nameFont.Dispose();
                _smallFont.Dispose();
                _headFont.Dispose();
                _sectionFont.Dispose();
                _glyphFont.Dispose();
                _hashFont.Dispose();
            }
            base.Dispose(disposing);
        }

        // ── building blocks ──────────────────────────────────────────────────────────

        private Button SecondaryButton(string key, int x, int y)
        {
            Button b = UiFactory.Button(key, x, y, 110, 30);
            b.BackColor = _t.Surface2;
            b.ForeColor = _t.Text;
            UiFactory.FitToText96(b, 90);
            Controls.Add(b);
            return b;
        }

        /// <summary>A wrapped label in the body; returns its bottom.</summary>
        private int AddText(string text, int x, int y, int width, Font font, Color colour)
        {
            var l = new Label
            {
                Text = text ?? "",
                Left = x,
                Top = y,
                Width = width,
                Font = font,
                ForeColor = colour,
                BackColor = Color.Transparent,
                UseMnemonic = false,
            };
            l.Height = UiFactory.Measure96(l.Text, font, width, TextFormatFlags.NoPrefix).Height + 2;
            _body.Controls.Add(l);
            return l.Bottom;
        }

        private int AddSection(string key, int y)
        {
            int bottom = AddText(Localization.T(key).ToUpper(CultureInfo.CurrentCulture), Pad, y, ClientW - 2 * Pad,
                                 _sectionFont, _t.TextMuted);
            var rule = new Panel { Left = Pad, Top = bottom + 2, Width = ClientW - 2 * Pad, Height = 1, BackColor = _t.Surface2 };
            _body.Controls.Add(rule);
            return rule.Bottom + 8;
        }

        /// <summary>
        /// A named fact, or — with <paramref name="state"/> — one check with its ✓ ✗ ⚠ –. Returns the y for the next.
        /// </summary>
        private int AddRow(IntegrityLayerState? state, string nameKey, string value, int y, Font valueFont = null)
        {
            int nameLeft = Pad + GlyphW;
            if (state.HasValue)
            {
                var glyph = new Label
                {
                    Text = Glyph(state.Value),
                    Left = Pad,
                    Top = y - 1,
                    Width = GlyphW,
                    Height = 20,
                    Font = _glyphFont,
                    ForeColor = GlyphColour(state.Value),
                    BackColor = Color.Transparent,
                    UseMnemonic = false,
                };
                _body.Controls.Add(glyph);
            }
            var name = new Label
            {
                Text = Localization.T(nameKey),
                Left = nameLeft,
                Top = y,
                Width = _nameW,
                Height = 18,
                Font = _nameFont,
                ForeColor = _t.TextMuted,
                BackColor = Color.Transparent,
                AutoEllipsis = true,
                UseMnemonic = false,
            };
            _body.Controls.Add(name);

            int left = nameLeft + _nameW + 8;
            int bottom = AddText(value, left, y, ClientW - Pad - left, valueFont ?? _valueFont,
                                 state == IntegrityLayerState.Failed ? Theme.Readable(_t.Danger, _t.Background, 4.5) : _t.Text);
            return Math.Max(y + 22, bottom + 7);
        }

        private static string Glyph(IntegrityLayerState s)
        {
            switch (s)
            {
                case IntegrityLayerState.Passed: return "✓";
                case IntegrityLayerState.Failed: return "✗";
                case IntegrityLayerState.Warning: return "⚠";
                default: return "–";
            }
        }

        private Color GlyphColour(IntegrityLayerState s)
        {
            switch (s)
            {
                case IntegrityLayerState.Passed: return Theme.Readable(_t.Success, _t.Background, 3.0);
                case IntegrityLayerState.Failed: return Theme.Readable(_t.Danger, _t.Background, 3.0);
                case IntegrityLayerState.Warning: return Theme.Readable(_t.Warning, _t.Background, 3.0);
                default: return _t.TextMuted;
            }
        }

        // ── the words ────────────────────────────────────────────────────────────────

        private static string ExeName(IntegrityReport r)
        {
            string n = null;
            try { n = Path.GetFileName(r?.ExePath); } catch { }
            return string.IsNullOrEmpty(n) ? DeploymentInfo.InstalledExeName : n;
        }

        internal static string Elapsed(long ms) =>
            ms < 1000 ? Localization.F("{0} ms", ms) : Localization.F("{0} s", (ms / 1000.0).ToString("0.0", CultureInfo.CurrentCulture));

        private static string Local(DateTime utc) => utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        private static string WhenLine(IntegrityReport r)
        {
            if (r == null || r.CheckedUtc == default) { return Localization.T("Not checked yet."); }
            long ms = r.LocalMs + r.OnlineMs;
            return Localization.F("Last checked {0} · took {1}", Local(r.CheckedUtc), Elapsed(ms));
        }

        private static string SizeLine(IntegrityReport r)
        {
            if (r.Length < 0) { return "—"; }
            string size = Localization.F("{0} bytes ({1} MB)", r.Length.ToString("N0", CultureInfo.CurrentCulture),
                                         (r.Length / 1048576.0).ToString("0.0", CultureInfo.CurrentCulture));
            return r.WrittenUtc.HasValue ? size + " · " + Localization.F("changed {0}", Local(r.WrittenUtc.Value)) : size;
        }

        private static string SpacedHash(string sha)
        {
            if (string.IsNullOrEmpty(sha)) { return "—"; }
            return sha.Length == 64 ? sha.Substring(0, 32) + " " + sha.Substring(32) : sha;
        }

        private static string OfficialIdLine(IntegrityReport r)
        {
            string id = r.Verdict == IntegrityVerdict.Genuine ? IntegrityCheck.FormatOfficialId(r.Sha256) : null;
            if (id != null && IntegrityCheck.ConfirmedByGitHub)
            {
                return Localization.F("{0} — matches GitHub's {1} release. Anyone can check it: it is the start of the SHA-256 GitHub lists for Tempo.exe.",
                                      id, r.OnlineTag ?? "");
            }
            switch (r.Verdict)
            {
                case IntegrityVerdict.TestBuild: return Localization.T("None — this is a test build.");
                case IntegrityVerdict.Modified:
                case IntegrityVerdict.Repackaged:
                case IntegrityVerdict.Damaged:
                case IntegrityVerdict.UnknownRelease:
                case IntegrityVerdict.UnpackedModified:
                case IntegrityVerdict.ForeignCode:
                    return Localization.T("None — this copy failed its check.");
                case IntegrityVerdict.Unverified: return Localization.T("Not checked.");
                default: return Localization.T("Not yet — GitHub has not confirmed this file.");
            }
        }

        private static string VersionLine(IntegrityReport r)
        {
            string line = Localization.F("{0} · build {1}", r.Version ?? "?", string.IsNullOrEmpty(r.BuildId) ? "?" : r.BuildId);
            return line + " · " + Localization.T(r.IsTestBuild ? "test build" : "release build");
        }

        private static string SignatureLine(IntegrityReport r)
        {
            if (!r.HasSignature)
            {
                return Localization.T("Not signed. Tempo's releases are not code-signed, which is why Windows may say \"Unknown publisher\".");
            }
            return Localization.F("Signed by {0}. Tempo's releases are not signed, so someone signed this copy themselves.",
                                  string.IsNullOrEmpty(r.SignedBy) ? "?" : r.SignedBy);
        }

        private static string DownloadLine(IntegrityReport r)
        {
            if (r.DownloadedFrom == null)
            {
                return Localization.T("No download mark — installed or updated by Tempo itself, or copied here.");
            }
            return r.DownloadedFromOfficial
                ? Localization.F("From {0} — one of Tempo's official sites.", r.DownloadedFrom)
                : Localization.F("From {0} — not one of Tempo's sites. Get Tempo from its GitHub releases page.", r.DownloadedFrom);
        }

        private static string IdentityLine(IntegrityReport r)
        {
            switch (r.Identity)
            {
                case IntegrityLayerState.Passed: return Localization.T("Stamped as an official Tempo build.");
                case IntegrityLayerState.Failed:
                    // The name it carries instead is in "Copy details" (English diagnostic text, like the log's).
                    return Localization.T("Not stamped as an official Tempo build.");
                default: return Localization.T("Not checked.");
            }
        }

        private static string StructureLine(IntegrityReport r)
        {
            switch (r.Structure)
            {
                case IntegrityLayerState.Passed:
                    return Localization.F("A valid Windows program · {0}% empty bytes · the speech libraries are inside.",
                                          (r.ZeroFraction * 100).ToString("0.0", CultureInfo.CurrentCulture));
                case IntegrityLayerState.Failed:
                    if (!r.LooksExecutable) { return Localization.T("Not a valid Windows program."); }
                    if (!r.HasNativePayload && r.ZeroFraction <= 0.25) { return Localization.T("The speech libraries it carries are missing."); }
                    return Localization.F("{0}% of it is empty bytes — it is truncated or corrupted.",
                                          Math.Round(r.ZeroFraction * 100).ToString(CultureInfo.CurrentCulture));
                case IntegrityLayerState.Warning:
                    return Localization.T("Could not be read right now.");
                default: return Localization.T("Not checked.");
            }
        }

        private static string FirstFew(IReadOnlyList<string> names, int shown = 4)
        {
            var head = new List<string>();
            for (int i = 0; i < names.Count && i < shown; i++) { head.Add(names[i]); }
            string s = string.Join(", ", head);
            return names.Count > shown ? s + " " + Localization.F("and {0} more", names.Count - shown) : s;
        }

        private static string UnpackedLine(IntegrityReport r)
        {
            string text;
            switch (r.Unpacked)
            {
                case IntegrityLayerState.Passed:
                    text = Localization.F("{0} files match {1} ({2} MB, {3}).", r.UnpackedChecked, ExeName(r),
                                          r.UnpackedBytes / 1048576, Elapsed(r.UnpackedMs));
                    if (r.UnpackedMissing.Count > 0)
                    {
                        text += " " + Localization.F("{0} missing — Windows' .NET host puts them back on the next start.", r.UnpackedMissing.Count);
                    }
                    break;
                case IntegrityLayerState.Failed:
                    var parts = new List<string>();
                    if (r.UnpackedChanged.Count > 0) { parts.Add(Localization.F("changed: {0}", FirstFew(r.UnpackedChanged))); }
                    if (r.UnpackedAdded.Count > 0) { parts.Add(Localization.F("added, never shipped: {0}", FirstFew(r.UnpackedAdded))); }
                    text = Localization.F("Altered — {0}. Press Repair in Settings to unpack a clean copy.", string.Join("; ", parts));
                    break;
                case IntegrityLayerState.Warning:
                    switch (r.UnpackedNote)
                    {
                        case IntegrityUnpackedNote.FolderNotThisBundle:
                            text = Localization.T("The unpack folder is not this build's, so it was not compared.");
                            break;
                        case IntegrityUnpackedNote.CompareFailed:
                            text = Localization.T("Could not be compared right now.");
                            break;
                        default:
                            text = Localization.T("Could not read the list of files inside the program.");
                            break;
                    }
                    break;
                case IntegrityLayerState.Skipped:
                    text = r.UnpackedNote == IntegrityUnpackedNote.NotSingleFile
                        ? Localization.T("Not a single-file build — nothing was unpacked.")
                        : Localization.T("Not compared.");
                    break;
                default:
                    text = Localization.T("Not checked.");
                    break;
            }
            if (!string.IsNullOrEmpty(r.UnpackDirectory))
            {
                text += "\n" + Localization.F("Folder: {0}", r.UnpackDirectory);
            }
            return text;
        }

        private static string BesideLine(IntegrityReport r)
        {
            switch (r.Beside)
            {
                case IntegrityLayerState.Passed:
                    return Localization.T("Nothing was loaded into Tempo from its own folder.");
                case IntegrityLayerState.Failed:
                    var names = new List<string>();
                    foreach (string p in r.LoadedBeside) { names.Add(p); }
                    return Localization.F("Windows loaded these into Tempo from its folder: {0}. Tempo never puts files there — if you didn't, delete them and restart Tempo.",
                                          string.Join(", ", names));
                case IntegrityLayerState.Warning:
                    return Localization.F("Nothing loaded, but these are not Tempo's: {0}", FirstFew(r.StrayBeside, 8));
                case IntegrityLayerState.Skipped:
                    return Localization.T("Not checked for this kind of build.");
                default:
                    return Localization.T("Not checked.");
            }
        }

        private static string LocalLine(IntegrityReport r)
        {
            switch (r.LocalOutcome)
            {
                case IntegrityLocalOutcome.Matches:
                    return Localization.F("Matches the fingerprint this PC recorded on {0}.", Or(r.BaselineRecorded));
                case IntegrityLocalOutcome.Recorded:
                    return Localization.T("First run of this version here — its fingerprint was recorded now.");
                case IntegrityLocalOutcome.RecordedForUpdate:
                    return Localization.F("Updated from {0} — the fingerprint was recorded again.", Or(r.BaselineVersion));
                case IntegrityLocalOutcome.Differs:
                    return Localization.F("Not the file recorded on {0}, though the version number is the same.", Or(r.BaselineRecorded));
                case IntegrityLocalOutcome.TestBuildRebuilt:
                    return Localization.T("A test build is rebuilt on every publish, so its bytes change under the same version.");
                default:
                    return Localization.T("Not compared.");
            }
        }

        private static string OnlineLine(IntegrityReport r)
        {
            string tag = Or(r.OnlineTag);
            switch (r.OnlineOutcome)
            {
                case IntegrityOnlineOutcome.Confirmed:
                    return Localization.F("Matches the {0} release published on GitHub.", tag);
                case IntegrityOnlineOutcome.ConfirmedEarlier:
                    return Localization.F("Matches the {0} release (confirmed on {1} — the same file is not asked about twice).",
                                          tag, Or(r.OnlineConfirmed));
                case IntegrityOnlineOutcome.TestBuildSkipped:
                    return Localization.T("Not asked — a test build is not a published release.");
                case IntegrityOnlineOutcome.NoSuchRelease:
                    return Localization.F("GitHub has no release {0}.", tag);
                case IntegrityOnlineOutcome.NoChecksum:
                    return Localization.F("The {0} release lists no checksum for Tempo.exe.", tag);
                case IntegrityOnlineOutcome.Mismatch:
                    return Localization.F("Does NOT match the {0} release published on GitHub.", tag);
                case IntegrityOnlineOutcome.Unreachable:
                    // Not the error itself: it is .NET's English text ("The remote name could not be resolved…"), which
                    // read as English inside every translated sentence. "Copy details" carries it, for bug reports.
                    string line = Localization.T("Could not reach GitHub. Tempo asks again by itself.");
                    return r.OnlineRetryAtUtc.HasValue ? line + " " + Localization.F("Not before {0}.", Local(r.OnlineRetryAtUtc.Value)) : line;
                default:
                    return Localization.T("Not asked.");
            }
        }

        private static string Or(string s) => string.IsNullOrEmpty(s) ? "?" : s;

        /// <summary>
        /// The whole report as plain English text, for "Copy details" — English like the About box's copy,
        /// because it goes into bug reports. Never throws.
        /// </summary>
        internal static string BuildReportText(IntegrityReport r)
        {
            var sb = new StringBuilder();
            try
            {
                sb.AppendLine("Tempo tamper check");
                if (r == null)
                {
                    sb.AppendLine("  not run yet");
                    return sb.ToString();
                }
                sb.AppendLine("  Verdict      : " + r.Verdict + " — " + r.Summary);
                sb.AppendLine("  Checked      : " + r.CheckedUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
                              + "  (local " + r.LocalMs + " ms, GitHub " + r.OnlineMs + " ms)");
                sb.AppendLine("  File         : " + (r.ExePath ?? "?"));
                sb.AppendLine("  Size         : " + (r.Length >= 0 ? r.Length.ToString(CultureInfo.InvariantCulture) + " bytes" : "?")
                              + (r.WrittenUtc.HasValue ? "  · written " + r.WrittenUtc.Value.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) : ""));
                sb.AppendLine("  SHA-256      : " + (r.Sha256 ?? "?"));
                sb.AppendLine("  Official ID  : " + IntegrityCheck.OfficialIdReportLine());
                sb.AppendLine("  Version      : " + (r.Version ?? "?") + "  · build " + (r.BuildId ?? "?") + (r.IsTestBuild ? " (test)" : " (release)"));
                sb.AppendLine("  Signature    : " + (r.HasSignature ? "signed by " + (r.SignedBy ?? "?") : "none"));
                sb.AppendLine("  Downloaded   : " + (r.DownloadedFrom == null ? "no download mark" : r.DownloadedFrom + (r.DownloadedFromOfficial ? " (official)" : " (NOT official)")));
                sb.AppendLine("  1 Identity   : " + r.Identity + (r.IdentityProblem != null ? " — " + r.IdentityProblem : ""));
                sb.AppendLine("  2 File       : " + r.Structure + "  · MZ " + r.LooksExecutable + " · zeroes "
                              + (r.ZeroFraction * 100).ToString("0.0", CultureInfo.InvariantCulture) + "% · speech libraries " + r.HasNativePayload);
                sb.AppendLine("  3 Unpacked   : " + r.Unpacked + (r.UnpackedNote != IntegrityUnpackedNote.None ? " (" + r.UnpackedNote + ")" : "")
                              + "  · " + r.UnpackedChecked + " files, " + (r.UnpackedBytes / 1048576) + " MB, " + r.UnpackedMs + " ms"
                              + (r.UnpackedChanged.Count > 0 ? " · changed: " + string.Join(", ", r.UnpackedChanged) : "")
                              + (r.UnpackedAdded.Count > 0 ? " · added: " + string.Join(", ", r.UnpackedAdded) : "")
                              + (r.UnpackedMissing.Count > 0 ? " · missing: " + r.UnpackedMissing.Count : "")
                              + (r.UnpackedError != null ? " · " + r.UnpackedError : ""));
                if (!string.IsNullOrEmpty(r.UnpackDirectory)) { sb.AppendLine("                 " + r.UnpackDirectory); }
                sb.AppendLine("  4 Beside exe : " + r.Beside
                              + (r.LoadedBeside.Count > 0 ? " · LOADED: " + string.Join(", ", r.LoadedBeside) : "")
                              + (r.StrayBeside.Count > 0 ? " · not loaded: " + string.Join(", ", r.StrayBeside) : ""));
                sb.AppendLine("  5 This PC    : " + r.Local + " (" + r.LocalOutcome + ")  · was " + (r.BaselineVersion ?? "-")
                              + " build " + (r.BaselineBuild ?? "-") + " recorded " + (r.BaselineRecorded ?? "-"));
                sb.AppendLine("  6 GitHub     : " + r.Online + " (" + r.OnlineOutcome + ")  · tag " + (r.OnlineTag ?? "-")
                              + (r.OnlineConfirmed != null ? " · confirmed " + r.OnlineConfirmed : "")
                              + (r.OnlineError != null ? " · " + r.OnlineError : ""));
            }
            catch (Exception ex)
            {
                sb.AppendLine("  (could not describe: " + ex.Message + ")");
            }
            return sb.ToString();
        }
    }
}
