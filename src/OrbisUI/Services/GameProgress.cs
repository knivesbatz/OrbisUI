using System.Text.Json;
using OrbisUI.Core;
using OrbisUI.Library;

namespace OrbisUI.Services;

/// <summary>
/// Trophy progress for one game and how far your friends got, for the cards under the selected game on the home screen.
/// Steam games use Steam achievements and friends (Steam Web API key); emulated games use RetroAchievements and the
/// users you follow there. Steam and RetroAchievements have no trophy grades, so grades come from rarity (Steam) or
/// points (RetroAchievements), and finishing every achievement counts as the platinum. Cached for 10 minutes per game.
/// </summary>
public static class GameProgress
{
    /// <summary>Grade: 0 platinum, 1 gold, 2 silver, 3 bronze.</summary>
    /// <summary>When is null for a locked trophy.</summary>
    public sealed record Trophy(string Name, string? Icon, int Grade, DateTime? When, string Description = "");
    public sealed record Friend(string Name, string? Avatar, int Percent, bool PlayingNow);
    /// <param name="All">Every trophy of the game, earned (newest first) then locked.</param>
    public sealed record Progress(int Percent, int[] Earned, Trophy? Recent, List<Friend> Friends, List<Trophy> All);

    static readonly Dictionary<string, (DateTime at, Progress? p)> _cache = new();

    public static bool Supported(Game g) =>
        g.Kind == LaunchKind.Steam && g.SteamAppId != null ? SteamActivity.Configured
        : g.Kind == LaunchKind.Emulator && RetroAchievements.Configured;

    /// <summary>Cached result, if any, without a network call.</summary>
    public static bool TryCached(Game g, out Progress? p)
    {
        p = null;
        if (!_cache.TryGetValue(g.Id, out var c) || DateTime.Now - c.at > TimeSpan.FromMinutes(10)) return false;
        p = c.p;
        return true;
    }

    public static async Task<Progress?> LoadAsync(Game g)
    {
        if (TryCached(g, out var cached)) return cached;
        Progress? p = null;
        try { p = g.Kind == LaunchKind.Steam ? await SteamAsync(g.SteamAppId!) : await RaAsync(g); }
        catch (Exception ex) { Log.Warn($"Trophies for {g.Title}: {ex.Message}"); }
        Log.Info($"Trophies for {g.Title}: {(p == null ? "none" : $"{p.Percent}%, {p.Friends.Count} friends")}");
        _cache[g.Id] = (DateTime.Now, p);
        return p;
    }

    public static int GradeFromRarity(double percent) => percent < 10 ? 1 : percent < 35 ? 2 : 3;
    public static int GradeFromPoints(int points) => points >= 25 ? 1 : points >= 10 ? 2 : 3;

    static int Pct(int got, int total) => total == 0 ? 0 : (int)Math.Round(100.0 * got / total);

    // ---------------- Steam ----------------
    static async Task<Progress?> SteamAsync(string app)
    {
        var key = Secrets.Get(Secrets.SteamWebApi)!;
        var me = AppSettings.Current.SteamId64!;
        var mine = await SteamAchieved(key, me, app);
        if (mine == null || mine.Count == 0) return null; // no achievements, or the game isn't on this account

        var schema = new Dictionary<string, (string name, string? icon, string? gray, string desc)>();
        using (var d = await SteamActivity.Get($"ISteamUserStats/GetSchemaForGame/v2/?key={key}&appid={app}&l=english"))
            if (d?.RootElement.GetProperty("game").TryGetProperty("availableGameStats", out var st) == true && st.TryGetProperty("achievements", out var achs))
                foreach (var a in achs.EnumerateArray())
                    schema[a.GetProperty("name").GetString()!] = (a.TryGetProperty("displayName", out var n) ? n.GetString()! : "", a.TryGetProperty("icon", out var i) ? i.GetString() : null,
                        a.TryGetProperty("icongray", out var ig) ? ig.GetString() : null, a.TryGetProperty("description", out var de) ? de.GetString() ?? "" : "");

        var rarity = new Dictionary<string, double>();
        using (var d = await SteamActivity.Get($"ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2/?gameid={app}"))
            if (d?.RootElement.TryGetProperty("achievementpercentages", out var ap) == true)
                foreach (var a in ap.GetProperty("achievements").EnumerateArray())
                {
                    var v = a.GetProperty("percent");
                    rarity[a.GetProperty("name").GetString()!] = v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.TryParse(v.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var x) ? x : 50;
                }

        var earned = new int[4];
        var got = mine.Where(m => m.Value.HasValue).ToList();
        foreach (var m in got) earned[GradeFromRarity(rarity.GetValueOrDefault(m.Key, 50))]++;
        if (got.Count == mine.Count) earned[0] = 1;
        Trophy? recent = got.OrderByDescending(m => m.Value).Select(m => new Trophy(
            schema.TryGetValue(m.Key, out var s) && s.name.Length > 0 ? s.name : m.Key, schema.GetValueOrDefault(m.Key).icon,
            GradeFromRarity(rarity.GetValueOrDefault(m.Key, 50)), m.Value!.Value)).FirstOrDefault();

        var list = mine.Select(m =>
        {
            var s = schema.GetValueOrDefault(m.Key);
            return new Trophy(s.name is { Length: > 0 } ? s.name : m.Key, m.Value.HasValue ? s.icon : s.gray ?? s.icon, GradeFromRarity(rarity.GetValueOrDefault(m.Key, 50)), m.Value, s.desc ?? "");
        }).OrderByDescending(t => t.When.HasValue).ThenByDescending(t => t.When).ToList();

        // Friends who have this game on a public profile; the ones playing it now first. Four requests at a time.
        var all = await SteamActivity.FriendsAsync();
        var playing = all.Where(f => f.GameId == app).ToList();
        var gate = new SemaphoreSlim(4);
        var friends = (await Task.WhenAll(playing.Concat(all.Except(playing)).Take(40).Select(async f =>
        {
            await gate.WaitAsync();
            try
            {
                var a = await SteamAchieved(key, f.SteamId, app);
                return a == null ? null : new Friend(f.Name, f.Avatar, Pct(a.Count(x => x.Value.HasValue), a.Count), f.GameId == app);
            }
            finally { gate.Release(); }
        }))).OfType<Friend>().OrderByDescending(f => f.PlayingNow).ThenByDescending(f => f.Percent).Take(3).ToList();

        return new Progress(Pct(got.Count, mine.Count), earned, recent, friends, list);
    }

