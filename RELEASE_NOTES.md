# Latest release notes

This file always holds the notes for the most recent version. Per-version notes live in the `release-notes/` folder.

## Tempo 1.0.115

**Clicker - new: finish notification**
- New option under the Start button: "Notify when a fixed run finishes". When a
  fixed-count or fixed-duration run completes on its own, Tempo plays a chime and
  shows a tray notice with the session click count. Stopping a run yourself stays
  silent - the engine now tracks whether a run ended naturally or was stopped.

**Macros - "Save Macro" redesigned**
- Recordings no longer drop straight into the list under a timestamp name. A proper
  Save Recording dialog now appears when you stop: it shows what was captured
  (steps and length), lets you name it, add an optional note, and pin it to the top
  of the list. "Keep default" (or closing) still saves under the automatic name, so
  a recording is never lost.

Windows may show "Unknown publisher" because the app isn't code-signed — click More info → Run anyway. It's safe.
