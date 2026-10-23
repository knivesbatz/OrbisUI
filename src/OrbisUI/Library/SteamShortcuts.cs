using System.Diagnostics;
using System.Text;
using OrbisUI.Core;
using OrbisUI.Launch;
using OrbisUI.Metadata;

namespace OrbisUI.Library;

/// <summary>
/// Adds emulated and manually added PC games to Steam as non-Steam shortcuts (tagged "OrbisUI") so they launch from
/// Big Picture with the overlay. Steam only reads shortcuts.vdf at startup and rewrites it on exit, so writes happen
/// while Steam is closed. Existing shortcuts are never removed; ours are added once and repaired in place.
/// </summary>
public static class SteamShortcuts
{
    public const string Tag = "OrbisUI";
    const int KeepBackups = 5;

    /// <summary>Non-Steam shortcut id (top bit set), derived from our stable game id so a moved emulator or renamed game updates the same entry.</summary>
    public static uint AppId(Game g) => Crc32("OrbisUI:" + g.Id) | 0x80000000u;

    internal static uint Crc32(string s)
    {
        uint c = 0xFFFFFFFF;
        foreach (var b in Encoding.UTF8.GetBytes(s))
        {
            c ^= b;
            for (int k = 0; k < 8; k++) c = (c >> 1) ^ (0xEDB88320u & (uint)-(int)(c & 1));
        }
        return ~c;
    }

    public sealed record Entry(Game Game, string Name, string Exe, string Args, string? WorkDir);

    static string Q(string? p) => string.IsNullOrEmpty(p) ? "" : "\"" + p.Trim('"') + "\"";
    static string Key(string? exe, string? rest) => (exe ?? "").Trim('"').ToLowerInvariant() + "|" + (rest ?? "").Trim().ToLowerInvariant();

    /// <summary>Adds missing entries and repairs ours. Returns the games that were newly added and how many were repaired.</summary>
    public static (List<Game> added, int repaired) Merge(Vdf.Node root, IEnumerable<Entry> entries)
    {
        if (root.Child("shortcuts") is not Vdf.Node sc) root["shortcuts"] = sc = new Vdf.Node();
        var all = sc.Values.OfType<Vdf.Node>().ToList();
        var byId = new Dictionary<uint, Vdf.Node>();
        foreach (var n in all) if (n.TryGetValue("appid", out var a) && a is uint u) byId.TryAdd(u, n);
        // Shortcuts the user made by hand or with other tools: same program and same game, so don't add a second one.
        var known = new HashSet<string>();
        foreach (var n in all)
        {
            var exe = n.Str("Exe") ?? n.Str("exe");
            known.Add(Key(exe, n.Str("LaunchOptions")));
            known.Add(Key(exe, n.Str("AppName") ?? n.Str("appname")));
        }
        int next = sc.Keys.Select(k => int.TryParse(k, out var i) ? i + 1 : 0).DefaultIfEmpty(0).Max();

        var added = new List<Game>();
        int repaired = 0;
        foreach (var e in entries)
        {
            uint id = AppId(e.Game);
            var fields = new (string key, string value)[] { ("AppName", e.Name), ("Exe", Q(e.Exe)), ("StartDir", Q(e.WorkDir)), ("LaunchOptions", e.Args) };
            if (byId.TryGetValue(id, out var n))
            {
                bool changed = false;
                foreach (var (k, v) in fields) if (n.Str(k) != v) { n[k] = v; changed = true; }
                var tags = n.Child("tags") ?? (Vdf.Node)(n["tags"] = new Vdf.Node());
                if (!tags.Values.OfType<string>().Contains(Tag)) { tags[tags.Count.ToString()] = Tag; changed = true; }
                if (changed) repaired++;
                continue;
            }
            if (known.Contains(Key(e.Exe, e.Args)) || known.Contains(Key(e.Exe, e.Name))) continue;
            n = new Vdf.Node { ["appid"] = id };
            foreach (var (k, v) in fields) n[k] = v;
            n["icon"] = e.Game.Art != null && File.Exists(e.Game.Art) ? e.Game.Art : "";
            n["ShortcutPath"] = "";
            n["IsHidden"] = 0u;
            n["AllowDesktopConfig"] = 1u;
            n["AllowOverlay"] = 1u;
            n["OpenVR"] = 0u;
            n["Devkit"] = 0u;
            n["DevkitGameID"] = "";
            n["DevkitOverrideAppID"] = 0u;
            n["LastPlayTime"] = 0u;
            n["FlatpakAppID"] = "";
            n["tags"] = new Vdf.Node { ["0"] = Tag, ["1"] = e.Game.Platform == "VITA" ? "PS Vita" : e.Game.Platform };
            sc[(next++).ToString()] = n;
            byId[id] = n;
            known.Add(Key(e.Exe, e.Args));
            added.Add(e.Game);
        }
        return (added, repaired);
    }

