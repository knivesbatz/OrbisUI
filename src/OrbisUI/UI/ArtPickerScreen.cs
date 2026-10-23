using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OrbisUI.Audio;
using OrbisUI.Core;
using OrbisUI.Input;
using OrbisUI.Library;
using OrbisUI.Metadata;

namespace OrbisUI.UI;

/// <summary>Pick a game's cover with the controller from SteamGridDB's square grids. △ searches another name, □ uses an image file.</summary>
public sealed class ArtPickerScreen : Screen
{
    const int Cols = 5;
    const double Tile = 300, Cell = 330, Left = 150, Top = 170;
    readonly Game _game;
    readonly string _title;
    readonly Func<string, Task<bool>> _useUrl;
    readonly Func<string, Task> _useFile;
    readonly Canvas _grid = new();
    readonly Border _focus = UiKit.FocusFrame(3);
    readonly TextBlock _status = UiKit.Label("Loading artwork from SteamGridDB…", 26, UiKit.Dim);
    List<MetadataService.ArtOption> _opts = new();
    int _sel, _firstRow;
    bool _busy;

    public ArtPickerScreen(Game g) : this(g, LibraryService.DisplayTitle(g),
        url => Shell.Metadata.ApplyChosenArtAsync(g, url),
        async f =>
        {
            AppSettings.Current.For(g.Id).ArtPath = f; AppSettings.Current.Save(); g.Art = null;
            await Shell.Metadata.ProcessAsync(g, false, CancellationToken.None);
            Shell.Library.Save();
        }) { }

    /// <summary>TV &amp; Video apps: same picker, saved as the app's square tile.</summary>
    public ArtPickerScreen(AppEntry a) : this(MetadataService.AppGame(a.Name), a.Name,
        async url => { var file = await Shell.Metadata.AppArtAsync(a.Name, url); if (file != null) SetAppArt(a, file); return file != null; },
        async f => { var data = await File.ReadAllBytesAsync(f); SetAppArt(a, MetadataService.SaveAppArt(a.Name, data)); }) { }

    static void SetAppArt(AppEntry a, string file) => Shell.Window.Dispatcher.Invoke(() =>
    {
        a.ArtPath = file;
        AppSettings.Current.Save();
        Shell.Library.NotifyChanged();
    });

    ArtPickerScreen(Game g, string title, Func<string, Task<bool>> useUrl, Func<string, Task> useFile)
    {
        _game = g; _title = title; _useUrl = useUrl; _useFile = useFile;
        Chrome("Change Artwork", (Btn.Cross, "Use"), (Btn.Circle, "Back"), (Btn.Triangle, "Search Name"), (Btn.Square, "Image File"));
        _status.Margin = new Thickness(Left, 124, 0, 0); _status.VerticalAlignment = VerticalAlignment.Top;
        Children.Add(_status);
        _grid.Margin = new Thickness(Left, Top, 0, 0);
        Children.Add(new Border { ClipToBounds = true, Margin = new Thickness(0, 160, 0, 92), Child = _grid });
        _focus.Width = Tile + 6; _focus.Height = Tile + 6;
        _ = Load(null);
    }

    async Task Load(string? search)
    {
        if (!Secrets.Has(Secrets.SteamGridDb))
        {
            _status.Text = "Add a SteamGridDB API key in Settings > Accounts and Services to browse artwork. □ picks an image file instead.";
            return;
        }
        _busy = true;
        _status.Text = "Loading artwork from SteamGridDB…";
        var (name, opts) = await Shell.Metadata.SgdbOptionsAsync(_game, search);
        _busy = false;
        _opts = opts;
        _sel = 0; _firstRow = 0;
        _status.Text = opts.Count == 0 ? "No square artwork found. Press △ to search with a different name." : $"{name}  ·  {opts.Count} options";
        Render();
    }

    void Render()
    {
        _grid.Children.Clear();
        int row = _sel / Cols;
        if (row < _firstRow) _firstRow = row;
        if (row > _firstRow + 1) _firstRow = row - 1;
        for (int i = _firstRow * Cols; i < Math.Min(_opts.Count, (_firstRow + 3) * Cols); i++)
        {
            var img = new Image { Width = Tile, Height = Tile, Stretch = Stretch.UniformToFill };
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit(); bi.UriSource = new Uri(_opts[i].Thumb); bi.DecodePixelWidth = 300; bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit();
                img.Source = bi;
            }
            catch { }
            var cell = new Grid { Width = Tile, Height = Tile, Background = UiKit.Panel };
            cell.Children.Add(img);
            Canvas.SetLeft(cell, (i % Cols) * Cell); Canvas.SetTop(cell, (i / Cols - _firstRow) * Cell);
            _grid.Children.Add(cell);
        }
        if (_opts.Count == 0) return;
        Canvas.SetLeft(_focus, (_sel % Cols) * Cell - 3);
        Canvas.SetTop(_focus, (_sel / Cols - _firstRow) * Cell - 3);
        _grid.Children.Add(_focus);
    }

    public override bool OnInput(Btn b)
    {
        int n = _sel;
        switch (b)
        {
            case Btn.Left: if (_sel % Cols > 0) n--; break;
            case Btn.Right: if (_sel % Cols < Cols - 1) n++; break;
            case Btn.Up: n -= Cols; break;
            case Btn.Down: n += Cols; break;
            case Btn.Cross when _opts.Count > 0 && !_busy:
                _busy = true;
                Play(Sfx.Decide);
                _status.Text = "Saving…";
                var opt = _opts[_sel];
                _ = Task.Run(async () =>
                {
                    var ok = await _useUrl(opt.Url);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        _busy = false;
                        if (ok) { Shell.Toast("Artwork changed", LibraryService.DisplayTitle(_game), ""); Shell.Pop(); }
                        else _status.Text = "That image couldn't be downloaded. Try another one.";
                    });
                });
                return true;
            case Btn.Triangle:
                Play(Sfx.Option);
                Osk.Ask("Search SteamGridDB", _title, v => { if (!string.IsNullOrWhiteSpace(v)) _ = Load(v.Trim()); });
                return true;
            case Btn.Square:
                Play(Sfx.Option);
                FilePicker.Pick("Choose Artwork", new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" }, async f =>
                {
                    try { await _useFile(f); Shell.Pop(); }
                    catch (Exception ex) { _status.Text = "That image couldn't be used: " + ex.Message; }
                });
                return true;
            case Btn.Circle: Play(Sfx.Back); Shell.Pop(); return true;
            default: return true;
        }
        n = Math.Clamp(n, 0, Math.Max(0, _opts.Count - 1));
        if (n != _sel) { _sel = n; Play(Sfx.Cursor); Render(); }
        return true;
    }
}
