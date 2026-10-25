using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Handsfreely.Core.Crypto;
using Handsfreely.Core.Pairing;
using Handsfreely.Core.Protocol;
using Handsfreely.Core.Transport;

namespace Handsfreely.Core.Tests;

public class ChannelTests
{
    [Fact]
    public void TamperedFrameFails()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        using var send = new FrameCipher(key);
        using var receive = new FrameCipher(key);
        var frame = send.Encrypt("hello"u8);
        frame[0] ^= 1;
        Assert.Throws<ProtocolException>(() => receive.Decrypt(frame));
    }

    [Fact]
    public void ReplayedFrameFails()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        using var send = new FrameCipher(key);
        using var receive = new FrameCipher(key);
        var frame = send.Encrypt("one"u8);
        receive.Decrypt(frame);
        Assert.Throws<ProtocolException>(() => receive.Decrypt(frame));
    }

    [Fact]
    public async Task OversizedFrameIsRejected()
    {
        var (a, b) = await Loopback.CreateAsync();
        await a.WriteAsync(new byte[] { 0x7F, 0xFF, 0xFF, 0xFF });
        await using var frames = new FrameStream(b);
        await Assert.ThrowsAsync<ProtocolException>(() => frames.ReadAsync());
        await a.DisposeAsync();
    }

    [Fact]
    public void PairingCodeRoundTrip()
    {
        using var key = P256KeyPair.Generate();
        var code = new PairingCode("08BFB854BA57", key.PublicKey, RandomNumberGenerator.GetBytes(16), "Рабочий ПК & co");
        Assert.True(PairingCode.TryParse(code.ToUri(), out var parsed));
        Assert.Equal(code.BluetoothAddress, parsed!.BluetoothAddress);
        Assert.Equal(code.PcPublicKey, parsed.PcPublicKey);
        Assert.Equal(code.Token, parsed.Token);
        Assert.Equal(code.PcName, parsed.PcName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://example.com")]
    [InlineData("handsfreely:1?a=XYZ&k=AA&t=AA")]
    [InlineData("handsfreely:2?a=08BFB854BA57&k=AA&t=AA")]
    public void GarbageIsNotAPairingCode(string text) => Assert.False(PairingCode.TryParse(text, out _));

    [Fact]
    public void EnumsAreCamelCaseAndLenient()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new CallInfo("1", CallState.Ringing, "+100", null, CallDirection.In, 5), Json.Options);
        Assert.Contains("\"state\":\"ringing\"", json);
        Assert.Contains("\"direction\":\"in\"", json);
        Assert.DoesNotContain("name", json);
        var parsed = System.Text.Json.JsonSerializer.Deserialize<CallInfo>(json.Replace("ringing", "teleporting"), Json.Options);
        Assert.Equal(CallState.Disconnected, parsed!.State);
    }

    [Fact]
    public async Task RpcRequestsErrorsAndEvents()
    {
        var (pcStream, phoneStream) = await Loopback.CreateAsync();
        var keyUp = RandomNumberGenerator.GetBytes(32);
        var keyDown = RandomNumberGenerator.GetBytes(32);
        var pcPeer = new RpcPeer(new SecureChannel(new FrameStream(pcStream), keyDown, keyUp));
        var phonePeer = new RpcPeer(new SecureChannel(new FrameStream(phoneStream), keyUp, keyDown));

        phonePeer.RequestHandler = (type, data, ct) => type switch
        {
            MessageTypes.ContactsList => Task.FromResult<JsonNode?>(RpcPeer.ToNode(new
            {
                contacts = new[] { new Contact("1", "Alex", true, false, [new PhoneNumber("+15550100", PhoneNumberType.Mobile)]) },
            })),
            MessageTypes.CallDial when data?["number"]?.GetValue<string>() == "" =>
                throw new RpcException(ErrorCodes.BadRequest, "empty number"),
            MessageTypes.CallDial => Task.FromResult<JsonNode?>(null),
            _ => throw new RpcException(ErrorCodes.UnknownType, type),
        };

        await using var client = new PhoneClient(pcPeer);
        var calls = new TaskCompletionSource<IReadOnlyList<CallInfo>>();
        client.CallStateChanged += c => calls.TrySetResult(c);
        client.Start();
        phonePeer.Start();

        var contacts = await client.ListContactsAsync();
        Assert.Equal("Alex", Assert.Single(contacts).Name);
        Assert.Equal(PhoneNumberType.Mobile, contacts[0].Phones[0].Type);

        await client.DialAsync("+15550100");
        var e = await Assert.ThrowsAsync<RpcException>(() => client.DialAsync(""));
        Assert.Equal(ErrorCodes.BadRequest, e.Code);
        var unknown = await Assert.ThrowsAsync<RpcException>(() => client.HoldAsync(true));
        Assert.Equal(ErrorCodes.UnknownType, unknown.Code);

        await phonePeer.SendEventAsync(MessageTypes.CallState, new
        {
            calls = new[] { new CallInfo("7", CallState.Ringing, "+15550100", "Alex", CallDirection.In, 1) },
        });
        var state = await calls.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(CallState.Ringing, Assert.Single(state).State);

        var closed = new TaskCompletionSource();
        client.Closed += _ => closed.TrySetResult();
        await phonePeer.DisposeAsync();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
