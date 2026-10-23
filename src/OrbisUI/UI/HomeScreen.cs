using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using System.Windows.Threading;
using OrbisUI.Audio;
using OrbisUI.Core;
using OrbisUI.Input;
using OrbisUI.Library;
using OrbisUI.Services;

namespace OrbisUI.UI;

/// <summary>
/// The home screen: info bar, the content row of square tiles with the focused one enlarged, its title beside it,
/// and the function area above (Up) with Store, Notifications, Friends, Messages, Profile, Trophies, Settings and Power.
/// </summary>
public sealed class HomeScreen : Screen
{
    // Geometry measured from the reference walkthrough and the user's screenshot (1920x1080).
    const double FnCell = 104, FnY = 500, BarY = 119, FnShift = 640;
    const double RowY = 232, Small = 202, Big = 346, Gap = 6, FocusX = 269, SmallY = 238, StartH = 81;

    readonly Canvas _row = new() { Width = UiKit.W, Height = 700 };
    readonly TranslateTransform _rowShift = new();
    readonly Dictionary<int, (FrameworkElement el, TranslateTransform t, ScaleTransform s)> _tiles = new();
    readonly TextBlock _title = UiKit.Label("", 54);
    readonly TextBlock _sub = UiKit.Label("", 24, UiKit.Dim);
    readonly TextBlock _desc = new() { FontSize = 22, Foreground = UiKit.Dim, FontFamily = UiKit.Font, TextWrapping = TextWrapping.Wrap, MaxHeight = 92, TextTrimming = TextTrimming.WordEllipsis, Width = 980 };
    readonly Border _startBar = new();
    readonly TextBlock _startText = UiKit.Label("Start", 26);
    readonly Border _focus = UiKit.FocusFrame(3);
    readonly Canvas _info = new() { Width = UiKit.W, Height = 200, IsHitTestVisible = false };
    readonly Canvas _statusIcons = new();
    readonly TextBlock _clock = UiKit.Label("", 26);
    readonly TextBlock _user = UiKit.Label("", 24);
    readonly Image _avatar = new() { Width = 40, Height = 40 };
    readonly TextBlock _noticeCount = UiKit.Label("", 18);
    readonly StackPanel _fnBar = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    readonly TextBlock _fnLabel = UiKit.Label("", 26);
    readonly Grid _fnLayer = new() { IsHitTestVisible = false };
    readonly Border _fnFocus = UiKit.FocusFrame(2);
    readonly DispatcherTimer _clockTimer;

    List<HomeItem> _items = new();
    int _sel;
    bool _fnMode;
    int _fnSel = 6;

    record Fn(string Name, string Glyph, Action Go, Geometry? Shape = null);
    static readonly Dictionary<string, string> FnIcon = new()
    {
        ["PlayStation Store"] = "store", ["Notifications"] = "notifications", ["Friends"] = "friends", ["Messages"] = "messages",
        ["Trophies"] = "trophies", ["Settings"] = "settings", ["Power"] = "power",
    };
    readonly List<Fn> _fns;

