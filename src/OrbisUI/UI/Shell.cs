using System.Windows;
using System.Windows.Controls;
using OrbisUI.Input;
using OrbisUI.Launch;
using OrbisUI.Library;
using OrbisUI.Metadata;

namespace OrbisUI.UI;

/// <summary>Screen stack and app-wide services, reachable from every screen.</summary>
public static class Shell
{
    public static MainWindow Window { get; internal set; } = null!;
    public static LibraryService Library { get; } = new();
    public static MetadataService Metadata { get; } = new(Library);
    public static LaunchService Launcher { get; } = new();
    public static Gamepad? Pad { get; internal set; }

    static readonly List<Screen> Stack = new();
    public static Screen? Top => Stack.Count > 0 ? Stack[^1] : null;
    public static int Depth => Stack.Count;

    public static void Push(Screen s)
    {
        var below = Top;
        Stack.Add(s);
        Window.ScreenLayer.Children.Add(s);
        if (below != null && !s.IsOverlay) foreach (var x in Stack.Take(Stack.Count - 1).Where(x => x.Visibility == Visibility.Visible)) Hide(x);
        UiKit.FadeIn(s, s.IsOverlay ? 160 : 220);
        s.OnShown();
    }

    public static void Pop()
    {
        if (Stack.Count <= 1) return;
        var s = Stack[^1];
        Stack.RemoveAt(Stack.Count - 1);
        s.OnHidden();
        UiKit.Animate(s, UIElement.OpacityProperty, 0, 140, () => Window.ScreenLayer.Children.Remove(s));
        s.IsHitTestVisible = false;
        // Re-show everything from the topmost full screen upwards.
        int from = Stack.FindLastIndex(x => !x.IsOverlay);
        for (int i = Math.Max(0, from); i < Stack.Count; i++)
        {
            var x = Stack[i];
            if (x.Visibility == Visibility.Visible) continue;
            x.Visibility = Visibility.Visible;
            UiKit.Animate(x, UIElement.OpacityProperty, 1, 160);
        }
        Stack[^1].OnResumed();
    }

    /// <summary>Pop back to the home screen.</summary>
    public static void Home()
    {
        while (Stack.Count > 1) Pop();
    }

    /// <summary>Swap the whole stack (boot -> setup -> home).</summary>
    public static void Reset(Screen s)
    {
        // Cross-fade: the old screens (e.g. the boot video) fade out underneath the new one.
        foreach (var old in Stack)
        {
            old.OnHidden();
            old.IsHitTestVisible = false;
            Panel.SetZIndex(old, 1); // fade out on top of the new screen so it is revealed underneath
            // Even ease so the fade reads as a fade rather than a cut.
            var fade = new System.Windows.Media.Animation.DoubleAnimation(0, UiKit.Dur(1200)) { EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut } };
            fade.Completed += (_, _) => Window.ScreenLayer.Children.Remove(old);
            old.BeginAnimation(UIElement.OpacityProperty, fade);
        }
        Stack.Clear();
        Push(s);
    }

    static void Hide(Screen s)
    {
        s.BeginAnimation(UIElement.OpacityProperty, null);
        s.Visibility = Visibility.Hidden;
        s.OnHidden();
    }

    public static bool Dispatch(Btn b) => Top?.OnInput(b) ?? false;

    public static T? Find<T>() where T : Screen => Stack.OfType<T>().FirstOrDefault();

    public static void Toast(string title, string body = "", string glyph = "î¥†") => Services.Notifications.Post(title, body, glyph);

    /// <summary>Yes/No confirmation in the console's dialog style.</summary>
    public static void Confirm(string title, string message, Action yes, string yesText = "Yes", string noText = "No")
    {
        Audio.Sound.Play(Audio.Sfx.DialogShow);
        ListScreen? dlg = null;
        dlg = new ListScreen(title, new[]
        {
            new ListItem { Glyph = "îœ¾", Label = yesText, Activate = () => { Pop(); yes(); } },
            new ListItem { Glyph = "îœ‘", Label = noText, Activate = () => Pop() },
        }, dialog: true, subtitle: message, select: 1);
        Push(dlg);
    }

    public static void Message(string title, string message)
    {
        Audio.Sound.Play(Audio.Sfx.DialogShow);
        Push(new ListScreen(title, new[] { new ListItem { Glyph = "îœ¾", Label = "OK", Activate = () => Pop() } }, dialog: true, subtitle: message));
    }
}
