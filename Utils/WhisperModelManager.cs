using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using AutoClicker.Persistence;

namespace AutoClicker.Utils
{
    /// <summary>
    /// One selectable Whisper speech model: its on-disk file name, a friendly
    /// label, the rough download size and a one-line accuracy/speed note, plus the
    /// URL Tempo can fetch it from. Tempo ships/downloads the small "base.en"
    /// model; the larger ones can be added later.
    /// </summary>
    public sealed class WhisperModelInfo
    {
        public string Key { get; }
        public string FileName { get; }
        public string Label { get; }
        public string Note { get; }
        public string DownloadUrl { get; }

        /// <summary>
        /// True for the ".en" models, which only understand English. Multilingual
        /// models (no ".en" in the file name) auto-detect the spoken language, so
        /// they can caption anything.
        /// </summary>
        public bool EnglishOnly => FileName != null &&
            FileName.IndexOf(".en.", StringComparison.OrdinalIgnoreCase) >= 0;

        public WhisperModelInfo(string key, string fileName, string label, string note, string downloadUrl)
        {
            Key = key;
            FileName = fileName;
            Label = label;
            Note = note;
            DownloadUrl = downloadUrl;
        }
    }

    /// <summary>
    /// Knows where Tempo keeps its Whisper speech models and which ones are
    /// installed. Models live in a "models" folder next to Tempo's settings, so
    /// they survive updates and don't need admin rights to add.
    /// </summary>
    public static class WhisperModelManager
    {
        /// <summary>
        /// The models Tempo offers, smallest/fastest first. The ".en" files are
        /// English-only; Large Turbo is multilingual (auto-detects the language) and
        /// is the distilled v3-turbo build \u2014 near large-v3 accuracy at several times
        /// the speed, which is what makes "best accuracy" usable for LIVE captions.
        /// </summary>
        // Every URL and size below was checked against the host, not copied from
        // documentation \u2014 the sizes are the actual Content-Length in MiB.
        //
        // The list used to be English-only for everything except Large Turbo, which
        // quietly made "pick a smaller model" and "caption another language" mutually
        // exclusive: choosing Small to keep pace also forced English, whatever the
        // spoken-language setting said. Each size now comes in both builds, plus the
        // quantised ones \u2014 same hearing, a third of the size and the CPU \u2014 which are
        // the genuinely useful middle of this range and were missing entirely.
        private const string Repo = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";

