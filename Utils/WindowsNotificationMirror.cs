using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using Windows.Storage.Streams;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Watches the Windows notification stream and re-emits each new toast from OTHER
    /// apps so Tempo can show it in its own animated style ("mirror"). Built on the OS
    /// <see cref="UserNotificationListener"/> — the same on-device API a smartwatch app
    /// uses to relay phone notifications. Nothing leaves the PC; Tempo only reads the
    /// title/body text of notifications the user already received.
    ///
    /// Honest limits (surfaced in <see cref="StatusText"/> so the UI can be truthful):
    ///  • The listener needs Windows to grant "notification access". On most Win10/11
    ///    builds a desktop app is allowed after the user permits it once; on some
    ///    locked-down builds the access is denied and mirroring simply isn't available.
    ///  • Reading a notification does NOT stop Windows from also showing its own toast.
    ///    To see ONLY Tempo's cards, the user turns on Windows "Do not disturb" — which
    ///    Tempo can point them to but never changes on its own.
    ///
    /// Reliability choice: the OS <c>NotificationChanged</c> event is flaky for
    /// unpackaged desktop apps, so this POLLS on a short timer and diffs by notification
    /// id, which is dependable everywhere. The backlog already sitting in the Action
    /// Center at start-up is recorded silently (not replayed), so enabling the feature
    /// doesn't dump a screenful of old notifications.
    /// </summary>
    public sealed class WindowsNotificationMirror : IDisposable
    {
        private readonly Action<string, string, string, Image, string> _onNew;  // app, title, body, icon, aumid
        private readonly Func<bool> _removeFromActionCenter;

        private UserNotificationListener _listener;
        private Timer _poll;
        private readonly HashSet<uint> _seen = new HashSet<uint>();
        private int _polling;          // Interlocked reentrancy guard
        private int _starting;         // Interlocked guard: only one Start() may proceed
        private bool _primed;          // first poll records the backlog silently
        private bool _disposed;

        // These are written on the poll / access threads and read on the UI thread for
        // Live Debug, so they must be volatile to be seen promptly across threads.
        private volatile bool _running;
        private volatile string _statusText = "off";
        private volatile int _mirroredCount;
        private volatile string _lastApp;
        private volatile int _eventHits;
        private volatile int _suppressedDuplicates;
        private volatile string _lastSuppressed;

        public bool Running => _running;
        public string StatusText => _statusText;
        public int MirroredCount => _mirroredCount;
        public string LastApp => _lastApp;
        /// <summary>Times the instant NotificationChanged fast-path fired (vs. polling).</summary>
        public int EventFastPathHits => _eventHits;
        /// <summary>Identical notifications collapsed into one card (e.g. the same site in many tabs).</summary>
        public int SuppressedDuplicates => _suppressedDuplicates;
        /// <summary>App whose duplicate was most recently suppressed.</summary>
        public string LastSuppressedApp => _lastSuppressed;
        /// <summary>App logos decoded and cached (each one saves a slow decode per notification).</summary>
        public int CachedIcons { get { lock (_iconCache) { return _iconCache.Count; } } }
        /// <summary>
        /// The interval currently in force, in ms — the worst-case lag behind a Windows
        /// toast. Adapts: fast around activity, slow while idle, and pushed out further
        /// on a machine where each poll is expensive (see SchedulePoll).
        /// </summary>
        public int PollIntervalMs => _currentIntervalMs;

        /// <summary>Measured cost of a single poll, in ms — what the interval adapts to.</summary>
        public int PollCostMs => _lastPollCostMs;

        public WindowsNotificationMirror(Action<string, string, string, Image, string> onNew,
                                         Func<bool> removeFromActionCenter)
        {
            _onNew = onNew;
            _removeFromActionCenter = removeFromActionCenter ?? (() => false);
        }

        /// <summary>
        /// Requests notification access and, if granted, starts polling. Returns true if
        /// mirroring is now live; otherwise <see cref="StatusText"/> explains why not.
        ///
        /// MUST be called off the UI thread: RequestAccessAsync can show a consent prompt
        /// and blocking on it from the STA UI thread risks freezing (or deadlocking) the
        /// window. The caller (ApplyNotificationSettings) runs this on a worker thread and
        /// marshals the status back.
        /// </summary>
        public bool Start()
        {
            if (_running) { return true; }

            // The plain `_running` check above is NOT enough. Start() is called on a
            // worker thread from ApplyNotificationSettings, which now runs from BOTH
            // OnLoad and the tray-start path — and _running isn't set until well inside
            // this method, so two calls could sail past the check together and each arm
            // its own poll timer. The log showed exactly that: "Windows notification
            // mirror started." twice, 4 ms apart, i.e. two timers polling the same
            // notification stream. Only one caller gets through here.
            if (System.Threading.Interlocked.CompareExchange(ref _starting, 1, 0) != 0)
            {
                return _running;
            }
            try
            {
                _listener = UserNotificationListener.Current;
                if (_listener == null)
                {
                    _statusText = "unavailable on this Windows build";
                    return false;
                }

                UserNotificationListenerAccessStatus status;
                try
                {
                    status = _listener.RequestAccessAsync().AsTask().GetAwaiter().GetResult();
                }
                catch (Exception rex)
                {
                    // Thrown on builds that can't grant the listener to an unpackaged app.
                    _statusText = "access request failed (" + rex.Message + ")";
                    Logger.Warn("[Notify] listener access request failed: " + rex.Message);
                    return false;
                }

                if (status != UserNotificationListenerAccessStatus.Allowed)
                {
                    _statusText = status == UserNotificationListenerAccessStatus.Denied
                        ? "denied — allow it in Windows Settings › Privacy › Notifications"
                        : "not decided (permission prompt was dismissed)";
                    Logger.Info("[Notify] notification access not granted: " + status);
                    return false;
                }

                _primed = false;
                // Mark running BEFORE arming the timer so the very first poll (and any
                // NotificationChanged that races in) isn't dropped by the Stop guard.
                _running = true;
                _statusText = "mirroring Windows notifications";

                // ADAPTIVE polling — a fixed fast poll cost an entire CPU core.
                //
                // GetNotificationsAsync is not a cheap local read: it is a blocking
                // cross-process call into the Windows notification platform. Measured on
                // this machine it costs about 62 ms of CPU per call. Firing it every
                // 70 ms therefore left almost no gap — Tempo sat at ~100% of one core
                // doing nothing but asking Windows for notifications, with its busiest
                // thread permanently Running and the next one parked in LpcReply. That
                // starved the UI thread, which is what made dragging the window and the
                // notification animations feel laggy: they were competing with a spin.
                //
                // Now it polls slowly while nothing is happening and speeds up for a few
                // seconds around actual activity, so a mirrored card still appears with
                // the native toast. SchedulePoll also refuses to run more often than a
                // few times its own measured cost, which caps the mirror's CPU share on
                // any machine — a slower PC backs itself off further instead of pegging.
                _poll = new Timer(_ => Poll(), null, 40, Timeout.Infinite);

                // Fast-path: when Windows raises NotificationChanged, poll immediately —
                // near-instant mirroring. Best-effort (this event is unreliable for
                // unpackaged desktop apps); the 200 ms timer is the dependable backstop.
                try
                {
                    // Subscribe ONCE for the life of the object. _eventHooked is what
                    // makes the matching detach safe — see Stop/Dispose.
                    if (!_eventHooked)
                    {
                        _listener.NotificationChanged += OnNotificationChanged;
                        _eventHooked = true;
                    }
                }
                catch (Exception hex)
                {
                    // Expected on unpackaged desktop apps — the 200 ms poll is the
                    // reliable path, so this isn't a warning, just a note.
                    Logger.Info("[Notify] event fast-path unavailable (using polling): " + hex.Message);
                }

                Logger.Info("[Notify] Windows notification mirror started.");
                return true;
            }
            catch (Exception ex)
            {
                _running = false;
                _statusText = "unavailable (" + ex.Message + ")";
                Logger.Warn("[Notify] mirror could not start: " + ex.Message);
                return false;
            }
        }

        public void Stop()
        {
            _running = false;   // set first so an in-flight poll/event bails before emitting

            // Do NOT detach the WinRT event here.
            //
            // This line used to run unconditionally, and it KILLED the process. On this
            // machine the subscribe throws — the log says so every launch:
            //     [Notify] event fast-path unavailable (using polling):
            // — so there was never a live subscription to remove. Detaching anyway sends
            // CsWinRT into UnsubscribeFromNative against event state that was never
            // built, and it faults:
            //     System.AccessViolationException: Attempted to read or write protected
            //     memory. at ABI.WinRT.Interop.EventSource`1.UnsubscribeFromNative(...)
            // The try/catch around it was worthless: an access violation is a
            // corrupted-state exception that .NET Core refuses to hand to managed code,
            // so nothing could catch it and Tempo simply died. Reported as "click Pop-up
            // notifications in the tray menu, then Tempo crashes" — that toggle runs
            // ApplyNotificationSettings, which calls Stop().
            //
            // Stop() only needs the mirror to go QUIET, and OnNotificationChanged
            // already returns immediately when !_running, so leaving the subscription
            // attached across a stop/start costs nothing and removes the churn of
            // detaching and re-attaching every time the setting is toggled. The one
            // real detach happens in Dispose, and only if the attach succeeded.
            try { _poll?.Dispose(); } catch { }
            _poll = null;
            _seen.Clear();
            lock (_recentContent) { _recentContent.Clear(); }
            // Release the start guard, otherwise turning mirroring off and on again
            // would be refused for the rest of the session.
            System.Threading.Interlocked.Exchange(ref _starting, 0);
            if (!_disposed) { _statusText = "off"; }
        }

        // Windows says a notification was added or removed — pull the change now
        // instead of waiting for the next poll tick.
        private void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
        {
            if (_disposed || !_running) { return; }
            _eventHits++;
            // The OS says something changed — that counts as activity, so the adaptive
            // interval stays fast for the burst that usually follows.
            _lastActivityTick = Environment.TickCount64;
            try { Poll(); } catch (Exception ex) { Logger.Swallow("Notify.Changed", ex); }
        }

        // Poll cadence. Fast only when something is actually happening.
        private const int ActivePollMs = 100;    // just saw a notification — stay responsive
        private const int IdlePollMs = 600;      // nothing happening — stay out of the way
        private const int ActiveWindowMs = 6000; // how long "recently active" lasts
        // Never spend more than roughly this share of one core on polling: the next poll
        // is pushed out to at least PollCostBudget x the last poll's measured duration.
        private const int PollCostBudget = 8;

        private long _lastActivityTick;
        private int _lastPollCostMs;
        private int _currentIntervalMs = IdlePollMs;

        /// <summary>Arms the one-shot timer for the next poll at the adaptive interval.</summary>
        private void SchedulePoll()
        {
            if (_disposed || !_running) { return; }

            bool recentlyActive =
                Environment.TickCount64 - _lastActivityTick < ActiveWindowMs;
            int want = recentlyActive ? ActivePollMs : IdlePollMs;

            // Self-limiting: a poll that costs 62 ms must not be repeated every 70 ms.
            int floor = _lastPollCostMs * PollCostBudget;
            if (want < floor) { want = floor; }
            if (want > 5000) { want = 5000; }

            _currentIntervalMs = want;
            try { _poll?.Change(want, Timeout.Infinite); } catch { }
        }

        private void Poll()
        {
            if (_disposed || !_running) { return; }
            if (Interlocked.Exchange(ref _polling, 1) == 1) { return; }   // a poll is still running
            long startedTick = Environment.TickCount64;
            try
            {
                IReadOnlyList<UserNotification> notes;
                try
                {
                    notes = _listener.GetNotificationsAsync(NotificationKinds.Toast)
                                     .AsTask().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Logger.Swallow("Notify.Poll", ex);
                    return;
                }
                if (notes == null) { return; }

                var current = new HashSet<uint>();
                foreach (var n in notes)
                {
                    current.Add(n.Id);
                    if (_seen.Contains(n.Id)) { continue; }
                    _seen.Add(n.Id);

                    // First poll after start: record what's ALREADY in the Action Center
                    // without replaying it as a burst of pop-ups.
                    if (!_primed) { continue; }

                    // Something real arrived — poll quickly for a while, since toasts
                    // very often come in bursts.
                    _lastActivityTick = Environment.TickCount64;
                    Emit(n);
                }
                _primed = true;

                // Prune ids that are gone so memory stays bounded (and a re-posted
                // notification can mirror again).
                _seen.RemoveWhere(id => !current.Contains(id));
            }
            finally
            {
                // Record what this poll actually cost, then arm the next one. Measuring
                // it is what lets the interval adapt to the machine instead of assuming.
                int cost = (int)(Environment.TickCount64 - startedTick);
                if (cost < 0) { cost = 0; }
                _lastPollCostMs = _lastPollCostMs == 0
                    ? cost
                    : (_lastPollCostMs * 3 + cost) / 4;    // smooth out one-off spikes
                Interlocked.Exchange(ref _polling, 0);
                SchedulePoll();
            }
        }

        private void Emit(UserNotification n)
        {
            string appName = "";
            string aumid = "";
            try { appName = n.AppInfo?.DisplayInfo?.DisplayName ?? ""; }
            catch { /* some notifications have no resolvable app info */ }
            // The app's identity — used to OPEN it when the mirrored card is clicked.
            try { aumid = n.AppInfo?.AppUserModelId ?? ""; }
            catch { /* not all notifications carry a resolvable AUMID */ }

            // Never mirror Tempo's own notifications back to itself.
            if (string.Equals(appName, "Tempo", StringComparison.OrdinalIgnoreCase)) { return; }

            string title = "";
            string body = "";
            try
            {
                NotificationBinding binding =
                    n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
                if (binding != null)
                {
                    var texts = binding.GetTextElements();
                    if (texts != null)
                    {
                        var lines = new List<string>();
                        for (int i = 0; i < texts.Count; i++)
                        {
                            string t = texts[i]?.Text;
                            if (!string.IsNullOrWhiteSpace(t)) { lines.Add(t.Trim()); }
                        }
                        if (lines.Count > 0)
                        {
                            title = lines[0];
                            if (lines.Count > 1)
                            {
                                body = string.Join("\n", lines.GetRange(1, lines.Count - 1));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Swallow("Notify.Emit", ex);
            }

            // Nothing readable — skip rather than show an empty card.
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body)) { return; }
            if (string.IsNullOrWhiteSpace(title)) { title = appName.Length > 0 ? appName : "Notification"; }

            // ── Duplicate suppression ───────────────────────────────────────────
            // One web site open in ten browser tabs posts ONE logical notification, but
            // each tab raises its own Windows toast — so the mirror faithfully produced
            // ten identical cards at once and buried the screen. Windows gives each of
            // them a different notification id, so the id-based "seen" set above can't
            // catch it; only the CONTENT can. Suppress a repeat of the same
            // app+title+body inside a short window, which collapses the burst to one
            // card while still letting a genuinely repeated message through later
            // (a second "You have a new message" a minute on is real news).
            //
            // Deliberately content-based rather than per-tab: the listener API never
            // tells us which tab, window, or even which browser profile a toast came
            // from, so there is no "active instance" to elect. Collapsing identical
            // content is the only thing that actually works here — and it fixes the
            // same burst from any source, not just browsers.
            if (IsDuplicate(appName, title, body))
            {
                _suppressedDuplicates++;
                _lastSuppressed = appName;
                return;
            }

            // Detect the source app's ICON for the top-left, exactly like the real
            // Windows 11 toast. We deliberately DON'T turn the app's square logo into a
            // big "picture" below the text — Windows 11 doesn't, and a blown-up icon in
            // a box looked wrong. A real inline content image (e.g. a screenshot
            // thumbnail) isn't exposed by the listener API, so there's no hero to show.
            Image icon = GetAppIconCached(n, aumid, appName);

            // Remove the Windows copy FIRST, with no delay, so the mirrored card
            // replaces it rather than sitting alongside it. (This clears the Action
            // Center entry immediately; on the Windows builds where RemoveNotification
            // also retracts the banner, calling it this early gives the best chance of
            // the pop-up never lingering. The banner itself can only be reliably hidden
            // by Windows "Do not disturb" — see the setting's guidance.)
            if (_removeFromActionCenter())
            {
                try { _listener.RemoveNotification(n.Id); } catch { }
            }

            // No consumer (or we were stopped mid-emit) — don't count it, and don't leak
            // the icon bitmap we just decoded.
            if (_onNew == null || !_running)
            {
                try { icon?.Dispose(); } catch { }
                return;
            }

            _mirroredCount++;
            _lastApp = appName;
            try { _onNew(appName, title, body, icon, aumid); }
            catch { try { icon?.Dispose(); } catch { } }
        }

        // Recent notification CONTENT, for duplicate suppression. Small and bounded —
        // this holds at most DedupeMax entries and is pruned on every check, so it can't
        // grow over a long session.
        private readonly Dictionary<string, long> _recentContent = new Dictionary<string, long>();
        private const int DedupeWindowMs = 8000;   // a burst from N tabs lands well inside this
        private const int DedupeMax = 64;

        /// <summary>
        /// True when this exact app+title+body was already mirrored moments ago — i.e.
        /// the same logical notification arriving once per open tab. Also refreshes the
        /// timestamp, so a continuing burst keeps collapsing instead of leaking a second
        /// card as the window rolls over.
        /// </summary>
        private bool IsDuplicate(string appName, string title, string body)
        {
            string key = (appName ?? "") + "" + (title ?? "") + "" + (body ?? "");
            long now = Environment.TickCount64;
            bool dup;
            lock (_recentContent)
            {
                dup = _recentContent.TryGetValue(key, out long seen) && now - seen < DedupeWindowMs;
                _recentContent[key] = now;

                // Prune anything past the window; hard-trim if a pathological burst of
                // DISTINCT messages ever outgrows the cap.
                if (_recentContent.Count > DedupeMax)
                {
                    var stale = new List<string>();
                    foreach (var kv in _recentContent)
                    {
                        if (now - kv.Value >= DedupeWindowMs) { stale.Add(kv.Key); }
                    }
                    foreach (string s in stale) { _recentContent.Remove(s); }
                    if (_recentContent.Count > DedupeMax) { _recentContent.Clear(); }
                }
            }
            return dup;
        }

        // Decoded app logos, keyed by AUMID. Decoding one costs several blocking WinRT
        // round-trips plus an image decode, and it sat ON THE CRITICAL PATH between the
        // notification arriving and Tempo's card appearing — so on a mid-range PC every
        // single notification paid that cost again and the card visibly trailed Windows'
        // own toast. The same handful of apps send almost all notifications, so caching
        // makes every notification after an app's first one essentially free.
        private readonly Dictionary<string, Image> _iconCache = new Dictionary<string, Image>();
        private const int IconCacheMax = 32;

        // Failed icon lookups are retried, but not on every single toast — resolving a
        // desktop app walks the process list.
        private const int MissRetryMs = 30_000;
        private readonly Dictionary<string, long> _iconMissAt =
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The source app's icon, decoded once per app and reused. Returns a COPY each
        /// time: the notification card owns (and disposes) whatever it is handed, so the
        /// cached master must never be given out directly.
        /// </summary>
        private Image GetAppIconCached(UserNotification n, string aumid, string appName)
        {
            // No stable key — fall back to decoding it fresh.
            if (string.IsNullOrEmpty(aumid)) { return TryGetAppIcon(n, null, appName); }

            Image master;
            bool known;
            lock (_iconCache)
            {
                known = _iconCache.TryGetValue(aumid, out master);
            }

            if (!known || master == null)
            {
                // A FAILURE is only cached for a short while, not forever. The packaged
                // logo either exists or never will, but the desktop-app fallback reads
                // the icon off the RUNNING process — so a miss can simply mean the app
                // wasn't up yet. Caching that permanently (as this used to) would leave
                // the app wearing the wrong icon for the rest of the session even after
                // it started. Retrying every toast would be just as wrong: each attempt
                // enumerates processes, so misses are rate-limited instead.
                lock (_iconCache)
                {
                    if (_iconMissAt.TryGetValue(aumid, out long lastMiss) &&
                        Environment.TickCount64 - lastMiss < MissRetryMs)
                    {
                        return null;
                    }
                }

                // Resolve OUTSIDE the lock. This does blocking WinRT I/O and may walk the
                // process list; holding the lock across it meant anything else touching
                // the cache — including Dispose() on shutdown — waited on that. Two
                // threads racing the same new app just resolve twice and one copy is
                // dropped, far cheaper than serialising every caller behind the I/O.
                Image decoded = TryGetAppIcon(n, aumid, appName);
                lock (_iconCache)
                {
                    if (decoded == null)
                    {
                        _iconMissAt[aumid] = Environment.TickCount64;
                        return null;
                    }

                    if (_iconCache.TryGetValue(aumid, out Image raced) && raced != null)
                    {
                        // Someone else got there first — keep theirs, drop ours.
                        if (!ReferenceEquals(raced, decoded)) { try { decoded?.Dispose(); } catch { } }
                        master = raced;
                    }
                    else
                    {
                        if (_iconCache.Count >= IconCacheMax)
                        {
                            foreach (var kv in _iconCache) { try { kv.Value?.Dispose(); } catch { } }
                            _iconCache.Clear();
                        }
                        _iconCache[aumid] = decoded;
                        _iconMissAt.Remove(aumid);
                        master = decoded;
                    }
                }
            }

            if (master == null) { return null; }
            lock (_iconCache)
            {
                // Copy under the lock so an eviction can't dispose the master mid-copy.
                try { return new Bitmap(master); }      // independent copy for the card
                catch { return null; }
            }
        }

        /// <summary>
        /// Reads the source app's logo (via AppInfo.DisplayInfo) into a small GDI+ icon
        /// for the card's top-left, or null if the app exposes no logo. Copied into an
        /// independent Bitmap so the WinRT stream releases at once.
        /// </summary>
        /// <summary>
        /// The source app's icon: the packaged-app logo where there is one, otherwise the
        /// icon off the running program itself.
        ///
        /// GetLogo only serves PACKAGED apps. For every ordinary desktop program —
        /// Discord, Chrome, Steam, Telegram — it returns null, and those cards then fell
        /// back to Tempo's own logo, which is what made a Discord message show up wearing
        /// Tempo's icon. The second route covers them.
        /// </summary>
        private static Image TryGetAppIcon(UserNotification n, string aumid, string appName)
        {
            Image packaged = TryGetPackagedLogo(n);
            if (packaged != null)
            {
                System.Threading.Interlocked.Increment(ref _fromPackagedLogo);
                return packaged;
            }

            Image desktop = AppActivator.TryGetIconForApp(aumid, appName);
            if (desktop != null)
            {
                System.Threading.Interlocked.Increment(ref _fromDesktopApp);
                return desktop;
            }

            System.Threading.Interlocked.Increment(ref _iconUnresolved);
            return null;
        }

        private static int _fromPackagedLogo;
        private static int _fromDesktopApp;
        private static int _iconUnresolved;

        /// <summary>Icons taken from a packaged app's manifest logo.</summary>
        public static int IconsFromPackagedLogo => _fromPackagedLogo;

        /// <summary>Icons read off a running desktop program's executable.</summary>
        public static int IconsFromDesktopApp => _fromDesktopApp;

        /// <summary>
        /// Notifications where no app icon could be found. Such a card falls back to a
        /// kind-tinted glyph badge — NOT Tempo's logo, which is what this used to say and
        /// what the Live Debug line used to report. Nothing has passed Tempo's icon down
        /// this path for a while: the mirror hands the resolved icon (or null) straight to
        /// the card, and the card paints the badge when it is null.
        /// </summary>
        public static int IconsUnresolved => _iconUnresolved;

        private static Image TryGetPackagedLogo(UserNotification n)
        {
            try
            {
                var logoRef = n.AppInfo?.DisplayInfo?.GetLogo(new Windows.Foundation.Size(48, 48));
                if (logoRef == null) { return null; }
                using (var ras = logoRef.OpenReadAsync().AsTask().GetAwaiter().GetResult())
                {
                    if (ras == null || ras.Size == 0 || ras.Size > 4_000_000) { return null; }
                    uint size = (uint)ras.Size;
                    using (var reader = new DataReader(ras))
                    {
                        reader.LoadAsync(size).AsTask().GetAwaiter().GetResult();
                        byte[] bytes = new byte[size];
                        reader.ReadBytes(bytes);
                        using (var ms = new MemoryStream(bytes))
                        using (var tmp = Image.FromStream(ms))
                        {
                            return new Bitmap(tmp);   // independent copy
                        }
                    }
                }
            }
            catch { return null; }
        }

        /// <summary>
        /// True only when <c>NotificationChanged += </c> actually SUCCEEDED. Detaching a
        /// WinRT event that was never attached faults the process (see Stop), so this is
        /// the gate on the one detach Tempo performs.
        /// </summary>
        private bool _eventHooked;

        public void Dispose()
        {
            _disposed = true;
            Stop();

            // The single, guarded detach. Skipped entirely when the attach failed, which
            // is the case that used to kill the process.
            if (_eventHooked && _listener != null)
            {
                _eventHooked = false;
                try { _listener.NotificationChanged -= OnNotificationChanged; }
                catch (Exception ex) { Logger.Swallow("Notify.Unhook", ex); }
            }
            lock (_iconCache)
            {
                foreach (var kv in _iconCache) { try { kv.Value?.Dispose(); } catch { } }
                _iconCache.Clear();
            }
            _statusText = "off";
        }
    }
}
