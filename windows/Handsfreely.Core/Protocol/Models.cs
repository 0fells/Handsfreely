using System.Text.Json;
using System.Text.Json.Serialization;

namespace Handsfreely.Core.Protocol;

public enum PhoneNumberType { Other, Mobile, Home, Work }
public enum CallLogType { In, Out, Missed, Rejected, Blocked, Voicemail }
public enum SmsType { In, Out }
public enum CallState { Disconnected, Ringing, Dialing, Active, Holding }
public enum CallDirection { In, Out }
public enum CallControl { Basic, Full }

public sealed record PhoneNumber(string Number, PhoneNumberType Type);

public sealed record Contact(string Id, string Name, bool Starred, bool HasPhoto, IReadOnlyList<PhoneNumber> Phones);

public sealed record CallLogEntry(string Id, string Number, string? Name, CallLogType Type, long Date, int Duration);

public sealed record SmsThread(string ThreadId, string Address, string? Name, string Snippet, long Date, int Unread);

public sealed record SmsMessage(string Id, string ThreadId, string Address, string Body, long Date, SmsType Type, bool Read);

public sealed record CallInfo(string Id, CallState State, string Number, string? Name, CallDirection Direction, long Since);

public sealed record DeviceStatus(int Battery, bool Charging, int Signal, string? Operator);

public sealed record DeviceInfo(
    string Name, string Model, int Battery, bool Charging, int Signal, string? Operator,
    CallControl CallControl, IReadOnlyList<string> Permissions);

public sealed record HelloInfo(string Name, string Model, string AppVersion, int Sdk);

public sealed record WelcomeInfo(string Name, string AppVersion);

public static class Json
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        o.Converters.Add(new LenientEnumConverterFactory());
        return o;
    }
}

internal sealed class LenientEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type t) => t.IsEnum;

    public override JsonConverter CreateConverter(Type t, JsonSerializerOptions o) =>
        (JsonConverter)Activator.CreateInstance(typeof(LenientEnumConverter<>).MakeGenericType(t))!;

    private sealed class LenientEnumConverter<T> : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o) =>
            reader.TokenType == JsonTokenType.String && Enum.TryParse<T>(reader.GetString(), true, out var v) ? v : default;

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions o) =>
            writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
    }
}
