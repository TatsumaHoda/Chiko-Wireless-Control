using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Chiko.WirelessControl.App.Services;

public interface IWifiApScanner
{
    Task<List<string>> ScanSsidsAsync(CancellationToken ct);
}
