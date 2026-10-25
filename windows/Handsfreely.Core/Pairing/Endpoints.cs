using System.Text.Json;
using Handsfreely.Core.Crypto;
using Handsfreely.Core.Protocol;
using Handsfreely.Core.Transport;

namespace Handsfreely.Core.Pairing;

public interface IPairingAuthority
{
    bool IsTrusted(byte[] phonePublicKey);

    byte[]? CurrentPairingToken();

    bool CompletePairing(byte[] token, byte[] phonePublicKey, HelloInfo hello);
}

public sealed class HandshakeRejectedException(HandshakeStatus status)
    : ProtocolException($"handshake rejected: {status}")
{
    public HandshakeStatus Status { get; } = status;
}

public sealed record AcceptedPhone(SecureChannel Channel, byte[] PhonePublicKey, HelloInfo Hello, bool NewlyPaired);

public static class PcEndpoint
{
    public static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(20);

    public static async Task<AcceptedPhone> AcceptAsync(
        Stream stream, P256KeyPair pcKey, IPairingAuthority authority, WelcomeInfo welcome, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(HandshakeTimeout);
        var frames = new FrameStream(stream);
        try
        {
            var m1 = await frames.ReadAsync(timeout.Token).ConfigureAwait(false)
                ?? throw new EndOfStreamException("closed before M1");
            if (!Handshake.CheckVersion(m1))
            {
                await frames.WriteAsync(Handshake.BuildM2(HandshakeStatus.BadVersion, default), timeout.Token).ConfigureAwait(false);
                throw new HandshakeRejectedException(HandshakeStatus.BadVersion);
            }
            var (mode, phoneStatic, phoneEph) = Handshake.ParseM1(m1);

            byte[] token = [];
            if (mode == HandshakeMode.Resume && !authority.IsTrusted(phoneStatic))
            {
                await frames.WriteAsync(Handshake.BuildM2(HandshakeStatus.UnknownDevice, default), timeout.Token).ConfigureAwait(false);
                throw new HandshakeRejectedException(HandshakeStatus.UnknownDevice);
            }
            if (mode == HandshakeMode.Pair)
            {
                var current = authority.CurrentPairingToken();
                if (current is null)
                {
                    await frames.WriteAsync(Handshake.BuildM2(HandshakeStatus.PairingClosed, default), timeout.Token).ConfigureAwait(false);
                    throw new HandshakeRejectedException(HandshakeStatus.PairingClosed);
                }
                token = current;
            }

            using var eph = P256KeyPair.Generate();
            var m2 = Handshake.BuildM2(HandshakeStatus.Ok, eph.PublicKey);
            await frames.WriteAsync(m2, timeout.Token).ConfigureAwait(false);

            var keys = Handshake.DeriveKeys(
                pcKey.PublicKey, m1, m2,
                eph.DeriveSharedSecret(phoneEph),
                pcKey.DeriveSharedSecret(phoneEph),
                eph.DeriveSharedSecret(phoneStatic),
                token);
            var channel = new SecureChannel(frames, sendKey: keys.PcToPhone, receiveKey: keys.PhoneToPc);

            var helloFrame = await channel.ReceiveAsync(timeout.Token).ConfigureAwait(false)
                ?? throw new EndOfStreamException("closed before hello");
            var hello = Message.Parse(helloFrame);
            if (hello.Type != MessageTypes.Hello) throw new ProtocolException("expected hello");
            var info = hello.Data.Deserialize<HelloInfo>(Json.Options) ?? throw new ProtocolException("empty hello");

            if (mode == HandshakeMode.Pair && !authority.CompletePairing(token, phoneStatic, info))
                throw new ProtocolException("pairing token already used");

            await channel.SendAsync(new Message(MessageTypes.Welcome, null, RpcPeer.ToNode(welcome)).Serialize(), timeout.Token).ConfigureAwait(false);
            return new AcceptedPhone(channel, phoneStatic, info, mode == HandshakeMode.Pair);
        }
        catch
        {
            await frames.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}

public static class PhoneEndpoint
{
    public static async Task<(SecureChannel Channel, WelcomeInfo Welcome)> ConnectAsync(
        Stream stream, P256KeyPair phoneKey, byte[] pcPublicKey, HandshakeMode mode, byte[]? token, HelloInfo hello,
        CancellationToken ct = default)
    {
        var frames = new FrameStream(stream);
        try
        {
            using var eph = P256KeyPair.Generate();
            var m1 = Handshake.BuildM1(mode, phoneKey.PublicKey, eph.PublicKey);
            await frames.WriteAsync(m1, ct).ConfigureAwait(false);
            var m2 = await frames.ReadAsync(ct).ConfigureAwait(false) ?? throw new EndOfStreamException("closed before M2");
            var (status, pcEph) = Handshake.ParseM2(m2);
            if (status != HandshakeStatus.Ok) throw new HandshakeRejectedException(status);

            var keys = Handshake.DeriveKeys(
                pcPublicKey, m1, m2,
                eph.DeriveSharedSecret(pcEph!),
                eph.DeriveSharedSecret(pcPublicKey),
                phoneKey.DeriveSharedSecret(pcEph!),
                mode == HandshakeMode.Pair ? token : []);
            var channel = new SecureChannel(frames, sendKey: keys.PhoneToPc, receiveKey: keys.PcToPhone);
            await channel.SendAsync(new Message(MessageTypes.Hello, null, RpcPeer.ToNode(hello)).Serialize(), ct).ConfigureAwait(false);

            var reply = await channel.ReceiveAsync(ct).ConfigureAwait(false) ?? throw new EndOfStreamException("closed before welcome");
            var welcome = Message.Parse(reply);
            if (welcome.Type != MessageTypes.Welcome) throw new ProtocolException("expected welcome");
            return (channel, welcome.Data.Deserialize<WelcomeInfo>(Json.Options) ?? throw new ProtocolException("empty welcome"));
        }
        catch
        {
            await frames.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
