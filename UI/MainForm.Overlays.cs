using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AutoClicker.Models;
using AutoClicker.Utils;

namespace AutoClicker.UI
{
    /// <summary>
    /// The running badges — clicking, playing a macro, recording one — and the tray's Overlays menu that
    /// moves and fades them.
    ///
    /// They could not be moved at all: the clicking badge and the macro badge went to one of six corners
    /// chosen in Settings, and the recording badge always sat in the main screen's top-right. Wherever
    /// that was, it could be on top of exactly the part of a game someone needed to see. Now each can be
    /// dragged where it is wanted (and stays there), and all three follow one opacity.
    /// </summary>
    public partial class MainForm
    {
        private enum OverlayBadge { Clicking, Macro, Recording }

        /// <summary>True while the badges can be dragged (tray ▸ Overlays ▸ Move overlays).</summary>
        private bool _overlayMoveMode;

        // Stand-ins put up only so a badge can be placed when it is not showing (nothing running). They
        // go away when moving ends; the real badges stay as they are.
        private ClickingIndicatorForm _clickingPreview;
        private ClickingIndicatorForm _macroPreview;
        private RecordingIndicatorForm _recordPreview;

        private ToolStripMenuItem _trayOverlaysItem;
        private ToolStripMenuItem _trayOverlayMoveItem;
        private ToolStripMenuItem _trayOverlayShowItem;
        private ToolStripMenuItem _trayOverlayOpacityItem;
        private ToolStripMenuItem _trayOverlayResetItem;

        /// <summary>The opacities the tray offers (the badge accepts 40–100 %).</summary>
        private static readonly int[] OverlayOpacities = { 100, 90, 80, 70, 60, 50, 40 };

        // ── Where each badge was put ──────────────────────────────────────────────────────────────

        private Point? OverlayPlace(OverlayBadge which)
        {
            if (_settings == null) { return null; }
            int x, y;
            switch (which)
            {
                case OverlayBadge.Clicking: x = _settings.ClickingOverlayX; y = _settings.ClickingOverlayY; break;
                case OverlayBadge.Macro: x = _settings.MacroOverlayX; y = _settings.MacroOverlayY; break;
                default: x = _settings.RecordingOverlayX; y = _settings.RecordingOverlayY; break;
            }
            return x == AppSettings.OverlayUnplaced || y == AppSettings.OverlayUnplaced ? (Point?)null : new Point(x, y);
        }

        private void SaveOverlayPlace(OverlayBadge which, Point? at)
        {
            if (_settings == null) { return; }
            int x = at.HasValue ? at.Value.X : AppSettings.OverlayUnplaced;
            int y = at.HasValue ? at.Value.Y : AppSettings.OverlayUnplaced;
            switch (which)
            {
                case OverlayBadge.Clicking: _settings.ClickingOverlayX = x; _settings.ClickingOverlayY = y; break;
                case OverlayBadge.Macro: _settings.MacroOverlayX = x; _settings.MacroOverlayY = y; break;
                default: _settings.RecordingOverlayX = x; _settings.RecordingOverlayY = y; break;
            }
            try { Persistence.SettingsManager.Save(_settings); } catch (Exception ex) { Logger.Swallow("SaveOverlayPlace", ex); }
            Logger.Info("[Overlays] " + which + " badge " + (at.HasValue ? "placed at " + x + "," + y : "back to its corner") + ".");
        }

        /// <summary>
        /// Everything a clicking or macro badge needs from the settings beyond its look: where it was
        /// put, and that it can be moved while move mode is on. Called once, as it is made.
        /// </summary>
        private void WireBadge(ClickingIndicatorForm badge, OverlayBadge which)
        {
            if (badge == null) { return; }
            badge.FreePosition = OverlayPlace(which);
            badge.Moved += at => SaveOverlayPlace(which, at);
            badge.MoveDone += () => SetOverlayMoveMode(false);
            if (_overlayMoveMode) { badge.SetMovable(true); }
        }

        private void WireBadge(RecordingIndicatorForm badge)
        {
            if (badge == null) { return; }
            badge.FreePosition = OverlayPlace(OverlayBadge.Recording);
            badge.SetOpacity(_settings != null ? _settings.OverlayOpacity : 96);
            badge.Moved += at => SaveOverlayPlace(OverlayBadge.Recording, at);
            badge.MoveDone += () => SetOverlayMoveMode(false);
            if (_overlayMoveMode) { badge.SetMovable(true); }
        }

