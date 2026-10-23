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
- [x] Ally profiles for PCSX2, PPSSPP, RPCS3 and Vita3K: Vulkan, no OSD, fast boot (v0.2.4)
- [x] TV & Video folder with square apps, added from Settings
- [x] Library grid, game options, power menu, notifications, settings
- [x] Lossless Scaling hotkey activation, GitHub auto-updater
- [x] Unit tests, build script, portable ZIP

## Done (v0.2.0 to v0.2.4)
- [x] UI sounds fixed (pack WAV headers repaired), live background, boot video, home music
- [x] Display settings with revert timer, PRO mode notification, emulator fast boot
- [x] PS4 status bar header, row layout, TV & Video grid, Store / What's New / browser tiles, square corners
- [x] ReShade presets (#21), Steam shortcuts import (#10), Controller Fixer (#23), WebView2 browser (#22 part)
- [x] What's New activity feed (#24 part), SteamGridDB as default art source

## Next (priority order)
1. File manager and PS4 PKG extractor (#22)
2. Native Wi-Fi, Bluetooth and audio device pickers (#19)
3. Steam friends list and chat in the UI (#24)
4. PlayStation Store metadata source (#8); ReShade installed in every emulator folder (#13)
5. RetroAchievements, patches and texture packs (#27)
6. Verify Lossless Scaling detection on the Ally with a real game (#20)