using Handsfreely.App.Infrastructure;
using Handsfreely.Core.Protocol;

namespace Handsfreely.App.Services;

public sealed class CallCoordinator
{
    private readonly PhoneHub _hub;
    private readonly HfpService _hfp;

    public CallCoordinator(PhoneHub hub, HfpService hfp)
    {
        _hub = hub;
        _hfp = hfp;
    }

    public (string Number, string? Name, DateTime At)? LastDialed { get; private set; }

    public event Action? Dialing;

    public bool Muted { get; private set; }
    public bool Held { get; private set; }
    public bool AudioOnPhone { get; private set; }

    private PhoneSession Session => _hub.Active ?? throw new InvalidOperationException("no phone connected");

    private bool FullControl => _hub.Active?.Info?.CallControl == CallControl.Full;

    public async Task DialAsync(string number, string? name = null)
    {
        number = CleanNumber(number);
        if (number.Length == 0) return;
        ResetCallState();
        LastDialed = (number, name, DateTime.Now);
        Dialing?.Invoke();
        if (_hfp.CanDial)
        {
            try
            {
                _hfp.Dial(number, name);
                return;
            }
            catch (Exception e)
            {
                Log.Error("hfp dial failed, falling back to phone", e);
            }
        }
        await Session.Client.DialAsync(number);
    }

    public async Task AnswerAsync()
    {
        ResetCallState();
        if (await TryPhoneAsync(c => c.AnswerAsync())) return;
        await _hfp.AnswerAsync();
    }

    public async Task RejectAsync()
    {
        if (await TryPhoneAsync(c => c.RejectAsync())) return;
        await _hfp.RejectAsync();
    }

    public async Task EndAsync()
    {
        if (await TryPhoneAsync(c => c.EndAsync())) return;
        await _hfp.EndAsync();
    }

    public async Task<bool> SetMuteAsync(bool on)
    {
        bool ok = (FullControl && await TryPhoneAsync(c => c.MuteAsync(on)))
            || await _hfp.MuteAsync(on)
            || MicrophoneMute.TrySet(on);
        if (ok) Muted = on;
        return ok;
    }

    public async Task<bool> SetHoldAsync(bool on)
    {
        bool ok = (FullControl && await TryPhoneAsync(c => c.HoldAsync(on))) || await _hfp.HoldAsync(on);
        if (ok) Held = on;
        return ok;
    }

    public async Task<bool> SendDtmfAsync(char digit) =>
        (FullControl && await TryPhoneAsync(c => c.SendDtmfAsync(digit))) || await _hfp.SendDtmfAsync(digit);

    public async Task<bool> SetAudioOnPhoneAsync(bool toPhone)
    {
        bool ok = (FullControl && await TryPhoneAsync(c => c.SetAudioTargetAsync(toPhone))) || await _hfp.AudioToPhoneAsync(toPhone);
        if (ok) AudioOnPhone = toPhone;
        return ok;
    }

    public void ResetCallState()
    {
        if (Muted) MicrophoneMute.TrySet(false);
        Muted = false;
        Held = false;
        AudioOnPhone = false;
    }

    public static string CleanNumber(string number) =>
        new(number.Where(c => char.IsDigit(c) || c is '+' or '*' or '#' or ',' or ';').ToArray());

    private async Task<bool> TryPhoneAsync(Func<PhoneClient, Task> action)
    {
        if (_hub.Active is not { } s) return false;
        try
        {
            await action(s.Client);
            return true;
        }
        catch (RpcException e) when (e.Code is ErrorCodes.Unsupported or ErrorCodes.Permission)
        {
            return false;
        }
        catch (Exception e)
        {
            Log.Error("phone call action failed", e);
            return false;
        }
    }
}
