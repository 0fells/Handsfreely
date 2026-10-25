using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Handsfreely.App.Infrastructure;
using Handsfreely.Core.Map;
using Handsfreely.Core.Protocol;
using Handsfreely.Core.Transport;

namespace Handsfreely.App.Services;

public static class DemoPhone
{
    public static async Task StartAsync(App app)
    {
        var data = Demo.Language == "ru" ? Russian() : English();
        var (pcStream, phoneStream) = InMemoryPair();
        var k1 = RandomNumberGenerator.GetBytes(32);
        var k2 = RandomNumberGenerator.GetBytes(32);
        var phonePeer = new RpcPeer(new SecureChannel(new FrameStream(phoneStream), k2, k1));
        phonePeer.RequestHandler = (type, request, _) => Task.FromResult(Answer(data, type, request));
        phonePeer.Start();

        var key = RandomNumberGenerator.GetBytes(65);
        var phone = new TrustedPhone
        {
            Id = TrustedPhone.IdFor(key),
            PublicKey = Convert.ToBase64String(key),
            Name = data.Info.Name,
            Model = data.Info.Model,
            PairedAt = DateTimeOffset.Now.AddDays(-12),
            LastSeen = DateTimeOffset.Now,
        };
        app.Devices.AddDemo(phone);
        app.Hfp.UseDemo();
        app.Sms.UseDemo(data.Sms);
        await app.Hub.AddDemoAsync(phone, new PhoneClient(new RpcPeer(new SecureChannel(new FrameStream(pcStream), k1, k2))));
    }

    private static JsonNode? Answer(Sample data, string type, JsonNode? request) => type switch
    {
        MessageTypes.DeviceInfo => RpcPeer.ToNode(data.Info),
        MessageTypes.ContactsList => RpcPeer.ToNode(new { contacts = data.Contacts }),
        MessageTypes.CallLogList => RpcPeer.ToNode(new { calls = request?["before"] is null ? data.Calls : [] }),
        _ => null,
    };

    private sealed record Sample(DeviceInfo Info, List<Contact> Contacts, List<CallLogEntry> Calls, List<MapMessageInfo> Sms);

    private static Sample Russian()
    {
        var people = new (string Name, string Number, bool Starred)[]
        {
            ("Анна Смирнова", "+7 900 555-01-12", true),
            ("Алексей Иванов", "+7 900 555-01-23", false),
            ("Мама", "+7 900 555-01-34", true),
            ("Дмитрий Козлов", "+7 900 555-01-45", false),
            ("Екатерина Новикова", "+7 900 555-01-56", false),
            ("Иван Петров", "+7 900 555-01-67", false),
            ("Ольга Соколова", "+7 900 555-01-78", false),
            ("Сергей Морозов", "+7 900 555-01-89", false),
            ("Татьяна Волкова", "+7 900 555-01-91", false),
            ("Михаил Лебедев", "+7 900 555-01-02", false),
            ("Служба доставки", "+7 800 555-01-05", false),
        };
        var sms = new (int Who, string Text, bool In, int MinutesAgo, bool Read)[]
        {
            (0, "Привет! Ты сегодня успеваешь к семи?", true, 52, true),
            (0, "Привет! Да, выезжаю в половине седьмого", false, 47, true),
            (0, "Отлично, займу столик у окна", true, 45, true),
            (0, "Договорились", false, 44, true),
            (0, "Возьми, пожалуйста, зарядку для ноутбука", true, 12, false),
            (0, "Если не сложно :)", true, 11, false),
            (2, "Позвони, когда освободишься", true, 130, true),
            (2, "Хорошо, вечером наберу", false, 125, true),
            (10, "Заказ 4821 передан курьеру. Доставка сегодня с 14:00 до 18:00", true, 300, true),
            (3, "Скинул документы на почту", false, 1500, true),
            (3, "Спасибо, посмотрю завтра утром", true, 1480, true),
            (6, "Спасибо за помощь с переездом!", true, 4300, true),
        };
        return Build(people, sms, "Оператор", [
            (0, CallLogType.In, 12, 245), (2, CallLogType.Out, 47, 612), (-1, CallLogType.Missed, 95, 0),
            (3, CallLogType.Out, 180, 95), (4, CallLogType.In, 300, 1310), (5, CallLogType.Missed, 1560, 0),
            (6, CallLogType.Out, 1620, 48), (10, CallLogType.In, 1700, 62), (7, CallLogType.Out, 2900, 380),
            (0, CallLogType.In, 3100, 120), (8, CallLogType.Rejected, 4400, 0), (9, CallLogType.Out, 5800, 230),
        ], "+7 900 555-01-47");
    }

