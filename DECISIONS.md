# Decisions

| # | Decision | Why | Alternatives considered |
|---|---|---|---|
| 1 | **WPF on .NET 10, self-contained single-file exe** | Native GPU-composited UI, low idle CPU, rich animation, no runtime install. OrbisPro (the reference project) proves WPF works for this | Electron/Tauri (heavier, or WebView2 dependency), WinUI 3 (packaging friction for portable apps), Avalonia |
| 2 | **UI built in C#, not XAML; every screen manages its own selection** | Controller-first focus is deterministic and avoids WPF keyboard-focus quirks | XAML + FocusManager |
| 3 | **1920×1080 design canvas inside a Viewbox** | Pixel layout measured from the reference video; scales to any resolution or DPI | Responsive grid layouts |
| 4 | **SDL3 (HIDAPI) for the DualShock 4** | Native DS4 over USB/BT without DS4Windows. SDL3.dll is zlib-licensed and already ships with the user's emulators | Raw HID parsing, XInput only, Windows.Gaming.Input |
| 5 | **Gamepad polled at 120 Hz in the foreground, 4 Hz in the background** | SDL's wait-for-event falls back to polling anyway; explicit rates keep idle CPU near zero | SDL_WaitEvent |
| 6 | **winmm PlaySound with pre-scaled PCM in unmanaged memory** | Zero idle cost and low latency; volume is applied by scaling the samples | MediaPlayer (latency), NAudio (dependency) |
| 7 | **Sony sounds and icons are user-supplied packs, never committed** | The repository is public; the user's own files are imported locally. Original fallback tones and vector glyphs are built in | Bundling them |
| 8 | **Function bar order Store → Power (PS4 order)** | The spec lists Power…Notifications, which is the console's own row read right to left | Reversed order |
| 9 | **Function icons always visible in the top bar** | User request; matches later PS4 firmware. Up moves focus there | Firmware 1.x style (icons appear only after pressing Up) |
| 10 | **"PC" platform shown as PS4; emulator names hidden on home** | User request: the shell should read like a console | |
| 11 | **SteamGridDB overrides all art; backgrounds only from SteamGridDB, after 5 s** | User request | Package art first |
| 12 | **Art fallback chain: package art → Steam cache → emulator covers → libretro-thumbnails → Steam Store → IGDB** | Free, keyless sources first; libretro names match Redump dumps exactly | PlayStation Store scraping (no public API, not reliable) |
| 13 | **Square tiles: near-square art is centre-cropped; anything else is fitted over a blurred, darkened fill** | Never distorts, never crops away titles | Letterboxing, stretch |
| 14 | **Multi-disc games grouped by title, disc chosen at launch** | User request; works for every emulator, including PCSX2, which has no .m3u | .m3u playlists only |
| 15 | **Cue sheets for "(Track N).bin" sets generated in Cache, not in the ROM folder** | ROM folders are read-only to us | Writing .cue next to ROMs |
| 16 | **Emulator profiles only for never-configured emulators, always backed up** | The user has tuned PCSX2/shadPS4 configs that must be preserved | Forcing defaults |
| 17 | **Steam games launch via steam://rungameid** | Keeps the Steam overlay and Steam Input; RunningAppID in the registry tells us when they exit | Starting the exe directly |
| 18 | **Lossless Scaling via its own global hotkey (read from Settings.xml)** | It has no CLI or API; the hotkey is the supported automation | UI automation of its window |
| 19 | **Updater: GitHub Releases of a fixed repo + SHA-256 sidecar; rename-swap with rollback** | No arbitrary URLs, verifies integrity, works on the running exe | Separate updater exe, Squirrel |
| 20 | **API keys in DPAPI (`Config\secrets.dat`)** | Encrypted per Windows user, still portable on the same account | Credential Manager (not portable), plain JSON |
| 21 | **System settings hand off to `ms-settings:` pages for now** | Real, safe controls today; native pickers come later | Fake toggles |
| 22 | **Test hook `ORBISUI_SCRIPT`** | Lets the UI be driven and screenshotted without stealing the user's keyboard focus | SendKeys (unreliable when focus moves) |