        /// <summary>A real badge just came up while moving: its stand-in is no longer needed.</summary>
        private void DropOverlayPreview(OverlayBadge which)
        {
            switch (which)
            {
                case OverlayBadge.Clicking: CloseQuietly(_clickingPreview); _clickingPreview = null; break;
                case OverlayBadge.Macro: CloseQuietly(_macroPreview); _macroPreview = null; break;
                default: CloseQuietly(_recordPreview); _recordPreview = null; break;
            }
        }

        private static void CloseQuietly(Form f)
        {
            try { if (f != null && !f.IsDisposed) { f.Close(); } } catch { }
        }

        // ── Move mode ─────────────────────────────────────────────────────────────────────────────

        private void ToggleOverlayMoveMode() => SetOverlayMoveMode(!_overlayMoveMode);

        /// <summary>
        /// Lets every badge be dragged — the ones on screen, and a stand-in for each that is not, so all
        /// three can be placed without starting a run, a playback and a recording first. Double-clicking
        /// any of them (or the tray switch again) ends it; each badge keeps where it was put.
        /// </summary>
        private void SetOverlayMoveMode(bool on)
        {
            if (_overlayMoveMode == on) { return; }
            _overlayMoveMode = on;
            try
            {
                if (on)
                {
                    var shown = new List<Form>();
                    if (IsUp(_clickingIndicator)) { _clickingIndicator.SetMovable(true); shown.Add(_clickingIndicator); }
                    else { shown.Add(ShowOverlayPreview(OverlayBadge.Clicking)); }
                    if (IsUp(_macroIndicator)) { _macroIndicator.SetMovable(true); shown.Add(_macroIndicator); }
                    else { shown.Add(ShowOverlayPreview(OverlayBadge.Macro)); }
                    if (IsUp(_recordIndicator)) { _recordIndicator.SetMovable(true); shown.Add(_recordIndicator); }
                    else { shown.Add(ShowOverlayPreview(OverlayBadge.Recording)); }
                    shown.RemoveAll(f => f == null);
                    SpreadOverlappingBadges(shown);
                }
                else
                {
                    foreach (Form f in new Form[] { _clickingIndicator, _macroIndicator, _recordIndicator })
                    {
                        if (f is ClickingIndicatorForm c && !c.IsDisposed) { c.SetMovable(false); }
                        if (f is RecordingIndicatorForm r && !r.IsDisposed) { r.SetMovable(false); }
                    }
                    DropOverlayPreview(OverlayBadge.Clicking);
                    DropOverlayPreview(OverlayBadge.Macro);
                    DropOverlayPreview(OverlayBadge.Recording);
                }
            }
            catch (Exception ex) { Logger.Swallow("SetOverlayMoveMode", ex); }
            if (_trayOverlayMoveItem != null) { _trayOverlayMoveItem.Checked = _overlayMoveMode; }
            Logger.Info("[Overlays] move mode " + (on ? "on" : "off") + ".");
        }

        /// <summary>
        /// Puts up the stand-in for one badge (movable, where it was last put), unless there already is
        /// one. Also called when a real badge goes away while moving — a run stopped, a macro finished —
        /// so there is still something there to place.
        /// </summary>
        private Form ShowOverlayPreview(OverlayBadge which)
        {
            try
            {
                switch (which)
                {
                    case OverlayBadge.Clicking:
                        if (IsUp(_clickingPreview)) { return _clickingPreview; }
                        _clickingPreview = new ClickingIndicatorForm(_theme);
                        ApplyOverlayConfig(_clickingPreview);
                        WireBadge(_clickingPreview, OverlayBadge.Clicking);
                        _clickingPreview.Show();
                        _clickingPreview.SetMovable(true);
                        return _clickingPreview;
                    case OverlayBadge.Macro:
                        if (IsUp(_macroPreview)) { return _macroPreview; }
                        _macroPreview = new ClickingIndicatorForm(_theme, Localization.T("Tempo — playing macro"), _theme.Accent);
                        ApplyOverlayConfig(_macroPreview);
                        WireBadge(_macroPreview, OverlayBadge.Macro);
                        Macro selected = SelectedMacro();
                        _macroPreview.SetStatusText(selected != null ? selected.Name : Localization.T("playing…"));
                        _macroPreview.Show();
                        _macroPreview.SetMovable(true);
                        return _macroPreview;
                    default:
                        if (IsUp(_recordPreview)) { return _recordPreview; }
                        _recordPreview = new RecordingIndicatorForm(_theme, RecordingStopKeyText()) { Preview = true };
                        WireBadge(_recordPreview);
                        _recordPreview.Show();
                        _recordPreview.SetMovable(true);
                        return _recordPreview;
                }
            }
            catch (Exception ex)
            {
                Logger.Swallow("ShowOverlayPreview", ex);
                return null;
            }
        }

