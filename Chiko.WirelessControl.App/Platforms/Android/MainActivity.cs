using Android.App;
using Android.Content.PM;
using Android.OS;

namespace Chiko.WirelessControl.App
{
    [Activity(
        Theme = "@style/Maui.SplashTheme", 
        MainLauncher = true, 
        LaunchMode = LaunchMode.SingleTop, 
        ConfigurationChanges = ConfigChanges.ScreenSize 
        | ConfigChanges.Orientation 
        | ConfigChanges.UiMode 
        | ConfigChanges.ScreenLayout 
        | ConfigChanges.SmallestScreenSize 
        | ConfigChanges.Density,
        ScreenOrientation = ScreenOrientation.Landscape // ★追加：横固定
        )]
    public class MainActivity : MauiAppCompatActivity
    {
    }
}
