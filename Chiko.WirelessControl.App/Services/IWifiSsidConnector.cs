using System.Threading;
using System.Threading.Tasks;

namespace Chiko.WirelessControl.App.Services;

public interface IWifiSsidConnector
{
    /// <summary>
    /// 指定SSIDへ一時接続し、成功したらそのWi-Fiへプロセスをバインドする。
    /// 返り値(lease)をDisposeすると解除（バインド解除＋コールバック解除）。
    /// </summary>
    Task<IAsyncDisposable> ConnectAsync(string ssid, string passphrase, CancellationToken ct);
}
