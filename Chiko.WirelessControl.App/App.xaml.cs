using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace Chiko.WirelessControl.App
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            // ★ DI コンテナから AppShell を取得（new しない）
            var shell = Current?.Handler?.MauiContext?.Services.GetService<AppShell>()
                        ?? throw new InvalidOperationException("AppShell is not registered in DI.");

            return new Window(shell);
        }
    }
}
