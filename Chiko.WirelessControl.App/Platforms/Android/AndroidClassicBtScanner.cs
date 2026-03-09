#if ANDROID
using Android.Bluetooth;
using Android.Content;
using Android.Runtime;
using Microsoft.Maui.ApplicationModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chiko.WirelessControl.App.Services;

// ★追加：カスタム権限（Platforms/Android/AndroidPermissions.cs に置く想定）
using Chiko.WirelessControl.App.Platforms.Android;

namespace Chiko.WirelessControl.App.Platforms.Android;

public sealed class AndroidClassicBtScanner : IClassicBtScanner
{
    public async Task<IReadOnlyList<ClassicBtDevice>> ScanAsync(CancellationToken ct)
    {
        // ★ ここで権限が無いと結果が出ない（Android 12+）
        if (!await EnsureBtPermissionsAsync())
            return Array.Empty<ClassicBtDevice>();

        var adapter = BluetoothAdapter.DefaultAdapter;
        if (adapter is null || !adapter.IsEnabled)
            return Array.Empty<ClassicBtDevice>();

        var set = new Dictionary<string, ClassicBtDevice>(StringComparer.OrdinalIgnoreCase);

        // 先にペア済みを投入
        try
        {
            foreach (var d in adapter.BondedDevices ?? Array.Empty<BluetoothDevice>())
            {
                var name = d?.Name ?? "";
                var addr = d?.Address ?? "";
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(addr))
                    set[addr] = new ClassicBtDevice(name, addr);
            }
        }
        catch { /* 端末差分は握りつぶし */ }

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var receiver = new DiscoveryReceiver(
            onFound: dev =>
            {
                try
                {
                    var name = dev?.Name ?? "";
                    var addr = dev?.Address ?? "";
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(addr)) return;

                    set[addr] = new ClassicBtDevice(name, addr);
                }
                catch { }
            },
            onFinished: () => tcs.TrySetResult(null));

        var filter = new IntentFilter();
        filter.AddAction(BluetoothDevice.ActionFound);
        filter.AddAction(BluetoothAdapter.ActionDiscoveryFinished);

        // ★修正：global:: を付けて namespace 衝突回避（CS0234対策）
        var ctx = global::Android.App.Application.Context;
        ctx.RegisterReceiver(receiver, filter);

        try
        {
            // 競合回避：既に探索中なら止めてから
            if (adapter.IsDiscovering)
                adapter.CancelDiscovery();

            adapter.StartDiscovery();

            // タイムアウト（例えば 6秒）で打ち切り
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(6));

            await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, timeoutCts.Token));

            // タイムアウトした場合も結果は返す
        }
        catch (OperationCanceledException) { }
        finally
        {
            try
            {
                if (adapter.IsDiscovering)
                    adapter.CancelDiscovery();
            }
            catch { }

            try { ctx.UnregisterReceiver(receiver); } catch { }
        }

        return set.Values
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<bool> EnsureBtPermissionsAsync()
    {
        // Android 12+(API31) : BLUETOOTH_SCAN / CONNECT（実行時許可が必要）
        // Android 11以下: 位置情報が必要になることがある

        // ★修正：global:: を付けて namespace 衝突回避（CS0234対策）
        if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.S)
        {
            // ★修正：MAUI標準 Permissions.BluetoothScan/Connect は無いのでカスタム権限を使う（CS0426対策）
            var scan = await Permissions.CheckStatusAsync<BluetoothScanPermission>();
            if (scan != PermissionStatus.Granted)
                scan = await Permissions.RequestAsync<BluetoothScanPermission>();

            var conn = await Permissions.CheckStatusAsync<BluetoothConnectPermission>();
            if (conn != PermissionStatus.Granted)
                conn = await Permissions.RequestAsync<BluetoothConnectPermission>();

            return scan == PermissionStatus.Granted && conn == PermissionStatus.Granted;
        }
        else
        {
            // 端末により Location を要求されることがある
            var loc = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (loc != PermissionStatus.Granted)
                loc = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            return loc == PermissionStatus.Granted;
        }
    }

    private sealed class DiscoveryReceiver : BroadcastReceiver, IDisposable
    {
        private readonly Action<BluetoothDevice?> _onFound;
        private readonly Action _onFinished;

        public DiscoveryReceiver(Action<BluetoothDevice?> onFound, Action onFinished)
        {
            _onFound = onFound;
            _onFinished = onFinished;
        }

        public override void OnReceive(Context? context, Intent? intent)
        {
            var action = intent?.Action ?? "";
            if (action == BluetoothDevice.ActionFound)
            {
                // API33 以降は GetParcelableExtra(key, Class) 推奨
                BluetoothDevice? device =
                    global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.Tiramisu
                        ? intent?.GetParcelableExtra(BluetoothDevice.ExtraDevice, Java.Lang.Class.FromType(typeof(BluetoothDevice))) as BluetoothDevice
                        : intent?.GetParcelableExtra(BluetoothDevice.ExtraDevice) as BluetoothDevice;

                _onFound(device);
            }
            else if (action == BluetoothAdapter.ActionDiscoveryFinished)
            {
                _onFinished();
            }
        }

        public void Dispose() { }
    }
}
#endif
