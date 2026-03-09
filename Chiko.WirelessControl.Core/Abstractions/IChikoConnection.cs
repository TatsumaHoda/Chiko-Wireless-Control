using Chiko.WirelessControl.Core.Models;

namespace Chiko.WirelessControl.Core.Abstractions;

public interface IChikoConnection : IAsyncDisposable
{
    TransportKind Kind { get; }
    bool IsConnected { get; }

    Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct);
    Task<int> ReceiveAsync(Memory<byte> buffer, CancellationToken ct);
}
