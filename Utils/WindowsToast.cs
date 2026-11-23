using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace AutoClicker.Utils
{
    /// <summary>
    /// Tempo's notifications as real Windows toasts when its own cards are switched off (round 39) — no longer tray
    /// balloons, whenever Windows knows Tempo by a Start-menu shortcut.
    ///
    /// A balloon's header is not Tempo's icon at all. Explorer turns a balloon into a toast headed by a picture of the
    /// tray icon that it took the FIRST time it saw that icon from this program — kept as IconSnapshot under
    /// HKCU\Control Panel\NotifyIconSettings — and never takes again. Measured with a stand-in program
    /// (scratchpad/traysnap): a new tray icon, the icon taken down and put back, even the stored picture rewritten —
    /// every header stayed the first picture. On the user's PC that was a cat from September, under a logo set in
    /// November ("Windows Notification shows old gif or picture not even show new gif or picture").
    ///
    /// A toast under the identity Windows already has for Tempo — the Start-menu shortcut, whose AppID is the exe's
    /// path (Get-StartApps) — is headed by that shortcut's icon and name, which <see cref="ShellLogo"/> keeps on the
    /// current logo (measured: the next toast's header followed a shortcut's new icon at once). The logo also goes in as
    /// the toast's picture. With no such shortcut (a portable copy) a toast would be dropped without a word, so the
    /// balloon stays. A click does what the balloon's click did.
    /// </summary>
    public static class WindowsToast
    {
        // Shown toasts, kept so their Activated handlers stay alive until the user can no longer click them.
        private static readonly Queue<Windows.UI.Notifications.ToastNotification> Recent = new Queue<Windows.UI.Notifications.ToastNotification>();
        private static readonly object Gate = new object();
        private static bool _offNoted, _failNoted;

        /// <summary>True when a toast can be Tempo's: a Start-menu shortcut opens this copy (ShellLogo found one).</summary>
        public static bool CanShow => ShellLogo.ToastAppId != null;

        /// <summary>How long a toast stays in the notification centre: Tempo's notices are about now, not tomorrow.</summary>
        internal static readonly TimeSpan KeepFor = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Shows one toast. True when Windows took it — or when Windows has Tempo's notifications switched off (the
        /// user's choice there: nothing is shown, and no balloon goes round it). False when it could not be sent, for
        /// the caller to show the balloon instead. Any thread.
        /// </summary>
        public static bool Show(string title, string body, Action onClick)
        {
            string appId = ShellLogo.ToastAppId;
            if (appId == null) { return false; }
            try
            {
                var doc = new Windows.Data.Xml.Dom.XmlDocument();
                doc.LoadXml(Xml(title, body, ShellLogo.Picture));
                var toast = new Windows.UI.Notifications.ToastNotification(doc)
                {
                    Tag = Tag(title, body),
                    Group = "tempo",
                    ExpirationTime = DateTimeOffset.Now.Add(KeepFor),
                    ExpiresOnReboot = true,
                };
                if (onClick != null)
                {
                    toast.Activated += (s, e) =>
                    {
                        try { onClick(); }
                        catch (Exception ex) { Logger.Swallow("WindowsToast.Activated", ex); }
                    };
                }

                var notifier = Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier(appId);
                Windows.UI.Notifications.NotificationSetting setting = Windows.UI.Notifications.NotificationSetting.Enabled;
                try { setting = notifier.Setting; } catch { }
                if (setting != Windows.UI.Notifications.NotificationSetting.Enabled)
                {
                    if (!_offNoted)
                    {
                        _offNoted = true;
                        Logger.Info("[Notify] Windows has Tempo's notifications turned off (" + setting + ") — not shown, as Windows was asked.");
                    }
                    return true;
                }
                notifier.Show(toast);
                lock (Gate)
                {
                    Recent.Enqueue(toast);
                    while (Recent.Count > 12) { Recent.Dequeue(); }
                }
                return true;
            }
            catch (Exception ex)
            {
                if (!_failNoted)
                {
                    _failNoted = true;
                    Logger.Warn("[Notify] a Windows toast could not be shown (" + ex.Message + ") — the tray balloon instead.");
                }
                return false;
            }
        }

        /// <summary>The toast's XML: title, body, and the logo as its picture.</summary>
        public static string Xml(string title, string body, string picture)
        {
            var sb = new StringBuilder();
            sb.Append("<toast><visual><binding template=\"ToastGeneric\">");
            sb.Append("<text>").Append(SecurityElement.Escape(title ?? "")).Append("</text>");
            if (!string.IsNullOrWhiteSpace(body)) { sb.Append("<text>").Append(SecurityElement.Escape(body)).Append("</text>"); }
            if (!string.IsNullOrEmpty(picture) && File.Exists(picture))
            {
                sb.Append("<image placement=\"appLogoOverride\" src=\"")
                  .Append(SecurityElement.Escape(new Uri(picture).AbsoluteUri)).Append("\"/>");
            }
            sb.Append("</binding></visual></toast>");
            return sb.ToString();
        }

        /// <summary>The same notice again replaces the first rather than stacking under it.</summary>
        private static string Tag(string title, string body) =>
            "t" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((title ?? "") + "\n" + (body ?? ""))), 0, 8).ToLowerInvariant();
    }
}
