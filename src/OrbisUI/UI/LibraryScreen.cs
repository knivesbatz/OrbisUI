using System.Windows;
using System.Windows.Controls;
using OrbisUI.Audio;
using OrbisUI.Core;
using OrbisUI.Input;
using OrbisUI.Library;

namespace OrbisUI.UI;

/// <summary>
/// The console's Library: a filter column (Search, All, Games, Applications, one row per system) with counts, a sort box,
/// and a three-column grid of wide cells (square art, name beside it). Hidden games are shown dimmed so they can be brought back.
/// Layout measured from the console's Library screen, scaled to 1920x1080.
/// </summary>
public sealed class LibraryScreen : Screen
{
    const int Cols = 3, VisibleRows = 4;
    const double ColX = 96, ColW = 377, ColTop = 152, ColPitch = 92;
    const double GridX = 592, GridY = 240, CellW = 424, CellH = 208, PitchX = 435, PitchY = 216, GridBottom = 988;
    const double SortX = 1081, SortY = 124, SortW = 802, SortH = 70;

    sealed record Entry(string Title, Game? Game, AppEntry? App);
    sealed record Filter(string Name, string Glyph, Func<Entry, bool> Match);

    enum Zone { Filters, Sort, Grid }
    static readonly string[] Sorts = { "Name: A-Z", "Name: Z-A", "Recently Used" };

    readonly Canvas _stage = new();
    readonly Canvas _grid = new() { Margin = new Thickness(3, 3, 0, 0) };
    readonly Border _clip = new() { Width = UiKit.W - GridX, Height = GridBottom - GridY + 6, ClipToBounds = true };
    readonly Border _focus = UiKit.FocusFrame(3);
    readonly List<Filter> _filters = new();
    List<Entry> _all = new(), _shown = new();
    Zone _zone = Zone.Grid;
    int _filter = 1, _sel, _firstRow, _sort;
    string _query = "";

    public LibraryScreen()
    {
        Chrome("Library", (Btn.Cross, "Start"), (Btn.Circle, "Back"), (Btn.Options, "Options"), (Btn.Square, "Rescan"));
        _stage.VerticalAlignment = VerticalAlignment.Top; _stage.HorizontalAlignment = HorizontalAlignment.Left;
        Children.Add(_stage);
        _clip.Child = _grid;
        Load();
        Shell.Library.Changed += OnLibraryChanged;
    }

    void OnLibraryChanged() => Dispatcher.BeginInvoke(Load);
    public override void OnHidden() { }

    void Load()
    {
        _all = Shell.Library.Games.Select(g => new Entry(LibraryService.DisplayTitle(g), g, null))
            .Concat(AppSettings.Current.Folders.SelectMany(f => f.Apps).Select(a => new Entry(a.Name, null, a))).ToList();
        _filters.Clear();
        _filters.Add(new("Search", "", e => _query.Length > 0 && e.Title.Contains(_query, StringComparison.OrdinalIgnoreCase)));
        _filters.Add(new("All", "", _ => true));
        _filters.Add(new("Games", "", e => e.Game != null));
        _filters.Add(new("Applications", "", e => e.App != null));
        foreach (var p in _all.Where(e => e.Game != null).Select(e => Tiles.PlatformLabel(e.Game!.Platform)).Distinct().OrderBy(p => p))
            _filters.Add(new(p, "", e => e.Game != null && Tiles.PlatformLabel(e.Game.Platform) == p));
        _filter = Math.Clamp(_filter, 0, _filters.Count - 1);
        Apply();
    }

