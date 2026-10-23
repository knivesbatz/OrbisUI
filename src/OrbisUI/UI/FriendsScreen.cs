using OrbisUI.Input;
using OrbisUI.Library;
using OrbisUI.Services;

namespace OrbisUI.UI;

/// <summary>
/// Friends and Messages: your Steam friends with who's online and what they're playing (Steam Web API).
/// Cross opens a chat with that friend in Steam. Without a Web API key both open Steam's own friends/chat.
/// </summary>
public static class FriendsScreen
{
    public static void Open(bool messages)
    {
        if (!SteamActivity.Configured) { HomeScreen.SteamHandoff(messages ? Steam.Links.Chat : Steam.Links.Friends, messages ? "Messages" : "Friends"); return; }
        var title = messages ? "Messages" : "Friends";
        Shell.Toast(title, "Loading your Steam friends…", "");
        _ = Task.Run(async () =>
        {
            var friends = await SteamActivity.FriendsAsync();
            Shell.Window.Dispatcher.Invoke(() =>
            {
                var online = friends.Count(f => f.State > 0);
                var items = friends.Select(f => new ListItem
                {
                    Glyph = f.Game != null ? "" : f.State > 0 ? "" : "",
                    Label = f.Name, Sub = () => SteamActivity.StateText(f), Value = () => f.State > 0 ? "●" : null,
                    Activate = () => Steam.OpenUrl("steam://friends/message/" + f.SteamId),
                }).ToList();
                items.Add(new ListItem { Glyph = "", Label = "Open Steam Friends", Sub = () => "Add friends, groups and the full chat", Activate = () => Steam.OpenUrl(Steam.Links.Friends) });
                Shell.Push(new ListScreen(title, items, subtitle: friends.Count == 0 ? "Steam didn't share a friends list. In Steam, set Profile > Privacy > Friends List to Public." : $"{online} online · {friends.Count} friends",
                    hints: new[] { (Btn.Cross, "Send Message"), (Btn.Circle, "Back") }));
            });
        });
    }
}
