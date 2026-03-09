using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Chiko.WirelessControl.App.Views.Controls;

public partial class MetricCard : ContentView
{
    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(MetricCard), "");

    public static readonly BindableProperty ValueTextProperty =
        BindableProperty.Create(nameof(ValueText), typeof(string), typeof(MetricCard), "0.00");

    public static readonly BindableProperty UnitProperty =
        BindableProperty.Create(nameof(Unit), typeof(string), typeof(MetricCard), "");

    // ★追加：カード背景（未指定なら Surface2 相当を使う）
    public static readonly BindableProperty CardBackgroundProperty =
        BindableProperty.Create(
            nameof(CardBackground),
            typeof(Brush),
            typeof(MetricCard),
            defaultValue: null);

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string ValueText
    {
        get => (string)GetValue(ValueTextProperty);
        set => SetValue(ValueTextProperty, value);
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    // ★追加
    public Brush CardBackground
    {
        get
        {
            // 未指定なら従来通り Surface2 を使う（最小変更で互換維持）
            var b = (Brush)GetValue(CardBackgroundProperty);
            if (b != null) return b;

            // StaticResource を C# 側から拾う
            if (Application.Current?.Resources.TryGetValue("Surface2", out var v) == true && v is Brush rb)
                return rb;

            // 最悪のフォールバック（黒系）
            return Brush.Black;
        }
        set => SetValue(CardBackgroundProperty, value);
    }

    public MetricCard()
    {
        InitializeComponent();
        // ★BindingContext = this は入れない（親VMを壊す）
    }
}