        /// <summary>
        /// The clicking and macro badges share a corner until one is moved, so their stand-ins came up on
        /// top of each other. Spread for moving only: a badge that is not dragged keeps its corner.
        /// </summary>
        private static void SpreadOverlappingBadges(List<Form> badges)
        {
            for (int i = 1; i < badges.Count; i++)
            {
                Form b = badges[i];
                for (int guard = 0; guard < badges.Count; guard++)
                {
                    Form hit = null;
                    for (int j = 0; j < i; j++)
                    {
                        if (badges[j].Bounds.IntersectsWith(b.Bounds)) { hit = badges[j]; break; }
                    }
                    if (hit == null) { break; }
                    Rectangle wa = Screen.FromRectangle(hit.Bounds).WorkingArea;
                    int below = hit.Bottom + 8;
                    b.Top = below + b.Height <= wa.Bottom ? below : Math.Max(wa.Top, hit.Top - b.Height - 8);
                }
            }
        }

        /// <summary>The key the recording badge names (the record toggle, else emergency stop), or null.</summary>
        private string RecordingStopKeyText()
        {
            var rec = _settings?.HotkeyFor(HotkeyAction.ToggleRecordMacro);
            if (rec != null && rec.IsValid) { return rec.ToDisplayString(); }
            var stop = _settings?.HotkeyFor(HotkeyAction.EmergencyStop);
            return stop != null && stop.IsValid ? stop.ToDisplayString() : null;
        }

        // ── Opacity and places ────────────────────────────────────────────────────────────────────

        /// <summary>One opacity for all three badges, applied to the ones on screen at once.</summary>
        private void SetOverlayOpacity(int percent)
        {
            if (_settings == null) { return; }
            _settings.OverlayOpacity = Math.Max(40, Math.Min(100, percent));
            try { Persistence.SettingsManager.Save(_settings); } catch (Exception ex) { Logger.Swallow("SetOverlayOpacity", ex); }
            ApplyOverlayLookToAll();
            Logger.Info("[Overlays] opacity " + _settings.OverlayOpacity + "%.");
        }

        /// <summary>The settings' look (corner, opacity, what the line shows) into every badge on screen.</summary>
        private void ApplyOverlayLookToAll()
        {
            foreach (ClickingIndicatorForm c in new[] { _clickingIndicator, _macroIndicator, _clickingPreview, _macroPreview })
            {
                if (c != null && !c.IsDisposed) { ApplyOverlayConfig(c); }
            }
            foreach (RecordingIndicatorForm r in new[] { _recordIndicator, _recordPreview })
            {
                if (r != null && !r.IsDisposed && _settings != null) { r.SetOpacity(_settings.OverlayOpacity); }
            }
        }

        private bool AnyOverlayPlaced() =>
            OverlayPlace(OverlayBadge.Clicking).HasValue || OverlayPlace(OverlayBadge.Macro).HasValue ||
            OverlayPlace(OverlayBadge.Recording).HasValue;

        /// <summary>Every badge back to its corner (tray ▸ Overlays ▸ Default places).</summary>
        private void ResetOverlayPlaces()
        {
            if (_settings == null) { return; }
            _settings.ClickingOverlayX = _settings.ClickingOverlayY = AppSettings.OverlayUnplaced;
            _settings.MacroOverlayX = _settings.MacroOverlayY = AppSettings.OverlayUnplaced;
            _settings.RecordingOverlayX = _settings.RecordingOverlayY = AppSettings.OverlayUnplaced;
            try { Persistence.SettingsManager.Save(_settings); } catch (Exception ex) { Logger.Swallow("ResetOverlayPlaces", ex); }
            foreach (ClickingIndicatorForm c in new[] { _clickingIndicator, _macroIndicator, _clickingPreview, _macroPreview })
            {
                if (c != null && !c.IsDisposed) { c.FreePosition = null; }
            }
            foreach (RecordingIndicatorForm r in new[] { _recordIndicator, _recordPreview })
            {
                if (r != null && !r.IsDisposed) { r.FreePosition = null; }
            }
            if (_overlayMoveMode)
            {
                // The stand-ins are back in one corner: spread them again so each can be picked up.
                var up = new List<Form>();
                foreach (Form f in new Form[] { _clickingIndicator ?? _clickingPreview, _macroIndicator ?? _macroPreview,
                                                 _recordIndicator ?? _recordPreview })
                {
                    if (IsUp(f)) { up.Add(f); }
                }
                SpreadOverlappingBadges(up);
            }
            Logger.Info("[Overlays] every badge back to its corner.");
        }

