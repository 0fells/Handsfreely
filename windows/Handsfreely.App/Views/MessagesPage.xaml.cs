using System.Collections.ObjectModel;
using Handsfreely.App.Infrastructure;
using Handsfreely.App.Localization;
using Handsfreely.App.Services;
using Handsfreely.Core.Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace Handsfreely.App.Views;

public sealed partial class MessagesPage : Page
{
    private readonly App _app = App.Current!;
    private readonly ObservableCollection<ThreadItem> _threads = [];
    private readonly ObservableCollection<MessageItem> _messages = [];
    private string? _threadId;
    private string? _address;

    public MessagesPage()
    {
        InitializeComponent();
        Threads.ItemsSource = _threads;
        MessagesList.ItemsSource = _messages;
        _app.Sms.StateChanged += OnSmsStateChanged;
        Unloaded += (_, _) =>
        {
            _app.OpenThreadId = null;
            _app.Sms.StateChanged -= OnSmsStateChanged;
        };
    }

    private void OnSmsStateChanged() => Ui.Run(() =>
    {
        if (_app.Sms.State == SmsState.Connected && _threads.Count == 0) _ = LoadThreadsAsync();
        else if (_threads.Count == 0) EmptyText.Text = StateText();
    });

    private string StateText() => _app.Sms.State switch
    {
        SmsState.Denied => L.T("sms.denied"),
        SmsState.Unavailable => L.T("sms.unavailable"),
        SmsState.Failed => L.T("sms.failed.connect"),
        _ => L.T("sms.connecting"),
    };

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        await LoadThreadsAsync();
        switch (e.Parameter)
        {
            case string p when p.StartsWith("to:", StringComparison.Ordinal):
                OpenAddress(p[3..]);
                break;
            case string threadId:
                Threads.SelectedItem = _threads.FirstOrDefault(t => t.Thread.ThreadId == threadId);
                break;
        }
    }

    private async Task LoadThreadsAsync()
    {
        _threads.Clear();
        if (_app.Hub.Active is null)
        {
            EmptyText.Text = L.T("common.nophone");
            return;
        }
        EmptyText.Text = StateText();
        try
        {
            foreach (var t in await _app.Sms.ThreadsAsync())
                _threads.Add(new ThreadItem(t, t.Name ?? _app.Contacts.NameOr(t.Address)));
            EmptyText.Text = _threads.Count == 0 ? L.T("sms.empty") : "";
        }
        catch (Exception ex)
        {
            Log.Error("sms threads load failed", ex);
            EmptyText.Text = _app.Sms.State is SmsState.Denied or SmsState.Unavailable ? StateText() : L.F("common.loadfailed", ex.Message);
        }
    }

    private async void OnThreadSelected(object sender, SelectionChangedEventArgs e)
    {
        if (Threads.SelectedItem is not ThreadItem item) return;
        _threadId = item.Thread.ThreadId;
        _address = item.Thread.Address;
        _app.OpenThreadId = _threadId;
        ShowPeer(item.Title, item.Thread.Address);
        await LoadMessagesAsync();
        if (item.Thread.Unread > 0)
        {
            try
            {
                await _app.Sms.MarkReadAsync(item.Thread.ThreadId);
                item.Thread = item.Thread with { Unread = 0 };
            }
            catch (Exception ex)
            {
                Log.Error("mark read failed", ex);
            }
        }
    }

    private void OpenAddress(string address)
    {
        var key = ContactBook.Key(address);
        var existing = _threads.FirstOrDefault(t => ContactBook.Key(t.Thread.Address) == key);
        if (existing is not null)
        {
            Threads.SelectedItem = existing;
            return;
        }
        Threads.SelectedItem = null;
        _threadId = null;
        _address = address;
        _app.OpenThreadId = null;
        _messages.Clear();
        ShowPeer(_app.Contacts.NameOr(address), address);
        ComposeBox.Focus(FocusState.Programmatic);
    }

    private void ShowPeer(string name, string address)
    {
        Conversation.Visibility = Visibility.Visible;
        PeerName.Text = name;
        PeerNumber.Text = name == address ? "" : address;
    }

    private async Task LoadMessagesAsync()
    {
        _messages.Clear();
        if (_threadId is null) return;
        try
        {
            var list = await _app.Sms.MessagesAsync(_threadId, 100);
            foreach (var m in list.OrderBy(m => m.Date)) _messages.Add(new MessageItem(m));
            ScrollToEnd();
        }
        catch (Exception ex)
        {
            Log.Error("sms messages load failed", ex);
        }
    }

    public void OnSmsReceived(SmsMessage m)
    {
        if (m.ThreadId == _threadId)
        {
            _messages.Add(new MessageItem(m));
            ScrollToEnd();
        }
        var thread = _threads.FirstOrDefault(t => t.Thread.ThreadId == m.ThreadId);
        var unread = m.ThreadId == _threadId ? 0 : (thread?.Thread.Unread ?? 0) + 1;
        if (thread is not null)
        {
            thread.Thread = thread.Thread with { Snippet = m.Body, Date = m.Date, Unread = unread };
            var index = _threads.IndexOf(thread);
            if (index > 0) _threads.Move(index, 0);
        }
        else
        {
            _threads.Insert(0, new ThreadItem(new SmsThread(m.ThreadId, m.Address, null, m.Body, m.Date, unread), _app.Contacts.NameOr(m.Address)));
        }
    }

    private void ScrollToEnd()
    {
        if (_messages.Count > 0) MessagesList.ScrollIntoView(_messages[^1]);
    }

    private void OnComposeKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (e.Key == VirtualKey.Enter && !shift)
        {
            e.Handled = true;
            OnSendClick(sender, e);
        }
    }

    private async void OnSendClick(object sender, RoutedEventArgs e)
    {
        var text = ComposeBox.Text.Trim();
        if (text.Length == 0 || _address is null) return;
        SendButton.IsEnabled = false;
        try
        {
            await _app.Sms.SendAsync(_address, text);
            ComposeBox.Text = "";
            var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            _messages.Add(new MessageItem(new SmsMessage("", _threadId ?? "", _address, text, now, SmsType.Out, true)));
            ScrollToEnd();
            if (_threadId is null)
            {
                var address = _address;
                await LoadThreadsAsync();
                OpenAddress(address);
            }
        }
        catch (Exception ex)
        {
            await Dialogs.InfoAsync(XamlRoot, L.F("sms.failed", ex.Message));
        }
        finally
        {
            SendButton.IsEnabled = true;
        }
    }

    private async void OnNewClick(object sender, RoutedEventArgs e)
    {
        var number = await Dialogs.AskAsync(XamlRoot, L.T("sms.new"), L.T("calls.number"));
        if (number is not null) OpenAddress(number);
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => _ = LoadThreadsAsync();

    private async void OnCallPeerClick(object sender, RoutedEventArgs e)
    {
        if (_address is null) return;
        try
        {
            await _app.Calls.DialAsync(_address, PeerName.Text);
        }
        catch (Exception ex)
        {
            await Dialogs.InfoAsync(XamlRoot, L.F("calls.failed", ex.Message));
        }
    }
}
