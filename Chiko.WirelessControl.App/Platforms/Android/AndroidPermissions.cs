#if ANDROID
using Android;
using Microsoft.Maui.ApplicationModel;

namespace Chiko.WirelessControl.App.Platforms.Android;

public sealed class BluetoothScanPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        new[]
        {
            (Manifest.Permission.BluetoothScan, true),
        };
}

public sealed class BluetoothConnectPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        new[]
        {
            (Manifest.Permission.BluetoothConnect, true),
        };
}
#endif
