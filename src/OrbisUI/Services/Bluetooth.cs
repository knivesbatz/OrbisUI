using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using OrbisUI.Core;

namespace OrbisUI.Services;

/// <summary>
/// Native Bluetooth through the WinRT device APIs Windows Settings uses: radio on/off, paired devices with their
/// connection state, discovery of nearby devices, pairing (PINs are confirmed automatically), connect and forget.
/// </summary>
public static class Bluetooth
{
    public sealed record Device(string Id, string Name, bool Paired, bool Connected, bool LowEnergy);

    static async Task<Radio?> RadioAsync() => (await Radio.GetRadiosAsync()).FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);

    public static async Task<bool?> IsOnAsync() => (await RadioAsync()) is Radio r ? r.State == RadioState.On : null;

    public static async Task<bool> SetOnAsync(bool on)
    {
        if (await RadioAsync() is not Radio r) return false;
        if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed) return false;
        return await r.SetStateAsync(on ? RadioState.On : RadioState.Off) == RadioAccessStatus.Allowed;
    }

    static readonly string[] Props = { "System.Devices.Aep.IsConnected", "System.Devices.Aep.IsPaired" };

    /// <summary>Paired devices, and with <paramref name="discover"/> nearby unpaired ones too (an inquiry takes a few seconds).</summary>
    public static async Task<List<Device>> DevicesAsync(bool discover)
    {
        var list = new List<Device>();
        async Task Add(string selector, bool le)
        {
            foreach (var d in await DeviceInformation.FindAllAsync(selector, Props, DeviceInformationKind.AssociationEndpoint))
            {
                if (string.IsNullOrWhiteSpace(d.Name) || list.Any(x => x.Id == d.Id)) continue;
                bool conn = d.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var c) && c is true;
                list.Add(new Device(d.Id, d.Name, d.Pairing.IsPaired, conn, le));
            }
        }
        await Add(BluetoothDevice.GetDeviceSelectorFromPairingState(true), false);
        await Add(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), true);
        if (discover)
        {
            // FindAllAsync waits for the whole inquiry; a watcher gives the nearby devices it has seen within 10 s.
            foreach (var (sel, le) in new[] { (BluetoothDevice.GetDeviceSelectorFromPairingState(false), false), (BluetoothLEDevice.GetDeviceSelectorFromPairingState(false), true) })
            {
                var seen = new System.Collections.Concurrent.ConcurrentDictionary<string, DeviceInformation>();
                var w = DeviceInformation.CreateWatcher(sel, Props, DeviceInformationKind.AssociationEndpoint);
                w.Added += (_, d) => seen[d.Id] = d;
                w.Updated += (_, u) => { if (seen.TryGetValue(u.Id, out var d)) d.Update(u); };
                w.Start();
                await Task.Delay(le ? 4000 : 10000);
                w.Stop();
                foreach (var d in seen.Values)
                    if (!string.IsNullOrWhiteSpace(d.Name) && list.All(x => x.Id != d.Id)) list.Add(new Device(d.Id, d.Name, false, false, le));
            }
        }
        return list.OrderByDescending(d => d.Connected).ThenByDescending(d => d.Paired).ThenBy(d => d.Name).ToList();
    }

    /// <summary>Pairs with the device, accepting the confirmation or PIN Windows would otherwise ask about. Returns a status text.</summary>
    public static async Task<string> PairAsync(string id)
    {
        var d = await DeviceInformation.CreateFromIdAsync(id);
        if (d.Pairing.IsPaired) return "Already paired";
        var custom = d.Pairing.Custom;
        void OnRequest(DeviceInformationCustomPairing s, DevicePairingRequestedEventArgs e)
        {
            // Controllers and headsets use "just works" or show a PIN to compare; legacy devices take 0000.
            if (e.PairingKind == DevicePairingKinds.ProvidePin) e.Accept("0000"); else e.Accept();
        }
        custom.PairingRequested += OnRequest;
        try
        {
            var r = await custom.PairAsync(DevicePairingKinds.ConfirmOnly | DevicePairingKinds.DisplayPin | DevicePairingKinds.ConfirmPinMatch | DevicePairingKinds.ProvidePin);
            Log.Info($"Bluetooth pair {d.Name}: {r.Status}");
            return r.Status is DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired ? "Paired" : Friendly(r.Status.ToString());
        }
        finally { custom.PairingRequested -= OnRequest; }
    }

    public static async Task<string> ForgetAsync(string id)
    {
        var d = await DeviceInformation.CreateFromIdAsync(id);
        var r = await d.Pairing.UnpairAsync();
        Log.Info($"Bluetooth forget {d.Name}: {r.Status}");
        return r.Status is DeviceUnpairingResultStatus.Unpaired or DeviceUnpairingResultStatus.AlreadyUnpaired ? "Removed" : Friendly(r.Status.ToString());
    }

    /// <summary>
    /// Windows has no public "connect" call; it connects paired devices when they are used. Asking a classic device for
    /// its services uncached (or an LE device for its GATT services) makes Windows open the link, which is what this does.
    /// </summary>
    public static async Task<bool> ConnectAsync(Device dev)
    {
        if (dev.LowEnergy)
        {
            using var le = await BluetoothLEDevice.FromIdAsync(dev.Id);
            if (le == null) return false;
            await le.GetGattServicesAsync(BluetoothCacheMode.Uncached);
            return le.ConnectionStatus == BluetoothConnectionStatus.Connected;
        }
        using var bt = await BluetoothDevice.FromIdAsync(dev.Id);
        if (bt == null) return false;
        await bt.GetRfcommServicesAsync(BluetoothCacheMode.Uncached);
        return bt.ConnectionStatus == BluetoothConnectionStatus.Connected;
    }

    static string Friendly(string status) => System.Text.RegularExpressions.Regex.Replace(status, "(?<=[a-z])(?=[A-Z])", " ");
}
