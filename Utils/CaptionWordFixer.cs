using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Repairs words the speech engines mis-hear, using Windows' built-in
    /// on-device spell engine (the same one every text box on the OS uses — no
    /// download, nothing leaves the PC). Speech-to-text mistakes very often
    /// surface as NON-WORDS ("seawhere", "jumpps", "brownn"): the OS engine flags
    /// them and proposes the real word or a two-word split, and Tempo applies the
    /// fix only under conservative rules so correctly-heard words are never
    /// "improved" away:
    ///  • only lowercase words of 4+ letters (names, acronyms and short words are
    ///    left alone),
    ///  • only when the suggestion is CLOSE — small edit distance, or the same
    ///    letters split into two words,
    ///  • every decision is cached, so the per-word cost is paid once.
    /// All failures degrade to "leave the text unchanged".
    /// </summary>
    public sealed class CaptionWordFixer : IDisposable
    {
        // ── ISpellChecker COM interop (spellcheck.h) ─────────────────────────
        [ComImport, Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC")]
        private class SpellCheckerFactoryClass { }

        [ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISpellCheckerFactory
        {
            void get_SupportedLanguages(out IntPtr value);
            int IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag);
            ISpellChecker CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string languageTag);
        }

        [ComImport, Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISpellChecker
        {
            void get_LanguageTag(out IntPtr tag);
            IEnumSpellingError Check([MarshalAs(UnmanagedType.LPWStr)] string text);
            IEnumString Suggest([MarshalAs(UnmanagedType.LPWStr)] string word);
            // Remaining methods unused — declared to keep the vtable aligned.
            void Add([MarshalAs(UnmanagedType.LPWStr)] string word);
            void Ignore([MarshalAs(UnmanagedType.LPWStr)] string word);
            void AutoCorrect([MarshalAs(UnmanagedType.LPWStr)] string from, [MarshalAs(UnmanagedType.LPWStr)] string to);
            void GetOptionValue([MarshalAs(UnmanagedType.LPWStr)] string optionId, out byte value);
            void get_OptionIds(out IntPtr value);
            void get_Id(out IntPtr value);
            void get_LocalizedName(out IntPtr value);
            void add_SpellCheckerChanged(IntPtr handler, out uint cookie);
            void remove_SpellCheckerChanged(uint cookie);
            void GetOptionDescription([MarshalAs(UnmanagedType.LPWStr)] string optionId, out IntPtr value);
            IEnumSpellingError ComprehensiveCheck([MarshalAs(UnmanagedType.LPWStr)] string text);
        }

        [ComImport, Guid("803E3BD4-2828-4410-8290-418D1D73C762"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IEnumSpellingError
        {
            [PreserveSig] int Next(out ISpellingError value);
        }

        [ComImport, Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISpellingError
        {
            uint StartIndex { get; }
            uint Length { get; }
            uint CorrectiveAction { get; }   // 0 none, 1 get-suggestions, 2 replace, 3 delete
            IntPtr Replacement { get; }      // LPWSTR (CoTaskMem) when action == replace
        }

        [ComImport, Guid("00000101-0000-0000-C000-000000000046"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IEnumString
        {
            [PreserveSig] int Next(int celt, [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr, SizeParamIndex = 0)] string[] rgelt, out int fetched);
        }

        // Words the OS dictionary flags as misspellings but that are REAL vocabulary
        // in the content Tempo captions (games, streams, tech). Several sit within
        // edit-distance 2 of a dictionary word, so without this list the fixer would
        // "repair" a correctly-heard word into a wrong one ("respawn" → "respond",
        // "loadout" → "loudout"…). Checked before any spell lookup — these are never
        // touched. Lowercase, letters-only, 4+ letters (the fixer's own scope).
        private static readonly HashSet<string> ProtectedWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "respawn", "respawns", "respawned", "respawning", "loadout", "loadouts",
            "hitbox", "hitboxes", "aimbot", "aimbots", "killcam", "minimap", "minimaps",
            "speedrun", "speedruns", "speedrunner", "speedrunners", "spawnpoint",
            "spawnpoints", "spawnkill", "griefer", "griefers", "griefing", "modpack",
            "modpacks", "modded", "modder", "modders", "enderman", "endermen",
            "redstone", "parkour", "debuff", "debuffs", "debuffed", "aggro", "respec",
            "gamertag", "gamertags", "unmute", "unmuted", "rerolls", "reroll",
            "roblox", "minecraft", "fortnite", "valorant", "warzone", "overwatch",
            "discord", "twitch", "tiktok", "youtube", "youtuber", "youtubers",
            "whatsapp", "reddit", "spotify", "netflix", "nvidia", "ryzen", "xbox",
            "playstation", "gameplay", "livestream", "livestreams", "livestreamer",
            "clickbait", "sponsorship", "subreddit", "subreddits", "emoji", "emojis",
            "tempo",
            // Shooter/session vocabulary — nearly all sit one or two edits from a
            // dictionary word the engine WOULD suggest ("laggy" → "baggy", "nerfed" →
            // "nerved", "peeker" → "seeker", "griefed" → "grieved", "gank" → "gang",
            // "smurf" → "surf", "prefire" → "prefer", "prestiged" → "prestige"…),
            // so every one of these is a live corruption risk, not just noise.
            "noob", "noobs", "laggy", "nerfed", "nerfing", "smurf", "smurfing",
            "gank", "ganked", "ganking", "crit", "crits", "peeker", "peekers",
            "fragger", "fraggers", "prefire", "prefired", "prestiged", "griefed",
            "sens", "unlockable", "unlockables",
            // Mashed compounds the letters-split rule below would happily un-mash
            // ("cooldown" → "cool down", "tryhard" → "try hard") — but spoken as ONE
            // word these are nouns with their own meaning, so a split is a
            // corruption, not a repair.
            "cooldown", "cooldowns", "powerup", "powerups", "hotbar", "keybind",
            "keybinds", "gamemode", "gamemodes", "killstreak", "killstreaks",
            "wallhack", "wallhacks", "tryhard", "tryhards", "lootbox", "battlepass",
            "ragequit", "roleplay", "roleplaying",
            // Roblox / Minecraft nouns ("obbies" → "lobbies" and "robux" → "robust"
            // are one-to-two edits away; "shulker" → "sulker", "piglin" → "piglet";
            // "gamepass" and "bedwars" would be split in two).
            "obby", "obbies", "robux", "gamepass", "gamepasses", "bedwars",
            "skywars", "skyblock", "shulker", "shulkers", "piglin", "piglins",
            // Stream/chat slang that IS the intended word ("vibing" → "viking",
            // "poggers" → "joggers", "copium" → "opium", "ratioed" → "rationed",
            // "bruh" → "brush", "yeet" → "yet", "rekt" → "rest", "dono" → "donor").
            "vtuber", "vtubers", "poggers", "sadge", "malding", "copium", "vibing",
            "yeet", "yeeted", "yeeting", "bruh", "rekt", "simp", "simps", "simping",
            "rizz", "sussy", "gacha", "griddy", "ratioed", "resub", "resubbed",
            "dono", "donos"
        };

        private ISpellChecker _checker;
        // Second opinion: a word the en-US engine flags may simply be BRITISH
        // ("colour", "realise", "favourite") — en-US happily "repairs" those into
        // American spellings, which is a false fix, not a correction. Any word the
        // en-GB engine accepts is left exactly as heard. Best-effort: when en-GB
        // isn't installed on the PC, behaviour is unchanged.
        private ISpellChecker _checkerGb;
        private readonly Dictionary<string, string> _cache = new Dictionary<string, string>(StringComparer.Ordinal);

        // Words English legitimately doubles with NO punctuation between them
        // ("he had had enough", "I know that that was wrong", "very very good") — the
        // stutter dedupe must not delete the second one and change the meaning.
        // Lowercase; 'bare' compared against these is already lowered.
        private static readonly HashSet<string> LegitDoubles = new HashSet<string>(StringComparer.Ordinal)
        {
            "had", "that", "very", "really", "no", "so", "yeah", "ha", "bye"
        };

        // Live Debug telemetry: is the fixer actually doing anything, and what was
        // its last repair? (Volatile is enough — written on the caption thread,
        // read by the debug panel; approximate counts are fine for a readout.)
        private volatile int _wordsChecked;
        private volatile int _wordsFixed;
        private volatile string _lastFix;

        /// <summary>Words that reached a spell verdict (cache hits included).</summary>
        public int WordsChecked => _wordsChecked;
        /// <summary>Words actually repaired this session.</summary>
        public int WordsFixed => _wordsFixed;
        /// <summary>The most recent repair, "seawhere → sea where"; null if none yet.</summary>
        public string LastFix => _lastFix;
        /// <summary>Cached spell verdicts held (bounded at 4096).</summary>
        public int CacheSize => _cache.Count;
        private volatile bool _available;

        /// <summary>True once the OS spell engine is up (English).</summary>
        public bool Available => _available;

        public CaptionWordFixer()
        {
            try
            {
                var factory = (ISpellCheckerFactory)new SpellCheckerFactoryClass();
                if (factory.IsSupported("en-US") != 0)
                {
                    _checker = factory.CreateSpellChecker("en-US");
                    _available = true;
                }
                try
                {
                    if (factory.IsSupported("en-GB") != 0)
                    {
                        _checkerGb = factory.CreateSpellChecker("en-GB");
                    }
                }
                catch { _checkerGb = null; }
            }
            catch (Exception ex)
            {
                Logger.Warn("[WordFix] OS spell engine unavailable: " + ex.Message);
                _available = false;
            }
        }

        public void Dispose()
        {
            _available = false;
            if (_checker != null)
            {
                try { Marshal.ReleaseComObject(_checker); } catch { }
                _checker = null;
            }
        }

        /// <summary>
        /// Returns <paramref name="text"/> with clearly-misheard words repaired.
        /// Cheap for repeated text: every word's verdict is cached.
        /// </summary>
        public string Fix(string text)
        {
            if (!_available || string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
            try
            {
                string[] parts = text.Split(' ');
                StringBuilder sb = null;
                string prevWordLower = null;
                // Sentence position matters: a Capitalised word at a sentence START is
                // ordinary text ("Seawhere did it go"), but Capitalised MID-sentence is
                // almost always a NAME ("ask Dani about it") — and names must never be
                // "repaired" into dictionary words. The first word of an emission
                // counts as a sentence start (the engines capitalise exactly there).
                bool sentenceStart = true;
                for (int i = 0; i < parts.Length; i++)
                {
                    string fixedWord = FixWord(parts[i], sentenceStart);
                    if (parts[i].Length > 0)
                    {
                        string trimmed = parts[i].TrimEnd('"', '\'', ')', ']', '”', '’');
                        char lastCh = trimmed.Length > 0 ? trimmed[trimmed.Length - 1] : ' ';
                        sentenceStart = lastCh == '.' || lastCh == '!' || lastCh == '?' ||
                                        lastCh == '…' || lastCh == ':';
                    }

                    // Stutter dedupe: speech engines love emitting "the the" / "and
                    // and" around chunk boundaries. Drop an exact immediate repeat
                    // (case-insensitive, punctuation-free words only, so "had had" with
                    // a comma or quotes still survives).
                    string bare = fixedWord.Trim().ToLowerInvariant();
                    bool wordOnly = bare.Length > 0;
                    for (int c = 0; c < bare.Length && wordOnly; c++)
                    {
                        if (!char.IsLetter(bare[c])) { wordOnly = false; }
                    }
                    if (wordOnly && prevWordLower != null && bare == prevWordLower
                        && !LegitDoubles.Contains(bare))
                    {
                        if (sb == null)
                        {
                            sb = new StringBuilder(text.Length + 16);
                            for (int k = 0; k < i; k++) { if (k > 0) sb.Append(' '); sb.Append(parts[k]); }
                        }
                        continue;                    // skip the duplicate
                    }
                    prevWordLower = wordOnly ? bare : null;

                    if (!ReferenceEquals(fixedWord, parts[i]) && sb == null)
                    {
                        sb = new StringBuilder(text.Length + 16);
                        for (int k = 0; k < i; k++) { if (k > 0) sb.Append(' '); sb.Append(parts[k]); }
                    }
                    if (sb != null)
                    {
                        if (sb.Length > 0) sb.Append(' ');
                        sb.Append(fixedWord);
                    }
                }
                return sb != null ? sb.ToString() : text;
            }
            catch
            {
                return text;
            }
        }

        private string FixWord(string token, bool sentenceStart)
        {
            // Split off trailing punctuation so "seawhere," still gets checked.
            int end = token.Length;
            while (end > 0 && !char.IsLetter(token[end - 1])) { end--; }
            if (end < 4)
            {
                return token;                      // too short to judge safely
            }
            string word = token.Substring(0, end);
            string tail = token.Substring(end);

            // Plain lowercase words are checked as-is. A Capitalised word (first letter
            // upper, rest lower) is checked via its lowercase form — but ONLY at a
            // sentence start: mid-sentence capitals are names ("Dani", "Matilda") and
            // a name within a couple of letters of a dictionary word would otherwise
            // be "repaired" into it. ALL-CAPS and mixed-case stay untouched too:
            // acronyms, game tags, stylised names.
            bool capitalised = char.IsUpper(word[0]);
            if (capitalised && !sentenceStart)
            {
                return token;                      // proper noun — hands off
            }
            for (int i = 1; i < word.Length; i++)
            {
                if (!char.IsLower(word[i]))
                {
                    return token;
                }
            }
            if (!capitalised && !char.IsLower(word[0]))
            {
                return token;
            }
            string lookupWord = capitalised ? char.ToLowerInvariant(word[0]) + word.Substring(1) : word;

            // Real gaming/tech vocabulary — never "repaired" into dictionary words.
            if (ProtectedWords.Contains(lookupWord))
            {
                return token;
            }

            string repl;
            _wordsChecked++;
            if (!_cache.TryGetValue(lookupWord, out repl))
            {
                repl = Lookup(lookupWord);
                if (_cache.Count > 4096) { _cache.Clear(); }   // bound memory
                _cache[lookupWord] = repl;
            }
            if (repl == null)
            {
                return token;
            }
            _wordsFixed++;
            _lastFix = lookupWord + " → " + repl;
            // Restore the capital on the repaired word ("Seawhere" → "Sea where").
            string fixedText = capitalised && repl.Length > 0
                ? char.ToUpperInvariant(repl[0]) + repl.Substring(1)
                : repl;
            return fixedText + tail;
        }

        /// <summary>True when the en-GB engine (if present) accepts the word as-is.</summary>
        private bool IsBritishOk(string word)
        {
            try
            {
                if (_checkerGb == null) { return false; }
                var errors = _checkerGb.Check(word);
                ISpellingError err;
                if (errors == null || errors.Next(out err) != 0 || err == null)
                {
                    return true;                   // valid British spelling
                }
                try { Marshal.ReleaseComObject(err); } catch { }
            }
            catch { }
            return false;
        }

        /// <summary>null = word is fine (or no safe fix); otherwise the replacement.</summary>
        private string Lookup(string word)
        {
            try
            {
                var errors = _checker.Check(word);
                ISpellingError err;
                if (errors == null || errors.Next(out err) != 0 || err == null)
                {
                    return null;                   // spelled fine — leave it
                }

                // en-US flagged it — but if it's simply BRITISH English, it isn't a
                // mishear at all. "Colour" must never be "fixed" into "color".
                if (IsBritishOk(word))
                {
                    try { Marshal.ReleaseComObject(err); } catch { }
                    return null;
                }
                try
                {
                    if (err.CorrectiveAction == 2 && err.Replacement != IntPtr.Zero)
                    {
                        string r = Marshal.PtrToStringUni(err.Replacement);
                        Marshal.FreeCoTaskMem(err.Replacement);
                        return SafeFix(word, r);
                    }
                    if (err.CorrectiveAction == 1)
                    {
                        // Scan the top FEW suggestions, not just the first: the OS
                        // often ranks a bolder rewrite first ("jumpps" → "jumper")
                        // with the actual mis-hear repair ("jumps") right behind it.
                        // SafeFix still gates every candidate, so this only ever
                        // finds MORE of the same conservative fixes, never bolder
                        // ones — the first candidate that passes wins.
                        var sugg = _checker.Suggest(word);
                        if (sugg != null)
                        {
                            var buf = new string[1];
                            for (int k = 0; k < 3; k++)
                            {
                                int got;
                                if (sugg.Next(1, buf, out got) != 0 || got != 1) { break; }
                                string fix = SafeFix(word, buf[0]);
                                if (fix != null) { return fix; }
                            }
                        }
                    }
                }
                finally
                {
                    try { Marshal.ReleaseComObject(err); } catch { }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Accepts a suggestion only when it's clearly the SAME word mis-heard: the
        /// identical letters split into two words ("seawhere" → "sea where"), or a
        /// small edit distance. Anything bolder is left alone.
        /// </summary>
        private static string SafeFix(string word, string suggestion)
        {
            if (string.IsNullOrWhiteSpace(suggestion) || suggestion.Equals(word, StringComparison.Ordinal))
            {
                return null;
            }
            string s = suggestion.ToLowerInvariant();

            // The engine sometimes "corrects" only the CASE ("iphone" → "iPhone").
            // Lowering the suggestion makes it identical to the input again, and the
            // split rule below would then hand the SAME word back as a "fix" — a
            // non-null verdict that makes Fix() allocate and rebuild the whole
            // emission on every repeat for zero visible change. Identical after
            // lowering = nothing worth doing.
            if (s.Equals(word, StringComparison.Ordinal))
            {
                return null;
            }

            // Same letters with a space inserted → a mashed two-word phrase.
            if (s.Replace(" ", "").Equals(word, StringComparison.Ordinal))
            {
                return s;
            }
            if (s.Contains(" "))
            {
                return null;                       // multi-word suggestion that changes letters — too bold
            }
            // Merged contractions ride through here for free: the speech engines
            // emit "dont" / "didnt" / "couldnt", the OS suggests the apostrophe
            // form, and the inserted apostrophe is just one edit ("dont" → "don't"
            // is distance 1). No apostrophe-specific handling needed — but the
            // distance check MUST keep comparing raw chars, not letters-only, or
            // this repair silently dies. ("wont"/"cant"/"shell"/"ill" never arrive:
            // they're dictionary words, so the engine never flags them — an
            // inherent limit we accept rather than guess at intent.)
            return EditDistanceAtMost(word, s, 2) ? s : null;
        }

        private static bool EditDistanceAtMost(string a, string b, int max)
        {
            if (Math.Abs(a.Length - b.Length) > max) return false;
            int[] prev = new int[b.Length + 1];
            int[] cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                int rowMin = cur[0];
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                    if (cur[j] < rowMin) rowMin = cur[j];
                }
                if (rowMin > max) return false;
                var t = prev; prev = cur; cur = t;
            }
            return prev[b.Length] <= max;
        }
    }
}
