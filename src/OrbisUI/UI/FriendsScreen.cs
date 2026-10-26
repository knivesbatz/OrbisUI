using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OrbisUI.Audio;
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
        WaitScreen.Run(SteamActivity.FriendsAsync, friends =>
        {
            if (!messages) { Shell.Push(new FriendsPage(friends)); return; }
            var online = friends.Count(f => f.State > 0);
            var items = friends.Select(f => new ListItem
            {
                Glyph = f.Game != null ? "" : f.State > 0 ? "" : "",
                Label = f.Name, Sub = () => SteamActivity.StateText(f), Value = () => f.State > 0 ? "●" : null,
                Activate = () => _ = Steam.OpenInBigPicture("steam://friends/message/" + f.SteamId),
            }).ToList();
            items.Add(new ListItem { Glyph = "", Label = "Open Steam Friends", Sub = () => "Add friends, groups and the full chat", Activate = () => _ = Steam.OpenInBigPicture(Steam.Links.Friends) });
            Shell.Push(new ListScreen("Messages", items, subtitle: friends.Count == 0 ? NoList : $"{online} online · {friends.Count} friends",
                hints: new[] { (Btn.Cross, "Send Message"), (Btn.Circle, "Back") }));
        });
    }

    internal const string NoList = "Steam didn't share a friends list. In Steam, set Profile > Privacy > Friends List to Public.";
}

/// <summary>
/// The console's two-pane Friends page (menu tour 1:36): categories on the left, the friends in the focused category
/// on the right. Right moves into the list, Left or Circle back out.
/// </summary>
sealed class FriendsPage : Screen
{
    record Cat(string Name, string Glyph, Func<SteamActivity.Friend, bool>? Filter, bool RuleAfter = false, Action? Go = null);

    // Measured from the tour at 960x540, doubled: categories x 150-580 from y 180 at 84 px, friends from x 680.
    const double CatX = 136, CatW = 450, Top = 170, RowH = 84, ListX = 680, ListW = 1140, FRowH = 100;
    const int Visible = 7;

    readonly List<SteamActivity.Friend> _all;
    readonly List<Cat> _cats;
    readonly Canvas _left = new(), _right = new();
    readonly Border _focus = new() { BorderBrush = UiKit.B("#E6FFFFFF"), BorderThickness = new Thickness(2), IsHitTestVisible = false };
    List<SteamActivity.Friend> _shown = new();
    int _cat = 1, _sel, _off;
    bool _inList;

    public FriendsPage(List<SteamActivity.Friend> friends)
    {
        _all = friends;
        _cats = new()
        {
            new("Search", "", null, Go: () => _ = Steam.OpenInBigPicture(Steam.Links.Friends)),
            new("All Friends", "", _ => true),
            new("Online", "", f => f.State > 0, RuleAfter: true),
            new("Now Playing", "", f => f.Game != null, RuleAfter: true),
            new("Add Friends", "", null, Go: () => _ = Steam.OpenInBigPicture(Steam.Links.Friends)),
        };
        Chrome("Friends", (Btn.Cross, "Enter"), (Btn.Circle, "Back"));
        foreach (var c in new[] { _left, _right }) { c.HorizontalAlignment = HorizontalAlignment.Left; c.VerticalAlignment = VerticalAlignment.Top; Children.Add(c); }
        Render();
    }