        public static readonly IReadOnlyList<WhisperModelInfo> Available = new List<WhisperModelInfo>
        {
            new WhisperModelInfo("tiny",   "ggml-tiny.en.bin",  "Tiny (fastest, English)",
                "74 MB \u00b7 instant even on weak PCs; rough accuracy. English only.",
                Repo + "ggml-tiny.en.bin"),
            new WhisperModelInfo("tiny-ml", "ggml-tiny.bin",    "Tiny (fastest, any language)",
                "74 MB \u00b7 the same speed as Tiny, but understands 90+ languages.",
                Repo + "ggml-tiny.bin"),
            new WhisperModelInfo("base",   "ggml-base.en.bin",  "Base (fast, English)",
                "141 MB \u00b7 fast with fair accuracy. English only.",
                Repo + "ggml-base.en.bin"),
            new WhisperModelInfo("base-ml", "ggml-base.bin",    "Base (fast, any language)",
                "141 MB \u00b7 Base speed and accuracy across 90+ languages.",
                Repo + "ggml-base.bin"),
            // Quantised Small: Small's hearing at Base's footprint. The best
            // accuracy-per-megabyte in the whole list and the first thing to try when
            // a bigger model can't hold real time.
            new WhisperModelInfo("small-q5", "ggml-small-q5_1.bin", "Small Compact (any language)",
                "181 MB \u00b7 Small's accuracy at a third of the size and CPU. 90+ languages.",
                Repo + "ggml-small-q5_1.bin"),
            new WhisperModelInfo("small",  "ggml-small.en.bin", "Small (balanced, English)",
                "465 MB \u00b7 better accuracy, a bit more CPU. English only.",
                Repo + "ggml-small.en.bin"),
            new WhisperModelInfo("small-ml", "ggml-small.bin",  "Small (balanced, any language)",
                "465 MB \u00b7 Small accuracy across 90+ languages.",
                Repo + "ggml-small.bin"),
            new WhisperModelInfo("medium-q5", "ggml-medium-q5_0.bin", "Medium Compact (any language)",
                "514 MB \u00b7 Medium's accuracy at a third of the size. 90+ languages.",
                Repo + "ggml-medium-q5_0.bin"),
            // The compressed (5-bit quantised) build of Large Turbo: near-identical
            // hearing at a third of the size and noticeably less CPU per chunk \u2014
            // the pick when full Large Turbo can't hold real time (busy/mid PCs).
            new WhisperModelInfo("large-q5", "ggml-large-v3-turbo-q5_0.bin", "Large Turbo Compact (fast, any language)",
                "547 MB \u00b7 Large Turbo hearing at a third of the size and less CPU \u2014 great when the full one lags. 90+ languages.",
                Repo + "ggml-large-v3-turbo-q5_0.bin"),
            new WhisperModelInfo("large-v3-q5", "ggml-large-v3-q5_0.bin", "Large v3 Compact (accurate, any language)",
                "1.0 GB \u00b7 full Large v3 hearing, compressed. Slower than Turbo but hears more. 90+ languages.",
                Repo + "ggml-large-v3-q5_0.bin"),
            new WhisperModelInfo("medium", "ggml-medium.en.bin","Medium (accurate, English)",
                "1.4 GB \u00b7 high accuracy, needs a strong CPU/GPU. English only.",
                Repo + "ggml-medium.en.bin"),
            new WhisperModelInfo("medium-ml", "ggml-medium.bin", "Medium (accurate, any language)",
                "1.4 GB \u00b7 Medium accuracy across 90+ languages. Needs a strong CPU/GPU.",
                Repo + "ggml-medium.bin"),
            new WhisperModelInfo("large",  "ggml-large-v3-turbo.bin", "Large Turbo (best all-round, any language)",
                "1.5 GB \u00b7 near Large v3 accuracy at several times the speed \u2014 the pick for live captions. 90+ languages.",
                Repo + "ggml-large-v3-turbo.bin"),
            // The full, undistilled v3. Hears the most of anything here and is far too
            // slow for live captions on a CPU \u2014 it earns its place only on the GPU
            // engine, which is why it sits last rather than being called "best".
            new WhisperModelInfo("large-v3", "ggml-large-v3.bin", "Large v3 (most accurate, GPU recommended)",
                "2.9 GB \u00b7 hears the most, decodes the slowest. Realistic only on the GPU engine. 90+ languages.",
                Repo + "ggml-large-v3.bin"),
        };

        /// <summary>
        /// Every model key from slowest/most accurate to fastest — the order the
        /// too-slow ladder steps DOWN. Not the same as the size order the combo uses:
        /// Large Turbo is distilled, so it decodes faster than Medium despite being the
        /// bigger file, and the full Large v3 is slower than everything.
        ///
        /// One list, because there were four copies of it in MainForm and each had
        /// drifted: they disagreed about whether Medium sat between Large Compact and
        /// Small, and none of them knew about any model added since.
        /// </summary>
        public static readonly IReadOnlyList<string> SpeedOrder = new List<string>
        {
            "large-v3", "medium-ml", "medium", "large-v3-q5", "large",
            "large-q5", "medium-q5", "small-ml", "small", "small-q5",
            "base-ml", "base", "tiny-ml", "tiny",
        };

        /// <summary>Position in <see cref="SpeedOrder"/>, or -1 for an unknown key.</summary>
        public static int IndexInSpeedOrder(string key)
        {
            for (int i = 0; i < SpeedOrder.Count; i++)
            {
                if (string.Equals(SpeedOrder[i], key, StringComparison.OrdinalIgnoreCase)) { return i; }
            }
            return -1;
        }

