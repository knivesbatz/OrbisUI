# Changelog

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
