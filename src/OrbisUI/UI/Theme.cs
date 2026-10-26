using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;
using Path = System.Windows.Shapes.Path;
using OrbisUI.Core;
using OrbisUI.Input;

namespace OrbisUI.UI;

/// <summary>
/// Colour themes like the console's: each one rotates the hue (and can lower saturation or brightness) of the royal-blue look.
/// UI colours are shifted when they are created (<see cref="UiKit.C"/>); videos get <see cref="HueEffect"/>, a pixel shader
/// compiled at start-up with Windows' own d3dcompiler_47.dll, so nothing is re-encoded and no shader tools are needed.
/// </summary>
public static class Theme
{
    public sealed record Def(string Name, double Hue, double Sat, double Bright, string Swatch);

    public static readonly Def[] All =
    {
        new("Royal Blue", 0, 1, 1, "#1A52C8"),
        new("Gold", 168, 1, 1.6, "#C08E00"),
        new("Dark Blue", 0, 0.85, 0.55, "#053C7A"),
        new("Red", -138, 1.05, 0.9, "#9C0A14"),
        new("Teal", 32, 0.45, 1.15, "#6A9EAA"),
        new("Purple", -48, 0.8, 1.05, "#7F5ED6"),
        new("Pink", -132, 0.6, 1.15, "#C25A70"),
        new("Gray", 0, 0.08, 0.8, "#4A4C56"),
    };

    public static Def Current => All.FirstOrDefault(t => t.Name == AppSettings.Current.Theme) ?? All[0];

    /// <summary>Shifts a blue accent colour into the theme; whites, greys and non-blue colours pass through.</summary>
    public static Color Shift(Color c) => Shift(c, Current);

    public static Color Shift(Color c, Def t)
    {
        if (t.Hue == 0 && t.Sat == 1 && t.Bright == 1) return c;
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        if (d < 0.12 || b < max) return c; // not saturated, or not blue-dominant: text, lines, art
        // Same YIQ rotation as the shader, so UI and video stay in step.
        var (nr, ng, nb) = Rotate(r, g, b, t);
        return Color.FromArgb(c.A, To8(nr), To8(ng), To8(nb));
    }

    static byte To8(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);

    internal static (double, double, double) Rotate(double r, double g, double b, Def t)
    {
        double y = 0.299 * r + 0.587 * g + 0.114 * b, i = 0.596 * r - 0.274 * g - 0.322 * b, q = 0.211 * r - 0.523 * g + 0.312 * b;
        double a = t.Hue * Math.PI / 180, cs = Math.Cos(a), sn = Math.Sin(a);
        double i2 = (i * cs - q * sn) * t.Sat, q2 = (i * sn + q * cs) * t.Sat;
        y *= t.Bright;
        return (y + 0.956 * i2 + 0.621 * q2, y - 0.272 * i2 - 0.647 * q2, y - 1.106 * i2 + 1.703 * q2);
    }

    /// <summary>The theme's video effect, or null for the default theme (no shader cost).</summary>
    public static Effect? VideoEffect() => Current.Name == All[0].Name ? null : HueEffect.Create(Current);

    // ---- Settings > Themes: the console's colour grid (4 x 2 swatches, check on the current one) ----
    public static Screen Picker() => new PickerScreen();

    sealed class PickerScreen : Screen
    {
        // Measured from the console's Themes screen, scaled to 1920x1080.
        const double X0 = 320, Y0 = 418, SwW = 290, SwH = 163, PitchX = 331, PitchY = 203;
        readonly Canvas _c = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        readonly Border _focus = UiKit.FocusFrame(3);
        int _sel = Math.Max(0, Array.IndexOf(All, Current));

        public PickerScreen()
        {
            Chrome("Themes", (Btn.Cross, "Enter"), (Btn.Circle, "Back"));
            Children.Add(_c);
            Render();
        }

