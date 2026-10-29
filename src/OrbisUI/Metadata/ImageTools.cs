using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OrbisUI.Metadata;

/// <summary>Turns any artwork into a square tile without distortion: near-square art is centre-cropped, anything else is
/// fitted over a blurred, darkened fill of itself (like the console does for odd-shaped icons).</summary>
public static class ImageTools
{
    public const int TileSize = 512;

    public static BitmapSource Load(byte[] data)
    {
        using var ms = new MemoryStream(data);
        var frame = BitmapFrame.Create(ms, BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        bgra.Freeze();
        return bgra;
    }

    static BitmapSource Scale(BitmapSource src, double sx, double sy)
    {
        var t = new TransformedBitmap(src, new ScaleTransform(sx, sy));
        t.Freeze();
        return t;
    }

    static int[] Pixels(BitmapSource b)
    {
        var px = new int[b.PixelWidth * b.PixelHeight];
        b.CopyPixels(px, b.PixelWidth * 4, 0);
        return px;
    }

    /// <summary>Returns JPEG bytes of a TileSize square.</summary>
    public static byte[] MakeSquare(byte[] data) => Encode(Square(Load(data)), jpeg: true);

    public static BitmapSource Square(BitmapSource src) => Fit(src, TileSize, TileSize);

    /// <summary>Fits art into a W x H frame: near-same-shape art is cropped to cover, anything else sits over a blurred fill.</summary>
    public static BitmapSource Fit(BitmapSource src, int W, int H)
    {
        double w = src.PixelWidth, h = src.PixelHeight;
        double aspect = w / h / ((double)W / H);
        var outPx = new int[W * H];

        if (aspect is > 0.9 and < 1.11)
        {
            // Close enough: scale to cover, crop the few overhanging pixels.
            double s = Math.Max(W / w, H / h);
            var scaled = Scale(src, s, s);
            var p = Pixels(scaled);
            int sw = scaled.PixelWidth, sh = scaled.PixelHeight;
            int ox = (sw - W) / 2, oy = (sh - H) / 2;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int sx2 = Math.Clamp(x + ox, 0, sw - 1), sy2 = Math.Clamp(y + oy, 0, sh - 1);
                    outPx[y * W + x] = p[sy2 * sw + sx2] | unchecked((int)0xFF000000);
                }
            return Make(outPx, W, H);
        }

        // Blurred fill: shrink to a few pixels, bilinear back up (cover-scaled, centred).
        const int tiny = 12;
        double cover = tiny / Math.Min(w, h);
        var small = Scale(src, cover, cover);
        var sp = Pixels(small);
        int tw = small.PixelWidth, th = small.PixelHeight;
        double k = Math.Min((double)tw / W, (double)th / H);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                double fx = (x + 0.5) * k - 0.5 + (tw - W * k) / 2.0;
                double fy = (y + 0.5) * k - 0.5 + (th - H * k) / 2.0;
                outPx[y * W + x] = Darken(Bilinear(sp, tw, th, fx, fy), 0.45);
            }

        // Fitted original on top, alpha blended.
        double fit = Math.Min(W / w, H / h);
        var fg = Scale(src, fit, fit);
        var fp = Pixels(fg);
        int fw = fg.PixelWidth, fh = fg.PixelHeight;
        int x0 = (W - fw) / 2, y0 = (H - fh) / 2;
        for (int y = 0; y < fh && y + y0 < H; y++)
            for (int x = 0; x < fw && x + x0 < W; x++)
            {
                int i = (y + y0) * W + x + x0;
                outPx[i] = Blend(outPx[i], fp[y * fw + x]);
            }
        return Make(outPx, W, H);
    }

    static BitmapSource Make(int[] px, int w, int h)
    {
        var b = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        b.Freeze();
        return b;
    }

    /// <summary>Transparent PNG of the title in white with a soft shadow, for a Steam library logo. Rendered on its own STA thread
    /// because WPF drawing needs one and this is called from background tasks.</summary>
    public static byte[] TitleLogo(string title)
    {
        byte[] png = [];
        var t = new Thread(() =>
        {
            var text = new FormattedText(title, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 96, Brushes.White, 1)
            { MaxTextWidth = 1200, TextAlignment = TextAlignment.Center, MaxLineCount = 2, Trimming = TextTrimming.CharacterEllipsis };
            var v = new DrawingVisual { Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.8 } };
            using (var dc = v.RenderOpen()) dc.DrawText(text, new Point(20, 20));
            var rtb = new RenderTargetBitmap(1240, (int)Math.Ceiling(text.Height) + 40, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(v);
            png = Encode(rtb, jpeg: false);
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        return png;
    }
    static int Bilinear(int[] p, int w, int h, double fx, double fy)
    {
        fx = Math.Clamp(fx, 0, w - 1); fy = Math.Clamp(fy, 0, h - 1);
        int x0 = (int)fx, y0 = (int)fy, x1 = Math.Min(x0 + 1, w - 1), y1 = Math.Min(y0 + 1, h - 1);
        double ax = fx - x0, ay = fy - y0;
        int c00 = p[y0 * w + x0], c10 = p[y0 * w + x1], c01 = p[y1 * w + x0], c11 = p[y1 * w + x1];
        int r = 0;
        for (int sh = 0; sh < 24; sh += 8)
        {
            double a = ((c00 >> sh) & 255) * (1 - ax) + ((c10 >> sh) & 255) * ax;
            double b = ((c01 >> sh) & 255) * (1 - ax) + ((c11 >> sh) & 255) * ax;
            r |= ((int)(a * (1 - ay) + b * ay) & 255) << sh;
        }
        return r | unchecked((int)0xFF000000);
    }

    static int Darken(int c, double k)
    {
        int r = (int)(((c >> 16) & 255) * k), g = (int)(((c >> 8) & 255) * k), b = (int)((c & 255) * k);
        return unchecked((int)0xFF000000) | r << 16 | g << 8 | b;
    }

    static int Blend(int dst, int src)
    {
        int a = (src >> 24) & 255;
        if (a == 255) return src;
        if (a == 0) return dst;
        int Mix(int sh) => (((src >> sh) & 255) * a + ((dst >> sh) & 255) * (255 - a)) / 255;
        return unchecked((int)0xFF000000) | Mix(16) << 16 | Mix(8) << 8 | Mix(0);
    }

    /// <summary>Background art: at most 1600 px wide, JPEG.</summary>
    public static byte[] MakeBackground(byte[] data)
    {
        var src = Load(data);
        double s = Math.Min(1.0, 1600.0 / src.PixelWidth);
        return Encode(s < 1 ? Scale(src, s, s) : src, jpeg: true);
    }

    public static byte[] Encode(BitmapSource b, bool jpeg)
    {
        BitmapEncoder enc = jpeg ? new JpegBitmapEncoder { QualityLevel = 90 } : new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(b));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>Decode an image for display at a bounded size so the home row stays light on RAM.</summary>
    public static ImageSource? LoadForDisplay(string? path, int decodeWidth)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bi.DecodePixelWidth = decodeWidth;
            bi.UriSource = new Uri(path);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }
}
