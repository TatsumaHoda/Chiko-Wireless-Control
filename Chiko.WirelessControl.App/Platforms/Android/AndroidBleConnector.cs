#if ANDROID
using global::Android.Bluetooth;
using global::Android.Content;
using global::Java.Util;
using global::System;
using global::System.IO;
using global::System.Threading;
using global::System.Threading.Channels;
using global::System.Threading.Tasks;

namespace Chiko.WirelessControl.App.Platforms.Android;

public sealed class AndroidBleConnector : global::Chiko.WirelessControl.App.Services.IBleConnector
{
    private static readonly UUID UartService = UUID.FromString("6E400001-B5A3-F393-E0A9-E50E24DCCA9E");
    private static readonly UUID UartRx = UUID.FromString("6E400002-B5A3-F393-E0A9-E50E24DCCA9E"); // Write
    private static readonly UUID UartTx = UUID.FromString("6E400003-B5A3-F393-E0A9-E50E24DCCA9E"); // Notify
    private static readonly UUID CccdUuid = UUID.FromString("00002902-0000-1000-8000-00805F9B34FB");

    private readonly Context _ctx;

    public AndroidBleConnector()
    {
        // ★ App 衝突回避
        _ctx = global::Android.App.Application.Context;
    }

    public async Task<global::Chiko.WirelessControl.App.Services.IChikoLink> ConnectAsync(string deviceId, CancellationToken ct)
    {
        var adapter = BluetoothAdapter.DefaultAdapter
            ?? throw new InvalidOperationException("BluetoothAdapter が取得できません。");

        var dev = adapter.GetRemoteDevice(deviceId)
            ?? throw new InvalidOperationException("BLE デバイスが取得できません。");

        var cb = new GattCb();

        using var reg = ct.Register(() => { try { cb.Close(); } catch { } });

        var gatt = dev.ConnectGatt(_ctx, false, cb, BluetoothTransports.Le);
        cb.AttachGatt(gatt);

        await cb.WaitReadyAsync(ct);

        var stream = new BleUartStream(cb);
        return new BleLink(cb, stream);
    }

    private sealed class BleLink : global::Chiko.WirelessControl.App.Services.IChikoLink
    {
        private readonly GattCb _cb;
        private readonly Stream _stream;

        public BleLink(GattCb cb, Stream stream)
        {
            _cb = cb;
            _stream = stream;
        }

        public Stream Stream => _stream;

