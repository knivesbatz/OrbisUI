using System.Runtime.InteropServices;

namespace OrbisUI.Services;

/// <summary>Windows accessibility switches that have a documented API (SystemParametersInfo).</summary>
public static class Access
{
    [StructLayout(LayoutKind.Sequential)] struct STICKYKEYS { public uint cbSize, dwFlags; }
    [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action, uint param, ref STICKYKEYS v, uint winIni);
    const uint SPI_GETSTICKYKEYS = 0x3A, SPI_SETSTICKYKEYS = 0x3B, SKF_HOTKEYACTIVE = 0x4, SPIF_SENDCHANGE = 0x3;

    /// <summary>The "press Shift five times" shortcut; handy to turn off on a handheld where Shift is mapped to a button.</summary>
    public static bool StickyShortcut
    {
        get { var s = new STICKYKEYS { cbSize = 8 }; return SystemParametersInfo(SPI_GETSTICKYKEYS, 8, ref s, 0) && (s.dwFlags & SKF_HOTKEYACTIVE) != 0; }
        set
        {
            var s = new STICKYKEYS { cbSize = 8 };
            if (!SystemParametersInfo(SPI_GETSTICKYKEYS, 8, ref s, 0)) return;
            s.dwFlags = value ? s.dwFlags | SKF_HOTKEYACTIVE : s.dwFlags & ~SKF_HOTKEYACTIVE;
            SystemParametersInfo(SPI_SETSTICKYKEYS, 8, ref s, SPIF_SENDCHANGE);
        }
    }
}
