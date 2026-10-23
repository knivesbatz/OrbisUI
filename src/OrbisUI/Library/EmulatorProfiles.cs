using OrbisUI.Core;

namespace OrbisUI.Library;

/// <summary>
/// ROG Ally starter profiles. Applied only when an emulator has never been set up (so its own first-run wizard would
/// block a headless launch). Existing configs are backed up to Backups\ before anything is written; tuned configs are left alone.
/// </summary>
public static class EmulatorProfiles
{
    public static void EnsureAll()
    {
        try { EnsureDuckStation(); } catch (Exception ex) { Log.Warn("DuckStation profile: " + ex.Message); }
        try { EnsurePcsx2QuietOsd(); } catch (Exception ex) { Log.Warn("PCSX2 profile: " + ex.Message); }
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
            if (!text.Contains("SetupWizardIncomplete = true", StringComparison.OrdinalIgnoreCase) && text.Contains("SettingsVersion")) return; // already set up
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
            Renderer = Automatic
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
    /// PCSX2 should run without on-screen messages (OsdMessagesPos = 0 hides them; they still go to PCSX2's log).
    /// Only that one key is changed, after a backup; every other setting the user tuned is kept.
    /// </summary>
    public static void EnsurePcsx2QuietOsd()
    {
        var ini = Pcsx2Ini();
        if (ini == null || !File.Exists(ini)) return;
        var text = File.ReadAllText(ini);
        var rx = new System.Text.RegularExpressions.Regex(@"^OsdMessagesPos\s*=\s*(\d+)\s*$", System.Text.RegularExpressions.RegexOptions.Multiline);
        var m = rx.Match(text);
        if (m.Success && m.Groups[1].Value == "0") return;
        Log.Info("PCSX2 config backed up to " + Backup(ini));
        if (m.Success) text = rx.Replace(text, "OsdMessagesPos = 0", 1);
        else if (text.Contains("[EmuCore/GS]")) text = text.Replace("[EmuCore/GS]", "[EmuCore/GS]\r\nOsdMessagesPos = 0");
        else text += "\r\n[EmuCore/GS]\r\nOsdMessagesPos = 0\r\n";
        AtomicFile.WriteAllText(ini, text);
        Log.Info("PCSX2 on-screen messages turned off: " + ini);
    }
}