using System.Diagnostics;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using OrbisUI.Core;
using OrbisUI.Library;

namespace OrbisUI.Services;

/// <summary>
/// Uninstall or delete a game: Steam and Store apps use their own uninstallers, PC games use the one Windows has registered
/// for their folder, everything else (ROMs, PS3/PS4/Vita game folders, a PC game with no uninstaller) goes to the Recycle Bin.
/// </summary>
public static class Uninstaller
{
    public enum Kind { Steam, Store, Registered, Recycle, None }

    public static (Kind kind, string detail, List<string> paths) Plan(Game g)
    {
        if (g.Kind == LaunchKind.Steam && g.SteamAppId != null) return (Kind.Steam, "Steam will uninstall it.", new());
        if (g.Path?.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase) == true) return (Kind.Store, "Windows will uninstall the app.", new());
        if (g.Kind == LaunchKind.Exe && g.InstallDir != null && RegisteredUninstaller(g.InstallDir) != null)
            return (Kind.Registered, "The game's own uninstaller will run.", new());
        var paths = RecyclePaths(g);
        if (paths.Count == 0) return (Kind.None, "", paths);
        var where = paths.Count == 1 ? paths[0] : $"{paths.Count} files in {Path.GetDirectoryName(paths[0])}";
        return (Kind.Recycle, $"Moves {where} to the Recycle Bin.", paths);
    }

    public static void Run(Game g, (Kind kind, string detail, List<string> paths) plan)
    {
        Log.Info($"Uninstall {g.Id} ({plan.kind}): {string.Join(", ", plan.paths)}");
        switch (plan.kind)
        {
            case Kind.Steam: Steam.OpenUrl(Steam.Links.Uninstall(g.SteamAppId!)); break;
            case Kind.Store:
                var pfn = g.Path!.Split('\\', 2)[1].Split('!')[0];
                Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -Command \"Get-AppxPackage | ? PackageFamilyName -eq '{pfn.Replace("'", "")}' | Remove-AppxPackage\"")
                    { UseShellExecute = false, CreateNoWindow = true })?.WaitForExit(120_000);
                break;
            case Kind.Registered:
                // "start" goes through ShellExecute, so uninstallers that need admin rights get their UAC prompt.
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c start \"\" {RegisteredUninstaller(g.InstallDir!)}") { UseShellExecute = false, CreateNoWindow = true });
                break;
            case Kind.Recycle:
                foreach (var p in plan.paths)
                {
                    if (Directory.Exists(p)) FileSystem.DeleteDirectory(p, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    else if (File.Exists(p)) FileSystem.DeleteFile(p, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                }
                break;
        }
    }

    /// <summary>What goes to the Recycle Bin: the game folder, or the ROM with its tracks (Game (Track 1).bin next to Game.cue) and other discs.</summary>
    internal static List<string> RecyclePaths(Game g)
    {
        var list = new List<string>();
        if (g.Kind == LaunchKind.Emulator && g.InstallDir != null && Directory.Exists(g.InstallDir)) list.Add(g.InstallDir);
        else if (g.Kind == LaunchKind.Emulator)
        {
            foreach (var rom in new[] { g.RomFile ?? g.Path }.Concat(g.Discs ?? new()).OfType<string>()
                         .Where(f => File.Exists(f) && !f.StartsWith(AppPaths.Cache, StringComparison.OrdinalIgnoreCase)))
            {
                var stem = Path.GetFileNameWithoutExtension(rom);
                list.AddRange(Directory.EnumerateFiles(Path.GetDirectoryName(rom)!).Where(f => Path.GetFileName(f).StartsWith(stem, StringComparison.OrdinalIgnoreCase)));
            }
        }
        else if (g.Kind == LaunchKind.Exe && g.InstallDir != null && Directory.Exists(g.InstallDir)) list.Add(g.InstallDir);
        else if (g.Kind == LaunchKind.Url && g.Path != null && File.Exists(g.Path)) list.Add(g.Path); // the .lnk / .url itself
        return list.Distinct(StringComparer.OrdinalIgnoreCase).Where(p => !Protected(p)).ToList();
    }

    /// <summary>Never recycle a drive root, Windows, Program Files, the user folder or anything above them.</summary>
    internal static bool Protected(string path)
    {
        var p = Path.GetFullPath(path).TrimEnd('\\');
        if (Path.GetPathRoot(p)?.TrimEnd('\\').Equals(p, StringComparison.OrdinalIgnoreCase) == true) return true;
        var special = new[]
        {
            Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.Desktop,
            Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData,
        }.Select(f => Environment.GetFolderPath(f).TrimEnd('\\')).Where(s => s.Length > 0).Append(AppPaths.Root.TrimEnd('\\'));
        return special.Any(s => s.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    static string? RegisteredUninstaller(string installDir)
    {
        var dir = installDir.TrimEnd('\\');
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        foreach (var root in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
        {
            using var k = hive.OpenSubKey(root);
            if (k == null) continue;
            foreach (var name in k.GetSubKeyNames())
            {
                using var e = k.OpenSubKey(name);
                var loc = (e?.GetValue("InstallLocation") as string)?.Trim('"', ' ').TrimEnd('\\');
                if (loc?.Equals(dir, StringComparison.OrdinalIgnoreCase) == true && e!.GetValue("UninstallString") is string u && u.Length > 0) return u;
            }
        }
        return null;
    }
}
