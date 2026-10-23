using System.Net.Http;
using System.Text.Json;
using OrbisUI.Core;

namespace OrbisUI.Services;

/// <summary>
/// What's New feed from the Steam Web API: you and your friends playing now, games played in the last two weeks,
/// and your recently unlocked achievements. Needs the Steam Web API key from Settings > Accounts; friends must have
/// public profiles. Cached for 10 minutes so opening the feed again is instant and cheap.
/// </summary>
public static class SteamActivity
{
    public sealed record Item(string Who, string? Avatar, string Text, string? AppId, DateTime? When, int Rank, string? Art = null);

    /// <summary>Your own recent plays from OrbisUI's history; needs no key and covers emulated games too.</summary>
    public static List<Item> Local()
    {
        string? avatar = null;
        if (Library.Steam.FindInstall() is string steam && Library.Steam.CurrentUser(steam).Item1 is string id)
            avatar = Path.Combine(steam, "config", "avatarcache", id + ".png");
        var games = UI.Shell.Library.Games.ToDictionary(g => g.Id, StringComparer.OrdinalIgnoreCase);
        return AppSettings.Current.LastPlayed
            .Where(kv => games.ContainsKey(kv.Key))
            .OrderByDescending(kv => kv.Value).Take(20)
            .Select(kv => { var g = games[kv.Key]; return new Item("You", avatar, "played " + Library.LibraryService.DisplayTitle(g), g.SteamAppId,
                DateTimeOffset.FromUnixTimeSeconds(kv.Value).LocalDateTime, 3, g.SteamAppId == null ? g.Art : null); })
            .ToList();
    }

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    static (DateTime at, List<Item> items)? _cache;
    const string Api = "https://api.steampowered.com/";

    public static bool Configured => Secrets.Has(Secrets.SteamWebApi) && AppSettings.Current.SteamId64 != null;

    public static async Task<List<Item>> LoadAsync(bool force = false)
    {
        if (!force && _cache is { } c && DateTime.Now - c.at < TimeSpan.FromMinutes(10)) return c.items;
        var key = Secrets.Get(Secrets.SteamWebApi);
        var me = AppSettings.Current.SteamId64;
        if (key == null || me == null) return new();
        var items = new List<Item>();

        var friends = new List<string>();
        using (var d = await Get($"ISteamUser/GetFriendList/v1/?key={key}&steamid={me}&relationship=friend"))
            if (d != null && d.RootElement.TryGetProperty("friendslist", out var fl))
                friends.AddRange(fl.GetProperty("friends").EnumerateArray().Select(f => f.GetProperty("steamid").GetString()!));

        // Names, avatars and "playing now" for you and up to 99 friends in one call.
        var people = new Dictionary<string, (string name, string? avatar)>();
        var ids = new[] { me }.Concat(friends).Take(100).ToList();
        using (var d = await Get($"ISteamUser/GetPlayerSummaries/v2/?key={key}&steamids={string.Join(',', ids)}"))
            if (d != null)
                foreach (var p in d.RootElement.GetProperty("response").GetProperty("players").EnumerateArray())
                {
                    var id = p.GetProperty("steamid").GetString()!;
                    var name = id == me ? "You" : p.GetProperty("personaname").GetString() ?? "";
                    people[id] = (name, p.TryGetProperty("avatarfull", out var a) ? a.GetString() : null);
                    if (p.TryGetProperty("gameextrainfo", out var game))
                        items.Add(new Item(name, people[id].avatar, $"{(id == me ? "are" : "is")} playing {game.GetString()}",
                            p.TryGetProperty("gameid", out var gid) ? gid.GetString() : null, DateTime.Now, 0));
                }

        // Recently played (last two weeks), a few games each, fetched four at a time.
        var gate = new SemaphoreSlim(4);
        var recent = await Task.WhenAll(ids.Where(people.ContainsKey).Select(async id =>
        {
            await gate.WaitAsync();
            try
            {
                using var d = await Get($"IPlayerService/GetRecentlyPlayedGames/v1/?key={key}&steamid={id}&count=2");
                if (d == null || !d.RootElement.GetProperty("response").TryGetProperty("games", out var games)) return new List<Item>();
                return games.EnumerateArray().Select(g => new Item(people[id].name, people[id].avatar,
                    $"played {g.GetProperty("name").GetString()} for {g.GetProperty("playtime_2weeks").GetInt32() / 60.0:0.#} hours this fortnight",
                    g.GetProperty("appid").GetRawText(), null, 2)).ToList();
            }
            finally { gate.Release(); }
        }));
        var mine = recent.SelectMany(x => x).Where(i => i.Who == "You").ToList();
        items.AddRange(recent.SelectMany(x => x));

        // Your achievements unlocked in the last two weeks, from your recently played games.
        foreach (var g in mine.Where(i => i.AppId != null))
        {
            using var d = await Get($"ISteamUserStats/GetPlayerAchievements/v1/?key={key}&steamid={me}&appid={g.AppId}&l=english");
            if (d == null || !d.RootElement.GetProperty("playerstats").TryGetProperty("achievements", out var achs)) continue;
            var game = d.RootElement.GetProperty("playerstats").TryGetProperty("gameName", out var gn) ? gn.GetString() : "a game";
            foreach (var a in achs.EnumerateArray().Where(a => a.GetProperty("achieved").GetInt32() == 1))
            {
                var when = DateTimeOffset.FromUnixTimeSeconds(a.GetProperty("unlocktime").GetInt64()).LocalDateTime;
                if (DateTime.Now - when > TimeSpan.FromDays(14)) continue;
                var name = a.TryGetProperty("name", out var n) ? n.GetString() : a.GetProperty("apiname").GetString();
                items.Add(new Item("You", people.GetValueOrDefault(me).avatar, $"earned the trophy \"{name}\" in {game}", g.AppId, when, 1));
            }
        }

        var sorted = items.OrderBy(i => i.Rank).ThenByDescending(i => i.When ?? DateTime.MinValue).ToList();
        _cache = (DateTime.Now, sorted);
        return sorted;
    }

    static async Task<JsonDocument?> Get(string path)
    {
        try
        {
            using var r = await Http.GetAsync(Api + path);
            if (!r.IsSuccessStatusCode) return null; // private profiles answer 401/403
            return JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        }
        catch (Exception ex) { Log.Warn("Steam activity: " + ex.Message); return null; }
    }

    public static string HeaderImage(string appId) => $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg";
}
