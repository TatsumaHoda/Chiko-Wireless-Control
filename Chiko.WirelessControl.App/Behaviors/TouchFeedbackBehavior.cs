using System;
using Microsoft.Maui.Controls;
using Chiko.WirelessControl.App.Services;

namespace Chiko.WirelessControl.App.Behaviors;

public sealed class TouchFeedbackBehavior : Behavior<VisualElement>
{
    public double PressedScale { get; set; } = 0.97;
    public double PressedOpacity { get; set; } = 0.92;
    public uint PressAnimMs { get; set; } = 60;
    public uint ReleaseAnimMs { get; set; } = 80;

    private Button? _button;
    private ImageButton? _imageButton;

    protected override void OnAttachedTo(VisualElement bindable)
    {
        base.OnAttachedTo(bindable);

        if (bindable is Button b)
        {
            _button = b;
            b.Pressed += OnPressed;
            b.Released += OnReleased;
        }
        else if (bindable is ImageButton ib)
        {
            _imageButton = ib;
            ib.Pressed += OnPressed;
            ib.Released += OnReleased;
        }
    }

    protected override void OnDetachingFrom(VisualElement bindable)
    {
        if (_button != null)
        {
            _button.Pressed -= OnPressed;
            _button.Released -= OnReleased;
            _button = null;
        }

        if (_imageButton != null)
        {
            _imageButton.Pressed -= OnPressed;
            _imageButton.Released -= OnReleased;
            _imageButton = null;
        }

        base.OnDetachingFrom(bindable);
    }

    private void OnPressed(object? sender, EventArgs e)
    {
        if (sender is not VisualElement ve) return;
        if (!ve.IsEnabled) return;

        // 触覚＋音
        TryFeedback(ve);

        // 押し込み（非同期だが待たない：体感重視）
        _ = ve.ScaleTo(PressedScale, PressAnimMs, Easing.CubicOut);
        _ = ve.FadeTo(PressedOpacity, PressAnimMs, Easing.CubicOut);
    }

    private void OnReleased(object? sender, EventArgs e)
    {
        if (sender is not VisualElement ve) return;

        _ = ve.ScaleTo(1.0, ReleaseAnimMs, Easing.CubicOut);
        _ = ve.FadeTo(1.0, ReleaseAnimMs, Easing.CubicOut);
    }

    private static void TryFeedback(VisualElement ve)
    {
        try
        {
            var sp = ve.Handler?.MauiContext?.Services;
            var svc = sp?.GetService(typeof(Chiko.WirelessControl.App.Services.IClickFeedback))
                      as Chiko.WirelessControl.App.Services.IClickFeedback;

            svc?.Trigger();
        }
        catch { }
    }

}
