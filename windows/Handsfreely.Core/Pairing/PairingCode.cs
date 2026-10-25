using System.Text.RegularExpressions;
using Handsfreely.Core.Crypto;

namespace Handsfreely.Core.Pairing;

public sealed partial record PairingCode(string BluetoothAddress, byte[] PcPublicKey, byte[] Token, string PcName)
{
    private const string Prefix = "handsfreely:1?";

    public string ToUri() =>
        $"{Prefix}a={BluetoothAddress}&k={Base64Url.Encode(PcPublicKey)}&t={Base64Url.Encode(Token)}&n={Uri.EscapeDataString(PcName)}";

    public static bool TryParse(string text, out PairingCode? code)
    {
        code = null;
        if (!text.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        var fields = new Dictionary<string, string>();
        foreach (var part in text[Prefix.Length..].Split('&'))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) return false;
            fields[part[..eq]] = part[(eq + 1)..];
        }
        if (!fields.TryGetValue("a", out var mac) || !MacRegex().IsMatch(mac)) return false;
        if (!fields.TryGetValue("k", out var k) || !Base64Url.TryDecode(k, out var key)) return false;
        if (!fields.TryGetValue("t", out var t) || !Base64Url.TryDecode(t, out var token)) return false;
        if (key.Length != P256KeyPair.PublicKeySize || token.Length != Handshake.TokenSize) return false;
        var name = fields.TryGetValue("n", out var n) ? Uri.UnescapeDataString(n) : "";
        code = new PairingCode(mac.ToUpperInvariant(), key, token, name);
        return true;
    }

    [GeneratedRegex("^[0-9A-Fa-f]{12}$")]
    private static partial Regex MacRegex();
}

public static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool TryDecode(string text, out byte[] data)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        try
        {
            data = Convert.FromBase64String(s);
            return true;
        }
        catch (FormatException)
        {
            data = [];
            return false;
        }
    }
}
