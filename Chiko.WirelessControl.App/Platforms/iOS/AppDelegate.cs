using Foundation;
using AVFoundation;
using UIKit;
using Microsoft.Maui;

namespace Chiko.WirelessControl.App
{
    [Register("AppDelegate")]
    public class AppDelegate : MauiUIApplicationDelegate
    {
        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

        public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                System.Diagnostics.Debug.WriteLine("[BOOT] UnhandledException: " + e.ExceptionObject);
            };

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                System.Diagnostics.Debug.WriteLine("[BOOT] UnobservedTaskException: " + e.Exception);
                e.SetObserved();
            };

            // 先にMAUI側の初期化を通す
            var ok = base.FinishedLaunching(application, launchOptions);

            // ★追加：音が鳴らない端末/状態の保険（効果音用）
            try
            {
                var session = AVAudioSession.SharedInstance();
                session.SetCategory(AVAudioSessionCategory.Ambient);
                session.SetActive(true);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[BOOT] AVAudioSession init error: " + ex); }

            return ok;
        }
    }
}
