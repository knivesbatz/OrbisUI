using OrbisUI.Core;
using OrbisUI.Library;

namespace OrbisUI.Services;

/// <summary>
/// Per-game ReShade presets. ReShade picks its preset from PresetPath= in the [GENERAL] section of the ReShade.ini
/// next to the game (or emulator) exe, so before launch we point that line at the game's chosen preset.
/// Every ini is backed up to Backups\ReShade before its first change and before each restore.
/// Games with anti-cheat are never touched: changing injected DLL config there risks a ban.
/// </summary>
public static class ReShade
{
    static readonly string[] AntiCheatMarkers =
    {
        "EasyAntiCheat", "BattlEye", "EasyAntiCheat_EOS_Setup.exe", "BEService.exe", "BEService_x64.exe",
        "PnkBstrA.exe", "PunkBuster", "xigncode", "nProtect", "GameGuard", "mhyprot2.sys", "vgk.sys", "EAAntiCheat.Installer.exe",
    };

    public static string SharedPresets => Dir(Path.Combine(AppPaths.Root, "Data", "ReShade Presets"));
    static string BackupDir => Dir(Path.Combine(AppPaths.Backups, "ReShade"));
    static string Dir(string p) { Directory.CreateDirectory(p); return p; }

    /// <summary>Folder the game's exe runs from (the emulator's folder for emulated games).</summary>
    public static string? GameDir(Game g) => g.Kind switch
    {
        LaunchKind.Emulator => Emulators.ExePath(g.Platform) is string e ? Path.GetDirectoryName(e) : null,
        LaunchKind.Steam => g.InstallDir,
        LaunchKind.Exe or LaunchKind.SteamShortcut => g.Path is string p && File.Exists(p) ? Path.GetDirectoryName(p) : g.InstallDir,
        _ => null,
    };

    public static bool HasAntiCheat(string dir)
    {
        try
        {
            // Top level and one folder down covers EasyAntiCheat\, BattlEye\ and the launchers that sit beside bin\ folders.
            var names = Directory.EnumerateFileSystemEntries(dir)
                .Concat(Directory.EnumerateDirectories(dir).SelectMany(d => { try { return Directory.EnumerateFileSystemEntries(d); } catch { return []; } }))
                .Select(Path.GetFileName);
            return names.Any(n => AntiCheatMarkers.Any(m => n!.StartsWith(m, StringComparison.OrdinalIgnoreCase)));
        }
        catch { return false; }
    }

