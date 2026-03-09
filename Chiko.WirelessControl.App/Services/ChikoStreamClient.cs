// Chiko.WirelessControl.App/Services/ChikoStreamClient.cs
using System.Net.Sockets;
using System.Text;

namespace Chiko.WirelessControl.App.Services;

public sealed class ChikoStreamClient : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly TcpClient? _tcp;
    private readonly bool _leaveOpen;

    // ★同一Streamの送受信を直列化（S4ポーリングと操作コマンドが競合しない）
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    private ChikoStreamClient(Stream stream, TcpClient? tcp = null, bool leaveOpen = false)
    {
        _stream = stream;
        _tcp = tcp;
        _leaveOpen = leaveOpen;
    }

    public static async Task<ChikoStreamClient> ConnectTcpAsync(string host, int port, CancellationToken ct)
    {
        var tcp = new TcpClient();
        using var reg = ct.Register(() => { try { tcp.Dispose(); } catch { } });

        await tcp.ConnectAsync(host, port, ct);
        return new ChikoStreamClient(tcp.GetStream(), tcp);
    }

    // ★BT/BLE の Stream から生成
    public static ChikoStreamClient FromStream(Stream stream, bool leaveOpen = false)
        => new(stream, tcp: null, leaveOpen: leaveOpen);

    public async Task<string> SendCommandAsync(string framedCommandWithoutCr, CancellationToken ct)
    {
        await _ioLock.WaitAsync(ct);
        try
        {
            var enc = Encoding.ASCII;

            // 送信（末尾CR付与）
            var sendBytes = enc.GetBytes(framedCommandWithoutCr + ChikoCommCodec.Terminator);
            await _stream.WriteAsync(sendBytes, ct);
            await _stream.FlushAsync(ct);

            // 受信（CRまで）
            using var ms = new MemoryStream();
            var buf = new byte[256];

            while (true)
            {
                int n = await _stream.ReadAsync(buf, ct);
                if (n == 0) throw new IOException("Remote closed connection");

                ms.Write(buf, 0, n);

                if (buf[n - 1] == (byte)ChikoCommCodec.Terminator)
                    break;

                var arr = ms.ToArray();
                if (arr.Length > 0 && arr[^1] == (byte)ChikoCommCodec.Terminator)
                    break;
            }

            var resp = enc.GetString(ms.ToArray());
            return resp.TrimEnd(ChikoCommCodec.Terminator);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_leaveOpen)
        {
            try { _stream.Dispose(); } catch { }
        }

        try { _tcp?.Close(); } catch { }
        await Task.CompletedTask;
    }
}
