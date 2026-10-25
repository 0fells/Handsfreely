using Handsfreely.App.Infrastructure;
using Handsfreely.App.Localization;
using Handsfreely.App.Services;
using Handsfreely.Core.Pairing;
using Handsfreely.Core.Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using QRCoder;

namespace Handsfreely.App.Views;

public sealed class PhoneRow
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Model { get; init; }
    public required string State { get; init; }
    public required string Details { get; init; }
    public required string ActiveText { get; init; }
    public required Visibility UseVisibility { get; init; }
}

public sealed partial class DevicesPage : Page
{
    private static readonly string[] AllPermissions = ["contacts", "calllog", "phone"];

    private readonly App _app = App.Current!;

    public DevicesPage()
    {
        InitializeComponent();
        _app.Devices.Changed += OnChanged;
        _app.Hub.Changed += OnChanged;
        _app.Hfp.StateChanged += OnChanged;
        _app.Sms.StateChanged += OnChanged;
        Unloaded += (_, _) =>
        {
            _app.Sms.StateChanged -= OnChanged;
            _app.Devices.Changed -= OnChanged;
            _app.Hub.Changed -= OnChanged;
            _app.Hfp.StateChanged -= OnChanged;
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        Refresh();
        if (e.Parameter as string == "pair") Loaded += OpenPairingOnce;
    }

    private void OpenPairingOnce(object sender, RoutedEventArgs e)
    {
        Loaded -= OpenPairingOnce;
        OnAddClick(sender, e);
    }

    private void OnChanged() => Ui.Run(Refresh);

    private void Refresh()
    {
        var active = _app.Hub.Active;
        PhonesList.ItemsSource = _app.Devices.Phones.Select(p =>
        {
            var session = _app.Hub.Sessions.FirstOrDefault(s => s.Phone.Id == p.Id);
            var missing = session?.Info is { } info ? AllPermissions.Except(info.Permissions).ToList() : [];
            var details = new List<string>();
            if (p.BluetoothAddress is { } mac) details.Add("Bluetooth " + string.Join(":", mac.Chunk(2).Select(c => new string(c))));
            if (session?.Info is { } i) details.Add(L.T(i.CallControl == CallControl.Full ? "devices.control.full" : "devices.control.basic"));
            if (missing.Count > 0) details.Add(L.F("devices.missing", string.Join(", ", missing.Select(m => L.T("perm.name." + m)))));
            return new PhoneRow
            {
                Id = p.Id,
                Name = p.Name,
                Model = p.Model,
                State = session is not null ? L.T("devices.connected")
                    : p.LastSeen is { } seen ? L.F("devices.lastseen", seen.LocalDateTime.ToString("g"))
                    : L.T("devices.never"),
                Details = string.Join(" · ", details),
                ActiveText = session is not null && session == active ? L.T("devices.active") : "",
                UseVisibility = session is not null && session != active ? Visibility.Visible : Visibility.Collapsed,
            };
        }).ToList();
        EmptyText.Text = _app.Devices.Phones.Count == 0 ? L.T("devices.empty") : "";
        EmptyText.Visibility = _app.Devices.Phones.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        HfpText.Text = active is null ? L.T("common.nophone") : L.T($"hfp.{_app.Hfp.State.ToString().ToLowerInvariant()}");
        HfpControlText.Text = L.T(HfpService.CallControlAvailable ? "hfp.win11" : "hfp.win10");
        SmsText.Text = active is null ? L.T("common.nophone") : _app.Sms.State switch
        {
            SmsState.Connected => L.T("sms.connected"),
            SmsState.Denied => L.T("sms.denied"),
            SmsState.Unavailable => L.T("sms.unavailable"),
            SmsState.Failed => L.T("sms.failed.connect"),
            _ => L.T("sms.connecting"),
        };
    }

    private void OnUseClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id }) _app.Hub.SetActive(id);
    }

    private async void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id } || _app.Devices.Find(id) is not { } phone) return;
        if (!await Dialogs.ConfirmAsync(XamlRoot, L.F("devices.remove.confirm", phone.Name), L.T("devices.remove"))) return;
        await _app.Hub.DisconnectAsync(id);
        _app.Devices.Remove(id);
    }

    private void OnHfpRetryClick(object sender, RoutedEventArgs e) => _ = _app.Hfp.ReconnectAsync();

    private void OnSmsRetryClick(object sender, RoutedEventArgs e) => _ = _app.Sms.RetryAsync();

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (_app.AdapterAddress is not { } adapter)
        {
            await Dialogs.InfoAsync(XamlRoot, L.T("banner.nobt"));
            return;
        }

        var image = new Image { Width = 280, Height = 280 };
        var countdown = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
        var panel = new StackPanel { Spacing = 12, Width = 420 };
        panel.Children.Add(new TextBlock { Text = L.T("pair.steps"), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new Border { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White), Padding = new Thickness(12), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Center, Child = image });
        panel.Children.Add(countdown);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = L.T("devices.add"),
            Content = panel,
            CloseButtonText = L.T("common.cancel"),
        };

        DateTimeOffset expires = default;
        async Task NewCodeAsync()
        {
            var token = _app.Devices.OpenPairingWindow();
            expires = DateTimeOffset.Now + DeviceStore.PairingWindow;
            var code = new PairingCode(adapter, _app.Hub.Key.PublicKey, token, Environment.MachineName);
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(code.ToUri(), QRCodeGenerator.ECCLevel.M);
            image.Source = await Images.FromBytesAsync(new PngByteQRCode(data).GetGraphic(10));
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += async (_, _) =>
        {
            var left = expires - DateTimeOffset.Now;
            if (left <= TimeSpan.Zero)
            {
                await NewCodeAsync();
                left = expires - DateTimeOffset.Now;
            }
            countdown.Text = L.F("pair.expires", left.ToString(@"m\:ss"));
        };

        TrustedPhone? paired = null;
        void OnPaired(TrustedPhone p) => Ui.Run(() =>
        {
            paired = p;
            dialog.Hide();
        });

        await NewCodeAsync();
        countdown.Text = L.F("pair.expires", DeviceStore.PairingWindow.ToString(@"m\:ss"));
        _app.Devices.Paired += OnPaired;
        timer.Start();
        try
        {
            await dialog.ShowAsync();
        }
        finally
        {
            timer.Stop();
            _app.Devices.Paired -= OnPaired;
            _app.Devices.ClosePairingWindow();
        }

        if (paired is not null)
        {
            _app.Hub.SetActive(paired.Id);
            await Dialogs.InfoAsync(XamlRoot, L.F("pair.done", paired.Name));
        }
    }
}