        void Render()
        {
            _c.Children.Clear();
            var hint = UiKit.Label("Select a color.", 30);
            hint.Width = UiKit.W; hint.TextAlignment = TextAlignment.Center;
            Canvas.SetTop(hint, 300); _c.Children.Add(hint);
            for (int i = 0; i < All.Length; i++)
            {
                var t = All[i];
                var sw = new Grid { Width = SwW, Height = SwH, Background = UiKit.Raw(t.Swatch) };
                sw.Children.Add(new Path { Data = Geometry.Parse("M0,90 C70,40 120,40 170,70 S250,90 290,50 L290,163 L0,163 Z"), Fill = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)) });
                if (t == Current) { var ck = UiKit.Glyph("", 34); ck.HorizontalAlignment = HorizontalAlignment.Right; ck.VerticalAlignment = VerticalAlignment.Bottom; ck.Margin = new Thickness(0, 0, 14, 10); sw.Children.Add(ck); }
                var border = new Border { BorderBrush = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), BorderThickness = new Thickness(1), Child = sw };
                Canvas.SetLeft(border, X0 + (i % 4) * PitchX); Canvas.SetTop(border, Y0 + (i / 4) * PitchY);
                _c.Children.Add(border);
            }
            _focus.Width = SwW + 8; _focus.Height = SwH + 8;
            Canvas.SetLeft(_focus, X0 + (_sel % 4) * PitchX - 4); Canvas.SetTop(_focus, Y0 + (_sel / 4) * PitchY - 4);
            _c.Children.Add(_focus);
        }

        public override bool OnInput(Btn b)
        {
            int n = _sel;
            switch (b)
            {
                case Btn.Left when _sel % 4 > 0: n--; break;
                case Btn.Right when _sel % 4 < 3: n++; break;
                case Btn.Up when _sel >= 4: n -= 4; break;
                case Btn.Down when _sel < 4: n += 4; break;
                case Btn.Cross:
                    Play(Audio.Sfx.Decide);
                    AppSettings.Current.Theme = All[_sel].Name;
                    AppSettings.Current.Save();
                    Shell.Window.ApplyTheme();
                    return true;
                case Btn.Circle: Play(Audio.Sfx.Back); Shell.Pop(); return true;
                default: return true;
            }
            if (n != _sel) { _sel = n; Play(Audio.Sfx.Cursor); Render(); }
            return true;
        }
    }
}

/// <summary>YIQ hue rotation with saturation and brightness, as a WPF pixel shader compiled at run time.</summary>
public sealed class HueEffect : ShaderEffect
{
    const string Hlsl = @"
sampler2D input : register(s0);
float4 p : register(c0); // x = cos(hue), y = sin(hue), z = saturation, w = brightness
float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 c = tex2D(input, uv);
    float y = dot(c.rgb, float3(0.299, 0.587, 0.114));
    float i = dot(c.rgb, float3(0.596, -0.274, -0.322));
    float q = dot(c.rgb, float3(0.211, -0.523, 0.312));
    float i2 = (i * p.x - q * p.y) * p.z;
    float q2 = (i * p.y + q * p.x) * p.z;
    y *= p.w;
    float3 o = float3(y + 0.956 * i2 + 0.621 * q2, y - 0.272 * i2 - 0.647 * q2, y - 1.106 * i2 + 1.703 * q2);
    return float4(saturate(o), c.a);
}";

    static PixelShader? _shader;
    static bool _failed;

    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty("Input", typeof(HueEffect), 0);
    public static readonly DependencyProperty ParamsProperty = DependencyProperty.Register("Params", typeof(Point4D), typeof(HueEffect),
        new UIPropertyMetadata(new Point4D(1, 0, 1, 1), PixelShaderConstantCallback(0)));

    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    public Point4D Params { get => (Point4D)GetValue(ParamsProperty); set => SetValue(ParamsProperty, value); }

    HueEffect(PixelShader s) { PixelShader = s; UpdateShaderValue(InputProperty); UpdateShaderValue(ParamsProperty); }

    public static HueEffect? Create(Theme.Def t)
    {
        if (Compile() is not PixelShader s) return null;
        double a = t.Hue * Math.PI / 180;
        return new HueEffect(s) { Params = new Point4D(Math.Cos(a), Math.Sin(a), t.Sat, t.Bright) };
    }

    static PixelShader? Compile()
    {
        if (_shader != null || _failed) return _shader;
        try
        {
            int hr = D3DCompile(Hlsl, (IntPtr)Hlsl.Length, "hue", IntPtr.Zero, IntPtr.Zero, "main", "ps_2_0", 0, 0, out var code, out var errors);
            if (hr < 0 || code == null)
            {
                Log.Warn("Theme shader didn't compile: " + (errors != null ? Marshal.PtrToStringAnsi(errors.GetBufferPointer()) : hr.ToString("X")));
                _failed = true; return null;
            }
            var bytes = new byte[(int)code.GetBufferSize()];
            Marshal.Copy(code.GetBufferPointer(), bytes, 0, bytes.Length);
            var s = new PixelShader();
            s.SetStreamSource(new MemoryStream(bytes));
            s.Freeze();
            return _shader = s;
        }
        catch (Exception ex) { Log.Warn("Theme shader: " + ex.Message); _failed = true; return null; }
    }

    [ComImport, Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ID3DBlob { [PreserveSig] IntPtr GetBufferPointer(); [PreserveSig] IntPtr GetBufferSize(); }

    [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi)]
    static extern int D3DCompile([MarshalAs(UnmanagedType.LPStr)] string src, IntPtr len, [MarshalAs(UnmanagedType.LPStr)] string name,
        IntPtr defines, IntPtr include, [MarshalAs(UnmanagedType.LPStr)] string entry, [MarshalAs(UnmanagedType.LPStr)] string target,
        uint flags1, uint flags2, out ID3DBlob code, out ID3DBlob errors);
}
