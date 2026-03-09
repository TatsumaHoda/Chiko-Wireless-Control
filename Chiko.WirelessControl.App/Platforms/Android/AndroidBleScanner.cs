#if ANDROID
using Android.Bluetooth;
using Android.Bluetooth.LE;
// using Android.OS;  // ★削除：OperationCanceledException の衝突原因
using Chiko.WirelessControl.App.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Chiko.WirelessControl.App.Platforms.Android;

public sealed class AndroidBleScanner : IBleScanner
{
    public async Task<IReadOnlyList<BleDevice>> ScanAsync(CancellationToken ct)
    {
        var adapter = BluetoothAdapter.DefaultAdapter;
        if (adapter is null || !adapter.IsEnabled)
            return Array.Empty<BleDevice>();

        var scanner = adapter.BluetoothLeScanner;
        if (scanner is null)
            return Array.Empty<BleDevice>();

        var set = new Dictionary<string, BleDevice>(StringComparer.OrdinalIgnoreCase);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(6));

        var callback = new ScanCb((name, id) =>
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            if (string.IsNullOrWhiteSpace(name)) name = "(no name)";
            set[id] = new BleDevice(name, id);
        });

        try
        {
            var settings = new ScanSettings.Builder()
                .SetScanMode(global::Android.Bluetooth.LE.ScanMode.LowLatency) // ★LE側を明示
                .Build();

            scanner.StartScan(null, settings, callback);

            await Task.Delay(Timeout.Infinite, timeoutCts.Token);
        }
        catch (System.OperationCanceledException)
        {
            // timeout / cancel は正常系
        }
        catch
        {
            // 権限不足など：空で返す
            return Array.Empty<BleDevice>();
        }
        finally
        {
            try { scanner.StopScan(callback); } catch { }
        }

        return set.Values
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed class ScanCb : ScanCallback
    {
        private readonly Action<string?, string?> _onFound;
        public ScanCb(Action<string?, string?> onFound) => _onFound = onFound;

        public override void OnScanResult(ScanCallbackType callbackType, ScanResult? result)
        {
            try
            {
                var dev = result?.Device;
                if (dev is null) return;

                var name = dev.Name ?? result?.ScanRecord?.DeviceName ?? "";
                var id = dev.Address ?? "";
                _onFound(name, id);
            }
            catch { }
        }

        public override void OnBatchScanResults(System.Collections.Generic.IList<ScanResult>? results)
        {
            if (results is null) return;
            foreach (var r in results) OnScanResult(ScanCallbackType.AllMatches, r);
        }
    }
}
#endif
