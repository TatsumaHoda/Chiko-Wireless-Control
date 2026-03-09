using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Chiko.WirelessControl.App.Services;

public interface IBleScanner
{
    Task<IReadOnlyList<BleDevice>> ScanAsync(CancellationToken ct);
}

public sealed record BleDevice(string Name, string Id);
