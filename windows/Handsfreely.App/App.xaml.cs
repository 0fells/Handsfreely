using Handsfreely.App.Infrastructure;
using Handsfreely.App.Localization;
using Handsfreely.App.Services;
using Handsfreely.App.Views;
using Handsfreely.Core.Protocol;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Windows.ApplicationModel.Activation;

namespace Handsfreely.App;

public enum AppPage { Calls, Contacts, Messages, Devices, Settings }

public partial class App : Application
{
    private CallWindow? _callWindow;
    private bool _exiting;
    private BluetoothServer? _server;
    private TrayService? _tray;
    private PhoneConnector? _connector;

    public App()
    {
        Current = this;
        if (Demo.Enabled) RequestedTheme = Demo.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Log.Error("unhandled", e.Exception);
            e.Handled = true;
        };
    }

    public static new App? Current { get; private set; }

    public DeviceStore Devices { get; } = new();
    public PhoneHub Hub { get; private set; } = null!;
    public HfpService Hfp { get; } = new();
    public CallCoordinator Calls { get; private set; } = null!;
    public ToastService Toasts { get; } = new();
    public ContactBook Contacts { get; } = new();
    public SmsService Sms { get; private set; } = null!;
    public ContactPhotos Photos { get; private set; } = null!;
    public MainWindow? Window { get; private set; }
    public bool BluetoothAvailable { get; private set; }
    public string? AdapterAddress => _server?.AdapterAddress;

    public string? OpenThreadId { get; set; }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        Ui.Init(DispatcherQueue.GetForCurrentThread());
        Log.Info($"start {typeof(App).Assembly.GetName().Version}, windows call control: {HfpService.CallControlAvailable}{(Demo.Enabled ? ", demo" : "")}");
        Toasts.Invoked += (a, input) => Ui.Run(() => OnToast(a, input));
        if (!Demo.Enabled) Toasts.Register();

        var key = await PcIdentity.LoadOrCreateAsync();
        Hub = new PhoneHub(Devices, key);
        Calls = new CallCoordinator(Hub, Hfp);
        Photos = new ContactPhotos(Hub);
        Sms = new SmsService(Contacts);
        Sms.Received += m => Ui.Run(() => OnSmsReceived(m));
        Hub.SessionStarted += s => Ui.Run(() => OnSessionStarted(s));
        Hub.Changed += () => Ui.Run(OnHubChanged);

        Window = new MainWindow();
        Window.Closed += OnMainWindowClosed;
        if (Demo.Enabled)
        {
            BluetoothAvailable = true;
            Window.Activate();
            await DemoPhone.StartAsync(this);
            await ReloadContactsAsync();
            Window.RefreshStatus();
            Window.Navigate(Demo.Page, Demo.Page == AppPage.Messages ? (await Sms.ThreadsAsync())[0].ThreadId : null);
            return;
        }
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        if (activation.Kind != ExtendedActivationKind.StartupTask) Window.Activate();

        _tray = new TrayService(ShowMain, ExitApp);
        _tray.Create();

        _server = new BluetoothServer();
        _server.Connected += Hub.Accept;
        try
        {
            BluetoothAvailable = await _server.StartAsync();
        }
        catch (Exception e)
        {
            Log.Error("bluetooth server failed", e);
        }
        _connector = new PhoneConnector(Hub, Devices);
        if (BluetoothAvailable) _connector.Start();
        Window.RefreshStatus();
        HandleActivation(activation);
    }

    public void OnRedirectedActivation(AppActivationArguments args) => Ui.Run(() =>
    {
        ShowMain();
        HandleActivation(args);
    });

    private void HandleActivation(AppActivationArguments args)
    {
        switch (args.Kind)
        {
            case ExtendedActivationKind.Protocol when args.Data is IProtocolActivatedEventArgs p:
                OpenTelUri(p.Uri);
                break;
            case ExtendedActivationKind.AppNotification when args.Data is AppNotificationActivatedEventArgs n:
                OnToast(n.Arguments, n.UserInput);
                break;
        }
    }

    private void OpenTelUri(Uri uri)
    {
        var number = CallCoordinator.CleanNumber(Uri.UnescapeDataString(uri.AbsolutePath.Split(';')[0]));
        ShowMain();
        Window?.Navigate(AppPage.Calls, number);
    }

    private async void OnToast(IDictionary<string, string> args, IDictionary<string, string> input)
    {
        args.TryGetValue("action", out var action);
        try
        {
            switch (action)
            {
                case "answer":
                    await Calls.AnswerAsync();
                    break;
                case "reject":
                    await Calls.RejectAsync();
                    break;
                case "openCall":
                    OpenCallWindow();
                    break;
                case "openThread":
                    ShowMain();
                    Window?.Navigate(AppPage.Messages, args.TryGetValue("thread", out var thread) ? thread : null);
                    break;
                case "smsReply" when input.TryGetValue("reply", out var text) && !string.IsNullOrWhiteSpace(text):
                    await Sms.SendAsync(args["to"], text);
                    break;
            }
        }
        catch (Exception e)
        {
            Log.Error($"toast action {action} failed", e);
            Toasts.ShowText("Handsfreely", L.T("error.action"));
        }
    }

    private void OnSessionStarted(PhoneSession s)
    {
        s.Client.CallStateChanged += calls => Ui.Run(() => OnCallsChanged(s, calls));
        s.Client.ContactsChanged += () => Ui.Run(() => _ = ReloadContactsAsync());
    }

    private void OnHubChanged()
    {
        var active = Hub.Active;
        _ = Hfp.AttachAsync(active?.Phone.BluetoothAddress);
        _ = Sms.AttachAsync(active?.Phone.BluetoothAddress);
        _tray?.SetTooltip(active is null ? "Handsfreely" : $"Handsfreely - {active.Phone.Name}");
        if (active is null)
        {
            Contacts.Clear();
            Photos.Clear();
            OnCallsChanged(null, []);
        }
        else
        {
            _ = ReloadContactsAsync();
            if (active.Calls.Count > 0) OnCallsChanged(active, active.Calls);
        }
        Window?.RefreshStatus();
    }

    public async Task ReloadContactsAsync()
    {
        if (Hub.Active is not { } s || !s.HasPermission("contacts"))
        {
            Contacts.Clear();
            return;
        }
        try
        {
            var list = await s.Client.ListContactsAsync(s.Token);
            if (Hub.Active == s) Contacts.Set(list);
        }
        catch (Exception e)
        {
            Log.Error("contacts load failed", e);
        }
    }

    private void OnCallsChanged(PhoneSession? s, IReadOnlyList<CallInfo> calls)
    {
        if (s is not null && s != Hub.Active) return;
        var dialed = Calls?.LastDialed is { } d && DateTime.Now - d.At < TimeSpan.FromMinutes(1) ? d : default((string Number, string? Name, DateTime At)?);
        var live = calls
            .Where(c => c.State != CallState.Disconnected)
            .Select(c => c.Direction == CallDirection.Out && c.Number.Length == 0 && dialed is { } x ? c with { Number = x.Number, Name = c.Name ?? x.Name } : c)
            .ToList();
        var ringing = live.FirstOrDefault(c => c.State == CallState.Ringing);

        if (ringing is not null && Hfp.State != HfpState.Connected)
            Toasts.ShowIncomingCall(ringing with { Name = ringing.Name ?? Contacts.Find(ringing.Number)?.Name });
        else Toasts.HideIncomingCall();

        LiveCalls = live;
        CallActivity.Set(live.Count > 0);
        if (live.Count == 0)
        {
            Calls?.ResetCallState();
            _callWindow?.CallsEnded();
        }
        else
        {
            _callWindow?.Update(live);
        }
        Window?.RefreshStatus();
    }

    public IReadOnlyList<CallInfo> LiveCalls { get; private set; } = [];

    public void OpenCallWindow()
    {
        if (LiveCalls.Count == 0) return;
        if (_callWindow is null)
        {
            _callWindow = new CallWindow();
            _callWindow.Closed += (_, _) => _callWindow = null;
        }
        _callWindow.Update(LiveCalls);
        _callWindow.Activate();
    }

    private void OnSmsReceived(SmsMessage m)
    {
        Window?.OnSmsReceived(m);
        if (!Settings.Current.SmsToasts) return;
        if (m.ThreadId == OpenThreadId && Window?.IsForeground == true) return;
        Toasts.ShowSms(m, Contacts.NameOr(m.Address));
    }

    public void ShowMain()
    {
        if (Window is null) return;
        Window.AppWindow.Show();
        Window.Activate();
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs e)
    {
        if (_exiting || !Settings.Current.CloseToTray)
        {
            ExitApp();
            return;
        }
        e.Handled = true;
        Window?.AppWindow.Hide();
    }

    public void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        Toasts.Unregister();
        _connector?.Stop();
        _tray?.Dispose();
        _server?.Dispose();
        _callWindow?.Close();
        Window?.Close();
        Exit();
    }
}
