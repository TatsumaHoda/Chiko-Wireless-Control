using System.Text;
using Chiko.WirelessControl.Core.Abstractions;
using Chiko.WirelessControl.Core.Models;

namespace Chiko.WirelessControl.Protocol.Sessions;

public sealed class ChikoSession
{
    private readonly IChikoConnection _conn;

    public ChikoSession(IChikoConnection conn)
    {
        _conn = conn;
    }

    // まずは制御有効だけ確定（W10 0100）
    public async Task InitializeAfterConnectAsync(CancellationToken ct)
    {
        // 例：BODY="W100100" を送る（フレーム化は後で共通化しても良い）
        // ※ ここはあなたの既存Protocol実装に合わせてBuildFrameに置換する想定
        var frame = BuildFrameAscii(body: "W100100"); // CONTROL_FLG=0100
        await _conn.SendAsync(frame, ct);

        // TODO: 書き込みフラグ（コマンド確定後にここへ追加）
        // var writeEnable = BuildFrameAscii("W??0100");
        // await _conn.SendAsync(writeEnable, ct);
    }

    // まずは“最低限動く” ASCII フレーム化（既存のProtocolCodecがあるなら置換）
    // 例: %01# + BODY + BCC + CR
    private static ReadOnlyMemory<byte> BuildFrameAscii(string body)
    {
        var header = "%01#";
        var core = header + body;

        byte bcc = 0;
        foreach (var ch in core)
            bcc ^= (byte)ch;

        // BCC を 2桁HEX（上位/下位ニブル）
        string bccHex = bcc.ToString("X2");
        string frame = core + bccHex + "\r";
        return Encoding.ASCII.GetBytes(frame);
    }
}
