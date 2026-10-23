# Feature status (v0.1.0)

Legend: **Done + tested**: implemented and checked on the ROG Ally or by automated tests. **Done (untested)**: implemented but not exercised end to end yet. **Partial**: works with stated limits. **Not yet**: not implemented.

| Area | Status | Notes |
|---|---|---|
| Portable folder layout, settings, logs | Done + tested | Atomic JSON writes, rotating log, unit tests |
| First-run setup wizard | Done + tested | System check, folders, sound pack, optional keys (DPAPI), preferences; re-run from Settings > System |
| Boot animation + login screen | Done + tested | Your boot video (Data\Media\boot.mp4) fades into home, PS skips it; otherwise the original animation and login screen; can be turned off |
| Live background + home music | Done + tested | Data\Media\background.mp4 loops behind the UI (paused when not in front); music.mp3 plays at home and pauses for games and apps (audio not verified by ear) |
| Home screen (PS4 layout) | Done + tested | Square tiles, enlarged focus tile, title and details, top function bar, clock, user |
| Function bar actions | Partial | Store, Friends, Messages, Profile and Trophies hand off to Steam (`steam://`). Notifications, Settings and Power are native |
| Background art | Done + tested | SteamGridDB heroes only, fade in after 5 s |
| Native DualShock 4 input | Done + tested | SDL3 HIDAPI, USB and Bluetooth, auto-repeat, hot-plug toasts, controller test screen |
| Keyboard fallback | Done + tested | |
| PS4 sound pack | Done + tested | 43 sounds imported from your folder; their broken WAV headers are repaired on load (they were silent in 0.1.0); volume, mute, throttling, generated fallback tones |
| PS4 icon pack | Done + tested | Imported from your local icon folder; vector glyph fallback |
| Steam library + shortcuts | Done + tested | libraryfolders.vdf, appmanifests, shortcuts.vdf; tools filtered |
| ROM scanning | Done + tested | Per-system folders, extra folders, PCSX2's own folders, multi-track .bin cue generation |
| Multi-disc grouping + disc picker | Done (untested) | Grouping is unit-tested; the picker has not been launched on device yet |
| PS4 / PS3 / Vita installed titles | Done (untested) | PARAM.SFO parsing is unit-tested; no PS4/PS3/Vita games on this device yet |
| Artwork pipeline | Done + tested | SteamGridDB (override), package art, Steam cache, emulator covers, libretro-thumbnails, Steam Store, IGDB (with keys) |
| PlayStation Store metadata | Not yet | No public API; PS4 game-package icons are used instead |
| Controller art picker | Done + tested | Up to 50 SteamGridDB square options, name search, image file |
| Game launching + splash + return | Partial | Window-ready detection and return to home are implemented. Steam launch is not yet verified on device |
| DuckStation Ally profile | Done + tested | Written only when DuckStation was never set up, after a backup |
| PCSX2 / PPSSPP / RPCS3 / Vita3K Ally profiles | Done (untested) | Vulkan, no OSD, fast boot; PS1/PS2/PS3 test ROMs launched and stayed running, image quality not checked on device |
| Other emulator profiles (PCSX2, PPSSPP, RPCS3, Vita3K, shadPS4) | Not yet | Your existing PCSX2 and shadPS4 configs are used as they are |
| PS button returns home over a running emulator | Done (untested) | |
| TV & Video folder | Done + tested | Down opens the square app row; apps are added in Settings > TV & Video (programs only); art from SteamGridDB or an image file via Options > Change Artwork |
| Library screen | Done + tested | Grid, platform filter (△), hidden games, rescan (□) |
| Game options | Done + tested | Information, Change Artwork, Game Settings (name, args, Lossless Scaling), hide, Steam uninstall |
| Power menu | Done (untested) | Close application, rest, shut down, restart, desktop, Big Picture |
| Settings | Partial | Real actions only. Display (resolution, refresh rate, brightness, 15 s revert) is built in; Wi-Fi, Bluetooth and audio still open the matching Windows Settings page |
| Lossless Scaling auto-activation | Done (untested) | Sends LS's own hotkey (read from its Settings.xml) to the game window after the delay; "PRO Mode Active" shows over the game once an LS window covering the game is detected (detection heuristic not yet verified with a real game) |
| Emulator fast boot | Done (untested in-game) | DuckStation and PCSX2 start with -fastboot; shadPS4 splash turned off after a config backup |
| Auto-updater | Done (untested) | GitHub Releases + SHA-256, rename-swap with rollback. Live once v0.1.1 is published |
| Notifications drawer + toasts | Done + tested | Launcher events only |
| Import games into Steam (shortcuts.vdf) | Not yet | Binary VDF writer exists and is tested |
| Steam friends / chat / activity inside the UI | Not yet | Handoff to Steam for now |
| ReShade per-game presets | Not yet | |
| Built-in browser and file manager | Not yet | A controller file picker exists |
| PKG extractor | Not yet | |
| Controller Fixer | Not yet | |
| RetroAchievements | Not yet | |
| Patches / texture packs | Not yet | |
