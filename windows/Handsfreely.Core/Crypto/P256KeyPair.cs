using System.Security.Cryptography;

namespace Handsfreely.Core.Crypto;

public sealed class P256KeyPair : IDisposable
{
    public const int PublicKeySize = 65;
    public const int PrivateKeySize = 32;

    private readonly ECDiffieHellman _ecdh;

    public byte[] PublicKey { get; }

    private P256KeyPair(ECDiffieHellman ecdh)
    {
        _ecdh = ecdh;
        PublicKey = EncodePublic(ecdh.ExportParameters(false).Q);
    }

    public static P256KeyPair Generate() =>
        new(ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256));

    public static P256KeyPair FromPrivateKey(ReadOnlySpan<byte> d)
    {
        if (d.Length != PrivateKeySize)
            throw new ArgumentException("P-256 private key must be 32 bytes", nameof(d));
        var ecdh = ECDiffieHellman.Create();
        ecdh.ImportParameters(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = d.ToArray() });
        return new P256KeyPair(ecdh);
    }

    public byte[] ExportPrivateKey() => _ecdh.ExportParameters(true).D!;

    public byte[] DeriveSharedSecret(ReadOnlySpan<byte> otherPublicKey)
    {
        using var other = ImportPublic(otherPublicKey);
        return _ecdh.DeriveRawSecretAgreement(other.PublicKey);
    }

    public static void ValidatePublicKey(ReadOnlySpan<byte> key)
    {
        using var _ = ImportPublic(key);
    }

    private static ECDiffieHellman ImportPublic(ReadOnlySpan<byte> key)
    {
        if (key.Length != PublicKeySize || key[0] != 0x04)
            throw new ProtocolException("bad public key encoding");
        var ecdh = ECDiffieHellman.Create();
        try
        {
            ecdh.ImportParameters(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = key.Slice(1, 32).ToArray(), Y = key.Slice(33, 32).ToArray() },
            });
        }
        catch (Exception e) when (e is CryptographicException or PlatformNotSupportedException)
        {
            ecdh.Dispose();
            throw new ProtocolException("public key is not on P-256", e);
        }
        return ecdh;
    }

    private static byte[] EncodePublic(ECPoint q)
    {
        var key = new byte[PublicKeySize];
        key[0] = 0x04;
        q.X!.CopyTo(key, 1);
        q.Y!.CopyTo(key, 33);
        return key;
    }

    public void Dispose() => _ecdh.Dispose();
}
