#if IOS || MACCATALYST
using Chiko.WirelessControl.App.Services;
using CoreBluetooth;
using CoreFoundation;
using Foundation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Chiko.WirelessControl.App.Platforms.iOS;

public sealed class IosBleScanner : IBleScanner
{
    public async Task<IReadOnlyList<BleDevice>> ScanAsync(CancellationToken ct)
    {
        var set = new Dictionary<string, BleDevice>(StringComparer.OrdinalIgnoreCase);
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        CBCentralManager? mgr = null;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(6));
        timeoutCts.Token.Register(() => tcs.TrySetCanceled(timeoutCts.Token));

        mgr = new CBCentralManager(new CentralDelegate(
            onReady: () =>
            {
                try
                {
                    // サービス指定なしでスキャン（必要なら後で UUID に絞る）
                    mgr.ScanForPeripherals(peripheralUuids: null, options: new PeripheralScanningOptions
                    {
                        AllowDuplicatesKey = false
                    });
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            },
            onDiscovered: (p, adv, rssi) =>
            {
                try
                {
                    var name = p?.Name ?? "";
                    var id = p?.Identifier?.AsString() ?? "";
                    if (string.IsNullOrWhiteSpace(id)) return;

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        // Advから取れる場合がある
                        if (adv != null && adv.TryGetValue(CBAdvertisement.DataLocalNameKey, out var v) && v is NSString s)
                            name = s.ToString();
                    }

                    if (string.IsNullOrWhiteSpace(name))
                        name = "(no name)";

                    set[id] = new BleDevice(name, id);
                }
                catch { }
            },
            onFailed: ex => tcs.TrySetException(ex)
        ), DispatchQueue.MainQueue);

        try
        {
            // CentralManager 初期化～Ready待ち
            await tcs.Task;
        }
        catch (TaskCanceledException)
        {
            // timeout/cancel OK
        }
        finally
        {
            try { mgr?.StopScan(); } catch { }
        }

        return set.Values
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed class CentralDelegate : CBCentralManagerDelegate
    {
        private readonly Action _onReady;
        private readonly Action<CBPeripheral, NSDictionary, NSNumber> _onDiscovered;
        private readonly Action<Exception> _onFailed;

        public CentralDelegate(
            Action onReady,
            Action<CBPeripheral, NSDictionary, NSNumber> onDiscovered,
            Action<Exception> onFailed)
        {
            _onReady = onReady;
            _onDiscovered = onDiscovered;
            _onFailed = onFailed;
        }

        public override void UpdatedState(CBCentralManager central)
        {
            switch (central.State)
            {
                case CBManagerState.PoweredOn:
                    _onReady();
                    break;

                case CBManagerState.Unsupported:
                case CBManagerState.Unauthorized:
                    _onFailed(new InvalidOperationException("Bluetooth is not available (unsupported/unauthorized)."));
                    break;

                default:
                    // PoweredOff / Resetting / Unknown は待つ or 失敗扱いは運用次第
                    break;
            }
        }


        public override void DiscoveredPeripheral(CBCentralManager central, CBPeripheral peripheral, NSDictionary advertisementData, NSNumber RSSI)
            => _onDiscovered(peripheral, advertisementData, RSSI);
    }
}
#endif
