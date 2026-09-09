using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoClicker.Models;
using AutoClicker.Utils;

namespace AutoClicker.Persistence
{
    /// <summary>Locked/unlocked/absent — the three states the Accounts page switches on.</summary>
    public enum VaultState
    {
        /// <summary>No vault file exists yet; the user has to set a master password first.</summary>
        NoVault,
        /// <summary>A vault exists on disk but is not open; the master password is needed.</summary>
        Locked,
        /// <summary>Open: the key is in memory and the accounts are loaded.</summary>
        Unlocked
    }

    /// <summary>
    /// The encrypted store behind the Roblox Account Manager. Same CRUD shape as the other
    /// Tempo stores so the page code reads the same, but with a lock on the front: the file
    /// (<c>accounts.vault</c>) is meaningless until <see cref="Unlock"/> succeeds, and the
    /// list is empty and the secrets unreachable until then.
    ///
    /// On disk it is the Base64 of <see cref="VaultCrypto.Seal"/>'s output — AES-256-GCM under
    /// the master-password key, wrapped in DPAPI — written through the same atomic writer (and
    /// therefore the same <c>.1</c> previous-copy safety net) as every other store. The 32-byte
    /// key is held only while unlocked and is zeroed the moment the vault locks.
    /// </summary>
    public sealed class AccountVault
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = false
        };

        private readonly List<RobloxAccount> _accounts = new List<RobloxAccount>();

        private byte[] _key;      // 32 bytes while Unlocked; null otherwise
        private byte[] _salt;     // the vault's PBKDF2 salt, reused across re-saves
        private int _iterations;

        public VaultState State { get; private set; } = VaultState.NoVault;

        /// <summary>The accounts — empty unless the vault is <see cref="VaultState.Unlocked"/>.</summary>
        public IReadOnlyList<RobloxAccount> Accounts => _accounts;

        public bool IsUnlocked => State == VaultState.Unlocked;

        public static string GetVaultPath()
        {
            return Path.Combine(SettingsManager.GetSettingsDirectory(), "accounts.vault");
        }

        /// <summary>
        /// Work out the state from what is on disk, without touching the key. Call at startup
        /// and whenever the page is shown; it never moves an already-<see cref="VaultState.Unlocked"/>
        /// vault backwards.
        /// </summary>
        public void Refresh()
        {
            if (State == VaultState.Unlocked)
            {
                return;
            }
            State = File.Exists(GetVaultPath()) ? VaultState.Locked : VaultState.NoVault;
        }

        /// <summary>
        /// Create a brand-new, empty vault protected by <paramref name="masterPassword"/> and
        /// leave it unlocked. Only valid from <see cref="VaultState.NoVault"/>.
        /// </summary>
        public bool CreateNew(string masterPassword)
        {
            if (State == VaultState.Locked)
            {
                return false; // a vault already exists — must be unlocked, not recreated
            }
            _salt = VaultCrypto.NewSalt();
            _iterations = VaultCrypto.DefaultIterations;
            ReplaceKey(VaultCrypto.DeriveKey(masterPassword, _salt, _iterations));
            _accounts.Clear();
            State = VaultState.Unlocked;
            bool ok = Save();
            if (ok) { Logger.Info("[Vault] created a new account vault."); }
            return ok;
        }

        /// <summary>
        /// Open the vault with <paramref name="masterPassword"/>. On failure the state is left
        /// as it was and <paramref name="error"/> explains which lock refused (wrong password,
        /// wrong Windows account, or a damaged file).
        /// </summary>
        public bool Unlock(string masterPassword, out string error)
        {
            error = null;
            byte[] disk;
            try
            {
                string b64 = File.ReadAllText(GetVaultPath());
                disk = Convert.FromBase64String(b64.Trim());
            }
            catch (Exception ex)
            {
                Logger.Error("[Vault] could not read the vault file.", ex);
                error = Localization.T("The vault file could not be read.");
                return false;
            }

            VaultCrypto.Opened opened;
            try
            {
                opened = VaultCrypto.Open(disk, masterPassword);
            }
            catch (VaultWrongPasswordException)
            {
                error = Localization.T("That master password is not right.");
                return false;
            }
            catch (VaultForeignException)
            {
                error = Localization.T(
                    "This vault belongs to a different Windows account, or was copied from another PC. "
                    + "It can only be opened by the Windows user that created it.");
                return false;
            }
            catch (VaultCorruptException)
            {
                error = Localization.T("The vault file is damaged and could not be opened.");
                return false;
            }

            try
            {
                LoadFromPlaintext(opened.Plaintext);
                _salt = opened.Salt;
                _iterations = opened.Iterations;
                ReplaceKey(opened.Key);
                State = VaultState.Unlocked;
                Logger.Info("[Vault] unlocked; " + _accounts.Count + " account(s).");
                return true;
            }
            catch (Exception ex)
            {
                // Decryption worked but the contents did not parse. Fail closed and stay locked.
                Logger.Error("[Vault] decrypted contents could not be read.", ex);
                CryptographicOperations.ZeroMemory(opened.Key);
                error = Localization.T("The vault opened but its contents could not be read.");
                return false;
            }
            finally
            {
                if (opened.Plaintext != null) { CryptographicOperations.ZeroMemory(opened.Plaintext); }
            }
        }

        /// <summary>Close the vault: scrub the key and drop every loaded account from memory.</summary>
        public void Lock()
        {
            ReplaceKey(null);
            _salt = null;
            _iterations = 0;
            _accounts.Clear();
            State = File.Exists(GetVaultPath()) ? VaultState.Locked : VaultState.NoVault;
            Logger.Info("[Vault] locked.");
        }

        /// <summary>Re-seal the current accounts to disk. Requires the vault be unlocked.</summary>
        public bool Save()
        {
            if (State != VaultState.Unlocked || _key == null)
            {
                return false;
            }
            byte[] plaintext = null;
            try
            {
                plaintext = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_accounts, Options));
                byte[] disk = VaultCrypto.Seal(plaintext, _key, _salt, _iterations);
                return PersistenceHelper.WriteAtomic(GetVaultPath(), Convert.ToBase64String(disk));
            }
            catch (Exception ex)
            {
                Logger.Error("[Vault] failed to save.", ex);
                return false;
            }
            finally
            {
                if (plaintext != null) { CryptographicOperations.ZeroMemory(plaintext); }
            }
        }

        /// <summary>
        /// Change the master password. Verifies <paramref name="current"/> against the file on
        /// disk (not just the in-memory key), then re-keys with a fresh salt and re-saves.
        /// </summary>
        public bool ChangeMasterPassword(string current, string next, out string error)
        {
            error = null;
            if (State != VaultState.Unlocked)
            {
                error = Localization.T("Unlock the vault first.");
                return false;
            }
            if (string.IsNullOrEmpty(next))
            {
                error = Localization.T("Choose a new master password.");
                return false;
            }

            // Prove the caller knows the current password before letting them replace it.
            try
            {
                string b64 = File.ReadAllText(GetVaultPath());
                var check = VaultCrypto.Open(Convert.FromBase64String(b64.Trim()), current);
                CryptographicOperations.ZeroMemory(check.Key);
                CryptographicOperations.ZeroMemory(check.Plaintext);
            }
            catch (VaultWrongPasswordException)
            {
                error = Localization.T("The current master password is not right.");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error("[Vault] could not verify the current password.", ex);
                error = Localization.T("The current password could not be checked.");
                return false;
            }

            _salt = VaultCrypto.NewSalt();
            _iterations = VaultCrypto.DefaultIterations;
            ReplaceKey(VaultCrypto.DeriveKey(next, _salt, _iterations));
            bool ok = Save();
            if (ok) { Logger.Info("[Vault] master password changed."); }
            else { error = Localization.T("The vault could not be re-saved."); }
            return ok;
        }

        /// <summary>
        /// Delete the vault file entirely (the "I forgot my password, start over" path). This
        /// destroys every stored account — there is no recovery, by design — so the caller must
        /// confirm first. Leaves the vault in <see cref="VaultState.NoVault"/>.
        /// </summary>
        public bool DeleteVaultFile()
        {
            try
            {
                ReplaceKey(null);
                _salt = null;
                _iterations = 0;
                _accounts.Clear();
                string path = GetVaultPath();
                if (File.Exists(path)) { File.Delete(path); }
                string prev = path + ".1";
                if (File.Exists(prev)) { File.Delete(prev); }
                State = VaultState.NoVault;
                Logger.Info("[Vault] vault file deleted at the user's request.");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("[Vault] could not delete the vault file.", ex);
                return false;
            }
        }

        // ── CRUD (only meaningful while unlocked) ───────────────────────────────

        public RobloxAccount GetByName(string name)
        {
            return _accounts.FirstOrDefault(a =>
                string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public RobloxAccount Add(RobloxAccount account)
        {
            if (account == null) { throw new ArgumentNullException(nameof(account)); }
            account.Name = MakeUniqueName(account.Name);
            _accounts.Add(account);
            return account;
        }

        public bool Remove(string name)
        {
            var existing = GetByName(name);
            if (existing == null) { return false; }
            // Scrub the secrets of the removed entry before dropping the reference.
            existing.Password = "";
            existing.Cookie = "";
            _accounts.Remove(existing);
            return true;
        }

        public bool Rename(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) { return false; }
            var existing = GetByName(oldName);
            if (existing == null) { return false; }
            var clash = GetByName(newName);
            if (clash != null && !ReferenceEquals(clash, existing)) { return false; }
            existing.Name = newName.Trim();
            return true;
        }

        public bool Move(string name, int delta)
        {
            var a = GetByName(name);
            if (a == null || delta == 0) { return false; }
            int i = _accounts.IndexOf(a);
            int j = i + delta;
            if (j < 0 || j >= _accounts.Count) { return false; }
            _accounts.RemoveAt(i);
            _accounts.Insert(j, a);
            return true;
        }

        /// <summary>
        /// Reorder for drag-and-drop: move the named account to <paramref name="targetIndex"/> — the
        /// insertion point (0..Count) measured against the list AS IT STANDS NOW (i.e. "drop it before
        /// the item currently at targetIndex"; Count = drop at the very end). Returns false if the name
        /// isn't found or the drop wouldn't change the order.
        /// </summary>
        public bool MoveTo(string name, int targetIndex)
        {
            var a = GetByName(name);
            if (a == null) { return false; }
            int cur = _accounts.IndexOf(a);
            if (cur < 0) { return false; }
            // Pulling the item out shifts everything after it down one, so a target past the old slot
            // moves back by one. Dropping on itself (or just after itself) is a no-op.
            int insertAt = targetIndex > cur ? targetIndex - 1 : targetIndex;
            if (insertAt < 0) { insertAt = 0; }
            if (insertAt > _accounts.Count - 1) { insertAt = _accounts.Count - 1; }
            if (insertAt == cur) { return false; }
            _accounts.RemoveAt(cur);
            _accounts.Insert(insertAt, a);
            return true;
        }

        // ── internals ───────────────────────────────────────────────────────────

        private void LoadFromPlaintext(byte[] plaintext)
        {
            _accounts.Clear();
            string json = Encoding.UTF8.GetString(plaintext);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }
            var loaded = JsonSerializer.Deserialize<List<RobloxAccount>>(json, Options);
            if (loaded == null)
            {
                return;
            }
            foreach (var a in loaded)
            {
                if (a == null) { continue; }
                if (string.IsNullOrWhiteSpace(a.Name)) { a.Name = "Account"; }
                a.Name = MakeUniqueName(a.Name);
                _accounts.Add(a);
            }
        }

        private void ReplaceKey(byte[] newKey)
        {
            if (_key != null) { CryptographicOperations.ZeroMemory(_key); }
            _key = newKey;
        }

        private string MakeUniqueName(string wanted)
        {
            string baseName = string.IsNullOrWhiteSpace(wanted) ? "Account" : wanted.Trim();
            string name = baseName;
            int n = 2;
            while (_accounts.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                name = baseName + " " + n++;
            }
            return name;
        }
    }
}
