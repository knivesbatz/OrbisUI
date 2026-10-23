using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using OrbisUI.Audio;
using OrbisUI.Input;
using OrbisUI.Library;

namespace OrbisUI.UI;

/// <summary>Original boot sequence: a light sweep and wordmark over black, then the "press the PS button" user screen.</summary>
public sealed class BootScreen : Screen
{
    readonly Action _done;
    readonly Grid _intro = new() { Background = Brushes.Black };
    readonly Grid _login = new() { Opacity = 0 };
    bool _atLogin, _finished;

    public BootScreen(Action done)
    {
        _done = done;
        Children.Add(_login);
        Children.Add(_intro);
        BuildIntro();
        BuildLogin();
    }

    void BuildIntro()
    {
        var streak = new Ellipse
        {
            Width = 1500, Height = 26, Opacity = 0,
            Fill = new RadialGradientBrush(UiKit.C("#FFFFFFFF"), UiKit.C("#0077C8FF")) { GradientStops = { new GradientStop(UiKit.C("#FFFFFFFF"), 0), new GradientStop(UiKit.C("#AA6FD0FF"), 0.35), new GradientStop(UiKit.C("#00104090"), 1) } },
            RenderTransform = new ScaleTransform(0.02, 1), RenderTransformOrigin = new Point(0.5, 0.5),
            Effect = new BlurEffect { Radius = 12 },
        };
        _intro.Children.Add(streak);
        var word = new TextBlock
        {
            Text = "O R B I S", FontFamily = UiKit.Font, FontSize = 120, Foreground = Brushes.White, Opacity = 0,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = new ScaleTransform(0.94, 0.94), RenderTransformOrigin = new Point(0.5, 0.5),
            Effect = new DropShadowEffect { Color = UiKit.C("#6FD0FF"), BlurRadius = 40, ShadowDepth = 0, Opacity = 0.8 },
        };
        _intro.Children.Add(word);

        Loaded += (_, _) =>
        {
            Sound.PlayFile("startup.mp3");
            var st = (ScaleTransform)streak.RenderTransform;
            st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.02, 1.2, TimeSpan.FromMilliseconds(1400)) { EasingFunction = UiKit.Ease });
            streak.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
            {
                KeyFrames = { new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)), new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(500))), new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2000))) },
            });
            word.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
            {
                KeyFrames = { new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(700))), new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1600))), new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(3300))), new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(4100))) },
            });
            var ws = (ScaleTransform)word.RenderTransform;
            ws.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.94, 1.0, TimeSpan.FromMilliseconds(4100)));
            ws.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, 1.0, TimeSpan.FromMilliseconds(4100)));
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(4100) };
            t.Tick += (_, _) => { t.Stop(); ShowLogin(); };
            t.Start();
        };
    }

    void BuildLogin()
    {
        var steam = Steam.FindInstall();
        string name = Environment.UserName; ImageSource? avatar = null;
        if (steam != null)
        {
            var (id, persona, _) = Steam.CurrentUser(steam);
            name = persona ?? name;
            if (id != null) avatar = Metadata.ImageTools.LoadForDisplay(System.IO.Path.Combine(steam, "config", "avatarcache", id + ".png"), 256);
        }
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var prompt = UiKit.Label("Press the PS button to use the controller.", 36);
        prompt.HorizontalAlignment = HorizontalAlignment.Center;
        prompt.Margin = new Thickness(0, 0, 0, 70);
        sp.Children.Add(prompt);
        var tile = new Grid { Width = 260, Height = 260, Background = UiKit.Panel };
        if (avatar != null) tile.Children.Add(new Image { Source = avatar, Stretch = Stretch.UniformToFill });
        else { var g = UiKit.Glyph("", 120); g.HorizontalAlignment = HorizontalAlignment.Center; tile.Children.Add(g); }
        var frame = UiKit.FocusFrame(3); frame.Margin = new Thickness(-4);
        tile.Children.Add(frame);
        sp.Children.Add(tile);
        var n = UiKit.Label(name, 34);
        n.HorizontalAlignment = HorizontalAlignment.Center; n.Margin = new Thickness(0, 22, 0, 0);
        sp.Children.Add(n);
        _login.Children.Add(sp);
        var hint = UiKit.Hints((Btn.PS, "Log In"), (Btn.Cross, "Log In"));
        hint.HorizontalAlignment = HorizontalAlignment.Center; hint.VerticalAlignment = VerticalAlignment.Bottom; hint.Margin = new Thickness(0, 0, 0, 60);
        _login.Children.Add(hint);
    }

    void ShowLogin()
    {
        if (_atLogin || _finished) return;
        _atLogin = true;
        UiKit.Animate(_intro, OpacityProperty, 0, 700, () => _intro.Visibility = Visibility.Collapsed);
        UiKit.Animate(_login, OpacityProperty, 1, 700);
    }

    public override bool OnInput(Btn b)
    {
        if (!_atLogin) { Sound.StopFile(); ShowLogin(); return true; }
        if (b is Btn.PS or Btn.Cross or Btn.Options)
        {
            if (_finished) return true;
            _finished = true;
            Sound.StopFile();
            Play(Sfx.Login);
            _done();
        }
        return true;
    }
}
