using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AutoClicker.UI
{
    /// <summary>
    /// Owns Tempo's custom notification pop-ups: a stack of <see cref="NotificationToastForm"/>
    /// cards anchored to a screen corner. Anything in the app — a milestone, a caption
    /// warning, or a MIRRORED Windows notification from another app — calls
    /// <see cref="Notify"/>, and the centre creates a card, animates it in, and reflows
    /// the stack as cards come and go.
    ///
    /// Thread-safe entry point: <see cref="Notify"/> may be called from any thread (the
    /// notification mirror polls on a background timer). It marshals onto the owner's UI
    /// thread before touching any Form. A small cap keeps at most a handful of cards on
    /// screen at once; the rest queue and appear as slots free up, so a burst of toasts
    /// can't paper over the whole screen.
    /// </summary>
    public sealed class NotificationCenter : IDisposable
    {
        private readonly Form _owner;
        private readonly Func<Theme> _theme;
        private readonly Func<int> _corner;          // AppSettings.NotificationCorner
        private readonly Func<int> _durationMs;      // AppSettings.NotificationDurationSeconds * 1000

        private readonly List<NotificationToastForm> _active = new List<NotificationToastForm>();
        // A list used as a FIFO, not a Queue: a repeat has to be able to find its twin
        // among the waiting cards and count itself onto it, which needs indexing.
        private readonly List<Pending> _queue = new List<Pending>();
        private const int MaxVisible = 5;

        /// <summary>
        /// How many cards may wait behind the visible five. The queue used to be
        /// unbounded: one chatty app could leave dozens waiting, each holding its icon
        /// and hero bitmap, and — since each card is on screen for seconds — they would
        /// still be arriving minutes after the thing they were about.
        /// </summary>
        private const int MaxQueued = 12;

        /// <summary>
        /// A queued card older than this is not worth showing any more. "A run finished"
        /// arriving a minute late is a lie about the present; the history keeps it.
        /// </summary>
        private const int StaleAfterMs = 60000;

        private bool _disposed;

        private struct Pending
        {
            public string App, Title, Body;
            public ToastKind Kind;
            public Image Icon;
            public Image Hero;
            public Action OnActivate;
            public DateTime RaisedUtc;    // when it was RAISED, so the history stays honest
            public long RaisedTick;       // monotonic, for the staleness test
            public int Repeats;           // folded-in twins that arrived while it waited
        }

        /// <summary>Total cards shown this session (for Live Debug).</summary>
        public int ShownCount { get; private set; }

        /// <summary>Cards currently on screen (for Live Debug).</summary>
        public int ActiveCount => _active.Count;

        /// <summary>Cards waiting for a free slot behind the visible cap (for Live Debug).</summary>
        public int QueuedCount => _queue.Count;

        /// <summary>Cards not shown because Windows said it wasn't a good moment.</summary>
        public int SuppressedCount { get; private set; }

        /// <summary>
        /// How many repeats were folded onto a card already on screen instead of
        /// stacking a duplicate. Surfaced in Live debug so the collapsing is visible
        /// rather than looking like notifications going missing.
        /// </summary>
        public int RepeatsCollapsed { get; private set; }

        /// <summary>Why the last suppressed card was held back.</summary>
        public string LastSuppressedReason { get; private set; }

        /// <summary>
        /// Cards that waited behind the visible stack and were then dropped rather than
        /// shown — too old by the time there was room, or the screen had meanwhile gone
        /// fullscreen. Surfaced in Live debug; every one is in the history with a reason.
        /// </summary>
        public int DroppedFromQueueCount { get; private set; }

        /// <summary>
        /// Called when someone right-clicks a MIRRORED card and chooses to mute that app.
        /// Optional: without it the card's menu offers only "Dismiss".
        /// </summary>
        private readonly Action<string> _muteApp;

        public NotificationCenter(Form owner, Func<Theme> theme, Func<int> corner, Func<int> durationMs,
                                  Action<string> muteApp = null)
        {
            _owner = owner;
            _theme = theme ?? (() => Theme.ForKind(Models.ThemeKind.Dark));
            _corner = corner ?? (() => 0);
            _durationMs = durationMs ?? (() => 5000);
            _muteApp = muteApp;
        }

        /// <summary>
        /// Shows a notification card. Safe to call from any thread. Newest cards appear
        /// nearest the chosen corner and push older ones away.
        /// </summary>
        public void Notify(string appName, string title, string body, ToastKind kind,
                           Image icon = null, Image hero = null, Action onActivate = null)
        {
            NotifyCard(appName, title, body, kind, icon, hero, onActivate);
        }

        /// <summary>
        /// Same as <see cref="Notify"/>, but hands back the card it created so the caller
        /// can upgrade it in place later (see NotificationToastForm.UpdateSource).
        /// Returns null when the card was queued behind a full stack, or on any failure.
        /// </summary>
        public NotificationToastForm NotifyCard(string appName, string title, string body, ToastKind kind,
                                                Image icon = null, Image hero = null, Action onActivate = null)
        {
            if (_disposed) { icon?.Dispose(); hero?.Dispose(); return null; }
            try
            {
                if (_owner == null || _owner.IsDisposed) { icon?.Dispose(); hero?.Dispose(); return null; }
                if (_owner.InvokeRequired)
                {
                    _owner.BeginInvoke((Action)(() => ShowOrQueue(appName, title, body, kind, icon, hero, onActivate)));
                    return null;   // created on the UI thread a moment from now
                }
                return ShowOrQueue(appName, title, body, kind, icon, hero, onActivate);
            }
            catch { icon?.Dispose(); hero?.Dispose(); return null; }
        }

        private NotificationToastForm ShowOrQueue(string appName, string title, string body, ToastKind kind, Image icon, Image hero, Action onActivate)
        {
            if (_disposed) { icon?.Dispose(); hero?.Dispose(); return null; }

            // Don't put a card on screen while Windows says not to.
            //
            // These cards are Tempo's own topmost windows, so none of Windows' own
            // suppression reached them: a card would appear over a fullscreen game —
            // and this is an auto-clicker, so that is exactly where people are — or over
            // a presentation, showing whatever a mirrored notification happened to say
            // to the whole room. Windows hides its own toasts in both situations by
            // default; Tempo now asks the same shell API before showing anything.
            //
            // DROPPED rather than queued, which is what Windows does too: a queue would
            // empty itself into a burst of stale cards the moment the game closed. A
            // mirrored notification is still sitting in the Action Center, and Tempo's
            // own messages are about a moment that has passed. The count is surfaced in
            // Live debug so this is never silent.
            if (Utils.GamePresence.ShouldHoldNotifications(out string holdReason))
            {
                SuppressedCount++;
                LastSuppressedReason = holdReason;
                // The dropped card is named as "<app>: <title>". It used to append the
                // title alone straight after the reason, which read as though the reason
                // named an app — "a fullscreen app is running: Notifications are working"
                // reported Tempo's own test message as the offending fullscreen app.
                Utils.Logger.Info("[Notify] card suppressed (" + holdReason + ") — dropped: " +
                                  (string.IsNullOrWhiteSpace(appName) ? "?" : appName.Trim()) +
                                  ": " + (title ?? "").Trim());

                // Dropping the CARD is right; dropping the message is not. This is the
                // one path where the user cannot possibly have seen it, so it is the
                // path the history exists for.
                Utils.NotificationHistory.Add(appName, title, body, kind.ToString(),
                    Utils.NotificationHistory.Outcome.Missed, holdReason);
                icon?.Dispose();
                if (hero != null && !ReferenceEquals(hero, icon)) { hero.Dispose(); }
                return null;
            }

            // Already saying exactly this? Count it on the card that is up rather than
            // stacking an identical twin.
            //
            // A warning that fires on a loop — a device that keeps dropping, an app that
            // re-notifies — used to produce a column of identical cards, each asking for
            // the same attention for the same fact, and each pushing the ones the user
            // had not read yet off the bottom of the stack. One card marked "×3" says
            // more and costs less. Only cards still on screen are considered, so a
            // message repeated minutes later is a genuinely new event and appears again.
            for (int i = 0; i < _active.Count; i++)
            {
                if (!_active[i].Matches(appName, title, body)) { continue; }
                _active[i].Repeat(DurationFor(title, body));
                RepeatsCollapsed++;
                Utils.NotificationHistory.Add(appName, title, body, kind.ToString(),
                    Utils.NotificationHistory.Outcome.Repeated);
                icon?.Dispose();
                if (hero != null && !ReferenceEquals(hero, icon)) { hero.Dispose(); }
                return _active[i];
            }

            // …and the same for a card still WAITING behind the visible stack. Collapsing
            // only against what was on screen meant a burst of one repeated warning filled
            // the queue with identical copies, which then arrived one at a time — the very
            // column of twins the collapsing exists to prevent, just delayed.
            for (int i = 0; i < _queue.Count; i++)
            {
                Pending q = _queue[i];
                if (!SameMessage(q.App, q.Title, q.Body, appName, title, body)) { continue; }
                q.Repeats++;
                _queue[i] = q;
                RepeatsCollapsed++;
                Utils.NotificationHistory.Add(appName, title, body, kind.ToString(),
                    Utils.NotificationHistory.Outcome.Repeated);
                icon?.Dispose();
                if (hero != null && !ReferenceEquals(hero, icon)) { hero.Dispose(); }
                return null;
            }

            if (_active.Count >= MaxVisible)
            {
                // A queued card is NOT written to the history yet: it is not yet known
                // whether it will be shown or dropped, and the entry carries the time it
                // was raised either way (see RaisedUtc), so nothing is lost by waiting.
                var pending = new Pending
                {
                    App = appName, Title = title, Body = body, Kind = kind,
                    Icon = icon, Hero = hero, OnActivate = onActivate,
                    RaisedUtc = DateTime.UtcNow, RaisedTick = Environment.TickCount64, Repeats = 1
                };

                // Full. Drop the OLDEST waiting card, not this one: in a burst the newest
                // message is the one that still describes the present.
                if (_queue.Count >= MaxQueued)
                {
                    Pending oldest = _queue[0];
                    _queue.RemoveAt(0);
                    DropPending(oldest, "a burst of notifications filled the queue");
                }
                _queue.Add(pending);
                return null;
            }

            // Recorded here rather than in SpawnCard so the entry is written at the moment
            // the card was raised — the history is about what Tempo had to say and when,
            // not about window management.
            Utils.NotificationHistory.Add(appName, title, body, kind.ToString(),
                Utils.NotificationHistory.Outcome.Shown);
            return SpawnCard(appName, title, body, kind, icon, hero, onActivate);
        }

        /// <summary>
        /// How long this card should stay up. The user's "Show (s)" is the FLOOR, not the
        /// whole story: a one-line "Screenshot copied" and a five-line chat message both
        /// vanishing after the same 5 s meant the long one was gone before it could be
        /// read. Extra time is granted by reading length (~200 wpm, the pace Windows
        /// paces its own toasts at), capped so nothing camps on screen.
        /// </summary>
        private int DurationFor(string title, string body)
        {
            int baseMs = _durationMs();
            try
            {
                int chars = (title ?? "").Length + (body ?? "").Length;
                // ~5.5 chars/word at 200 wpm ≈ 60 ms per character, plus a moment to
                // notice the card at all.
                int readMs = 700 + chars * 60;
                int want = Math.Max(baseMs, readMs);
                // Never more than 3× the chosen duration, and never past 20 s.
                return Math.Min(want, Math.Min(baseMs * 3, 20000));
            }
            catch { return baseMs; }
        }

        // When several cards land together their timers are nudged apart, so they expire
        // one after another instead of the whole stack blinking out at once.
        private long _lastSpawnTick;

        private NotificationToastForm SpawnCard(string appName, string title, string body, ToastKind kind, Image icon, Image hero, Action onActivate)
        {
            int ms = DurationFor(title, body);

            // Stagger: if another card appeared moments ago, hold this one a little
            // longer so the stack drains from the bottom up rather than all at once.
            long now = Environment.TickCount64;
            if (_active.Count > 0 && now - _lastSpawnTick < 1500)
            {
                ms += 900 * _active.Count;
                ms = Math.Min(ms, 25000);
            }
            _lastSpawnTick = now;

            var card = new NotificationToastForm(_theme(), appName, title, body, kind,
                                                 _corner(), ms, icon, hero, onActivate);

            // Only Tempo's own cards follow the animated logo. Mirrored cards carry the
            // sending app's icon and must keep it — replacing a Discord message's icon
            // with Tempo's is the exact bug the per-app icon lookup exists to prevent.
            // "Tempo" is the literal every internal caller passes; it is the product name,
            // not a translated string, so matching it here is stable.
            bool own = string.Equals(appName, "Tempo", StringComparison.Ordinal);
            card.AnimateAppIcon = own;
            // Only another app's card can be muted — a "mute Tempo" item on Tempo's own
            // card would switch off the thing the user is looking at, from the thing they
            // are looking at, with no obvious way back.
            if (!own) { card.MuteAppRequested = _muteApp; }
            card.Dismissed += OnCardDismissed;
            _active.Add(card);
            ShownCount++;

            // Place the new card at its resting X, then reflow so it slides in to the
            // slot nearest the corner (the others ease away to make room).
            var wa = TargetWorkArea();
            card.MoveTo(card.RestingX(wa), wa.Top, firstShow: true);
            card.Show();
            Reflow();
            return card;
        }

        /// <summary>
        /// The working area of the monitor the pop-ups belong on: the one showing the
        /// Tempo window, falling back to the primary screen. Using ONE screen for every
        /// card (instead of a per-card Screen.PrimaryScreen read) is what keeps the
        /// stack together on one display — the old code let cards scatter across
        /// monitors on a multi-screen setup.
        /// </summary>
        private Rectangle TargetWorkArea()
        {
            try
            {
                if (_owner != null && !_owner.IsDisposed && _owner.IsHandleCreated)
                {
                    // A MINIMIZED window reports bounds at (-32000,-32000), which would
                    // pick the wrong monitor — use its restore bounds instead so cards
                    // land on the screen the window actually lives on.
                    if (_owner.WindowState == FormWindowState.Minimized)
                    {
                        return Screen.FromRectangle(_owner.RestoreBounds).WorkingArea;
                    }
                    return Screen.FromControl(_owner).WorkingArea;
                }
            }
            catch { /* fall through to primary */ }
            return Screen.PrimaryScreen != null
                ? Screen.PrimaryScreen.WorkingArea
                : new Rectangle(0, 0, 1920, 1080);
        }

        private void OnCardDismissed(NotificationToastForm card)
        {
            _active.Remove(card);
            try { card.Dispose(); } catch { }
            Reflow();
            DrainQueue();
        }

        /// <summary>
        /// A slot freed up — release the next waiting card, if it is still worth showing.
        ///
        /// Both tests here were missing, and both matter most in the situation Tempo is
        /// built for. The queue was filled while five cards were up; by the time a slot
        /// frees, a game may have gone fullscreen — and the queue walked straight past the
        /// guard in ShowOrQueue and painted over it. And a card that has been waiting
        /// minutes is no longer news; it arrives looking like something that just happened.
        /// Dropped cards are written to the history with their ORIGINAL time and the reason.
        /// </summary>
        private void DrainQueue()
        {
            // Nothing may go on screen at all right now: drop the lot, exactly as a card
            // raised during a game is dropped rather than queued. Holding them would only
            // spill a burst of stale cards the moment the game closed.
            if (_queue.Count > 0 && Utils.GamePresence.ShouldHoldNotifications(out string holdReason))
            {
                SuppressedCount += _queue.Count;
                LastSuppressedReason = holdReason;
                for (int i = 0; i < _queue.Count; i++) { DropPending(_queue[i], holdReason); }
                _queue.Clear();
                return;
            }

            while (_queue.Count > 0 && _active.Count < MaxVisible)
            {
                Pending p = _queue[0];
                _queue.RemoveAt(0);

                long waited = Environment.TickCount64 - p.RaisedTick;
                if (waited > StaleAfterMs)
                {
                    DropPending(p, "it was still waiting " + (waited / 1000) + "s later");
                    continue;
                }

                Utils.NotificationHistory.Add(p.App, p.Title, p.Body, p.Kind.ToString(),
                    Utils.NotificationHistory.Outcome.Shown, "", p.RaisedUtc);
                NotificationToastForm spawned =
                    SpawnCard(p.App, p.Title, p.Body, p.Kind, p.Icon, p.Hero, p.OnActivate);

                // Twins that arrived while it waited become the "×3" badge, so the card
                // says what actually happened instead of arriving as a single event.
                if (spawned != null && p.Repeats > 1)
                {
                    for (int r = 1; r < p.Repeats; r++) { spawned.Repeat(DurationFor(p.Title, p.Body)); }
                }
                break;      // one card per freed slot; the next dismissal releases the next
            }
        }

        /// <summary>A waiting card that will never be shown: record it, free its bitmaps.</summary>
        private void DropPending(Pending p, string reason)
        {
            DroppedFromQueueCount++;
            LastSuppressedReason = reason;
            Utils.Logger.Info("[Notify] queued card dropped (" + reason + "): "
                              + (string.IsNullOrWhiteSpace(p.App) ? "?" : p.App.Trim())
                              + ": " + (p.Title ?? "").Trim());
            Utils.NotificationHistory.Add(p.App, p.Title, p.Body, p.Kind.ToString(),
                Utils.NotificationHistory.Outcome.Missed, reason, p.RaisedUtc);
            try { p.Icon?.Dispose(); } catch { }
            try { if (p.Hero != null && !ReferenceEquals(p.Hero, p.Icon)) { p.Hero.Dispose(); } } catch { }
        }

        private static bool SameMessage(string app1, string title1, string body1,
                                        string app2, string title2, string body2)
        {
            return string.Equals(app1 ?? "", app2 ?? "", StringComparison.Ordinal)
                   && string.Equals(title1 ?? "", title2 ?? "", StringComparison.Ordinal)
                   && string.Equals(body1 ?? "", body2 ?? "", StringComparison.Ordinal);
        }

        /// <summary>
        /// Lays the stack out from the chosen corner. Newest card sits nearest the
        /// corner; each older one is offset away by the card heights plus a gap. Cards
        /// ease toward their assigned Y, so removing one in the middle slides the rest
        /// up (or down) smoothly.
        /// </summary>
        private void Reflow()
        {
            var wa = TargetWorkArea();
            int corner = _corner();
            bool top = corner == 0 || corner == 1;
            const int margin = 18, gap = 14;   // a touch more breathing room between cards

            // Iterate newest → oldest so the newest is nearest the corner.
            int edge = top ? wa.Top + margin : wa.Bottom - margin;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var card = _active[i];

                // Push the LIVE corner onto every card first, so switching the corner
                // setting mid-flight migrates ALL on-screen cards together (RestingX and
                // the slide direction then agree with the stacking below). Without this,
                // older cards kept their creation-time corner and stranded in the old
                // one while new cards went to the new corner.
                card.Corner = corner;

                int x = card.RestingX(wa);
                int y;
                if (top)
                {
                    y = edge;
                    edge += card.Height + gap;
                }
                else
                {
                    edge -= card.Height;
                    y = edge;
                    edge -= gap;
                }

                // The just-added card already got its start position in SpawnCard; every
                // card just eases to the (possibly new-corner) slot assigned here.
                card.MoveTo(x, y, firstShow: false);
            }
        }

        /// <summary>
        /// Re-lays-out the on-screen cards immediately — call when the corner setting
        /// changes so any live pop-ups migrate to the new corner right away instead of
        /// waiting for the next notification. Safe from any thread.
        /// </summary>
        public void Relayout()
        {
            if (_disposed) { return; }
            try
            {
                if (_owner == null || _owner.IsDisposed) { return; }
                if (_owner.InvokeRequired) { _owner.BeginInvoke((Action)Reflow); }
                else { Reflow(); }
            }
            catch { /* owner tearing down */ }
        }

        public void Dispose()
        {
            _disposed = true;
            foreach (var c in _active.ToArray())
            {
                try { c.Dismissed -= OnCardDismissed; c.Close(); c.Dispose(); } catch { }
            }
            _active.Clear();
            foreach (var p in _queue)
            {
                try { p.Icon?.Dispose(); } catch { }
                try { if (p.Hero != null && !ReferenceEquals(p.Hero, p.Icon)) { p.Hero.Dispose(); } } catch { }
            }
            _queue.Clear();
        }
    }
}
