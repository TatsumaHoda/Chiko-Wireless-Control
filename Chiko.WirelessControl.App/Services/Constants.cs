// Chiko.WirelessControl.App/Services/Constants.cs
namespace Chiko.WirelessControl.App.Services
{
    /// <summary>
    /// TP 通信のコマンド定義（WPF版をそのまま移植）
    /// </summary>
    public static class Constants
    {
        /// <summary>
        /// 電文
        /// </summary>
        public static class Comm
        {
            /// <summary>局番</summary>
            public const string COMM_NUMBER = "01";
            /// <summary>Writeコマンド</summary>
            public const string METHOD_WRITE = "W";
            /// <summary>Readコマンド</summary>
            public const string METHOD_READ = "R";

            /// <summary>運転状態</summary>
            public const string COMMAND_UNTEN_JOTAI = "01";
            /// <summary>風量レベル</summary>
            public const string COMMAND_FURYO_LEVEL = "02";
            /// <summary>初期風量登録</summary>
            public const string COMMAND_SET_INITIAL_FLOW = "03";
            /// <summary>排気圧力</summary>
            public const string COMMAND_HAIKI = "09";
            /// <summary>吸込圧力</summary>
            public const string COMMAND_SUIKOMI = "0A";
            /// <summary>外部圧力</summary>
            public const string COMMAND_GAIBU = "0B";
            /// <summary>差圧</summary>
            public const string COMMAND_SAATSU = "0C";
            /// <summary>ブロア周辺温度</summary>
            public const string COMMAND_BLOWER_TEMP = "0D";
            /// <summary>基板周辺温度</summary>
            public const string COMMAND_KIBAN_TEMP = "0E";
            /// <summary>制御有効化フラグ</summary>
            public const string COMMAND_CONTROL_FLG = "10";
            /// <summary>風量低下判定割合: 0～100</summary>
            public const string COMMAND_FURYO_TEIKA_HANTEI = "2B";
            /// <summary>初期風量</summary>
            public const string COMMAND_SHOKI_FURYO = "2C";
            /// <summary>風量低下判定閾値</summary>
            public const string COMMAND_TEIKA_HANTEI_ISHI = "2D";

            /// <summary>機種名LL3</summary>
            public const string COMMAND_MODEL_NAME_LL3 = "61";
            /// <summary>機種名LL2</summary>
            public const string COMMAND_MODEL_NAME_LL2 = "62";
            /// <summary>機種名LL1</summary>
            public const string COMMAND_MODEL_NAME_LL1 = "63";

            /// <summary>モータ回転数データL</summary>
            public const string COMMAND_MOTOR_L = "6F";
            /// <summary>モータ回転数データH</summary>
            public const string COMMAND_MOTOR_H = "70";
            /// <summary>エラー情報</summary>
            public const string COMMAND_ERROR_INFO = "7F";
            /// <summary>プログラムバージョン</summary>
            public const string COMMAND_PG = "83";
            /// <summary>製造番号データL</summary>
            public const string COMMAND_SERIAL_NUMBER_L = "84";
            /// <summary>製造番号データH</summary>
            public const string COMMAND_SERIAL_NUMBER_H = "85";
            /// <summary>機種名LL</summary>
            public const string COMMAND_MODEL_NAME_LL = "87";
            /// <summary>機種名LH</summary>
            public const string COMMAND_MODEL_NAME_LH = "88";
            /// <summary>機種名HL</summary>
            public const string COMMAND_MODEL_NAME_HL = "89";
            /// <summary>機種名HH</summary>
            public const string COMMAND_MODEL_NAME_HH = "8A";
            /// <summary>ロギングデータ</summary>
            public const string COMMAND_LOGGING_DATA = "S2";
            /// <summary>書き込みフラグ</summary>
            public const string COMMAND_WRITE_FLG = "81";
            /// <summary>エラークリア</summary>
            public const string COMMAND_ERROR_CLEAR = "80";
            /// <summary>手動シェーキング</summary>
            public const string COMMAND_SHAKING = "34";
            /// <summary>Lv1PWM</summary>
            public const string COMMAND_PWM_LV1 = "3E";
            /// <summary>Lv2PWM</summary>
            public const string COMMAND_PWM_LV2 = "3F";
            /// <summary>Lv3PWM</summary>
            public const string COMMAND_PWM_LV3 = "40";
            /// <summary>Lv4PWM</summary>
            public const string COMMAND_PWM_LV4 = "41";
            /// <summary>Lv5PWM</summary>
            public const string COMMAND_PWM_LV5 = "42";
            /// <summary>Lv6PWM</summary>
            public const string COMMAND_PWM_LV6 = "43";
            /// <summary>Lv7PWM</summary>
            public const string COMMAND_PWM_LV7 = "44";
            /// <summary>Lv8PWM</summary>
            public const string COMMAND_PWM_LV8 = "45";
            /// <summary>Lv9PWM</summary>
            public const string COMMAND_PWM_LV9 = "46";
            /// <summary>Lv10PWM</summary>
            public const string COMMAND_PWM_LV10 = "47";
            /// <summary>Lv11PWM</summary>
            public const string COMMAND_PWM_LV11 = "48";
            /// <summary>Lv12PWM</summary>
            public const string COMMAND_PWM_LV12 = "49";
            /// <summary>Lv13PWM</summary>
            public const string COMMAND_PWM_LV13 = "4A";
            /// <summary>Lv14PWM</summary>
            public const string COMMAND_PWM_LV14 = "4B";
            /// <summary>Lv15PWM</summary>
            public const string COMMAND_PWM_LV15 = "4C";
            /// <summary>状態一括読み出し</summary>
            public const string COMMAND_IKKATSUYOMODASHI = "S4";
            /// <summary>パルストリガー</summary>
            public const string COMMAND_PULS = "45";
            /// <summary>プログラム切り替え</summary>
            public const string COMMAND_PG_CHANGE = "46";
            /// <summary>バルブ開度</summary>
            public const string COMANND_VALVE_LEVEL = "45";
            ///<summary>配管直径</summary>
            public const string COMMAND_HAIKAN = "0F";
            /// <summary>設定一括読み出し</summary>
            public const string COMMAND_SETTINYOMIDASHID = "S5";
        }

        /// <summary>
        /// 制御コード
        /// </summary>
        public static class ControlCode
        {
            /// <summary>ヘッダ</summary>
            public const string HEADER = "%";
            /// <summary>コマンド(命令)</summary>
            public const string COMMAND = "#";
            /// <summary>レスポンス(正常)</summary>
            public const string RESPONSE_OK = "$";
            /// <summary>レスポンス(異常)</summary>
            public const string RESPONSE_NG = "!";
            /// <summary>ターミネータ</summary>
            public const char TERMINATOR = '\r';
        }
    }
}