    public HomeScreen()
    {
        _row.RenderTransform = _rowShift;
        _row.VerticalAlignment = VerticalAlignment.Top; // row coordinates are screen coordinates
        Children.Add(_row);
        _row.Children.Add(_startBar);
        _row.Children.Add(_focus);

        // Darker band behind the content row across the whole width, like the console.
        var band = new Border { Width = UiKit.W, Height = Small, Background = UiKit.B("#26000A40") };
        Canvas.SetTop(band, SmallY);
        _row.Children.Add(band);
        // Start strip under the focused tile.
        _startBar.Width = Big; _startBar.Height = StartH;
        _startBar.Background = UiKit.B("#E02358C8");
        _startText.HorizontalAlignment = HorizontalAlignment.Center; _startText.VerticalAlignment = VerticalAlignment.Center;
        _startBar.Child = _startText;
        Canvas.SetLeft(_startBar, FocusX); Canvas.SetTop(_startBar, RowY + Big);
        _focus.Width = Big + 6; _focus.Height = Big + StartH + 6;
        Canvas.SetLeft(_focus, FocusX - 3); Canvas.SetTop(_focus, RowY - 3);

        var titleBlock = new StackPanel { Margin = new Thickness(FocusX + Big + 19, 0, 0, 0) };
        _title.MaxWidth = 1100;
        titleBlock.Children.Add(_title);
        titleBlock.Children.Add(_sub);
        _desc.Margin = new Thickness(0, 14, 0, 0);
        titleBlock.Children.Add(_desc);
        Canvas.SetTop(titleBlock, RowY + Big + StartH - 72); // the title's baseline sits on the Start bar
        Canvas.SetLeft(titleBlock, 0);
        _row.Children.Add(titleBlock);

        BuildInfoBar();

        _fns = new()
        {
            new("PlayStation Store", "", () => SteamHandoff(Steam.Links.Store, "Store")),
            new("Notifications", "", () => Shell.Push(new NotificationsScreen())),
            new("Friends", "", () => SteamHandoff(Steam.Links.Friends, "Friends")),
            new("Messages", "", () => SteamHandoff(Steam.Links.Chat, "Messages")),
            new("Profile", "", () => SteamHandoff(Steam.Links.Profile, "Profile")),
            new("Trophies", "", () => SteamHandoff(Steam.Links.Achievements, "Trophies"),
                Geometry.Parse("M14,4 H42 V14 C42,26 34,32 28,33 C22,32 14,26 14,14 Z M14,8 H6 C6,18 10,22 15,22 M42,8 H50 C50,18 46,22 41,22 M28,33 V42 M18,50 H38 L36,42 H20 Z")),
            new("Settings", "", () => Shell.Push(SettingsScreens.Root())),
            new("Power", "", PowerMenu.Show),
        };
        BuildFunctionArea();

        _clockTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        UpdateClock();

        Shell.Library.Changed += () => Dispatcher.BeginInvoke(Reload);
        Shell.Metadata.Updated += g => Dispatcher.BeginInvoke(() => RefreshGame(g));
        Notifications.Posted += _ => Dispatcher.BeginInvoke(UpdateNoticeCount);
        Reload();
    }

