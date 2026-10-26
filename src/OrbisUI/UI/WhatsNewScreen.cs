using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OrbisUI.Audio;
using OrbisUI.Input;
using OrbisUI.Library;
using OrbisUI.Services;

namespace OrbisUI.UI;

/// <summary>The console's What's New: a scrolling feed of activity cards, here from you and your Steam friends.</summary>
public sealed class WhatsNewScreen : Screen
{
    const double CardW = 1240, CardH = 180, Pitch = 200, Left = 340, Top = 150;
    readonly Canvas _feed = new();
    readonly TranslateTransform _scroll = new();
    readonly Border _focus = UiKit.FocusFrame(3);
    readonly TextBlock _status = UiKit.Label("Loading activity…", 26, UiKit.Dim);
    List<SteamActivity.Item> _items = new();
    int _sel;

    public WhatsNewScreen()
    {
        Chrome("What's New", (Btn.Cross, "View"), (Btn.Circle, "Back"), (Btn.Square, "Refresh"));
        _feed.RenderTransform = _scroll;
        Children.Add(new Border { ClipToBounds = true, Margin = new Thickness(0, 120, 0, 92), Child = _feed });
        _status.Margin = new Thickness(Left, Top, 0, 0); _status.VerticalAlignment = VerticalAlignment.Top;
        Children.Add(_status);
        _ = Load(false);
    }

    async Task Load(bool force)
    {
        _status.Text = "Loading activity…";
        var online = SteamActivity.Configured ? await SteamActivity.LoadAsync(force) : new();
        // Steam's feed already has your Steam plays; local history adds emulated and other games.
        _items = online.Concat(SteamActivity.Local().Where(l => l.AppId == null || !online.Any(o => o.Who == "You" && o.AppId == l.AppId)))
            .OrderBy(i => i.Rank == 3 ? 2 : i.Rank).ThenByDescending(i => i.When ?? DateTime.MinValue).ToList();
        _status.Text = _items.Count == 0 ? "No recent activity yet." : "";
        if (!SteamActivity.Configured) _items.Add(new SteamActivity.Item("Friends", null, "Add a Steam Web API key in Settings > Accounts and Services to see what your friends are playing.", null, null, 9));
        _sel = 0;
        Render();
    }

    void Render()
    {
        _feed.Children.Clear();
        for (int i = 0; i < _items.Count; i++)
        {
            var card = Card(_items[i]);
            Canvas.SetLeft(card, Left); Canvas.SetTop(card, Top - 120 + i * Pitch);
            _feed.Children.Add(card);
        }
        Place();
    }

    static FrameworkElement Card(SteamActivity.Item it)
    {
        var g = new Grid { Width = CardW, Height = CardH, Background = UiKit.B("#33FFFFFF") };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CardH) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CardH * 460 / 215) });
        if (Web(it.Avatar, 184) is ImageSource av) g.Children.Add(new Image { Source = av, Stretch = Stretch.UniformToFill });
        var text = new StackPanel { Margin = new Thickness(26, 22, 20, 0) };
        text.Children.Add(UiKit.Label(it.Who, 30));
        var body = UiKit.Label(it.Text, 24, UiKit.Dim); body.TextWrapping = TextWrapping.Wrap; body.MaxHeight = 64;
        text.Children.Add(body);
        if (it.When is DateTime w) text.Children.Add(UiKit.Label(Ago(w), 20, UiKit.Dim));
        Grid.SetColumn(text, 1); g.Children.Add(text);
        if (Web(it.AppId != null ? SteamActivity.HeaderImage(it.AppId) : it.Art, 400) is ImageSource hdr)
        {
            var img = new Image { Source = hdr, Stretch = Stretch.UniformToFill };
            Grid.SetColumn(img, 2); g.Children.Add(img);
        }
        return g;
    }

    static ImageSource? Web(string? url, int decode)
    {
        if (url == null) return null;
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit(); bi.UriSource = new Uri(url); bi.DecodePixelWidth = decode; bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit();
            return bi;
        }
        catch { return null; }
    }

    static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        return d.TotalMinutes < 2 ? "Now" : d.TotalHours < 1 ? $"{(int)d.TotalMinutes} minutes ago" : d.TotalDays < 1 ? $"{(int)d.TotalHours} hours ago" : $"{(int)d.TotalDays} days ago";
    }

    void Place()
    {
        _feed.Children.Remove(_focus);
        if (_items.Count == 0) return;
        double first = Math.Max(0, _sel - 2) * Pitch;
        UiKit.Animate(_scroll, TranslateTransform.YProperty, -first, 180);
        _focus.Width = CardW + 6; _focus.Height = CardH + 6;
        Canvas.SetLeft(_focus, Left - 3); Canvas.SetTop(_focus, Top - 123 + _sel * Pitch);
        _feed.Children.Add(_focus);
    }

    public override bool OnInput(Btn b)
    {
        switch (b)
        {
            case Btn.Up when _sel > 0: _sel--; Play(Sfx.Cursor); Place(); break;
            case Btn.Down when _sel < _items.Count - 1: _sel++; Play(Sfx.Cursor); Place(); break;
            case Btn.Square: Play(Sfx.Option); _ = Load(true); break;
            case Btn.Cross when _items.Count > 0 && _items[_sel].AppId is string app: Play(Sfx.Decide); _ = Steam.OpenInBigPicture(Steam.Links.StorePage(app)); break;
            case Btn.Circle: Play(Sfx.Back); Shell.Pop(); break;
        }
        return true;
    }
}
