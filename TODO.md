# TODO

Each item maps to a [GitHub issue](https://github.com/knivesbatz/OrbisUI/issues). The status of each feature is in [FEATURES.md](FEATURES.md).

## Done (v0.1.0)
- [x] Portable layout, settings, logging, DPAPI secrets
- [x] Setup wizard, boot + login screen
- [x] PS4 home: square tiles, top function bar, info bar, SteamGridDB backgrounds after 5 s
- [x] DualShock 4 via SDL3, keyboard fallback, controller test
- [x] PS4 sound pack and icon pack import (kept out of the public repo)
- [x] Steam games and shortcuts, ROM scanning, PS4/PS3/Vita installed titles, multi-disc grouping and disc picker
- [x] Artwork pipeline with SteamGridDB override and controller art picker
- [x] Launch, splash, return home, PS button over emulators
- [x] DuckStation Ally profile (backup first)
- [x] TV & Video folder with square apps, added from Settings
- [x] Library grid, game options, power menu, notifications, settings
- [x] Lossless Scaling hotkey activation, GitHub auto-updater
- [x] Unit tests, build script, portable ZIP

## Next (priority order)
1. Verify Steam launch, the return to home and Lossless Scaling on the Ally; fix whatever turns up
2. PCSX2: make sure the on-screen messages are off in a backed-up profile; PPSSPP, RPCS3, Vita3K and shadPS4 Ally profiles (#13)
3. Import emulated and PC games into Steam via shortcuts.vdf, with backup and Steam closed (#10)
4. Steam friends, activity and achievements through the Steam Web API key (#24)
5. Native Wi-Fi, Bluetooth and audio device pickers instead of Windows Settings handoffs (#19)
6. WebView2 browser with OSK and downloads, plus a file manager and PKG extraction (#22)
7. Controller Fixer (#23)
8. ReShade preset selector (#21)
9. RetroAchievements, patches and texture packs (#27)
10. Bundle VC++ runtime check / emulator download helper
