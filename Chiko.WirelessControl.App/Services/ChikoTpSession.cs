// Chiko.WirelessControl.App/Services/ChikoTpSession.cs
using System.Globalization;
using System.Diagnostics;
using System.Collections.Generic;

namespace Chiko.WirelessControl.App.Services;

public sealed class ChikoTpSession
{
    private readonly ChikoStreamClient _client;

    public ChikoTpSession(ChikoStreamClient client) => _client = client;

    public sealed record GeneralSettingsSnapshot(
        int VolumeDownRatePercent,
        double InitialAirVolume_m3min,
        double AirVolumeReductionThreshold_m3min,
        bool ShakingAvailable,
        bool PulseAvailable,
        int ShakingTimeIntervalMinutes,
        int ShakingOperatingSeconds,
        int PulseIntervalSeconds,
        bool PulseAutoMode,
        int OperationAnalogSignal
    );

    private static void Log(string msg)
    {
        Debug.WriteLine(msg);
#if ANDROID
        Android.Util.Log.Debug("CHIKO", msg);
#endif
    }

    // ---- フレーム送受（WPF互換：EditSendData→Send→GetCommData）----
    private async Task<string> SendAsync(string body, CancellationToken ct)
    {
        var frame = ChikoCommCodec.EditSendData(body); // %01# + body + BCC
        return await _client.SendCommandAsync(frame, ct);
    }

    // =========================
    // Control flags
    // =========================
    public Task SendControlEnableAsync(CancellationToken ct)
        => SendAsync("W" + Constants.Comm.COMMAND_CONTROL_FLG + "0100", ct);

    public Task SendControlDisableAsync(CancellationToken ct)
        => SendAsync("W" + Constants.Comm.COMMAND_CONTROL_FLG + "0000", ct);

    public Task SendWriteFlagAsync(CancellationToken ct)
        => SendAsync("W" + Constants.Comm.COMMAND_WRITE_FLG + "0100", ct);

    // =========================
    // Model / Serial / Program
    // =========================
    public async Task<(string Model, string Serial, string Program)> ReadModelSerialAsync(CancellationToken ct = default)
    {
        string serialL = await SendAsync(Constants.Comm.METHOD_READ + Constants.Comm.COMMAND_SERIAL_NUMBER_L + "0000", ct);
        string serialH = await SendAsync(Constants.Comm.METHOD_READ + Constants.Comm.COMMAND_SERIAL_NUMBER_H + "0000", ct);

        string serialRaw =
            ChikoCommCodec.GetCommData(serialH, Constants.Comm.COMMAND_SERIAL_NUMBER_H) +
            ChikoCommCodec.GetCommData(serialL, Constants.Comm.COMMAND_SERIAL_NUMBER_L);

        string serialFormatted =
            (serialRaw?.Length >= 6)
                ? $"20{serialRaw.Substring(0, 2)}-{serialRaw.Substring(2, 2)}-{serialRaw.Substring(4)}"
                : "-";

        string modelHH = await SendAsync(Constants.Comm.METHOD_READ + Constants.Comm.COMMAND_MODEL_NAME_HH + "0000", ct);
        string modelHL = await SendAsync(Constants.Comm.METHOD_READ + Constants.Comm.COMMAND_MODEL_NAME_HL + "0000", ct);
        string modelLH = await SendAsync(Constants.Comm.METHOD_READ + Constants.Comm.COMMAND_MODEL_NAME_LH + "0000", ct);
        string modelLL = await SendAsync(Constants.Comm.METHOD_READ + Constants.Comm.COMMAND_MODEL_NAME_LL + "0000", ct);
        string modelLL1 = await SendAsync(Constants.Comm.METHOD_READ + Constants.Comm.COMMAND_MODEL_NAME_LL1 + "0000", ct);
        string modelLL2 = await SendAsync(Constants.Comm.METHOD_READ + Constants.Comm.COMMAND_MODEL_NAME_LL2 + "0000", ct);
        string modelLL3 = await SendAsync(Constants.Comm.METHOD_READ + Constants.Comm.COMMAND_MODEL_NAME_LL3 + "0000", ct);

        string modelName =
            (ChikoCommCodec.GetCommData(modelHH, Constants.Comm.COMMAND_MODEL_NAME_HH) +
             ChikoCommCodec.GetCommData(modelHL, Constants.Comm.COMMAND_MODEL_NAME_HL) +
             ChikoCommCodec.GetCommData(modelLH, Constants.Comm.COMMAND_MODEL_NAME_LH) +
             ChikoCommCodec.GetCommData(modelLL, Constants.Comm.COMMAND_MODEL_NAME_LL) +
             ChikoCommCodec.GetCommData(modelLL1, Constants.Comm.COMMAND_MODEL_NAME_LL1) +
             ChikoCommCodec.GetCommData(modelLL2, Constants.Comm.COMMAND_MODEL_NAME_LL2) +
             ChikoCommCodec.GetCommData(modelLL3, Constants.Comm.COMMAND_MODEL_NAME_LL3))
            .TrimEnd();

        if (string.IsNullOrWhiteSpace(modelName))
            modelName = "-";

        string pgResp = await SendAsync(Constants.Comm.METHOD_READ + Constants.Comm.COMMAND_PG + "0000", ct);
        string pgRaw = ChikoCommCodec.GetCommData(pgResp, Constants.Comm.COMMAND_PG); // "0100"

        string programVersion = "-";
        if (!string.IsNullOrWhiteSpace(pgRaw) && pgRaw.Length == 4 && int.TryParse(pgRaw, out var pgVal))
            programVersion = (pgVal / 100.0).ToString("0.00", CultureInfo.InvariantCulture);

        return (modelName, serialFormatted, programVersion);
    }