    /// <summary>The ReShade.ini for this game, or null when ReShade isn't installed there.</summary>
    public static string? FindIni(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "ReShade.ini", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true })
                .FirstOrDefault();
        }
        catch { return null; }
    }

    public enum State { NotInstalled, AntiCheat, Ready }

    public static (State state, string? ini) Check(Game g)
    {
        var dir = GameDir(g);
        if (dir == null || !Directory.Exists(dir)) return (State.NotInstalled, null);
        if (g.Kind != LaunchKind.Emulator && HasAntiCheat(dir)) return (State.AntiCheat, null);
        var ini = FindIni(dir);
        return ini == null ? (State.NotInstalled, null) : (State.Ready, ini);
    }

    /// <summary>Preset files next to ReShade.ini (and its subfolders) plus the shared Data\ReShade Presets folder.</summary>
    public static List<string> Presets(string ini)
    {
        var opts = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true };
        return Directory.EnumerateFiles(Path.GetDirectoryName(ini)!, "*.ini", opts)
            .Concat(Directory.EnumerateFiles(SharedPresets, "*.ini", opts))
            .Where(IsPreset)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>A preset is any ini with a Techniques= line; ReShade.ini and other configs don't have one.</summary>
    public static bool IsPreset(string f)
    {
        try
        {
            if (new FileInfo(f).Length > 1 << 20) return false;
            return File.ReadLines(f).Any(l => l.TrimStart().StartsWith("Techniques=", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    public static string? CurrentPreset(string ini) => ReadPresetPath(File.ReadAllLines(ini));

    static string? ReadPresetPath(string[] lines)
    {
        bool general = false;
        foreach (var l in lines)
        {
            var t = l.Trim();
            if (t.StartsWith('[')) general = t.Equals("[GENERAL]", StringComparison.OrdinalIgnoreCase);
            else if (general && t.StartsWith("PresetPath=", StringComparison.OrdinalIgnoreCase)) return t[11..].Trim();
        }
        return null;
    }

    /// <summary>Returns the ini text with [GENERAL] PresetPath set, leaving every other line as it was.</summary>
    public static string WithPresetPath(string[] lines, string value)
    {
        var outLines = new List<string>(lines);
        int section = -1;
        for (int i = 0; i < outLines.Count; i++)
        {
            var t = outLines[i].Trim();
            if (t.StartsWith('[')) { if (section >= 0) break; if (t.Equals("[GENERAL]", StringComparison.OrdinalIgnoreCase)) section = i; continue; }
            if (section >= 0 && t.StartsWith("PresetPath=", StringComparison.OrdinalIgnoreCase)) { outLines[i] = "PresetPath=" + value; return string.Join("\r\n", outLines) + "\r\n"; }
        }
        if (section >= 0) outLines.Insert(section + 1, "PresetPath=" + value);
        else outLines.InsertRange(0, new[] { "[GENERAL]", "PresetPath=" + value, "" });
        return string.Join("\r\n", outLines) + "\r\n";
    }

    static string BackupName(string ini) =>
        string.Concat(Path.GetDirectoryName(ini)!.Split(Path.GetInvalidFileNameChars().Append(':').Append(' ').ToArray(), StringSplitOptions.RemoveEmptyEntries).TakeLast(3).Select(s => s + "_"));

    /// <summary>Backups of this ini, newest first. The oldest is the user's original.</summary>
    public static List<string> Backups(string ini) =>
        Directory.EnumerateFiles(BackupDir, BackupName(ini) + "ReShade.ini.*").OrderDescending().ToList();

    public static string Backup(string ini)
    {
        var dest = Path.Combine(BackupDir, $"{BackupName(ini)}ReShade.ini.{DateTime.Now:yyyyMMdd-HHmmss-fff}");
        File.Copy(ini, dest, false);
        return dest;
    }

    /// <summary>Points ReShade.ini at the preset (null = the user's original preset). Backs up before the first change.</summary>
    public static void SetPreset(string ini, string? preset)
    {
        var lines = File.ReadAllLines(ini);
        var backups = Backups(ini);
        if (preset == null)
        {
            if (backups.Count == 0) return; // never changed by us, nothing to undo
            preset = ReadPresetPath(File.ReadAllLines(backups[^1]));
            if (preset == null || ReadPresetPath(lines) == preset) return;
        }
        else
        {
            var dir = Path.GetDirectoryName(ini)!;
            var rel = Path.GetRelativePath(dir, preset);
            preset = rel.StartsWith("..") || Path.IsPathRooted(rel) ? preset : ".\\" + rel;
            if (ReadPresetPath(lines) == preset) return;
        }
        if (backups.Count == 0) Log.Info("ReShade.ini backed up to " + Backup(ini));
        AtomicFile.WriteAllText(ini, WithPresetPath(lines, preset));
        Log.Info($"ReShade preset set to {preset} in {ini}");
    }

    public static void Restore(string ini, string backup)
    {
        Backup(ini);
        File.Copy(backup, ini, true);
        Log.Info($"ReShade.ini restored from {backup}");
    }

    /// <summary>Called right before launch. Never throws: a ReShade problem must not stop the game.</summary>
    public static void ApplyFor(Game g)
    {
        try
        {
            var preset = AppSettings.Current.Games.GetValueOrDefault(g.Id)?.ReShadePreset;
            var (state, ini) = Check(g);
            if (state != State.Ready) return;
            if (preset != null && !File.Exists(preset)) { Notifications.Post("ReShade preset missing", Path.GetFileName(preset)); return; }
            SetPreset(ini!, preset);
        }
        catch (Exception ex) { Log.Warn("ReShade: " + ex.Message); }
    }
}