    void BuildInfoBar()
    {
        // The console's status bar: PS Plus, notifications, friends, you (online dot, avatar, name), trophies, clock.
        _info.VerticalAlignment = VerticalAlignment.Top;
        _info.HorizontalAlignment = HorizontalAlignment.Left;
        void At(UIElement el, double x, double h)
        {
            Canvas.SetLeft(el, x); Canvas.SetTop(el, BarY - h / 2);
            _statusIcons.Children.Add(el);
        }
        UIElement Icon(string key, string glyph, double size) =>
            IconPack.Get(key) is ImageSource img ? new Image { Source = img, Width = size, Height = size } : UiKit.Glyph(glyph, size * 0.8);

        // Yellow plus, drawn (the PS Plus mark).
        At(new Path { Data = Geometry.Parse("M10,0 H22 V10 H32 V22 H22 V32 H10 V22 H0 V10 H10 Z"), Fill = UiKit.B("#F5C518"), Width = 30, Height = 30, Stretch = Stretch.Uniform }, 96, 30);
        At(Icon("notifications", "\uEA8F", 34), 192, 34);
        _noticeCount.Foreground = UiKit.Text;
        At(_noticeCount, 230, 46);
        At(Icon("friends", "\uE716", 36), 730, 36);
        At(new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, Fill = UiKit.B("#3FA9FF") }, 800, 10);
        _avatar.Clip = new RectangleGeometry(new Rect(0, 0, 40, 40), 3, 3);
        At(_avatar, 818, 40);
        _user.VerticalAlignment = VerticalAlignment.Center;
        At(_user, 870, 34);
        At(Icon("trophies", "\uE734", 36), 1306, 36);
        _info.Children.Add(_statusIcons);
        // Right-aligned so the clock ends at 95% of the width whatever the time is.
        var clockBox = new Border { Width = 300, Child = _clock };
        _clock.HorizontalAlignment = HorizontalAlignment.Right;
        Canvas.SetLeft(clockBox, UiKit.W * 0.95 - 300); Canvas.SetTop(clockBox, BarY - 18);
        _info.Children.Add(clockBox);
        Children.Add(_info);
        LoadUser();
    }

    void LoadUser()
    {
        var steam = Steam.FindInstall();
        if (steam == null) { _user.Text = Environment.UserName; return; }
        var (id, persona, _) = Steam.CurrentUser(steam);
        _user.Text = persona ?? Environment.UserName;
        if (id != null)
        {
            AppSettings.Current.SteamId64 ??= id;
            var av = System.IO.Path.Combine(steam, "config", "avatarcache", id + ".png");
            _avatar.Source = Metadata.ImageTools.LoadForDisplay(av, 64);
        }
        if (_avatar.Source == null) _avatar.Visibility = Visibility.Collapsed;
    }

    void BuildFunctionArea()
    {
        for (int i = 0; i < _fns.Count; i++)
        {
            var f = _fns[i];
            var cell = new Grid { Width = FnCell, Height = 84 };
            var icon = f.Name == "Profile" ? _avatar.Source : IconPack.Get(FnIcon.GetValueOrDefault(f.Name, ""));
            if (icon != null)
                cell.Children.Add(new Image { Source = icon, Width = f.Name == "Profile" ? 40 : 46, Height = f.Name == "Profile" ? 40 : 46, Stretch = Stretch.Uniform });
            else if (f.Shape != null)
                cell.Children.Add(new Path { Data = f.Shape, Stroke = UiKit.Text, StrokeThickness = 3, Width = 44, Height = 44, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            else
            {
                var gl = UiKit.Glyph(f.Glyph, 36);
                gl.HorizontalAlignment = HorizontalAlignment.Center;
                cell.Children.Add(gl);
            }
            _fnBar.Children.Add(cell);
        }
        _fnBar.VerticalAlignment = VerticalAlignment.Top;
        _fnBar.Margin = new Thickness(0, FnY, 0, 0);
        _fnBar.Opacity = 0; // shown when Up moves into the function area
        _fnLayer.Children.Add(_fnBar);
        _fnFocus.Width = FnCell; _fnFocus.Height = 84; _fnFocus.Visibility = Visibility.Hidden; _fnLabel.Visibility = Visibility.Hidden;
        _fnFocus.HorizontalAlignment = HorizontalAlignment.Left; _fnFocus.VerticalAlignment = VerticalAlignment.Top;
        _fnLayer.Children.Add(_fnFocus);
        _fnLabel.HorizontalAlignment = HorizontalAlignment.Left; _fnLabel.VerticalAlignment = VerticalAlignment.Top;
        _fnLayer.Children.Add(_fnLabel);
        Children.Add(_fnLayer);
    }

    void UpdateClock()
    {
        _clock.Text = DateTime.Now.ToString("h:mm tt");
        // Sleep until the next minute instead of ticking every second.
        _clockTimer.Interval = TimeSpan.FromSeconds(61 - DateTime.Now.Second);
    }

    void UpdateNoticeCount() => _noticeCount.Text = Notifications.Unread > 0 ? Notifications.Unread.ToString() : "";

    public override void OnShown()
    {
        _clockTimer.Start();
        UpdateClock();
        UpdateNoticeCount();
        Shell.Window.Background_.SetRunning(true);
    }

    public override void OnHidden() => _clockTimer.Stop();

    public override void OnResumed()
    {
        _clockTimer.Start();
        UpdateClock();
        UpdateNoticeCount();
        ApplyBackground();
    }

    public void Reload()
    {
        var keepId = _items.Count > 0 ? _items[_sel].Id : AppSettings.Current.LastSelectedId;
        var list = Shell.Library.Visible.Select(g => new HomeItem { Kind = HomeKind.Game, Game = g, Title = LibraryService.DisplayTitle(g) }).ToList();
        foreach (var f in AppSettings.Current.Folders)
            list.Add(new HomeItem { Kind = HomeKind.Folder, Folder = f, Title = f.Name, Glyph = "", IconKey = "tv" });
        list.Add(new HomeItem { Kind = HomeKind.Library, Title = "Library", Glyph = "", IconKey = "library" });
        _items = list;
        int idx = keepId == null ? 0 : _items.FindIndex(i => i.Id == keepId);
        _sel = Math.Clamp(idx < 0 ? 0 : idx, 0, _items.Count - 1);
        foreach (var t in _tiles.Values) _row.Children.Remove(t.el);
        _tiles.Clear();
        Layout(false);
    }

    void RefreshGame(Game g)
    {
        int i = _items.FindIndex(x => x.Game?.Id == g.Id);
        if (i < 0) return;
        if (_tiles.Remove(i, out var t)) _row.Children.Remove(t.el);
        Layout(false);
    }

    double TargetX(int i) => i == _sel ? FocusX
        : i < _sel ? FocusX - (_sel - i) * (Small + Gap)
        : FocusX + (_fnMode ? Small : Big + 3) + Gap + (i - _sel - 1) * (Small + Gap);

    void Layout(bool animate)
    {
        // Only tiles near the screen exist, so huge libraries stay cheap.
        int lo = Math.Max(0, _sel - 2), hi = Math.Min(_items.Count - 1, _sel + 8);
        foreach (var k in _tiles.Keys.Where(k => k < lo || k > hi).ToList())
        {
            _row.Children.Remove(_tiles[k].el);
            _tiles.Remove(k);
        }
        for (int i = lo; i <= hi; i++)
        {
            bool isSel = i == _sel && !_fnMode;
            double x = TargetX(i), y = isSel ? RowY : SmallY, sc = isSel ? Big / Small : 1;
            if (!_tiles.TryGetValue(i, out var t))
            {
                var el = Tiles.Make(_items[i], Small);
                var s = new ScaleTransform(sc, sc);
                var tr = new TranslateTransform(animate ? x + (i > _sel ? 80 : -80) : x, y);
                var grp = new TransformGroup(); grp.Children.Add(s); grp.Children.Add(tr);
                el.RenderTransform = grp;
                Panel.SetZIndex(el, 1);
                _row.Children.Insert(0, el);
                t = (el, tr, s);
                _tiles[i] = t;
                if (animate) UiKit.FadeIn(el, 200);
            }
            Panel.SetZIndex(t.el, isSel ? 2 : 1);
            if (animate)
            {
                UiKit.Animate(t.t, TranslateTransform.XProperty, x, 170);
                UiKit.Animate(t.t, TranslateTransform.YProperty, y, 170);
                UiKit.Animate(t.s, ScaleTransform.ScaleXProperty, sc, 170);
                UiKit.Animate(t.s, ScaleTransform.ScaleYProperty, sc, 170);
            }
            else
            {
                t.t.BeginAnimation(TranslateTransform.XProperty, null); t.t.X = x;
                t.t.BeginAnimation(TranslateTransform.YProperty, null); t.t.Y = y;
                t.s.BeginAnimation(ScaleTransform.ScaleXProperty, null); t.s.ScaleX = sc;
                t.s.BeginAnimation(ScaleTransform.ScaleYProperty, null); t.s.ScaleY = sc;
            }
        }
        Panel.SetZIndex(_focus, 3);
        UpdateDetails();
    }

    void UpdateDetails()
    {
        if (_items.Count == 0) return;
        var it = _items[_sel];
        _title.Text = it.Title;
        _startText.Text = it.Kind == HomeKind.Game ? "Start" : "Open";
        if (it.Game is Game g)
        {
            var parts = new List<string> { Tiles.PlatformLabel(g.Platform) };
            if (!string.IsNullOrEmpty(g.Year)) parts.Add(g.Year!);
            if (!string.IsNullOrEmpty(g.Publisher)) parts.Add(g.Publisher!);
            _sub.Text = string.Join("   ·   ", parts.Where(p => p.Length > 0));
            _desc.Text = g.Description ?? "";
        }
        else
        {
            _sub.Text = it.Kind switch
            {
                HomeKind.Folder => $"{it.Folder!.Apps.Count} items",
                HomeKind.Library => $"{Shell.Library.Games.Count} games and apps",
                _ => "",
            };
            _desc.Text = "";
        }
        AppSettings.Current.LastSelectedId = it.Id;
        BuildSubRow();
        ApplyBackground();
    }

    // ---- Folder contents row (TV & Video): visible under a focused folder, entered with Down ----
    const double SubY = 690, SubTile = 255, SubGap = 60;
    readonly Canvas _subRow = new();
    readonly Border _subFocus = UiKit.FocusFrame(3);
    int _subSel = -1;

    void BuildSubRow()
    {
        if (_subRow.Parent == null) { _row.Children.Add(_subRow); Panel.SetZIndex(_subRow, 4); }
        _subRow.Children.Clear();
        _subSel = -1;
        _focus.Visibility = _fnMode ? Visibility.Hidden : Visibility.Visible;
        if (_items.Count == 0 || _items[_sel].Folder is not AppFolder f) return;
        for (int i = 0; i < f.Apps.Count; i++)
        {
            var a = f.Apps[i];
            var tile = Tiles.Make(new HomeItem { Kind = HomeKind.App, App = a, Title = a.Name, Glyph = a.Target.Contains("://") ? "\uE774" : "\uE7F4" }, SubTile);
            var cell = new StackPanel { Width = SubTile };
            cell.Children.Add(tile);
            var name = UiKit.Label(a.Name, 22);
            var strip = new Border { Background = UiKit.B("#C0061A55"), Padding = new Thickness(10, 6, 10, 6), Child = name };
            cell.Children.Add(strip);
            Canvas.SetLeft(cell, FocusX + i * (SubTile + SubGap));
            Canvas.SetTop(cell, SubY);
            _subRow.Children.Add(cell);
            UiKit.FadeIn(cell, 200);
        }
        if (f.Apps.Count == 0)
        {
            var hint = UiKit.Label("Add applications in Settings > TV & Video.", 24, UiKit.Dim);
            Canvas.SetLeft(hint, FocusX); Canvas.SetTop(hint, SubY);
            _subRow.Children.Add(hint);
        }
    }

    void PlaceSub()
    {
        _subRow.Children.Remove(_subFocus);
        if (_subSel < 0) { _focus.Visibility = Visibility.Visible; _startBar.Visibility = Visibility.Visible; return; }
        _focus.Visibility = Visibility.Hidden;
        _subFocus.Width = SubTile + 6; _subFocus.Height = SubTile + 46;
        Canvas.SetLeft(_subFocus, FocusX + _subSel * (SubTile + SubGap) - 3);
        Canvas.SetTop(_subFocus, SubY - 3);
        _subRow.Children.Add(_subFocus);
    }

    bool SubInput(Btn b)
    {
        var f = _items[_sel].Folder!;
        switch (b)
        {
            case Btn.Left when _subSel > 0: _subSel--; Play(Sfx.Cursor); PlaceSub(); return true;
            case Btn.Right when _subSel < f.Apps.Count - 1: _subSel++; Play(Sfx.Cursor); PlaceSub(); return true;
            case Btn.Up or Btn.Circle: _subSel = -1; Play(Sfx.Back); PlaceSub(); return true;
            case Btn.Cross: Shell.Window.StartApp(f.Apps[_subSel]); return true;
            case Btn.Options:
                var app = f.Apps[_subSel];
                Play(Sfx.Option);
                Shell.Push(FolderScreen.AppOptions(f, app, () => { _subSel = Math.Min(_subSel, f.Apps.Count - 1); BuildSubRow(); }));
                return true;
        }
        return true;
    }

    DispatcherTimer? _bgTimer;

    /// <summary>Background art fades in only after the selection rests on a game for 5 s, and only SteamGridDB heroes are used.</summary>
    void ApplyBackground()
    {
        var it = _items.Count > 0 ? _items[_sel] : null;
        var g = it?.Game;
        var bg = !_fnMode && g?.BgSource == "SteamGridDB" ? g.Background : null;
        _bgTimer?.Stop();
        Shell.Window.Background_.SetArt(null);
        if (bg == null) return;
        _bgTimer ??= new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(5) };
        _bgTimer.Tick -= OnBgTimer;
        _bgPending = bg;
        _bgTimer.Tick += OnBgTimer;
        _bgTimer.Start();
    }

    string? _bgPending;
    void OnBgTimer(object? s, EventArgs e)
    {
        _bgTimer!.Stop();
        if (Shell.Top == this && !_fnMode) Shell.Window.Background_.SetArt(_bgPending);
    }

    void Move(int d)
    {
        int n = Math.Clamp(_sel + d, 0, _items.Count - 1);
        if (n == _sel) return;
        _sel = n;
        Play(Sfx.Cursor);
        Layout(true);
    }

    void SetFnMode(bool on)
    {
        if (_fnMode == on) return;
        _fnMode = on;
        Play(on ? Sfx.Enter : Sfx.Back);
        // Like the console: the status icons give way to the function row and the content row slides to the bottom, all small.
        UiKit.Animate(_rowShift, TranslateTransform.YProperty, on ? FnShift : 0, 220);
        UiKit.Animate(_row, OpacityProperty, on ? 0.7 : 1, 200);
        UiKit.Animate(_fnBar, OpacityProperty, on ? 1 : 0, 200);
        UiKit.Animate(_statusIcons, OpacityProperty, on ? 0 : 1, 200);
        Layout(true);
        _fnFocus.Visibility = _fnLabel.Visibility = on ? Visibility.Visible : Visibility.Hidden;
        _focus.Visibility = on ? Visibility.Hidden : Visibility.Visible;
        _startBar.Visibility = _focus.Visibility;
        PlaceFn();
        ApplyBackground();
    }

    void PlaceFn()
    {
        double total = _fns.Count * FnCell;
        double x0 = (UiKit.W - total) / 2 + _fnSel * FnCell;
        _fnFocus.Margin = new Thickness(x0, FnY, 0, 0);
        _fnLabel.Text = _fns[_fnSel].Name;
        _fnLabel.Width = 400;
        _fnLabel.TextAlignment = TextAlignment.Center;
        _fnLabel.Margin = new Thickness(x0 + FnCell / 2 - 200, FnY + 88, 0, 0);
    }

    public override bool OnInput(Btn b)
    {
        if (_fnMode)
        {
            switch (b)
            {
                case Btn.Left when _fnSel > 0: _fnSel--; Play(Sfx.Cursor); PlaceFn(); return true;
                case Btn.Right when _fnSel < _fns.Count - 1: _fnSel++; Play(Sfx.Cursor); PlaceFn(); return true;
                case Btn.Down or Btn.Circle or Btn.PS: SetFnMode(false); return true;
                case Btn.Cross: Play(Sfx.Decide); _fns[_fnSel].Go(); return true;
            }
            return true;
        }
        if (_subSel >= 0) return SubInput(b);
        switch (b)
        {
            case Btn.Down when _items[_sel].Folder is { Apps.Count: > 0 }: _subSel = 0; Play(Sfx.Cursor); PlaceSub(); return true;
            case Btn.Left: Move(-1); return true;
            case Btn.Right: Move(1); return true;
            case Btn.L1: Move(-5); return true;
            case Btn.R1: Move(5); return true;
            case Btn.Up: SetFnMode(true); return true;
            case Btn.PS: if (_sel != 0) { _sel = 0; Play(Sfx.Back); Layout(true); } return true;
            case Btn.Cross: Activate(_items[_sel]); return true;
            case Btn.Options or Btn.Triangle when _items[_sel].Game is Game g:
                Play(Sfx.Option); GameMenu.Show(g); return true;
        }
        return false;
    }

    void Activate(HomeItem it)
    {
        switch (it.Kind)
        {
            case HomeKind.Game: Shell.Window.StartGame(it.Game!); break;
            case HomeKind.Folder when it.Folder!.Apps.Count > 0: _subSel = 0; Play(Sfx.Cursor); PlaceSub(); break;
            case HomeKind.Library: Play(Sfx.Enter); Shell.Push(new LibraryScreen()); break;
        }
    }

    static void SteamHandoff(string url, string what)
    {
        if (Steam.FindInstall() == null) { Shell.Message(what, "Steam isn't installed, so this isn't available yet. Install Steam and sign in, then try again."); return; }
        Steam.OpenUrl(url);
    }
}
