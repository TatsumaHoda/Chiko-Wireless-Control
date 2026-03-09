// Chiko.WirelessControl.App/Services/ChikoCommCodec.cs
using System.Text;

namespace Chiko.WirelessControl.App.Services;

public static class ChikoCommCodec
{
    // WPF Constants.ControlCode 相当
    public const string Header = "%";
    public const string CommandMark = "#";
    public const char Terminator = '\r';
    public const char ResponseOk = '$';

    // WPF Constants.Comm.COMM_NUMBER 相当
    public const string CommNumber = "01";

    public static string EditSendData(string body) // body例: "W100100" or "R840000"
    {
        var s = Header + CommNumber + CommandMark + body;
        return AddBcc(s);
    }

    public static string AddBcc(string command)
        => command + CalcBcc(command);

    public static string CalcBcc(string command)
    {
        var ascii = Encoding.ASCII.GetBytes(command);
        int x = 0;
        for (int i = 0; i < ascii.Length; i++) x ^= ascii[i];

        int b1 = (x & 0xF0) >> 4;
        int b2 = x & 0x0F;
        return b1.ToString("X") + b2.ToString("X");
    }

    // WPF Lib.GetCommData 相当：該当コマンドのHLを返す（H+L）
    public static string GetCommData(string resp, string commandHex2)
    {
        if (string.IsNullOrEmpty(resp)) return "";

        var lines = resp.Split(Terminator);
        foreach (var line in lines)
        {
            if (line.Length < 13) continue;
            if (line[0] != Header[0]) continue;
            if (line[3] != ResponseOk) continue;
            if (line.Substring(5, 2) != commandHex2) continue;

            // line: %01$R84LLHH...
            string l = line.Substring(7, 2);
            string h = line.Substring(9, 2);
            return h + l;
        }
        return "";
    }
    // ===== 追加：LLHH <-> 値 変換（2桁単位スワップ） =====

    public static string EncodeDec4_LLHH(int value)
    {
        // 10進 4桁 -> LLHH
        // 例: 15 -> "0015" -> "15" + "00" = "1500"
        if (value < 0 || value > 9999) throw new ArgumentOutOfRangeException(nameof(value));
        var s = value.ToString("D4");
        return s.Substring(2, 2) + s.Substring(0, 2);
    }

    public static int DecodeDec4_LLHH(string llhh)
    {
        // LLHH -> LHLL -> int
        if (llhh.Length != 4) throw new ArgumentException("need 4 chars", nameof(llhh));
        var lhll = llhh.Substring(2, 2) + llhh.Substring(0, 2);
        return int.Parse(lhll);
    }

    public static int DecodeHex_LLHH(string llhh, int digits)
    {
        // LLHH(4) or LLLLLH(5) など、末尾2桁=H側 として入替える前提
        // digits=4: "6419" -> "1964"
        // digits=5: "23456" -> "56234"（※仕様が5桁ならこの形）
        if (llhh.Length != digits) throw new ArgumentException($"need {digits} chars", nameof(llhh));

        if (digits == 4)
        {
            var lhll = llhh.Substring(2, 2) + llhh.Substring(0, 2);
            return Convert.ToInt32(lhll, 16);
        }
        else if (digits == 5)
        {
            // 5桁は「末尾2桁を先頭へ」＋「先頭3桁を後ろへ」
            // 例: "30339"（仮） -> "39" + "303" = "39303"
            var swapped = llhh.Substring(3, 2) + llhh.Substring(0, 3);
            return Convert.ToInt32(swapped, 16);
        }

        throw new NotSupportedException("digits must be 4 or 5");
    }

    // ===== 追加：フレーム全体の BCC 検証 =====
    public static bool VerifyBcc(string frameWithBccNoCr)
    {
        // frameWithBccNoCr: 末尾2文字がBCC、CRは除外した文字列
        if (frameWithBccNoCr.Length < 3) return false;

        var body = frameWithBccNoCr.Substring(0, frameWithBccNoCr.Length - 2);
        var bcc = frameWithBccNoCr.Substring(frameWithBccNoCr.Length - 2, 2);

        var calc = CalcBcc(body);
        return string.Equals(calc, bcc, StringComparison.OrdinalIgnoreCase);
    }

