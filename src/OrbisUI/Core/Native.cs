using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace OrbisUI.Core;

internal static partial class Native
{
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("kernel32.dll")] public static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);
    [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
    [DllImport("winmm.dll")] public static extern bool PlaySound(IntPtr pszSound, IntPtr hmod, uint fdwSound);
    [DllImport("user32.dll")] public static extern bool LockSetForegroundWindow(uint uLockCode);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);

    public const int SW_HIDE = 0, SW_SHOWNORMAL = 1, SW_MINIMIZE = 6, SW_RESTORE = 9, SW_SHOW = 5, SW_SHOWMAXIMIZED = 3;
    public const uint SND_ASYNC = 0x0001, SND_NODEFAULT = 0x0002, SND_MEMORY = 0x0004, SND_PURGE = 0x0040;

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public KEYBDINPUT ki; public long pad; }
    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    const uint KEYEVENTF_KEYUP = 0x2;

    public static void SendKeyCombo(params ushort[] vks)
    {
        var list = new List<INPUT>();
        foreach (var vk in vks) list.Add(new INPUT { type = 1, ki = new KEYBDINPUT { wVk = vk } });
        for (int i = vks.Length - 1; i >= 0; i--) list.Add(new INPUT { type = 1, ki = new KEYBDINPUT { wVk = vks[i], dwFlags = KEYEVENTF_KEYUP } });
        SendInput((uint)list.Count, list.ToArray(), Marshal.SizeOf<INPUT>());
    }

    /// <summary>Windows blocks background processes from stealing focus; attaching to the foreground thread is the documented workaround.</summary>
    public static void ForceForeground(IntPtr hwnd)
    {
        var fg = GetForegroundWindow();
        if (fg == hwnd) return;
        uint fgThread = GetWindowThreadProcessId(fg, out _);
        uint me = GetCurrentThreadId();
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        if (fgThread != me) AttachThreadInput(me, fgThread, true);
        // An ALT tap satisfies the "last input event" foreground rule.
        SendKeyCombo(0x12);
        BringWindowToTop(hwnd);
        SetForegroundWindow(hwnd);
        if (fgThread != me) AttachThreadInput(me, fgThread, false);
    }

    public static string WindowTitle(IntPtr h)
    {
        int len = GetWindowTextLength(h);
        if (len <= 0) return "";
        var sb = new StringBuilder(len + 1);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>Top-level visible windows owned by any of the given processes.</summary>
    public static IntPtr FindMainWindow(ICollection<int> pids)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || GetWindow(h, 4 /*GW_OWNER*/) != IntPtr.Zero) return true;
            GetWindowThreadProcessId(h, out uint pid);
            if (!pids.Contains((int)pid)) return true;
            GetWindowRect(h, out var r);
            if (r.Right - r.Left < 200 || r.Bottom - r.Top < 150) return true;
            found = h;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    public static void TrimMemory()
    {
        try
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
            GC.WaitForPendingFinalizers();
            SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);
        }
        catch { }
    }

    public static int? ProcessIdOf(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        GetWindowThreadProcessId(hwnd, out uint pid);
        return (int)pid;
    }

    public static void OpenShell(string target, string? args = null)
    {
        // Store (UWP) apps are started by their AppUserModelID through the Applications folder.
        if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) { Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true }); return; }
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, Arguments = args ?? "" });
    }

    /// <summary>True when the window fills (nearly) the whole primary screen: fullscreen or maximized without much chrome.</summary>
    public static bool CoversScreen(IntPtr h)
    {
        if (h == IntPtr.Zero || !GetWindowRect(h, out var r)) return false;
        int sw = GetSystemMetrics(0), sh = GetSystemMetrics(1);
        return r.Right - r.Left >= sw * 0.9 && r.Bottom - r.Top >= sh * 0.85;
    }

    /// <summary>Target, arguments and working folder of a .lnk; target is empty for Store/advertised shortcuts.</summary>
    public static (string target, string args, string workDir) ReadShortcut(string lnk)
    {
        var t = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host is unavailable.");
        dynamic sh = Activator.CreateInstance(t)!;
        try
        {
            dynamic sc = sh.CreateShortcut(lnk);
            return ((string)sc.TargetPath ?? "", (string)sc.Arguments ?? "", (string)sc.WorkingDirectory ?? "");
        }
        finally { Marshal.FinalReleaseComObject(sh); }
    }
}
