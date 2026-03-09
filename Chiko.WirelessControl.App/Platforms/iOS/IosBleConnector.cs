#if IOS
using CoreBluetooth;
using CoreFoundation;
using Foundation;
using global::Chiko.WirelessControl.App.Services;
using System.Threading.Channels;

namespace Chiko.WirelessControl.App.Platforms.iOS;

public sealed class IosBleConnector : NSObject, IBleConnector, ICBCentralManagerDelegate, ICBPeripheralDelegate
{
    private static readonly CBUUID UartService = CBUUID.FromString("6E400001-B5A3-F393-E0A9-E50E24DCCA9E");
    private static readonly CBUUID UartRx = CBUUID.FromString("6E400002-B5A3-F393-E0A9-E50E24DCCA9E"); // Write
    private static readonly CBUUID UartTx = CBUUID.FromString("6E400003-B5A3-F393-E0A9-E50E24DCCA9E"); // Notify

    private CBCentralManager? _central;
    private TaskCompletionSource<bool>? _poweredOnTcs;

    private TaskCompletionSource<bool>? _connectedTcs;
    private TaskCompletionSource<bool>? _discoveredTcs;

    private CBPeripheral? _peripheral;
    private CBCharacteristic? _rx;
    private CBCharacteristic? _tx;

    private readonly Channel<byte> _rxBytes =
        Channel.CreateUnbounded<byte>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task<IChikoLink> ConnectAsync(string deviceId, CancellationToken ct)
    {
        _poweredOnTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _central = new CBCentralManager(this, DispatchQueue.MainQueue);

        using (ct.Register(() => _poweredOnTcs.TrySetCanceled(ct)))
            await _poweredOnTcs.Task;

        if (!Guid.TryParse(deviceId, out var guid))
            throw new InvalidOperationException("iOS BLE deviceId は UUID(GUID) 形式が必要です。");

        var uuid = new NSUuid(guid.ToString());
        var arr = _central.RetrievePeripheralsWithIdentifiers(new[] { uuid });
        _peripheral = arr?.FirstOrDefault();

        if (_peripheral is null)
            throw new InvalidOperationException("指定UUIDのPeripheralが見つかりません（スキャン結果Idを確認してください）。");

        _peripheral.Delegate = this;

        _connectedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _discoveredTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _central.ConnectPeripheral(_peripheral);

        using (ct.Register(() => _connectedTcs.TrySetCanceled(ct)))
            await _connectedTcs.Task;

        _peripheral.DiscoverServices(new[] { UartService });

        using (ct.Register(() => _discoveredTcs.TrySetCanceled(ct)))
            await _discoveredTcs.Task;

        if (_rx is null || _tx is null)
            throw new InvalidOperationException("UART RX/TX が取得できません。");

        _peripheral.SetNotifyValue(true, _tx);

        var stream = new BleUartStream(this, ct);
        return new Link(this, stream);
    }

    private sealed class Link : IChikoLink
    {
        private readonly IosBleConnector _owner;
        private readonly Stream _stream;

        public Link(IosBleConnector owner, Stream stream) { _owner = owner; _stream = stream; }
        public Stream Stream => _stream;

        public ValueTask DisposeAsync()
        {
            try { _stream.Dispose(); } catch { }
            try { _owner.DisposeConnection(); } catch { }
            return ValueTask.CompletedTask;
        }
    }

    private void DisposeConnection()
    {
        if (_central != null && _peripheral != null)
        {
            try { _central.CancelPeripheralConnection(_peripheral); } catch { }
        }
        _peripheral = null;
        _rx = null;
        _tx = null;
    }

    // ===== Central Delegate (Export で実装) =====

    [Export("centralManagerDidUpdateState:")]
    public void UpdatedState(CBCentralManager central)
    {
        // ★ enum は CBManagerState を使う
        if (central.State == CBManagerState.PoweredOn)
            _poweredOnTcs?.TrySetResult(true);
        else if (central.State == CBManagerState.Unsupported || central.State == CBManagerState.Unauthorized)
            _poweredOnTcs?.TrySetException(new InvalidOperationException($"Bluetooth not available: {central.State}"));
    }

