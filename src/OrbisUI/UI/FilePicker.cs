using OrbisUI.Input;

namespace OrbisUI.UI;

/// <summary>Controller-friendly file/folder chooser (drives, folders, matching files).</summary>
public static class FilePicker
{
    static string? _lastDir;

    /// <param name="extensions">File types to show; null picks a folder instead.</param>
    public static void Pick(string title, string[]? extensions, Action<string> chosen, string? startDir = null)
    {
        var dir = startDir ?? _lastDir;
        Shell.Push(Build(title, extensions, chosen, dir != null && Directory.Exists(dir) ? dir : null));
    }

    static ListScreen Build(string title, string[]? exts, Action<string> chosen, string? dir)
    {
        var items = new List<ListItem>();
        void Go(string? d) { Shell.Pop(); Shell.Push(Build(title, exts, chosen, d)); }

        if (dir == null)
        {
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady))
                items.Add(new ListItem { Glyph = "", Label = $"{d.Name.TrimEnd('\\')}  {d.VolumeLabel}", Value = () => $"{d.AvailableFreeSpace / (1L << 30)} GB free", Activate = () => Go(d.RootDirectory.FullName) });
            foreach (var (name, path) in new[] { ("Downloads", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")), ("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.Desktop)), ("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)) })
                if (Directory.Exists(path)) items.Add(new ListItem { Glyph = "", Label = name, Activate = () => Go(path) });
        }
        else
        {
            _lastDir = dir;
            var parent = Directory.GetParent(dir)?.FullName;
            items.Add(new ListItem { Glyph = "", Label = "..", Sub = () => parent ?? "Drives", Activate = () => Go(parent) });
            if (exts == null)
                items.Add(new ListItem { Glyph = "", Label = "Use This Folder", Sub = () => dir, Activate = () => { Shell.Pop(); chosen(dir); } });
            try
            {
                foreach (var d in Directory.EnumerateDirectories(dir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    var attr = File.GetAttributes(d);
                    if ((attr & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    items.Add(new ListItem { Glyph = "", Label = Path.GetFileName(d), Activate = () => Go(d) });
                }
                if (exts != null)
                    foreach (var f in Directory.EnumerateFiles(dir).Where(f => exts.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                        items.Add(new ListItem { Glyph = "", Label = Path.GetFileName(f), Activate = () => { Shell.Pop(); chosen(f); } });
            }
            catch (Exception ex) { items.Add(new ListItem { Glyph = "", Label = "This folder can't be opened", Sub = () => ex.Message }); }
        }
        return new ListScreen(title, items, subtitle: dir ?? "Choose a drive", hints: new[] { (Btn.Cross, "Open"), (Btn.Circle, "Back") });
    }
}
