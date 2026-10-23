using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using OrbisUI.Core;
using OrbisUI.Input;

namespace OrbisUI.UI;

/// <summary>Shared look: colours, type ramp, focus frame, PS button glyphs and animation helpers.</summary>
public static class UiKit
{
    public const double W = 1920, H = 1080;

    public static readonly FontFamily Font = new("Segoe UI Light, Segoe UI");
    public static readonly FontFamily FontRegular = new("Segoe UI");
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static Brush Text => B(AppSettings.Current.HighContrast ? "#FFFFFF" : "#F2F6FF");
    public static Brush Dim => B(AppSettings.Current.HighContrast ? "#E0E8FF" : "#A9BEEA");
    public static readonly Brush Focus = B("#86E1FF");
    public static readonly Brush FocusFill = new LinearGradientBrush(C("#5539B6FF"), C("#2239B6FF"), 90);
    public static readonly Brush Panel = B("#1AFFFFFF");
    public static readonly Brush Line = B("#40FFFFFF");

    public static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    public static SolidColorBrush B(string hex) { var b = new SolidColorBrush(C(hex)); b.Freeze(); return b; }

    public static TextBlock Label(string text, double size, Brush? fg = null, FontFamily? font = null, FontWeight? weight = null)
        => new()
        {
            Text = text, FontSize = size, Foreground = fg ?? Text, FontFamily = font ?? Font,
            FontWeight = weight ?? FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis,
        };

    public static TextBlock Glyph(string glyph, double size, Brush? fg = null)
        => new() { Text = glyph, FontFamily = Icons, FontSize = size, Foreground = fg ?? Text, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>The console's cyan focus outline with soft glow.</summary>
    public static Border FocusFrame(double thickness = 3)
    {
        var b = new Border
        {
            BorderBrush = Focus, BorderThickness = new Thickness(thickness), Background = Brushes.Transparent, IsHitTestVisible = false,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = C("#7FD8FF"), BlurRadius = 18, ShadowDepth = 0, Opacity = 0.9 },
        };
        return b;
    }

    /// <summary>Face-button glyphs drawn as on the pad: ✕ ○ □ △ inside a ring.</summary>
    public static FrameworkElement Button(Btn b, double size = 26)
    {
        var g = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new Ellipse { Stroke = Text, StrokeThickness = 1.4, Fill = B("#22FFFFFF") });
        double s = size * 0.42, o = (size - s) / 2;
        Shape? shape = b switch
        {
            Btn.Cross => new Path { Data = Geometry.Parse($"M{o},{o} L{o + s},{o + s} M{o + s},{o} L{o},{o + s}"), Stroke = B("#8FB8FF") },
            Btn.Circle => new Ellipse { Width = s, Height = s, Stroke = B("#FF7A8A") },
            Btn.Square => new Rectangle { Width = s * 0.9, Height = s * 0.9, Stroke = B("#F59BD6") },
            Btn.Triangle => new Path { Data = Geometry.Parse($"M{size / 2},{o} L{o + s},{o + s * 0.9} L{o},{o + s * 0.9} Z"), Stroke = B("#62E2B8") },
            _ => null,
        };
        if (shape != null)
        {
            shape.StrokeThickness = 1.8;
            if (shape is not Path) { shape.HorizontalAlignment = HorizontalAlignment.Center; shape.VerticalAlignment = VerticalAlignment.Center; }
            g.Children.Add(shape);
        }
        else
        {
            g.Children.Clear();
            var name = b switch { Btn.Options => "OPTIONS", Btn.Share => "SHARE", Btn.L1 => "L1", Btn.R1 => "R1", Btn.L2 => "L2", Btn.R2 => "R2", Btn.PS => "PS", _ => b.ToString() };
            g.Width = double.NaN;
            g.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(size / 2), BorderBrush = Text, BorderThickness = new Thickness(1.2), Padding = new Thickness(8, 0, 8, 0),
                Child = new TextBlock { Text = name, FontSize = size * 0.42, Foreground = Text, FontFamily = FontRegular, VerticalAlignment = VerticalAlignment.Center },
            });
        }
        return g;
    }

    /// <summary>Bottom hint bar: "✕ Enter  ○ Back".</summary>
    public static StackPanel Hints(params (Btn b, string text)[] items)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (b, t) in items)
        {
            sp.Children.Add(Button(b, 26));
            sp.Children.Add(new TextBlock { Text = t, FontSize = 22, Foreground = Text, FontFamily = Font, Margin = new Thickness(8, 0, 34, 0), VerticalAlignment = VerticalAlignment.Center });
        }
        return sp;
    }

    public static bool Motion => !AppSettings.Current.ReducedMotion;

    public static Duration Dur(int ms) => new(TimeSpan.FromMilliseconds(Motion ? ms : 1));

    public static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

    public static void Animate(UIElement el, DependencyProperty p, double to, int ms, Action? done = null, double? from = null)
    {
        var a = new DoubleAnimation { To = to, Duration = Dur(ms), EasingFunction = Ease };
        if (from != null) a.From = from;
        if (done != null) a.Completed += (_, _) => done();
        el.BeginAnimation(p, a);
    }

    public static void Animate(Animatable target, DependencyProperty p, double to, int ms)
        => target.BeginAnimation(p, new DoubleAnimation { To = to, Duration = Dur(ms), EasingFunction = Ease });

    public static void FadeIn(UIElement el, int ms = 250) { el.Opacity = 0; Animate(el, UIElement.OpacityProperty, 1, ms); }
}