        // ── The tray's Overlays menu ──────────────────────────────────────────────────────────────

        /// <summary>Builds tray ▸ Overlays: move, show, opacity, default places.</summary>
        private ToolStripMenuItem BuildTrayOverlaysMenu()
        {
            _trayOverlaysItem = new ToolStripMenuItem(Localization.T("Overlays"))
            { Tag = new TrayItemStyle(TrayGlyph.Badge) };

            _trayOverlayMoveItem = new ToolStripMenuItem(Localization.T("Move overlays"))
            {
                CheckOnClick = false,
                Tag = new TrayItemStyle(TrayGlyph.Move) { Toggle = true },
                ShortcutKeyDisplayString = TrayItemStyle.SwitchRoom
            };
            _trayOverlayMoveItem.Click += (s, e) => TrayAction("Move overlays", ToggleOverlayMoveMode);

            // The same switch as Settings' "Show on-screen overlay while running", and kept in step with
            // it: Save Settings writes that checkbox back, and would otherwise undo a change made here.
            _trayOverlayShowItem = new ToolStripMenuItem(Localization.T("Show on-screen overlay while running"))
            {
                CheckOnClick = false,
                Tag = new TrayItemStyle(TrayGlyph.Badge) { Toggle = true },
                ShortcutKeyDisplayString = TrayItemStyle.SwitchRoom
            };
            _trayOverlayShowItem.Click += (s, e) => TrayAction("Show overlay", () =>
            {
                if (_settings == null) { return; }
                _settings.ShowClickingIndicator = !_settings.ShowClickingIndicator;
                if (_showIndicatorCheck != null) { _showIndicatorCheck.Checked = _settings.ShowClickingIndicator; }
                try { Persistence.SettingsManager.Save(_settings); } catch (Exception ex) { Logger.Swallow("TrayShowOverlay", ex); }
                ShowClickingIndicator(_engine != null && _engine.IsRunning);
                if (!_settings.ShowClickingIndicator) { HideMacroIndicator(); }
                _trayOverlayShowItem.Checked = _settings.ShowClickingIndicator;
            });

            _trayOverlayOpacityItem = new ToolStripMenuItem(Localization.T("Opacity"))
            { Tag = new TrayItemStyle(TrayGlyph.Badge) };
            foreach (int pct in OverlayOpacities)
            {
                int p = pct;
                var item = new ToolStripMenuItem(Localization.F("{0}%", p)) { Tag = new TrayItemStyle(TrayGlyph.Dot) };
                item.Click += (s, e) => TrayAction("Overlay opacity", () => SetOverlayOpacity(p));
                _trayOverlayOpacityItem.DropDownItems.Add(item);
            }

            _trayOverlayResetItem = new ToolStripMenuItem(Localization.T("Default places"))
            { Tag = new TrayItemStyle(TrayGlyph.Refresh) };
            _trayOverlayResetItem.Click += (s, e) => TrayAction("Overlay places", ResetOverlayPlaces);

            _trayOverlaysItem.DropDownItems.Add(_trayOverlayMoveItem);
            _trayOverlaysItem.DropDownItems.Add(_trayOverlayShowItem);
            _trayOverlaysItem.DropDownItems.Add(_trayOverlayOpacityItem);
            _trayOverlaysItem.DropDownItems.Add(new ToolStripSeparator());
            _trayOverlaysItem.DropDownItems.Add(_trayOverlayResetItem);
            return _trayOverlaysItem;
        }

        /// <summary>The Overlays menu's switches and ticks as they are now (every time the tray opens).</summary>
        private void UpdateTrayOverlayItems()
        {
            if (_trayOverlaysItem == null || _settings == null) { return; }
            _trayOverlayMoveItem.Checked = _overlayMoveMode;
            _trayOverlayShowItem.Checked = _settings.ShowClickingIndicator;
            // The opacity in force, marked; a value set in Settings between two choices marks the nearest.
            int nearest = OverlayOpacities[0];
            foreach (int pct in OverlayOpacities)
            {
                if (Math.Abs(pct - _settings.OverlayOpacity) < Math.Abs(nearest - _settings.OverlayOpacity)) { nearest = pct; }
            }
            for (int i = 0; i < _trayOverlayOpacityItem.DropDownItems.Count && i < OverlayOpacities.Length; i++)
            {
                if (_trayOverlayOpacityItem.DropDownItems[i] is ToolStripMenuItem mi) { mi.Checked = OverlayOpacities[i] == nearest; }
            }
            _trayOverlayResetItem.Enabled = AnyOverlayPlaced();
        }
    }
}
