using System.Net;
using System.Net.Sockets;
using Handsfreely.Core.Pairing;
using Handsfreely.Core.Protocol;

namespace Handsfreely.Core.Tests;

internal static class Loopback
{
    public static async Task<(Stream Pc, Stream Phone)> CreateAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var client = new TcpClient();
        var connect = client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        var server = await listener.AcceptTcpClientAsync();
        await connect;
        return (server.GetStream(), client.GetStream());
    }
}

internal sealed class FakeAuthority : IPairingAuthority
{
    public readonly List<byte[]> Trusted = [];
    public byte[]? Token;
    public HelloInfo? LastHello;

    public bool IsTrusted(byte[] key) => Trusted.Any(k => k.AsSpan().SequenceEqual(key));

    public byte[]? CurrentPairingToken() => Token;

    public bool CompletePairing(byte[] token, byte[] key, HelloInfo hello)
    {
        if (Token is null || !Token.AsSpan().SequenceEqual(token)) return false;
        Token = null;
        Trusted.Add(key);
        LastHello = hello;
        return true;
    }
}

internal static class Samples
{
    public static readonly HelloInfo Hello = new("Pixel", "Pixel 9", "1.0", 35);
    public static readonly WelcomeInfo Welcome = new("pc-2", "1.0");
}
