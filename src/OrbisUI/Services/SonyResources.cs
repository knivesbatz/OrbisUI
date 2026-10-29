using System.Diagnostics;
using OrbisUI.Core;
using OrbisUI.Library;

namespace OrbisUI.Services;

/// <summary>
/// SONYRESOURCES is the user's backup of everything the GitHub download doesn't ship (emulators, firmware, BIOS, their
/// sound, icon and video packs). It mirrors the OrbisUI folder. Dropped inside the OrbisUI folder its files are moved
/// into place; found beside it (e.g. C:\Sony\SONYRESOURCES next to C:\Sony\OrbisUI) they are copied once and the backup
/// stays. Existing files always win; anything left inside is renamed "SONYRESOURCES (imported)", and the PS3 and Vita firmware are installed afterwards.
/// </summary>
public static class SonyResources
{
    public const string FolderName = "SONYRESOURCES";
    static string Marker => Path.Combine(AppPaths.Config, "sonyresources-imported.txt");

    /// <summary>At start, before sounds and icons load. Data (packs, media) goes first and at once; the emulators follow in the background.</summary>
    public static void Import()
    {
        var inside = Path.Combine(AppPaths.Root, FolderName);
        bool move = Directory.Exists(inside);
        if (!move && File.Exists(Marker)) return;
        // Searching elsewhere is only for a fresh install (no settings yet, or an import still staged): an install already
        // in use keeps its own emulators unless the backup is dropped inside it.
        if (!move && File.Exists(Path.Combine(AppPaths.Config, "settings.json")) && !Directory.Exists(Path.Combine(AppPaths.Root, ".sonyimport"))) return;
        // Not inside: beside the OrbisUI folder, else X:\SONYRESOURCES or X:\Sony\SONYRESOURCES on any fixed drive.
        var src = move ? inside : new[] { Path.Combine(Path.GetDirectoryName(AppPaths.Root.TrimEnd('\\')) ?? "", FolderName) }
            .Concat(DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .SelectMany(d => new[] { Path.Combine(d.Name, FolderName), Path.Combine(d.Name, "Sony", FolderName) }))
            .FirstOrDefault(Directory.Exists);
        if (src == null) return;
        Transfer(src, AppPaths.Root, move, rel => rel.StartsWith("Data\\", StringComparison.OrdinalIgnoreCase));
        _ = Task.Run(() =>
        {
            // Startup detects emulators and writes starter configs while this runs, so a half-copied emulator must never be
            // visible: copies go to a staging folder first, then whole folders are moved into place (a rename, atomic).
            var from = src;
            if (!move)
            {
                from = Path.Combine(AppPaths.Root, ".sonyimport");
                Transfer(src, from, false, rel => !rel.StartsWith("Extras\\", StringComparison.OrdinalIgnoreCase) && !rel.StartsWith("Data\\", StringComparison.OrdinalIgnoreCase));
            }
            int n = Merge(from, AppPaths.Root, top: true);
            Log.Info($"SONYRESOURCES: {n} files moved into place");
            // What stays behind (Extras, files the install already had) is kept, renamed so it isn't imported again.
            try { if (move) Directory.Move(src, src + " (imported)"); else Directory.Delete(from, true); } catch (Exception ex) { Log.Warn("SONYRESOURCES cleanup: " + ex.Message); }
            if (!move) File.WriteAllText(Marker, $"Copied from {src} on {DateTime.Now:u}");
            UI.Shell.Window?.Dispatcher.Invoke(() =>
            {
                if (n > 0) Notifications.Post("Sony resources imported", $"{n} files from {FolderName} were added.", "");
                UI.Shell.Window.Rescan();
            });
            InstallFirmware();
        });
    }

