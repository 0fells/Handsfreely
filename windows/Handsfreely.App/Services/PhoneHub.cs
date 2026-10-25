using Handsfreely.App.Infrastructure;
using Handsfreely.Core.Crypto;
using Handsfreely.Core.Pairing;
using Handsfreely.Core.Protocol;

namespace Handsfreely.App.Services;

public sealed class PhoneSession : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();

    public PhoneSession(TrustedPhone phone, PhoneClient client)
    {
        Phone = phone;
        Client = client;
        client.CallStateChanged += calls => Calls = calls;
        client.StatusChanged += s => Status = s;
    }

    public TrustedPhone Phone { get; }
    public PhoneClient Client { get; }
    public DeviceInfo? Info { get; set; }
    public DeviceStatus? Status { get; set; }
    public IReadOnlyList<CallInfo> Calls { get; private set; } = [];
    public CancellationToken Token => _cts.Token;

    public bool HasPermission(string name) => Info?.Permissions.Contains(name) == true;

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        await Client.DisposeAsync();
        _cts.Dispose();
    }
}

public sealed class PhoneHub
{
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(30);

    private readonly DeviceStore _store;
    private readonly P256KeyPair _key;
    private readonly object _lock = new();
    private readonly Dictionary<string, PhoneSession> _sessions = [];

    public PhoneHub(DeviceStore store, P256KeyPair key)
    {
        _store = store;
        _key = key;
    }

    public P256KeyPair Key => _key;

    public event Action? Changed;
    public event Action<PhoneSession>? SessionStarted;

    public IReadOnlyList<PhoneSession> Sessions
    {
        get { lock (_lock) return [.. _sessions.Values]; }
    }

    public PhoneSession? Active
    {
        get
        {
            lock (_lock)
            {
                if (Settings.Current.ActivePhoneId is { } id && _sessions.TryGetValue(id, out var s)) return s;
                return _sessions.Values.FirstOrDefault();
            }
        }
    }

    public bool IsConnected(string phoneId)
    {
        lock (_lock) return _sessions.ContainsKey(phoneId);
    }

    public void SetActive(string phoneId)
    {
        Settings.Current.ActivePhoneId = phoneId;
        Settings.Current.Save();
        Changed?.Invoke();
    }

    public async void Accept(Stream stream, string address)
    {
        try
        {
            var welcome = new WelcomeInfo(Environment.MachineName, typeof(PhoneHub).Assembly.GetName().Version?.ToString(3) ?? "0");
            var accepted = await PcEndpoint.AcceptAsync(stream, _key, _store, welcome);
            var id = TrustedPhone.IdFor(accepted.PhonePublicKey);
            _store.Update(id, p =>
            {
                p.Name = accepted.Hello.Name;
                p.Model = accepted.Hello.Model;
                p.LastSeen = DateTimeOffset.Now;
                if (address.Length == 12) p.BluetoothAddress = address;
            });
            var phone = _store.Find(id)!;
            if (accepted.NewlyPaired && phone.BluetoothAddress is { } mac)
            {
                foreach (var stale in _store.Phones.Where(p => p.Id != id && p.BluetoothAddress == mac))
                {
                    await DisconnectAsync(stale.Id);
                    _store.Remove(stale.Id);
                    Log.Info($"removed stale record {stale.Id} of the same phone");
                }
            }
            var session = new PhoneSession(phone, new PhoneClient(new RpcPeer(accepted.Channel)));
            session.Client.Closed += e => _ = DropAsync(session, e);

            PhoneSession? old;
            lock (_lock)
            {
                _sessions.TryGetValue(id, out old);
                _sessions[id] = session;
            }
            if (old is not null) await old.DisposeAsync();

            session.Client.Start();
            Log.Info($"phone {id} ({phone.Name}) connected{(accepted.NewlyPaired ? ", newly paired" : "")}");
            try
            {
                session.Info = await session.Client.GetDeviceInfoAsync(session.Token);
                session.Status = new DeviceStatus(session.Info.Battery, session.Info.Charging, session.Info.Signal, session.Info.Operator);
            }
            catch (Exception e)
            {
                Log.Error("device.info failed", e);
            }
            SessionStarted?.Invoke(session);
            Changed?.Invoke();
            _ = KeepAliveAsync(session);
        }
        catch (HandshakeRejectedException e)
        {
            Log.Info($"handshake from {address} rejected: {e.Status}");
        }
        catch (Exception e)
        {
            Log.Error($"handshake from {address} failed", e);
            stream.Dispose();
        }
    }

    public async Task DisconnectAsync(string phoneId)
    {
        PhoneSession? s;
        lock (_lock) _sessions.Remove(phoneId, out s);
        if (s is not null)
        {
            await s.DisposeAsync();
            Changed?.Invoke();
        }
    }

    private async Task KeepAliveAsync(PhoneSession session)
    {
        try
        {
            while (!session.Token.IsCancellationRequested)
            {
                await Task.Delay(PingInterval, session.Token);
                var info = await session.Client.GetDeviceInfoAsync(session.Token);
                var permissionsChanged = session.Info is null || !session.Info.Permissions.Order().SequenceEqual(info.Permissions.Order())
                    || session.Info.CallControl != info.CallControl;
                session.Info = info;
                session.Status = new DeviceStatus(info.Battery, info.Charging, info.Signal, info.Operator);
                if (permissionsChanged)
                {
                    Log.Info($"phone {session.Phone.Id} permissions: {string.Join(",", info.Permissions)}");
                    Changed?.Invoke();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            await DropAsync(session, e);
        }
    }

    private async Task DropAsync(PhoneSession session, Exception? reason)
    {
        bool removed;
        lock (_lock)
        {
            removed = _sessions.TryGetValue(session.Phone.Id, out var current) && current == session;
            if (removed) _sessions.Remove(session.Phone.Id);
        }
        if (!removed) return;
        Log.Info($"phone {session.Phone.Id} disconnected{(reason is null ? "" : ": " + reason.Message)}");
        _store.Update(session.Phone.Id, p => p.LastSeen = DateTimeOffset.Now);
        await session.DisposeAsync();
        Changed?.Invoke();
    }
}