        /// <summary>True when this model key is an English-only (".en") build.</summary>
        public static bool IsEnglishOnly(string key)
        {
            var m = FindByKey(key);
            return m != null && m.FileName != null &&
                   m.FileName.IndexOf(".en.", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// The next INSTALLED model down the speed ladder from <paramref name="currentKey"/>,
        /// or null when nothing faster is installed.
        ///
        /// English-only builds are skipped when the current model understands any
        /// language. Stepping down to keep pace should not silently stop captioning the
        /// language being listened to — before the multilingual builds existed that was
        /// unavoidable below Large, and the ladder simply did it.
        /// </summary>
        public static string NextFasterInstalled(string currentKey)
        {
            int cur = -1;
            for (int i = 0; i < SpeedOrder.Count; i++)
            {
                if (string.Equals(SpeedOrder[i], currentKey, StringComparison.OrdinalIgnoreCase))
                {
                    cur = i;
                    break;
                }
            }
            bool keepMultilingual = currentKey != null && !IsEnglishOnly(currentKey);
            for (int i = cur + 1; i < SpeedOrder.Count; i++)
            {
                string key = SpeedOrder[i];
                if (keepMultilingual && IsEnglishOnly(key)) { continue; }
                var m = FindByKey(key);
                if (m != null && m.Key == key && IsInstalled(m)) { return key; }
            }
            return null;
        }

        /// <summary>
        /// Downloads the given model into the models folder, reporting progress and
        /// honouring a cancel check. Writes to a temp file and renames on success so
        /// a partial/failed download never leaves a half file that looks installed.
        /// </summary>
        public static bool Download(WhisperModelInfo model, Action<long, long> onProgress,
            Func<bool> isCancelled, out string error)
        {
            error = null;
            if (model == null || string.IsNullOrWhiteSpace(model.DownloadUrl))
            {
                error = "No download link for this model.";
                return false;
            }

            string finalPath = PathFor(model);
            string tempPath = finalPath + ".part";

            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                ServicePointManager.Expect100Continue = false;

                // RESUME an interrupted download instead of starting over.
                //
                // The speech models are large — the biggest is about 1.6 GB — and this
                // used to open the .part file with FileMode.Create, which truncates. A
                // dropped connection, a sleep, or a cancel at 95% therefore threw away
                // everything and made the user pull the whole file again. Now the bytes
                // already on disk are kept and the server is asked to continue from that
                // offset with an HTTP Range request.
                long already = 0;
                try
                {
                    var part = new FileInfo(tempPath);
                    if (part.Exists) { already = part.Length; }
                }
                catch { already = 0; }

                var request = (HttpWebRequest)WebRequest.Create(model.DownloadUrl);
                request.Method = "GET";
                request.UserAgent = "Tempo/" + (UpdateChecker.CurrentVersion?.ToString() ?? "1.0");
                request.Timeout = 20000;
                request.ReadWriteTimeout = 120000;
                request.AllowAutoRedirect = true; // HuggingFace redirects to a CDN.
                // Avoid the WPAD proxy-discovery hang on networks without a proxy.
                request.Proxy = null;
                request.KeepAlive = false;
                if (already > 0)
                {
                    try { request.AddRange(already); }
                    catch { already = 0; }   // can't ask for a range — fall back to a full fetch
                }

                using (var response = (HttpWebResponse)request.GetResponse())
                using (Stream source = response.GetResponseStream())
                {
                    // 206 means the server honoured the range and is sending the REST of
                    // the file. Anything else (a 200, a CDN that ignores Range) means it
                    // is sending the whole thing again, so the partial file must go or
                    // the two would be spliced together into a corrupt model.
                    bool resuming = already > 0 &&
                                    response.StatusCode == HttpStatusCode.PartialContent;
                    if (already > 0 && !resuming)
                    {
                        Logger.Info("[Model] server ignored the resume request — restarting the download.");
                        already = 0;
                    }
                    else if (resuming)
                    {
                        Logger.Info("[Model] resuming from " + (already / 1048576) + " MB already on disk.");
                    }

                using (var dest = new FileStream(tempPath,
                           resuming ? FileMode.Append : FileMode.Create,
                           FileAccess.Write, FileShare.None))
                {
                    // ContentLength on a 206 is what REMAINS, so the real total is what
                    // we already hold plus what is still coming.
                    long total = response.ContentLength > 0
                        ? response.ContentLength + already
                        : response.ContentLength;
                    var buffer = new byte[131072];
                    long readTotal = already;
                    int read;
                    while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (isCancelled != null && isCancelled())
                        {
                            error = "The download was cancelled.";
                            // KEEP the partial file. Cancelling used to delete it, so
                            // changing your mind at 90% of a 1.6 GB model meant fetching
                            // all of it again; now pressing Download continues from here.
                            try { dest.Flush(); dest.Dispose(); } catch { }
                            return false;
                        }
                        dest.Write(buffer, 0, read);
                        readTotal += read;
                        onProgress?.Invoke(readTotal, total);
                    }

                    if (total > 0 && readTotal < total)
                    {
                        error = "The download was interrupted — press Download again to carry on from here.";
                        // Also kept, for the same reason: the next attempt resumes.
                        try { dest.Flush(); dest.Dispose(); } catch { }
                        return false;
                    }
                }
                }

                // A real model is tens of MB; reject an obvious error page.
                var info = new FileInfo(tempPath);
                if (!info.Exists || info.Length < 1024 * 1024)
                {
                    error = "The downloaded model looks incomplete.";
                    TryDelete(tempPath);
                    return false;
                }

                // Atomic-ish swap into place.
                try { if (File.Exists(finalPath)) File.Delete(finalPath); } catch { }
                File.Move(tempPath, finalPath);
                return true;
            }
            catch (Exception ex)
            {
                // A network failure leaves the partial file in place so the next attempt
                // resumes rather than restarting. Only a file that turned out to be
                // GARBAGE (below) is deleted.
                error = "Couldn't download the model: " + ex.Message +
                        " — press Download again to carry on from where it stopped.";
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
            catch { }
        }

        /// <summary>Base stays the default — the best speed/accuracy floor for most PCs.</summary>
        public static WhisperModelInfo Default
        {
            get
            {
                foreach (var m in Available)
                {
                    if (m.Key == "base")
                    {
                        return m;
                    }
                }
                return Available[0];
            }
        }

        /// <summary>
        /// What Tempo worked out about a model file by reading it. Produced by
        /// <see cref="WhisperModelManager.ReadFacts"/>; see there for how each field is
        /// derived and what it was verified against.
        /// </summary>
        public sealed class WhisperModelFacts
        {
            public string Path;
            public long Bytes;
            public bool Valid;
            /// <summary>Plain-English reason this file can't be used. Null when Valid.</summary>
            public string Problem;

            public int Vocab, AudioState, AudioLayers, TextLayers, Mels, RawFtype;

            public string Family = "Unknown size";
            public string Generation = "";      // "v3" or ""
            public bool IsTurbo;
            public bool IsMultilingual;
            public string Precision = "";
            public bool IsQuantised;

            public string FileName
            {
                get { try { return System.IO.Path.GetFileName(Path); } catch { return Path ?? ""; } }
            }

            public string SizeText
            {
                get
                {
                    return Bytes >= 1024L * 1024 * 1024
                        ? (Bytes / (1024.0 * 1024 * 1024)).ToString("0.00") + " GB"
                        : (Bytes / (1024.0 * 1024)).ToString("0") + " MB";
                }
            }

            /// <summary>"Large v3 Turbo" — the model's identity, ignoring its filename.</summary>
            public string Name
            {
                get
                {
                    string s = Family;
                    if (Generation.Length > 0) { s += " " + Generation; }
                    if (IsTurbo) { s += " Turbo"; }
                    return s;
                }
            }

            public string LanguageText
            {
                get { return IsMultilingual ? "any language" : "English only"; }
            }

            /// <summary>One line for a menu: "Large v3 Turbo · any language · Q5_0 · 547 MB".</summary>
            public string Headline
            {
                get { return Name + "  ·  " + LanguageText + "  ·  " + Precision + "  ·  " + SizeText; }
            }

            /// <summary>
            /// What this model will feel like live. Said in terms of the thing the user
            /// cares about — whether captions keep up — rather than parameter counts.
            /// </summary>
            public string SpeedHint
            {
                get
                {
                    if (IsTurbo) { return "Fast for its accuracy — the decoder is cut down, so it keeps up far better than a full Large."; }
                    switch (Family)
                    {
                        case "Tiny": return "Instant even on weak PCs; accuracy is rough.";
                        case "Base": return "Quick on any modern PC; decent accuracy.";
                        case "Small": return "A good middle ground — noticeably better than Base, still real-time on most PCs.";
                        case "Medium": return "Accurate, but needs a strong CPU or the GPU engine to stay live.";
                        case "Large": return "The most accurate, and the heaviest — realistically needs the GPU engine for live captions.";
                        default: return "Tempo doesn't recognise this size, so it can't predict whether it will keep up.";
                    }
                }
            }

            /// <summary>Extra note when the file is quantised — the least-understood part.</summary>
            public string PrecisionHint
            {
                get
                {
                    return IsQuantised
                        ? "Quantised (" + Precision + ") — about a third of the size and CPU of the full model, with almost no accuracy lost."
                        : "Full precision (" + Precision + ") — the original weights, largest and slowest of this size.";
                }
            }
        }

        /// <summary>Folder that holds the model files (created on demand).</summary>
        public static string GetModelsDirectory()
        {
            string dir = Path.Combine(SettingsManager.GetSettingsDirectory(), "models");
            try { Directory.CreateDirectory(dir); } catch { }
            return dir;
        }

        public static WhisperModelInfo FindByKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return Default;
            foreach (var m in Available)
            {
                if (string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return m;
                }
            }
            return Default;
        }

