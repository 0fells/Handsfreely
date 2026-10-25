using System.Text.Json;
using System.Text.Json.Nodes;

namespace Handsfreely.Core.Protocol;

public sealed record Message(string Type, long? Id, JsonNode? Data)
{
    public byte[] Serialize()
    {
        var obj = new JsonObject { ["t"] = Type };
        if (Id is { } id) obj["id"] = id;
        if (Data is not null) obj["d"] = Data.DeepClone();
        return JsonSerializer.SerializeToUtf8Bytes(obj);
    }

    public static Message Parse(ReadOnlySpan<byte> utf8)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(utf8);
        }
        catch (JsonException e)
        {
            throw new ProtocolException("message is not valid JSON", e);
        }
        if (node is not JsonObject obj || obj["t"] is not JsonValue t || !t.TryGetValue<string>(out var type))
            throw new ProtocolException("message has no type");
        long? id = obj["id"] is JsonValue v && v.TryGetValue<long>(out var n) ? n : null;
        var data = obj["d"];
        data?.Parent?.AsObject().Remove("d");
        return new Message(type, id, data);
    }
}

public static class MessageTypes
{
    public const string Hello = "hello";
    public const string Welcome = "welcome";
    public const string Result = "result";
    public const string Error = "error";

    public const string Ping = "ping";
    public const string DeviceInfo = "device.info";
    public const string ContactsList = "contacts.list";
    public const string ContactsPhoto = "contacts.photo";
    public const string CallLogList = "calllog.list";
    public const string CallDial = "call.dial";
    public const string CallAnswer = "call.answer";
    public const string CallReject = "call.reject";
    public const string CallEnd = "call.end";
    public const string CallHold = "call.hold";
    public const string CallMute = "call.mute";
    public const string CallDtmf = "call.dtmf";
    public const string CallAudio = "call.audio";

    public const string CallState = "call.state";
    public const string DeviceStatus = "device.status";
    public const string ContactsChanged = "contacts.changed";
    public const string CallLogChanged = "calllog.changed";
}

public static class ErrorCodes
{
    public const string UnknownType = "unknown_type";
    public const string BadRequest = "bad_request";
    public const string Permission = "permission";
    public const string Unsupported = "unsupported";
    public const string Failed = "failed";
}

public sealed class RpcException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
