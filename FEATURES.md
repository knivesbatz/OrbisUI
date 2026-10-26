# Feature status (v0.4.3)

Legend: **Done + tested**: implemented and checked on the ROG Ally or by automated tests. **Done (untested)**: implemented but not exercised end to end yet. **Partial**: works with stated limits. **Not yet**: not implemented.

| Area | Status | Notes |
|---|---|---|
| Portable folder layout, settings, logs | Done + tested | Atomic JSON writes, rotating log, unit tests |
| First-run setup wizard | Done + tested | System check, folders, sound pack, optional keys (DPAPI), preferences; re-run from Settings > System |
| Boot video + login screen | Done + tested | Your boot video fades into home, PS skips it |
| Live background + home music | Done + tested | From your own media in Data\Media |
| Home screen (PS4 layout) | Done + tested | Status bar, square tiles, Store / What's New / Browser tiles, TV & Video, Library |
| Function area | Done + tested | Like the console: row drops to the bottom, large icons, selected one named. Notifications, Friends, Messages, Settings, Power native; Store, Profile and Trophies hand off to Steam |
| Native DualShock 4 input | Done + tested | SDL3 HIDAPI, USB and Bluetooth, hot-plug toasts, controller test screen; named DUALSHOCK 4, battery level in the connect notification |
| PS4 sound and icon packs | Done + tested | Your files, repaired WAV headers |
| Steam library + shortcuts, ROM scanning, multi-disc picker | Done + tested | |
| PC games from .exe, .lnk, .url and Microsoft Store apps | Done + tested | Shortcuts to an exe launch that exe with its arguments; .url and Store apps start through the shell and are tracked by their window |
| Game versions with a picker before launch | Done + tested | Game Settings > Versions: extra exes, shortcuts or Store apps |
| Artwork pipeline | Done + tested | SteamGridDB by default, controller art picker, backgrounds after 5 s |
| PlayStation Store metadata | Done + tested | Store web search (no sign-in): square key art, description, publisher, year, genres; LIMBO checked |
| Launch splash | Done + tested | Fades into the SteamGridDB background (cover and title without one) and fades out once the game window has focus (at most 10 s after it appears) |
| Home trophy and friend cards | Done + tested | Steam (NARUTO STORM) and RetroAchievements (Shadow Hearts) checked; the friends card needs friends with public profiles who own the game, not yet seen with real data |
| Steam Big Picture handoffs | Done (return-on-close not yet checked on the Ally) | Friends, chat, profile and store open in Big Picture; when it closes Steam is minimized and OrbisUI returns. Game launches keep Steam minimized |
| Return home / PS button over a running game | Done + tested | |
| Emulator Ally profiles | Done + tested | Vulkan, no OSD, fast boot for DuckStation, PCSX2, PPSSPP, RPCS3, Vita3K; shadPS4 splash off; configs backed up first. 2026-10-24 pass: Silent Hill (PS1), GTA Liberty City Stories (PSP) and LIMBO (PS4) run in-game with Vulkan and ReShade; Minecraft (PS3) boots with firmware 4.92 (gameplay not confirmed); Persona 3 FES (PS2) boots, no picture captured; Gravity Rush (Vita) not confirmed; shadPS4 pipeline cache and Bloodborne patches (Bloodborne itself not tested: base game not installed) |
| PS4 PKG installer | Done + tested | ShadPs4Plus pkg_extractor: games and updates to shadPS4's games folder, DLC to its add-on folder; the LIMBO test package installed and appeared in the library |
| File manager | Done + tested | In Settings. Drives and folders, copy, move, rename, new folder, delete to the Recycle Bin, open files, install .pkg |
| Internet browser | Done + tested | WebView2, D-pad link navigation, Google by default |
| What's New feed, Friends and Messages | Done + tested | Steam Web API key needed; friends need a public friends list; chat opens in Steam |
| Wi-Fi and audio output | Done + tested | Native lists; joining a new secured network asks for its password with the on-screen keyboard |
| Bluetooth pairing | Partial | Opens Windows Bluetooth settings |
| Display settings | Done + tested | Resolution, refresh rate, brightness, 15 s revert |
| TV & Video apps | Done + tested | Add, rename, change art, remove |
| ReShade | Done + tested | Every emulator through ReShade's Vulkan layer with your shaders (checked in DuckStation); per-game presets for PC games |
| Lossless Scaling auto-activation | Done (untested) | Detection of the scaled window not yet verified with a real game |
| Controller Fixer | Done (untested) | |
| Uninstall / Delete | Done + tested | Two confirmations (checked with Cancel); Steam, Store, registered uninstaller or Recycle Bin; uninstalled added games leave the library |
| Import games into Steam | Done + tested | shortcuts.vdf backed up first |
| Auto-updater | Done + tested | GitHub Releases + SHA-256 |
| RetroAchievements | Done + tested | Trophies shows your points and recent trophies (Web API key); emulators sign in on their own |
| Patches / texture packs | Partial | PCSX2 widescreen and no-interlace patches and DuckStation's widescreen hack are on; there is no trusted texture-pack source to download from |
