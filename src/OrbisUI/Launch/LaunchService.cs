using System.Diagnostics;
using Microsoft.Win32;
using OrbisUI.Core;
using OrbisUI.Library;
using OrbisUI.Services;

namespace OrbisUI.Launch;

public sealed class LaunchException(string message) : Exception(message);

/// <summary>
/// Starts a game and reports two moments: when its window is actually up (to drop the splash) and when it has exited
/// (to bring the home screen back). Polling only happens while a game is running, at a slow rate.
/// </summary>
public sealed class LaunchService
{
    public Game? Running { get; private set; }
    public IntPtr GameWindow { get; private set; }

    public event Action<Game, string>? Status;
    public event Action<Game, IntPtr>? Ready;
    public event Action<Game>? Exited;

    CancellationTokenSource? _cts;

    public static (string exe, string args, string? workDir) BuildCommand(Game g, string? disc = null)
    {
        var ovr = g.IsVersion ? null : AppSettings.Current.Games.GetValueOrDefault(g.Id);
        switch (g.Kind)
        {
            case LaunchKind.Exe:
                return (g.Path!, ovr?.Args ?? g.Args ?? "", g.WorkDir ?? Path.GetDirectoryName(g.Path));
            case LaunchKind.Emulator:
                var exe = Emulators.ExePath(g.Platform)
                    ?? throw new LaunchException($"No {Emulators.Get(g.Platform)?.Name ?? g.Platform} emulator is set up. Add it in Settings > Emulators.");
                var template = ovr?.Args ?? Emulators.Args(g.Platform);
                var args = template.Replace("{rom}", disc ?? g.Path ?? "").Replace("{id}", g.TitleId ?? "");
                return (exe, args, Path.GetDirectoryName(exe));
            default:
                throw new LaunchException("This item is launched through Steam.");
        }
    }

    public async Task LaunchAsync(Game g, string? disc = null)
    {
        if (Running != null) throw new LaunchException($"{Running.Title} is already running.");
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        Running = g;
        GameWindow = IntPtr.Zero;
        AppSettings.Current.LastPlayed[g.Id] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        AppSettings.Current.Save();
        try
        {
            if (g.Kind is LaunchKind.Steam or LaunchKind.SteamShortcut) await RunViaSteam(g, ct);
            else if (g.Kind == LaunchKind.Url) await RunShell(g, ct);
            else await RunProcess(g, disc, ct);
        }
        catch
        {
            Running = null;
            throw;
        }
    }

