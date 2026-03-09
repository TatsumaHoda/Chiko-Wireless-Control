#if ANDROID
using Android.Content;
using Android.Net.Wifi;

namespace Chiko.WirelessControl.App.Services;

public sealed class WifiApScanner : IWifiApScanner
{
    public async Task<List<string>> ScanSsidsAsync(CancellationToken ct)
    {
#if ANDROID
        Android.Util.Log.Debug("CHIKO", "[WIFI] ScanSsidsAsync start");
#endif
        var context = Android.App.Application.Context;
        var wifi = (WifiManager?)context.GetSystemService(Context.WifiService);
        if (wifi is null) return new();

        var tcs = new TaskCompletionSource<bool>();

        using var receiver = new ScanResultsReceiver(() => tcs.TrySetResult(true));
        context.RegisterReceiver(receiver, new IntentFilter(WifiManager.ScanResultsAvailableAction));

        try
        {
            // スキャン開始
            wifi.StartScan();

            // 結果待ち（タイムアウト）
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, timeoutCts.Token));

            // 取得
            var results = wifi.ScanResults;
            var ssids = results
                .Select(r => r?.Ssid)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .ToList();
#if ANDROID
            Android.Util.Log.Debug("CHIKO", "[WIFI] results count=" + results.Count);
#endif

            return ssids;
        }
        finally
        {
            try { context.UnregisterReceiver(receiver); } catch { }
        }
    }

    private sealed class ScanResultsReceiver : BroadcastReceiver
    {
        private readonly Action _onReceived;
        public ScanResultsReceiver(Action onReceived) => _onReceived = onReceived;
        public override void OnReceive(Context? context, Intent? intent) => _onReceived();
    }
}
#endif
