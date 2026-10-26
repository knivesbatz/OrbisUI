using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Path = System.Windows.Shapes.Path;
using OrbisUI.Audio;
using OrbisUI.Core;
using OrbisUI.Input;

namespace OrbisUI.UI;

/// <summary>
/// The console's "Please wait..." page (menu tour 0:44): the two rules, and the four PS shapes fading in turn beside the text.
/// Run shows it while something loads, then swaps in the result; Circle cancels.
/// </summary>
public sealed class WaitScreen : Screen
{
    WaitScreen()
    {
        Children.Add(new Border { Height = 1, Background = UiKit.Line, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 136, 0, 0) });
        Children.Add(new Border { Height = 1, Background = UiKit.Line, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 135) });
        // Square, triangle, cross, circle in a 2x2 block, as in the reference.
        var shapes = new Canvas { Width = 66, Height = 60 };
        string[] geo = { "M2,2 H22 V22 H2 Z", "M4,22 L14,3 L24,22 Z", "M3,3 L21,21 M21,3 L3,21", "M12,2 A10,10 0 1 1 11.99,2 Z" };
        for (int i = 0; i < 4; i++)
        {
            var p = new Path { Data = Geometry.Parse(geo[i]), Stroke = UiKit.Text, StrokeThickness = 2.5, Width = 24, Height = 24, Opacity = 0.15 };
            Canvas.SetLeft(p, i % 2 * 36); Canvas.SetTop(p, i / 2 * 34);
            shapes.Children.Add(p);
            var a = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromSeconds(1.6), BeginTime = TimeSpan.FromSeconds(i * 0.2) };
            a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.25)));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(0.15, KeyTime.FromPercent(0.75)));
            Timeline.SetDesiredFrameRate(a, 30); // a loading spinner doesn't need 60 fps
            p.BeginAnimation(OpacityProperty, a);
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(shapes);
        var text = UiKit.Label("Please wait...", 32);
        text.Margin = new Thickness(30, 0, 0, 0); text.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(text);
        Children.Add(row);
    }

    public override bool OnInput(Btn b)
    {
        if (b == Btn.Circle) { Play(Sfx.Back); Shell.Pop(); }
        return true;
    }

    /// <summary>Shows the wait page while load runs off the UI thread, then pops it and calls show with the result (unless cancelled).</summary>
    public static void Run<T>(Func<Task<T>> load, Action<T> show)
    {
        var w = new WaitScreen();
        Shell.Push(w);
        _ = Task.Run(async () =>
        {
            try
            {
                var r = await load();
                Shell.Window.Dispatcher.Invoke(() => { if (Shell.Top != w) return; Shell.Pop(); show(r); });
            }
            catch (Exception ex)
            {
                Log.Warn("Loading: " + ex.Message);
                Shell.Window.Dispatcher.Invoke(() => { if (Shell.Top == w) Shell.Pop(); });
            }
        });
    }
}
