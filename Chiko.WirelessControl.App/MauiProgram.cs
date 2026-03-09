// MauiProgram.cs
using Chiko.WirelessControl.App.Services;
using Chiko.WirelessControl.App.ViewModels;
using Microsoft.Extensions.Logging;

// ★ LiveCharts 初期化用
using LiveChartsCore.SkiaSharpView.Maui;

// ★ SkiaSharp.Views 初期化用（SKLottie等）
using SkiaSharp.Views.Maui.Controls.Hosting;

using Plugin.Maui.Audio;

#if ANDROID
using Chiko.WirelessControl.App.Platforms.Android;
#endif
#if WINDOWS
using Chiko.WirelessControl.App.Platforms.Windows;
#endif
#if IOS
using Chiko.WirelessControl.App.Platforms.iOS;
#endif

namespace Chiko.WirelessControl.App
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            try
            {
                Console.WriteLine("[BOOT] MauiProgram START");

                var builder = MauiApp.CreateBuilder();

                // ★★★重要：rc5+ は UseMauiApp<App>() にチェーンする必要あり
                builder
                    .UseMauiApp<App>()
                    .UseLiveCharts()
                    .UseSkiaSharp()
                    .ConfigureFonts(fonts =>
                    {
                        fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                        fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                    });

                // ★ SkiaSharp.Views (SKCanvasView/SKLottie等) の初期化は “明示呼び出し”
                SkiaSharp.Views.Maui.Controls.Hosting.AppHostBuilderExtensions.UseSkiaSharp(builder);

                // ★ここに追加（Buildより前）
                builder.Services.AddSingleton<MainPageViewModel>();
                builder.Services.AddSingleton<MainPage>();
                builder.Services.AddSingleton<AppShell>();

#if ANDROID
                builder.Services.AddSingleton<IWifiApScanner, WifiApScanner>();
                builder.Services.AddSingleton<IWifiSsidConnector, AndroidWifiSsidConnector>();

                builder.Services.AddSingleton<IClassicBtScanner, AndroidClassicBtScanner>();
                builder.Services.AddSingleton<IBleScanner, AndroidBleScanner>();

                builder.Services.AddSingleton<IClassicBtConnector, AndroidClassicBtConnector>();
                builder.Services.AddSingleton<IBleConnector, AndroidBleConnector>();

                builder.Services.AddSingleton(AudioManager.Current);
                builder.Services.AddSingleton<IClickFeedback, ClickFeedback>();
#endif

#if WINDOWS
                builder.Services.AddSingleton<IWifiApScanner, WindowsWifiApScanner>();
                builder.Services.AddSingleton<IClassicBtScanner, WindowsClassicBtScanner>();
                builder.Services.AddSingleton<IBleScanner, WindowsBleScanner>();
                builder.Services.AddSingleton<IClassicBtConnector, WindowsClassicBtConnector>();
                builder.Services.AddSingleton<IBleConnector, WindowsBleConnector>();
#endif

#if IOS
                builder.Services.AddSingleton<IWifiApScanner, IosWifiApScanner>();
                builder.Services.AddSingleton<IBleScanner, IosBleScanner>();
                builder.Services.AddSingleton<IBleConnector, IosBleConnector>();
#endif

#if DEBUG
                builder.Logging.AddDebug();
#endif

                Console.WriteLine("[BOOT] MauiProgram BEFORE Build()");
                var app = builder.Build();
                Console.WriteLine("[BOOT] MauiProgram AFTER Build()");
                return app;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[BOOT] MauiProgram EXCEPTION: " + ex);
                throw;
            }
        }
    }
}
