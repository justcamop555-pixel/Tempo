# Latest release notes

This file always holds the notes for the most recent version. Per-version notes live in the `release-notes/` folder, and every change is in `CHANGELOG.md`.

## Tempo 1.0.327

**Any tab in its own window · anti-AFK for Roblox · see which game each account is in · a crash and freeze guard · a tidier Settings tab · Tempo now runs on .NET 10**

### Your window, your way
- **Any tab in its own window.** Right-click a tab in the sidebar and choose **Open in its own window**. The tab then sits in a window of its own, beside your game, and stays open while Tempo's main window is minimised. Its place in Tempo offers **Show the window** and **Bring it back here**, and each window remembers where it was. This replaces the Accounts-only "In its own window" setting.
- **A tidier Settings tab.** Nine cards by topic, with the ones most people never change folded under **Advanced** (Display & logo, More caption options, Camera-relative movement). On the Clicker tab, Second Cursor, Background Clicking and Sound fold under Advanced too.
- **The Window card** saves opacity, always on top and "remember the window" the moment they change, and a double-click on the opacity slider puts it back to 100%.

### Roblox and accounts
- **Anti-AFK.** Roblox disconnects a player who is idle for 20 minutes. Turn on **Anti-AFK** on the Roblox client card and pick how often (every 1 to 18 minutes). Tempo then brings each Roblox window that is in a game to the front, presses F13 once (a key games don't use) and puts your window back.
  - It doesn't modify or inject into Roblox. It presses a real key, as you would.
  - It waits until you have stopped typing and moving for a few seconds. It stays out of the way while you hold a mouse button, play with a controller, run something else full screen or have the PC locked. It warns you if a window gets close to 20 minutes.
  - Turn it off for one account from the account list's right-click menu. Like any key press, it keeps Windows from switching the screen off.
- **See which game each account is in.** The Roblox client card shows a live line, such as "Connected: in Blox Fruits as Main, since 10:21", and the Session column says **In game**. Tempo can tell you when a client joins a game, is disconnected (and why) or crashes. It reads Roblox's own log on your PC. The only thing it sends is a game's number, once, to learn its name, and **Name the Roblox game you're playing** in Updates & privacy turns that off.
- **A crash and freeze guard.** When a Roblox client crashes or stops responding for a minute, Tempo starts it again as the same account in the same game, at most 3 times in 15 minutes. It covers clients Tempo launched and clients you started yourself that play as a saved account. It's on by default. It can't stop Roblox from crashing in the first place.
- **Smoother play.** The Roblox window you're in comes first: other Roblox clients run at a lower priority, and Tempo's own animations pause while Roblox is in front. Tempo also warns you when the PC is low on memory.
- **Faster joins and Recent games.** Launching an account into a game takes less time, and **Recent games** fills in a Place ID you played lately.
- **Fix Roblox links** repairs Roblox's links when an update left them pointing at a version that no longer exists.
- **The Accounts tab** is laid out in four cards with a Session column, and a failed launch ticket no longer marks a good session as expired.
- **Fixed:** opening an account's browser a second time gave an empty tab.

### Clicker, macros and profiles
- **Multi-Point: work on several points at once.** Select them with Ctrl+A, **Select all** or Ctrl-click, then edit, move or delete them together, or drag them as a block. Point names, buttons and types appear in your language.
- **The Live Monitor follows the playing step** until you scroll away, and **Jump to the playing step** catches up. Right-click a step to edit or copy it, double-click it to open it in the editor, or press Ctrl+C.
- **Profiles:** picking a profile counts as using it, and its card updates at once.

### Live Captions
- **Fewer missed words.** Short words, words after a pause and words said twice reach the caption line far more often.
- **Laughter shows as "ha ha ha",** not "5, 5, 5".
- **Captions follow the sound** to whichever speakers or headset it plays on.
- **Older caption text dims** after 10 seconds, so the newest line stands out.
- The model line suggests a bigger model you have downloaded when your graphics card can run it.

### Under the hood
- **Tempo now runs on .NET 10,** Microsoft's current long-term release, supported until November 2028. Support for .NET 8 ends on 10 November 2026. There is nothing to install: Tempo carries .NET inside it, and it starts as quickly as before.
- **Fixed:**
  - Changing the language, which restarts Tempo, could end in a crash report.
  - Holding an arrow key in a list or drop-down list made it stall.
  - The divider between Live debug's panes couldn't be dragged. Shift+wheel now scrolls Live debug sideways.
  - Game mode took browsers, video players, Tempo itself and maximised windows on a second screen for a game.
  - The update line in Settings now says when the next check is due.
