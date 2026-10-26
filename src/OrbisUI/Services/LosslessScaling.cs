using System.Text.Json.Nodes;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using OrbisUI.Core;
using OrbisUI.Library;

namespace OrbisUI.Services;

/// <summary>
/// Lossless Scaling has no command-line or API for scaling a window. The supported route is its own global hotkey,
/// so after the game is ready (and a delay) we focus the game window and press that hotkey once.
/// The app stays the user's own install; it is started minimised to the tray if needed.
/// </summary>
public static class LosslessScaling
{
    static string? _activatedFor;

    public static string? FindExe()
    {
        var s = AppSettings.Current.LosslessPath;
        if (!string.IsNullOrEmpty(s) && File.Exists(s)) return s;
        var steam = Steam.FindInstall();
        if (steam == null) return null;
        return Steam.LibraryFolders(steam)
            .Select(l => Path.Combine(l, "steamapps", "common", "Lossless Scaling", "LosslessScaling.exe"))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>The hotkey the user set inside Lossless Scaling (Settings.xml), default Ctrl+Alt+S.</summary>
    public static ushort[] Hotkey()
    {
        var keys = new List<ushort>();
        try
        {
            var f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lossless Scaling", "Settings.xml");
            if (File.Exists(f))
            {
                var x = XDocument.Load(f).Root;
                var mods = x?.Element("HotkeyModifierKeys")?.Value ?? "Alt Control";
                var key = x?.Element("Hotkey")?.Value ?? "S";
                if (mods.Contains("Control")) keys.Add(0x11);
                if (mods.Contains("Alt")) keys.Add(0x12);
                if (mods.Contains("Shift")) keys.Add(0x10);
                if (mods.Contains("Windows")) keys.Add(0x5B);
                if (key.Length == 1) keys.Add(char.ToUpperInvariant(key[0]));
                else if (key.StartsWith('F') && int.TryParse(key[1..], out var fn)) keys.Add((ushort)(0x6F + fn));
                else if (key.StartsWith('D') && key.Length == 2 && char.IsDigit(key[1])) keys.Add(key[1]);
                if (keys.Count > 0) return keys.ToArray();
            }
        }
        catch (Exception ex) { Log.Warn("Lossless Scaling settings: " + ex.Message); }
        return new ushort[] { 0x11, 0x12, (ushort)'S' };
    }

    public static bool EnabledFor(Game g)
    {
        var o = AppSettings.Current.Games.GetValueOrDefault(g.Id);
        return o?.Lossless ?? AppSettings.Current.LosslessEnabled;
    }

    public static async Task ActivateAsync(Game g, IntPtr gameWindow, Func<bool> stillRunning)
    {
        if (gameWindow == IntPtr.Zero || _activatedFor == g.Id) return;
        if (AutoLsHandles(gameWindow))
        {
            // Auto Lossless Scaling scales this game itself; a second hotkey press would toggle scaling back off.
            _activatedFor = g.Id;
            Log.Info($"Auto Lossless Scaling handles {g.Title}; waiting for its scaling");
            await WaitForScaling(g, gameWindow, stillRunning, 120);
            return;
        }
        if (!EnabledFor(g)) return;
        var exe = FindExe();
        if (exe == null) { Notifications.Post("Lossless Scaling not found", "Install it from Steam or set its path in Settings."); return; }
        if (Process.GetProcessesByName("LosslessScaling").Length == 0)
        {
            // -StartMinimized is the switch its own "start with Windows" entry uses: straight to the tray, no window.
            Process.Start(new ProcessStartInfo(exe, "-StartMinimized") { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Minimized });
            await Task.Delay(3000);
        }
        int delay = AppSettings.Current.Games.GetValueOrDefault(g.Id)?.LosslessDelaySec ?? AppSettings.Current.LosslessDelaySec;
        await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, delay)));
        if (!stillRunning() || !Native.IsWindowVisible(gameWindow)) { Log.Info("Lossless Scaling skipped: game no longer running"); return; }
        Native.ForceForeground(gameWindow);
        await Task.Delay(400);
        if (Native.GetForegroundWindow() != gameWindow) { Log.Warn("Lossless Scaling: game window not in front, hotkey not sent"); return; }
        if (IsScaling(gameWindow)) { _activatedFor = g.Id; Activated?.Invoke(g); return; }
        Native.SendKeyCombo(Hotkey());
        _activatedFor = g.Id;
        Log.Info($"Lossless Scaling hotkey sent for {g.Title}");
        // The hotkey can be ignored (wrong hotkey, unsupported window), so only announce PRO mode once scaling is seen.
        await WaitForScaling(g, gameWindow, stillRunning, 20);
    }

    static async Task WaitForScaling(Game g, IntPtr gameWindow, Func<bool> stillRunning, int halfSeconds)
    {
        for (int i = 0; i < halfSeconds && stillRunning(); i++)
        {
            await Task.Delay(500);
            if (IsScaling(gameWindow)) { Activated?.Invoke(g); return; }
        }
        Log.Warn("Lossless Scaling: no scaling window appeared for " + g.Title);
    }

    static string AutoLsConfig => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Auto Lossless Scaling", "config.json");

    /// <summary>True when Auto Lossless Scaling is running and its list has the game's exe with scaling on.</summary>
    static bool AutoLsHandles(IntPtr gameWindow)
    {
        if (Process.GetProcessesByName("Auto Lossless Scaling").Length == 0 || !File.Exists(AutoLsConfig)) return false;
        try
        {
            var store = JsonNode.Parse(File.ReadAllText(AutoLsConfig))?["store"];
            if (store?["enableLosslessScaling"]?.GetValue<bool>() != true) return false;
            Native.GetWindowThreadProcessId(gameWindow, out var pid);
            using var p = Process.GetProcessById((int)pid);
            var exe = p.MainModule?.FileName;
            return store["processes"]?.AsArray().Any(x => string.Equals(x?["path"]?.ToString(), exe, StringComparison.OrdinalIgnoreCase)
                && x?["options"]?["enableLosslessScaling"]?.GetValue<bool>() != false) == true;
        }
        catch (Exception ex) { Log.Warn("Auto Lossless Scaling config: " + ex.Message); return false; }
    }

    /// <summary>
    /// Auto Lossless Scaling restarts Lossless Scaling (elevated) through a script in Documents that opens its full window,
    /// which a normal process can't hide afterwards. Adding -StartMinimized to that script keeps it in the tray.
    /// The app rewrites the script when it starts, so this runs before every launch.
    /// </summary>
    public static void KeepAutoLsHeadless()
    {
        try
        {
            var vbs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "run-lossless-scaling-as-admin.vbs");
            if (!File.Exists(vbs)) return;
            var text = File.ReadAllText(vbs);
            var fixedText = text.Replace("exePath, \"\", \"\", \"runas\"", "exePath, \"-StartMinimized\", \"\", \"runas\"");
            if (fixedText != text) { File.WriteAllText(vbs, fixedText); Log.Info("Auto Lossless Scaling: Lossless Scaling now starts minimised"); }
        }
        catch (Exception ex) { Log.Warn("Auto Lossless Scaling script: " + ex.Message); }
    }

    /// <summary>Raised (on a worker thread) when scaling is confirmed for the game.</summary>
    public static event Action<Game>? Activated;

    /// <summary>
    /// While scaling, Lossless Scaling shows its output in its own borderless window covering the game.
    /// So scaling is on when a visible window of its process (not its settings window) covers most of the game's screen.
    /// </summary>
    public static bool IsScaling(IntPtr gameWindow)
    {
        if (!Native.GetWindowRect(gameWindow, out var gr)) return false;
        foreach (var p in Process.GetProcessesByName("LosslessScaling"))
        {
            using (p)
            {
                bool found = false;
                Native.EnumWindows((h, _) =>
                {
                    // The settings window is titled "Lossless Scaling"; the scaling output window is not.
                    if (Native.WindowTitle(h) == "Lossless Scaling" || !Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;
                    Native.GetWindowThreadProcessId(h, out var pid);
                    if (pid != (uint)p.Id || !Native.GetWindowRect(h, out var r)) return true;
                    found = Covers(r, gr);
                    if (found) Log.Info($"Lossless Scaling window {r.Right - r.Left}x{r.Bottom - r.Top} over game {gr.Right - gr.Left}x{gr.Bottom - gr.Top}");
                    return !found;
                }, IntPtr.Zero);
                if (found) return true;
            }
        }
        return false;
    }

    /// <summary>True when r covers at least 90% of the game window's area.</summary>
    internal static bool Covers(Native.RECT r, Native.RECT game)
    {
        long ix = Math.Max(0, Math.Min(r.Right, game.Right) - Math.Max(r.Left, game.Left));
        long iy = Math.Max(0, Math.Min(r.Bottom, game.Bottom) - Math.Max(r.Top, game.Top));
        long ga = (long)(game.Right - game.Left) * (game.Bottom - game.Top);
        return ga > 0 && ix * iy >= ga * 0.9;
    }

    public static void Reset() => _activatedFor = null;

    static string AlsDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Auto Lossless Scaling");

    /// <summary>True when Auto Lossless Scaling is running and lists the game's exe, so it does the scaling.</summary>
    static bool AlsHandles(IntPtr gameWindow)
    {
        try
        {
            if (Process.GetProcessesByName("Auto Lossless Scaling").Length == 0) return false;
            Native.GetWindowThreadProcessId(gameWindow, out var pid);
            using var p = Process.GetProcessById((int)pid);
            var exe = p.MainModule?.FileName;
            var cfg = Path.Combine(AlsDir, "config.json");
            if (exe == null || !File.Exists(cfg)) return false;
            var store = JsonDocument.Parse(File.ReadAllText(cfg)).RootElement.GetProperty("store");
            if (store.TryGetProperty("enableLosslessScaling", out var en) && en.ValueKind == JsonValueKind.False) return false;
            return store.GetProperty("processes").EnumerateArray().Any(x => string.Equals(x.GetProperty("path").GetString(), exe, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) { Log.Warn("Auto Lossless Scaling config: " + ex.Message); return false; }
    }

    /// <summary>
    /// Auto Lossless Scaling restarts Lossless Scaling for every game through a .vbs in Documents that opens it maximised in front.
    /// Lossless Scaling runs as admin, so its window can't be hidden from here afterwards; instead the script is made to pass
    /// -StartMinimized (Lossless Scaling's own tray-start switch) with a hidden window. Auto Lossless Scaling rewrites the
    /// script when it starts, so this runs before every launch.
    /// </summary>
    public static void PatchAlsLauncher()
    {
        try
        {
            var f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "run-lossless-scaling-as-admin.vbs");
            if (!File.Exists(f)) return;
            var s = File.ReadAllText(f);
            var patched = Regex.Replace(s, @"ShellExecute exePath, ""[^""]*"", ""([^""]*)"", ""runas"", \d+", @"ShellExecute exePath, ""-StartMinimized"", ""$1"", ""runas"", 0");
            if (patched != s) { File.WriteAllText(f, patched); Log.Info("Auto Lossless Scaling launcher set to start Lossless Scaling minimised"); }
        }
        catch (Exception ex) { Log.Warn("Auto Lossless Scaling launcher: " + ex.Message); }
    }
}
