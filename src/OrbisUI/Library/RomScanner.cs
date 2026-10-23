using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OrbisUI.Core;

namespace OrbisUI.Library;

/// <summary>Finds emulated games: ROM files under the ROM root (one folder per system) plus titles installed inside emulators.</summary>
public static partial class RomScanner
{
    const int MaxDepth = 4;

    public static string StableHash(string s)
        => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s.ToLowerInvariant()))).Substring(0, 12);

    [GeneratedRegex(@"\s*[\(\[][^\)\]]*[\)\]]")] private static partial Regex TagRx();
    [GeneratedRegex(@"\((Disc|Disk) (\d+)\)", RegexOptions.IgnoreCase)] private static partial Regex DiscRx();
    [GeneratedRegex(@"\(Track (\d+)\)", RegexOptions.IgnoreCase)] private static partial Regex TrackRx();
    [GeneratedRegex(@"\s{2,}")] private static partial Regex SpaceRx();

    /// <summary>"Shadow Hearts - Covenant (USA) (Disc 1).iso" -> "Shadow Hearts - Covenant (Disc 1)".</summary>
    public static string CleanTitle(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var disc = DiscRx().Match(name);
        var clean = TagRx().Replace(name, "").Replace('_', ' ').Trim();
        clean = SpaceRx().Replace(clean, " ");
        // "Legend of Zelda, The" -> "The Legend of Zelda"
        if (clean.EndsWith(", The", StringComparison.OrdinalIgnoreCase)) clean = "The " + clean[..^5];
        if (disc.Success) clean += $" (Disc {disc.Groups[2].Value})";
        return clean.Length > 0 ? clean : name;
    }

    /// <summary>All folders to scan with the system they belong to.</summary>
    public static List<(string system, string dir)> Roots()
    {
        var list = new List<(string, string)>();
        var s = AppSettings.Current;
        var romRoot = string.IsNullOrWhiteSpace(s.RomRoot) ? AppPaths.DefaultGames : s.RomRoot!;
        foreach (var def in Emulators.All)
        {
            if (!Directory.Exists(romRoot)) break;
            foreach (var sub in Directory.EnumerateDirectories(romRoot))
                if (def.FolderAliases.Any(a => a.Equals(Path.GetFileName(sub), StringComparison.OrdinalIgnoreCase)))
                    list.Add((def.System, sub));
        }
        foreach (var extra in s.ExtraRomDirs)
        {
            var parts = extra.Split('|', 2);
            if (parts.Length == 2 && Directory.Exists(parts[1])) list.Add((parts[0].ToUpperInvariant(), parts[1]));
        }
        return list.Distinct().ToList();
    }

    public static List<Game> Scan(CancellationToken ct = default)
    {
        var games = new List<Game>();
        foreach (var (system, dir) in Roots())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (system == "PS4") games.AddRange(ScanPs4Folder(dir, 3));
                else if (system == "PS3") { games.AddRange(ScanPs3Folders(dir)); games.AddRange(ScanFiles("PS3", dir)); }
                else if (system == "VITA") games.AddRange(ScanFiles("VITA", dir));
                else games.AddRange(ScanFiles(system, dir));
            }
            catch (Exception ex) { Log.Warn($"Scan {system} {dir}: {ex.Message}"); }
        }
        games.AddRange(ShadPs4InstallDirs().SelectMany(d => ScanPs4Folder(d, 2)));
        games.AddRange(Rpcs3Installed());
        games.AddRange(Vita3kInstalled());
        return games.GroupBy(g => g.Id).Select(g => g.First()).ToList();
    }

    static IEnumerable<string> Walk(string dir, int depth)
    {
        IEnumerable<string> files = Array.Empty<string>();
        try { files = Directory.EnumerateFiles(dir).ToList(); } catch { }
        foreach (var f in files) yield return f;
        if (depth <= 0) yield break;
        IEnumerable<string> subs = Array.Empty<string>();
        try { subs = Directory.EnumerateDirectories(dir).ToList(); } catch { }
        foreach (var s in subs)
            foreach (var f in Walk(s, depth - 1)) yield return f;
    }

    internal static List<Game> ScanFiles(string system, string dir)
    {
        var def = Emulators.Get(system)!;
        var exts = new HashSet<string>(def.Extensions, StringComparer.OrdinalIgnoreCase);
        var files = Walk(dir, MaxDepth).Where(f => exts.Contains(Path.GetExtension(f))).ToList();
        var games = new List<Game>();

        var byDir = files.GroupBy(f => Path.GetDirectoryName(f)!, StringComparer.OrdinalIgnoreCase);
        foreach (var grp in byDir)
        {
            var list = grp.ToList();
            bool hasSheet = list.Any(f => Path.GetExtension(f).Equals(".cue", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(f).Equals(".m3u", StringComparison.OrdinalIgnoreCase));
            foreach (var f in list)
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext is ".bin" or ".img")
                {
                    if (hasSheet) continue;                     // the .cue already describes the bins
                    var tr = TrackRx().Match(Path.GetFileName(f));
                    if (tr.Success && tr.Groups[1].Value != "1") continue;
                }
                string launchPath = f;
                if (ext is ".bin" or ".img" && system is "PS1" or "PS2")
                    launchPath = MakeCue(f) ?? f;                // multi-track dumps without a .cue sheet
                games.Add(new Game
                {
                    Id = $"emu:{system}:{StableHash(f)}", Title = CleanTitle(f), Platform = system, Kind = LaunchKind.Emulator,
                    Path = launchPath, RomFile = f, SizeBytes = SafeSize(f),
                });
            }
        }
        // Vita .vpk files are install packages; they are listed once installed in Vita3K instead.
        return system == "VITA" ? new List<Game>() : GroupDiscs(system, games);
    }

    /// <summary>"Game (Disc 1)", "Game (Disc 2)" become one entry; the disc is chosen when starting it.</summary>
    internal static List<Game> GroupDiscs(string system, List<Game> games)
    {
        var result = new List<Game>();
        foreach (var grp in games.GroupBy(g => DiscTitleRx().Replace(g.Title, "").Trim(), StringComparer.OrdinalIgnoreCase))
        {
            var discs = grp.Where(g => DiscTitleRx().IsMatch(g.Title)).ToList();
            if (discs.Count < 2) { result.AddRange(grp); continue; }
            var ordered = discs.OrderBy(g => int.Parse(DiscTitleRx().Match(g.Title).Groups[1].Value)).ToList();
            var first = ordered[0];
            result.Add(new Game
            {
                Id = $"emu:{system}:{StableHash(system + "|" + grp.Key)}", Title = grp.Key, Platform = system, Kind = LaunchKind.Emulator,
                Path = first.Path, RomFile = first.RomFile, SizeBytes = ordered.Sum(g => g.SizeBytes),
                Discs = ordered.Select(g => g.Path!).ToList(),
            });
            result.AddRange(grp.Except(discs));
        }
        return result;
    }

    [GeneratedRegex(@"\s*\(Disc (\d+)\)", RegexOptions.IgnoreCase)] private static partial Regex DiscTitleRx();

    static long SafeSize(string f) { try { return new FileInfo(f).Length; } catch { return 0; } }

    /// <summary>
    /// Builds a cue sheet in the cache (the user's ROM folder is never written to) for "(Track N).bin" sets
    /// that ship without one, so audio tracks play correctly.
    /// </summary>
    internal static string? MakeCue(string bin)
    {
        try
        {
            var dir = Path.GetDirectoryName(bin)!;
            var name = Path.GetFileName(bin);
            var tr = TrackRx().Match(name);
            List<string> tracks;
            if (tr.Success)
            {
                var prefix = name[..tr.Index];
                tracks = Directory.GetFiles(dir, "*.bin")
                    .Where(f => Path.GetFileName(f).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && TrackRx().IsMatch(Path.GetFileName(f)))
                    .OrderBy(f => int.Parse(TrackRx().Match(Path.GetFileName(f)).Groups[1].Value)).ToList();
            }
            else tracks = new List<string> { bin };

            var sb = new StringBuilder();
            for (int i = 0; i < tracks.Count; i++)
            {
                sb.AppendLine($"FILE \"{tracks[i]}\" BINARY");
                if (i == 0) sb.AppendLine("  TRACK 01 MODE2/2352").AppendLine("    INDEX 01 00:00:00");
                else sb.AppendLine($"  TRACK {i + 1:00} AUDIO").AppendLine("    INDEX 00 00:00:00").AppendLine("    INDEX 01 00:02:00");
            }
            var cueDir = Path.Combine(AppPaths.Cache, "Cues");
            Directory.CreateDirectory(cueDir);
            var cue = Path.Combine(cueDir, StableHash(bin) + ".cue");
            var text = sb.ToString();
            if (!File.Exists(cue) || File.ReadAllText(cue) != text) AtomicFile.WriteAllText(cue, text);
            return cue;
        }
        catch (Exception ex) { Log.Warn("cue for " + bin + ": " + ex.Message); return null; }
    }

    // ---- PS4 (shadPS4): extracted game folders with eboot.bin + sce_sys/param.sfo ----
    internal static IEnumerable<Game> ScanPs4Folder(string dir, int depth)
    {
        if (!Directory.Exists(dir)) yield break;
        var stack = new Stack<(string, int)>();
        stack.Push((dir, depth));
        while (stack.Count > 0)
        {
            var (d, left) = stack.Pop();
            var eboot = Path.Combine(d, "eboot.bin");
            var sfo = Path.Combine(d, "sce_sys", "param.sfo");
            if (File.Exists(eboot) && File.Exists(sfo))
            {
                var p = Sfo.Read(sfo);
                var cat = p.GetValueOrDefault("CATEGORY") ?? "gd";
                if (cat.StartsWith("gp", StringComparison.OrdinalIgnoreCase)) continue; // patch folder
                var id = p.GetValueOrDefault("TITLE_ID") ?? Path.GetFileName(d);
                yield return new Game
                {
                    Id = "emu:PS4:" + id, Title = p.GetValueOrDefault("TITLE") ?? id, TitleId = id, Platform = "PS4",
                    Kind = LaunchKind.Emulator, Path = eboot, InstallDir = d,
                    LocalArtHint = ExistingOrNull(Path.Combine(d, "sce_sys", "icon0.png")),
                    LocalBgHint = ExistingOrNull(Path.Combine(d, "sce_sys", "pic1.png")) ?? ExistingOrNull(Path.Combine(d, "sce_sys", "pic0.png")),
                };
                continue;
            }
            if (left <= 0) continue;
            try { foreach (var s in Directory.EnumerateDirectories(d)) stack.Push((s, left - 1)); } catch { }
        }
    }

    static string? ExistingOrNull(string p) => File.Exists(p) ? p : null;

    /// <summary>Game folders shadPS4 already knows about (its own settings, read only).</summary>
    internal static List<string> ShadPs4InstallDirs()
    {
        var dirs = new List<string>();
        var appdata = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "shadPS4");
        var exe = Emulators.ExePath("PS4");
        var portable = exe == null ? null : Path.Combine(Path.GetDirectoryName(exe)!, "user");
        foreach (var root in new[] { portable, appdata })
        {
            if (root == null) continue;
            try
            {
                var json = Path.Combine(root, "config.json");
                if (File.Exists(json))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(json));
                    if (doc.RootElement.TryGetProperty("General", out var gen) && gen.TryGetProperty("install_dirs", out var arr))
                        foreach (var e in arr.EnumerateArray())
                            if (e.TryGetProperty("path", out var p) && p.GetString() is string s) dirs.Add(s);
                }
                var toml = Path.Combine(root, "config.toml");
                if (File.Exists(toml))
                {
                    var m = Regex.Match(File.ReadAllText(toml), @"installDirs\s*=\s*\[(.*?)\]", RegexOptions.Singleline);
                    foreach (Match q in Regex.Matches(m.Groups[1].Value, "\"([^\"]+)\"")) dirs.Add(q.Groups[1].Value.Replace("\\\\", "\\"));
                }
            }
            catch (Exception ex) { Log.Warn("shadPS4 config: " + ex.Message); }
        }
        return dirs.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ---- PS3 (RPCS3) ----
    internal static IEnumerable<Game> ScanPs3Folders(string dir)
    {
        foreach (var d in SafeDirs(dir))
        {
            var sfo = new[] { Path.Combine(d, "PS3_GAME", "PARAM.SFO"), Path.Combine(d, "PARAM.SFO") }.FirstOrDefault(File.Exists);
            if (sfo != null) yield return Ps3Game(Path.GetDirectoryName(sfo)!, d, sfo);
        }
    }

    static IEnumerable<string> SafeDirs(string d) { try { return Directory.EnumerateDirectories(d).ToList(); } catch { return Array.Empty<string>(); } }

    static Game Ps3Game(string contentDir, string launchDir, string sfo)
    {
        var p = Sfo.Read(sfo);
        var id = p.GetValueOrDefault("TITLE_ID") ?? Path.GetFileName(launchDir);
        var eboot = Path.Combine(contentDir, "USRDIR", "EBOOT.BIN");
        return new Game
        {
            Id = "emu:PS3:" + id, Title = (p.GetValueOrDefault("TITLE") ?? id).Replace("\n", " "), TitleId = id, Platform = "PS3",
            Kind = LaunchKind.Emulator, Path = File.Exists(eboot) ? eboot : launchDir, InstallDir = launchDir,
            LocalArtHint = ExistingOrNull(Path.Combine(contentDir, "ICON0.PNG")),
            LocalBgHint = ExistingOrNull(Path.Combine(contentDir, "PIC1.PNG")),
        };
    }

    internal static List<Game> Rpcs3Installed()
    {
        var list = new List<Game>();
        var exe = Emulators.ExePath("PS3");
        if (exe == null) return list;
        var root = Path.GetDirectoryName(exe)!;
        // Disc games registered in RPCS3 (config/games.yml: "BLUS12345: D:/path/")
        foreach (var yml in new[] { Path.Combine(root, "config", "games.yml"), Path.Combine(root, "games.yml") })
        {
            if (!File.Exists(yml)) continue;
            foreach (var line in File.ReadAllLines(yml))
            {
                var i = line.IndexOf(": ", StringComparison.Ordinal);
                if (i <= 0) continue;
                var path = line[(i + 2)..].Trim().Trim('"').Replace('/', '\\');
                var sfo = Path.Combine(path, "PS3_GAME", "PARAM.SFO");
                if (File.Exists(sfo)) list.Add(Ps3Game(Path.Combine(path, "PS3_GAME"), path, sfo));
            }
        }
        // Digital titles installed to the virtual HDD (category HG); GD entries are updates/data.
        var hdd = Path.Combine(root, "dev_hdd0", "game");
        foreach (var d in SafeDirs(hdd))
        {
            var sfo = Path.Combine(d, "PARAM.SFO");
            if (!File.Exists(sfo) || !File.Exists(Path.Combine(d, "USRDIR", "EBOOT.BIN"))) continue;
            if (Sfo.Read(sfo).GetValueOrDefault("CATEGORY") != "HG") continue;
            list.Add(Ps3Game(d, d, sfo));
        }
        return list;
    }

    // ---- PS Vita (Vita3K): installed apps under ux0/app/TITLEID ----
    internal static List<Game> Vita3kInstalled()
    {
        var list = new List<Game>();
        var exe = Emulators.ExePath("VITA");
        if (exe == null) return list;
        var root = Path.GetDirectoryName(exe)!;
        var pref = root;
        var cfg = Path.Combine(root, "config.yml");
        if (File.Exists(cfg))
        {
            var m = Regex.Match(File.ReadAllText(cfg), @"^pref-path:\s*(.+)$", RegexOptions.Multiline);
            if (m.Success && !string.IsNullOrWhiteSpace(m.Groups[1].Value.Trim().Trim('\'', '"'))) pref = m.Groups[1].Value.Trim().Trim('\'', '"');
        }
        foreach (var d in SafeDirs(Path.Combine(pref, "ux0", "app")))
        {
            var sfo = Path.Combine(d, "sce_sys", "param.sfo");
            if (!File.Exists(sfo)) continue;
            var p = Sfo.Read(sfo);
            var id = Path.GetFileName(d);
            list.Add(new Game
            {
                Id = "emu:VITA:" + id, Title = p.GetValueOrDefault("STITLE") ?? p.GetValueOrDefault("TITLE") ?? id, TitleId = id,
                Platform = "VITA", Kind = LaunchKind.Emulator, Path = d, InstallDir = d,
                LocalArtHint = ExistingOrNull(Path.Combine(d, "sce_sys", "icon0.png")),
                LocalBgHint = ExistingOrNull(Path.Combine(d, "sce_sys", "pic0.png")),
            });
        }
        return list;
    }

    /// <summary>PCSX2's own game list folders (read only), offered as extra PS2 folders during setup.</summary>
    public static List<string> Pcsx2GameDirs()
    {
        var result = new List<string>();
        var exe = Emulators.ExePath("PS2");
        var inis = new List<string> { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PCSX2", "inis", "PCSX2.ini") };
        if (exe != null) inis.Insert(0, Path.Combine(Path.GetDirectoryName(exe)!, "inis", "PCSX2.ini"));
        foreach (var ini in inis.Where(File.Exists))
            foreach (var line in File.ReadAllLines(ini))
            {
                var m = Regex.Match(line, @"^\s*(RecursivePaths|Paths)\s*=\s*(.+)$");
                if (m.Success && Directory.Exists(m.Groups[2].Value.Trim())) result.Add(m.Groups[2].Value.Trim());
            }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
