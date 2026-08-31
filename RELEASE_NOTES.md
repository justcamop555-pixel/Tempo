# Latest release notes

This file always holds the notes for the most recent version. Per-version notes live in the `release-notes/` folder.

## Tempo 1.0.223

**Moves the anti-corruption rendering to the form level (where it actually sticks), and finishes the solid-background fix for all labels and radio buttons**
- WS_EX_COMPOSITED is now applied to the main window itself instead of the individual tab pages. Setting it on a tab page doesn't stick - the tab control recreates each page's window when you switch tabs, dropping the style - which is why checkboxes and labels kept corrupting after scrolling despite the previous attempt. On the stable main-window handle it composites the whole UI in one pass.
- The solid-background fix is now applied to ALL labels and radio buttons too, not just checkboxes. 1.0.222 only fixed checkboxes, which is why labels were still clipping. None of these controls use a transparent background anymore - they match their card or page, so they erase cleanly on every repaint.
- Hardened the background-colour picker so a control can never accidentally resolve to white (a guard added after the blank-box report).

If the blank box near the taskbar appears again, it should be gone with this build; if not, a crash report in %LOCALAPPDATA%\AutoClicker\logs will help pinpoint it.

Windows may show "Unknown publisher" because the app isn't code-signed - click More info → Run anyway. It's safe.