    async Task RunProcess(Game g, string? disc, CancellationToken ct)
    {
        var (exe, args, wd) = BuildCommand(g, disc);
        if (!File.Exists(exe)) throw new LaunchException($"Can't find {Path.GetFileName(exe)}.");
        var rom = disc ?? g.Path;
        if (g.Kind == LaunchKind.Emulator && rom != null && !File.Exists(rom) && !Directory.Exists(rom) && g.Platform != "VITA")
            throw new LaunchException("The game file is missing. It may have been moved or deleted.");
        ReShade.ApplyFor(g);
        Log.Info($"Launch: \"{exe}\" {args}");
        Status?.Invoke(g, "Starting…");
        var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, WorkingDirectory = wd ?? "" })
                ?? throw new LaunchException("The game did not start.");
        var started = DateTime.UtcNow;
        var dir = Path.GetDirectoryName(exe)!;

        // Wait for a real game window, then a short settle so the first frames are drawn.
        var hwnd = await WaitForWindow(() => Pids(p, dir), ct);
        if (hwnd != IntPtr.Zero)
        {
            // Hold the splash until the game has gone fullscreen/maximized (emulators open a small window first), at most 10 s.
            for (int i = 0; i < 40 && !Native.CoversScreen(hwnd); i++)
            {
                await Task.Delay(250, ct);
                if (Native.FindMainWindow(Pids(p, dir)) is var h2 && h2 != IntPtr.Zero) hwnd = h2;
            }
            GameWindow = hwnd;
            await Task.Delay(g.Kind == LaunchKind.Emulator ? 1500 : 800, ct);
            Ready?.Invoke(g, hwnd);
        }
        else Ready?.Invoke(g, IntPtr.Zero); // timeout: drop the splash anyway

        await p.WaitForExitAsync(ct);
        // Some launchers start the real game and exit; follow processes from the same folder.
        if ((DateTime.UtcNow - started).TotalSeconds < 20)
            while (!ct.IsCancellationRequested && Pids(null, dir).Count > 0) await Task.Delay(2000, ct);
        Finish(g);
    }

    /// <summary>
    /// Shortcuts, .url files and Store apps: the shell starts them, so the game is the next large window that takes the
    /// foreground. It's tracked until that window closes.
    /// </summary>
    async Task RunShell(Game g, CancellationToken ct)
    {
        Log.Info($"Launch (shell): {g.Path}");
        Status?.Invoke(g, "Starting…");
        var before = Native.GetForegroundWindow();
        Native.OpenShell(g.Path!);
        var deadline = DateTime.UtcNow.AddSeconds(AppSettings.Current.LaunchTimeoutSec);
        IntPtr hwnd = IntPtr.Zero;
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await Task.Delay(500, ct);
            var fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == before || Native.ProcessIdOf(fg) == Environment.ProcessId || !Native.GetWindowRect(fg, out var r) || r.Right - r.Left < 200) continue;
            hwnd = fg;
            break;
        }
        if (hwnd == IntPtr.Zero) { Ready?.Invoke(g, IntPtr.Zero); Finish(g); return; } // nothing to track: show home again
        for (int i = 0; i < 40 && !Native.CoversScreen(hwnd); i++) await Task.Delay(250, ct);
        GameWindow = hwnd;
        await Task.Delay(800, ct);
        Ready?.Invoke(g, hwnd);
        while (!ct.IsCancellationRequested && Native.IsWindow(hwnd)) await Task.Delay(2000, ct);
        Finish(g);
    }

    static List<int> Pids(Process? p, string dir)
    {
        var list = new List<int>();
        // While the started process lives it is the game; full enumeration only for launcher-style hand-offs.
        if (p != null) { try { if (!p.HasExited) return new List<int> { p.Id }; } catch { } }
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (list.Contains(proc.Id) || proc.Id == Environment.ProcessId) continue;
                var path = proc.MainModule?.FileName;
                if (path != null && path.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) list.Add(proc.Id);
            }
            catch { /* access denied for system processes */ }
            finally { proc.Dispose(); }
        }
        return list;
    }

    async Task<IntPtr> WaitForWindow(Func<List<int>> pids, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(AppSettings.Current.LaunchTimeoutSec);
        int n = 0;
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            // Process enumeration is expensive; the cheap check is every 250 ms, the full one every second.
            var ids = pids();
            var h = Native.FindMainWindow(ids);
            if (h != IntPtr.Zero) return h;
            if (ids.Count == 0 && n > 8) return IntPtr.Zero;
            n++;
            await Task.Delay(n < 20 ? 250 : 1000, ct);
        }
        return IntPtr.Zero;
    }

    static string? SteamRunningAppId()
        => Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "RunningAppID", 0)?.ToString();

    async Task RunViaSteam(Game g, CancellationToken ct)
    {
        if (Steam.FindInstall() == null) throw new LaunchException("Steam isn't installed.");
        Status?.Invoke(g, Steam.IsRunning() ? "Starting through Steam…" : "Starting Steam…");
        Steam.OpenUrl(Steam.Links.Run(g.SteamAppId!));
        var deadline = DateTime.UtcNow.AddSeconds(AppSettings.Current.LaunchTimeoutSec + 60);
        string dir = g.InstallDir ?? (g.Path != null ? Path.GetDirectoryName(g.Path) ?? "" : "");
        bool isShortcut = g.Kind == LaunchKind.SteamShortcut;

        // Ready = Steam reports the app running (or, for shortcuts, its process exists) and a game window is up.
        bool running = false;
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            running = isShortcut ? Pids(null, dir).Count > 0 : SteamRunningAppId() == g.SteamAppId;
            if (running)
            {
                var win = dir.Length > 3 ? Native.FindMainWindow(Pids(null, dir)) : Native.GetForegroundWindow();
                if (win != IntPtr.Zero) { GameWindow = win; break; }
            }
            await Task.Delay(1000, ct);
        }
        if (!running)
        {
            Running = null;
            throw new LaunchException("The game didn't start. Steam may be showing a message, an update or a launch option.");
        }
        await Task.Delay(1500, ct);
        Ready?.Invoke(g, GameWindow);

        // Steam keeps RunningAppID set until the game exits; every 2 s is plenty.
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(2000, ct);
            bool still = isShortcut ? Pids(null, dir).Count > 0 : SteamRunningAppId() == g.SteamAppId;
            if (!still) break;
        }
        Finish(g);
    }

    void Finish(Game g)
    {
        Log.Info($"Exited: {g.Title}");
        Running = null;
        GameWindow = IntPtr.Zero;
        Exited?.Invoke(g);
    }

    /// <summary>Stop tracking (the game keeps running if it can't be closed).</summary>
    public void Abandon()
    {
        _cts?.Cancel();
        if (Running is Game g) Finish(g);
    }

    /// <summary>Close the running game: ask nicely, then end the emulator if needed.</summary>
    public void CloseGame()
    {
        if (Running == null || GameWindow == IntPtr.Zero) return;
        var pid = Native.ProcessIdOf(GameWindow);
        if (pid is int id)
        {
            try
            {
                using var p = Process.GetProcessById(id);
                if (!p.CloseMainWindow() || !p.WaitForExit(5000)) { if (Running.Kind == LaunchKind.Emulator) p.Kill(); }
            }
            catch (Exception ex) { Log.Warn("close game: " + ex.Message); }
        }
    }
}
