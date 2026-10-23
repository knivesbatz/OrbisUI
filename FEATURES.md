# Feature status (v0.3.0)

Legend: **Done + tested**: implemented and checked on the ROG Ally or by automated tests. **Done (untested)**: implemented but not exercised end to end yet. **Partial**: works with stated limits. **Not yet**: not implemented.

| Area | Status | Notes |
|---|---|---|
| Portable folder layout, settings, logs | Done + tested | Atomic JSON writes, rotating log, unit tests |
| First-run setup wizard | Done + tested | System check, folders, sound pack, optional keys (DPAPI), preferences; re-run from Settings > System |
| Boot video + login screen | Done + tested | Your boot video fades into home, PS skips it |
| Live background + home music | Done + tested | From your own media in Data\Media |
| Home screen (PS4 layout) | Done + tested | Status bar, square tiles, Store / What's New / Browser / File Manager tiles, TV & Video, Library |
| Function bar actions | Done + tested | Notifications, Friends, Messages, Settings, Power native; Store, Profile and Trophies hand off to Steam |
| Native DualShock 4 input | Done + tested | SDL3 HIDAPI, USB and Bluetooth, hot-plug toasts, controller test screen |
| PS4 sound and icon packs | Done + tested | Your files, repaired WAV headers |
| Steam library + shortcuts, ROM scanning, multi-disc picker | Done + tested | |
| PC games from .exe, .lnk, .url and Microsoft Store apps | Done + tested | Shortcuts to an exe launch that exe with its arguments; .url and Store apps start through the shell and are tracked by their window |
| Game versions with a picker before launch | Done + tested | Game Settings > Versions: extra exes, shortcuts or Store apps |
| Artwork pipeline | Done + tested | SteamGridDB by default, controller art picker, backgrounds after 5 s |
| PlayStation Store metadata | Not yet | Sony has no public PlayStation Store API; PS4 package icons, SteamGridDB, Steam Store and IGDB are used |
| Launch splash | Done + tested | Fades in and holds until the game window is fullscreen/maximized (at most 10 s after it appears) |
| Return home / PS button over a running game | Done + tested | |
| Emulator Ally profiles | Done + tested | Vulkan, no OSD, fast boot for DuckStation, PCSX2, PPSSPP, RPCS3, Vita3K; shadPS4 splash off; configs backed up first. PS1, PS2 and PS3 test ROMs launch and run |
| PS4 PKG installer | Done + tested | ShadPs4Plus pkg_extractor: games and updates to shadPS4's games folder, DLC to its add-on folder; the LIMBO test package installed and appeared in the library |
| File manager | Done + tested | Drives and folders, copy, move, rename, new folder, delete to the Recycle Bin, open files, install .pkg |
| Internet browser | Done + tested | WebView2, D-pad link navigation, Google by default |
| What's New feed, Friends and Messages | Done + tested | Steam Web API key needed; friends need a public friends list; chat opens in Steam |
| Wi-Fi and audio output | Done + tested | Native lists; joining a new secured network asks for its password with the on-screen keyboard |
| Bluetooth pairing | Partial | Opens Windows Bluetooth settings |
| Display settings | Done + tested | Resolution, refresh rate, brightness, 15 s revert |
| TV & Video apps | Done + tested | Add, rename, change art, remove |
| ReShade per-game presets | Done (untested) | Needs ReShade installed in the game's or emulator's folder; no emulator folder has ReShade yet |
| Lossless Scaling auto-activation | Done (untested) | Detection of the scaled window not yet verified with a real game |
| Controller Fixer | Done (untested) | |
| Import games into Steam | Done + tested | shortcuts.vdf backed up first |
| Auto-updater | Done + tested | GitHub Releases + SHA-256 |
| RetroAchievements | Partial | Use each emulator's own login (PCSX2 is already signed in). Their tokens are encrypted per emulator, so OrbisUI doesn't copy them |
| Patches / texture packs | Partial | PCSX2 widescreen and no-interlace patches and DuckStation's widescreen hack are on; there is no trusted texture-pack source to download from |
