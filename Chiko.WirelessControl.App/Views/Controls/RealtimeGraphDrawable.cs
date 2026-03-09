using Microsoft.Maui.Graphics;
using System.Collections.Generic;

namespace Chiko.WirelessControl.App.Views.Controls;

public sealed class RealtimeGraphDrawable : IDrawable
{
    private readonly object _gate = new();
    private readonly int _windowSeconds;
    private readonly Queue<(DateTime t, RealtimeGraphSample v)> _samples = new();

    public RealtimeGraphDrawable(int windowSeconds)
    {
        _windowSeconds = windowSeconds;
    }

    public bool FrameOnly { get; set; }

    public void AddSample(DateTime t, RealtimeGraphSample v)
    {
        lock (_gate)
        {
            _samples.Enqueue((t, v));
            TrimOldLocked(t);
        }
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        const float leftPad = 52;
        const float rightPad = 10;
        const float topPad = 10;
        const float bottomPad = 24;

        var plot = new RectF(
            dirtyRect.Left + leftPad,
            dirtyRect.Top + topPad,
            dirtyRect.Width - leftPad - rightPad,
            dirtyRect.Height - topPad - bottomPad);

        if (plot.Width <= 10 || plot.Height <= 10)
            return;

        // 内枠（枠だけは常時描く）
        canvas.StrokeColor = Colors.White.WithAlpha(0.35f);
        canvas.StrokeSize = 1;
        canvas.DrawRectangle(plot);

        // スナップショット
        List<(DateTime t, RealtimeGraphSample v)> snapshot;
        DateTime now;

        lock (_gate)
        {
            now = _samples.Count > 0 ? GetLastTimeLocked() : DateTime.UtcNow;
            TrimOldLocked(now);
            snapshot = new List<(DateTime, RealtimeGraphSample)>(_samples);
        }

        // 縦軸MAX：表示窓内の最大値（風量/圧力が主、温度/rpmも同一軸表示なら含める）
        double yMax = 0;
        foreach (var (_, v) in snapshot)
        {
            yMax = Math.Max(yMax, v.Volume);
            yMax = Math.Max(yMax, v.OP);
            yMax = Math.Max(yMax, v.SP);
            yMax = Math.Max(yMax, v.DP);
            yMax = Math.Max(yMax, v.EP);
            yMax = Math.Max(yMax, v.Temp);
            yMax = Math.Max(yMax, v.Rpm);
        }
        if (yMax <= 0) yMax = 1.0;
        yMax *= 1.10;

        // グリッド
        canvas.StrokeColor = Colors.White.WithAlpha(0.12f);
        canvas.StrokeSize = 1;

        const int yDiv = 6;
        for (int i = 1; i < yDiv; i++)
        {
            float y = plot.Top + plot.Height * i / yDiv;
            canvas.DrawLine(plot.Left, y, plot.Right, y);
        }

        const int xDiv = 6;
        for (int i = 1; i < xDiv; i++)
        {
            float x = plot.Left + plot.Width * i / xDiv;
            canvas.DrawLine(x, plot.Top, x, plot.Bottom);
        }

        // 軸ラベル（最小限）
        canvas.FontColor = Colors.White.WithAlpha(0.55f);
        canvas.FontSize = 11;

        canvas.DrawString($"{yMax:0.##}",
            dirtyRect.Left + 2, plot.Top - 6, leftPad - 6, 16,
            HorizontalAlignment.Left, VerticalAlignment.Center);

        canvas.DrawString($"{_windowSeconds}s",
            plot.Left, plot.Bottom + 2, 80, 16,
            HorizontalAlignment.Left, VerticalAlignment.Top);

        canvas.DrawString("0s",
            plot.Right - 30, plot.Bottom + 2, 30, 16,
            HorizontalAlignment.Right, VerticalAlignment.Top);

        // 枠だけモード
        if (FrameOnly) return;
        if (snapshot.Count < 2) return;

        // TaskMgr：右が0秒（now）、左が60秒
        float MapX(DateTime t)
        {
            var dt = (now - t).TotalSeconds;
            if (dt < 0) dt = 0;
            if (dt > _windowSeconds) dt = _windowSeconds;
            return plot.Right - (float)(dt / _windowSeconds) * plot.Width;
        }

        float MapY(double value)
        {
            var r = value / yMax;
            if (r < 0) r = 0;
            if (r > 1) r = 1;
            return plot.Bottom - (float)r * plot.Height;
        }

        // 系列（必要なものだけ残してください）
        DrawSeries(canvas, snapshot, s => s.Volume, MapX, MapY, Colors.Cyan.WithAlpha(0.95f));
        DrawSeries(canvas, snapshot, s => s.OP, MapX, MapY, Colors.Orange.WithAlpha(0.80f));
        DrawSeries(canvas, snapshot, s => s.SP, MapX, MapY, Colors.Yellow.WithAlpha(0.80f));
        DrawSeries(canvas, snapshot, s => s.DP, MapX, MapY, Colors.Lime.WithAlpha(0.80f));
        DrawSeries(canvas, snapshot, s => s.EP, MapX, MapY, Colors.Magenta.WithAlpha(0.80f));
        DrawSeries(canvas, snapshot, s => s.Temp, MapX, MapY, Colors.White.WithAlpha(0.55f));
        DrawSeries(canvas, snapshot, s => s.Rpm, MapX, MapY, Colors.White.WithAlpha(0.35f));
    }

    private static void DrawSeries(
        ICanvas canvas,
        List<(DateTime t, RealtimeGraphSample v)> pts,
        Func<RealtimeGraphSample, double> selector,
        Func<DateTime, float> mapX,
        Func<double, float> mapY,
        Color stroke)
    {
        canvas.StrokeColor = stroke;
        canvas.StrokeSize = 2;

        bool started = false;
        float prevX = 0, prevY = 0;

        foreach (var (t, v) in pts)
        {
            float x = mapX(t);
            float y = mapY(selector(v));

            if (!started)
            {
                started = true;
                prevX = x; prevY = y;
                continue;
            }

            canvas.DrawLine(prevX, prevY, x, y);
            prevX = x; prevY = y;
        }
    }

    private void TrimOldLocked(DateTime now)
    {
        var limit = now.AddSeconds(-_windowSeconds);
        while (_samples.Count > 0 && _samples.Peek().t < limit)
            _samples.Dequeue();
    }

    private DateTime GetLastTimeLocked()
    {
        DateTime last = DateTime.MinValue;
        foreach (var (t, _) in _samples) last = t;
        return last == DateTime.MinValue ? DateTime.UtcNow : last;
    }
}
