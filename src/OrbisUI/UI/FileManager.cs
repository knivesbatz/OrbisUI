using System.Diagnostics;
using Microsoft.VisualBasic.FileIO;
using OrbisUI.Core;
using OrbisUI.Input;
using OrbisUI.Services;

namespace OrbisUI.UI;

/// <summary>
/// Controller file manager: drives and folders, Cross opens (a .pkg installs for shadPS4), OPTIONS for copy, move, paste,
/// rename, new folder and delete. Delete goes to the Recycle Bin, never a permanent delete.
/// </summary>
public static class FileManager
{
    static string? _clip;
    static bool _cut;
    static string? _dir;

    public static void Open(string? dir = null) => Shell.Push(Build(dir ?? _dir));

    /// <summary>Rebuild the top file manager page in place (after a change or a finished copy).</summary>
    static void Go(string? dir, string? focus = null)
    {
        if (Shell.Top is ListScreen { Tag: "files" }) Shell.Pop();
        Shell.Push(Build(dir, focus));
    }

    static string Size(long b) => b >= 1L << 30 ? $"{b / (double)(1L << 30):0.0} GB" : b >= 1 << 20 ? $"{b / (double)(1 << 20):0.0} MB" : $"{Math.Max(1, b / 1024)} KB";

    static ListScreen Build(string? dir, string? focus = null)
    {
        if (dir != null && !Directory.Exists(dir)) dir = null;
        _dir = dir;
        var items = new List<ListItem>();
        var paths = new Dictionary<ListItem, string>();
        if (dir == null)
        {
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady))
                items.Add(new ListItem { Glyph = "", Label = $"{d.Name.TrimEnd('\\')}  {d.VolumeLabel}", Value = () => $"{Size(d.AvailableFreeSpace)} free of {Size(d.TotalSize)}", Activate = () => Go(d.RootDirectory.FullName) });
            foreach (var (name, path) in new[] { ("Downloads", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")), ("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.Desktop)), ("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)), ("OrbisUI", AppPaths.Root) })
                if (Directory.Exists(path)) items.Add(new ListItem { Glyph = "", Label = name, Sub = () => path, Activate = () => Go(path) });
        }
        else
        {
            var parent = Directory.GetParent(dir)?.FullName;
            items.Add(new ListItem { Glyph = "", Label = "..", Sub = () => parent ?? "Drives", Activate = () => Go(parent, dir) });
            try
            {
                foreach (var d in Directory.EnumerateDirectories(dir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    if ((File.GetAttributes(d) & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    var it = new ListItem { Glyph = "", Label = Path.GetFileName(d), Activate = () => Go(d) };
                    items.Add(it); paths[it] = d;
                }
                foreach (var f in Directory.EnumerateFiles(dir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    var fi = new FileInfo(f);
                    if ((fi.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    var pkg = fi.Extension.Equals(".pkg", StringComparison.OrdinalIgnoreCase);
                    var it = new ListItem
                    {
                        Glyph = pkg ? "" : "", Label = fi.Name, Value = () => $"{Size(fi.Length)}   {fi.LastWriteTime:yyyy-MM-dd}",
                        Sub = pkg ? () => "PS4 package: press Cross to install for shadPS4" : null,
                        Activate = () => { if (pkg) AskInstall(f); else OpenFile(f); },
                    };
                    items.Add(it); paths[it] = f;
                }
            }
            catch (Exception ex) { items.Add(new ListItem { Glyph = "", Label = "This folder can't be opened", Sub = () => ex.Message }); }
        }
        var sel = focus == null ? 0 : Math.Max(0, items.FindIndex(i => paths.TryGetValue(i, out var p) && p.Equals(focus, StringComparison.OrdinalIgnoreCase)));
        var clip = _clip == null ? "" : $"   ·   {(_cut ? "Moving" : "Copying")}: {Path.GetFileName(_clip)}";
        var screen = new ListScreen("File Manager", items, subtitle: (dir ?? "Choose a drive") + clip, select: sel,
            hints: new[] { (Btn.Cross, "Open"), (Btn.Options, "Options"), (Btn.Circle, "Back") }) { Tag = "files" };
        screen.OnOptions = it => { if (dir != null) Options(dir, paths.GetValueOrDefault(it)); };
        return screen;
    }

    static void OpenFile(string f)
    {
        try { Process.Start(new ProcessStartInfo(f) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(f)! }); }
        catch (Exception ex) { Shell.Message("Can't Open", ex.Message); }
    }

    static void Options(string dir, string? path)
    {
        var items = new List<ListItem>();
        void Close() => Shell.Pop();
        if (path != null)
        {
            var name = Path.GetFileName(path);
            if (path.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase))
                items.Add(new ListItem { Glyph = "", Label = "Install for shadPS4", Activate = () => { Close(); AskInstall(path); } });
            items.Add(new ListItem { Glyph = "", Label = "Copy", Activate = () => { Close(); _clip = path; _cut = false; Go(dir, path); } });
            items.Add(new ListItem { Glyph = "", Label = "Move", Activate = () => { Close(); _clip = path; _cut = true; Go(dir, path); } });
            items.Add(new ListItem { Glyph = "", Label = "Rename", Activate = () => { Close(); Osk.Ask("Rename", name, n => Guard("Rename", () => Rename(path, n), dir)); } });
            items.Add(new ListItem { Glyph = "", Label = "Delete", Sub = () => "Moves it to the Recycle Bin", Activate = () => { Close(); Shell.Confirm("Delete", $"Move \"{name}\" to the Recycle Bin?", () => Guard("Delete", () => Recycle(path), dir)); } });
        }
        if (_clip != null)
            items.Add(new ListItem { Glyph = "", Label = (_cut ? "Move" : "Paste") + " Here", Sub = () => _clip, Activate = () => { Close(); Paste(dir); } });
        items.Add(new ListItem { Glyph = "", Label = "New Folder", Activate = () => { Close(); Osk.Ask("New Folder", "New folder", n => Guard("New Folder", () => Directory.CreateDirectory(Path.Combine(dir, Clean(n))), dir)); } });
        Shell.Push(new ListScreen(path == null ? Path.GetFileName(dir) : Path.GetFileName(path), items, dialog: true));
    }

    static string Clean(string n)
    {
        n = n.Trim();
        if (n.Length == 0 || n is "." or ".." || n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException("That name can't be used for a file or folder.");
        return n;
    }

    static void Rename(string path, string n)
    {
        var dest = Path.Combine(Path.GetDirectoryName(path)!, Clean(n));
        if (Directory.Exists(path)) Directory.Move(path, dest); else File.Move(path, dest);
    }

    static void Recycle(string path)
    {
        if (Directory.Exists(path)) FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        else FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
    }

    static void Guard(string what, Action a, string dir)
    {
        try { a(); }
        catch (Exception ex) { Shell.Message(what, ex.Message); return; }
        Go(dir);
    }

    /// <summary>Copy or move the clipboard item here on a background thread; existing files are never overwritten.</summary>
    static void Paste(string dir)
    {
        var src = _clip!; var cut = _cut;
        var dest = Path.Combine(dir, Path.GetFileName(src));
        if (dest.Equals(src, StringComparison.OrdinalIgnoreCase) || File.Exists(dest) || Directory.Exists(dest)) { Shell.Message("Paste", "Something with that name is already here."); return; }
        if (Directory.Exists(src) && (dir + "\\").StartsWith(src.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) { Shell.Message("Paste", "A folder can't be put inside itself."); return; }
        _clip = null;
        Shell.Toast(cut ? "Moving" : "Copying", Path.GetFileName(src), "");
        Task.Run(() =>
        {
            try
            {
                if (Directory.Exists(src)) { if (cut) FileSystem.MoveDirectory(src, dest); else FileSystem.CopyDirectory(src, dest); }
                else { if (cut) File.Move(src, dest); else File.Copy(src, dest); }
                Shell.Window.Dispatcher.Invoke(() => { Shell.Toast(cut ? "Moved" : "Copied", Path.GetFileName(dest), ""); if (_dir == dir && Shell.Top is ListScreen { Tag: "files" }) Go(dir, dest); });
            }
            catch (Exception ex)
            {
                Log.Warn($"Paste {src} -> {dest}: {ex.Message}");
                Shell.Window.Dispatcher.Invoke(() => Shell.Message(cut ? "Move" : "Copy", ex.Message));
            }
        });
    }

    public static void AskInstall(string pkg)
    {
        if (PkgInstaller.FindExtractor() == null)
        {
            Shell.Message("Install", "pkg_extractor.exe (ShadPs4Plus PKG Extractor) wasn't found. Put it in OrbisUI's Tools folder or next to shadPS4.");
            return;
        }
        Shell.Confirm("Install", $"Install \"{Path.GetFileName(pkg)}\" for shadPS4?\nGames and updates go to {PkgInstaller.GamesDir()}, DLC to {PkgInstaller.AddonsDir()}. The .pkg file is kept.", () => Install(pkg), "Install", "Cancel");
    }

    static void Install(string pkg)
    {
        Shell.Toast("Installing", Path.GetFileName(pkg), "");
        Task.Run(() =>
        {
            try
            {
                var (kind, dest) = PkgInstaller.Install(pkg);
                Shell.Window.Dispatcher.Invoke(async () =>
                {
                    Shell.Toast(kind switch { PkgInstaller.Kind.Update => "Update installed", PkgInstaller.Kind.Dlc => "Add-on installed", _ => "Game installed" }, Path.GetFileName(dest), "");
                    await Shell.Library.RescanAsync();
                });
            }
            catch (Exception ex)
            {
                Log.Warn($"Install {pkg}: {ex.Message}");
                Shell.Window.Dispatcher.Invoke(() => Shell.Message("Install Failed", ex.Message));
            }
        });
    }
}
