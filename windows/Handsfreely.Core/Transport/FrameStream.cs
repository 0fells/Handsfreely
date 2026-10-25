using System.Buffers.Binary;

namespace Handsfreely.Core.Transport;

public sealed class FrameStream : IAsyncDisposable
{
    public const int MaxFrameSize = 8 * 1024 * 1024;

    private readonly Stream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public FrameStream(Stream stream) => _stream = stream;

    public async Task<byte[]?> ReadAsync(CancellationToken ct = default)
    {
        var header = new byte[4];
        int got = 0;
        while (got < header.Length)
        {
            int n = await _stream.ReadAsync(header.AsMemory(got), ct).ConfigureAwait(false);
            if (n == 0)
            {
                if (got == 0) return null;
                throw new EndOfStreamException("connection closed inside a frame header");
            }
            got += n;
        }
        uint length = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (length > MaxFrameSize)
            throw new ProtocolException($"frame too large: {length}");
        var body = new byte[length];
        await _stream.ReadExactlyAsync(body, ct).ConfigureAwait(false);
        return body;
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> body, CancellationToken ct = default)
    {
        if (body.Length > MaxFrameSize)
            throw new ArgumentException("frame too large", nameof(body));
        var buffer = new byte[4 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)body.Length);
        body.CopyTo(buffer.AsMemory(4));
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(buffer, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync().ConfigureAwait(false);
        _writeLock.Dispose();
    }
}