    public async Task<(string ProductName, string SerialNumber, string ProgramVersion)> ReadProductInfoAsync(CancellationToken ct)
    {
        var (model, serial, program) = await ReadModelSerialAsync(ct);
        return (model, serial, program);
    }
    // =========================
    // S4 (TP1: data area = 51)
    // =========================
    public sealed record S4Status(
     bool IsRun,
     int Level,
     bool InitialFlowFlag,   // ★追加（d4）
     decimal Volume_m3min,
     decimal Outside_kPa,
     decimal Suction_kPa,
     decimal Diff_kPa,
     decimal Exhaust_kPa,
     decimal BlowerTemp_C,
     int Speed_rpm,
     decimal DuctVelocity_ms,
     ushort ErrorValue,
     string ErrorHex
    );

    // =========================
    // S5
    // =========================
    public sealed record S5Settings(
        bool ShakingEnabled,     // シェイキング有無（搭載/許可）
        bool ShakingConfigured,  // シェイキング設定有無
        bool PulseEnabled,       // パルス有無
        int PipeDiameterMm,      // 配管径(mm) 0 or 20..180（図では 1mm単位）
        string RawFrame          // デバッグ用
    );
    // S5 応答の想定フレーム長（図: 0..49 => 50 bytes）
    private const int S5_FRAME_LEN = 50;

    /// <summary>
    /// S4 一括読み出し (TP1: 51バイト固定)。
    /// 応答文字列中の "S4" を探し、直後 51文字をデータとして取り出して解析する。
    /// </summary>
    public async Task<S4Status> ReadS4Async(CancellationToken ct)
    {
        // 通信：R S4 0000
        var resp = await SendAsync("R" + "S4" + "0000", ct);

        // ★生電文ログ（返答全文）
        Log("[S4][RAW] " + resp);

        int s4Index = resp.IndexOf("S4", StringComparison.Ordinal);
        if (s4Index < 0)
            throw new InvalidOperationException($"S4 応答内にコマンド 'S4' が見つかりません。応答={resp}");

        int dataStart = s4Index + 2; // "S4" の直後
        int remain = resp.Length - dataStart;

        if (remain < 51)
            throw new InvalidOperationException($"S4 data too short. len={remain} line='{resp}'");

        string data = resp.Substring(dataStart, 51);

        // ★データ部51文字ログ（解析対象）
        Log("[S4][DATA51] " + data);

        // ---- d1:運転状態(1) / d2-3:Lv(2) / d4:初期風量フラグ(1) ----
        bool isRun = (data.Length >= 1 && data[0] != '0');

        int level = 0;
        if (data.Length >= 3)
        {
            _ = int.TryParse(data.Substring(1, 2), out level); // "01".."15"
            if (level < 1 || level > 15) level = 0;
        }

        bool initFlowFlag = false;
        if (data.Length >= 4)
        {
            // d4: "0" or "1" 想定（仕様が 0/1 以外なら後で調整）
            initFlowFlag = data[3] != '0';
        }

        // ---- d5-8  風量(0.01, 10進, 4桁LH)
        // ---- d9-12 OP / d13-16 SP / d17-20 DP / d21-24 EP (0.01, 10進, 4桁LH)
        decimal flowTp = Parse0p01From4LH(data.Substring(4, 4));
        decimal op = Parse0p01From4LH(data.Substring(8, 4));
        decimal sp = Parse0p01From4LH(data.Substring(12, 4));
        decimal dp = Parse0p01From4LH(data.Substring(16, 4));
        decimal ep = Parse0p01From4LH(data.Substring(20, 4));

        // ---- d25-27 ブロワ温度(℃, 10進3桁, 仕様図の並び)
        decimal blowerTempC = ParseTempFrom3(data.Substring(24, 3));

        // ---- d31-35 回転数(rpm, 16進5桁, 仕様図の並び)
        int rpm = ParseRpmFrom5Hex(data.Substring(30, 5));

        // ---- d41-44 管内風速(0.01, 10進4桁LH)  ※表示は 99.99 形式
        decimal ductVel = 0m;
        if (data.Length >= 44)
            ductVel = Parse0p01From4LH(data.Substring(40, 4));

        // d48-51 エラーコード(16bit, HEX4桁 / ★2byte ASCIIのため入替が必要)
        string errHex = "0000";
        ushort errVal = 0;

        if (data.Length >= 51)
        {
            errHex = data.Substring(47, 4); // index 47..50

            // ★ここが重要：raw "0008" → parsed 0x0800 のように2byte入替して解釈
            errVal = ParseErrorValueFrom4Hex(errHex);

            Log($"[S4][ERR] raw='{errHex}' parsed=0x{errVal:X4}");
        }

        Log($"[S4][PARSED] run={(isRun ? 1 : 0)} lv={level} Q={flowTp:0.00} OP={op:0.00} SP={sp:0.00} DP={dp:0.00} EP={ep:0.00} T={blowerTempC:0.0} rpm={rpm} V={ductVel:0.00}");

        return new S4Status(
            IsRun: isRun,
            Level: level,
            InitialFlowFlag: initFlowFlag, // ★追加
            Volume_m3min: flowTp,
            Outside_kPa: op,
            Suction_kPa: sp,
            Diff_kPa: dp,
            Exhaust_kPa: ep,
            BlowerTemp_C: blowerTempC,
            Speed_rpm: rpm,
            DuctVelocity_ms: ductVel,
            ErrorValue: errVal,    // ★追加
            ErrorHex: errHex       // ★追加
        );
    }

