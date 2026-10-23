using System.Diagnostics;
using Microsoft.Win32;
using OrbisUI.Core;

namespace OrbisUI.Library;

/// <summary>Reads the local Steam installation: install path, libraries, installed apps, non-Steam shortcuts and the logged-in user.</summary>
public static class Steam
{
    // Tools and runtimes that are not games.
    static readonly HashSet<string> ToolAppIds = new()
    {
        "228980", "993090", "1070560", "1391110", "1628350", "1493710", "1826330", "2180100", "250820", "1007", "1161040", "961940", "2348590", "431960",
    };

    public static string? FindInstall()
    {
        var configured = AppSettings.Current.SteamPath;
        if (!string.IsNullOrEmpty(configured) && File.Exists(Path.Combine(configured, "steam.exe"))) return configured;
        try
        {
            var p = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) return Path.GetFullPath(p);
        }
        catch { }
        var def = @"C:\Program Files (x86)\Steam";
        return Directory.Exists(def) ? def : null;
    }

    public static bool IsRunning() => Process.GetProcessesByName("steam").Length > 0;

    /// <summary>Starts Steam in the background (no window) when it is not already running.</summary>
    public static void EnsureRunningSilently()
    {
        var root = FindInstall();
        if (root == null || IsRunning()) return;
        try
        {
            Process.Start(new ProcessStartInfo(Path.Combine(root, "steam.exe"), "-silent") { UseShellExecute = false, WorkingDirectory = root });
            Log.Info("Started Steam silently");
        }
        catch (Exception ex) { Log.Warn("Could not start Steam: " + ex.Message); }
    }

    public static (string? id64, string? persona, string? account) CurrentUser(string steamRoot)
    {
        try
        {
            var f = Path.Combine(steamRoot, "config", "loginusers.vdf");
            if (!File.Exists(f)) return default;
            var users = Vdf.ParseText(File.ReadAllText(f)).Child("users");
            if (users == null) return default;
            Vdf.Node? pick = null; string? id = null;
            foreach (var (k, v) in users)
            {
                if (v is not Vdf.Node n) continue;
                if (pick == null || n.Str("MostRecent") == "1") { pick = n; id = k; }
            }
            return (id, pick?.Str("PersonaName"), pick?.Str("AccountName"));
        }
        catch (Exception ex) { Log.Warn("loginusers.vdf: " + ex.Message); return default; }
    }

    public static string? UserDataDir(string steamRoot, string? id64)
    {
        if (id64 != null && ulong.TryParse(id64, out var id))
        {
            var d = Path.Combine(steamRoot, "userdata", (id - 76561197960265728UL).ToString());
            if (Directory.Exists(d)) return d;
        }
        var ud = Path.Combine(steamRoot, "userdata");
        return Directory.Exists(ud) ? Directory.GetDirectories(ud).OrderByDescending(Directory.GetLastWriteTimeUtc).FirstOrDefault() : null;
    }

    public static List<string> LibraryFolders(string steamRoot)
    {
        var result = new List<string> { steamRoot };
        try
        {
            var f = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (File.Exists(f))
            {
                var root = Vdf.ParseText(File.ReadAllText(f)).Child("libraryfolders");
                foreach (var v in root?.Values ?? Enumerable.Empty<object>())
                    if (v is Vdf.Node n && n.Str("path") is string p && Directory.Exists(p)) result.Add(p);
            }
        }
        catch (Exception ex) { Log.Warn("libraryfolders.vdf: " + ex.Message); }
        return result.Select(p => Path.GetFullPath(p).TrimEnd('\\')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<Game> InstalledGames(string steamRoot)
    {
        var games = new List<Game>();
        foreach (var lib in LibraryFolders(steamRoot))
        {
            var apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var acf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                try
                {
                    var st = Vdf.ParseText(File.ReadAllText(acf)).Child("AppState");
                    if (st == null) continue;
                    var id = st.Str("appid");
                    var name = st.Str("name");
                    if (id == null || name == null || ToolAppIds.Contains(id)) continue;
                    if (name.StartsWith("Steamworks", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Proton", StringComparison.OrdinalIgnoreCase) || name.Contains("Steam Linux Runtime")) continue;
                    // StateFlags bit 4 = fully installed.
                    if (int.TryParse(st.Str("StateFlags"), out var flags) && (flags & 4) == 0) continue;
                    var dir = Path.Combine(apps, "common", st.Str("installdir") ?? "");
                    long.TryParse(st.Str("SizeOnDisk"), out var size);
                    var g = new Game
                    {
                        Id = "steam:" + id, Title = name, Platform = "PC", Kind = LaunchKind.Steam, SteamAppId = id,
                        InstallDir = dir, SizeBytes = size,
                    };
                    (g.LocalArtHint, g.LocalBgHint) = LocalArt(steamRoot, id);
                    games.Add(g);
                }
                catch (Exception ex) { Log.Warn($"{acf}: {ex.Message}"); }
            }
        }
        return games;
    }

    /// <summary>Steam's own cached library art: portrait capsule (for the square tile) and hero (for the background).</summary>
    public static (string? tile, string? bg) LocalArt(string steamRoot, string appId)
    {
        string? tile = null, bg = null;
        var custom = UserDataDir(steamRoot, AppSettings.Current.SteamId64);
        if (custom != null)
        {
            var grid = Path.Combine(custom, "config", "grid");
            tile = FirstExisting(grid, appId + "p.png", appId + "p.jpg");
            bg = FirstExisting(grid, appId + "_hero.png", appId + "_hero.jpg");
        }
        var cache = Path.Combine(steamRoot, "appcache", "librarycache");
        var dir = Path.Combine(cache, appId);
        if (Directory.Exists(dir))
        {
            var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
            tile ??= files.FirstOrDefault(f => Path.GetFileName(f).Equals("library_600x900.jpg", StringComparison.OrdinalIgnoreCase))
                     ?? files.FirstOrDefault(f => Path.GetFileName(f).Equals("header.jpg", StringComparison.OrdinalIgnoreCase));
            bg ??= files.FirstOrDefault(f => Path.GetFileName(f).Equals("library_hero.jpg", StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            tile ??= FirstExisting(cache, appId + "_library_600x900.jpg", appId + "_header.jpg");
            bg ??= FirstExisting(cache, appId + "_library_hero.jpg");
        }
        return (tile, bg);
    }

    static string? FirstExisting(string dir, params string[] names)
        => names.Select(n => Path.Combine(dir, n)).FirstOrDefault(File.Exists);

    public static List<Game> Shortcuts(string steamRoot)
    {
        var games = new List<Game>();
        var ud = UserDataDir(steamRoot, AppSettings.Current.SteamId64);
        var f = ud == null ? null : Path.Combine(ud, "config", "shortcuts.vdf");
        if (f == null || !File.Exists(f)) return games;
        try
        {
            var root = Vdf.ParseBinary(File.ReadAllBytes(f)).Child("shortcuts");
            foreach (var v in root?.Values ?? Enumerable.Empty<object>())
            {
                if (v is not Vdf.Node n) continue;
                var name = n.Str("AppName") ?? n.Str("appname");
                if (string.IsNullOrWhiteSpace(name)) continue;
                // Entries this launcher created are listed from their real source instead.
                if ((n.Child("tags")?.Values.OfType<string>() ?? Enumerable.Empty<string>()).Contains("OrbisUI")) continue;
                uint appid = n.TryGetValue("appid", out var a) && a is uint u ? u : 0;
                var exe = (n.Str("Exe") ?? n.Str("exe") ?? "").Trim('"');
                var g = new Game
                {
                    Id = "sc:" + appid, Title = name, Platform = "PC", Kind = LaunchKind.SteamShortcut,
                    SteamAppId = (((ulong)appid << 32) | 0x02000000UL).ToString(), Path = exe,
                    InstallDir = (n.Str("StartDir") ?? "").Trim('"'),
                };
                var grid = Path.Combine(ud!, "config", "grid");
                g.LocalArtHint = FirstExisting(grid, appid + "p.png", appid + "p.jpg", appid + ".png", appid + ".jpg");
                g.LocalBgHint = FirstExisting(grid, appid + "_hero.png", appid + "_hero.jpg");
                games.Add(g);
            }
        }
        catch (Exception ex) { Log.Warn("shortcuts.vdf: " + ex.Message); }
        return games;
    }

    public static void OpenUrl(string url)
    {
        try { Native.OpenShell(url); }
        catch (Exception ex) { Log.Warn($"steam url {url}: {ex.Message}"); }
    }

    /// <summary>Steam Big Picture handoffs (they bring Steam to the foreground).</summary>
    public static class Links
    {
        public const string BigPicture = "steam://open/bigpicture";
        public const string Store = "steam://store";
        public const string Friends = "steam://open/friends";
        public const string Chat = "steam://open/chat";
        public const string Profile = "steam://url/SteamIDMyProfile";
        public const string Achievements = "steam://open/achievements";
        public static string Run(string appId) => "steam://rungameid/" + appId;
        public static string Uninstall(string appId) => "steam://uninstall/" + appId;
        public static string StorePage(string appId) => "steam://store/" + appId;
    }
}
