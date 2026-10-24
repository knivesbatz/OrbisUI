using System.Text.Json;
using OrbisUI.Core;

namespace OrbisUI.Services;

/// <summary>Your RetroAchievements trophies (Web API key + user name from Secrets). Read-only; emulators sign in on their own.</summary>
public static class RetroAchievements
{
    public sealed record Trophy(string Title, string Description, string Game, string Console, int Points, bool Hardcore, DateTime When);

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20), DefaultRequestHeaders = { { "User-Agent", "OrbisUI" } } };

    public static bool Configured => Secrets.Has(Secrets.RetroAchievementsKey) && Secrets.Has(Secrets.RetroAchievementsUser);

    static async Task<JsonDocument?> Get(string api, string query)
    {
        var url = $"https://retroachievements.org/API/{api}.php?y={Uri.EscapeDataString(Secrets.Get(Secrets.RetroAchievementsKey)!)}&{query}";
        try
        {
            using var r = await Http.GetAsync(url);
            if (!r.IsSuccessStatusCode) { Log.Warn($"RetroAchievements {api}: {(int)r.StatusCode}"); return null; }
            return JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        }
        catch (Exception ex) { Log.Warn($"RetroAchievements {api}: {ex.Message}"); return null; } // never log the url: it holds the key
    }

    /// <summary>Total points and the trophies earned in the last 90 days, newest first.</summary>
    public static async Task<(int points, List<Trophy> recent)> LoadAsync()
    {
        var user = Uri.EscapeDataString(Secrets.Get(Secrets.RetroAchievementsUser)!);
        int points = 0;
        using (var p = await Get("API_GetUserProfile", "u=" + user))
            if (p?.RootElement.TryGetProperty("TotalPoints", out var tp) == true && tp.ValueKind == JsonValueKind.Number) points = tp.GetInt32();
        var list = new List<Trophy>();
        using (var d = await Get("API_GetUserRecentAchievements", $"u={user}&m={90 * 24 * 60}"))
            if (d?.RootElement.ValueKind == JsonValueKind.Array)
                foreach (var a in d.RootElement.EnumerateArray())
                {
                    string S(string k) => a.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
                    int pts = a.TryGetProperty("Points", out var pv) && pv.ValueKind == JsonValueKind.Number ? pv.GetInt32() : 0;
                    bool hc = a.TryGetProperty("HardcoreMode", out var h) && (h.ValueKind == JsonValueKind.True || h.ValueKind == JsonValueKind.Number && h.GetInt32() == 1);
                    DateTime.TryParse(S("Date"), out var when);
                    list.Add(new Trophy(S("Title"), S("Description"), S("GameTitle"), S("ConsoleName"), pts, hc, when));
                }
        return (points, list.OrderByDescending(t => t.When).ToList());
    }
}
