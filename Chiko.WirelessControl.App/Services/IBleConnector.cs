using System.Threading;
using System.Threading.Tasks;

namespace Chiko.WirelessControl.App.Services;

public interface IBleConnector
{
    /// <summary>
    /// BLE(UART) に接続し、通信可能なリンクを返す
    /// </summary>
    Task<IChikoLink> ConnectAsync(string deviceId, CancellationToken ct);
}
