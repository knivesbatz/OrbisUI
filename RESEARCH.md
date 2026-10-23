# Research notes

## Reference material in the workspace
- `UI Videos/Complete PS4 UI Walkthrough.mp4` (23.6 min, 720p, firmware 1.x): frames sampled every 20 s. Home layout (1920×1080 scale): focused tile 360 px at x=330, y=186; other tiles 210 px at y=225, 12 px gaps; title to the right of the focus tile under the row; folder contents in a row below (y≈690, 255 px tiles); settings lists are 84 px rows with an icon, label and right-aligned value/checkbox, a cyan focus bar, the title top-left with a rule, and ✕ Enter / ○ Back hints at the bottom.
- `UI Sounds/`: `snd_*.wav` (48 kHz 16-bit mono PCM) plus MP3 copies and `Ps4 Startup.mp3`.
- `source code refrences/OrbisPro-main`: a VB.NET WPF PS4-style shell (no license file). Used for ideas and as the user's local icon pack (not redistributed).
- Emulators under `emulators\` with sources: DuckStation, PCSX2, RPCS3, shadPS4, PPSSPP, Vita3K.

## Emulator command lines (verified in the bundled sources)
- PCSX2 `pcsx2-qt/QtHost.cpp`: `-batch`, `-nogui`, `-fullscreen`, `-nofullscreen`, `-bigpicture`, then `--` and the path. The user's `Documents\PCSX2\inis\PCSX2.ini` already has every `OsdShow*` off, upscale ×2, `RecursivePaths = D:\GAMEROM`.
- DuckStation `duckstation-qt/qthost.cpp`: `-batch`, `-nogui`, `-fullscreen`, `-bigpicture`. Data dir (`core/core.cpp`): exe dir if `portable.txt`/`settings.ini` exists, else `Documents\DuckStation` if it exists, else `%LOCALAPPDATA%\DuckStation`. The setup wizard runs when `[Main] SetupWizardIncomplete` is true or no `SettingsVersion` exists.
- shadPS4 `src/main.cpp`: `-g/--game <path|ID>`, `-f/--fullscreen true|false`, `-p/--patch`, `--add-game-folder`. The user's QtLauncher `config.json` has `install_dirs: C:\Sony\GAME`, borderless fullscreen and 720p internal.
- RPCS3 `rpcs3/rpcs3.cpp`: `--no-gui`, `--fullscreen` (only with no-gui). Games: `config/games.yml` and `dev_hdd0/game/<ID>` (category HG).
- Vita3K `vita3k/config/src/config.cpp`: `-F/--fullscreen`, `-r/--installed-path <TITLEID>`, `--frontend sdl`. Apps live in `<pref-path>/ux0/app/<TITLEID>`.
- PPSSPP `Windows/main.cpp`: `--fullscreen`, `--escape-exit`.

## Steam
- Install path: `HKCU\Software\Valve\Steam\SteamPath`. Libraries: `steamapps/libraryfolders.vdf`. Apps: `appmanifest_*.acf` (`StateFlags & 4` = installed).
- Running game: `HKCU\Software\Valve\Steam\RunningAppID`.
- Library art cache: `appcache/librarycache/<appid>/` (`library_600x900.jpg`, `header.jpg`, `library_hero.jpg`, sometimes in hashed subfolders). Custom art lives in `userdata/<id>/config/grid/`.
- Non-Steam shortcuts: binary KeyValues `userdata/<accountid>/config/shortcuts.vdf`. The game id for `steam://rungameid/` is `(appid << 32) | 0x02000000`.
- Logged-in user: `config/loginusers.vdf` (MostRecent). Avatar: `config/avatarcache/<steamid64>.png`.
- Handoffs: `steam://open/bigpicture`, `steam://store`, `steam://open/friends`, `steam://url/SteamIDMyProfile`, `steam://uninstall/<appid>`.

## Metadata and art
- SteamGridDB API v2 (Bearer key): `search/autocomplete/{term}`, `games/steam/{appid}`, `grids/game/{id}?dimensions=512x512,1024x1024` (square), `heroes/game/{id}`.
- libretro-thumbnails: `https://thumbnails.libretro.com/<System>/Named_Boxarts/<name>.png`. The name is the dump name with `&*/:`<>?\|"` replaced by `_`. No key needed.
- Steam Store (no key): `store.steampowered.com/api/storesearch`, `api/appdetails`; CDN `shared.akamai.steamstatic.com/store_item_assets/steam/apps/<id>/library_600x900_2x.jpg`.
- IGDB v4: Twitch client-credentials token, then `POST /v4/games` with Apicalypse. Platforms: PS1=7, PS2=8, PS3=9, PS4=48, PSP=38, Vita=46.
- PlayStation Store: no public API. Not used. PS4 package `icon0.png` is the official square icon.

## Lossless Scaling
- No CLI or API. The global hotkey and modifiers are in `%LOCALAPPDATA%\Lossless Scaling\Settings.xml` (`<Hotkey>S</Hotkey>`, `<HotkeyModifierKeys>Alt Control</HotkeyModifierKeys>`). Per-profile `AutoScale` exists inside LS itself.

## DualShock 4
- SDL3 3.4 HIDAPI PS4 driver handles USB and Bluetooth (enhanced reports for touchpad/gyro). SDL_Event layout: type@0, which@16, button@20, down@21; axis value@24 (int16).
