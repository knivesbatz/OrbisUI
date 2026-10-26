using System.Windows;
using System.Windows.Controls;
using OrbisUI.Audio;
using OrbisUI.Input;

namespace OrbisUI.UI;

/// <summary>A full-stage page. Screens own their own selection state; there is no WPF keyboard focus involved.</summary>
public abstract class Screen : Grid
{
    protected Screen()
    {
        Width = UiKit.W;
        Height = UiKit.H;
    }

    /// <summary>Overlay screens (menus, dialogs, keyboard) keep the screen below visible.</summary>
    public virtual bool IsOverlay => false;

    /// <summary>Return true if the press was handled.</summary>
    public abstract bool OnInput(Btn b);

    public virtual void OnShown() { }
    public virtual void OnHidden() { }
    public virtual void OnResumed() { }

    /// <summary>Called by the shell when typed text arrives (physical keyboard).</summary>
    public virtual void OnText(string text) { }

    protected static void Play(Sfx s) => Sound.Play(s);

    /// <summary>Standard page chrome: title at top-left with a rule under it and a hint bar at the bottom.</summary>
    protected Grid Chrome(string title, params (Btn, string)[] hints)
    {
        var root = new Grid();
        // Measured from the PS4 Settings screen: title at x 105, rules at y 136 and 135 px above the bottom.
        var t = UiKit.Label(title, 54);
        t.Margin = new Thickness(105, 52, 0, 0);
        t.VerticalAlignment = VerticalAlignment.Top;
        root.Children.Add(t);
        root.Children.Add(new Border { Height = 1, Background = UiKit.Line, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 136, 0, 0) });
        root.Children.Add(new Border { Height = 1, Background = UiKit.Line, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 135) });
        var h = UiKit.Hints(hints.Length > 0 ? hints : new[] { (Btn.Cross, "Enter"), (Btn.Circle, "Back") });
        h.Margin = new Thickness(105, 0, 0, 82);
        h.VerticalAlignment = VerticalAlignment.Bottom;
        root.Children.Add(h);
        Children.Add(root);
        return root;
    }
}
