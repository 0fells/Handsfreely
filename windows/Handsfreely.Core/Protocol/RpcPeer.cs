using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Handsfreely.Core.Transport;

namespace Handsfreely.Core.Protocol;

public sealed class RpcPeer : IAsyncDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly SecureChannel _channel;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonNode?>> _pending = new();
    private readonly CancellationTokenSource _cts = new();
    private long _nextId;
    private Task? _loop;

    public RpcPeer(SecureChannel channel) => _channel = channel;

    public event Action<string, JsonNode?>? EventReceived;

    public event Action<Exception?>? Closed;

    public Func<string, JsonNode?, CancellationToken, Task<JsonNode?>>? RequestHandler { get; set; }

    public void Start() => _loop ??= Task.Run(() => RunAsync(_cts.Token));

    public async Task<JsonNode?> RequestAsync(string type, JsonNode? data = null, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        long id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        try
        {
            await _channel.SendAsync(new Message(type, id, data).Serialize(), ct).ConfigureAwait(false);
            return await tcs.Task.WaitAsync(timeout ?? DefaultTimeout, ct).ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public async Task<T> RequestAsync<T>(string type, object? data = null, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var result = await RequestAsync(type, ToNode(data), timeout, ct).ConfigureAwait(false);
        return result.Deserialize<T>(Json.Options) ?? throw new ProtocolException($"empty result for {type}");
    }

    public Task SendEventAsync(string type, object? data = null, CancellationToken ct = default) =>
        _channel.SendAsync(new Message(type, null, ToNode(data)).Serialize(), ct);

    public static JsonNode? ToNode(object? data) => data switch
    {
        null => null,
        JsonNode node => node,
        _ => JsonSerializer.SerializeToNode(data, data.GetType(), Json.Options),
    };

    private async Task RunAsync(CancellationToken ct)
    {
        Exception? error = null;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var frame = await _channel.ReceiveAsync(ct).ConfigureAwait(false);
                if (frame is null) break;
                Dispatch(Message.Parse(frame), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            error = e;
        }
        var reason = error ?? new EndOfStreamException("connection closed");
        foreach (var (_, tcs) in _pending) tcs.TrySetException(reason);
        Closed?.Invoke(error);
    }

    private void Dispatch(Message m, CancellationToken ct)
    {
        switch (m.Type)
        {
            case MessageTypes.Result when m.Id is { } id:
                if (_pending.TryGetValue(id, out var ok)) ok.TrySetResult(m.Data);
                break;
            case MessageTypes.Error when m.Id is { } id:
                if (_pending.TryGetValue(id, out var failed))
                    failed.TrySetException(new RpcException(
                        m.Data?["code"]?.GetValue<string>() ?? ErrorCodes.Failed,
                        m.Data?["message"]?.GetValue<string>() ?? ""));
                break;
            case var _ when m.Id is { } id:
                _ = HandleRequestAsync(id, m, ct);
                break;
            default:
                EventReceived?.Invoke(m.Type, m.Data);
                break;
        }
    }

    private async Task HandleRequestAsync(long id, Message m, CancellationToken ct)
    {
        Message reply;
        try
        {
            if (RequestHandler is null) throw new RpcException(ErrorCodes.UnknownType, m.Type);
            reply = new Message(MessageTypes.Result, id, await RequestHandler(m.Type, m.Data, ct).ConfigureAwait(false));
        }
        catch (RpcException e)
        {
            reply = Error(id, e.Code, e.Message);
        }
        catch (Exception e)
        {
            reply = Error(id, ErrorCodes.Failed, e.Message);
        }
        try
        {
            await _channel.SendAsync(reply.Serialize(), ct).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static Message Error(long id, string code, string message) =>
        new(MessageTypes.Error, id, new JsonObject { ["code"] = code, ["message"] = message });

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        await _channel.DisposeAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); } catch { }
        }
        _cts.Dispose();
    }
}