    void Render()
    {
        var c = _cats[_cat];
        _shown = c.Filter == null ? new() : _all.Where(c.Filter).ToList();
        _left.Children.Clear();
        _right.Children.Clear();
        double y = Top;
        for (int i = 0; i < _cats.Count; i++)
        {
            var cat = _cats[i];
            bool big = i == _cat && !_inList;
            var g = UiKit.Glyph(cat.Glyph, big ? 40 : 32);
            Put(_left, g, CatX + 20, y + RowH / 2 - (big ? 26 : 21));
            var t = UiKit.Label(cat.Name, big ? 32 : 26, i == _cat ? UiKit.Text : UiKit.Dim);
            Put(_left, t, CatX + 82, y + RowH / 2 - (big ? 22 : 18));
            if (cat.Filter != null && cat.Name == "All Friends")
            {
                var n = UiKit.Label($"● {_all.Count(f => f.State > 0)}", 22, UiKit.B("#7FD0FF"));
                n.Width = 80; n.TextAlignment = TextAlignment.Right;
                Put(_left, n, CatX + CatW - 100, y + RowH / 2 - 15);
            }
            if (i == _cat && !_inList) PlaceFocus(_left, CatX, y + 6, CatW, RowH - 12);
            y += RowH;
            if (cat.RuleAfter) { Put(_left, new Border { Width = CatW - 30, Height = 1, Background = UiKit.Line }, CatX + 15, y + 4); y += 12; }
        }

        if (c.Filter == null) return;
        if (_shown.Count == 0)
        {
            var msg = UiKit.Label(_all.Count == 0 ? FriendsScreen.NoList : "No one here right now.", 26, UiKit.Dim);
            msg.MaxWidth = ListW; msg.TextWrapping = TextWrapping.Wrap;
            Put(_right, msg, ListX, Top + 20);
            return;
        }
        for (int i = _off; i < Math.Min(_shown.Count, _off + Visible); i++)
        {
            var f = _shown[i];
            double ry = Top + (i - _off) * FRowH;
            if (WhatsNewScreen.Web(f.Avatar, 128) is ImageSource av) Put(_right, new Image { Source = av, Width = 76, Height = 76 }, ListX + 12, ry + 12);
            var name = UiKit.Label(f.Name, 30);
            name.MaxWidth = ListW - 160;
            Put(_right, name, ListX + 110, ry + 14);
            var state = UiKit.Label(SteamActivity.StateText(f), 22, f.State > 0 ? UiKit.B("#7FD0FF") : UiKit.Dim);
            state.MaxWidth = ListW - 160;
            Put(_right, state, ListX + 110, ry + 54);
            Put(_right, new Border { Width = ListW, Height = 1, Background = UiKit.B("#40FFFFFF") }, ListX, ry + FRowH);
            if (_inList && i == _sel) PlaceFocus(_right, ListX, ry, ListW, FRowH);
        }
    }

    void PlaceFocus(Canvas c, double x, double y, double w, double h) { _focus.Width = w; _focus.Height = h; Put(c, _focus, x, y); }

    static void Put(Canvas c, UIElement el, double x, double y) { Canvas.SetLeft(el, x); Canvas.SetTop(el, y); c.Children.Add(el); }

    public override bool OnInput(Btn b)
    {
        if (_inList)
        {
            switch (b)
            {
                case Btn.Up when _sel > 0: _sel--; break;
                case Btn.Down when _sel < _shown.Count - 1: _sel++; break;
                case Btn.Left or Btn.Circle: _inList = false; Play(Sfx.Back); Render(); return true;
                case Btn.Cross: Play(Sfx.Decide); _ = Steam.OpenInBigPicture("steam://friends/message/" + _shown[_sel].SteamId); return true;
                default: return true;
            }
            _off = Math.Clamp(_off, _sel - Visible + 1, _sel);
            Play(Sfx.Cursor); Render();
            return true;
        }
        switch (b)
        {
            case Btn.Up when _cat > 0: _cat--; break;
            case Btn.Down when _cat < _cats.Count - 1: _cat++; break;
            case Btn.Right or Btn.Cross when _cats[_cat].Go is Action go: if (b == Btn.Cross) { Play(Sfx.Decide); go(); } return true;
            case Btn.Right or Btn.Cross when _shown.Count > 0: _inList = true; _sel = _off = 0; Play(Sfx.Cursor); Render(); return true;
            case Btn.Circle: Play(Sfx.Back); Shell.Pop(); return true;
            default: return true;
        }
        Play(Sfx.Cursor); Render();
        return true;
    }
}
