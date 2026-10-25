using System.Buffers.Binary;
using System.Text;

namespace Handsfreely.Core.Map;

public static class ObexHeader
{
    public const byte Name = 0x01;
    public const byte Type = 0x42;
    public const byte Target = 0x46;
    public const byte Who = 0x4A;
    public const byte Body = 0x48;
    public const byte EndOfBody = 0x49;
    public const byte AppParameters = 0x4C;
    public const byte ConnectionId = 0xCB;

    public static byte[] Unicode(byte id, string value)
    {
        var text = value.Length == 0 ? [] : Encoding.BigEndianUnicode.GetBytes(value + "\0");
        return Bytes(id, text);
    }

    public static byte[] Ascii(byte id, string value) => Bytes(id, Encoding.ASCII.GetBytes(value + "\0"));

    public static byte[] Bytes(byte id, ReadOnlySpan<byte> value)
    {
        var h = new byte[3 + value.Length];
        h[0] = id;
        BinaryPrimitives.WriteUInt16BigEndian(h.AsSpan(1), (ushort)h.Length);
        value.CopyTo(h.AsSpan(3));
        return h;
    }

    public static byte[] UInt32(byte id, uint value)
    {
        var h = new byte[5];
        h[0] = id;
        BinaryPrimitives.WriteUInt32BigEndian(h.AsSpan(1), value);
        return h;
    }

    public static List<(byte Id, byte[] Value)> Parse(ReadOnlySpan<byte> data)
    {
        var list = new List<(byte, byte[])>();
        int i = 0;
        while (i < data.Length)
        {
            byte id = data[i];
            int length = (id & 0xC0) switch
            {
                0xC0 => 5,
                0x80 => 2,
                _ => i + 3 <= data.Length ? BinaryPrimitives.ReadUInt16BigEndian(data[(i + 1)..]) : -1,
            };
            if (length < 3 && (id & 0x80) == 0 || length < 0 || i + length > data.Length)
                throw new ProtocolException("bad OBEX header");
            int offset = (id & 0x80) == 0 ? 3 : 1;
            list.Add((id, data.Slice(i + offset, length - offset).ToArray()));
            i += length;
        }
        return list;
    }
}

public sealed class AppParams
{
    private readonly List<byte> _data = [];

    public AppParams Byte(byte tag, byte value)
    {
        _data.AddRange([tag, 1, value]);
        return this;
    }

    public AppParams UInt16(byte tag, ushort value)
    {
        _data.AddRange([tag, 2, (byte)(value >> 8), (byte)value]);
        return this;
    }

    public byte[] ToArray() => [.. _data];
}

public sealed class ObexException(byte code, string operation) : Exception($"OBEX {operation} failed: 0x{code:X2}")
{
    public byte Code { get; } = code;
    public bool Forbidden => Code is 0xC1 or 0xC3;
}

public sealed class ObexClient : IAsyncDisposable
{
    private const byte OpConnect = 0x80;
    private const byte OpDisconnect = 0x81;
    private const byte OpPut = 0x02;
    private const byte OpPutFinal = 0x82;
    private const byte OpGetFinal = 0x83;
    private const byte OpSetPath = 0x85;
    private const byte Continue = 0x90;
    private const byte Success = 0xA0;
    private const ushort OurMaxPacket = 0xFFFE;

    private readonly Stream _stream;
    private uint? _connectionId;
    private int _peerMaxPacket = 255;

    public ObexClient(Stream stream) => _stream = stream;