    void Apply()
    {
        var list = _all.Where(_filters[_filter].Match);
        var last = AppSettings.Current.LastPlayed;
        _shown = (_sort switch
        {
            1 => list.OrderByDescending(e => e.Title, StringComparer.OrdinalIgnoreCase),
            2 => list.OrderByDescending(e => e.Game != null ? last.GetValueOrDefault(e.Game.Id) : 0).ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase),
            _ => list.OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase),
        }).ToList();
        _sel = Math.Clamp(_sel, 0, Math.Max(0, _shown.Count - 1));
        if (_zone == Zone.Grid && _shown.Count == 0) _zone = Zone.Filters;
        Render();
    }

    void Put(Canvas c, UIElement el, double x, double y) { Canvas.SetLeft(el, x); Canvas.SetTop(el, y); c.Children.Add(el); }

    void Render()
    {
        _stage.Children.Clear();

        // Filter column with counts; a rule under Search like the console.
        for (int i = 0; i < _filters.Count; i++)
        {
            double y = ColTop + i * ColPitch + (i > 0 ? 10 : 0);
            Put(_stage, UiKit.Glyph(_filters[i].Glyph, 34), ColX + 36, y + 26);
            var label = UiKit.Label(i == 0 && _query.Length > 0 ? _query : _filters[i].Name, 28, i == _filter || i == 0 ? UiKit.Text : UiKit.Dim);
            label.MaxWidth = 230;
            Put(_stage, label, ColX + 96, y + 26);
            if (i > 0)
            {
                var n = UiKit.Label(_all.Count(_filters[i].Match).ToString(), 22);
                n.Width = 80; n.TextAlignment = TextAlignment.Right;
                Put(_stage, n, ColX + ColW - 100, y + 32);
            }
            else Put(_stage, new Border { Width = ColW - 56, Height = 1, Background = UiKit.Line }, ColX + 24, y + ColPitch - 4);
        }

        // Sort box.
        var sort = new Grid { Width = SortW, Height = SortH };
        sort.Children.Add(new Border { BorderBrush = UiKit.B("#CCFFFFFF"), BorderThickness = new Thickness(1.5), Background = UiKit.B("#22000A40") });
        var st = UiKit.Label(Sorts[_sort], 30); st.VerticalAlignment = VerticalAlignment.Center; st.Margin = new Thickness(22, 0, 0, 0);
        sort.Children.Add(st);
        var chev = UiKit.Glyph("", 22); chev.HorizontalAlignment = HorizontalAlignment.Right; chev.VerticalAlignment = VerticalAlignment.Center; chev.Margin = new Thickness(0, 0, 22, 0);
        sort.Children.Add(chev);
        Put(_stage, sort, SortX, SortY);

        // Grid of wide cells, clipped above the hint bar.
        _grid.Children.Clear();
        int row = _sel / Cols;
        if (row < _firstRow) _firstRow = row;
        if (row > _firstRow + VisibleRows - 2) _firstRow = row - (VisibleRows - 2);
        for (int i = _firstRow * Cols; i < Math.Min(_shown.Count, (_firstRow + VisibleRows) * Cols); i++)
        {
            var e = _shown[i];
            double x = (i % Cols) * PitchX, y = (i / Cols - _firstRow) * PitchY;
            var cell = new Grid { Width = CellW, Height = CellH, Background = UiKit.B("#552A62E0") };
            bool hidden = e.Game != null && AppSettings.Current.Games.TryGetValue(e.Game.Id, out var o) && o.Hidden;
            var tile = e.Game != null
                ? Tiles.Make(new HomeItem { Kind = HomeKind.Game, Game = e.Game, Title = e.Game.Title }, CellH)
                : Tiles.Make(new HomeItem { Kind = HomeKind.App, App = e.App, Title = e.Title, Glyph = "" }, CellH);
            tile.HorizontalAlignment = HorizontalAlignment.Left;
            cell.Children.Add(tile);
            var name = UiKit.Label(e.Title, 24);
            name.TextWrapping = TextWrapping.Wrap; name.TextTrimming = TextTrimming.WordEllipsis; name.MaxHeight = 98;
            name.Margin = new Thickness(CellH + 10, 10, 10, 0); name.VerticalAlignment = VerticalAlignment.Top;
            cell.Children.Add(name);
            cell.Opacity = hidden ? 0.4 : 1;
            Put(_grid, cell, x, y);
        }
        if (_shown.Count == 0)
            Put(_grid, UiKit.Label(_filter == 0 ? "Press X on Search to look for a game." : "Nothing here yet. Add a ROM folder or emulator in Settings, or press □ to rescan.", 26, UiKit.Dim), 0, 0);
        Put(_stage, _clip, GridX - 3, GridY - 3);

        // Focus frame in whichever zone is active.
        switch (_zone)
        {
            case Zone.Filters:
                _focus.Width = ColW; _focus.Height = ColPitch - 6;
                Put(_stage, _focus, ColX, ColTop + _filter * ColPitch + (_filter > 0 ? 10 : 0) + 3);
                break;
            case Zone.Sort:
                _focus.Width = SortW + 6; _focus.Height = SortH + 6;
                Put(_stage, _focus, SortX - 3, SortY - 3);
                break;
            default:
                _focus.Width = CellW + 6; _focus.Height = CellH + 6;
                Put(_grid, _focus, (_sel % Cols) * PitchX - 3, (_sel / Cols - _firstRow) * PitchY - 3);
                break;
        }
    }

    public override bool OnInput(Btn b)
    {
        switch (_zone)
        {
            case Zone.Filters: return FilterInput(b);
            case Zone.Sort: return SortInput(b);
            default: return GridInput(b);
        }
    }

    bool Common(Btn b)
    {
        switch (b)
        {
            case Btn.Square: Play(Sfx.Decide); Shell.Window.Rescan(); return true;
            case Btn.Circle: Play(Sfx.Back); Shell.Library.Changed -= OnLibraryChanged; Shell.Pop(); return true;
        }
        return false;
    }

    bool FilterInput(Btn b)
    {
        int n = _filter;
        switch (b)
        {
            case Btn.Up: n--; break;
            case Btn.Down: n++; break;
            case Btn.Right when _shown.Count > 0: _zone = Zone.Grid; Play(Sfx.Cursor); Render(); return true;
            case Btn.Cross when _filter == 0:
                Play(Sfx.Decide);
                Osk.Ask("Search", _query, q => { _query = q.Trim(); _sel = 0; _firstRow = 0; Apply(); });
                return true;
            case Btn.Cross when _shown.Count > 0: _zone = Zone.Grid; Play(Sfx.Decide); Render(); return true;
            default: return Common(b);
        }
        n = Math.Clamp(n, 0, _filters.Count - 1);
        if (n != _filter) { _filter = n; _sel = 0; _firstRow = 0; Play(Sfx.Cursor); Apply(); }
        return true;
    }

    bool SortInput(Btn b)
    {
        switch (b)
        {
            case Btn.Cross or Btn.Right: _sort = (_sort + 1) % Sorts.Length; Play(Sfx.Option); _sel = 0; _firstRow = 0; Apply(); return true;
            case Btn.Down when _shown.Count > 0: _zone = Zone.Grid; Play(Sfx.Cursor); Render(); return true;
            case Btn.Left: _zone = Zone.Filters; Play(Sfx.Cursor); Render(); return true;
            default: return Common(b);
        }
    }

    bool GridInput(Btn b)
    {
        int n = _sel;
        switch (b)
        {
            case Btn.Left when _sel % Cols == 0: _zone = Zone.Filters; Play(Sfx.Cursor); Render(); return true;
            case Btn.Left: n--; break;
            case Btn.Right: if (_sel % Cols < Cols - 1 && _sel + 1 < _shown.Count) n++; break;
            case Btn.Up when _sel < Cols: _zone = Zone.Sort; Play(Sfx.Cursor); Render(); return true;
            case Btn.Up: n -= Cols; break;
            case Btn.Down: n = Math.Min(_shown.Count - 1, n + Cols); break;
            case Btn.L1: n = Math.Max(0, n - Cols * 3); break;
            case Btn.R1: n = Math.Min(_shown.Count - 1, n + Cols * 3); break;
            case Btn.Cross when _shown.Count > 0:
                if (_shown[_sel].Game is Game sg) Shell.Window.StartGame(sg); else Shell.Window.StartApp(_shown[_sel].App!);
                return true;
            case Btn.Options when _shown.Count > 0 && _shown[_sel].Game is Game g:
                var o = AppSettings.Current.For(g.Id);
                Play(Sfx.Option);
                if (!o.Hidden) { GameMenu.Show(g); return true; }
                Shell.Push(new ListScreen(LibraryService.DisplayTitle(g), new[]
                {
                    new ListItem { Glyph = "", Label = "Show on Home", Activate = () => { o.Hidden = false; AppSettings.Current.Save(); Shell.Pop(); Shell.Library.NotifyChanged(); } },
                    new ListItem { Glyph = "", Label = "Information", Activate = () => { Shell.Pop(); Shell.Push(GameMenu.InfoScreen(g)); } },
                }, side: true));
                return true;
            default: return Common(b);
        }
        n = Math.Clamp(n, 0, Math.Max(0, _shown.Count - 1));
        if (n != _sel) { _sel = n; Play(Sfx.Cursor); Render(); }
        return true;
    }
}