    private static string Swap2Bytes4(string s4)
    {
        if (string.IsNullOrWhiteSpace(s4) || s4.Length != 4) return s4;
        return s4.Substring(2, 2) + s4.Substring(0, 2); // "7500" -> "0075"
    }

    private static string TrimTailCrLf(string s)
    {
        return s.TrimEnd('\r', '\n');
    }


    // 図の「byte番号」から文字を抜く（%をbyte0として扱う）
    // ※SendAsyncの戻りがCRを含む/含まない差を吸収するため、Trimしてから見る
    public async Task<S5Settings> ReadS5Async(CancellationToken ct)
    {
        var resp = await SendAsync("R" + "S5" + "0000", ct);
        Log("[S5][RAW] " + resp);

        var frame = resp;

        // 複数フレーム混在に備えて先頭%から
        int p = frame.IndexOf('%');
        if (p >= 0) frame = frame.Substring(p);

        frame = TrimTailCrLf(frame);

        // "S5" の位置確認（無いなら異常）
        int s5 = frame.IndexOf("S5", StringComparison.Ordinal);
        if (s5 < 0)
            throw new InvalidOperationException($"S5 応答内に 'S5' が見つかりません。raw={frame}");

        // ---- 配管径：末尾から取る（BCC 2桁の直前4桁）----
        // 末尾構造： ... [Pipe(4)] [BCC(2)]   （CRは上で除去済み）
        // 例: ...750001 -> Pipe="7500" BCC="01"
        if (frame.Length < 6)
            throw new InvalidOperationException($"S5 frame too short. len={frame.Length}, raw={frame}");

        string bcc2 = frame.Substring(frame.Length - 2, 2);
        string pipeRawLh = frame.Substring(frame.Length - 6, 4);   // "7500"
        string pipeHl = Swap2Bytes4(pipeRawLh);                    // "0075"

        int pipeMm = 0;
        if (!int.TryParse(pipeHl, out pipeMm))
            pipeMm = 0;

        Log($"[S5][PIPE] rawLH='{pipeRawLh}' -> HL='{pipeHl}' -> mm={pipeMm} (BCC='{bcc2}')");

        // ---- シェイキング/パルス等 ----
        // 図の byte番号は「%をbyte0」として扱う前提
        // 実フレーム: %01$RS501111000000000001100000040060011311750007
        // シェイキング関連は 24～25 byte 付近、パルスはその次を参照
        const int IDX_SHAKING_ENABLED = 24;
        const int IDX_SHAKING_CONFIGURED = 25;
        const int IDX_PULSE_ENABLED = 26;

        bool shakingEnabled = ParseBool01(frame, IDX_SHAKING_ENABLED);
        bool shakingConfigured = ParseBool01(frame, IDX_SHAKING_CONFIGURED);
        bool pulseEnabled = ParseBool01(frame, IDX_PULSE_ENABLED);

        var c24 = (IDX_SHAKING_ENABLED >= 0 && IDX_SHAKING_ENABLED < frame.Length) ? frame[IDX_SHAKING_ENABLED] : '?';
        var c25 = (IDX_SHAKING_CONFIGURED >= 0 && IDX_SHAKING_CONFIGURED < frame.Length) ? frame[IDX_SHAKING_CONFIGURED] : '?';
        var c26 = (IDX_PULSE_ENABLED >= 0 && IDX_PULSE_ENABLED < frame.Length) ? frame[IDX_PULSE_ENABLED] : '?';
        Log($"[S5][FLAGS] idx24='{c24}' idx25='{c25}' idx26='{c26}'");
        Log($"[S5][FLAGS] shakingEnabled={shakingEnabled} shakingConfigured={shakingConfigured} pulseEnabled={pulseEnabled}");

        return new S5Settings(
            ShakingEnabled: shakingEnabled,
            ShakingConfigured: shakingConfigured,
            PulseEnabled: pulseEnabled,
            PipeDiameterMm: pipeMm,
            RawFrame: frame
        );
    }

    private static bool ParseBool01(string s, int index)
    {
        if (index < 0 || index >= s.Length)
            return false;
        return s[index] == '1';
    }

