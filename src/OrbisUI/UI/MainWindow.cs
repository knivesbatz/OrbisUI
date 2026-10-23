using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using System.Windows.Threading;
using OrbisUI.Audio;
using OrbisUI.Core;
using OrbisUI.Input;
using OrbisUI.Launch;
using OrbisUI.Library;
using OrbisUI.Services;

namespace OrbisUI.UI;

public sealed class MainWindow : Window
{
    public WaveBackground Background_ { get; } = new();
    public Grid ScreenLayer { get; } = new();
    readonly Grid _overlay = new();
    readonly Grid _stage = new() { Width = UiKit.W, Height = UiKit.H, ClipToBounds = true };
    public InputRouter Router { get; }
    IntPtr _hwnd;
    bool _gameInFront;

    // Splash
    readonly Grid _splash = new() { Visibility = Visibility.Collapsed, Background = UiKit.B("#F0050C24") };
    readonly Image _splashArt = new() { Width = 420, Height = 420, Stretch = Stretch.UniformToFill };
    readonly Grid _splashFallback = new() { Width = 420, Height = 420 };
    readonly TextBlock _splashTitle = UiKit.Label("", 44);
    readonly TextBlock _splashStatus = UiKit.Label("", 26, UiKit.Dim);
    readonly Path _spinner = new() { Width = 46, Height = 46, Stroke = UiKit.Focus, StrokeThickness = 4, Data = Geometry.Parse("M23,2 A21,21 0 1 1 2,23"), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform() };

    // Toast
    readonly Border _toast = new() { Visibility = Visibility.Collapsed };
    readonly Queue<Notice> _toasts = new();
    bool _toastShowing;

    public MainWindow()
    {
        Title = "OrbisUI";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
        Background = Brushes.Black;
        ShowInTaskbar = true;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);

