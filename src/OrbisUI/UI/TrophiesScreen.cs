using OrbisUI.Input;
using OrbisUI.Library;
using OrbisUI.Services;

namespace OrbisUI.UI;

/// <summary>Trophies: your RetroAchievements from emulated games, plus Steam achievements in Steam. Without a RetroAchievements key it opens Steam's.</summary>
public static class TrophiesScreen
{
    public static void Open()
    {
        if (!RetroAchievements.Configured) { HomeScreen.SteamHandoff(Steam.Links.Achievements, "Trophies"); return; }
        WaitScreen.Run(RetroAchievements.LoadAsync, r =>
        {
            var (points, recent) = r;
            {
                var items = recent.Select(t => new ListItem
                {
                    Glyph = t.Hardcore ? "" : "", Label = t.Title,
                    Sub = () => $"{t.Game} ({t.Console})  ·  {t.Description}", Value = () => $"{t.Points} pts   {t.When:MMM d}",
                }).ToList();
                if (items.Count == 0) items.Add(new ListItem { Glyph = "", Label = "No trophies in the last 90 days", Sub = () => "Sign in to RetroAchievements inside each emulator to earn them" });
                items.Add(new ListItem { Glyph = "", Label = "Steam Achievements", Sub = () => "Opens in Steam", Activate = () => HomeScreen.SteamHandoff(Steam.Links.Achievements, "Trophies") });
                Shell.Push(new ListScreen("Trophies", items, subtitle: $"RetroAchievements  ·  {points:N0} points  ·  {recent.Count} earned in the last 90 days",
                    hints: new[] { (Btn.Circle, "Back") }));
            }
        });
    }
    static readonly string[] Grades = { "Platinum", "Gold", "Silver", "Bronze" };

    /// <summary>One game's trophies: icon, name, description, grade, and the date earned or Locked.</summary>
    public static void ForGame(Game g, GameProgress.Progress p)
    {
        var items = p.All.Select(t => new ListItem
        {
            Image = t.Icon, Label = t.Name, Sub = () => t.Description,
            Value = () => Grades[t.Grade] + "   " + (t.When is DateTime w ? w.ToString("MMM d, yyyy") : "Locked"),
        });
        Shell.Push(new ListScreen(LibraryService.DisplayTitle(g), items, subtitle: $"Trophies  ·  {p.Percent}%  ·  {p.All.Count(t => t.When.HasValue)} of {p.All.Count} earned",
            hints: new[] { (Btn.Circle, "Back") }));
    }
}
