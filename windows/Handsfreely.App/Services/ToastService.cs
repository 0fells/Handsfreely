using Handsfreely.App.Infrastructure;
using Handsfreely.App.Localization;
using Handsfreely.Core.Protocol;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Handsfreely.App.Services;

public sealed class ToastService
{
    private const string CallTag = "call";
    private const string CallGroup = "calls";

    private bool _registered;

    public event Action<IDictionary<string, string>, IDictionary<string, string>>? Invoked;

    public void Register()
    {
        try
        {
            AppNotificationManager.Default.NotificationInvoked += (_, e) => Invoked?.Invoke(e.Arguments, e.UserInput);
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch (Exception e)
        {
            Log.Error("toast registration failed", e);
        }
    }

    public void Unregister()
    {
        if (_registered) AppNotificationManager.Default.Unregister();
    }

    public void ShowIncomingCall(CallInfo call)
    {
        var builder = new AppNotificationBuilder()
            .SetScenario(AppNotificationScenario.IncomingCall)
            .SetTag(CallTag)
            .SetGroup(CallGroup)
            .AddArgument("action", "openCall")
            .AddText(L.T("toast.incoming"))
            .AddText(call.Name ?? call.Number)
            .AddText(call.Name is null ? "" : call.Number)
            .AddButton(new AppNotificationButton(L.T("call.answer")).AddArgument("action", "answer"))
            .AddButton(new AppNotificationButton(L.T("call.decline")).AddArgument("action", "reject"));
        Show(builder);
    }

    public async void HideIncomingCall()
    {
        if (!_registered) return;
        try
        {
            await AppNotificationManager.Default.RemoveByTagAndGroupAsync(CallTag, CallGroup);
        }
        catch (Exception e)
        {
            Log.Error("toast remove failed", e);
        }
    }

    public void ShowSms(SmsMessage message, string sender)
    {
        var builder = new AppNotificationBuilder()
            .AddArgument("action", "openThread")
            .AddArgument("thread", message.ThreadId)
            .AddText(sender)
            .AddText(message.Body)
            .AddTextBox("reply", L.T("sms.placeholder"), "")
            .AddButton(new AppNotificationButton(L.T("sms.reply"))
                .AddArgument("action", "smsReply")
                .AddArgument("thread", message.ThreadId)
                .AddArgument("to", message.Address)
                .SetInputId("reply"));
        Show(builder);
    }

    public void ShowText(string title, string text) =>
        Show(new AppNotificationBuilder().AddText(title).AddText(text));

    private void Show(AppNotificationBuilder builder)
    {
        if (!_registered) return;
        try
        {
            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch (Exception e)
        {
            Log.Error("toast show failed", e);
        }
    }
}
