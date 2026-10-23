using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OrbisUI.Core;
using OrbisUI.Library;
using OrbisUI.Metadata;

namespace OrbisUI.UI;

public enum HomeKind { Game, Folder, Library, App, Add, Store, WhatsNew }

public sealed class HomeItem
{
    public HomeKind Kind { get; init; }
    public Game? Game { get; init; }
    public AppFolder? Folder { get; init; }
    public AppEntry? App { get; init; }
    public string Title { get; init; } = "";
    public string? Glyph { get; init; }
    /// <summary>Icon-pack tile image for system tiles (TV &amp; Video, Library).</summary>
    public string? IconKey { get; init; }
    public string Id => Game?.Id ?? $"{Kind}:{Title}";
}

/// <summary>Square tiles: artwork when available, otherwise an original generated tile (gradient, title, platform badge).</summary>
public static class Tiles
{
    static readonly Dictionary<string, string[]> PlatformColors = new()
    {
        ["PC"] = new[] { "#2A3550", "#11182A" }, ["PS1"] = new[] { "#5B5F66", "#2A2C30" }, ["PS2"] = new[] { "#1B2A6B", "#0A1235" },
        ["PS3"] = new[] { "#3A3A3A", "#111111" }, ["PS4"] = new[] { "#0F4BB8", "#062460" }, ["PSP"] = new[] { "#4A4A55", "#1C1C22" },
        ["VITA"] = new[] { "#1E5DB0", "#0C2B5C" },
    };

    // PC games are presented as PS4 titles so the shell reads like a console.
    public static string PlatformLabel(string p) => p switch { "VITA" => "PS Vita", "PC" => "PS4", _ => p };

    public static FrameworkElement Make(HomeItem it, double size)
    {
        var g = new Grid { Width = size, Height = size, ClipToBounds = true };
        string? art = it.Game?.Art ?? it.App?.ArtPath;
        var src = ImageTools.LoadForDisplay(art, 384);
        if (src != null)
        {
            g.Children.Add(new Image { Source = src, Stretch = Stretch.UniformToFill });
            return g;
        }
        if (it.Kind == HomeKind.Game && it.Game != null) return Fallback(g, it.Game.Platform, LibraryService.DisplayTitle(it.Game), size);

        if (it.IconKey != null && IconPack.Get(it.IconKey, 384) is ImageSource tileImg)
        {
            g.Children.Add(new Image { Source = tileImg, Stretch = Stretch.UniformToFill });
            return g;
        }
        // System tiles (folders, library, apps): translucent panel with a large icon, like the console's app tiles.
        g.Background = new LinearGradientBrush(UiKit.C("#5A8BD8FF"), UiKit.C("#2A5AB0FF"), 90);
        var glyph = UiKit.Glyph(it.Glyph ?? "", size * 0.36);
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        g.Children.Add(glyph);
        g.Children.Add(new Border { BorderBrush = UiKit.B("#55FFFFFF"), BorderThickness = new Thickness(1) });
        return g;
    }

    static FrameworkElement Fallback(Grid g, string platform, string title, double size)
    {
        var c = PlatformColors.GetValueOrDefault(platform, PlatformColors["PC"]);
        g.Background = new LinearGradientBrush(UiKit.C(c[0]), UiKit.C(c[1]), 60);
        var t = new TextBlock
        {
            Text = title, Foreground = Brushes.White, FontFamily = UiKit.Font, FontSize = size * 0.11, TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(size * 0.08), MaxHeight = size * 0.7, TextTrimming = TextTrimming.WordEllipsis,
        };
        g.Children.Add(t);
        var badge = new Border
        {
            Background = UiKit.B("#AA000000"), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(size * 0.05),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
            Child = new TextBlock { Text = PlatformLabel(platform), Foreground = Brushes.White, FontSize = size * 0.065, FontFamily = UiKit.FontRegular },
        };
        g.Children.Add(badge);
        return g;
    }
}
