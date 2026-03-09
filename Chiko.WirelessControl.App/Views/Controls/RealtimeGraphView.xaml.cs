using Microsoft.Maui.Dispatching;

namespace Chiko.WirelessControl.App.Views.Controls;

public partial class RealtimeGraphView : ContentView
{
    private readonly RealtimeGraphDrawable _drawable;
    private readonly IDispatcherTimer _renderTimer;

    public RealtimeGraphView()
    {
        InitializeComponent();

        _drawable = new RealtimeGraphDrawable(windowSeconds: 60);
        GraphCanvas.Drawable = _drawable;

        // 描画は間引き（例: 10fps）
        _renderTimer = Dispatcher.CreateTimer();
        _renderTimer.Interval = TimeSpan.FromMilliseconds(100);
        _renderTimer.Tick += (_, __) => GraphCanvas.Invalidate();
        _renderTimer.Start();
    }

    // 枠だけ表示（必要なら）
    public bool FrameOnly
    {
        get => _drawable.FrameOnly;
        set => _drawable.FrameOnly = value;
    }

    /// <summary>
    /// 受信したリアルタイム値を投入する（呼び出しスレッドは問わない）。
    /// スケール変換：
    ///  - 風量/圧力: ×1
    ///  - 温度: ÷10
    ///  - 回転数: ÷1000
    /// </summary>
    public void AddSample(
        DateTime timestamp,
        double volume,
        double outsidePressure,
        double suctionPressure,
        double differentialPressure,
        double exhaustPressure,
        double tempC,
        double rpm)
    {
        _drawable.AddSample(timestamp, new RealtimeGraphSample
        {
            Volume = volume * 1.0,
            OP = outsidePressure * 1.0,
            SP = suctionPressure * 1.0,
            DP = differentialPressure * 1.0,
            EP = exhaustPressure * 1.0,
            Temp = tempC * 0.1,   // 1/10
            Rpm = rpm * 0.001     // 1/1000
        });
    }
}
