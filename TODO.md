# TODO

Open work only; everything implemented is listed in the README's Features section and in [FEATURES.md](FEATURES.md). Items map to [GitHub issues](https://github.com/knivesbatz/OrbisUI/issues) where one exists.

1. Bloodborne: install the base game and the 1.09 update, then test (configs and patches are ready)
2. PSP test ROM is a .7z, which PPSSPP can't open; keep an extracted .iso for tests
3. RPCS3: confirm Minecraft (PS3) reaches gameplay; PS3 shader compiling takes too long. (Firmware now installs with `--headless`: no prompt or window; RPCS3 still exits 0xC0000409 afterwards, so success is checked by the installed files.)
4. Persona 3 FES (PS2) boots (log) but no picture was captured yet
5. Gravity Rush (Vita): Vita3K runs it at 30 FPS but shows a black screen, even when launched directly
6. Texture packs: no trusted source to download them from (#27)

## Where the work stands (paused 2026-10-30)

- v0.4.8 is on `main` and running in the personal copy, but it is **not published**. `dist` holds the zip, `.sha256` and `notes-0.4.8.md`. To publish, run `gh release create v0.4.8 dist/OrbisUI-0.4.8-win-x64.zip dist/OrbisUI-0.4.8-win-x64.zip.sha256 --title "OrbisUI 0.4.8" --notes-file dist/notes-0.4.8.md --target main`, then confirm the auto-updater sees it. Never upload the personal zip.
- Item 3 was being investigated when work paused. The 2026-10-26 Minecraft log shows about 10 minutes at 100% CPU with almost no log output before the game's PPU compile starts, and the cause isn't confirmed. A 2026-10-29 windowed retest ran about 3.5 minutes and then hung at exit ("rsx::thread is too sleepy"). Gameplay has not been confirmed.
- Item 5, not tried yet: windowed mode, OpenGL, HDR off, 1x resolution, a newer Vita3K, another Vita game.