    /// <summary>Moves src\* under dst: a folder dst lacks moves as a whole, otherwise its files are merged one by one.
    /// Existing files are kept. Returns how many files were added.</summary>
    static int Merge(string src, string dst, bool top)
    {
        int added = 0;
        foreach (var d in Directory.EnumerateDirectories(src))
        {
            var name = Path.GetFileName(d);
            if (top && name.Equals("Extras", StringComparison.OrdinalIgnoreCase)) continue;
            var target = Path.Combine(dst, name);
            try
            {
                if (Directory.Exists(target)) { added += Merge(d, target, false); continue; }
                int files = Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories).Count();
                Directory.Move(d, target);
                added += files;
            }
            catch (Exception ex) { Log.Warn($"SONYRESOURCES {target}: {ex.Message}"); }
        }
        foreach (var f in Directory.EnumerateFiles(src))
        {
            var target = Path.Combine(dst, Path.GetFileName(f));
            if ((top && Path.GetFileName(f).Equals("README.txt", StringComparison.OrdinalIgnoreCase)) || File.Exists(target)) continue;
            try { File.Move(f, target); added++; } catch (Exception ex) { Log.Warn($"SONYRESOURCES {target}: {ex.Message}"); }
        }
        return added;
    }

    /// <summary>Moves or copies src\* into dstRoot, skipping files that already exist. Returns how many were added.</summary>
    static int Transfer(string src, string dstRoot, bool move, Func<string, bool> pick)
    {
        int added = 0;
        foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, f);
            if (rel.Equals("README.txt", StringComparison.OrdinalIgnoreCase) || !pick(rel)) continue;
            var dest = Path.Combine(dstRoot, rel);
            try
            {
                if (File.Exists(dest)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                if (move) File.Move(f, dest); else File.Copy(f, dest);
                added++;
            }
            catch (Exception ex) { Log.Warn($"SONYRESOURCES {rel}: {ex.Message}"); }
        }
        Log.Info($"SONYRESOURCES: {added} files {(move ? "moved" : "copied")} from {src}");
        return added;
    }
    /// <summary>Install PS3 and Vita firmware from Firmware\PS3 and Firmware\VITA when the emulator doesn't have it yet.</summary>
    public static void InstallFirmware()
    {
        // Startup may have picked another install while the copy ran; the imported emulators win for their systems.
        foreach (var def in Emulators.All)
            if (Directory.Exists(Path.Combine(AppPaths.DefaultEmulators, def.System)) && Emulators.ExePath(def.System) is string cur
                && !cur.StartsWith(AppPaths.Root, StringComparison.OrdinalIgnoreCase))
                AppSettings.Current.Emulators.Remove(def.System);
        if (Emulators.AutoDetect().Count > 0) AppSettings.Current.Save();
        var fw = AppPaths.DefaultFirmware;
        var ps3Pup = Path.Combine(fw, "PS3", "PS3UPDAT.PUP");
        // --headless skips RPCS3's "Install firmware?" prompt and its window. RPCS3 then fails fast (0xC0000409) while exiting
        // even after a good install, so success is judged by the installed files, not the exit code.
        if (Emulators.ExePath("PS3") is string rpcs3 && File.Exists(ps3Pup))
        {
            var vsh = Path.Combine(Path.GetDirectoryName(rpcs3)!, "dev_flash", "vsh", "module", "vsh.self");
            if (!File.Exists(vsh)) Run("PS3 firmware", rpcs3, $"--headless --installfw \"{ps3Pup}\"", () => File.Exists(vsh));
        }

        if (Emulators.ExePath("VITA") is string vita3k)
        {
            var sys = Path.Combine(Emulators.Vita3kPrefPath(vita3k), "vs0", "sys");
            if (!Directory.Exists(sys))
                foreach (var pup in new[] { "PSVUPDAT.PUP", "PSP2UPDAT.PUP" }.Select(n => Path.Combine(fw, "VITA", n)).Where(File.Exists))
                    Run("Vita firmware", vita3k, $"--firmware \"{pup}\"", () => Directory.Exists(sys));
        }
    }

    static void Run(string what, string exe, string args, Func<bool> installed)
    {
        try
        {
            Log.Info($"Installing {what}: \"{exe}\" {args}");
            using var p = Process.Start(new ProcessStartInfo(exe, args) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false, CreateNoWindow = true })!;
            if (!p.WaitForExit(TimeSpan.FromMinutes(10))) { p.Kill(true); Log.Warn($"{what} install timed out"); return; }
            if (!installed()) { Log.Warn($"{what} install failed (exit 0x{p.ExitCode:X})"); return; }
            Log.Info($"{what} installed");
            UI.Shell.Window?.Dispatcher.Invoke(() => Notifications.Post($"{what} installed", Path.GetFileName(exe), ""));
        }
        catch (Exception ex) { Log.Warn($"{what} install: {ex.Message}"); }
    }
}
