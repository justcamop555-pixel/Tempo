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
        private readonly Queue<Pending> _queue = new Queue<Pending>();
        private const int MaxVisible = 5;
        private bool _disposed;

        private struct Pending
        {
            public string App, Title, Body;
            public ToastKind Kind;
            public Image Icon;
            public Image Hero;
            public Action OnActivate;
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

        public NotificationCenter(Form owner, Func<Theme> theme, Func<int> corner, Func<int> durationMs)
        {
            _owner = owner;
            _theme = theme ?? (() => Theme.ForKind(Models.ThemeKind.Dark));
            _corner = corner ?? (() => 0);
            _durationMs = durationMs ?? (() => 5000);
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
                icon?.Dispose();
                if (hero != null && !ReferenceEquals(hero, icon)) { hero.Dispose(); }
                return _active[i];
            }

            if (_active.Count >= MaxVisible)
            {
                _queue.Enqueue(new Pending { App = appName, Title = title, Body = body, Kind = kind, Icon = icon, Hero = hero, OnActivate = onActivate });
                return null;
            }
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

            // A slot freed up — release the next queued toast, if any.
            if (_queue.Count > 0 && _active.Count < MaxVisible)
            {
                var p = _queue.Dequeue();
                SpawnCard(p.App, p.Title, p.Body, p.Kind, p.Icon, p.Hero, p.OnActivate);
            }
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
