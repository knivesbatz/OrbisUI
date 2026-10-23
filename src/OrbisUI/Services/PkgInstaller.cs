using System.Diagnostics;
using System.Text.Json;
using OrbisUI.Core;
using OrbisUI.Library;

namespace OrbisUI.Services;

/// <summary>
/// Installs PS4 .pkg files for shadPS4 with the ShadPs4Plus pkg_extractor CLI: "--check-type" exits 101 (game), 102 (update)
/// or 103 (DLC); extracting into the games folder makes CUSAxxxxx (or CUSAxxxxx-patch for updates). DLC goes to shadPS4's addon folder.
/// The .pkg itself is only read.
/// </summary>
public static class PkgInstaller
{
    public enum Kind { Unknown = 0, Game = 101, Update = 102, Dlc = 103 }

    /// <summary>pkg_extractor.exe in OrbisUI's Tools folder or next to shadPS4 (two folders deep).</summary>
    public static string? FindExtractor()
    {
        var roots = new List<string> { Path.Combine(AppPaths.Root, "Tools") };
        if (Emulators.ExePath("PS4") is string exe) roots.Add(Path.GetDirectoryName(exe)!);
        foreach (var r in roots.Where(Directory.Exists))
        {
            try
            {
                var hit = Directory.EnumerateFiles(r, "pkg_extractor.exe", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true }).FirstOrDefault();
                if (hit != null) return hit;
            }
            catch { }
        }
        return null;
    }

    /// <summary>Where shadPS4 looks for games: its first install folder, else the PS4 ROM folder.</summary>
    public static string GamesDir()
    {
        var dirs = RomScanner.ShadPs4InstallDirs();
        if (dirs.Count > 0) return dirs[0];
        var s = AppSettings.Current;
        return Path.Combine(string.IsNullOrWhiteSpace(s.RomRoot) ? AppPaths.DefaultGames : s.RomRoot!, "PS4");
    }

    /// <summary>shadPS4's addon_install_dir, else a DLC folder beside the games folder.</summary>
    public static string AddonsDir()
    {
        try
        {
            var json = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "shadPS4", "config.json");
            if (File.Exists(json))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(json));
                foreach (var sec in doc.RootElement.EnumerateObject())
                    if (sec.Value.ValueKind == JsonValueKind.Object && sec.Value.TryGetProperty("addon_install_dir", out var a) && a.GetString() is string d && d.Length > 0)
                        return d;
            }
        }
        catch (Exception ex) { Log.Warn("shadPS4 addon dir: " + ex.Message); }
        return Path.Combine(Directory.GetParent(GamesDir())?.FullName ?? GamesDir(), "DLC");
    }

    static int Run(string exe, string args, Action<string>? line = null)
    {
        var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(exe)! };
        using var p = Process.Start(psi)!;
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) Log.Warn("pkg_extractor: " + e.Data); };
        p.BeginErrorReadLine();
        string? l;
        while ((l = p.StandardOutput.ReadLine()) != null) line?.Invoke(l);
        p.WaitForExit();
        return p.ExitCode;
    }

    public static Kind CheckType(string pkg)
    {
        var exe = FindExtractor() ?? throw new FileNotFoundException("pkg_extractor.exe wasn't found next to shadPS4 or in OrbisUI's Tools folder.");
        var code = Run(exe, $"\"{pkg}\" --check-type");
        return Enum.IsDefined(typeof(Kind), code) ? (Kind)code : Kind.Unknown;
    }

    /// <summary>Extract a pkg. Returns its kind and the folder it went to. progress gets "Extracting file 3 of 58" style lines.</summary>
    public static (Kind kind, string dest) Install(string pkg, Action<string>? progress = null)
    {
        var exe = FindExtractor() ?? throw new FileNotFoundException("pkg_extractor.exe wasn't found next to shadPS4 or in OrbisUI's Tools folder.");
        var kind = CheckType(pkg);
        if (kind == Kind.Unknown) throw new InvalidDataException("This isn't a PS4 package the extractor can read.");
        var dest = kind == Kind.Dlc ? AddonsDir() : GamesDir();
        Directory.CreateDirectory(dest);
        var before = Directory.EnumerateDirectories(dest).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int last = -1;
        Run(exe, $"\"{pkg}\" \"{dest.TrimEnd('\\')}\"", l =>
        {
            var m = System.Text.RegularExpressions.Regex.Match(l, @"Extracting file (\d+) of (\d+)");
            if (m.Success && m.Groups[1].Value != last.ToString()) { last = int.Parse(m.Groups[1].Value); progress?.Invoke($"{m.Groups[1].Value} of {m.Groups[2].Value} files"); }
        });
        var made = Directory.EnumerateDirectories(dest).FirstOrDefault(d => !before.Contains(d));
        if (last < 0 && made == null) throw new IOException("The extractor didn't write any files. See Logs\\orbisui.log.");
        Log.Info($"Installed {kind} {pkg} -> {made ?? dest}");
        return (kind, made ?? dest);
    }
}
