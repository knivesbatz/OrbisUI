using OrbisUI.Core;

namespace OrbisUI.Library;

/// <summary>
/// ROG Ally starter profiles. Applied only when an emulator has never been set up (so its own first-run wizard would
/// block a headless launch). Tuned configs only get the Ally essentials (Vulkan, no OSD, fast boot), always after a backup to Backups.
/// </summary>
public static class EmulatorProfiles
{
    public static void EnsureAll()
    {
        try { EnsureDuckStation(); } catch (Exception ex) { Log.Warn("DuckStation profile: " + ex.Message); }
        try { EnsurePcsx2QuietOsd(); } catch (Exception ex) { Log.Warn("PCSX2 profile: " + ex.Message); }
        try { EnsureShadPs4NoSplash(); } catch (Exception ex) { Log.Warn("shadPS4 profile: " + ex.Message); }
        try { EnsureShadPs4Ally(); } catch (Exception ex) { Log.Warn("shadPS4 Ally profile: " + ex.Message); }
        try { EnsureStarter("PSP", Path.Combine("memstick", "PSP", "SYSTEM", "ppsspp.ini"), PpssppIni); } catch (Exception ex) { Log.Warn("PPSSPP profile: " + ex.Message); }
        try { EnsureStarter("PS3", Path.Combine("config", "config.yml"), Rpcs3Yml); } catch (Exception ex) { Log.Warn("RPCS3 profile: " + ex.Message); }
        try { EnsureVita3k(); } catch (Exception ex) { Log.Warn("Vita3K profile: " + ex.Message); }
        try { Services.ReShade.EnsureEmulators(); } catch (Exception ex) { Log.Warn("ReShade for emulators: " + ex.Message); }
    }

    /// <summary>
    /// Set "Key = value" lines in an ini, adding missing ones under [section]. Backs the file up once if anything changes.
    /// Keys are matched anywhere in the file, so only use keys that are unique to one section. Returns true when the file changed.
    /// </summary>
    public static bool SetIniKeys(string ini, string section, params (string key, string value)[] keys)
    {
        var text = File.ReadAllText(ini);
        var orig = text;
        foreach (var (key, value) in keys)
        {
            var rx = new System.Text.RegularExpressions.Regex($@"^{System.Text.RegularExpressions.Regex.Escape(key)}[ \t]*=[^\r\n]*", System.Text.RegularExpressions.RegexOptions.Multiline);
            if (rx.IsMatch(text)) text = rx.Replace(text, $"{key} = {value}", 1);
            else if (text.Contains($"[{section}]")) text = text.Replace($"[{section}]", $"[{section}]\r\n{key} = {value}");
            else text += $"\r\n[{section}]\r\n{key} = {value}\r\n";
        }
        if (text == orig) return false;
        Log.Info("Config backed up to " + Backup(ini));
        AtomicFile.WriteAllText(ini, text);
        return true;
    }

