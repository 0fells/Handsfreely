using Handsfreely.App.Infrastructure;
using Handsfreely.App.Localization;
using Handsfreely.Core.Protocol;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Handsfreely.App.Views;

public sealed partial class CallWindow : Window
{
    private readonly App _app = App.Current!;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private CallInfo? _call;
    private bool _ended;

    public CallWindow()
    {
        InitializeComponent();
        Title = "Handsfreely";
        WindowChrome.Apply(this, DragArea);
        SystemBackdrop = MicaController.IsSupported() ? new MicaBackdrop() : new DesktopAcrylicBackdrop();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Handsfreely.ico"));
        AppWindow.Resize(new SizeInt32(380, 560));
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.IsAlwaysOnTop = true;
            p.IsMaximizable = false;
            p.IsResizable = false;
        }
        PlaceBottomRight();

        ToolTipService.SetToolTip(MuteButton, L.T("call.mute"));
        ToolTipService.SetToolTip(HoldButton, L.T("call.hold"));
        ToolTipService.SetToolTip(KeypadButton, L.T("call.keypad"));
        ToolTipService.SetToolTip(AudioButton, L.T("call.audiophone"));
        ToolTipService.SetToolTip(AnswerButton, L.T("call.answer"));
        ToolTipService.SetToolTip(DeclineButton, L.T("call.decline"));
        ToolTipService.SetToolTip(EndButton, L.T("call.end"));
        BuildKeypad();

        _timer.Tick += (_, _) => UpdateState();
        Closed += (_, _) => _timer.Stop();
    }

    public void Update(IReadOnlyList<CallInfo> live)
    {
        _ended = false;
        _call = live.FirstOrDefault(c => c.State == CallState.Ringing)
            ?? live.FirstOrDefault(c => c.State == CallState.Active)
            ?? live.FirstOrDefault(c => c.State == CallState.Dialing)
            ?? live[0];

        var contact = _app.Contacts.Find(_call.Number);
        var name = _call.Name ?? contact?.Name;
        NameText.Text = name ?? _call.Number;
        NumberText.Text = name is null ? "" : _call.Number;
        Picture.DisplayName = name ?? "";
        _ = ShowPhotoAsync(contact);
        OtherCallsText.Text = live.Count > 1 ? L.F("call.others", live.Count - 1) : "";

        bool ringing = _call.State == CallState.Ringing && _call.Direction == CallDirection.In;
        AnswerButton.Visibility = ringing ? Visibility.Visible : Visibility.Collapsed;
        DeclineButton.Visibility = ringing ? Visibility.Visible : Visibility.Collapsed;
        EndButton.Visibility = ringing ? Visibility.Collapsed : Visibility.Visible;
        Controls.Visibility = ringing ? Visibility.Collapsed : Visibility.Visible;
        if (ringing) Keypad.Visibility = Visibility.Collapsed;

        MuteButton.IsChecked = _app.Calls.Muted;
        HoldButton.IsChecked = _call.State == CallState.Holding;
        AudioButton.IsChecked = _app.Calls.AudioOnPhone;

        UpdateState();
        _timer.Start();
        AppWindow.Show();
        if (ringing) Activate();
    }

    private string? _photoFor;

    private async Task ShowPhotoAsync(Contact? contact)
    {
        if (contact?.Id == _photoFor) return;
        _photoFor = contact?.Id;
        Picture.ProfilePicture = null;
        if (contact is not { HasPhoto: true }) return;
        var photo = await _app.Photos.GetAsync(contact.Id);
        if (_photoFor == contact.Id) Picture.ProfilePicture = photo;
    }

    public void CallsEnded()
    {
        if (_ended) return;
        _ended = true;
        _timer.Stop();
        StateText.Text = L.T("call.ended");
        Controls.Visibility = Visibility.Collapsed;
        Keypad.Visibility = Visibility.Collapsed;
        AnswerButton.Visibility = DeclineButton.Visibility = EndButton.Visibility = Visibility.Collapsed;
        var close = DispatcherQueue.CreateTimer();
        close.Interval = TimeSpan.FromSeconds(2);
        close.IsRepeating = false;
        close.Tick += (_, _) =>
        {
            if (_ended) Close();
        };
        close.Start();
    }

    private void UpdateState()
    {
        if (_call is null) return;
        StateText.Text = _call.State switch
        {
            CallState.Ringing when _call.Direction == CallDirection.In => L.T("call.incoming"),
            CallState.Ringing or CallState.Dialing => L.T("call.dialing"),
            CallState.Holding => L.T("call.onhold"),
            CallState.Active when _call.Since > 0 => Format.Duration((int)Math.Max(0, (DateTimeOffset.Now.ToUnixTimeMilliseconds() - _call.Since) / 1000)),
            CallState.Active => L.T("call.active"),
            _ => "",
        };
    }

    private void BuildKeypad()
    {
        string[] keys = ["1", "2", "3", "4", "5", "6", "7", "8", "9", "*", "0", "#"];
        for (int i = 0; i < 3; i++) Keypad.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < 4; i++) Keypad.RowDefinitions.Add(new RowDefinition());
        for (int i = 0; i < keys.Length; i++)
        {
            var key = keys[i];
            var b = new Button { Content = key, Style = (Style)Application.Current.Resources["DialPadButtonStyle"], Height = 44 };
            b.Click += async (_, _) =>
            {
                if (!await _app.Calls.SendDtmfAsync(key[0])) Hint(L.T("call.dtmf.unavailable"));
            };
            Grid.SetColumn(b, i % 3);
            Grid.SetRow(b, i / 3);
            Keypad.Children.Add(b);
        }
    }

    private async void OnAnswerClick(object sender, RoutedEventArgs e) => await Run(_app.Calls.AnswerAsync);
    private async void OnDeclineClick(object sender, RoutedEventArgs e) => await Run(_app.Calls.RejectAsync);
    private async void OnEndClick(object sender, RoutedEventArgs e) => await Run(_app.Calls.EndAsync);

    private async void OnMuteClick(object sender, RoutedEventArgs e) =>
        await Toggle(MuteButton, _app.Calls.SetMuteAsync, "call.mute.unavailable");

    private async void OnHoldClick(object sender, RoutedEventArgs e) =>
        await Toggle(HoldButton, _app.Calls.SetHoldAsync, "call.hold.unavailable");

    private async void OnAudioClick(object sender, RoutedEventArgs e) =>
        await Toggle(AudioButton, _app.Calls.SetAudioOnPhoneAsync, "call.audio.unavailable");

    private void OnKeypadClick(object sender, RoutedEventArgs e) =>
        Keypad.Visibility = KeypadButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private async Task Toggle(ToggleButton button, Func<bool, Task<bool>> action, string unavailableKey)
    {
        bool on = button.IsChecked == true;
        if (!await action(on))
        {
            button.IsChecked = !on;
            Hint(L.T(unavailableKey));
        }
        else
        {
            Hint("");
        }
    }

    private async Task Run(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception e)
        {
            Log.Error("call action failed", e);
            Hint(L.F("error.withreason", e.Message));
        }
    }

    private void Hint(string text) => HintText.Text = text;

    private void PlaceBottomRight()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Move(new PointInt32(area.X + area.Width - AppWindow.Size.Width - 24, area.Y + area.Height - AppWindow.Size.Height - 24));
    }
}
