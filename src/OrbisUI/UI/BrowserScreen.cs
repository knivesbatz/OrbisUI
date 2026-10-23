using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using OrbisUI.Audio;
using OrbisUI.Core;
using OrbisUI.Input;

namespace OrbisUI.UI;

/// <summary>
/// Controller-friendly web browser (WebView2, the Edge runtime Windows 11 ships). The page lives in its own borderless
/// window because a native web view can't be scaled inside the shell's Viewbox; the controller keeps driving it through
/// this screen: the D-pad jumps between links and fields, Cross clicks, Circle goes back.
/// </summary>
public sealed class BrowserScreen : Screen
{
    // Spatial navigation injected into every page: picks the nearest link/field in the pressed direction.
    const string NavJs = """
        (() => { if (window.__orbis) return;
          const sel = 'a[href],button,input:not([type=hidden]),select,textarea,[role=button],[role=link],[role=tab],[tabindex]:not([tabindex="-1"]),[onclick],summary';
          let cur = null;
          // Inline styles through CSSOM still work on sites whose CSP blocks injected <style> tags.
          const mark = (e, on) => ['outline', 'outline-offset', 'box-shadow'].forEach((k, i) =>
            on ? e.style.setProperty(k, ['4px solid #4fc3ff', '2px', '0 0 16px #4fc3ff'][i], 'important') : e.style.removeProperty(k));
          const vis = e => { const r = e.getBoundingClientRect(); if (r.width < 2 || r.height < 2) return false;
            if (r.bottom < 0 || r.right < 0 || r.top > innerHeight || r.left > innerWidth) return false;
            const s = getComputedStyle(e); return s.visibility !== 'hidden' && s.display !== 'none'; };
          const mid = r => ({ x: r.left + r.width / 2, y: r.top + r.height / 2 });
          const set = e => { if (cur) mark(cur, false); cur = e;
            if (e) { mark(e, true); e.scrollIntoView({ block: 'nearest', inline: 'nearest' }); try { e.focus({ preventScroll: true }); } catch {} } };
          window.__orbis = {
            move(d) {
              const els = [...document.querySelectorAll(sel)].filter(vis);
              const step = d === 'down' ? 320 : d === 'up' ? -320 : 0;
              if (!cur || !document.contains(cur) || !vis(cur)) { if (els.length) set(els[0]); else scrollBy(0, step); return; }
              const c = mid(cur.getBoundingClientRect()); let best = null, bs = 1e9;
              for (const e of els) { if (e === cur || e.contains(cur) || cur.contains(e)) continue;
                const p = mid(e.getBoundingClientRect()), dx = p.x - c.x, dy = p.y - c.y;
                const [main, cross] = d === 'right' ? [dx, dy] : d === 'left' ? [-dx, dy] : d === 'down' ? [dy, dx] : [-dy, dx];
                if (main <= 4) continue; const s = main + Math.abs(cross) * 2; if (s < bs) { bs = s; best = e; } }
              if (best) set(best); else scrollBy(0, step);
            },
            click() { if (!cur) return ''; const t = cur.tagName;
              if ((t === 'INPUT' && !['button','submit','checkbox','radio','reset','image','file'].includes(cur.type)) || t === 'TEXTAREA') return 'text:' + (cur.value || '');
              cur.click(); return ''; },
            type(v) { if (!cur) return; cur.focus(); cur.value = v; cur.dispatchEvent(new Event('input', { bubbles: true })); cur.dispatchEvent(new Event('change', { bubbles: true }));
              if (cur.tagName === 'INPUT' && cur.form) { cur.form.requestSubmit ? cur.form.requestSubmit() : cur.form.submit(); }
              else ['keydown','keypress','keyup'].forEach(k => cur.dispatchEvent(new KeyboardEvent(k, { key: 'Enter', code: 'Enter', keyCode: 13, bubbles: true }))); },
            scroll(dy) { scrollBy({ top: dy, behavior: 'smooth' }); },
            reset() { cur = null; }
          };
        })();
        """;