    /// <summary>Write an Ally starter config for a portable emulator only when it has none yet; existing configs are never replaced.</summary>
    static void EnsureStarter(string system, string relPath, string content)
    {
        var exe = Emulators.ExePath(system);
        if (exe == null) return;
        var file = Path.Combine(Path.GetDirectoryName(exe)!, relPath);
        if (File.Exists(file)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        AtomicFile.WriteAllText(file, content);
        Log.Info($"{system} ROG Ally profile written: {file}");
    }

    // PPSSPP (portable memstick next to the exe): Vulkan, 3x internal (1440x816), fullscreen, no FPS counter, OSD or update check.
    const string PpssppIni = """
        [General]
        FirstRun = False
        CheckForNewVersion = False
        ShowOnScreenMessages = False
        [Graphics]
        GraphicsBackend = 3 (VULKAN)
        InternalResolution = 3
        FullScreen = True
        ShowFPSCounter = 0
        """;

    // RPCS3: Vulkan, 720p 16:9, no performance overlay or compilation hints, fullscreen.
    const string Rpcs3Yml = """
        Video:
          Renderer: Vulkan
          Resolution: 1280x720
          Aspect ratio: 16:9
          Performance Overlay:
            Enabled: false
        Miscellaneous:
          Start games in fullscreen mode: true
          Show shader compilation hint: false
          Show PPU compilation hint: false
          Show autosave/autoload hint: false
        """;

    // Vita3K: Vulkan, boot straight into the game (no live area), fullscreen, no welcome dialog or performance overlay.
    // Vita3K refuses to start ("Failed to initialise config") on an unknown key or a wrong value type, so only these keys.
    static readonly (string key, string value)[] Vita3kKeys =
    {
        ("backend-renderer", "Vulkan"), ("boot-apps-full-screen", "true"), ("show-live-area-screen", "false"),
        ("show-welcome", "false"), ("performance-overlay", "false"),
    };

    /// <summary>Vita3K's welcome dialog blocks a direct game boot, so its own config gets the Ally keys too (after a backup).</summary>
    static void EnsureVita3k()
    {
        var exe = Emulators.ExePath("VITA");
        if (exe == null) return;
        var file = Path.Combine(Path.GetDirectoryName(exe)!, "config.yml");
        if (!File.Exists(file))
        {
            AtomicFile.WriteAllText(file, "---\n" + string.Join("\n", Vita3kKeys.Select(k => $"{k.key}: {k.value}")) + "\n...\n");
            Log.Info($"VITA ROG Ally profile written: {file}");
            return;
        }
        var text = File.ReadAllText(file);
        var orig = text;
        // An older OrbisUI starter wrote this key, which stops Vita3K from starting.
        text = System.Text.RegularExpressions.Regex.Replace(text, @"^check-for-updates:[^\r\n]*\r?\n?", "", System.Text.RegularExpressions.RegexOptions.Multiline);
        foreach (var (key, value) in Vita3kKeys)
        {
            var rx = new System.Text.RegularExpressions.Regex($@"^{key}:[^\r\n]*", System.Text.RegularExpressions.RegexOptions.Multiline);
            text = rx.IsMatch(text) ? rx.Replace(text, $"{key}: {value}", 1) : text.Replace("\n...", $"\n{key}: {value}\n...");
        }
        if (text == orig) return;
        Log.Info("Config backed up to " + Backup(file));
        AtomicFile.WriteAllText(file, text);
    }

    static string Backup(string file)
    {
        var dest = Path.Combine(AppPaths.Backups, $"{Path.GetFileName(Path.GetDirectoryName(file))}-{Path.GetFileName(file)}-{DateTime.Now:yyyyMMdd-HHmmss}");
        File.Copy(file, dest, true);
        return dest;
    }

    /// <summary>DuckStation keeps settings next to the exe in portable mode, otherwise in Documents\DuckStation or %LOCALAPPDATA%\DuckStation.</summary>
    public static string? DuckStationIni()
    {
        var exe = Emulators.ExePath("PS1");
        if (exe == null) return null;
        var dir = Path.GetDirectoryName(exe)!;
        if (File.Exists(Path.Combine(dir, "portable.txt")) || File.Exists(Path.Combine(dir, "settings.ini"))) return Path.Combine(dir, "settings.ini");
        var docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DuckStation");
        if (Directory.Exists(docs)) return Path.Combine(docs, "settings.ini");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DuckStation", "settings.ini");
    }

    static string? FindPs1Bios()
    {
        var exe = Emulators.ExePath("PS1");
        var dirs = new List<string>();
        if (exe != null) dirs.Add(Path.Combine(Path.GetDirectoryName(exe)!, "BIOS"));
        dirs.Add(AppPaths.DefaultFirmware);
        dirs.Add(Path.Combine(AppPaths.DefaultFirmware, "PS1"));
        foreach (var d in Emulators.SearchRoots()) { dirs.Add(Path.Combine(Directory.GetParent(d)?.FullName ?? d, "BIOS")); }
        return dirs.FirstOrDefault(d => Directory.Exists(d) && Directory.EnumerateFiles(d).Any(f => Path.GetFileName(f).StartsWith("scph", StringComparison.OrdinalIgnoreCase)));
    }

    public static void EnsureDuckStation()
    {
        var ini = DuckStationIni();
        if (ini == null) return;
        if (File.Exists(ini))
        {
            var text = File.ReadAllText(ini);
            if (!text.Contains("SetupWizardIncomplete = true", StringComparison.OrdinalIgnoreCase) && text.Contains("SettingsVersion"))
            {
                // Already set up: keep the user's tuning, only make sure it renders with Vulkan and shows no OSD.
                SetIniKeys(ini, "GPU", ("Renderer", "Vulkan"));
                SetIniKeys(ini, "Display", ("ShowOSDMessages", "false"), ("ShowFPS", "false"));
                return;
            }
            Log.Info("DuckStation config backed up to " + Backup(ini));
        }
        var bios = FindPs1Bios();
        Directory.CreateDirectory(Path.GetDirectoryName(ini)!);
        // 720p internal (3x of 240p), 16:9 widescreen hack, bilinear smoothing, no OSD, DS4 through SDL.
        var sdl = "SDL-0/";
        var text2 = $"""
            [Main]
            SettingsVersion = 3
            SetupWizardIncomplete = false
            ConfirmPowerOff = false
            StartFullscreen = true
            PauseOnFocusLoss = false
            EnableDiscordPresence = false
            InhibitScreensaver = true

            [AutoUpdater]
            CheckAtStartup = false

            [BIOS]
            {(bios != null ? $"SearchDirectory = {bios}" : "")}

            [GPU]
            Renderer = Vulkan
            ResolutionScale = 3
            TextureFilter = Bilinear
            WidescreenHack = true
            PGXPEnable = true

            [Display]
            AspectRatio = 16:9
            ShowOSDMessages = false
            ShowFPS = false

            [InputSources]
            SDL = true
            SDLControllerEnhancedMode = true

            [Pad1]
            Type = AnalogController
            ForceAnalogOnReset = true
            Up = {sdl}DPadUp
            Right = {sdl}DPadRight
            Down = {sdl}DPadDown
            Left = {sdl}DPadLeft
            Triangle = {sdl}Y
            Circle = {sdl}B
            Cross = {sdl}A
            Square = {sdl}X
            Select = {sdl}Back
            Start = {sdl}Start
            L1 = {sdl}LeftShoulder
            R1 = {sdl}RightShoulder
            L2 = {sdl}+LeftTrigger
            R2 = {sdl}+RightTrigger
            L3 = {sdl}LeftStick
            R3 = {sdl}RightStick
            LLeft = {sdl}-LeftX
            LRight = {sdl}+LeftX
            LDown = {sdl}+LeftY
            LUp = {sdl}-LeftY
            RLeft = {sdl}-RightX
            RRight = {sdl}+RightX
            RDown = {sdl}+RightY
            RUp = {sdl}-RightY
            SmallMotor = {sdl}SmallMotor
            LargeMotor = {sdl}LargeMotor

            [Hotkeys]
            OpenPauseMenu = {sdl}Touchpad
            """;
        AtomicFile.WriteAllText(ini, text2);
        Log.Info("DuckStation ROG Ally profile written: " + ini);
    }

    public static string? Pcsx2Ini()
    {
        var exe = Emulators.ExePath("PS2");
        if (exe != null)
        {
            var dir = Path.GetDirectoryName(exe)!;
            if (File.Exists(Path.Combine(dir, "portable.ini")) || File.Exists(Path.Combine(dir, "portable.txt"))) return Path.Combine(dir, "inis", "PCSX2.ini");
        }
        var docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PCSX2", "inis", "PCSX2.ini");
        return File.Exists(docs) ? docs : null;
    }

    /// <summary>
    /// PCSX2 on the Ally: Vulkan, no on-screen messages (OsdMessagesPos = 0; they still go to the log), fast boot, widescreen patches.
    /// Only those keys are changed, after a backup; every other setting the user tuned is kept.
    /// </summary>
    public static void EnsurePcsx2QuietOsd()
    {
        var ini = Pcsx2Ini();
        if (ini == null || !File.Exists(ini)) return;
        // Renderer 14 = Vulkan. Fast boot and widescreen patches live in [EmuCore].
        if (SetIniKeys(ini, "EmuCore/GS", ("OsdMessagesPos", "0"), ("Renderer", "14"))) Log.Info("PCSX2 set to Vulkan, no OSD: " + ini);
        if (SetIniKeys(ini, "EmuCore", ("EnableFastBoot", "true"), ("EnableWideScreenPatches", "true"))) Log.Info("PCSX2 fast boot and widescreen on: " + ini);
    }

    // Bloodborne on the Ally: skip the logos, 60 fps with frame pacing fixes, and the two cheapest visual costs off.
    // Only applied while no Bloodborne patch is enabled yet, so choices made later in shadPS4's patch manager stick.
    static readonly string[] BloodbornePatches = { "Skip Intro", "60 FPS++", "Disable Chromatic Aberration", "Disable Motion Blur (Perf Increase)" };

    /// <summary>
    /// shadPS4 for the Ally: pipeline cache on (no shader stutter after the first visit to an area) and the Bloodborne patches
    /// above. Files are backed up first. Patches target the 1.09 game update.
    /// </summary>
    public static void EnsureShadPs4Ally()
    {
        var appdata = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "shadPS4");
        var cfg = Path.Combine(appdata, "config.json");
        if (File.Exists(cfg) && File.ReadAllText(cfg) is var json && json.Contains("\"pipeline_cache_enabled\": false"))
        {
            Log.Info("shadPS4 config backed up to " + Backup(cfg));
            AtomicFile.WriteAllText(cfg, json.Replace("\"pipeline_cache_enabled\": false", "\"pipeline_cache_enabled\": true"));
            Log.Info("shadPS4 pipeline cache turned on");
        }
        var bb = Path.Combine(appdata, "patches", "shadPS4", "Bloodborne.xml");
        if (!File.Exists(bb)) return;
        var xml = File.ReadAllText(bb);
        if (xml.Contains("isEnabled=\"true\"")) return;
        var updated = xml;
        foreach (var name in BloodbornePatches)
            updated = System.Text.RegularExpressions.Regex.Replace(updated, $"(<Metadata [^>]*Name=\"{System.Text.RegularExpressions.Regex.Escape(name)}\"[^>]*isEnabled=)\"false\"", "$1\"true\"");
        if (updated == xml) return;
        Log.Info("Bloodborne patches backed up to " + Backup(bb));
        AtomicFile.WriteAllText(bb, updated);
        Log.Info("Bloodborne patches on: " + string.Join(", ", BloodbornePatches));
    }

    /// <summary>shadPS4 shows the game's splash image before booting; turn it off (config.json in newer builds, config.toml in older ones). Backed up first.</summary>
    public static void EnsureShadPs4NoSplash()
    {
        var exe = Emulators.ExePath("PS4");
        var dirs = new[] { exe != null ? Path.Combine(Path.GetDirectoryName(exe)!, "user") : null, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "shadPS4") };
        foreach (var dir in dirs.OfType<string>())
            foreach (var (name, on, off) in new[] { ("config.json", "\"show_splash\": true", "\"show_splash\": false"), ("config.toml", "showSplash = true", "showSplash = false") })
            {
                var file = Path.Combine(dir, name);
                if (!File.Exists(file)) continue;
                var text = File.ReadAllText(file);
                if (!text.Contains(on)) continue;
                Log.Info("shadPS4 config backed up to " + Backup(file));
                AtomicFile.WriteAllText(file, text.Replace(on, off));
                Log.Info("shadPS4 splash turned off: " + file);
            }
    }
}