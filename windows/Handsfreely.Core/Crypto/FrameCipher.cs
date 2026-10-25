using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Handsfreely.Core.Crypto;

public sealed class FrameCipher : IDisposable
{
    public const int TagSize = 16;
    private const int NonceSize = 12;

    private readonly AesGcm _aes;
    private readonly object _lock = new();
    private ulong _counter;

    public FrameCipher(byte[] key)
    {
        if (key.Length != 32) throw new ArgumentException("key must be 32 bytes", nameof(key));
        _aes = new AesGcm(key, TagSize);
    }

    public byte[] Encrypt(ReadOnlySpan<byte> plaintext)
    {
        var output = new byte[plaintext.Length + TagSize];
        lock (_lock)
        {
            _aes.Encrypt(NextNonce(), plaintext, output.AsSpan(0, plaintext.Length), output.AsSpan(plaintext.Length));
        }
        return output;
    }

    public byte[] Decrypt(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < TagSize) throw new ProtocolException("encrypted frame too short");
        var plaintext = new byte[frame.Length - TagSize];
        lock (_lock)
        {
            try
            {
                _aes.Decrypt(NextNonce(), frame[..plaintext.Length], frame[plaintext.Length..], plaintext);
            }
            catch (AuthenticationTagMismatchException e)
            {
                throw new ProtocolException("frame authentication failed", e);
            }
        }
        return plaintext;
    }

    private byte[] NextNonce()
    {
        if (_counter == ulong.MaxValue) throw new ProtocolException("nonce counter exhausted");
        var nonce = new byte[NonceSize];
        BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), _counter++);
        return nonce;
    }

    public void Dispose() => _aes.Dispose();
}
