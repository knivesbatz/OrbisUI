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
    }

    public static void Reset() => _activatedFor = null;
}
