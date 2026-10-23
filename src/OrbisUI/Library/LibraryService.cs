using System.Text.Json;
using OrbisUI.Core;

namespace OrbisUI.Library;

/// <summary>The unified library: Steam, Steam shortcuts, emulated games and manually added PC games, cached in Config/library.json.</summary>
public sealed class LibraryService
{
    public List<Game> Games { get; private set; } = new();
    public event Action? Changed;

    static string CachePath => Path.Combine(AppPaths.Config, "library.json");
    static string ManualPath => Path.Combine(AppPaths.Config, "manual-games.json");

    /// <summary>Instant start: show what we knew last time, then rescan in the background.</summary>
    public void LoadCached()
    {
        try
        {
            if (File.Exists(CachePath))
                Games = JsonSerializer.Deserialize<List<Game>>(File.ReadAllText(CachePath), AppSettings.JsonOpts) ?? new();
        }
        catch (Exception ex) { Log.Warn("library.json: " + ex.Message); Games = new(); }
        Sort();
    }

    public async Task RescanAsync(CancellationToken ct = default)
    {
        var started = DateTime.Now;
        var found = await Task.Run(() => Discover(ct), ct);
        // Keep metadata and art we already fetched for games that still exist.
        var old = Games.ToDictionary(g => g.Id);
        foreach (var g in found)
        {
            if (!old.TryGetValue(g.Id, out var prev)) continue;
            g.Description = prev.Description; g.Publisher = prev.Publisher; g.Year = prev.Year; g.Genres = prev.Genres;
            g.MetaSource = prev.MetaSource; g.MetaTried = prev.MetaTried; g.SgdbTried = prev.SgdbTried; g.BgSource = prev.BgSource; g.ArtSource = prev.ArtSource;
            g.Art = prev.Art != null && File.Exists(prev.Art) ? prev.Art : null;
            g.Background = prev.Background != null && File.Exists(prev.Background) ? prev.Background : null;
        }
        Games = found;
        Sort();
        Save();
        Log.Info($"Library scan: {Games.Count} games in {(DateTime.Now - started).TotalMilliseconds:0} ms");
        Changed?.Invoke();
    }

    static List<Game> Discover(CancellationToken ct)
    {
        var list = new List<Game>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var steam = Steam.FindInstall();
        if (steam != null)
        {
            try { list.AddRange(Steam.InstalledGames(steam)); } catch (Exception ex) { Log.Warn("Steam scan: " + ex.Message); }
            try { list.AddRange(Steam.Shortcuts(steam)); } catch (Exception ex) { Log.Warn("Shortcut scan: " + ex.Message); }
        }
        var tSteam = sw.ElapsedMilliseconds;
        ct.ThrowIfCancellationRequested();
        try { list.AddRange(RomScanner.Scan(ct)); } catch (OperationCanceledException) { throw; } catch (Exception ex) { Log.Warn("ROM scan: " + ex.Message); }
        var tRoms = sw.ElapsedMilliseconds - tSteam;
        list.AddRange(LoadManual());
        AttachEmulatorCovers(list);
        Log.Info($"Scan timings: steam {tSteam} ms, roms {tRoms} ms, covers {sw.ElapsedMilliseconds - tSteam - tRoms} ms");
        return list.GroupBy(g => g.Id).Select(g => g.First()).ToList();
    }

    /// <summary>Reuse cover art the user already has in PCSX2 / DuckStation cover folders.</summary>
    static void AttachEmulatorCovers(List<Game> list)
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var dirs = new Dictionary<string, string>
        {
            ["PS2"] = Path.Combine(docs, "PCSX2", "covers"),
            ["PS1"] = Path.Combine(docs, "DuckStation", "covers"),
        };
        foreach (var g in list.Where(g => g.LocalArtHint == null && g.Path != null && dirs.ContainsKey(g.Platform)))
        {
            var dir = dirs[g.Platform];
            if (!Directory.Exists(dir)) continue;
            var stem = Path.GetFileNameWithoutExtension(g.Path!);
            foreach (var name in new[] { stem, g.Title })
                foreach (var ext in new[] { ".jpg", ".png", ".webp" })
                {
                    var f = Path.Combine(dir, name + ext);
                    if (g.LocalArtHint == null && File.Exists(f)) g.LocalArtHint = f;
                }
        }
    }

    public static List<Game> LoadManual()
    {
        try
        {
            return File.Exists(ManualPath) ? JsonSerializer.Deserialize<List<Game>>(File.ReadAllText(ManualPath), AppSettings.JsonOpts) ?? new() : new();
        }
        catch { return new(); }
    }

    public void AddManual(string exe, string? title = null)
    {
        var list = LoadManual();
        var g = new Game
        {
            Id = "exe:" + RomScanner.StableHash(exe), Title = title ?? Path.GetFileNameWithoutExtension(exe), Platform = "PC",
            Kind = LaunchKind.Exe, Path = exe, WorkDir = Path.GetDirectoryName(exe), InstallDir = Path.GetDirectoryName(exe),
        };
        list.RemoveAll(x => x.Id == g.Id);
        list.Add(g);
        AtomicFile.WriteAllText(ManualPath, JsonSerializer.Serialize(list, AppSettings.JsonOpts));
    }

    public void RemoveManual(string id)
    {
        var list = LoadManual();
        if (list.RemoveAll(x => x.Id == id) > 0) AtomicFile.WriteAllText(ManualPath, JsonSerializer.Serialize(list, AppSettings.JsonOpts));
    }

    public void Save()
    {
        try { AtomicFile.WriteAllText(CachePath, JsonSerializer.Serialize(Games, AppSettings.JsonOpts)); }
        catch (Exception ex) { Log.Warn("save library: " + ex.Message); }
    }

    /// <summary>Most recently played first, like the console home row; then alphabetical.</summary>
    public void Sort()
    {
        var lp = AppSettings.Current.LastPlayed;
        Games = Games.OrderByDescending(g => lp.TryGetValue(g.Id, out var t) ? t : 0).ThenBy(g => g.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IEnumerable<Game> Visible
    {
        get
        {
            var o = AppSettings.Current.Games;
            return Games.Where(g => !(o.TryGetValue(g.Id, out var x) && x.Hidden));
        }
    }

    public static string DisplayTitle(Game g)
        => AppSettings.Current.Games.TryGetValue(g.Id, out var o) && !string.IsNullOrWhiteSpace(o.Title) ? o.Title! : g.Title;

    public void NotifyChanged() => Changed?.Invoke();
}
