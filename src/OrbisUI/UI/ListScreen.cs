using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OrbisUI.Audio;
using OrbisUI.Input;

namespace OrbisUI.UI;

public sealed class ListItem
{
    public string Glyph { get; init; } = "";
    public required string Label { get; init; }
    public Func<string?>? Sub { get; init; }
    public Func<string?>? Value { get; init; }
    public Func<bool>? Check { get; init; }
    public Action? Activate { get; init; }
    /// <summary>Left/right changes the value in place (sliders, choices).</summary>
    public Action<int>? Adjust { get; init; }
    public Func<bool>? Enabled { get; init; }
    public bool IsHeader { get; init; }
}

/// <summary>The console's settings-style list: icon, label, value or checkbox, cyan focus bar. Also used as a centred dialog menu.</summary>
public class ListScreen : Screen
{
    readonly List<ListItem> _items;
    readonly StackPanel _rows = new();
    readonly TranslateTransform _scroll = new();
    readonly Border _focus = UiKit.FocusFrame();
    readonly bool _dialog, _side;
    readonly double _rowH;
    readonly double _viewH;
    int _sel;
    public Action? OnBack { get; set; }
    public Action<ListItem>? OnOptions { get; set; }
    public int Selected => _sel;

    public override bool IsOverlay => _dialog || _side;

    /// <param name="side">The console's options panel: a full-height black panel on the right, text only, white focus box.</param>
    public ListScreen(string title, IEnumerable<ListItem> items, bool dialog = false, string? subtitle = null, int select = 0, (Btn, string)[]? hints = null, bool side = false)
    {
        _items = items.ToList();
        _dialog = dialog && !side;
        _side = side;
        dialog = _dialog;
        _rowH = side ? 74 : dialog ? 78 : 84;
        double listW = side ? 444 : dialog ? 820 : 1340, left = dialog ? (UiKit.W - 820) / 2 : 290, top = dialog ? 0 : 150;
        _viewH = side ? Math.Min(_items.Count, 11) * _rowH : dialog ? Math.Min(_items.Count, 8) * _rowH : 820;

        FrameworkElement host;
        var viewport = new Canvas { Width = listW, Height = _viewH, ClipToBounds = true };
        _rows.RenderTransform = _scroll;
        _rows.Width = listW;
        viewport.Children.Add(_rows);
        _focus.Width = listW; _focus.Height = _rowH - 6; _focus.RenderTransform = new TranslateTransform();
        viewport.Children.Add(_focus);

        if (side)
        {
            // Measured from the menu tour (8:44): panel from x 1352, first row centred at y 280, rows 74 px apart.
            _focus.BorderBrush = Brushes.White; _focus.BorderThickness = new Thickness(2); _focus.Effect = null;
            _focus.Width = listW; _focus.Height = _rowH + 6;
            viewport.Margin = new Thickness(8, 280 - _rowH / 2 - 6, 0, 0);
            viewport.Height = _viewH + 12;
            var panel = new Grid { Width = UiKit.W - 1352, HorizontalAlignment = HorizontalAlignment.Right, Background = UiKit.B("#F2000000"), RenderTransform = new TranslateTransform(UiKit.W - 1352, 0) };
            viewport.HorizontalAlignment = HorizontalAlignment.Left; viewport.VerticalAlignment = VerticalAlignment.Top;
            panel.Children.Add(viewport);
            Children.Add(panel);
            UiKit.Animate((TranslateTransform)panel.RenderTransform, TranslateTransform.XProperty, 0, 180);
        }
        else if (dialog)
        {
            Background = UiKit.B("#99000814");
            var panel = new StackPanel { Width = listW + 120, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var head = UiKit.Label(title, 40);
            head.Margin = new Thickness(60, 40, 0, 6);
            panel.Children.Add(head);
            if (subtitle != null) { var st = UiKit.Label(subtitle, 24, UiKit.Dim); st.Margin = new Thickness(60, 0, 60, 10); st.TextWrapping = TextWrapping.Wrap; panel.Children.Add(st); }
            panel.Children.Add(new Border { Height = 1, Background = UiKit.Line, Margin = new Thickness(0, 10, 0, 20) });
            viewport.Margin = new Thickness(60, 0, 60, 20);
            panel.Children.Add(viewport);
            var hb = UiKit.Hints(hints ?? new[] { (Btn.Cross, "Enter"), (Btn.Circle, "Back") });
            hb.Margin = new Thickness(60, 10, 0, 34);
            panel.Children.Add(hb);
            host = new Border
            {
                Child = panel, Background = new LinearGradientBrush(UiKit.C("#F2163C9E"), UiKit.C("#F20B2466"), 90),
                BorderBrush = UiKit.Line, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            Children.Add(host);
        }
        else
        {
            Chrome(title, hints ?? new[] { (Btn.Cross, "Enter"), (Btn.Circle, "Back") });
            viewport.HorizontalAlignment = HorizontalAlignment.Left;
            viewport.VerticalAlignment = VerticalAlignment.Top;
            viewport.Margin = new Thickness(left, top, 0, 0);
            if (subtitle != null)
            {
                var st = UiKit.Label(subtitle, 24, UiKit.Dim);
                st.Margin = new Thickness(left, 122, 0, 0); st.VerticalAlignment = VerticalAlignment.Top;
                Children.Add(st);
                viewport.Margin = new Thickness(left, top + 20, 0, 0);
            }
            Children.Add(viewport);
        }
        _sel = Math.Clamp(select, 0, Math.Max(0, _items.Count - 1));
        if (_items.Count > 0 && _items[_sel].IsHeader) Move(1, silent: true);
        Build();
    }

    void Build()
    {
        _rows.Children.Clear();
        foreach (var it in _items) _rows.Children.Add(Row(it));
        Place(false);
    }

    FrameworkElement Row(ListItem it)
    {
        bool enabled = it.Enabled?.Invoke() ?? true;
        var g = new Grid { Height = _rowH, Opacity = enabled ? 1 : 0.45 };
        if (it.IsHeader)
        {
            var h = UiKit.Label(it.Label, 24, UiKit.Dim);
            h.VerticalAlignment = VerticalAlignment.Bottom; h.Margin = new Thickness(14, 0, 0, 10);
            g.Children.Add(h);
            return g;
        }
        if (_side)
        {
            var t = UiKit.Label(it.Label, 28);
            t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(18, 0, 18, 0);
            g.Children.Add(t);
            return g;
        }
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = UiKit.Glyph(it.Glyph, 26);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        g.Children.Add(icon);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(UiKit.Label(it.Label, 30));
        if (it.Sub?.Invoke() is string sub && sub.Length > 0) text.Children.Add(UiKit.Label(sub, 20, UiKit.Dim));
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        FrameworkElement? right = null;
        if (it.Check != null)
        {
            bool on = it.Check();
            right = new Border
            {
                Width = 34, Height = 34, BorderBrush = UiKit.Text, BorderThickness = new Thickness(1.5), Background = UiKit.B("#22FFFFFF"),
                Child = on ? UiKit.Glyph("", 24) : null, Margin = new Thickness(0, 0, 18, 0),
            };
            if (on && right is Border rb && rb.Child is TextBlock tb) tb.HorizontalAlignment = HorizontalAlignment.Center;
        }
        else if (it.Value?.Invoke() is string v)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 18, 0) };
            if (it.Adjust != null) sp.Children.Add(UiKit.Glyph("", 16, UiKit.Dim));
            var vt = UiKit.Label(v, 26, UiKit.Dim);
            vt.Margin = new Thickness(10, 0, 10, 0); vt.MaxWidth = 620;
            sp.Children.Add(vt);
            if (it.Adjust != null) sp.Children.Add(UiKit.Glyph("", 16, UiKit.Dim));
            right = sp;
        }
        if (right != null) { right.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(right, 2); g.Children.Add(right); }
        g.Children.Add(new Border { Height = 1, Background = UiKit.B("#22FFFFFF"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(64, 0, 0, 0), IsHitTestVisible = false });
        Grid.SetColumnSpan(g.Children[^1], 3);
        return g;
    }

