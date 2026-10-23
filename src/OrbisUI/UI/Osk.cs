using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OrbisUI.Audio;
using OrbisUI.Input;

namespace OrbisUI.UI;

/// <summary>On-screen keyboard in the console layout. ✕ types, □ deletes, △ space, L1/R1 move the cursor, R2/OPTIONS done, ○ cancels.
/// A physical keyboard (including Ctrl+V) also types into it.</summary>
public sealed class Osk : Screen
{
    static readonly string[][] Letters =
    {
        new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" },
        new[] { "q", "w", "e", "r", "t", "y", "u", "i", "o", "p" },
        new[] { "a", "s", "d", "f", "g", "h", "j", "k", "l", "'" },
        new[] { "z", "x", "c", "v", "b", "n", "m", ",", ".", "?" },
    };
    static readonly string[][] Symbols =
    {
        new[] { "!", "@", "#", "$", "%", "^", "&", "*", "(", ")" },
        new[] { "-", "_", "=", "+", "[", "]", "{", "}", "\\", "|" },
        new[] { ";", ":", "\"", "/", "<", ">", "~", "`", "€", "£" },
        new[] { ".com", "https://", "www.", "/", ":", "-", "_", "@", ".", "?" },
    };
    // Bottom row: shift, symbols, space (4 wide), left, right, backspace, done (2 wide) = 11 columns.
    static readonly string[] Bottom = { "⇧", "@#:", "Space", "Space", "Space", "Space", "◀", "▶", "⌫", "Done", "Done" };

    public override bool IsOverlay => true;

    readonly Action<string> _done;
    readonly bool _secret;
    string _text;
    int _cursor;
    bool _shift, _sym;
    int _r = 1, _c;
    readonly TextBlock _field = new() { FontSize = 34, FontFamily = UiKit.FontRegular, Foreground = Brushes.White, TextTrimming = TextTrimming.None };
    readonly Grid _keys = new();

    public static void Ask(string title, string initial, Action<string> done, bool secret = false) => Shell.Push(new Osk(title, initial, done, secret));

