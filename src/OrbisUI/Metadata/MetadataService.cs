using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OrbisUI.Core;
using OrbisUI.Library;

namespace OrbisUI.Metadata;

/// <summary>
/// Fills in artwork and details. Order: art shipped with the game (PS4/PS3/Vita package icons, Steam's own cache, emulator covers)
/// -> SteamGridDB -> Steam Store -> IGDB, then a generated fallback tile in the UI. Every download is cached; failures are remembered
/// so the network is not hit again on each start.
/// </summary>
public sealed partial class MetadataService
{
    static readonly HttpClient Http = CreateClient();
    readonly SemaphoreSlim _gate = new(3);
    readonly LibraryService _lib;
    CancellationTokenSource? _cts;
    string? _igdbToken;
    DateTime _igdbExpiry;

    public event Action<Game>? Updated;

    public MetadataService(LibraryService lib) => _lib = lib;

    static HttpClient CreateClient()
    {
        var c = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All, PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("OrbisUI/0.1 (+https://github.com/knivesbatz/OrbisUI)");
        return c;
    }

    public void Cancel() => _cts?.Cancel();

    /// <summary>Process everything that still lacks art (cheap local pass first, then network).</summary>
    public async Task RunAsync(bool force = false)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        bool sgdb = Secrets.Has(Secrets.SteamGridDb);
        var todo = _lib.Games.Where(g => force || g.Art == null || g.Background == null || !g.MetaTried || (sgdb && WantsSgdb(g))).ToList();
        if (todo.Count == 0) return;
        Log.Info($"Metadata: {todo.Count} items to process");
        var tasks = todo.Select(async g =>
        {
            await _gate.WaitAsync(ct);
            try { await ProcessAsync(g, force, ct); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Warn($"Metadata {g.Title}: {ex.Message}"); }
            finally { _gate.Release(); }
        });
        try { await Task.WhenAll(tasks); } catch (OperationCanceledException) { }
        _lib.Save();
    }

    public async Task ProcessAsync(Game g, bool force, CancellationToken ct)
    {
        bool changed = false;
        var ovr = AppSettings.Current.Games.GetValueOrDefault(g.Id);
        var title = LibraryService.DisplayTitle(g);

        // 0) SteamGridDB overrides every other source (the user's choice), unless they picked an image themselves.
        if (ovr?.ArtPath == null && Secrets.Has(Secrets.SteamGridDb) && (force || WantsSgdb(g)))
        {
            var r = await SteamGridDbAsync(g, title, g.Kind == LaunchKind.Steam ? g.SteamAppId : null, ct, replace: true);
            // Only give up on SteamGridDB once it actually answered; offline starts retry next time.
            g.SgdbTried = r != null;
            changed |= r == true;
        }

        // 1) Art the user picked, or that came with the game.
        if (ovr?.ArtPath != null || g.Art == null)
        {
            var pick = ovr?.ArtPath ?? g.LocalArtHint;
            if (pick != null && File.Exists(pick) && SaveTile(g, await File.ReadAllBytesAsync(pick, ct), SourceName(g, pick))) changed = true;
        }
        // Backgrounds come only from SteamGridDB heroes (user preference).

        bool online = !g.MetaTried || force;
        if (online)
        {
            // Steam games: details and missing art straight from the Steam Store by app id.
            string? appId = g.Kind == LaunchKind.Steam ? g.SteamAppId : null;

            // 2) SteamGridDB (square grids are what we want for tiles).
            if (g.Art == null || g.Background == null)
                changed |= await SteamGridDbAsync(g, title, appId, ct) == true;

            // 2a) PlayStation Store: square key art and details for PlayStation games.
            if (g.Platform != "PC" && (g.Art == null || g.Description == null))
                changed |= await PsStoreAsync(g, title, ct);

            // 2b) libretro-thumbnails: free box art for disc-image names (Redump/No-Intro), no key needed.
            if (g.Art == null && g.Kind == LaunchKind.Emulator)
                changed |= await LibretroAsync(g, ct);

            // 3) Steam Store: details for everything, art for PC titles.
            if (g.Platform == "PC")
            {
                appId ??= await SteamSearchAsync(title, ct);
                if (appId != null) changed |= await SteamStoreAsync(g, appId, ct);
            }

            // 4) IGDB: console titles and anything still missing details.
            if (g.Description == null || g.Art == null)
                changed |= await IgdbAsync(g, title, ct);

            g.MetaTried = true;
            changed = true;
        }
        if (changed) Updated?.Invoke(g);
    }

    /// <summary>SteamGridDB is the default art source: keep trying until the tile comes from it (or the user picked their own).</summary>
    static bool WantsSgdb(Game g) => AppSettings.Current.Games.GetValueOrDefault(g.Id)?.ArtPath == null
        && (!g.SgdbTried || g.ArtSource?.StartsWith("SteamGridDB") != true);

    static string SourceName(Game g, string path)
    {
        if (path.Contains("librarycache", StringComparison.OrdinalIgnoreCase) || path.Contains(@"\grid\", StringComparison.OrdinalIgnoreCase)) return "Steam (local)";
        if (g.Platform is "PS4" or "PS3" or "VITA" && path.Contains("sce_sys", StringComparison.OrdinalIgnoreCase) || path.EndsWith("ICON0.PNG", StringComparison.OrdinalIgnoreCase)) return "PlayStation (game package)";
        if (path.Contains("covers", StringComparison.OrdinalIgnoreCase)) return "Emulator covers";
        return "Local file";
    }

    static string SafeId(Game g) => Regex.Replace(g.Id, @"[^A-Za-z0-9_\-]", "_");

    bool SaveTile(Game g, byte[] data, string source)
    {
        try
        {
            var orig = Path.Combine(AppPaths.ArtOriginals, SafeId(g) + "_tile.src");
            AtomicFile.WriteAllBytes(orig, data);
            var file = Path.Combine(AppPaths.Art, SafeId(g) + ".jpg");
            AtomicFile.WriteAllBytes(file, ImageTools.MakeSquare(data));
            g.Art = file;
            g.ArtSource = source;
            Log.Info($"Art for {g.Title} from {source}");
            return true;
        }
        catch (Exception ex) { Log.Warn($"tile {g.Title}: {ex.Message}"); return false; }
    }

    bool SaveBackground(Game g, byte[] data)
    {
        try
        {
            var file = Path.Combine(AppPaths.Art, SafeId(g) + "_bg.jpg");
            AtomicFile.WriteAllBytes(file, ImageTools.MakeBackground(data));
            g.Background = file;
            g.BgSource = "SteamGridDB";
            return true;
        }
        catch (Exception ex) { Log.Warn($"background {g.Title}: {ex.Message}"); return false; }
    }

    static async Task<byte[]?> GetBytes(string url, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var r = await Http.GetAsync(url, ct);
                if (r.StatusCode == HttpStatusCode.TooManyRequests) { await Task.Delay(2000 * (attempt + 1), ct); continue; }
                if (!r.IsSuccessStatusCode) return null;
                return await r.Content.ReadAsByteArrayAsync(ct);
            }
            catch (HttpRequestException) { await Task.Delay(500, ct); }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { }
        }
        return null;
    }

    static async Task<JsonDocument?> GetJson(HttpRequestMessage req, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            using var clone = Clone(req);
            try
            {
                using var r = await Http.SendAsync(clone, ct);
                if (r.StatusCode == HttpStatusCode.TooManyRequests) { await Task.Delay(2000 * (attempt + 1), ct); continue; }
                if (!r.IsSuccessStatusCode) { Log.Warn($"{req.RequestUri?.Host} {(int)r.StatusCode}"); return null; }
                return JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
            }
            catch (HttpRequestException ex) { Log.Warn($"{req.RequestUri?.Host}: {ex.Message}"); await Task.Delay(500, ct); }
            catch (JsonException) { return null; }
        }
        return null;
    }

    static HttpRequestMessage Clone(HttpRequestMessage r)
    {
        var c = new HttpRequestMessage(r.Method, r.RequestUri);
        foreach (var h in r.Headers) c.Headers.TryAddWithoutValidation(h.Key, h.Value);
        if (r.Content is StringContent sc) c.Content = new StringContent(sc.ReadAsStringAsync().Result, Encoding.UTF8, sc.Headers.ContentType?.MediaType ?? "text/plain");
        return c;
    }

    [GeneratedRegex(@"[^a-z0-9]")] private static partial Regex NonAlnum();

    /// <summary>Lowercase alphanumerics, no ™/®, no disc suffix, "&" = "and".</summary>
    public static string Normalize(string s)
    {
        s = Regex.Replace(s, @"\(Disc \d+\)", "", RegexOptions.IgnoreCase).Replace("&", "and").ToLowerInvariant();
        return NonAlnum().Replace(s, "");
    }

    public static bool TitlesMatch(string a, string b)
    {
        var na = Normalize(a); var nb = Normalize(b);
        if (na.Length == 0 || nb.Length == 0) return false;
        if (na == nb) return true;
        var (shorter, longer) = na.Length < nb.Length ? (na, nb) : (nb, na);
        if (!longer.StartsWith(shorter) || shorter.Length < 0.6 * longer.Length) return false;
        // "portal2" is a sequel of "portal", not the same game.
        return !Regex.IsMatch(longer[shorter.Length..], "^([0-9]+|i{1,3}|iv|v|vi{1,3}|ix|x)$");
    }

    // ---------------- SteamGridDB ----------------
    /// <summary>True when art was saved, false when SteamGridDB has none, null when it couldn't be reached.</summary>
    async Task<bool?> SteamGridDbAsync(Game g, string title, string? steamAppId, CancellationToken ct, bool replace = false)
    {
        var key = Secrets.Get(Secrets.SteamGridDb);
        if (key == null) return false;
        HttpRequestMessage Req(string path)
        {
            var r = new HttpRequestMessage(HttpMethod.Get, "https://www.steamgriddb.com/api/v2/" + path);
            r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            return r;
        }
        string? gameId = null;
        if (steamAppId != null)
        {
            using var d = await GetJson(Req("games/steam/" + steamAppId), ct);
            if (d != null && d.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("id", out var idEl)) gameId = idEl.GetRawText();
        }
        if (gameId == null)
        {
            using var d = await GetJson(Req("search/autocomplete/" + Uri.EscapeDataString(Regex.Replace(title, @"\(Disc \d+\)", "").Trim())), ct);
            if (d == null) return null;
            if (!d.RootElement.TryGetProperty("data", out var arr) || arr.GetArrayLength() == 0) return false;
            var best = arr.EnumerateArray().FirstOrDefault(e => TitlesMatch(e.GetProperty("name").GetString() ?? "", title));
            if (best.ValueKind == JsonValueKind.Undefined) best = arr[0];
            gameId = best.GetProperty("id").GetRawText();
        }
        bool changed = false;
        if (g.Art == null || replace)
        {
            using var d = await GetJson(Req($"grids/game/{gameId}?dimensions=512x512,1024x1024&types=static&nsfw=false&humor=false"), ct);
            if (FirstUrl(d) is string url && await GetBytes(url, ct) is byte[] img) changed |= SaveTile(g, img, "SteamGridDB");
        }
        if (g.Background == null || replace)
        {
            using var d = await GetJson(Req($"heroes/game/{gameId}?types=static&nsfw=false&humor=false"), ct);
            if (FirstUrl(d) is string url && await GetBytes(url, ct) is byte[] img) changed |= SaveBackground(g, img);
        }
        return changed;
    }

    static string? FirstUrl(JsonDocument? d)
    {
        if (d == null || !d.RootElement.TryGetProperty("data", out var arr) || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0) return null;
        return arr[0].TryGetProperty("url", out var u) ? u.GetString() : null;
    }

    // ---------------- Artwork picker (SteamGridDB alternatives) ----------------
    public sealed record ArtOption(string Thumb, string Url, string Author);

    /// <summary>Square grids for the best-matching SteamGridDB game (or a search term the user typed).</summary>
    public async Task<(string? gameName, List<ArtOption> options)> SgdbOptionsAsync(Game g, string? search = null)
    {
        var key = Secrets.Get(Secrets.SteamGridDb);
        var list = new List<ArtOption>();
        if (key == null) return (null, list);
        HttpRequestMessage Req(string path)
        {
            var r = new HttpRequestMessage(HttpMethod.Get, "https://www.steamgriddb.com/api/v2/" + path);
            r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            return r;
        }
        string? id = null, name = null;
        if (search == null && g.Kind == LaunchKind.Steam && g.SteamAppId != null)
        {
            using var d = await GetJson(Req("games/steam/" + g.SteamAppId), CancellationToken.None);
            if (d != null && d.RootElement.TryGetProperty("data", out var data)) { id = data.GetProperty("id").GetRawText(); name = data.GetProperty("name").GetString(); }
        }
        if (id == null)
        {
            var term = search ?? Regex.Replace(LibraryService.DisplayTitle(g), @"\(Disc \d+\)", "").Trim();
            using var d = await GetJson(Req("search/autocomplete/" + Uri.EscapeDataString(term)), CancellationToken.None);
            if (d == null || !d.RootElement.TryGetProperty("data", out var arr) || arr.GetArrayLength() == 0) return (null, list);
            var best = arr.EnumerateArray().FirstOrDefault(e => TitlesMatch(e.GetProperty("name").GetString() ?? "", term));
            if (best.ValueKind == JsonValueKind.Undefined) best = arr[0];
            id = best.GetProperty("id").GetRawText(); name = best.GetProperty("name").GetString();
        }
        using (var d = await GetJson(Req($"grids/game/{id}?dimensions=512x512,1024x1024&nsfw=false&humor=any&limit=50"), CancellationToken.None))
        {
            if (d != null && d.RootElement.TryGetProperty("data", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var e in arr.EnumerateArray())
                    list.Add(new ArtOption(e.GetProperty("thumb").GetString() ?? "", e.GetProperty("url").GetString() ?? "",
                        e.TryGetProperty("author", out var a) && a.TryGetProperty("name", out var an) ? an.GetString() ?? "" : ""));
        }
        return (name, list);
    }

    /// <summary>Use a chosen image as this game's tile from now on (kept across refreshes).</summary>
    public async Task<bool> ApplyChosenArtAsync(Game g, string url)
    {
        var data = await GetBytes(url, CancellationToken.None);
        if (data == null) return false;
        var dir = Path.Combine(AppPaths.Art, "Chosen");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, SafeId(g) + Path.GetExtension(new Uri(url).AbsolutePath));
        AtomicFile.WriteAllBytes(file, data);
        AppSettings.Current.For(g.Id).ArtPath = file;
        AppSettings.Current.Save();
        var ok = SaveTile(g, data, "SteamGridDB (chosen)");
        if (ok) { _lib.Save(); Updated?.Invoke(g); }
        return ok;
    }

    /// <summary>Square art for a TV &amp; Video app (Netflix, YouTube...) from SteamGridDB (the first match, or a chosen image url), cached under Cache\Art\Apps.</summary>
    public async Task<string?> AppArtAsync(string name, string? url = null)
    {
        try
        {
            if (url == null)
            {
                var (_, opts) = await SgdbOptionsAsync(AppGame(name), name);
                if (opts.Count == 0) return null;
                url = opts[0].Url;
            }
            var data = await GetBytes(url, CancellationToken.None);
            return data == null ? null : SaveAppArt(name, data);
        }
        catch (Exception ex) { Log.Warn($"app art {name}: {ex.Message}"); return null; }
    }

    /// <summary>
    /// Steam library art for a non-Steam shortcut, from SteamGridDB (the user's default source): portrait capsule, wide capsule,
    /// hero and logo, saved under userdata\...\config\grid with Steam's file names. Existing files are kept.
    /// </summary>
    /// <summary>Grid, wide grid, hero, logo and icon for a non-Steam shortcut, saved the way Steam looks for them.
    /// Returns the icon file (Steam takes it from the shortcut's "icon" field), or null.</summary>
    public static async Task<string?> SteamLibraryArtAsync(Game g, string title, string gridDir, uint appId, CancellationToken ct)
    {
        string? Icon() => Directory.Exists(gridDir) ? Directory.EnumerateFiles(gridDir, $"{appId}_icon.*").FirstOrDefault() : null;
        var suffixes = new[] { "p", "", "_hero", "_logo", "_icon" };
        if (Directory.Exists(gridDir) && suffixes.All(sfx => Directory.EnumerateFiles(gridDir, $"{appId}{sfx}.*").Any(f => sfx != "" || Path.GetFileNameWithoutExtension(f) == appId.ToString()))) return Icon();
        var key = Secrets.Get(Secrets.SteamGridDb);
        if (key == null) return Icon();
        HttpRequestMessage Req(string path)
        {
            var r = new HttpRequestMessage(HttpMethod.Get, "https://www.steamgriddb.com/api/v2/" + path);
            r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            return r;
        }
        string term = Regex.Replace(title, @"\(Disc \d+\)", "").Trim();
        string? id;
        using (var d = await GetJson(Req("search/autocomplete/" + Uri.EscapeDataString(term)), ct))
        {
            if (d == null || !d.RootElement.TryGetProperty("data", out var arr) || arr.GetArrayLength() == 0) return Icon();
            var best = arr.EnumerateArray().FirstOrDefault(e => TitlesMatch(e.GetProperty("name").GetString() ?? "", term));
            id = (best.ValueKind == JsonValueKind.Undefined ? arr[0] : best).GetProperty("id").GetRawText();
        }
        Directory.CreateDirectory(gridDir);
        foreach (var (query, suffix) in new[]
        {
            ($"grids/game/{id}?dimensions=600x900&types=static&nsfw=false&humor=false", "p"),
            ($"grids/game/{id}?dimensions=920x430,460x215&types=static&nsfw=false&humor=false", ""),
            ($"heroes/game/{id}?types=static&nsfw=false&humor=false", "_hero"),
            ($"logos/game/{id}?types=static&nsfw=false&humor=false", "_logo"),
            ($"icons/game/{id}?types=static&nsfw=false&humor=false", "_icon"),
        })
        {
            if (Directory.EnumerateFiles(gridDir, $"{appId}{suffix}.*").Any(f => suffix != "" || Path.GetFileNameWithoutExtension(f) == appId.ToString())) continue;
            using var d = await GetJson(Req(query), ct);
            if (FirstUrl(d) is not string url || await GetBytes(url, ct) is not byte[] img) continue;
            var ext = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant() switch { ".jpg" or ".jpeg" => ".jpg", ".ico" => ".ico", _ => ".png" };
            AtomicFile.WriteAllBytes(Path.Combine(gridDir, $"{appId}{suffix}{ext}"), img);
        }
        Log.Info($"Steam library art for {title} from SteamGridDB");
        return Icon();
    }

    /// <summary>Stand-in game so app art can use the SteamGridDB search.</summary>
    public static Game AppGame(string name) => new() { Id = "app:" + name, Title = name };

    public static string SaveAppArt(string name, byte[] data)
    {
        var dir = Path.Combine(AppPaths.Art, "Apps");
        Directory.CreateDirectory(dir);
        // A new name per save so the tile cache never shows the old picture.
        var file = Path.Combine(dir, $"{RomScanner.StableHash(name)}-{DateTime.Now:yyyyMMddHHmmss}.jpg");
        AtomicFile.WriteAllBytes(file, ImageTools.MakeSquare(data));
        foreach (var old in Directory.GetFiles(dir, RomScanner.StableHash(name) + "*.jpg").Where(x => x != file)) try { File.Delete(old); } catch { }
        return file;
    }

    // ---------------- libretro-thumbnails (thumbnails.libretro.com) ----------------
    static readonly Dictionary<string, string> LibretroSystems = new()
    {
        ["PS1"] = "Sony - PlayStation", ["PS2"] = "Sony - PlayStation 2", ["PSP"] = "Sony - PlayStation Portable",
        ["PS3"] = "Sony - PlayStation 3", ["VITA"] = "Sony - PlayStation Vita",
    };

    /// <summary>libretro names thumbnails after the dump name, with &amp;*/:`&lt;&gt;?\|" replaced by _.</summary>
    public static string LibretroName(string fileStem)
    {
        var s = Regex.Replace(fileStem, @"\s*\(Track \d+\)", "", RegexOptions.IgnoreCase);
        return Regex.Replace(s, "[&*/:`<>?\\\\|\"]", "_");
    }

    async Task<bool> LibretroAsync(Game g, CancellationToken ct)
    {
        if (!LibretroSystems.TryGetValue(g.Platform, out var sys) || g.Path == null) return false;
        var stem = Path.GetFileNameWithoutExtension(g.RomFile ?? g.Path);
        var names = new List<string> { LibretroName(stem) };
        var noDisc = Regex.Replace(names[0], @"\s*\(Disc \d+\)", "");
        if (noDisc != names[0]) names.Add(noDisc);
        bool changed = false;
        foreach (var n in names)
        {
            var baseUrl = $"https://thumbnails.libretro.com/{Uri.EscapeDataString(sys)}/";
            if (g.Art == null && await GetBytes(baseUrl + "Named_Boxarts/" + Uri.EscapeDataString(n) + ".png", ct) is byte[] box)
                changed |= SaveTile(g, box, "libretro-thumbnails");
            if (g.Art != null) break;
        }
        return changed;
    }

    // ---------------- Steam Store (public storefront endpoints, no key) ----------------
    async Task<string?> SteamSearchAsync(string title, CancellationToken ct)
    {
        using var d = await GetJson(new HttpRequestMessage(HttpMethod.Get,
            "https://store.steampowered.com/api/storesearch/?l=english&cc=US&term=" + Uri.EscapeDataString(title)), ct);
        if (d == null || !d.RootElement.TryGetProperty("items", out var items)) return null;
        foreach (var it in items.EnumerateArray())
            if (TitlesMatch(it.GetProperty("name").GetString() ?? "", title)) return it.GetProperty("id").GetRawText();
        return null;
    }

    async Task<bool> SteamStoreAsync(Game g, string appId, CancellationToken ct)
    {
        bool changed = false;
        if (g.Description == null)
        {
            using var d = await GetJson(new HttpRequestMessage(HttpMethod.Get,
                $"https://store.steampowered.com/api/appdetails?appids={appId}&l=english&filters=basic,genres,release_date"), ct);
            if (d != null && d.RootElement.TryGetProperty(appId, out var root) && root.TryGetProperty("data", out var data))
            {
                g.Description = data.TryGetProperty("short_description", out var sd) ? WebUtility.HtmlDecode(sd.GetString()) : null;
                if (data.TryGetProperty("publishers", out var pubs) && pubs.ValueKind == JsonValueKind.Array && pubs.GetArrayLength() > 0) g.Publisher = pubs[0].GetString();
                if (data.TryGetProperty("release_date", out var rd) && rd.TryGetProperty("date", out var date))
                    g.Year = Regex.Match(date.GetString() ?? "", @"\d{4}").Value is { Length: 4 } y ? y : null;
                if (data.TryGetProperty("genres", out var gen) && gen.ValueKind == JsonValueKind.Array)
                    g.Genres = string.Join(", ", gen.EnumerateArray().Select(x => x.GetProperty("description").GetString()).Take(3));
                g.MetaSource = "Steam Store";
                changed = true;
            }
        }
        var cdn = $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/";
        if (g.Art == null && await GetBytes(cdn + "library_600x900_2x.jpg", ct) is byte[] cap) changed |= SaveTile(g, cap, "Steam Store");
        if (g.Art == null && await GetBytes(cdn + "header.jpg", ct) is byte[] hdr) changed |= SaveTile(g, hdr, "Steam Store");
        return changed;
    }

    // ---------------- IGDB (Twitch client credentials) ----------------
    static readonly Dictionary<string, string> IgdbPlatforms = new()
    {
        ["PS1"] = "7", ["PS2"] = "8", ["PS3"] = "9", ["PS4"] = "48", ["PSP"] = "38", ["VITA"] = "46", ["PC"] = "6",
    };

    async Task<string?> IgdbTokenAsync(CancellationToken ct)
    {
        if (_igdbToken != null && DateTime.UtcNow < _igdbExpiry) return _igdbToken;
        var id = Secrets.Get(Secrets.IgdbClientId); var secret = Secrets.Get(Secrets.IgdbClientSecret);
        if (id == null || secret == null) return null;
        using var r = await Http.PostAsync($"https://id.twitch.tv/oauth2/token?client_id={Uri.EscapeDataString(id)}&client_secret={Uri.EscapeDataString(secret)}&grant_type=client_credentials", null, ct);
        if (!r.IsSuccessStatusCode) { Log.Warn("IGDB token failed: " + (int)r.StatusCode); return null; }
        using var d = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
        _igdbToken = d.RootElement.GetProperty("access_token").GetString();
        _igdbExpiry = DateTime.UtcNow.AddSeconds(d.RootElement.GetProperty("expires_in").GetInt32() - 300);
        return _igdbToken;
    }

    // ---------------- PlayStation Store (the store website's own search query, no sign-in) ----------------
    // Same persisted GraphQL query and headers the store.playstation.com web app sends (as used by playnite-metadata-psn-universal).
    // shortcut: the query hash changes when Sony updates the store app; on failure the other sources still run. Update the hash then.
    const string PsStoreSearchHash = "4df6284f982e57bec70f23c77e2c219dc792eb19af7fb3d3a81767aa3f1958aa";

    static HttpRequestMessage PsStoreRequest(string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Safari/537.36");
        req.Headers.TryAddWithoutValidation("Origin", "https://store.playstation.com");
        req.Headers.TryAddWithoutValidation("Referer", "https://store.playstation.com/");
        req.Headers.TryAddWithoutValidation("apollographql-client-name", "@sie-ppr-web-store/app");
        req.Headers.TryAddWithoutValidation("apollographql-client-version", "0.113.0");
        req.Headers.TryAddWithoutValidation("X-PSN-Store-Locale-Override", "en-US");
        req.Content = new StringContent("", Encoding.UTF8, "application/json"); // the store API answers 400 without a JSON content type, even on GET
        return req;
    }

    async Task<bool> PsStoreAsync(Game g, string title, CancellationToken ct)
    {
        var clean = Regex.Replace(title, @"\(Disc \d+\)|\((USA|Europe|Japan|En|[A-Z][a-z](,[A-Z][a-z])*)[^)]*\)", "").Trim();
        var vars = JsonSerializer.Serialize(new { countryCode = "US", languageCode = "en", nextCursor = "", pageOffset = 0, pageSize = 10, searchTerm = clean });
        var ext = $"{{\"persistedQuery\":{{\"version\":1,\"sha256Hash\":\"{PsStoreSearchHash}\"}}}}";
        using var d = await GetJson(PsStoreRequest($"https://web.np.playstation.com/api/graphql/v1//op?operationName=getSearchResults&variables={Uri.EscapeDataString(vars)}&extensions={Uri.EscapeDataString(ext)}"), ct);
        if (d == null || !d.RootElement.TryGetProperty("data", out var data) || !data.TryGetProperty("universalSearch", out var us) || !us.TryGetProperty("results", out var results)) return false;
        var best = results.EnumerateArray().FirstOrDefault(r => r.TryGetProperty("name", out var n) && TitlesMatch(n.GetString() ?? "", clean));
        if (best.ValueKind == JsonValueKind.Undefined) return false; // no confident match: leave it to IGDB
        bool changed = false;
        if (g.Art == null && best.TryGetProperty("media", out var media))
        {
            var url = media.EnumerateArray().Where(m => m.GetProperty("type").GetString() == "IMAGE")
                .FirstOrDefault(m => m.GetProperty("role").GetString() == "MASTER") is { ValueKind: JsonValueKind.Object } master ? master.GetProperty("url").GetString() : null;
            if (url != null && await GetBytes(url + "?w=720", ct) is byte[] img) changed |= SaveTile(g, img, "PlayStation Store");
        }
        if (g.Description == null && best.TryGetProperty("id", out var idEl) && idEl.GetString() is string id)
        {
            try
            {
                using var r = await Http.SendAsync(PsStoreRequest("https://store.playstation.com/en-us/product/" + Uri.EscapeDataString(id)), ct);
                if (r.IsSuccessStatusCode)
                {
                    var html = await r.Content.ReadAsStringAsync(ct);
                    string? Field(string qa) => Regex.Match(html, $"data-qa=\"{Regex.Escape(qa)}\"[^>]*>(.*?)</(p|span|div)>", RegexOptions.Singleline) is { Success: true } m
                        ? WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(m.Groups[1].Value, @"<br\s*/?>", "\n"), "<[^>]+>", "")).Trim() : null;
                    var desc = Field("mfe-game-overview#description");
                    if (!string.IsNullOrWhiteSpace(desc))
                    {
                        g.Description = desc;
                        g.Publisher ??= Field("mfe-game-title#publisher");
                        g.Year ??= Regex.Match(Field("gameInfo#releaseInformation#releaseDate-value") ?? "", @"\d{4}").Value is { Length: 4 } y ? y : null;
                        g.Genres ??= Field("gameInfo#releaseInformation#genre-value");
                        g.MetaSource = "PlayStation Store";
                        changed = true;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { Log.Warn("PlayStation Store page: " + ex.Message); }
        }
        return changed;
    }

    async Task<bool> IgdbAsync(Game g, string title, CancellationToken ct)
    {
        var token = await IgdbTokenAsync(ct);
        if (token == null) return false;
        var clean = Regex.Replace(title, @"\(Disc \d+\)", "").Replace("\"", "").Trim();
        var platform = IgdbPlatforms.GetValueOrDefault(g.Platform);
        var body = $"search \"{clean}\"; fields name,summary,first_release_date,cover.image_id,artworks.image_id,screenshots.image_id,genres.name,involved_companies.company.name,involved_companies.publisher; "
                 + (platform != null ? $"where platforms = ({platform}); " : "") + "limit 5;";
        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/games") { Content = new StringContent(body, Encoding.UTF8, "text/plain") };
        req.Headers.TryAddWithoutValidation("Client-ID", Secrets.Get(Secrets.IgdbClientId));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var d = await GetJson(req, ct);
        if (d == null || d.RootElement.ValueKind != JsonValueKind.Array || d.RootElement.GetArrayLength() == 0) return false;
        var best = d.RootElement.EnumerateArray().FirstOrDefault(e => TitlesMatch(e.GetProperty("name").GetString() ?? "", clean));
        if (best.ValueKind == JsonValueKind.Undefined) best = d.RootElement[0];

        bool changed = false;
        if (g.Description == null && best.TryGetProperty("summary", out var sum))
        {
            g.Description = sum.GetString();
            if (best.TryGetProperty("first_release_date", out var frd)) g.Year = DateTimeOffset.FromUnixTimeSeconds(frd.GetInt64()).Year.ToString();
            if (best.TryGetProperty("genres", out var gen)) g.Genres = string.Join(", ", gen.EnumerateArray().Select(x => x.GetProperty("name").GetString()).Take(3));
            if (best.TryGetProperty("involved_companies", out var ic))
                g.Publisher = ic.EnumerateArray().Where(x => x.TryGetProperty("publisher", out var p) && p.GetBoolean())
                    .Select(x => x.GetProperty("company").GetProperty("name").GetString()).FirstOrDefault();
            g.MetaSource = "IGDB";
            changed = true;
        }
        if (g.Art == null && best.TryGetProperty("cover", out var cover) && cover.TryGetProperty("image_id", out var cid)
            && await GetBytes($"https://images.igdb.com/igdb/image/upload/t_cover_big_2x/{cid.GetString()}.jpg", ct) is byte[] img)
            changed |= SaveTile(g, img, "IGDB");
        return changed;
    }

    /// <summary>Validate an API key during setup (no data is stored on failure).</summary>
    public static async Task<bool> ValidateSteamGridDb(string key)
    {
        var r = new HttpRequestMessage(HttpMethod.Get, "https://www.steamgriddb.com/api/v2/search/autocomplete/portal");
        r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        try { using var resp = await Http.SendAsync(r); return resp.IsSuccessStatusCode; } catch { return false; }
    }

    public static async Task<bool> ValidateIgdb(string id, string secret)
    {
        try
        {
            using var r = await Http.PostAsync($"https://id.twitch.tv/oauth2/token?client_id={Uri.EscapeDataString(id)}&client_secret={Uri.EscapeDataString(secret)}&grant_type=client_credentials", null);
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
