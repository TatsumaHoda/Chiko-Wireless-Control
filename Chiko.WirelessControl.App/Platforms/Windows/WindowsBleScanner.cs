#if WINDOWS
using Chiko.WirelessControl.App.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth.Advertisement;

namespace Chiko.WirelessControl.App.Platforms.Windows;

public sealed class WindowsBleScanner : IBleScanner
{
    public async Task<IReadOnlyList<BleDevice>> ScanAsync(CancellationToken ct)
    {
        var set = new Dictionary<string, BleDevice>(StringComparer.OrdinalIgnoreCase);

        var watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };

        watcher.Received += (_, e) =>
        {
            try
            {
                var name = e.Advertisement?.LocalName ?? "";
                if (string.IsNullOrWhiteSpace(name)) return;

                var id = e.BluetoothAddress.ToString("X"); // 例: MAC相当（環境依存）
                set[id] = new BleDevice(name, id);
            }
            catch { }
        };

        watcher.Start();

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(6), ct);
        }
        catch (OperationCanceledException) { }
        finally
        {
            try { watcher.Stop(); } catch { }
        }

        return set.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
#endif
