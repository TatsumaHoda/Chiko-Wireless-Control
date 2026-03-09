using Chiko.WirelessControl.Core.Models;

namespace Chiko.WirelessControl.Core.Abstractions;

public interface IDeviceScanner
{
    TransportKind Kind { get; }
    Task<IReadOnlyList<DiscoveredDevice>> ScanAsync(TimeSpan timeout, CancellationToken ct);
}