    public void Refresh(int? index = null)
    {
        if (index is int i && i >= 0 && i < _items.Count && i < _rows.Children.Count)
        {
            _rows.Children.RemoveAt(i);
            _rows.Children.Insert(i, Row(_items[i]));
            return;
        }
        Build();
    }

    void Place(bool animate)
    {
        if (_items.Count == 0) { _focus.Visibility = Visibility.Collapsed; return; }
        double y = _sel * _rowH;
        double off = -_scroll.Y;
        if (y < off + _rowH && _sel > 0) off = Math.Max(0, y - _rowH);
        if (y < off) off = y;
        double maxOff = Math.Max(0, _items.Count * _rowH - _viewH);
        if (y + _rowH * 2 > off + _viewH) off = Math.Min(maxOff, y + _rowH * 2 - _viewH);
        off = Math.Min(off, maxOff);
        var ft = (TranslateTransform)_focus.RenderTransform;
        if (animate)
        {
            UiKit.Animate(_scroll, TranslateTransform.YProperty, -off, 140);
            UiKit.Animate(ft, TranslateTransform.YProperty, y - off + 3, 120);
        }
        else
        {
            _scroll.BeginAnimation(TranslateTransform.YProperty, null); _scroll.Y = -off;
            ft.BeginAnimation(TranslateTransform.YProperty, null); ft.Y = y - off + 3;
        }
    }

    bool Move(int d, bool silent = false)
    {
        int n = _sel;
        do { n += d; } while (n >= 0 && n < _items.Count && _items[n].IsHeader);
        if (n < 0 || n >= _items.Count) return false;
        _sel = n;
        if (!silent) Play(Sfx.Cursor);
        Place(!silent);
        return true;
    }

    public void Select(int i) { _sel = Math.Clamp(i, 0, _items.Count - 1); Place(false); }

    public override bool OnInput(Btn b)
    {
        var it = _items.Count > 0 ? _items[_sel] : null;
        switch (b)
        {
            case Btn.Up: Move(-1); return true;
            case Btn.Down: Move(1); return true;
            case Btn.L1: for (int k = 0; k < 6; k++) Move(-1, k > 0); return true;
            case Btn.R1: for (int k = 0; k < 6; k++) Move(1, k > 0); return true;
            case Btn.Left or Btn.Right when it?.Adjust != null:
                it.Adjust(b == Btn.Left ? -1 : 1);
                Play(Sfx.Spin);
                Refresh(_sel);
                return true;
            case Btn.Cross:
                if (it == null || !(it.Enabled?.Invoke() ?? true)) { Play(Sfx.Error); return true; }
                if (it.Activate != null) { Play(it.Check != null ? Sfx.Option : Sfx.Decide); it.Activate(); Refresh(_sel); }
                else if (it.Adjust != null) { it.Adjust(1); Play(Sfx.Spin); Refresh(_sel); }
                return true;
            case Btn.Options when OnOptions != null && it != null:
                OnOptions(it); return true;
            case Btn.Circle:
                Play(Sfx.Back);
                if (OnBack != null) OnBack(); else Shell.Pop();
                return true;
        }
        return false;
    }

    public override void OnResumed() => Refresh();
}
