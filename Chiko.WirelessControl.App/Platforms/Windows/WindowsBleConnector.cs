#if WINDOWS
using global::Chiko.WirelessControl.App.Services;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace Chiko.WirelessControl.App.Platforms.Windows;

public sealed class WindowsBleConnector : IBleConnector
{
    private static readonly Guid UartService = Guid.Parse("6E400001-B5A3-F393-E0A9-E50E24DCCA9E");
    private static readonly Guid UartRx = Guid.Parse("6E400002-B5A3-F393-E0A9-E50E24DCCA9E"); // Write
    private static readonly Guid UartTx = Guid.Parse("6E400003-B5A3-F393-E0A9-E50E24DCCA9E"); // Notify

    public async Task<IChikoLink> ConnectAsync(string deviceId, CancellationToken ct)
    {
        // deviceId: "D48AFCEFC646" を想定（=MAC相当の16進）
        var addr = ParseBluetoothAddress(deviceId);

        ct.ThrowIfCancellationRequested();

        var dev = await BluetoothLEDevice.FromBluetoothAddressAsync(addr);
        if (dev is null)
            throw new InvalidOperationException("BluetoothLEDevice が取得できませんでした。");

        // UART Service
        var svcRes = await dev.GetGattServicesForUuidAsync(UartService, BluetoothCacheMode.Uncached).AsTask(ct);
        if (svcRes.Status != GattCommunicationStatus.Success)
            throw new IOException($"GetGattServicesForUuidAsync failed: {svcRes.Status}");

        var svc = svcRes.Services.FirstOrDefault()
            ?? throw new IOException("UART Service が見つかりません。");

        // RX/TX
        var chRes = await svc.GetCharacteristicsAsync(BluetoothCacheMode.Uncached).AsTask(ct);
        if (chRes.Status != GattCommunicationStatus.Success)
            throw new IOException($"GetCharacteristicsAsync failed: {chRes.Status}");

        var rx = chRes.Characteristics.FirstOrDefault(c => c.Uuid == UartRx)
            ?? throw new IOException("UART RX characteristic が見つかりません。");
        var tx = chRes.Characteristics.FirstOrDefault(c => c.Uuid == UartTx)
            ?? throw new IOException("UART TX characteristic が見つかりません。");

        // Notify enable
        var cccd = await tx.WriteClientCharacteristicConfigurationDescriptorAsync(
            GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(ct);

        if (cccd != GattCommunicationStatus.Success)
            throw new IOException($"Notify enable failed: {cccd}");

        var cb = new NotifyPipe(tx);
        cb.Start();

        var stream = new BleUartStream(rx, cb, ct);
        return new Link(dev, cb, stream);
    }

    private sealed class Link : IChikoLink
    {
        private readonly BluetoothLEDevice _dev;
        private readonly NotifyPipe _pipe;
        private readonly Stream _stream;

        public Link(BluetoothLEDevice dev, NotifyPipe pipe, Stream stream)
        {
            _dev = dev;
            _pipe = pipe;
            _stream = stream;
        }

        public Stream Stream => _stream;

        public ValueTask DisposeAsync()
        {
            try { _stream.Dispose(); } catch { }
            try { _pipe.Dispose(); } catch { }
            try { _dev.Dispose(); } catch { }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NotifyPipe : IDisposable
    {
        private readonly GattCharacteristic _tx;
        private readonly Channel<byte> _ch =
            Channel.CreateUnbounded<byte>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

        public NotifyPipe(GattCharacteristic tx) => _tx = tx;

        public ChannelReader<byte> Reader => _ch.Reader;

        public void Start()
        {
            _tx.ValueChanged += OnValueChanged;
        }

        private void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            var data = args.CharacteristicValue.ToArray();
            foreach (var b in data)
                _ch.Writer.TryWrite(b);
        }

        public void Dispose()
        {
            try { _tx.ValueChanged -= OnValueChanged; } catch { }
            try { _ch.Writer.TryComplete(); } catch { }
        }
    }

    private sealed class BleUartStream : Stream
    {
        private readonly GattCharacteristic _rx;
        private readonly NotifyPipe _pipe;
        private readonly CancellationToken _outerCt;
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        // Windows は MaxWriteValueLength が取れるので活用（取れない場合は20）
        private readonly int _payloadMax;

        public BleUartStream(GattCharacteristic rx, NotifyPipe pipe, CancellationToken outerCt)
        {
            _rx = rx;
            _pipe = pipe;
            _outerCt = outerCt;

            //_payloadMax = Math.Max(20, rx.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)
            //    ? rx.GetMaxWriteValueLength(GattWriteOption.WriteWithoutResponse)
            //    : rx.GetMaxWriteValueLength(GattWriteOption.WriteWithResponse));
            _payloadMax = 20;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Use ReadAsync");

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var ct = CancellationTokenSource.CreateLinkedTokenSource(_outerCt, cancellationToken).Token;

            // 最低1byte待つ
            var b1 = await _pipe.Reader.ReadAsync(ct);
            buffer[offset] = b1;
            var read = 1;

            while (read < count && _pipe.Reader.TryRead(out var b))
            {
                buffer[offset + read] = b;
                read++;
            }
            return read;
        }

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Use WriteAsync");

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var ct = CancellationTokenSource.CreateLinkedTokenSource(_outerCt, cancellationToken).Token;

            await _writeLock.WaitAsync(ct);
            try
            {
                var pos = 0;
                while (pos < count)
                {
                    ct.ThrowIfCancellationRequested();

                    var len = Math.Min(_payloadMax, count - pos);
                    var chunk = buffer.AsSpan(offset + pos, len).ToArray();
                    var ibuf = chunk.AsBuffer();

                    var st = await _rx.WriteValueAsync(ibuf, GattWriteOption.WriteWithResponse).AsTask(ct);
                    if (st != GattCommunicationStatus.Success)
                        throw new IOException($"BLE write failed: {st}");

                    pos += len;
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private static ulong ParseBluetoothAddress(string s)
    {
        var hex = new string(s.Where(Uri.IsHexDigit).ToArray());
        if (hex.Length != 12)
            throw new ArgumentException($"BLE id/address invalid: '{s}'", nameof(s));

        return Convert.ToUInt64(hex, 16);
    }
}
#endif
