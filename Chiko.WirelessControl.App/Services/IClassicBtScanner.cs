using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Chiko.WirelessControl.App.Services;

public interface IClassicBtScanner
{
    Task<IReadOnlyList<ClassicBtDevice>> ScanAsync(CancellationToken ct);
}

public sealed record ClassicBtDevice(string Name, string Address);
