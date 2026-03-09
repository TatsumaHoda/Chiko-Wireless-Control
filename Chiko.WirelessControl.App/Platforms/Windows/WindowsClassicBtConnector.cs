#if WINDOWS
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using global::Chiko.WirelessControl.App.Services;

using global::Windows.Devices.Bluetooth;
using global::Windows.Devices.Bluetooth.Rfcomm;
using global::Windows.Devices.Enumeration;
using global::Windows.Networking.Sockets;

namespace Chiko.WirelessControl.App.Platforms.Windows;

public sealed class WindowsClassicBtConnector : IClassicBtConnector
{
    public async Task<IChikoLink> ConnectAsync(string addressOrId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // 1) デバイスDI（ペアリング用）
        var di = await ResolveDeviceInfoAsync(addressOrId, ct);

        // 2) 未ペアリングなら「PIN不要」前提でペアリング（ConfirmOnly系だけ Accept）
        if (!di.Pairing.IsPaired)
        {
            var custom = di.Pairing.Custom;

            void OnPairingRequested(DeviceInformationCustomPairing sender, DevicePairingRequestedEventArgs args)
            {
                if (args.PairingKind == DevicePairingKinds.ConfirmOnly ||
                    args.PairingKind == DevicePairingKinds.ConfirmPinMatch)
                {
                    args.Accept();
                }
                // PIN が必要な種類が来たら Accept しない（=失敗にする）
            }

            custom.PairingRequested += OnPairingRequested;
            try
            {
                var res = await custom.PairAsync(
                    DevicePairingKinds.ConfirmOnly | DevicePairingKinds.ConfirmPinMatch,
                    DevicePairingProtectionLevel.None
                ).AsTask(ct);

                if (res.Status != DevicePairingResultStatus.Paired &&
                    res.Status != DevicePairingResultStatus.AlreadyPaired)
                {
                    throw new InvalidOperationException($"ペアリングに失敗しました: {res.Status}");
                }
            }
            finally
            {
                custom.PairingRequested -= OnPairingRequested;
            }
        }

        ct.ThrowIfCancellationRequested();

        // 3) BluetoothDevice を作って RFCOMM(SPP) を“直取り”する（ここが重要）
        if (!TryParseBluetoothAddress(addressOrId, out var btAddr))
        {
            // addressOrId が Id の場合もあるので、DI から AEP address を拾って補完
            if (di.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var addrObj) &&
                addrObj is string addrStr &&
                TryParseBluetoothAddress(addrStr, out btAddr))
            {
                // ok
            }
            else
            {
                throw new ArgumentException($"Bluetooth address format invalid: '{addressOrId}'", nameof(addressOrId));
            }
        }

        var bt = await BluetoothDevice.FromBluetoothAddressAsync(btAddr).AsTask(ct);
        if (bt is null)
            throw new InvalidOperationException("BluetoothDevice.FromBluetoothAddressAsync が null を返しました。");

        // ★ SerialPort(=SPP, UUID 0x1101) を Uncached で問い合わせ
        var sppResult = await bt.GetRfcommServicesForIdAsync(
            RfcommServiceId.SerialPort,
            BluetoothCacheMode.Uncached
        ).AsTask(ct);

        var spp = sppResult.Services?.FirstOrDefault();

        // 4) 0件なら全サービスを列挙してログ（原因切り分け）
        if (spp is null)
        {
            var all = await bt.GetRfcommServicesAsync(BluetoothCacheMode.Uncached).AsTask(ct);

            var dump = (all.Services is null || all.Services.Count == 0)
                ? "Rfcomm services: 0"
                : "Rfcomm services:\n" + string.Join("\n",
                    all.Services.Select(s => $"- {s.ServiceId?.AsString()}  Name='{s.Device?.Name}'  Conn='{s.ConnectionServiceName}'"));

            throw new InvalidOperationException(
                "SPP(Rfcomm SerialPort) サービスが見つかりませんでした。\n" +
                dump +
                "\n\n※ まず (Package/MSIX) 実行になっているか、Windows設定で当該デバイスがペアリング済みか確認してください。");
        }

        // 5) StreamSocket 接続
        var socket = new StreamSocket();
        using var reg = ct.Register(() => { try { socket.Dispose(); } catch { } });

        await socket.ConnectAsync(
            spp.ConnectionHostName,
            spp.ConnectionServiceName,
            SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication
        ).AsTask(ct);

        return new Link(socket);
    }

    private sealed class Link : IChikoLink
    {
        private readonly StreamSocket _socket;
        private readonly Stream _stream;

        public Link(StreamSocket socket)
        {
            _socket = socket;
            var read = socket.InputStream.AsStreamForRead();
            var write = socket.OutputStream.AsStreamForWrite();
            _stream = new DuplexStream(read, write);
        }

        public Stream Stream => _stream;

        public ValueTask DisposeAsync()
        {
            try { _stream.Dispose(); } catch { }
            try { _socket.Dispose(); } catch { }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DuplexStream : Stream
    {
        private readonly Stream _read;
        private readonly Stream _write;

        public DuplexStream(Stream read, Stream write)
        {
            _read = read;
            _write = write;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => _read.Read(buffer, offset, count);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _read.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Write(byte[] buffer, int offset, int count)
        {
            _write.Write(buffer, offset, count);
            _write.Flush();
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await _write.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
            await _write.FlushAsync(cancellationToken);
        }

        public override void Flush() => _write.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _write.FlushAsync(cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    // --- DeviceInformation 解決（MACでもIdでもOK）---
    private static async Task<DeviceInformation> ResolveDeviceInfoAsync(string addressOrId, CancellationToken ct)
    {
        if (addressOrId.Length > 20 && (addressOrId.Contains('\\') || addressOrId.Contains('{')))
            return await DeviceInformation.CreateFromIdAsync(addressOrId).AsTask(ct);

        if (!TryParseBluetoothAddress(addressOrId, out var btAddr))
            throw new ArgumentException($"Bluetooth address format invalid: '{addressOrId}'", nameof(addressOrId));

        var selector = BluetoothDevice.GetDeviceSelectorFromBluetoothAddress(btAddr);
        var dis = await DeviceInformation.FindAllAsync(selector).AsTask(ct);

        return dis.FirstOrDefault()
            ?? throw new InvalidOperationException("DeviceInformation が見つかりませんでした（未検出/権限/ペアリング状態を確認）。");
    }

    private static bool TryParseBluetoothAddress(string s, out ulong value)
    {
        try { value = ParseBluetoothAddress(s); return true; }
        catch { value = 0; return false; }
    }

    private static ulong ParseBluetoothAddress(string s)
    {
        var hex = new string(s.Where(Uri.IsHexDigit).ToArray());
        if (hex.Length != 12)
            throw new ArgumentException($"Bluetooth address format invalid: '{s}'", nameof(s));
        return Convert.ToUInt64(hex, 16);
    }
}
#endif
