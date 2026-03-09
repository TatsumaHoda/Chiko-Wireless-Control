using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace Chiko.WirelessControl.App
{
    public partial class App : Application
    {
        private readonly IServiceProvider? _services;

        public App()
        {
            InitializeComponent();
        }

        public App(IServiceProvider services) : this()
        {
            _services = services;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            try
            {
                // iOS単体起動では Current.Handler が未初期化のタイミングがあるため、DIを優先して解決する
                var services =
                    _services
                    ?? IPlatformApplication.Current?.Services
                    ?? Current?.Handler?.MauiContext?.Services;

                if (services is null)
                    throw new InvalidOperationException("Service provider is not available at window creation.");

                var shell = services.GetService<AppShell>()
                            ?? ActivatorUtilities.CreateInstance<AppShell>(services);

                return new Window(shell);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[BOOT] CreateWindow EXCEPTION: " + ex);

                // 例外でプロセス終了しないよう、最小限のフォールバック画面を返す
                return new Window(new ContentPage
                {
                    Content = new Label
                    {
                        Text = "Application failed to initialize.",
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.Center
                    }
                });
            }
        }
    }
}
