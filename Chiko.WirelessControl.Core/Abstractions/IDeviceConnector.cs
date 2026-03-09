using Chiko.WirelessControl.Core.Models;

namespace Chiko.WirelessControl.Core.Abstractions;

public interface IDeviceConnector
{
    TransportKind Kind { get; }
    Task<IChikoConnection> ConnectAsync(DiscoveredDevice device, CancellationToken ct);
}
