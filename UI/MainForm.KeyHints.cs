using AutoClicker.Models;
using AutoClicker.Native;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// Which key does what RIGHT NOW, in words — one answer for every place that tells the user.
    ///
    /// Six places named a key for the clicker and they disagreed. With the default bindings the Start
    /// button, the status bar and the footer said F6 (Start / Stop), the on-screen badge said "Press F8
    /// to stop" (Emergency stop: its lookup skipped the Start / Stop key its own comment named), and the
    /// footer said F6 even after F6 was rebound or unbound, because it was typed into the string. The
    /// status bar said "Paused — F6 to resume" while F6 STOPS a paused run (the Stop button beside it
    /// said so correctly) — resuming is Pause / Resume, F9. In Hold-to-click mode the Start / Stop key
    /// is held, not pressed, and a key Windows refused to bind was still advertised as if it worked.
    /// </summary>
    public partial class MainForm
    {
        /// <summary>The Clicker tab's footer line, which names the start / stop keys.</summary>
        private System.Windows.Forms.Label _clickerTipLabel;

        private bool HoldModeSelected => GetSelectedMode() == ClickMode.HoldToClick;

        /// <summary>
        /// The key bound to <paramref name="action"/> as it is shown ("F6", "Strg+F6"), or null when that
        /// action has no key that would actually do anything.
        /// </summary>
        private string LiveKeyText(HotkeyAction action)
        {
            HotkeyDefinition hk = _settings?.HotkeyFor(action);
            if (hk == null || !hk.IsValid) { return null; }

            // Hold-to-click polls the Start / Stop key itself: it is live whether or not it is registered.
            bool polled = action == HotkeyAction.ToggleStartStop && HoldModeSelected;
            // Asleep in the tray, the key comes back on waking — it is not broken, so still name it.
            if (!polled && !_traySleepActive && _hotkeys != null
                && _hotkeys.RouteOf(action.ToString()) == GlobalHotkeyManager.BindRoute.None)
            {
                return null;   // set in the Keybinds tab, but nothing delivers it: don't advertise a dead key
            }
            return hk.ToDisplayString().Replace(" + ", "+");
        }

        /// <summary>The key that starts clicking by a press, or null. (Hold-to-click has its own words.)</summary>
        private string StartKeyText()
        {
            return LiveKeyText(HotkeyAction.ToggleStartStop) ?? LiveKeyText(HotkeyAction.StartClicking);
        }

        /// <summary>
        /// The key that stops clicking, or null: Stop clicking if it has one, else the Start / Stop key, else
        /// Emergency stop. In Hold-to-click the Start / Stop key stops by being RELEASED, so it is not offered
        /// here — callers say "release" for that case themselves.
        /// </summary>
        private string StopKeyText()
        {
            string stop = LiveKeyText(HotkeyAction.StopClicking);
            if (stop != null) { return stop; }
            if (!HoldModeSelected)
            {
                string toggle = LiveKeyText(HotkeyAction.ToggleStartStop);
                if (toggle != null) { return toggle; }
            }
            return LiveKeyText(HotkeyAction.EmergencyStop);
        }

        /// <summary>The hold trigger in Hold-to-click mode, or null when none is set (hold mode cannot engage).</summary>
        private string HoldKeyText()
        {
            return HoldModeSelected ? LiveKeyText(HotkeyAction.ToggleStartStop) : null;
        }

        /// <summary>
        /// The small third line on the clicking badge. Hold-to-click stops when the key is let go, which is
        /// what that user is doing with their hand; every other mode names the stop key.
        /// </summary>
        private string ClickingBadgeHint()
        {
            string hold = HoldKeyText();
            if (hold != null) { return Localization.F("Release {0} to stop", hold); }
            string stop = StopKeyText();
            return stop != null ? Localization.F("Press {0} to stop", stop) : null;
        }

        /// <summary>The Clicker tab's footer tip, from the live bindings instead of a typed-in F6.</summary>
        private string ClickerFooterTip()
        {
            string tip;
            if (HoldModeSelected)
            {
                string hold = HoldKeyText();
                tip = hold != null
                    ? Localization.F("Tip: hold {0} to click", hold)
                    : Localization.T("Tip: Hold mode needs a Start / Stop key — set one on the Keybinds tab");
            }
            else
            {
                string start = StartKeyText();
                string stop = StopKeyText();
                if (start != null && start == stop) { tip = Localization.F("Tip: press {0} to start and stop", start); }
                else if (start != null && stop != null) { tip = Localization.F("Tip: {0} starts, {1} stops", start, stop); }
                else if (start != null) { tip = Localization.F("Tip: press {0} to start", start); }
                else { tip = Localization.T("Tip: set a Start / Stop key on the Keybinds tab"); }
            }
            return "ⓘ  " + tip + "    •    " + Localization.T("All times in milliseconds");
        }

        /// <summary>
        /// The Start button's words while idle. In Hold mode it names the key to HOLD: a Start button
        /// cannot hold a key, and pressing it used to start a run the 15 ms hold poll stopped at once —
        /// "▶ Start · F6" that visibly did nothing.
        /// </summary>
        private string IdleStartButtonText()
        {
            if (HoldModeSelected)
            {
                string hold = HoldKeyText();
                return hold != null
                    ? "▶ " + Localization.F("Hold {0}", hold)
                    : "▶ " + Localization.T("Start");
            }
            // Always the key when there is one, as before: the layout fitter makes a longer translation
            // fit at start-up (it widens into free space, then steps the font down), and deciding here
            // from the unfitted width would have taken F6 off the Spanish and French buttons.
            string start = StartKeyText();
            return "▶ " + Localization.T("Start") + (start != null ? " · " + start : "");
        }

        /// <summary>
        /// Start pressed in Hold mode: say how that mode starts, instead of starting a run the hold poll
        /// stops again within 15 ms. True when it was handled here.
        /// </summary>
        private bool ExplainHoldModeStart()
        {
            if (!HoldModeSelected || (_engine != null && _engine.IsRunning)) { return false; }
            string hold = HoldKeyText();
            ShowInfo(hold != null
                ? Localization.F("In Hold mode Tempo clicks only while you hold {0} — hold it wherever you want the clicks. The Start button can't hold it for you.", hold)
                : Localization.T("In Hold mode Tempo clicks only while you hold the Start / Stop key, and none is set. Set one on the Keybinds tab."));
            return true;
        }
    }
}
