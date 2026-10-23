using System.Windows;
using System.Windows.Controls;
using OrbisUI.Input;

namespace OrbisUI.UI;

/// <summary>Shows every button and stick as it is pressed. Hold ○ for one second to leave (so ○ itself can be tested).</summary>
public sealed class ControllerTestScreen : Screen
{
    readonly Dictionary<Btn, Border> _cells = new();
    readonly TextBlock _name = UiKit.Label("", 30);
    readonly TextBlock _axes = UiKit.Label("", 26, UiKit.Dim);
    readonly float[] _axis = new float[6];
    DateTime _circleDown = DateTime.MaxValue;

    public ControllerTestScreen()
    {
        Chrome("Controller Test", (Btn.Circle, "Hold to go back"));
        var wrap = new WrapPanel { Margin = new Thickness(150, 230, 150, 0), VerticalAlignment = VerticalAlignment.Top };
        foreach (Btn b in Enum.GetValues<Btn>())
        {
            var cell = new Border { Width = 190, Height = 90, Margin = new Thickness(8), Background = UiKit.Panel, BorderBrush = UiKit.Line, BorderThickness = new Thickness(1) };
            var t = UiKit.Label(b.ToString(), 26);
            t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center;
            cell.Child = t;
            _cells[b] = cell;
            wrap.Children.Add(cell);
        }
        Children.Add(wrap);
        _name.Margin = new Thickness(158, 140, 0, 0); _name.VerticalAlignment = VerticalAlignment.Top;
        Children.Add(_name);
        _axes.Margin = new Thickness(158, 720, 0, 0); _axes.VerticalAlignment = VerticalAlignment.Top;
        Children.Add(_axes);
        UpdateName(Shell.Pad?.PadName);
    }

    void UpdateName(string? n) => _name.Text = n == null ? "No controller connected. Keyboard keys are shown instead." : n;

    void OnAxis(int a, float v)
    {
        if (a < 6) _axis[a] = v;
        Dispatcher.BeginInvoke(() => _axes.Text = $"Left stick  {_axis[0],5:0.00} {_axis[1],5:0.00}     Right stick  {_axis[2],5:0.00} {_axis[3],5:0.00}     L2 {_axis[4]:0.00}   R2 {_axis[5]:0.00}");
    }

    void OnConnected(string? n) => Dispatcher.BeginInvoke(() => UpdateName(n));

    public override void OnShown()
    {
        if (Shell.Pad != null) { Shell.Pad.Axis += OnAxis; Shell.Pad.Connected += OnConnected; }
        Shell.Window.Router.Released += OnReleased;
    }

    public override void OnHidden()
    {
        if (Shell.Pad != null) { Shell.Pad.Axis -= OnAxis; Shell.Pad.Connected -= OnConnected; }
        Shell.Window.Router.Released -= OnReleased;
    }

    void OnReleased(Btn b)
    {
        if (_cells.TryGetValue(b, out var c)) c.Background = UiKit.Panel;
        if (b == Btn.Circle) _circleDown = DateTime.MaxValue;
    }

    public override bool OnInput(Btn b)
    {
        if (_cells.TryGetValue(b, out var c)) c.Background = UiKit.FocusFill;
        if (b == Btn.Circle)
        {
            if (_circleDown == DateTime.MaxValue) _circleDown = DateTime.Now;
            var started = _circleDown;
            Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(1000);
                if (_circleDown == started && Shell.Top == this) { Play(Audio.Sfx.Back); Shell.Pop(); }
            });
        }
        return true;
    }
}