    public async Task<GeneralSettingsSnapshot> ReadGeneralSettingsAsync(CancellationToken ct)
    {
        var s5 = await ReadS5Async(ct);
        var payload = ExtractS5PayloadForSettings(s5.RawFrame);

        var volumeDownRate = ParseDigitsOrZero(payload, 5, 3);
        var initialAirVolume = ParseDigitsOrZero(payload, 8, 4) / 100.0;
        var reductionThreshold = ParseDigitsOrZero(payload, 12, 4) / 100.0;

        var shakingAvailable = ParseFlag01(payload, 16) || ParseFlag01(payload, 17);
        var pulseAvailable = ParseFlag01(payload, 18);

        var pulseInterval = ParseDigitsOrZero(payload, 19, 4);
        var pulseAuto = ParseFlag01(payload, 23);
        var shakingOperating = ParseDigitsOrZero(payload, 24, 3);
        var shakingInterval = ParseDigitsOrZero(payload, 27, 2);

        var analogRaw = ParseDigitsOrZero(payload, 34, 1);
        var analogUi = analogRaw >= 1 && analogRaw <= 5 ? analogRaw - 1 : 0;

        return new GeneralSettingsSnapshot(
            volumeDownRate,
            initialAirVolume,
            reductionThreshold,
            shakingAvailable,
            pulseAvailable,
            shakingInterval,
            shakingOperating,
            pulseInterval,
            pulseAuto,
            analogUi);
    }

    public async Task WriteVolumeDownRateAsync(int value, CancellationToken ct)
    {
        if (value < 0 || value > 100)
            throw new ArgumentOutOfRangeException(nameof(value), "VolumeDownRate must be in range 0..100.");

        var data = FormatWordCdab(value);
        await SendGeneralAsync(Constants.Comm.METHOD_WRITE + Constants.Comm.COMMAND_FURYO_TEIKA_HANTEI + data, ct);
    }

    public async Task WriteRemoteOutputSignalAsync(int value, CancellationToken ct)
    {
        if (value < 0 || value > 4)
            throw new ArgumentOutOfRangeException(nameof(value), "RemoteOutputSignal must be in range 0..4.");

        var deviceValue = value + 1;
        var data = FormatWordCdab(deviceValue);
        await SendGeneralAsync(Constants.Comm.METHOD_WRITE + Constants.Comm.COMMAND_OPERATION_ANALOG_SIGNAL + data, ct);
    }

    public async Task WriteShakingIntervalAsync(int value, CancellationToken ct)
    {
        if (value < 0 || value > 60 || value % 5 != 0)
            throw new ArgumentOutOfRangeException(nameof(value), "ShakingInterval must be 0..60 and divisible by 5.");

        var data = FormatWordCdab(value);
        await SendGeneralAsync(Constants.Comm.METHOD_WRITE + Constants.Comm.COMMAND_AUTO_SHAKING_TIME + data, ct);
    }

    public async Task WriteShakingOperatingTimeAsync(int value, CancellationToken ct)
    {
        if (value < 20 || value > 180 || value % 5 != 0)
            throw new ArgumentOutOfRangeException(nameof(value), "ShakingOperatingTime must be 20..180 and divisible by 5.");

        var data = FormatWordCdab(value);
        await SendGeneralAsync(Constants.Comm.METHOD_WRITE + Constants.Comm.COMMAND_SHAKING_TIME + data, ct);
    }

    public async Task WritePulseIntervalAsync(int value, CancellationToken ct)
    {
        if (value < 0 || value > 9999)
            throw new ArgumentOutOfRangeException(nameof(value), "PulseInterval must be in range 0..9999.");

        var data = FormatWordCdab(value);
        await SendGeneralAsync(Constants.Comm.METHOD_WRITE + Constants.Comm.COMMAND_PULSE_INTERVAL + data, ct);
    }

    public async Task WriteAutoPulseAsync(bool value, CancellationToken ct)
    {
        var data = FormatWordCdab(value ? 1 : 0);
        await SendGeneralAsync(Constants.Comm.METHOD_WRITE + Constants.Comm.COMMAND_PULSE_AUTO_CONTROL + data, ct);
    }

    public async Task ClearInitialAirVolumeAsync(CancellationToken ct)
    {
        var data = FormatWordCdab(0);
        await SendGeneralAsync(Constants.Comm.METHOD_WRITE + Constants.Comm.COMMAND_SET_INITIAL_FLOW + data, ct);
    }

    public async Task TriggerManualPulseAsync(CancellationToken ct)
    {
        await SendGeneralAsync(Constants.Comm.METHOD_WRITE + Constants.Comm.COMMAND_PULSE_OPERATION + "0000", ct);
    }

    public async Task ResetSettingValueAsync(CancellationToken ct)
    {
        await SendGeneralAsync(Constants.Comm.METHOD_WRITE + Constants.Comm.COMMAND_SET_RESET + "0000", ct);
    }

    private async Task<string> SendGeneralAsync(string body, CancellationToken ct)
    {
        Log($"[GENERAL][TX-BODY] {body}");
        var frame = ChikoCommCodec.EditSendData(body);
        Log($"[GENERAL][TX-FRAME] {frame}");
        var response = await _client.SendCommandAsync(frame, ct);
        Log($"[GENERAL][RX] {response}");
        return response;
    }

    private static string FormatWordCdab(int value)
    {
        if (value < 0 || value > 9999)
            throw new ArgumentOutOfRangeException(nameof(value), "Value must be in range 0..9999.");

        return ToLH4(value.ToString("D4", CultureInfo.InvariantCulture));
    }

