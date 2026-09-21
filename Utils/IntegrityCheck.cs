using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace AutoClicker.Utils
{
    /// <summary>What the integrity check concluded about this copy of Tempo.</summary>
    public enum IntegrityVerdict
    {
        /// <summary>Not run yet.</summary>
        Unknown,
        /// <summary>Matches the copy that was installed.</summary>
        Ok,
        /// <summary>First time this build has run — the fingerprint was just recorded.</summary>
        Baselined,
        /// <summary>Could not be checked (file locked, unreadable, or check disabled).</summary>
        Unverified,
        /// <summary>The file is physically damaged — truncated, zeroed, or not an executable.</summary>
        Damaged,
        /// <summary>Same version, different bytes: Tempo.exe was altered after it was installed.</summary>
        Modified,
        /// <summary>Built or packaged by someone else — the identity stamped into it is not Tempo's.</summary>
        Repackaged,
        /// <summary>GitHub confirms this is byte-for-byte the file published for this version.</summary>
        Genuine,
        /// <summary>
        /// The file says of itself that it is a TEST build, not a release.
        ///
        /// A distinct state on purpose, and never a pass. A test build is rebuilt on
        /// every publish, so it cannot match either the copy recorded on this PC or the
        /// release GitHub published for its version number — comparing it to those and
        /// reporting the mismatch produced two ERRORs on every launch saying the file
        /// "was not built or published by the project", which was false, and which taught
        /// anyone reading the log to ignore the one message that would have mattered.
        ///
        /// It does NOT mean the file is trustworthy: nothing off this machine has
        /// vouched for it, and the UI has to keep saying so. It means the question the
        /// other verdicts answer does not apply to this file.
        /// </summary>
        TestBuild,
        /// <summary>
        /// GitHub has never published this version. Not proof of anything on its own —
        /// a build made from source looks exactly like this — but it does mean nothing
        /// off this machine can vouch for the file.
        /// </summary>
        UnknownRelease,
        /// <summary>
        /// Tempo.exe is intact, but the copy the .NET host unpacked from it into TEMP — the files
        /// Tempo actually RUNS — was changed, or holds program files the exe never carried.
        /// Reinstalling the same exe does not fix it (the host reuses that folder); deleting the
        /// folder does, which is what Repair does.
        /// </summary>
        UnpackedModified
    }

    /// <summary>
    /// Checks that the Tempo.exe running right now is the one that was installed, and
    /// says so plainly when it is not.
    ///
    /// HONEST SCOPE — this is tamper-EVIDENT, not tamper-proof, and the distinction
    /// matters. Tempo is unsigned and self-contained, so anyone who can rewrite the exe
    /// can also rewrite this check out of it. What it reliably catches is the damage and
    /// the meddling that does NOT bother to do that:
    ///
    ///   • a half-written or zeroed exe left by a crash, a power cut or a failed update
    ///     — Tempo has already shipped one of these (2026-08-31: the installed exe kept
    ///     its exact 105,973,211-byte length with 43% of it zeroes and the speech
    ///     payload gone, so every size-based check passed while the file was dead);
    ///   • a patched or "cracked" Tempo.exe dropped over a real install;
    ///   • a repackaged build from a download site that is not the official one;
    ///   • an update that replaced the exe when the user never asked for one.
    ///
    /// It cannot catch a modification made before Tempo ever ran here, because the
    /// fingerprint it compares against is recorded on first run — trust-on-first-use.
    /// <see cref="PublishedHash"/> is the way out of that: when a release publishes its
    /// SHA-256, the reference comes from off the machine entirely.
    /// </summary>
    public static class IntegrityCheck
    {
        /// <summary>The verdict from the last run.</summary>
        public static IntegrityVerdict Verdict { get; private set; } = IntegrityVerdict.Unknown;

        /// <summary>One line for the UI and the log.</summary>
        public static string Summary { get; private set; } = "not run";

        /// <summary>SHA-256 of the running executable, or null when it could not be read.</summary>
        public static string CurrentHash { get; private set; }

        /// <summary>The fingerprint this build was expected to have, or null on a first run.</summary>
        public static string ExpectedHash { get; private set; }

        /// <summary>True when the verdict is one the user should be told about.</summary>
        public static bool IsProblem
        {
            get
            {
                return Verdict == IntegrityVerdict.Modified
                    || Verdict == IntegrityVerdict.Damaged
                    || Verdict == IntegrityVerdict.Repackaged
                    || Verdict == IntegrityVerdict.UnpackedModified
                    // Not proof of tampering by itself — a build from source looks the
                    // same — but it does mean nothing outside this PC vouches for the
                    // file, which is worth saying rather than passing over in silence.
                    || Verdict == IntegrityVerdict.UnknownRelease;
            }
        }

        /// <summary>True once GitHub has confirmed this exact file for this version.</summary>
        public static bool ConfirmedByGitHub { get; private set; }

        /// <summary>What the online check concluded, for the status line.</summary>
        public static string OnlineSummary { get; private set; } = "not checked";

        /// <summary>What comparing the unpacked copy with Tempo.exe found, for Live Debug.</summary>
        public static string PayloadSummary { get; private set; } = "not checked";

        /// <summary>The folder the running Tempo was unpacked into, once it has been compared; else null.</summary>
        public static string UnpackDirectory { get; private set; }

        /// <summary>True when Repair can fix the verdict: the unpacked copy was altered and its folder is known.</summary>
        public static bool IsRepairable =>
            Verdict == IntegrityVerdict.UnpackedModified && !string.IsNullOrEmpty(UnpackDirectory);

        /// <summary>
        /// The Official ID: the first 16 characters of this file's SHA-256, grouped for reading
        /// ("CC21-FB7A-D6F3-8831") — given ONLY once GitHub has confirmed that the whole 64-character
        /// hash is the Tempo.exe it published. Null for a test build, an unverified, modified or
        /// repackaged copy, an altered unpacked copy, or before the check has run.
        ///
        /// A slice of the hash rather than a number of its own, because that is what lets anyone check
        /// it: GitHub lists the SHA-256 of every release file, so these characters can be matched on the
        /// release page — or against Get-FileHash on the user's own Tempo.exe — without trusting Tempo at
        /// all. A number the app printed about itself would prove nothing; a modified copy can print any
        /// ID it likes. Sixteen characters (64 bits) because a short prefix can be forged by generating
        /// files until one happens to start the same way. It is only the label: the check compares all 64.
        /// </summary>
        public static string OfficialId =>
            Verdict == IntegrityVerdict.Genuine && ConfirmedByGitHub ? FormatOfficialId(CurrentHash) : null;

        /// <summary>The release GitHub confirmed this file for (e.g. "v1.0.321"), or null.</summary>
        public static string OfficialTag { get; private set; }

        /// <summary>That release's page on GitHub, where the file's SHA-256 is listed; null without a tag.</summary>
        public static string OfficialReleaseUrl =>
            string.IsNullOrEmpty(OfficialTag)
                ? null
                : "https://github.com/" + UpdateChecker.Repository + "/releases/tag/" + Uri.EscapeDataString(OfficialTag);

        /// <summary>"cc21fb7ad6f38831…" → "CC21-FB7A-D6F3-8831"; null for anything that is not a SHA-256.</summary>
        public static string FormatOfficialId(string sha256)
        {
            if (string.IsNullOrEmpty(sha256) || sha256.Length != 64) { return null; }
            foreach (char c in sha256)
            {
                if (!Uri.IsHexDigit(c)) { return null; }
            }
            string u = sha256.Substring(0, 16).ToUpperInvariant();
            return u.Substring(0, 4) + "-" + u.Substring(4, 4) + "-" + u.Substring(8, 4) + "-" + u.Substring(12, 4);
        }

        /// <summary>The Official ID state as one English line, for crash reports, diagnostics and Live Debug.</summary>
        public static string OfficialIdReportLine()
        {
            string id = OfficialId;
            if (id != null)
            {
                return id + " — verified against GitHub's " + OfficialTag + " release";
            }
            switch (Verdict)
            {
                case IntegrityVerdict.Unknown:
                    return "not checked yet";
                case IntegrityVerdict.TestBuild:
                    return "none — test build " + BuildInfo.Id + ", not a GitHub release";
                case IntegrityVerdict.Modified:
                case IntegrityVerdict.Repackaged:
                case IntegrityVerdict.Damaged:
                case IntegrityVerdict.UnknownRelease:
                case IntegrityVerdict.UnpackedModified:
                    return "none — " + Verdict;
                case IntegrityVerdict.Unverified:
                    return "none — not verified: " + Summary;
                default:
                    return "not verified yet — GitHub: " + OnlineSummary;
            }
        }

        // What a genuine Tempo build stamps into itself. Set from the csproj, so a build
        // packaged by someone else under their own name does not match.
        private const string ExpectedCompany = "Tempo Project";
        private const string ExpectedProduct = "Tempo";

        // The single-file bundle carries the whisper/ggml native libraries inside it. If
        // that payload is gone the file is not a working Tempo, whatever its length says
        // — this is the check that would have caught the 2026-08-31 corruption.
        private static readonly byte[] NativeMarker = Encoding.ASCII.GetBytes("ggml");

        // A real build is overwhelmingly non-zero bytes. The corrupted one was 43% zeroes.
        // The threshold sits far below anything a healthy build produces (measured: ~1.8%)
        // and far above the damage seen, so ordinary variation can never trip it.
        private const double MaxZeroFraction = 0.25;

        /// <summary>
        /// Runs the check off the UI thread and reports back. Never throws, never blocks
        /// start-up: hashing ~106 MB costs about 120 ms warm and 380 ms cold, which is
        /// nothing on a worker and would be visible on the startup path.
        /// </summary>
        public static void RunInBackground(Models.AppSettings settings, Action<IntegrityVerdict> onDone)
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                IntegrityVerdict v;
                try
                {
                    v = Run(settings);

                    // Then ask GitHub, which is the only party that can tell a genuine
                    // build from a convincing one.
                    //
                    // Including when the local pass said Modified. GitHub outranks the
                    // note this PC wrote to itself, and it can CLEAR a false alarm as
                    // well as raise a true one: reinstalling the official exe over a
                    // tampered one leaves a baseline that no longer matches, so the
                    // local check calls the good file "modified". Asking GitHub turns
                    // that into "genuine" instead of leaving the user with a warning
                    // that reinstalling could never fix.
                    //
                    // Skipped for Damaged and Repackaged: a truncated or foreign file
                    // has nothing to compare, and a network round trip would only delay
                    // a verdict that is already certain.
                    if (settings != null && settings.IntegrityCheckEnabled &&
                        !string.IsNullOrEmpty(CurrentHash) &&
                        (v == IntegrityVerdict.Ok || v == IntegrityVerdict.Baselined ||
                         v == IntegrityVerdict.Modified))
                    {
                        v = VerifyAgainstGitHub(settings);
                    }
                }
                catch (Exception ex)
                {
                    // A self-check that takes the app down is worse than the fault it
                    // was looking for.
                    Logger.Swallow("IntegrityCheck", ex);
                    Verdict = IntegrityVerdict.Unverified;
                    Summary = "could not be checked (" + ex.Message + ")";
                    v = Verdict;
                }
                try { onDone?.Invoke(v); } catch (Exception ex) { Logger.Swallow("IntegrityCheck.onDone", ex); }
            });
        }

        /// <summary>
        /// Runs every layer and records the verdict. Safe to call on any thread.
        ///
        /// <paramref name="exePathOverride"/> exists so this can be aimed at a COPY of
        /// the executable and told to find damage that has been put there deliberately.
        /// A tamper check that has never been shown a tampered file is a guess; the
        /// probe uses this to flip a byte, zero a region and strip the native payload
        /// out of a real Tempo.exe and confirm each one is caught. Production passes
        /// null and checks the running process. <paramref name="unpackDirOverride"/> aims the
        /// unpacked-copy comparison at a copied (and deliberately damaged) folder the same way;
        /// with only an exe override that comparison is skipped — a copy of the exe has no
        /// unpacked folder of its own.
        /// </summary>
        public static IntegrityVerdict Run(Models.AppSettings settings, string exePathOverride = null,
                                           string unpackDirOverride = null)
        {
            // A fresh run vouches for nothing until GitHub confirms THIS file again, so an Official ID
            // from an earlier run cannot outlive a file that changed in the meantime.
            ConfirmedByGitHub = false;
            OfficialTag = null;
            OnlineSummary = "not checked";

            if (settings != null && !settings.IntegrityCheckEnabled)
            {
                return Set(IntegrityVerdict.Unverified, "turned off in Settings");
            }

            string exe = exePathOverride;
            if (string.IsNullOrEmpty(exe))
            {
                try { exe = Environment.ProcessPath; } catch { }
            }
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                return Set(IntegrityVerdict.Unverified, "the running program file could not be located");
            }

            // ── 1. Identity: is this even claiming to be Tempo? ─────────────────────
            string who = IdentityMismatch();
            if (who != null)
            {
                return Set(IntegrityVerdict.Repackaged,
                    "this build is not stamped as an official Tempo build (" + who + ")");
            }

            // ── 2 + 3. One pass over the file: hash, zero-density and native payload ─
            Scan scan;
            try
            {
                scan = ScanExecutable(exe);
            }
            catch (Exception ex)
            {
                // Locked or unreadable is NOT evidence of tampering. Crying wolf here
                // would teach the user to ignore the one warning that matters.
                return Set(IntegrityVerdict.Unverified, "the program file could not be read (" + ex.Message + ")");
            }

            CurrentHash = scan.Hash;

            if (!scan.LooksExecutable)
            {
                return Set(IntegrityVerdict.Damaged, "the program file is not a valid Windows executable");
            }
            if (scan.Length > 0 && scan.ZeroFraction > MaxZeroFraction)
            {
                return Set(IntegrityVerdict.Damaged,
                    "the program file is " + Math.Round(scan.ZeroFraction * 100) +
                    "% empty bytes — it is truncated or corrupted");
            }
            if (!scan.HasNativePayload)
            {
                return Set(IntegrityVerdict.Damaged,
                    "the bundled speech libraries are missing from the program file");
            }

            // ── 3b. The code Tempo actually runs: its unpacked copy ────────────────
            // Tempo.exe can be byte-perfect while the files the host unpacked from it — the ones
            // it loads — have been swapped. See BundlePayload for why the host never notices.
            string unpackedProblem = CheckUnpackedCopy(exe, scan, exePathOverride, unpackDirOverride);
            if (unpackedProblem != null)
            {
                return Set(IntegrityVerdict.UnpackedModified, unpackedProblem);
            }

            // ── 4. Fingerprint against what this build looked like when it first ran ─
            string version = CrashReporter.CurrentVersion;
            string baseVersion = settings != null ? settings.IntegrityBaselineVersion : null;
            string baseHash = settings != null ? settings.IntegrityBaselineHash : null;
            ExpectedHash = baseHash;

            if (settings == null)
            {
                return Set(IntegrityVerdict.Unverified, "no settings available to compare against");
            }

            if (string.IsNullOrEmpty(baseHash) || string.IsNullOrEmpty(baseVersion))
            {
                Remember(settings, version, scan);
                return Set(IntegrityVerdict.Baselined,
                    "first run of " + version + " — fingerprint recorded");
            }

            if (!string.Equals(baseVersion, version, StringComparison.OrdinalIgnoreCase))
            {
                // A different version is an update, not an attack. Re-baseline quietly;
                // warning on every legitimate update is how a check gets switched off.
                Remember(settings, version, scan);
                return Set(IntegrityVerdict.Baselined,
                    "updated to " + version + " — fingerprint re-recorded");
            }

            if (!string.Equals(baseHash, scan.Hash, StringComparison.OrdinalIgnoreCase))
            {
                // Name the BUILDS, not just the version.
                //
                // "the file was replaced or edited" is true and nearly useless: the
                // version is unchanged by definition here, so it identifies nothing. The
                // build ID does — and it separates the two cases that look identical
                // otherwise. A DIFFERENT build id is a different build of the same
                // version, which is what swapping a test build over an installed one
                // looks like. The SAME build id with different bytes is the alarming one:
                // those bytes should be identical and are not.
                //
                // The verdict does not change either way. A build id is compiled into the
                // file it describes, so anyone who can edit the bytes can edit the id;
                // treating "the id differs" as innocent would hand them the check. This
                // only makes the message say what actually happened.
                string wasBuild = settings.IntegrityBaselineBuild;
                string nowBuild = BuildInfo.Id;
                string detail;
                if (!string.IsNullOrEmpty(wasBuild) && !string.IsNullOrEmpty(nowBuild))
                {
                    detail = string.Equals(wasBuild, nowBuild, StringComparison.OrdinalIgnoreCase)
                        ? " — same build (" + nowBuild + "), different bytes"
                        : " — was build " + wasBuild + ", now build " + nowBuild;
                }
                else
                {
                    detail = " — the file was replaced or edited";
                }

                // A test build is REBUILT on every publish, so different bytes under the
                // same version is the expected outcome, not evidence of anything. Calling
                // that "Modified" logged an ERROR every launch and spent the alarm on the
                // one case where it can never mean what it says.
                if (BuildInfo.IsTest && !BuildInfo.IsUnstamped)
                {
                    // Only the informative half of the detail. The fallback wording
                    // ("the file was replaced or edited") is both redundant here — being
                    // rebuilt is the whole point of a test build — and exactly the
                    // accusing tone this verdict exists to stop.
                    string which = !string.IsNullOrEmpty(wasBuild) &&
                                   !string.Equals(wasBuild, nowBuild, StringComparison.OrdinalIgnoreCase)
                        ? ", replacing build " + wasBuild
                        : "";
                    return Set(IntegrityVerdict.TestBuild,
                        "this is a test build (" + BuildInfo.Id + ")" + which +
                        ", rebuilt on every publish — nothing outside this PC has vouched for it");
                }

                return Set(IntegrityVerdict.Modified,
                    "Tempo.exe has changed since it was installed, but its version is still "
                    + version + detail);
            }

            return Set(IntegrityVerdict.Ok, "matches the copy installed on " +
                (string.IsNullOrEmpty(settings.IntegrityBaselineUtc) ? "this PC" : settings.IntegrityBaselineUtc));
        }

        /// <summary>
        /// Asks GitHub what the build claiming to be this version should look like, and
        /// compares.
        ///
        /// This is the layer that does not trust the machine it is running on, and it
        /// closes the two holes the local fingerprint cannot:
        ///
        ///   • trust-on-first-use — a copy already modified before Tempo ever started
        ///     here is baselined as good by the local check, but GitHub still knows the
        ///     real digest for that version;
        ///   • a bumped version number — the local check re-baselines on a version
        ///     change, because that is what an update looks like. Someone shipping a
        ///     modified build as "1.0.319" would sail straight through. Against GitHub
        ///     it does not: either that release exists and the bytes disagree, or the
        ///     release was never published at all.
        ///
        /// Uses the digest GitHub publishes for the asset itself, so nothing has to be
        /// downloaded and the release does not need to carry a .sha256 file.
        ///
        /// Only ever tightens the verdict. A network failure leaves the local result
        /// exactly as it was — being offline is not evidence of tampering.
        /// </summary>
        /// <param name="tagOverride">
        /// Test seam, like <c>exePathOverride</c> on <see cref="Run"/>. In production the
        /// tag always comes from the running assembly's own version, so it and the bytes
        /// cannot disagree — which is precisely why a probe cannot check the genuine
        /// case without it: pointing the scan at a downloaded v1.0.316 while the host
        /// assembly says 1.0.318 would compare that file against the wrong release.
        /// For the same reason it sets aside the host's own test-build status: the file
        /// being compared is the one the probe named, not the build that is running it.
        /// </param>
        public static IntegrityVerdict VerifyAgainstGitHub(Models.AppSettings settings,
                                                           string tagOverride = null)
        {
            if (string.IsNullOrEmpty(CurrentHash))
            {
                OnlineSummary = "nothing to compare yet";
                return Verdict;
            }

            // A test build is not a candidate for any release, so there is nothing here
            // to compare it WITH. It carries a real version number because the assembly
            // must, but the file GitHub published under that tag is a different build by
            // definition — so this used to fetch that release, find the obvious
            // mismatch, and report "it was not built or published by the project" as an
            // ERROR on every single launch. That is false about a build the project made
            // ten minutes ago, and a check that cries wolf on its own developer's routine
            // is a check nobody reads when it finally means something.
            //
            // Skipped rather than softened, and the request is not made at all. The
            // verdict below is not a pass: it says plainly that nothing has vouched for
            // this file, which is the honest answer for a test build.
            //
            // The stamp is compiled into the file it describes, so anyone able to edit
            // the bytes could also set this channel — the same is already true of the
            // build id, and of the check itself. This system is tamper-EVIDENT, not
            // tamper-proof: it tells the truth to a user who has not been attacked, and
            // does not pretend to bind one who has.
            if (string.IsNullOrEmpty(tagOverride) && BuildInfo.IsTest && !BuildInfo.IsUnstamped)
            {
                ConfirmedByGitHub = false;
                OfficialTag = null;
                OnlineSummary = "not compared — this is a test build, not a published release";
                return Set(IntegrityVerdict.TestBuild,
                    "this is a test build (" + BuildInfo.Id +
                    "), so there is no published release to compare it against");
            }

            // Once a given file has been confirmed there is no reason to ask again:
            // the answer cannot change while the bytes do not. One request per distinct
            // build, not one per launch — which also keeps well clear of GitHub's
            // unauthenticated rate limit.
            string version = CrashReporter.CurrentVersion;      // e.g. 1.0.318.0
            string tag = string.IsNullOrEmpty(tagOverride)
                ? TagFor(version)                               // e.g. v1.0.318
                : tagOverride;

            if (settings != null &&
                string.Equals(settings.IntegrityVerifiedHash, CurrentHash, StringComparison.OrdinalIgnoreCase))
            {
                ConfirmedByGitHub = true;
                OfficialTag = tag;
                OnlineSummary = "confirmed against GitHub on " + settings.IntegrityVerifiedUtc;
                return Set(IntegrityVerdict.Genuine,
                    "verified against the release published on GitHub");
            }

            UpdateChecker.GitHubRelease release;
            string error;
            UpdateChecker.ReleaseLookup lookup = UpdateChecker.FetchReleaseByTag(tag, out release, out error);

            if (lookup == UpdateChecker.ReleaseLookup.Unavailable)
            {
                OnlineSummary = "could not reach GitHub (" + (error ?? "unknown") + ")";
                Logger.Warn("[Integrity] " + OnlineSummary + " — keeping the local result.");
                return Verdict;                                  // offline ≠ tampered
            }

            if (lookup == UpdateChecker.ReleaseLookup.NoSuchRelease)
            {
                ConfirmedByGitHub = false;
                OfficialTag = null;
                OnlineSummary = "GitHub has no release " + tag;
                return Set(IntegrityVerdict.UnknownRelease,
                    "no release named " + tag + " exists on GitHub, so nothing off this PC " +
                    "can vouch for this build");
            }

            string published = PublishedExeDigest(release);
            if (published == null)
            {
                OnlineSummary = "release " + tag + " publishes no checksum for Tempo.exe";
                Logger.Warn("[Integrity] " + OnlineSummary + " — keeping the local result.");
                return Verdict;
            }

            if (string.Equals(published, CurrentHash, StringComparison.OrdinalIgnoreCase))
            {
                ConfirmedByGitHub = true;
                OfficialTag = tag;
                if (settings != null)
                {
                    settings.IntegrityVerifiedHash = CurrentHash;
                    settings.IntegrityVerifiedUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'");

                    // Adopt this file as the local baseline as well. GitHub has just
                    // vouched for it, so a stale baseline left over from a previous
                    // (possibly tampered) copy must not keep raising the alarm on every
                    // launch after the user has already put the official file back.
                    settings.IntegrityBaselineVersion = version;
                    settings.IntegrityBaselineHash = CurrentHash;
                    settings.IntegrityBaselineUtc = settings.IntegrityVerifiedUtc;
                    ExpectedHash = CurrentHash;
                }
                OnlineSummary = "matches the " + tag + " release on GitHub";
                return Set(IntegrityVerdict.Genuine,
                    "verified against the " + tag + " release published on GitHub");
            }

            ConfirmedByGitHub = false;
            OfficialTag = null;
            OnlineSummary = "does NOT match the " + tag + " release on GitHub";
            return Set(IntegrityVerdict.Modified,
                "this file claims to be " + version + ", but GitHub's " + tag +
                " release is a different file — it was not built or published by the project");
        }

        /// <summary>
        /// Compares the running Tempo's unpacked copy with Tempo.exe. Returns the problem to report,
        /// or null when it matches — or when it cannot be compared, which is never evidence.
        /// </summary>
        private static string CheckUnpackedCopy(string exe, Scan scan, string exePathOverride, string unpackDirOverride)
        {
            string dir = unpackDirOverride;
            if (string.IsNullOrEmpty(dir))
            {
                if (!string.IsNullOrEmpty(exePathOverride))
                {
                    PayloadSummary = "not compared (checking a copy of the exe)";
                    return null;
                }
                dir = RunningUnpackDirectory(exe);
                if (dir == null)
                {
                    PayloadSummary = "not a single-file run — nothing was unpacked";
                    return null;
                }
            }

            BundlePayload.Manifest manifest;
            try { manifest = BundlePayload.Read(exe, scan.BundleSignatureOffset); }
            catch (Exception ex)
            {
                PayloadSummary = "the bundle manifest could not be read (" + ex.Message + ")";
                Logger.Warn("[Integrity] unpacked copy: " + PayloadSummary);
                return null;
            }
            if (manifest == null)
            {
                PayloadSummary = "Tempo.exe carries no bundle manifest this check understands";
                Logger.Warn("[Integrity] unpacked copy: " + PayloadSummary);
                return null;
            }

            // The host names the folder after the bundle id. Any other name means this is not the
            // folder THIS exe was unpacked into, and comparing it would prove nothing.
            if (string.IsNullOrEmpty(unpackDirOverride) &&
                !string.Equals(Path.GetFileName(dir.TrimEnd('\\', '/')), manifest.BundleId, StringComparison.Ordinal))
            {
                PayloadSummary = "the unpack folder is not named after this bundle (" + manifest.BundleId + "), so it was not compared";
                Logger.Warn("[Integrity] unpacked copy: " + PayloadSummary);
                return null;
            }

            BundlePayload.Report report = BundlePayload.Verify(exe, manifest, dir);
            if (report.Error != null)
            {
                PayloadSummary = "could not be compared (" + report.Error + ")";
                Logger.Warn("[Integrity] unpacked copy: " + PayloadSummary);
                return null;
            }

            UnpackDirectory = dir;
            string cost = (report.Bytes / (1024 * 1024)) + " MB in " + report.Milliseconds + " ms";
            if (!report.Tampered)
            {
                PayloadSummary = report.Checked + " unpacked files match Tempo.exe (" + cost + ")"
                    + (report.Missing.Count > 0
                        ? " · " + report.Missing.Count + " missing, restored on the next start"
                        : "");
                Logger.Info("[Integrity] unpacked copy: " + PayloadSummary + ".");
                return null;
            }

            var found = new List<string>();
            if (report.Changed.Count > 0)
            {
                found.Add(report.Changed.Count + " changed (" + FirstFew(report.Changed) + ")");
            }
            if (report.Unexpected.Count > 0)
            {
                found.Add(report.Unexpected.Count + " program file(s) added that Tempo never shipped ("
                          + FirstFew(report.Unexpected) + ")");
            }
            PayloadSummary = string.Join("; ", found) + " — " + cost;
            return "Tempo.exe is intact, but the unpacked copy it runs from was altered: "
                + string.Join("; ", found) + " — the code running is not the code in Tempo.exe";
        }

        /// <summary>
        /// Where the host unpacked the running bundle: AppContext.BaseDirectory when that differs from
        /// the exe's own folder. Null for a loose (not single-file) build.
        /// </summary>
        private static string RunningUnpackDirectory(string exe)
        {
            try
            {
                string baseDir = (AppContext.BaseDirectory ?? "").TrimEnd('\\', '/');
                string exeDir = (Path.GetDirectoryName(exe) ?? "").TrimEnd('\\', '/');
                if (baseDir.Length == 0 || string.Equals(baseDir, exeDir, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                return Directory.Exists(baseDir) ? baseDir : null;
            }
            catch { return null; }
        }

        private static string FirstFew(List<string> names)
        {
            const int Shown = 3;
            string head = string.Join(", ", names.GetRange(0, Math.Min(Shown, names.Count)));
            return names.Count > Shown ? head + " and " + (names.Count - Shown) + " more" : head;
        }

        /// <summary>"1.0.318.0" → "v1.0.318", the tag shape these releases use.</summary>
        private static string TagFor(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) { return null; }
            string[] parts = version.Split('.');
            if (parts.Length >= 3)
            {
                // Assembly versions carry a fourth component that the tags never do.
                version = parts[0] + "." + parts[1] + "." + parts[2];
            }
            return "v" + version;
        }

        /// <summary>
        /// The SHA-256 GitHub holds for the release's bare Tempo.exe asset.
        ///
        /// Prefers the exe over the zip: the exe is what actually runs, and it is also
        /// what the in-app updater downloads. Falls back to nothing rather than
        /// guessing — a comparison against the wrong asset would produce a confident,
        /// wrong "modified".
        /// </summary>
        private static string PublishedExeDigest(UpdateChecker.GitHubRelease release)
        {
            if (release == null || release.assets == null) { return null; }
            foreach (UpdateChecker.GitHubAsset a in release.assets)
            {
                if (a == null || string.IsNullOrEmpty(a.name) || string.IsNullOrEmpty(a.digest))
                {
                    continue;
                }
                if (!a.name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { continue; }

                string d = a.digest.Trim();
                const string prefix = "sha256:";
                if (d.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return d.Substring(prefix.Length);
                }
            }
            return null;
        }

        /// <summary>Forgets the stored fingerprint so the next run records a fresh one.</summary>
        public static void ResetBaseline(Models.AppSettings settings)
        {
            if (settings == null) { return; }
            settings.IntegrityBaselineHash = "";
            settings.IntegrityBaselineVersion = "";
            settings.IntegrityBaselineSize = 0;
            settings.IntegrityBaselineUtc = "";
        }

        private static void Remember(Models.AppSettings settings, string version, Scan scan)
        {
            settings.IntegrityBaselineVersion = version;
            settings.IntegrityBaselineHash = scan.Hash;
            settings.IntegrityBaselineSize = scan.Length;
            settings.IntegrityBaselineUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'");
            settings.IntegrityBaselineBuild = BuildInfo.Id ?? "";
            ExpectedHash = scan.Hash;
        }

        private static IntegrityVerdict Set(IntegrityVerdict v, string summary)
        {
            Verdict = v;
            Summary = summary;
            if (v == IntegrityVerdict.Modified || v == IntegrityVerdict.Repackaged ||
                v == IntegrityVerdict.UnpackedModified)
            {
                Logger.Error("[Integrity] " + summary);
            }
            else if (v == IntegrityVerdict.Damaged)
            {
                Logger.Error("[Integrity] " + summary);
            }
            else if (v == IntegrityVerdict.Unverified || v == IntegrityVerdict.TestBuild)
            {
                // Warn, not Error: worth seeing in the log, but it is a statement about
                // what this build IS, not a report of something wrong with it.
                Logger.Warn("[Integrity] " + summary);
            }
            else
            {
                Logger.Info("[Integrity] " + summary);
            }
            return v;
        }

        /// <summary>
        /// Checks the identity compiled into the running assembly. Catches a build
        /// packaged under someone else's name; it does NOT catch a byte-patched copy,
        /// which is what the fingerprint is for.
        /// </summary>
        private static string IdentityMismatch()
        {
            try
            {
                Assembly a = Assembly.GetExecutingAssembly();
                string company = a.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;
                string product = a.GetCustomAttribute<AssemblyProductAttribute>()?.Product;

                if (!string.IsNullOrEmpty(company) &&
                    !string.Equals(company, ExpectedCompany, StringComparison.Ordinal))
                {
                    return "company reads \"" + company + "\"";
                }
                if (!string.IsNullOrEmpty(product) &&
                    !string.Equals(product, ExpectedProduct, StringComparison.Ordinal))
                {
                    return "product reads \"" + product + "\"";
                }
            }
            catch
            {
                // Unreadable metadata is not proof of anything.
            }
            return null;
        }

        private sealed class Scan
        {
            public string Hash;
            public long Length;
            public long ZeroBytes;
            public bool HasNativePayload;
            public bool LooksExecutable;
            /// <summary>Where the single-file bundle marker sits, or -1 when there is none.</summary>
            public long BundleSignatureOffset = -1;
            public double ZeroFraction { get { return Length <= 0 ? 0 : (double)ZeroBytes / Length; } }
        }

        /// <summary>
        /// Reads the executable ONCE and answers every question at the same time: its
        /// SHA-256, how much of it is zero bytes, whether the native payload is still in
        /// there, and whether it starts like a Windows executable at all.
        ///
        /// One pass rather than four. At ~106 MB the read dominates the cost, so doing
        /// these separately would have cost four times as much for the same answers.
        /// </summary>
        private static Scan ScanExecutable(string path)
        {
            var scan = new Scan();
            byte[] buffer = new byte[1 << 20];        // 1 MB
            // Marker matches can straddle a chunk boundary, so the tail of each chunk is
            // carried into the next one before searching.
            int carry = NativeMarker.Length - 1;
            byte[] window = new byte[buffer.Length + carry];
            int carried = 0;
            // The bundle marker the same way, so the manifest is found without a second pass.
            byte[] bundleMark = BundlePayload.Signature;
            int markCarry = bundleMark.Length - 1;
            byte[] markWindow = new byte[buffer.Length + markCarry];
            int markCarried = 0;

            using (var sha = SHA256.Create())
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                int read;
                bool first = true;
                while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (first)
                    {
                        first = false;
                        scan.LooksExecutable = read >= 2 && buffer[0] == (byte)'M' && buffer[1] == (byte)'Z';
                    }

                    sha.TransformBlock(buffer, 0, read, null, 0);

                    for (int i = 0; i < read; i++)
                    {
                        if (buffer[i] == 0) { scan.ZeroBytes++; }
                    }

                    if (!scan.HasNativePayload)
                    {
                        Buffer.BlockCopy(buffer, 0, window, carried, read);
                        int len = carried + read;
                        if (IndexOf(window, len, NativeMarker) >= 0) { scan.HasNativePayload = true; }
                        carried = Math.Min(carry, len);
                        if (carried > 0) { Buffer.BlockCopy(window, len - carried, window, 0, carried); }
                    }

                    if (scan.BundleSignatureOffset < 0)
                    {
                        Buffer.BlockCopy(buffer, 0, markWindow, markCarried, read);
                        int len = markCarried + read;
                        int at = IndexOf(markWindow, len, bundleMark);
                        if (at >= 0) { scan.BundleSignatureOffset = scan.Length - markCarried + at; }
                        markCarried = Math.Min(markCarry, len);
                        if (markCarried > 0) { Buffer.BlockCopy(markWindow, len - markCarried, markWindow, 0, markCarried); }
                    }

                    scan.Length += read;
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

                var sb = new StringBuilder(64);
                foreach (byte b in sha.Hash) { sb.Append(b.ToString("x2")); }
                scan.Hash = sb.ToString();
            }
            return scan;
        }

        private static int IndexOf(byte[] haystack, int length, byte[] needle)
        {
            int last = length - needle.Length;
            for (int i = 0; i <= last; i++)
            {
                int j = 0;
                while (j < needle.Length && haystack[i + j] == needle[j]) { j++; }
                if (j == needle.Length) { return i; }
            }
            return -1;
        }
    }
}
