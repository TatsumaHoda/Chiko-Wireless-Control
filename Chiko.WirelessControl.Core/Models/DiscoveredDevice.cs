namespace Chiko.WirelessControl.Core.Models;

public sealed record DiscoveredDevice(
    TransportKind Kind,
    string DisplayName,
    string Id,     // Classic: MAC / BLE: DeviceId / Wi-Fi: IP等
    int? Rssi = null);