        /// <summary>Full path the given model would live at (whether or not present).</summary>
        public static string PathFor(WhisperModelInfo model)
        {
            if (model == null) return null;
            return Path.Combine(GetModelsDirectory(), model.FileName);
        }

        /// <summary>
        /// Cheap validity check on a candidate model file, so a wrong pick fails HERE with
        /// a clear message instead of inside the native whisper library, where a bad file
        /// is an access violation that takes the process with it rather than an exception
        /// anything can catch.
        ///
        /// Every ggml whisper model starts with the 32-bit magic 0x67676D6C, which on disk
        /// is the byte sequence 6C 6D 67 67 — verified against the four models installed on
        /// this machine (base.en, medium.en, large-v3-turbo and its q5_0 quantisation), all
        /// of which begin with exactly those bytes. This deliberately does NOT accept GGUF
        /// ("GGUF" in ASCII): whisper.net loads ggml, and quietly taking a file it cannot
        /// read would just move the failure somewhere less explicable.
        /// </summary>
        public static bool LooksLikeModelFile(string path)
        {
            return ReadFacts(path).Valid;
        }

        /// <summary>
        /// What a ggml model file actually IS, read out of the file rather than guessed
        /// from its name.
        ///
        /// A name proves nothing — it is whatever the person who saved it typed, and a
        /// model that came from somewhere other than Tempo is exactly the case where it
        /// cannot be trusted. The header, though, carries the real shape of the network,
        /// and every field below was verified against the four models installed on this
        /// machine (base.en, medium.en, large-v3-turbo and its q5_0 quantisation):
        ///
        ///     file                          a_layer  t_layer   state   mels   ftype
        ///     ggml-base.en.bin                    6        6     512     80       1
        ///     ggml-medium.en.bin                 24       24    1024     80       1
        ///     ggml-large-v3-turbo.bin            32        4    1280    128       1
        ///     ggml-large-v3-turbo-q5_0.bin       32        4    1280    128    2008
        ///
        /// From which: n_audio_state gives the size class, n_vocab separates English-only
        /// (51864) from multilingual (51865+), n_mels of 128 marks the v3 generation, a
        /// text-layer count BELOW the audio-layer count marks a turbo model's distilled
        /// decoder, and ftype names the precision. The q5_0 file reading 2008 is what pins
        /// ftype's meaning: it is a ggml_ftype (where 8 is Q5_0), not a ggml_type (where 8
        /// would be Q8_0), plus a quantisation-version factor of 1000.
        /// </summary>
        public static WhisperModelFacts ReadFacts(string path)
        {
            var f = new WhisperModelFacts { Path = path };
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    f.Problem = "That file no longer exists.";
                    return f;
                }

