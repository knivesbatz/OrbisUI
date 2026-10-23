using System.Windows;
using System.Windows.Controls;
using OrbisUI.Audio;
using OrbisUI.Core;
using OrbisUI.Input;
using OrbisUI.Library;

namespace OrbisUI.UI;

/// <summary>Everything installed, as a grid. Hidden games are shown dimmed so they can be brought back.</summary>
public sealed class LibraryScreen : Screen
{
    const int Cols = 6;
    const double Tile = 236, CellW = 270, CellH = 318, Left = 150, Top = 150;
    readonly Canvas _grid = new();
    readonly Border _focus = UiKit.FocusFrame(3);
    readonly TextBlock _count = UiKit.Label("", 24, UiKit.Dim);
    List<Game> _games = new();
    int _sel, _firstRow;
    string _filter = "All";
    static readonly string[] Filters = { "All", "PC", "PS1", "PS2", "PS3", "PS4", "PSP", "VITA" };

    public LibraryScreen()
    {
        Chrome("Library", (Btn.Cross, "Start"), (Btn.Circle, "Back"), (Btn.Options, "Options"), (Btn.Triangle, "Filter"), (Btn.Square, "Rescan"));
        _count.Margin = new Thickness(320, 50, 0, 0);
        _count.VerticalAlignment = VerticalAlignment.Top;
        Children.Add(_count);
        _grid.Margin = new Thickness(Left, Top, 0, 0);
        _grid.ClipToBounds = false;
        Children.Add(new Border { ClipToBounds = true, Margin = new Thickness(0, 120, 0, 92), Child = _grid });
        _focus.Width = Tile + 6; _focus.Height = Tile + 6;
        Load();
        Shell.Library.Changed += OnLibraryChanged;
    }

    void OnLibraryChanged() => Dispatcher.BeginInvoke(Load);
    public override void OnHidden() { }

    void Load()
    {
        var all = Shell.Library.Games.OrderBy(g => LibraryService.DisplayTitle(g), StringComparer.OrdinalIgnoreCase);
        _games = (_filter == "All" ? all : all.Where(g => g.Platform == _filter)).ToList();
        _count.Text = $"{_filter}  ·  {_games.Count}";
        _sel = Math.Clamp(_sel, 0, Math.Max(0, _games.Count - 1));
        Render();
    }

    void Render()
    {
        _grid.Children.Clear();
        int row = _sel / Cols;
        if (row < _firstRow) _firstRow = row;
        if (row > _firstRow + 1) _firstRow = row - 1;
        for (int i = _firstRow * Cols; i < Math.Min(_games.Count, (_firstRow + 3) * Cols); i++)
        {
            var g = _games[i];
            double x = (i % Cols) * CellW, y = (i / Cols - _firstRow) * CellH;
            var tile = Tiles.Make(new HomeItem { Kind = HomeKind.Game, Game = g, Title = g.Title }, Tile);
            bool hidden = AppSettings.Current.Games.TryGetValue(g.Id, out var o) && o.Hidden;
            tile.Opacity = hidden ? 0.4 : 1;
            Canvas.SetLeft(tile, x); Canvas.SetTop(tile, y);
            _grid.Children.Add(tile);
            var name = UiKit.Label(LibraryService.DisplayTitle(g), 20, i == _sel ? UiKit.Text : UiKit.Dim);
            name.Width = Tile;
            Canvas.SetLeft(name, x); Canvas.SetTop(name, y + Tile + 6);
            _grid.Children.Add(name);
        }
        if (_games.Count == 0)
        {
            var empty = UiKit.Label("No games found. Add a ROM folder or emulator in Settings, or press □ to rescan.", 28, UiKit.Dim);
            _grid.Children.Add(empty);
            return;
        }
        Canvas.SetLeft(_focus, (_sel % Cols) * CellW - 3);
        Canvas.SetTop(_focus, (_sel / Cols - _firstRow) * CellH - 3);
        _grid.Children.Add(_focus);
    }

    public override bool OnInput(Btn b)
    {
        int n = _sel;
        switch (b)
        {
            case Btn.Left: if (_sel % Cols > 0) n--; break;
            case Btn.Right: if (_sel % Cols < Cols - 1 && _sel + 1 < _games.Count) n++; break;
            case Btn.Up: n -= Cols; break;
            case Btn.Down: n = Math.Min(_games.Count - 1, n + Cols); break;
            case Btn.L1: n -= Cols * 3; break;
            case Btn.R1: n = Math.Min(_games.Count - 1, n + Cols * 3); break;
            case Btn.Cross when _games.Count > 0: Shell.Window.StartGame(_games[_sel]); return true;
            case Btn.Options when _games.Count > 0:
                var g = _games[_sel];
                var o = AppSettings.Current.For(g.Id);
                if (o.Hidden)
                {
                    Play(Sfx.Option);
                    Shell.Push(new ListScreen(LibraryService.DisplayTitle(g), new[]
                    {
                        new ListItem { Glyph = "", Label = "Show on Home", Activate = () => { o.Hidden = false; AppSettings.Current.Save(); Shell.Pop(); Shell.Library.NotifyChanged(); } },
                        new ListItem { Glyph = "", Label = "Information", Activate = () => { Shell.Pop(); Shell.Push(GameMenu.InfoScreen(g)); } },
                    }, dialog: true));
                }
                else { Play(Sfx.Option); GameMenu.Show(g); }
                return true;
            case Btn.Triangle:
                _filter = Filters[(Array.IndexOf(Filters, _filter) + 1) % Filters.Length];
                _sel = 0; _firstRow = 0; Play(Sfx.Option); Load(); return true;
            case Btn.Square:
                Play(Sfx.Decide); Shell.Window.Rescan(); return true;
            case Btn.Circle:
                Play(Sfx.Back); Shell.Library.Changed -= OnLibraryChanged; Shell.Pop(); return true;
            default: return false;
        }
        n = Math.Clamp(n, 0, Math.Max(0, _games.Count - 1));
        if (n != _sel) { _sel = n; Play(Sfx.Cursor); Render(); }
        return true;
    }
}
