using Handsfreely.App.Localization;
using Handsfreely.App.Services;
using Handsfreely.App.Views;
using Handsfreely.Core.Protocol;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Handsfreely.App;

public sealed partial class MainWindow : Window
{
    private static readonly Dictionary<AppPage, Type> PageTypes = new()
    {
        [AppPage.Calls] = typeof(CallsPage),
        [AppPage.Contacts] = typeof(ContactsPage),
        [AppPage.Messages] = typeof(MessagesPage),
        [AppPage.Devices] = typeof(DevicesPage),
        [AppPage.Settings] = typeof(SettingsPage),
    };

    private AppPage _page = AppPage.Calls;
    private Action? _bannerAction;
    private bool _navigating;

    public MainWindow()
    {
        InitializeComponent();
        Infrastructure.WindowChrome.Apply(this, TitleBar);
        SystemBackdrop = MicaController.IsSupported() ? new MicaBackdrop() : new DesktopAcrylicBackdrop();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Handsfreely.ico"));
        AppWindow.Resize(new SizeInt32(1120, 760));
        Activated += (_, e) => IsForeground = e.WindowActivationState != WindowActivationState.Deactivated;

        ApplyTexts();
        L.LanguageChanged += () =>
        {
            ApplyTexts();
            Navigate(_page, null);
        };
        App.Current!.Hfp.StateChanged += () => Infrastructure.Ui.Run(RefreshStatus);
        App.Current.Devices.Changed += () => Infrastructure.Ui.Run(RefreshStatus);
        Navigate(AppPage.Calls, null);
    }

    public bool IsForeground { get; private set; }

    public void Navigate(AppPage page, object? parameter)
    {
        _page = page;
        _navigating = true;
        Nav.SelectedItem = page switch
        {
            AppPage.Calls => NavCalls,
            AppPage.Contacts => NavContacts,
            AppPage.Messages => NavMessages,
            AppPage.Devices => NavDevices,
            _ => NavSettings,
        };
        _navigating = false;
        ContentFrame.Navigate(PageTypes[page], parameter);
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_navigating || args.SelectedItem is not NavigationViewItem { Tag: string tag }) return;
        Navigate(Enum.Parse<AppPage>(tag), null);
    }

    public void OnSmsReceived(SmsMessage m)
    {
        if (ContentFrame.Content is MessagesPage page) page.OnSmsReceived(m);
    }

    private void ApplyTexts()
    {
        NavCalls.Content = L.T("nav.calls");
        NavContacts.Content = L.T("nav.contacts");
        NavMessages.Content = L.T("nav.messages");
        NavDevices.Content = L.T("nav.devices");
        NavSettings.Content = L.T("nav.settings");
        RefreshStatus();
    }

    public void RefreshStatus()
    {
        var app = App.Current!;
        var active = app.Hub?.Active;

        PhoneStatus.Visibility = active is null ? Visibility.Collapsed : Visibility.Visible;
        if (active is not null)
        {
            PhoneName.Text = active.Phone.Name;
            var status = active.Status;
            BatteryText.Text = status is null ? "" : $"{status.Battery}%{(status.Charging ? " +" : "")}";
            SignalIcon.Glyph = ((char)(0xEC37 + Math.Clamp(status?.Signal ?? 0, 0, 4))).ToString();
            AudioIcon.Opacity = app.Hfp.State == HfpState.Connected ? 1 : 0.35;
            ToolTipService.SetToolTip(AudioIcon, L.T($"hfp.{app.Hfp.State.ToString().ToLowerInvariant()}"));
            active.Client.StatusChanged -= OnStatus;
            active.Client.StatusChanged += OnStatus;
        }

        _bannerAction = null;
        if (app.LiveCalls.FirstOrDefault() is { } call)
            ShowBanner(InfoBarSeverity.Success, L.F("banner.call", call.Name ?? app.Contacts.Find(call.Number)?.Name ?? call.Number), L.T("banner.call.open"), app.OpenCallWindow);
        else if (!app.BluetoothAvailable && app.Hub is not null)
            ShowBanner(InfoBarSeverity.Error, L.T("banner.nobt"), null);
        else if (active is null && app.Devices.Phones.Count == 0)
            ShowBanner(InfoBarSeverity.Informational, L.T("banner.nophones"), L.T("devices.add"), () =>
            {
                Navigate(AppPage.Devices, "pair");
            });
        else if (active is null)
            ShowBanner(InfoBarSeverity.Informational, L.T("banner.waiting"), null);
        else if (app.Hfp.State is HfpState.NotFound or HfpState.AccessDenied or HfpState.Failed)
            ShowBanner(InfoBarSeverity.Warning, L.T($"hfp.{app.Hfp.State.ToString().ToLowerInvariant()}"), L.T("hfp.retry"), () =>
            {
                _ = app.Hfp.ReconnectAsync();
            });
        else
            Banner.IsOpen = false;
    }

    private void OnStatus(DeviceStatus _) => Infrastructure.Ui.Run(RefreshStatus);

    private void ShowBanner(InfoBarSeverity severity, string text, string? button, Action? action = null)
    {
        Banner.Severity = severity;
        Banner.Message = text;
        Banner.IsOpen = true;
        BannerButton.Visibility = button is null ? Visibility.Collapsed : Visibility.Visible;
        BannerButton.Content = button;
        _bannerAction = action;
    }

    private void OnBannerButtonClick(object sender, RoutedEventArgs e) => _bannerAction?.Invoke();
}
