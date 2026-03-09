using System.Net.Sockets;
using Chiko.WirelessControl.Core.Abstractions;
using Chiko.WirelessControl.Core.Models;

namespace Chiko.WirelessControl.Transports.Wifi;

public sealed class WifiConnector : IDeviceConnector
{
    public TransportKind Kind => TransportKind.Wifi;

    public async Task<IChikoConnection> ConnectAsync(DiscoveredDevice device, CancellationToken ct)
    {
        // device.Id を IP として扱う（例: "192.168.4.1"）
        var client = new TcpClient();
        await client.ConnectAsync(device.Id, 10001, ct); // Portはあなたの規約に合わせる
        return new TcpChikoConnection(client);
    }
}

internal sealed class TcpChikoConnection : IChikoConnection
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;

    public TcpChikoConnection(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
    }

    public TransportKind Kind => TransportKind.Wifi;
    public bool IsConnected => _client.Connected;

    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
        => _stream.WriteAsync(data, ct).AsTask();

    public Task<int> ReceiveAsync(Memory<byte> buffer, CancellationToken ct)
        => _stream.ReadAsync(buffer, ct).AsTask();

    public ValueTask DisposeAsync()
    {
        try { _stream.Close(); } catch { }
        try { _client.Close(); } catch { }
        return ValueTask.CompletedTask;
    }
}