    // ===== 追加：S4 応答パース =====
    public sealed record S4Status(
        bool IsRun,
        int Level,                       // ★ 接続時に1回だけUI反映
        bool IsInitialFlowRegistered,
        double Volume_m3min,             // 0.01
        double Outside_kPa,              // 0.01
        double Suction_kPa,              // 0.01
        double Diff_kPa,                 // 0.01
        double Exhaust_kPa,              // 0.01
        double BlowerTemp_C,             // 0.1
        double BoardTemp_C,              // 0.1
        int Speed_rpm,                   // hex 4 or 5
        int RunHours,                    // hex 4 or 5
        double DuctVelocity_ms,          // 0.01
        bool RemoteMode,
        bool UartControlEnabled,
        int ErrorCode                    // hex4
    );

    public static S4Status ParseS4FromFullResponse(string resp)
    {
        // resp: "%01$RS4....BCC\r"
        // まず Terminator で分割し、S4行を拾う
        var lines = resp.Split(Terminator);
        foreach (var lineRaw in lines)
        {
            var line = lineRaw;
            if (string.IsNullOrWhiteSpace(line)) continue;

            // 最低 "%01$RS4" + BCC2 くらい
            if (line.Length < 10) continue;
            if (line[0] != Header[0]) continue;
            if (line[3] != ResponseOk) continue;

            // 応答は "%01$RS4...."
            if (line.Substring(4, 3) != "RS4") continue;

            // CRはSplitで落ちている前提。末尾2文字がBCC
            if (line.Length < 2) continue;
            if (!VerifyBcc(line)) continue;

            // payload は "RS4" の後〜BCCの手前
            var payload = line.Substring(7, line.Length - 7 - 2);
            return ParseS4Payload(payload);
        }

        throw new FormatException("S4 response not found / invalid.");
    }

    public static S4Status ParseS4Payload(string payload)
    {
        // 添付S4仕様に準拠（実装は “長さで4桁/5桁Hexを吸収”）
        // payload 例: 運転状態(1) + Lv(2) + 初期風量FLG(1) + 風量(4) + OP(4) + SP(4) + DP(4) + EP(4)
        //           + ブロワ温度(3) + 基板温度(3) + 回転数(hex 4or5) + 実働時間(hex 4or5)
        //           + 管内風速(4) + リモート(1) + UART制御(1) + エラー(hex4)

        int i = 0;
        string Take(int len)
        {
            if (i + len > payload.Length) throw new FormatException("S4 payload too short.");
            var s = payload.Substring(i, len);
            i += len;
            return s;
        }

        bool isRun = Take(1) == "1";
        int level = int.Parse(Take(2));                 // ★10進2桁（"01".."15"）
        bool initFlow = Take(1) == "1";

        double vol = DecodeDec4_LLHH(Take(4)) / 100.0;
        double op = DecodeDec4_LLHH(Take(4)) / 100.0;
        double sp = DecodeDec4_LLHH(Take(4)) / 100.0;
        double dp = DecodeDec4_LLHH(Take(4)) / 100.0;
        double ep = DecodeDec4_LLHH(Take(4)) / 100.0;

        double blowerTemp = int.Parse(Take(3)) / 10.0;
        double boardTemp = int.Parse(Take(3)) / 10.0;

        // ここから先：hexの桁数が機種で違う可能性があるので “残り長” で判定
        // 末尾固定部：風速(4)+リモート(1)+UART(1)+エラー(4)=10
        int remain = payload.Length - i;
        if (remain < 10) throw new FormatException("S4 tail too short.");

        // speed+hours の合計桁数は (remain - 10 - 4?) ではなく
        // 風速(4)があるので：speedDigits + hoursDigits + 4 + 1 + 1 + 4 = remain
        // -> speedDigits + hoursDigits = remain - 10
        int sum = remain - 10;

        int speedDigits, hoursDigits;
        if (sum == 8) { speedDigits = 4; hoursDigits = 4; }
        else if (sum == 10) { speedDigits = 5; hoursDigits = 5; }
        else
        {
            // 万一 5+4 のような混在が来たら安全側で 5/5 を優先
            speedDigits = 5;
            hoursDigits = sum - 5;
            if (hoursDigits != 4 && hoursDigits != 5) throw new FormatException($"Unexpected S4 hex digits sum={sum}");
        }

        int rpm = DecodeHex_LLHH(Take(speedDigits), speedDigits);
        int hours = DecodeHex_LLHH(Take(hoursDigits), hoursDigits);

        double ductVel = DecodeDec4_LLHH(Take(4)) / 100.0;

        bool remote = Take(1) == "1";
        bool uartCtrl = Take(1) == "1";

        int err = DecodeHex_LLHH(Take(4), 4);

        return new S4Status(
            isRun, level, initFlow,
            vol, op, sp, dp, ep,
            blowerTemp, boardTemp,
            rpm, hours, ductVel,
            remote, uartCtrl, err);
    }

}
