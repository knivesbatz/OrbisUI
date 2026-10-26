using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.RegularExpressions;
using OrbisUI.Core;

namespace OrbisUI.Services;

/// <summary>
/// Native Wi-Fi (through netsh, so no extra permissions) and audio output switching (Core Audio + the policy-config
/// interface Windows' own sound settings use), plus the default microphone's mute and level. Bluetooth is in Bluetooth.cs.
/// </summary>
public static class Devices
{
    // ---------------- Wi-Fi ----------------
    public sealed record Network(string Ssid, string Auth, int Signal, bool Saved, bool Connected);

    static string Netsh(string args)
    {
        var psi = new ProcessStartInfo("netsh.exe", args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return o;
    }

    public static List<Network> WifiNetworks()
    {
        var saved = Regex.Matches(Netsh("wlan show profiles"), @"All User Profile\s*:\s*(.+)").Select(m => m.Groups[1].Value.Trim()).ToHashSet();
        var cur = Regex.Match(Netsh("wlan show interfaces"), @"^\s*SSID\s*:\s*(.+)$", RegexOptions.Multiline).Groups[1].Value.Trim();
        var list = new List<Network>();
        foreach (var block in Regex.Split(Netsh("wlan show networks mode=bssid"), @"(?m)^SSID \d+ : ").Skip(1))
        {
            var ssid = block.Split('\n')[0].Trim();
            if (ssid.Length == 0) continue;
            var auth = Regex.Match(block, @"Authentication\s*:\s*(.+)").Groups[1].Value.Trim();
            int.TryParse(Regex.Match(block, @"Signal\s*:\s*(\d+)%").Groups[1].Value, out var sig);
            list.Add(new Network(ssid, auth, sig, saved.Contains(ssid), ssid == cur));
        }
        return list.GroupBy(n => n.Ssid).Select(g => g.MaxBy(n => n.Signal)!).OrderByDescending(n => n.Connected).ThenByDescending(n => n.Signal).ToList();
    }

    /// <summary>
    /// Asks every Wi-Fi adapter for a fresh scan and waits for it. netsh alone only reports the cached list, which while
    /// connected is often just the current network. Blocks for a few seconds, so call it off the UI thread.
    /// </summary>
    public static void ScanWifi()
    {
        if (WlanOpenHandle(2, IntPtr.Zero, out _, out var h) != 0) return;
        try
        {
            if (WlanEnumInterfaces(h, IntPtr.Zero, out var list) != 0) return;
            try
            {
                int n = Marshal.ReadInt32(list);
                for (int i = 0; i < n; i++)
                {
                    // WLAN_INTERFACE_INFO_LIST: two DWORDs, then 532-byte entries starting with the interface GUID.
                    var guid = Marshal.PtrToStructure<Guid>(list + 8 + i * 532);
                    WlanScan(h, ref guid, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                }
            }
            finally { WlanFreeMemory(list); }
            Thread.Sleep(4000); // Windows finishes a scan within 4 s
        }
        finally { WlanCloseHandle(h, IntPtr.Zero); }
    }

    [DllImport("wlanapi.dll")] static extern int WlanOpenHandle(uint ver, IntPtr reserved, out uint negotiated, out IntPtr handle);
    [DllImport("wlanapi.dll")] static extern int WlanCloseHandle(IntPtr handle, IntPtr reserved);
    [DllImport("wlanapi.dll")] static extern int WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] static extern int WlanScan(IntPtr handle, ref Guid iface, IntPtr ssid, IntPtr ie, IntPtr reserved);
    [DllImport("wlanapi.dll")] static extern void WlanFreeMemory(IntPtr mem);

    public static bool IsOpen(Network n) => n.Auth.Equals("Open", StringComparison.OrdinalIgnoreCase);

    /// <summary>Connect to a network. New secured networks need the password; the temporary profile file is deleted right after import.</summary>
    public static string Connect(Network n, string? password)
    {
        if (!n.Saved)
        {
            var name = SecurityElement.Escape(n.Ssid);
            var sec = IsOpen(n)
                ? "<authEncryption><authentication>open</authentication><encryption>none</encryption><useOneX>false</useOneX></authEncryption>"
                : $"<authEncryption><authentication>{(n.Auth.Contains("WPA3") ? "WPA3SAE" : "WPA2PSK")}</authentication><encryption>AES</encryption><useOneX>false</useOneX></authEncryption>"
                  + $"<sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>{SecurityElement.Escape(password ?? "")}</keyMaterial></sharedKey>";
            var xml = $"<?xml version=\"1.0\"?><WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\"><name>{name}</name><SSIDConfig><SSID><name>{name}</name></SSID></SSIDConfig>"
                + $"<connectionType>ESS</connectionType><connectionMode>auto</connectionMode><MSM><security>{sec}</security></MSM></WLANProfile>";
            var file = Path.Combine(Path.GetTempPath(), $"orbisui-wifi-{Guid.NewGuid():N}.xml");
            try
            {
                File.WriteAllText(file, xml);
                var add = Netsh($"wlan add profile filename=\"{file}\" user=current");
                if (!add.Contains("is added", StringComparison.OrdinalIgnoreCase)) return add.Trim();
            }
            finally { try { File.Delete(file); } catch { } }
        }
        return Netsh($"wlan connect name=\"{n.Ssid}\" ssid=\"{n.Ssid}\"").Trim();
    }

    public static void Disconnect() => Netsh("wlan disconnect");

    // ---------------- Audio output ----------------
    public sealed record AudioDevice(string Id, string Name, bool IsDefault);

    /// <param name="flow">0 = outputs (render), 1 = microphones (capture).</param>
    public static List<AudioDevice> AudioOutputs(int flow = 0)
    {
        var list = new List<AudioDevice>();
        var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            en.GetDefaultAudioEndpoint(flow, 1, out var def);
            def.GetId(out var defId);
            Marshal.ReleaseComObject(def);
            en.EnumAudioEndpoints(flow, 1 /*active*/, out var col);
            col.GetCount(out var n);
            for (int i = 0; i < n; i++)
            {
                col.Item(i, out var d);
                d.GetId(out var id);
                d.OpenPropertyStore(0, out var ps);
                var key = new PropertyKey { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
                ps.GetValue(ref key, out var pv);
                var name = pv.vt == 31 ? Marshal.PtrToStringUni(pv.p) ?? id : id;
                PropVariantClear(ref pv);
                list.Add(new AudioDevice(id, name, id == defId));
                Marshal.ReleaseComObject(ps); Marshal.ReleaseComObject(d);
            }
            Marshal.ReleaseComObject(col);
        }
        finally { Marshal.ReleaseComObject(en); }
        return list.OrderBy(d => d.Name).ToList();
    }

    /// <summary>Make this the default output for every role (console, multimedia, communications).</summary>
    public static void SetDefaultOutput(string id)
    {
        var pc = (IPolicyConfig)new CPolicyConfigClient();
        try { for (int role = 0; role < 3; role++) Marshal.ThrowExceptionForHR(pc.SetDefaultEndpoint(id, role)); }
        finally { Marshal.ReleaseComObject(pc); }
        Log.Info("Audio output set to " + id);
    }

    /// <summary>The default microphone's mute state and level (0..1); null when there is no microphone.</summary>
    public static (bool Muted, float Level)? MicState()
    {
        var v = MicVolume();
        if (v == null) return null;
        try { v.GetMute(out var m); v.GetMasterVolumeLevelScalar(out var l); return (m, l); }
        finally { Marshal.ReleaseComObject(v); }
    }

    public static void SetMic(bool? mute = null, float? level = null)
    {
        var v = MicVolume();
        if (v == null) return;
        var ctx = Guid.Empty;
        try
        {
            if (mute is bool m) v.SetMute(m, ref ctx);
            if (level is float l) v.SetMasterVolumeLevelScalar(Math.Clamp(l, 0, 1), ref ctx);
        }
        finally { Marshal.ReleaseComObject(v); }
    }

    static IAudioEndpointVolume? MicVolume()
    {
        var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            if (en.GetDefaultAudioEndpoint(1, 1, out var d) != 0 || d == null) return null;
            var iid = typeof(IAudioEndpointVolume).GUID;
            d.Activate(ref iid, 1 /*CLSCTX_INPROC_SERVER*/, IntPtr.Zero, out var p);
            Marshal.ReleaseComObject(d);
            if (p == IntPtr.Zero) return null;
            var v = (IAudioEndpointVolume)Marshal.GetObjectForIUnknown(p);
            Marshal.Release(p);
            return v;
        }
        finally { Marshal.ReleaseComObject(en); }
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr n);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr n);
        [PreserveSig] int GetChannelCount(out uint n);
        [PreserveSig] int SetMasterVolumeLevel(float db, ref Guid ctx);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolumeLevel(out float db);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint ch, float db, ref Guid ctx);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
        [PreserveSig] int GetChannelVolumeLevel(uint ch, out float db);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint ch, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid fmtid; public int pid; }
    [StructLayout(LayoutKind.Sequential)] struct PropVariant { public ushort vt; ushort r1, r2, r3; public IntPtr p; IntPtr p2; }
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant pv);

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }
    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")] class CPolicyConfigClient { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int ctx, IntPtr p, out IntPtr iface);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int n);
        [PreserveSig] int GetAt(int i, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }

    // Undocumented but stable since Windows 7; it's what the Sound control panel uses to change the default device.
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(string id, IntPtr f);
        [PreserveSig] int GetDeviceFormat(string id, int def, IntPtr f);
        [PreserveSig] int ResetDeviceFormat(string id);
        [PreserveSig] int SetDeviceFormat(string id, IntPtr a, IntPtr b);
        [PreserveSig] int GetProcessingPeriod(string id, int def, IntPtr a, IntPtr b);
        [PreserveSig] int SetProcessingPeriod(string id, IntPtr a);
        [PreserveSig] int GetShareMode(string id, IntPtr m);
        [PreserveSig] int SetShareMode(string id, IntPtr m);
        [PreserveSig] int GetPropertyValue(string id, IntPtr key, IntPtr v);
        [PreserveSig] int SetPropertyValue(string id, IntPtr key, IntPtr v);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility(string id, int visible);
    }
}