                var fi = new FileInfo(path);
                f.Bytes = fi.Length;
                // Even Tiny is ~75 MB, so anything smaller is a partial or aborted download.
                if (fi.Length < 1024 * 1024)
                {
                    f.Problem = "The file is far too small to be a speech model — it looks like " +
                                "an interrupted download.";
                    return f;
                }

                using (var fs = File.OpenRead(path))
                using (var br = new BinaryReader(fs))
                {
                    var head = br.ReadBytes(4);
                    if (head.Length != 4 ||
                        head[0] != 0x6C || head[1] != 0x6D || head[2] != 0x67 || head[3] != 0x67)
                    {
                        f.Problem = "This isn't a ggml speech model. Tempo needs the .bin files " +
                                    "whisper.cpp uses — a GGUF file or an archive won't load.";
                        return f;
                    }

                    f.Vocab = br.ReadInt32();
                    br.ReadInt32();                    // n_audio_ctx
                    f.AudioState = br.ReadInt32();
                    br.ReadInt32();                    // n_audio_head
                    f.AudioLayers = br.ReadInt32();
                    br.ReadInt32();                    // n_text_ctx
                    br.ReadInt32();                    // n_text_state
                    br.ReadInt32();                    // n_text_head
                    f.TextLayers = br.ReadInt32();
                    f.Mels = br.ReadInt32();
                    f.RawFtype = br.ReadInt32();
                }

