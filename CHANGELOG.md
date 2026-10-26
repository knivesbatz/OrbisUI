# Changelog

## 0.4.4 (2026-10-26)
- Steam friends, chat, profile and store pages open in Steam Big Picture. When Big Picture closes, Steam goes back to minimized and OrbisUI comes back to the front.
- Steam games launch with Steam kept minimized instead of popping up the Steam window; Steam also starts in the background with OrbisUI.
- The launch splash fades into the game's SteamGridDB background and only fades out once the game is running and in focus. Games without a SteamGridDB background keep the cover, title and spinner.
- The platform label ("PC", "PS2" and so on) no longer shows next to game names or on fallback tiles.

## 0.4.3 (2026-10-24)
- Uninstall or Delete from a game's Options: Steam and Microsoft Store games use their own uninstallers, PC games the uninstaller Windows has for them, and ROMs or PS3/PS4/Vita game folders go to the Recycle Bin. Two confirmations come first, each starting on Cancel.
- Added PC games and Store apps that were uninstalled drop out of the library on the next scan (Steam and ROM games already did).
- A PS4 controller shows as DUALSHOCK 4, and the notification when it connects (also at start) shows its battery level.

## 0.4.2 (2026-10-24)
- Vita3K starts again: the Ally profile no longer writes a setting Vita3K rejects ("Failed to initialise config"), and an existing Vita3K config gets the welcome dialog and live area turned off so games boot straight in.
- Vita games installed in Vita3K's own data folder (%APPDATA%\Vita3K) now show up in the library.

## 0.4.1 (2026-10-24)
- Internet Browser: the controller now drives the page (it was ignored while the page was in front), the first D-pad press lands on the search box, and searching from the on-screen keyboard submits Google's search.
- Internet Browser no longer crashes or strands its window: PS/Home, game launches and Alt+F4 close it cleanly, invalid addresses become a Google search, and a crashed page reloads.

## 0.4.0 (2026-10-23)
- Up on the home screen works like the console: the status bar hides, the home row drops to the bottom and a row of large function icons appears, with the selected one enlarged and named.
- PlayStation Store is a metadata source for PlayStation games: square key art, description, publisher, year and genres.
- ReShade runs in every emulator through its Vulkan layer, using your shaders and settings (FPS counter off).
- Trophies shows your RetroAchievements points and recent trophies (add your user name and Web API key in Settings > Accounts and Services).
- shadPS4: pipeline cache on; Bloodborne gets Skip Intro, 60 FPS++ and no chromatic aberration or motion blur (game update 1.09).

## 0.3.1 (2026-10-23)
- File Manager moved from the home row into Settings.

## 0.3.0 (2026-10-23)
- File Manager on the home row: browse drives, copy, move, rename, new folder, delete to the Recycle Bin.
- PS4 PKG installer: Cross on a .pkg installs games and updates to shadPS4's games folder and DLC to its add-on folder.
- PC games from shortcuts (.lnk, .url) and Microsoft Store apps.
- Game versions: add other exes or shortcuts in Game Settings > Versions and choose one before the game starts.
- TV & Video apps can be renamed.
- The launch splash fades in and stays until the game is fullscreen.
- Friends and Messages list your Steam friends with who's online and playing; Cross opens a chat in Steam.
- Wi-Fi and audio output pickers in Settings > Network and Devices.
- Yes/No dialogs show their icons again.

## 0.2.4 (2026-10-23)
- Every emulator defaults to the ROG Ally setup: Vulkan, no on-screen messages, fast boot. DuckStation and PCSX2 keep your tuning and only get those keys (backed up first); PPSSPP (3x, fullscreen), RPCS3 (720p 16:9, no overlay or hints) and Vita3K (boots straight into the game, fullscreen) get a starter config when they have none.

## 0.2.3 (2026-10-23)
- Internet Browser on the home row (controller: D-pad between links, Cross select, Triangle address or Google search, Circle back). Controller Fixer searches open in it.
- What's New opens a PS4-style feed: what you and your Steam friends are playing, recent games and new achievements (needs a Steam Web API key for friends), plus your own play history.
- PlayStation Store and What's New tiles lead the home row, using your icons; every tile is square.
- Boot video fades into home more smoothly.

## 0.2.2 (2026-10-23)
- Background uses the console's royal blue.
- TV & Video opens like the console: preview row, Down lifts the home row and shows a 4-column app grid.
- Up highlights the header icons in place (Messages, Settings and Power added to the bar).
- Per-game ReShade preset in Game Settings, applied before launch (ini backed up, anti-cheat games skipped).
- Emulated and added games are added to Steam with SteamGridDB art before Steam starts (Settings > Library; shortcuts.vdf is backed up).
- Controller Fixer in Settings: search for PlayStation prompt mods, install from Downloads with backup, undo.

## 0.2.1 (2026-10-23)
- Home screen matches the console: status bar with PS Plus, notifications, friends, your Steam avatar and name, trophies and the clock; the game row sits right under it with the console's tile sizes, and the selected game's title sits beside its Start bar. Up slides the row down and shows the function icons.

## 0.2.0 (2026-10-23)
- Fixed: UI sounds were silent with the PS4 sound pack (its WAV headers declare a block align Windows rejects; repaired on load).
- Live video background, boot video and home music: drop background.mp4, boot.mp4 and music.mp3 into Data\Media. The boot video fades into the home screen; the PS button skips it. Music pauses whenever a game or app is in front. New Home Music and Music Volume settings.
- Display settings inside the UI: resolution, refresh rate and brightness, with a 15 second revert timer.
- PRO Mode Active notification over the game once Lossless Scaling is confirmed scaling.
- TV & Video: websites can no longer be added; change any app's art from SteamGridDB or an image file.
- SteamGridDB is the default art source and is retried after offline starts.
- PS1 and PS2 games fast boot (no BIOS intro); shadPS4's splash is turned off (config backed up first).
- Personal builds keep your settings and art cache when rebuilt.
## 0.1.0 (2026-10-23)
First portable build.
- PS4-style home: square tiles, enlarged focus tile, top function bar with Store, Notifications, Friends, Messages, Profile, Trophies, Settings and Power, user and clock.
- Setup wizard, boot animation and login screen.
- Native DualShock 4 (SDL3) plus keyboard; controller test screen.
- Steam games and shortcuts, PS1/PS2/PSP/PS3/PS4/Vita games, multi-disc grouping with a disc picker.
- Artwork from SteamGridDB (overrides everything), game packages, Steam, emulator covers, libretro-thumbnails, Steam Store and IGDB. Controller cover picker. SteamGridDB backgrounds fade in after 5 s.
- Launch splash, return to home, PS button over running emulators, Lossless Scaling hotkey activation.
- DuckStation ROG Ally profile (backed up first).
- TV & Video folder with square apps, managed in Settings.
- Library grid, game options, power menu, notifications, settings.
- PS4 sound and icon packs imported from your own folders.
- GitHub auto-updater with SHA-256 verification.