    Osk(string title, string initial, Action<string> done, bool secret)
    {
        _done = done; _secret = secret; _text = initial ?? ""; _cursor = _text.Length;
        Background = UiKit.B("#99000814");
        var panel = new StackPanel { Width = 1240, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 60) };
        var head = UiKit.Label(title, 30, UiKit.Dim);
        head.Margin = new Thickness(0, 0, 0, 10);
        panel.Children.Add(head);
        panel.Children.Add(new Border { Background = UiKit.B("#EE0A1C4A"), BorderBrush = UiKit.Focus, BorderThickness = new Thickness(2), Padding = new Thickness(18, 10, 18, 10), Child = _field });
        _keys.Margin = new Thickness(0, 16, 0, 12);
        for (int i = 0; i < 11; i++) _keys.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < 5; i++) _keys.RowDefinitions.Add(new RowDefinition { Height = new GridLength(86) });
        panel.Children.Add(new Border { Background = UiKit.B("#E6123476"), Padding = new Thickness(14), Child = _keys });
        var hints = UiKit.Hints((Btn.Cross, "Enter"), (Btn.Square, "Delete"), (Btn.Triangle, "Space"), (Btn.R2, "Done"), (Btn.Circle, "Cancel"));
        hints.Margin = new Thickness(0, 16, 0, 0);
        panel.Children.Add(hints);
        Children.Add(panel);
        Render();
    }

    string KeyAt(int r, int c)
    {
        if (r == 4) return Bottom[c];
        if (c >= 10) return "⌫";
        var k = (_sym ? Symbols : Letters)[r][c];
        return _shift && k.Length == 1 ? k.ToUpperInvariant() : k;
    }

    void Render()
    {
        _keys.Children.Clear();
        for (int r = 0; r < 5; r++)
        {
            for (int c = 0; c < 11; c++)
            {
                var k = KeyAt(r, c);
                int span = 1;
                if (r == 4 && c > 0 && Bottom[c - 1] == k) continue;
                if (r == 4) { while (c + span < 11 && Bottom[c + span] == k) span++; }
                bool focused = r == _r && (c == _c || (r == 4 && _c >= c && _c < c + span));
                var cell = new Border
                {
                    Margin = new Thickness(4), Background = focused ? UiKit.B("#FFE9F4FF") : UiKit.B(c >= 10 || r == 4 ? "#33FFFFFF" : "#22FFFFFF"),
                    Child = new TextBlock
                    {
                        Text = k == "⇧" && _shift ? "⇪" : k, FontSize = k.Length > 3 ? 22 : 30, FontFamily = UiKit.FontRegular,
                        Foreground = focused ? UiKit.B("#0B2A70") : Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                Grid.SetRow(cell, r); Grid.SetColumn(cell, c); Grid.SetColumnSpan(cell, span);
                _keys.Children.Add(cell);
            }
        }
        var shown = _secret ? new string('•', _text.Length) : _text;
        _field.Text = shown.Insert(Math.Min(_cursor, shown.Length), "|");
    }

    void Type(string s) { _text = _text.Insert(_cursor, s); _cursor += s.Length; if (_shift && s.Length == 1) _shift = false; Play(Sfx.KeyPress); }
    void Backspace() { if (_cursor > 0) { _text = _text.Remove(_cursor - 1, 1); _cursor--; Play(Sfx.KeyBack); } else Play(Sfx.Error); }

    void Finish() { Play(Sfx.Decide); Shell.Pop(); _done(_text); }

    void Press()
    {
        var k = KeyAt(_r, _c);
        switch (k)
        {
            case "⇧": _shift = !_shift; Play(Sfx.Option); break;
            case "@#:": _sym = !_sym; Play(Sfx.Option); break;
            case "Space": Type(" "); break;
            case "◀": if (_cursor > 0) _cursor--; Play(Sfx.KeyMove); break;
            case "▶": if (_cursor < _text.Length) _cursor++; Play(Sfx.KeyMove); break;
            case "⌫": Backspace(); break;
            case "Done": Finish(); return;
            default: Type(k); break;
        }
        Render();
    }

    public override bool OnInput(Btn b)
    {
        switch (b)
        {
            case Btn.Up: _r = (_r + 4) % 5; break;
            case Btn.Down: _r = (_r + 1) % 5; break;
            case Btn.Left:
                if (_r == 4) { var k = Bottom[_c]; while (_c > 0 && Bottom[_c] == k) _c--; if (Bottom[_c] == k) _c = 10; }
                else _c = (_c + 10) % 11;
                break;
            case Btn.Right:
                if (_r == 4) { var k = Bottom[_c]; while (_c < 10 && Bottom[_c] == k) _c++; if (Bottom[_c] == k) _c = 0; }
                else _c = (_c + 1) % 11;
                break;
            case Btn.Cross: Press(); return true;
            case Btn.Square: Backspace(); break;
            case Btn.Triangle: Type(" "); break;
            case Btn.L1: if (_cursor > 0) _cursor--; break;
            case Btn.R1: if (_cursor < _text.Length) _cursor++; break;
            case Btn.L2: _shift = !_shift; break;
            case Btn.R2 or Btn.Options: Finish(); return true;
            case Btn.Circle: Play(Sfx.Back); Shell.Pop(); return true;
            default: return true;
        }
        if (b is Btn.Up or Btn.Down or Btn.Left or Btn.Right) Play(Sfx.KeyMove);
        Render();
        return true;
    }

    public override void OnText(string text)
    {
        foreach (var ch in text)
        {
            if (ch == '\b') Backspace();
            else if (ch == '\r' || ch == '\n') { Finish(); return; }
            else if (!char.IsControl(ch)) Type(ch.ToString());
        }
        Render();
    }
}
