using System.Text;
using Handsfreely.App.Infrastructure;
using Handsfreely.Core.Map;
using Handsfreely.Core.Protocol;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;

namespace Handsfreely.App.Services;

public enum SmsState { Idle, Connecting, Connected, Denied, Unavailable, Failed }

public sealed class SmsService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private const int ListSize = 500;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ContactBook _contacts;
    private MapClient? _map;
    private string? _address;
    private CancellationTokenSource? _poll;
    private HashSet<string>? _knownInbox;
    private List<MapMessageInfo> _cache = [];
    private bool _demo;

    public SmsService(ContactBook contacts) => _contacts = contacts;

    public SmsState State { get; private set; } = SmsState.Idle;
    public event Action? StateChanged;
    public event Action<SmsMessage>? Received;

    public async Task AttachAsync(string? bluetoothAddress)
    {
        if (_demo) return;
        if (bluetoothAddress == _address && State == SmsState.Connected) return;
        _poll?.Cancel();
        await _gate.WaitAsync();
        try
        {
            await CloseLocked();
            _address = bluetoothAddress;
            _knownInbox = null;
            _cache = [];
            SetState(SmsState.Idle);
        }
        finally
        {
            _gate.Release();
        }
        if (string.IsNullOrEmpty(bluetoothAddress)) return;
        _poll = new CancellationTokenSource();
        _ = PollAsync(_poll.Token);
    }

    public Task RetryAsync()
    {
        var address = _address;
        _address = null;
        return AttachAsync(address);
    }

    public async Task<IReadOnlyList<SmsThread>> ThreadsAsync(CancellationToken ct = default)
    {
        var all = _demo ? _cache : await RunAsync(async map =>
        {
            var inbox = await map.ListAsync(MapFolder.Inbox, ListSize, 0, ct);
            var sent = await map.ListAsync(MapFolder.Sent, ListSize, 0, ct);
            return inbox.Concat(sent).ToList();
        }, ct);
        _cache = all;
        return [.. all
            .GroupBy(m => ThreadKey(m.Address))
            .Select(g =>
            {
                var last = g.MaxBy(m => m.Date)!;
                var address = g.FirstOrDefault(m => m.Folder == MapFolder.Inbox)?.Address ?? last.Address;
                var name = _contacts.Find(address)?.Name ?? g.Select(m => m.Name).FirstOrDefault(n => n is not null);
                return new SmsThread(g.Key, address, name, last.Subject, ToUnixMs(last.Date), g.Count(m => !m.Read));
            })
            .OrderByDescending(t => t.Date)];
    }

    public async Task<IReadOnlyList<SmsMessage>> MessagesAsync(string threadId, int limit, CancellationToken ct = default)
    {
        if (_cache.Count == 0) await ThreadsAsync(ct);
        var items = _cache.Where(m => ThreadKey(m.Address) == threadId).OrderByDescending(m => m.Date).Take(limit).ToList();
        var result = new List<SmsMessage>();
        foreach (var m in items) result.Add(new SmsMessage(m.Handle, threadId, m.Address, await BodyAsync(m, ct), ToUnixMs(m.Date), m.Folder == MapFolder.Inbox ? SmsType.In : SmsType.Out, m.Read));
        return result;
    }

    public Task SendAsync(string to, string text, CancellationToken ct = default) => _demo ? Task.CompletedTask :
        RunAsync(async map =>
        {
            await map.SendAsync(to, text, ct);
            return true;
        }, ct);

    public async Task MarkReadAsync(string threadId, CancellationToken ct = default)
    {
        var unread = _cache.Where(m => !m.Read && ThreadKey(m.Address) == threadId).ToList();
        if (unread.Count == 0) return;
        if (!_demo) await RunAsync(async map =>
        {
            foreach (var m in unread) await map.MarkReadAsync(m.Handle, ct);
            return true;
        }, ct);
        _cache = [.. _cache.Select(m => unread.Contains(m) ? m with { Read = true } : m)];
    }

    public static string ThreadKey(string address)
    {
        var key = ContactBook.Key(address);
        return key.Length > 0 ? key : address.Trim().ToLowerInvariant();
    }

    private async Task<string> BodyAsync(MapMessageInfo m, CancellationToken ct)
    {
        if (Encoding.UTF8.GetByteCount(m.Subject) >= m.Size && m.Subject.Length > 0) return m.Subject;
        try
        {
            return await RunAsync(map => map.GetBodyAsync(m.Handle, ct), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.Error("map get message failed", e);
            return m.Subject;
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (CallActivity.Active)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(3), ct); } catch (OperationCanceledException) { return; }
                continue;
            }
            try
            {
                var latest = await RunAsync(map => map.ListAsync(MapFolder.Inbox, 20, 0, ct), ct);
                if (_knownInbox is null)
                {
                    _knownInbox = [.. latest.Select(m => m.Handle)];
                }
                else
                {
                    foreach (var m in latest.Where(m => !m.Read && _knownInbox.Add(m.Handle)).OrderBy(m => m.Date))
                    {
                        var key = ThreadKey(m.Address);
                        _cache.Add(m);
                        Received?.Invoke(new SmsMessage(m.Handle, key, m.Address, await BodyAsync(m, ct), ToUnixMs(m.Date), SmsType.In, false));
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                Log.Error("map poll failed", e);
            }
            try
            {
                await Task.Delay(State == SmsState.Connected ? PollInterval : PollInterval * 4, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<T> RunAsync<T>(Func<MapClient, Task<T>> action, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                var map = _map ?? await OpenLocked(ct);
                try
                {
                    return await action(map);
                }
                catch (Exception e) when (attempt == 0 && e is IOException or ObjectDisposedException or System.Runtime.InteropServices.COMException)
                {
                    Log.Info($"map session lost ({e.Message}), reopening");
                    await CloseLocked();
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<MapClient> OpenLocked(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_address)) throw new InvalidOperationException("no phone");
        SetState(SmsState.Connecting);
        try
        {
            var device = await BluetoothDevice.FromBluetoothAddressAsync(Convert.ToUInt64(_address, 16))
                ?? throw new IOException("phone is not paired with Windows");
            var services = await device.GetRfcommServicesForIdAsync(RfcommServiceId.FromUuid(MapClient.MasServiceUuid), BluetoothCacheMode.Uncached);
            if (services.Services.Count == 0)
            {
                SetState(SmsState.Unavailable);
                throw new IOException($"no MAP service: {services.Error}");
            }
            var service = services.Services[0];
            if (await service.RequestAccessAsync() != DeviceAccessStatus.Allowed)
            {
                SetState(SmsState.Denied);
                throw new UnauthorizedAccessException("windows denied access to MAP");
            }
            var socket = new StreamSocket();
            await socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName,
                SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            wait.CancelAfter(TimeSpan.FromSeconds(60));
            _map = await MapClient.ConnectAsync(new SocketStream(socket), wait.Token);
            SetState(SmsState.Connected);
            Log.Info("map session opened");
            return _map;
        }
        catch (ObexException e) when (e.Forbidden)
        {
            SetState(SmsState.Denied);
            throw;
        }
        catch (Exception) when (State == SmsState.Connecting)
        {
            SetState(SmsState.Failed);
            throw;
        }
    }

    private async Task CloseLocked()
    {
        if (_map is { } map)
        {
            _map = null;
            try { await map.DisposeAsync(); } catch { }
        }
    }

    private static long ToUnixMs(DateTime local) =>
        local == DateTime.MinValue ? 0 : new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Local)).ToUnixTimeMilliseconds();

    public void UseDemo(List<MapMessageInfo> messages)
    {
        _demo = true;
        _cache = messages;
        SetState(SmsState.Connected);
    }

    private void SetState(SmsState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke();
    }
}
