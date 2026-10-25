using Handsfreely.App.Infrastructure;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;

namespace Handsfreely.App.Services;

public sealed class PhoneConnector
{
    public static readonly Guid PhoneServiceUuid = new("1c3afcb6-f84e-400c-9554-6c9dd39596a3");
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    private readonly PhoneHub _hub;
    private readonly DeviceStore _store;
    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<string> _unreachable = [];
    private readonly Dictionary<string, (int Skip, int Penalty)> _rejected = [];

    public PhoneConnector(PhoneHub hub, DeviceStore store)
    {
        _hub = hub;
        _store = store;
    }

    public void Start() => _ = LoopAsync(_cts.Token);

    public void Stop() => _cts.Cancel();

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                foreach (var phone in _store.Phones)
                {
                    if (ct.IsCancellationRequested) return;
                    if (phone.BluetoothAddress is not { Length: 12 } mac || _hub.IsConnected(phone.Id)) continue;
                    if (CallActivity.Active) break;
                    if (_rejected.TryGetValue(mac, out var r) && r.Skip > 0)
                    {
                        _rejected[mac] = (r.Skip - 1, r.Penalty);
                        continue;
                    }
                    if (await TryConnectAsync(mac))
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), ct);
                        if (_hub.IsConnected(phone.Id)) _rejected.Remove(mac);
                        else
                        {
                            int penalty = Math.Min(_rejected.TryGetValue(mac, out var old) ? old.Penalty * 2 : 1, 40);
                            _rejected[mac] = (penalty, penalty);
                        }
                    }
                }
                await Task.Delay(Interval, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<bool> TryConnectAsync(string mac)
    {
        try
        {
            var device = await BluetoothDevice.FromBluetoothAddressAsync(Convert.ToUInt64(mac, 16));
            if (device is null) return false;
            var services = await device.GetRfcommServicesForIdAsync(RfcommServiceId.FromUuid(PhoneServiceUuid), BluetoothCacheMode.Uncached);
            if (services.Services.Count == 0) return false;
            var service = services.Services[0];
            var socket = new StreamSocket();
            await socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName,
                SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);
            _unreachable.Remove(mac);
            Log.Info($"connected to phone {mac}");
            _hub.Accept(new SocketStream(socket), mac);
            return true;
        }
        catch (Exception e)
        {
            if (_unreachable.Add(mac)) Log.Info($"phone {mac} not reachable: {e.Message}");
            return false;
        }
    }
}
