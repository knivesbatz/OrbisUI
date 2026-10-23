using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using OrbisUI.Core;
using OrbisUI.Library;

namespace OrbisUI.Services;

/// <summary>Finds and installs DualShock button-prompt mods. Every install is backed up first and can be undone.</summary>
public static class ControllerFixer
{
    public sealed record Install(string GameId, string Target, string Archive, DateTime When, List<string> Added, List<string> Replaced);

    public static readonly string[] ArchiveTypes = { ".zip", ".7z", ".rar" };

    /// <summary>Web searches for the game's PS button-prompt mods: everywhere, then Nexus Mods only.</summary>
    public static (string label, string url)[] Searches(string title)
    {
        string q(string s) => "https://duckduckgo.com/?q=" + Uri.EscapeDataString(s);
        return new[]
        {
            ("Search the Web", q($"\"{title}\" dualshock button prompts mod")),
            ("Search Nexus Mods", q($"{title} dualshock OR ps4 OR playstation button prompts site:nexusmods.com")),
        };
    }

    public static string? GameFolder(Game g) =>
        g.InstallDir is string d && Directory.Exists(d) ? d :
        g.Kind == LaunchKind.Exe && g.Path != null ? Path.GetDirectoryName(g.Path) : null;

    static string GameDir(string gameId) => Path.Combine(AppPaths.Backups, "ControllerFixer", string.Concat(gameId.Split(Path.GetInvalidFileNameChars())));

    /// <summary>Unpacks the archive into a staging folder and returns the folder whose contents should be copied into the game.</summary>
    public static string Stage(string archive)
    {
        var stage = Path.Combine(AppPaths.Downloads, "ControllerFixer-staging");
        if (Directory.Exists(stage)) Directory.Delete(stage, true);
        Directory.CreateDirectory(stage);
        if (Path.GetExtension(archive).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            ZipFile.ExtractToDirectory(archive, stage);
        else
        {
            var seven = SevenZip() ?? throw new InvalidOperationException("Install 7-Zip to open .7z and .rar files, or download the .zip version of the mod.");
            using var p = Process.Start(new ProcessStartInfo(seven, $"x -y -o\"{stage}\" \"{archive}\"") { CreateNoWindow = true, UseShellExecute = false })!;
            p.WaitForExit();
            if (p.ExitCode != 0) throw new InvalidOperationException($"7-Zip couldn't open the file (code {p.ExitCode}).");
        }
        return stage;
    }

    /// <summary>Mods are often zipped inside one wrapper folder; strip it unless the game has a folder of that name.</summary>
    public static string ContentRoot(string stage, string target)
    {
        var entries = Directory.GetFileSystemEntries(stage);
        return entries.Length == 1 && Directory.Exists(entries[0]) && !Directory.Exists(Path.Combine(target, Path.GetFileName(entries[0])))
            ? entries[0] : stage;
    }

    public static string[] Files(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(root, f)).ToArray();

    /// <summary>Copies the mod into target. Files it overwrites are backed up first; if anything fails the game folder is restored.</summary>
    public static Install Apply(string gameId, string archive, string root, string target)
    {
        var when = DateTime.Now;
        var backup = Path.Combine(GameDir(gameId), when.ToString("yyyyMMdd-HHmmss"));
        var inst = new Install(gameId, target, archive, when, new(), new());
        try
        {
            foreach (var rel in Files(root))
            {
                var dst = Path.GetFullPath(Path.Combine(target, rel));
                if (!dst.StartsWith(Path.GetFullPath(target).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) continue; // zip-slip guard
                if (File.Exists(dst))
                {
                    var bak = Path.Combine(backup, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(bak)!);
                    File.Copy(dst, bak, true);
                    inst.Replaced.Add(rel);
                }
                else inst.Added.Add(rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(Path.Combine(root, rel), dst, true);
            }
            Directory.CreateDirectory(backup);
            File.WriteAllText(Path.Combine(backup, "install.json"), JsonSerializer.Serialize(inst));
            Log.Info($"controller fixer: installed {Path.GetFileName(archive)} into {target} ({inst.Added.Count} new, {inst.Replaced.Count} replaced)");
            return inst;
        }
        catch
        {
            Restore(inst, backup);
            throw;
        }
    }

    public static List<(Install inst, string dir)> History(string gameId)
    {
        var d = GameDir(gameId);
        if (!Directory.Exists(d)) return new();
        return Directory.GetDirectories(d).OrderByDescending(x => x)
            .Select(x => Path.Combine(x, "install.json")).Where(File.Exists)
            .Select(f => (JsonSerializer.Deserialize<Install>(File.ReadAllText(f))!, Path.GetDirectoryName(f)!)).ToList();
    }

    /// <summary>Removes the mod's new files and puts the original files back, then drops the backup.</summary>
    public static void Undo(Install inst, string backupDir)
    {
        Restore(inst, backupDir);
        Directory.Delete(backupDir, true);
        Log.Info($"controller fixer: undid {Path.GetFileName(inst.Archive)} in {inst.Target}");
    }

    static void Restore(Install inst, string backupDir)
    {
        foreach (var rel in inst.Added) { var f = Path.Combine(inst.Target, rel); if (File.Exists(f)) File.Delete(f); }
        foreach (var rel in inst.Replaced) File.Copy(Path.Combine(backupDir, rel), Path.Combine(inst.Target, rel), true);
    }

    static string? SevenZip() => new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe"),
    }.FirstOrDefault(File.Exists);
}