    private static Sample English()
    {
        var people = new (string Name, string Number, bool Starred)[]
        {
            ("Anna Brooks", "+1 202-555-0112", true),
            ("Alex Carter", "+1 202-555-0123", false),
            ("Mom", "+1 202-555-0134", true),
            ("David Miller", "+1 202-555-0145", false),
            ("Emma Wilson", "+1 202-555-0156", false),
            ("James Taylor", "+1 202-555-0167", false),
            ("Olivia Moore", "+1 202-555-0178", false),
            ("Ryan Clark", "+1 202-555-0189", false),
            ("Sophie Turner", "+1 202-555-0191", false),
            ("Michael Hall", "+1 202-555-0102", false),
            ("Parcel delivery", "+1 800-555-0105", false),
        };
        var sms = new (int Who, string Text, bool In, int MinutesAgo, bool Read)[]
        {
            (0, "Hi! Are you still on for seven tonight?", true, 52, true),
            (0, "Hi! Yes, leaving at half past six", false, 47, true),
            (0, "Great, I'll grab a table by the window", true, 45, true),
            (0, "Deal", false, 44, true),
            (0, "Could you bring the laptop charger, please?", true, 12, false),
            (0, "If it's no trouble :)", true, 11, false),
            (2, "Call me when you're free", true, 130, true),
            (2, "Sure, I'll call tonight", false, 125, true),
            (10, "Order 4821 is out for delivery today between 2 and 6 PM", true, 300, true),
            (3, "Sent the documents to your email", false, 1500, true),
            (3, "Thanks, I'll look tomorrow morning", true, 1480, true),
            (6, "Thanks for helping with the move!", true, 4300, true),
        };
        return Build(people, sms, "Carrier", [
            (0, CallLogType.In, 12, 245), (2, CallLogType.Out, 47, 612), (-1, CallLogType.Missed, 95, 0),
            (3, CallLogType.Out, 180, 95), (4, CallLogType.In, 300, 1310), (5, CallLogType.Missed, 1560, 0),
            (6, CallLogType.Out, 1620, 48), (10, CallLogType.In, 1700, 62), (7, CallLogType.Out, 2900, 380),
            (0, CallLogType.In, 3100, 120), (8, CallLogType.Rejected, 4400, 0), (9, CallLogType.Out, 5800, 230),
        ], "+1 202-555-0147");
    }

    private static Sample Build(
        (string Name, string Number, bool Starred)[] people,
        (int Who, string Text, bool In, int MinutesAgo, bool Read)[] sms,
        string carrier,
        (int Who, CallLogType Type, int MinutesAgo, int Duration)[] calls,
        string unknownNumber)
    {
        var now = DateTimeOffset.Now;
        var contacts = people
            .Select((p, i) => new Contact($"c{i}", p.Name, p.Starred, false, [new PhoneNumber(p.Number, PhoneNumberType.Mobile)]))
            .ToList();
        var log = calls.Select((c, i) => new CallLogEntry(
            $"l{i}",
            c.Who < 0 ? unknownNumber : people[c.Who].Number,
            c.Who < 0 ? null : people[c.Who].Name,
            c.Type,
            now.AddMinutes(-c.MinutesAgo).ToUnixTimeMilliseconds(),
            c.Duration)).ToList();
        var messages = sms.Select((m, i) => new MapMessageInfo(
            $"m{i}",
            m.In ? MapFolder.Inbox : MapFolder.Sent,
            people[m.Who].Number,
            null,
            m.Text,
            DateTime.Now.AddMinutes(-m.MinutesAgo),
            Encoding.UTF8.GetByteCount(m.Text),
            m.Read)).ToList();
        var info = new DeviceInfo("Galaxy S26 Ultra", "Samsung Galaxy S26 Ultra", 76, false, 3, carrier, CallControl.Basic, ["phone", "contacts", "calllog"]);
        return new Sample(info, contacts, log, messages);
    }

    private static (Stream Pc, Stream Phone) InMemoryPair()
    {
        var toPhone = new AnonymousPipeServerStream(PipeDirection.Out);
        var fromPc = new AnonymousPipeClientStream(PipeDirection.In, toPhone.ClientSafePipeHandle);
        var toPc = new AnonymousPipeServerStream(PipeDirection.Out);
        var fromPhone = new AnonymousPipeClientStream(PipeDirection.In, toPc.ClientSafePipeHandle);
        return (new DuplexStream(fromPhone, toPhone), new DuplexStream(fromPc, toPc));
    }

    private sealed class DuplexStream(Stream input, Stream output) : Stream
    {
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => input.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => input.ReadAsync(buffer, ct);
        public override void Write(byte[] buffer, int offset, int count) => output.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => output.WriteAsync(buffer, ct);
        public override void Flush() => output.Flush();
        public override Task FlushAsync(CancellationToken ct) => output.FlushAsync(ct);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