    /// <summary>apiname -> unlock time (null when locked); null when the profile is private or doesn't own the game.</summary>
    static async Task<Dictionary<string, DateTime?>?> SteamAchieved(string key, string id, string app)
    {
        using var d = await SteamActivity.Get($"ISteamUserStats/GetPlayerAchievements/v1/?key={key}&steamid={id}&appid={app}");
        if (d == null || !d.RootElement.GetProperty("playerstats").TryGetProperty("achievements", out var achs)) return null;
        return achs.EnumerateArray().ToDictionary(a => a.GetProperty("apiname").GetString()!,
            a => a.GetProperty("achieved").GetInt32() == 1 ? DateTimeOffset.FromUnixTimeSeconds(a.GetProperty("unlocktime").GetInt64()).LocalDateTime : (DateTime?)null);
    }

    // ---------------- RetroAchievements ----------------
    static List<(int id, string title)>? _raGames;

    public static string Normalize(string s) => new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    static async Task<Progress?> RaAsync(Game g)
    {
        var user = Secrets.Get(Secrets.RetroAchievementsUser)!;
        // Your RetroAchievements games, matched to the library by title ("Shadow Hearts" == "Shadow Hearts").
        if (_raGames == null)
        {
            var list = new List<(int, string)>();
            using var d = await RetroAchievements.Get("API_GetUserCompletionProgress", $"u={Uri.EscapeDataString(user)}&c=500");
            if (d?.RootElement.TryGetProperty("Results", out var res) == true)
                foreach (var r in res.EnumerateArray()) list.Add((r.GetProperty("GameID").GetInt32(), r.GetProperty("Title").GetString() ?? ""));
            _raGames = list;
        }
        var title = Normalize(LibraryService.DisplayTitle(g));
        var match = _raGames.FirstOrDefault(x => Normalize(x.title) == title);
        if (match.id == 0) match = _raGames.FirstOrDefault(x => Normalize(x.title).StartsWith(title) || title.StartsWith(Normalize(x.title)));
        if (match.id == 0) return null;

        var mine = await RaGame(match.id, user);
        if (mine == null || mine.Count == 0) return null;
        var earned = new int[4];
        var got = mine.Where(a => a.when.HasValue).ToList();
        foreach (var a in got) earned[GradeFromPoints(a.points)]++;
        if (got.Count == mine.Count) earned[0] = 1;
        var r0 = got.OrderByDescending(a => a.when).FirstOrDefault();
        Trophy? recent = r0.name == null ? null : new Trophy(r0.name, r0.badge, GradeFromPoints(r0.points), r0.when!.Value, r0.desc);
        var all = mine.Select(a => new Trophy(a.name, a.when.HasValue ? a.badge : a.badge.Replace(".png", "_lock.png"), GradeFromPoints(a.points), a.when, a.desc))
            .OrderByDescending(t => t.When.HasValue).ThenByDescending(t => t.When).ToList();

        var friends = new List<Friend>();
        using (var d = await RetroAchievements.Get("API_GetUsersIFollow", "c=20"))
            if (d?.RootElement.TryGetProperty("Results", out var res) == true)
                foreach (var f in res.EnumerateArray().Take(10))
                {
                    var name = f.GetProperty("User").GetString()!;
                    var theirs = await RaGame(match.id, name);
                    int n = theirs?.Count(a => a.when.HasValue) ?? 0;
                    if (n > 0) friends.Add(new Friend(name, $"https://media.retroachievements.org/UserPic/{Uri.EscapeDataString(name)}.png", Pct(n, theirs!.Count), false));
                }
        return new Progress(Pct(got.Count, mine.Count), earned, recent, friends.OrderByDescending(f => f.Percent).Take(3).ToList(), all);
    }

    static async Task<List<(string name, int points, string badge, DateTime? when, string desc)>?> RaGame(int game, string user)
    {
        using var d = await RetroAchievements.Get("API_GetGameInfoAndUserProgress", $"g={game}&u={Uri.EscapeDataString(user)}");
        if (d == null || !d.RootElement.TryGetProperty("Achievements", out var achs) || achs.ValueKind != JsonValueKind.Object) return null;
        var list = new List<(string, int, string, DateTime?, string)>();
        foreach (var p in achs.EnumerateObject())
        {
            var a = p.Value;
            string S(string k) => a.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
            int pts = a.TryGetProperty("Points", out var pv) && pv.ValueKind == JsonValueKind.Number ? pv.GetInt32() : 0;
            var date = S("DateEarnedHardcore").Length > 0 ? S("DateEarnedHardcore") : S("DateEarned");
            list.Add((S("Title"), pts, $"https://media.retroachievements.org/Badge/{S("BadgeName")}.png", DateTime.TryParse(date, out var w) ? w : null, S("Description")));
        }
        return list;
    }
}
