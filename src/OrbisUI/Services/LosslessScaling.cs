using System.Diagnostics;
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
        if (!EnabledFor(g) || gameWindow == IntPtr.Zero || _activatedFor == g.Id) return;
        var exe = FindExe();
        if (exe == null) { Notifications.Post("Lossless Scaling not found", "Install it from Steam or set its path in Settings."); return; }
        if (Process.GetProcessesByName("LosslessScaling").Length == 0)
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Minimized });
            await Task.Delay(3000);
        }
        int delay = AppSettings.Current.Games.GetValueOrDefault(g.Id)?.LosslessDelaySec ?? AppSettings.Current.LosslessDelaySec;
        await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, delay)));
        if (!stillRunning() || !Native.IsWindowVisible(gameWindow)) { Log.Info("Lossless Scaling skipped: game no longer running"); return; }
        Native.ForceForeground(gameWindow);
        await Task.Delay(400);
        if (Native.GetForegroundWindow() != gameWindow) { Log.Warn("Lossless Scaling: game window not in front, hotkey not sent"); return; }
        Native.SendKeyCombo(Hotkey());
        _activatedFor = g.Id;
        Log.Info($"Lossless Scaling hotkey sent for {g.Title}");
        // The hotkey can be ignored (wrong hotkey, unsupported window), so only announce PRO mode once scaling is seen.
        for (int i = 0; i < 20 && stillRunning(); i++)
        {
            await Task.Delay(500);
            if (IsScaling(gameWindow))
            {
                Activated?.Invoke(g);
                return;
            }
        }
        Log.Warn("Lossless Scaling: no scaling window appeared after the hotkey");
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
                var main = p.MainWindowHandle;
                bool found = false;
                Native.EnumWindows((h, _) =>
                {
                    if (h == main || !Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;
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
}