    static List<Entry> Entries(IEnumerable<Game> games)
    {
        var list = new List<Entry>();
        foreach (var g in games.Where(g => g.Kind is LaunchKind.Exe or LaunchKind.Emulator))
        {
            try
            {
                var (exe, args, wd) = LaunchService.BuildCommand(g);
                if (File.Exists(exe)) list.Add(new Entry(g, LibraryService.DisplayTitle(g), exe, args, wd));
            }
            catch (LaunchException) { /* emulator not set up yet */ }
        }
        return list;
    }

    /// <summary>Writes shortcuts.vdf (backup first, atomic swap) and downloads Steam library art for new entries. Steam must be closed.</summary>
    static async Task<string> SyncAsync(string steamRoot, List<Game> games)
    {
        var ud = Steam.UserDataDir(steamRoot, AppSettings.Current.SteamId64);
        if (ud == null) return "Sign in to Steam once first.";
        var cfg = Path.Combine(ud, "config");
        Directory.CreateDirectory(cfg);
        var file = Path.Combine(cfg, "shortcuts.vdf");
        var old = File.Exists(file) ? File.ReadAllBytes(file) : null;
        // An unreadable file throws here, before anything is written.
        var root = old == null ? new Vdf.Node() : Vdf.ParseBinary(old);
        int before = root.Child("shortcuts")?.Count ?? 0;
        var (added, repaired) = Merge(root, Entries(games));
        if (added.Count + repaired > 0)
        {
            var bytes = Vdf.WriteBinary(root);
            if ((Vdf.ParseBinary(bytes).Child("shortcuts")?.Count ?? 0) != before + added.Count)
                throw new InvalidDataException("shortcuts.vdf check failed; nothing was written.");
            if (old != null)
            {
                File.WriteAllBytes(file + $".orbisui-{DateTime.Now:yyyyMMdd-HHmmss}.bak", old);
                foreach (var b in Directory.GetFiles(cfg, "shortcuts.vdf.orbisui-*.bak").OrderDescending().Skip(KeepBackups)) File.Delete(b);
            }
            AtomicFile.WriteAllBytes(file, bytes);
            Log.Info($"Steam shortcuts: {added.Count} added, {repaired} repaired");
        }
        var grid = Path.Combine(cfg, "grid");
        foreach (var g in added)
            try { await MetadataService.SteamLibraryArtAsync(g, LibraryService.DisplayTitle(g), grid, AppId(g), CancellationToken.None); }
            catch (Exception ex) { Log.Warn($"Steam art {g.Title}: {ex.Message}"); }
        return added.Count + repaired == 0 ? "Steam already has all your games." : $"Added {added.Count} games to Steam" + (repaired > 0 ? $", updated {repaired}." : ".");
    }

    /// <summary>Startup path: only touches Steam's files when Steam isn't running.</summary>
    public static async Task SyncIfClosedAsync(List<Game> games)
    {
        var root = Steam.FindInstall();
        if (root == null || Steam.IsRunning()) return;
        try { await SyncAsync(root, games); }
        catch (Exception ex) { Log.Warn("Steam shortcuts: " + ex.Message); }
    }

    /// <summary>Settings path: closes Steam if needed, writes, then starts Steam again.</summary>
    public static async Task<string> SyncNowAsync(List<Game> games)
    {
        var root = Steam.FindInstall();
        if (root == null) return "Steam isn't installed.";
        try
        {
            return await Task.Run(async () =>
            {
                bool wasRunning = Steam.IsRunning();
                if (wasRunning)
                {
                    Process.Start(new ProcessStartInfo(Path.Combine(root, "steam.exe"), "-shutdown") { UseShellExecute = false, WorkingDirectory = root })?.Dispose();
                    for (int i = 0; i < 60 && Steam.IsRunning(); i++) await Task.Delay(500);
                    if (Steam.IsRunning()) return "Steam didn't close, so nothing was changed. Close Steam and try again.";
                }
                var msg = await SyncAsync(root, games);
                if (wasRunning) Steam.EnsureRunningSilently();
                return msg;
            });
        }
        catch (Exception ex) { Log.Warn("Steam shortcuts: " + ex.Message); return "Couldn't update Steam: " + ex.Message; }
    }
}
