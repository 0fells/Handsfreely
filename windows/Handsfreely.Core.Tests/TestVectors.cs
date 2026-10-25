using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Handsfreely.Core.Crypto;

namespace Handsfreely.Core.Tests;

public class TestVectors
{
    private static byte[] Scalar(string seed) => SHA256.HashData(Encoding.ASCII.GetBytes(seed));

    private static JsonObject Build()
    {
        using var pcStatic = P256KeyPair.FromPrivateKey(Scalar("pc static"));
        using var pcEph = P256KeyPair.FromPrivateKey(Scalar("pc eph"));
        using var phoneStatic = P256KeyPair.FromPrivateKey(Scalar("phone static"));
        using var phoneEph = P256KeyPair.FromPrivateKey(Scalar("phone eph"));
        var token = SHA256.HashData("token"u8)[..Handshake.TokenSize];

        var m1 = Handshake.BuildM1(HandshakeMode.Pair, phoneStatic.PublicKey, phoneEph.PublicKey);
        var m2 = Handshake.BuildM2(HandshakeStatus.Ok, pcEph.PublicKey);
        var keys = Handshake.DeriveKeys(
            pcStatic.PublicKey, m1, m2,
            phoneEph.DeriveSharedSecret(pcEph.PublicKey),
            phoneEph.DeriveSharedSecret(pcStatic.PublicKey),
            phoneStatic.DeriveSharedSecret(pcEph.PublicKey),
            token);

        var plaintext = "{\"t\":\"hello\"}"u8.ToArray();
        using var up = new FrameCipher(keys.PhoneToPc);
        var frame0 = up.Encrypt(plaintext);
        var frame1 = up.Encrypt(plaintext);

        return new JsonObject
        {
            ["pcStaticPrivate"] = Hex(pcStatic.ExportPrivateKey()),
            ["pcEphPrivate"] = Hex(pcEph.ExportPrivateKey()),
            ["phoneStaticPrivate"] = Hex(phoneStatic.ExportPrivateKey()),
            ["phoneEphPrivate"] = Hex(phoneEph.ExportPrivateKey()),
            ["pcStaticPublic"] = Hex(pcStatic.PublicKey),
            ["phoneStaticPublic"] = Hex(phoneStatic.PublicKey),
            ["token"] = Hex(token),
            ["m1"] = Hex(m1),
            ["m2"] = Hex(m2),
            ["keyPhoneToPc"] = Hex(keys.PhoneToPc),
            ["keyPcToPhone"] = Hex(keys.PcToPhone),
            ["plaintext"] = Hex(plaintext),
            ["frame0"] = Hex(frame0),
            ["frame1"] = Hex(frame1),
        };
    }

    private static string Hex(byte[] b) => Convert.ToHexStringLower(b);

    private static string VectorsPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "docs"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "docs", "test-vectors.json");
    }

    [Fact]
    public void MatchesCommittedVectors()
    {
        var path = VectorsPath();
        var json = Build().ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n";
        if (Environment.GetEnvironmentVariable("HANDSFREELY_WRITE_VECTORS") == "1" || !File.Exists(path))
        {
            File.WriteAllText(path, json);
            return;
        }
        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), json);
    }
}
