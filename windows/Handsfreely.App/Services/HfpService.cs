using Handsfreely.App.Infrastructure;
using Windows.ApplicationModel.Calls;
using Windows.Devices.Enumeration;
using Windows.Foundation.Metadata;

namespace Handsfreely.App.Services;

public enum HfpState
{
    Idle,
    Connecting,
    Connected,
    NotFound,
    AccessDenied,
    Failed,
}

public sealed class HfpService
{
    private const string AddressProperty = "System.DeviceInterface.Bluetooth.DeviceAddress";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private PhoneLineTransportDevice? _device;
    private PhoneLineWatcher? _watcher;
    private PhoneLine? _line;
    private string? _address;
    private bool? _lastCanDial;

    public HfpState State { get; private set; } = HfpState.Idle;
    public event Action? StateChanged;

    public static bool CallControlAvailable { get; } =
        ApiInformation.IsTypePresent("Windows.ApplicationModel.Calls.PhoneCall");

    public bool CanDial => _line?.CanDial == true;

    public async Task AttachAsync(string? bluetoothAddress, [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        if (Demo.Enabled) return;
        await Gate.WaitAsync();
        try
        {
            if (bluetoothAddress == _address && (State == HfpState.Connected || CanDial)) return;
            if (CallActivity.Active && bluetoothAddress == _address)
            {
                Log.Info("hfp: reattach skipped during a call");
                return;
            }
            Log.Info($"hfp: attach from {caller}, state {State}");
            Detach();
            _address = bluetoothAddress;
            if (string.IsNullOrEmpty(bluetoothAddress))
            {
                SetState(HfpState.Idle);
                return;
            }
            SetState(HfpState.Connecting);

            var selector = PhoneLineTransportDevice.GetDeviceSelector(PhoneLineTransport.Bluetooth);
            var devices = await DeviceInformation.FindAllAsync(selector, [AddressProperty]);
            var info = devices.FirstOrDefault(d =>
                d.Properties.TryGetValue(AddressProperty, out var a) &&
                string.Equals(a as string, bluetoothAddress, StringComparison.OrdinalIgnoreCase));
            if (info is null)
            {
                Log.Info($"hfp: no transport device for {bluetoothAddress}");
                SetState(HfpState.NotFound);
                return;
            }

            _device = PhoneLineTransportDevice.FromId(info.Id);
            var access = await _device.RequestAccessAsync();
            if (access != DeviceAccessStatus.Allowed)
            {
                Log.Info($"hfp: access {access}");
                SetState(HfpState.AccessDenied);
                return;
            }
            if (!_device.IsRegistered()) _device.RegisterApp();
            if (CallControlAvailable) _device.AudioRoutingStatusChanged += (d, _) => Log.Info($"hfp: audio routing {d.AudioRoutingStatus}");
            var connected = await _device.ConnectAsync();
            Log.Info($"hfp: connect {(connected ? "ok" : "failed")}");
            await WatchLinesAsync();
            if (connected || CanDial) SetState(HfpState.Connected);
            else if (State != HfpState.Connected) SetState(HfpState.Failed);
        }
        catch (Exception e)
        {
            Log.Error("hfp attach failed", e);
            SetState(HfpState.Failed);
        }
        finally
        {
            Gate.Release();
        }
    }

    public Task ReconnectAsync()
    {
        var address = _address;
        _address = null;
        return AttachAsync(address);
    }

    public void Dial(string number, string? name)
    {
        if (_line is null) throw new InvalidOperationException("no phone line");
        _line.Dial(number, name ?? number);
    }

    public async Task<bool> TryCallActionAsync(Func<PhoneCall, bool> action)
    {
        if (!CallControlAvailable || _line is null) return false;
        try
        {
            var result = await _line.GetAllActivePhoneCallsAsync();
            foreach (var info in result.AllActivePhoneCalls)
            {
                var call = PhoneCall.GetFromId(info.CallId);
                if (call is not null && action(call)) return true;
            }
        }
        catch (Exception e)
        {
            Log.Error("hfp call action failed", e);
        }
        return false;
    }

    public Task<bool> MuteAsync(bool on) =>
        TryCallActionAsync(c => (on ? c.Mute() : c.Unmute()) == PhoneCallOperationStatus.Succeeded);

    public Task<bool> HoldAsync(bool on) =>
        TryCallActionAsync(c => (on ? c.Hold() : c.ResumeFromHold()) == PhoneCallOperationStatus.Succeeded);

    public Task<bool> AnswerAsync() =>
        TryCallActionAsync(c => c.Status == PhoneCallStatus.Incoming && c.AcceptIncoming() == PhoneCallOperationStatus.Succeeded);

    public Task<bool> RejectAsync() =>
        TryCallActionAsync(c => c.Status == PhoneCallStatus.Incoming && c.RejectIncoming() == PhoneCallOperationStatus.Succeeded);

    public Task<bool> EndAsync() =>
        TryCallActionAsync(c => c.End() == PhoneCallOperationStatus.Succeeded);

    public Task<bool> AudioToPhoneAsync(bool toPhone) =>
        TryCallActionAsync(c => c.ChangeAudioDevice(toPhone ? PhoneCallAudioDevice.LocalDevice : PhoneCallAudioDevice.RemoteDevice)
            == PhoneCallOperationStatus.Succeeded);

    public Task<bool> SendDtmfAsync(char digit)
    {
        DtmfKey? key = digit switch
        {
            >= '0' and <= '9' => (DtmfKey)(digit - '0'),
            '*' => DtmfKey.Star,
            '#' => DtmfKey.Pound,
            _ => null,
        };
        if (key is null) return Task.FromResult(false);
        return TryCallActionAsync(c => c.SendDtmfKey(key.Value, DtmfToneAudioPlayback.Play) == PhoneCallOperationStatus.Succeeded);
    }

    private async Task WatchLinesAsync()
    {
        var store = await PhoneCallManager.RequestStoreAsync();
        _watcher = store.RequestLineWatcher();
        _watcher.LineAdded += async (_, e) => await ConsiderLineAsync(e.LineId);
        _watcher.LineUpdated += async (_, e) => await ConsiderLineAsync(e.LineId);
        _watcher.LineRemoved += (_, e) =>
        {
            if (_line?.Id == e.LineId) _line = null;
        };
        _watcher.Start();
    }

    private async Task ConsiderLineAsync(Guid lineId)
    {
        try
        {
            var line = await PhoneLine.FromIdAsync(lineId);
            if (line.Transport != PhoneLineTransport.Bluetooth || _device is null) return;
            if (ApiInformation.IsPropertyPresent("Windows.ApplicationModel.Calls.PhoneLine", "TransportDeviceId") &&
                !string.Equals(line.TransportDeviceId, _device.DeviceId, StringComparison.OrdinalIgnoreCase))
                return;
            bool changed = _line?.Id != line.Id || _lastCanDial != line.CanDial;
            _line = line;
            _lastCanDial = line.CanDial;
            if (changed) Log.Info($"hfp: phone line {lineId} canDial={line.CanDial}");
            if (line.CanDial) SetState(HfpState.Connected);
            else StateChanged?.Invoke();
        }
        catch (Exception e)
        {
            Log.Error("hfp line lookup failed", e);
        }
    }

    private void Detach()
    {
        try { _watcher?.Stop(); } catch { }
        _watcher = null;
        _line = null;
        _device = null;
    }

    public void UseDemo() => SetState(HfpState.Connected);

    private void SetState(HfpState state)
    {
        State = state;
        StateChanged?.Invoke();
    }
}