    [Export("centralManager:didConnectPeripheral:")]
    public void ConnectedPeripheral(CBCentralManager central, CBPeripheral peripheral)
        => _connectedTcs?.TrySetResult(true);

    [Export("centralManager:didFailToConnectPeripheral:error:")]
    public void FailedToConnectPeripheral(CBCentralManager central, CBPeripheral peripheral, NSError? error)
        => _connectedTcs?.TrySetException(new IOException(error?.LocalizedDescription ?? "BLE connect failed"));

    // ===== Peripheral Delegate (Export で実装) =====

    [Export("peripheral:didDiscoverServices:")]
    public void DiscoveredService(CBPeripheral peripheral, NSError? error)
    {
        if (error != null)
        {
            _discoveredTcs?.TrySetException(new IOException(error.LocalizedDescription));
            return;
        }

        var svc = peripheral.Services?.FirstOrDefault(s => s.UUID.Equals(UartService));
        if (svc == null)
        {
            _discoveredTcs?.TrySetException(new InvalidOperationException("UART Service not found"));
            return;
        }

        peripheral.DiscoverCharacteristics(new[] { UartRx, UartTx }, svc);
    }

    [Export("peripheral:didDiscoverCharacteristicsForService:error:")]
    public void DiscoveredCharacteristic(CBPeripheral peripheral, CBService service, NSError? error)
    {
        if (error != null)
        {
            _discoveredTcs?.TrySetException(new IOException(error.LocalizedDescription));
            return;
        }

        _rx = service.Characteristics?.FirstOrDefault(c => c.UUID.Equals(UartRx));
        _tx = service.Characteristics?.FirstOrDefault(c => c.UUID.Equals(UartTx));

        if (_rx == null || _tx == null)
        {
            _discoveredTcs?.TrySetException(new InvalidOperationException("UART RX/TX not found"));
            return;
        }

        _discoveredTcs?.TrySetResult(true);
    }

    [Export("peripheral:didUpdateValueForCharacteristic:error:")]
    public void UpdatedCharacterteristicValue(CBPeripheral peripheral, CBCharacteristic characteristic, NSError? error)
    {
        if (error != null) return;
        if (_tx == null) return;
        if (!characteristic.UUID.Equals(_tx.UUID)) return;

        var data = characteristic.Value;
        if (data == null) return;

        foreach (var b in data.ToArray())
            _rxBytes.Writer.TryWrite(b);
    }

    // ===== Stream wrapper =====
    private sealed class BleUartStream : Stream
    {
        private readonly IosBleConnector _o;
        private readonly CancellationToken _outerCt;

        public BleUartStream(IosBleConnector owner, CancellationToken outerCt)
        {
            _o = owner;
            _outerCt = outerCt;
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

            var b1 = await _o._rxBytes.Reader.ReadAsync(ct);
            buffer[offset] = b1;
            var read = 1;

            while (read < count && _o._rxBytes.Reader.TryRead(out var b))
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

            var p = _o._peripheral ?? throw new InvalidOperationException("Peripheral not ready");
            var rx = _o._rx ?? throw new InvalidOperationException("RX not ready");

            await _o._writeLock.WaitAsync(ct);
            try
            {
                var max = (int)p.GetMaximumWriteValueLength(CBCharacteristicWriteType.WithResponse);
                if (max <= 0) max = 20;

                var pos = 0;
                while (pos < count)
                {
                    ct.ThrowIfCancellationRequested();

                    var len = Math.Min(max, count - pos);
                    var chunk = new byte[len];
                    Array.Copy(buffer, offset + pos, chunk, 0, len);

                    using var ns = NSData.FromArray(chunk);
                    p.WriteValue(ns, rx, CBCharacteristicWriteType.WithResponse);

                    pos += len;
                }
            }
            finally
            {
                _o._writeLock.Release();
            }
        }

        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
#endif
