using System;

namespace AutoClicker.Models
{
    /// <summary>
    /// One account in the Roblox Account Manager: the details you keep for a Roblox login,
    /// plus the Tempo setup that goes with it.
    ///
    /// This one CAN hold credentials — a password and/or a <c>.ROBLOSECURITY</c> cookie —
    /// because that is what the user asked the manager to be. That is a serious
    /// responsibility, so those two fields are treated as secrets everywhere:
    ///
    ///   * They only ever live inside the encrypted vault (see <see cref="AutoClicker.Persistence.AccountVault"/>
    ///     and <see cref="AutoClicker.Persistence.VaultCrypto"/>). The file on disk is AES-256-GCM
    ///     under a key from the user's master password, wrapped again in Windows DPAPI. Nothing
    ///     here is written anywhere in the clear.
    ///   * They are NEVER logged, never put in a crash report, never sent anywhere. Tempo does
    ///     not contact Roblox with them; there is no automatic sign-in and no automatic signup.
    ///     Launching an account stays a manual step the user drives themselves.
    ///
    /// Everything else (label, username, alias, notes, the linked Tempo profile/macro and a
    /// game link) is ordinary organiser data and shares the same encrypted file for simplicity.
    /// </summary>
    public sealed class RobloxAccount
    {
        /// <summary>How this account's session was obtained.</summary>
        public const string ViaLogin = "Login";     // captured from a real sign-in (the normal path)
        public const string ViaUserPass = "UserPass";
        public const string ViaCookie = "Cookie";
        public const string ViaManual = "Manual";

        /// <summary>Your own label for this account — unique within the vault.</summary>
        public string Name { get; set; } = "Account";

        /// <summary>The Roblox username.</summary>
        public string Username { get; set; } = "";

        /// <summary>An optional alias / nickname, shown alongside the username.</summary>
        public string Alias { get; set; } = "";

        /// <summary>The numeric Roblox user ID, if you want to keep it. Display only.</summary>
        public string UserId { get; set; } = "";

        /// <summary>Free text: what this account is for. Shown as the "Description" column.</summary>
        public string Note { get; set; } = "";

        /// <summary>Emoji shown beside the row.</summary>
        public string Icon { get; set; } = "\U0001F3AE";

        /// <summary>
        /// SECRET. The account password, if stored. Lives only inside the encrypted vault and
        /// is never logged or transmitted. May be empty for a cookie-only or label-only entry.
        /// </summary>
        public string Password { get; set; } = "";

        /// <summary>
        /// SECRET. The <c>.ROBLOSECURITY</c> session cookie, if stored. Same protection as
        /// <see cref="Password"/>. Holding this IS being signed in, so it never leaves the vault.
        /// </summary>
        public string Cookie { get; set; } = "";

        /// <summary>One of <see cref="ViaUserPass"/>/<see cref="ViaCookie"/>/<see cref="ViaManual"/>.</summary>
        public string AddedVia { get; set; } = ViaManual;

        /// <summary>The Tempo profile to activate with this account, or empty for none.</summary>
        public string ProfileName { get; set; } = "";

        /// <summary>A macro to select with this account, or empty for none.</summary>
        public string MacroName { get; set; } = "";

        /// <summary>A roblox.com link opened in your browser — Roblox does the signing in there.</summary>
        public string GameUrl { get; set; } = "";

        public bool Favorite { get; set; }
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime? LastUsedUtc { get; set; }
        public int TimesUsed { get; set; }

        /// <summary>True if this entry carries either stored secret.</summary>
        public bool HasSecret =>
            !string.IsNullOrEmpty(Password) || !string.IsNullOrEmpty(Cookie);

        public RobloxAccount()
        {
        }

        public RobloxAccount(string name)
        {
            Name = name;
        }

        public RobloxAccount Clone()
        {
            return new RobloxAccount
            {
                Name = Name,
                Username = Username,
                Alias = Alias,
                UserId = UserId,
                Note = Note,
                Icon = Icon,
                Password = Password,
                Cookie = Cookie,
                AddedVia = AddedVia,
                ProfileName = ProfileName,
                MacroName = MacroName,
                GameUrl = GameUrl,
                Favorite = Favorite,
                CreatedUtc = CreatedUtc,
                LastUsedUtc = LastUsedUtc,
                TimesUsed = TimesUsed
            };
        }

        public override string ToString() => Name;
    }
}
