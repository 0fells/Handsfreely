using System.Text.Json;
using System.Text.Json.Nodes;

namespace Handsfreely.Core.Protocol;

public sealed class PhoneClient : IAsyncDisposable
{
    private readonly RpcPeer _peer;

    public PhoneClient(RpcPeer peer)
    {
        _peer = peer;
        _peer.EventReceived += OnEvent;
        _peer.Closed += e => Closed?.Invoke(e);
    }

    public event Action<IReadOnlyList<CallInfo>>? CallStateChanged;
    public event Action<DeviceStatus>? StatusChanged;
    public event Action? ContactsChanged;
    public event Action? CallLogChanged;
    public event Action<Exception?>? Closed;

    public void Start() => _peer.Start();

    public Task PingAsync(CancellationToken ct = default) =>
        _peer.RequestAsync(MessageTypes.Ping, null, TimeSpan.FromSeconds(10), ct);

    public Task<DeviceInfo> GetDeviceInfoAsync(CancellationToken ct = default) =>
        _peer.RequestAsync<DeviceInfo>(MessageTypes.DeviceInfo, ct: ct);

    public async Task<IReadOnlyList<Contact>> ListContactsAsync(CancellationToken ct = default) =>
        (await _peer.RequestAsync<ContactsResult>(MessageTypes.ContactsList, null, TimeSpan.FromMinutes(2), ct)).Contacts;

    public async Task<byte[]?> GetContactPhotoAsync(string id, CancellationToken ct = default)
    {
        var r = await _peer.RequestAsync(MessageTypes.ContactsPhoto, new JsonObject { ["id"] = id }, ct: ct);
        return r?["data"]?.GetValue<string>() is { } b64 ? Convert.FromBase64String(b64) : null;
    }

    public async Task<IReadOnlyList<CallLogEntry>> ListCallLogAsync(int limit, long? before = null, CancellationToken ct = default) =>
        (await _peer.RequestAsync<CallLogResult>(MessageTypes.CallLogList, new { limit, before }, ct: ct)).Calls;

    public Task DialAsync(string number, CancellationToken ct = default) =>
        _peer.RequestAsync(MessageTypes.CallDial, RpcPeer.ToNode(new { number }), ct: ct);

    public Task AnswerAsync(CancellationToken ct = default) => _peer.RequestAsync(MessageTypes.CallAnswer, ct: ct);
    public Task RejectAsync(CancellationToken ct = default) => _peer.RequestAsync(MessageTypes.CallReject, ct: ct);
    public Task EndAsync(CancellationToken ct = default) => _peer.RequestAsync(MessageTypes.CallEnd, ct: ct);

    public Task HoldAsync(bool on, CancellationToken ct = default) =>
        _peer.RequestAsync(MessageTypes.CallHold, RpcPeer.ToNode(new { on }), ct: ct);

    public Task MuteAsync(bool on, CancellationToken ct = default) =>
        _peer.RequestAsync(MessageTypes.CallMute, RpcPeer.ToNode(new { on }), ct: ct);

    public Task SendDtmfAsync(char digit, CancellationToken ct = default) =>
        _peer.RequestAsync(MessageTypes.CallDtmf, RpcPeer.ToNode(new { digit = digit.ToString() }), ct: ct);

    public Task SetAudioTargetAsync(bool toPhone, CancellationToken ct = default) =>
        _peer.RequestAsync(MessageTypes.CallAudio, RpcPeer.ToNode(new { target = toPhone ? "phone" : "pc" }), ct: ct);

    private void OnEvent(string type, JsonNode? data)
    {
        try
        {
            switch (type)
            {
                case MessageTypes.CallState:
                    CallStateChanged?.Invoke(data.Deserialize<CallStateEvent>(Json.Options)?.Calls ?? []);
                    break;
                case MessageTypes.DeviceStatus when data.Deserialize<DeviceStatus>(Json.Options) is { } s:
                    StatusChanged?.Invoke(s);
                    break;
                case MessageTypes.ContactsChanged:
                    ContactsChanged?.Invoke();
                    break;
                case MessageTypes.CallLogChanged:
                    CallLogChanged?.Invoke();
                    break;
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
        }
    }

    public ValueTask DisposeAsync() => _peer.DisposeAsync();

    private sealed record ContactsResult(IReadOnlyList<Contact> Contacts);
    private sealed record CallLogResult(IReadOnlyList<CallLogEntry> Calls);
    private sealed record CallStateEvent(IReadOnlyList<CallInfo> Calls);
}
