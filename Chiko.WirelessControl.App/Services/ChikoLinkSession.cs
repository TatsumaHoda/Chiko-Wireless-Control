using System.Text;

namespace Chiko.WirelessControl.App.Services;

public sealed class ChikoLinkSession : IAsyncDisposable
{
    private readonly IChikoLink _link;

    // 送信直列化 + 送信間隔制御（仕様：100ms未満禁止）
    private readonly SemaphoreSlim _txLock = new(1, 1);
    private DateTime _lastSendUtc = DateTime.MinValue;

    public ChikoLinkSession(IChikoLink link)
    {
        _link = link;
    }

    public async ValueTask DisposeAsync()
    {
        await _link.DisposeAsync();
        _txLock.Dispose();
    }

    /// <summary>
    /// body例: "W010100", "W021500", "W800100", "RS40000"
    /// 送信:%01# + body + BCC + CR
    /// 受信:%01$... + BCC + CR （CRまで1フレーム）
    /// </summary>
    public async Task<string> SendAndReceiveAsync(string body, CancellationToken ct)
    {
        // %01#bodyBCC\r
        var frame = ChikoCommCodec.EditSendData(body) + ChikoCommCodec.Terminator;

        await _txLock.WaitAsync(ct);
        try
        {
            // ★ 100ms未満禁止
            var now = DateTime.UtcNow;
            var elapsedMs = (now - _lastSendUtc).TotalMilliseconds;
            if (elapsedMs < 100)
                await Task.Delay(100 - (int)elapsedMs, ct);

            // 送信
            var bytes = Encoding.ASCII.GetBytes(frame);
            await _link.Stream.WriteAsync(bytes, 0, bytes.Length, ct);
            await _link.Stream.FlushAsync(ct);

            _lastSendUtc = DateTime.UtcNow;

            // 受信（CRまで）
            return await ReadUntilCrAsync(ct);
        }
        finally
        {
            _txLock.Release();
        }
    }

    /// <summary>
    /// % から CR までを 1 フレームとして読み込む（仕様どおり）
    /// </summary>
    private async Task<string> ReadUntilCrAsync(CancellationToken ct)
    {
        var sb = new StringBuilder(256);
        var buf = new byte[1];

        while (true)
        {
            int n = await _link.Stream.ReadAsync(buf, 0, 1, ct);
            if (n <= 0) throw new IOException("Disconnected (read 0).");

            char ch = (char)buf[0];
            sb.Append(ch);

            if (ch == ChikoCommCodec.Terminator)
                break;
        }

        return sb.ToString();
    }
}
