using System.Threading;
using System.Threading.Tasks;

namespace Chiko.WirelessControl.App.Services;

public interface IClassicBtConnector
{
    /// <summary>
    /// Classic Bluetooth (SPP) に接続し、通信可能なリンクを返す
    /// </summary>
    Task<IChikoLink> ConnectAsync(string address, CancellationToken ct);
}
