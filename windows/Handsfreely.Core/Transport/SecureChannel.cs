using Handsfreely.Core.Crypto;

namespace Handsfreely.Core.Transport;

public sealed class SecureChannel : IAsyncDisposable
{
    private readonly FrameStream _frames;
    private readonly FrameCipher _send;
    private readonly FrameCipher _receive;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public SecureChannel(FrameStream frames, byte[] sendKey, byte[] receiveKey)
    {
        _frames = frames;
        _send = new FrameCipher(sendKey);
        _receive = new FrameCipher(receiveKey);
    }

    public async Task SendAsync(ReadOnlyMemory<byte> plaintext, CancellationToken ct = default)
    {
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _frames.WriteAsync(_send.Encrypt(plaintext.Span), ct).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task<byte[]?> ReceiveAsync(CancellationToken ct = default)
    {
        var frame = await _frames.ReadAsync(ct).ConfigureAwait(false);
        return frame is null ? null : _receive.Decrypt(frame);
    }

    public async ValueTask DisposeAsync()
    {
        await _frames.DisposeAsync().ConfigureAwait(false);
        _send.Dispose();
        _receive.Dispose();
        _sendLock.Dispose();
    }
}
