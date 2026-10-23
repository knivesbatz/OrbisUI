using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using OrbisUI.Audio;
using OrbisUI.Core;

namespace OrbisUI.UI;

/// <summary>
/// A console-style notification drawn over the running game: a small topmost window that never takes focus and lets
/// clicks through, so the game keeps input. Works over windowed / borderless games (which Lossless Scaling needs anyway).
/// </summary>
public static class GameToast
{
    const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);

    public static void Show(string title, string body, string glyph)
    {
        var area = SystemParameters.WorkArea;
        double scale = SystemParameters.PrimaryScreenHeight / UiKit.H;
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(UiKit.Glyph(glyph, 34));
        var sp = new StackPanel();
        sp.Children.Add(UiKit.Label(title, 26));
        if (body.Length > 0) sp.Children.Add(UiKit.Label(body, 20, UiKit.Dim));
        Grid.SetColumn(sp, 1);
        row.Children.Add(sp);
        var card = new Border
        {
            Width = 620, Padding = new Thickness(20, 16, 20, 16), BorderBrush = UiKit.Line, BorderThickness = new Thickness(1),
            Background = new LinearGradientBrush(UiKit.C("#F21E3C7E"), UiKit.C("#F2112652"), 90), Child = row,
            LayoutTransform = new ScaleTransform(scale, scale),
        };
        var w = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true,
            ShowActivated = false, ShowInTaskbar = false, SizeToContent = SizeToContent.WidthAndHeight, Content = card,
            Left = area.Left + 40 * scale, Top = area.Top + 36 * scale, Opacity = 0, Focusable = false,
        };
        w.SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(w).Handle;
            SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        };
        w.Show();
        Sound.Play(Sfx.Notify, inBackground: true);
        UiKit.Animate(w, UIElement.OpacityProperty, 1, 250);
        var hide = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.5) };
        hide.Tick += (_, _) => { hide.Stop(); UiKit.Animate(w, UIElement.OpacityProperty, 0, 300, w.Close); };
        hide.Start();
        Log.Info($"In-game notification: {title}");
    }
}