        public ValueTask DisposeAsync()
        {
            try { _stream.Dispose(); } catch { }
            try { _cb.Close(); } catch { }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class GattCb : BluetoothGattCallback
    {
        private BluetoothGatt? _gatt;

        private readonly TaskCompletionSource _readyTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private BluetoothGattCharacteristic? _rx;
        private BluetoothGattCharacteristic? _tx;

        private readonly Channel<byte> _rxBytes =
            Channel.CreateUnbounded<byte>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private TaskCompletionSource<bool>? _writeTcs;

        private int _payloadMax = 20;

        public void AttachGatt(BluetoothGatt gatt) => _gatt = gatt;

        public override void OnConnectionStateChange(BluetoothGatt gatt, GattStatus status, ProfileState newState)
        {
            if (status != GattStatus.Success)
            {
                _readyTcs.TrySetException(new IOException($"GATT connect failed: {status}"));
                return;
            }

            if (newState == ProfileState.Connected)
            {
                try { gatt.RequestMtu(247); } catch { }
                try { gatt.DiscoverServices(); }
                catch (Exception ex) { _readyTcs.TrySetException(ex); }
            }
            else if (newState == ProfileState.Disconnected)
            {
                _readyTcs.TrySetException(new IOException("GATT disconnected"));
            }
        }

        public override void OnMtuChanged(BluetoothGatt gatt, int mtu, GattStatus status)
        {
            if (status == GattStatus.Success && mtu >= 23)
                _payloadMax = Math.Max(20, mtu - 3);
        }

        public override void OnServicesDiscovered(BluetoothGatt gatt, GattStatus status)
        {
            if (status != GattStatus.Success)
            {
                _readyTcs.TrySetException(new IOException($"DiscoverServices failed: {status}"));
                return;
            }

            var svc = gatt.GetService(UartService);
            if (svc == null)
            {
                _readyTcs.TrySetException(new InvalidOperationException("UART Service が見つかりません。"));
                return;
            }

            _rx = svc.GetCharacteristic(UartRx);
            _tx = svc.GetCharacteristic(UartTx);

            if (_rx == null || _tx == null)
            {
                _readyTcs.TrySetException(new InvalidOperationException("UART Characteristic(RX/TX) が見つかりません。"));
                return;
            }

            try
            {
                gatt.SetCharacteristicNotification(_tx, true);

                var cccd = _tx.GetDescriptor(CccdUuid);
                if (cccd == null)
                {
                    _readyTcs.TrySetException(new InvalidOperationException("CCCD が見つかりません。"));
                    return;
                }

#pragma warning disable CA1422
                cccd.SetValue(BluetoothGattDescriptor.EnableNotificationValue.ToArray());
#pragma warning restore CA1422

                gatt.WriteDescriptor(cccd);

                // ここは “多くの機器で即OK” だが、必要なら OnDescriptorWrite を待つ方式に拡張可能
                _readyTcs.TrySetResult();
            }
            catch (Exception ex)
            {
                _readyTcs.TrySetException(ex);
            }
        }

        public override void OnCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic)
        {
            if (_tx == null) return;
            if (!EqualsUuid(characteristic.Uuid, _tx.Uuid)) return;

            var data = characteristic.GetValue();
            if (data == null || data.Length == 0) return;

            foreach (var b in data)
                _rxBytes.Writer.TryWrite(b);
        }

        public override void OnCharacteristicWrite(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, GattStatus status)
        {
            _writeTcs?.TrySetResult(status == GattStatus.Success);
        }

        private static bool EqualsUuid(UUID? a, UUID? b)
            => a != null && b != null && string.Equals(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);

        public async Task WaitReadyAsync(CancellationToken ct)
        {
            using var reg = ct.Register(() => _readyTcs.TrySetCanceled(ct));
            await _readyTcs.Task;
        }

        public async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            var read = 0;

            var b1 = await _rxBytes.Reader.ReadAsync(ct);
            buffer[offset] = b1;
            read = 1;

            while (read < count && _rxBytes.Reader.TryRead(out var b))
            {
                buffer[offset + read] = b;
                read++;
            }

            return read;
        }

        public async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            if (_gatt == null || _rx == null)
                throw new InvalidOperationException("GATT not ready");

            await _writeLock.WaitAsync(ct);
            try
            {
                var pos = 0;
                while (pos < count)
                {
                    ct.ThrowIfCancellationRequested();

                    var len = Math.Min(_payloadMax, count - pos);
                    var chunk = new byte[len];
                    Array.Copy(buffer, offset + pos, chunk, 0, len);

                    _writeTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                    // ★ SetWriteType は無い：WriteType プロパティを使う
                    _rx.WriteType = GattWriteType.Default; // WithResponse
                    _rx.SetValue(chunk);

#pragma warning disable CA1422
                    if (!_gatt.WriteCharacteristic(_rx))
                        throw new IOException("WriteCharacteristic failed");
#pragma warning restore CA1422

                    using var reg = ct.Register(() => _writeTcs.TrySetCanceled(ct));
                    var ok = await _writeTcs.Task;
                    if (!ok) throw new IOException("Characteristic write failed");

                    pos += len;
                }
            }
            finally
            {
                _writeTcs = null;
                _writeLock.Release();
            }
        }

        public void Close()
        {
            try { _gatt?.Disconnect(); } catch { }
            try { _gatt?.Close(); } catch { }
            _gatt = null;
        }
    }

    private sealed class BleUartStream : Stream
    {
        private readonly GattCb _cb;

        public BleUartStream(GattCb cb) => _cb = cb;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Use ReadAsync");

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _cb.ReadAsync(buffer, offset, count, cancellationToken);

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Use WriteAsync");

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _cb.WriteAsync(buffer, offset, count, cancellationToken);

        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
#endif
