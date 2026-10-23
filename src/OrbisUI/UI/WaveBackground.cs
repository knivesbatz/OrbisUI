using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using OrbisUI.Core;

namespace OrbisUI.UI;

/// <summary>
/// The blue console backdrop with slow translucent ribbons (original artwork drawn in code). Ribbons are bitmap-cached and only
/// translated, so the GPU composites them for almost nothing; animation stops whenever the window is not in front.
/// A game's background art fades over it when one is selected.
/// </summary>
public sealed class WaveBackground : Grid
{
    readonly Canvas _waves = new() { Width = UiKit.W, Height = UiKit.H };
    readonly Image _art = new() { Stretch = Stretch.UniformToFill, Opacity = 0 };
    readonly Image _artNext = new() { Stretch = Stretch.UniformToFill, Opacity = 0 };
    readonly List<(UIElement el, TranslateTransform t, double dx, double dy, double secs)> _ribbons = new();
    readonly MediaElement? _video;
    bool _running;

    public WaveBackground()
    {
        Width = UiKit.W; Height = UiKit.H;
        ClipToBounds = true;
        Background = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop(UiKit.C("#0A2A8E"), 0), new GradientStop(UiKit.C("#1747B4"), 0.45),
                new GradientStop(UiKit.C("#1E58CC"), 0.75), new GradientStop(UiKit.C("#123A9E"), 1),
            },
        };
        Children.Add(_waves);
        AddRibbon("M-300,820 C 200,560 640,1000 1100,700 S 1900,420 2300,640 L 2300,760 C 1900,560 1500,900 1080,860 S 300,760 -300,960 Z", "#30A8D4FF", 160, 30, 26);
        AddRibbon("M-300,260 C 300,520 820,40 1320,320 S 2000,520 2300,300 L 2300,360 C 1900,600 1500,420 1180,420 S 360,520 -300,360 Z", "#2490C8FF", -140, 40, 34);
        AddRibbon("M-300,640 C 400,380 900,860 1400,520 S 2100,300 2300,420 L 2300,450 C 1900,350 1500,640 1300,640 S 500,500 -300,700 Z", "#3FD8F0FF", 120, -50, 22);
        AddRibbon("M-300,1000 C 500,880 1000,1100 1500,900 S 2100,800 2300,880 L 2300,1180 L -300,1180 Z", "#200A1A60", -80, 20, 40);
        // A user-supplied live background (Data\Media\background.mp4) replaces the drawn ribbons; decoded on the GPU.
        if (AppPaths.MediaFile("background.mp4") is string bg)
        {
            _video = new MediaElement
            {
                Source = new Uri(bg), LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual,
                IsMuted = true, Stretch = Stretch.UniformToFill, Width = UiKit.W, Height = UiKit.H, ScrubbingEnabled = true,
            };
            _video.MediaEnded += (_, _) => { _video.Position = TimeSpan.Zero; _video.Play(); };
            _video.MediaFailed += (_, e) => { Log.Warn("background video: " + e.ErrorException?.Message); _video.Visibility = Visibility.Collapsed; _waves.Visibility = Visibility.Visible; };
            _waves.Visibility = Visibility.Collapsed;
            Children.Add(_video);
            _video.Play(); _video.Pause(); // shows the first frame even while animation is off
        }
        // Royal-blue grade over the video/ribbons so the home matches the console's colour (user reference: #0A3CC4 -> #0630A8).
        Children.Add(new Border
        {
            IsHitTestVisible = false, Opacity = 0.85,
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
                GradientStops = { new GradientStop(UiKit.C("#0030C8"), 0), new GradientStop(UiKit.C("#0034B8"), 0.5), new GradientStop(UiKit.C("#0630A8"), 1) },
            },
        });
        foreach (var img in new[] { _art, _artNext })
        {
            img.Effect = null;
            Children.Add(img);
        }
        // A blue veil keeps text readable over any artwork, like the console does.
        Children.Add(new Border { Background = new LinearGradientBrush(UiKit.C("#B30A2370"), UiKit.C("#660A2370"), 90), IsHitTestVisible = false, Opacity = 0, Name = "Veil" });
    }

    void AddRibbon(string data, string color, double dx, double dy, double secs)
    {
        var c = UiKit.C(color);
        var path = new Path
        {
            Data = Geometry.Parse(data),
            Fill = new LinearGradientBrush(Color.FromArgb(0, c.R, c.G, c.B), c, 0) { GradientStops = { new GradientStop(c, 0.5), new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1) } },
            CacheMode = new BitmapCache { RenderAtScale = 0.5 },
        };
        var t = new TranslateTransform();
        path.RenderTransform = t;
        _waves.Children.Add(path);
        _ribbons.Add((path, t, dx, dy, secs));
    }

    public void SetRunning(bool run)
    {
        run &= AppSettings.Current.AnimatedBackground && UiKit.Motion;
        if (run == _running) return;
        _running = run;
        if (_video != null) { if (run) _video.Play(); else _video.Pause(); return; }
        foreach (var (_, t, dx, dy, secs) in _ribbons)
        {
            if (!run)
            {
                // Freeze where they are (no snapping back).
                var x = t.X; var y = t.Y;
                t.BeginAnimation(TranslateTransform.XProperty, null); t.BeginAnimation(TranslateTransform.YProperty, null);
                t.X = x; t.Y = y;
                continue;
            }
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            t.BeginAnimation(TranslateTransform.XProperty, Slow(t.X, dx, secs, ease));
            t.BeginAnimation(TranslateTransform.YProperty, Slow(t.Y, dy, secs * 1.3, ease));
        }
    }

    static DoubleAnimation Slow(double from, double to, double secs, IEasingFunction ease)
    {
        var a = new DoubleAnimation(from, to, TimeSpan.FromSeconds(secs)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
        // 30 fps is plenty for slow ribbons and halves GPU/CPU wakeups.
        Timeline.SetDesiredFrameRate(a, 30);
        return a;
    }

    string? _current;

    /// <summary>Cross-fade to a game's background art (null = plain waves).</summary>
    public void SetArt(string? path)
    {
        if (path == _current) return;
        _current = path;
        var veil = (Border)Children[^1];
        var src = OrbisUI.Metadata.ImageTools.LoadForDisplay(path, 1280);
        var front = _art.Opacity >= _artNext.Opacity ? _art : _artNext;
        var back = front == _art ? _artNext : _art;
        if (src == null)
        {
            UiKit.Animate(front, OpacityProperty, 0, 400);
            UiKit.Animate(veil, OpacityProperty, 0, 400);
            return;
        }
        back.Source = src;
        UiKit.Animate(back, OpacityProperty, 0.55, 500);
        UiKit.Animate(front, OpacityProperty, 0, 500);
        UiKit.Animate(veil, OpacityProperty, 1, 400);
    }
}

