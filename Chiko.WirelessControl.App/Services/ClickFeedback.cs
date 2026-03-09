using System;
using Microsoft.Maui.ApplicationModel;
using Plugin.Maui.Audio;

namespace Chiko.WirelessControl.App.Services;

public sealed class ClickFeedback : IClickFeedback, IDisposable
{
    private readonly IAudioManager _audio;
    private IAudioPlayer? _player;

    // 連打で音が割れるのを抑える（必要なら調整）
    private long _lastTicks;
    private static readonly long MinIntervalTicks = TimeSpan.FromMilliseconds(40).Ticks;

    public ClickFeedback(IAudioManager audio)
    {
        _audio = audio;
        _ = PreloadAsync(); // fire and forget（失敗しても動作継続）
    }

    private async System.Threading.Tasks.Task PreloadAsync()
    {
        try
        {
            var stream = await FileSystem.OpenAppPackageFileAsync("click.wav");
            _player = _audio.CreatePlayer(stream);
            _player.Volume = 1.0;
        }
        catch
        {
            // click.wav が無い・読み込み失敗でもアプリは動かす
            _player = null;
        }
    }

    public void Trigger()
    {
        // ---- Haptic (main threadで実行) ----
        try
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    HapticFeedback.Default.Perform(HapticFeedbackType.Click);
                }
                catch { }
            });
        }
        catch { }

#if ANDROID
        // ---- Android: Clickが弱い端末向けに短い振動を追加（設定で触覚が無効でも出る可能性が上がる）----
        try
        {
            Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(12));
        }
        catch { }
#endif

        // ---- Sound ----
        try
        {
            var now = DateTime.UtcNow.Ticks;
            if (now - _lastTicks < MinIntervalTicks) return;
            _lastTicks = now;

            if (_player == null) return;

            _player.Stop();
            _player.Play();
        }
        catch { }
    }


    public void Dispose()
    {
        try { _player?.Dispose(); } catch { }
        _player = null;
    }
}
