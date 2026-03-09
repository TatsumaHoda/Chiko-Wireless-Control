namespace Chiko.WirelessControl.App.Views.Controls;

public sealed class RealtimeGraphSample
{
    public double Volume { get; set; } // 風量（×1）
    public double OP { get; set; }     // Outside Pressure（×1）
    public double SP { get; set; }     // Suction Pressure（×1）
    public double DP { get; set; }     // Differential Pressure（×1）
    public double EP { get; set; }     // Exhaust Pressure（×1）
    public double Temp { get; set; }   // 温度（÷10した値）
    public double Rpm { get; set; }    // 回転数（÷1000した値）
}
