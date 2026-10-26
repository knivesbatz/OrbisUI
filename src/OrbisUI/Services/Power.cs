using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace OrbisUI.Services;

/// <summary>The Windows power mode slider and screen-off / sleep timers, without Windows Settings.</summary>
public static class Power
{
    [StructLayout(LayoutKind.Sequential)]
    struct SYSTEM_POWER_STATUS { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }
    [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);
    // The power mode slider (Settings > Power) is an "overlay" scheme; these powrprof exports are what Settings calls.
    [DllImport("powrprof.dll", EntryPoint = "PowerGetEffectiveOverlayScheme")] static extern uint GetOverlay(out Guid g);
    [DllImport("powrprof.dll", EntryPoint = "PowerSetActiveOverlayScheme")] static extern uint SetOverlay(Guid g);

    public static readonly (string Name, Guid Id)[] Modes =
    {
        ("Best Power Efficiency", new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a")),
        ("Balanced", Guid.Empty),
        ("Best Performance", new Guid("ded574b5-45a0-4f42-8737-46345c09c238")),
    };

    public static bool OnBattery => GetSystemPowerStatus(out var s) && s.ACLineStatus == 0;

    public static int Mode()
    {
        if (GetOverlay(out var g) != 0) return 1;
        var i = Array.FindIndex(Modes, m => m.Id == g);
        return i < 0 ? 1 : i;
    }

    public static void SetMode(int i) => SetOverlay(Modes[Math.Clamp(i, 0, Modes.Length - 1)].Id);

    public static readonly int[] Minutes = { 1, 2, 3, 5, 10, 15, 30, 60, 0 };
    public static string MinutesText(int m) => m == 0 ? "Never" : m < 60 ? $"{m} min" : $"{m / 60} h";

    static string PowerCfg(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("powercfg.exe", args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        var o = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return o;
    }

    /// <summary>Minutes before the screen turns off (<paramref name="sleep"/>: before the device sleeps) for the current power source.</summary>
    public static int Timeout(bool sleep)
    {
        var o = PowerCfg(sleep ? "/q SCHEME_CURRENT SUB_SLEEP STANDBYIDLE" : "/q SCHEME_CURRENT SUB_VIDEO VIDEOIDLE");
        var m = Regex.Match(o, (OnBattery ? "DC" : "AC") + @" Power Setting Index:\s*0x([0-9a-fA-F]+)");
        return m.Success ? Convert.ToInt32(m.Groups[1].Value, 16) / 60 : 0;
    }

    /// <summary>Sets the timer for both battery and plugged in, like the console's single setting.</summary>
    public static void SetTimeout(bool sleep, int minutes)
    {
        var what = sleep ? "standby-timeout" : "monitor-timeout";
        PowerCfg($"/change {what}-ac {minutes}");
        PowerCfg($"/change {what}-dc {minutes}");
    }
}
