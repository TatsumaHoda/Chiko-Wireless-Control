#if WINDOWS
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Networking.Connectivity;
using Chiko.WirelessControl.App.Services;

namespace Chiko.WirelessControl.App.Platforms.Windows;

/// <summary>
/// Windows: 周囲SSID一覧は取得しない（iOSと同様）
/// → 現在接続中のSSIDのみ返す。未接続なら空リスト。
/// </summary>
public sealed class WindowsWifiApScanner : IWifiApScanner
{
    public Task<List<string>> ScanSsidsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var list = new List<string>();

        var profile = NetworkInformation.GetInternetConnectionProfile();
        if (profile is not null && profile.IsWlanConnectionProfile)
        {
            var ssid = profile.WlanConnectionProfileDetails?.GetConnectedSsid();
            if (!string.IsNullOrWhiteSpace(ssid))
                list.Add(ssid);
        }

        return Task.FromResult(list);
    }
}
#endif