                f.Family = FamilyFor(f.AudioState);
                f.IsMultilingual = f.Vocab >= 51865;
                f.IsTurbo = f.TextLayers > 0 && f.TextLayers < f.AudioLayers;
                f.Generation = f.Mels >= 128 ? "v3" : "";
                f.Precision = PrecisionName(f.RawFtype % 1000);
                f.IsQuantised = (f.RawFtype % 1000) > 1;
                f.Valid = true;
                return f;
            }
            catch (Exception ex)
            {
                f.Problem = "That file couldn't be read (" + ex.Message + ").";
                return f;
            }
        }

        private static string FamilyFor(int audioState)
        {
            switch (audioState)
            {
                case 384: return "Tiny";
                case 512: return "Base";
                case 768: return "Small";
                case 1024: return "Medium";
                case 1280: return "Large";
                default: return "Unknown size";
            }
        }

        /// <summary>
        /// ggml_ftype → a name people recognise. Confirmed by the q5_0 model on this
        /// machine reporting 2008 (version 2, ftype 8): in ggml_ftype, 8 is Q5_0.
        /// </summary>
        private static string PrecisionName(int ftype)
        {
            switch (ftype)
            {
                case 0: return "F32";
                case 1: return "F16";
                case 2: return "Q4_0";
                case 3: return "Q4_1";
                case 4: return "Q4_1/F16";
                case 7: return "Q8_0";
                case 8: return "Q5_0";
                case 9: return "Q5_1";
                case 10: return "Q2_K";
                case 11: return "Q3_K";
                case 12: return "Q4_K";
                case 13: return "Q5_K";
                case 14: return "Q6_K";
                default: return "type " + ftype;
            }
        }

        /// <summary>
        /// Model files sitting in the models folder that are NOT one of the known
        /// downloads. Tempo has always offered an "Open models folder" button, which
        /// invites people to put files there — but nothing ever read anything except the
        /// fourteen exact filenames in <see cref="Available"/>. A model copied over from
        /// whisper.cpp, a quantisation with a different suffix, a fine-tune, or simply a
        /// renamed file was invisible, and Tempo would report that no model was installed
        /// while sitting in a folder containing one.
        /// </summary>
        public static List<string> DiscoverExtraModelFiles()
        {
            var found = new List<string>();
            try
            {
                var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var m in Available) { known.Add(m.FileName); }

                foreach (string p in Directory.GetFiles(GetModelsDirectory(), "*.bin"))
                {
                    if (known.Contains(Path.GetFileName(p))) { continue; }
                    if (LooksLikeModelFile(p)) { found.Add(p); }
                }
                found.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch { }
            return found;
        }

        /// <summary>
        /// The human-browsable page for the SAME repository the built-in downloads come
        /// from. Derived from <see cref="Repo"/> rather than written out again, so the
        /// link Tempo sends people to can never drift from the one it downloads from —
        /// which is exactly how a "get more models" link ends up pointing somewhere
        /// abandoned, and how someone ends up downloading a file that will not load.
        /// </summary>
        public static string OfficialModelsPageUrl
        {
            get { return Repo.Replace("/resolve/main/", "/tree/main"); }
        }

        /// <summary>
        /// Folders where OTHER speech apps keep their ggml models, plus Downloads.
        ///
        /// Someone who already has a model rarely knows the path to it: it was put there
        /// by Subtitle Edit or whisper.cpp, or it is one of forty things in Downloads.
        /// Telling them to "browse for the file" assumes the one piece of knowledge they
        /// are missing, so Tempo looks in the handful of places it is actually likely to
        /// be. Deliberately a SHALLOW, fixed list — never a disk sweep, which would be
        /// slow, surprising, and would read folders that are none of Tempo's business.
        /// </summary>
        private static IEnumerable<string> ModelSearchFolders()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            yield return Path.Combine(home, "Downloads");
            yield return Path.Combine(roaming, "Subtitle Edit", "Whisper", "Purfview-Whisper-Faster");
            yield return Path.Combine(roaming, "Subtitle Edit", "Whisper", "whispercpp");
            yield return Path.Combine(roaming, "Subtitle Edit", "Whisper");
            yield return Path.Combine(local, "Programs", "Subtitle Edit", "Whisper");
            yield return Path.Combine(local, "Buzz", "models");
            yield return Path.Combine(home, ".cache", "whisper");
            yield return Path.Combine(home, ".cache", "whisper.cpp");
            yield return Path.Combine(home, "whisper.cpp", "models");
            yield return Path.Combine(progFiles, "whisper.cpp", "models");
        }

        /// <summary>
        /// Valid ggml models sitting somewhere else on this PC. Each candidate is verified
        /// by reading its header, so a stray .bin that happens to share the extension is
        /// never offered. Capped, because this feeds a menu.
        /// </summary>
        public static List<string> FindModelsElsewhere(int max = 12)
        {
            var found = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string ownFolder = GetModelsDirectory();

            foreach (string dir in ModelSearchFolders())
            {
                if (found.Count >= max) { break; }
                try
                {
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) { continue; }
                    // Already covered by DiscoverExtraModelFiles — don't list it twice.
                    if (string.Equals(Path.GetFullPath(dir).TrimEnd('\\'),
                                      Path.GetFullPath(ownFolder).TrimEnd('\\'),
                                      StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    foreach (string p in Directory.GetFiles(dir, "*.bin"))
                    {
                        if (found.Count >= max) { break; }
                        if (!seen.Add(Path.GetFullPath(p))) { continue; }
                        if (LooksLikeModelFile(p)) { found.Add(p); }
                    }
                }
                catch
                {
                    // A folder we can't read is simply not a place a model was found.
                }
            }
            return found;
        }

        /// <summary>"ggml-my-finetune.bin (1,463 MB)" — for menus and status lines.</summary>
        public static string DescribeModelFile(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                return fi.Name + "  (" + (fi.Length / (1024 * 1024)).ToString("N0") + " MB)";
            }
            catch { return path ?? ""; }
        }

        /// <summary>Whether the given model's file is present on disk.</summary>
        public static bool IsInstalled(WhisperModelInfo model)
        {
            try
            {
                string p = PathFor(model);
                return !string.IsNullOrEmpty(p) && File.Exists(p) && new FileInfo(p).Length > 1024;
            }
            catch { return false; }
        }

        /// <summary>
        /// Returns the path to the best installed model, preferring the requested
        /// key and falling back to any other installed model. Null if none are
        /// installed (the caller then tells the user to install one).
        /// </summary>
        public static string ResolveInstalledPath(string preferredKey)
        {
            var preferred = FindByKey(preferredKey);
            if (IsInstalled(preferred))
            {
                return PathFor(preferred);
            }
            foreach (var m in Available)
            {
                if (IsInstalled(m))
                {
                    return PathFor(m);
                }
            }

            // Last resort: a model file the user put in the folder themselves. Reaching
            // here means none of the known downloads is present, and the alternative is
            // telling someone no model is installed while a perfectly good one sits in the
            // folder Tempo's own button just opened for them.
            foreach (string extra in DiscoverExtraModelFiles())
            {
                Logger.Info("[Captions] using a model found in the models folder: " + Path.GetFileName(extra));
                return extra;
            }
            return null;
        }
    }
}
