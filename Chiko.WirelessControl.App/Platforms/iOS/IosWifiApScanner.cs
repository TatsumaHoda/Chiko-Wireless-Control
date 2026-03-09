#if IOS
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Chiko.WirelessControl.App.Services;
using NetworkExtension;

namespace Chiko.WirelessControl.App.Platforms.iOS;

public sealed class IosWifiApScanner : IWifiApScanner
{
    public async Task<List<string>> ScanSsidsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            var net = await NEHotspotNetwork.FetchCurrentAsync();
            var ssid = net?.Ssid ?? "";

            if (string.IsNullOrWhiteSpace(ssid))
                return new List<string>();          // ★空

            return new List<string> { ssid };       // ★1件だけ返す
        }
        catch
        {
            return new List<string>();              // ★空（上位でメッセージ表示）
        }
    }
}
#endif
