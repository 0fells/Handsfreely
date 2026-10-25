using System.Buffers.Binary;
using System.Text;
using Handsfreely.Core.Map;

namespace Handsfreely.Core.Tests;

public class MapTests
{
    private const string Listing = """
        <?xml version='1.0' encoding='UTF-8' standalone='yes' ?>
        <MAP-msg-listing version="1.0">
          <msg handle="00400000000005BA" subject="Hello there" datetime="20261025T090222" sender_name="Alex" sender_addressing="+15550100" recipient_name="" recipient_addressing="" type="SMS_GSM" size="11" text="yes" reception_status="complete" attachment_size="0" priority="no" read="no" sent="no" protected="no" />
          <msg handle="00400000000005B9" subject="Code 1234" datetime="20261025T045007+0000" sender_addressing="Bank" type="SMS_GSM" size="9" read="yes" />
        </MAP-msg-listing>
        """;

    [Fact]
    public void ParsesListing()
    {
        var list = MapClient.ParseListing(Encoding.UTF8.GetBytes(Listing), MapFolder.Inbox);
        Assert.Equal(2, list.Count);
        Assert.Equal("00400000000005BA", list[0].Handle);
        Assert.Equal("+15550100", list[0].Address);
        Assert.Equal("Alex", list[0].Name);
        Assert.False(list[0].Read);
        Assert.Equal(new DateTime(2026, 10, 25, 9, 2, 22), list[0].Date);
        Assert.Null(list[1].Name);
        Assert.True(list[1].Read);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 4, 50, 7, TimeSpan.Zero).LocalDateTime, list[1].Date);
    }

    [Fact]
    public void BMessageRoundTrip()
    {
        var text = "Привет!\nВторая строка";
        var bmsg = BMessage.Build("+15550100", text);
        Assert.Contains("TEL:+15550100\r\n", bmsg);
        var content = "BEGIN:MSG\r\nПривет!\r\nВторая строка\r\nEND:MSG\r\n";
        Assert.Contains($"LENGTH:{Encoding.UTF8.GetByteCount(content)}\r\n", bmsg);
        Assert.Equal(text, BMessage.ParseBody(bmsg));
    }

    [Fact]
    public void ParsesSplitBody()
    {
        var bmsg = "BEGIN:BMSG\r\nBEGIN:BBODY\r\nBEGIN:MSG\r\nfirst part \r\nEND:MSG\r\nBEGIN:MSG\r\nsecond\r\nEND:MSG\r\nEND:BBODY\r\nEND:BMSG\r\n";
        Assert.Equal("first part second", BMessage.ParseBody(bmsg));
    }

    [Fact]
    public async Task GetCollectsMultiPartBodyAndPutSplitsBody()
    {
        var (client, server) = await Loopback.CreateAsync();
        var fake = Task.Run(() => FakeMasAsync(server));

        async Task<T> Guard<T>(Task<T> step)
        {
            var done = await Task.WhenAny(step, fake).WaitAsync(TimeSpan.FromSeconds(10));
            if (done == fake && !step.IsCompleted)
            {
                await fake;
                throw new InvalidOperationException("fake server finished early");
            }
            return await step;
        }

        var map = await Guard(MapClient.ConnectAsync(client));
        var list = await Guard(map.ListAsync(MapFolder.Inbox, 10));
        Assert.Equal(2, list.Count);

        await Guard(map.SendAsync("+15550100", new string('x', 3000)).ContinueWith(t => { t.GetAwaiter().GetResult(); return true; }));
        await map.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        var pushed = await fake.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("TEL:+15550100", pushed);
        Assert.Contains(new string('x', 3000), pushed);
    }

    private static async Task<string> FakeMasAsync(Stream s)
    {
        async Task<(byte Op, byte[] Data)> ReadAsync()
        {
            var head = new byte[3];
            await s.ReadExactlyAsync(head);
            var data = new byte[BinaryPrimitives.ReadUInt16BigEndian(head.AsSpan(1)) - 3];
            await s.ReadExactlyAsync(data);
            return (head[0], data);
        }
        async Task ReplyAsync(byte code, byte[] payload)
        {
            var p = new byte[3 + payload.Length];
            p[0] = code;
            BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(1), (ushort)p.Length);
            payload.CopyTo(p, 3);
            await s.WriteAsync(p);
        }

        var (op, _) = await ReadAsync();
        Assert.Equal(0x80, op);
        await ReplyAsync(0xA0, [0x10, 0x00, 0x04, 0x00, .. ObexHeader.UInt32(ObexHeader.ConnectionId, 7)]);
        for (int i = 0; i < 3; i++)
        {
            (op, _) = await ReadAsync();
            Assert.Equal(0x85, op);
            await ReplyAsync(0xA0, []);
        }

        var xml = Encoding.UTF8.GetBytes(Listing);
        var chunks = xml.Chunk(xml.Length / 3 + 1).ToList();
        for (int i = 0; i < chunks.Count; i++)
        {
            (op, _) = await ReadAsync();
            Assert.Equal(0x83, op);
            bool last = i == chunks.Count - 1;
            await ReplyAsync(last ? (byte)0xA0 : (byte)0x90, ObexHeader.Bytes(last ? ObexHeader.EndOfBody : ObexHeader.Body, chunks[i]));
        }

        var body = new MemoryStream();
        while (true)
        {
            (op, var data) = await ReadAsync();
            Assert.True(data.Length + 3 <= 0x0400, "packet exceeds peer max size");
            foreach (var (id, value) in ObexHeader.Parse(data))
                if (id is ObexHeader.Body or ObexHeader.EndOfBody) body.Write(value);
            if (op == 0x82)
            {
                await ReplyAsync(0xA0, []);
                break;
            }
            Assert.Equal(0x02, op);
            await ReplyAsync(0x90, []);
        }

        (op, _) = await ReadAsync();
        Assert.Equal(0x81, op);
        await ReplyAsync(0xA0, []);
        return Encoding.UTF8.GetString(body.ToArray());
    }
}
