#if ANDROID
using System;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Android.Net;
using Android.Net.Wifi;
using Chiko.WirelessControl.App.Services;

namespace Chiko.WirelessControl.App.Platforms.Android;

public sealed class AndroidWifiSsidConnector : IWifiSsidConnector
{
    private readonly Context _ctx;

    public AndroidWifiSsidConnector()
    {
        // ★ここがポイント：namespace末尾がAndroidなので global:: を付ける
        _ctx = global::Android.App.Application.Context;
    }

    public async Task<IAsyncDisposable> ConnectAsync(string ssid, string passphrase, CancellationToken ct)
    {
        var cm = (ConnectivityManager)_ctx.GetSystemService(Context.ConnectivityService)!;

        var specifier = new WifiNetworkSpecifier.Builder()
            .SetSsid(ssid)
            .SetWpa2Passphrase(passphrase)
            .Build();

        var request = new NetworkRequest.Builder()
            .AddTransportType(TransportType.Wifi)
            .SetNetworkSpecifier(specifier)
            .Build();

        var tcs = new TaskCompletionSource<Network>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callback = new Callback(tcs);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(25));

        cm.RequestNetwork(request, callback);

        try
        {
            using (timeoutCts.Token.Register(() => tcs.TrySetCanceled(timeoutCts.Token)))
            {
                var network = await tcs.Task.ConfigureAwait(false);

                // 重要：このWi-Fiにプロセスをバインド
                cm.BindProcessToNetwork(network);

                return new Lease(cm, callback);
            }
        }
        catch
        {
            SafeUnregister(cm, callback);
            throw;
        }
    }

    private static void SafeUnregister(ConnectivityManager cm, ConnectivityManager.NetworkCallback cb)
    {
        try { cm.UnregisterNetworkCallback(cb); } catch { }
    }

    private sealed class Callback : ConnectivityManager.NetworkCallback
    {
        private readonly TaskCompletionSource<Network> _tcs;

        public Callback(TaskCompletionSource<Network> tcs) => _tcs = tcs;

        public override void OnAvailable(Network network) => _tcs.TrySetResult(network);

        public override void OnUnavailable()
            => _tcs.TrySetException(new InvalidOperationException("Wi-Fi接続が拒否されたか、利用できませんでした。"));
    }

    private sealed class Lease : IAsyncDisposable
    {
        private readonly ConnectivityManager _cm;
        private readonly ConnectivityManager.NetworkCallback _cb;
        private bool _disposed;

        public Lease(ConnectivityManager cm, ConnectivityManager.NetworkCallback cb)
        {
            _cm = cm;
            _cb = cb;
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;

            try { _cm.BindProcessToNetwork(null); } catch { }
            try { _cm.UnregisterNetworkCallback(_cb); } catch { }

            return ValueTask.CompletedTask;
        }
    }
}
#endif
