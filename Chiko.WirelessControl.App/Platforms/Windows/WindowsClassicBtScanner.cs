#if WINDOWS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Chiko.WirelessControl.App.Services;

namespace Chiko.WirelessControl.App.Platforms.Windows;

public sealed class WindowsClassicBtScanner : IClassicBtScanner
{
    public async Task<IReadOnlyList<ClassicBtDevice>> ScanAsync(CancellationToken ct)
    {
        string[] props =
        [
            "System.ItemNameDisplay",
        "System.Devices.Aep.DeviceAddress",
        "System.Devices.Aep.IsPaired",
        "System.Devices.Aep.IsConnected",
        "System.Devices.ContainerId",
    ];

        // ★ paired / unpaired を両方引く
        var pairedAqs = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
        var unpairedAqs = BluetoothDevice.GetDeviceSelectorFromPairingState(false);

        var paired = await DeviceInformation.FindAllAsync(pairedAqs, props).AsTask(ct);
        var unpaired = await DeviceInformation.FindAllAsync(unpairedAqs, props).AsTask(ct);

        var dis = paired.Concat(unpaired)
                        .GroupBy(x => x.Id)
                        .Select(g => g.First())
                        .ToList();

        var list = new List<ClassicBtDevice>();
        foreach (var di in dis)
        {
            ct.ThrowIfCancellationRequested();

            var name = di.Name ?? "";
            if (string.IsNullOrWhiteSpace(name))
                name = di.Properties.TryGetValue("System.ItemNameDisplay", out var v) ? (v?.ToString() ?? "") : "";
            if (string.IsNullOrWhiteSpace(name)) continue;

            var addr = di.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var a)
                ? (a?.ToString() ?? "")
                : "";

            list.Add(new ClassicBtDevice(name, string.IsNullOrWhiteSpace(addr) ? di.Id : addr));
        }

        return list.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

}
#endif
