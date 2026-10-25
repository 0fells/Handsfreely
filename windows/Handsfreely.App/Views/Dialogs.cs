using Handsfreely.App.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Handsfreely.App.Views;

public static class Dialogs
{
    public static async Task InfoAsync(XamlRoot root, string text)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "Handsfreely",
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = L.T("common.ok"),
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
    }

    public static async Task<bool> ConfirmAsync(XamlRoot root, string text, string confirm)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "Handsfreely",
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = confirm,
            CloseButtonText = L.T("common.cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public static async Task<string?> AskAsync(XamlRoot root, string title, string placeholder)
    {
        var box = new TextBox { PlaceholderText = placeholder, InputScope = new Microsoft.UI.Xaml.Input.InputScope { Names = { new Microsoft.UI.Xaml.Input.InputScopeName(Microsoft.UI.Xaml.Input.InputScopeNameValue.TelephoneNumber) } } };
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = box,
            PrimaryButtonText = L.T("common.ok"),
            CloseButtonText = L.T("common.cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary && box.Text.Trim().Length > 0 ? box.Text.Trim() : null;
    }
}
