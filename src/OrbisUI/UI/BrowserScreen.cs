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
              // First press lands on the page's focused or first text field (a search box), else the first link.
              if (!cur || !document.contains(cur) || !vis(cur)) { const a = document.activeElement;
                const first = els.includes(a) ? a : els.find(e => e.matches('input[type=text],input[type=search],input:not([type]),textarea')) || els[0];
                if (first) set(first); else scrollBy(0, step); return; }
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
              // Search boxes (Google's is a combobox textarea) submit their form; synthetic Enter keys are ignored by most sites.
              if (cur.form && (cur.tagName === 'INPUT' || cur.getAttribute('role') === 'combobox' || cur.name === 'q')) { cur.form.requestSubmit ? cur.form.requestSubmit() : cur.form.submit(); }
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
    bool _closed, _done;

    public const string HomePage = "https://www.google.com";

    /// <summary>True while the page window has focus; the shell then routes the controller here.</summary>
    public bool InFront => !_done && _win.IsActive;

    public BrowserScreen(string url = HomePage)
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
        _win.Deactivated += (_, _) => { if (Shell.Pad != null && !Shell.Window.IsActive) Shell.Pad.Foreground = false; };
        _win.Closed += (_, _) => { _closed = true; if (!_done) Shell.Window.Dispatcher.BeginInvoke(Close); }; // Alt+F4 on the page
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
            if (_done) return; // closed while the runtime was starting
            await _web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(NavJs);
            _web.CoreWebView2.SourceChanged += (_, _) => _address.Text = _web.Source?.ToString() ?? "";
            // Pop-ups open in the same view so the controller never loses the page.
            _web.CoreWebView2.NewWindowRequested += (_, e) => { e.Handled = true; Go(e.Uri); };
            _web.CoreWebView2.ProcessFailed += (_, e) =>
            {
                Log.Warn($"browser process failed: {e.ProcessFailedKind} {e.Reason}");
                // A crashed page reloads; losing the browser process itself ends the session.
                if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.BrowserProcessExited) Shell.Window.Dispatcher.BeginInvoke(() => { Close(); Shell.Message("Internet Browser", "The browser stopped unexpectedly. Open it again from the home screen."); });
                else if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive) Shell.Window.Dispatcher.BeginInvoke(() => { if (!_done) _web.CoreWebView2.Reload(); });
            };
            Go(_home);
        }
        catch (Exception ex)
        {
            Log.Warn("browser: " + ex.Message);
            if (_done) return;
            Close();
            Shell.Message("Internet Browser", "The browser needs the Microsoft Edge WebView2 Runtime, which comes with Windows 11. " + ex.Message);
        }
    }

    void Go(string url)
    {
        try { _web.CoreWebView2?.Navigate(url); }
        catch (ArgumentException) { _web.CoreWebView2?.Navigate(Search(url)); } // not a valid address: search for it
    }

    async Task<string> Js(string code)
    {
        try { return _done || _web.CoreWebView2 == null ? "" : await _web.CoreWebView2.ExecuteScriptAsync(code); }
        catch (Exception ex) { Log.Warn("browser js: " + ex.Message); return ""; }
    }

    void Close()
    {
        if (_done) return;
        if (Shell.Top == this) Shell.Pop(); // OnHidden tears the page down
        else OnHidden();
        Shell.Window.Activate();
    }

    /// <summary>Runs on every way off the stack (Back, PS/Home, a game launch), so the page window never outlives the screen.</summary>
    public override void OnHidden()
    {
        if (_done) return;
        _done = true;
        if (!_closed) _win.Close();
        _web.Dispose();
    }

    /// <summary>Test hook: the bars render through WPF and the page (a native surface WPF can't render) is pasted in from WebView2's own capture.</summary>
    internal async Task Shot(string path)
    {
        var page = new System.Windows.Media.Imaging.BitmapImage();
        using (var ms = new MemoryStream())
        {
            await _web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
            ms.Position = 0;
            page.BeginInit(); page.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; page.StreamSource = ms; page.EndInit();
        }
        var root = (FrameworkElement)_win.Content;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(root.RenderSize));
            dc.DrawImage(page, new Rect(_web.TranslatePoint(new Point(), root), _web.RenderSize));
        }
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
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
        if (_done) return true;
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
                Ask("Web Address or Search", _web.Source?.ToString() ?? "", v => Go(ToUrl(v)));
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
        return Search(v);
    }

    static string Search(string v) => HomePage + "/search?q=" + Uri.EscapeDataString(v);
}