        _stage.Children.Add(Background_);
        _stage.Children.Add(ScreenLayer);
        _stage.Children.Add(_overlay);
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = _stage };
        BuildSplash();
        BuildToast();

        Router = new InputRouter(Dispatcher);
        Router.Pressed += OnPressed;
        Notifications.Posted += n => Dispatcher.BeginInvoke(() => EnqueueToast(n));
        LosslessScaling.Activated += g => Dispatcher.BeginInvoke(() => GameToast.Show("PRO Mode Active", "Frame generation is on for this game.", "\uE945"));

        Shell.Window = this;
        Shell.Launcher.Ready += (g, h) => Dispatcher.BeginInvoke(() => OnGameReady(g, h));
        Shell.Launcher.Exited += g => Dispatcher.BeginInvoke(() => OnGameExited(g));
        Shell.Launcher.Status += (g, s) => Dispatcher.BeginInvoke(() => _splashStatus.Text = s);

        Activated += (_, _) => { if (Shell.Pad != null) Shell.Pad.Foreground = true; Background_.SetRunning(true); Sound.Muted = false; SyncMusic(); };
        Deactivated += (_, _) => { if (Shell.Pad != null) Shell.Pad.Foreground = false; Background_.SetRunning(false); Sound.Muted = true; SyncMusic(); };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) Background_.SetRunning(false); };
        MouseMove += (_, _) => Cursor = null;
        SourceInitialized += (_, _) => _hwnd = new WindowInteropHelper(this).Handle;
        Loaded += (_, _) => Start();
        Closing += (_, _) => { AppSettings.Current.Save(); Shell.Library.Save(); };
    }

    // ---------------- startup ----------------
    void Start()
    {
        if (Emulators.AutoDetect().Count > 0) AppSettings.Current.Save();
        Task.Run(EmulatorProfiles.EnsureAll);
        Shell.Library.LoadCached();
        // Steam reads shortcuts.vdf only at startup, so emulated and added games go in before Steam starts.
        var games = Shell.Library.Games.ToList();
        Task.Run(async () =>
        {
            if (AppSettings.Current.AddGamesToSteam) await SteamShortcuts.SyncIfClosedAsync(games);
            if (AppSettings.Current.StartSteamSilently) Steam.EnsureRunningSilently();
        });
        Action next = () =>
        {
            if (!AppSettings.Current.SetupComplete) Shell.Reset(new SetupWizard());
            else EnterHome();
        };
        if (AppSettings.Current.BootAnimation) Shell.Reset(new BootScreen(next));
        else next();

        // Library refresh after the first frame so startup stays instant.
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, Rescan);
        var updTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        updTimer.Tick += async (_, _) =>
        {
            updTimer.Interval = TimeSpan.FromHours(Math.Max(1, AppSettings.Current.UpdateCheckHours));
            if (!AppSettings.Current.AutoCheckUpdates || Shell.Launcher.Running != null) return;
            var u = await Updater.CheckAsync();
            if (u != null) Notifications.Post("System software update available", $"Version {u.Tag}. Install it from Settings > System Software Update.", "");
        };
        updTimer.Start();
        if (Environment.GetEnvironmentVariable("ORBISUI_SCRIPT") is { Length: > 0 } script) RunScript(script);
    }

    bool _atHome;
    public void EnterHome() { _atHome = true; Shell.Reset(new HomeScreen()); SyncMusic(); }

    /// <summary>Home music plays only while the shell is in front at home with no game starting or running.</summary>
    public void SyncMusic() => Sound.SetMusic(_atHome && IsActive && _splash.Visibility != Visibility.Visible && Shell.Launcher.Running == null && AppSettings.Current.HomeMusic, AppSettings.Current.MusicVolume);

    /// <summary>
    /// Test hook: ORBISUI_SCRIPT="wait:3000;Up;shot:fn;Cross" drives the UI with button presses and saves screenshots of the
    /// stage to Logs\shots, so the shell can be checked without stealing the user's keyboard focus.
    /// </summary>
    async void RunScript(string script)
    {
        foreach (var raw in script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                if (raw.StartsWith("wait:")) { await Task.Delay(int.Parse(raw[5..])); continue; }
                if (raw.StartsWith("shot:"))
                {
                    var dir = System.IO.Path.Combine(AppPaths.Logs, "shots");
                    Directory.CreateDirectory(dir);
                    var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)UiKit.W / 2, (int)UiKit.H / 2, 48, 48, PixelFormats.Pbgra32);
                    rtb.Render(_stage);
                    var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                    using var fs = File.Create(System.IO.Path.Combine(dir, raw[5..] + ".png"));
                    enc.Save(fs);
                    continue;
                }
                if (raw == "quit") { Application.Current.Shutdown(); return; }
                if (raw == "gametoast") { GameToast.Show("PRO Mode Active", "Frame generation is on for this game.", "\uE945"); continue; }
                if (Enum.TryParse<Btn>(raw, true, out var b)) Shell.Dispatch(b);
                await Task.Delay(250);
            }
            catch (Exception ex) { Log.Warn($"script step {raw}: {ex.Message}"); }
        }
    }

    bool _scanning;
    public async void Rescan()
    {
        if (_scanning) return;
        _scanning = true;
        try
        {
            var before = Shell.Library.Games.Count;
            await Shell.Library.RescanAsync();
            var diff = Shell.Library.Games.Count - before;
            if (diff > 0 && before > 0) Notifications.Post("Library updated", $"{diff} new item{(diff == 1 ? "" : "s")} added.", "");
            await Shell.Metadata.RunAsync();
            foreach (var a in AppSettings.Current.Folders.SelectMany(x => x.Apps).ToList()) await FolderScreen.FetchArt(a);
        }
        catch (Exception ex) { Log.Error("Rescan", ex); }
        finally { _scanning = false; }
    }

    // ---------------- input ----------------
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Shell.Top is Osk osk)
        {
            if (e.Key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Clipboard.ContainsText()) { osk.OnText(Clipboard.GetText().Trim()); e.Handled = true; return; }
            if (e.Key == Key.Back) { osk.OnText("\b"); e.Handled = true; return; }
            if (e.Key == Key.Enter) { osk.OnText("\r"); e.Handled = true; return; }
            if (e.Key is Key.Escape or Key.Up or Key.Down or Key.Left or Key.Right) { Router.FromKey(e.Key, e.IsRepeat); e.Handled = true; }
            return; // characters arrive through OnTextInput
        }
        if (e.Key == Key.System && e.SystemKey == Key.F4) return;
        if (Router.FromKey(e.Key == Key.System ? e.SystemKey : e.Key, e.IsRepeat)) e.Handled = true;
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        if (Shell.Top is Osk osk && !string.IsNullOrEmpty(e.Text) && e.Text != "\r" && e.Text != "\b") { osk.OnText(e.Text); e.Handled = true; }
    }

    public void OnPadButton(Btn b, bool down)
    {
        // PS button while an emulator/PC game is in front: come back to the home screen; the game keeps running.
        if (down && b == Btn.PS && _gameInFront && Shell.Launcher.Running is Game g && g.Kind is LaunchKind.Emulator or LaunchKind.Exe)
        {
            Dispatcher.BeginInvoke(ShowHomeOverGame);
            return;
        }
        Router.FromPad(b, down);
    }

    void OnPressed(Btn b)
    {
        if (!IsActive) return;
        Cursor = Cursors.None;
        if (_splash.Visibility == Visibility.Visible)
        {
            if (b == Btn.Circle && Shell.Launcher.Running != null) { Shell.Launcher.Abandon(); HideSplash(); }
            return;
        }
        try { Shell.Dispatch(b); }
        catch (Exception ex) { Log.Error("Input handler", ex); Shell.Message("Something went wrong", ex.Message); }
    }

    // ---------------- games ----------------
    public async void StartGame(Game g, string? disc = null)
    {
        if (Shell.Launcher.Running?.Id == g.Id && Shell.Launcher.GameWindow != IntPtr.Zero)
        {
            // Resume the running game, like selecting it again on the console.
            Sound.Play(Sfx.Decide);
            SendGameToFront();
            return;
        }
        if (Shell.Launcher.Running is Game other)
        {
            Shell.Confirm("Close Application", $"{other.Title} is running. Close it and start {LibraryService.DisplayTitle(g)}?", () => { Shell.Launcher.CloseGame(); });
            return;
        }
        if (disc == null && g.Discs is { Count: > 1 } discs)
        {
            Sound.Play(Sfx.DialogShow);
            Shell.Push(new ListScreen(LibraryService.DisplayTitle(g), discs.Select((d, i) => new ListItem
            {
                Glyph = "\uE958", Label = $"Disc {i + 1}", Sub = () => System.IO.Path.GetFileName(d),
                Activate = () => { Shell.Pop(); StartGame(g, d); },
            }), dialog: true, subtitle: "Choose the disc to start."));
            return;
        }
        Sound.Play(Sfx.GameStart);
        ShowSplash(g);
        try
        {
            await Shell.Launcher.LaunchAsync(g, disc);
        }
        catch (LaunchException ex)
        {
            HideSplash();
            Sound.Play(Sfx.Error);
            Notifications.Post("Couldn't start " + LibraryService.DisplayTitle(g), ex.Message, "");
            Shell.Message("Can't Start", ex.Message);
        }
        catch (OperationCanceledException) { HideSplash(); }
        catch (Exception ex)
        {
            Log.Error("Launch " + g.Title, ex);
            HideSplash();
            Shell.Message("Can't Start", ex.Message);
        }
    }

    void OnGameReady(Game g, IntPtr hwnd)
    {
        HideSplash();
        if (hwnd != IntPtr.Zero) SendGameToFront();
        else Log.Warn($"No window detected for {g.Title}; leaving the launcher visible");
        _ = LosslessScaling.ActivateAsync(g, hwnd, () => Shell.Launcher.Running?.Id == g.Id);
    }

    void SendGameToFront()
    {
        _gameInFront = true;
        var gw = Shell.Launcher.GameWindow;
        WindowState = WindowState.Minimized;
        if (gw != IntPtr.Zero) Native.ForceForeground(gw);
        // While a game runs the shell should cost nothing: stop animation, drop caches.
        Background_.SetRunning(false);
        Native.TrimMemory();
    }

    void ShowHomeOverGame()
    {
        _gameInFront = false;
        RestoreShell();
        Shell.Home();
        Sound.Play(Sfx.Enter);
    }

    void OnGameExited(Game g)
    {
        _gameInFront = false;
        LosslessScaling.Reset();
        HideSplash();
        RestoreShell();
        Sound.Play(Sfx.GameQuit);
        SyncMusic();
        Shell.Library.Sort();
        Shell.Library.NotifyChanged();
    }

    void RestoreShell()
    {
        WindowState = WindowState.Maximized;
        Show();
        Activate();
        if (_hwnd != IntPtr.Zero) Native.ForceForeground(_hwnd);
    }

    public void StartApp(AppEntry a)
    {
        try
        {
            Sound.Play(Sfx.Boot);
            if (a.Target.StartsWith("http", StringComparison.OrdinalIgnoreCase) || a.Target.Contains("://")) Native.OpenShell(a.Target);
            else StartExternal(a.Target, a.Args ?? "");
        }
        catch (Exception ex) { Shell.Message(a.Name, "It couldn't be opened: " + ex.Message); }
    }

    /// <summary>Run a helper program (emulator settings, Explorer). The shell comes back when it closes.</summary>
    public void StartExternal(string exe, string args)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true, WorkingDirectory = System.IO.Path.GetDirectoryName(exe) ?? "" });
            if (p == null || exe.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase)) return;
            WindowState = WindowState.Minimized;
            p.EnableRaisingEvents = true;
            p.Exited += (_, _) => Dispatcher.BeginInvoke(RestoreShell);
        }
        catch (Exception ex) { Shell.Message("Can't Open", ex.Message); }
    }

    public void ExitToDesktop()
    {
        AppSettings.Current.Save();
        Shell.Library.Save();
        Application.Current.Shutdown();
    }

    public async Task InstallUpdate(UpdateInfo u, Action<double> progress)
    {
        try
        {
            var zip = await Updater.DownloadAsync(u, new Progress<double>(progress));
            AppSettings.Current.Save();
            if (Updater.Apply(zip)) Application.Current.Shutdown();
            else Shell.Message("Update", "The update couldn't be installed, so the current version was kept.");
        }
        catch (Exception ex)
        {
            Log.Error("Update", ex);
            Shell.Message("Update", ex.Message);
        }
    }

    // ---------------- splash ----------------
    void BuildSplash()
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var art = new Grid { Width = 420, Height = 420 };
        art.Children.Add(_splashFallback);
        art.Children.Add(_splashArt);
        sp.Children.Add(art);
        _splashTitle.HorizontalAlignment = HorizontalAlignment.Center; _splashTitle.Margin = new Thickness(0, 34, 0, 8); _splashTitle.MaxWidth = 1400;
        sp.Children.Add(_splashTitle);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        row.Children.Add(_spinner);
        _splashStatus.Margin = new Thickness(18, 0, 0, 0); _splashStatus.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(_splashStatus);
        sp.Children.Add(row);
        _splash.Children.Add(sp);
        var hint = UiKit.Hints((Btn.Circle, "Stop Waiting"));
        hint.VerticalAlignment = VerticalAlignment.Bottom; hint.HorizontalAlignment = HorizontalAlignment.Right; hint.Margin = new Thickness(0, 0, 60, 40);
        _splash.Children.Add(hint);
        _overlay.Children.Add(_splash);
    }

    void ShowSplash(Game g)
    {
        _splashTitle.Text = LibraryService.DisplayTitle(g);
        _splashStatus.Text = "Starting…";
        _splashArt.Source = Metadata.ImageTools.LoadForDisplay(g.Art, 420);
        _splashFallback.Children.Clear();
        if (_splashArt.Source == null) _splashFallback.Children.Add(Tiles.Make(new HomeItem { Kind = HomeKind.Game, Game = g, Title = g.Title }, 420));
        Background_.SetArt(g.BgSource == "SteamGridDB" ? g.Background : null);
        _splash.Visibility = Visibility.Visible;
        _splash.Opacity = 0;
        SyncMusic();
        var art = (FrameworkElement)((StackPanel)_splash.Children[0]).Children[0];
        art.RenderTransformOrigin = new Point(0.5, 0.5);
        var st = new ScaleTransform(0.86, 0.86);
        art.RenderTransform = st;
        UiKit.Animate(st, ScaleTransform.ScaleXProperty, 1, 380);
        UiKit.Animate(st, ScaleTransform.ScaleYProperty, 1, 380);
        UiKit.Animate(_splash, OpacityProperty, 1, 250);
        var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever };
        Timeline.SetDesiredFrameRate(spin, 30);
        ((RotateTransform)_spinner.RenderTransform).BeginAnimation(RotateTransform.AngleProperty, spin);
    }

    void HideSplash()
    {
        if (_splash.Visibility != Visibility.Visible) return;
        ((RotateTransform)_spinner.RenderTransform).BeginAnimation(RotateTransform.AngleProperty, null);
        UiKit.Animate(_splash, OpacityProperty, 0, 200, () => { _splash.Visibility = Visibility.Collapsed; SyncMusic(); });
    }

    // ---------------- toasts (top-left, like console notifications) ----------------
    void BuildToast()
    {
        _toast.HorizontalAlignment = HorizontalAlignment.Left;
        _toast.VerticalAlignment = VerticalAlignment.Top;
        _toast.Margin = new Thickness(40, 36, 0, 0);
        _toast.Width = 620;
        _toast.Background = new LinearGradientBrush(UiKit.C("#F21E3C7E"), UiKit.C("#F2112652"), 90);
        _toast.BorderBrush = UiKit.Line; _toast.BorderThickness = new Thickness(1);
        _toast.Padding = new Thickness(20, 16, 20, 16);
        _toast.RenderTransform = new TranslateTransform();
        _overlay.Children.Add(_toast);
    }

    void EnqueueToast(Notice n)
    {
        _toasts.Enqueue(n);
        if (!_toastShowing) NextToast();
    }

    void NextToast()
    {
        if (_toasts.Count == 0) { _toastShowing = false; return; }
        _toastShowing = true;
        var n = _toasts.Dequeue();
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(UiKit.Glyph(n.Glyph, 34));
        var sp = new StackPanel();
        var t = UiKit.Label(n.Title, 26); sp.Children.Add(t);
        if (!string.IsNullOrEmpty(n.Body)) { var b = UiKit.Label(n.Body, 20, UiKit.Dim); b.TextWrapping = TextWrapping.Wrap; b.MaxHeight = 60; sp.Children.Add(b); }
        Grid.SetColumn(sp, 1);
        g.Children.Add(sp);
        _toast.Child = g;
        _toast.Visibility = Visibility.Visible;
        Sound.Play(Sfx.Notify);
        var tt = (TranslateTransform)_toast.RenderTransform;
        UiKit.Animate(tt, TranslateTransform.XProperty, 0, 260);
        tt.X = -700;
        UiKit.Animate(tt, TranslateTransform.XProperty, 0, 300);
        var hide = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.5) };
        hide.Tick += (_, _) =>
        {
            hide.Stop();
            UiKit.Animate(_toast, OpacityProperty, 0, 250, () => { _toast.Visibility = Visibility.Collapsed; _toast.Opacity = 1; NextToast(); });
        };
        hide.Start();
    }
}
