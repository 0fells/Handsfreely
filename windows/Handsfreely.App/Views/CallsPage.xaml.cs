using Handsfreely.App.Infrastructure;
using Handsfreely.App.Localization;
using Handsfreely.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace Handsfreely.App.Views;

public sealed partial class CallsPage : Page
{
    private static readonly (string Digit, string Letters)[] Keys =
    [
        ("1", ""), ("2", "ABC"), ("3", "DEF"),
        ("4", "GHI"), ("5", "JKL"), ("6", "MNO"),
        ("7", "PQRS"), ("8", "TUV"), ("9", "WXYZ"),
        ("*", ""), ("0", "+"), ("#", ""),
    ];

    private readonly App _app = App.Current!;

    public CallsPage()
    {
        InitializeComponent();
        BuildPad();
        _app.Hub.Changed += OnHubChanged;
        Unloaded += (_, _) => _app.Hub.Changed -= OnHubChanged;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is string number && number.Length > 0)
        {
            NumberBox.Text = number;
            CallButton.Focus(FocusState.Programmatic);
        }
        _ = LoadRecentAsync();
    }

    private void OnHubChanged() => Ui.Run(() => _ = LoadRecentAsync());

    private void BuildPad()
    {
        for (int i = 0; i < 3; i++) Pad.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < 4; i++) Pad.RowDefinitions.Add(new RowDefinition());
        for (int i = 0; i < Keys.Length; i++)
        {
            var (digit, letters) = Keys[i];
            var content = new StackPanel { Spacing = -2 };
            content.Children.Add(new TextBlock { Text = digit, FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center });
            content.Children.Add(new TextBlock
            {
                Text = letters,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });
            var button = new Button { Content = content, Style = (Style)Application.Current.Resources["DialPadButtonStyle"] };
            button.Click += (_, _) => Append(digit);
            if (digit == "0") button.RightTapped += (_, _) => Append("+");
            Grid.SetColumn(button, i % 3);
            Grid.SetRow(button, i / 3);
            Pad.Children.Add(button);
        }
    }

    private void Append(string s)
    {
        NumberBox.Text += s;
        NumberBox.SelectionStart = NumberBox.Text.Length;
    }

    private void OnBackspaceClick(object sender, RoutedEventArgs e)
    {
        if (NumberBox.Text.Length > 0) NumberBox.Text = NumberBox.Text[..^1];
    }

    private void OnNumberChanged(object sender, TextChangedEventArgs e)
    {
        var contact = _app.Contacts.Find(NumberBox.Text);
        MatchText.Text = contact?.Name ?? "";
    }

    private void OnNumberKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            OnCallClick(sender, e);
        }
    }

    private async void OnCallClick(object sender, RoutedEventArgs e)
    {
        var number = CallCoordinator.CleanNumber(NumberBox.Text);
        if (number.Length == 0) return;
        if (_app.Hub.Active is null)
        {
            await Dialogs.InfoAsync(XamlRoot, L.T("common.nophone"));
            return;
        }
        try
        {
            await _app.Calls.DialAsync(number, _app.Contacts.Find(number)?.Name);
        }
        catch (Exception ex)
        {
            Log.Error("dial failed", ex);
            await Dialogs.InfoAsync(XamlRoot, L.F("calls.failed", ex.Message));
        }
    }

    private void OnRecentClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CallLogItem item) NumberBox.Text = item.Entry.Number;
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => _ = LoadRecentAsync();

    private async Task LoadRecentAsync()
    {
        var s = _app.Hub.Active;
        if (s is null)
        {
            RecentList.ItemsSource = null;
            EmptyText.Text = L.T("common.nophone");
            return;
        }
        if (!s.HasPermission("calllog"))
        {
            RecentList.ItemsSource = null;
            EmptyText.Text = L.T("perm.calllog");
            return;
        }
        try
        {
            var calls = await s.Client.ListCallLogAsync(200, ct: s.Token);
            RecentList.ItemsSource = calls.Select(c => new CallLogItem(c, _app.Contacts.Find(c.Number)?.Name)).ToList();
            EmptyText.Text = calls.Count == 0 ? L.T("calls.empty") : "";
        }
        catch (Exception ex)
        {
            Log.Error("call log load failed", ex);
            EmptyText.Text = L.F("common.loadfailed", ex.Message);
        }
    }
}
