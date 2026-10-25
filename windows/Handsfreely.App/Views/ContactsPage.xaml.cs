using Handsfreely.App.Infrastructure;
using Handsfreely.App.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Handsfreely.App.Views;

public sealed partial class ContactsPage : Page
{
    private readonly App _app = App.Current!;
    private List<ContactItem> _all = [];

    public ContactsPage()
    {
        InitializeComponent();
        _app.Contacts.Changed += OnContactsChanged;
        Unloaded += (_, _) => _app.Contacts.Changed -= OnContactsChanged;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => Rebuild();

    private void OnContactsChanged() => Ui.Run(Rebuild);

    private void Rebuild()
    {
        _all = [.. _app.Contacts.Contacts.Select(c => new ContactItem(c))];
        ApplyFilter();
        var s = _app.Hub.Active;
        EmptyText.Text = s is null ? L.T("common.nophone")
            : !s.HasPermission("contacts") ? L.T("perm.contacts")
            : _all.Count == 0 ? L.T("contacts.empty")
            : "";
    }

    private void ApplyFilter()
    {
        var q = SearchBox.Text.Trim();
        var digits = new string(q.Where(char.IsDigit).ToArray());
        List.ItemsSource = q.Length == 0 ? _all : _all.Where(c =>
            c.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
            (digits.Length > 0 && c.Contact.Phones.Any(p => new string(p.Number.Where(char.IsDigit).ToArray()).Contains(digits)))).ToList();
    }

    private void OnSearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => ApplyFilter();

    private void OnRefreshClick(object sender, RoutedEventArgs e) => _ = _app.ReloadContactsAsync();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (List.SelectedItem is not ContactItem item)
        {
            Details.Visibility = Visibility.Collapsed;
            return;
        }
        Details.Visibility = Visibility.Visible;
        DetailName.Text = item.Name;
        DetailPicture.Initials = item.Initials;
        DetailPicture.ProfilePicture = item.Photo;
        NumbersList.ItemsSource = item.Contact.Phones.Select(p => new NumberItem(p)).ToList();
    }

    private async void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not ContactItem { Contact.HasPhoto: true, PhotoRequested: false } item) return;
        item.PhotoRequested = true;
        item.Photo = await _app.Photos.GetAsync(item.Contact.Id);
        if (List.SelectedItem == item) DetailPicture.ProfilePicture = item.Photo;
    }

    private async void OnCallNumberClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string number }) return;
        try
        {
            await _app.Calls.DialAsync(number, (List.SelectedItem as ContactItem)?.Name);
        }
        catch (Exception ex)
        {
            await Dialogs.InfoAsync(XamlRoot, L.F("calls.failed", ex.Message));
        }
    }

    private void OnSmsNumberClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string number })
            _app.Window?.Navigate(AppPage.Messages, "to:" + number);
    }
}
