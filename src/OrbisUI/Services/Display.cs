using System.Diagnostics;
using System.Runtime.InteropServices;
using OrbisUI.Core;

namespace OrbisUI.Services;

/// <summary>
/// Resolution / refresh rate of the main display and internal panel brightness, without opening Windows Settings.
/// New modes are applied temporarily first (not saved to the registry) so a revert just restores the saved mode.
/// </summary>
public static class Display
{
    public readonly record struct Mode(int Width, int Height, int Hz)
    {
        public override string ToString() => $"{Width} × {Height}";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string? device, int mode, ref DEVMODE dm);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsEx(string? device, ref DEVMODE dm, IntPtr hwnd, int flags, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsEx(string? device, IntPtr dm, IntPtr hwnd, int flags, IntPtr param);

    const int ENUM_CURRENT_SETTINGS = -1, CDS_UPDATEREGISTRY = 1, CDS_TEST = 2, DISP_CHANGE_SUCCESSFUL = 0;
    const int DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000, DM_DISPLAYFREQUENCY = 0x400000;

    static DEVMODE New() => new() { dmSize = (short)Marshal.SizeOf<DEVMODE>() };

    public static Mode Current()
    {
        var dm = New();
        EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm);
        return new Mode(dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency);
    }

    /// <summary>Every 32-bit mode of the primary display, largest first.</summary>
    public static List<Mode> Modes()
    {
        var set = new HashSet<Mode>();
        var dm = New();
        for (int i = 0; EnumDisplaySettings(null, i, ref dm); i++)
            if (dm.dmBitsPerPel == 32) set.Add(new Mode(dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency));
        return set.OrderByDescending(m => m.Width * m.Height).ThenByDescending(m => m.Hz).ToList();
    }

    public static List<Mode> Resolutions() => Modes().DistinctBy(m => (m.Width, m.Height)).ToList();
    public static List<int> RefreshRates(int w, int h) => Modes().Where(m => m.Width == w && m.Height == h).Select(m => m.Hz).Distinct().OrderDescending().ToList();

    /// <summary>Switches temporarily (not saved). Returns an error message, or null on success.</summary>
    public static string? TryApply(Mode m)
    {
        var dm = New();
        dm.dmPelsWidth = m.Width; dm.dmPelsHeight = m.Height; dm.dmDisplayFrequency = m.Hz;
        dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;
        if (ChangeDisplaySettingsEx(null, ref dm, IntPtr.Zero, CDS_TEST, IntPtr.Zero) != DISP_CHANGE_SUCCESSFUL) return "The display doesn't support that mode.";
        int r = ChangeDisplaySettingsEx(null, ref dm, IntPtr.Zero, 0, IntPtr.Zero);
        Log.Info($"Display: trying {m} @ {m.Hz} Hz (result {r})");
        return r == DISP_CHANGE_SUCCESSFUL ? null : $"Windows couldn't change the display (code {r}).";
    }

    /// <summary>Saves the mode currently in use so it survives a restart.</summary>
    public static void Keep(Mode m)
    {
        var dm = New();
        dm.dmPelsWidth = m.Width; dm.dmPelsHeight = m.Height; dm.dmDisplayFrequency = m.Hz;
        dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;
        ChangeDisplaySettingsEx(null, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
        Log.Info($"Display: kept {m} @ {m.Hz} Hz");
    }

    /// <summary>Goes back to the mode saved in the registry (what was in use before TryApply).</summary>
    public static void Revert()
    {
        ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        Log.Info("Display: reverted");
    }

    // Internal panel brightness goes through WMI; PowerShell avoids pulling System.Management into the app.
    public static int? GetBrightness()
    {
        var o = Ps("(Get-CimInstance -Namespace root/wmi -ClassName WmiMonitorBrightness -ErrorAction Stop | Select-Object -First 1).CurrentBrightness");
        return int.TryParse(o, out var v) ? v : null;
    }

    static int _pending = -1;

    /// <summary>Queues a brightness change; rapid presses collapse into one call.</summary>
    public static void SetBrightness(int percent)
    {
        if (Interlocked.Exchange(ref _pending, Math.Clamp(percent, 0, 100)) != -1) return;
        Task.Run(() =>
        {
            Thread.Sleep(150);
            int v = Interlocked.Exchange(ref _pending, -1);
            Ps($"Get-CimInstance -Namespace root/wmi -ClassName WmiMonitorBrightnessMethods | Invoke-CimMethod -MethodName WmiSetBrightness -Arguments @{{Timeout=1;Brightness={v}}} | Out-Null");
        });
    }

    static string? Ps(string script)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -Command \"{script}\"")
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!;
            var o = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(5000);
            return o;
        }
        catch (Exception ex) { Log.Warn("brightness: " + ex.Message); return null; }
    }
}
