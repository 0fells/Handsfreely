using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace Handsfreely.Core.Map;

public enum MapFolder { Inbox, Sent }

public sealed record MapMessageInfo(
    string Handle, MapFolder Folder, string Address, string? Name, string Subject, DateTime Date, int Size, bool Read);

public sealed class MapClient : IAsyncDisposable
{
    public static readonly Guid MasServiceUuid = new("00001132-0000-1000-8000-00805f9b34fb");

    private static readonly byte[] MasTarget = Convert.FromHexString("BB582B40420C11DBB0DE0800200C9A66");

    private const byte TagMaxListCount = 0x01;
    private const byte TagListStartOffset = 0x02;
    private const byte TagFilterMessageType = 0x03;
    private const byte TagAttachment = 0x0A;
    private const byte TagSubjectLength = 0x13;
    private const byte TagCharset = 0x14;
    private const byte TagStatusIndicator = 0x17;
    private const byte TagStatusValue = 0x18;

    private const byte OnlySms = 0x0C;
    private const byte CharsetUtf8 = 1;

    private readonly ObexClient _obex;

    private MapClient(ObexClient obex) => _obex = obex;

    public static async Task<MapClient> ConnectAsync(Stream stream, CancellationToken ct = default)
    {
        var obex = new ObexClient(stream);
        try
        {
            await obex.ConnectAsync(MasTarget, ct);
            await obex.SetPathAsync("", ct);
            await obex.SetPathAsync("telecom", ct);
            await obex.SetPathAsync("msg", ct);
            return new MapClient(obex);
        }
        catch
        {
            await obex.DisposeAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<MapMessageInfo>> ListAsync(MapFolder folder, int maxCount, int offset = 0, CancellationToken ct = default)
    {
        var p = new AppParams()
            .UInt16(TagMaxListCount, (ushort)maxCount)
            .UInt16(TagListStartOffset, (ushort)offset)
            .Byte(TagFilterMessageType, OnlySms)
            .Byte(TagSubjectLength, 255)
            .ToArray();
        var xml = await _obex.GetAsync(FolderName(folder), "x-bt/MAP-msg-listing", p, ct);
        return ParseListing(xml, folder);
    }

    public async Task<string> GetBodyAsync(string handle, CancellationToken ct = default)
    {
        var p = new AppParams().Byte(TagAttachment, 0).Byte(TagCharset, CharsetUtf8).ToArray();
        var data = await _obex.GetAsync(handle, "x-bt/message", p, ct);
        return BMessage.ParseBody(Encoding.UTF8.GetString(data));
    }

    public Task SendAsync(string to, string text, CancellationToken ct = default)
    {
        var p = new AppParams().Byte(TagCharset, CharsetUtf8).ToArray();
        return _obex.PutAsync("outbox", "x-bt/message", p, Encoding.UTF8.GetBytes(BMessage.Build(to, text)), ct);
    }

    public Task MarkReadAsync(string handle, CancellationToken ct = default)
    {
        var p = new AppParams().Byte(TagStatusIndicator, 0).Byte(TagStatusValue, 1).ToArray();
        return _obex.PutAsync(handle, "x-bt/messageStatus", p, [0x30], ct);
    }

    public static IReadOnlyList<MapMessageInfo> ParseListing(byte[] xml, MapFolder folder)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(Encoding.UTF8.GetString(xml).TrimStart('\uFEFF'));
        }
        catch (System.Xml.XmlException e)
        {
            throw new ProtocolException("bad MAP listing", e);
        }
        var list = new List<MapMessageInfo>();
        foreach (var m in doc.Root?.Elements("msg") ?? [])
        {
            string? A(string name) => (string?)m.Attribute(name);
            var handle = A("handle");
            if (handle is null) continue;
            var inbox = folder == MapFolder.Inbox;
            var address = (inbox ? A("sender_addressing") : A("recipient_addressing")) ?? "";
            var name = inbox ? A("sender_name") : A("recipient_name");
            list.Add(new MapMessageInfo(
                handle, folder, address, string.IsNullOrWhiteSpace(name) ? null : name,
                A("subject") ?? "",
                ParseDate(A("datetime")),
                int.TryParse(A("size"), out var size) ? size : 0,
                !inbox || A("read") == "yes"));
        }
        return list;
    }

    public static DateTime ParseDate(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < 15) return DateTime.MinValue;
        if (!DateTime.TryParseExact(value[..15], "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            return DateTime.MinValue;
        var zone = value[15..];
        if (zone == "Z") return DateTime.SpecifyKind(t, DateTimeKind.Utc).ToLocalTime();
        if (zone.Length == 5 && int.TryParse(zone[1..3], out var hh) && int.TryParse(zone[3..], out var mm))
        {
            var offset = new TimeSpan(hh, mm, 0) * (zone[0] == '-' ? -1 : 1);
            return new DateTimeOffset(t, offset).LocalDateTime;
        }
        return t;
    }

    private static string FolderName(MapFolder folder) => folder == MapFolder.Inbox ? "inbox" : "sent";

    public async ValueTask DisposeAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await _obex.DisconnectAsync(cts.Token);
        await _obex.DisposeAsync();
    }
}

public static class BMessage
{
    public static string Build(string to, string text)
    {
        text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
        var content = $"BEGIN:MSG\r\n{text}\r\nEND:MSG\r\n";
        var sb = new StringBuilder();
        sb.Append("BEGIN:BMSG\r\n");
        sb.Append("VERSION:1.0\r\n");
        sb.Append("STATUS:READ\r\n");
        sb.Append("TYPE:SMS_GSM\r\n");
        sb.Append("FOLDER:telecom/msg/outbox\r\n");
        sb.Append("BEGIN:BENV\r\n");
        sb.Append("BEGIN:VCARD\r\n");
        sb.Append("VERSION:2.1\r\n");
        sb.Append("N:\r\n");
        sb.Append($"TEL:{to}\r\n");
        sb.Append("END:VCARD\r\n");
        sb.Append("BEGIN:BBODY\r\n");
        sb.Append("CHARSET:UTF-8\r\n");
        sb.Append($"LENGTH:{Encoding.UTF8.GetByteCount(content)}\r\n");
        sb.Append(content);
        sb.Append("END:BBODY\r\n");
        sb.Append("END:BENV\r\n");
        sb.Append("END:BMSG\r\n");
        return sb.ToString();
    }

    public static string ParseBody(string bmsg)
    {
        var parts = new List<string>();
        int pos = 0;
        while (true)
        {
            int begin = bmsg.IndexOf("BEGIN:MSG", pos, StringComparison.Ordinal);
            if (begin < 0) break;
            int start = bmsg.IndexOf('\n', begin);
            if (start < 0) break;
            int end = bmsg.IndexOf("END:MSG", start, StringComparison.Ordinal);
            if (end < 0) break;
            parts.Add(bmsg[(start + 1)..end].TrimEnd('\r', '\n'));
            pos = end + 7;
        }
        return string.Join("", parts).Replace("\r\n", "\n");
    }
}