    public async Task ConnectAsync(byte[] target, CancellationToken ct = default)
    {
        byte[] prefix = [0x10, 0x00, OurMaxPacket >> 8, OurMaxPacket & 0xFF];
        var (code, data) = await RequestAsync(OpConnect, prefix, [ObexHeader.Bytes(ObexHeader.Target, target)], ct);
        if (code != Success) throw new ObexException(code, "connect");
        if (data.Length < 4) throw new ProtocolException("bad OBEX connect response");
        _peerMaxPacket = Math.Max(255, (int)BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(2)));
        foreach (var (id, value) in ObexHeader.Parse(data.AsSpan(4)))
            if (id == ObexHeader.ConnectionId) _connectionId = BinaryPrimitives.ReadUInt32BigEndian(value);
    }

    public async Task SetPathAsync(string name, CancellationToken ct = default)
    {
        byte flags = name == ".." ? (byte)0x03 : (byte)0x02;
        var headers = WithConnection(name == ".." ? [] : [ObexHeader.Unicode(ObexHeader.Name, name)]);
        var (code, _) = await RequestAsync(OpSetPath, [flags, 0x00], headers, ct);
        if (code != Success) throw new ObexException(code, $"setpath {name}");
    }

    public async Task<byte[]> GetAsync(string name, string type, byte[]? appParams, CancellationToken ct = default)
    {
        var headers = new List<byte[]>
        {
            ObexHeader.Unicode(ObexHeader.Name, name),
            ObexHeader.Ascii(ObexHeader.Type, type),
        };
        if (appParams is { Length: > 0 }) headers.Add(ObexHeader.Bytes(ObexHeader.AppParameters, appParams));

        using var body = new MemoryStream();
        var (code, data) = await RequestAsync(OpGetFinal, [], WithConnection(headers), ct);
        while (true)
        {
            foreach (var (id, value) in ObexHeader.Parse(data))
                if (id is ObexHeader.Body or ObexHeader.EndOfBody) body.Write(value);
            if (code == Success) return body.ToArray();
            if (code != Continue) throw new ObexException(code, $"get {type}");
            (code, data) = await RequestAsync(OpGetFinal, [], WithConnection([]), ct);
        }
    }

    public async Task PutAsync(string name, string type, byte[]? appParams, byte[] body, CancellationToken ct = default)
    {
        var first = new List<byte[]>
        {
            ObexHeader.Unicode(ObexHeader.Name, name),
            ObexHeader.Ascii(ObexHeader.Type, type),
        };
        if (appParams is { Length: > 0 }) first.Add(ObexHeader.Bytes(ObexHeader.AppParameters, appParams));
        first = WithConnection(first);

        int offset = 0;
        bool firstPacket = true;
        while (true)
        {
            int overhead = 3 + (firstPacket ? first.Sum(h => h.Length) : 5) + 3;
            int chunk = Math.Min(body.Length - offset, _peerMaxPacket - overhead);
            if (chunk < 0) throw new ProtocolException("OBEX headers do not fit into a packet");
            bool last = offset + chunk >= body.Length;
            var headers = firstPacket ? new List<byte[]>(first) : WithConnection([]);
            headers.Add(ObexHeader.Bytes(last ? ObexHeader.EndOfBody : ObexHeader.Body, body.AsSpan(offset, chunk)));
            var (code, _) = await RequestAsync(last ? OpPutFinal : OpPut, [], headers, ct);
            if (last)
            {
                if (code != Success) throw new ObexException(code, $"put {type}");
                return;
            }
            if (code != Continue) throw new ObexException(code, $"put {type}");
            offset += chunk;
            firstPacket = false;
        }
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        try
        {
            await RequestAsync(OpDisconnect, [], WithConnection([]), ct);
        }
        catch (Exception e) when (e is IOException or ProtocolException or ObjectDisposedException)
        {
        }
    }

    private List<byte[]> WithConnection(List<byte[]> headers)
    {
        if (_connectionId is { } id) headers.Insert(0, ObexHeader.UInt32(ObexHeader.ConnectionId, id));
        return headers;
    }

    private async Task<(byte Code, byte[] Data)> RequestAsync(byte opcode, byte[] prefix, List<byte[]> headers, CancellationToken ct)
    {
        int length = 3 + prefix.Length + headers.Sum(h => h.Length);
        var packet = new byte[length];
        packet[0] = opcode;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(1), (ushort)length);
        prefix.CopyTo(packet, 3);
        int pos = 3 + prefix.Length;
        foreach (var h in headers)
        {
            h.CopyTo(packet, pos);
            pos += h.Length;
        }
        await _stream.WriteAsync(packet, ct).ConfigureAwait(false);
        await _stream.FlushAsync(ct).ConfigureAwait(false);

        var head = new byte[3];
        await _stream.ReadExactlyAsync(head, ct).ConfigureAwait(false);
        int responseLength = BinaryPrimitives.ReadUInt16BigEndian(head.AsSpan(1));
        if (responseLength < 3) throw new ProtocolException("bad OBEX response length");
        var data = new byte[responseLength - 3];
        await _stream.ReadExactlyAsync(data, ct).ConfigureAwait(false);
        return ((byte)(head[0] | 0x80), data);
    }

    public ValueTask DisposeAsync() => _stream.DisposeAsync();
}
