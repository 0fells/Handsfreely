using Windows.Networking.Sockets;

namespace Handsfreely.App.Infrastructure;

public sealed class SocketStream(StreamSocket socket) : Stream
{
    private readonly Stream _in = socket.InputStream.AsStreamForRead(0);
    private readonly Stream _out = socket.OutputStream.AsStreamForWrite(0);

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => _in.Read(buffer, offset, count);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => _in.ReadAsync(buffer, ct);
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => _in.ReadAsync(buffer, offset, count, ct);
    public override void Write(byte[] buffer, int offset, int count) => _out.Write(buffer, offset, count);
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => _out.WriteAsync(buffer, ct);
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) => _out.WriteAsync(buffer, offset, count, ct);
    public override void Flush() => _out.Flush();
    public override Task FlushAsync(CancellationToken ct) => _out.FlushAsync(ct);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _in.Dispose();
            _out.Dispose();
            socket.Dispose();
        }
        base.Dispose(disposing);
    }
}
