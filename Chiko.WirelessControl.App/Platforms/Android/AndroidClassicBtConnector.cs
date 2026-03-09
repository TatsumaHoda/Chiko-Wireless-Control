#if ANDROID
using global::Android.Bluetooth;
using global::Android.Content;
using global::Java.IO;
using global::Java.Util;
using global::System;
using global::System.IO;
using global::System.Threading;
using global::System.Threading.Tasks;

namespace Chiko.WirelessControl.App.Platforms.Android;

public sealed class AndroidClassicBtConnector : global::Chiko.WirelessControl.App.Services.IClassicBtConnector
{
    private static readonly UUID SppUuid =
        UUID.FromString("00001101-0000-1000-8000-00805F9B34FB"); // SPP

    private readonly Context _ctx;

    public AndroidClassicBtConnector()
    {
        // ★ App 衝突回避：global:: を必ず使う
        _ctx = global::Android.App.Application.Context;
    }

    public async Task<global::Chiko.WirelessControl.App.Services.IChikoLink> ConnectAsync(string address, CancellationToken ct)
    {
        var adapter = BluetoothAdapter.DefaultAdapter
            ?? throw new InvalidOperationException("BluetoothAdapter が取得できません。");

        try { if (adapter.IsDiscovering) adapter.CancelDiscovery(); } catch { }

        var device = adapter.GetRemoteDevice(address)
            ?? throw new InvalidOperationException("Bluetooth デバイスが取得できません。");

        var socket = device.CreateRfcommSocketToServiceRecord(SppUuid);

        using var reg = ct.Register(() => { try { socket.Close(); } catch { } });

        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            socket.Connect(); // blocking
        }, ct);

        return new Link(socket);
    }

    private sealed class Link : global::Chiko.WirelessControl.App.Services.IChikoLink
    {
        private readonly BluetoothSocket _socket;
        private readonly Stream _stream;

        public Link(BluetoothSocket socket)
        {
            _socket = socket;

            // あなたの環境では InputStream/OutputStream は System.IO.Stream
            var input = (Stream)socket.InputStream!;
            var output = (Stream)socket.OutputStream!;

            _stream = new DuplexStream(input, output);
        }

        public Stream Stream => _stream;

        public ValueTask DisposeAsync()
        {
            try { _stream.Dispose(); } catch { }
            try { _socket.Close(); } catch { }
            return ValueTask.CompletedTask;
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

            public override int Read(byte[] buffer, int offset, int count)
                => _read.Read(buffer, offset, count);

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
                => _read.ReadAsync(buffer, offset, count, cancellationToken);

            public override void Write(byte[] buffer, int offset, int count)
            {
                _write.Write(buffer, offset, count);
                _write.Flush();
            }

            public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                await _write.WriteAsync(buffer, offset, count, cancellationToken);
                await _write.FlushAsync(cancellationToken);
            }

            public override void Flush() => _write.Flush();
            public override Task FlushAsync(CancellationToken cancellationToken) => _write.FlushAsync(cancellationToken);

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }

}
#endif
