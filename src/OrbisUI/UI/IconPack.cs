using System.Windows.Media;
using OrbisUI.Core;

namespace OrbisUI.UI;

/// <summary>
/// Console-style icons. They are user-supplied (like the sound pack) and copied into Data\Icons, so no proprietary art
/// lives in the public repository; built-in vector glyphs are used when a file is missing.
/// </summary>
public static class IconPack
{
    // Key -> file name in the icon pack.
    static readonly Dictionary<string, string> Files = new()
    {
        ["store"] = "StoreNew.png", ["notifications"] = "quickmenu_notifications.png", ["friends"] = "quickmenu_friends.png",
        ["messages"] = "messages_big_selected.png", ["party"] = "party_big_selected.png", ["trophies"] = "trophy_big_selected.png",
        ["settings"] = "Setting.png", ["power"] = "power_big_selected.png", ["tv"] = "TVServicesNew.png", ["library"] = "LibraryNew.png",
        ["browser"] = "BrowserNew.png", ["files"] = "FileManager.png", ["video"] = "Video.png", ["music"] = "Music.png",
        ["controller"] = "quickmenu_controller.png", ["update"] = "Update.png", ["network"] = "Network.png", ["sound"] = "Sound.png",
        ["bluetooth"] = "Bluetooth.png", ["storage"] = "Storage.png", ["lock"] = "Lock.png", ["plus"] = "psplus.png",
        // Home tiles the user supplied (Data\Icons\store.png and "whats new.png").
        ["storetile"] = "store.png", ["whatsnew"] = "whats new.png", ["browsertile"] = "web browser.png", ["filestile"] = "file manager.png",
    };

    static string Dir => Path.Combine(AppPaths.Root, "Data", "Icons");
    static readonly Dictionary<string, ImageSource?> Cache = new();

    public static ImageSource? Get(string key, int decode = 128)
    {
        if (Cache.TryGetValue(key, out var img)) return img;
        img = Files.TryGetValue(key, out var f) ? Metadata.ImageTools.LoadForDisplay(Path.Combine(Dir, f), decode) : null;
        return Cache[key] = img;
    }

    public static string? AutoFind()
    {
        var parent = Directory.GetParent(AppPaths.Root)?.FullName ?? AppPaths.Root;
        var candidates = new[]
        {
            Path.Combine(AppPaths.Root, "Icons"), Path.Combine(parent, "Icons"),
            @"C:\Sony\OrbisUI\source code refrences\OrbisPro-main\OrbisPro\Icons",
        };
        return candidates.FirstOrDefault(d => Directory.Exists(d) && File.Exists(Path.Combine(d, "StoreNew.png")));
    }

    /// <summary>Copy the icon pack in once (startup); returns the number of files copied.</summary>
    public static int EnsureImported()
    {
        if (Directory.Exists(Dir) && Directory.EnumerateFiles(Dir, "*.png").Any()) return 0;
        var src = AutoFind();
        if (src == null) return 0;
        Directory.CreateDirectory(Dir);
        int n = 0;
        foreach (var f in Files.Values.Distinct())
        {
            var s = Path.Combine(src, f);
            if (File.Exists(s)) { File.Copy(s, Path.Combine(Dir, f), true); n++; }
        }
        Log.Info($"Imported {n} icons from {src}");
        Cache.Clear();
        return n;
    }
}