    readonly string _home;
    readonly Window _win;
    readonly WebView2 _web = new() { DefaultBackgroundColor = System.Drawing.Color.White };
    readonly TextBlock _address = new() { FontFamily = UiKit.FontRegular, FontSize = 20, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    bool _closed;

    public BrowserScreen(string url = "https://www.google.com")
    {
        _home = url;
        Background = Brushes.Black;
        var top = new Border { Background = UiKit.B("#0A2370"), Height = 52, Padding = new Thickness(24, 0, 24, 0), Child = _address };
        var hints = new TextBlock
        {
            FontFamily = UiKit.FontRegular, FontSize = 18, Foreground = UiKit.Dim, VerticalAlignment = VerticalAlignment.Center,
            Text = "✕ Select    ○ Back    △ Address    □ Reload    L1 / R1 Page    OPTIONS Close",
        };
        var bottom = new Border { Background = UiKit.B("#0A2370"), Height = 44, Padding = new Thickness(24, 0, 24, 0), Child = hints };
        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top); dock.Children.Add(top);
        DockPanel.SetDock(bottom, Dock.Bottom); dock.Children.Add(bottom);
        dock.Children.Add(_web);
        _win = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, WindowState = WindowState.Maximized,
            ShowInTaskbar = false, Owner = Shell.Window, Background = Brushes.Black, Content = dock, Title = "OrbisUI Browser",
        };
        // The pad only feeds the shell while one of its windows is in front.
        _win.Activated += (_, _) => { if (Shell.Pad != null) Shell.Pad.Foreground = true; Sound.Muted = false; };
        _win.Closed += (_, _) => _closed = true;
        Loaded += async (_, _) => await Start();
    }

    async Task Start()
    {
        try
        {
            _win.Show();
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppPaths.Cache, "Browser"));
            await _web.EnsureCoreWebView2Async(env);
            await _web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(NavJs);
            _web.CoreWebView2.SourceChanged += (_, _) => _address.Text = _web.Source?.ToString() ?? "";
            // Pop-ups open in the same view so the controller never loses the page.
            _web.CoreWebView2.NewWindowRequested += (_, e) => { e.Handled = true; _web.CoreWebView2.Navigate(e.Uri); };
            _web.CoreWebView2.Navigate(_home);
        }
        catch (Exception ex)
        {
            Log.Warn("browser: " + ex.Message);
            Close();
            Shell.Message("Internet Browser", "The browser needs the Microsoft Edge WebView2 Runtime, which comes with Windows 11. " + ex.Message);
        }
    }

    Task<string> Js(string code) => _web.CoreWebView2?.ExecuteScriptAsync(code) ?? Task.FromResult("");

    void Close()
    {
        if (!_closed) _win.Close();
        _web.Dispose();
        Shell.Pop();
        Shell.Window.Activate();
    }

    /// <summary>The on-screen keyboard lives in the shell window, so the page steps aside while it is up.</summary>
    void Ask(string title, string initial, Action<string> done)
    {
        _win.Hide();
        Shell.Window.Activate();
        Osk.Ask(title, initial, v => { done(v); });
    }

    public override void OnResumed()
    {
        if (_closed) return;
        _win.Show();
        _win.Activate();
    }

    public override bool OnInput(Btn b)
    {
        if (_web.CoreWebView2 == null) { if (b is Btn.Circle or Btn.Options) Close(); return true; }
        switch (b)
        {
            case Btn.Up: _ = Js("__orbis.move('up')"); break;
            case Btn.Down: _ = Js("__orbis.move('down')"); break;
            case Btn.Left: _ = Js("__orbis.move('left')"); break;
            case Btn.Right: _ = Js("__orbis.move('right')"); break;
            case Btn.L1: _ = Js("__orbis.scroll(-innerHeight * 0.8)"); break;
            case Btn.R1: _ = Js("__orbis.scroll(innerHeight * 0.8)"); break;
            case Btn.Square: Play(Sfx.Option); _web.CoreWebView2.Reload(); break;
            case Btn.Cross:
                Play(Sfx.Decide);
                _ = Click();
                break;
            case Btn.Circle:
                Play(Sfx.Back);
                if (_web.CoreWebView2.CanGoBack) _web.CoreWebView2.GoBack(); else Close();
                break;
            case Btn.Triangle:
                Play(Sfx.Option);
                Ask("Web Address or Search", _web.Source?.ToString() ?? "", v => _web.CoreWebView2.Navigate(ToUrl(v)));
                break;
            case Btn.Options or Btn.PS: Play(Sfx.Back); Close(); break;
        }
        return true;
    }

    async Task Click()
    {
        // ExecuteScriptAsync returns JSON; a text field answers "text:<current value>".
        var r = System.Text.Json.JsonSerializer.Deserialize<string>(await Js("__orbis.click()")) ?? "";
        if (r.StartsWith("text:")) Ask("Enter Text", r[5..], v => _ = Js("__orbis.type(" + System.Text.Json.JsonSerializer.Serialize(v) + ")"));
    }

    /// <summary>Typed text becomes a URL when it looks like one, otherwise a search.</summary>
    internal static string ToUrl(string v)
    {
        v = v.Trim();
        if (Uri.TryCreate(v, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https")) return v;
        if (!v.Contains(' ') && v.Contains('.') && Uri.TryCreate("https://" + v, UriKind.Absolute, out _)) return "https://" + v;
        return "https://www.google.com/search?q=" + Uri.EscapeDataString(v);
    }
}