    private static string ExtractS5PayloadForSettings(string rawFrame)
    {
        if (string.IsNullOrWhiteSpace(rawFrame))
            return string.Empty;

        var frame = TrimTailCrLf(rawFrame);
        var s5Index = frame.IndexOf("S5", StringComparison.Ordinal);
        if (s5Index < 0 || s5Index + 2 >= frame.Length)
            return frame;

        var payload = frame.Substring(s5Index + 2);
        if (payload.Length >= 2)
        {
            var tail2 = payload.Substring(payload.Length - 2, 2);
            if (IsHex2(tail2))
                payload = payload.Substring(0, payload.Length - 2);
        }

        return payload;
    }

    private static int ParseDigitsOrZero(string s, int start, int length)
    {
        if (string.IsNullOrEmpty(s) || start < 0 || length <= 0 || start + length > s.Length)
            return 0;

        var slice = s.Substring(start, length);
        for (var i = 0; i < slice.Length; i++)
        {
            if (!char.IsDigit(slice[i]))
                return 0;
        }

        return int.TryParse(slice, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private static bool ParseFlag01(string s, int index)
    {
        if (string.IsNullOrEmpty(s) || index < 0 || index >= s.Length)
            return false;

        return s[index] == '1';
    }

    private static bool IsHex2(string s)
    {
        if (s.Length != 2)
            return false;

        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            var isHex = (c >= '0' && c <= '9') ||
                        (c >= 'A' && c <= 'F') ||
                        (c >= 'a' && c <= 'f');
            if (!isHex)
                return false;
        }

        return true;
    }

    public sealed record LogHistoryItem(
        DateTime Timestamp,
        int TriggerType,
        int Level,
        double AirVolume,
        double OutsidePressure,
        double SuctionPressure,
        double? DifferentialPressure,
        double ExhaustPressure,
        double BlowerMotorTemperature,
        double BoardTemperature,
        int Rpm,
        int OperationHours,
        string ErrorCodeRaw,
        string ErrorSummary,
        string RunState,
        string RawFrame
    );

    private const int OperationHistoryRequestCount = 1160;

    public async Task<IReadOnlyList<LogHistoryItem>> ReadLogHistoryAsync(CancellationToken ct, IProgress<int>? progress = null)
    {
        var items = new List<LogHistoryItem>(256);
        var first = true;

        progress?.Report(0);

        while (items.Count < OperationHistoryRequestCount)
        {
            var data = first ? "6011" : "FFFF";
            var body = Constants.Comm.METHOD_READ + Constants.Comm.COMMAND_LOGGING_DATA + data;
            var response = await SendLogAsync(body, ct);

            if (TryGetProtocolErrorCode(response, out var errorCode))
            {
                if (errorCode == "E6")
                {
                    if (first)
                    {
                        Log("[LOG][END] first response was E6, no history data");
                    }
                    else
                    {
                        Log($"[LOG][END] E6 received after {items.Count} items");
                    }

                    break;
                }

                throw new InvalidOperationException($"S2 read failed with error code {errorCode}.");
            }

            var item = ParseS2LogFrame(response);
            items.Add(item);
            progress?.Report(items.Count);
            Log($"[LOG][COUNT] read item count = {items.Count}");
            first = false;
        }

        return items;
    }

    private async Task<string> SendLogAsync(string body, CancellationToken ct)
    {
        Log($"[LOG][TX-BODY] {body}");
        var frame = ChikoCommCodec.EditSendData(body);
        Log($"[LOG][TX-FRAME] {frame}");
        var response = await _client.SendCommandAsync(frame, ct);
        Log($"[LOG][RX] {response}");
        return response;
    }

    private static bool TryGetProtocolErrorCode(string response, out string errorCode)
    {
        errorCode = string.Empty;
        if (string.IsNullOrWhiteSpace(response))
            return false;

        var line = TrimTailCrLf(response);
        var start = line.IndexOf('%');
        if (start >= 0)
            line = line.Substring(start);

        if (line.Length < 6 || line[0] != '%' || line[3] != '!')
            return false;

        for (var i = 0; i < line.Length - 1; i++)
        {
            if (line[i] == 'E' && line[i + 1] >= '1' && line[i + 1] <= '7')
            {
                errorCode = line.Substring(i, 2);
                return true;
            }
        }

        errorCode = "E2";
        return true;
    }

    private sealed record S2Values(
        int Level,
        double AirVolume,
        double OutsidePressure,
        double SuctionPressure,
        double? DifferentialPressure,
        double ExhaustPressure,
        double BlowerMotorTemperature,
        double BoardTemperature,
        int Rpm,
        int OperationHours,
        string ErrorCodeRaw,
        string ErrorSummary,
        string RunState
    );

    private LogHistoryItem ParseS2LogFrame(string response)
    {
        var line = TrimTailCrLf(response);
        var start = line.IndexOf('%');
        if (start >= 0)
            line = line.Substring(start);

        var s2Index = line.IndexOf("RS2", StringComparison.Ordinal);
        if (s2Index < 0)
            throw new InvalidOperationException($"S2 response payload not found. response={response}");

        var payload = line.Substring(s2Index + 3);
        if (payload.Length >= 2 && IsHex2(payload.Substring(payload.Length - 2, 2)))
            payload = payload.Substring(0, payload.Length - 2);

        var idx = 0;
        var trigger = ParseDecDigits(TakePayload(payload, ref idx, 1));
        var month = ParseDecDigits(TakePayload(payload, ref idx, 2));
        var year2 = ParseDecDigits(TakePayload(payload, ref idx, 2));
        var hour = ParseDecDigits(TakePayload(payload, ref idx, 2));
        var day = ParseDecDigits(TakePayload(payload, ref idx, 2));
        var minute = ParseDecDigits(TakePayload(payload, ref idx, 2));

        var year = 2000 + year2;
        var timestamp = SafeDateTime(year, month, day, hour, minute);

        var values = trigger switch
        {
            1 => ParseS2TypeRun(payload, ref idx),
            2 => ParseS2TypeHourly(payload, ref idx),
            3 => ParseS2TypeError(payload, ref idx),
            4 => ParseS2TypeStop(payload, ref idx),
            _ => throw new FormatException($"Unsupported S2 trigger type: {trigger}")
        };

        Log($"[LOG][PARSE] trigger={trigger}, timestamp={timestamp:yyyy-MM-dd HH:mm}");

        return new LogHistoryItem(
            timestamp,
            trigger,
            values.Level,
            values.AirVolume,
            values.OutsidePressure,
            values.SuctionPressure,
            values.DifferentialPressure,
            values.ExhaustPressure,
            values.BlowerMotorTemperature,
            values.BoardTemperature,
            values.Rpm,
            values.OperationHours,
            values.ErrorCodeRaw,
            values.ErrorSummary,
            values.RunState,
            line);
    }

    private static S2Values ParseS2TypeRun(string payload, ref int idx)
        => ParseS2TypeCommon(payload, ref idx, "RUN");

    private static S2Values ParseS2TypeHourly(string payload, ref int idx)
        => ParseS2TypeCommon(payload, ref idx, "-");

    private static S2Values ParseS2TypeError(string payload, ref int idx)
        => ParseS2TypeCommon(payload, ref idx, "-");

    private static S2Values ParseS2TypeStop(string payload, ref int idx)
        => ParseS2TypeCommon(payload, ref idx, "STOP");

    private static S2Values ParseS2TypeCommon(string payload, ref int idx, string runState)
    {
        var level = ParseDecDigits(TakePayload(payload, ref idx, 2));
        var airVolume = ParseDec4Cdab(TakePayload(payload, ref idx, 4)) / 100.0;
        var op = ParseDec4Cdab(TakePayload(payload, ref idx, 4)) / 100.0;
        var sp = ParseDec4Cdab(TakePayload(payload, ref idx, 4)) / 100.0;

        double? dp = null;
        var remaining = payload.Length - idx;
        if (remaining >= 28)
            dp = ParseDec4Cdab(TakePayload(payload, ref idx, 4)) / 100.0;

        var ep = ParseDec4Cdab(TakePayload(payload, ref idx, 4)) / 100.0;
        var blower = ParseTemp3(TakePayload(payload, ref idx, 3));
        var board = ParseTemp3(TakePayload(payload, ref idx, 3));
        var rpm = ParseHex5Special(TakePayload(payload, ref idx, 5));
        var hours = ParseHex5Special(TakePayload(payload, ref idx, 5));
        var errRaw = ParseHex4Cdab(TakePayload(payload, ref idx, 4));
        var errSummary = BuildErrorSummary(errRaw);

        return new S2Values(
            level,
            airVolume,
            op,
            sp,
            dp,
            ep,
            blower,
            board,
            rpm,
            hours,
            errRaw,
            errSummary,
            runState);
    }

    private static string TakePayload(string payload, ref int idx, int len)
    {
        if (idx + len > payload.Length)
            throw new FormatException("S2 payload is too short.");

        var value = payload.Substring(idx, len);
        idx += len;
        return value;
    }

    private static DateTime SafeDateTime(int year, int month, int day, int hour, int minute)
    {
        var y = Math.Clamp(year, 2000, 2099);
        var m = Math.Clamp(month, 1, 12);
        var maxDay = DateTime.DaysInMonth(y, m);
        var d = Math.Clamp(day, 1, maxDay);
        var h = Math.Clamp(hour, 0, 23);
        var min = Math.Clamp(minute, 0, 59);
        return new DateTime(y, m, d, h, min, 0);
    }

    private static int ParseDecDigits(string s)
    {
        if (string.IsNullOrEmpty(s))
            throw new FormatException("Expected decimal digits.");

        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] < '0' || s[i] > '9')
                throw new FormatException("Invalid decimal payload.");
        }

        return int.Parse(s, NumberStyles.None, CultureInfo.InvariantCulture);
    }

    private static int ParseDec4Cdab(string s)
    {
        if (s.Length != 4)
            throw new FormatException("Expected 4-digit decimal.");

        var restored = s.Substring(2, 2) + s.Substring(0, 2);
        if (!int.TryParse(restored, NumberStyles.None, CultureInfo.InvariantCulture, out var v))
            throw new FormatException("Invalid 4-digit decimal payload.");

        return v;
    }

    private static double ParseTemp3(string s)
    {
        if (s.Length != 3)
            throw new FormatException("Expected 3-digit temperature.");

        var restored = s[2].ToString() + s[0] + s[1];
        if (!int.TryParse(restored, NumberStyles.None, CultureInfo.InvariantCulture, out var v))
            throw new FormatException("Invalid temperature payload.");

        return v / 10.0;
    }

    private static int ParseHex5Special(string s)
    {
        if (s.Length != 5)
            throw new FormatException("Expected 5-digit hex.");

        var restored = s[4].ToString() + s[2] + s[3] + s[0] + s[1];
        return Convert.ToInt32(restored, 16);
    }

    private static string ParseHex4Cdab(string s)
    {
        if (s.Length != 4)
            throw new FormatException("Expected 4-digit hex.");

        var restored = s.Substring(2, 2) + s.Substring(0, 2);
        for (var i = 0; i < restored.Length; i++)
        {
            var c = restored[i];
            var isHex = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');
            if (!isHex)
                throw new FormatException("Invalid hex payload.");
        }

        return restored.ToUpperInvariant();
    }

    private static string BuildErrorSummary(string errorCodeRaw)
    {
        if (!int.TryParse(errorCodeRaw, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            return string.Empty;

        var messages = new List<string>();
        void AddIfSpecBit(int bitNumber, string text)
        {
            var bitIndex = bitNumber - 1;
            if (bitIndex < 0 || bitIndex > 15)
                return;

            if (((value >> bitIndex) & 1) == 1)
                messages.Add(text);
        }

        AddIfSpecBit(1, "INV error detected");
        AddIfSpecBit(2, "RPM fault");
        AddIfSpecBit(3, "Internal temperature fault");
        AddIfSpecBit(5, "Pressure fault");
        AddIfSpecBit(7, "Run button ground fault");
        AddIfSpecBit(8, "Internal temperature rise warning");
        AddIfSpecBit(10, "Suction pressure low warning");
        AddIfSpecBit(11, "Air volume drop warning");
        AddIfSpecBit(12, "Exhaust pressure abnormal warning");
        AddIfSpecBit(15, "Remote lock warning");

        return messages.Count == 0 ? string.Empty : string.Join(", ", messages);
    }
    // =========================
    // Initial Flow (03)
    // =========================

    /// <summary>
    /// 初期風量登録（W03）。
    /// 仕様：フラグ書込は 0001 をLH化して "0100" を送る運用（W10/W81/W80 と同じ）。
    /// </summary>
    public Task SetInitialFlowAsync(CancellationToken ct)
        => SendAsync(Constants.Comm.METHOD_WRITE + Constants.Comm.COMMAND_SET_INITIAL_FLOW + "0100", ct);

    // =========================
    // Shaking
    // =========================

    /// <summary>
    /// 手動シェーキング実行。
    /// COMMAND_SHAKING を使用し、データ部はフラグONとして "0100" を送る。
    /// （仕様上は 0000 / 0100 どちらでも可だが、既存のフラグ系コマンドに合わせて 0100 を採用）
    /// </summary>
    public Task SetShakingAsync(CancellationToken ct)
        => SendAsync(Constants.Comm.METHOD_WRITE + Constants.Comm.COMMAND_SHAKING + "0100", ct);

    // =========================
    // RUN / STOP / LEVEL / ERR CLR
    // =========================

    public Task SetRunAsync(bool run, CancellationToken ct)
    {
        var data = run ? ToLH4("0001") : "0000";
        return SendAsync("W" + "01" + data, ct);
    }

    public Task SetLevelAsync(int level, CancellationToken ct)
    {
        if (level < 1 || level > 15)
            throw new ArgumentOutOfRangeException(nameof(level), "Levelは 1..15 です。");

        // ★10進4桁(HL) → LH化（HEXは禁止）
        string hl = level.ToString("D4");              // 0010
        string lh = hl.Substring(2, 2) + hl.Substring(0, 2); // 1000

        return SendAsync("W" + "02" + lh, ct);
    }

    public Task ClearErrorAsync(CancellationToken ct)
        => SendAsync("W" + "80" + "0100", ct);

    // =========================
    // Pipe Diameter (0F)
    // =========================

    /// <summary>
    /// 返答のデータ4桁は経路/実装により HL または LH のことがあるため、
    /// “そのまま/入替” 両方を試して妥当値(0 or 20..180)を採用する。
    /// </summary>
    public async Task<int?> ReadPipeDiameterAsync(CancellationToken ct)
    {
        // 通信：R 0F 0000
        var resp = await SendAsync(Constants.Comm.METHOD_READ + Constants.Comm.COMMAND_HAIKAN + "0000", ct);

        // 返答から data4 を抜く（4桁）
        string raw = ChikoCommCodec.GetCommData(resp, Constants.Comm.COMMAND_HAIKAN);

        if (string.IsNullOrWhiteSpace(raw) || raw.Length < 4)
            return null;

        raw = raw.Substring(0, 4);

        // ★両解釈（そのまま / 2byte入替）で “妥当値” を採用する
        if (TryParsePipeMmSmart(raw, out var mm, out var chosen))
        {
            Log($"[0F][PIPE] raw='{raw}' -> mm={mm} ({chosen})");
            return mm;
        }

        Log($"[0F][PIPE] raw='{raw}' -> invalid");
        return null;
    }

    private static bool TryParsePipeMmSmart(string raw4, out int mm, out string chosen)
    {
        mm = 0;
        chosen = "";

        if (string.IsNullOrWhiteSpace(raw4) || raw4.Length < 4)
            return false;

        raw4 = raw4.Substring(0, 4);

        // 候補A：そのまま（= HLとして解釈）
        if (int.TryParse(raw4, out var a) && IsValidPipeMm(a))
        {
            mm = a;
            chosen = "as-is(HL)";
            return true;
        }

        // 候補B：2byte入替（= LH->HL）
        var swapped = Swap2Bytes4(raw4); // "7500" -> "0075"
        if (int.TryParse(swapped, out var b) && IsValidPipeMm(b))
        {
            mm = b;
            chosen = "swap(LH->HL)";
            return true;
        }

        return false;
    }

    private static bool IsValidPipeMm(int v)
        => v == 0 || (v >= 20 && v <= 180);


    /// <summary>
    /// 配管径(mm)を設定する（W0F）。
    /// 仕様：0 または 20..180
    /// 送信データは mm を10進4桁(ASCII)にし、LH順にして送る。
    /// 例: 65 -> "0065" -> "6500"
    ///     100 -> "0100" -> "0001"
    /// </summary>
    public Task SetPipeDiameterAsync(int mm, CancellationToken ct)
    {
        if (!(mm == 0 || (mm >= 20 && mm <= 180)))
            throw new ArgumentOutOfRangeException(nameof(mm), "配管径(mm)は 0 または 20..180 の範囲です。");

        // mm -> DEC4 (HL) -> LH
        string hl = mm.ToString("D4"); // 例: 100 -> "0100"
        string lh = ToLH4(hl);         // 例: "0100" -> "0001"

        // 通信：W 0F {LH4}
        return SendAsync(Constants.Comm.METHOD_WRITE + Constants.Comm.COMMAND_HAIKAN + lh, ct);
    }



    // =========================
    // Helpers（仕様図準拠）
    // =========================

    private static string ToLH4(string raw4)
    {
        if (string.IsNullOrWhiteSpace(raw4) || raw4.Length != 4) return raw4;
        return raw4.Substring(2, 2) + raw4.Substring(0, 2);
    }

    /// <summary>
    /// 4桁の LH(下位2 + 上位2) を HL に戻して /100。
    /// 例: "3000" → "0030" → 0.30
    /// </summary>
    private static decimal Parse0p01From4LH(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length < 4)
            return 0m;

        string reordered = raw.Substring(2, 2) + raw.Substring(0, 2);

        if (!int.TryParse(reordered, out var v))
            return 0m;

        return v / 100m;
    }

    /// <summary>
    /// 5桁の並び（仕様図：2桁目/1桁目/4桁目/3桁目/5桁目）を正順に戻して /100。
    /// 例: raw = [2][1][4][3][5] → normal = [1][2][3][4][5]
    /// </summary>
    private static decimal Parse0p01From5LH(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length < 5)
            return 0m;

        // normal = raw[1] raw[0] raw[3] raw[2] raw[4]
        string reordered =
            raw.Substring(1, 1) +
            raw.Substring(0, 1) +
            raw.Substring(3, 1) +
            raw.Substring(2, 1) +
            raw.Substring(4, 1);

        if (!int.TryParse(reordered, out var v))
            return 0m;

        return v / 100m;
    }

    /// <summary>
    /// 3桁の温度(10進)を並び替えて /10 して温度[℃]に変換。
    /// 例: "123" → "312" → 31.2℃
    ///?     "284" → "428" → 42.8℃
    /// </summary>
    private static decimal ParseTempFrom3(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length < 3)
            return 0m;

        // ★仕様：raw[2] raw[0] raw[1]
        string reordered =
            raw.Substring(2, 1) +
            raw.Substring(0, 1) +
            raw.Substring(1, 1);

        if (!int.TryParse(reordered, out var v))
            return 0m;

        return v / 10m;
    }


    /// <summary>
    /// 回転数16進5桁：返答文の5桁を並び替えて「0x?????」として解釈し、10進rpmに変換する。
    /// 例) raw="64190" → reordered="01964" → 0x01964 → 6500
    /// </summary>
    private static int ParseRpmFrom5Hex(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length < 5)
            return 0;

        raw = raw.Substring(0, 5);

        // ★ 仕様どおり：raw[4], raw[2], raw[3], raw[0], raw[1]
        string reordered =
            raw[4].ToString() +
            raw[2].ToString() +
            raw[3].ToString() +
            raw[0].ToString() +
            raw[1].ToString();

        try
        {
            return Convert.ToInt32(reordered, 16);
        }
        catch
        {
            return 0;
        }
    }

    private static ushort ParseErrorValueFrom4Hex(string raw4)
    {
        if (string.IsNullOrWhiteSpace(raw4) || raw4.Length < 4)
            return 0;

        // 仕様：16bitを little-endian（下位バイト→上位バイト）でASCII4桁化して送ってくる
        // raw: [lowByte(2桁)][highByte(2桁)] 例 "0008" → "0800"
        string reordered = raw4.Substring(2, 2) + raw4.Substring(0, 2);

        if (ushort.TryParse(reordered, System.Globalization.NumberStyles.HexNumber,
                            System.Globalization.CultureInfo.InvariantCulture, out var v))
            return v;

        return 0;
    }


}
