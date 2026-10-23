using System.Windows.Input;
using System.Windows.Threading;

namespace OrbisUI.Input;

/// <summary>
/// Merges controller and keyboard into one stream of <see cref="Btn"/> presses on the UI thread,
/// with console-style auto-repeat for held directions. The repeat timer only runs while a direction is held.
/// </summary>
public sealed class InputRouter
{
    readonly Dispatcher _ui;
    readonly DispatcherTimer _repeat;
    Btn? _held;
    int _repeatCount;

    public event Action<Btn>? Pressed;
    public event Action<Btn>? Released;

    public InputRouter(Dispatcher ui)
    {
        _ui = ui;
        _repeat = new DispatcherTimer(DispatcherPriority.Input, ui) { Interval = TimeSpan.FromMilliseconds(380) };
        _repeat.Tick += (_, _) =>
        {
            if (_held is not Btn b) { _repeat.Stop(); return; }
            _repeatCount++;
            // Accelerate the longer it is held, like scrolling a long list on the console.
            _repeat.Interval = TimeSpan.FromMilliseconds(_repeatCount > 8 ? 55 : 110);
            Pressed?.Invoke(b);
        };
    }

    static bool IsDirection(Btn b) => b is Btn.Up or Btn.Down or Btn.Left or Btn.Right;

    /// <summary>Called from the gamepad thread.</summary>
    public void FromPad(Btn b, bool down) => _ui.BeginInvoke(DispatcherPriority.Input, () => Handle(b, down));

    void Handle(Btn b, bool down)
    {
        if (down)
        {
            Pressed?.Invoke(b);
            if (IsDirection(b))
            {
                _held = b;
                _repeatCount = 0;
                _repeat.Interval = TimeSpan.FromMilliseconds(380);
                _repeat.Start();
            }
        }
        else
        {
            if (_held == b) { _held = null; _repeat.Stop(); }
            Released?.Invoke(b);
        }
    }

    /// <summary>Keyboard fallback for troubleshooting. Windows already auto-repeats keys, so no timer here.</summary>
    public bool FromKey(Key key, bool isRepeat)
    {
        Btn? b = key switch
        {
            Key.Up => Btn.Up, Key.Down => Btn.Down, Key.Left => Btn.Left, Key.Right => Btn.Right,
            Key.Enter or Key.Space => Btn.Cross,
            Key.Escape or Key.Back => Btn.Circle,
            Key.F2 => Btn.Square, Key.F3 => Btn.Triangle,
            Key.Tab or Key.F1 or Key.Apps => Btn.Options,
            Key.PageUp => Btn.L1, Key.PageDown => Btn.R1,
            Key.Home => Btn.PS,
            _ => null,
        };
        if (b is not Btn btn) return false;
        if (isRepeat && !IsDirection(btn)) return true;
        Pressed?.Invoke(btn);
        return true;
    }
}
