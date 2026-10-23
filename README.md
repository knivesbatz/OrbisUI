# OrbisUI

A portable, controller-first PlayStation 4 style home screen for Windows handhelds, built and tuned for the ASUS ROG Ally.

OrbisUI is a native WPF (.NET 10) shell with native DualShock 4 input (SDL3/HIDAPI over USB and Bluetooth, no DS4Windows). It brings Steam, PC games and the PS1/PS2/PS3/PS4/PSP/Vita emulators together in one console-style interface. It is a personal project.

The real status of every feature is in [FEATURES.md](FEATURES.md). The roadmap is in [TODO.md](TODO.md) and the [GitHub issues](https://github.com/knivesbatz/OrbisUI/issues).

## Install (portable)

1. Download `OrbisUI-x.y.z-win-x64.zip` from [Releases](https://github.com/knivesbatz/OrbisUI/releases) and unzip it anywhere, for example `C:\OrbisUI` or a microSD card.
2. Run `OrbisUI.exe`. No installer or .NET runtime is needed; everything is inside the folder.
3. The first run opens the setup wizard. It checks Windows, the display, Steam, free space, the controller and the emulators, then asks for your folders and optional online keys.

Everything OrbisUI writes stays in its own folder:

| Folder | Contents |
|---|---|
| `Config\` | settings.json, library cache, encrypted API keys (`secrets.dat`, Windows DPAPI) |
| `Cache\` | square artwork, backgrounds, generated cue sheets |
| `Logs\` | `orbisui.log` (diagnostics) |
| `Data\Sounds`, `Data\Icons` | your console sound and icon packs (see below) |
| `Games\PS1` … `Games\VITA` | default ROM folders (the ROM folder can be moved in Settings) |
| `Emulators\PS1` … `Emulators\VITA` | optional place for emulators (existing installs are detected) |
| `Firmware\` | BIOS/firmware you dumped yourself |
| `Backups\` | copies of emulator configs taken before OrbisUI changed them |

### Sounds and icons

The public package contains no Sony media. Built-in tones and vector icons are used until you add your own:

* **Sounds:** in Settings > Sound and Screen > Import Sound Pack, pick a folder of `snd_*.wav` files (plus an optional `*Startup*.mp3`). A `UI Sounds` folder next to the app is picked up automatically.
* **Icons:** put PNG icons in `Data\Icons` (file names in [IconPack.cs](src/OrbisUI/UI/IconPack.cs)). An `Icons` folder next to the app is imported automatically.

## Controls

| DualShock 4 | Keyboard | Action |
|---|---|---|
| D-pad / left stick | Arrow keys | Move |
| ✕ | Enter / Space | Select, start a game |
| ○ | Esc / Backspace | Back |
| △ / OPTIONS | F3 / Tab | Game options (artwork, settings, hide, uninstall) |
| Up on the home row | Up | Function bar (Store, Notifications, Friends, Messages, Profile, Trophies, Settings, Power) |
| Down on TV & Video | Down | Its apps |
| L1 / R1 | Page Up / Down | Jump |
| PS button | Home | Home screen. While an emulator or PC game runs it brings the home screen back over the game |
| Touchpad (in DuckStation) | | Emulator pause menu |

## Games

* **Steam:** installed games and existing non-Steam shortcuts are found automatically and start through Steam, so the overlay works.
* **Emulated games:** put them in the ROM folder in subfolders `PS1`, `PS2`, `PS3`, `PS4`, `PSP`, `VITA` (any case), or add more folders in Settings > Library > Extra Game Folders. PS4 games are extracted folders with `eboot.bin`. Vita and PS3 digital titles are read from the emulators' own install folders. Multi-disc games appear once, and you choose the disc when starting.
* **Artwork:** SteamGridDB (with your free API key) overrides every other source. Without a key, art comes from the game package (PS4/PS3/Vita icons), Steam's cache, your PCSX2/DuckStation covers, libretro-thumbnails, the Steam Store and IGDB. Every tile is square, and odd shapes are padded over a blurred copy of themselves. Backgrounds come only from SteamGridDB and fade in after a game has been selected for 5 seconds. Press OPTIONS > Change Artwork to pick another SteamGridDB image with the controller.

## Emulators

| System | Emulator | Default launch |
|---|---|---|
| PS1 | DuckStation | `-batch -nogui -fullscreen -- "{rom}"` |
| PS2 | PCSX2 | `-batch -nogui -fullscreen -bigpicture -- "{rom}"` |
| PS3 | RPCS3 | `--no-gui --fullscreen "{rom}"` |
| PS4 | shadPS4 | `-g "{rom}" -f true` |
| PSP | PPSSPP | `--fullscreen --escape-exit "{rom}"` |
| Vita | Vita3K | `-F -r {id}` |

Arguments can be changed per system (Settings > Emulators) and per game (OPTIONS > Game Settings). If DuckStation has never been set up, OrbisUI writes an Ally profile so games boot straight in: 720p (3x), 16:9 widescreen hack, bilinear smoothing, no on-screen messages and DualShock 4 bindings. Any existing config is backed up first. Configs you already tuned are left alone.

## Building

Requires the .NET 10 SDK on Windows.

```powershell
.\build.ps1             # tests + dist\OrbisUI-x.y.z-win-x64.zip (+ .sha256)
.\build.ps1 -Personal   # also a package that includes your local sound/icon packs (keep it private)
```

To test against a scratch data folder, set `ORBISUI_ROOT`. `ORBISUI_SCRIPT="wait:3000;Up;shot:fn;quit"` drives the UI and saves screenshots to `Logs\shots`.

## Updates

OrbisUI checks the [latest GitHub release](https://github.com/knivesbatz/OrbisUI/releases/latest) at startup and every 6 hours (Settings > System Software Update). Updates install only after the ZIP matches the SHA-256 published with it. Old files are kept as `*.old` until the next start and are rolled back if anything fails. `Config`, `Cache`, `Logs`, `Data`, `Games`, `Firmware`, `Emulators` and `Backups` are never touched.

## Troubleshooting

* **A game doesn't start:** the error is shown and logged in `Logs\orbisui.log`, including the exact command line.
* **Splash screen never goes away:** press ○ to stop waiting. The launcher gives up on its own after 90 seconds.
* **The controller does nothing:** check Settings > Controllers. SDL3.dll must sit next to OrbisUI.exe. The keyboard always works.
* **Wrong cover:** use OPTIONS > Change Artwork. △ searches SteamGridDB under another name.
