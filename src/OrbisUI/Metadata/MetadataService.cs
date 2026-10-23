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
        var todo = _lib.Games.Where(g => force || g.Art == null || g.Background == null || !g.MetaTried || (sgdb && !g.SgdbTried)).ToList();
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
        if (ovr?.ArtPath == null && Secrets.Has(Secrets.SteamGridDb) && (force || !g.SgdbTried))
        {
            g.SgdbTried = true;
            changed |= await SteamGridDbAsync(g, title, g.Kind == LaunchKind.Steam ? g.SteamAppId : null, ct, replace: true);
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
                changed |= await SteamGridDbAsync(g, title, appId, ct);

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
        if (r.Content is StringContent sc) c.Content = new StringContent(sc.ReadAsStringAsync().Result, Encoding.UTF8, "text/plain");
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
    async Task<bool> SteamGridDbAsync(Game g, string title, string? steamAppId, CancellationToken ct, bool replace = false)
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
            if (d == null || !d.RootElement.TryGetProperty("data", out var arr) || arr.GetArrayLength() == 0) return false;
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

    /// <summary>Square art for a TV &amp; Video app (Netflix, YouTube...) from SteamGridDB, cached under Cache\Art\Apps.</summary>
    public async Task<string?> AppArtAsync(string name)
    {
        try
        {
            var (_, opts) = await SgdbOptionsAsync(new Game { Id = "app:" + name, Title = name }, name);
            if (opts.Count == 0) return null;
            var data = await GetBytes(opts[0].Url, CancellationToken.None);
            if (data == null) return null;
            var dir = Path.Combine(AppPaths.Art, "Apps");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, RomScanner.StableHash(name) + ".jpg");
            AtomicFile.WriteAllBytes(file, ImageTools.MakeSquare(data));
            return file;
        }
        catch (Exception ex) { Log.Warn($"app art {name}: {ex.Message}"); return null; }
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
