using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Chiko.WirelessControl.App.Services;

public static class WifiScanner
{
    public static async Task<List<string>> ScanAsync(int port, int timeoutMs, int maxConcurrency, CancellationToken ct)
    {
        var (ip, mask) = GetLocalIPv4AndMask();
        if (ip is null || mask is null)
            return new List<string>();

        var hosts = EnumerateHosts(ip, mask).ToList();

        var results = new List<string>();
        using var sem = new SemaphoreSlim(maxConcurrency);

        var tasks = hosts.Select(async host =>
        {
            ct.ThrowIfCancellationRequested();
            await sem.WaitAsync(ct);
            try
            {
                if (await CanConnectAsync(host, port, timeoutMs, ct))
                {
                    lock (results) results.Add(host.ToString());
                }
            }
            finally
            {
                sem.Release();
            }
        });

        await Task.WhenAll(tasks);
        results.Sort();
        return results;
    }

    private static async Task<bool> CanConnectAsync(IPAddress host, int port, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);

            var done = await Task.WhenAny(connectTask, Task.Delay(timeoutMs, ct));
            if (done != connectTask) return false;

            // ConnectAsync が例外を投げていればここで拾える
            await connectTask;
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static (IPAddress? ip, IPAddress? mask) GetLocalIPv4AndMask()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            var ipProps = ni.GetIPProperties();
            foreach (var ua in ipProps.UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (IPAddress.IsLoopback(ua.Address)) continue;

                // 一部環境で IPv4Mask が null のことがある
                if (ua.IPv4Mask is null) continue;

                return (ua.Address, ua.IPv4Mask);
            }
        }
        return (null, null);
    }

    private static IEnumerable<IPAddress> EnumerateHosts(IPAddress ip, IPAddress mask)
    {
        var ipBytes = ip.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();

        uint ipU = ToUInt32(ipBytes);
        uint maskU = ToUInt32(maskBytes);

        uint network = ipU & maskU;
        uint broadcast = network | ~maskU;

        // network/broadcast を除外
        for (uint i = network + 1; i < broadcast; i++)
        {
            yield return FromUInt32(i);
        }
    }

    private static uint ToUInt32(byte[] bytes)
        => (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);

    private static IPAddress FromUInt32(uint value)
        => new(new byte[]
        {
            (byte)((value >> 24) & 0xFF),
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF),
        });
}
