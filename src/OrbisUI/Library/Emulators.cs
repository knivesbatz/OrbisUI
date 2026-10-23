using OrbisUI.Core;

namespace OrbisUI.Library;

public sealed record EmulatorDef(
    string System,          // PS1, PS2, PS3, PS4, PSP, VITA
    string Name,
    string[] ExeNames,      // file name patterns, first match wins
    string DefaultArgs,     // {rom} = game path, {id} = title id
    string[] Extensions,    // ROM file types, empty for folder-based systems
    string[] FolderAliases);

/// <summary>Emulator registry: what each system runs with, how to find it and how to start a game headless and fullscreen.</summary>
public static class Emulators
{
    public static readonly EmulatorDef[] All =
    {
        // DuckStation: -batch exits with the game, -nogui hides the main window, -fastboot skips the BIOS intro.
        new("PS1", "DuckStation", new[] { "duckstation-qt*.exe", "duckstation*.exe" },
            "-batch -nogui -fullscreen -fastboot -- \"{rom}\"", new[] { ".cue", ".chd", ".pbp", ".m3u", ".ecm", ".bin", ".img" }, new[] { "PS1", "PSX", "PSONE" }),
        // PCSX2: -bigpicture makes the pause menu controller-friendly, -fastboot skips the BIOS, OSD is configured in PCSX2.ini.
        new("PS2", "PCSX2", new[] { "pcsx2-qt.exe", "pcsx2*.exe" },
            "-batch -nogui -fullscreen -bigpicture -fastboot -- \"{rom}\"", new[] { ".iso", ".chd", ".cso", ".zso", ".gz", ".cue", ".bin", ".elf" }, new[] { "PS2" }),
        new("PS3", "RPCS3", new[] { "rpcs3.exe" },
            "--no-gui --fullscreen \"{rom}\"", new[] { ".iso" }, new[] { "PS3" }),
        new("PS4", "shadPS4", new[] { "shadPS4.exe" },
            "-g \"{rom}\" -f true", Array.Empty<string>(), new[] { "PS4" }),
        new("PSP", "PPSSPP", new[] { "PPSSPPWindows64.exe", "PPSSPPWindows.exe" },
            "--fullscreen --escape-exit \"{rom}\"", new[] { ".iso", ".cso", ".chd", ".pbp", ".elf" }, new[] { "PSP" }),
        new("VITA", "Vita3K", new[] { "Vita3K.exe" },
            "-F -r {id}", new[] { ".vpk" }, new[] { "VITA", "PSVITA", "PSV" }),
    };

    public static EmulatorDef? Get(string system) => All.FirstOrDefault(e => e.System.Equals(system, StringComparison.OrdinalIgnoreCase));

    public static string? ExePath(string system)
    {
        if (AppSettings.Current.Emulators.TryGetValue(system, out var s) && File.Exists(s.ExePath)) return s.ExePath;
        return null;
    }

    public static string Args(string system)
    {
        if (AppSettings.Current.Emulators.TryGetValue(system, out var s) && !string.IsNullOrWhiteSpace(s.Args)) return s.Args!;
        return Get(system)?.DefaultArgs ?? "\"{rom}\"";
    }

    /// <summary>Folders searched for emulator installs, most specific first.</summary>
    public static IEnumerable<string> SearchRoots()
    {
        if (!string.IsNullOrEmpty(AppSettings.Current.EmulatorRoot)) yield return AppSettings.Current.EmulatorRoot!;
        yield return Path.Combine(AppPaths.Root, "Emulators");
        var parent = Directory.GetParent(AppPaths.Root)?.FullName;
        if (parent != null) yield return Path.Combine(parent, "emulators");
        yield return @"C:\Sony\OrbisUI\emulators";
        yield return @"C:\Sony";
    }

    /// <summary>Find an emulator executable: the system folder (PS2\pcsx2-qt.exe) or one level below it. Source trees are never searched deeply.</summary>
    public static string? Detect(EmulatorDef def)
    {
        foreach (var root in SearchRoots().Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var candidates = new List<string>();
            foreach (var alias in def.FolderAliases.Append(def.Name))
            {
                var d = Path.Combine(root, alias);
                if (Directory.Exists(d)) candidates.Add(d);
            }
            candidates.Add(root);
            foreach (var dir in candidates)
            {
                if (FindExe(dir, def) is string hit) return hit;
                try
                {
                    foreach (var sub in Directory.EnumerateDirectories(dir))
                        if (FindExe(sub, def) is string deep) return deep;
                }
                catch { }
            }
        }
        return null;
    }

    static string? FindExe(string dir, EmulatorDef def)
    {
        foreach (var pattern in def.ExeNames)
        {
            try
            {
                var f = Directory.EnumerateFiles(dir, pattern).FirstOrDefault(x => !Path.GetFileName(x).Contains("updater", StringComparison.OrdinalIgnoreCase));
                if (f != null) return f;
            }
            catch { }
        }
        return null;
    }

    /// <summary>Fill in any emulator that is not configured yet. Returns the systems that were newly found.</summary>
    public static List<string> AutoDetect()
    {
        var found = new List<string>();
        foreach (var def in All)
        {
            if (ExePath(def.System) != null) continue;
            var exe = Detect(def);
            if (exe == null) continue;
            AppSettings.Current.Emulators[def.System] = new EmulatorSetting { ExePath = exe };
            found.Add(def.System);
            Log.Info($"Detected {def.Name}: {exe}");
        }
        return found;
    }
}
