using System.Security.Cryptography;
using Handsfreely.Core.Crypto;
using Handsfreely.Core.Pairing;
using Handsfreely.Core.Transport;

namespace Handsfreely.Core.Tests;

public class HandshakeTests
{
    private static async Task<(Task<AcceptedPhone> Pc, Task<(SecureChannel, Protocol.WelcomeInfo)> Phone)> RunAsync(
        P256KeyPair pcKey, FakeAuthority authority, P256KeyPair phoneKey, byte[] pcKeySeenByPhone, HandshakeMode mode, byte[]? token)
    {
        var (pcStream, phoneStream) = await Loopback.CreateAsync();
        var pc = PcEndpoint.AcceptAsync(pcStream, pcKey, authority, Samples.Welcome);
        var phone = PhoneEndpoint.ConnectAsync(phoneStream, phoneKey, pcKeySeenByPhone, mode, token, Samples.Hello);
        return (pc, phone);
    }

    [Fact]
    public async Task PairThenResumeAndExchangeData()
    {
        using var pcKey = P256KeyPair.Generate();
        using var phoneKey = P256KeyPair.Generate();
        var authority = new FakeAuthority { Token = RandomNumberGenerator.GetBytes(16) };

        var (pc, phone) = await RunAsync(pcKey, authority, phoneKey, pcKey.PublicKey, HandshakeMode.Pair, authority.Token);
        var accepted = await pc;
        var (phoneChannel, welcome) = await phone;

        Assert.True(accepted.NewlyPaired);
        Assert.Equal("Pixel", accepted.Hello.Name);
        Assert.Equal("pc-2", welcome.Name);
        Assert.Null(authority.Token);
        Assert.True(authority.IsTrusted(phoneKey.PublicKey));

        await phoneChannel.SendAsync("up"u8.ToArray());
        Assert.Equal("up"u8.ToArray(), await accepted.Channel.ReceiveAsync());
        await accepted.Channel.SendAsync("down"u8.ToArray());
        Assert.Equal("down"u8.ToArray(), await phoneChannel.ReceiveAsync());
        await phoneChannel.DisposeAsync();
        await accepted.Channel.DisposeAsync();

        (pc, phone) = await RunAsync(pcKey, authority, phoneKey, pcKey.PublicKey, HandshakeMode.Resume, null);
        var resumed = await pc;
        var (resumedPhone, _) = await phone;
        Assert.False(resumed.NewlyPaired);
        await resumed.Channel.DisposeAsync();
        await resumedPhone.DisposeAsync();
    }

    [Fact]
    public async Task WrongTokenIsRejected()
    {
        using var pcKey = P256KeyPair.Generate();
        using var phoneKey = P256KeyPair.Generate();
        var authority = new FakeAuthority { Token = RandomNumberGenerator.GetBytes(16) };

        var (pc, phone) = await RunAsync(pcKey, authority, phoneKey, pcKey.PublicKey, HandshakeMode.Pair, RandomNumberGenerator.GetBytes(16));

        await Assert.ThrowsAsync<ProtocolException>(() => pc);
        await Assert.ThrowsAnyAsync<Exception>(() => phone);
        Assert.Empty(authority.Trusted);
        Assert.NotNull(authority.Token);
    }

    [Fact]
    public async Task ImpersonatedPcIsRejected()
    {
        using var realPc = P256KeyPair.Generate();
        using var fakePc = P256KeyPair.Generate();
        using var phoneKey = P256KeyPair.Generate();
        var authority = new FakeAuthority { Token = RandomNumberGenerator.GetBytes(16) };

        var (pc, phone) = await RunAsync(fakePc, authority, phoneKey, realPc.PublicKey, HandshakeMode.Pair, authority.Token);

        await Assert.ThrowsAsync<ProtocolException>(() => pc);
        await Assert.ThrowsAnyAsync<Exception>(() => phone);
        Assert.Empty(authority.Trusted);
    }

    [Fact]
    public async Task UnknownPhoneCannotResume()
    {
        using var pcKey = P256KeyPair.Generate();
        using var phoneKey = P256KeyPair.Generate();
        var authority = new FakeAuthority();

        var (pc, phone) = await RunAsync(pcKey, authority, phoneKey, pcKey.PublicKey, HandshakeMode.Resume, null);

        var pcError = await Assert.ThrowsAsync<HandshakeRejectedException>(() => pc);
        var phoneError = await Assert.ThrowsAsync<HandshakeRejectedException>(() => phone);
        Assert.Equal(HandshakeStatus.UnknownDevice, pcError.Status);
        Assert.Equal(HandshakeStatus.UnknownDevice, phoneError.Status);
    }

    [Fact]
    public async Task PairingNeedsOpenWindow()
    {
        using var pcKey = P256KeyPair.Generate();
        using var phoneKey = P256KeyPair.Generate();
        var authority = new FakeAuthority();

        var (pc, phone) = await RunAsync(pcKey, authority, phoneKey, pcKey.PublicKey, HandshakeMode.Pair, RandomNumberGenerator.GetBytes(16));

        await Assert.ThrowsAsync<HandshakeRejectedException>(() => pc);
        var e = await Assert.ThrowsAsync<HandshakeRejectedException>(() => phone);
        Assert.Equal(HandshakeStatus.PairingClosed, e.Status);
    }

    [Fact]
    public void InvalidPointIsRejected()
    {
        var bad = new byte[P256KeyPair.PublicKeySize];
        bad[0] = 0x04;
        bad[1] = 1;
        Assert.Throws<ProtocolException>(() => P256KeyPair.ValidatePublicKey(bad));
    }

    [Fact]
    public void PrivateKeyRoundTrip()
    {
        using var a = P256KeyPair.Generate();
        using var b = P256KeyPair.FromPrivateKey(a.ExportPrivateKey());
        Assert.Equal(a.PublicKey, b.PublicKey);
    }
}
