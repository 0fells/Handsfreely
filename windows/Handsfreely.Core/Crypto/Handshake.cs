using System.Security.Cryptography;
using System.Text;

namespace Handsfreely.Core.Crypto;

public enum HandshakeMode : byte
{
    Pair = 1,
    Resume = 2,
}

public enum HandshakeStatus : byte
{
    Ok = 0,
    UnknownDevice = 1,
    PairingClosed = 2,
    BadVersion = 3,
}

public sealed record SessionKeys(byte[] PhoneToPc, byte[] PcToPhone);

public static class Handshake
{
    public const byte Version = 1;
    public const int TokenSize = 16;

    private const int KeySize = P256KeyPair.PublicKeySize;
    private const int M1Size = 3 + 1 + KeySize + KeySize;
    private static readonly byte[] ProtocolName = Encoding.ASCII.GetBytes("Handsfreely/1");
    private static readonly byte[] KeysInfo = Encoding.ASCII.GetBytes("Handsfreely/1 keys");

    public static byte[] BuildM1(HandshakeMode mode, ReadOnlySpan<byte> phoneStatic, ReadOnlySpan<byte> phoneEph)
    {
        var m = new byte[M1Size];
        WriteMagic(m);
        m[3] = (byte)mode;
        phoneStatic.CopyTo(m.AsSpan(4));
        phoneEph.CopyTo(m.AsSpan(4 + KeySize));
        return m;
    }

    public static (HandshakeMode Mode, byte[] PhoneStatic, byte[] PhoneEph) ParseM1(ReadOnlySpan<byte> m)
    {
        if (m.Length != M1Size) throw new ProtocolException("bad M1 size");
        var mode = (HandshakeMode)m[3];
        if (mode is not (HandshakeMode.Pair or HandshakeMode.Resume))
            throw new ProtocolException("bad handshake mode");
        var phoneStatic = m.Slice(4, KeySize).ToArray();
        var phoneEph = m.Slice(4 + KeySize, KeySize).ToArray();
        P256KeyPair.ValidatePublicKey(phoneStatic);
        P256KeyPair.ValidatePublicKey(phoneEph);
        return (mode, phoneStatic, phoneEph);
    }

    public static bool CheckVersion(ReadOnlySpan<byte> m) =>
        m.Length >= 3 && m[0] == (byte)'H' && m[1] == (byte)'F' && m[2] == Version;

    public static byte[] BuildM2(HandshakeStatus status, ReadOnlySpan<byte> pcEph)
    {
        var m = new byte[status == HandshakeStatus.Ok ? 4 + KeySize : 4];
        WriteMagic(m);
        m[3] = (byte)status;
        if (status == HandshakeStatus.Ok) pcEph.CopyTo(m.AsSpan(4));
        return m;
    }

    public static (HandshakeStatus Status, byte[]? PcEph) ParseM2(ReadOnlySpan<byte> m)
    {
        if (!CheckVersion(m) || m.Length < 4) throw new ProtocolException("bad M2");
        var status = (HandshakeStatus)m[3];
        if (status != HandshakeStatus.Ok)
            return (status, null);
        if (m.Length != 4 + KeySize) throw new ProtocolException("bad M2 size");
        var pcEph = m.Slice(4, KeySize).ToArray();
        P256KeyPair.ValidatePublicKey(pcEph);
        return (status, pcEph);
    }

    public static SessionKeys DeriveKeys(
        ReadOnlySpan<byte> pcStatic, ReadOnlySpan<byte> m1, ReadOnlySpan<byte> m2,
        ReadOnlySpan<byte> dhEphEph, ReadOnlySpan<byte> dhPhoneEphPcStatic, ReadOnlySpan<byte> dhPhoneStaticPcEph,
        ReadOnlySpan<byte> token)
    {
        var h = SHA256.HashData([.. ProtocolName, .. pcStatic, .. m1, .. m2]);
        byte[] ikm = [.. dhEphEph, .. dhPhoneEphPcStatic, .. dhPhoneStaticPcEph, .. token];
        var okm = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 64, h, KeysInfo);
        CryptographicOperations.ZeroMemory(ikm);
        return new SessionKeys(okm[..32], okm[32..]);
    }

    private static void WriteMagic(Span<byte> m)
    {
        m[0] = (byte)'H';
        m[1] = (byte)'F';
        m[2] = Version;
    }
}
